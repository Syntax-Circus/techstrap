# PHASE-09: Public Portal

## Objective

Deliver `TechStrap.Portal`, the public Blazor SSR site served on one domain at
`/p/{key}`: per-product themed pages, the contact form with knowledge-base
deflection, the customer ticket view/reply/lost-link flow (`/t/{token}`),
knowledge-base browse/search/article pages, and SEO (meta tags, JSON-LD,
sitemap, robots) through `SyntaxCircus.Blazor.Seo`.

## Dependencies

- **Depends on:** [PHASE-02](PHASE-02-brand-and-ux.md) (BRAND.md, `UX-BRIEF-portal.md`, SCSS tokens), [PHASE-06](PHASE-06-ticket-operations.md) (customer handlers, attachment access; transitively [PHASE-05](PHASE-05-intake-email-worker.md) intake and branding endpoint), [PHASE-08](PHASE-08-knowledge-base.md) (public KB handlers).
- **Unblocks:** [PHASE-12](PHASE-12-release-hardening.md). (Independent of 07/10/11.)
- **External prerequisites:** Public base URL and reverse proxy in front of the portal; API reachable from the portal on the pinned compose subnet; seed products with branding and published KB articles; SMTP not required (emails are sent by the worker).

## Architecture Decisions

- **No new server entry points; the portal never touches the database or the Application layer.** It references `TechStrap.Contracts` only and calls the API through typed clients built on `SyntaxCircus.Http.Resilience`. The portal calls the API **anonymously** (public endpoints); customer calls carry the access token in the `X-Ticket-Token` header per the P06 contract (header-name constant from Contracts; never logged, never in API paths or query strings).
- **Rendering model:** static SSR with enhanced forms (`[SupplyParameterFromForm]`, antiforgery). Everything works without a circuit. The single exception is the deflection list on the contact form: an **InteractiveServer island** (`KbDeflectionSuggestions`) debounces subject input and calls the public search; if the circuit is unavailable the form still submits normally (**Assumption**; alternative is progressive fetch to a portal-hosted endpoint, rejected because it adds an entry point).
- **Routes (fixed in `02-ARCHITECTURE.md` section 8.2):** `/p/{key}` (product home + KB search box), `/p/{key}/contact`, `/p/{key}/contact/received` (confirmation: ticket number only, carried by a short-lived reference, never the access token), `/p/{key}/lost-link`, `/p/{key}/kb`, `/p/{key}/kb/{category}`, `/p/{key}/kb/{category}/{articleSlug}`, `/p/{key}/kb/search?q=` (the category slug `search` is reserved), `/t/{token}`. `/` shows a minimal "choose a product" or redirects to a configured default product (**Assumption**; `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` optional). The portal base URL is `TECHSTRAP_PORTAL_PUBLIC_URL`.
- **Per-product theming:** branding (name, logo URL, accent colour) comes from `GetPublicProductRequestHandler` and is applied as CSS custom properties on the page wrapper (`style="--ts-accent:…;--ts-on-accent:…;--ts-accent-ink:…"`), not as injected `<style>` blocks, so CSP stays strict. The accent value is re-validated against the `#RRGGBB` constant before use; the on-accent and accent-ink colours are derived by a feature-local `BrandingThemeFactory` (canonical names: `--ts-accent`, `--ts-on-accent`, `--ts-accent-ink`; the unprefixed BRAND.md names get the `--ts-` prefix) (non-trivial mapping). Logo `<img>` URLs must be https (or relative) else omitted.
- **Unknown/inactive product key** -> the same NotFound page as an unknown route (no enumeration).
- **Token page security (`/t/{token}`):** `Referrer-Policy: no-referrer`, `Cache-Control: no-store`, `X-Robots-Tag: noindex`, `<meta name="robots" content="noindex">`, excluded in `robots.txt` and the sitemap. Invalid, expired or revoked tokens return the **uniform 404** page. Only public messages are shown; internal notes never reach the portal.
- **Closed tickets:** read-only banner; the reply form submits normally and `AddCustomerReplyRequestHandler` creates a follow-up ticket; the page then shows the follow-up link/number (response contract per P06).
- **Lost link:** form always shows the same confirmation regardless of match; the response is never inspected for existence.
- **Abuse controls at the edge:** honeypot field (hidden, `autocomplete=off`, `tabindex=-1`), antiforgery, request body size limit equal to the API limit, client-side file hints only (server enforces size/type allowlist). **Rate limits are enforced by the API**, which must see the real client IP: the portal forwards the original client IP (trusted `X-Forwarded-For` from the reverse proxy) to the API through `.AddForwardedClientIp()`, and the API trusts forwarded headers only from the pinned compose subnet (D-019; `CLIENT_IP_RATE_LIMITING.md`, P01/P05 config).
- **Attachments for customers:** `/t/{token}/attachments/{id}` is a portal-hosted pass-through adapter that forwards to `GET /api/customer/attachments/{id}` (`GetCustomerAttachmentRequestHandler`, D-038) with `X-Ticket-Token` forwarded server-side and streams the response (no business logic), with `Content-Disposition: attachment` and `nosniff`. Exempt per D-017; same pattern as the admin.
- **Rendered HTML:** the KB article body arrives sanitized from the API (`PublishedKbArticleDto.Html`) and is rendered through `MarkupString` at exactly one site (`KbArticleBody`); message bodies likewise at `CustomerMessageBody`. Portal does not re-sanitize (single source of truth), but an architecture/lint test restricts `MarkupString` to those two components.
- **SEO** via `SyntaxCircus.Blazor.Seo`: `AddSyntaxCircusSeo`, `UseCanonicalHost`, `MapRobotsTxt`, `MapSitemap` with entries provided by an `ISitemapEntryProvider` implementation that calls `GetSitemapEntriesRequestHandler` through the API (cached 15 minutes, **Assumption**). `SeoHead` per page; JSON-LD `BreadcrumbListSchema` on KB pages and an article schema (custom POCO) on article pages. Contact/ticket pages are `noindex`.
- **Caching:** KB list/article/category pages use ASP.NET output caching with short TTL and vary by route (**Assumption**: 60 s); never cache `/t/*` or form pages.
- **Accessibility/UX** follow `UX-BRIEF-portal.md` (mobile-first, no JS required, visible focus, error summaries, labels, 4.5:1 contrast with the computed `--ts-on-accent` and `--ts-accent-ink`).
- **Tests:** bUnit for component logic, `WebApplicationFactory<Portal>` with a fake API handler for page-level SSR output (token headers, noindex, canonical, sitemap). Playwright e2e optional (**Assumption**, P09-T20).

