# Portal app (customers)

`TechStrap.Portal` is the public site customers use: a product's own help and support pages, with the product's name, logo and accent. It never touches the database: every page asks the TechStrap API,
anonymously. This page is for people who run it and people who extend it. How agents work the tickets customers send is in [ADMIN-APP.md](ADMIN-APP.md); the decisions behind the Portal are D-045 in the
[decision log](../architecture/04-DECISION-LOG.md) (with its 2026-10-06 addendum for 09b) and the spec is [PHASE-09](../architecture/PHASE-09-public-portal.md).

PHASE-09 is delivered in three pull requests. **09a** is the foundation: the settings, the API client, the per-product theme and shell, the product home, the root page, the ticket-page headers, robots.txt, log
redaction and the architecture rules. **09b** (this page describes both) adds the customer flows: the contact form with its suggestions and received page, the ticket page with replies and the Closed follow-up,
the attachment pass-through and the lost-link page. **09c** adds the knowledge base pages, the sitemap and the polish pass. The routes of all three are in `PortalRoutes`.

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
- **Copy** lives in `ShellCopy`, `ProblemCopy`, `FormCopy`, `ContactCopy`, `LostLinkCopy` and `TicketCopy`; routes in `PortalRoutes` (a page declares `@attribute [Route(PortalRoutes.XTemplate)]` and builds every link with a builder).

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

### The ticket page and attachments

`/t/{token}` (`Ticket.razor`) parses the token first (`TicketToken.TryParse`; a malformed one is the uniform 404 and the API is never asked), loads the ticket through `ICustomerTicketClient`, then the ticket's
product theme (an inactive or unknown product is the neutral theme, never a 404). `CustomerTicketPresenter` builds the view model: the status in the customer's words (New "Received", Open "In progress",
Pending "Waiting for your reply", Solved "Solved" with a note that a reply reopens it, Closed "Closed" with the note that a reply starts a follow-up), "You" for the customer's messages, the API's resolved name
for an agent exactly as it came, a neutral label for the system. **`CustomerMessageBody` is the single place the Portal turns text into markup** (`PortalRules.MarkupStringSites`): the API sanitises the message HTML.
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
lost link, attachment stream) and `IPublicKbClient` (search only; 09c extends it). `MultipartForm` builds the bodies (text fields first, one `Attachments` part per file, the file streams owned by the request).

### Headers, robots.txt and the canonical host

`UseTechStrapWebHost(PortalHeaderRules.Rules)` applies per-path rules after the shared security headers: everything under `/t` gets `Referrer-Policy: no-referrer`, `Cache-Control: no-store` and
`X-Robots-Tag: noindex`, only `/t/{token}/attachments/{id}` is sandboxed (so the ticket page keeps the normal CSP), and the four form pages of a product (`/p/{key}/contact`, `/contact/received`, `/lost-link` and
`/suggest`) get `no-store` and `noindex`. `/robots.txt` disallows `/t/` and names `{public url}/sitemap.xml`, which answers 404 until 09c. The canonical-host redirect is an allow-list of legacy hosts and does
nothing until configured.

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
  Clients/        ApiConnection, ProblemMapping and ProblemCopy, TicketToken, ApiClientRegistration (the two named clients), the typed clients (IPublicProductClient, IPublicTicketClient,
                  ICustomerTicketClient, IPublicKbClient), MultipartForm, AttachmentFileName, ApiQuery, ApiDownload
  Components/
    Layout/       PortalLayout, ProductHeader, ProductFooter
    Pages/        Home (the root), ProductHome, Contact, ContactReceived, Ticket, LostLink, NotFound, Error, StyleGuide (Development only)
    Tickets/      CustomerMessageBody (the one markup site), MessageThread, TicketStatusBanner
    Ui/           AccentScope, PoweredByFooter, ProductUnavailable, DevelopmentOnly, ErrorSummary, FieldError, FormField, AttachmentInput, HoneypotField
  Forms/          FormError and FormFields, FormCopy, FormFailure, AttachmentRules, EmailRules, ContactFormViewModel and its validator, ReplyForm, LostLinkForm, ContactCopy, ReceivedReference
  Headers/        PortalHeaderRules (the /t rules, the attachment sandbox and the form pages)
  Products/       ProductThemeViewModel, ProductScope, ProductPageBase
  Routing/        PortalRoutes, ProductKeyShape
  Seo/            PortalSeoRegistration (Blazor.Seo: base URL, robots.txt, canonical host)
  Settings/       PortalOptions and its validator
  Suggestions/    SuggestEndpoint (GET /p/{key}/suggest)
  Tickets/        CustomerTicketPresenter and its view models, TicketCopy, FollowUpLink, AttachmentPassThrough
  Uploads/        RequestTooLargeMiddleware
  Styles/         SCSS partials over the brand tokens (docs/BRAND.md)
  wwwroot/js/     kb-suggestions.js
