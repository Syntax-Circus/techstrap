# TechStrap brand assets

Generated from `source/logo-source.png` (the owner's logo, draft 02) by
[`scripts/brand/generate-brand-assets.py`](../../scripts/brand/generate-brand-assets.py).
Do not edit the generated files by hand; replace the source and re-run:

```sh
pip install pillow numpy vtracer fonttools brotli
dotnet build src/TechStrap.Admin   # once: libman restores the IBM Plex Mono file the wordmark is outlined from
python scripts/brand/generate-brand-assets.py
```

| File | Use |
| --- | --- |
| `logo.png`, `logo-512.png` | Full mascot, transparent background (README, emails; the portal shows only the Powered-by mark, D-023) |
| `mark.png`, `mark-512.png` | Monitor-head mark, transparent (compact spaces, avatars) |
| `favicon.ico` | Browser favicon, 16/32/48/64 px |
| `favicon-16.png`, `favicon-32.png` | PNG favicons |
| `apple-touch-icon.png` | iOS home screen, 180 px, opaque white background |
| `icon-192.png`, `icon-512.png` | Web app manifest icons |
| `mark.svg`, `logo.svg` | **Provisional.** Auto-traced SVGs (vtracer) of the head mark and the full mascot, 5 color levels, about 31 KB and 79 KB. Used by the Admin layout, brand moments and style guide |
| `wordmark.svg` | **Provisional.** The text `TechStrap` in IBM Plex Mono SemiBold, converted to outlines (no font needed at runtime), `fill="currentColor"` so it follows the theme |

The SVGs are provisional until the owner supplies hand-drawn vector artwork: the traced edges are approximations of the PNG source, and colors are quantized, so they differ slightly from the brand palette in `docs/BRAND.md`. `scripts/brand/generate-brand-assets.py` also copies `mark.svg`, `logo.svg` and `wordmark.svg` into `src/TechStrap.Admin/wwwroot/brand/` and `mark.svg` into `src/TechStrap.Portal/wwwroot/brand/` (the apps cannot read `assets/`); `scripts/tests/BrandAssets.Tests.ps1` fails when a copy drifts.

The favicon and app icons use the head mark because the full figure is unreadable below about 64 px.
Color palette and usage rules are defined in `docs/BRAND.md` (PHASE-02).
