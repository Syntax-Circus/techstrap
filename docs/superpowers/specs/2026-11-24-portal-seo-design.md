# Portal SEO: fix Bing findings and add guardrails

Status: approved by Jon (direction), ready for implementation planning.
Date: 2026-11-24. Origin: Bing Webmaster Tools SEO report for `support.sinforgiver.com` (the Sin Forgiver support portal, served by `TechStrap.Portal`).

## 1. Why this work exists

Bing Webmaster Tools reported two moderate-severity recommendations against Techstrap-served pages:

1. **"Too many pages with insufficient content"**: `https://support.sinforgiver.com/` and `https://support.sinforgiver.com/kb`.
2. **"Meta descriptions too short"**: `https://support.sinforgiver.com/kb` (plus four `www.sinforgiver.com` pages that are NOT Techstrap; see section 9).

This came out of the Sin Forgiver SEO initiative. Goal: Techstrap-generated pages should have good SEO by construction, not just have these two URLs patched. Success looks like: both URLs pass Bing's checks on the next scan (description 150 to 160 characters; meaningful visible body text), and tests prevent the regression for any product.

## 2. Findings (verified against the code, 2026-11-24)

All paths are under `src/TechStrap.Portal` unless noted.

### The page Bing sees at `/` is `ProductHome.razor`, not `Home.razor`
On a product host (`support.sinforgiver.com`), middleware rewrites `/` to that product's home (`ProductHostContext` in `Hosting/`; `Home.razor.cs` documents this: "On a product host the middleware has already rewritten `/` to that product's home"). The page rendered is `Components/Pages/ProductHome.razor`, which today contains only: a `PageTitle`, an h1 (`ShellCopy.HomeHeading`), `KbSearchBox`, and a contact button. **It has no `SeoHead` at all** (so no meta description, canonical, OG or Twitter tags) and very little text. This is the thin page.

`Components/Pages/Home.razor` is the root of a host that is not tied to one product. It either 302-redirects (`TECHSTRAP_PORTAL_DEFAULT_PRODUCT` set, via `Navigation.NavigateTo`), lists product cards (`TECHSTRAP_PORTAL_LANDING=Products`), or shows a neutral placeholder. Its card view has `SeoHead` (description from `ShellCopy.LandingIntro`, one sentence); its placeholder branch has only `PageTitle`.

### `/kb` (`Components/Pages/KbHome.razor` + `.razor.cs`)
- Description is `KbCopy.HomeDescription(product)` = "Help articles and answers for {Product}." (about 40 characters). This is the Bing "too short" finding.
- Body: h1, `KbSearchBox`, and one card per category (h2 link, optional description, article count). No intro paragraph, no article links, no JSON-LD. Thin.

### Shared SEO plumbing (already good, reuse it)
- `Seo/PortalSeoRegistration.cs` wires `SyntaxCircus.Blazor.Seo` (`AddSyntaxCircusSeo`, `UseSyntaxCircusSeo`, `MapSeoRobotsTxt`, `MapSeoSitemap` with `PortalSitemap.ProviderAsync`). `Seo/ProductHostSeoUrlBuilder.cs` builds host-aware absolute URLs. `Seo/PortalSitemapBuilder.cs` lists product home, KB home, categories, articles.
- `SeoHead` (package component) emits title, description, canonical, robots, OG, Twitter, and JSON-LD from `StructuredData`. It does not enforce a description length.
- Article pages (`KbArticle.razor.cs`) already do this well: `KbPlainText.Describe` (summary, else first sentence of body, cut at 160, fallback `KbCopy.ArticleDescriptionFallback`), `Seo/KbStructuredData.cs` (BreadcrumbList + Article), `Seo/JsonLdText.Safe` (the package does not escape `</script>`; always route JSON-LD strings through it).
- Package pin: `SyntaxCircus.Blazor.Seo` **0.1.5** in `Directory.Packages.props`. Sin Forgiver's Web uses **0.1.8**.
- Not used in Techstrap and out of scope: `SyntaxCircus.Seo.Analysis`, `SyntaxCircus.AspNetCore.IndexNow`.