### Corrections (D-045, 2026-10-05)

Where this page and D-045 differ, D-045 wins.
- **Delivery.** Three pull requests: 09a (the foundation: T01 to T05, T17, T19, T22), 09b (the customer flows: T06 to T11, T18, T21, T23) and 09c (the knowledge base, SEO and polish: T12 to T16). T20 is deferred.
- **References.** The Portal references `TechStrap.Contracts` and `TechStrap.Hosting` (the shared host wiring, D-042), not Contracts only.
- **KB suggestions.** A vanilla-JS custom element `<ts-kb-suggestions>` and a Portal-hosted `GET /p/{key}/kb/suggest` adapter replace the `KbDeflectionSuggestions` InteractiveServer island. No page has a circuit.
- **API additions.** 09c adds a paged list of a category's articles and `GET api/public/products`; 09b adds `ProductKey` to `CustomerTicketDto`. `/` redirects to `TECHSTRAP_PORTAL_DEFAULT_PRODUCT`, or shows a neutral page.
- **Blazor.Seo.** The real names are `AddSyntaxCircusSeo`, `UseSyntaxCircusSeo`, `MapSeoRobotsTxt(extraDirectives)` and `MapSeoSitemap(staticEntries, provider)`; `UseCanonicalHost`, `MapRobotsTxt`, `MapSitemap` and `ISitemapEntryProvider` do not exist. `Seo:BaseUrl` is derived from `TECHSTRAP_PORTAL_PUBLIC_URL`.
- **Theming.** `BrandingThemeFactory` is replaced by a thin `ProductThemeViewModel` over `AccentScope` and the DTO's derived colours; the logo address is re-checked.
- **Clients.** A hand-written `ApiConnection` and `ProblemMapping` (reads retried, writes never), not `ApiClientBase`; each client arrives with its page.
- **Lost link.** The Portal's responses are byte-identical whatever the address; the timing assertion in P09-T10 is dropped (D-038 accepts the residual difference).
- **Received page.** The ticket number travels in a data-protection-protected `?ref=` value that expires after 10 minutes.
- **Headers.** Per-path header rules in `UseTechStrapWebHost` give `/t/*` its `no-referrer`, `no-store` and `noindex`, and give only `/t/{token}/attachments/{id}` the sandbox CSP.

