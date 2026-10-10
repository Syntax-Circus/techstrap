# PHASE-11g: Portal theme packs and per-product skins (engine)

## Objective

Give the public Portal a look that is not bland by default and can match each product:

- Five built-in **theme packs** (Classic, Slate, Paper, Contrast, Midnight). An Admin picks the deployment default, stored as an Admin-editable site setting.
- A per-product **skin**: an optional pack choice plus optional token overrides (colours, fonts from a built-in list, radius, border width, shadow, button and header presets). Resolution order: Classic < deployment default pack < product pack < product token overrides.
- Skins are **structured tokens**, never raw CSS. A token model is validated against the sibling repo `dragon-poop`, whose skin is deliberately creative, to find holes early.
- Emails take colours only (the accent and the chrome colour); no web fonts or CSS.

Delivery is two pull requests. **11g (this phase)**: the token model, the five packs, storage, the Api, the Portal rendering, emails, docs. **11h**: the Admin site-settings page, the product Appearance editor with live preview, and the dragon-poop validation skin. D-053 records the decision.

## Dependencies

- **Depends on:** [PHASE-09](PHASE-09-public-portal.md) (Portal styles, `AccentScope`, CSP tests), [PHASE-11e](PHASE-11e-product-hosts.md) (product hosts), [PHASE-11f](PHASE-11f-landing-and-logos.md) (landing cards, uploaded logo), [PHASE-04](PHASE-04-agent-auth-and-admin-config.md) and [PHASE-07](PHASE-07-admin-app.md) (products and the Admin).
- **Unblocks:** PHASE-11h (the editor) and a better UAT soak look; PHASE-12c is not blocked by it.
- **External prerequisites:** none to build. The owner approves the BRAND.md and UX-BRIEF amendments listed under Architecture Decisions by approving the plan (2026-10-10).

## Architecture Decisions

- **Tokens, not CSS.** The Portal CSP is `style-src 'self'` with `style-src-attr 'unsafe-inline'`; an inline `<style>` is banned and tested, and a test requires every `[style]` element to be the single `ts-accent-scope` wrapper. The project validates admin-supplied values to a closed grammar, re-checks them at render and derives with one shared function. Tokens rendered as custom properties on the existing wrapper pass the CSP unchanged and keep contrast enforceable. Raw CSS needs a sanitiser, an XSS corpus and a review row; it is out of scope.
- **Token grammar.** Every field optional (null means inherit): `Pack` (a key from `SkinPacks`), colours `Background`, `Surface`, `Ink`, `Muted`, `Border`, `Brand`, `Chrome`, `Focus` (`#RRGGBB`), `HeadingFont` and `BodyFont` (keys from `SkinFonts`), `Radius` (`square`, `soft`, `round`), `BorderWidth` (1 to 4), `Shadow` (`none`, `soft`, `hard`), `Button` (`flat`, `bevel`, `outline`), `Header` (`plain`, `solid`, `band`). `Brand` is the product's existing `AccentColour` when unset, so no current product changes look.
- **One resolver.** `SkinResolver.Resolve(deploymentDefaultPack, productSkin)` in Contracts is the only place that merges and derives (`OnBrand`, `OnChrome`, `BrandInk`, `MutedInk`); the Portal, the Api validation, the email renderer and the Admin preview all call it, and an architecture rule forbids recomputing. `ProductAccent.TryDerive` stays the implementation for the light-background case so existing accents derive unchanged.
- **Contrast rules (new, amend D-031 for products that set skin tokens).** `Ink` on `Background` and on `Surface` at least 4.5:1, `Muted` on `Background` at least 4.5:1, derived `OnBrand` on `Brand` at least 4.5:1 (always satisfiable), `Border` and `Focus` at least 3:1 against `Background`. Save fails with 400 `skin-contrast-invalid` naming the pair; at render a failing value falls back to the pack value and is never written. A product that sets no skin tokens keeps D-031's "contrast is not validated" for its accent.
- **Packs are data.** Each pack is a full vetted token set with a declared scheme (light or dark), held in Contracts (`SkinPacks`) and mirrored by compiled SCSS keyed on `data-ts-pack`; a parity test fails if the two differ. **Midnight** is a dark pack: it sets its own variables and never follows the OS (`prefers-color-scheme` stays absent from the CSS).
- **Storage.** `Product.Skin` (owned value, validated in Domain, one `skin` text column holding versioned size-capped JSON; unknown keys rejected). New singleton `SiteSettings` (`DefaultPack`), table `site_settings` with one row seeded to `classic` by the migration.
- **Api.** `GET api/settings/site` and `PUT api/settings/site` (Admin), `GET api/public/site` (anonymous, `public` limit, `Cache-Control: public, max-age=300`, returns only the default pack key). `PublicProductDto`, `ProductDto`, `CreateProductRequest` and `UpdateProductRequest` gain `Skin` as trailing optionals (null means unchanged on update, as `PortalHost`). Audit `changed: ["skin"]`; admin event `SiteSettingsUpdated`. Contracts 0.4.0 with a `### 0.4.0` note; no shims.
- **Portal rendering.** A `TimeProvider` snapshot client for the site setting (60 s, stale-while-revalidate; last good value, else Classic). `PortalThemeViewModel` replaces the accent-only theme. `AccentScope` stays the only `style=` carrier and additionally writes the validated resolved variables; presets travel as `data-ts-pack`, `data-ts-button`, `data-ts-header`, `data-ts-shadow` attributes (attribute selectors satisfy `ResponsiveStyleTests`). Neutral pages (root, 404, error) use the deployment default pack only, never a product, so their body is identical for every address (D-045). Landing cards each get their own scope. Existing selectors read the new variables with today's values as fallbacks, so an unthemed page is pixel-identical.
- **Fonts.** OFL families added through libman and `_fonts.scss` (Nunito, Atkinson Hyperlegible, Source Serif 4, Pixelify Sans, beside IBM Plex Sans); `FontHostingTests` gains rows; no font binary is tracked; no CDN.
- **Amended text (owner-approved with the plan).** BRAND.md "Portal tokens (light only in v1)", the "Use the mascot's palette as a portal theme" line (the prohibition on TechStrap colours and the mascot stays), and the "Product-accent override rule" (section 22: an accent sets only three variables); UX-BRIEF-portal "light-only in v1" and "do not generate per-product stylesheets"; the 2026-10-02 owner decision that the Portal is light only in v1. Each is amended by an "Amended by D-053" line, not rewritten.
- **Still forbidden.** TechStrap colours, mascot or name on a customer page beyond the footer; raw CSS; background images (deferred); a theme that follows the visitor's OS.

