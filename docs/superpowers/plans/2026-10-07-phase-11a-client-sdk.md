# PHASE-11a Client SDK core: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the first of three pull requests of PHASE-11 (Client SDK, `docs/architecture/PHASE-11-client-sdk.md`; D-005, D-020): `TechStrap.Client`, the NuGet package that lets a company app submit a ticket with an API key, plus the packaging of `TechStrap.Contracts`, with the web-neutral split of `SyntaxCircus.Common` as its prerequisite. 11a covers P11-T01, T02, T03, T04, T06, T10 and T17.
- **The SDK:** `ITechStrapClient` (`SubmitTicketAsync`, `SubmitTicketOnceAsync`) over `HttpRequestResiliencePipeline`, returning the Syntax Circus `Result<SubmitTicketResponse>`; retried only when the submit carries an `Idempotency-Key`; the key never leaves the header.
- **The packaging:** `eng/Packaging.props` imported by Contracts and Client only; a pack dry run in CI that checks README, license, repository URL, symbols and the exact dependency list of each package.
- **The proof:** unit tests with a scripted handler, integration tests against the real Api (Testcontainers Postgres) including the retry-with-the-same-key proof (T17) and an OpenAPI contract test, and the repository pins (architecture rules, Pester) that keep it so.

**Architecture:**
- **The Client depends on Contracts and five reviewed packages only** (`SyntaxCircus.Http.Resilience`, `SyntaxCircus.Common`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection.Abstractions`); Contracts keeps zero `PackageReference`s.
- **Replay safety is per call.** The SDK owns one `HttpRequestResiliencePipeline` and passes `HttpRequestReplaySafety` on every send: `Replayable` with an `Idempotency-Key`, `NotReplayable` without one.
- **The wire is named once.** Header names, the route and the limits come from Contracts (`HeaderNames`, `IntakeRoutes`, `IntakeLimits`); an architecture rule flags the quoted literals anywhere else in `src/`.

**Tech Stack:** .NET 10, `SyntaxCircus.Http.Resilience` 0.2.2, `SyntaxCircus.Common` 0.2.0 (web-neutral), `Microsoft.Extensions.Http`, xUnit v3, Shouldly, NSubstitute, `Microsoft.AspNetCore.Mvc.Testing`, Testcontainers (Postgres), `FakeTimeProvider` and Pester.

**Spec:** `docs/architecture/PHASE-11-client-sdk.md` (T01, T02, T03, T04, T06, T10, T17 and the Success Criteria 1, 3, 5), `docs/architecture/04-DECISION-LOG.md` (D-005, D-020, D-034, and D-047, added in Task 6), `docs/architecture/03-PACKAGE-MAP.md`, and the owner decisions of 2026-10-07 (the PHASE-11 planning session, "Owner decisions"), recorded as **D-047** in Task 6.

### Owner decisions (2026-10-07), recorded as D-047
1. **PR split: three PRs.** 11a (this plan): P11-T01, T02, T03, T04, T06, T10, T17. 11b: MAUI (T07 to T09). 11c: READMEs, samples, `publish-nuget.yml`, validation, nuget.org, `v1.0.0-rc.1` (T11 to T16). 11b and 11c wait on owner actions #10 (macOS) and #9 (nuget.org).
2. **Attachments vs D-034: JSON-only SDK v1.** No `TicketAttachment`, no multipart (`IntakeController` is `[FromBody] SubmitTicketRequest`). P11-T05 and the MAUI screenshot adapter are deferred to a later 11d that first adds multipart intake in a P05 amendment.
3. **SDK dependencies: `SyntaxCircus.Http.Resilience` + `SyntaxCircus.Common`** (`03-PACKAGE-MAP.md:24` "plain `HttpClient`" is corrected). **But** Common 0.1.3 carries `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (for `ICurrentUserService` over `IHttpContextAccessor`), which an SDK consumer, and a MAUI app above all, cannot take. Owner: **fix Common first** (the deferred proposal in `SyntaxCircus.Common/docs/enhancements/web-neutral-contracts.md`), moving the HTTP-bound types into the **existing `SyntaxCircus.AspNetCore.Common`** (already depends on Common, already hosts `IHttpContextAccessor` pieces). So `SyntaxCircus.Common` is fixed first, and `ICurrentUserService` moves to `SyntaxCircus.AspNetCore.Common`.
4. **MAUI CI:** `net10.0` + android on the existing runner; iOS only on a `v*` tag on macOS. (11b concern; 11a leaves `TechStrap.Client.Maui` as the `net10.0` placeholder.)

### Decisions made while drafting (D-047 records them)
- **Retries use `HttpRequestResiliencePipeline`, not `AddResilientHttpClient`.** The package's `AddResilientHttpClient` retries every request (POST included) on 408/429/500/502/503/504 -> duplicate tickets. `HttpRequestResiliencePipeline.SendAsync(requestFactory, sender, completionOption, HttpRequestReplaySafety, ct)` takes a per-call replay flag and a fresh request per attempt. A submit **with** an `Idempotency-Key` is `Replayable` (same key every attempt); **without** one it is `NotReplayable` (sent once). Retryable: transport, timeout, 408, 502, 503, 504. **Not 500** (the Portal's `ApiClientRegistration.IsRetryable` rule: a 500 is the API's own answer). **Not 429**: the pipeline cannot honour or cap `Retry-After` per response and its status set also drives the circuit breaker; a 429 is returned as `rate-limited` for the caller to handle. The Api never emits `Retry-After` today (grep: none in `src/TechStrap.Api`), so the SDK does not carry it (no `ResultError.Target` hack); revisit when the server sends one.
- **Result mapping mirrors the Portal** (`src/TechStrap.Portal/Clients/ProblemMapping.cs`, `ApiConnection.cs`; copy, the Portal's is `internal`): see the table in Task 3. Caller cancellation propagates as `OperationCanceledException`; programmer errors throw; nothing else does.
- **Error-code constants live in `TechStrap.Client`** (`TechStrapClientErrorCodes`), not Contracts: produced by the SDK, never on the wire (the Portal and Admin keep local `ApiErrorCodes` for the same reason). Wire codes (400 `errorCodes`, `IntakeWarnings`) pass through unchanged.
- **Constants reconciled to what exists.** Spec's `TechStrapHeaders` = `TechStrap.Contracts.Http.HeaderNames` (`ApiKey = "X-Api-Key"`, `IdempotencyKey = "Idempotency-Key"`); spec's `TicketMetadataLimits` = `IntakeLimits.MaxMetadata*` (+ `MaxIdempotencyKeyLength = 200`); `TicketMetadataKeys` is 11b. New: `IntakeRoutes.Tickets = "api/intake/tickets"` (precedent `TicketHubRoutes.Path` in `Contracts/Live/LiveNames.cs`), used by the controller.
- **The key never leaves the header.** No logging handlers (`RemoveAllLoggers()`, as the Portal); primary handler `SocketsHttpHandler { AllowAutoRedirect = false }` (a redirect would replay `X-Api-Key` to another host); `ApiKeyHandler` refuses any request whose authority is not `BaseAddress`'s; options is a class (a record's `ToString` would print the key); validation messages never echo it.
- **Two methods, not a `SubmitOptions` type:** `SubmitTicketAsync(request, idempotencyKey = null, ct)` (null -> SDK generates a key; retries on) and `SubmitTicketOnceAsync(request, ct)` (no key, one attempt). `MaxAttempts` (attempts incl. the first) rather than the spec's `retryCount` to match the pipeline's own option.
- **Packaging in 11a is the minimum:** `eng/Packaging.props` imported by Contracts and Client only, `IsPackable=false` in the root `Directory.Build.props`. **`GitVersion.MsBuild` is deferred to 11c** (the publish workflow): adding it to Contracts puts the GitVersion task under every image build (the Dockerfiles pass `/p:DisableGitVersionTask`) for no 11a benefit; the CI dry run passes `-p:Version=0.0.0-ci`. **No `GenerateDocumentationFile`** (CS1591 under `TreatWarningsAsErrors` would fail every undocumented Contracts member; 11c). **No `Microsoft.SourceLink.GitHub` reference** (bundled in the SDK; `PublishRepositoryUrl` + `EmbedUntrackedSources` suffice).
- **`tests/TechStrap.Client.Tests` references `src/TechStrap.Api` directly** (not `Api.Tests`, an xUnit v3 executable with an assembly fixture and `internal` test data). It links the reusable files (`Compile Include` precedent `../Shared/*.cs`): `../TechStrap.Api.Tests/TestPostgres.cs`, `Auth/ApiTestDatabase.cs`, `Auth/TestJwt.cs`, `Intake/IntakeTestData.cs`, `SetRemoteIpAddressStartupFilter.cs`, `../Shared/CollectingSink.cs`, and writes a ~30-line `ClientApiFactory : WebApplicationFactory<TechStrap.Api.Program>` copying `HostFactory`'s defaults (`DotEnv__Enabled=false` static ctor, `TestJwt.Settings` env vars, `MigrateOnStartup=false`, public URLs, `Storage:Local:RootPath`, `TestJwt.Configure`).
- **Controller ruling (a), from Step 0a.** `FindFirstValue` turned out to be an ASP.NET extension (`Microsoft.AspNetCore.Http`), not a BCL one, so after the framework reference is removed Common's `ClaimsPrincipalExtensions` uses a private equivalent (`ArgumentNullException` parity kept).
- **Controller ruling (b), the order.** Task 1 runs before Step 0c: 0c bumps `SyntaxCircus.Common` to 0.2.0 and `SyntaxCircus.AspNetCore.Common` to 0.1.16, and waits on the owner publishing them (PRs Syntax-Circus/SyntaxCircus.Common#5 and Syntax-Circus/SyntaxCircus.AspNetCore.Common#17). Task 1 needs neither package, so it does not block.
- **Controller ruling (c), the "verbatim copy".** The "verbatim copy of the Portal's `Problem` parsing" (Task 3) is accepted as a copy because the Portal's class is `internal` to a host app and Contracts is dependency-free DTOs, so there is no shared home for it.
- **Controller ruling (d), the packaging pin.** `Packaging_props_package_references_are_all_private_assets` is proved by a fixture, not by the live props: today `eng/Packaging.props` has zero `PackageReference`s, so a test on the live file would be vacuous. The fixture proves the rule and the live-file test guards 11c.

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors`. Private fields are `_camelCase`; namespaces are file-scoped.
- **Packages.** `SyntaxCircus.Common` 0.1.3 to 0.2.0 and `SyntaxCircus.AspNetCore.Common` 0.1.15 to 0.1.16 (`Directory.Packages.props` and `03-PACKAGE-MAP.md` in the same commit; `scripts/Check-PackageVersions.ps1` fails when they disagree; the two must move together because 0.1.15 pins Common exactly). Everything else the Client references is already pinned. `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql` and `Microsoft.Extensions.TimeProvider.Testing` are used by the new test project and are pinned already.
- **Migrations.** None. Verify with `dotnet ef migrations has-pending-model-changes`.
- **Architecture rules.**
  - Contracts and Domain reference no project, package or framework (`ProjectReferenceDirectionTests.Domain_and_Contracts_reference_no_project_package_or_framework`, `ReferenceRules.DependencyFreeProjects`).
  - `TechStrap.Client` references Contracts and only `ReferenceRules.ClientAllowedPackages`.
  - The wire literals `"X-Api-Key"`, `"X-Ticket-Token"`, `"Idempotency-Key"` and `"api/intake/tickets"` appear in `src/` only in `Contracts/Http/HeaderNames.cs`, `Contracts/Intake/IntakeRoutes.cs` and the named exemption `Hosting/Sentry/SensitiveHeaderSentryProcessor.cs:12` (Hosting references no TechStrap project).
- **Secrets.** The API key travels in the `X-Api-Key` header only: never in a URL, a log, an exception message, a validation message or `ToString()`. No logging handlers on the SDK's named client. Redirects are off.
- **Encoding.** Non-ASCII in C# as `\u` escapes (`SourceEncodingTests` walks `src/` and `tests/`); keep new READMEs and docs ASCII. New files are written with LF.
- **Tests.**
  - Failing test first (RED), then GREEN, then a recorded mutation run for each task.
  - Every waiting test carries `Timeout` and `Xunit.TestContext.Current.CancellationToken`; no sleeps; time is a `FakeTimeProvider` or 1 ms delays.
  - Mutation batches are at most 12, run in the foreground, one at a time, and the file is restored after each. After an interrupted run `git status` must show nothing but staged files before the next one.
  - Docker tests carry `[Trait("Integration","Docker")]`; `docker ps` shows no leftover container of yours at the end.
- **Commits.** Use Conventional Commits. Run `git diff --cached --stat` before every commit. End each one with exactly these two lines, on separate lines:
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden.** `git add -f`, `git add -A` (the working tree may hold other agents' files), committing `.superpowers/`, and `docker compose down -v`.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release`: 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release`: passes (Docker running).
  - `pwsh -File scripts/Invoke-ScriptTests.ps1`: passes.
  - `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build`: no changes.
- **Branch.** Work on `feat/phase-11a-client-sdk` (cut from `main`). Never edit or commit on `main`. Execution: `superpowers:subagent-driven-development` (Sonnet implements, Haiku for mechanical edits; the controller session reviews).

---

## Step 0 (prerequisite, two sibling repos): make `SyntaxCircus.Common` web-neutral

Both repos are local and clean on `main`: `D:\dev\SyntaxCircus\SyntaxCircus.Common` (v0.1.4; GitVersion TrunkBased, `commit-message-incrementing: Enabled`; `build.yml` packs, publishes via OIDC and tags on every push to `main`) and `D:\dev\SyntaxCircus\SyntaxCircus.AspNetCore.Common` (v0.1.15; pins `SyntaxCircus.Common` `[0.1.3]`).

### Step 0a: Common to 0.2.0

Branch `feat/web-neutral`, PR (Syntax-Circus/SyntaxCircus.Common#5), merge commit message carries `+semver: minor`. Done: commits 9fe0d12..4660809.

- [x] **0a.1: Project file.** In `src/SyntaxCircus.Common/SyntaxCircus.Common.csproj`: remove `<FrameworkReference Include="Microsoft.AspNetCore.App" />`; add `PackageReference`s `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions` (for `PeriodicBackgroundService`), `Microsoft.Extensions.DependencyInjection.Abstractions`; pin them in `Directory.Packages.props` (10.0.x). Update `Description`/`PackageTags` (drop "ICurrentUserService").
- [x] **0a.2: Move the HTTP-bound types out.** Delete `ICurrentUserService.cs`, `CurrentUserService.cs`, `CurrentUserServiceExtensions.cs`; drop `global using Microsoft.AspNetCore.Http;` from `GlobalUsings.cs` (keep Hosting/Logging/DI). Delete `tests/.../CurrentUserServiceTests.cs`, `CurrentUserServiceExtensionsTests.cs`; remove the test csproj's `FrameworkReference`. (Controller ruling (a): `ClaimsPrincipalExtensions` gets a private `FindFirstValue` equivalent, keeping `ArgumentNullException` parity.)
- [x] **0a.3: Docs.** `README.md`: remove the `ICurrentUserService` section (lines ~77 to 94), point to `SyntaxCircus.AspNetCore.Common`; add a "0.2.0 breaking change" note. Replace `docs/enhancements/web-neutral-contracts.md` status with "Done 2026-10-07: types moved to SyntaxCircus.AspNetCore.Common 0.1.16; `ApiResult` stays (uses `System.Net.HttpStatusCode` only)".
- [x] **0a.4: Verify.** `dotnet build/test` green; `dotnet pack` -> nuspec has **no** `frameworkReferences`, dependencies are the three `Microsoft.Extensions.*` only. Publish happens on merge; confirm the `v0.2.0` tag and nuget.org indexing (owner action, see Step 0c).

### Step 0b: AspNetCore.Common to 0.1.16

Branch `feat/current-user-service`, PR (Syntax-Circus/SyntaxCircus.AspNetCore.Common#17), `+semver: patch` or default. Done: commits cc51b59..8f05223.

- [x] **0b.1: Add the types.** Add `ICurrentUserService.cs`, `CurrentUserService.cs`, `CurrentUserServiceExtensions.cs` under `namespace SyntaxCircus.AspNetCore.Common` (same members; `AddCurrentUserService()` calls `AddHttpContextAccessor()`), plus the two moved test files. Bump `Directory.Packages.props` `SyntaxCircus.Common` to `[0.2.0]`. README section added.
- [x] **0b.2: Verify.** Build/test green; pack dependency list shows `SyntaxCircus.Common 0.2.0`.

### Step 0c: techstrap bump (waits on the owner publishing Common 0.2.0 and AspNetCore.Common 0.1.16)

Controller ruling (b): Task 1 is done first; this step is the first commit of the branch that needs the published packages. It runs when both PRs are merged and indexed on nuget.org.

- [ ] **0c.1: Versions.** `Directory.Packages.props`: `SyntaxCircus.Common` 0.1.3 -> 0.2.0 and `SyntaxCircus.AspNetCore.Common` 0.1.15 -> 0.1.16 (must move together: 0.1.15 pins Common exactly). The same two rows in `docs/architecture/03-PACKAGE-MAP.md` (`scripts/Check-PackageVersions.ps1` fails when they disagree).
- [ ] **0c.2: The doc comment.** techstrap uses no `ICurrentUserService` (only a doc comment in `src/TechStrap.Application/Agents/ICurrentAgentClaims.cs:7`); reword it.
- [ ] **0c.3: Verify and commit.** `dotnet build TechStrap.slnx -c Release` 0 warnings; `pwsh -File scripts/Check-PackageVersions.ps1`; `git diff --cached --stat`; commit `build(deps): SyntaxCircus.Common 0.2.0 and AspNetCore.Common 0.1.16 (D-047)`.
- [ ] **0c.4: Save this plan.** The plan itself is committed first as `docs(plans): PHASE-11a client SDK core plan (D-047)` in `docs/superpowers/plans/2026-10-07-phase-11a-client-sdk.md`, in the format of `2026-10-07-phase-10b-live-admin.md` (Goal / Architecture / Owner decisions / Decisions made while drafting / Global Constraints / Tasks).

---

## Task 1: P11-T01 + Contracts half of P11-T10: route constant, wire-literal rule, Contracts pack metadata

Done: techstrap commit 1912653 (`feat(contracts): IntakeRoutes, wire-literal rule and pack metadata (P11-T01, P11-T10)`).

**Files:**

- Create: `src/TechStrap.Contracts/Intake/IntakeRoutes.cs`, `eng/Packaging.props`, `src/TechStrap.Contracts/README.md`
- Modify: `src/TechStrap.Api/Controllers/IntakeController.cs` (line 16), `src/TechStrap.Application/Intake/IntakeErrors.cs` (line 28), `src/TechStrap.Contracts/TechStrap.Contracts.csproj`, `Directory.Build.props`
- Test (create): `tests/TechStrap.Architecture.Tests/WireLiteralRules.cs`, `tests/TechStrap.Architecture.Tests/WireLiteralTests.cs`
- Test (add to existing): Api.Tests `IntakeController_route_template_equals_IntakeRoutes_Tickets`; Application.Tests pin of the value next to `IntakeLimitsParityTests`

**Interfaces:**
- Produces: `public static class IntakeRoutes { public const string Tickets = "api/intake/tickets"; }` (relative, no leading slash); `eng/Packaging.props` (no `PackageReference`); Contracts packable as `TechStrap.Contracts` with zero dependencies.

- [x] **Step 1: Tests first (RED).** `WireLiteralRules.cs` + `WireLiteralTests.cs`: a pure `Evaluate(IEnumerable<(Path, Text)>)` that strips `//` and `///` lines and flags the quoted literals `"X-Api-Key"`, `"X-Ticket-Token"`, `"Idempotency-Key"`, `"api/intake/tickets"` anywhere in `src/` except `Contracts/Http/HeaderNames.cs`, `Contracts/Intake/IntakeRoutes.cs` and the named exemption `Hosting/Sentry/SensitiveHeaderSentryProcessor.cs:12` (Hosting references no TechStrap project). RED today on `IntakeController` and `IntakeErrors`. Plus fixture tests per literal, per exemption and for comments; `Packaging_props_package_references_are_all_private_assets` (proved by a fixture, controller ruling (d); the live props has zero `PackageReference`s and the test guards 11c); Api.Tests `IntakeController_route_template_equals_IntakeRoutes_Tickets`; an Application.Tests pin of the value next to `IntakeLimitsParityTests`.
- [x] **Step 2: Implementation (GREEN).**
  - Create `src/TechStrap.Contracts/Intake/IntakeRoutes.cs`; `IntakeController.cs:16` -> `[Route(IntakeRoutes.Tickets)]`; `src/TechStrap.Application/Intake/IntakeErrors.cs:28` literal `"Idempotency-Key"` -> `HeaderNames.IdempotencyKey`.
  - Create `eng/Packaging.props`: `IsPackable=true`, `Authors`/`Company` "Syntax Circus LLC", `PackageLicenseExpression` MIT, `PackageProjectUrl` / `RepositoryUrl` (github.com/Syntax-Circus/techstrap), `RepositoryType` git, `PublishRepositoryUrl`, `EmbedUntrackedSources`, `IncludeSymbols` + `snupkg`, `ContinuousIntegrationBuild` when `GITHUB_ACTIONS`, `EnablePackageValidation`, `PackageReadmeFile=README.md` + `<None Include="$(MSBuildProjectDirectory)/README.md" Pack="true" PackagePath="/" />`. No `PackageReference` (GitVersion is 11c).
  - Root `Directory.Build.props`: `<IsPackable>false</IsPackable>` (today `dotnet pack TechStrap.slnx` would pack every class library).
  - `src/TechStrap.Contracts/TechStrap.Contracts.csproj`: import the props; `PackageId` TechStrap.Contracts, `Description`, `PackageTags`. Create `src/TechStrap.Contracts/README.md` (short, ASCII: what it is, stability promise, "see TechStrap.Client"). Contracts must keep **zero** `PackageReference`s (`ProjectReferenceDirectionTests.Domain_and_Contracts_reference_no_project_package_or_framework`, `ReferenceRules.DependencyFreeProjects`).
- [x] **Step 3: Mutations.** Change the constant; put a literal back; drop the exemption. Each must be killed.
- [x] **Step 4: Validate.** Architecture tests; `dotnet build TechStrap.slnx -c Release`; `dotnet pack src/TechStrap.Contracts -c Release -p:Version=0.0.0-local -o <scratch>` -> README, MIT, repo URL, `.snupkg`, **no dependencies**.
- [x] **Step 5: Commit.** `feat(contracts): IntakeRoutes, wire-literal rule and pack metadata (P11-T01, P11-T10)` with the attribution lines (commit 1912653).

---

## Task 2: P11-T02 + P11-T03: Client project, options, validator, `ApiKeyHandler`, architecture rules, CI filter

**Files:**

- Create: `src/TechStrap.Client/TechStrap.Client.csproj`, `src/TechStrap.Client/README.md`, `TechStrapClientOptions`, the internal options validator, `ApiKeyHandler`, `TechStrapClientDefaults`, `TechStrapClientErrorCodes`, `tests/TechStrap.Client.Tests/TechStrap.Client.Tests.csproj`
- Modify: `TechStrap.slnx`, `TechStrap.CI.slnf` (Client.Maui stays out), `tests/TechStrap.Architecture.Tests/ReferenceRules.cs`, `tests/TechStrap.Architecture.Tests/ProjectReferenceDirectionTests.cs`

**Interfaces:**
- Produces:
  - `TechStrapClientOptions` (class): `Uri? BaseAddress`, `string? ApiKey`, `TimeSpan Timeout = 30s` (total budget), `int MaxAttempts = 3`, `TimeSpan RetryBaseDelay = 500ms`, `TimeSpan MaxRetryDelay = 5s`.
  - Internal `IValidateOptions<TechStrapClientOptions>`, checked on first use (no generic host on MAUI, so no `ValidateOnStart`).
  - `ApiKeyHandler(IOptions<TechStrapClientOptions>) : DelegatingHandler`.
  - `TechStrapClientDefaults { HttpClientName = "TechStrap"; ConfigurationSection = "TechStrap" }`.
  - `TechStrapClientErrorCodes { InvalidApiKey = "invalid-api-key", ValidationFailed, PayloadTooLarge, UnsupportedMediaType, RateLimited, ApiUnavailable, UnexpectedResponse = "api-unexpected-response", ApiError }`.

- [ ] **Step 1: Branch and project skeleton.**
  - `src/TechStrap.Client/TechStrap.Client.csproj`: import the props; `PackageId` TechStrap.Client; `PackageReference`s `SyntaxCircus.Http.Resilience`, `SyntaxCircus.Common`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection.Abstractions` (all already pinned); `ProjectReference` Contracts; `InternalsVisibleTo` TechStrap.Client.Tests. Short `README.md`.
  - Create `tests/TechStrap.Client.Tests/TechStrap.Client.Tests.csproj` (refs Client, Contracts, Api; links the files listed in "Decisions made while drafting"; packages `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`, `Microsoft.Extensions.TimeProvider.Testing`, all pinned). Add to `TechStrap.slnx` and `TechStrap.CI.slnf` (Client.Maui stays out).
- [ ] **Step 2: Architecture rules first (RED).** `tests/TechStrap.Architecture.Tests/ReferenceRules.cs`: `ClientAllowedPackages` = the five packages above; `Evaluate` emits "`TechStrap.Client` must not reference package X"; `ProjectReferenceDirectionTests`: `Client_references_only_Contracts_and_the_reviewed_packages` + a fixture test.
- [ ] **Step 3: Unit tests (RED, no Docker).**
  - Validator theories per rule: absolute http/https, no userinfo/query/fragment, https unless loopback; key non-blank and header-safe; `0 < Timeout <= 10 min`; `1 <= MaxAttempts <= 10`; `0 < RetryBaseDelay <= MaxRetryDelay`.
  - Loopback http allowed (`localhost`, `127.0.0.1`, `[::1]`).
  - `Validation_messages_never_contain_the_api_key`; `Options_ToString_does_not_print_the_key`.
  - Handler sets the header (`HeaderNames.ApiKey`) and replaces any caller value; a foreign authority -> nothing sent.
  - `Registration_has_no_logging_handler_on_the_named_client` (inspect the `IHttpMessageHandlerFactory` chain for `Logging*HttpMessageHandler`).
  - `Primary_handler_does_not_follow_redirects` (stub answers 302, no second request).
- [ ] **Step 4: Implementation (GREEN).** The options class, the validator (messages never contain the key), `ApiKeyHandler` (sets `HeaderNames.ApiKey`, replacing any caller value, and throws without sending for a foreign authority), `TechStrapClientDefaults`, `TechStrapClientErrorCodes`.
- [ ] **Step 5: Mutations.** Drop the loopback exemption; flip the https rule; skip the authority check; echo the key in a message; `AllowAutoRedirect = true`.
- [ ] **Step 6: Validate.** `dotnet test --project tests/TechStrap.Client.Tests -c Release --filter-not-trait "Integration=Docker"`; the Architecture tests.
- [ ] **Step 7: Commit.** `feat(client): TechStrap.Client project, options validation and ApiKeyHandler (P11-T02, P11-T03)`, after `git diff --cached --stat`.

---

## Task 3: P11-T04 + unit half of P11-T17: the client, the mapping, the DI

**Files:**

- Create: `ITechStrapClient`, internal `TechStrapClient`, internal `ProblemResponseMapper`, internal `TechStrapClientMessages`, `TechStrapClientServiceCollectionExtensions` (all under `src/TechStrap.Client/`)
- Test (create): the unit tests under `tests/TechStrap.Client.Tests/` (stub handler with a scripted queue)

**Interfaces:**
- Produces:
  - `ITechStrapClient`: `Task<Result<SubmitTicketResponse>> SubmitTicketAsync(SubmitTicketRequest request, string? idempotencyKey = null, CancellationToken ct = default)`; `SubmitTicketOnceAsync(request, ct)`.
  - `TechStrapClientServiceCollectionExtensions.AddTechStrapClient(Action<TechStrapClientOptions>)` and `AddTechStrapClient(IConfiguration)` (binds the six keys by hand; `Options.ConfigurationExtensions` is not pinned).

- [ ] **Step 1: Spike first (at most 15 minutes, scratch).** Restore `SyntaxCircus.Http.Resilience` 0.2.2 (only 0.2.1 is in the local cache) and confirm: the `HttpRequestResiliencePipeline` / `HttpRequestResilienceOptions` names; that `HttpRequestTimeoutException` and `HttpCircuitOpenException` are public; whether the last attempt rethrows the original exception; whether `TotalRequestTimeout` includes backoff delays; the circuit-breaker defaults. Record the answers in `TechStrapClient` comments and in the D-047 text (Task 6).
- [ ] **Step 2: Tests first (RED).** Stub handler with a scripted queue, recording requests and headers; the fixture uses 1 ms delays.
  - One test per row of the mapping table below; `Server_text_of_non_400_responses_never_reaches_the_result`.
  - Retries: 503/408/502/504 retried with the identical key on every attempt; 500/429/400 not; a transport failure retried then `api-unavailable` after `MaxAttempts`; `MaxAttempts_1_never_retries`; `SubmitTicketOnceAsync` sends no key and one attempt; a generated key is 32 hex and differs per call but not per attempt; a supplied key is sent verbatim; an invalid key throws before sending; `Each_attempt_gets_a_fresh_request_and_body`.
  - Budget: a hanging stub -> `api-unavailable` within `Timeout`.
  - Circuit: an open circuit skips the handler (skip if the spike shows the thresholds are not deterministic; cover it in Task 4).
  - Cancellation propagates; a null request throws; invalid options -> `OptionsValidationException` on first use.
  - Request shape: POST to `IntakeRoutes.Tickets`, the web-JSON body round-trips, the key only in the header.
  - DI: the client is a singleton; the `IConfiguration` overload reads the six keys; double registration is safe.
- [ ] **Step 3: Implementation (GREEN).**
  - `TechStrapClient` (singleton; owns one pipeline `"techstrap-submit"` built from options: `MaxAttempts`, `TotalRequestTimeout = Timeout`, `BackoffBaseDelay`/`MaximumDelay` from the delays, `RetryableStatusCodes = {408, 502, 503, 504}`, `RetryableExceptionCategories = {Transport, Timeout}`); a fresh `HttpRequestMessage` + `JsonContent` (`JsonSerializerDefaults.Web`) per attempt to `IntakeRoutes.Tickets`; generated key = `Guid.NewGuid().ToString("N")`; a supplied key is validated: non-blank, at most `IntakeLimits.MaxIdempotencyKeyLength`, visible ASCII, else `ArgumentException`.
  - `ProblemResponseMapper`: a copy of the Portal's `Problem` parsing (accepted as a copy, controller ruling (c)): `errorCodes`/`errors` per field, `field.Length == 0` -> null target.
  - `TechStrapClientMessages`; the DI extensions.
  - The named client: `RemoveAllLoggers()`, `Timeout = InfiniteTimeSpan` (the pipeline owns the deadline), `BaseAddress` from options, `.AddHttpMessageHandler<ApiKeyHandler>()`, primary `SocketsHttpHandler { AllowAutoRedirect = false }`; idempotent when called twice.

  The result mapping (mirrors the Portal's `ProblemMapping.cs` and `ApiConnection.cs`):

| Outcome | Code | `ResultErrorKind` |
| --- | --- | --- |
| 201 with readable body | success | |
| 2xx body null/unreadable | `api-unexpected-response` | Failure |
| 400, 422 | per-field codes from `errorCodes` (message from `errors`), else `validation-failed` with `detail` | Validation |
| 401 | `invalid-api-key` | Unauthenticated |
| 403 (server never sends it; defensive) | `invalid-api-key` | Forbidden |
| 413 / 415 | `payload-too-large` / `unsupported-media-type` | Failure |
| 429 (not retried) | `rate-limited` | Failure |
| 5xx (500 not retried); `HttpRequestException`, `TimeoutException`, `HttpRequestTimeoutException`, `HttpCircuitOpenException`, non-caller `OperationCanceledException` | `api-unavailable` (fixed copy, never exception text/host) | Failure |
| any other status | `api-error` | Failure |

- [ ] **Step 4: Mutations (at most 12).** Add 429/500 to the retryable set; `NotReplayable` -> `Replayable`; regenerate the key per attempt; swap the 401/403 kinds; drop the field target; include the server `detail` for 5xx; cancellation -> Result; reuse the request; ignore `Timeout`; drop 422; attempts-vs-retries off-by-one; key sent from `Once`.
- [ ] **Step 5: Validate.** `dotnet test --project tests/TechStrap.Client.Tests -c Release --filter-not-trait "Integration=Docker"`; `dotnet build TechStrap.slnx -c Release` 0 warnings.
- [ ] **Step 6: Commit.** `feat(client): ITechStrapClient with idempotent retries and Result mapping (P11-T04, P11-T17)`, after `git diff --cached --stat`.

---

## Task 4: P11-T06 + integration half of P11-T17: real API + OpenAPI contract (`[Trait("Integration","Docker")]`)

**Files:**

- Test (create): `tests/TechStrap.Client.Tests/Integration/ClientApiFactory.cs`, `SdkHost.cs`, `SubmitAgainstTheApiTests.cs`, `IdempotencyAgainstTheApiTests.cs`, `OpenApiContractTests.cs`

- [ ] **Step 1: The host.** `ClientApiFactory.cs` (the ~30-line `WebApplicationFactory<TechStrap.Api.Program>` copying `HostFactory`'s defaults). `SdkHost.cs`: `AddTechStrapClient` with `BaseAddress = factory.Server.BaseAddress` (`http://localhost/`, loopback passes validation) + `ConfigurePrimaryHttpMessageHandler(() => factory.Server.CreateHandler())`. Seeding pattern: `IntakeEndpointTests.StartAsync` (Public and Trusted keys via `IntakeTestData`).
- [ ] **Step 2: Submit tests.**
  - Trusted key -> number + `ViewUrl`, `metadata_trusted` true, channel Api.
  - Public key -> `external-user-ref-ignored` warning, untrusted metadata.
  - Revoked / unknown / deactivated-product key -> `invalid-api-key`.
  - Invalid email -> Validation targeting `email`.
  - Oversized body -> `payload-too-large` (`factory.UseKestrel(0)` + real loopback address, as `IntakeEndpointTests`).
  - Rate limit (`RateLimiting:Intake:TrustedKeyPermitLimit=1`, as `IntakeRateLimitTests`) -> `rate-limited`.
- [ ] **Step 3: T17 idempotency tests.** A handler below `ApiKeyHandler` forwards the first request to the server then throws `HttpRequestException` -> the retry carries the same key -> exactly one `tickets` row, same `TicketNumber`, two requests seen with one key. `SubmitTicketOnceAsync` with the same handler -> one attempt, `api-unavailable`, one ticket. A supplied key replayed in a second call returns the first ticket (D-020).
- [ ] **Step 4: OpenAPI contract tests.** `/openapi/v1.json` has `paths["/" + IntakeRoutes.Tickets].post`; security `ApiKey` = `{type: apiKey, in: header, name: HeaderNames.ApiKey}`; the request body is `application/json` whose property names equal the camelCase `SubmitTicketRequest` constructor parameters (reflection) and nothing else; the 201 schema matches `SubmitTicketResponse`; the `Idempotency-Key` header parameter is present (assert what is true, note a gap); nothing multipart.
- [ ] **Step 5: Mutations.** Rename a DTO property; change `HeaderNames.IdempotencyKey`; send a key from `Once`; PascalCase serializer.
- [ ] **Step 6: Validate.** `dotnet test --project tests/TechStrap.Client.Tests -c Release`; `dotnet test --solution TechStrap.CI.slnf -c Release`; `docker ps` shows no leftovers.
- [ ] **Step 7: Commit.** `test(client): SDK against the real API, idempotent retry proof and OpenAPI contract (P11-T06, P11-T17)`, after `git diff --cached --stat`.

---

## Task 5: pack dry run + CI

**Files:**

- Create: `scripts/Test-PackageContents.ps1`, `scripts/tests/Test-PackageContents.Tests.ps1`
- Modify: `.github/workflows/ci.yml`; Pester pin for the workflow

**Interfaces:**
- Produces: `scripts/Test-PackageContents.ps1` (`-PackageDirectory`, `-Expected` id -> dependency ids). 11c's T14 extends it.

- [ ] **Step 1: Pester first (RED).** `scripts/tests/Test-PackageContents.Tests.ps1` with zip fixtures in `$TestDrive`: one good, one per defect (README missing, license not MIT, repository URL missing, readme unset, dependency set differs, `.snupkg` missing). Pester pin: `ci.yml` contains the pack step; `TechStrap.CI.slnf` lists `TechStrap.Client.Tests`.
- [ ] **Step 2: The script (GREEN).** Opens each `.nupkg` (`System.IO.Compression`), asserts README.md present, `<license type="expression">MIT</license>`, repository URL and readme set, dependency ids equal the expected set, matching `.snupkg` exists.
- [ ] **Step 3: CI.** `.github/workflows/ci.yml`, after Test, before the EF pending-model check: `dotnet pack src/TechStrap.Contracts` and `src/TechStrap.Client` with `-c Release --no-build -p:Version=0.0.0-ci -o $RUNNER_TEMP/pack`, then the script with `TechStrap.Contracts = @()` and `TechStrap.Client = @('TechStrap.Contracts','SyntaxCircus.Http.Resilience','SyntaxCircus.Common','Microsoft.Extensions.Http','Microsoft.Extensions.Options','Microsoft.Extensions.DependencyInjection.Abstractions')`.
- [ ] **Step 4: Mutations.** Drop a dependency from the expected set; remove the README item from the props; break the license.
- [ ] **Step 5: Validate.** `pwsh -File scripts/Invoke-ScriptTests.ps1`; local pack + script; `pwsh -File scripts/Check-PackageVersions.ps1`.
- [ ] **Step 6: Commit.** `ci: pack dry run for TechStrap.Contracts and TechStrap.Client (P11-T10)`, after `git diff --cached --stat`.

---

## Task 6: close-out docs and Pester pins

**Files:**

- Modify: `docs/architecture/04-DECISION-LOG.md`, `docs/architecture/03-PACKAGE-MAP.md`, `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, `docs/architecture/00-DISCOVERY-INDEX.md`, `docs/architecture/02-ARCHITECTURE.md`, `docs/architecture/PHASE-11-client-sdk.md`, `scripts/tests/RepositoryDocs.Tests.ps1`
- Create: `docs/development/CLIENT-SDK.md`

- [ ] **Step 1: Decision log.** `docs/architecture/04-DECISION-LOG.md`:
  - Approval-basis bullet after D-046's: `**Owner decision (2026-10-07, PHASE-11 planning):** D-047, the owner decisions on the three-PR split, the JSON-only SDK, the SDK's dependencies and the Common split, and the MAUI CI split. Its technical rulings were proposed in the PHASE-11a plan and approved when the owner approved the plan.`
  - Index row: `| D-047 | PHASE-11: three pull requests, JSON-only SDK v1, web-neutral SyntaxCircus.Common 0.2.0 + Http.Resilience as dependencies, retry only with an Idempotency-Key through HttpRequestResiliencePipeline | Approved (owner 2026-10-07; technical rulings at PHASE-11a plan review) | 2026-10-07 | PHASE-11, 03-PACKAGE-MAP |`
  - The full section: Status / Date / Owner / Related D-001, D-005, D-016, D-020, D-034, D-045 / Context = the findings above incl. the Common framework reference / Decision = owner table + technical rulings + the mapping table / Alternatives: `AddResilientHttpClient` (no per-request replay safety), plain `HttpClient`, own result type in Client (rejected: one Result type across Syntax Circus packages), one PR, multipart now, error codes in Contracts, `SubmitOptions` type / Consequences: Contracts public API, spec corrections, `Client.Tests`, 429 surfaced not retried, one circuit per process, net10.0 only, Common 0.2.0 + AspNetCore.Common 0.1.16 bumped / Approval. Fill the measured facts after the Task 3 spike.
- [ ] **Step 2: Package map.** `03-PACKAGE-MAP.md`: line 24 -> "`TechStrap.Client` (D-047) uses `HttpRequestResiliencePipeline` directly, not `AddResilientHttpClient`: submit is retried only with an `Idempotency-Key`; consumers inherit this package and `SyntaxCircus.Common`" and add P11 to its phase cell; line 115 -> the five dependencies; `SyntaxCircus.Common` row -> 0.2.0, "web-neutral since 0.2.0 (D-047)", P11; `SyntaxCircus.AspNetCore.Common` -> 0.1.16; the `Microsoft.Extensions.*` row mentions the Client.
- [ ] **Step 3: Roadmap and index.** `99-IMPLEMENTATION-ROADMAP.md`:
  - Row 10 -> `10a merged (PR #18); 10b merged (PR #19); PHASE-10 complete: the owner's manual checks with a real identity provider (two browsers, a worker auto-close, the kill switch) are open`.
  - Row 11 -> `D-005, D-020, D-047 | 11a complete (pending merge): T01, T02, T03, T04, T06, T10, T17; 11b (MAUI, T07 to T09) and 11c (READMEs, samples, publish workflow, nuget.org, rc.1; T11 to T16) not started; T05 (attachments) deferred to 11d, which first needs multipart intake`.
  - P11 task index: T05 "(deferred, D-047)"; line ~331 "until PHASE-11" -> "until 11b"; owner actions #9/#10 note 11c/11b.
  - `00-DISCOVERY-INDEX.md` rows 10 and 11 likewise.
- [ ] **Step 4: Architecture doc.** `02-ARCHITECTURE.md` lines ~45/102: "SDK over Contracts (NuGet): JSON submit, retry only with an Idempotency-Key (D-047)"; line ~53: `TechStrap.Client.Tests` "created in PHASE-11a".
- [ ] **Step 5: The phase page.** `PHASE-11-client-sdk.md`: add `### Corrections (D-047, 2026-10-07)` after External prerequisites (PHASE-10 pattern: "Where this page and D-047 differ, D-047 wins."), covering: three PRs; JSON-only, T05/`TicketAttachment` deferred, signature without attachments; `HttpRequestResiliencePipeline` not `AddResilientHttpClient`/`ApiClientBase`; `HeaderNames`/`IntakeLimits` names; error codes in the Client; `TicketMetadataKeys` in 11b; 429 surfaced not retried; Common 0.2.0 web-neutral; GitVersion/docs-file/SourceLink deferred to 11c. Tick T01, T02, T03, T04, T06, T10, T17 with `**As built (11a):**` notes; T05 unticked "(deferred, D-047)"; tick the matching Deliverables and Success Criteria 1, 3 (partially), 5; update the D-034 risk bullet.
- [ ] **Step 6: Developer doc.** New `docs/development/CLIENT-SDK.md` (ASCII): packages and local pack; retry/idempotency semantics (incl. retention: a key replayed after the server's retention window creates a new ticket); the error-code table; test layout and Docker; never-log-the-key; redirects off; one circuit per process.
- [ ] **Step 7: Pester pins (RED first, then GREEN).** `scripts/tests/RepositoryDocs.Tests.ps1`: **remove line 546** (`Should -Not -Match '(?m)^## D-047'`); edit lines 518 to 523 (the row-10 pin) to the new text; add `Describe 'D-047 (client SDK)'` pinning the heading, status, date, index row and approval bullet; the phrases `Three pull requests`, `JSON-only`, `HttpRequestResiliencePipeline`, `TechStrapClientErrorCodes`, `NotReplayable`, `Microsoft.AspNetCore.App`, `eng/Packaging.props`, `P11-T05`; the PHASE-11 Corrections heading and ticks; roadmap/discovery rows 10 and 11; the package map no longer matching ``plain `HttpClient` ``; 02-ARCHITECTURE "11a"; the `CLIENT-SDK.md` headings.
- [ ] **Step 8: Validate and commit.** `pwsh -File scripts/Invoke-ScriptTests.ps1`; `pwsh -File scripts/Check-PackageVersions.ps1`; `git diff --cached --stat`; commit `docs: PHASE-11a close-out, D-047 and CLIENT-SDK guide`.

---

## Verification (whole PR)

```
dotnet build TechStrap.slnx -c Release                      # 0 warnings
dotnet test --solution TechStrap.CI.slnf -c Release          # Docker running
pwsh -File scripts/Invoke-ScriptTests.ps1
pwsh -File scripts/Check-PackageVersions.ps1
dotnet pack src/TechStrap.Contracts -c Release -p:Version=0.0.0-local -o <scratch> && dotnet pack src/TechStrap.Client ... && pwsh scripts/Test-PackageContents.ps1 ...
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build
```

Then `superpowers:finishing-a-development-branch` -> PR "PHASE-11a: Client SDK core (D-047)" against `main`; the PR description lists the two upstream releases (Common 0.2.0, AspNetCore.Common 0.1.16) it depends on.

## Risks / open items

- Http.Resilience 0.2.2 is unverified locally (the cache has 0.2.1); the Task 3 spike settles names and circuit defaults.
- The docker-build CI job must still pass after Contracts becomes packable (no GitVersion task added, so low risk).
- `ClientApiFactory` duplicates ~30 lines of `HostFactory` defaults; a shared `tests/Shared` move is not worth it in 11a.
- 11b must re-check `SocketsHttpHandler { AllowAutoRedirect = false }` on MAUI platform handlers, and the Common 0.2.0 dependency closes the framework-reference risk for the MAUI package.
- Step 0c waits on the owner publishing Common 0.2.0 and AspNetCore.Common 0.1.16 (PRs Syntax-Circus/SyntaxCircus.Common#5 and Syntax-Circus/SyntaxCircus.AspNetCore.Common#17); until then Tasks 2 to 6 that need the new packages cannot build.
