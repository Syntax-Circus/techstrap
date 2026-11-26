# TechStrap Aspire Local Stack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One `aspire run` gives a signed-in, seeded, fully self-contained TechStrap on ports 55200-55299.

**Architecture:** A dev-only AppHost in `tools/` runs Postgres, Mailpit, Authentik (with a committed blueprint), a webhook catcher and the four hosts, and passes the hosts all their configuration as environment variables. The only shipped-code change is a Debug-only switch that sends the Worker's webhook HTTP traffic to the catcher.

**Tech Stack:** .NET 10, Aspire 17.0.0, Authentik 2026.8.5, Postgres 17, Mailpit v1.31.4, xUnit v3, Shouldly, Pester 6.2.0.

**Spec:** `docs/superpowers/specs/2026-11-26-aspire-local-stack-design.md`

**Templates to port from** the sibling reference implementation (read them; do not copy product-specific parts): its AppHost (`LocalStackPorts.cs`, `LocalStackComposition.cs`, `HostWiring.cs`, `Authentik\`, csproj, `Properties\launchSettings.json`), its LocalStack tests (`AppHostInfrastructureTests.cs`, `AppHostWiringTests.cs`, `LocalStackSmokeTests.cs`, `KillOnCloseJob.cs`), its `Reset-LocalStack.ps1` and its spike findings.

## Global Constraints

- Work on branch `feat/aspire-local-stack`. Where the spike findings (Task 0) differ from this plan, the findings win.
- Do not change `docker-compose.yml`, anything under `deploy/`, `WebhookPolicy.cs`, `WebhookSender.cs`, `GetCurrentAgentRequestHandler.cs`, or the existing package pins. Add no CI jobs or steps.
- New projects: `tools/TechStrap.AppHost`, `tools/TechStrap.WebhookCatcher`, `tests/TechStrap.LocalStack.Tests`, all `IsPackable=false`, listed in both `TechStrap.slnx` and `TechStrap.CI.slnf`. No `src/` project references `tools/`.
- New packages: `Aspire.Hosting.AppHost`, `Aspire.Hosting.Dotnet`, `Aspire.Hosting.PostgreSQL` 17.0.0, each in `Directory.Packages.props` and as a Selected row in `docs/architecture/03-PACKAGE-MAP.md`, same commit. No inline versions. No direct `Microsoft.Extensions.*` references from the new projects.
- Ports: dashboard 55200, OTLP 55201, resource service 55202, Postgres 55210, Mailpit SMTP 55220 / UI 55221, Api 55230 https / 55231 http, Admin 55240 / 55241, Portal 55250 / 55251, Worker 55260 http, webhook catcher 55270 http, Authentik 55290 http.
- Capture setting key: `TECHSTRAP_WEBHOOK_CAPTURE_URL`. It appears in no appsettings file, env example, deploy template or compose file.
- Documentation is US English.
- Warnings are errors. Tests: `dotnet test --project <csproj> -c Release` per task (Microsoft.Testing.Platform); the whole `TechStrap.CI.slnf` only in Task 8.

## Review Focus

1. A mistyped or remote capture URL (https, public host, credentials, query): the Worker refuses to start; nothing is sent anywhere (Task 1).
2. A ticket title containing `<script>` reaching the catcher page: it shows as text (Task 2).
3. A stray `.env`/`.env.local` at the repo root or beside a project: it has no effect on the stack (Task 5).
4. Signing in as Sam or Riley: they become the seeded agents, never duplicates, also after a restart (Tasks 0 and 4).
5. Running the reset script while compose volumes and sibling stacks exist: only this stack's resources go (Task 6).

---

### Task 0: Spikes (blocks Tasks 4 and 5; Task 3 needs 0b)

Throwaway code lives in the scratchpad, never in the solution. Needs Docker running.

**Files:** Create `docs/superpowers/spikes/2026-11-26-aspire/findings.md` and `authentik-blueprint.yaml` (the verified copy).

- [ ] **0a Authentik.** Start Postgres 17 and Authentik 2026.8.5 (server + worker) from empty volumes with throwaway containers, applying a blueprint ported from the sibling reference implementation to the spec's "Authentik blueprint" section. Get a token by authorization code flow (a script driving the flow API, or one browser sign-in). Record: `sub` is exactly `dev|sam` in the ID token, access token **and** userinfo; the access token has `email`, `name`, `groups`, `aud=techstrap`, `iss=http://localhost:55290/application/o/techstrap/`; refresh keeps those claims; restarting the worker re-applies without error; the discovery document appears only after the blueprint applies. If `sub_mode: user_upn` fails, try a scope mapping that returns `sub`; if that also fails, **stop and ask the user**.
- [ ] **0b Aspire in this repo.** In a scratch copy of the repo (`git worktree`), add a minimal AppHost under `tools/` that runs Postgres and the four hosts. Record: restore passes with NuGet audit; `Mvc.Testing` 10.0.13 coexists with Aspire in one test project; a Release model test passes with neither Aspire CLI nor DCP on PATH (note how this was checked); a clean concurrent first build of the hosts works (GitVersion, libman, Sass); whether `AddDotnetProject` builds the apps in Debug; OTLP reaches the dashboard through `OpenTelemetry__*`; the exact persistent network name.
- [ ] **0c Sign-in in a browser.** With 0a's Authentik, run the real Api and Admin on `https://localhost:55230` / `:55240` with environment variables only (`DotEnv__Enabled=false`, Task 5's values). Verify sign-in, `/api/agents/me` succeeds as seeded Sam (one agent row for `sam@example.com`), token refresh, sign-out, and that the next sign-in asks for credentials again. Note any CSP or cookie error. Use Claude in Chrome if connected, else ask the user to click through. Fallback: a Development-only setting, or https for Authentik; ask the user before changing shipped code.
- [ ] Write `findings.md` (what held, what changed), remove spike containers, volumes and the worktree, commit `docs: record Aspire local stack spike findings`.

