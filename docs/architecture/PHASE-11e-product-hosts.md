# PHASE-11e: Product hosts

## Objective

Let a product have its own public hostname (for example
`support.dragonpoop.com`) in addition to the shared portal host:

- The default host (`TECHSTRAP_PORTAL_PUBLIC_URL`, for example `support.syntaxcircus.com`) keeps serving every product under `/p/{key}` and the default product (`TECHSTRAP_PORTAL_DEFAULT_PRODUCT`) at `/`.
- A product with a `PortalHost` is also served at `https://{host}/` with clean paths (`/`, `/contact`, `/kb/...`, `/t/{token}`); the `/p/{key}` prefix disappears on that host.
- Emails, admin links, canonical URLs, JSON-LD and the sitemap use the product host for that product.
- The mapping lives on the product (data, edited in the Admin), not in configuration.

Delivery is one pull request, PHASE-11e, before PHASE-12. D-050 records the
decision and amends D-002 ("single portal domain with per-product theming").

## Dependencies

- **Depends on:** [PHASE-09](PHASE-09-public-portal.md) (portal routes, `ProductPageBase`, SEO, sitemap, output cache), [PHASE-05](PHASE-05-intake-email-worker.md) (`PortalLinkOptions`, intake and notification link call sites, the Worker's email drain), [PHASE-04](PHASE-04-agent-auth-and-admin-config.md) and [PHASE-07](PHASE-07-admin-app.md) (the `Product` aggregate, product handlers and the Admin product editor), [PHASE-11](PHASE-11-client-sdk.md) (the Contracts package is published as `v0.1.0`, so the additive DTO fields must stay additive).
- **Unblocks:** [PHASE-12](PHASE-12-release-hardening.md) (UAT configuration adds one Caddy site and one DNS record per product host; the security review covers the Host-header handling).
- **External prerequisites:** none to build. To use a product host in a deployed environment the operator adds a DNS record for the host and a Caddy site that proxies to the Portal; this is PHASE-12 UAT work, not part of this pull request.

## Architecture Decisions

- **Mapping on the product, not in config.** `Product.PortalHost` (`string?`) holds the lowercase hostname; `null` means the product is served on the default host only. No new setting is added: `TECHSTRAP_PORTAL_PUBLIC_URL` and `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` keep their meaning. This amends D-002; its note that per-product domains multiply TLS and DNS burden is now an accepted cost.
- **Host shape.** A new `HostNameShape` rule in `src/TechStrap.Domain/Rules/` validates: lowercase after normalization, RFC 1123 labels (letters, digits, hyphens), dots between labels, no leading or trailing hyphen in a label, no scheme, port, path or userinfo, at most `DomainLimits.HostNameMaxLength` (253) characters, and at least one dot (a bare word is rejected). `Product.SetPortalHost(string?)` normalizes to lowercase, validates through the rule and stores the value. Domain owns the limit; Contracts carries a copy (`ProductLimits.HostNameMaxLength`) with a parity test, as for the other limits.
- **Uniqueness.** The host is unique case-insensitively across products. EF column `PortalHost` varchar(253) nullable with a unique index (all stored values are lowercase, so the index is case-insensitive in effect). Error codes: `product-host-invalid` (shape) and `product-host-taken` (uniqueness, mapped to 409 like the other product conflicts). The migration is `AddProductPortalHost` (name pattern `yyyyMMddHHmmss_Name`, created following the repo's `ef-migrate` skill note).
- **Contracts are additive.** `PortalHost` is added to `ProductDto`, `CreateProductRequest`, `UpdateProductRequest`, `PublicProductDto` and `PublicProductSummaryDto`. The public endpoints keep their shape: `GET api/public/products` (summaries now carry `PortalHost`) and `GET api/public/products/{productKey}`.
- **Links are built from stored values only.** `PortalLinkOptions` gains product-aware overloads: `TicketLink(Product product, string token)` and `ArticleLink(Product product, ...)`. The base is `https://{product.PortalHost}` when set (product hosts are always https), else the configured public URL. On a product host the article path drops `/p/{key}`. The old overloads stay for callers with no product. Local development keeps `http://localhost:8082` on the default host.
- **The raw Host header is a lookup key, never a value.** `ProductHostMiddleware` lowercases `Request.Host.Host` and looks it up against the stored product hosts. The header value never reaches a URL, a log line or a redirect target; only a matched stored `PortalHost` or the configured public URL does. Host poisoning and open redirects are prevented by construction. An unknown host (a raw IP, a stray name) behaves as the default host.
- **Host resolution.** A scoped `IProductHostResolver` is backed by `ProductHostMap`, an `IMemoryCache` entry built from the `GET api/public/products` summaries with a 60 s TTL. A miss refreshes the map at most once per 10 s, which bounds Api calls from unknown or hostile hosts. The current host context (`ProductHostContext { Key?, Host? }`) is scoped per request.
- **Middleware, before routing.** `Hosting/ProductHostMiddleware` runs before `UseRouting`. On a product host it rewrites `/` to `/p/{key}` and `/contact`, `/contact/received`, `/lost-link`, `/kb...` and `/suggest` to `/p/{key}/...`. It passes through `/t/...`, `/_framework`, `/_blazor`, `/_content`, static files, `/sitemap.xml`, `/robots.txt`, `/health*`, `/not-found` and `/error`. `/p/{sameKey}/x` gets a 301 to `/x` (canonical). `/p/{otherKey}/x` gets a 301 to that product's canonical absolute URL: its host with a clean path when it has one, else the default host with `/p/{otherKey}/x`. On the default host, `/p/{key}/...` for a product that has a host gets a 301 to `https://{host}/...` (clean).
- **Links in components.** A scoped `PortalLinks` service (`ProductHome(key)`, `Contact(key)`, `KbArticle(key, category, slug)`, `Ticket(token)`, `Absolute(path)` and the rest) is built on `PortalRoutes`, the host context and the product host map. It returns a clean relative path when the target product is the current host's product, an absolute `https://{host}/...` when the target product has its own host, and `/p/{key}/...` otherwise. Components use `Links.*` instead of `PortalRoutes.*`; an architecture test forbids `PortalRoutes.` in `.razor` and `.razor.cs` files except inside `PortalLinks`. `RouteLiteralTests` (no route literals outside `PortalRoutes`) stays.
- **SEO per host.** The canonical URL, JSON-LD and the sitemap go through `PortalLinks.Absolute`. On a product host `/sitemap.xml` lists only that product, with clean paths on that host. The default host's sitemap lists only the products without a host (the 15-minute cache stays). `UseSyntaxCircusSeo`'s legacy-host redirect, driven by `CanonicalHost:*`, is unchanged.
- **Output cache varies by host.** `PortalOutputCache` moves from `SetVaryByHost(false)` to `SetVaryByHost(true)`; the existing "never vary by host" test flips. Antiforgery cookies are per host (no change needed); CSP `form-action 'self'` already fits a product host.
- **Follow-up redirects** keep using the last token segment and never another host.
- **Admin.** The product editor gets an optional "Portal host" field with shape validation and the conflict message, and the product list shows it. "View on portal" (`PortalUrlOptions`) uses the product host when `ProductDto.PortalHost` is set.
- **Worker compatibility.** `DrainEmailOutboxHandler` compares `a.Url.EndsWith(PortalLinkOptions.ArticlePath(...))`; it must keep working for both path shapes (with and without `/p/{key}`).
- **Security tests pin the leak surface.** A request with an unknown or hostile `Host` renders the default host's pages and every generated URL still uses the configured public URL; no Host-derived text appears in a body, header, log or redirect.
- **Operations notes.** `ALLOWEDHOSTS` and HSTS `includeSubDomains` get documentation notes. DEPLOYMENT.md gains "one Caddy site per product host -> Portal; DNS". PORTAL-APP.md's "never the Host header" sentence becomes "the Host header is only a lookup key against stored product hosts; URLs are built from stored values".

### Corrections (D-050, 2026-10-08)

Where this page and D-050 differ, D-050 wins. The build differs from the text above in these places:

- **Contracts copy.** The host rule copy in Contracts is `TechStrap.Contracts.Products.ProductHostRules` (`HostNameMaxLength`, `TryNormalize`), not `ProductLimits.HostNameMaxLength`.
- **Link overloads.** They take the host string: `TicketLink(string? portalHost, string token)` and `ArticleLink(string? portalHost, key, category, slug)`, not `TicketLink(Product, ...)`. The Worker's check is `PortalLinkOptions.IsArticleLink`.
- **Error target.** `product-host-invalid` carries the kebab-case target `portal-host`.
- **Host shape.** An all-digit last label is rejected, so an IP literal is never a product host. Single-label and IP hosts are never looked up or redirected (so `/p/{key}` is not 301'd on `localhost`).
- **Host map.** `ProductHostMap` is a `volatile` immutable snapshot driven by `TimeProvider` (60 s TTL, miss refresh at most once per 10 s), not an `IMemoryCache` entry, and it is stale-while-revalidate. `ProductHostContext` is scoped, stored on `HttpContext.Features` and resolved through `IHttpContextAccessor`.
- **Middleware position.** `UseProductHosts()` runs directly after `UsePortalSeo()` and before `UseTechStrapErrorPages()`, and calls `UseRouting()` itself.
- **Redirects.** Only GET and HEAD are redirected; the same-host canonical redirect is an absolute `https://{storedHost}/...`.
- **No interactive circuit.** The Portal is static SSR; there is no circuit to test on a product host.
- **Sitemap and robots.** The static `/` sitemap entry moved into the provider; `robots.txt` on a product host names the default host's sitemap (`SyntaxCircus.Blazor.Seo` builds it from `SeoOptions.BaseUrl`). (Amended 2026-10-08, D-050: see the decision log.)
- **Known limits** are listed in D-050 (unique-index race, `IsArticleLink`, no `Cache-Control` on the 301s, unbounded output-cache keys per Host value, a pre-11e caller clearing a host, Contracts binary compatibility). (Amended 2026-10-08, D-050: see the decision log.)

## Application Boundaries

Follow _template APPLICATION_ARCHITECTURE.md. This phase adds two product
handler changes (create and update gain the host rules) and no new handlers;
the Portal host logic is transport and presentation code, not a use case.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| `POST api/products` (create product) | `CreateProductRequestHandler` (existing, extended) | `IProductRepository` (existing), `Product.SetPortalHost`, `HostNameShape` | EF repository with the new `PortalHost` column and unique index | 201; invalid shape -> 400 `product-host-invalid`; duplicate host -> 409 `product-host-taken` | Extend the existing handler; no new handler |
| `PUT api/products/{id}` (update product) | `UpdateProductRequestHandler` (existing, extended) | Same as above | Same as above | 200; `product-host-invalid` -> 400; `product-host-taken` -> 409; concurrency conflict unchanged | Extend the existing handler; no new handler |
| `GET api/public/products` and `GET api/public/products/{productKey}` | Existing public product query handlers | Existing abstractions | Existing read model, now projecting `PortalHost` | 200 with `PortalHost` in the summary and the detail | Shape unchanged; one added field |
| Intake and notification links (`SubmitTicketRequestHandler`, `AddCustomerReplyRequestHandler`, `TicketNotificationPlanner`) | Unchanged handlers | `PortalLinkOptions` product-aware overloads (the planner already loads the `Product`) | None new | Email and response URLs point at the product host when set | Call-site change only |
| Portal host resolution (`ProductHostMiddleware`, `IProductHostResolver`, `ProductHostMap`) | Exempt (framework middleware; no application workflow) | `IPublicProductClient` (existing), `IMemoryCache` | `ProductHostMap` over `GET api/public/products` | Rewrite, pass-through, 301 or default-host behavior | Transport/routing concern; see Boundary Validation |
| Portal link building (`PortalLinks`) | Exempt (presentation service; no workflow) | `PortalRoutes`, `ProductHostContext`, `ProductHostMap` | None | Relative path or absolute `https://{host}/...` | Pure URL builder |
| Admin product editor | Existing product editor view model calls the existing product client | `IProductsClient` (existing, `Clients/ReferenceDataClients.cs`) | Existing typed client | Field-level shape message; 409 shown as the host-taken message | Feature-local ViewModel change |

## Razor Component Boundaries

Follow _template RAZOR_COMPONENT_ARCHITECTURE.md. No new components with logic
are added; existing Portal components switch from `PortalRoutes.*` to `Links.*`,
and the Admin product editor gains one field.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| Portal pages and shared components that build links | Existing paired `.razor`/`.razor.cs`; the `.razor.cs` injects `PortalLinks` and exposes the link values; no `PortalRoutes.` outside `PortalLinks` (architecture test) | No new ViewModels; links come from the injected service | Unchanged; host context is per request | Public DTOs (`PublicProductDto`, `PublicProductSummaryDto`) mapped in code-behind; `PortalHost` is not shown to visitors |
| `ProductPageBase` | Existing base class; resolves the product from the route key (the middleware has already rewritten the path on a product host) | No change | Unchanged | `PublicProductDto` |
| Admin product editor (`ProductEditorViewModel`, editor `.razor`/`.razor.cs`) | Paired files stay; the new "Portal host" input and its validation messages are bound through the code-behind | `ProductEditorViewModel` gains `PortalHost` (feature-local, presentation only); mapped to `CreateProductRequest`/`UpdateProductRequest` | Existing save/validation/conflict states; adds the host-taken message | `CreateProductRequest`, `UpdateProductRequest`, `ProductDto` (never the ViewModel) |
| Admin product list | Existing paired files; one added column | Existing row model gains the host | Unchanged | `ProductDto` |

## Syntax Circus Packages

Versions in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md). No new package is added or
upgraded in this phase.

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.Common` | `Result`/`Result<T>`, `ResultError` | Host rule failures return `Result` errors with the two new codes | Handler tests for `product-host-invalid` and `product-host-taken` |
| `SyntaxCircus.AspNetCore.Common` | Result-to-HTTP mapping | 400 and 409 mapping for the new codes | Api tests assert the status codes |
| `SyntaxCircus.Blazor.Seo` | Canonical host redirect, sitemap and structured data | Canonical URL, JSON-LD and sitemap move to per-host bases; legacy-host redirect unchanged | Portal tests for the canonical, JSON-LD and sitemap per host |
| `SyntaxCircus.Http.Resilience` | Typed Api client | `IPublicProductClient` feeds `ProductHostMap`; no change to the registration | Existing client tests; map refresh tests with a stub client |

## Deliverables

- [x] `Product.PortalHost`, `Product.SetPortalHost`, `HostNameShape`, `DomainLimits.HostNameMaxLength`, the EF mapping with a unique index and migration `AddProductPortalHost`. **As built (11e):** see P11e-T01.
- [x] Contracts: `PortalHost` on the five product DTOs and requests, `ProductLimits.HostNameMaxLength` with a parity test. **As built (11e):** `ProductHostRules.HostNameMaxLength`, not `ProductLimits`.
- [x] Product create/update handlers validate shape and uniqueness (`product-host-invalid`, `product-host-taken`); public summaries and details carry the host.
- [x] `PortalLinkOptions` product-aware overloads, every call site updated, Worker `EndsWith` check proven for both path shapes.
- [x] Admin product editor "Portal host" field, product list column and "View on portal" using the product host.
- [x] Portal: `ProductHostMap`, `IProductHostResolver`, `ProductHostContext`, `ProductHostMiddleware` with rewrite, pass-through and 301 rules. **As built (11e):** the map is a `TimeProvider` snapshot with stale-while-revalidate, not `IMemoryCache`.
- [x] Portal: `PortalLinks`, components migrated from `PortalRoutes.*`, the architecture test, canonical/JSON-LD/sitemap per host, `SetVaryByHost(true)`.
- [x] D-050 in [04-DECISION-LOG.md](04-DECISION-LOG.md); DEPLOYMENT.md, PORTAL-APP.md and UX-BRIEF-portal updates; roadmap and discovery index rows for 11e; any count or doc pins.

## Actionable Tasks

- [x] **P11e-T01** Domain and Infrastructure: add `Product.PortalHost`, `Product.SetPortalHost(string?)`, the `HostNameShape` rule in `src/TechStrap.Domain/Rules/`, `DomainLimits.HostNameMaxLength = 253`; map `PortalHost` in `ProductRecordConfiguration` (varchar(253), nullable, unique index) and add the migration `AddProductPortalHost` (see the `ef-migrate` skill note)
  - **Depends on:** P03, P04
  - **Validation:** Domain tests: lowercase normalization; accepts `support.dragonpoop.com` and `a-b.example.co.uk`; rejects a bare word, a scheme, a port, a path, userinfo, a leading or trailing hyphen in a label, an empty label, an underscore, a non-ASCII name and a 254-character name; `null` clears the host. Infrastructure integration test (Testcontainers): two products with the same host differing only in case fail on the unique index, two products with `null` do not; the migration applies to an empty database and to the current schema. **As built (11e):** `HostNameShape.TryNormalize(string?, out string?)` also rejects an all-digit last label (IP literals); EF column `portal_host varchar(253)`, unique index `ix_products_portal_host`, migration `20261008154111_AddProductPortalHost`; `IProductRepository.IsPortalHostTakenAsync(host, exceptProductId, ct)` was added.
- [x] **P11e-T02** Contracts and Api: add `PortalHost` to `ProductDto`, `CreateProductRequest`, `UpdateProductRequest`, `PublicProductDto` and `PublicProductSummaryDto`; add `ProductLimits.HostNameMaxLength`; extend the create and update product handlers to validate shape and uniqueness and return `product-host-invalid` or `product-host-taken` (409); project the host in the public queries
  - **Depends on:** P11e-T01
  - **Validation:** Contracts parity test: `ProductLimits.HostNameMaxLength` equals `DomainLimits.HostNameMaxLength`. Handler and Api tests: invalid shape -> 400 `product-host-invalid`; a host used by another product -> 409 `product-host-taken` on create and update; updating a product with its own host is not a conflict; an uppercase host is stored lowercase; `GET api/public/products` and `GET api/public/products/{productKey}` return `PortalHost` (null when unset); the OpenAPI contract test from PHASE-11 still passes (the intake operation is unchanged). **As built (11e):** the Contracts copy is `TechStrap.Contracts.Products.ProductHostRules` (with `HostNameMaxLength` and `TryNormalize`), not `ProductLimits`, with `ProductHostRulesParityTests`; `string? PortalHost = null` is the last parameter of each DTO; `product-host-invalid` is a 400 with the target `portal-host`, `product-host-taken` a 409 without a target; the handlers normalize, pre-check with `IsPortalHostTakenAsync`, then `SetPortalHost`. A unique-index race past the pre-check surfaces as `product-key-taken` on Create and as the raw Duplicate error on Update.
- [x] **P11e-T03** Links: add the product-aware `PortalLinkOptions.TicketLink(Product, string)` and `ArticleLink(Product, ...)` overloads; update `SubmitTicketRequestHandler` (confirmation email and `ViewUrl`), `AddCustomerReplyRequestHandler` (follow-up link) and `TicketNotificationPlanner` (article and ticket links); keep `DrainEmailOutboxHandler`'s article-path comparison working for both shapes; make the Admin "View on portal" (`PortalUrlOptions`) use the product host
  - **Depends on:** P11e-T01, P11e-T02
  - **Validation:** Unit tests: a product with a host yields `https://{host}/t/{token}` and `https://{host}/kb/{category}/{slug}`; a product without a host yields the existing `{PublicUrl}/t/{token}` and `{PublicUrl}/p/{key}/kb/{category}/{slug}`; handler tests assert the emailed URL and `ViewUrl` for both product kinds; a Worker test drains an outbox row with a product-host article URL and with a default-host article URL and matches both; an Admin test checks the "View on portal" URL for both kinds. **As built (11e):** the overloads are `TicketLink(string? portalHost, string token)` and `ArticleLink(string? portalHost, key, category, slug)`, not `TicketLink(Product, ...)`; plus `ArticlePathOnHost`, `ProductHostBase` and `IsArticleLink(url, key, category, slug)`, which the Worker's drain uses for both shapes. `AddCustomerReplyRequestHandler` now injects `IProductRepository` and falls back to the default host when the product is missing; `PortalUrlOptions.ArticleUrl(portalHost, key, category, slug)` serves the Admin (only `KbArticleEditorPage` "View on portal" consumes it). `IsArticleLink` does not check that the host belongs to the product.
- [x] **P11e-T04** Admin: add the optional "Portal host" field to `ProductEditorViewModel` and the editor, with shape validation (same rule text as the Domain) and the host-taken message from the 409; show the host in the product list
  - **Depends on:** P11e-T02
  - **Validation:** Admin unit tests: valid host maps into the create and update requests; an invalid host blocks the save with a field message; a `product-host-taken` result shows the conflict message on the field; clearing the field sends `null`; the list shows the host (or an empty cell). bUnit or render test for the editor field. **As built (11e):** `ApiFields.PortalHost = "portal-host"`; the 409 `product-host-taken` is shown as a field error ("Another product already uses this hostname."); the list column is "Portal host"; the editor input has no maxlength attribute (the shared field fragment has none).
- [x] **P11e-T05** Portal host resolution: add `ProductHostMap` (60 s TTL, refresh on a miss at most once per 10 s), `IProductHostResolver`, `ProductHostContext`, and `Hosting/ProductHostMiddleware` registered before `UseRouting` with the rewrite, pass-through and 301 rules
  - **Depends on:** P11e-T02
  - **Validation:** Portal tests with a stub `IPublicProductClient`: on a product host `/`, `/contact`, `/kb/a/b`, `/lost-link` and `/suggest` resolve to the product's pages; `/t/{token}`, `/sitemap.xml`, `/robots.txt`, `/health` and static files pass through; `/p/{sameKey}/contact` -> 301 `/contact`; `/p/{otherKey}/x` -> 301 to the other product's canonical absolute URL (its host, or the default host with `/p/{otherKey}/x`); on the default host `/p/{key}` for a product with a host -> 301 `https://{host}/`. Map tests: a second unknown-host request inside 10 s causes no second Api call; the map refreshes after 60 s. Host tests: the Blazor hub (`/_blazor`) still connects on a rewritten path (interactive page renders and responds on a product host). **As built (11e):** `ProductHostMap` is a `volatile` immutable snapshot driven by `TimeProvider`, not an `IMemoryCache` entry, with stale-while-revalidate (an expired snapshot is served while at most one background reload runs; only a cold start awaits); `ProductHostContext` lives on `HttpContext.Features` and is resolved through `IHttpContextAccessor`; `UseProductHosts()` runs after `UsePortalSeo()` and before `UseTechStrapErrorPages()` and calls `UseRouting()`; single-label and IP hosts are never product hosts; redirects are GET/HEAD only and absolute `https://{storedHost}/...`; the Portal is static SSR, so there is no interactive circuit (`POST /_blazor/negotiate` is 405 on every host) and the hub check reduced to `blazor.web.js` answering 200 on a product host plus negotiate parity.
- [x] **P11e-T06** Portal links, SEO and cache: add `PortalLinks` on `PortalRoutes`, the host context and the map; move components from `PortalRoutes.*` to `Links.*`; add the architecture test; route canonical URL, JSON-LD and the sitemap through `PortalLinks.Absolute`; per-host sitemap content; `PortalOutputCache` `SetVaryByHost(true)`
  - **Depends on:** P11e-T05
  - **Validation:** `PortalLinks` unit tests for the three outcomes (clean relative path, absolute product host URL, `/p/{key}/...`). Architecture test: no `PortalRoutes.` in `.razor` or `.razor.cs` outside `PortalLinks`; `RouteLiteralTests` still passes. Page tests on a product host: canonical and JSON-LD use `https://{host}/...` with clean paths; `/sitemap.xml` on the product host lists only that product; the default host's sitemap lists only products without a host. Output cache test: the same path on two hosts yields two cache entries (the former "never vary by host" test is flipped). Security tests: a request with an unknown or hostile `Host` renders the default host's page, and every generated URL, redirect and canonical uses the configured public URL; follow-up redirects use the last token segment only. **As built (11e):** `PortalLinks` also has `ToFragment` (skip link, error-summary field links, ticket reply jump link) and `TicketAttachment`; the architecture rule is `PortalRules` + `PortalLinkRuleTests` (no `PortalRoutes.<builder>(` in `.razor`, `.razor.cs` or `Seo/*` except `Routing/PortalLinks.cs` and `Routing/PageLinks.cs`); canonical and JSON-LD go through `PortalLinks.Absolute` with no package change; the static `/` sitemap entry moved into the provider; `PortalSitemapCache` is keyed by `Context.Host ?? "default"`; `SetVaryByHost(true)` replaced the "never vary by host" test with "two hosts, two entries". `robots.txt` on a product host still names the default host's sitemap (package limit).
- [x] **P11e-T07** Docs and close-out: record D-050 (amends D-002) in [04-DECISION-LOG.md](04-DECISION-LOG.md); update DEPLOYMENT.md ("one Caddy site per product host -> Portal; DNS", `ALLOWEDHOSTS`, HSTS `includeSubDomains`), PORTAL-APP.md (the Host-header sentence) and [UX-BRIEF-portal.md](UX-BRIEF-portal.md); add the 11e row to [99-IMPLEMENTATION-ROADMAP.md](99-IMPLEMENTATION-ROADMAP.md) and [00-DISCOVERY-INDEX.md](00-DISCOVERY-INDEX.md); update [05-SCHEMA.md](05-SCHEMA.md) for the new column; refresh doc pins and counts
  - **Depends on:** P11e-T01 to P11e-T06
  - **Validation:** `dotnet build` and `dotnet test` green; doc pin tests (if any) pass; D-050 has Related artifacts and links to D-002; the grep for "never the Host header" finds the new wording only; a manual check against the local compose stack with a hosts-file entry shows a product reachable at its host with clean paths and the default host redirecting to it. **As built (11e):** D-050 recorded with the as-built corrections; docs and pins updated; the manual compose check with a Host header passed (recorded in the plan's As built section).

## Success Criteria

- [x] A product with `PortalHost` set is reachable at `https://{host}/` with clean paths (`/`, `/contact`, `/kb/...`, `/t/{token}`); its emails and Admin "View on portal" link there; the default host answers `/p/{key}/...` for that product with a 301 to the clean URL. **As built (11e):** verified by unit and host tests; the compose check exercised the same-host 301.
- [x] A product without a host is unchanged: served on the default host under `/p/{key}`, and the default product at `/`.
- [x] An unknown or hostile `Host` header behaves as the default host and leaks nothing: no Host-derived text in any URL, header, body, log line or redirect target.
- [x] Setting an invalid or duplicate host in the Admin fails with `product-host-invalid` or `product-host-taken` and a clear field message; hosts are stored lowercase and are unique case-insensitively.
- [x] Canonical URL, JSON-LD and sitemap are correct per host; the default host's sitemap lists only products without a host; the output cache varies by host.
- [x] The Worker's email drain matches article links of both shapes.
- [x] The Blazor hub connects on a product host; no new setting is required. **As built (11e):** the Portal is static SSR with no hub or circuit; `blazor.web.js` is served on a product host and `POST /_blazor/negotiate` is 405 on every host. No new setting.
- [x] `dotnet build`, `dotnet test` green; D-050 recorded. **As built (11e):** D-050 is in the decision log; the manual compose check with a Host header passed.

## Boundary Validation

- [x] Application use-case entry points delegate to the named handlers listed above (the product create and update endpoints keep their existing handlers; no new endpoints).
- [x] Framework-owned operational or static exemptions execute no application workflow (`ProductHostMiddleware` only rewrites or redirects paths; it does not call a handler or write data).
- [x] Handler constructor dependencies contain only approved abstractions (the extended product handlers use the existing repository and the Domain rule; the Portal middleware depends on `IProductHostResolver` and `IPublicProductClient`, not on infrastructure types).
- [x] Persistence and integration entities do not cross infrastructure boundaries (`PortalHost` leaves the Api only as a DTO field; the Portal sees `PublicProductDto` and `PublicProductSummaryDto`).
- [x] Cancellation reaches asynchronous handler dependencies (map refresh and handler uniqueness checks take the request's `CancellationToken`).
- [x] Expected outcomes and transport mapping have focused tests (`product-host-invalid` -> 400, `product-host-taken` -> 409, middleware 301 and rewrite rules).
- [x] Infrastructure implementations have integration coverage where applicable (unique index on `PortalHost` and the migration, Testcontainers; Portal host tests through the test server).
- [x] Inline Razor components contain only simple parameters and, at most, one trivial synchronous `EventCallback`-forwarding callback (no inline logic is added).
- [x] Every component beyond the inline ceiling uses paired `.razor` and `.razor.cs` files, with all C# in code-behind (link values come from `PortalLinks` in code-behind; the editor field is bound in code-behind).
- [x] Each Razor ViewModel is feature-local and presentation-only (`ProductEditorViewModel` gains `PortalHost` and is mapped to the request DTOs; no Api ViewModel is exposed).
- [x] A factory or presentation service is used only for non-trivial mapping, asynchronous assembly, or multiple dependencies (`PortalLinks` combines `PortalRoutes`, the host context and the host map, so it earns a service).
- [x] API request and response contracts use DTO names and contracts, never Razor ViewModels.
- [x] Repeated or business-meaningful literals are named constants at the right scope (`HostNameMaxLength` in Domain with a Contracts copy and parity test; the pass-through path prefixes live in one place in the middleware; the 60 s and 10 s intervals are named constants; route literals stay in `PortalRoutes`).
- [x] Duplicated-looking logic across flows was evaluated for genuine divergence (host shape validation exists in Domain and, for instant feedback, in the Admin editor; the Domain rule is authoritative and the Admin message text is checked against it; ticket and article link building stays in one place, `PortalLinkOptions`, called by the intake handlers, the planner and the Worker).
- [x] Security: the raw Host header never reaches a URL, log line or redirect target; only a matched stored `PortalHost` or the configured public URL does (tests pin an unknown and a hostile Host).

## Risks and Open Questions

- [x] **Caddy and DNS per host is operator work.** Each product host needs a DNS record and a Caddy site proxying to the Portal; it belongs to the PHASE-12 UAT configuration, documented in DEPLOYMENT.md here.
- [x] **HSTS `includeSubDomains` interplay.** A product host under the same registrable domain as another site inherits HSTS from a parent that sets `includeSubDomains`. Documented in DEPLOYMENT.md; confirm with the owner for each real host.
- [x] **Output-cache key growth per host.** Varying by host multiplies entries by the number of hosts. Product hosts are few and the unknown-host case renders the default host's pages, but unknown hosts still add cache keys per distinct `Host` value; confirm the cache has a size bound, or key unknown hosts to one variant. **As built (11e):** not bounded; deferred to PHASE-12 hardening (D-050 known limit).
- [x] **Api load from unknown hosts.** Every unknown host triggers a `GET api/public/products` refresh; bounded to one per 10 s by the refresh rule, and tested.
- [x] **Blazor base URI on a rewritten path.** The circuit URL and the displayed URL can differ after a rewrite. The rewrite must happen before `UseRouting` and the Blazor hub must still connect; the host tests (P11e-T05) verify an interactive page on a product host, including `NavigationManager` navigation to a clean link. **As built (11e):** the Portal is static SSR with no interactive page and no circuit, so there is no hub to connect; the host tests cover the rewrite and the clean links.
- [x] **Sitemap discoverability.** A product with a host is no longer in the default host's sitemap (intended); its own host serves its sitemap and `robots.txt`. **As built (11e):** its own host serves its sitemap, but `robots.txt` on a product host names the default host's sitemap (D-050 known limit). (Amended 2026-10-08, D-050: see the decision log.)
- [x] **D-002 cost accepted.** D-002 warned that per-product domains multiply the TLS and DNS burden; D-050 accepts that cost for products that ask for a host.
- [x] **Host changes break old links.** Changing or clearing a product's `PortalHost` leaves emailed links to the old host dead until DNS is removed; the old host then falls to default-host behavior. Operators should keep the old host's Caddy site until old links age out (**Assumption**: no redirect table is kept). **As built (11e):** keeping the old host's Caddy site preserves `/t/` links only; old help-center links on it answer 404 (D-050).
- [x] **Local development.** Host-based behavior is tested with the test server and a hosts-file entry; `http://localhost:8082` stays the default host.

## Handoff

Before [PHASE-12](PHASE-12-release-hardening.md) starts: a product with a
`PortalHost` is served at its own host with clean paths, emails and Admin links
point there, and the default host redirects to it; D-050 is recorded with D-002
amended; DEPLOYMENT.md describes the per-host Caddy site and DNS. PHASE-12's UAT
configuration adds the real product hosts, and its security review covers the
Host-header handling (lookup key only, no Host-derived URLs) and the HSTS
`includeSubDomains` setting.
