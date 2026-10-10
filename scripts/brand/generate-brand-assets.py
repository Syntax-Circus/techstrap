"""Generate TechStrap brand assets (logo, mark, favicons, app icons, SVGs) from the source artwork.

Usage:  python scripts/brand/generate-brand-assets.py
Needs:  pip install pillow numpy vtracer fonttools brotli
        and the restored IBM Plex Mono font (run `dotnet build src/TechStrap.Admin` once; libman restores it)

Input:  assets/brand/source/logo-source.png (1254x1254 RGB on white)
Output: assets/brand/*.png, assets/brand/favicon.ico,
        assets/brand/{mark,logo,wordmark}.svg (provisional auto-traces, see the README),
        and byte-identical copies in src/TechStrap.Admin/wwwroot/brand and src/TechStrap.Portal/wwwroot/brand

Re-run after replacing the source artwork. If the artwork changes shape, re-tune HEAD_POLYGON
(source-pixel coordinates outlining the monitor head, excluding hands, arms and body).
"""

import re
import shutil
import tempfile
from pathlib import Path

import numpy as np
import vtracer
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "assets" / "brand" / "source" / "logo-source.png"
OUT = ROOT / "assets" / "brand"

# Background removal: near-white pixels connected to the image border become transparent.
BACKGROUND_MIN_CHANNEL = 228
FRINGE_RADIUS_FILTER = 5          # MaxFilter size used to find anti-aliased edge pixels
FRINGE_OPAQUE_AT = 60             # min channel value treated as fully opaque on the fringe
FLOOD_SEED_STEP = 7

# Monitor-head outline in source coordinates (measured against the bezel and side-box outlines).
HEAD_POLYGON = [
    (300, 25), (1120, 25), (1120, 492), (1000, 538), (940, 559),
    (892, 590), (360, 538), (330, 533), (300, 526),
]

# vtracer settings tuned so the head mark traces to about 30 KB and the full mascot to about 80 KB.
TRACE_OPTIONS = dict(
    colormode="color",
    hierarchical="stacked",
    mode="spline",
    filter_speckle=10,
    color_precision=5,
    layer_difference=28,
    corner_threshold=60,
    length_threshold=5.0,
    path_precision=1,
)

# The wordmark is the text "TechStrap" in IBM Plex Mono SemiBold, converted to outlines so it needs no font at runtime.
WORDMARK_TEXT = "TechStrap"
WORDMARK_FONT = ROOT / "src" / "TechStrap.Admin" / "wwwroot" / "fonts" / "ibm-plex-mono" / "files" / "ibm-plex-mono-latin-600-normal.woff2"

# Copies served by the apps (the apps cannot read assets/). The Portal gets only the 16px Powered-by head mark.
APP_COPIES = {
    ROOT / "src" / "TechStrap.Admin" / "wwwroot" / "brand": ["mark.svg", "logo.svg", "wordmark.svg"],
    ROOT / "src" / "TechStrap.Portal" / "wwwroot" / "brand": ["mark.svg"],
}

MARK_PADDING = 16
LOGO_PADDING = 24
ICO_SIZES = [16, 32, 48, 64]
APPLE_TOUCH_SIZE = 180
APPLE_TOUCH_BACKGROUND = (255, 255, 255, 255)
APPLE_TOUCH_MARK_SCALE = 0.86