### Task 1: Webhook capture switch

**Files:**
- Create: `src/TechStrap.Infrastructure/Webhooks/LocalCapture/WebhookCaptureRegistration.cs`
- Modify: `src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj`, `src/TechStrap.Worker/Program.cs` (the `IWebhookSender` line)
- Test: `tests/TechStrap.Infrastructure.IntegrationTests/WebhookCaptureTests.cs`, `tests/TechStrap.Api.Tests/Config/WorkerWebhookCaptureTests.cs`, `tests/TechStrap.Architecture.Tests/WebhookCaptureAbsenceTests.cs`

**Interfaces (produces), all inside `#if DEBUG`:**
```csharp
namespace TechStrap.Infrastructure.Webhooks.LocalCapture;
public static class WebhookCaptureRegistration
{
    public const string ConfigurationKey = "TECHSTRAP_WEBHOOK_CAPTURE_URL";
    public const string OriginalHostHeader = "X-TechStrap-Capture-Original-Host";
    public static IServiceCollection AddTechStrapWebhookSender(this IServiceCollection services); // singleton IWebhookSender; reads config lazily
    internal static WebhookSender CreateSender(string? captureUrl, bool isDevelopment, ILogger logger, HttpMessageHandler? transport = null);
}
```
Blank URL returns `new WebhookSender()`. Otherwise it uses `new WebhookSender(handler)` where an internal `DelegatingHandler` sets `RequestUri` to the capture URL and adds the original **host only** in `OriginalHostHeader` (remove any existing value first). Default transport: `SocketsHttpHandler` with no redirect, proxy or cookies, `ActivityHeadersPropagator = null`. Accept only `http`, host `localhost` or a loopback IP, empty user info, query and fragment; otherwise throw `InvalidOperationException` naming the key and never echoing the value. Not Development: throw. Log one warning when capture is on.

