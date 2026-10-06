# Portal app (customers)

`TechStrap.Portal` is the public site customers use: a product's own help and support pages, with the product's name, logo and accent. It never touches the database: every page asks the TechStrap API,
anonymously. This page is for people who run it and people who extend it. How agents work the tickets customers send is in [ADMIN-APP.md](ADMIN-APP.md); the decisions behind the Portal are D-045 in the
[decision log](../architecture/04-DECISION-LOG.md) (with its 2026-10-06 addenda for 09b and 09c) and the spec is [PHASE-09](../architecture/PHASE-09-public-portal.md).

PHASE-09 is delivered in four pull requests. **09a** is the foundation: the settings, the API client, the per-product theme and shell, the product home, the root page, the ticket-page headers, robots.txt, log
redaction and the architecture rules. **09b** adds the customer flows: the contact form with its suggestions and received page, the ticket page with replies and the Closed follow-up, the attachment pass-through
and the lost-link page. **09c** (this page describes all three) adds the help centre: the knowledge base home, category, search and article pages, the SEO head and structured data, output caching and the sitemap.
**09d** is the polish pass (styling, accessibility, the no-JS check, the double-send guard and the copy button, counter and sending state). The routes of all of them are in `PortalRoutes`.

## Run it locally

You need the API running (the root README starts it with Docker Compose, or run `src/TechStrap.Api`). With the Development seed (`TECHSTRAP_SEED_DEV_DATA=true`, see
[DEV-DATA.md](DEV-DATA.md)) the products `orbitly` and `paperplane` exist.

```bash
cp src/TechStrap.Portal/.env.example src/TechStrap.Portal/.env.local     # then edit the values below
dotnet run --project src/TechStrap.Portal --urls http://localhost:8082
```

`.env.local` is read in Development only and is git-ignored. Open `http://localhost:8082/p/paperplane` for the themed product home, `http://localhost:8082/p/paperplane/contact` for the contact form (try
`?subject=Printer%20jam&name=Jane%20Doe&email=jane%40example.com` for the prefill) and `http://localhost:8082/p/nope` for the not-found page. A ticket link from an email is `/t/{token}`. With Docker Compose the
Portal listens on `http://127.0.0.1:8082` and compose sets `Api__BaseUrl` and `TECHSTRAP_PORTAL_PUBLIC_URL` for it.

### Configuration

The Portal refuses to start with a message that names the missing or malformed key. Every key is in `src/TechStrap.Portal/appsettings.json` with its default (D-043). 09b adds no key.

| Key | Required | Default | Meaning |
| --- | --- | --- | --- |
| `API__BASEURL` (`Api:BaseUrl`) | Yes | blank (the compose files set `http://api/`) | The address of the TechStrap API, absolute http or https |
| `TECHSTRAP_PORTAL_PUBLIC_URL` | Outside Development | blank | The Portal's public address as customers see it, absolute http or https, no query or fragment. The base of canonical URLs and robots.txt's sitemap line (`Seo:BaseUrl` is derived from it); the same value as the Api's key |
| `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` | No | blank | A product key. When set, `/` redirects (302) to `/p/<key>`; blank shows a neutral page with no product list |
| `TECHSTRAP_PORTAL_SHOW_POWERED_BY` | No | `true` | `false` hides the "Powered by TechStrap" line on every page. Any other value than `true` or `false` stops the start (D-024) |
| `CANONICALHOST__CANONICALHOST` | No | blank | The host to redirect legacy hosts to; blank turns the redirect off |
| `CANONICALHOST__LEGACYHOSTS__0` ... | No | none | The hosts that are redirected. Only these are; any other host is left alone |
| `CANONICALHOST__FORCEHTTPS`, `CANONICALHOST__PERMANENT` | No | `false`, `true` | https target; 301 (true) or 302 |
| `DATAPROTECTION__KEYRINGPATH`, `TRUSTEDPROXY__*`, `SECURITYHEADERS__*`, `SENTRY__*`, `OPENTELEMETRY__*`, `SERILOG__*` | No | see `appsettings.json` | Shared with the other hosts. In production keep `DATAPROTECTION__KEYRINGPATH` on a volume (both compose files do): the "received" page's `?ref=` is protected with it, and without a persisted ring a restart makes every pending reference show the generic confirmation (and invalidates every antiforgery token in flight) |

