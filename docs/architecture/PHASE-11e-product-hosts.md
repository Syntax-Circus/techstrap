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
- **Host shape.** A new `HostNameShape` rule in `src/TechStrap.Domain/Rules/` validates: lowercase after normalisation, RFC 1123 labels (letters, digits, hyphens), dots between labels, no leading or trailing hyphen in a label, no scheme, port, path or userinfo, at most `DomainLimits.HostNameMaxLength` (253) characters, and at least one dot (a bare word is rejected). `Product.SetPortalHost(string?)` normalises to lowercase, validates through the rule and stores the value. Domain owns the limit; Contracts carries a copy (`ProductLimits.HostNameMaxLength`) with a parity test, as for the other limits.
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
| Portal host resolution (`ProductHostMiddleware`, `IProductHostResolver`, `ProductHostMap`) | Exempt (framework middleware; no application workflow) | `IPublicProductClient` (existing), `IMemoryCache` | `ProductHostMap` over `GET api/public/products` | Rewrite, pass-through, 301 or default-host behaviour | Transport/routing concern; see Boundary Validation |
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

- [ ] `Product.PortalHost`, `Product.SetPortalHost`, `HostNameShape`, `DomainLimits.HostNameMaxLength`, the EF mapping with a unique index and migration `AddProductPortalHost`.
- [ ] Contracts: `PortalHost` on the five product DTOs and requests, `ProductLimits.HostNameMaxLength` with a parity test.
- [ ] Product create/update handlers validate shape and uniqueness (`product-host-invalid`, `product-host-taken`); public summaries and details carry the host.
- [ ] `PortalLinkOptions` product-aware overloads, every call site updated, Worker `EndsWith` check proven for both path shapes.
- [ ] Admin product editor "Portal host" field, product list column and "View on portal" using the product host.
- [ ] Portal: `ProductHostMap`, `IProductHostResolver`, `ProductHostContext`, `ProductHostMiddleware` with rewrite, pass-through and 301 rules.
- [ ] Portal: `PortalLinks`, components migrated from `PortalRoutes.*`, the architecture test, canonical/JSON-LD/sitemap per host, `SetVaryByHost(true)`.
- [ ] D-050 in [04-DECISION-LOG.md](04-DECISION-LOG.md); DEPLOYMENT.md, PORTAL-APP.md and UX-BRIEF-portal updates; roadmap and discovery index rows for 11e; any count or doc pins.

## Actionable Tasks

- [ ] **P11e-T01** Domain and Infrastructure: add `Product.PortalHost`, `Product.SetPortalHost(string?)`, the `HostNameShape` rule in `src/TechStrap.Domain/Rules/`, `DomainLimits.HostNameMaxLength = 253`; map `PortalHost` in `ProductRecordConfiguration` (varchar(253), nullable, unique index) and add the migration `AddProductPortalHost` (see the `ef-migrate` skill note)
  - **Depends on:** P03, P04
  - **Validation:** Domain tests: lowercase normalisation; accepts `support.dragonpoop.com` and `a-b.example.co.uk`; rejects a bare word, a scheme, a port, a path, userinfo, a leading or trailing hyphen in a label, an empty label, an underscore, a non-ASCII name and a 254-character name; `null` clears the host. Infrastructure integration test (Testcontainers): two products with the same host differing only in case fail on the unique index, two products with `null` do not; the migration applies to an empty database and to the current schema.
- [ ] **P11e-T02** Contracts and Api: add `PortalHost` to `ProductDto`, `CreateProductRequest`, `UpdateProductRequest`, `PublicProductDto` and `PublicProductSummaryDto`; add `ProductLimits.HostNameMaxLength`; extend the create and update product handlers to validate shape and uniqueness and return `product-host-invalid` or `product-host-taken` (409); project the host in the public queries
  - **Depends on:** P11e-T01
  - **Validation:** Contracts parity test: `ProductLimits.HostNameMaxLength` equals `DomainLimits.HostNameMaxLength`. Handler and Api tests: invalid shape -> 400 `product-host-invalid`; a host used by another product -> 409 `product-host-taken` on create and update; updating a product with its own host is not a conflict; an uppercase host is stored lowercase; `GET api/public/products` and `GET api/public/products/{productKey}` return `PortalHost` (null when unset); the OpenAPI contract test from PHASE-11 still passes (the intake operation is unchanged).
