"""Generate TechStrap brand assets (logo, mark, favicons, app icons) from the source artwork.

Usage:  python scripts/brand/generate-brand-assets.py
Needs:  Pillow, numpy

Input:  assets/brand/source/logo-source.png (1254x1254 RGB on white)
Output: assets/brand/*.png, assets/brand/favicon.ico

Re-run after replacing the source artwork. If the artwork changes shape, re-tune HEAD_POLYGON
(source-pixel coordinates outlining the monitor head, excluding hands, arms and body).
"""

from pathlib import Path

import numpy as np
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

    # Un-premultiply fringe colours against the white they were composited on.
    a = (alpha / 255.0)[..., None]
    safe = np.where(a > 0.02, a, 1)
    unmixed = np.clip((rgb - 255 * (1 - a)) / safe, 0, 255)
    colour = np.where(fringe[..., None], unmixed, rgb)

    return Image.fromarray(np.dstack([colour, alpha]).astype(np.uint8), "RGBA")


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

    for path in sorted(OUT.glob("*.*")):
        print(f"{path.relative_to(ROOT)}  {path.stat().st_size:>8} bytes")


if __name__ == "__main__":
    main()