### Reference implementation to copy ideas from
`D:/dev/SyntaxCircus/sinforgiver/src/SinForgiver.Web/Services/SeoText.cs` (titles clamped to 70, descriptions to 160, with fallbacks) and `docs/marketing/seo-offsite-runbook.md` in that repo.

## 3. Goals

- `ProductHome` (what `/` is on a product host) becomes a real landing page with its own complete SEO head and enough visible, useful text.
- `/kb` gets a 150 to 160 character description and substantive body content (intro, article links), plus structured data.
- Product owners can set a custom meta description and intro per product in Admin; a good generated fallback applies when unset.
- Guardrails: a small text helper plus tests so no product can ship a short or missing description, and so the regression is caught in CI/local runs.
- Bring `SyntaxCircus.Blazor.Seo` to 0.1.8.

## 4. Non-goals

- IndexNow submission, `SyntaxCircus.Seo.Analysis`, or any off-site SEO (explicitly deferred by Jon).
- Rewriting article/category/search pages (articles are already fine; check category and search only for description length, see 5.6).
- Changing the redirect or product-host rewrite behavior of `Home.razor`.
- Publishing packages. Do not publish any NuGet package on Jon's behalf. If the 0.1.8 package is not available to restore locally, stop and report; do not work around it by publishing.

## 5. Design

### 5.1 Per-product SEO fields (follow the `Tagline` precedent exactly)
`Tagline` is the existing example of an optional plain-text product field; trace it end to end and mirror it for two new optional fields:

- `SeoDescription`: plain text, meta description override, max 160 chars.
- `HelpCenterIntro`: plain text, short intro paragraph shown on `ProductHome` and `/kb`, max about 600 chars. Plain text only, encoded on render (no HTML/markdown), consistent with how tagline and category descriptions are treated.

Touch points (find each by following `Tagline`):
- `src/TechStrap.Domain/Products/Product.cs` (`ProductBranding` record: private ctor, `Restore`, `Create`, `CreateForUpdate`, `Build`), `Domain/Rules/DomainLimits.cs` (`TaglineMaxLength`, add new limits), `Domain/Rules/Guard.cs` (`OptionalTagline`; add an equivalent optional-text guard, reusing it if its semantics fit).
- `src/TechStrap.Contracts/Products/ProductDtos.cs` (admin DTOs + request), `PublicProductDto.cs`, `PublicProductSummaryDto.cs` (only add to the public DTOs if the portal needs it; the product home reads the single-product DTO, the landing card list does not need them).
- `src/TechStrap.Application/Products/`: `ProductMapping.cs`, `CreateProductRequestHandler.cs`, `UpdateProductRequestHandler.cs`, `GetPublicProductRequestHandler.cs`.
- `src/TechStrap.Infrastructure`: EF mapping and a **new migration** (follow existing naming; latest is `20261016174640_AddWebhookNotifications`). Columns are nullable text; no backfill. Migration must be non-destructive and reversible.
- `src/TechStrap.Admin/Features/Settings/Products/` (`ProductEditorViewModel`, `ProductFields`, `ProductsCopy`) and `Clients/ApiFields.cs`: add the two fields to the editor with a live character counter (description target 150 to 160, hard max 160).
- `src/TechStrap.Portal/Products/ProductThemeViewModel.cs`: add the two values so pages can read them from `Theme`.
- Check whether product changes are audited or webhook-notified (there is a webhook notifications feature) and keep behavior consistent with how tagline changes are handled.
- Docs: Techstrap keeps decision records (D-0xx references appear throughout). Add a short decision entry in whichever decision log the repo uses (look under `docs/architecture/`).

### 5.2 Text helper
Add `Seo/SeoText.cs` (name may follow local conventions). Responsibilities:
- `Description(string? candidate, Func<string> fallback)`: trim, collapse whitespace, strip control characters; if the candidate is null/blank or shorter than a floor (suggest 110), use the fallback; clamp to 160 on a word boundary with an ellipsis only if truncated.
- `Title(...)` clamp to 70.
- Pure and unit-testable. Do not take a dependency on Sin Forgiver code; port the idea.