## Application Boundaries

Follow _template APPLICATION_ARCHITECTURE.md. This phase adds **no new server
entry points and no new handlers**. Razor pages are presentation endpoints that
call application-facing typed clients; the table lists every API entry point
consumed and the portal-hosted framework/adapter endpoints.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| Product theme/branding for every `/p/{key}/…` page | `GetPublicProductRequestHandler` (P05) via `GET /api/public/products/{key}` | Portal `IPublicProductClient` | `PublicProductClient : ApiClientBase` | `PublicProductDto`; 404 -> NotFound page | Consumed; no new server entry point |
| Contact form submit (`POST /api/public/products/{key}/tickets`, multipart) | `SubmitTicketRequestHandler` (P05) | `IPublicTicketClient` | `PublicTicketClient` | 201 `SubmitTicketResponse` -> redirect to `/p/{key}/contact/received`; 400 -> field errors; 429 -> "try later" message; 413/415 -> attachment error | Consumed; no new server entry point |
| Customer ticket view (`/t/{token}`) | `GetCustomerTicketRequestHandler` (P06) | `ICustomerTicketClient` | `CustomerTicketClient` | `CustomerTicketDto`; uniform 404 -> NotFound page | Consumed; no new server entry point |
| Customer reply (incl. Closed -> follow-up ticket) | `AddCustomerReplyRequestHandler` (P06) | `ICustomerTicketClient` | `CustomerTicketClient` | 201 `CustomerReplyResponse` with optional follow-up ticket link; 400/404/429 mapping | Consumed; no new server entry point |
| Lost-link request | `RequestNewAccessLinkRequestHandler` (P06) | `ICustomerTicketClient` | `CustomerTicketClient` | Always 202/identical page | Consumed; no new server entry point |
| Customer attachment download | `GetCustomerAttachmentRequestHandler` (P06b, `GET /api/customer/attachments/{id}`) | `ICustomerTicketClient` | `CustomerTicketClient` | Stream; uniform 404 | Consumed. Portal-hosted `GET /t/{token}/attachments/{id}` is a pass-through adapter, exempt per D-017 |
| KB search page and deflection suggestions | `SearchPublicKbArticlesRequestHandler` (P08) | `IPublicKbClient` | `PublicKbClient` | `KbSearchResponse`; empty on no results | Consumed; same use case for search and deflection |
| KB article page | `GetPublishedKbArticleRequestHandler` (P08) | `IPublicKbClient` | `PublicKbClient` | `PublishedKbArticleDto`; 404 -> NotFound | Consumed; no new server entry point |
| KB home/category lists | `ListPublicKbCategoriesRequestHandler` (P08) | `IPublicKbClient` | `PublicKbClient` | Category DTOs with counts | Consumed; no new server entry point |
| `GET /sitemap.xml` entries | `GetSitemapEntriesRequestHandler` (P08) | `ISitemapEntryProvider` (Portal) -> `IPublicKbClient` | `ApiSitemapEntryProvider` | `SitemapEntry[]` | Consumed; the `MapSitemap` endpoint itself is package-owned (`Blazor.Seo`) and runs no application workflow |
| `GET /robots.txt` | Exempt | `Blazor.Seo` | `MapRobotsTxt` | Static text | Exempt: package-owned, no workflow |
| `/health/live`, `/health/ready`, static assets (`/_framework`, SCSS output, logos) | Exempt | `SyntaxCircus.AspNetCore.Common` | Package health endpoints | 200/503 | Exempt operational/static endpoints |

## Razor Component Boundaries

