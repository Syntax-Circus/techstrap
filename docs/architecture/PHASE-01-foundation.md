# PHASE-01: Foundation

## Objective

A buildable, testable, containerised, CI-verified empty skeleton: every project exists with the correct reference direction, central package versions are locked, the API migrates an empty Postgres 17 database on startup, every host exposes health endpoints, and `docker compose up` brings up the stack with all four images healthy. No product behaviour is implemented here.

## Dependencies

- **Depends on:** none (discovery set approved: `00-DISCOVERY-INDEX.md`, `02-ARCHITECTURE.md`, `03-PACKAGE-MAP.md`, `04-DECISION-LOG.md`).
- **Unblocks:** [PHASE-02](PHASE-02-brand-and-ux.md) and [PHASE-03](PHASE-03-domain-and-persistence.md) (02 then runs in parallel with 03 to 06).
- **External prerequisites:** .NET 10 SDK, Docker with buildx, `dotnet-ef` tool, GitHub repository `syntax-circus/techstrap` with GHCR enabled, access to the SyntaxCircus NuGet feed at the versions in `03-PACKAGE-MAP.md`.

## Architecture Decisions

- Solution is `TechStrap.slnx`. `src/` holds `TechStrap.{Domain,Application,Infrastructure,Contracts,Api,Admin,Portal,Worker,Client,Client.Maui}`; `tests/` holds the five test projects created in this phase (`Domain.Tests`, `Application.Tests`, `Infrastructure.IntegrationTests`, `Api.Tests`, `Architecture.Tests`); `TechStrap.Admin.Tests`, `TechStrap.Portal.Tests` and `TechStrap.Client.Tests` are created in their owning phases (PHASE-07, PHASE-09, PHASE-11), as listed in `02-ARCHITECTURE.md`. Client and Client.Maui are empty skeletons here (built in PHASE-11). `Client.Maui` is excluded from the default CI build via a solution filter because it needs MAUI workloads. **Assumption.**
- Reference direction (enforced by architecture tests): `Domain` references nothing; `Contracts` references nothing (dependency-free leaf: no MVC, EF or ASP.NET, no attributes, D-016); `Application` references `Domain`, `Contracts` and `SyntaxCircus.Common`; `Infrastructure` references `Application`, `Domain` and `Contracts`; `Api` and `Worker` reference `Application`, `Infrastructure` and `Contracts` (composition roots); `Admin` and `Portal` reference `Contracts` only, never `Application`, `Infrastructure` or `Domain` (they reach data through the API); `Client` references `Contracts`; `Client.Maui` references `Client` and `Contracts`.
- Package versions are locked once in `Directory.Packages.props` (`ManagePackageVersionsCentrally`), copied from `03-PACKAGE-MAP.md`. Do not restate versions in this document. The xunit.v3 pair follows the NCrunch rule in the _template AGENT_GUIDE.md; the chosen pair and the reason are recorded in a comment in the props file.
- `global.json` selects the .NET 10 SDK and `Microsoft.Testing.Platform` as test runner (as dragon-poop). `Directory.Build.props`: nullable, implicit usings, `TreatWarningsAsErrors`. `Directory.Build.targets`: SassCompiler target (as dragon-poop) for Admin and Portal. `GitVersion.yml` copied from dragon-poop (TrunkBased, `next-version: 0.1.0`); `GitVersion.MsBuild` referenced by host projects.
- Postgres 17, snake_case naming via `SyntaxCircus.EntityFrameworkCore.Postgres`; migrations only through `dotnet ef`; migrate-on-startup runs in the API only, under an advisory lock. Worker, Admin and Portal never migrate.
- Configuration: `SyntaxCircus.DotEnv`; `.env.example` committed per host (`src/TechStrap.{Api,Admin,Portal,Worker}/.env.example`); `.env.local` gitignored and loaded by compose through `env_file` with `required: false` (dragon-poop pattern).
- Health: `/health/live` (process up, no dependencies) and `/health/ready` (Api and Worker check Postgres) via `SyntaxCircus.AspNetCore.Common`. The Worker hosts a minimal health endpoint. Admin and Portal report live only until PHASE-07/09 add an API reachability check.
- Client IP and rate limiting follow the _template `CLIENT_IP_RATE_LIMITING.md` pattern. Compose pins a product-unique subnet, `172.16.31.0/24` (owner-confirmed 2026-10-02: `172.16.0.0/16` is outside Docker's default auto-assign pool, which had already claimed every `172.17`–`172.31` /16 on the owner's machine; unused in the pattern registry, which lists sinforgiver `172.23`, the-button `172.28`, dragon-poop `172.29`, example `172.30`; the TechStrap row is a cross-repo owner action, see P01-T16 and D-019). The API trusts the pinned subnet (plus the single reverse-proxy address only when the proxy runs outside it); Admin and Portal trust only the reverse proxy. Admin/Portal typed clients use `AddForwardedClientIp()` when they land (PHASE-07/09). Never trust `172.16.0.0/12` or `0.0.0.0/0`.
- Dockerfiles at repo root (`Dockerfile.{api,admin,portal,worker}`) per spec §9: copy the whole tree before restore, BuildKit NuGet cache mount, `mcr.microsoft.com/dotnet/aspnet:10.0` runtime, non-root uid 10001, pre-created and chowned `storage`, `logs`, `dataprotection-keys`, `curl` for health checks, `ASPNETCORE_URLS=http://+:80`, build args `BUILD_VERSION`, `BUILD_INFORMATIONAL_VERSION`, `DISABLE_GITVERSION_TASK`. Admin and Portal builds assert `wwwroot/css/app.css` exists after publish (CSS is generated in the build, never committed). Dragon-poop's keyring-owner entrypoint script is replaced by build-time chown of the mount points.
- `Build-TechStrapDocker.ps1` follows spec §9 and `Build-SinForgiverDocker.ps1` / the-button script: parameters `-Targets` (default `api, admin, portal, worker`), `-ImageTag`, `-SemVerTag`, `-Registry`, `-Push`, `-PushLatest` (default true), `-NoCache`, `-Platforms` (default `linux/amd64`, `linux/arm64`), `-VersionProjectPath`. GitVersion resolves via `dotnet msbuild -target:GetVersion`, then `dotnet-gitversion`, then `gitversion`; result validated as SemVer. With `-Push` and a registry: one multi-platform `buildx --push` per image. Otherwise per-platform `--load` builds with `-amd64`/`-arm64` suffix tags (amd64 also gets canonical tags). Image names `techstrap-{api,admin,portal,worker}`. The script leaves `-Registry` empty by default; CI passes `ghcr.io/syntax-circus` (**Assumption**). A `-DryRun` switch (dragon-poop has one) makes it testable.
- Compose files follow the-button layout: `docker-compose.yml` (local: Postgres 17 plus built images), `docker-compose.uat.yml`, `docker-compose.production.yml` (GHCR images, pinned subnet, required-secret interpolation) and `.env.production.example`. TLS and reverse proxy stay outside compose.
- CI (GitHub Actions, `.github/workflows/`): `ci.yml` on PR and push to main runs restore, build, test (Testcontainers uses the runner's Docker) and `docker build` of all four images without push. `release.yml` on tag `v*` builds multi-arch images and pushes to GHCR with `GITHUB_TOKEN`. NuGet publish for the SDK is added in PHASE-11.
- Tests: xUnit v3, Shouldly, NSubstitute, Testcontainers.PostgreSql. A shared `PostgresFixture` lives in `TechStrap.Infrastructure.IntegrationTests` for reuse by later phases.
- Open source from day one: MIT `LICENSE`, `README.md`, `CONTRIBUTING.md` (Conventional Commits, test-first, EF-tool-only migrations), `SECURITY.md` (private reporting address, supported versions, scope).
- Dev data hook: `IDevelopmentDataSeeder` interface in Application, implemented in Infrastructure, invoked by the API composition root only when `ASPNETCORE_ENVIRONMENT=Development` and `TECHSTRAP_SEED_DEV_DATA=true`, after migration. Phase 01 ships a no-op implementation and the wiring; later phases add data. It is a host startup step like migrate-on-startup, not an HTTP use case. **Assumption**: exempt from the handler rule because it runs only in Development, takes no external input and has no transport outcome; listed as exempt in `02-ARCHITECTURE.md` section 7.6. Record in `04-DECISION-LOG.md` if the reviewer disagrees.

## Application Boundaries

Follow _template `APPLICATION_ARCHITECTURE.md` (not copied into this repo). No application use-case entry points exist in this phase; the handler catalog starts in PHASE-04. Everything below is a framework-owned operational or startup concern.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| `GET /health/live` (Api, Admin, Portal, Worker) | Exempt: framework health endpoint, no workflow | n/a | `SyntaxCircus.AspNetCore.Common` health mapping | 200 / 503 | Exempt (operational endpoint) |
| `GET /health/ready` (Api, Worker) | Exempt: runs registered health checks only | n/a | Npgsql connectivity check | 200 / 503 | Exempt |
| `GET /openapi/v1.json` (Api) | Exempt: framework-generated document | n/a | `Microsoft.AspNetCore.OpenApi` | 200 | Exempt |
| Static assets (Admin, Portal `wwwroot`) | Exempt: static files | n/a | ASP.NET static files | 200 / 304 / 404 | Exempt |
| API startup: migrate database | Exempt: host startup step, no request input | n/a | `SyntaxCircus.EntityFrameworkCore.Postgres` advisory-lock migrator | Startup fails fast on error | Exempt, API only |
| API startup: seed dev data | Exempt (**Assumption**, see Decisions) | `IDevelopmentDataSeeder` | No-op `DevelopmentDataSeeder` in Infrastructure | Logged only | Dev-only; revisit if it gains behaviour |

Handlers must not depend on HTTP objects, EF types, concrete infrastructure, or transport response types. Link an approved decision for every exception.

## Razor Component Boundaries

Follow _template `RAZOR_COMPONENT_ARCHITECTURE.md`. ViewModels are Razor-only and feature-local; API contracts use DTO names.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| N/A — no Razor in this phase | n/a | n/a | n/a | n/a |

Admin and Portal contain only a host shell (`Program.cs` and a placeholder root with one static page holding no logic) so the images build. Real UI starts in PHASE-07 and PHASE-09, after PHASE-02.

## Syntax Circus Packages

Exact versions live in `03-PACKAGE-MAP.md`; lock all of them in `Directory.Packages.props` in this phase.

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.Common` | `Result` / `Result<T>`, `ICurrentUserService` | Referenced by Application so architecture tests can assert handler shape from day one | Architecture tests reference the types |
| `SyntaxCircus.DotEnv` | `.env` / `.env.local` loading | `.env.example` per host | Host starts with only `.env.local` values |
| `SyntaxCircus.AspNetCore.Common` | Problem details, correlation id, security headers, health, forwarded headers | Health endpoints and trusted-proxy startup filter | `/health/live` 200; Production start fails without TrustedProxy config |
| `SyntaxCircus.AspNetCore.Serilog` | Structured logging | Logging in every host | Log line with correlation id on first request |
| `SyntaxCircus.Observability` | OpenTelemetry | Wired in every host | Exporter options bind without error |
| `SyntaxCircus.EntityFrameworkCore.Postgres` | snake_case, migrate-on-startup | Initial migration and startup migrator | `MigrationStartupTests` |
| `SyntaxCircus.Blazor.Components` | Error boundary, reconnect UI | Referenced by Admin and Portal shells | Image build succeeds; used in PHASE-07/09 |

Third-party build packages (`AspNetCore.SassCompiler`, `Microsoft.Web.LibraryManager.Build`, `GitVersion.MsBuild`) are locked here too, and the `dotnet-ef` tool is pinned in `.config/dotnet-tools.json`; the Pester module used by `BuildScriptTests` is installed in CI (version in `03-PACKAGE-MAP.md`); libman manifests are created in PHASE-02. Packages owned by later phases (`SyntaxCircus.AspNetCore.Authentication`, `Email`, `Storage`, `Http.Resilience`, `Blazor.Auth`, `Blazor.Seo`) are locked in `Directory.Packages.props` but not referenced by any project until their phase.

Record the exact package version in the linked package map. In the foundation phase, lock every selected version in `Directory.Packages.props`.

## Deliverables

- [x] `TechStrap.slnx`, 10 `src/` projects and the 5 `tests/` projects owned by this phase with the correct reference direction (the Admin, Portal and Client test projects follow in PHASE-07, 09 and 11)
- [x] `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props` (all selected versions locked), `global.json`, `GitVersion.yml`, `.editorconfig`, `.gitignore`, `.gitattributes`, `.dockerignore`
- [x] `LICENSE` (MIT), `README.md`, `CONTRIBUTING.md`, `SECURITY.md`
- [x] `.env.example` in each of Api, Admin, Portal, Worker; `.env.production.example` at root
- [x] Serilog, Observability, AspNetCore.Common and DotEnv wired in all four hosts
- [x] Health endpoints and OpenAPI document (Api)
- [x] `TechStrapDbContext` with snake_case convention and an initial empty migration generated by `dotnet ef`
- [x] Migrate-on-startup (API only) and `IDevelopmentDataSeeder` wiring
- [x] `Dockerfile.{api,admin,portal,worker}` and `Build-TechStrapDocker.ps1`
- [x] `docker-compose.yml`, `docker-compose.uat.yml`, `docker-compose.production.yml` with pinned subnet and trusted-proxy env
- [x] `.github/workflows/ci.yml` and `release.yml`
- [x] Test projects with `PostgresFixture` and `TechStrap.Architecture.Tests`

## Actionable Tasks

Test-first where a test applies: write the named test class, watch it fail, then add the code.

- [x] **P01-T01** Create `TechStrap.slnx` and the 10 `src/` and 5 `tests/` project skeletons with the reference direction above (`Admin.Tests`, `Portal.Tests` and `Client.Tests` are created later by their owning phases)
  - **Depends on:** none
  - **Validation:** `dotnet build TechStrap.slnx` exits 0 (Client.Maui excluded via filter); `dotnet sln list` shows 15 projects
- [x] **P01-T02** Add `global.json`, `Directory.Build.props`, `Directory.Build.targets`, `GitVersion.yml`, `.editorconfig`, `.gitignore`, `.gitattributes`, `.dockerignore`
  - **Depends on:** P01-T01
  - **Validation:** an unused variable fails the build (`TreatWarningsAsErrors`); `dotnet msbuild src/TechStrap.Api -target:GetVersion -getProperty:GitVersion_SemVer` prints a SemVer
- [x] **P01-T03** Write `Directory.Packages.props` locking every version from `03-PACKAGE-MAP.md`, including the xunit.v3 pair with a pin comment
  - **Depends on:** P01-T01
  - **Validation:** `dotnet restore` succeeds; `scripts/Check-PackageVersions.ps1` fails if any `PackageReference` has an inline `Version` or a props version differs from the map table
- [x] **P01-T04** Write `ProjectReferenceDirectionTests` in `TechStrap.Architecture.Tests` (Domain and Contracts reference no project; Application does not reference Infrastructure or any host; Admin and Portal reference only Contracts; Infrastructure does not reference hosts), parsing `.csproj` files
  - **Depends on:** P01-T01
  - **Validation:** `dotnet test tests/TechStrap.Architecture.Tests` passes; adding `Admin -> Application` makes `ProjectReferenceDirectionTests` fail
- [x] **P01-T05** Write `HandlerConstructorDependencyTests` and `HandlerShapeTests`: Application types ending `Handler` have a matching `I...Handler` interface; constructors take no `DbContext`, `DbSet`, `HttpContext`, `IActionResult`, `ControllerBase`, concrete Infrastructure types or persistence entities; Api controllers take handlers via `[FromServices]` only. Rules pass vacuously now; a deliberately bad fixture type in the test assembly proves each rule can fail
  - **Depends on:** P01-T04
  - **Validation:** `dotnet test --filter "FullyQualifiedName~HandlerConstructorDependencyTests"` passes and the bad fixture is flagged by each rule
- [x] **P01-T06** Write `HealthEndpointTests` (`TechStrap.Api.Tests`, `WebApplicationFactory`) for `/health/live` and `/health/ready`, then wire AspNetCore.Common, Serilog, Observability, DotEnv and health in `TechStrap.Api`
  - **Depends on:** P01-T03
  - **Validation:** `HealthEndpointTests` pass; an Api request log line contains a correlation id
- [x] **P01-T07** Wire the same cross-cutting packages and health endpoints in Admin, Portal and Worker (placeholder static page in Admin/Portal, health endpoint in Worker) and add `HostHealthSmokeTests`
  - **Depends on:** P01-T06
  - **Validation:** each host returns 200 on `/health/live` (Admin and Portal under `WebApplicationFactory`; Worker via a `dotnet run` smoke script)
- [x] **P01-T08** Add `.env.example` per host documenting every variable (connection string, `TECHSTRAP_AGENT_GROUP`, `TECHSTRAP_ADMIN_GROUP`, `TECHSTRAP_BOOTSTRAP_ADMIN`, OIDC authority/audience, `TRUSTEDPROXY__TRUSTEDNETWORKS__n`, rate-limit keys, SMTP, storage path `/app/storage`, `TECHSTRAP_PORTAL_PUBLIC_URL`, `TECHSTRAP_SEED_DEV_DATA`) and confirm `.env.local` is ignored
  - **Depends on:** P01-T07
  - **Validation:** `git check-ignore src/TechStrap.Api/.env.local` returns the path; `EnvExampleCompletenessTests` asserts every key bound by an options class appears in the matching `.env.example`
- [x] **P01-T09** Add `TechStrapDbContext` (empty model, snake_case convention), register it in Api and Worker, and write `MigrationStartupTests` against Testcontainers (empty Postgres 17 migrates, second run is a no-op, two concurrent startups serialise on the advisory lock)
  - **Depends on:** P01-T03
  - **Validation:** `dotnet test tests/TechStrap.Infrastructure.IntegrationTests --filter MigrationStartupTests` passes with Docker running
- [x] **P01-T10** Generate the initial empty migration with `dotnet ef migrations add Initial` and add the migrator call to Api startup only
  - **Depends on:** P01-T09
  - **Validation:** files come from the tool (generated header, no manual edits in the PR diff); `dotnet ef migrations has-pending-model-changes` exits 0; a startup test asserts Worker, Admin and Portal do not call the migrator
- [x] **P01-T11** Create `PostgresFixture` and `PostgresIntegrationTestBase` (container start, migrate, per-test reset) for reuse by PHASE-03 onward
  - **Depends on:** P01-T09
  - **Validation:** `PostgresFixtureSmokeTests` runs two tests sharing one container in under 60 s
- [x] **P01-T12** Add `IDevelopmentDataSeeder`, the no-op implementation and `DevSeedGatingTests` (runs only in Development with `TECHSTRAP_SEED_DEV_DATA=true`, never in Production)
  - **Depends on:** P01-T10
  - **Validation:** `DevSeedGatingTests` pass for all four environment/flag combinations
- [x] **P01-T13** Add forwarded-headers and public rate-limit scaffolding in Api per `CLIENT_IP_RATE_LIMITING.md` (`AddTrustedProxyForwardedHeaders`, validated `RateLimiting:Public` options with `ValidateOnStart`, default-deny fallback policy, `[AllowAnonymous]` on health only) with `TrustedProxyStartupTests` and `PublicRateLimitOptionsTests`
  - **Depends on:** P01-T06
  - **Validation:** tests show a bad `PermitLimit` fails boot and Production without trusted-proxy config fails startup; a request past the limit from one fake IP gets 429 while another IP is unaffected (uses the checked-in default `192.0.2.0/24` via `IStartupFilter`, no in-memory override of `TrustedProxy`)
- [x] **P01-T14** Write the four Dockerfiles per Decisions
  - **Depends on:** P01-T07
  - **Validation:** `docker build -f Dockerfile.api .` (and admin, portal, worker) succeeds; `docker run --rm --entrypoint id <image> -u` prints 10001; admin and portal builds fail if `wwwroot/css/app.css` is absent
- [x] **P01-T15** Write `Build-TechStrapDocker.ps1` per spec §9 with a `-DryRun` switch
  - **Depends on:** P01-T14
  - **Validation:** `./Build-TechStrapDocker.ps1 -DryRun` prints four `docker buildx build` commands carrying `BUILD_VERSION`, `BUILD_INFORMATIONAL_VERSION` and `DISABLE_GITVERSION_TASK=true`; Pester `BuildScriptTests` cover tag selection, SemVer rejection (`-ImageTag not-semver` throws) and `-amd64`/`-arm64` suffixing; a real local build tags `techstrap-api:<semver>` and `:latest`
- [x] **P01-T16** Write `docker-compose.yml`, `docker-compose.uat.yml`, `docker-compose.production.yml` and `.env.production.example` with pinned subnet `172.16.31.0/24`, Postgres 17, health checks, API trusting proxy plus subnet, Admin/Portal trusting only the proxy
  - **Depends on:** P01-T14, P01-T08
  - **Owner action (cross-repo, D-019):** open a PR in `_template` adding the TechStrap `172.16.31.0/24` row to the `CLIENT_IP_RATE_LIMITING.md` subnet registry; confirm the subnet is free on the UAT host. The compose files mount the named volume `techstrap-storage` at `/app/storage` on both `api` and `worker`.
  - **Validation:** `docker compose config` shows the subnet and resolved `TRUSTEDPROXY__*` per host; `docker compose up -d` reaches all services healthy and the API `/health/ready` returns 200; the production file refuses to resolve without `POSTGRES_PASSWORD`
- [x] **P01-T17** Add `.github/workflows/ci.yml`: restore, build, test (Testcontainers), then `docker build` of four images on PR
  - **Depends on:** P01-T14, P01-T11
  - **Validation:** a throwaway PR shows a green run including integration tests; adding a failing architecture test turns it red
- [x] **P01-T18** Add `.github/workflows/release.yml`: on tag `v*`, GHCR login with `GITHUB_TOKEN` then `Build-TechStrapDocker.ps1 -Push -Registry ghcr.io/syntax-circus`
  - **Depends on:** P01-T15, P01-T17
  - **Validation:** tag `v0.1.0-rc.1` publishes four images with the SemVer tag (`latest` moves only on stable tags); `docker buildx imagetools inspect` lists amd64 and arm64
- [x] **P01-T19** Add `LICENSE` (MIT), `README.md` (what it is, compose quick start, link to `docs/architecture`), `CONTRIBUTING.md`, `SECURITY.md`
  - **Depends on:** P01-T01
  - **Validation:** GitHub detects the MIT licence; README quick-start commands run verbatim on a clean clone; `SECURITY.md` shows in the repo Security tab
- [x] **P01-T20** Run a clean-clone verification (`dotnet build`, `dotnet test`, `docker compose up`, health checks) and mark PHASE-01 complete in `00-DISCOVERY-INDEX.md`
  - **Depends on:** P01-T16, P01-T17, P01-T19
  - **Validation:** every Success Criteria item below is ticked, with command output pasted in the PR description

## Success Criteria

- [x] `dotnet build TechStrap.slnx -c Release` exits 0 with warnings as errors.
- [x] `dotnet test` passes with Docker running, including `ProjectReferenceDirectionTests`, `HandlerConstructorDependencyTests` and `MigrationStartupTests`.
- [x] `Directory.Packages.props` contains every package version in `03-PACKAGE-MAP.md` and no project has an inline `Version` (`scripts/Check-PackageVersions.ps1` exits 0).
- [x] `docker compose up -d` brings Postgres 17 and four app containers healthy; `/health/live` returns 200 on all four and `/health/ready` returns 200 on Api and Worker.
- [x] The database has `__EFMigrationsHistory` containing exactly the `Initial` migration, produced by `dotnet ef`.
- [ ] `./Build-TechStrapDocker.ps1` builds all four images locally; `-Push -Registry ghcr.io/syntax-circus` works from CI on a tag. Local build verified; the CI tag push is pending the first tag run (owner).
- [ ] CI is green on a PR (build, test, docker build) and publishes images to GHCR on a `v*` tag. Pending first PR/tag run (owner); on a tag, `latest` moves only for stable tags.
- [x] `docker compose config` shows the pinned subnet `172.16.31.0/24` and trusted-proxy variables.
- [x] `LICENSE`, `README.md`, `CONTRIBUTING.md`, `SECURITY.md` and four `.env.example` files exist; `.env.local` is gitignored.
- [x] No compiled CSS is tracked (`git ls-files '*/wwwroot/css/*'` returns nothing).

## Boundary Validation

- [x] Application use-case entry points delegate to the named handlers listed above. (None exist; architecture tests are in place for later phases.)
- [x] Framework-owned operational or static exemptions execute no application workflow.
- [x] Handler constructor dependencies contain only approved abstractions (`HandlerConstructorDependencyTests`).
- [x] Persistence and integration entities do not cross infrastructure boundaries.
- [x] Cancellation reaches asynchronous handler dependencies. (N/A until handlers exist; the test convention is set here.)
- [x] Expected outcomes and transport mapping have focused tests. (N/A until PHASE-04.)
- [x] Infrastructure implementations have integration coverage where applicable (`MigrationStartupTests`).
- [x] Inline Razor components contain only simple parameters and, at most, one
      trivial synchronous `EventCallback`-forwarding callback. (N/A, placeholder shell only.)
- [x] Every component beyond the inline ceiling uses paired `.razor` and
      `.razor.cs` files, with all C# in code-behind. (N/A.)
- [x] Each Razor ViewModel is feature-local and presentation-only; the recorded
      direct-model decision does not expose an API ViewModel. (N/A.)
- [x] A factory or presentation service is used only for non-trivial mapping,
      asynchronous assembly, or multiple dependencies. (N/A.)
- [x] API request and response contracts use DTO names and contracts, never
      Razor ViewModels. (N/A.)
- [x] Repeated or business-meaningful literals are named constants at the
      right scope, not bare magic values (env var names, health paths, image names and uid 10001 live in constants or script variables).
- [x] Duplicated-looking logic across flows was evaluated for genuine
      divergence before extracting (or intentionally not extracting) a shared
      abstraction (the four near-identical Dockerfiles stay separate; the build script owns shared logic).

## Risks and Open Questions

- [x] Client.Maui needs MAUI workloads and may break or slow CI; mitigated by the solution filter (`TechStrap.CI.slnf`) and a plain `net10.0` placeholder project, install deferred to PHASE-11.
- [ ] `linux/arm64` builds under QEMU on GitHub runners are slow; consider native arm64 runners or arm64 on release only.
- [x] xunit.v3 pin depends on the owner's NCrunch version; confirm before locking (AGENT_GUIDE known constraints). Resolved 2026-10-02: the owner's NCrunch 5.23 runs the 4.0.x pair; pinned in `Directory.Packages.props`.
- [ ] Subnet `172.16.31.0/24` must be checked against the owner's UAT host and added to the _template pattern registry.
- [ ] The seed-hook exemption from the handler rule is an **Assumption**; confirm or record a decision.
- [ ] Whether Admin/Portal `/health/ready` should probe the API (deferred to PHASE-07/09).
- [x] Security reporting address for `SECURITY.md` is not yet chosen. Resolved 2026-10-02: GitHub private vulnerability reporting, no email address (the owner enables it in repository settings).

## Handoff

Before PHASE-02 and PHASE-03 start: `main` is green in CI, `docker compose up` works from a clean clone, `Directory.Packages.props` and the architecture tests are merged, and the `Initial` migration is committed. PHASE-02 (brand and UX) then runs in parallel with PHASE-03 to PHASE-06. Next: [PHASE-02-brand-and-ux.md](PHASE-02-brand-and-ux.md) and [PHASE-03-domain-and-persistence.md](PHASE-03-domain-and-persistence.md).
