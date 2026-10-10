# PHASE-02: Brand and UX

## Objective

Establish the visual identity and design constraints that block all UI work: a filled-in `docs/BRAND.md` produced through the _template `DESIGN.md` process, final UX briefs for Admin and Portal, and a Bootstrap 5 SCSS token layer with libman set up in both Blazor apps, so PHASE-07 and PHASE-09 can start without design rework.

## Dependencies

- **Depends on:** [PHASE-01](PHASE-01-foundation.md) (Admin/Portal projects, SassCompiler build target and Dockerfile CSS assertion exist).
- **Unblocks:** [PHASE-07](PHASE-07-admin-app.md) and [PHASE-09](PHASE-09-public-portal.md). Runs in parallel with PHASE-03 to PHASE-06 (no shared files).
- **External prerequisites:** owner availability for design review and direction choice; `UX-BRIEF-admin.md` and `UX-BRIEF-portal.md` drafts from discovery; network access to jsdelivr for libman.

## Architecture Decisions

- `docs/BRAND.md` follows _template `DESIGN.md` section 13 (identity, audience, personality, "should feel like / should NOT feel like", visual metaphor, typography, color, geometry, composition, imagery, iconography, motion, motifs, tokens, anti-patterns). TechStrap is a revival of a college call-logging tool. Personality: **cheeky frame, serious tools**. The mascot and wink-y copy live only in a closed set of Admin brand moments (all-caught-up, sign-in, 404), the style guide and README; working screens are dense, calm and plain-spoken, and the portal is product-led. The visual direction is **Carbon Copy v2**, chosen by the owner on 2026-10-02: see `docs/BRAND.md` and decision D-023.
- Design process order (DESIGN.md): understand identity, find a visual metaphor, explore 2 to 3 directions with rendered mockups, select the grammar, record anti-patterns, then implement tokens. The Admin (dense, agent productivity) and Portal (public, per-product themed) share one brand but have different density. **Assumption.**
- Portal theming has two layers: TechStrap brand tokens (fixed) and per-product branding (name, logo, accent color from the product record, applied at runtime via CSS custom properties on the portal root, never by recompiling SCSS). The brand must define which tokens a product accent may override and the contrast rule (a derived on-accent text color meeting WCAG AA). Email templates in PHASE-05 consume the same accent rule.
- Bootstrap 5 SCSS only: libman installs `bootstrap@5.3.x` (same minor as the `03-PACKAGE-MAP.md` pin, dragon-poop style) into `Styles/Vendor/bootstrap` in each app; `Styles/app.scss` sets token overrides before `@import`/`@use` of Bootstrap; `sasscompiler.json` compiles to `wwwroot/css/app.css`. Vendor SCSS is restored by libman at build, and neither vendor files nor compiled CSS are committed (`.gitignore` entries). Admin and Portal each own a separate token file (`_tokens.scss`) importing a shared `_brand-tokens.scss`. Sharing mechanism: a linked file via `Directory.Build.targets` or copy; pick the simpler in task P02-T05. **Assumption**: duplicate-by-copy if linking proves fragile.
- Dark mode: Admin supports light and dark through Bootstrap 5.3 `data-bs-theme`; Portal supports light only in v1. **Assumption** (confirm in the UX briefs).
- Accessibility baseline: WCAG 2.2 AA contrast, visible focus, reduced-motion respected, keyboard-operable queue and reply flows.
- No server-side entry points are added; this phase touches only docs, SCSS, libman and static shell markup.

## Application Boundaries

Follow _template `APPLICATION_ARCHITECTURE.md` (not copied into this repo). This phase adds no server-side entry points.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| N/A — no application entry points; static CSS served from `wwwroot` | Exempt: static assets execute no application workflow | n/a | ASP.NET static files | 200 / 304 / 404 | Exempt |

Handlers must not depend on HTTP objects, EF types, concrete infrastructure, or transport response types. Link an approved decision for every exception.

## Razor Component Boundaries

Follow _template `RAZOR_COMPONENT_ARCHITECTURE.md`. Only presentation shells are touched; logic-bearing components are built in PHASE-07 and PHASE-09.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| Admin `MainLayout`, nav shell (static placeholder links) | Inline: markup and `@Body` only, no injection or state | No ViewModel (no data) | None | None |
| Portal `PortalLayout` (static header/footer with token classes) | Inline: parameters only. Per-product accent injection arrives with PHASE-09, which will pair `.razor.cs` | No ViewModel | None in this phase | None |
| Brand style-guide page (`/_styleguide`, Development only) rendering tokens, buttons, forms, alerts, empty/loading/error patterns | Inline: static markup, no logic | No ViewModel | None | None |

Where a later phase needs injection, lifecycle work or navigation in these layouts, it must convert them to `.razor` + `.razor.cs` pairs.