Follow _template RAZOR_COMPONENT_ARCHITECTURE.md. Feature folders under
`TechStrap.Portal/Features/{Shell,Contact,Tickets,Kb}`. Pages are static SSR
unless stated.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| `App.razor` / `Routes.razor` | Inline (error boundary + router + `SeoHead`-free shell) | None | Stateless | None |
| `PortalLayout` | Paired (injects branding scope/`BrandingThemeFactory`, navigation) | `ProductThemeViewModel` via **`BrandingThemeFactory`** (contrast computation, URL/colour validation) | Resolved once per request from route `{key}` | `PublicProductDto` |
| `ProductHeader`, `ProductFooter` | Inline (parameters only) | `ProductThemeViewModel` | Stateless | None |
| `ProductHomePage` (`/p/{key}`) | Paired (route param, async load, injection) | `ProductHomeViewModel` built in code-behind | Loading/Error/NotFound/Content (SSR, so mostly Content/NotFound) | `PublicProductDto`, `ListPublicKbCategories` response |
| `NotFoundPage` (router adapter over `NotFoundView`) | Inline | None | Stateless | None |
| `ContactPage` (`/p/{key}/contact`) | Paired (`[SupplyParameterFromForm]`, submit, redirect, error mapping) | `ContactFormViewModel` (form model with data-annotation validation constants) | Form state per request; validation + server errors as summary; honeypot check | `SubmitTicketRequest` mapped in code-behind (simple) |
| `AttachmentInput` | Paired (`InputFile` multiple, client-side hint validation) | None | Local selected file list | Files streamed into multipart request |
| `HoneypotField` | Inline | None | Stateless | None |
| `KbDeflectionSuggestions` (InteractiveServer island) | Paired (debounced subject changes, async search, cancellation, disposal) | `KbSuggestionViewModel` list | Idle/Searching/Results/Empty/Error (error silently hides) | `KbSearchResponse` |
| `SubmittedPage` (`/p/{key}/contact/received`) | Paired (short-lived reference, loads nothing sensitive) | None | Stateless; shows ticket number and "check your email" | None (number only) |
| `CustomerTicketPage` (`/t/{token}`) | Paired (token route, load, noindex head, reply submit) | `CustomerTicketViewModel` assembled by feature-local **`CustomerTicketPresenter`** (messages, attachments, closed/follow-up state, status text) | Loading/NotFound(uniform)/Content; posting state; Closed banner | `CustomerTicketDto`, `AddCustomerReplyRequest` |
| `MessageThread` (customer) / `CustomerMessageBody` | `MessageThread` inline loop over view models; `CustomerMessageBody` paired (single `MarkupString` site, link rewriting none) | `CustomerMessageViewModel` | Stateless | `CustomerMessageDto` (body sanitized by API) |
| `CustomerReplyForm` | Paired (form model, attachment input, submit) | `CustomerReplyViewModel` | Submit-in-flight disabled; errors summary | `AddCustomerReplyRequest` |
| `TicketStatusBanner` | Inline | None | Stateless | Status constants from Contracts |
| `LostLinkPage` + `LostLinkForm` | `LostLinkPage` paired (form handling, constant confirmation); `LostLinkForm` inline markup | `LostLinkViewModel` | Always shows identical confirmation | `RequestNewAccessLinkRequest` |
| `KbHomePage` (`/p/{key}/kb`) | Paired | `KbHomeViewModel` | Loading/Empty/Content | `KbCategoryDto`, search box -> GET form |
| `KbCategoryPage` | Paired (route, async load, paging) | `KbCategoryViewModel` | Loading/Empty/NotFound/Content | `KbSearchResponse` (category filter) |
| `KbSearchPage` | Paired (query string) | `KbSearchViewModel` | Empty-query/No-results/Results | `KbSearchResponse` |
| `KbArticlePage` | Paired (route, async load, `SeoHead`, JSON-LD) | `KbArticleViewModel` | Loading/NotFound/Content | `PublishedKbArticleDto` |
| `KbArticleBody` | Inline (single `MarkupString` parameter site) | None | Stateless | Sanitized HTML string from DTO |
| `KbArticleCard`, `KbBreadcrumbs` | Inline | Take primitives/view models | Stateless | None |
| `StateMessage` (empty/error panel) | Inline | None | Stateless | None |

## Syntax Circus Packages