```

Rules the code follows (the architecture tests check them): the Portal references **Contracts and Hosting only**; **components never inject `HttpClient`** (only `Clients/` mentions it); no inline script
or style; no interactive render mode; `MarkupString` is used in one file, `Components/Tickets/CustomerMessageBody.razor` (09c adds `KbArticleBody`, argued for in its commit); every plain-text DTO field is encoded.

## Tests

`tests/TechStrap.Portal.Tests` runs the host in memory with a stub API behind its two named clients (`PortalFactory.Api`, `StubApiHandler`), so a test sees what the API would see, including the headers the
Portal added. `ProxyHopStartupFilter` puts the host behind a trusted reverse proxy and every API-calling host test asserts the visitor's address with `AssertEveryCallBore`; `OkProbeStartupFilter` answers 200 on a
path no Portal route matches. A test that needs the real server (a request size limit) calls `UseKestrel(0)` and `StartServer()` and reads the address from `IServerAddressesFeature`. bUnit covers the layout.
`tests/TechStrap.Portal.Tests/js` holds the node tests of the browser module. The shared rules are in `TechStrap.Architecture.Tests` (`PortalRules`), the Hosting header-rule mechanism in `TechStrap.Api.Tests`
(`PathHeaderRuleHostTests`), and the log and Sentry redaction in `TechStrap.Api.Tests`.

## Known gaps

- `/robots.txt` names `/sitemap.xml`, which answers 404 until 09c maps it. The search box on the product home and the KB links answer 404 until 09c.
- There is no "copy" button for the ticket number, no live character counter and no "sending" state on the submit button: they need script, and 09b keeps every flow script-free (09c's polish pass decides).
- A chunked post over the size limit is the framework's 400 about an antiforgery token, not a 413 (a browser form post always declares its length).
- A post to an unknown product is the framework's 400 with no body (the not-found page is re-executed with the post and has no handler), not the 404 page a GET gets. Nothing is created.
- A legacy-host redirect decodes percent-escapes in the query string (the package builds the target with `Uri.ToString()`), so a value that holds an encoded `&` or `#` changes meaning. It affects only hosts
  in `CANONICALHOST__LEGACYHOSTS`; report it upstream before using the redirect with the contact prefill.
- `NavigationManager.NotFound()` adds the framework's `blazor-enhanced-nav: allow` response header to an unknown product's 404, which the router's own unknown-route 404 does not carry. The bodies are identical,
  and both are 404, so it reveals nothing about which products exist.
- The Portal does not use `GlobalErrorBoundary`: its Try again button needs interactivity, and catching a render error in the page would answer 200 with a branded page instead of the plain 500 error page.
- Sentry has no general email rule: it masks the `name`, `email`, `subject` and `ref` query values and the `/t/{token}` path only (Serilog's email pattern does catch addresses in logs).
- If OpenTelemetry tracing were enabled, server spans would carry `url.path=/t/<token>`. It is off by default; masking it is a follow-up.
- A double click on "Send message" can send twice before the redirect arrives (there is no script to disable the button); the API creates a ticket for each.
