# Visual direction exploration

These four HTML files in `mockups/` are **throwaway exploration artifacts** from PHASE-02. They exist to compare directions with rendered screens (queue, ticket, portal, light and dark). They are not production code, load fonts from a CDN for convenience, and are not maintained. **`docs/BRAND.md` is the system of record**; if it disagrees with a mockup, `BRAND.md` wins. The selected direction is recorded in decision D-023.

| File | Direction | Outcome |
| --- | --- | --- |
| [`mockups/direction-beige-box.html`](mockups/direction-beige-box.html) | Beige Box | Rejected as a working-screen look; window frame and mascot palette kept for Admin brand moments |
| [`mockups/direction-night-shift.html`](mockups/direction-night-shift.html) | Night Shift Console | Rejected as a look; keyboard layer kept |
| [`mockups/direction-carbon-copy.html`](mockups/direction-carbon-copy.html) | Carbon Copy (original) | Owner-picked base; superseded by v2 |
| [`mockups/direction-carbon-copy-v2.html`](mockups/direction-carbon-copy-v2.html) | **Carbon Copy v2** | **Selected** (owner, 2026-10-02); reference implementation |

**Beige Box.** A late-90s office workstation: beige plates, 2px navy outlines, a CRT-blue accent, window title bars and light bevels. It is closest to the mascot and the product's heritage, but heavy outlines are boxy at queue density and the title bars and bevels drift toward Windows 95 parody. Its retro window and palette survive, restricted to Admin all-caught-up, sign-in and 404.

**Night Shift Console.** A dark, keyboard-first ops console with mono data, bracketed badges, a hairline grid and a status bar. It had the best keyboard model and density, but felt cold, and mono everywhere slows reading. The keyboard layer (keycaps, j/k, command palette, status bar) was re-skinned in Carbon Copy terms and kept.

**Carbon Copy.** A triplicate call-log form: ruled ledger rows, rubber-stamp statuses and a white / canary / pink tint code that separates customer, public reply and internal note. The most ownable look and the best match to the call-log heritage. Tilted stamps hurt dense lists and there was no keyboard model, so the owner picked it as a base with three revisions.

**Carbon Copy v2.** Carbon Copy plus the Night Shift keyboard layer, calmer queue stamps (straight, single border; tilted with a stamp-down only on the ticket view), and Beige Box windows with the mascot on Admin brand moments only. The portal stays plain and product-led, showing only "Powered by TechStrap". Defined in full in `docs/BRAND.md`.

The published artifact copies are private; see `docs/BRAND.md` section 9 for the links.
