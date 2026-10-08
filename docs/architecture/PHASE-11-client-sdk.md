# PHASE-11: Client SDK

## Objective

Publish NuGet packages that let any company app submit tickets to TechStrap
with an API key:

- `TechStrap.Client`: typed HTTP client over `TechStrap.Contracts`, API-key header, `SyntaxCircus.Http.Resilience`.
- `TechStrap.Client.Maui`: MAUI helper that captures device/app metadata through MAUI Essentials and offers a submit-ticket helper on top of `TechStrap.Client`.
- `TechStrap.Contracts`: published as the dependency both packages share (see Architecture Decisions).

Delivery includes pack + publish to nuget.org by GitHub Actions on a version
tag, a runnable sample, and a README per package.

## Dependencies

- **Depends on:** [PHASE-05](PHASE-05-intake-email-worker.md) (the `POST /api/intake/tickets` contract, `SubmitTicketRequest/Response`, key kinds, the OpenAPI document), [PHASE-01](PHASE-01-foundation.md) (CI, `Directory.Packages.props`, GitVersion).
- **Unblocks:** [PHASE-12](PHASE-12-release-hardening.md). Can run in parallel with Phases 07–10.
- **External prerequisites:** nuget.org account/organization owning the `TechStrap.*` ID prefix (reserve IDs); NuGet **Trusted Publishing** policy for the GitHub repo/workflow (as used by SyntaxCircus.Maui.TokenStorage) and repository secret `NUGET_USER` (**Assumption**; API-key fallback `NUGET_API_KEY`); GitHub environment `release` with required reviewers; macOS runner for the MAUI workload.

### Corrections (D-047, 2026-10-07)