Generated fallbacks must be real sentences, not padding. Example for KB home: "Find answers, how-to guides and troubleshooting for {Product}: browse {N} help articles across {category names}, or contact the support team." Compose from product name, category names (truncate list), article count; guarantee the result lands in the 110 to 160 range for any realistic input (very long names or zero categories must still produce valid output, e.g. drop the category list when it would overflow).

### 5.3 `ProductHome.razor` (the `/` that Bing flagged)
- Add `SeoHead` with: title (existing `ShellCopy.ProductTitle`), description (override else generated fallback, from `SeoText`), canonical via `Links.Absolute(Links.ProductHome(theme.Key))`, OG image via `KbSeo.Image(theme)`, plus `WebSite` JSON-LD (see 5.5).
- Body content (visible, server-rendered, plain text, encoded): h1 with product name; the intro paragraph (override, else a generated sentence or two); the search box; a "Browse by topic" section listing categories (name, short description, count, link); a "Popular and recent articles" list (top N, about 6 to 8, title as link + summary); a "Still need help?" section with the contact button and a short explanation of how tickets and replies work. Target at least about 150 visible words for a product with seeded content. Use h2s for sections.
- This page currently inherits `ProductPageBase` and loads only the product. It will need the KB client (`IPublicKbClient`) like `KbHome.razor.cs` does. Decide the "popular" source from what the KB API offers (most recent by `UpdatedAt` is acceptable if there is no view-count data; do not add analytics). If listing top articles needs a new API endpoint or parameter, keep it minimal, public-safe (published articles only, same visibility rules as the existing public KB client) and flag it in the PR description. Failure of the KB call must degrade to the page without those sections, still 200, with a complete head.
- Make sure the home page is in the sitemap for product hosts (check `PortalSitemapBuilder.cs:72`, it already yields `ProductHome`) and that `Home.razor`'s behavior is untouched.

### 5.4 `/kb` (`KbHome.razor`)
- Description: override else generated fallback (5.2), replacing the one-line `KbCopy.HomeDescription` usage. Keep the copy class for strings; do not inline user-facing text in markup (Techstrap keeps copy in `*Copy.cs` classes).
- Add the intro paragraph, and an "Popular and recent articles" list beneath the categories (same data source as 5.3; share a component, e.g. `Components/Kb/FeaturedArticles.razor`, rather than duplicating).
- Add JSON-LD: `BreadcrumbList` plus `CollectionPage` or `WebSite` (see 5.5). Extend `Seo/KbStructuredData.cs`; all strings through `JsonLdText.Safe`.
- Empty-category state keeps working (still gets a full head and the intro; do not add noindex unless it is already the policy).

### 5.5 Structured data
- `WebSite` (name, url) with `potentialAction` `SearchAction` ONLY IF the KB search route accepts a query parameter in a stable URL (`Components/Pages/KbSearch.razor`); otherwise omit `SearchAction`.
- `Organization` (product name, logo if acceptable) is already used as publisher in the Article JSON-LD; reuse that builder.
- Validate JSON parses; never emit unescaped `</script>`.

### 5.6 Other pages
Quick audit, fix only if trivial: `KbCategory.razor` and `KbSearch.razor` descriptions should also be in the 110 to 160 range via `SeoText` (and search results should be `noindex` if they are not already, since that is standard). Report what you find in the PR.

### 5.7 Package bump
`SyntaxCircus.Blazor.Seo` 0.1.5 to 0.1.8 in `Directory.Packages.props`. Read the package's changelog/commits in `D:/dev/SyntaxCircus/SyntaxCircus.Blazor.Seo` for breaking changes; fix call sites (`Seo/PortalSeoRegistration.cs`, `SeoHead` usages). If a sibling-package change is needed, use a feature branch there with a local `nuget pack`; do not publish.