- [ ] **P11e-T03** Links: add the product-aware `PortalLinkOptions.TicketLink(Product, string)` and `ArticleLink(Product, ...)` overloads; update `SubmitTicketRequestHandler` (confirmation email and `ViewUrl`), `AddCustomerReplyRequestHandler` (follow-up link) and `TicketNotificationPlanner` (article and ticket links); keep `DrainEmailOutboxHandler`'s article-path comparison working for both shapes; make the Admin "View on portal" (`PortalUrlOptions`) use the product host
  - **Depends on:** P11e-T01, P11e-T02
  - **Validation:** Unit tests: a product with a host yields `https://{host}/t/{token}` and `https://{host}/kb/{category}/{slug}`; a product without a host yields the existing `{PublicUrl}/t/{token}` and `{PublicUrl}/p/{key}/kb/{category}/{slug}`; handler tests assert the emailed URL and `ViewUrl` for both product kinds; a Worker test drains an outbox row with a product-host article URL and with a default-host article URL and matches both; an Admin test checks the "View on portal" URL for both kinds.
- [ ] **P11e-T04** Admin: add the optional "Portal host" field to `ProductEditorViewModel` and the editor, with shape validation (same rule text as the Domain) and the host-taken message from the 409; show the host in the product list
  - **Depends on:** P11e-T02
  - **Validation:** Admin unit tests: valid host maps into the create and update requests; an invalid host blocks the save with a field message; a `product-host-taken` result shows the conflict message on the field; clearing the field sends `null`; the list shows the host (or an empty cell). bUnit or render test for the editor field.
- [ ] **P11e-T05** Portal host resolution: add `ProductHostMap` (60 s TTL, refresh on a miss at most once per 10 s), `IProductHostResolver`, `ProductHostContext`, and `Hosting/ProductHostMiddleware` registered before `UseRouting` with the rewrite, pass-through and 301 rules
  - **Depends on:** P11e-T02
  - **Validation:** Portal tests with a stub `IPublicProductClient`: on a product host `/`, `/contact`, `/kb/a/b`, `/lost-link` and `/suggest` resolve to the product's pages; `/t/{token}`, `/sitemap.xml`, `/robots.txt`, `/health` and static files pass through; `/p/{sameKey}/contact` -> 301 `/contact`; `/p/{otherKey}/x` -> 301 to the other product's canonical absolute URL (its host, or the default host with `/p/{otherKey}/x`); on the default host `/p/{key}` for a product with a host -> 301 `https://{host}/`. Map tests: a second unknown-host request inside 10 s causes no second Api call; the map refreshes after 60 s. Host tests: the Blazor hub (`/_blazor`) still connects on a rewritten path (interactive page renders and responds on a product host).
- [ ] **P11e-T06** Portal links, SEO and cache: add `PortalLinks` on `PortalRoutes`, the host context and the map; move components from `PortalRoutes.*` to `Links.*`; add the architecture test; route canonical URL, JSON-LD and the sitemap through `PortalLinks.Absolute`; per-host sitemap content; `PortalOutputCache` `SetVaryByHost(true)`
  - **Depends on:** P11e-T05
  - **Validation:** `PortalLinks` unit tests for the three outcomes (clean relative path, absolute product host URL, `/p/{key}/...`). Architecture test: no `PortalRoutes.` in `.razor` or `.razor.cs` outside `PortalLinks`; `RouteLiteralTests` still passes. Page tests on a product host: canonical and JSON-LD use `https://{host}/...` with clean paths; `/sitemap.xml` on the product host lists only that product; the default host's sitemap lists only products without a host. Output cache test: the same path on two hosts yields two cache entries (the former "never vary by host" test is flipped). Security tests: a request with an unknown or hostile `Host` renders the default host's page, and every generated URL, redirect and canonical uses the configured public URL; follow-up redirects use the last token segment only.