Where this page and D-047 differ, D-047 wins.
- **Delivery.** Three pull requests: 11a (T01, T02, T03, T04, T06, T10, T17: the SDK core and the packaging of Contracts and Client), 11b (MAUI, T07 to T09) and 11c (READMEs, samples, publish workflow, nuget.org, `v1.0.0-rc.1`; T11 to T16). 11b and 11c wait on owner actions #10 and #9.
- **JSON-only.** The intake endpoint takes `[FromBody] SubmitTicketRequest` (D-034). There is no `TicketAttachment` and no multipart; P11-T05 and the MAUI screenshot adapter are deferred to 11d, which first needs multipart intake. The surface is `SubmitTicketAsync(SubmitTicketRequest, CancellationToken)` (the SDK generates the key), `SubmitTicketAsync(SubmitTicketRequest, string idempotencyKey, CancellationToken)` (the caller's stable key) and `SubmitTicketOnceAsync`.
- **Resilience.** The SDK uses `HttpRequestResiliencePipeline` directly, not `AddResilientHttpClient`, `AddTypedClient` or `ApiClientBase`: the package's client registration retries every request, POST included. A submit with an `Idempotency-Key` is replayable; `SubmitTicketOnceAsync` sends once. There is no `retryCount`; the option is `MaxAttempts` (attempts in total).
- **Constant names.** `TechStrapHeaders` is `HeaderNames`; `TicketMetadataLimits` is `IntakeLimits`; `IntakeRoutes.Tickets` is new. `TicketMetadataKeys` is created in 11b.
- **Error codes** are `TechStrapClientErrorCodes` in `TechStrap.Client`, not Contracts constants.
- **429.** It is surfaced as `rate-limited` and not retried, and `Retry-After` is not carried: the server sends it, but `ResultError` has no slot for it. A 500 is not retried either, and an exhausted 408 is `api-unavailable`.
- **MAUI CI (11b).** `net10.0` and Android build on the existing runner; iOS builds only on a `v*` tag, on macOS.
- **Common.** `SyntaxCircus.Common` 0.2.0 is web-neutral (no `Microsoft.AspNetCore.App` framework reference); `ICurrentUserService` moved to `SyntaxCircus.AspNetCore.Common` 0.1.16.
- **Packaging.** `eng/Packaging.props` carries the pack metadata for Contracts and Client. `GitVersion.MsBuild`, the documentation file and a SourceLink package are deferred to 11c; a local pack passes `-p:Version=`.

### Corrections (D-048, 2026-10-07)

Where this page and D-048 differ, D-048 wins.
- **Target framework.** `TechStrap.Client.Maui` has a single `net10.0` target, not `net10.0-android;net10.0-ios;net10.0`. There is no MAUI workload, no `UseMaui` and no macOS runner (owner action #10 is no longer needed).
- **Essentials, not Controls.** The package depends on `Microsoft.Maui.Essentials` 10.0.0 (plus `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options`, and the `TechStrap.Client` and `TechStrap.Contracts` references), not on `Microsoft.Maui.Controls`.
- **`TicketMetadataKeys`** is created in `TechStrap.Contracts` (13 default keys and 6 extras), with `IntakeLimits.MaxMetadataJsonLength`.
- **Submit helper.** `IMauiTicketSubmitter.SubmitAsync(MauiTicketDraft, CancellationToken)` takes a draft record; there is no `FileResult` or `TicketAttachment` parameter until 11d adds attachments.
- **Extras are flags.** Display and battery metadata are `DeviceContextOptions.IncludeDisplay` and `IncludeBattery` (both off by default), not a list of extras.
- **The collector.** `IDeviceContextCollector.Collect()` is synchronous and never throws; a field that fails is skipped.
- **Registration.** `AddTechStrapMaui` has two overloads: one that needs `AddTechStrapClient` first, and one that takes the client options and calls it.
- **Lazy defaults.** The Essentials defaults are registered as factories, so nothing reads `AppInfo.Current` and the like at registration; an app's own registration wins.
- **MAUI CI.** D-047's MAUI CI bullet (Android on the existing runner, iOS on macOS on a tag) is withdrawn: the one `net10.0` target builds and tests on the ubuntu CI.
- **OpenAPI contract test.** The Api documents no response schemas, so the test pins the request, the security scheme and the `Idempotency-Key` parameter, not the 201 body (a PHASE-05 follow-up).

### Corrections (D-049, 2026-10-08)

Where this page and D-049 differ, D-049 wins.
- **Version from the tag.** The packages take their version from the release tag (`dotnet pack -p:Version=<tag>`), not from `GitVersion.MsBuild`; the hosts keep GitVersion. There is no `Microsoft.SourceLink.GitHub` package: SourceLink is bundled in the SDK.
- **When the workflow runs.** `publish-nuget.yml` runs on a `v*` tag push and on `workflow_dispatch`; it has no `pull_request` trigger (`ci.yml` dry-runs the pack on pull requests). A dispatch is a dry run: it packs and validates and publishes nothing. A new workflow can only be dispatched once it is on `main`.
- **Publishing.** NuGet Trusted Publishing only: there is no `NUGET_API_KEY` fallback. The GitHub Release is created by `publish-nuget.yml` (with generated notes, as a prerelease when the version has a hyphen), not by `release.yml`, which still pushes the GHCR images.
- **README snippets** are compiled `#region readme:*` blocks (in the console sample and in `tests/TechStrap.Client.Maui.Tests`), and `scripts/tests/ReadmeSnippets.Tests.ps1` checks that each README block matches its region. There is no MAUI sample project.
- **Documentation file.** `GenerateDocumentationFile` is on for every package, every public member is documented, and `Test-PackageContents.ps1` fails when `lib/net10.0/<id>.xml` is missing.
- **AOT.** `TechStrap.Client` and `TechStrap.Client.Maui` are AOT-compatible: JSON goes through the source-generated `TechStrapJsonContext`, and `IsAotCompatible` is on with no suppressions.
- **T16 validation.** UAT is not deployed, so the post-publish check runs against the local compose stack; the UAT submit moves to PHASE-12 (P12-T14).

## Architecture Decisions

- **Three packages, not two.** `TechStrap.Client` depends on `TechStrap.Contracts`, so Contracts must be a public package (`TechStrap.Contracts`; DTOs and constants only, no inward project references). The plan listed two; this is a necessary addition (see Risks). All three share one lockstep version from GitVersion and the `v*` tag (**Assumption**; independent versioning rejected for simplicity).
- **SDK surface (minimal, create-only):** `ITechStrapClient.SubmitTicketAsync(SubmitTicketRequest, IReadOnlyList<TicketAttachment>?, CancellationToken)` -> `Result<SubmitTicketResponse>` (ticket number + view URL) over `POST /api/intake/tickets` (`SubmitTicketRequestHandler`, P05), sending an `Idempotency-Key` per call (generated, or supplied by the caller; D-020). No read/list operations: API keys are create-only by design ([01-REQUIREMENTS.md](01-REQUIREMENTS.md)).
- **Auth:** key sent in a header handled by a `DelegatingHandler` (`ApiKeyHandler`), per Http.Resilience guidance (auth is caller-owned). Header names are constants in Contracts shared with the API (`TechStrapHeaders`: `X-Api-Key`, matching the P05/`SyntaxCircus.AspNetCore.Authentication` API-key scheme, and `Idempotency-Key`). The key never appears in URLs, logs or exception messages.
- **Trust kinds:** `TechStrapClientOptions.KeyKind`-agnostic: the server decides trust by key kind. The SDK exposes `ExternalUserRef` and `Metadata` on the request; for **Public** keys the server flags metadata untrusted and ignores `ExternalUserRef` (documented in READMEs: use a Trusted key only from server-side code, never embed it in apps).
- **Resilience:** registered with `AddResilientHttpClient("techstrap", …)` and `AddTypedClient<TechStrapClient>()`. Ticket creation is not naturally idempotent, so submit retries only when an `Idempotency-Key` is present (D-020; server side in P05-T17): the SDK sets one per call, reuses the same key across attempts, and enables retries for submit only in that case; with the key unset (or if D-020 is rejected) `retryCount` stays 0. The circuit breaker and timeout remain. Timeout default 30 s (attachments), configurable.
- **Results over exceptions:** expected failures (400/422 validation with field errors, 401/403 bad key, 413/415 attachment limits, 429 rate-limited with `RetryAfter`, 5xx) map to `Result` failures with stable error codes from Contracts constants; only programmer errors (null args, disposed) throw. Package `SyntaxCircus.Common` is therefore a public dependency (**Assumption**).
- **Target frameworks:** `TechStrap.Contracts` and `TechStrap.Client`: `net10.0` (**Assumption**; multi-target to `net8.0` is a post-1.0 consideration). `TechStrap.Client.Maui`: `net10.0-android;net10.0-ios;net10.0` mirroring `SyntaxCircus.Maui.TokenStorage`; the plain `net10.0` target holds the platform-neutral abstractions so unit tests run without a device.
- **MAUI metadata capture** (`IDeviceContextCollector`, default `MauiDeviceContextCollector`): reads through injectable Essentials abstractions (`IAppInfo`, `IDeviceInfo`, `IConnectivity`, `IDeviceDisplay`) so it is testable. Default fields (keys are constants in `TechStrap.Contracts.TicketMetadataKeys`): `app.name`, `app.version`, `app.build`, `app.package`, `os.platform`, `os.version`, `device.manufacturer`, `device.model`, `device.idiom`, `device.type` (physical/virtual), `locale`, `timezone`, `network.access`. Opt-in extras: display size/density/orientation, battery state. **Never collected:** advertising/device unique ids, location, contacts, IP. Collection is explicit via `IncludeDeviceContext` (default true; apps can disable or supply a redaction callback). Values are truncated to `TicketMetadataLimits` constants (shared with the server's validation).
- **MAUI submit helper:** `IMauiTicketSubmitter.SubmitAsync(subject, message, requesterEmail, requesterName?, attachments?, ct)` merges collected context + app-supplied extra metadata into `SubmitTicketRequest.Metadata`, adapts `FileResult` (e.g. screenshots from `MediaPicker`) to `TicketAttachment`, and calls `ITechStrapClient`. DI: `AddTechStrapClient(...)` and `AddTechStrapMaui(...)` extension methods; no static singletons.
- **Public-key guidance:** MAUI apps embed a **Public** key (extractable by design). Rate-limiting and untrusted metadata are the server's protections (P05); the SDK README states this explicitly and recommends `Maui.Environments` (see below) for non-prod keys.
- **Packaging reference:** follow `SyntaxCircus.Maui.TokenStorage` for csproj pack metadata (`PackageId`, `Authors`/`Company` "Syntax Circus LLC", `PackageLicenseExpression` MIT, `PackageReadmeFile`, `PackageProjectUrl`/`RepositoryUrl`, `PublishRepositoryUrl`, `IncludeSymbols` + `snupkg`, `GitVersion.MsBuild` private asset) and its `build.yml` (build + test on macOS, `dotnet pack`, upload artifact, then an `environment: release` job using `NuGet/login@v1` OIDC and `dotnet nuget push --skip-duplicate`). Two deliberate differences: publishing is triggered by **tag `v*`** (not every push to main), and a local `publish.ps1`-style dry-run script is optional (**Assumption**).
- **Sample and docs:** `samples/TechStrap.Client.Samples.Console` (server-side Trusted-key submit, built in CI); MAUI usage shown as a README snippet plus an optional `samples/TechStrap.Client.Samples.Maui` project built only on the macOS job (**Assumption**). Samples use project references locally and `PackageReference` against the packed artifacts in the release-verification step.
- **Contract safety:** an integration test runs the SDK against the real API (`WebApplicationFactory` + Testcontainers) and a contract test compares the SDK's request model with the published `/openapi/v1.json` operation `POST /api/intake/tickets`.

## Application Boundaries

Follow _template APPLICATION_ARCHITECTURE.md. This phase adds **no server-side
entry points and no handlers**. The SDK is a client of the Phase 05 intake entry
point.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| Consumed: `POST /api/intake/tickets` (API key, multipart) | `SubmitTicketRequestHandler` (P05, unchanged) | SDK: `ITechStrapClient`, `IMauiTicketSubmitter`, `IDeviceContextCollector` (client-side abstractions) | `TechStrapClient : ApiClientBase` (HTTP) with `ApiKeyHandler`; `MauiDeviceContextCollector` | 201 -> `Result<SubmitTicketResponse>`; 400/422 -> validation failure with field errors; 401/403 -> `InvalidApiKey`; 413/415 -> attachment failures; 429 -> `RateLimited(RetryAfter)`; 5xx/network -> `Unavailable` | Consumed; no new server entry point |
| Server-side changes | None | None | None | None | Idempotency support is P05-T17 (D-020); any other contract change is amended in P05 via its own PR, not here |
| CI/CD release workflow | Exempt | GitHub Actions | `.github/workflows/publish-nuget.yml` | Pipeline status | Not a server application entry point |

## Razor Component Boundaries

Follow _template RAZOR_COMPONENT_ARCHITECTURE.md. The SDK ships **no Razor
components**; the table records that decision and the sample's UI choice.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| `TechStrap.Client` / `TechStrap.Client.Maui` packages | N/A: no Razor components in either package (non-visual libraries; no feedback form UI shipped) | N/A | N/A | Public surface uses `SubmitTicketRequest`/`Response`/`TicketAttachment` and `Result`; no ViewModels |
| Console sample | N/A: no UI | N/A | N/A | Uses Contracts DTOs directly |
| MAUI sample (optional) | If implemented as MAUI XAML: out of scope of the Razor guide. If a MAUI Blazor Hybrid page is chosen later, it must follow the paired `.razor`/`.razor.cs` rule with a feature-local `FeedbackFormViewModel` mapped to `SubmitTicketRequest` in code-behind | `FeedbackFormViewModel` (sample only, feature-local) | Submitting/Success/Error | `SubmitTicketRequest` |

## Syntax Circus Packages

Versions in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md). These are **runtime dependencies of the published packages**, so version ranges are also a public compatibility decision.

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.Http.Resilience` | `ApiClientBase`, retry/circuit breaker, ProblemDetails | Typed client + resilience pipeline | Unit tests: no retry on POST, circuit breaker opens after repeated 503, ProblemDetails -> `Result` failure |
| `SyntaxCircus.Common` | `Result`/`Result<T>` | SDK return type | Unit tests |
| `SyntaxCircus.Maui.TokenStorage` | (Not a dependency) | Reference for packaging layout only; apps wanting a stable install id may use its `InstallationIdentity` and pass it as metadata | README note only |
| `SyntaxCircus.Maui.Environments` | (Not a dependency) | Apps use it to switch API base URL/key per environment (see `docs/patterns/MOBILE_RUNTIME_ENVIRONMENT_SWITCHING.md`); SDK exposes `TechStrapClientOptions` that can be rebuilt per environment | README note only |
| `SyntaxCircus.RevenueCat.Maui` | (Not applicable) | Packaging reference only | N/A |

Third-party: `Microsoft.Extensions.Http`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Maui.Controls`/Essentials, `GitVersion.MsBuild`, `Microsoft.SourceLink.GitHub` (SDK includes it by default in .NET 8+). All are pinned in the package map.

## Deliverables

- [x] `src/TechStrap.Contracts` pack metadata + README; `src/TechStrap.Client` and `src/TechStrap.Client.Maui` projects with pack metadata + README each. *(11a: Contracts and Client; 11b: `TechStrap.Client.Maui`, on a single `net10.0` target.)*
- [ ] `ITechStrapClient`, options, DI extension, `ApiKeyHandler`, `Result` mapping, attachment type. *(11a: all but the attachment type, deferred by D-047.)*
- [x] `IDeviceContextCollector`, `MauiDeviceContextCollector`, `IMauiTicketSubmitter`, `AddTechStrapMaui`. *(11b.)*
- [x] `tests/TechStrap.Client.Tests` (unit + API-contract integration), created in this phase (listed in `02-ARCHITECTURE.md`). *(Created in 11a.)*
- [x] Console sample (+ optional MAUI sample); README usage snippets. *(11c: the console sample and compiled README snippets; no MAUI sample project, D-049.)*
- [x] `.github/workflows/publish-nuget.yml` and package validation (readme/license/symbols/dependency check), dry-run on non-tag builds. *(11a delivered the pack dry run in `ci.yml` and `scripts/Test-PackageContents.ps1`; 11c added the workflow.)*
- [ ] First prerelease (`v1.0.0-rc.1`) published and consumed from nuget.org by the sample. *(Pending the owner's tag push, P11-T16.)*

## Actionable Tasks

- [x] **P11-T01** Add shared SDK constants to Contracts (`TechStrapHeaders` created in P05-T01, `TicketMetadataKeys`, `TicketMetadataLimits`, error code constants) and confirm the header names and limits against P05's implementation
  - **Depends on:** P05
  - **Validation:** API uses the same constants (compile-time); Api.Tests assert the header name and limits are enforced; no duplicate literals in Api/Client (grep/architecture test).
  - **As built (11a):** `HeaderNames` and `IntakeLimits` already existed (the spec's `TechStrapHeaders` and `TicketMetadataLimits`); `IntakeRoutes.Tickets` is new and the controller uses it. `WireLiteralRules` allows the four quoted wire literals in `src/` only in `HeaderNames.cs`, `IntakeRoutes.cs` and the named Sentry exemption. Error codes live in the Client (`TechStrapClientErrorCodes`); `TicketMetadataKeys` is 11b.
- [x] **P11-T02** Create `TechStrap.Client` project with pack metadata modeled on `SyntaxCircus.Maui.TokenStorage.csproj`; add `PackageReadmeFile`, SourceLink, snupkg, `GitVersion.MsBuild`, `EnablePackageValidation`
  - **Depends on:** P11-T01, P01
  - **Validation:** `dotnet pack` produces `.nupkg` + `.snupkg`; unzip shows README, MIT license expression, repository URL, dependency list exactly `TechStrap.Contracts`, `SyntaxCircus.Http.Resilience`, `SyntaxCircus.Common`, `Microsoft.Extensions.*`.
  - **As built (11a):** `eng/Packaging.props` (imported by Contracts and Client only) carries the metadata; `GitVersion.MsBuild`, the documentation file and a SourceLink package are deferred to 11c. The dependency set is pinned by an architecture rule and checked in CI by `scripts/Test-PackageContents.ps1`: Contracts has none; Client has the six ids (Contracts, `SyntaxCircus.Http.Resilience`, `SyntaxCircus.Common` and three `Microsoft.Extensions.*`).
- [x] **P11-T03** Implement `TechStrapClientOptions` (BaseAddress, ApiKey, Timeout) with validation and `ApiKeyHandler` (header injection; key never logged)
  - **Depends on:** P11-T02
  - **Validation:** Unit tests: missing/invalid base URL or key fails on first use with a clear message; handler sets the header; log/exception capture never contains the key.
  - **As built (11a):** `TechStrapClientOptions` also has `MaxAttempts`, `RetryBaseDelay` and `MaxRetryDelay`; an internal validator fails with `OptionsValidationException` on first use (no `ValidateOnStart`) and never echoes the key. `ApiKeyHandler` replaces a caller-set header and refuses a request to another authority; the named client has no logging handlers and no redirects.
- [x] **P11-T04** Implement `ITechStrapClient`/`TechStrapClient.SubmitTicketAsync` (multipart with attachments, cancellation, `Result` mapping, `RetryAfter` parsing) and `AddTechStrapClient` DI extension using `AddResilientHttpClient`; submit retries only when an `Idempotency-Key` is set (D-020), otherwise `retryCount: 0`
  - **Depends on:** P11-T03
  - **Validation:** Stub-handler tests: 201 -> response mapped; 400 field errors; 401/403; 413/415; 429 with `Retry-After`; 503 -> `Unavailable`; without a key the call is not retried (handler invoked once), with a key it is retried with the identical `Idempotency-Key` on every attempt; circuit breaker opens after N failures; cancellation aborts the request.
  - **As built (11a):** JSON only, no attachments; `HttpRequestResiliencePipeline` replaces `AddResilientHttpClient`; three methods (`SubmitTicketAsync` with a generated key, `SubmitTicketAsync` with the caller's key, `SubmitTicketOnceAsync`) instead of `SubmitOptions`. Host-wide `ConfigureHttpClientDefaults` handlers are removed from the SDK's named client (they would retry a call that has no key); a handler of your own goes on `TechStrapClientDefaults.HttpClientName` after `AddTechStrapClient`. A 429 maps to `rate-limited` without a retry and without `Retry-After` (no slot in `ResultError`); 500 is not retried; an exhausted 408 or a 5xx is `api-unavailable`. The circuit opens when at least half of the last 5+ calls in a 30 s window failed (the package defaults) and stays open 30 s; there is one circuit per DI container (per `TechStrapClient` singleton).
- [ ] **P11-T05** (deferred, D-047) Add `TicketAttachment` helpers (stream, file name, content type; guard against disposed streams; pre-check size/type against Contracts limits). The SDK v1 is JSON-only; this waits for multipart intake (11d).
  - **Depends on:** P11-T04
  - **Validation:** Unit tests: oversize/disallowed type fail locally with the same error codes as the server without sending the request.
- [x] **P11-T06** Create `tests/TechStrap.Client.Tests` with an integration test hosting the API (`WebApplicationFactory` + Testcontainers.PostgreSql) and a contract test against `/openapi/v1.json`
  - **Depends on:** P11-T04
  - **Validation:** SDK submits a ticket to the in-test API with a Public and a Trusted key; ticket exists with expected channel/metadata trust flag; OpenAPI contract test fails if the operation, request fields or security scheme drift.
  - **As built (11a):** 162 tests, unit and `[Trait("Integration","Docker")]`. The test project references `src/TechStrap.Api` and links the `Api.Tests` helpers. The contract test pins the POST operation, the `application/json` request properties, the `ApiKey` scheme and the `Idempotency-Key` parameter; the Api documents no response schemas, so the 201 body is pinned by the real-API tests instead (known gap, a PHASE-05 follow-up).
- [x] **P11-T07** Create `TechStrap.Client.Maui` project (net10.0-android;net10.0-ios;net10.0) with pack metadata and README; define `IDeviceContextCollector`, options (`IncludeDeviceContext`, extras, redaction callback)
  - **Depends on:** P11-T02
  - **Validation:** `dotnet workload restore` + build on macOS for all targets; pack succeeds; plain `net10.0` target compiles without MAUI platform APIs.
  - **As built (11b):** A single `net10.0` target (D-048), so there is no workload restore and no macOS build. The package depends on `Microsoft.Maui.Essentials` 10.0.0; `ClientMauiRules` pins the package set and rejects `UseMaui`, and the CI pack dry run packs all three packages and checks the five nuspec dependencies.
- [x] **P11-T08** Implement `MauiDeviceContextCollector` over injected Essentials abstractions with truncation to `TicketMetadataLimits` and the never-collect list
  - **Depends on:** P11-T07, P11-T01
  - **Validation:** Unit tests with NSubstitute for `IAppInfo`/`IDeviceInfo`/`IConnectivity`/`IDeviceDisplay`: exact key set emitted by default; extras only when enabled; redaction callback applied; long values truncated; no key outside `TicketMetadataKeys`.
  - **As built (11b):** `MauiDeviceContextCollector` takes `IAppInfo`, `IDeviceInfo`, `IConnectivity`, `IDeviceDisplay`, `IBattery` and the options; it emits the 13 default keys (display and battery only when enabled), reads each field in its own try/catch and never throws. 14 collector tests cover the key set, truncation, redaction, culture and failing accessors.
- [x] **P11-T09** Implement `IMauiTicketSubmitter` (`SubmitAsync`, `FileResult` -> `TicketAttachment` adapter) and `AddTechStrapMaui` DI extension
  - **Depends on:** P11-T08, P11-T04
  - **Validation:** Unit tests: collected context + app metadata merged with app-supplied values winning only for non-reserved keys; disabling context sends no `device.*` keys; `FileResult` adapter streams and disposes correctly; DI resolves the full graph.
  - **As built (11b):** `SubmitAsync(MauiTicketDraft, CancellationToken)` merges the collected context with the app's metadata (reserved keys ignored) and sends through `ITechStrapClient`; a draft that breaks the metadata limits fails locally with `metadata-invalid`. There is no `FileResult` adapter (attachments wait for 11d). `AddTechStrapMaui` has two overloads and registers the Essentials defaults lazily; 9 registration tests resolve the graph without a device.
- [x] **P11-T10** Add `TechStrap.Contracts` pack metadata + README (and verify it has no dependency on non-public projects)
  - **Depends on:** P11-T01
  - **Validation:** Pack output dependency list contains only framework/third-party packages; consumer sample restores with Contracts only transitively through Client.
  - **As built (11a):** the Contracts nuspec has zero dependencies; CI packs Contracts and Client with `-p:Version=0.0.0-ci` and `scripts/Test-PackageContents.ps1` checks README, license, repository URL, symbols and the exact dependency set. The consumer sample is 11c.
- [x] **P11-T11** Write per-package READMEs (what it is, install, minimal example, configuration, Trusted vs Public key guidance, error handling, privacy of collected metadata, versioning/compat)
  - **Depends on:** P11-T04, P11-T09
  - **Validation:** Every README code block is compiled from the samples (snippets copied by a CI check or `#region` extraction); links resolve; README present in each nupkg.
  - **As built (11c):** full READMEs for `TechStrap.Contracts`, `TechStrap.Client` (error-code table, Trusted vs Public keys, retries and idempotency, configuration, compatibility) and `TechStrap.Client.Maui` (19-key table, platform permissions, privacy, limits). The register, submit and errors snippets are compiled `#region readme:*` blocks, and `scripts/tests/ReadmeSnippets.Tests.ps1` checks each README block against its region. The XML documentation file is enabled for every package (`GenerateDocumentationFile`); 154 undocumented public members in Contracts were documented, and `Test-PackageContents.ps1` fails when the `.xml` file is missing.
- [x] **P11-T12** Build `samples/TechStrap.Client.Samples.Console` (and optional MAUI sample) using the SDK against a local compose stack
  - **Depends on:** P11-T04, P11-T09
  - **Validation:** Running the console sample against `docker compose up` prints a ticket number and view URL; the ticket appears in admin; MAUI sample (if built) compiles on macOS.
  - **As built (11c):** `samples/TechStrap.Client.Samples.Console` (in `TechStrap.slnx` and `TechStrap.CI.slnf`, built but not a test) reads `--base-address` and `--api-key` or the `TECHSTRAP__*` environment variables and calls `AddTechStrapClient`. It exits 0 with `Ticket <n>` and `View: <url>`, 1 with an SDK failure `<code>: <message>`, 2 for a configuration problem, and never prints the key. A live run against the compose stack printed `ORB-7` and its view URL (exit 0); a wrong key gave `invalid-api-key` (exit 1) and no key gave exit 2. There is no MAUI sample project (D-049); the MAUI snippets compile in `tests/TechStrap.Client.Maui.Tests`. The sample's one new central pin is `Microsoft.Extensions.Hosting` 10.0.12. `docs/development/CLIENT-SDK.md` has a "Running the sample" section.
- [x] **P11-T13** Add `.github/workflows/publish-nuget.yml`: on push to any branch/PR build+test+pack (artifact, no publish); on tag `v*` verify tag == packed version, then publish via `NuGet/login@v1` OIDC in the `release` environment with `--skip-duplicate`, create a GitHub Release for the tag
  - **Depends on:** P11-T02, P11-T07, P11-T10, P01 CI
  - **Validation:** Dry run on a branch produces three `.nupkg` + `.snupkg` artifacts; a test tag `v0.0.0-test.1` on a fork/branch (publish step guarded) passes the version-match check; prerelease tags publish as prerelease only.
  - **As built (11c):** the workflow triggers on a `v*` tag push and `workflow_dispatch`, with no `pull_request` trigger. The `pack` job takes the version from the tag (a tag that is not `v<semver>` fails; a dispatch uses `0.0.0-dryrun.<run_number>`), runs `dotnet test --solution TechStrap.CI.slnf`, packs the three packages with `-p:Version`, runs `Test-PackageContents.ps1` and uploads the packages. The `publish` job runs only for `github.ref_type == 'tag' && github.event_name == 'push'`, needs `pack`, uses the `release` environment with `id-token: write`, checks the file-name versions, logs in with `NuGet/login@v1` (`secrets.NUGET_USER`), pushes with `--skip-duplicate` and runs `gh release create --generate-notes` (`--prerelease` when the version has a hyphen). `scripts/tests/PublishWorkflow.Tests.ps1` pins it. GitHub cannot dispatch a workflow that is not on `main`, so the dry run happens after the merge, before the first tag (see `docs/development/RELEASING.md`).
- [x] **P11-T14** Add package content validation script (CI step): README present, license metadata, symbol packages, expected dependency set, no secrets, size sanity
  - **Depends on:** P11-T13
  - **Validation:** CI fails when a README or dependency is missing (verified with a deliberately broken pack in a draft PR).
  - **As built (11c):** `scripts/Test-PackageContents.ps1` came with 11a; 11c adds the check for `lib/net10.0/<id>.xml` (fixture-tested) and runs the script with the three-package map in both `ci.yml` (pack dry run) and `publish-nuget.yml`. The script checks the README, license, repository URL, dependency set, symbol package and xml documentation. It does NOT check for secrets or package size, so those two items of the task text remain open (PHASE-12 or a later 11c follow-up).
- [x] **P11-T15** Reserve package IDs and configure nuget.org Trusted Publishing policy + `release` environment; document the one-time setup in the repo (not secrets)
  - **Depends on:** P11-T13
  - **Validation:** Owner confirms IDs and policy; a prerelease tag publishes all three packages without a stored API key.
  - **As built (11c):** the owner confirmed on 2026-10-08 that the `TechStrap.*` IDs are reserved, each package has a Trusted Publishing policy (repository `Syntax-Circus/techstrap`, workflow `publish-nuget.yml`, environment `release`), `NUGET_USER` is set as an organization secret available to the repository (not a repository secret), and the `release` environment has a required reviewer and a deployment rule "Selected branches and tags" with the tag pattern `v*` ("Protected branches only" blocks tag refs). The controller created the environment on 2026-10-08 and verified both with `gh api repos/Syntax-Circus/techstrap/environments/release`. `docs/development/RELEASING.md` records the values (no secrets). That a prerelease tag publishes all three packages without a stored key is proven by T16.
- [ ] **P11-T16** Publish `v1.0.0-rc.1` packages and run the post-publish check: fresh project restores `TechStrap.Client` and `TechStrap.Client.Maui` from nuget.org and submits a ticket to UAT
  - **Depends on:** P11-T12, P11-T15, P05 deployed to UAT
  - **Validation:** Restore from nuget.org succeeds (indexed); sample submits against UAT and the ticket arrives with metadata flagged untrusted for a Public key.
  - **Pending (11c):** the owner's `v1.0.0-rc.1` tag push after the merge. The post-publish check runs against the local compose stack (UAT is not deployed; the UAT submit is PHASE-12, P12-T14). T16 is ticked in a follow-up docs commit once the packages restore from nuget.org.
- [x] **P11-T17** Rely on server-side idempotency for submit retries (D-020): generate an `Idempotency-Key` per `SubmitTicketAsync` call, allow the caller to supply one, enable retries for submit only when a key is present, and document the behavior in the READMEs; if the owner rejects D-020, keep retries disabled and record that in [04-DECISION-LOG.md](04-DECISION-LOG.md)
  - **Depends on:** P11-T04, P05-T17
  - **Validation:** `Client.Tests` integration test against the real API: a simulated timeout followed by a retry with the same key yields exactly one ticket and the same `SubmitTicketResponse`; a call without a key is never retried; D-020 status is reflected in [04-DECISION-LOG.md](04-DECISION-LOG.md).
  - **As built (11a):** a lost first response followed by a retry with the same key leaves exactly one ticket row and returns the same ticket number; `SubmitTicketOnceAsync` after a lost response makes one attempt and leaves one row. The key is a generated GUID unless the caller supplies one; the README note is 11c (see `docs/development/CLIENT-SDK.md` for now). D-020 stands (approved).

## Success Criteria

- [ ] A .NET app can `dotnet add package TechStrap.Client`, configure base URL + key, and create a ticket; the response gives the ticket number and view URL. *(11a proved the call against the real Api with project references, not the package, so this stays open; consuming the packed package is the 11c sample, and the nuget.org install is 11c.)*
- [ ] A MAUI app can `dotnet add package TechStrap.Client.Maui`, submit a ticket with device/app metadata and an optional screenshot, with metadata truncation and opt-out working. *(Partial in 11b: submit with device/app metadata, truncation, redaction and opt-out are built and tested; there is no screenshot until 11d adds attachments, and the package is not on nuget.org until 11c.)*
- [ ] Failure modes (bad key, rate limit, validation, attachments, outage) return typed `Result` failures; submit is never silently duplicated by retries. *(Partial in 11a: all but attachments, deferred to 11d by D-047; 413 and 415 are mapped.)*
- [ ] Pushing tag `v*` builds, tests, packs and publishes `TechStrap.Contracts`, `TechStrap.Client` and `TechStrap.Client.Maui` to nuget.org with symbols and READMEs, through OIDC (no long-lived key in the repo). *(Workflow in place and pinned; dry run and rc.1 publish follow the merge.)*
- [x] Contract test proves the SDK matches the API's OpenAPI document and the real intake endpoint. *(Response schema not pinned, see Corrections.)*
- [x] Each package has a README; samples compile and run. *(11c: the console sample builds in CI and ran against the compose stack; the README snippets are compiled.)*
- [x] `dotnet build`, `dotnet test` green; the single net10.0 target builds and tests on the ubuntu CI (D-048).

## Boundary Validation

- [ ] Application use-case entry points delegate to the named handlers listed above (none added; SDK consumes `SubmitTicketRequestHandler` through the P05 endpoint).
- [ ] Framework-owned operational or static exemptions execute no application workflow (N/A).
- [ ] Handler constructor dependencies contain only approved abstractions (N/A: no handlers added).
- [ ] Persistence and integration entities do not cross infrastructure boundaries (SDK exposes only Contracts DTOs and `Result`).
- [ ] Cancellation reaches asynchronous handler dependencies (SDK passes `CancellationToken` to HTTP, attachment streaming and the MAUI collector).
- [ ] Expected outcomes and transport mapping have focused tests (HTTP status -> `Result` failure mapping).
- [ ] Infrastructure implementations have integration coverage where applicable (SDK <-> real API in `TechStrap.Client.Tests`).
- [ ] Inline Razor components contain only simple parameters and, at most, one trivial synchronous `EventCallback`-forwarding callback (N/A: no Razor components).
- [ ] Every component beyond the inline ceiling uses paired `.razor` and `.razor.cs` files, with all C# in code-behind (N/A).
- [ ] Each Razor ViewModel is feature-local and presentation-only; the recorded direct-model decision does not expose an API ViewModel (N/A; sample-only note).
- [ ] A factory or presentation service is used only for non-trivial mapping, asynchronous assembly, or multiple dependencies (N/A).
- [ ] API request and response contracts use DTO names and contracts, never Razor ViewModels.
- [ ] Repeated or business-meaningful literals are named constants at the right scope (header name, metadata keys, limits, timeout/circuit breaker values) — shared in Contracts only because they cross the API/SDK boundary.
- [ ] Duplicated-looking logic across flows was evaluated for genuine divergence (client-side attachment pre-checks reuse Contracts limits rather than a copy of server rules; SDK and API validation are intentionally separate code that share constants only).

## Risks and Open Questions

- [ ] **Resilience and Common as public dependencies.** Settled (D-047): `SyntaxCircus.Http.Resilience` 0.2.2 and `SyntaxCircus.Common` 0.2.0 (web-neutral, so no `Microsoft.AspNetCore.App` framework reference reaches a consumer) are the Client's runtime dependencies, with three `Microsoft.Extensions.*` packages; an architecture rule pins the set.
- [ ] **Contracts becomes a public API.** Breaking DTO changes after 1.0 need semantic-versioning discipline (additive only; `EnablePackageValidation` baseline after 1.0.0). Plan lists two packages; confirm the third (`TechStrap.Contracts`) is acceptable, or inline a trimmed copy into Client (rejected: drift).
- [ ] **Non-idempotent submit vs retries** (see P11-T17, D-020). Default: no automatic retry on submit unless an `Idempotency-Key` is set. Settled (D-047): `HttpRequestResiliencePipeline` with a per-call replay flag; `SubmitTicketOnceAsync` never retries; D-020 is approved and proved end to end by P11-T17.
- [ ] **Header names and trusted/public behavior** (`X-Api-Key`, `Idempotency-Key`) must exactly match P05/`AspNetCore.Authentication`; they are Contracts constants. Settled (D-047): they are `HeaderNames` (not `TechStrapHeaders`), and an architecture rule keeps the literals out of the rest of `src/`.
- [ ] **MAUI workload build** needs macOS runners (cost/time); consider building only `net10.0` + android on Linux/Windows and iOS only for tags (**Assumption**: macOS for all MAUI jobs, as the reference repo does). Settled (D-047): `net10.0` and Android on the existing runner; iOS only on a `v*` tag on macOS (11b). Superseded (D-048): a single `net10.0` target with `Microsoft.Maui.Essentials`; no workload, no platform targets and no macOS runner.
- [ ] **Public keys are extractable.** Client-side mitigations (honeypot is portal-only) do not exist; abuse relies on server rate limits and untrusted metadata flags — highlight in README and in the security review ([PHASE-12](PHASE-12-release-hardening.md)).
- [ ] **Privacy of collected metadata** (OS/model/locale/timezone/network): document, default to the minimal set, offer opt-out and redaction.
- [ ] `net10.0`-only targeting excludes older consumers; revisit after 1.0.
- [ ] Trusted Publishing needs `NUGET_USER` and a policy per package ID; the first publish of a new ID may require manual ownership steps.
- [ ] `TechStrap.Client.Tests` is listed in the test layout in `02-ARCHITECTURE.md`; sample projects are not part of the test list.
- [ ] Carried forward from PHASE-05 (D-034): API-key intake is JSON-only; add multipart attachments if the SDK or the MAUI helper needs screenshots. Settled (D-047): the SDK v1 is JSON-only; attachments (P11-T05) wait for multipart intake (11d).

## Handoff

Before [PHASE-12](PHASE-12-release-hardening.md) starts: the three packages
are published as `v1.0.0-rc.1` (or the pack/publish workflow is verified end to
end with a dry run and the nuget.org policy is in place), samples run against
UAT, READMEs are complete, and the idempotency decision (D-020) and header
naming are confirmed and recorded. Phase 12 includes the SDK/public-key abuse
paths in its security review and uses the same tag for the final `v1.0.0`
release.