def remove_background(image: Image.Image) -> Image.Image:
    rgb = np.asarray(image.convert("RGB")).astype(np.float32)
    near_white = rgb.min(axis=2) >= BACKGROUND_MIN_CHANNEL

    # copy(): an array-backed image does not keep floodfill writes.
    fill = Image.fromarray((near_white * 255).astype(np.uint8)).copy()
    width, height = fill.size
    seeds = [(x, y) for x in range(0, width, FLOOD_SEED_STEP) for y in (0, height - 1)]
    seeds += [(x, y) for y in range(0, height, FLOOD_SEED_STEP) for x in (0, width - 1)]
    for seed in seeds:
        if fill.getpixel(seed) == 255:
            ImageDraw.floodfill(fill, seed, 128)
    background = np.asarray(fill) == 128

    grown = np.asarray(
        Image.fromarray((background * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(FRINGE_RADIUS_FILTER))
    ) > 0
    fringe = grown & ~background

    alpha = np.full(background.shape, 255.0, dtype=np.float32)
    alpha[background] = 0
    fringe_alpha = np.clip((255 - rgb.min(axis=2)) / (255 - FRINGE_OPAQUE_AT) * 255, 0, 255)
    alpha[fringe] = fringe_alpha[fringe]

    # Un-premultiply fringe colors against the white they were composited on.
    a = (alpha / 255.0)[..., None]
    safe = np.where(a > 0.02, a, 1)
    unmixed = np.clip((rgb - 255 * (1 - a)) / safe, 0, 255)
    color = np.where(fringe[..., None], unmixed, rgb)

    return Image.fromarray(np.dstack([color, alpha]).astype(np.uint8), "RGBA")


def square(image: Image.Image, padding: int) -> Image.Image:
    trimmed = image.crop(image.getbbox())
    width, height = trimmed.size
    side = max(width, height) + padding * 2
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(trimmed, ((side - width) // 2, (side - height) // 2), trimmed)
    return canvas


def head_mark(logo: Image.Image) -> Image.Image:
    mask = Image.new("L", logo.size, 0)
    ImageDraw.Draw(mask).polygon(HEAD_POLYGON, fill=255)
    head = logo.copy()
    head.putalpha(ImageChops.multiply(head.getchannel("A"), mask))
    return square(head, MARK_PADDING)


def resized(image: Image.Image, size: int) -> Image.Image:
    return image.resize((size, size), Image.LANCZOS)


def trace_svg(image: Image.Image, destination: Path, title: str) -> None:
    """Auto-trace a transparent RGBA image to SVG with vtracer and make it embeddable (viewBox, no fixed size)."""
    with tempfile.TemporaryDirectory() as folder:
        source = Path(folder) / "trace-input.png"
        traced = Path(folder) / "trace-output.svg"
        image.save(source)
        vtracer.convert_image_to_svg_py(str(source), str(traced), **TRACE_OPTIONS)
        text = traced.read_text(encoding="utf-8")

    opening = re.search(r'<svg[^>]*width="(\d+)"[^>]*height="(\d+)"[^>]*>', text)
    if opening is None:
        raise RuntimeError("vtracer output has no sized <svg> element")
    width, height = opening.group(1), opening.group(2)
    body = text[opening.end():]
    destination.write_text(
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}">\n<title>{title}</title>\n{body}',
        encoding="utf-8",
    )


def wordmark_svg() -> str:
    """Outline WORDMARK_TEXT from the Plex Mono font file; the fill is currentColor so the wordmark follows the theme."""
    if not WORDMARK_FONT.exists():
        raise SystemExit(f"Missing {WORDMARK_FONT}. Run `dotnet build src/TechStrap.Admin` so libman restores the fonts.")

    font = TTFont(WORDMARK_FONT)
    glyph_set = font.getGlyphSet()
    cmap = font.getBestCmap()
    advances = font["hmtx"]
    commands = []
    bounds = BoundsPen(glyph_set)
    x = 0
    for character in WORDMARK_TEXT:
        name = cmap[ord(character)]
        flip = (1, 0, 0, -1, x, 0)  # font units point up, SVG points down
        pen = SVGPathPen(glyph_set, ntos=lambda value: f"{value:.0f}")
        glyph_set[name].draw(TransformPen(pen, flip))
        glyph_set[name].draw(TransformPen(bounds, flip))
        commands.append(pen.getCommands())
        x += advances[name][0]

    left, top, right, bottom = bounds.bounds
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{left:.0f} {top:.0f} {right - left:.0f} {bottom - top:.0f}">\n'
        f"<title>{WORDMARK_TEXT}</title>\n"
        f'<path fill="currentColor" d="{" ".join(commands)}"/>\n</svg>\n'
    )


def main() -> None:
    logo = remove_background(Image.open(SOURCE))
    logo_square = square(logo, LOGO_PADDING)
    mark = head_mark(logo)

    logo_square.save(OUT / "logo.png", optimize=True)
    resized(logo_square, 512).save(OUT / "logo-512.png", optimize=True)
    mark.save(OUT / "mark.png", optimize=True)
    resized(mark, 512).save(OUT / "mark-512.png", optimize=True)

    resized(mark, 16).save(OUT / "favicon-16.png", optimize=True)
    resized(mark, 32).save(OUT / "favicon-32.png", optimize=True)
    resized(mark, 256).save(OUT / "favicon.ico", sizes=[(s, s) for s in ICO_SIZES])
    resized(mark, 192).save(OUT / "icon-192.png", optimize=True)
    resized(mark, 512).save(OUT / "icon-512.png", optimize=True)

    apple = Image.new("RGBA", (APPLE_TOUCH_SIZE, APPLE_TOUCH_SIZE), APPLE_TOUCH_BACKGROUND)
    inner = int(APPLE_TOUCH_SIZE * APPLE_TOUCH_MARK_SCALE)
    offset = (APPLE_TOUCH_SIZE - inner) // 2
    apple.alpha_composite(resized(mark, inner), (offset, offset))
    apple.convert("RGB").save(OUT / "apple-touch-icon.png", optimize=True)

    trace_svg(mark, OUT / "mark.svg", "TechStrap mascot head")
    trace_svg(logo_square, OUT / "logo.svg", "TechStrap mascot")
    (OUT / "wordmark.svg").write_text(wordmark_svg(), encoding="utf-8")

    for folder, names in APP_COPIES.items():
        folder.mkdir(parents=True, exist_ok=True)
        for name in names:
            shutil.copyfile(OUT / name, folder / name)

    for path in sorted(OUT.glob("*.*")):
        print(f"{path.relative_to(ROOT)}  {path.stat().st_size:>8} bytes")


if __name__ == "__main__":
    main()