Versions in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md), locked in
`Directory.Packages.props`.

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.Blazor.Seo` | `SeoHead`, JSON-LD, sitemap, robots, canonical host | KB discoverability; noindex on token/form pages | Page tests assert title/description/canonical/OG/JSON-LD; `/sitemap.xml` lists KB URLs only for published articles; `/robots.txt` disallows `/t/` |
| `SyntaxCircus.Http.Resilience` | Typed clients, retry/circuit breaker | Portal -> API clients; retry GETs only, no retry on form submit or customer reply | Stub-handler tests per client |
| `SyntaxCircus.Blazor.Components` | `GlobalErrorBoundary`, `NotFoundView`, reconnect (island only) | Error/not-found UI; `ReconnectModal` only if the island circuit is enabled (**Assumption**: omitted; island degrades silently) | bUnit |
| `SyntaxCircus.Common` | `Result` | Clients return `Result` | Unit tests |
| `SyntaxCircus.AspNetCore.Common` | Health, security headers, correlation id | Strict CSP/headers on public site | Header assertions in host tests |
| `SyntaxCircus.DotEnv`, `.AspNetCore.Serilog`, `.Observability` | Config/telemetry | `.env.example`, token redaction in logs | Log test: `/t/{token}` path is redacted |

Not used: `Blazor.Auth` (portal is anonymous), `Blazor.Tracking` (Not applicable), `Storage`/`Email` (API/worker).

## Deliverables

- [ ] `TechStrap.Portal` host with `.env.example`, forwarded-headers and client-IP forwarding to the API.
- [ ] Typed clients for public product, public ticket, customer ticket, public KB.
- [ ] Branded layout with per-product theming and NotFound handling.
- [ ] Contact page with honeypot, attachments, deflection island, submitted page.
- [ ] Customer ticket view, reply (incl. Closed -> follow-up), lost-link, attachment pass-through.
- [ ] KB home/category/search/article pages with SEO, JSON-LD, sitemap, robots.
- [ ] Page-level SSR tests and bUnit tests; portal container healthy under compose.

## Actionable Tasks

- [ ] **P09-T01** Extend the `tests/TechStrap.Portal.Tests` project (skeleton created in PHASE-02; add bUnit and host tests), add portal options, `.env.example` (API base URL, `TECHSTRAP_PORTAL_PUBLIC_URL`, `TECHSTRAP_PORTAL_DEFAULT_PRODUCT`, `TECHSTRAP_PORTAL_SHOW_POWERED_BY` (D-024), forwarded-header settings) and constants for route templates and header names (Contracts header constants for `X-Ticket-Token`)
  - **Depends on:** P01 (host skeleton)
  - **Validation:** Options validation unit test fails fast on missing API URL; route constants used by every page (no inline route strings repeated).
- [ ] **P09-T02** Implement typed clients (`IPublicProductClient`, `IPublicTicketClient`, `ICustomerTicketClient`, `IPublicKbClient`) with ProblemDetails -> `Result`, GET-only retry, multipart submit, and `.AddForwardedClientIp()` forwarding the original client IP (D-019)
  - **Depends on:** P09-T01, P05, P06, P08 DTOs
  - **Validation:** Stub-handler tests: success/400/404/429/503; POST not retried; `X-Forwarded-For` set from the trusted inbound header; token header never logged (log assertion).
- [ ] **P09-T03** Implement `BrandingThemeFactory`, `PortalLayout`, header/footer and the product-scope resolution (unknown/inactive -> NotFound)
  - **Depends on:** P09-T02, P02 tokens
  - **Validation:** Theory over accent colours (black, white, mid-gray, brand) asserts the computed `--ts-on-accent` meets 4.5:1 on the accent and `--ts-accent-ink` meets 4.5:1 on white; invalid colour falls back to the default; bUnit: unknown key renders NotFound.
- [ ] **P09-T04** Wire `Blazor.Seo` (`AddSyntaxCircusSeo`, `UseCanonicalHost`, `MapRobotsTxt`, `MapSitemap` with `ApiSitemapEntryProvider`) and security headers/CSP
  - **Depends on:** P09-T02
  - **Validation:** Host test: `/robots.txt` disallows `/t/`; `/sitemap.xml` contains published KB URLs only and is cached; response headers include CSP and `X-Content-Type-Options`.
- [ ] **P09-T05** Build `App`/`Routes`/`NotFoundPage` with `GlobalErrorBoundary` and the product home page
  - **Depends on:** P09-T03
  - **Validation:** bUnit/host test: home renders branded name and KB search box; unmatched route -> NotFound with 404 status.
- [ ] **P09-T06** Build `ContactPage`, `ContactFormViewModel`, `HoneypotField`, `AttachmentInput`, and `SubmittedPage` (redirect-after-post)
  - **Depends on:** P09-T03
  - **Validation:** Host test with fake API: valid post -> 302 to `/p/{key}/contact/received`; invalid -> 200 with error summary and preserved input; honeypot filled -> silently success-page without calling the API; 429 -> friendly message; antiforgery missing -> 400.
- [ ] **P09-T07** Build `KbDeflectionSuggestions` island and place it on the contact page
  - **Depends on:** P09-T06, P08-T08
  - **Validation:** bUnit with fake `IPublicKbClient` and `TimeProvider`: rapid typing yields one search; stale responses discarded; cancellation on dispose; failure hides the panel; contact form still posts without the island.
- [ ] **P09-T08** Build `CustomerTicketPage`, `CustomerTicketPresenter`, `MessageThread`, `CustomerMessageBody`, `TicketStatusBanner` with security headers (`no-store`, `no-referrer`, `noindex`)
  - **Depends on:** P09-T03, P06
  - **Validation:** Host test: valid token renders public messages only; invalid/expired/revoked all return the identical 404 body; response headers asserted; presenter unit test for Closed state.
- [ ] **P09-T09** Build `CustomerReplyForm` with attachments, including Closed -> follow-up flow handling
  - **Depends on:** P09-T08
  - **Validation:** Host test: reply on Open ticket refreshes thread; reply on Closed ticket shows follow-up ticket link; oversize/disallowed attachment shows error; double-submit guarded.
- [ ] **P09-T10** Build `LostLinkPage` (`/p/{key}/lost-link`)
  - **Depends on:** P09-T03
  - **Validation:** Host test: matching and non-matching emails produce byte-identical responses and timing within a small tolerance (no early-return branch visible in code review); 429 handled generically.
- [ ] **P09-T11** Implement the portal `GET /t/{token}/attachments/{id}` pass-through adapter (D-017)
  - **Depends on:** P09-T08
  - **Validation:** Host test: streams bytes with `attachment` disposition and `nosniff`; other tokens/ids -> uniform 404; no Infrastructure reference (architecture test).
- [ ] **P09-T12** Build `KbHomePage` and `KbCategoryPage` with paging and `KbArticleCard`/`KbBreadcrumbs`
  - **Depends on:** P09-T03, P08-T08
  - **Validation:** bUnit: empty category shows empty state; paging links preserve query; shared + product articles appear.
- [ ] **P09-T13** Build `KbSearchPage` (GET form) and result highlighting using the API snippet (plain text, escaped)
  - **Depends on:** P09-T12
  - **Validation:** bUnit: empty query shows prompt; no results shows contact-us link; snippet HTML-encoded (test with `<b>` in data).
- [ ] **P09-T14** Build `KbArticlePage`/`KbArticleBody` with `SeoHead`, canonical URL, Open Graph, `BreadcrumbListSchema` and article JSON-LD
  - **Depends on:** P09-T04, P09-T12
  - **Validation:** Host test parses the page head: title, description, canonical, `og:*`, valid JSON-LD; unpublished slug -> 404; body markup identical to API HTML (no re-encoding bugs); `MarkupString` only in `KbArticleBody`/`CustomerMessageBody` (architecture test).
- [ ] **P09-T15** Add output caching for KB pages and sitemap; exclude `/t/*` and forms
  - **Depends on:** P09-T12, P09-T14
  - **Validation:** Host test: KB responses carry cache headers and hit the fake API once for repeated requests; `/t/*` has `no-store`.
- [ ] **P09-T16** Apply BRAND.md/UX-BRIEF-portal styling: responsive layout, error summaries, focus states, themed accent usage, no-JS verification
  - **Depends on:** P09-T06, P09-T08, P09-T14
  - **Validation:** UX-BRIEF-portal checklist completed; manual run with JavaScript disabled covers contact -> submitted and ticket view -> reply; axe run has no critical findings; Lighthouse accessibility >= 90 (**Assumption**).
- [ ] **P09-T17** Redact `/t/{token}` and `X-` token headers in portal request logs; add log-redaction test
  - **Depends on:** P09-T08
  - **Validation:** Test host captures Serilog output for a token request and asserts the token string never appears.
- [ ] **P09-T18** Add portal to compose with forwarded-headers/subnet trust (D-019) and the end-to-end intake path (portal -> API) rate-limit check
  - **Depends on:** P09-T06, P05
  - **Validation:** `docker compose up`; hitting the contact form repeatedly from one client IP behind the proxy trips the API 429 for that IP only (second source IP unaffected); `/health/ready` 200.
- [ ] **P09-T19** Add architecture rules: Portal references only Contracts; no `[Inject] HttpClient` in components; `MarkupString` restricted
  - **Depends on:** P09-T14
  - **Validation:** Architecture.Tests fail on a deliberate violation sample.
- [ ] **P09-T20** (Optional, **Assumption**) Playwright e2e: submit ticket -> read email link (from test SMTP sink) -> view -> reply
  - **Depends on:** P09-T09, P09-T18
  - **Validation:** Passes in nightly CI against compose with MailPit/test sink.
- [ ] **P09-T21** (D-024) Contact-page prefill: bind `subject`, `name` and `email` from the query string into `ContactFormViewModel` through the same validation attributes and length constants as posted input; all three stay visible and editable (inputs carry `maxlength` equal to the model limit), no hidden field carries prefill data, nothing auto-submits, unknown parameters are ignored and never echoed; add `name` and `email` query values to the request-log redaction (P09-T17). App context (version, device) is not a URL concern: it goes through the SDK/API
  - **Depends on:** P09-T06, P09-T17
  - **Validation:** host tests: `?subject=&name=&email=` render three visible editable inputs with the values, HTML-encoded (injected markup is escaped); an over-length subject and an invalid email fail on post exactly as typed input does; extra parameters such as `product` or `token` change nothing; a log-capture test shows no prefilled name or email
- [ ] **P09-T22** (D-024) "Powered by TechStrap" link and setting: portal options `ShowPoweredBy` bound from `TECHSTRAP_PORTAL_SHOW_POWERED_BY` (default `true`); the footer renders the line as a link to https://github.com/Syntax-Circus/techstrap (constant, neutral secondary ink, underlined, 4.5:1) on every page including NotFound and error pages, and omits it entirely when false
  - **Depends on:** P09-T03
  - **Validation:** host tests: with the default every page type contains exactly one link with the exact href and no other TechStrap text; with the option false none contains the line; the option defaults to true when the variable is unset; an invalid value fails startup validation
- [ ] **P09-T23** (D-024) Agent identity on the ticket view: `CustomerMessage` shows the API-resolved `AuthorDisplayName` as-is (HTML-encoded) for agent messages and "You" for the customer's own; no agent email, id or avatar is rendered
  - **Depends on:** P09-T08, P06-T22
  - **Validation:** bUnit: an agent message shows "Sam from Orbitly Support"; a fixture with override "Samantha from Orbitly Support" shows it unchanged; markup in the name is encoded; a DTO shape test shows the portal model has no agent email property


## Success Criteria

- [ ] A customer can open `/p/{key}/contact`, see matching KB suggestions as they type, submit with attachments, and see a confirmation with the ticket number; the form also works with JavaScript disabled (without suggestions).
- [ ] Following the emailed `/t/{token}` link shows the public conversation; the customer can reply; replying on a Closed ticket creates and links a follow-up ticket.
- [ ] Invalid, expired and revoked tokens are indistinguishable (identical 404); lost-link responses are identical for known and unknown emails.
- [ ] Each product's portal pages use its name, logo and accent colour, including readable contrast; an unknown product key shows NotFound.
- [ ] KB pages are browsable, searchable, SEO-tagged with sitemap/robots as specified; ticket pages are `noindex`/disallowed.
- [ ] The contact URL prefills `subject`, `name` and `email` (visible, editable, validated like typed input); "Powered by TechStrap" links to the GitHub repo and disappears when `TECHSTRAP_PORTAL_SHOW_POWERED_BY=false`; agents appear as the resolved public name (D-024).
- [ ] Portal request logs contain no access tokens; token pages send `no-store` and `no-referrer`.
- [ ] Rate limits observe the real client IP through the portal.
- [ ] `dotnet build`/`dotnet test` green; portal container healthy under compose.

## Boundary Validation

- [ ] Application use-case entry points delegate to the named handlers listed above (all via API typed clients; the two attachment pass-throughs forward only).
- [ ] Framework-owned operational or static exemptions execute no application workflow (`robots.txt`, health, static).
- [ ] Handler constructor dependencies contain only approved abstractions (N/A: no handlers added; verified unchanged in P05/P06/P08).
- [ ] Persistence and integration entities do not cross infrastructure boundaries (Portal references only Contracts).
- [ ] Cancellation reaches asynchronous handler dependencies (components pass request-aborted/component tokens to clients).
- [ ] Expected outcomes and transport mapping have focused tests (client `Result` mapping and page-level status codes).
- [ ] Infrastructure implementations have integration coverage where applicable (N/A for portal; e2e optional).
- [ ] Inline Razor components contain only simple parameters and, at most, one trivial synchronous `EventCallback`-forwarding callback.
- [ ] Every component beyond the inline ceiling uses paired `.razor` and `.razor.cs` files, with all C# in code-behind.
- [ ] Each Razor ViewModel is feature-local and presentation-only; no direct-model exception exposes an API ViewModel.
- [ ] A factory or presentation service is used only for non-trivial mapping, asynchronous assembly, or multiple dependencies (`BrandingThemeFactory`, `CustomerTicketPresenter` only).
- [ ] API request and response contracts use DTO names and contracts, never Razor ViewModels.
- [ ] Repeated or business-meaningful literals are named constants (routes, header names, cache TTLs, accent regex, honeypot field name).
- [ ] Duplicated-looking logic across flows was evaluated for genuine divergence (customer reply vs contact form share `AttachmentInput` only; the two attachment pass-throughs in Admin/Portal differ in authorization and are intentionally not shared).

## Risks and Open Questions

- [ ] **Real client IP for rate limiting (D-019):** relies on the P01/P05 forwarded-headers/pinned-subnet config trusting the portal as a proxy hop; if rate limits ever partition by connection IP they collapse to the portal's IP. Verify in P09-T18.
- [ ] **Deflection island** needs a SignalR circuit on a public site (memory/DoS surface). Mitigate with the reconnect-less minimal circuit and per-IP limits; fallback is dropping deflection to a GET search link.
- [ ] Portal-hosted attachment pass-through is not in the handler catalog (exempt adapter, D-017); same decision as the admin pass-through.
- [ ] The customer token header (`X-Ticket-Token`) and reply/follow-up response shape come from P06 and Contracts; keep route/header constants in Contracts.
- [ ] Output caching and per-product branding: ensure cache key varies by product key and branding changes appear within the TTL.
- [ ] Sitemap size/volume for many products: paginate or sitemap index if >50k URLs (unlikely; **Assumption**).
- [ ] bUnit (version in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md)) is required; Portal tests live in `tests/TechStrap.Portal.Tests`, skeleton created in PHASE-02, extended in P09-T01 (listed in `02-ARCHITECTURE.md`).
- [ ] Carried forward from the PHASE-03 final review: Customer DTOs never carry `LastActivityAt`, the agent email or internal events (shared with PHASE-06); the portal reads messages with `publicOnly: true`.
- [ ] Carried forward from the PHASE-04 final review: Add a no-brand guard test on the Portal error page, so it never shows product branding for an unhandled exception.
- [ ] Carried forward from PHASE-05 (D-034): follow-up submission must check the ticket belongs to the caller's product and requester before attaching (the PHASE-03 carry-forward). The lost-link flow issues a fresh token when a link reaches its 365-day cap (D-032).
- [ ] Carried forward from PHASE-06c (D-039): add the Sentry header scrub for `X-Ticket-Token`, `X-Api-Key`, `Authorization` and `Cookie` to the Portal (the Api has it), and wire `PiiRedactionEnricher` into the Portal's `AddStandardSerilog` call once the Portal handles requester data. Both now live in TechStrap.Hosting (D-040): reference it, call options.AddSensitiveHeaderScrubbing() in the UseSentry callback and logger.Enrich.With<PiiRedactionEnricher>() in AddStandardSerilog.

## Handoff

Before [PHASE-12](PHASE-12-release-hardening.md) starts: portal pages and
flows above work end to end against the compose stack with seed data; token,
form and KB-render paths are documented for the security review (list the
`MarkupString` sites, token headers/log redaction, honeypot and rate-limit
configuration); the portal image builds and is healthy; the attachment
pass-throughs are recorded as D-017 and any further decision (for example the
deflection island) is recorded in [04-DECISION-LOG.md](04-DECISION-LOG.md).
