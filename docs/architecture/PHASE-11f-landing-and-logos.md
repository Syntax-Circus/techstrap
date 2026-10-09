# PHASE-11f: Portal landing page and product logos

## Objective

Give the shared Portal host a landing page that lists the products, and give a
product a logo that is uploaded rather than linked:

- `TECHSTRAP_PORTAL_LANDING=Products` turns the Portal root (`/` on the default host) into a list of product cards: logo, name and a short tagline, each linking to the product's help centre (its own host when it has one, `/p/{key}` otherwise). `Neutral`, the default, keeps today's page byte for byte.
- A per-product flag, `ListedOnLanding` (default true), keeps a product off that list. It changes nothing else: the product stays reachable by key and host and keeps its sitemap entries.
- A product gains a one-line `Tagline`, shown on its landing card.
- An administrator can upload a logo (PNG, JPEG or WebP, at most 1 MiB) for a product. The uploaded logo supersedes the linked `LogoPath` everywhere a logo is shown (Portal header, Open Graph image, Admin preview, emails when the Worker knows the Api's public address). Removing it falls back to the linked logo.

Delivery is one pull request, PHASE-11f, before PHASE-12c, so the UAT soak
and `v0.3.0` include it. D-052 records the decision and amends D-045 ("`/`
shows a neutral page with no product list").

## Dependencies

- **Depends on:** [PHASE-09](PHASE-09-public-portal.md) (Portal root, `ProductThemeViewModel`, SEO, sitemap, output cache, header rules), [PHASE-11e](PHASE-11e-product-hosts.md) (`PortalLinks`, `ProductHostContext`, `ProductHostMap`, the public product list), [PHASE-08](PHASE-08-knowledge-base.md) (the KB image pipeline this phase mirrors: D-044), [PHASE-04](PHASE-04-agent-auth-and-admin-config.md) and [PHASE-07](PHASE-07-admin-app.md) (the `Product` aggregate, product handlers, the Admin product editor), [PHASE-11](PHASE-11-client-sdk.md) (Contracts is published, 0.2.0; the DTO changes are trailing optional parameters and the break is named in the Version notes).
- **Unblocks:** [PHASE-12](PHASE-12-release-hardening.md) 12c (the UAT soak runs with this feature; `v0.3.0` publishes Contracts 0.3.0).
- **External prerequisites:** none to build. To use it, the operator sets `TECHSTRAP_PORTAL_LANDING=Products` on the Portal and, for emails to carry an uploaded logo, `TECHSTRAP_API_PUBLIC_URL` on the Worker; the Caddy Api site must proxy `/product-logos/` as it proxies `/kb-images/`.

## Architecture Decisions

- **Landing mode is deployment configuration; listing is per product.** `TECHSTRAP_PORTAL_LANDING` (`Neutral` | `Products`, an enum, default `Neutral`) lives in `PortalOptions`. `Products` together with a non-blank `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` is a startup validation error naming both keys: one root cannot both redirect and list. Whether a product appears is data on the product (`Product.ListedOnLanding`), edited in the Admin, not configuration.
- **The list is the existing public list.** `GET api/public/products` (D-045) keeps returning every active product, as the sitemap and the host map need; each summary now carries `Tagline`, `LogoUrl` (the effective logo), `AccentColour` and `ListedOnLanding`. The Portal filters on the flag for the landing page only. `GET api/public/products/{productKey}` is unchanged: an inactive product is 404, an unlisted one is 200. **Assumption:** keys were already public through the sitemap, so listing name, tagline, logo and accent of active products on the default host is accepted; D-045's "nothing can be enumerated" becomes "nothing inactive can be enumerated".
- **Unlisted means the landing card only.** An unlisted product keeps `/p/{key}`, its own host, its sitemap entries and its KB in search engines. Hiding it from the sitemap would be theatre (the key is reachable anyway) and would drop its KB tree from search. **Assumption:** this is what the owner means by "hiding specific products"; a product that must not be public is deactivated, as today.
- **Hosted products are listed and link to their host.** A card for a product with a `PortalHost` links to `https://{host}/` through `PortalLinks.ForListedProduct` and `PortalLinks.Absolute`; the landing never builds a URL from the Host header. A product host's root is that product's home (`ProductHostMiddleware` rewrites `/`), so the landing list renders only when `ProductHostContext.IsProductHost` is false; the component guards it explicitly as well.
- **Fail soft.** An empty list, a 429 or any Api failure on the landing renders the `Neutral` copy with status 200; the root never shows an error page for the list.
- **SEO and cache for the landing.** In `Products` mode the root has an `<SeoHead>` (indexable, canonical `Links.Absolute("/")`) and the sitemap lists `/`. The root is output-cached and answers `Cache-Control: public, max-age=60` only in `Products` mode and only for `/` with no query string. The header rules become `PortalHeaderRules.Rules(PortalOptions)`; the output-cache policy reads `PortalOptions` from the request services. The page renders no form, so no antiforgery cookie is set and it is storable.
- **Tagline.** `ProductBranding.Tagline` (`string?`, trimmed, plain text, no line breaks, at most `DomainLimits.TaglineMaxLength = 160`; `Guard.OptionalTagline`, error `tagline-invalid` with target `tagline`). `BrandingRules.IsAcceptableTagline` in Contracts mirrors it with a parity test. The tagline takes part in `ProductBranding` equality so a tagline-only edit is detected and audited (`branding`).
- **Uploaded logo is a file name, not a URL.** `ProductBranding.UploadedLogo` (`string?`) holds `{32 hex}.{png|jpg|webp}`; the file lives under `product-logos/` on the storage volume, which only the Api mounts (D-043). The absolute URL is built at read time by `IProductLogoUrls.UrlFor(name)` (Application interface): the Api implementation gives `{TECHSTRAP_API_PUBLIC_URL}/product-logos/{name}` like `KbImageUrls`; the Worker implementation gives it when the Worker has `TECHSTRAP_API_PUBLIC_URL` (new, optional) and `null` otherwise. The Worker never references the Api project (architecture rule). **Assumption:** storing the name keeps an Api hostname move from orphaning logos, which the KB images (absolute URLs in Markdown, D-044) do not survive; that asymmetry is accepted.
- **Effective logo.** `uploaded URL ?? LogoPath`, computed in Application mapping: `ProductBrandingDto.UploadedLogoUrl` (read-only) plus the public DTOs' `LogoPath`/`LogoUrl` carrying the effective value, so the Portal, `KbSeo.Image`, `ProductThemeViewModel` and `EmailBranding` need no new logic. `DrainEmailOutboxHandler` builds `EmailBranding` with the effective URL (blank Worker setting: the linked logo). A Production Api behind plain http shows no uploaded logo anywhere (the Portal keeps https-only logos and the email layout requires https); SELF-HOSTING says so.
- **Store, limits and names.** `IProductLogoStore` (Application) and `ProductLogoStore` (Infrastructure) mirror the KB image store: the declared length and the real length are both capped at `ProductLogoLimits.MaxBytes = 1 MiB` (`product-logo-too-large`), the type comes from the bytes (`KbImageSignatures.Identify`, restricted to png, jpeg and webp; gif, SVG, HTML and a zero-byte file answer `product-logo-type-not-allowed`), the name is a version 7 GUID plus the sniffed extension, and a failed store deletes its own key. The capped read and sniff are extracted from `KbImageStore` into one shared `CappedImageIntake`; the KB store keeps its limits and codes. `ProductLogoName` (regex, `StorageKey`, `ContentTypeOf`) and `ProductLogoLimits` live in Contracts so the Admin can pre-check a file.
- **Write ordering.** Upload: store the new file, set the name on the product, commit, then delete the previous file best effort; a failed commit deletes the new file. Remove: clear the name, commit, delete the file best effort. Both are audited as `ProductUpdated` with `changed: ["uploadedLogo"]`. A branding update through `PUT api/products/{id}` never touches the uploaded logo: `ProductBranding.CreateForUpdate` carries it over from the stored branding (it is not in `ProductBrandingRequest`).
- **Routes.** `POST api/products/{id:guid}/logo` (Admin policy, multipart part `file`, `[ReadFormBeforeBinding]`, `RequestSizeLimit(MaxBytes + 1 MiB)`, 200 `ProductDto`), `DELETE api/products/{id:guid}/logo` (Admin, 200 `ProductDto`), both in the D-022 admin route list and in 02-ARCHITECTURE 7.2 with their handlers `UploadProductLogoRequestHandler` and `RemoveProductLogoRequestHandler`. Anonymous `GET`/`HEAD /product-logos/{name}` mirrors `/kb-images/{name}` (name validated before storage is touched, `X-Content-Type-Options: nosniff`, `Cache-Control: public, max-age=31536000, immutable`, `Cross-Origin-Resource-Policy: cross-origin`, 404 `no-store`, CSP `sandbox` through the `AttachmentSandbox` prefix list) and is a static exempt route in 7.6.
- **Contracts 0.3.0 (trailing optional parameters; a binary break named in the Version notes).** `ProductDto(..., bool ListedOnLanding = true)`; `CreateProductRequest(..., bool? ListedOnLanding = null)` (null means true); `UpdateProductRequest(..., bool? ListedOnLanding = null)` (null means unchanged, as `PortalHost`); `ProductBrandingDto(..., string? Tagline = null, string? UploadedLogoUrl = null)`; `ProductBrandingRequest(..., string? Tagline = null)`; `PublicProductDto(..., string? Tagline = null)`; `PublicProductSummaryDto(..., string? Tagline = null, string? LogoUrl = null, string? AccentColour = null, bool ListedOnLanding = true)`. The package version comes from the `v0.3.0` tag at the end of PHASE-12c; the README gets the `### 0.3.0` entry now.
- **Admin.** The product editor gains the tagline field, a "Listed on the landing page" checkbox (on create and edit; create sends the value explicitly) and, when editing, a `ProductLogoUploadButton` modelled on `KbImageUploadButton` (preview of the effective logo, Upload, Remove uploaded logo; client-side extension and size pre-check; errors mapped to field messages). Upload and remove return a `ProductDto` with a new `Version`; the editor splices only `Branding.UploadedLogoUrl` and `Version` into the live model so pending edits survive and the next save does not 409. The create form shows "Save the product first, then upload a logo from its editor" because create navigates to the API-keys page. The products list gains a "Listed" column.
- **Persistence.** Columns `tagline varchar(160) null`, `uploaded_logo varchar(64) null`, `listed_on_landing boolean not null default true` (`HasDefaultValue(true)`), tool-generated migration `AddProductLandingAndLogo`; `05-SCHEMA.md` and the 02-ARCHITECTURE column list are updated.
- **Security.** The upload rows of `docs/security/SECURITY-REVIEW.md` cite the new tests (size, magic bytes, zero byte, hostile names irrelevant because the name is generated, random keys, nosniff and sandbox, SVG and HTML refused, prefix isolated from `attachments/`, disk full gives the generic 500 ProblemDetails and no partial file). SR-05 gains a note that logo keys are public by design, as KB image keys are. The hostile upload corpus gains a `productLogo` expectation per entry.
- **Operations notes.** SELF-HOSTING documents `TECHSTRAP_PORTAL_LANDING`, the Worker's optional `TECHSTRAP_API_PUBLIC_URL` and the `/product-logos/` proxy path beside `/kb-images/`; the backup runbook names the new prefix and adds it to the post-restore ownership check; `backup.sh` needs no change (the volume is archived whole).

## Application Boundaries

Follow _template APPLICATION_ARCHITECTURE.md. This phase adds two handlers
(logo upload and removal) and extends the product handlers and the public
queries; the landing page is presentation code.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| `POST api/products` and `PUT api/products/{id}` | `CreateProductRequestHandler`, `UpdateProductRequestHandler` (existing, extended) | `IProductRepository`, `IProductLogoUrls`, Domain guards | EF repository with the three new columns | 201/200 with `Tagline`, `ListedOnLanding`, `UploadedLogoUrl`; `tagline-invalid` -> 400 | Extend the existing handlers |
| `POST api/products/{id:guid}/logo` | `UploadProductLogoRequestHandler` (new) | `IProductRepository`, `IProductLogoStore`, `IProductLogoUrls`, `IUnitOfWork`, `IAdminEventRepository`, `IClock` | `ProductLogoStore` over `IStorageProvider` | 200 `ProductDto`; `product-logo-too-large` -> 400; `product-logo-type-not-allowed` -> 400; `file-required` -> 400; unknown product -> 404; storage failure -> generic 500 | New handler |
| `DELETE api/products/{id:guid}/logo` | `RemoveProductLogoRequestHandler` (new) | `IProductRepository`, `IProductLogoStore`, `IProductLogoUrls`, `IUnitOfWork`, `IAdminEventRepository`, `IClock` | Same | 200 `ProductDto` (idempotent when there is no uploaded logo); unknown product -> 404 | New handler |
| `GET api/public/products` and `GET api/public/products/{productKey}` | Existing public query handlers (extended) | Existing abstractions plus `IProductLogoUrls` | Existing read model, projecting the new fields and the effective logo | 200 with the new fields; shape unchanged otherwise | One projection change |
| `GET`/`HEAD /product-logos/{name}` | Exempt (static asset, like `/kb-images/{name}`) | `IProductLogoStore.OpenReadAsync` | `IStorageProvider` | 200 with immutable cache headers and sandbox; 404 `no-store` | Transport concern; see Boundary Validation |
| Email drain | `DrainEmailOutboxHandler` (existing, extended) | `IProductLogoUrls` (Worker implementation) | Options-bound URL base | `EmailBranding.LogoPath` is the effective logo | Call-site change only |
| Portal landing | Exempt (presentation; reads `IPublicProductClient`) | `IPublicProductClient`, `PortalLinks`, `ProductHostContext`, `PortalOptions` | Typed client | Cards or the neutral copy | Code-behind view model |

## Razor Component Boundaries

Follow _template RAZOR_COMPONENT_ARCHITECTURE.md.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| Portal `Home` | Existing paired files; the code-behind reads the mode, calls the client, filters and maps | `LandingCardViewModel(Name, Tagline, LogoUrl, Href)` (feature-local, presentation only) built in code-behind from `PublicProductSummaryDto` | One render per request (static SSR); fail-soft to neutral copy | `PublicProductSummaryDto` only |
| Admin product editor | Existing paired files; tagline and listed fields bound in code-behind | `ProductEditorViewModel` gains `Tagline`, `ListedOnLanding`, `UploadedLogoUrl` | Existing dirty/save states; upload splices `UploadedLogoUrl` and `Version` | `CreateProductRequest`, `UpdateProductRequest`, `ProductDto` |
| Admin `ProductLogoUploadButton` | New paired files, modelled on `KbImageUploadButton` | No ViewModel; parameters are the product id, the current URL and two callbacks | Idle, uploading, error; stale-result guard | `ProductDto` from the client |
| Admin products list | Existing paired files; one added column | `ProductRowViewModel` gains `ListedOnLanding` | Unchanged | `ProductDto` |

## Syntax Circus Packages

Versions in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md). No new package is added or
upgraded in this phase.

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.Storage` | `IStorageProvider` | The logo store writes and reads `product-logos/` keys through it | Store tests with the real local provider and the failing provider |
| `SyntaxCircus.Common` | `Result`, `ResultError` | The three new error codes | Handler tests |
| `SyntaxCircus.AspNetCore.Common` | Result-to-HTTP mapping | 400 for the new codes | Api tests |
| `SyntaxCircus.Blazor.Seo` | `SeoHead`, sitemap | The landing's head and the `/` sitemap entry | Portal host tests |

## Deliverables

- [ ] Domain: `Product.ListedOnLanding` and `SetListedOnLanding`, `ProductBranding.Tagline` and `UploadedLogo` with carry-over on update, `Guard.OptionalTagline`, `DomainLimits.TaglineMaxLength`.
- [ ] Contracts 0.3.0: the seven records' trailing optional parameters, `BrandingRules.IsAcceptableTagline`, `ProductLogoLimits`, `ProductLogoName`, README Version notes.
- [ ] Persistence: three columns, mappings, migration `AddProductLandingAndLogo`, schema docs.
- [ ] Application and Api: extended product handlers and public queries, `IProductLogoUrls`, `IProductLogoStore`, upload and remove handlers and routes, `/product-logos/{name}`, D-022 list, 02-ARCHITECTURE rows.
- [ ] Worker: optional `TECHSTRAP_API_PUBLIC_URL`, effective logo in emails.
- [ ] Portal: `TECHSTRAP_PORTAL_LANDING`, landing cards, SEO, sitemap, cache rules.
- [ ] Admin: tagline, listed checkbox, logo upload and removal, list column.
- [ ] D-052 in [04-DECISION-LOG.md](04-DECISION-LOG.md) (amends D-045); SELF-HOSTING, DEPLOYMENT, runbook, security review, ADMIN-APP, PORTAL-APP, roadmap and discovery rows; Pester pins.

## Actionable Tasks

- [ ] **P11f-T01** Spec, decision and rows: this page, D-052 (amends D-045), roadmap and discovery rows for 11f, Pester pins for the new phrases
  - **Depends on:** owner answers of 2026-10-09
  - **Validation:** `scripts/Invoke-ScriptTests.ps1` green with the new pins.
- [ ] **P11f-T02** Contracts 0.3.0: trailing optional parameters on `ProductDto`, `CreateProductRequest`, `UpdateProductRequest`, `ProductBrandingDto`, `ProductBrandingRequest`, `PublicProductDto` and `PublicProductSummaryDto` with `<param>` docs; `BrandingRules.IsAcceptableTagline`; `ProductLogoLimits.MaxBytes`; `ProductLogoName`; README Stability line and `### 0.3.0` Version notes; positional constructions in tests updated
  - **Depends on:** P11f-T01
  - **Validation:** the solution compiles; a test constructs each record positionally with the 0.2.0 argument list; `ProductLogoName` accepts `{32 hex}.png|jpg|webp` and rejects gif, SVG, upper case, traversal and a missing extension; the README names the records and says consumers recompile.
- [ ] **P11f-T03** Domain: `Product.ListedOnLanding` (default true) and `SetListedOnLanding`; `ProductBranding.Tagline` and `UploadedLogo` as trailing optional factory parameters; `CreateForUpdate` carries `UploadedLogo` over; `Guard.OptionalTagline` and `DomainLimits.TaglineMaxLength = 160`; parity test with `BrandingRules`
  - **Depends on:** P11f-T02
  - **Validation:** Domain tests: a 161-character tagline, a tagline with a line break and a tagline of only whitespace are rejected or normalised to null as specified; equality differs on the tagline; `CreateForUpdate` keeps the uploaded logo; `Restore` without the new arguments gives listed = true and null tagline and logo.
- [ ] **P11f-T04** Persistence: `ProductRecord`, `ProductRecordConfiguration` (`HasDefaultValue(true)`), `ProductMappings`, migration `AddProductLandingAndLogo` generated with the `ef-migrate` skill, `05-SCHEMA.md` and the 02-ARCHITECTURE column list
  - **Depends on:** P11f-T03
  - **Validation:** integration test round-trips the three fields; an existing row (inserted without the column) reads listed = true; `has-pending-model-changes` reports none; the schema docs test passes.
- [ ] **P11f-T05** Application and Api read side: `IProductLogoUrls` (Application) and its Api implementation; `ProductMapping` and the public handlers project tagline, listed flag and the effective logo; `CreateProductRequestHandler` and `UpdateProductRequestHandler` apply tagline and the `ListedOnLanding` null semantics and audit `listedOnLanding`
  - **Depends on:** P11f-T04
  - **Validation:** handler tests: create with null lists the product; update with null leaves the flag; update with false unlists and audits `listedOnLanding`; a tagline-only edit audits `branding`; the effective logo is the uploaded URL when a name is stored and the linked path otherwise; the public list carries the four new fields and still returns unlisted active products; the property pins of the public DTO tests are amended; an Api host test reads the new fields through `GET /api/public/products`.
- [ ] **P11f-T06** Write side: `CappedImageIntake` extracted from `KbImageStore`; `IProductLogoStore` and `ProductLogoStore`; the upload and remove handlers; the two admin routes; `/product-logos/{name}` with the sandbox prefix; D-022 list; 02-ARCHITECTURE 7.2 rows and 7.6 static row; corpus column; disk-full twin
  - **Depends on:** P11f-T05
  - **Validation:** store tests (limits at the boundary, type set, gif and SVG refused, zero byte refused, hostile corpus with the `productLogo` column, delete on failed store); KB store tests unchanged and green; Api tests with real Postgres: upload returns 200 with `UploadedLogoUrl`, a second upload replaces and the old file is gone, remove clears and deletes, 2 MiB -> 413 or 400 as the pipeline maps it, SVG -> 400 `product-logo-type-not-allowed`, agent token -> 403, unknown id -> 404; serving tests mirror `KbImageServingTests`; the disk-full twin leaves no file; the entry-point catalog and the D-022 coverage tests pass.
- [ ] **P11f-T07** Worker: `IProductLogoUrls` Worker implementation bound to the optional `TECHSTRAP_API_PUBLIC_URL` (Worker `appsettings.json`, both `.env` examples, `ConfigContract` blank-keys list); `DrainEmailOutboxHandler` uses the effective logo
  - **Depends on:** P11f-T05
  - **Validation:** handler tests: with the setting, an uploaded logo is the email logo; without it, the linked logo is; `ConfigContract.Tests.ps1` passes.
- [ ] **P11f-T08** Portal landing: `PortalOptions.Landing` and the validator rule; `Home` cards with fail-soft; `ShellCopy`; `SeoHead`; sitemap `/` entry; `PortalHeaderRules.Rules(PortalOptions)` and output cache for `/` without a query string
  - **Depends on:** P11f-T06
  - **Validation:** host tests: `Neutral` is byte-identical (existing root tests kept); `Products` renders cards in key order with name, tagline and an https logo only, a hosted product links to `https://{host}/`, an unlisted product is absent, an empty list, a 429 and a failure render the neutral copy, a product host never renders the list, no `Set-Cookie`, the second request is a cache hit, `/?x=1` is not cached, the head has a canonical and the sitemap lists `/`; `NeutralPagesGuardTests` run in `Neutral`; `Products` with a default product fails startup with both keys in the message.
- [ ] **P11f-T09** Admin: `IProductsClient.UploadLogoAsync` and `RemoveLogoAsync`; editor tagline and listed fields; `ProductLogoUploadButton`; `PreviewLogo` prefers the uploaded logo; version splice; create-mode hint; products list column
  - **Depends on:** P11f-T06
  - **Validation:** bUnit tests: create sends the flag explicitly; update sends null when untouched and the value when toggled; a 161-character tagline blocks the save with the field message; upload shows the new preview and keeps a pending name edit, and the next save sends the new version; remove restores the linked logo; the create form shows the hint and no upload control; the list shows the Listed pill; client tests for the two methods.
- [ ] **P11f-T10** Docs and close-out: SELF-HOSTING (settings, Caddy `/product-logos/`, plain-http note), DEPLOYMENT, Api and deploy `.env` proxy notes, runbook prefixes and ownership check, SECURITY-REVIEW rows and SR-05 note, ADMIN-APP and PORTAL-APP, 02-ARCHITECTURE 8.2 and 11.2, roadmap and discovery rows, Pester pins, this page's ticks and As-built notes
  - **Depends on:** P11f-T01 to P11f-T09
  - **Validation:** `dotnet build`, `dotnet test` and the Pester suite green; the compose stack shows a card list at `/` with `TECHSTRAP_PORTAL_LANDING=Products`, an uploaded logo on `/p/{key}` and the headers on `/product-logos/{name}`.

## Success Criteria

- [ ] With `TECHSTRAP_PORTAL_LANDING=Products` the default host's root lists the active, listed products as cards with logo, name and tagline; hosted products link to their host; `Neutral` is unchanged.
- [ ] Unlisting a product removes only its card.
- [ ] An administrator can upload a PNG, JPEG or WebP logo of at most 1 MiB and remove it; the uploaded logo shows on the Portal, in the Admin preview, as the Open Graph image and, when the Worker has the Api public URL, in emails; a PUT of the product never drops it.
- [ ] SVG, GIF, HTML, zero-byte and oversize uploads are refused with ProblemDetails; a full disk leaves no partial file and answers the generic 500.
- [ ] `/product-logos/{name}` serves with nosniff, immutable cache and the sandbox CSP, and 404s with `no-store` for any name the store could not have written.
- [ ] Contracts 0.3.0 Version notes name the records and the recompile; the solution, the Pester suite and the architecture gates are green; D-052 is recorded.

## Boundary Validation

- [ ] Application use-case entry points delegate to the named handlers listed above (the two logo routes to the two new handlers; one `[FromServices]` handler per action).
- [ ] Framework-owned static exemptions execute no application workflow (`/product-logos/{name}` only validates the name and streams the file).
- [ ] Handler constructor dependencies contain only approved abstractions (`IProductLogoStore`, `IProductLogoUrls`, repositories, `IUnitOfWork`, `IClock`).
- [ ] Persistence entities do not cross boundaries (the Portal sees `PublicProductSummaryDto`; the file name leaves the Api only as an absolute URL).
- [ ] Cancellation reaches asynchronous dependencies (store, repository and client calls take the request token; the post-commit delete uses a non-request token).
- [ ] Expected outcomes and transport mapping have focused tests (400 codes, 404, 403, 413, cache headers, startup validation).
- [ ] Infrastructure implementations have integration coverage (store against the local provider and the failing provider; migration on an empty and the current schema).
- [ ] Inline Razor components contain only simple parameters; every component beyond the inline ceiling uses paired `.razor` and `.razor.cs` files.
- [ ] Each Razor ViewModel is feature-local and presentation-only (`LandingCardViewModel`, `ProductEditorViewModel`).
- [ ] API contracts use DTO names, never Razor ViewModels.
- [ ] Repeated or business-meaningful literals are named constants (`TaglineMaxLength`, `ProductLogoLimits.MaxBytes`, the `product-logos/` prefix, the 60 s root cache lifetime).
- [ ] Duplicated-looking logic was evaluated (the capped read and sniff are shared with the KB store; name rules differ by design and stay separate; the https logo check stays in `ProductThemeViewModel`).
- [ ] Security: the raw Host header never reaches a URL; the file name is generated, never taken from the client; served files carry nosniff and the sandbox CSP.

## Risks and Open Questions

- [ ] **Contracts 0.3.0 binary break.** Consumers compiled against 0.2.0 recompile; no shims (owner rule, D-050). Named in the Version notes and the Release.
- [ ] **Unlisted is not hidden.** An unlisted product is still reachable and in the sitemap by design; the Admin help text says so.
- [ ] **Worker setting is optional and easy to forget.** Emails silently keep the linked logo when the Worker has no Api public URL; SELF-HOSTING lists the setting in the Worker table.
- [ ] **Plain-http Api in Production.** Shows no uploaded logo anywhere; documented.
- [ ] **Env-parity pins.** Every new key must land in the template, the deploy example, the SELF-HOSTING table and `ConfigContract` in the same task.
- [ ] **Orphan files.** A crash between the file store and the commit can leave a `product-logos/` file with no row (as D-044 accepts for KB images); the delete-on-failed-commit covers the common path. No sweeper in this phase.

## Handoff

Before [PHASE-12](PHASE-12-release-hardening.md) 12c starts: the landing page,
the flag, the tagline and the uploaded logo work on the compose stack; D-052 is
recorded with D-045 amended; SELF-HOSTING names the two settings and the proxy
path. 12c deploys the result to UAT (`TECHSTRAP_PORTAL_LANDING=Products`, the
owner uploads the product logos), soaks it and tags `v0.3.0`, which publishes
Contracts 0.3.0.