## Syntax Circus Packages

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.Blazor.Components` | Shared Blazor UI pieces (error boundary, reconnect) | Brand must style its components (reconnect modal, error UI) before PHASE-07/09 use them | Style-guide page shows the reconnect and error-boundary states styled with brand tokens |
| `AspNetCore.SassCompiler` (third-party, locked in PHASE-01) | SCSS compilation | Token overrides compile to `app.css` | `dotnet build` emits `wwwroot/css/app.css` in both apps |
| `Microsoft.Web.LibraryManager.Build` (third-party, locked in PHASE-01) | Bootstrap restore | libman manifests added here | Clean clone build restores `Styles/Vendor/bootstrap` |

Versions are in `03-PACKAGE-MAP.md`. No new SyntaxCircus package is introduced; `SyntaxCircus.Blazor.Seo` stays in PHASE-09.

Record the exact package version in the linked package map. In the foundation phase, lock every selected version in `Directory.Packages.props`.

## Deliverables

- [x] `docs/BRAND.md`, complete and approved by the owner (owner approved 2026-10-02)
- [x] Final `UX-BRIEF-admin.md` and `UX-BRIEF-portal.md` (open questions resolved or deferred explicitly)
- [x] Rendered design exploration (screenshots or mockups) kept under `docs/design/` (small, optimized images only)
- [x] `libman.json` and `sasscompiler.json` in Admin and Portal; `Styles/app.scss`, `_tokens.scss`, per-app partials
- [x] Logo/wordmark and favicon assets (SVG source, no raster-only logos)
- [x] Development-only style-guide page in each app
- [x] `.gitignore` entries for `Styles/Vendor/` and `wwwroot/css/app.css` and `wwwroot/fonts/`

## Actionable Tasks

- [x] **P02-T01** Audit existing identity inputs (college-project origin, the owner's other products, `_template/docs/BRAND.md`, dragon-poop `docs/BRAND.md` as a worked example) and write the identity, audience, personality and "feels like / does not feel like" sections of `BRAND.md`
  - **Depends on:** none (inside this phase)
  - **Validation:** owner signs off the identity section in PR review; the section answers the DESIGN.md "Understand the Identity" prompts with no TODO left
- [x] **P02-T02** Produce 2 to 3 candidate visual directions with rendered mockups of one Admin queue screen and one Portal contact form, applying the DESIGN.md exploration step and logo-removal test
  - **Depends on:** P02-T01
  - **Validation:** screenshots committed under `docs/design/`; each direction has a one-paragraph rationale and a recorded logo-removal-test result
- [x] **P02-T03** Record the owner's chosen direction and finish `BRAND.md` (typography, color, geometry, composition, imagery, iconography, motion, motifs, anti-patterns, token table including the product-accent override and contrast rule)
  - **Depends on:** P02-T02
  - **Validation:** every DESIGN.md section 13 heading is present and filled; the token table lists each token with light and dark values; a contrast script or documented checker output shows AA for all text/background pairs
- [x] **P02-T04** Finalize `UX-BRIEF-admin.md` and `UX-BRIEF-portal.md` against the chosen direction: screen inventory, key flows, loading/error/empty states, accessibility needs, responsive rules
  - **Depends on:** P02-T03
  - **Validation:** both briefs have no TODO or open question without an owner decision or an explicit "deferred to phase N" note; the screen inventories cover every page named in PHASE-07, PHASE-08 and PHASE-09
- [x] **P02-T05** Add `libman.json` and `sasscompiler.json` to Admin and Portal (Bootstrap pinned to the map version, destination `Styles/Vendor/bootstrap`, `files: scss/**`), plus `.gitignore` entries
  - **Depends on:** none (needs only PHASE-01)
  - **Validation:** from a clean clone `dotnet build src/TechStrap.Admin` and `src/TechStrap.Portal` restore Bootstrap and emit `wwwroot/css/app.css`; `git status` is clean afterwards
- [x] **P02-T06** Implement `_tokens.scss` and `app.scss` in each app from the `BRAND.md` token table (Bootstrap variable overrides before import, `data-bs-theme` dark values for Admin, CSS custom properties for product accent in Portal)
  - **Depends on:** P02-T03, P02-T05
  - **Validation:** `StyleBuildTests` (in `TechStrap.Admin.Tests` and `TechStrap.Portal.Tests`, which this phase creates early as skeletons; PHASE-07/09 extend them) asserts the compiled CSS contains each brand custom property and the Bootstrap primary override; Dockerfile `test -f wwwroot/css/app.css` still passes
- [x] **P02-T07** Build the Development-only style-guide page in each app showing type scale, palette, buttons, forms, tables, badges for the five statuses (`New`, `Open`, `Pending`, `Solved`, `Closed`) and priority, alerts, skeleton/loading, empty and error states, and the reconnect UI
  - **Depends on:** P02-T06
  - **Validation:** page returns 200 in Development and 404 in Production (test `StyleGuideEnvironmentTests` in `TechStrap.Admin.Tests` and `TechStrap.Portal.Tests`); desktop and mobile screenshots reviewed against `BRAND.md` and saved in `docs/design/phase-02-review/` (done)
- [x] **P02-T08** Add logo, wordmark and favicon assets and wire them into both layouts
  - **Depends on:** P02-T03
  - **Validation:** assets are SVG sources plus generated favicon sizes; Lighthouse accessibility score for the style-guide page is at least 95 (done: Admin 100, Portal 100); logo-removal test recorded in `BRAND.md` section 25 (done)
- [x] **P02-T09** Prove the product-accent override: a Portal layout test fixture applies a sample accent via CSS custom properties and the derived on-accent color meets AA for a set of sample accents (including a very light and a very dark one)
  - **Depends on:** P02-T06
  - **Validation:** `ProductAccentContrastTests` pass for the sample accents; the contrast function lives in a small, reusable class that PHASE-05 email rendering can call (**Assumption**: placed in Contracts or a Portal-local helper; PHASE-05 decides)
- [x] **P02-T10** Review the visual critique loop (DESIGN.md sections 10 and 14) and tick the Definition of Done
  - **Depends on:** P02-T07, P02-T08, P02-T09
  - **Validation:** DESIGN.md section 14 checklist pasted into the PR with each item ticked or justified

## Success Criteria

- [x] `docs/BRAND.md` exists, is not the blank template, and is approved by the owner.
- [x] `UX-BRIEF-admin.md` and `UX-BRIEF-portal.md` are final and cover every UI page in PHASE-07 to PHASE-09.
- [x] A clean-clone `dotnet build` produces `wwwroot/css/app.css` in Admin and Portal via libman and SassCompiler; no CSS or Bootstrap vendor files are tracked (`git ls-files` check).
- [x] Style-guide pages render all five status badges, empty/loading/error states and the reconnect UI with brand tokens in light mode (and dark mode for Admin).
- [x] `ProductAccentContrastTests` and `StyleBuildTests` pass.
- [x] CI (from PHASE-01) stays green and the Admin/Portal Docker builds still pass the CSS assertion.

## Boundary Validation

- [x] Application use-case entry points delegate to the named handlers listed above. (None added.)
- [x] Framework-owned operational or static exemptions execute no application workflow.
- [x] Handler constructor dependencies contain only approved abstractions. (N/A.)
- [x] Persistence and integration entities do not cross infrastructure boundaries. (N/A.)
- [x] Cancellation reaches asynchronous handler dependencies. (N/A.)
- [x] Expected outcomes and transport mapping have focused tests. (N/A.)
- [x] Infrastructure implementations have integration coverage where applicable. (N/A.)
- [x] Inline Razor components contain only simple parameters and, at most, one
      trivial synchronous `EventCallback`-forwarding callback (layouts and the style-guide page).
- [x] Every component beyond the inline ceiling uses paired `.razor` and
      `.razor.cs` files, with all C# in code-behind.
- [x] Each Razor ViewModel is feature-local and presentation-only; the recorded
      direct-model decision does not expose an API ViewModel. (No ViewModels in this phase.)
- [x] A factory or presentation service is used only for non-trivial mapping,
      asynchronous assembly, or multiple dependencies. (None.)
- [x] API request and response contracts use DTO names and contracts, never
      Razor ViewModels. (N/A.)
- [x] Repeated or business-meaningful literals are named constants at the
      right scope, not bare magic values (design values live in SCSS tokens, not repeated literals in components).
- [x] Duplicated-looking logic across flows was evaluated for genuine
      divergence before extracting (or intentionally not extracting) a shared
      abstraction (Admin and Portal SCSS are deliberately separate beyond the shared brand tokens).

## Risks and Open Questions

- [x] Visual direction depends on owner taste and iteration; budget at least one revision round (schedule risk for PHASE-07 and PHASE-09).
- [x] Per-product accent colors can break contrast; the derived on-accent rule must be enforced at product save time (feeds PHASE-04 validation of `UpdateProductRequestHandler`).
- [x] Sharing brand tokens between two projects (link vs copy) is an **Assumption**; revisit if drift appears. Closed: link by relative import chosen.
- [x] Dark mode for Portal is deferred; confirm in the UX brief.
- [ ] Keyboard-only walk-through (tab order, focus ring, shortcuts) is deferred to PHASE-07 (P07-T19) and PHASE-09: no interactive flows exist in PHASE-02, only static style-guide pages. Reduced motion is covered at CSS level by `StampStyleTests`.
- [x] Font licensing and hosting: self-host fonts, no third-party CDN calls from the public Portal (privacy). **Assumption.** Closed: self-hosted through libman.
- [x] jsdelivr availability at build time: confirm the CI cache and Docker build tolerate it, or vendor Bootstrap SCSS through the libman cache. Closed: the Docker font and CSS assertions fail loudly.

## Handoff

Before PHASE-07 (Admin) and PHASE-09 (Portal) start: `docs/BRAND.md` is approved, both UX briefs are final, tokens compile in both apps, and the style-guide pages exist. This phase does not block PHASE-03 to PHASE-06. Related: [PHASE-03-domain-and-persistence.md](PHASE-03-domain-and-persistence.md) (parallel), [PHASE-07-admin-app.md](PHASE-07-admin-app.md), [PHASE-09-public-portal.md](PHASE-09-public-portal.md).
