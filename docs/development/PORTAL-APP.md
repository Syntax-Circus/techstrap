# Portal app (customers)

`TechStrap.Portal` is the public site customers use: a product's own help and support pages, with the product's name, logo and accent. It never touches the database: every page asks the TechStrap API,
anonymously. This page is for people who run it and people who extend it. How agents work the tickets customers send is in [ADMIN-APP.md](ADMIN-APP.md); the decisions behind the Portal are D-045 in the
[decision log](../architecture/04-DECISION-LOG.md) and the spec is [PHASE-09](../architecture/PHASE-09-public-portal.md).

PHASE-09 is delivered in three pull requests. **09a** (this page describes it) is the foundation: the settings, the API client, the per-product theme and shell, the product home, the root page, the
ticket-page headers, robots.txt, log redaction and the architecture rules. **09b** adds the customer flows (contact form, ticket view and reply, lost link, attachments). **09c** adds the knowledge base
pages, the sitemap and the polish pass. The routes of all three are already in `PortalRoutes`.

## Run it locally

You need the API running (the root README starts it with Docker Compose, or run `src/TechStrap.Api`). With the Development seed (`TECHSTRAP_SEED_DEV_DATA=true`, see
[DEV-DATA.md](DEV-DATA.md)) the products `orbitly` and `paperplane` exist.

```bash
cp src/TechStrap.Portal/.env.example src/TechStrap.Portal/.env.local     # then edit the values below
dotnet run --project src/TechStrap.Portal --urls http://localhost:8082
```

`.env.local` is read in Development only and is git-ignored. Open `http://localhost:8082/p/paperplane` for the themed product home, `http://localhost:8082/p/nope` for the not-found page. With Docker Compose the
Portal listens on `http://127.0.0.1:8082` and compose sets `Api__BaseUrl` and `TECHSTRAP_PORTAL_PUBLIC_URL` for it.

### Configuration

The Portal refuses to start with a message that names the missing or malformed key. Every key is in `src/TechStrap.Portal/appsettings.json` with its default (D-043).

| Key | Required | Default | Meaning |
| --- | --- | --- | --- |
| `API__BASEURL` (`Api:BaseUrl`) | Yes | blank (the compose files set `http://api/`) | The address of the TechStrap API, absolute http or https |
| `TECHSTRAP_PORTAL_PUBLIC_URL` | Outside Development | blank | The Portal's public address as customers see it, absolute http or https, no query or fragment. The base of canonical URLs and robots.txt's sitemap line (`Seo:BaseUrl` is derived from it); the same value as the Api's key |
| `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` | No | blank | A product key. When set, `/` redirects (302) to `/p/<key>`; blank shows a neutral page with no product list |
| `TECHSTRAP_PORTAL_SHOW_POWERED_BY` | No | `true` | `false` hides the "Powered by TechStrap" line on every page. Any other value than `true` or `false` stops the start (D-024) |
| `CANONICALHOST__CANONICALHOST` | No | blank | The host to redirect legacy hosts to; blank turns the redirect off |
| `CANONICALHOST__LEGACYHOSTS__0` ... | No | none | The hosts that are redirected. Only these are; any other host is left alone |
| `CANONICALHOST__FORCEHTTPS`, `CANONICALHOST__PERMANENT` | No | `false`, `true` | https target; 301 (true) or 302 |
| `DATAPROTECTION__KEYRINGPATH`, `TRUSTEDPROXY__*`, `SECURITYHEADERS__*`, `SENTRY__*`, `OPENTELEMETRY__*`, `SERILOG__*` | No | see `appsettings.json` | Shared with the other hosts |

## How a page is served

Every page is static server-side rendering: there is no render mode, no circuit and no SignalR (`PortalRules.InteractivityViolations` fails the build of the architecture tests if one appears).

- **The product scope.** A page under `/p/{key}` derives from `ProductPageBase`. It loads the product once through `IPublicProductClient` and puts the result in the request's `ProductScope`; `PortalLayout`
  then wraps the page in the product's accent (`AccentScope`), a header (logo and name) and a footer. The scope is per request, so the not-found and error pages, which the host renders in a fresh scope, are
  never branded.
- **One answer for "no such product".** An unknown key, an inactive product and a key that is not a slug (`^[a-z0-9]+(-[a-z0-9]+)*$`, at most 40 characters) all call `NavigationManager.NotFound()`, so the visitor
  gets the same neutral 404 page as for an unknown route; a malformed key never reaches the API. `NeutralPagesGuardTests` pins it.
