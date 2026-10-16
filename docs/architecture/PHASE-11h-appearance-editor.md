# PHASE-11h: Admin appearance editor

**Status:** Complete, merged in PR #38 (2026-10-16). Builds on D-053 and PHASE-11g, merged in PR #33. This phase does not block PHASE-12c.

## Objective and boundaries

Let an Admin choose the Portal deployment default at `/settings/appearance` and edit a product's existing skin through structured Appearance controls. Use the existing versioned `GET/PUT api/settings/site` and product APIs. No new API, database migration, raw CSS, CSP change or new skin format is needed.

The default selector offers Classic, Slate, Paper, Contrast and Midnight. Product controls cover every optional `ProductSkin` field: `Pack`, `Background`, `Surface`, `Ink`, `Muted`, `Border`, `Brand`, `Chrome`, `Focus`, `HeadingFont`, `BodyFont`, `Radius`, `BorderWidth`, `Shadow`, `Button` and `Header`. Each override can inherit/reset; removing the skin restores inheritance. The product accent remains the fallback for an unset brand.

The advanced JSON editor remains available with an explicit **Apply JSON** action. Unapplied or invalid JSON must not silently replace the structured draft. Preview is local, unsaved and isolated from the Admin shell. It uses `SkinResolver` and the existing packs, fonts, preset grammar and contrast rules; previewing never writes settings or the product.

## Actionable tasks

- [x] **P11h-T01** Add the Admin-only site Appearance page and navigation, with loading/error states, versioned save and conflict handling. Agents must not load site settings.
- [x] **P11h-T02** Add structured product Appearance controls, inherit/reset/remove behavior and explicit Apply JSON, preserving the rest of the product editor and its draft/error behavior.
- [x] **P11h-T03** Add an isolated live preview of the unsaved skin through `SkinResolver`, with validation feedback and the existing font/preset choices.
- [x] **P11h-T04** Verify authorization, save/concurrency failures, draft preservation, navigation and preview isolation; reuse and validate [dragon-poop.skin.json](../skins/dragon-poop.skin.json), record its existing limits and update the operator/developer docs.

## Validation and remaining work

Verification on 2026-10-16: the Release CI solution build passed with zero warnings/errors; all 8,686 .NET tests and 518 script tests passed. EF reported no pending model changes. Independent code review found no important or critical issues. Coverage includes Admin authorization, versioned/uncertain writes, failed reload and draft preservation, internal navigation confirmation, every product field, explicit JSON application, safe preview fallbacks, CSS isolation and local fonts. The code is merged in PR #38; browser UAT remains part of the owner checks below. The existing dragon-poop sample was delivered in 11g; this phase reuses it to validate editing and preview, rather than introducing a new skin. Its documented gaps remain in [the skins guide](../skins/README.md).

PHASE-12c UAT and hardening, owner manual checks, release/tag work and SDK attachment work are separate and remain open. SDK attachments are implemented separately in PHASE-11d (D-055).