- [ ] **P11e-T07** Docs and close-out: record D-050 (amends D-002) in [04-DECISION-LOG.md](04-DECISION-LOG.md); update DEPLOYMENT.md ("one Caddy site per product host -> Portal; DNS", `ALLOWEDHOSTS`, HSTS `includeSubDomains`), PORTAL-APP.md (the Host-header sentence) and [UX-BRIEF-portal.md](UX-BRIEF-portal.md); add the 11e row to [99-IMPLEMENTATION-ROADMAP.md](99-IMPLEMENTATION-ROADMAP.md) and [00-DISCOVERY-INDEX.md](00-DISCOVERY-INDEX.md); update [05-SCHEMA.md](05-SCHEMA.md) for the new column; refresh doc pins and counts
  - **Depends on:** P11e-T01 to P11e-T06
  - **Validation:** `dotnet build` and `dotnet test` green; doc pin tests (if any) pass; D-050 has Related artifacts and links to D-002; the grep for "never the Host header" finds the new wording only; a manual check against the local compose stack with a hosts-file entry shows a product reachable at its host with clean paths and the default host redirecting to it.

## Success Criteria

- [ ] A product with `PortalHost` set is reachable at `https://{host}/` with clean paths (`/`, `/contact`, `/kb/...`, `/t/{token}`); its emails and Admin "View on portal" link there; the default host answers `/p/{key}/...` for that product with a 301 to the clean URL.
- [ ] A product without a host is unchanged: served on the default host under `/p/{key}`, and the default product at `/`.
- [ ] An unknown or hostile `Host` header behaves as the default host and leaks nothing: no Host-derived text in any URL, header, body, log line or redirect target.
- [ ] Setting an invalid or duplicate host in the Admin fails with `product-host-invalid` or `product-host-taken` and a clear field message; hosts are stored lowercase and are unique case-insensitively.
- [ ] Canonical URL, JSON-LD and sitemap are correct per host; the default host's sitemap lists only products without a host; the output cache varies by host.
- [ ] The Worker's email drain matches article links of both shapes.
- [ ] The Blazor hub connects on a product host; no new setting is required.
- [ ] `dotnet build`, `dotnet test` green; D-050 recorded.

## Boundary Validation