- [ ] **Step 1: Write failing tests.**
  - `WebhookCaptureTests` (whole file in `#if DEBUG`), recording transport: `Generic_delivery_goes_to_the_capture_url_with_only_the_original_host` (destination `https://hooks.example.com/services/SECRET?token=x`; assert request URI equals the capture URL, header is `hooks.example.com`, no header or URI contains `SECRET`, `X-TechStrap-Event-Id` present); `Discord_delivery_keeps_its_Discord_body` (body has `allowed_mentions`); `Rejected_capture_urls_throw` theory: `https://localhost:55270/capture`, `http://example.com/capture`, `http://192.168.1.10/capture`, `http://user:p@localhost/capture`, `http://localhost/capture?x=1`, `http://localhost/capture#a`, `not a url`; `Capture_outside_Development_throws`; `Private_destinations_stay_invalid_with_capture_on` (`https://127.0.0.1/x` gives result `destination-invalid`, transport never called).
  - `WorkerWebhookCaptureTests`, using `ConfigHosts.TryStartAsync(HostKind.Worker, …)` with the settings `WorkerFactory` uses: Release (`#if !DEBUG`) `Release_worker_refuses_a_capture_url` (Development, key set: exception message contains the key); both configurations `Worker_starts_without_a_capture_url`; Debug `Debug_worker_refuses_capture_in_Production`.
  - `WebhookCaptureAbsenceTests`: under `#if DEBUG` assert the type exists; under `#else` assert no type in the Infrastructure or Worker assemblies is in namespace `…Webhooks.LocalCapture` or has `Capture` in a name under a `Webhooks` namespace except the existing `WebhookCapture` and its nested types (outbox staging, which stays). `Capture_source_is_debug_only` (both): first non-blank line of the file is `#if DEBUG`, last is `#endif`; csproj has `Compile Remove="Webhooks/LocalCapture/**"` conditioned on non-Debug; every `Dockerfile.*` `dotnet publish` line has `-c Release`.
- [ ] **Step 2:** Run the three projects in `-c Debug`; expect compile failures.
- [ ] **Step 3: Implement.** In `Program.cs`: Debug calls `builder.Services.AddTechStrapWebhookSender()` and, after `Build()`, resolves `IWebhookSender` once so bad settings fail startup. Release keeps today's registration and after `Build()` throws if `app.Configuration["TECHSTRAP_WEBHOOK_CAPTURE_URL"]` is non-blank ("works only in Debug builds").
- [ ] **Step 4:** Run the new tests in **both** `-c Debug` and `-c Release`; all pass. Existing `WebhookSenderTests` and `WebhookPolicyTests` still pass.
- [ ] **Step 5:** Commit `feat(worker): Debug-only webhook capture for the local stack`.

### Task 2: Webhook catcher and the LocalStack test project

**Files:**
- Create: `tools/TechStrap.WebhookCatcher/{TechStrap.WebhookCatcher.csproj, Program.cs, CaptureStore.cs, CapturePage.cs, Properties/launchSettings.json}`, `tests/TechStrap.LocalStack.Tests/{TechStrap.LocalStack.Tests.csproj, WebhookCatcherTests.cs}`
- Modify: `TechStrap.slnx` (new `/tools/` folder), `TechStrap.CI.slnf`

**Interfaces (produces):** `record CapturedRequest(DateTimeOffset ReceivedAt, string OriginalHost, string? EventId, string ContentType, string Body)`; `CaptureStore` with `Capacity = 200`, `MaxBodyBytes = 65536`, `Add`, `Snapshot()` (newest first), `Clear()`; marker `namespace TechStrap.WebhookCatcher { public partial class Program; }`; `aspire` launch profile `http://localhost:55270`.
Routes: `POST /capture` 204 (413 over the limit), `GET /` HTML list, `GET /api/requests` JSON, `POST /clear`, `GET /health`. The project has no package references and does not reference `src/`; it reads the headers named in Task 1.

