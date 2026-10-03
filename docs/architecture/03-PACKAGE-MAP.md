# 03 - Package Map

Status: Draft for review. Versions verified against nuget.org (flat-container index) on 2026-10-02 and cross-checked with `dragon-poop` and `sinforgiver` `Directory.Packages.props`.

All **Selected** versions below are copied into `Directory.Packages.props` in [PHASE-01](PHASE-01-foundation.md) (central package management, `ManagePackageVersionsCentrally=true`). Catalog source: _template `PACKAGE_CATALOG.md`; integration patterns: the matching usage pages. Concrete APIs must be verified against the selected version before the owning phase implements them.

Related: [02-ARCHITECTURE.md](02-ARCHITECTURE.md), [04-DECISION-LOG.md](04-DECISION-LOG.md).

## 1. SyntaxCircus catalog packages

All releases are 0.x. Pin exact versions (no floating ranges).

| Concern | Status | Package | Exact version | Source/release verified | Purpose and boundary | Owning phase |
| --- | --- | --- | --- | --- | --- | --- |
| Operation results, current user, periodic background service | Selected | `SyntaxCircus.Common` | 0.1.3 | [0.1.3](https://www.nuget.org/packages/SyntaxCircus.Common/0.1.3) (0.1.4 is docs-only; `SyntaxCircus.AspNetCore.Common` 0.1.15 pins `[0.1.3]` exactly, so 0.1.4 fails restore with NU1107) | `Result`/`Result<T>`, `PagedResult<T>`, `ICurrentUserService`, `PeriodicBackgroundService` (worker loops). Used by Application, Infrastructure and all hosts. Carries no HTTP status. | P01 (referenced from P03/P04 on) |
| Dev `.env` loading | Selected | `SyntaxCircus.DotEnv` | 0.1.3 | [0.1.3](https://www.nuget.org/packages/SyntaxCircus.DotEnv/0.1.3) (dragon-poop 0.1.3) | `.env.local` loading in Development for every host. Not a secret store. Prereq: `.env.example` per host, `.env.local` gitignored. | P01 |
| API middleware, Result to ProblemDetails | Selected | `SyntaxCircus.AspNetCore.Common` | 0.1.15 | [0.1.15](https://www.nuget.org/packages/SyntaxCircus.AspNetCore.Common/0.1.15) (dragon-poop 0.1.15) | Correlation id, ProblemDetails, security headers, health, forwarded headers, rate-limit rejection shape, `ToActionResult`. Controllers select the success response; handlers stay transport-neutral. | P01 (health, middleware), P04 (mapping) |
| API authentication | Selected | `SyntaxCircus.AspNetCore.Authentication` | 0.1.5 | [0.1.5](https://www.nuget.org/packages/SyntaxCircus.AspNetCore.Authentication/0.1.5) (dragon-poop 0.1.5) | Agent OIDC JWT bearer and API-key authentication for intake (`Trusted`/`Public` kinds, hashed lookup via `IApiKeyHasher` + `IProductRepository`). Group-claim authorization policies are application-owned (D-029). Prereq: OIDC authority/audience env vars; `TECHSTRAP_AGENT_GROUP`, `TECHSTRAP_ADMIN_GROUP`, `TECHSTRAP_GROUP_CLAIM_TYPE`. Does not do Blazor token forwarding. | P04 |
| Structured logging | Selected | `SyntaxCircus.AspNetCore.Serilog` | 0.1.4 | [0.1.4](https://www.nuget.org/packages/SyntaxCircus.AspNetCore.Serilog/0.1.4) (dragon-poop 0.1.3; newer available) | Serilog host config for Api, Admin, Portal, Worker. Sinks/retention are deployment-owned. PII redaction (D-006) is project code. Prereq: logs volume mount. | P01 |
| OpenTelemetry + Sentry | Selected | `SyntaxCircus.Observability` | 0.1.2 | [0.1.2](https://www.nuget.org/packages/SyntaxCircus.Observability/0.1.2) (dragon-poop 0.1.1; newer available) | OTLP + Sentry bootstrap, feeds Serilog via `ConfigureSerilog`. Sampling/retention are deployment-owned. Prereq: OTLP headers and Sentry DSN come from deployment secrets only; both optional. | P01 |
| EF Core/Postgres conventions | Selected | `SyntaxCircus.EntityFrameworkCore.Postgres` | 0.1.3 | [0.1.3](https://www.nuget.org/packages/SyntaxCircus.EntityFrameworkCore.Postgres/0.1.3) (dragon-poop 0.1.3) | snake_case naming, UTC handling, migrate-on-startup with advisory lock (API only). Mappings and migrations are project-owned; migrations via `dotnet ef` only. Prereq: EF Core and Npgsql versions aligned (section 2). | P01 (wiring), P03 (mappings) |
| Transactional email | Selected | `SyntaxCircus.Email` | 0.1.6 | [0.1.6](https://www.nuget.org/packages/SyntaxCircus.Email/0.1.6) (sinforgiver 0.1.5) | `IEmailSender` over MailKit SMTP; Null sender for Development, in-memory sender for tests. Used only by the worker outbox drainer. Templates, outbox and per-product branding are project code (`IEmailTemplateRenderer`). Prereq: `Email__Smtp__*` env vars (Host, Port, credentials, `DefaultFrom`, `MaxRetryAttempts`) on the worker container; credentials from secrets. | P05 |
| File/blob storage | Selected | `SyntaxCircus.Storage` | 0.2.1 | [0.2.1](https://www.nuget.org/packages/SyntaxCircus.Storage/0.2.1) (sinforgiver 0.2.1) | `IStorageProvider` behind `IAttachmentStore` (ticket attachments, KB images). Local-disk provider in core. Authorization, size limit, type allowlist and retention are application-owned. Prereq: `Storage__Provider=Local`, `Storage__Local__RootPath=/app/storage` on a shared named volume mounted by both the API (intake, download, KB upload, the public-read `kb-images/` prefix served as static assets) and the Worker (cleanup), writable by uid 10001. `PublicBaseUrl` is only needed for `GetAccessUrlAsync`, which is not planned: downloads stream through `GetAttachmentRequestHandler`. S3 is a later config switch. | P05 |
| Resilient outbound HTTP | Selected | `SyntaxCircus.Http.Resilience` | 0.2.2 | [0.2.2](https://www.nuget.org/packages/SyntaxCircus.Http.Resilience/0.2.2) (sinforgiver 0.2.1) | Typed API clients in Admin and Portal (to the API). Per-client timeout/retry budgets; auth and idempotency stay caller-owned (no blind retry of non-idempotent POST). `TechStrap.Client` SDK uses plain `HttpClient` so consumers inherit no extra dependency (**Assumption**). | P07 (Admin), P09 (Portal) |
| Blazor token forwarding | Selected | `SyntaxCircus.Blazor.Auth` | 0.1.7 | [0.1.7](https://www.nuget.org/packages/SyntaxCircus.Blazor.Auth/0.1.7) (dragon-poop 0.1.7) | Admin (Blazor Server) forwards the agent's OIDC access token to the API and manages session refresh. Cookie/OIDC sign-in config is app-owned. Prereq: save tokens, request `offline_access`. Portal is anonymous and does not use it. | P07 |
| Blazor reusable UI | Selected | `SyntaxCircus.Blazor.Components` | 0.1.3 | [0.1.3](https://www.nuget.org/packages/SyntaxCircus.Blazor.Components/0.1.3) (dragon-poop 0.1.3) | Error boundaries, not-found, reconnect UI in Admin and Portal. Presentation only; no CSS, layout or authorization. | P07, P09 |
| Blazor SEO | Selected | `SyntaxCircus.Blazor.Seo` | 0.1.4 | [0.1.4](https://www.nuget.org/packages/SyntaxCircus.Blazor.Seo/0.1.4) (sinforgiver 0.1.4) | Meta tags, canonical, Open Graph and sitemap support for portal KB pages (`GetSitemapEntriesRequestHandler`). Portal only. Prereq: public base URL env var `TECHSTRAP_PORTAL_PUBLIC_URL`. Ticket pages (`/t/{token}`) must be `noindex`. | P09 |
| MAUI runtime environment switching | Not applicable | `SyntaxCircus.Maui.Environments` | n/a | [Source](https://github.com/Syntax-Circus/SyntaxCircus.Maui.Environments) (0.1.2 current; not selected) | `TechStrap.Client.Maui` is a **library** (device/app metadata capture and a submit helper) that takes a base URL and API key from the host app. The host app owns its environments, and referencing the package would force a dependency and policy on every consumer. The helper must not read or store environment state. Hosts needing runtime switching adopt the package themselves and pass the active URL in. | n/a |
| MAUI secure token storage | Not applicable | `SyntaxCircus.Maui.TokenStorage` | n/a | n/a | The helper stores no user session or token; the API key is host-supplied. | n/a |
| MAUI StoreKit / RevenueCat | Not applicable | `SyntaxCircus.Maui.StoreKit`, `SyntaxCircus.RevenueCat`, `SyntaxCircus.RevenueCat.Maui` | n/a | n/a | No subscriptions or purchases in TechStrap. | n/a |
| AI providers | Not applicable | `SyntaxCircus.AI.Providers` | n/a | n/a | No AI features in core scope. | n/a |
| Message correlation (MassTransit) | Not applicable | `SyntaxCircus.AspNetCore.Common.MassTransit` | n/a | n/a | No message broker. Asynchrony is the Postgres outbox + LISTEN/NOTIFY (D-007, D-010). | n/a |
| Analytics/consent | Not applicable | `SyntaxCircus.Blazor.Tracking` | n/a | n/a | No analytics in portal/admin for core. | n/a |
| Decorative Blazor effects | Not applicable | `SyntaxCircus.FancyBlazor` | n/a | n/a | Support UI is functional; see UX briefs. | n/a |
| Headless CMS client | Not applicable | `SyntaxCircus.Cmsify.Client`, `SyntaxCircus.Cmsify.Client.DistributedCaching` | n/a | n/a | The KB is built in (Markdown in Postgres, D-011/D-014), not Cmsify. | n/a |
| Local credential storage | Excluded | `SyntaxCircus.Credentials` | n/a | n/a | Targets local/desktop credential storage; `DotEnv` plus deployment secrets cover configuration. | n/a |

## 2. Notable third-party packages

Pin all Microsoft.* 10.0.x packages to the **same patch**. nuget.org latest stable is 10.0.12; dragon-poop and sinforgiver are on 10.0.11. Foundation selects 10.0.12 and verifies the SyntaxCircus packages restore against it (**Assumption**; fall back to 10.0.11 if a dependency floor conflicts).

| Package | Status | Exact version | Source/release verified | Purpose and boundary | Owning phase |
| --- | --- | --- | --- | --- | --- |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | Selected | 10.0.3 | [10.0.3](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL/10.0.3) (both repos 10.0.3; 11.x is prerelease) | EF Core provider; FTS (`tsvector`), jsonb, LISTEN/NOTIFY via Npgsql. Infrastructure only. | P01 / P03 |
| `Microsoft.EntityFrameworkCore` (+ `.Relational`, `.Design`) | Selected | 10.0.12 | [Design 10.0.12](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Design/10.0.12) | EF Core runtime and `dotnet ef` tooling (Design is `PrivateAssets=all`). | P01 |
| `Microsoft.AspNetCore.OpenApi` | Selected | 10.0.12 | [10.0.12](https://www.nuget.org/packages/Microsoft.AspNetCore.OpenApi/10.0.12) | `/openapi/v1.json` that the SDK builds against. | P01 |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | Selected | 10.0.12 (dragon-poop 10.0.11) | [10.0.12](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.OpenIdConnect/10.0.12) (verified latest stable 2026-10-02) | Admin OIDC sign-in (cookie + OIDC). | P07 |
| `Microsoft.AspNetCore.SignalR.Client` | Selected | 10.0.12 | [10.0.12](https://www.nuget.org/packages/Microsoft.AspNetCore.SignalR.Client/10.0.12) (sinforgiver 10.0.11) | Admin server-side hub connection (D-007). The hub itself ships in the shared framework. | P10 |
| `HtmlSanitizer` | Selected | 9.2.1039 | [9.2.1039](https://www.nuget.org/packages/HtmlSanitizer/9.2.1039) (10.x is beta-only) | Behind `IHtmlSanitizer`: sanitizes message bodies on write and KB HTML after rendering (D-014). Interface in Application, implementation in Infrastructure. | P05 |
| `Markdig` | Selected | 1.4.0 | [1.4.0](https://www.nuget.org/packages/Markdig/1.4.0) (dragon-poop 1.3.2) | Behind `IMarkdownRenderer`: KB Markdown to HTML, always followed by sanitization. | P08 |
| `AspNetCore.SassCompiler` | Selected | 1.105.1 | [1.105.1](https://www.nuget.org/packages/AspNetCore.SassCompiler/1.105.1) (dragon-poop 1.103.0) | Compiles Bootstrap 5 SCSS at build for Admin and Portal. No compiled CSS committed. | P01 (wiring), P02 (tokens) |
| `Microsoft.Web.LibraryManager.Build` | Selected | 3.0.114 | [3.0.114](https://www.nuget.org/packages/Microsoft.Web.LibraryManager.Build/3.0.114) (dragon-poop pin; verified latest stable 2026-10-02) | Restores Bootstrap SCSS sources via libman. | P01 |
| `GitVersion.MsBuild` | Selected | 6.8.2 | [6.8.2](https://www.nuget.org/packages/GitVersion.MsBuild/6.8.2) (dragon-poop 6.8.2) | Version stamping for images and SDK NuGet packages. `PrivateAssets=all`. | P01 |
| `xunit.v3` | Selected | 4.0.0 | [4.0.0](https://www.nuget.org/packages/xunit.v3/4.0.0) (both repos 4.0.0; 4.0.1 is latest) | Test framework. **NCrunch decision to verify:** no NCrunch config was found in dragon-poop or sinforgiver, so the installed NCrunch version is unknown. Per AGENT_GUIDE, 4.0.0 broke the xunit.v3 adapter on older NCrunch releases. Default to what dragon-poop uses (4.0.0); the owner confirms their NCrunch release includes the fix, otherwise pin `xunit.v3` 3.2.2 and `xunit.runner.visualstudio` 3.1.5 with a comment in `Directory.Packages.props` naming the NCrunch constraint. | P01 |
| `xunit.runner.visualstudio` | Selected | 4.0.0 | [4.0.0](https://www.nuget.org/packages/xunit.runner.visualstudio/4.0.0) (both repos 4.0.0) | IDE/VSTest runner; moves together with `xunit.v3` (NCrunch decision above). | P01 |
| `Microsoft.NET.Test.Sdk` | Selected | 18.10.1 | [18.10.1](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.10.1) (dragon-poop 18.9.0) | Test host. Drop if the MTP-only `global.json` setup in PHASE-01 makes it unnecessary. | P01 |
| `Shouldly` | Selected | 4.3.0 | [4.3.0](https://www.nuget.org/packages/Shouldly/4.3.0) (sinforgiver 4.3.0; 5.x is prerelease) | Assertions. | P01 |
| `NSubstitute` | Selected | 6.2.0 | [6.2.0](https://www.nuget.org/packages/NSubstitute/6.2.0) (sinforgiver 6.2.0) | Mocks for handler dependencies in Application tests. | P01 |
| `Testcontainers.PostgreSql` | Selected | 4.15.0 | [4.15.0](https://www.nuget.org/packages/Testcontainers.PostgreSql/4.15.0) (sinforgiver 4.14.0) | Real Postgres 17 for Infrastructure integration and API tests (FTS, SKIP LOCKED, LISTEN/NOTIFY cannot be faked). Requires Docker on dev and CI. | P01 (fixture), P03+ |
| `Microsoft.AspNetCore.Mvc.Testing` | Selected | 10.0.12 | [10.0.12](https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing/10.0.12) | `WebApplicationFactory` for `TechStrap.Api.Tests`. | P04 |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | Selected | 10.0.12 | [10.0.12](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer/10.0.12) | Agent JWT validation (via `SyntaxCircus.AspNetCore.Authentication`) and `JwtBearerEvents.OnMessageReceived` for the `/hubs/*` `access_token` (D-007). Api only. | P04, P10 |
| `Npgsql` | Selected | 10.0.3 | [10.0.3](https://www.nuget.org/packages/Npgsql/10.0.3) (matches the EF provider's dependency) | Direct use for the dedicated `LISTEN` connection (API listener) and `pg_notify` (Worker broadcaster). Infrastructure only. | P10 |
| `Microsoft.Extensions.Http`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options` | Selected | 10.0.12 | [Http](https://www.nuget.org/packages/Microsoft.Extensions.Http/10.0.12), [DI.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12), [Options](https://www.nuget.org/packages/Microsoft.Extensions.Options/10.0.12) | Runtime dependencies of the published `TechStrap.Client`; version ranges are a public compatibility decision (P11). | P11 |
| `Microsoft.Maui.Controls` (with Essentials) | Selected | 10.0.110 | [10.0.110](https://www.nuget.org/packages/Microsoft.Maui.Controls/10.0.110) | `TechStrap.Client.Maui` device and app metadata (`IAppInfo`, `IDeviceInfo`, `IConnectivity`, `IDeviceDisplay`). Needs the MAUI workload (macOS CI job). Align with the installed workload at P11. | P11 |
| `Microsoft.SourceLink.GitHub` | Selected | 10.0.401 | [10.0.401](https://www.nuget.org/packages/Microsoft.SourceLink.GitHub/10.0.401) | SourceLink for published packages. Bundled in the .NET SDK since .NET 8; reference explicitly only if the SDK-bundled version is insufficient. `PrivateAssets=all`. | P11 |
| `bunit` | Selected | 2.11.3 | [2.11.3](https://www.nuget.org/packages/bunit/2.11.3) (latest stable 2026-10-02) | Razor component tests in `TechStrap.Admin.Tests` and `TechStrap.Portal.Tests`. Test projects only. | P07, P08, P09 |
| `Microsoft.Extensions.TimeProvider.Testing` | Selected | 10.10.0 | [10.10.0](https://www.nuget.org/packages/Microsoft.Extensions.TimeProvider.Testing/10.10.0) (latest stable 2026-10-02; versioned separately from the 10.0.x runtime patch) | `FakeTimeProvider` for handler, worker, presence-TTL and debounce tests. Test projects only. | P03, P05, P10 |
| `Pester` (PowerShell Gallery module, not NuGet) | Selected | 6.2.0 | [6.2.0](https://www.powershellgallery.com/packages/Pester/6.2.0) (latest stable 2026-10-02) | `BuildScriptTests` for `Build-TechStrapDocker.ps1`. Installed in CI with `Install-Module`; not in `Directory.Packages.props`. | P01 |
| `Microsoft.Playwright` | Deferred (optional) | 1.63.0 | [1.63.0](https://www.nuget.org/packages/Microsoft.Playwright/1.63.0) (latest stable 2026-10-02) | Optional nightly end-to-end smoke tests (P07-T21, P09-T20). Add to the props file only if those tasks are taken. | P07, P09 |
| `dotnet-ef` (dotnet tool) | Selected | 10.0.12 | [10.0.12](https://www.nuget.org/packages/dotnet-ef/10.0.12) | Migrations and `has-pending-model-changes` checks; pinned in `.config/dotnet-tools.json`. Migrations are generated only with this tool. | P01 |
| `Microsoft.EntityFrameworkCore.InMemory` | Excluded | n/a | n/a | Behaves differently from Postgres; integration tests use Testcontainers. | n/a |

## 3. Boundaries and prerequisites summary

- **Layering:** `Email`, `Storage` and EF/Npgsql are consumed behind Application interfaces (`IEmailOutbox`/`IEmailOutboxStore` with an `IEmailSender` adapter, `IAttachmentStore`, repositories). Handlers never reference `DbContext`, `IStorageProvider`, `IEmailSender` or HTTP types (see 02-ARCHITECTURE).
- **Storage volume:** local provider root `/app/storage` is a named volume writable by uid 10001, mounted on API and Worker. Backups: `pg_dump` plus this volume (P12).
- **Email SMTP env vars:** `Email__Smtp__Host`, `__Port`, `__Username`, `__Password`, `__DefaultFrom`, `__MaxRetryAttempts`. Worker only. Development uses the Null sender; tests use the in-memory sender.
- **Auth env vars:** OIDC authority/audience, `TECHSTRAP_AGENT_GROUP`, `TECHSTRAP_ADMIN_GROUP`, `TECHSTRAP_GROUP_CLAIM_TYPE` (API); OIDC client id/secret (Admin).
- **Observability env vars:** OTLP endpoint/headers and Sentry DSN, all optional, secret-only.
- **Proxy and rate limiting:** forwarded headers and a pinned compose subnet per the _template `CLIENT_IP_RATE_LIMITING.md` pattern, using `AspNetCore.Common`.
- **Version policy:** nuget.org latest stable on 2026-10-02. SyntaxCircus packages are pre-1.0: re-verify the day PHASE-01 starts and update this table in the same change as `Directory.Packages.props`.
- **All versions were verified against nuget.org (and the PowerShell Gallery for Pester) on 2026-10-02.** Every package named by a PHASE document appears in this map.

## 4. Tools and container images (not NuGet packages)

Versions of these are pinned by the owning phase when it starts; they were not verified here.

| Item | Used for | Owning phase |
| --- | --- | --- |
| `postgres:17` | Database (compose, Testcontainers) | P01 |
| `mcr.microsoft.com/dotnet/aspnet:10.0` and `sdk:10.0` | Dockerfile base images | P01 |
| Mailpit image | Local and test SMTP capture | P05 |
| `NuGet/login@v1` GitHub Action | nuget.org Trusted Publishing (OIDC) | P11 |
| k6 | Load tests (`tests/load/`) | P12 |
| Trivy | Container image scan | P12 |

## 4b. Front-end libraries restored at build by libman (npm via jsdelivr)

These are npm packages, not NuGet packages, so `Directory.Packages.props` and `scripts/Check-PackageVersions.ps1` do not cover them (the table header deliberately has no `Package` or `Status` column). `scripts/tests/Libman.Tests.ps1` asserts that every `libman.json` library is pinned exactly, listed here with the same version, and restored only into a gitignored folder. Restored files are never committed, and the Docker build restores them again from jsdelivr (a build without network access fails loudly at the font assertion in `Dockerfile.admin` and `Dockerfile.portal`, never silently without fonts).

| Library | Used for | Exact version | Source/release verified | Owning phase |
| --- | --- | --- | --- | --- |
| `bootstrap` | SCSS base for Admin and Portal (`Styles/Vendor/bootstrap`, `scss/**` only) | 5.3.8 | [5.3.8](https://www.npmjs.com/package/bootstrap/v/5.3.8) via jsdelivr, restored in both apps by `Microsoft.Web.LibraryManager.Build` | P02 |
| `@fontsource/ibm-plex-sans` | IBM Plex Sans, weights 400, 500, 600, Latin subset, WOFF2 (`wwwroot/fonts/ibm-plex-sans`), self-hosted in Admin and Portal. SIL OFL 1.1; the package `LICENSE` is restored beside the files | 5.3.0 | [5.3.0](https://www.npmjs.com/package/@fontsource/ibm-plex-sans/v/5.3.0) via jsdelivr | P02 |
| `@fontsource/ibm-plex-mono` | IBM Plex Mono, weights 400, 500, 600, Latin subset, WOFF2 (`wwwroot/fonts/ibm-plex-mono`), Admin and Portal (ticket id). SIL OFL 1.1; `LICENSE` restored beside the files | 5.3.0 | [5.3.0](https://www.npmjs.com/package/@fontsource/ibm-plex-mono/v/5.3.0) via jsdelivr | P02 |
| `@fontsource-variable/source-serif-4` | Source Serif 4, variable weight and optical size (`opsz`), Latin subset, one WOFF2 (`wwwroot/fonts/source-serif-4`), Admin only (message bodies, brand-moment copy). SIL OFL 1.1; `LICENSE` restored beside the file | 5.3.0 | [5.3.0](https://www.npmjs.com/package/@fontsource-variable/source-serif-4/v/5.3.0) via jsdelivr | P02 |

## 5. Published by TechStrap

These are artifacts this repository produces, not dependencies.

| Artifact | Kind | Version | Owning phase | Notes |
| --- | --- | --- | --- | --- |
| `TechStrap.Contracts` | NuGet package | Lockstep GitVersion SemVer on tag `v*` | P11 | Public DTO surface; semver-stable after 1.0.0 (D-005, D-016). No MVC, EF or ASP.NET dependencies |
| `TechStrap.Client` | NuGet package | Lockstep with Contracts | P11 | Typed client over Contracts; depends on `SyntaxCircus.Http.Resilience` and `SyntaxCircus.Common` |
| `TechStrap.Client.Maui` | NuGet package | Lockstep with Contracts | P11 | Device and app metadata plus submit helper; MAUI workload build |
| `ghcr.io/syntax-circus/techstrap-api` | Container image | GitVersion SemVer and `latest` | P01 (script, workflows), P12 (v1.0.0) | `linux/amd64` and `linux/arm64` |
| `ghcr.io/syntax-circus/techstrap-admin` | Container image | as above | P01, P12 | |
| `ghcr.io/syntax-circus/techstrap-portal` | Container image | as above | P01, P12 | |
| `ghcr.io/syntax-circus/techstrap-worker` | Container image | as above | P01, P12 | |