## 6. Constraints and conventions
- US English only (hard rule): color, line (not queue), etc. in all copy, comments, docs and commit messages.
- Conventional Commits; trunk-based; short-lived feature branch off `main`; do not push or open a PR unless asked.
- `TreatWarningsAsErrors` is on; follow `.editorconfig` (4 spaces, Allman braces).
- Tests: xUnit with **Shouldly** assertions; unit tests may use NSubstitute and EF InMemory; integration tests use real Postgres via TestContainers (do not mock the database). 80% coverage floor. Existing SEO tests live in `tests/TechStrap.Portal.Tests/Seo/` (`SeoHostTests`, `SeoProductHostTests`, `SitemapHostTests`, `PortalSitemap*Tests`, `KbStructuredDataTests`, `JsonLdTextTests`, `ProductHostSeoUrlBuilderTests`); mirror their harness. Plan doc `docs/superpowers/plans/2026-10-06-phase-09c-kb-seo.md` explains the original KB SEO design and mentions `KbHomeHostTests`.
- Use `dotnet test --project <test project>` per project (global.json selects Microsoft.Testing.Platform); do not run whole-solution commands if they stall.
- Everything user-controlled (names, intros, descriptions) is plain text and encoded; no raw HTML.
- Security: new admin fields go through the same authorization, validation and audit paths as tagline.

## 7. Approaches considered

1. **Per-product editable fields with generated fallback (chosen by Jon).** Best long-term: owners control wording, tests guarantee the floor even when unset. Costs a migration and Admin UI.
2. Generated-only (no new fields): smallest change, but generic copy is weak for SEO and cannot be tuned per product. Rejected.
3. Generated first, overrides later: viable but duplicates review cycles; Jon preferred doing it in one pass.

For `/`: Jon chose **keep it a landing page and give it real content** over server-301 to `/kb` or making `/` the help center. Note that the landing page in practice is `ProductHome`.

## 8. Tests and acceptance

Add tests (Shouldly) that fail on the original behavior:
- `SeoText`: blank/short candidate uses fallback; over-long clamps at a word boundary to at most 160; whitespace/control chars normalized; fallbacks land in 110 to 160 for edge inputs (very long product name, zero categories, many categories).
- Rendered `ProductHome` and `KbHome` (use the existing host/render test harness): description length 110 to 160 (target 150 to 160 for generated); canonical present and host-correct; one h1; `og:*` present; JSON-LD parses; visible text at least 150 words with a seeded product (categories + articles); KB-API failure still yields 200 with full head.
- Domain/Application: new fields validate (max length, trim, blank to null), round-trip through create/update/get; update without the fields leaves existing values; migration applies cleanly (integration test with TestContainers if the repo already does this for migrations).
- Admin: editor round-trips the fields.
- Sitemap still lists the product home and KB home.

Manual: run the portal locally with a seeded product, view-source `/` and `/kb` on the product host and confirm head tags and body text.

Acceptance: description lengths and body word counts as above; all new and existing tests pass for the touched projects; no warnings; migration present; docs entry added.

## 9. Out of scope here, tracked separately
The other four short-description findings are in Sin Forgiver, not Techstrap: `https://www.sinforgiver.com/support`, `/tos`, `/copyright`, `/privacy` (in `D:/dev/SyntaxCircus/sinforgiver/src/SinForgiver.Web`). Do not change them from this work; Jon's Sin Forgiver session handles them, ideally reusing the `SeoText` idea.

## 10. After deploy (Jon)
Re-request indexing for `/` and `/kb` in Bing Webmaster Tools (and Google Search Console if the support host is registered there) and re-run the SEO report. Bing re-scans lag by days.

## 11. Suggested order of work
1. Branch `feat/portal-seo` in `techstrap`. Baseline: run the Portal tests.
2. Package bump and fix compile fallout.
3. `SeoText` + tests.
4. Product fields: Domain, Contracts, Application, Infrastructure migration, Admin editor, theme view model, with tests.
5. Shared featured-articles component and any KB client addition.
6. `ProductHome` and `KbHome` content + SEO + JSON-LD, with render tests.
7. Category/search audit, decision-log entry, final full test pass for touched projects.

Report back with: what changed, anything you had to decide that this doc leaves open, test results, and any API additions.