- [ ] **Step 1: Failing tests** with `WebApplicationFactory<TechStrap.WebhookCatcher.Program>`: `Capture_stores_host_event_id_and_body` (204; shows in `/api/requests`); `Only_the_newest_200_are_kept` (post 201); `Oversized_body_is_rejected` (65537 bytes: 413, nothing stored); `Page_encodes_captured_values` (body and host contain `<script>alert(1)</script>`: page lacks `<script>alert`, contains `&lt;script&gt;`); `Clear_empties_the_list`; `Refuses_to_start_outside_Development`; `Non_loopback_callers_get_403` (set `RemoteIpAddress` through a test startup filter; a null address, as in TestServer, is allowed).
- [ ] **Step 2:** Run; expect failures. **Step 3:** Implement. **Step 4:** Run in Release; pass. **Step 5:** `pwsh ./scripts/Check-PackageVersions.ps1` passes; commit `feat(tools): local webhook catcher`.

### Task 3: AppHost skeleton and ports

**Files:**
- Create: `tools/TechStrap.AppHost/{TechStrap.AppHost.csproj, AppHost.cs, LocalStackPorts.cs, LocalStackOptions.cs, LocalStackComposition.cs, appsettings.Development.json, Properties/launchSettings.json}`, `aspire.config.json` (repo root, path `tools/TechStrap.AppHost/TechStrap.AppHost.csproj`), `tests/TechStrap.LocalStack.Tests/{LocalStackPortsTests.cs, LocalStackOptionsTests.cs}`
- Modify: `Directory.Packages.props`, `docs/architecture/03-PACKAGE-MAP.md`, `.dockerignore` (add `tools/`), `TechStrap.slnx`, `TechStrap.CI.slnf`, the LocalStack test csproj

**Interfaces (produces):**
```csharp
public static class LocalStackPorts { Dashboard, DashboardOtlp, ResourceService, Postgres, MailpitSmtp, MailpitUi,
    ApiHttps, ApiHttp, AdminHttps, AdminHttp, PortalHttps, PortalHttp, Worker, WebhookCatcher, Authentik } // public const int
public sealed record LocalStackOptions(bool SeedDevData) { public static LocalStackOptions FromConfiguration(IConfiguration configuration); } // key "LocalStack:SeedDevData"
public static void AddLocalStack(this IDistributedApplicationBuilder builder, string repositoryRoot); // LocalStackComposition; empty here. Task 5 changes the return type to LocalApplications
```
The csproj follows the sibling reference implementation's (plain SDK, `IsAspireHost`, a **new** `UserSecretsId` GUID, `InternalsVisibleTo` the tests), without the transitive-pinning opt-out. `AppHost.cs`: `#if !DEBUG` throws "The local stack runs only in Debug builds." before building anything; the check is not in `LocalStackComposition`.

- [ ] **Step 1: Failing tests:** `Ports_are_unique_and_inside_the_block` (all in 55200..55299, 15 distinct); `AppHost_launch_profile_matches_the_ports` (55200, 55201, 55202); `Aspire_config_points_at_the_AppHost`; `SeedDevData` theory: missing, blank, `true`, `TRUE` → true; `false` → false; `yes` → throws naming `LocalStack:SeedDevData`; `No_src_project_references_tools`.
- [ ] **Step 2-4:** Fail, implement, pass in Release.
- [ ] **Step 5:** Run `pwsh ./scripts/Check-PackageVersions.ps1`, `dotnet list TechStrap.slnx package --vulnerable --include-transitive` (clean), `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`. Commit `feat(tools): Aspire AppHost skeleton and port block`.

### Task 4: Postgres, Mailpit and Authentik

**Files:**
- Create: `tools/TechStrap.AppHost/Authentik/{AuthentikResources.cs, blueprints/techstrap.yaml}`, `tests/TechStrap.LocalStack.Tests/{AppHostInfrastructureTests.cs, BlueprintTests.cs}`
- Modify: `LocalStackComposition.cs`, `appsettings.Development.json`

