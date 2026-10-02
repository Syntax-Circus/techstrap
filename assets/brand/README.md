# TechStrap brand assets

Generated from `source/logo-source.png` (the owner's logo, draft 02) by
[`scripts/brand/generate-brand-assets.py`](../../scripts/brand/generate-brand-assets.py).
Do not edit the generated files by hand; replace the source and re-run:

```sh
pip install pillow numpy
python scripts/brand/generate-brand-assets.py
```

| File | Use |
| --- | --- |
| `logo.png`, `logo-512.png` | Full mascot, transparent background (README, portal header, emails) |
| `mark.png`, `mark-512.png` | Monitor-head mark, transparent (compact spaces, avatars) |
| `favicon.ico` | Browser favicon, 16/32/48/64 px |
| `favicon-16.png`, `favicon-32.png` | PNG favicons |
| `apple-touch-icon.png` | iOS home screen, 180 px, opaque white background |
| `icon-192.png`, `icon-512.png` | Web app manifest icons |

The favicon and app icons use the head mark because the full figure is unreadable below about 64 px.
Colour palette and usage rules are defined in `docs/BRAND.md` (PHASE-02).