### Known limits (from the dragon-poop review)

Out of reach for tokens by design: pixel-art imagery and composition, hero and marketing sections, copy and voice, multi-layer ornament (stepped text shadow, stripe patterns, rotated scraps), a product's own display face unless it is in the built-in list, WIP-label provenance, background images, third-party chrome. PHASE-11h records the gaps found by building the skin through the Admin.

## Application Boundaries

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| `POST api/products`, `PUT api/products/{id}` | `CreateProductRequestHandler`, `UpdateProductRequestHandler` (extended) | `IProductRepository`, Domain `ProductSkin`, `SkinResolver` (Contracts) | EF repository with the `skin` column | 201/200 with `Skin`; `skin-invalid`, `skin-contrast-invalid` -> 400 | Extend the existing handlers |
| `GET api/settings/site` (Admin) | `GetSiteSettingsRequestHandler` (new) | `ISiteSettingsRepository`, `ICurrentAgentClaims`, `IAgentRepository` | EF repository | 200 `SiteSettingsDto` | New handler |
| `PUT api/settings/site` (Admin) | `UpdateSiteSettingsRequestHandler` (new) | `ISiteSettingsRepository`, `IAdminEventRepository`, `IUnitOfWork`, `IAgentRepository`, `ICurrentAgentClaims`, `TimeProvider` | EF repository, UoW | 200 `SiteSettingsDto`; unknown pack -> 400 `skin-pack-unknown` | New handler |
| `GET api/public/site` | `GetPublicSiteRequestHandler` (new) | `ISiteSettingsRepository` | EF repository | 200 `PublicSiteDto` (default pack key) | New handler |
| `GET api/public/products[/{key}]` | Existing public handlers (extended) | Existing | Existing read model | 200 with `Skin` | Projection change |
| Portal theme resolution | Exempt (presentation) | `ISiteSettingsClient`, `IPublicProductClient`, `SkinResolver` | Typed clients | Variables and data attributes on the wrapper | View-model code |

## Razor Component Boundaries

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| `PortalLayout`, `AccentScope` | Paired files; code-behind builds the style string and data attributes from the view model | `PortalThemeViewModel` (feature-local, presentation only) built by a small factory because it combines the default pack, the product skin and the resolver | Per request | `PublicProductDto`, `PublicSiteDto` only |
| Landing cards | Existing `Home` code-behind | `LandingCardViewModel` gains the brand and chrome colours | One render per request | `PublicProductSummaryDto` (gains `Skin`) |

## Syntax Circus Packages

No package is added or upgraded. `SyntaxCircus.Common` carries the new error codes; `SyntaxCircus.AspNetCore.Common` maps them to 400.

## Deliverables

- [ ] Contracts 0.4.0: token model, `SkinPacks`, `SkinFonts`, `SkinResolver`, contrast derivation, rules, README notes.
- [ ] Domain `ProductSkin` and `SiteSettings`; persistence and migration `AddSkinAndSiteSettings`.
- [ ] Application and Api: skin on product DTOs and handlers; three site routes; D-022 list; entry-point catalog.
- [ ] Portal: settings client, theme view model, `AccentScope` extension, packs and presets SCSS, fonts, landing-card scopes.
- [ ] Emails: chrome colour.
- [ ] D-053; BRAND.md, UX-BRIEF, SELF-HOSTING, PORTAL-APP, security review; Pester pins.

