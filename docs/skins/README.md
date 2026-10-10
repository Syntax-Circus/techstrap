# Product skins

This folder holds worked examples of a product skin (D-053). A skin is a small JSON object of colors, fonts and presets that restyles one product's portal. Every member is optional; a member left out inherits from the pack (the deployment default, or the skin's own `pack`).

## dragon-poop.skin.json

The `dragon-poop` sample: a parchment page, wood-dark ink and chrome, a square pixel-style look with a hard shadow and beveled buttons. It was derived from dragon-poop's own `_tokens.scss`, `_bootstrap-overrides.scss` and `_buttons.scss`. It has exactly 15 members and resolves with no problems.

## Apply it

- Admin: open the product (Settings, Products), expand "Appearance (advanced)", paste the file into the "Skin (JSON)" field and save. A color pair that fails the contrast rule is refused at the field with the pair named.
- API: `PUT api/products/{id}` with `"skin": { ... }` (the file's content) and the product's current `version`.

The product's own accent color applies as the brand unless the skin sets `brand`; the sample sets `#63371F`.

## Clear it

Empty the "Skin (JSON)" field and save, or send `"skin": {}` in the `PUT api/products/{id}` body. Leaving `skin` out of the request leaves the stored skin unchanged.

## Switch the deployment default pack

Until the PHASE-11h page exists, change the pack every product without its own `pack` uses with `PUT api/settings/site` (an Admin call; `GET api/settings/site` shows the current value). The packs are Classic, Slate, Paper, Contrast and Midnight.

## Tips

- The resolver checks four pairs for contrast: ink on background, ink on surface and muted on background (4.5:1), and the focus ring on background (3:1). When a pair fails, both members of the pair revert to the pack's values, so a light page on a dark pack (or the reverse) also needs `muted` and `focus` set, not only `background` and `ink`.
- Keep the whole JSON under 2000 characters.

## Known gaps measured against dragon-poop's own site

- The gold focus ring (`#FFCF4A`) fails the 3:1 rule against the parchment background, so the sample uses the wood-dark `#26140C`. Dragon-poop uses gold only on dark chrome.
- Text on the brand color is derived white or black, not dragon-poop's cream `#FFF5D6`.
- The accent text color `#B04A17` used for taglines and step titles has no token. Links use the derived brand ink.
- The stepped two-layer heading shadow, the hero sky image, the pixel-art logo and mascot, the parchment "scrap" rotation and the ground and stone-band marketing strips are out of reach of a skin.
- Copy and voice ("Off the map", "A rough landing") are not skin data.