- [ ] Application use-case entry points delegate to the named handlers listed above (the product create and update endpoints keep their existing handlers; no new endpoints).
- [ ] Framework-owned operational or static exemptions execute no application workflow (`ProductHostMiddleware` only rewrites or redirects paths; it does not call a handler or write data).
- [ ] Handler constructor dependencies contain only approved abstractions (the extended product handlers use the existing repository and the Domain rule; the Portal middleware depends on `IProductHostResolver` and `IPublicProductClient`, not on infrastructure types).
- [ ] Persistence and integration entities do not cross infrastructure boundaries (`PortalHost` leaves the Api only as a DTO field; the Portal sees `PublicProductDto` and `PublicProductSummaryDto`).
- [ ] Cancellation reaches asynchronous handler dependencies (map refresh and handler uniqueness checks take the request's `CancellationToken`).
- [ ] Expected outcomes and transport mapping have focused tests (`product-host-invalid` -> 400, `product-host-taken` -> 409, middleware 301 and rewrite rules).
- [ ] Infrastructure implementations have integration coverage where applicable (unique index on `PortalHost` and the migration, Testcontainers; Portal host tests through the test server).
- [ ] Inline Razor components contain only simple parameters and, at most, one trivial synchronous `EventCallback`-forwarding callback (no inline logic is added).
- [ ] Every component beyond the inline ceiling uses paired `.razor` and `.razor.cs` files, with all C# in code-behind (link values come from `PortalLinks` in code-behind; the editor field is bound in code-behind).
- [ ] Each Razor ViewModel is feature-local and presentation-only (`ProductEditorViewModel` gains `PortalHost` and is mapped to the request DTOs; no Api ViewModel is exposed).
- [ ] A factory or presentation service is used only for non-trivial mapping, asynchronous assembly, or multiple dependencies (`PortalLinks` combines `PortalRoutes`, the host context and the host map, so it earns a service).
- [ ] API request and response contracts use DTO names and contracts, never Razor ViewModels.
- [ ] Repeated or business-meaningful literals are named constants at the right scope (`HostNameMaxLength` in Domain with a Contracts copy and parity test; the pass-through path prefixes live in one place in the middleware; the 60 s and 10 s intervals are named constants; route literals stay in `PortalRoutes`).
- [ ] Duplicated-looking logic across flows was evaluated for genuine divergence (host shape validation exists in Domain and, for instant feedback, in the Admin editor; the Domain rule is authoritative and the Admin message text is checked against it; ticket and article link building stays in one place, `PortalLinkOptions`, called by the intake handlers, the planner and the Worker).
- [ ] Security: the raw Host header never reaches a URL, log line or redirect target; only a matched stored `PortalHost` or the configured public URL does (tests pin an unknown and a hostile Host).

## Risks and Open Questions

- [ ] **Caddy and DNS per host is operator work.** Each product host needs a DNS record and a Caddy site proxying to the Portal; it belongs to the PHASE-12 UAT configuration, documented in DEPLOYMENT.md here.
- [ ] **HSTS `includeSubDomains` interplay.** A product host under the same registrable domain as another site inherits HSTS from a parent that sets `includeSubDomains`. Documented in DEPLOYMENT.md; confirm with the owner for each real host.
- [ ] **Output-cache key growth per host.** Varying by host multiplies entries by the number of hosts. Product hosts are few and the unknown-host case renders the default host's pages, but unknown hosts still add cache keys per distinct `Host` value; confirm the cache has a size bound, or key unknown hosts to one variant.
- [ ] **Api load from unknown hosts.** Every unknown host triggers a `GET api/public/products` refresh; bounded to one per 10 s by the refresh rule, and tested.
- [ ] **Blazor base URI on a rewritten path.** The circuit URL and the displayed URL can differ after a rewrite. The rewrite must happen before `UseRouting` and the Blazor hub must still connect; the host tests (P11e-T05) verify an interactive page on a product host, including `NavigationManager` navigation to a clean link.
- [ ] **Sitemap discoverability.** A product with a host is no longer in the default host's sitemap (intended); its own host serves its sitemap and `robots.txt`.
- [ ] **D-002 cost accepted.** D-002 warned that per-product domains multiply the TLS and DNS burden; D-050 accepts that cost for products that ask for a host.
- [ ] **Host changes break old links.** Changing or clearing a product's `PortalHost` leaves emailed links to the old host dead until DNS is removed; the old host then falls to default-host behaviour. Operators should keep the old host's Caddy site until old links age out (**Assumption**: no redirect table is kept).
- [ ] **Local development.** Host-based behaviour is tested with the test server and a hosts-file entry; `http://localhost:8082` stays the default host.

## Handoff

Before [PHASE-12](PHASE-12-release-hardening.md) starts: a product with a
`PortalHost` is served at its own host with clean paths, emails and Admin links
point there, and the default host redirects to it; D-050 is recorded with D-002
amended; DEPLOYMENT.md describes the per-host Caddy site and DNS. PHASE-12's UAT
configuration adds the real product hosts, and its security review covers the
Host-header handling (lookup key only, no Host-derived URLs) and the HSTS
`includeSubDomains` setting.