- **When the API fails** the page shows "This page could not be loaded." with a Try again link and a 503 (429 when the API is rate limiting), never a stack trace.
- **Branding is untrusted.** `ProductThemeViewModel` keeps the accent only if `ProductAccent.TryDerive` accepts it and the logo only if it is https (or http to `localhost` or `127.0.0.1` in Development, the same
  rule as the CSP's `img-src`). The product name is always encoded.
- **Copy** lives in `ShellCopy` and `ProblemCopy`; routes in `PortalRoutes` (a page declares `@attribute [Route(PortalRoutes.XTemplate)]` and builds every link with a builder).

### Talking to the API

`ApiConnection` (internal, `Clients/`) is the only place the Portal uses HTTP. It sends reads through a client that retries transport errors, 408 and 502 to 504 (twice, honouring `Retry-After` up to
2 seconds, no circuit breaker) and writes through a client that never retries. Both forward the visitor's address in `X-Forwarded-For` (`AddForwardedClientIp`; the API trusts it only from the compose subnet,
D-019) and have no logging handlers. `ProblemMapping` turns every answer into a `Result`: 400 keeps the API's field codes, 404 is one not-found whatever the API called it, 413 and 415 are the attachment
errors, 429 is rate limited, any 5xx or transport error is `api-unavailable`, with fixed sentences from `ProblemCopy`. A call made as a ticket's customer takes a `TicketToken` (43 base64url characters; it
prints as `[token]`), which becomes the `X-Ticket-Token` header of that request only. The ticket and KB clients arrive with their pages in 09b and 09c.

### Headers, robots.txt and the canonical host

`UseTechStrapWebHost(PortalHeaderRules.Rules)` applies per-path rules after the shared security headers: everything under `/t` gets `Referrer-Policy: no-referrer`, `Cache-Control: no-store` and
`X-Robots-Tag: noindex`, and only `/t/{token}/attachments/{id}` is sandboxed (so the ticket page keeps the normal CSP). `/robots.txt` disallows `/t/` and names `{public url}/sitemap.xml`, which answers 404
until 09c. The canonical-host redirect is an allow-list of legacy hosts and does nothing until configured.

### Logs and Sentry

The framework's request lines (path and query) are suppressed by default (`Microsoft.AspNetCore` is set to Warning); an operator who enables them gets them redacted. The PII enricher masks the access token in a `/t/{token}` address and the value of a `name` or `email` query parameter (the contact
page prefill, 09b), and the Sentry processors mask the same in URLs, headers, breadcrumbs and spans. `RequestLogRedactionHostTests` and `TicketTokenLeakTests` scan every level at Verbose.

## Where things live

```text
src/TechStrap.Portal/
  Clients/        ApiConnection, ProblemMapping and ProblemCopy, TicketToken, ApiClientRegistration (the two named clients), IPublicProductClient
  Components/
    Layout/       PortalLayout, ProductHeader, ProductFooter
    Pages/        Home (the root), ProductHome, NotFound, Error, StyleGuide (Development only)
    Ui/           AccentScope, PoweredByFooter, ProductUnavailable, DevelopmentOnly
  Headers/        PortalHeaderRules (the /t rules)
  Products/       ProductThemeViewModel, ProductScope, ProductPageBase
  Routing/        PortalRoutes, ProductKeyShape
  Seo/            PortalSeoRegistration (Blazor.Seo: base URL, robots.txt, canonical host)
  Settings/       PortalOptions and its validator
  Styles/         SCSS partials over the brand tokens (docs/BRAND.md)
```

Rules the code follows (the architecture tests check them): the Portal references **Contracts and Hosting only**; **components never inject `HttpClient`** (only `Clients/` mentions it); no inline script
or style; no interactive render mode; `MarkupString` is used nowhere yet (09b adds `CustomerMessageBody` and 09c `KbArticleBody`, each argued for in its commit); every plain-text DTO field is encoded.

## Tests

`tests/TechStrap.Portal.Tests` runs the host in memory with a stub API behind its two named clients (`PortalFactory.Api`, `StubApiHandler`), so a test sees what the API would see, including the headers the
Portal added. `ProxyHopStartupFilter` puts the host behind a trusted reverse proxy; `OkProbeStartupFilter` answers 200 on a path no Portal route matches. bUnit covers the layout. The shared rules are in
`TechStrap.Architecture.Tests` (`PortalRules`), the Hosting header-rule mechanism in `TechStrap.Api.Tests` (`PathHeaderRuleHostTests`), and the log and Sentry redaction in `TechStrap.Api.Tests`.

## Known gaps in 09a

- The product home links to pages that arrive later: Contact support (`/p/{key}/contact`, 09b), the footer's "Lost your ticket link?" (`/p/{key}/lost-link`, 09b) and the search box (`/p/{key}/kb/search`, 09c) answer 404 until then.
- `/robots.txt` names `/sitemap.xml`, which answers 404 until 09c maps it.
- A legacy-host redirect decodes percent-escapes in the query string (the package builds the target with `Uri.ToString()`), so a value that holds an encoded `&` or `#` changes meaning. It affects only hosts
  in `CANONICALHOST__LEGACYHOSTS`; report it upstream before using the redirect with the contact prefill.
- `NavigationManager.NotFound()` adds the framework's `blazor-enhanced-nav: allow` response header to an unknown product's 404, which the router's own unknown-route 404 does not carry. The bodies are identical,
  and both are 404, so it reveals nothing about which products exist.
- The Portal does not use `GlobalErrorBoundary`: its Try again button needs interactivity, and catching a render error in the page would answer 200 with a branded page instead of the plain 500 error page.
- Sentry has no general email rule: it masks the `name` and `email` query values and the `/t/{token}` path only (Serilog's email pattern does catch addresses in logs).
- If OpenTelemetry tracing were enabled, server spans would carry `url.path=/t/<token>`. It is off by default; masking it is a follow-up.
- A category named `suggest` would be unreachable once 09b serves `/p/{key}/kb/suggest`; 09b decides whether to reserve the slug.