**Interfaces (produces):** `record AuthentikSecrets(SecretKey, BootstrapPassword, SamPassword, RileyPassword, CaseyPassword, ClientSecret)` (all `IResourceBuilder<ParameterResource>`); `AuthentikResources.AddAuthentik(builder, db, secrets)` returning the server; consts `Slug = ClientId = "techstrap"`. Parameters in `appsettings.Development.json`: `authentik-akadmin-password`, `authentik-sam-password`, `authentik-riley-password`, `authentik-casey-password`, all `TechStrap-Local-Dev-2026`. Generated and persisted: `postgres-password`, `authentik-secret-key`, `authentik-client-secret`. Postgres user `techstrap`; databases `techstrap-db`→`techstrap`, `authentik-db`→`authentik`. Volumes `techstrap-local-postgres`, `techstrap-local-authentik-media`. Worker blueprint inputs: `TS_ADMIN_ORIGIN`, `TS_CLIENT_SECRET`, `TS_SAM_PASSWORD`, `TS_RILEY_PASSWORD`, `TS_CASEY_PASSWORD`.

Start the blueprint from the verified spike copy. Users `sam`, `riley`, `casey` per the spec; `casey` (`casey@example.com`) has no group and no `upn`. Top comment: local only; `user_upn` deliberately differs from `docs/self-hosting/AUTHENTIK.md`.