## Actionable Tasks

- [ ] **P11g-T01** Spec, D-053, "Amended by D-053" lines, roadmap and discovery rows, pins
  - **Depends on:** plan approval 2026-10-10
  - **Validation:** `scripts/Invoke-ScriptTests.ps1` green with the new pins.
- [ ] **P11g-T02** Contracts: skin record, `SkinPacks` (five), `SkinFonts`, `SkinResolver`, contrast derivation, `BrandingRules` skin rules, 0.4.0 note
  - **Depends on:** P11g-T01
  - **Validation:** precedence table test; every pack passes the contrast rules; a hostile token set fails naming the right pair; `ProductAccent` results unchanged; the dragon-poop fixture resolves with the documented used and unused tokens.
- [ ] **P11g-T03** Domain: `ProductSkin` value and validation, `SiteSettings` aggregate
  - **Depends on:** P11g-T02
  - **Validation:** Domain tests for the grammar, unknown keys, size cap; parity with `BrandingRules`.
- [ ] **P11g-T04** Persistence: `skin` column, `site_settings` table and seed, migration via the `ef-migrate` skill, schema docs
  - **Depends on:** P11g-T03
  - **Validation:** round trip; the seed row reads `classic`; no pending model changes.
- [ ] **P11g-T05** Application and Api: product skin on DTOs and handlers, site-settings handlers and routes, D-022 list, entry-point catalog
  - **Depends on:** P11g-T04
  - **Validation:** null means unchanged; contrast failure is 400; agent is 403 on the admin routes; public site read is anonymous with the cache header; OpenAPI surface includes the routes.
- [ ] **P11g-T06** Portal: client, view model, `AccentScope` extension, packs and presets SCSS, fonts, landing scopes, neutral default pack
  - **Depends on:** P11g-T05
  - **Validation:** unthemed product page byte-compares to the previous output; each pack renders its variables and attributes; hostile values never reach `style=`; neutral pages identical across hosts; no `<style>`, no new `[style]` class, no new `url(`; pack SCSS and Contracts parity; font pins.
- [ ] **P11g-T07** Emails: chrome colour in the header bar
  - **Depends on:** P11g-T05
  - **Validation:** renderer tests with and without a chrome colour; fixed font stack unchanged.
- [ ] **P11g-T08** Admin: an interim "Skin (JSON)" field on the product editor (validated through the Contracts grammar) and the dragon-poop sample skin in `docs/skins/dragon-poop.skin.json`, so a skin can be set and tested before the PHASE-11h editor
  - **Depends on:** P11g-T05
  - **Validation:** bUnit: the field round-trips the stored skin as JSON; blank on an unskinned product sends nothing, blanked on a skinned product clears it; invalid JSON and Api token errors show at the field; the dragon-poop sample resolves without problems and its limits are listed.
- [ ] **P11g-T09** Docs and close-out: SELF-HOSTING, PORTAL-APP, BRAND, UX-BRIEF, security review, roadmap, Pester pins, As built
  - **Depends on:** P11g-T01 to P11g-T08
  - **Validation:** build, full test run, Pester, migration check green; compose check of the five packs.

## Success Criteria

- [ ] An Admin-chosen default pack changes the root, product pages and ticket pages; a product override changes only that product.
- [ ] A product with no skin looks as it did before.
- [ ] No token value reaches the page unvalidated; contrast rules hold for every pack and every saved skin.
- [ ] CSP, style, font, responsive and neutral-body tests are green; the CSP is unchanged.
- [ ] D-053 recorded; the amended documents carry "Amended by D-053" lines.

## Boundary Validation

- [ ] Entry points delegate to the named handlers; one `[FromServices]` handler per action.
- [ ] Handler dependencies are approved abstractions; `SkinResolver` is a pure Contracts function.
- [ ] Persistence records do not cross boundaries; the Portal sees `PublicProductDto` and `PublicSiteDto`.
- [ ] Cancellation reaches asynchronous dependencies.
- [ ] Every renderer of a skin value goes through `SkinResolver` (architecture rule).
- [ ] Docs ASCII; new files LF; non-ASCII in C# only as `\u` escapes.

## Risks and Open Questions

- [ ] **Reversals.** Light-only and "an accent may set only three variables" are amended; recorded in D-053.
- [ ] **Contrast generalisation** is new logic in one resolver; every consumer must use it.
- [ ] **Pixel-identical default.** Moving neutrals to variables risks regressions; fallbacks equal today's values and a golden test guards it.
- [ ] **Tokens will not match everything.** The 11h dragon-poop build records the follow-ups (background image, copy overrides, more presets).
- [ ] **Font weight and licences.** Only used faces download; every family is OFL and restored from libman, never tracked.
- [ ] **Contracts 0.4.0** is another binary break (trailing optionals), named in the notes, no shims.

## Handoff

Before PHASE-11h: the five packs render, the default pack is switchable through `PUT api/settings/site`, a product skin round-trips and renders, and D-053 is recorded. 11h adds the Admin editor and the validation skin.