## How a page is served

Every page is static server-side rendering: there is no render mode, no circuit and no SignalR (`PortalRules.InteractivityViolations` fails the build of the architecture tests if one appears).

- **The product scope.** A page under `/p/{key}` derives from `ProductPageBase`. It loads the product once through `IPublicProductClient` and puts the result in the request's `ProductScope`; `PortalLayout`
  then wraps the page in the product's accent (`AccentScope`), a header (logo and name) and a footer. The scope is per request, so the not-found and error pages, which the host renders in a fresh scope, are
  never branded. The ticket page has no key in its address: it loads the ticket first and asks for the product named by the ticket's `ProductKey`.
- **One answer for "no such product".** An unknown key, an inactive product and a key that is not a slug (`^[a-z0-9]+(-[a-z0-9]+)*$`, at most 40 characters) all call `NavigationManager.NotFound()`, so the visitor
  gets the same neutral 404 page as for an unknown route; a malformed key never reaches the API. `NeutralPagesGuardTests` pins it.
- **When the API fails** the page shows "This page could not be loaded." with a Try again link and a 503 (429 when the API is rate limiting), never a stack trace.
- **Branding is untrusted.** `ProductThemeViewModel` keeps the accent only if `ProductAccent.TryDerive` accepts it and the logo only if it is https (or http to `localhost` or `127.0.0.1` in Development, the same
  rule as the CSP's `img-src`). The product name is always encoded.
- **Copy** lives in `ShellCopy`, `ProblemCopy`, `FormCopy`, `ContactCopy`, `LostLinkCopy`, `TicketCopy` and `KbCopy`; routes in `PortalRoutes` (a page declares `@attribute [Route(PortalRoutes.XTemplate)]` and builds every link with a builder).

### Forms and uploads

The contact form, the reply form and the lost-link form are plain static-SSR forms: `<form method="post" @formname=...>` with `<AntiforgeryToken />`, a `[SupplyParameterFromForm]` model and a redirect after the post, so
nothing needs script and a refresh never sends twice.

- **Antiforgery** is enforced by the framework: a post without a valid token is a 400 and no handler runs.
- **The model.** A class with settable properties and a nullable `IReadOnlyList<IBrowserFile>? Files` binds `<input type="file" multiple name="Form.Files">`; a form with no file chosen binds no files at all. The property
  may not have an initializer (analyzer BL0008): the page sets it in `OnInitializedAsync` when the framework left it null. Always read a file with `OpenReadStream(IntakeLimits.MaxFileBytes)`: the framework's
  default stops at 512,000 bytes.
- **The size limit.** `[RequestSizeLimit(IntakeLimits.FormBodyBytes)]` on the page (27,262,976 bytes, the API's own limit) is applied by endpoint routing before the antiforgery check reads the form, so an
  oversized body is never buffered. `RequestTooLargeMiddleware` answers a declared length over the limit with a plain 413 before the framework turns the failed read into a 400 about a token; a chunked body over
  the limit is still that 400. `TestServer` has no limit feature, so these tests use `UseKestrel(0)`.
  Deployment: set a request body limit and a rate limit for the Portal's form POSTs at the reverse proxy, because the Portal buffers a form of up to about 27 MB before the antiforgery check runs (see [DEPLOYMENT.md](../self-hosting/DEPLOYMENT.md)).
- **Checks before the API.** `ContactFormValidator`, `ReplyFormValidator`, `EmailRules` and `AttachmentRules` use the `IntakeLimits` constants (name 100, email 320, subject 200, body 100,000, 5 files, 10 MB each,
  25 MB in all, ten extensions), the same constants the inputs' `maxlength` attributes use; `IntakeLimitsParityTests` keeps them equal to the Domain's. The API checks again and is the authority. An error is a
  `FormError` (field, code, sentence): the codes are the API's (`email-invalid`, `attachments-too-many`, ...) and the sentences are `FormCopy`'s, so the API's own text is never shown. `FormFailure` decides
  what the page shows for a refused call: field errors for a 400, the attachment error for a 413 or 415, a calm notice and a status for a 429 (429), `reply-conflict` (409) or an outage (503), and the uniform 404
  when the product or ticket is gone. What the visitor typed is kept; files are not (a browser never keeps them), and the form says so.
- **File names** come from the browser, so every file name is cleaned (`AttachmentFileName`: last path segment, no quotes, control or format characters) before it is shown or sent.
- **The honeypot** (`Form.Website`) is hidden by a style sheet class, out of the tab order, `aria-hidden` and `autocomplete="off"`. A filled one is sent to the API like any other value; the API validates the
  product and answers a believable 201 without creating a ticket, so a bot gets the real response.
- **The prefill** `?subject=&name=&email=` fills the same three visible, editable inputs on the contact page (nothing hidden, nothing auto-submitted, other parameters ignored and never echoed) and is judged on post
  exactly like typed text. The form posts to the page's own address without the query. The `name`, `email`, `subject`, `ref` and `q` query values are masked in logs and Sentry.
- **The received page** (`/p/{key}/contact/received?ref=`) shows the ticket number from a data-protected, 10-minute reference (`ReceivedReference`, purpose `TechStrap.Portal.ContactReceived.v1`). A missing, expired, tampered or
  foreign reference shows the generic confirmation, never an error. It carries the number only.

### Suggestions beside the subject

The contact page puts `<ts-kb-suggestions field="subject" src="/p/{key}/suggest">` after the subject field, with a plain link to the help search inside it (shown only without script), and loads
`wwwroot/js/kb-suggestions.js` as `<script type="module" src=...>` (no CSP change). The module defines the element: a 300 ms debounce, at least 3 characters, one request at a time (a newer one aborts the older
and its answer is dropped), results written with `textContent` and links built only from root-relative paths, hide-on-error, and clean-up in `disconnectedCallback`. It never parses text as markup, and a test fails
if the file ever contains `innerHTML`. `GET /p/{key}/suggest?q=` (`SuggestEndpoint`) is the Portal-hosted adapter: it asks `IPublicKbClient.SearchAsync` (a read, forwarding the visitor's address), cuts the text at 200
characters, returns at most 5 `{title, snippet, href}` items with the links built by `PortalRoutes.KbArticle`, passes the API's 429 through and answers an empty list for a blank text or any other failure.
The module is tested with `node --test` (`tests/TechStrap.Portal.Tests/js/kb-suggestions.test.mjs`, run by `scripts/tests/PortalScripts.Tests.ps1`, skipped without node).

### The help centre

Four pages, each on `ProductPageBase`, each static SSR with plain links and a GET form, so everything works without script:

- `/p/{key}/kb` (`KbHome`) lists the categories the product can see with their article counts and descriptions (`IPublicKbClient.ListCategoriesAsync`); a product with no article shows the empty state.
- `/p/{key}/kb/{category}` (`KbCategory`) is a page of the category's published articles, newest update first, 10 a page (`ListCategoryArticlesAsync`). The pager (`Pager`) is `?page=n` links: page one has no query.
- `/p/{key}/kb/search?q=&page=` (`KbSearch`) is a GET form (`KbSearchBox`). An empty text shows a prompt and makes no call; no result shows a link to contact support; a snippet is plain text; a text is cut at 200
  characters; a page with a text is `noindex`; paging links keep the escaped text.
- `/p/{key}/kb/{category}/{slug}` (`KbArticle`) shows the article: breadcrumbs, the title, the day it changed, the body and a "Still need help?" link. **`KbArticleBody` is the second and last place the Portal turns
  text into markup** (`PortalRules.MarkupStringSites` lists exactly it and `CustomerMessageBody`): the API renders the Markdown and sanitises the HTML (D-044), and the Portal passes it on byte for byte
  (`KbArticleHostTests` compares it with the API's string). A plain-http image in an article is blocked by the CSP's `img-src 'self' https: data:` in Production; that is the intended posture.

Everything an agent wrote or a visitor typed (a name, a title, a summary, a snippet, a search text) is plain text shown by Razor, which encodes it. `/p/{key}/kb/search/{x}` matches the article route with the category
`search`, which the API reserves, so it is a 404.

**One answer for "not there".** An unknown or inactive product, a category that is unknown, another product's or empty, a page past the end, and an article that is a draft, archived, another product's, in the wrong
category or unknown are all the neutral 404, byte for byte the page an unknown route gets, with none of the product's theme (`ProductPageBase.Fail` forgets the product first). A key or slug that is not a slug is
refused by `IPublicKbClient` without a call (`KbSlugShape`, like `ProductKeyShape`). When the API fails the page shows the calm message with a 503 (429 when rate limiting), in the product's theme.
**The `page` and `q` values are read as text** (`KbPaging.Parse`, `KbSearchText.Clean`): the framework's binding to a number answers 500 for `?page=abc`.

### SEO and structured data

Each page sets its head with `SeoHead` (`SyntaxCircus.Blazor.Seo`): a unique title ("{page} - {product} Help Centre", the product being the site because `Seo:SiteName` is global), a description, the canonical
address (built from `TECHSTRAP_PORTAL_PUBLIC_URL`, never the Host header; a page of a category names its own page, and a shared article is canonical under the product the visitor is on), Open Graph (the product's logo
when it has an acceptable one, else `/icon-512.png`, never the bare site address) and `NoIndex` for a search with a text. The description of an article is its summary, else the first sentence of the body as plain
text (`KbPlainText`), else the title and product. The article page also writes a `BreadcrumbList` and an `Article` as JSON-LD.

**JSON-LD must go through `JsonLdText`.** `SyntaxCircus.Blazor.Seo` 0.1.4's `JsonLd` writes its JSON through a markup string with an encoder that leaves `<`, `>` and `&` alone, so a `</script>` in an article title
ends the block and injects markup. Escaping first would be escaped twice, so every string of the Portal's own structured-data records (`BreadcrumbListLd`, `ArticleSchema`) is a `JsonLdText`, whose converter writes
`<`, `>`, `&`, the apostrophe, `+` and every non-ASCII character as `\uXXXX`; the JSON reads back as the original. A Razor file cannot hold the text `</script` in a string (the Razor parser reads a tag), so the
hostile texts live in C# tests. The block is data, not script, so the CSP does not change. Do not use the package's schema records for text a visitor or an agent wrote.

### Caching and the sitemap

The help-centre home, a category page and an article page are kept for 60 seconds by the framework's output cache (`AddPortalOutputCache`, one base policy with the path predicate `PortalCachePaths.IsCacheable`; no
attribute on a page). The key varies by the `page` query value only and never by host (the framework's default key holds the whole query string and the host, so `?utm=1`, `?utm=2` ... would fill the store); a `page`
value is kept only on a category page and only from two up (the home and an article ignore `page`; `?page=1` is the page with no value); any other value is answered but never kept. Only all-lowercase paths are kept (a path with an upper-case letter is never stored or looked up), so a capitalised path can never be answered from the lower-case entry: `/p/ACME/kb` stays the neutral 404 and `/p/acme/KB` is a 200 that is never stored. The search page, the form pages, `/p/{key}` itself, `/t/*`, the suggest adapter, `/not-found`, the sitemap and every answer that is not a 200 are never kept,
and the output cache never stores a response that sets a cookie (no help-centre page does). A delivered KB page tells browsers `Cache-Control: public, max-age=60` (`PathHeaderRule.SetOnSuccess` in Hosting: a 404,
429 or 503 never gets it); the search page is `no-store`. `UsePortalOutputCache` goes after the error pages and before the endpoints (`ProgramOrderTests` pins the order), so the shared security headers and the per-path
rules are applied to a cached answer too, and it sets the request's own `X-Correlation-Id` again when the response starts, because a stored copy replays the first request's.

`/sitemap.xml` is `MapSeoSitemap` with a provider (`PortalSitemap`). A build (`PortalSitemapBuilder`) asks for the active products (`IPublicProductClient.ListAsync`), then for each product's published articles, and
lists the root page (only when no default product is configured: `/` is then a redirect), each product's home, its help centre home, its categories and its articles; a shared article is listed under each product. Every
address is absolute (from the public URL) and at most 50,000 are listed, the root page included. `PortalSitemapCache` keeps the result for 15 minutes in an `IMemoryCache` with single-flight (twenty concurrent
requests make one build), builds on its own task with its own cancellation token (a crawler that goes away stops waiting but cannot cancel the build), remembers a failed build for one minute while the last good sitemap is
served, and fails the request (the 500 page) only when there has never been a good one. While a rebuild runs, a request other than the one that started it gets the last good sitemap at once. The build's calls carry the address of the visitor whose request started it (a stand-in `HttpContext`; known gap below).

### The ticket page and attachments

`/t/{token}` (`Ticket.razor`) parses the token first (`TicketToken.TryParse`; a malformed one is the uniform 404 and the API is never asked), loads the ticket through `ICustomerTicketClient`, then the ticket's
product theme (an inactive or unknown product is the neutral theme, never a 404). `CustomerTicketPresenter` builds the view model: the status in the customer's words (New "Received", Open "In progress",
Pending "Waiting for your reply", Solved "Solved" with a note that a reply reopens it, Closed "Closed" with the note that a reply starts a follow-up), "You" for the customer's messages, the API's resolved name
for an agent exactly as it came, a neutral label for the system. **`CustomerMessageBody` is one of the two places the Portal turns text into markup** (`PortalRules.MarkupStringSites`; the other is `KbArticleBody`): the API sanitises the message HTML.
Every other string, the subject, the names and the file names included, is encoded by Razor.

A reply is a multipart post through the write client (never retried) with the token as the `X-Ticket-Token` header. On success the page redirects to itself; when the API started a follow-up (a reply to a Closed
ticket) the new token is read from the last segment of `FollowUpViewUrl` by `FollowUpLink` and the redirect is `PortalRoutes.Ticket(newToken)`, so a visitor is never sent to another host; a link that cannot
be read gives a generic "we started a follow-up" message and no redirect.

`GET /t/{token}/attachments/{id}` (`AttachmentPassThrough`, exempt like D-017) opens `GET api/customer/attachments/{id}` with `ApiConnection.OpenStreamAsync` (response headers only, the body is streamed, never
buffered) and always sends a download: `Content-Disposition: attachment` with a cleaned name, `X-Content-Type-Options: nosniff`, and, from the `/t` header rules, `no-store`, `no-referrer`, `noindex` and the
sandbox CSP. A bad token, an id that is not a GUID (default `D` format) and an upstream 404 are the same empty 404, which the host turns into the one neutral page; the API's 429 is a 429; anything else is a 502.

### The lost-link page

`/p/{key}/lost-link` asks `ICustomerTicketClient.RequestAccessLinkAsync` (a write, never retried, no token) and redirects to `?sent=1`, which shows one sentence. The Portal looks at nothing the API answered beyond
success, so every well-formed address gets a byte-identical response (`LostLinkHostTests` compares them; the timing difference D-038 accepts is out of scope). A malformed address is a field error and the API is not
asked; a 429 is a calm notice.

### Talking to the API

`ApiConnection` (internal, `Clients/`) is the only place the Portal uses HTTP. It sends reads through a client that retries transport errors, 408 and 502 to 504 (twice, honouring `Retry-After` up to
2 seconds, no circuit breaker) and writes through a client that never retries. Both forward the visitor's address in `X-Forwarded-For` (`AddForwardedClientIp`; the API trusts it only from the compose subnet,
D-019) and have no logging handlers. `ProblemMapping` turns every answer into a `Result`: 400 keeps the API's field codes, 404 is one not-found whatever the API called it, 409 is `reply-conflict`, 413 and 415 are the
attachment errors, 429 is rate limited, any 5xx or transport error is `api-unavailable`, with fixed sentences from `ProblemCopy`. A call made as a ticket's customer takes a `TicketToken` (43 base64url characters;
it prints as `[token]`), which becomes the `X-Ticket-Token` header of that request only. The typed clients are `IPublicProductClient`, `IPublicTicketClient` (multipart intake), `ICustomerTicketClient` (view, reply,
lost link, attachment stream) and `IPublicKbClient` (paged search, categories, a category's articles, the article and the sitemap entries; `IPublicProductClient` also lists the active products for the sitemap). `MultipartForm` builds the bodies (text fields first, one `Attachments` part per file, the file streams owned by the request).

### Headers, robots.txt and the canonical host

`UseTechStrapWebHost(PortalHeaderRules.Rules)` applies per-path rules after the shared security headers: everything under `/t` gets `Referrer-Policy: no-referrer`, `Cache-Control: no-store` and
`X-Robots-Tag: noindex`, only `/t/{token}/attachments/{id}` is sandboxed (so the ticket page keeps the normal CSP), and the four form pages of a product (`/p/{key}/contact`, `/contact/received`, `/lost-link` and
`/suggest`) get `no-store` and `noindex`; a delivered help-centre page gets `public, max-age=60` and the search page `no-store` (see Caching). `/robots.txt` disallows `/t/` and names `{public url}/sitemap.xml`. The
canonical-host redirect is an allow-list of legacy hosts and does nothing until configured.

### Logs and Sentry

The framework's request lines (path and query) are suppressed by default (`Microsoft.AspNetCore` is set to Warning); an operator who enables them gets them redacted. The PII enricher masks the access token in a
`/t/{token}` address and the value of a `name`, `email`, `subject`, `ref` or `q` query parameter; the Sentry processors mask the same (except `q`, which they already masked) in URLs, headers, breadcrumbs and spans.
`RequestLogRedactionHostTests`, `TicketReplyHostTests`, `ContactPostHostTests` and `TicketTokenLeakTests` scan every level at Verbose.

### The compose smoke

`pwsh scripts/Test-ComposeSmoke.ps1` (by hand or the manual "Compose smoke" workflow) also starts the Portal, checks its `/health/ready`, and proves the Api's rate limit sees the visitor, not the Portal's
container (D-019): its override lowers the Api's public limit to 3 and makes the Portal trust the compose subnet, then calls `GET /p/smoke/suggest` from inside the network as `203.0.113.10` (three 200s, then a 429)
and as `203.0.113.11` (a 200). It never runs `down -v`.

## Where things live

```text
src/TechStrap.Portal/
  Caching/        PortalCachePaths (what is kept), PortalOutputCache (the policy and the pipeline step)
  Clients/        ApiConnection, ProblemMapping and ProblemCopy, TicketToken, ApiClientRegistration (the two named clients), the typed clients (IPublicProductClient, IPublicTicketClient,
                  ICustomerTicketClient, IPublicKbClient), MultipartForm, AttachmentFileName, ApiQuery, ApiDownload
  Components/
    Kb/           KbArticleBody (the other markup site), KbArticleCard, KbBreadcrumbs and KbCrumb, KbSearchBox, KbPlainText
    Layout/       PortalLayout, ProductHeader, ProductFooter
    Pages/        Home (the root), ProductHome, KbHome, KbCategory, KbSearch, KbArticle, Contact, ContactReceived, Ticket, LostLink, NotFound, Error, StyleGuide (Development only)
    Tickets/      CustomerMessageBody (a markup site), MessageThread, TicketStatusBanner
    Ui/           AccentScope, PoweredByFooter, ProductUnavailable, DevelopmentOnly, ErrorSummary, FieldError, FormField, AttachmentInput, HoneypotField, Pager, StateMessage
    KbCopy.cs     the words of the help centre (beside ShellCopy)
  Forms/          FormError and FormFields, FormCopy, FormFailure, AttachmentRules, EmailRules, ContactFormViewModel and its validator, ReplyForm, LostLinkForm, ContactCopy, ReceivedReference
  Kb/             KbPaging, KbSearchText (plain helpers the pages and the suggest adapter share)
  Headers/        PortalHeaderRules (the /t rules, the attachment sandbox, the form pages and the help centre)
  Products/       ProductThemeViewModel, ProductScope, ProductPageBase
  Routing/        PortalRoutes, ProductKeyShape, KbSlugShape
  Seo/            PortalSeoRegistration (Blazor.Seo: base URL, robots.txt, the sitemap, canonical host), PortalSitemap, PortalSitemapBuilder, PortalSitemapCache, JsonLdText,
                  KbStructuredData (the breadcrumb and article records), KbSeo
  Settings/       PortalOptions and its validator
  Suggestions/    SuggestEndpoint (GET /p/{key}/suggest)
  Tickets/        CustomerTicketPresenter and its view models, TicketCopy, FollowUpLink, AttachmentPassThrough
  Uploads/        RequestTooLargeMiddleware
  Styles/         SCSS partials over the brand tokens (docs/BRAND.md)
  wwwroot/js/     kb-suggestions.js
```

Rules the code follows (the architecture tests check them): the Portal references **Contracts and Hosting only**; **components never inject `HttpClient`** (only `Clients/` mentions it); no inline script
or style; no interactive render mode; `MarkupString` is used in exactly two files, `Components/Tickets/CustomerMessageBody.razor` and `Components/Kb/KbArticleBody.razor` (a third is a design decision, argued in
its own commit); every plain-text DTO field is encoded and every JSON-LD string is a `JsonLdText`.

## Tests

`tests/TechStrap.Portal.Tests` runs the host in memory with a stub API behind its two named clients (`PortalFactory.Api`, `StubApiHandler`), so a test sees what the API would see, including the headers the
Portal added. `ProxyHopStartupFilter` puts the host behind a trusted reverse proxy and every API-calling host test asserts the visitor's address with `AssertEveryCallBore`; `OkProbeStartupFilter` answers 200 on a
path no Portal route matches. A test that needs the real server (a request size limit) calls `UseKestrel(0)` and `StartServer()` and reads the address from `IServerAddressesFeature`. bUnit covers the layout and
the shared help-centre components; the help-centre host tests (`Kb/`) parse the page with AngleSharp (which bUnit brings) and assert on elements, the JSON-LD is parsed as JSON, and `OutputCachePipelineTests` builds a
small host with the real wiring to prove what the cache keeps. The cache and sitemap-cache tests that wait use short real lifetimes, because a `MemoryCache` has no `TimeProvider`.
`tests/TechStrap.Portal.Tests/js` holds the node tests of the browser module. The shared rules are in `TechStrap.Architecture.Tests` (`PortalRules`), the Hosting header-rule mechanism in `TechStrap.Api.Tests`
(`PathHeaderRuleHostTests`), and the log and Sentry redaction in `TechStrap.Api.Tests`.

## Known gaps

- `ProductHome` and `KbHome` each carry a search box, so the product home shows a duplicate of the help-centre one; merging them is PHASE-09d.
- The sitemap build's API calls carry the address of the visitor whose request started it, so each sitemap build makes 1 + N API calls under one forwarded IP, and the API's public limit is 120 per minute per IP:
  with about 120 or more active products the sitemap build is rate-limited and the sitemap goes stale or becomes unavailable. Possible later fixes are a bulk sitemap endpoint, or exempting the Portal's own build traffic from the limit.
- The help centre has no category description on its category page and the product home has no category list; the visual polish, the accessibility and no-JS pass and the double-send guard are PHASE-09d.
- There is no "copy" button for the ticket number, no live character counter and no "sending" state on the submit button: they need script, and the Portal keeps every flow script-free (09d decides).
- The package's `JsonLd` component is unsafe for any text an author wrote (see SEO); the Portal does not use it for strings, and the problem is to be reported to `SyntaxCircus.Blazor.Seo`.
- A chunked post over the size limit is the framework's 400 about an antiforgery token, not a 413 (a browser form post always declares its length).
- A post to an unknown product is the framework's plain-text 400 ("Cannot submit the form 'contact' because no form on the page currently has that name."), not the 404 page a GET gets: the product page ends in `NotFound()` before the form is rendered, so there is no form to post to. Nothing is created. A post to a malformed or unknown ticket token is the same for the form `reply`, with an identical body, so nothing tells the two apart.
- A legacy-host redirect decodes percent-escapes in the query string (the package builds the target with `Uri.ToString()`), so a value that holds an encoded `&` or `#` changes meaning. It affects only hosts
  in `CANONICALHOST__LEGACYHOSTS`; report it upstream before using the redirect with the contact prefill.
- `NavigationManager.NotFound()` adds the framework's `blazor-enhanced-nav: allow` response header to an unknown product's 404, which the router's own unknown-route 404 does not carry. The bodies are identical,
  and both are 404, so it reveals nothing about which products exist.
- The Portal does not use `GlobalErrorBoundary`: its Try again button needs interactivity, and catching a render error in the page would answer 200 with a branded page instead of the plain 500 error page.
- Sentry has no general email rule: it masks the `name`, `email`, `subject` and `ref` query values and the `/t/{token}` path only (Serilog's email pattern does catch addresses in logs).
- If OpenTelemetry tracing were enabled, server spans would carry `url.path=/t/<token>`. It is off by default; masking it is a follow-up.
- A double click on "Send message" can send twice before the redirect arrives (there is no script to disable the button); the API creates a ticket for each (09d adds a guard).