- [ ] **Step 1: Failing tests** (model inspection as in the sibling reference implementation's `AppHostInfrastructureTests`): image tags `17`, `v1.31.4`, `2026.8.5`; volume names and persistent lifetime; fixed host ports; Mailpit `/readyz`; server has both health checks; worker waits for server **start** only; blueprint mounted read-only into the worker. `BlueprintTests` (text/YAML assertions): `sub_mode: user_upn`, `issuer_mode: per_provider`, `default-invalidation-flow`, both grant types, four scope mappings, strict redirect and logout URIs from `TS_ADMIN_ORIGIN`, no literal password or client secret. `Seeded_logins_match_the_seeder`: read `src/TechStrap.Infrastructure/Seeding/DevelopmentDataSeeder.cs` as text and assert each of `dev|sam`, `Sam Whitfield`, `sam@example.com`, `dev|riley`, `Riley Chen`, `riley@example.com` appears in both it and the blueprint, and that Sam is in `techstrap-admins`, Riley in `techstrap-agents`.
- [ ] **Step 2-4:** Fail, implement, pass in Release.
- [ ] **Step 5:** `dotnet run --project tools/TechStrap.AppHost` from empty volumes: postgres, mailpit, authentik server and worker go healthy. Commit `feat(tools): local Postgres, Mailpit and Authentik`.

### Task 5: Wire the applications

**Files:**
- Create: `tools/TechStrap.AppHost/HostWiring.cs`, `Properties/launchSettings.json` in `src/TechStrap.{Api,Admin,Portal,Worker}` (one `aspire` profile each, `Development`, ports from the constraints), `tests/TechStrap.LocalStack.Tests/AppHostWiringTests.cs`
- Modify: `LocalStackComposition.cs`, the AppHost csproj (`<Compile Include="..\..\src\TechStrap.Infrastructure\Seeding\DevelopmentApiKeys.cs" Link="Seeding\DevelopmentApiKeys.cs" />`), `LocalStackPortsTests.cs`

**Interfaces (produces):** `record LocalApplications(Api, Worker, Admin, Portal, WebhookCatcher, SampleClient)` (`IResourceBuilder<DotnetProjectResource>`). Resource names `api`, `worker`, `admin`, `portal`, `webhook-catcher`, `sample-client`. All hosts use the `aspire` profile.

Environment (exact). Port the OTLP callback from `HostWiring.WithLocalHostDefaults`, plus `OpenTelemetry__TracesSampleRate=1`, `OpenTelemetry__Environment=local`. "State" is `<repo>/local/.state`. Containers are reached at `127.0.0.1`; issuer URLs keep `localhost`. Never derive a public URL from a process's listening port.

| Host | Settings |
|---|---|
| all four | `DotEnv__Enabled=false`, `Sentry__Dsn=` |
| api | `ConnectionStrings__TechStrap` (127.0.0.1:55210), `Authentication__JwtBearer__Authority=http://localhost:55290/application/o/techstrap/`, `…__Audiences__0=techstrap`, `…__RequireHttpsMetadata=false`, `TECHSTRAP_PORTAL_PUBLIC_URL=https://localhost:55250`, `TECHSTRAP_API_PUBLIC_URL=https://localhost:55230`, `TECHSTRAP_ADMIN_PUBLIC_URL=https://localhost:55240`, `Storage__Local__RootPath=<state>/api/storage`, `TECHSTRAP_SEED_DEV_DATA`, `TrustedProxy__TrustedNetworks__0=127.0.0.1/32`, `__1=::1/128` |
| worker | connection string, admin and api public URLs, `Email__Smtp__Host=127.0.0.1`, `__Port=55220`, `__TlsMode=None`, `__Username=`, `__Password=`, `__DefaultFrom=support@techstrap.localhost`, `__MaxRetryAttempts=1`, `__RetryMode=TransientOnly`, `__TotalSendTimeout=00:00:30`, `TECHSTRAP_WEBHOOK_CAPTURE_URL=http://127.0.0.1:55270/capture` |
| admin | `Auth__Authority` (as Api), `Auth__ClientId=techstrap`, `Auth__ClientSecret` (parameter), `Api__BaseUrl=https://localhost:55230/`, portal public URL, `DataProtection__KeyRingPath=<state>/dataprotection/admin` |
| portal | `Api__BaseUrl`, portal public URL, key ring `<state>/dataprotection/portal` |
| sample-client | `TechStrap__BaseAddress=https://localhost:55230/`, `TechStrap__ApiKey=DevelopmentApiKeys.OrbitlyTrusted` only when seeding (else a logged warning), `.WithExplicitStart()` |

Health: api and worker `/health/ready`, admin and portal `/health/live`, catcher `/health`. Waits per the spec's Resources table (worker waits for a **healthy** api).

- [ ] **Step 1: Failing tests** (style of `AppHostWiringTests`): one test per row asserting its exact values; `Every_host_ignores_dotenv_files`; seed switch true and false (value of `TECHSTRAP_SEED_DEV_DATA`; sample key present/absent); sample is explicit-start; wait relationships; extend ports test: all five app launch profiles match `LocalStackPorts`; `No_committed_config_names_the_capture_key` (scan appsettings, `.env*example`, `deploy/`, `docker-compose.yml`).
- [ ] **Step 2-4:** Fail, implement, pass in Release. `pwsh ./scripts/Invoke-ScriptTests.ps1` passes.
- [ ] **Step 5: Run it.** From reset state `aspire run`; all resources healthy; seeded data on the Portal; the sample starts from the dashboard and exits 0; its email is in Mailpit; save a webhook `https://hooks.example.com/test` in Admin, send the test, see it in the catcher; with a root `.env` containing `API__BASEURL=http://invalid.invalid/`, nothing changes. Commit `feat(tools): run the TechStrap applications under Aspire`.

### Task 6: Reset script

**Files:** Create `scripts/Reset-LocalStack.ps1`, `scripts/tests/Reset-LocalStack.Tests.ps1`

Port the sibling reference implementation's script with its safeguards and `-Secrets`, `-WhatIf`, `-Force`. Containers `^(postgres|mailpit|authentik-server|authentik-worker)-[0-9a-f]{8}$`, only with a suffix proven by this stack's volumes or network; volumes exactly `techstrap-local-postgres` and `techstrap-local-authentik-media`; network `aspire-persistent-network-<hash>-TechStrap` (confirm against Task 0b); `local/.state`; secrets by the AppHost's `UserSecretsId` only; refuse while port 55200 is in use.

- [ ] **Step 1: Failing Pester tests,** following how the sibling script and this repo's `scripts/tests` isolate logic, with fake Docker output: selects this stack's four containers; **never** selects `techstrap-api-1`, `techstrap-postgres-1`, volumes `techstrap_pgdata`/`techstrap_techstrap-storage`, a sibling's `postgres-deadbeef`, or `otherapp-local-postgres`; `-WhatIf` removes nothing; `.dockerignore` contains `tools/`.
- [ ] **Step 2-4:** Fail, implement, `pwsh ./scripts/Invoke-ScriptTests.ps1` passes.
- [ ] **Step 5:** `pwsh ./scripts/Reset-LocalStack.ps1 -WhatIf` on this machine lists only this stack's resources; then a real reset and a cold start succeed. Commit `feat(scripts): reset the Aspire local stack`.

### Task 7: Opt-in smoke test

**Files:** Create `tests/TechStrap.LocalStack.Tests/{LocalStackSmokeTests.cs, KillOnCloseJob.cs}`

Port from the sibling reference implementation: skip unless `TECHSTRAP_LOCALSTACK_SMOKE=1`; run the AppHost as a **child process** in Debug with `ASPIRE_SUPPRESS_CLI_RUN_HOOK=true` (the testing builder would randomize ports); kill the whole tree and wait.

- [ ] **Step 1:** Write the test: Authentik discovery 200 with the expected issuer; `/health/ready` on Api and Worker; Portal `/p/orbitly` 200; an unauthenticated Admin request redirects to the Authentik authorize URL; submit a ticket with `DevelopmentApiKeys` Orbitly trusted key (read the value from the source file; no Infrastructure reference) and a unique subject; poll Mailpit `http://127.0.0.1:55221/api/v1/messages` until it appears. Assume seeding is on.
- [ ] **Step 2:** Without the variable: reported skipped. **Step 3:** With it: passes from cold and from warm, and no AppHost or app process is left. **Step 4:** Commit `test(tools): opt-in local stack smoke test`.

### Task 8: Documentation and final verification

**Files:** Create `docs/development/LOCAL-STACK.md`. Modify `docs/architecture/04-DECISION-LOG.md` (D-059, confirm it is next: placement in `tools/`, Debug-only capture, compose stays), `README.md`, `CONTRIBUTING.md`, `docs/development/{DEV-DATA,ADMIN-APP,PORTAL-APP,WEBHOOKS}.md`, `docs/self-hosting/AUTHENTIK.md` (the "no blueprint ships" line becomes "none for deployment").

- [ ] **Step 1:** `LOCAL-STACK.md`: prerequisites (Docker, .NET SDK, Aspire CLI install, `dotnet dev-certs https --trust`), `aspire run`, URL/port table, logins, demo-data switch (`dotnet user-secrets set "LocalStack:SeedDevData" false --project tools/TechStrap.AppHost`), sample client, webhook capture, reset, smoke test, relation to compose, troubleshooting (shared `localhost` cookies with sibling stacks; blueprint edits need a worker restart; new `aspire` profiles change plain `dotnet run`). Check the `--urls` commands in `ADMIN-APP.md`/`PORTAL-APP.md` still behave as written and say so there.
- [ ] **Step 2:** Run and record output: `dotnet test --solution TechStrap.CI.slnf -c Release`; `-c Debug` on Infrastructure.IntegrationTests, Api.Tests and Architecture.Tests filtered to the capture tests; `pwsh ./scripts/Invoke-ScriptTests.ps1`; `pwsh ./scripts/Check-PackageVersions.ps1`; the vulnerability command from `.github/workflows/ci.yml`; `docker compose up -d --build` becomes healthy, then `docker compose down` (no `-v`).
- [ ] **Step 3:** From reset, cold `aspire run`, walk the spec's Verification list (Sam, Riley, Casey, sign-out, no duplicate agents after restart, KB images, dashboard telemetry, smoke test). Report anything not checked.
- [ ] **Step 4:** Commit `docs: Aspire local stack (D-059)`.
