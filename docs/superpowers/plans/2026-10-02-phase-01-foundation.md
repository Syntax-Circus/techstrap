# TechStrap PHASE-01 Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the buildable, testable, containerized, CI-verified empty skeleton of TechStrap: 15 projects with enforced reference direction, locked package versions, an API that migrates an empty Postgres 17 database on startup, health endpoints on all four hosts, and a compose stack that comes up healthy.

**Architecture:** One .NET 10 solution (`TechStrap.slnx`) with Domain, Contracts, Application, Infrastructure, four hosts (Api, Admin, Portal, Worker) and two client skeletons. Architecture tests parse `.csproj` files and reflect over assemblies to enforce the layering from `02-ARCHITECTURE.md`. Persistence is EF Core on Postgres 17 (snake_case via `SyntaxCircus.EntityFrameworkCore.Postgres`); only the API migrates, under an advisory lock. Every host wires the SyntaxCircus cross-cutting packages (DotEnv, AspNetCore.Common, AspNetCore.Serilog, Observability) and exposes `/health/live` and `/health/ready`. Images, compose files, a Pester-tested build script and GitHub Actions workflows ship the result.

**Tech Stack:** .NET 10 (SDK 10.0.401), ASP.NET Core, Blazor (Admin: Blazor Server, Portal: SSR), EF Core 10 + Npgsql, Postgres 17, xUnit v3 + Shouldly + NSubstitute + Testcontainers.PostgreSql, Microsoft.Testing.Platform, SyntaxCircus packages, AspNetCore.SassCompiler, GitVersion, Docker (buildx), PowerShell 7 + Pester 6.2.0, GitHub Actions.

**Spec:** `docs/architecture/PHASE-01-foundation.md` (tasks P01-T01 to P01-T20 and its Success Criteria). Also read before starting: `docs/architecture/02-ARCHITECTURE.md` (sections 2, 7.6, 11), `docs/architecture/03-PACKAGE-MAP.md`, `docs/architecture/04-DECISION-LOG.md` (D-013, D-016, D-019) and the template rules `D:\dev\SyntaxCircus\_template\AGENT_GUIDE.md`, `docs\APPLICATION_ARCHITECTURE.md`, `docs\patterns\CLIENT_IP_RATE_LIMITING.md`.

## Global Constraints

Every task's requirements include this section. Exact values come from the spec, the package map and the owner's answers of 2026-10-02.

- **SDK and language:** .NET 10 (`net10.0`), `global.json` selects the Microsoft.Testing.Platform runner (`"test": { "runner": "Microsoft.Testing.Platform" }`). Nullable enabled, implicit usings, `TreatWarningsAsErrors=true`. The build must stay warning-free.
- **Central package management:** versions live only in `Directory.Packages.props` (`ManagePackageVersionsCentrally`), copied from `03-PACKAGE-MAP.md`; no project has an inline `Version`; `scripts/Check-PackageVersions.ps1` enforces both.
- **xunit.v3 pair:** use the 4.0.x pair from the package map (`xunit.v3` 4.0.0 and `xunit.runner.visualstudio` 4.0.0). The pin comment in `Directory.Packages.props` says the owner's NCrunch 5.23 runs the 4.0.x adapter correctly.
- **Reference direction (enforced by architecture tests):** `Domain` references nothing; `Contracts` references nothing (no MVC, EF or ASP.NET, no attributes, D-016); `Application` references `Domain`, `Contracts` and `SyntaxCircus.Common`; `Infrastructure` references `Application`, `Domain` and `Contracts`; `Api` and `Worker` reference `Application`, `Infrastructure` and `Contracts`; `Admin` and `Portal` reference `Contracts` only; `Client` references `Contracts`; `Client.Maui` references `Client` and `Contracts`.
- **Handlers:** sealed, with a matching `I...Handler` interface; constructors never take `DbContext`, `DbSet`, `HttpContext`, `IActionResult`, `ControllerBase`, concrete Infrastructure types or persistence entities; Api controllers take handlers through `[FromServices]` only.
- **Database:** Postgres 17 (`postgres:17`), snake_case naming via `SyntaxCircus.EntityFrameworkCore.Postgres`, migrations only through `dotnet ef` (never hand-written), migrate-on-startup in the API only and under an advisory lock. Worker, Admin and Portal never migrate.
- **Configuration:** `SyntaxCircus.DotEnv`; `.env.example` committed per host (`src/TechStrap.{Api,Admin,Portal,Worker}/.env.example`); `.env.local` gitignored and loaded by compose through `env_file` with `required: false`.
- **Health:** `/health/live` (process up, no dependencies) and `/health/ready` (Api and Worker check Postgres) via `SyntaxCircus.AspNetCore.Common`. Admin and Portal report live only.
- **Client IP and rate limiting:** follow `CLIENT_IP_RATE_LIMITING.md` exactly. Compose pins subnet `172.16.31.0/24`. The API trusts the pinned subnet (plus the single reverse-proxy address); Admin and Portal trust only the reverse proxy. Never trust `172.16.0.0/12` or `0.0.0.0/0`. A bad `RateLimiting:Public` value fails boot (`ValidateOnStart`). Default-deny fallback policy; `AllowAnonymous` only on health and OpenAPI.
- **Images:** `Dockerfile.{api,admin,portal,worker}` at the repo root: full tree copied before restore, BuildKit NuGet cache mount, `mcr.microsoft.com/dotnet/aspnet:10.0` runtime, non-root uid 10001, pre-created and chowned `storage`, `logs`, `dataprotection-keys`, `curl` installed, `ASPNETCORE_URLS=http://+:80`, build args `BUILD_VERSION`, `BUILD_INFORMATIONAL_VERSION`, `DISABLE_GITVERSION_TASK`. Admin and Portal assert `wwwroot/css/app.css` exists after publish. Image names `techstrap-{api,admin,portal,worker}`.
- **Compiled CSS is never tracked:** `src/*/wwwroot/css/app.css` is gitignored and generated by `AspNetCore.SassCompiler` at build time (`git ls-files '*/wwwroot/css/*'` returns nothing).
- **CI and release (owner answers):** PR builds build the four images for `linux/amd64` only, no push. Tags `v*` build and push `linux/amd64` and `linux/arm64` to `ghcr.io/syntax-circus` with `GITHUB_TOKEN`.
- **Repository:** `github.com/Syntax-Circus/techstrap`, branch `feat/phase-01-foundation` (already checked out). `.gitignore` already exists and is committed: do not recreate it. Only add `.gitattributes` and `.dockerignore`.
- **Open source:** MIT `LICENSE`, `README.md` (extend it, keep its content), `CONTRIBUTING.md` (Conventional Commits, test-first, EF-tool-only migrations), `SECURITY.md` using GitHub private vulnerability reporting (no email address).
- **Dev data hook:** `IDevelopmentDataSeeder` in Application, no-op implementation in Infrastructure, run by the API only when `ASPNETCORE_ENVIRONMENT=Development` and `TECHSTRAP_SEED_DEV_DATA=true`, after migration. It is exempt from the handler rule (host startup step, `02-ARCHITECTURE.md` section 7.6).
- **Constants:** repeated or meaningful literals (setting keys, health paths, policy names, the uid) are named constants or script variables, not bare magic values.
- **Brand assets (owner, committed on this branch by the coordinator, do not create them):** `assets/brand/` holds `logo.png`, `logo-512.png`, `mark.png`, `mark-512.png`, `favicon.ico`, `favicon-16.png`, `favicon-32.png`, `apple-touch-icon.png`, `icon-192.png`, `icon-512.png`. Task 9 copies five of them into the Admin and Portal `wwwroot/`; Task 15 shows `logo-512.png` in the README.
- **Commits:** Conventional Commits. Every commit message ends with the two trailer lines `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi`. Do not push or open the PR before Task 16.
- **Shells:** commands work in Git Bash and PowerShell 7 unless a block says `powershell`. Run everything from the repository root (`D:\dev\SyntaxCircus\techstrap`). `dotnet test` uses the .NET 10 Microsoft.Testing.Platform syntax (`--project`, `--solution`, `--filter-class`).
- **Docker must be running** for the integration tests (Testcontainers) and the image and compose steps. A missing Docker daemon makes those tests fail loudly; never skip them.

## Review Focus

Failure modes the spec implies but does not name. Each line has a test in the owning task.

1. A malformed, empty or multi-value `X-Forwarded-For` from a trusted proxy must never cause a server error; the request is still served (Task 8, `A_malformed_forwarded_for_from_a_trusted_proxy_never_causes_a_server_error`).
2. A caller-supplied `X-Correlation-Id` that is very long must not break the request or the response headers (Task 6, `An_oversized_caller_supplied_correlation_id_does_not_break_the_request`).
3. Migration enabled with a blank connection string must fail fast at startup instead of hanging or limping on (Task 7, `Startup_with_migration_enabled_and_a_blank_connection_string_fails_fast`).
4. A failing development seeder must abort startup, never be swallowed (Task 7, `A_failing_seeder_aborts_startup_instead_of_being_swallowed`).
5. With the default-deny policy and no real authentication scheme yet, an unknown or protected route must answer 401, not a 500 (the package maps the framework exception to a misleading 409), so a PHASE-04 author who forgets `AllowAnonymous` gets a clean denial (Task 8, `An_unknown_route_is_denied_with_401_not_a_server_error`).

---

## Mapping: plan tasks to P01 task IDs

| Plan task | Deliverable | P01 tasks covered |
| :-- | :-- | :-- |
| 1 | Build configuration, central package versions, package-version check script | P01-T02, P01-T03 |
| 2 | Solution skeleton (15 projects) and `ProjectReferenceDirectionTests` | P01-T01, P01-T04 |
| 3 | Handler boundary architecture rules | P01-T05 |
| 4 | `TechStrapDbContext`, snake_case, readiness check, `PostgresFixture` | P01-T09 (context and DI), P01-T11 |
| 5 | `MigrationStartupTests` and the `Initial` migration from `dotnet ef` | P01-T09 (tests), P01-T10 (migration) |
| 6 | API composition root and health endpoints | P01-T06 |
| 7 | API migrate-on-startup and `IDevelopmentDataSeeder` | P01-T10 (migrator in API only), P01-T12 |
| 8 | Forwarded headers, public rate limit, default-deny | P01-T13 |
| 9 | Worker, Admin and Portal hosts | P01-T07, P01-T10 (the other hosts do not migrate) |
| 10 | `.env.example` per host and completeness tests | P01-T08 |
| 11 | The four Dockerfiles | P01-T14 |
| 12 | `Build-TechStrapDocker.ps1` and `BuildScriptTests` | P01-T15 |
| 13 | Compose files and `.env.production.example` | P01-T16 |
| 14 | GitHub Actions `ci.yml` and `release.yml` | P01-T17, P01-T18 |
| 15 | `LICENSE`, `README.md`, `CONTRIBUTING.md`, `SECURITY.md` | P01-T19 |
| 16 | Clean-clone verification, tick PHASE-01, open the PR | P01-T20 |

All 20 P01 tasks (T01 to T20) map to at least one plan task. Task count: 16.

## Decisions this plan makes where the spec leaves room

- **Client.Maui** is a plain `net10.0` library in PHASE-01 (no MAUI workload). `TechStrap.CI.slnf` lists every project except it, as the spec asks, so CI never needs MAUI workloads; PHASE-11 switches it to the MAUI target frameworks. `dotnet build TechStrap.slnx` builds all 15 projects.
- **Worker** is a `WebApplication` (Microsoft.NET.Sdk.Web) that serves only `/health/*`. Its smoke test is a `WebApplicationFactory` test instead of a `dotnet run` script; the compose health check covers the real container.
- **Readiness check** is a small `IHealthCheck` in Infrastructure (`DatabaseReadinessHealthCheck`, `DbContext.Database.CanConnectAsync`). No health-check NuGet package is added because none is in the package map. Infrastructure therefore has a `FrameworkReference` to `Microsoft.AspNetCore.App`.
- **Public rate limit** attaches to `/openapi/v1.json`, the only anonymous public surface in PHASE-01. Later phases attach the `public` policy (and the named policies from section 11.2) to their public controllers.
- **Placeholder authentication scheme** (`UnauthenticatedScheme`) supplies the 401 challenge the default-deny policy needs until PHASE-04 adds JWT bearer.
- **`SyntaxCircus.Common` is pinned to 0.1.3, not the 0.1.4 in the package map.** `SyntaxCircus.AspNetCore.Common` 0.1.15 depends on exactly `[0.1.3]`, so 0.1.4 fails restore with NU1107; 0.1.4 only changed docs. Task 1 updates the package map row in the same commit.
- **Compose subnet (owner, 2026-10-02):** the pinned subnet is `172.16.31.0/24`. It sits in `172.16.0.0/16`, outside Docker's default auto-assign pool (`172.17`–`172.31` /16s, all already taken by other projects on the planning machine), so no auto-created network can claim it. `TECHSTRAP_SUBNET` stays as an escape hatch; the default is what `docker compose config` shows.
- **DataProtection key ring:** Admin and Portal persist keys to `DataProtection:KeyRingPath` when set (compose mounts a volume at `/app/dataprotection-keys`); Api and Worker do not use data protection yet.

## File Structure

New files by responsibility. Paths are relative to the repo root. `(T)` marks a test file.

```text
Directory.Build.props  Directory.Build.targets  Directory.Packages.props  global.json  GitVersion.yml
.editorconfig  .gitattributes  .dockerignore  .config/dotnet-tools.json
TechStrap.slnx  TechStrap.CI.slnf
Dockerfile.{api,admin,portal,worker}  Build-TechStrapDocker.ps1
docker-compose.yml  docker-compose.uat.yml  docker-compose.production.yml  .env.production.example
LICENSE  CONTRIBUTING.md  SECURITY.md  README.md (extended)
.github/workflows/ci.yml  .github/workflows/release.yml
scripts/Check-PackageVersions.ps1  scripts/Invoke-ScriptTests.ps1
scripts/tests/  Check-PackageVersions.Tests.ps1  BuildScriptTests.Tests.ps1  ComposeFiles.Tests.ps1
                Dockerfiles.Tests.ps1  RepositoryDocs.Tests.ps1                                   (T)
src/TechStrap.Domain/          empty leaf library
src/TechStrap.Contracts/       empty leaf library
src/TechStrap.Application/     ApplicationAssemblyMarker.cs  Seeding/IDevelopmentDataSeeder.cs
src/TechStrap.Infrastructure/  InfrastructureAssemblyMarker.cs
                               Persistence/{TechStrapDatabase,TechStrapDbContext,TechStrapDbContextFactory,
                                            DatabaseReadinessHealthCheck,PersistenceServiceCollectionExtensions}.cs
                               Seeding/{DevelopmentDataSeeder,SeedingServiceCollectionExtensions}.cs
                               Migrations/   (generated by dotnet ef)
src/TechStrap.Api/            Program.cs  appsettings*.json  .env.example
                               Startup/ApiStartupTasks.cs  Options/PublicRateLimitOptions.cs
                               Security/UnauthenticatedScheme.cs
src/TechStrap.Worker/         Program.cs  appsettings.json  .env.example
src/TechStrap.Admin/          Program.cs  appsettings*.json  .env.example  Styles/app.scss  wwwroot/ (icons)
                               Components/{App,Routes,_Imports}.razor  Components/Pages/{Home,NotFound}.razor
src/TechStrap.Portal/         same shape as Admin (Blazor SSR)
src/TechStrap.Client/         empty library          src/TechStrap.Client.Maui/   empty library
tests/Directory.Build.props   shared test packages and usings
tests/TechStrap.Architecture.Tests/   ProjectGraph  ReferenceRules  HandlerRules  HandlerFixtures
                                      ProjectReferenceDirectionTests  HandlerShapeTests
                                      HandlerConstructorDependencyTests  ControllerHandlerInjectionTests  (T)
tests/TechStrap.Infrastructure.IntegrationTests/  PostgresFixture  PostgresIntegrationTestBase
                                      PostgresFixtureSmokeTests  SnakeCaseConventionTests  MigrationStartupTests  (T)
tests/TechStrap.Api.Tests/    TestPostgres  HostFactory  HealthEndpointTests  ApiStartupTasksTests
                              ApiMigrationOnStartupTests  PublicApiHardeningTests  PublicRateLimitOptionsTests
                              TrustedProxyStartupTests  SetRemoteIpAddressStartupFilter  HostHealthSmokeTests
                              ShellHostTests  HostMigrationBoundaryTests  EnvExampleCompletenessTests       (T)
tests/TechStrap.Domain.Tests/  tests/TechStrap.Application.Tests/   placeholder tests (replaced from PHASE-03)
```

---

### Task 1: Build configuration, central package versions and the version check

Maps to P01-T02 and P01-T03. Deliverable: the repo-wide MSBuild configuration, every package version locked once, and `scripts/Check-PackageVersions.ps1` proving the props file and the package map agree.

**Files:**
- Create: `global.json`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `GitVersion.yml`, `.editorconfig`, `.gitattributes`, `.dockerignore`, `.config/dotnet-tools.json`
- Create: `scripts/Check-PackageVersions.ps1`, `scripts/Invoke-ScriptTests.ps1`
- Create (test): `scripts/tests/Check-PackageVersions.Tests.ps1`
- Modify: `docs/architecture/03-PACKAGE-MAP.md` (the `SyntaxCircus.Common` row)
- Do not touch: `.gitignore` (already committed)

**Interfaces:**
- Consumes: `docs/architecture/03-PACKAGE-MAP.md` tables (columns `Package`, `Status`, `Exact version`).
- Produces: `Directory.Packages.props` (every later `csproj` uses `<PackageReference Include="X" />` with no version); `scripts/Invoke-ScriptTests.ps1 [-Path <file or dir>] [-Output Detailed|Normal|Minimal]` runs Pester 6.2.0 and exits 1 on any failure (used by every later Pester step and by CI); `Check-PackageVersions.ps1 [-RepoRoot] [-MapPath] [-PropsPath]` exits 0 or 1.

- [ ] **Step 1: Write the failing Pester test**

The test builds throwaway repositories in `TestDrive` (a map, a props file and a project) and runs the script against them, then runs it against the real repository.

```powershell
BeforeAll {
    $script:ScriptPath = Join-Path $PSScriptRoot '..' 'Check-PackageVersions.ps1'

    function New-FakeRepo {
        param(
            [string]$Root,
            [string]$PropsVersion = '1.2.3',
            [string]$ProjectReference = '<PackageReference Include="Foo.Bar" />',
            [string]$ExtraProps = ''
        )

        New-Item -ItemType Directory -Force -Path (Join-Path $Root 'docs/architecture') | Out-Null
        New-Item -ItemType Directory -Force -Path (Join-Path $Root 'src/App') | Out-Null

        @'
| Concern | Status | Package | Exact version | Source |
| --- | --- | --- | --- | --- |
| Thing | Selected | `Foo.Bar` | 1.2.3 | link |
| Other | Not applicable | `Skipped.Pkg` | n/a | link |

| Package | Status | Exact version | Source |
| --- | --- | --- | --- |
| `Base.Pkg` (+ `.Design`) | Selected | 10.0.12 (older repo 10.0.11) | link |
| `Pester` (module) | Selected | 6.2.0 | link |
'@ | Set-Content -LiteralPath (Join-Path $Root 'docs/architecture/03-PACKAGE-MAP.md')

        @"
<Project>
  <ItemGroup>
    <PackageVersion Include="Foo.Bar" Version="$PropsVersion" />
    <PackageVersion Include="Base.Pkg" Version="10.0.12" />
    <PackageVersion Include="Base.Pkg.Design" Version="10.0.12" />
    $ExtraProps
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $Root 'Directory.Packages.props')

        @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    $ProjectReference
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $Root 'src/App/App.csproj')
    }

    function Invoke-Check {
        param([string]$Root)
        $output = & pwsh -NoProfile -File $script:ScriptPath -RepoRoot $Root 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }
}

Describe 'Check-PackageVersions.ps1' {
    It 'passes when props, map and projects agree' {
        New-FakeRepo -Root $TestDrive
        $result = Invoke-Check -Root $TestDrive
        $result.ExitCode | Should -Be 0
    }

    It 'fails when a project has an inline Version' {
        New-FakeRepo -Root $TestDrive -ProjectReference '<PackageReference Include="Foo.Bar" Version="1.2.3" />'
        $result = Invoke-Check -Root $TestDrive
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'inline Version'
    }

    It 'fails when a props version differs from the map' {
        New-FakeRepo -Root $TestDrive -PropsVersion '9.9.9'
        $result = Invoke-Check -Root $TestDrive
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match "Foo.Bar.*9.9.9.*1.2.3"
    }

    It 'fails when the props file has a package the map does not select' {
        New-FakeRepo -Root $TestDrive -ExtraProps '<PackageVersion Include="Rogue.Pkg" Version="1.0.0" />'
        $result = Invoke-Check -Root $TestDrive
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Rogue.Pkg'
    }

    It 'fails when a Selected package is missing from props' {
        New-FakeRepo -Root $TestDrive
        $props = Join-Path $TestDrive 'Directory.Packages.props'
        (Get-Content -LiteralPath $props) | Where-Object { $_ -notmatch 'Base.Pkg.Design' } | Set-Content -LiteralPath $props
        $result = Invoke-Check -Root $TestDrive
        $result.ExitCode | Should -Be 1
        $result.Output | Should -Match 'Base.Pkg.Design'
    }
}

Describe 'the real repository' {
    It 'passes against the checked-in package map' {
        $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
        $result = Invoke-Check -Root $repoRoot
        $result.ExitCode | Should -Be 0
    }
}
```

- [ ] **Step 2: Install Pester 6.2.0, add the Pester runner, and run the test to see it fail**

Pester is a PowerShell Gallery module, not a NuGet package, so it is not in `Directory.Packages.props`. Windows PowerShell ships Pester 3.4.0; install the pinned version alongside it.

```powershell
Install-Module Pester -RequiredVersion 6.2.0 -Scope CurrentUser -Force -SkipPublisherCheck
```

Create the runner that every later Pester step uses:

```powershell
<#
.SYNOPSIS
  Runs the Pester tests under scripts/tests with the pinned Pester version and fails (exit 1) on any failure.
.EXAMPLE
  pwsh ./scripts/Invoke-ScriptTests.ps1
.EXAMPLE
  pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/Check-PackageVersions.Tests.ps1
#>
[CmdletBinding()]
param(
    [string]$Path = (Join-Path $PSScriptRoot 'tests'),
    [ValidateSet('Normal', 'Detailed', 'Minimal')]
    [string]$Output = 'Detailed'
)

$ErrorActionPreference = 'Stop'

# Pinned in docs/architecture/03-PACKAGE-MAP.md (Pester is a PowerShell Gallery module, not a NuGet package).
$PesterVersion = '6.2.0'

if (-not (Get-Module -ListAvailable -Name Pester | Where-Object { $_.Version -eq [version]$PesterVersion })) {
    throw "Pester $PesterVersion is required. Install it with: Install-Module Pester -RequiredVersion $PesterVersion -Scope CurrentUser -Force -SkipPublisherCheck"
}

Import-Module Pester -RequiredVersion $PesterVersion
$result = Invoke-Pester -Path $Path -Output $Output -PassThru

if ($result.Result -ne 'Passed') {
    exit 1
}
```

Run the new test:

```bash
pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/Check-PackageVersions.Tests.ps1
```

Expected: FAIL. The script under test does not exist yet, so the cases that expect exit code 0 or a specific message fail (`Tests Passed: ... Failed: 6` or similar).

- [ ] **Step 3: Write `scripts/Check-PackageVersions.ps1`**

It reads every table row of the package map whose `Status` is `Selected`, expands rows such as ``Microsoft.EntityFrameworkCore` (+ `.Relational`, `.Design`)`` into one package per name, ignores `Pester` and `dotnet-ef` (not NuGet packages), and compares against `Directory.Packages.props`. It also fails on any inline `Version` or `VersionOverride` in a project or props file.

```powershell
<#
.SYNOPSIS
  Verifies central package management against docs/architecture/03-PACKAGE-MAP.md.
.DESCRIPTION
  Fails (exit 1) when:
    - a project file states an inline Version / VersionOverride on a PackageReference,
    - a package marked Selected in the package map is missing from Directory.Packages.props,
    - a version in Directory.Packages.props differs from the package map,
    - Directory.Packages.props contains a package that the package map does not select.
  Pester (PowerShell Gallery) and dotnet-ef (dotnet tool) are listed in the map but are not NuGet
  package versions, so they are skipped.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$MapPath = (Join-Path $RepoRoot 'docs/architecture/03-PACKAGE-MAP.md'),
    [string]$PropsPath = (Join-Path $RepoRoot 'Directory.Packages.props')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$NonNuGetItems = @('Pester', 'dotnet-ef')
$errors = New-Object System.Collections.Generic.List[string]

function Get-MapVersions {
    param([string]$Path)

    $versions = @{}
    $packageColumn = -1
    $statusColumn = -1
    $versionColumn = -1

    foreach ($line in Get-Content -LiteralPath $Path) {
        if (-not $line.TrimStart().StartsWith('|')) {
            $packageColumn = $statusColumn = $versionColumn = -1
            continue
        }

        $cells = @($line.Trim().Trim('|') -split '\|' | ForEach-Object { $_.Trim() })

        if ($cells -contains 'Package' -and $cells -contains 'Status' -and $cells -contains 'Exact version') {
            $packageColumn = [array]::IndexOf($cells, 'Package')
            $statusColumn = [array]::IndexOf($cells, 'Status')
            $versionColumn = [array]::IndexOf($cells, 'Exact version')
            continue
        }

        if ($packageColumn -lt 0 -or $cells.Count -le [Math]::Max($versionColumn, [Math]::Max($packageColumn, $statusColumn))) {
            continue
        }

        if ($cells[$statusColumn] -ne 'Selected') {
            continue
        }

        if ($cells[$versionColumn] -notmatch '^(\d+\.\d+\.\d+[^\s(]*)') {
            continue
        }
        $version = $Matches[1]

        $tokens = @([regex]::Matches($cells[$packageColumn], '`([^`]+)`') | ForEach-Object { $_.Groups[1].Value })
        if ($tokens.Count -eq 0) {
            continue
        }

        $baseName = $tokens[0]
        foreach ($token in $tokens) {
            $name = if ($token.StartsWith('.')) { "$baseName$token" } else { $token }
            if ($NonNuGetItems -contains $name) {
                continue
            }
            $versions[$name] = $version
        }
    }

    return $versions
}

if (-not (Test-Path -LiteralPath $MapPath)) { throw "Package map not found: $MapPath" }
if (-not (Test-Path -LiteralPath $PropsPath)) { throw "Directory.Packages.props not found: $PropsPath" }

$mapVersions = Get-MapVersions -Path $MapPath

[xml]$propsXml = Get-Content -LiteralPath $PropsPath -Raw
$propsVersions = @{}
foreach ($node in $propsXml.SelectNodes('//PackageVersion')) {
    $propsVersions[$node.GetAttribute('Include')] = $node.GetAttribute('Version')
}

foreach ($name in ($mapVersions.Keys | Sort-Object)) {
    if (-not $propsVersions.ContainsKey($name)) {
        $errors.Add("Package '$name' ($($mapVersions[$name])) is Selected in the package map but missing from Directory.Packages.props.")
    }
    elseif ($propsVersions[$name] -ne $mapVersions[$name]) {
        $errors.Add("Package '$name' is $($propsVersions[$name]) in Directory.Packages.props but $($mapVersions[$name]) in the package map.")
    }
}

foreach ($name in ($propsVersions.Keys | Sort-Object)) {
    if (-not $mapVersions.ContainsKey($name)) {
        $errors.Add("Package '$name' is in Directory.Packages.props but is not Selected in the package map.")
    }
}

$projectFiles = Get-ChildItem -LiteralPath $RepoRoot -Recurse -Include *.csproj, *.props, *.targets -File |
    Where-Object { $_.FullName -notmatch '[\/](bin|obj|node_modules|\.git)[\/]' -and $_.Name -ne 'Directory.Packages.props' }

foreach ($file in $projectFiles) {
    [xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($reference in $xml.SelectNodes('//PackageReference')) {
        foreach ($attribute in 'Version', 'VersionOverride') {
            if ($reference.HasAttribute($attribute)) {
                $relative = [System.IO.Path]::GetRelativePath($RepoRoot, $file.FullName)
                $errors.Add("$relative : PackageReference '$($reference.GetAttribute('Include'))' has an inline $attribute. Versions live in Directory.Packages.props.")
            }
        }
    }
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    Write-Host "Package version check failed with $($errors.Count) problem(s)."
    exit 1
}

Write-Host "Package version check passed: $($mapVersions.Count) packages match the package map."
exit 0
```

- [ ] **Step 4: Write the build configuration files**

`global.json` selects the Microsoft.Testing.Platform runner (the SDK version is not pinned, as in dragon-poop):

```json
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

`Directory.Build.props` applies to every project. `TargetFramework` is set here so project files stay minimal:

```xml
<Project>
  <PropertyGroup>
    <Company>Syntax Circus</Company>
    <Authors>Syntax Circus</Authors>
    <Product>TechStrap</Product>
    <Copyright>Copyright (c) 2026 Syntax Circus</Copyright>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

`Directory.Build.targets` is the SassCompiler target copied unchanged from `dragon-poop`. It runs only in projects that reference `AspNetCore.SassCompiler` (Admin and Portal, Task 9) and adds the compiled CSS to the build output:

```xml
<Project>
  <Target Name="Compile Sass"
          BeforeTargets="Build;ResolveScopedCssInputs;ResolveProjectStaticWebAssets"
          Condition="Exists('$(SassCompilerTasksAssembly)') And '$(DesignTimeBuild)' != 'true'">
    <CompileSass AppsettingsFile="$(SassCompilerAppsettingsJson)"
                 SassCompilerFile="$(SassCompilerSassCompilerJson)"
                 Command="$(SassCompilerBuildCommand)"
                 Snapshot="$(SassCompilerBuildSnapshot)"
                 Configuration="$(SassCompilerConfiguration)"
                 TargetFramework="$(TargetFramework)"
                 TargetFrameworks="$(TargetFrameworks)">
      <Output TaskParameter="GeneratedFiles" ItemName="CompiledCssFiles" />
    </CompileSass>
    <ItemGroup>
      <None Remove="@(CompiledCssFiles)" />
      <_NewCompiledCssFiles Include="@(CompiledCssFiles)" Exclude="@(Content)" />
      <Content Include="@(_NewCompiledCssFiles)" />
    </ItemGroup>
  </Target>
</Project>
```

`GitVersion.yml` is copied from `dragon-poop` (TrunkBased, `next-version: 0.1.0`):

```yaml
workflow: TrunkBased/preview1
tag-prefix: '[vV]?'

branches:
  main:
    increment: None
    mode: ContinuousDelivery

commit-message-incrementing: Disabled

assembly-versioning-scheme: MajorMinorPatch
assembly-file-versioning-format: '{Major}.{Minor}.{Patch}.{CommitsSinceVersionSource}'
assembly-informational-format: '{InformationalVersion}'

next-version: 0.1.0
```

`.config/dotnet-tools.json` pins the `dotnet-ef` tool (10.0.12, from the package map). The globally installed 10.0.8 is not used by this repo:

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "dotnet-ef": {
      "version": "10.0.12",
      "commands": [
        "dotnet-ef"
      ],
      "rollForward": false
    }
  }
}
```

`Directory.Packages.props` locks every `Selected` NuGet version in `03-PACKAGE-MAP.md`. Packages owned by later phases are locked here but not referenced by any project yet. The xunit.v3 pin comment records the owner's NCrunch decision:

```xml
<Project>
  <!--
    Every version below is copied from docs/architecture/03-PACKAGE-MAP.md.
    scripts/Check-PackageVersions.ps1 fails when this file and the map disagree, or when a project
    states an inline Version. Change the map and this file in the same commit.

    xunit.v3 / xunit.runner.visualstudio 4.0.0: pinned to the 4.0.x pair. The owner's NCrunch 5.23
    runs the 4.0.x xunit.v3 adapter correctly (the 4.0.0 break affected older NCrunch releases, see
    _template AGENT_GUIDE.md). Do not downgrade to 3.2.2 / 3.1.5 unless NCrunch is downgraded.
  -->
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup Label="SyntaxCircus">
    <PackageVersion Include="SyntaxCircus.AspNetCore.Authentication" Version="0.1.5" />
    <PackageVersion Include="SyntaxCircus.AspNetCore.Common" Version="0.1.15" />
    <PackageVersion Include="SyntaxCircus.AspNetCore.Serilog" Version="0.1.4" />
    <PackageVersion Include="SyntaxCircus.Blazor.Auth" Version="0.1.7" />
    <PackageVersion Include="SyntaxCircus.Blazor.Components" Version="0.1.3" />
    <PackageVersion Include="SyntaxCircus.Blazor.Seo" Version="0.1.4" />
    <PackageVersion Include="SyntaxCircus.Common" Version="0.1.3" />
    <PackageVersion Include="SyntaxCircus.DotEnv" Version="0.1.3" />
    <PackageVersion Include="SyntaxCircus.Email" Version="0.1.6" />
    <PackageVersion Include="SyntaxCircus.EntityFrameworkCore.Postgres" Version="0.1.3" />
    <PackageVersion Include="SyntaxCircus.Http.Resilience" Version="0.2.2" />
    <PackageVersion Include="SyntaxCircus.Observability" Version="0.1.2" />
    <PackageVersion Include="SyntaxCircus.Storage" Version="0.2.1" />
  </ItemGroup>
  <ItemGroup Label="Microsoft and EF">
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.12" />
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="10.0.12" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.12" />
    <PackageVersion Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.12" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.12" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Relational" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Options" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Maui.Controls" Version="10.0.110" />
    <PackageVersion Include="Npgsql" Version="10.0.3" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
  </ItemGroup>
  <ItemGroup Label="Third-party runtime and build">
    <PackageVersion Include="AspNetCore.SassCompiler" Version="1.105.1" />
    <PackageVersion Include="GitVersion.MsBuild" Version="6.8.2" />
    <PackageVersion Include="HtmlSanitizer" Version="9.2.1039" />
    <PackageVersion Include="Markdig" Version="1.4.0" />
    <PackageVersion Include="Microsoft.SourceLink.GitHub" Version="10.0.401" />
    <PackageVersion Include="Microsoft.Web.LibraryManager.Build" Version="3.0.114" />
  </ItemGroup>
  <ItemGroup Label="Testing">
    <PackageVersion Include="bunit" Version="2.11.3" />
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageVersion Include="NSubstitute" Version="6.2.0" />
    <PackageVersion Include="Shouldly" Version="4.3.0" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.15.0" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="4.0.0" />
    <PackageVersion Include="xunit.v3" Version="4.0.0" />
  </ItemGroup>
</Project>
```

`.gitattributes` (LF for scripts, container files and YAML; binary assets):

```text
# Normalise line endings in the repo; checkouts use the platform default except where noted.
* text=auto

# Shell and container files must stay LF so they run inside Linux containers and CI.
*.sh text eol=lf
Dockerfile.* text eol=lf
*.yml text eol=lf
*.yaml text eol=lf

# PowerShell scripts and solution files.
*.ps1 text eol=lf
*.slnx text eol=lf

# Generated EF migration snapshots stay as the tool wrote them.
src/TechStrap.Infrastructure/Migrations/*.cs linguist-generated=true

# Binary assets.
*.png binary
*.ico binary
*.jpg binary
*.woff2 binary
```

`.dockerignore` keeps git history, secrets, compiled CSS and build output out of every image build context:

```text
.git/
.github/
.config/
.vs/
.idea/
.claude/
.superpowers/
docs/
assets/
scripts/
local/
backups/
artifacts/
publish/
**/bin/
**/obj/
**/TestResults/
**/.env
**/.env.*
!**/.env.example
**/wwwroot/css/app.css
**/wwwroot/css/app.css.map
**/storage/
**/logs/
tests/
```

`.editorconfig` (generated EF migrations are excluded from analysis; private fields use `_camelCase`):

```ini
root = true

[*]
charset = utf-8
indent_style = space
indent_size = 4
insert_final_newline = true
trim_trailing_whitespace = true

[*.{csproj,props,targets,slnx,json,yml,yaml,razor,html,scss,css,ps1,md}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false

[*.cs]
csharp_style_namespace_declarations = file_scoped:suggestion
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
dotnet_sort_system_directives_first = true

# Private fields: _camelCase (APPLICATION_ARCHITECTURE.md naming conventions)
dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_camel
dotnet_naming_rule.private_fields_underscore.severity = warning
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_camel.required_prefix = _
dotnet_naming_style.underscore_camel.capitalization = camel_case

# EF migrations are generated by the dotnet ef tool; never hand-edit or lint them.
[**/Migrations/*.cs]
generated_code = true
dotnet_analyzer_diagnostic.severity = none
```

- [ ] **Step 5: Correct the package map for `SyntaxCircus.Common`**

`SyntaxCircus.AspNetCore.Common` 0.1.15 depends on exactly `SyntaxCircus.Common` `[0.1.3]`. With the map's 0.1.4 any project that references both fails restore with `NU1107: Version conflict detected for SyntaxCircus.Common`. 0.1.4 only changed documentation, so pin 0.1.3. Edit `docs/architecture/03-PACKAGE-MAP.md` and replace this text in the `SyntaxCircus.Common` row:

Old:

```text
| `SyntaxCircus.Common` | 0.1.4 | [0.1.4](https://www.nuget.org/packages/SyntaxCircus.Common/0.1.4) (sinforgiver 0.1.3) |
```

New:

```text
| `SyntaxCircus.Common` | 0.1.3 | [0.1.3](https://www.nuget.org/packages/SyntaxCircus.Common/0.1.3) (0.1.4 is docs-only; `SyntaxCircus.AspNetCore.Common` 0.1.15 pins `[0.1.3]` exactly, so 0.1.4 fails restore with NU1107) |
```

`Directory.Packages.props` already says `SyntaxCircus.Common` `0.1.3`, so the two now agree.

- [ ] **Step 6: Run the tests and the script itself**

```bash
dotnet tool restore
pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/Check-PackageVersions.Tests.ps1
pwsh ./scripts/Check-PackageVersions.ps1
```

Expected: `dotnet tool restore` prints `Tool 'dotnet-ef' (version '10.0.12') was restored.`; Pester prints `Tests Passed: 6, Failed: 0`; the script prints `Package version check passed: 41 packages match the package map.` and exits 0.

- [ ] **Step 7: Commit**

```bash
git add global.json Directory.Build.props Directory.Build.targets Directory.Packages.props GitVersion.yml .editorconfig .gitattributes .dockerignore .config/dotnet-tools.json scripts docs/architecture/03-PACKAGE-MAP.md
git commit -m "build: add build configuration, central package versions and version check" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 2: Solution skeleton and `ProjectReferenceDirectionTests`

Maps to P01-T01 and P01-T04. Deliverable: `TechStrap.slnx` with the ten `src/` and five `tests/` projects and the reference direction enforced by tests that parse the `.csproj` files.

**Files:**
- Create: `TechStrap.slnx`, `TechStrap.CI.slnf`
- Create: `src/TechStrap.{Domain,Contracts,Application,Infrastructure,Api,Worker,Admin,Portal,Client,Client.Maui}/*.csproj`, plus `Program.cs` in Api, Worker, Admin and Portal
- Create: `tests/Directory.Build.props`, `tests/TechStrap.{Architecture.Tests,Domain.Tests,Application.Tests,Infrastructure.IntegrationTests,Api.Tests}/*.csproj`, placeholder tests in four of them
- Create (test): `tests/TechStrap.Architecture.Tests/{ProjectReferenceDirectionTests,ProjectGraph,ReferenceRules}.cs`

**Interfaces:**
- Consumes: `Directory.Build.props`, `Directory.Packages.props` (Task 1).
- Produces: all 15 projects; `ReferenceRules.AllowedProjectReferences` (project name to allowed project references), `ReferenceRules.Evaluate(IReadOnlyDictionary<string, ProjectNode>) : IReadOnlyList<string>` (violations, empty when clean), `ProjectGraph.FindRepositoryRoot()`, `ProjectGraph.LoadSourceProjects(string repositoryRoot)`. Host `Program.cs` files declare `namespace TechStrap.<Host> { public partial class Program; }` so `WebApplicationFactory<TechStrap.<Host>.Program>` resolves the right assembly in later tasks. Test projects share `tests/Directory.Build.props` (xunit.v3, runner, Test.Sdk, Shouldly, NSubstitute, global usings for `Xunit` and `Shouldly`).

- [ ] **Step 1: Scaffold the projects**

The source libraries. Domain and Contracts have no references at all:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Domain references nothing: no projects, no packages, no framework references. -->

</Project>
```

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Dependency-free leaf (D-016): no MVC, EF or ASP.NET, no attributes. Shared by Api, Admin, Portal, Client. -->

</Project>
```

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="../TechStrap.Domain/TechStrap.Domain.csproj" />
    <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="SyntaxCircus.Common" />
  </ItemGroup>

</Project>
```

Infrastructure takes its final package set now (Tasks 3 and 4 need EF types). `Microsoft.AspNetCore.App` is a framework reference because the readiness check (Task 4) implements `IHealthCheck`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="../TechStrap.Application/TechStrap.Application.csproj" />
    <ProjectReference Include="../TechStrap.Domain/TechStrap.Domain.csproj" />
    <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- Health checks (IHealthCheck) and ConfigurationBinder live in the shared framework. -->
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="SyntaxCircus.EntityFrameworkCore.Postgres" />
  </ItemGroup>

</Project>
```

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
  </ItemGroup>

</Project>
```

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Plain net10.0 placeholder. PHASE-11 switches this to the MAUI target frameworks and workload. -->

  <ItemGroup>
    <ProjectReference Include="../TechStrap.Client/TechStrap.Client.csproj" />
    <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
  </ItemGroup>

</Project>
```

The four hosts. They get `GitVersion.MsBuild` now so version stamping works (later tasks add the other packages). Api and Worker are composition roots; Admin and Portal reference Contracts only. `src/TechStrap.Api/TechStrap.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="../TechStrap.Application/TechStrap.Application.csproj" />
    <ProjectReference Include="../TechStrap.Infrastructure/TechStrap.Infrastructure.csproj" />
    <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="GitVersion.MsBuild" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

`src/TechStrap.Worker/TechStrap.Worker.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="../TechStrap.Application/TechStrap.Application.csproj" />
    <ProjectReference Include="../TechStrap.Infrastructure/TechStrap.Infrastructure.csproj" />
    <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="GitVersion.MsBuild" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

`src/TechStrap.Admin/TechStrap.Admin.csproj` and `src/TechStrap.Portal/TechStrap.Portal.csproj` (identical for now):

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="GitVersion.MsBuild" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

Each host starts as an empty web host. `src/TechStrap.Api/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();

namespace TechStrap.Api
{
    public partial class Program;
}
```

`src/TechStrap.Worker/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();

namespace TechStrap.Worker
{
    public partial class Program;
}
```

`src/TechStrap.Admin/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();

namespace TechStrap.Admin
{
    public partial class Program;
}
```

`src/TechStrap.Portal/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.Run();

namespace TechStrap.Portal
{
    public partial class Program;
}
```

The shared test configuration. xunit.v3 test projects are executables, so no `OutputType` is needed:

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />

  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" PrivateAssets="all" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="NSubstitute" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
    <Using Include="Shouldly" />
  </ItemGroup>
</Project>
```

The five test projects:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="../../src/TechStrap.Application/TechStrap.Application.csproj" />
    <ProjectReference Include="../../src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj" />
    <ProjectReference Include="../../src/TechStrap.Api/TechStrap.Api.csproj" />
  </ItemGroup>

</Project>
```

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="../../src/TechStrap.Domain/TechStrap.Domain.csproj" />
  </ItemGroup>

</Project>
```

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="../../src/TechStrap.Application/TechStrap.Application.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
  </ItemGroup>

</Project>
```

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="../../src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Testcontainers.PostgreSql" />
  </ItemGroup>

</Project>
```

The Api test project references all four hosts (host smoke tests live here) and Testcontainers:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="../../src/TechStrap.Api/TechStrap.Api.csproj" />
    <ProjectReference Include="../../src/TechStrap.Worker/TechStrap.Worker.csproj" />
    <ProjectReference Include="../../src/TechStrap.Admin/TechStrap.Admin.csproj" />
    <ProjectReference Include="../../src/TechStrap.Portal/TechStrap.Portal.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Testcontainers.PostgreSql" />
  </ItemGroup>

</Project>
```

`dotnet test` fails a project that discovers zero tests, so four projects get a placeholder that later tasks replace or delete (Architecture.Tests gets real tests in Step 2). `tests/TechStrap.Domain.Tests/PlaceholderTests.cs`:

```csharp
namespace TechStrap.Domain.Tests;

// A later task replaces this placeholder with real tests. It keeps the project discoverable by dotnet test.
public sealed class PlaceholderTests
{
    [Fact]
    public void Project_is_wired_into_the_test_runner() => true.ShouldBeTrue();
}
```

`tests/TechStrap.Application.Tests/PlaceholderTests.cs`:

```csharp
namespace TechStrap.Application.Tests;

// A later task replaces this placeholder with real tests. It keeps the project discoverable by dotnet test.
public sealed class PlaceholderTests
{
    [Fact]
    public void Project_is_wired_into_the_test_runner() => true.ShouldBeTrue();
}
```

`tests/TechStrap.Api.Tests/PlaceholderTests.cs` (deleted in Task 6):

```csharp
namespace TechStrap.Api.Tests;

// A later task replaces this placeholder with real tests. It keeps the project discoverable by dotnet test.
public sealed class PlaceholderTests
{
    [Fact]
    public void Project_is_wired_into_the_test_runner() => true.ShouldBeTrue();
}
```

`tests/TechStrap.Infrastructure.IntegrationTests/PlaceholderTests.cs` (deleted in Task 4):

```csharp
namespace TechStrap.Infrastructure.IntegrationTests;

// A later task replaces this placeholder with real tests. It keeps the project discoverable by dotnet test.
public sealed class PlaceholderTests
{
    [Fact]
    public void Project_is_wired_into_the_test_runner() => true.ShouldBeTrue();
}
```

The solution (`.slnx`) lists all 15 projects, and the CI solution filter lists every project except `TechStrap.Client.Maui` (forward slashes are required in `.slnf` for an `.slnx` solution):

```xml
<Solution>
  <Folder Name="/Solution Items/">
    <File Path=".dockerignore" />
    <File Path=".editorconfig" />
    <File Path=".gitattributes" />
    <File Path=".gitignore" />
    <File Path="Directory.Build.props" />
    <File Path="Directory.Build.targets" />
    <File Path="Directory.Packages.props" />
    <File Path="GitVersion.yml" />
    <File Path="global.json" />
    <File Path="README.md" />
  </Folder>
  <Folder Name="/src/">
    <Project Path="src/TechStrap.Admin/TechStrap.Admin.csproj" />
    <Project Path="src/TechStrap.Api/TechStrap.Api.csproj" />
    <Project Path="src/TechStrap.Application/TechStrap.Application.csproj" />
    <Project Path="src/TechStrap.Client.Maui/TechStrap.Client.Maui.csproj" />
    <Project Path="src/TechStrap.Client/TechStrap.Client.csproj" />
    <Project Path="src/TechStrap.Contracts/TechStrap.Contracts.csproj" />
    <Project Path="src/TechStrap.Domain/TechStrap.Domain.csproj" />
    <Project Path="src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj" />
    <Project Path="src/TechStrap.Portal/TechStrap.Portal.csproj" />
    <Project Path="src/TechStrap.Worker/TechStrap.Worker.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj" />
    <Project Path="tests/TechStrap.Application.Tests/TechStrap.Application.Tests.csproj" />
    <Project Path="tests/TechStrap.Architecture.Tests/TechStrap.Architecture.Tests.csproj" />
    <Project Path="tests/TechStrap.Domain.Tests/TechStrap.Domain.Tests.csproj" />
    <Project Path="tests/TechStrap.Infrastructure.IntegrationTests/TechStrap.Infrastructure.IntegrationTests.csproj" />
  </Folder>
</Solution>
```

```json
{
  "solution": {
    "path": "TechStrap.slnx",
    "projects": [
      "src/TechStrap.Admin/TechStrap.Admin.csproj",
      "src/TechStrap.Api/TechStrap.Api.csproj",
      "src/TechStrap.Application/TechStrap.Application.csproj",
      "src/TechStrap.Client/TechStrap.Client.csproj",
      "src/TechStrap.Contracts/TechStrap.Contracts.csproj",
      "src/TechStrap.Domain/TechStrap.Domain.csproj",
      "src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj",
      "src/TechStrap.Portal/TechStrap.Portal.csproj",
      "src/TechStrap.Worker/TechStrap.Worker.csproj",
      "tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj",
      "tests/TechStrap.Application.Tests/TechStrap.Application.Tests.csproj",
      "tests/TechStrap.Architecture.Tests/TechStrap.Architecture.Tests.csproj",
      "tests/TechStrap.Domain.Tests/TechStrap.Domain.Tests.csproj",
      "tests/TechStrap.Infrastructure.IntegrationTests/TechStrap.Infrastructure.IntegrationTests.csproj"
    ]
  }
}
```

Build everything and count the projects:

```bash
dotnet build TechStrap.slnx
dotnet sln TechStrap.slnx list
```

Expected: `Build succeeded.` with `0 Warning(s)` and `0 Error(s)`; `dotnet sln list` prints `Project(s)` and then 15 project paths (10 under `src/`, 5 under `tests/`).

- [ ] **Step 2: Write the failing test**

`tests/TechStrap.Architecture.Tests/ProjectReferenceDirectionTests.cs`. The first four tests read the real `.csproj` files; the rest feed `ReferenceRules.Evaluate` deliberately bad graphs to prove every rule can fail:

```csharp
namespace TechStrap.Architecture.Tests;

public sealed class ProjectReferenceDirectionTests
{
    private static ProjectNode Node(string name, string[]? projects = null, string[]? packages = null, string[]? frameworks = null) =>
        new(
            name,
            (projects ?? []).ToHashSet(),
            (packages ?? []).ToHashSet(),
            (frameworks ?? []).ToHashSet());

    [Fact]
    public void Source_projects_follow_the_allowed_reference_direction()
    {
        var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

        ReferenceRules.Evaluate(graph).ShouldBeEmpty();
    }

    [Fact]
    public void Solution_contains_the_ten_source_projects()
    {
        var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

        graph.Keys.Order().ShouldBe(ReferenceRules.AllowedProjectReferences.Keys.Order());
    }

    [Fact]
    public void Domain_and_Contracts_reference_no_project_package_or_framework()
    {
        var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

        foreach (var name in new[] { ReferenceRules.Domain, ReferenceRules.Contracts })
        {
            graph[name].ProjectReferences.ShouldBeEmpty(name);
            graph[name].PackageReferences.ShouldBeEmpty(name);
            graph[name].FrameworkReferences.ShouldBeEmpty(name);
        }
    }

    [Fact]
    public void Admin_and_Portal_reference_only_Contracts()
    {
        var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

        graph[ReferenceRules.Admin].ProjectReferences.ShouldBe([ReferenceRules.Contracts]);
        graph[ReferenceRules.Portal].ProjectReferences.ShouldBe([ReferenceRules.Contracts]);
    }

    // The tests below feed the rules deliberately bad graphs: they prove each rule can fail.

    [Fact]
    public void Rules_flag_Admin_referencing_Application()
    {
        var graph = new Dictionary<string, ProjectNode>
        {
            [ReferenceRules.Admin] = Node(ReferenceRules.Admin, projects: [ReferenceRules.Contracts, ReferenceRules.Application]),
        };

        ReferenceRules.Evaluate(graph).ShouldContain($"{ReferenceRules.Admin} must not reference {ReferenceRules.Application}.");
    }

    [Fact]
    public void Rules_flag_Application_referencing_Infrastructure_or_a_host()
    {
        var graph = new Dictionary<string, ProjectNode>
        {
            [ReferenceRules.Application] = Node(
                ReferenceRules.Application,
                projects: [ReferenceRules.Domain, ReferenceRules.Infrastructure, ReferenceRules.Api]),
        };

        var violations = ReferenceRules.Evaluate(graph);

        violations.ShouldContain($"{ReferenceRules.Application} must not reference {ReferenceRules.Infrastructure}.");
        violations.ShouldContain($"{ReferenceRules.Application} must not reference {ReferenceRules.Api}.");
    }

    [Fact]
    public void Rules_flag_Infrastructure_referencing_a_host()
    {
        var graph = new Dictionary<string, ProjectNode>
        {
            [ReferenceRules.Infrastructure] = Node(ReferenceRules.Infrastructure, projects: [ReferenceRules.Worker]),
        };

        ReferenceRules.Evaluate(graph).ShouldContain($"{ReferenceRules.Infrastructure} must not reference {ReferenceRules.Worker}.");
    }

    [Fact]
    public void Rules_flag_Domain_and_Contracts_with_any_reference()
    {
        var graph = new Dictionary<string, ProjectNode>
        {
            [ReferenceRules.Domain] = Node(ReferenceRules.Domain, projects: [ReferenceRules.Contracts]),
            [ReferenceRules.Contracts] = Node(
                ReferenceRules.Contracts,
                packages: ["Microsoft.EntityFrameworkCore"],
                frameworks: ["Microsoft.AspNetCore.App"]),
        };

        var violations = ReferenceRules.Evaluate(graph);

        violations.ShouldContain($"{ReferenceRules.Domain} must not reference {ReferenceRules.Contracts}.");
        violations.ShouldContain($"{ReferenceRules.Contracts} must not reference package Microsoft.EntityFrameworkCore.");
        violations.ShouldContain($"{ReferenceRules.Contracts} must not reference framework Microsoft.AspNetCore.App.");
    }

    [Fact]
    public void Rules_flag_extra_packages_in_Application()
    {
        var graph = new Dictionary<string, ProjectNode>
        {
            [ReferenceRules.Application] = Node(
                ReferenceRules.Application,
                projects: [ReferenceRules.Domain, ReferenceRules.Contracts],
                packages: ["SyntaxCircus.Common", "Npgsql"]),
        };

        ReferenceRules.Evaluate(graph)
            .ShouldContain($"{ReferenceRules.Application} may reference only SyntaxCircus.Common, not package Npgsql.");
    }

    [Fact]
    public void Rules_flag_an_unknown_project()
    {
        var graph = new Dictionary<string, ProjectNode> { ["TechStrap.Mystery"] = Node("TechStrap.Mystery") };

        ReferenceRules.Evaluate(graph)
            .ShouldContain("TechStrap.Mystery: unknown project. Add it to ReferenceRules.AllowedProjectReferences.");
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

```bash
dotnet test --project tests/TechStrap.Architecture.Tests
```

Expected: FAIL to build, with errors such as `CS0103: The name 'ProjectGraph' does not exist in the current context` and `CS0103: The name 'ReferenceRules' does not exist in the current context`.

- [ ] **Step 4: Write the rule and graph helpers**

`ProjectGraph` parses a `.csproj` into direct project, package and framework references and finds the repo root by walking up to `TechStrap.slnx`:

```csharp
using System.Xml.Linq;

namespace TechStrap.Architecture.Tests;

/// <summary>One src project as declared in its .csproj: direct project, package and framework references.</summary>
public sealed record ProjectNode(
    string Name,
    IReadOnlySet<string> ProjectReferences,
    IReadOnlySet<string> PackageReferences,
    IReadOnlySet<string> FrameworkReferences);

public static class ProjectGraph
{
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("TechStrap.slnx was not found above " + AppContext.BaseDirectory);
    }

    public static IReadOnlyDictionary<string, ProjectNode> LoadSourceProjects(string repositoryRoot)
    {
        var projects = Directory
            .GetFiles(Path.Combine(repositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Parse)
            .ToList();

        return projects.ToDictionary(project => project.Name, StringComparer.Ordinal);
    }

    public static ProjectNode Parse(string csprojPath)
    {
        var document = XDocument.Load(csprojPath);
        var projectReferences = document
            .Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
            .ToHashSet(StringComparer.Ordinal);
        var packageReferences = document
            .Descendants("PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        var frameworkReferences = document
            .Descendants("FrameworkReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        return new ProjectNode(
            Path.GetFileNameWithoutExtension(csprojPath),
            projectReferences,
            packageReferences,
            frameworkReferences);
    }
}
```

`ReferenceRules` holds the allowed direction from `02-ARCHITECTURE.md` section 2. `Evaluate` is a pure function over a graph:

```csharp
namespace TechStrap.Architecture.Tests;

/// <summary>
/// The allowed reference direction from docs/architecture/02-ARCHITECTURE.md section 2.
/// Evaluate() is a pure function so the tests can prove it fails on a deliberately bad graph.
/// </summary>
public static class ReferenceRules
{
    public const string Domain = "TechStrap.Domain";
    public const string Contracts = "TechStrap.Contracts";
    public const string Application = "TechStrap.Application";
    public const string Infrastructure = "TechStrap.Infrastructure";
    public const string Api = "TechStrap.Api";
    public const string Worker = "TechStrap.Worker";
    public const string Admin = "TechStrap.Admin";
    public const string Portal = "TechStrap.Portal";
    public const string Client = "TechStrap.Client";
    public const string ClientMaui = "TechStrap.Client.Maui";

    private const string CommonPackage = "SyntaxCircus.Common";

    public static IReadOnlyDictionary<string, string[]> AllowedProjectReferences { get; } =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Domain] = [],
            [Contracts] = [],
            [Application] = [Domain, Contracts],
            [Infrastructure] = [Application, Domain, Contracts],
            [Api] = [Application, Infrastructure, Contracts],
            [Worker] = [Application, Infrastructure, Contracts],
            [Admin] = [Contracts],
            [Portal] = [Contracts],
            [Client] = [Contracts],
            [ClientMaui] = [Client, Contracts],
        };

    /// <summary>Projects that must stay free of packages and framework references (leaf libraries).</summary>
    public static IReadOnlyCollection<string> DependencyFreeProjects { get; } = [Domain, Contracts];

    public static IReadOnlyList<string> Evaluate(IReadOnlyDictionary<string, ProjectNode> graph)
    {
        var violations = new List<string>();

        foreach (var project in graph.Values.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            if (!AllowedProjectReferences.TryGetValue(project.Name, out var allowed))
            {
                violations.Add($"{project.Name}: unknown project. Add it to ReferenceRules.AllowedProjectReferences.");
                continue;
            }

            foreach (var reference in project.ProjectReferences.Where(r => !allowed.Contains(r)).Order(StringComparer.Ordinal))
            {
                violations.Add($"{project.Name} must not reference {reference}.");
            }

            if (DependencyFreeProjects.Contains(project.Name))
            {
                foreach (var package in project.PackageReferences.Order(StringComparer.Ordinal))
                {
                    violations.Add($"{project.Name} must not reference package {package}.");
                }

                foreach (var framework in project.FrameworkReferences.Order(StringComparer.Ordinal))
                {
                    violations.Add($"{project.Name} must not reference framework {framework}.");
                }
            }

            if (project.Name == Application)
            {
                foreach (var package in project.PackageReferences.Where(p => p != CommonPackage).Order(StringComparer.Ordinal))
                {
                    violations.Add($"{Application} may reference only {CommonPackage}, not package {package}.");
                }

                foreach (var framework in project.FrameworkReferences.Order(StringComparer.Ordinal))
                {
                    violations.Add($"{Application} must not reference framework {framework}.");
                }
            }
        }

        foreach (var expected in AllowedProjectReferences.Keys.Where(name => !graph.ContainsKey(name)).Order(StringComparer.Ordinal))
        {
            violations.Add($"{expected}: project is missing from src/.");
        }

        return violations;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Architecture.Tests
```

Expected: `Test run summary: Passed!` with `total: 10`, `failed: 0`.

- [ ] **Step 6: Prove the rule fails on a real violation**

Temporarily add `<ProjectReference Include="../TechStrap.Application/TechStrap.Application.csproj" />` to the `ItemGroup` in `src/TechStrap.Admin/TechStrap.Admin.csproj`, then run:

```bash
dotnet test --project tests/TechStrap.Architecture.Tests --filter-class "*ProjectReferenceDirectionTests"
```

Expected: FAIL. `Source_projects_follow_the_allowed_reference_direction` reports `["TechStrap.Admin must not reference TechStrap.Application."]` and `Admin_and_Portal_reference_only_Contracts` fails. Remove the line again and re-run; both pass.

- [ ] **Step 7: Prove `TreatWarningsAsErrors` and GitVersion stamping**

Create `src/TechStrap.Domain/Probe.cs`:

```csharp
namespace TechStrap.Domain;

internal static class Probe
{
    public static void Run()
    {
        var unused = 1;
    }
}
```

```bash
dotnet build src/TechStrap.Domain
```

Expected: FAIL with `error CS0219: The variable 'unused' is assigned but its value is never used`. Delete `Probe.cs`, then:

```bash
dotnet build src/TechStrap.Domain
dotnet msbuild src/TechStrap.Api/TechStrap.Api.csproj -nologo -verbosity:quiet -target:GetVersion -getProperty:GitVersion_SemVer
```

Expected: the build succeeds; the second command prints a SemVer such as `0.1.0-feat-phase-01-foundation.1`.

- [ ] **Step 8: Verify the CI filter and the whole suite**

```bash
dotnet build TechStrap.CI.slnf --no-incremental
dotnet test --solution TechStrap.slnx
```

Expected: the filtered build succeeds and mentions no `TechStrap.Client.Maui`; the test run prints `Test run summary: Passed!` with `total: 14` (10 architecture tests plus 4 placeholders).

- [ ] **Step 9: Commit**

```bash
git add TechStrap.slnx TechStrap.CI.slnf src tests
git commit -m "build: add the 15-project solution skeleton and reference-direction architecture tests" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 3: Handler boundary architecture rules

Maps to P01-T05. Deliverable: `HandlerShapeTests`, `HandlerConstructorDependencyTests` and `ControllerHandlerInjectionTests`. The rules pass vacuously now (no handlers or controllers exist) and are proven able to fail by deliberately bad fixture types that live only in the test assembly.

**Files:**
- Create: `src/TechStrap.Application/ApplicationAssemblyMarker.cs`, `src/TechStrap.Infrastructure/InfrastructureAssemblyMarker.cs`
- Create (test): `tests/TechStrap.Architecture.Tests/{HandlerShapeTests,HandlerConstructorDependencyTests,ControllerHandlerInjectionTests,HandlerRules,HandlerFixtures}.cs`

**Interfaces:**
- Consumes: the Task 2 projects. `TechStrap.Api.Program` (the empty partial class from Task 2) anchors the Api assembly.
- Produces: `ApplicationAssemblyMarker` (static class) and `InfrastructureAssemblyMarker` (sealed class) as assembly anchors; `HandlerRules.FindShapeViolations(IEnumerable<Type>)`, `HandlerRules.FindConstructorDependencyViolations(IEnumerable<Type>, Assembly infrastructureAssembly)` and `HandlerRules.FindControllerInjectionViolations(IEnumerable<Type>)`, each returning a list of violation messages (empty when clean). A handler is a non-abstract class whose name ends in `Handler`; its interface is `I` plus the class name and must declare `HandleAsync` with a `CancellationToken` as the last parameter. Later phases add handlers to Application and controllers to Api and these tests pick them up automatically.

- [ ] **Step 1: Write the failing tests**

The tests scan the real assemblies (must be clean) and the fixtures (must be flagged):

```csharp
using TechStrap.Application;

namespace TechStrap.Architecture.Tests;

public sealed class HandlerShapeTests
{
    private static readonly Type[] ApplicationTypes = typeof(ApplicationAssemblyMarker).Assembly.GetTypes();

    [Fact]
    public void Application_handlers_are_sealed_have_a_matching_interface_and_take_a_cancellation_token()
    {
        // Passes vacuously until PHASE-04 adds the first handler; the rule is already enforced.
        HandlerRules.FindShapeViolations(ApplicationTypes).ShouldBeEmpty();
    }

    [Fact]
    public void A_well_formed_handler_passes()
    {
        HandlerRules.FindShapeViolations([typeof(HandlerFixtures.GoodSampleHandler)]).ShouldBeEmpty();
    }

    [Fact]
    public void An_unsealed_handler_is_flagged()
    {
        HandlerRules.FindShapeViolations([typeof(HandlerFixtures.UnsealedHandler)])
            .ShouldContain("UnsealedHandler must be sealed.");
    }

    [Fact]
    public void A_handler_without_a_matching_interface_is_flagged()
    {
        HandlerRules.FindShapeViolations([typeof(HandlerFixtures.NoInterfaceHandler)])
            .ShouldContain("NoInterfaceHandler must implement INoInterfaceHandler.");
    }

    [Fact]
    public void A_handler_method_without_a_cancellation_token_is_flagged()
    {
        HandlerRules.FindShapeViolations([typeof(HandlerFixtures.NoCancellationHandler)])
            .ShouldContain("INoCancellationHandler.HandleAsync must take a CancellationToken as its last parameter.");
    }
}
```

```csharp
using System.Reflection;
using TechStrap.Application;
using TechStrap.Infrastructure;

namespace TechStrap.Architecture.Tests;

public sealed class HandlerConstructorDependencyTests
{
    private static readonly Type[] ApplicationTypes = typeof(ApplicationAssemblyMarker).Assembly.GetTypes();
    private static readonly Assembly InfrastructureAssembly = typeof(InfrastructureAssemblyMarker).Assembly;

    [Fact]
    public void Application_handlers_depend_only_on_approved_abstractions()
    {
        // Passes vacuously until PHASE-04 adds the first handler; the rule is already enforced.
        HandlerRules.FindConstructorDependencyViolations(ApplicationTypes, InfrastructureAssembly).ShouldBeEmpty();
    }

    [Fact]
    public void A_handler_taking_only_an_application_interface_passes()
    {
        HandlerRules.FindConstructorDependencyViolations([typeof(HandlerFixtures.GoodSampleHandler)], InfrastructureAssembly)
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(HandlerFixtures.DbContextHandler), "Microsoft.EntityFrameworkCore.DbContext")]
    [InlineData(typeof(HandlerFixtures.DbSetHandler), "Microsoft.EntityFrameworkCore.DbSet")]
    [InlineData(typeof(HandlerFixtures.HttpContextHandler), "Microsoft.AspNetCore.Http.HttpContext")]
    [InlineData(typeof(HandlerFixtures.HttpContextAccessorHandler), "Microsoft.AspNetCore.Http.IHttpContextAccessor")]
    [InlineData(typeof(HandlerFixtures.ActionResultHandler), "Microsoft.AspNetCore.Mvc.IActionResult")]
    [InlineData(typeof(HandlerFixtures.ControllerBaseHandler), "Microsoft.AspNetCore.Mvc.ControllerBase")]
    [InlineData(typeof(HandlerFixtures.ConcreteInfrastructureHandler), "TechStrap.Infrastructure.InfrastructureAssemblyMarker")]
    [InlineData(typeof(HandlerFixtures.NestedGenericDependencyHandler), "Microsoft.EntityFrameworkCore.DbSet")]
    public void A_handler_with_a_forbidden_dependency_is_flagged(Type badHandler, string forbiddenTypeFragment)
    {
        var violations = HandlerRules.FindConstructorDependencyViolations([badHandler], InfrastructureAssembly);

        violations.ShouldContain(violation => violation.Contains(forbiddenTypeFragment, StringComparison.Ordinal));
    }
}
```

```csharp
namespace TechStrap.Architecture.Tests;

public sealed class ControllerHandlerInjectionTests
{
    private static readonly Type[] ApiTypes = typeof(TechStrap.Api.Program).Assembly.GetTypes();

    [Fact]
    public void Api_controllers_take_handlers_through_FromServices_action_parameters_only()
    {
        // Passes vacuously until PHASE-04 adds the first controller; the rule is already enforced.
        HandlerRules.FindControllerInjectionViolations(ApiTypes).ShouldBeEmpty();
    }

    [Fact]
    public void A_controller_using_FromServices_passes()
    {
        HandlerRules.FindControllerInjectionViolations([typeof(HandlerFixtures.GoodController)]).ShouldBeEmpty();
    }

    [Fact]
    public void A_controller_with_a_constructor_injected_handler_is_flagged()
    {
        HandlerRules.FindControllerInjectionViolations([typeof(HandlerFixtures.ConstructorInjectedController)])
            .ShouldContain(violation => violation.Contains("constructor must not take IGoodSampleHandler", StringComparison.Ordinal));
    }

    [Fact]
    public void An_action_parameter_without_FromServices_is_flagged()
    {
        HandlerRules.FindControllerInjectionViolations([typeof(HandlerFixtures.MissingFromServicesController)])
            .ShouldContain(violation => violation.Contains("must use [FromServices]", StringComparison.Ordinal));
    }

    [Fact]
    public void An_action_depending_on_a_concrete_handler_is_flagged()
    {
        HandlerRules.FindControllerInjectionViolations([typeof(HandlerFixtures.ConcreteHandlerController)])
            .ShouldContain(violation => violation.Contains("must depend on the handler interface", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test --project tests/TechStrap.Architecture.Tests
```

Expected: FAIL to build, with `CS0246`/`CS0103` errors for `ApplicationAssemblyMarker`, `InfrastructureAssemblyMarker`, `HandlerRules` and `HandlerFixtures`.

- [ ] **Step 3: Write the assembly markers**

```csharp
namespace TechStrap.Application;

/// <summary>Anchor for assembly scanning (architecture tests, DI registration).</summary>
public static class ApplicationAssemblyMarker;
```

```csharp
namespace TechStrap.Infrastructure;

/// <summary>Anchor for assembly scanning (architecture tests).</summary>
public sealed class InfrastructureAssemblyMarker;
```

- [ ] **Step 4: Write the rules**

The rules are pure functions over a set of types. The forbidden constructor dependencies are any type in the Infrastructure assembly and any type in the `Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore.Mvc`, `Microsoft.AspNetCore.Http` or `Npgsql` namespaces (generic arguments and array elements are checked too):

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Handler boundary rules from _template APPLICATION_ARCHITECTURE.md. Each method is a pure function
/// over a set of types so the tests can run it against both the real assemblies (which must pass)
/// and deliberately bad fixture types (which must fail).
/// </summary>
public static class HandlerRules
{
    public const string HandlerSuffix = "Handler";
    public const string HandleMethodName = "HandleAsync";

    /// <summary>Namespaces a handler constructor must never depend on (EF, MVC, HTTP and Npgsql types).</summary>
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore.Mvc",
        "Microsoft.AspNetCore.Http",
        "Npgsql",
    ];

    public static bool IsHandlerClass(Type type) =>
        type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith(HandlerSuffix, StringComparison.Ordinal);

    public static IReadOnlyList<string> FindShapeViolations(IEnumerable<Type> types)
    {
        var violations = new List<string>();

        foreach (var type in types.Where(IsHandlerClass).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (!type.IsSealed)
            {
                violations.Add($"{type.Name} must be sealed.");
            }

            var expectedInterfaceName = "I" + type.Name;
            var handlerInterface = type.GetInterfaces().FirstOrDefault(i => i.Name == expectedInterfaceName);
            if (handlerInterface is null)
            {
                violations.Add($"{type.Name} must implement {expectedInterfaceName}.");
                continue;
            }

            if (!handlerInterface.IsPublic && !handlerInterface.IsNestedPublic)
            {
                violations.Add($"{expectedInterfaceName} must be public.");
            }

            var handleMethods = handlerInterface.GetMethods().Where(m => m.Name == HandleMethodName).ToList();
            if (handleMethods.Count == 0)
            {
                violations.Add($"{expectedInterfaceName} must declare {HandleMethodName}.");
            }

            foreach (var method in handleMethods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[^1].ParameterType != typeof(CancellationToken))
                {
                    violations.Add($"{expectedInterfaceName}.{HandleMethodName} must take a CancellationToken as its last parameter.");
                }
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> FindConstructorDependencyViolations(
        IEnumerable<Type> types,
        Assembly infrastructureAssembly)
    {
        var violations = new List<string>();

        foreach (var type in types.Where(IsHandlerClass).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    foreach (var dependency in Flatten(parameter.ParameterType))
                    {
                        if (IsForbidden(dependency, infrastructureAssembly))
                        {
                            violations.Add($"{type.Name} must not depend on {dependency.FullName}.");
                        }
                    }
                }
            }
        }

        return violations;
    }

    /// <summary>Controllers take handlers through [FromServices] action parameters, never the constructor.</summary>
    public static IReadOnlyList<string> FindControllerInjectionViolations(IEnumerable<Type> types)
    {
        var violations = new List<string>();

        var controllers = types
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var controller in controllers)
        {
            foreach (var constructor in controller.GetConstructors())
            {
                foreach (var parameter in constructor.GetParameters().Where(p => IsHandlerType(p.ParameterType)))
                {
                    violations.Add($"{controller.Name} constructor must not take {parameter.ParameterType.Name}; bind it with [FromServices] on the action.");
                }
            }

            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var action in actions)
            {
                foreach (var parameter in action.GetParameters().Where(p => IsHandlerType(p.ParameterType)))
                {
                    if (!parameter.ParameterType.IsInterface)
                    {
                        violations.Add($"{controller.Name}.{action.Name} must depend on the handler interface, not {parameter.ParameterType.Name}.");
                    }

                    if (!parameter.GetCustomAttributes(typeof(FromServicesAttribute), inherit: false).Any())
                    {
                        violations.Add($"{controller.Name}.{action.Name} parameter {parameter.Name} must use [FromServices].");
                    }
                }
            }
        }

        return violations;
    }

    private static bool IsHandlerType(Type type) =>
        type.Name.EndsWith(HandlerSuffix, StringComparison.Ordinal);

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }

        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var inner in Flatten(element))
            {
                yield return inner;
            }
        }
    }

    private static bool IsForbidden(Type dependency, Assembly infrastructureAssembly)
    {
        if (dependency.Assembly == infrastructureAssembly)
        {
            return true;
        }

        var ns = dependency.Namespace ?? string.Empty;
        return ForbiddenNamespacePrefixes.Any(prefix =>
            ns.Equals(prefix, StringComparison.Ordinal) || ns.StartsWith(prefix + ".", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 5: Write the bad fixtures**

One good handler and good controller, plus one bad type per rule: unsealed handler, handler without its interface, `HandleAsync` without a `CancellationToken`, handlers taking `DbContext`, `DbSet<>`, `HttpContext`, `IHttpContextAccessor`, `IActionResult`, `ControllerBase`, a concrete Infrastructure type and a nested `IEnumerable<DbSet<>>`, a controller with a constructor-injected handler, an action parameter without `[FromServices]`, and an action taking a concrete handler:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechStrap.Infrastructure;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Deliberately bad (and a few good) types that live only in this test assembly. The rule tests feed
/// them to HandlerRules to prove each rule can fail. The real scans look at the Application and
/// Api assemblies only, so these are never picked up as production code.
/// </summary>
public static class HandlerFixtures
{
    public sealed record SampleRequest(string Name);

    public interface ISampleRepository;

    public sealed class FixtureEntity
    {
        public int Id { get; set; }
    }

    // ---- shape fixtures ----

    public interface IGoodSampleHandler
    {
        Task HandleAsync(SampleRequest request, CancellationToken cancellationToken);
    }

    public sealed class GoodSampleHandler(ISampleRepository repository) : IGoodSampleHandler
    {
        public ISampleRepository Repository { get; } = repository;

        public Task HandleAsync(SampleRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public interface IUnsealedHandler
    {
        Task HandleAsync(SampleRequest request, CancellationToken cancellationToken);
    }

    public class UnsealedHandler : IUnsealedHandler
    {
        public Task HandleAsync(SampleRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class NoInterfaceHandler
    {
        public Task HandleAsync(SampleRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public interface INoCancellationHandler
    {
        Task HandleAsync(SampleRequest request);
    }

    public sealed class NoCancellationHandler : INoCancellationHandler
    {
        public Task HandleAsync(SampleRequest request) => Task.CompletedTask;
    }

    // ---- constructor dependency fixtures ----

    public sealed class DbContextHandler(DbContext context)
    {
        public DbContext Context { get; } = context;
    }

    public sealed class DbSetHandler(DbSet<FixtureEntity> entities)
    {
        public DbSet<FixtureEntity> Entities { get; } = entities;
    }

    public sealed class HttpContextHandler(HttpContext context)
    {
        public HttpContext Context { get; } = context;
    }

    public sealed class HttpContextAccessorHandler(IHttpContextAccessor accessor)
    {
        public IHttpContextAccessor Accessor { get; } = accessor;
    }

    public sealed class ActionResultHandler(IActionResult result)
    {
        public IActionResult Result { get; } = result;
    }

    public sealed class ControllerBaseHandler(ControllerBase controller)
    {
        public ControllerBase Controller { get; } = controller;
    }

    public sealed class ConcreteInfrastructureHandler(InfrastructureAssemblyMarker infrastructure)
    {
        public InfrastructureAssemblyMarker Infrastructure { get; } = infrastructure;
    }

    public sealed class NestedGenericDependencyHandler(IEnumerable<DbSet<FixtureEntity>> entities)
    {
        public IEnumerable<DbSet<FixtureEntity>> Entities { get; } = entities;
    }

    // ---- controller fixtures ----

    public sealed class GoodController : ControllerBase
    {
        public Task<IActionResult> Create(SampleRequest request, [FromServices] IGoodSampleHandler handler, CancellationToken ct) =>
            Task.FromResult<IActionResult>(Ok());
    }

    public sealed class ConstructorInjectedController(IGoodSampleHandler handler) : ControllerBase
    {
        public IGoodSampleHandler Handler { get; } = handler;
    }

    public sealed class MissingFromServicesController : ControllerBase
    {
        public Task<IActionResult> Create(SampleRequest request, IGoodSampleHandler handler, CancellationToken ct) =>
            Task.FromResult<IActionResult>(Ok());
    }

    public sealed class ConcreteHandlerController : ControllerBase
    {
        public Task<IActionResult> Create(SampleRequest request, [FromServices] GoodSampleHandler handler, CancellationToken ct) =>
            Task.FromResult<IActionResult>(Ok());
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Architecture.Tests
```

Expected: `Test run summary: Passed!` with `total: 30` (10 from Task 2, 5 shape, 10 constructor dependency, 5 controller).

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Application src/TechStrap.Infrastructure tests/TechStrap.Architecture.Tests
git commit -m "test: add handler shape, constructor dependency and controller injection architecture rules" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 4: `TechStrapDbContext`, snake_case, readiness check and `PostgresFixture`

Maps to P01-T09 (the context and its DI registration) and P01-T11 (`PostgresFixture` and `PostgresIntegrationTestBase`). Deliverable: an empty, snake_case `TechStrapDbContext` registered for the Api and Worker with a readiness check, plus the shared Testcontainers fixture later phases reuse.

**Files:**
- Create: `src/TechStrap.Infrastructure/Persistence/{TechStrapDatabase,TechStrapDbContext,TechStrapDbContextFactory,DatabaseReadinessHealthCheck,PersistenceServiceCollectionExtensions}.cs`
- Create (test): `tests/TechStrap.Infrastructure.IntegrationTests/{PostgresFixture,PostgresIntegrationTestBase,PostgresFixtureSmokeTests,SnakeCaseConventionTests}.cs`
- Delete: `tests/TechStrap.Infrastructure.IntegrationTests/PlaceholderTests.cs`

**Interfaces:**
- Consumes: `SyntaxCircus.EntityFrameworkCore.Postgres` `0.1.3` (`UseSyntaxCircusSnakeCaseNamingConvention()` on `DbContextOptionsBuilder`, `MigrateWithAdvisoryLockAsync<TContext>(long lockKey, CancellationToken)` on a `DbContext`), `Npgsql.EntityFrameworkCore.PostgreSQL` `10.0.3`, `Testcontainers.PostgreSql` `4.15.0`.
- Produces:
  - `TechStrapDatabase.ConnectionStringName` (`"TechStrap"`, the `ConnectionStrings:TechStrap` key), `TechStrapDatabase.MigrationLockKey` (`long`), `TechStrapDatabase.ReadyHealthTag` (`"ready"`), `TechStrapDatabase.Configure(DbContextOptionsBuilder, string? connectionString) : DbContextOptionsBuilder`.
  - `TechStrapDbContext(DbContextOptions<TechStrapDbContext>)`, sealed, empty model.
  - `IServiceCollection.AddTechStrapPersistence()` registers the context (connection string resolved lazily from `IConfiguration`) and a health check `"database"` tagged `ready`.
  - Test side: `PostgresFixture` (xunit.v3 assembly fixture, one `postgres:17` container, `CreateDatabaseAsync(bool migrated = true) : Task<TestDatabase>`, `ConnectionStringFor(string database)`), `TestDatabase` (`Name`, `ConnectionString`, `CreateDbContext() : TechStrapDbContext`, `IAsyncDisposable` drops the database), `PostgresIntegrationTestBase(PostgresFixture)` (per-test migrated database, `Database`, `CreateDbContext()`).

- [ ] **Step 1: Write the failing tests**

Delete the placeholder, then add two test classes. The smoke tests share one container and each create the same table: if databases leaked between tests the second `CREATE TABLE` would fail. The convention test builds a throwaway context with the same options (the real context has no entities yet):

```bash
git rm tests/TechStrap.Infrastructure.IntegrationTests/PlaceholderTests.cs
```

```csharp
using Microsoft.EntityFrameworkCore;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Two tests share one container (the assembly fixture) but each gets a fresh database. Both create
/// the same table: if databases leaked between tests, the second CREATE TABLE would fail.
/// </summary>
public sealed class PostgresFixtureSmokeTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    [Fact]
    public async Task Runs_against_postgres_17()
    {
        await using var context = CreateDbContext();

        var version = await context.Database.SqlQueryRaw<string>("SELECT version() AS \"Value\"").SingleAsync(TestContext.Current.CancellationToken);

        version.ShouldStartWith("PostgreSQL 17");
    }

    [Fact]
    public async Task First_test_gets_an_isolated_database()
    {
        await using var context = CreateDbContext();

        await context.Database.ExecuteSqlRawAsync("CREATE TABLE isolation_marker (id int)", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Second_test_gets_an_isolated_database()
    {
        await using var context = CreateDbContext();

        await context.Database.ExecuteSqlRawAsync("CREATE TABLE isolation_marker (id int)", TestContext.Current.CancellationToken);
    }
}
```

```csharp
using Microsoft.EntityFrameworkCore;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Proves TechStrapDatabase.Configure applies the snake_case convention. TechStrapDbContext has no
/// entities yet, so this uses a throwaway context built with the same options. No database needed.
/// </summary>
public sealed class SnakeCaseConventionTests
{
    [Fact]
    public void Tables_and_columns_are_snake_case()
    {
        var options = new DbContextOptionsBuilder<ConventionProbeContext>();
        TechStrapDatabase.Configure(options, "Host=localhost;Database=probe");
        using var context = new ConventionProbeContext(options.Options);

        var entity = context.Model.FindEntityType(typeof(TicketProbe))!;

        entity.GetTableName().ShouldBe("ticket_probe");
        entity.FindProperty(nameof(TicketProbe.LastActivityAt))!.GetColumnName().ShouldBe("last_activity_at");
    }

    private sealed class TicketProbe
    {
        public int Id { get; set; }

        public DateTimeOffset LastActivityAt { get; set; }
    }

    private sealed class ConventionProbeContext(DbContextOptions<ConventionProbeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.Entity<TicketProbe>();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests
```

Expected: FAIL to build: `CS0246: The type or namespace name 'PostgresIntegrationTestBase' could not be found`, `PostgresFixture`, and `TechStrapDatabase` / `TechStrap.Infrastructure.Persistence`.

- [ ] **Step 3: Write the persistence code in Infrastructure**

The single place that configures EF options (Npgsql plus the SyntaxCircus snake_case convention), with the constants the Api, Worker and tests share:

```csharp
using Microsoft.EntityFrameworkCore;
using SyntaxCircus.EntityFrameworkCore.Postgres;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>Database-wide constants and the single place that configures EF options for TechStrap.</summary>
public static class TechStrapDatabase
{
    /// <summary>Name under ConnectionStrings: ConnectionStrings__TechStrap.</summary>
    public const string ConnectionStringName = "TechStrap";

    /// <summary>Postgres advisory lock key that serializes migrate-on-startup across API instances.</summary>
    public const long MigrationLockKey = 6_387_541_208;

    /// <summary>Health check tag that /health/ready selects.</summary>
    public const string ReadyHealthTag = "ready";

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string? connectionString) =>
        builder
            .UseNpgsql(connectionString ?? string.Empty)
            .UseSyntaxCircusSnakeCaseNamingConvention();
}
```

The empty context:

```csharp
using Microsoft.EntityFrameworkCore;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>
/// The TechStrap database. The model is empty in PHASE-01; PHASE-03 adds the entity configurations.
/// Naming (snake_case) comes from <see cref="TechStrapDatabase.Configure"/>.
/// </summary>
public sealed class TechStrapDbContext(DbContextOptions<TechStrapDbContext> options) : DbContext(options);
```

A design-time factory so `dotnet ef` works without starting a host or a database:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>
/// Used only by the dotnet ef tool, so migrations can be generated without starting a host or a database.
/// Override the connection with the TECHSTRAP_DESIGN_CONNECTION environment variable if needed.
/// </summary>
public sealed class TechStrapDbContextFactory : IDesignTimeDbContextFactory<TechStrapDbContext>
{
    private const string DesignConnectionVariable = "TECHSTRAP_DESIGN_CONNECTION";
    private const string DefaultDesignConnection = "Host=localhost;Port=5432;Database=techstrap_design;Username=postgres;Password=postgres";

    public TechStrapDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(DesignConnectionVariable) ?? DefaultDesignConnection;
        var builder = new DbContextOptionsBuilder<TechStrapDbContext>();
        TechStrapDatabase.Configure(builder, connectionString);
        return new TechStrapDbContext(builder.Options);
    }
}
```

The readiness check (`CanConnectAsync`; any exception is reported as unhealthy):

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TechStrap.Infrastructure.Persistence;

/// <summary>Readiness probe: the database accepts connections. Registered under the "ready" tag.</summary>
public sealed class DatabaseReadinessHealthCheck(TechStrapDbContext database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await database.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database reachable.")
                : HealthCheckResult.Unhealthy("Database not reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Database check failed.", exception);
        }
    }
}
```

The DI registration used by the Api and the Worker only:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TechStrapDbContext"/> and the "database" readiness check. The connection
    /// string is resolved when the context is first created, so test overrides apply.
    /// Used by the Api and the Worker only; Admin and Portal never touch the database.
    /// </summary>
    public static IServiceCollection AddTechStrapPersistence(this IServiceCollection services)
    {
        services.AddDbContext<TechStrapDbContext>((provider, options) =>
            TechStrapDatabase.Configure(
                options,
                provider.GetRequiredService<IConfiguration>().GetConnectionString(TechStrapDatabase.ConnectionStringName)));

        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>("database", tags: [TechStrapDatabase.ReadyHealthTag]);

        return services;
    }
}
```

- [ ] **Step 4: Write the Testcontainers fixture and test base**

xunit.v3 assembly fixtures are declared with `[assembly: AssemblyFixture(typeof(...))]` and injected into test class constructors. `IAsyncLifetime` in v3 returns `ValueTask`. The fixture migrates a template database once; each test then gets a cheap copy (`CREATE DATABASE ... TEMPLATE`), so tests never see each other's rows:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SyntaxCircus.EntityFrameworkCore.Postgres;
using TechStrap.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

// xunit.v3 assembly fixture: one Postgres 17 container is started for the whole test assembly and
// injected into any test class constructor that asks for PostgresFixture.
[assembly: AssemblyFixture(typeof(TechStrap.Infrastructure.IntegrationTests.PostgresFixture))]

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Starts one postgres:17 container, migrates a template database once, and hands out a cheap
/// per-test copy of it (CREATE DATABASE ... TEMPLATE). Requires Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:17";
    private const string TemplateDatabase = "techstrap_template";
    private const string MaintenanceDatabase = "postgres";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage).Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        await ExecuteAdminAsync($"CREATE DATABASE \"{TemplateDatabase}\"");

        var options = new DbContextOptionsBuilder<TechStrapDbContext>();
        TechStrapDatabase.Configure(options, ConnectionStringFor(TemplateDatabase));
        await using var context = new TechStrapDbContext(options.Options);
        await context.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = database,
            Pooling = false,
        }.ConnectionString;

    /// <summary>Creates an isolated database. <paramref name="migrated"/> copies the migrated template; otherwise it is empty.</summary>
    public async Task<TestDatabase> CreateDatabaseAsync(bool migrated = true)
    {
        var name = $"t_{Guid.NewGuid():N}";
        var template = migrated ? $" TEMPLATE \"{TemplateDatabase}\"" : string.Empty;
        await ExecuteAdminAsync($"CREATE DATABASE \"{name}\"{template}");
        return new TestDatabase(name, ConnectionStringFor(name), DropDatabaseAsync);
    }

    private Task DropDatabaseAsync(string name) => ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionStringFor(MaintenanceDatabase));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>One isolated database for one test. Disposing drops it.</summary>
public sealed class TestDatabase(string name, string connectionString, Func<string, Task> drop) : IAsyncDisposable
{
    public string Name { get; } = name;

    public string ConnectionString { get; } = connectionString;

    public TechStrapDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TechStrapDbContext>();
        TechStrapDatabase.Configure(options, ConnectionString);
        return new TechStrapDbContext(options.Options);
    }

    public async ValueTask DisposeAsync() => await drop(Name);
}
```

```csharp
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Base class for tests that need a real database. Every test gets its own migrated database
/// (a copy of the template), dropped when the test ends, so tests never see each other's rows.
/// </summary>
public abstract class PostgresIntegrationTestBase(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture Postgres { get; } = postgres;

    protected TestDatabase Database { get; private set; } = null!;

    public async ValueTask InitializeAsync() => Database = await Postgres.CreateDatabaseAsync();

    public async ValueTask DisposeAsync() => await Database.DisposeAsync();

    protected TechStrapDbContext CreateDbContext() => Database.CreateDbContext();
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Docker must be running; the first run pulls `postgres:17`.

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests
```

Expected: `Test run summary: Passed!` with `total: 4`; the whole run, container start included, takes well under 60 seconds.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Infrastructure tests/TechStrap.Infrastructure.IntegrationTests
git commit -m "feat(infrastructure): add the snake_case DbContext, readiness check and Postgres test fixture" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 5: `MigrationStartupTests` and the `Initial` migration

Maps to P01-T09 (the migration tests) and P01-T10 (the migration generated by `dotnet ef`). Deliverable: an empty `Initial` migration produced by the EF tool and tests proving the startup migrator applies it once, is a no-op the second time, and serializes concurrent startups on the advisory lock.

**Files:**
- Modify: `src/TechStrap.Api/TechStrap.Api.csproj` (add `Microsoft.EntityFrameworkCore.Design`, `PrivateAssets="all"`)
- Create (generated, never edited): `src/TechStrap.Infrastructure/Migrations/*_Initial.cs`, `*_Initial.Designer.cs`, `TechStrapDbContextModelSnapshot.cs`
- Create (test): `tests/TechStrap.Infrastructure.IntegrationTests/MigrationStartupTests.cs`

**Interfaces:**
- Consumes: `TechStrapDatabase.MigrationLockKey`, `TechStrapDbContext` (Task 4), `PostgresFixture.CreateDatabaseAsync(migrated: false)` for an empty database, `MigrateWithAdvisoryLockAsync` from `SyntaxCircus.EntityFrameworkCore.Postgres`.
- Produces: the committed `Initial` migration (so `PostgresFixture`'s template database and every later test starts from it), and the exact tool command used for all future migrations.

- [ ] **Step 1: Write the failing tests**

The tests call the same method the API calls at startup. The advisory-lock test holds `pg_advisory_lock(key)` on a separate connection, starts the migrator, proves it has not finished after 1.5 seconds, releases the lock, and proves it then completes:

```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SyntaxCircus.EntityFrameworkCore.Postgres;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Exercises the same call the API makes at startup: MigrateWithAdvisoryLockAsync with the
/// TechStrap lock key, against an empty Postgres 17 database.
/// </summary>
public sealed class MigrationStartupTests(PostgresFixture postgres)
{
    private static readonly TimeSpan LockObservationDelay = TimeSpan.FromSeconds(1.5);

    [Fact]
    public async Task An_empty_database_migrates_to_exactly_the_Initial_migration()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrated: false);
        await using var context = database.CreateDbContext();

        await context.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken);

        var applied = await ReadHistoryAsync(database);
        applied.Count.ShouldBe(1);
        applied[0].ShouldEndWith("_Initial");
    }

    [Fact]
    public async Task Running_the_migrator_a_second_time_is_a_no_op()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrated: false);
        await using (var first = database.CreateDbContext())
        {
            await first.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken);
        }

        await using var second = database.CreateDbContext();
        await second.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken);

        (await ReadHistoryAsync(database)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Two_concurrent_startups_both_succeed_and_apply_the_migration_once()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrated: false);
        await using var first = database.CreateDbContext();
        await using var second = database.CreateDbContext();

        await Task.WhenAll(
            first.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken),
            second.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken));

        (await ReadHistoryAsync(database)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_startup_waits_while_another_instance_holds_the_advisory_lock()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrated: false);
        await using var holder = new NpgsqlConnection(database.ConnectionString);
        await holder.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(holder, $"SELECT pg_advisory_lock({TechStrapDatabase.MigrationLockKey})");

        await using var context = database.CreateDbContext();
        var migration = context.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, TestContext.Current.CancellationToken);

        await Task.Delay(LockObservationDelay, TestContext.Current.CancellationToken);
        migration.IsCompleted.ShouldBeFalse("the migrator must block on the advisory lock");

        await ExecuteAsync(holder, $"SELECT pg_advisory_unlock({TechStrapDatabase.MigrationLockKey})");
        await migration.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        (await ReadHistoryAsync(database)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_model_has_no_changes_that_are_missing_from_the_migrations()
    {
        await using var database = await postgres.CreateDatabaseAsync(migrated: false);
        await using var context = database.CreateDbContext();

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    private static async Task<List<string>> ReadHistoryAsync(TestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"";

        var ids = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests --filter-class "*MigrationStartupTests"
```

Expected: FAIL. Four of the five tests fail because no migration exists, so `__EFMigrationsHistory` is never created (`relation "__EFMigrationsHistory" does not exist`); `The_model_has_no_changes_that_are_missing_from_the_migrations` passes.

- [ ] **Step 3: Give the Api project the EF design package**

The `dotnet ef` tool needs `Microsoft.EntityFrameworkCore.Design` in the startup project (the Api). `PrivateAssets="all"` keeps it from flowing to anything that references the Api. Replace `src/TechStrap.Api/TechStrap.Api.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="../TechStrap.Application/TechStrap.Application.csproj" />
    <ProjectReference Include="../TechStrap.Infrastructure/TechStrap.Infrastructure.csproj" />
    <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="GitVersion.MsBuild" PrivateAssets="all" />
    <!-- Required in the startup project for the dotnet ef tool; never flows to consumers. -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Generate the migration with the EF tool**

Never write migration code by hand; the tool generates it. The local tool (10.0.12) comes from `.config/dotnet-tools.json`.

```bash
dotnet tool restore
dotnet ef migrations add Initial --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api
```

Expected: `Build started...`, `Build succeeded.`, `Done. To undo this action, use 'ef migrations remove'`. Three files appear in `src/TechStrap.Infrastructure/Migrations/`: `<timestamp>_Initial.cs`, `<timestamp>_Initial.Designer.cs` and `TechStrapDbContextModelSnapshot.cs`. The generated `Up` and `Down` methods are empty because the model is empty. Do not edit them.

- [ ] **Step 5: Confirm the model and the migration agree**

```bash
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api
```

Expected: `No changes have been made to the model since the last migration.` and exit code 0.

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests
```

Expected: `Test run summary: Passed!` with `total: 9` (4 from Task 4 plus 5 migration tests). `PostgresFixture` now copies a template that already holds the `Initial` migration for every `migrated: true` database.

- [ ] **Step 7: Commit**

The migration files are committed exactly as the tool wrote them.

```bash
git add src/TechStrap.Api/TechStrap.Api.csproj src/TechStrap.Infrastructure/Migrations tests/TechStrap.Infrastructure.IntegrationTests/MigrationStartupTests.cs
git commit -m "feat(infrastructure): add the Initial migration and migration startup tests" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 6: API composition root and health endpoints

Maps to P01-T06. Deliverable: the API host with DotEnv, Observability, Serilog, AspNetCore.Common (correlation id, security headers, ProblemDetails, health), OpenAPI and the persistence registration, proven by `HealthEndpointTests` through `WebApplicationFactory`.

**Files:**
- Modify: `src/TechStrap.Api/TechStrap.Api.csproj`, `src/TechStrap.Api/Program.cs`
- Create: `src/TechStrap.Api/appsettings.json`
- Create (test): `tests/TechStrap.Api.Tests/{TestPostgres,HostFactory,HealthEndpointTests,ObservabilityOptionsTests}.cs`
- Delete: `tests/TechStrap.Api.Tests/PlaceholderTests.cs`

**Interfaces:**
- Consumes: `AddTechStrapPersistence()` and the `ready` health tag (Task 4), the `Initial` migration (Task 5), and these SyntaxCircus APIs, all verified against the selected versions:
  - `SyntaxCircus.DotEnv` 0.1.3: `IConfiguration.ShouldLoadDotEnv(IHostEnvironment)`, `IConfigurationBuilder.AddSyntaxCircusDotEnvFiles(string contentRoot)`.
  - `SyntaxCircus.Observability` 0.1.2: `IHostApplicationBuilder.AddSyntaxCircusObservability(string serviceName)` returning a registration with `ConfigureSerilog`, `Options.Sentry.IsEnabled`, `ConfigureSentry(options, Func<..., double?>)` and `LogStartupWarning(ILogger)`; `SyntaxCircusObservabilityOptions.FromConfiguration(IConfiguration)`.
  - `SyntaxCircus.AspNetCore.Serilog` 0.1.4: `IHostApplicationBuilder.AddStandardSerilog(configureFileLogging: null, configureEnrichment: ...)`.
  - `SyntaxCircus.AspNetCore.Common` 0.1.15: `AddCorrelationId()`/`UseCorrelationId()` (header `X-Correlation-Id`), `AddSecurityHeaders(IConfiguration)`/`UseSecurityHeaders()`, `AddProblemDetailsExceptionHandling()`/`UseProblemDetailsExceptionHandling()`, `MapStandardHealthChecks()` (maps `/health/live` with no checks and `/health/ready` with checks tagged `ready`), `CorrelationContextAccessor.CurrentCorrelationId`.
- Produces: the Api host with `/health/live`, `/health/ready`, `/openapi/v1.json`; `HostFactory<TProgram>(string environment = "Development", IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null)` plus `ApiFactory`, `WorkerFactory`, `AdminFactory`, `PortalFactory` (each starts the host in-process, sets `DotEnv__Enabled=false` so a developer's `.env.local` never leaks into tests, adds `Database:MigrateOnStartup=false` by default and exposes `CollectingSink LogSink`, a Serilog sink that records every log event); `TestPostgres` (assembly fixture, `CreateDatabaseAsync() : Task<string>` returns a connection string for a new empty database).

- [ ] **Step 1: Write the failing tests**

Delete the placeholder, then add the test support and the tests. `TestPostgres` is a deliberately small container fixture for this assembly; the richer `PostgresFixture` stays in the integration test project. `HostFactory` applies settings as lazily bound in-memory configuration (options that Program.cs binds eagerly, such as `TrustedProxy` and the Observability sections, cannot be overridden this way; the factory documents it):

```bash
git rm tests/TechStrap.Api.Tests/PlaceholderTests.cs
```

```csharp
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(TechStrap.Api.Tests.TestPostgres))]

namespace TechStrap.Api.Tests;

/// <summary>
/// One postgres:17 container for the Api.Tests assembly. Deliberately small: repository-level tests
/// use the richer PostgresFixture in TechStrap.Infrastructure.IntegrationTests. Requires Docker.
/// </summary>
public sealed class TestPostgres : IAsyncLifetime
{
    private const string MaintenanceDatabase = "postgres";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a new empty database and returns a connection string for it.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(ConnectionStringFor(MaintenanceDatabase));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{name}\"";
        await command.ExecuteNonQueryAsync();
        return ConnectionStringFor(name);
    }

    public string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database, Pooling = false }.ConnectionString;
}
```

```csharp
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace TechStrap.Api.Tests;

/// <summary>Captures every Serilog event the host writes so tests can assert on log lines.</summary>
public sealed class CollectingSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events.ToArray();

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}

/// <summary>
/// Starts one of the TechStrap hosts in-process. Settings are applied as lazily-bound in-memory
/// configuration. TrustedProxy is NOT overridable this way: AddTrustedProxyForwardedHeaders binds it
/// eagerly in Program.cs, before the factory's configuration is applied (see CLIENT_IP_RATE_LIMITING.md).
/// </summary>
public class HostFactory<TProgram>(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    static HostFactory()
    {
        // A developer's gitignored .env.local must never leak into tests.
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");
    }

    public CollectingSink LogSink { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            // Tests opt in to migration explicitly; most do not need a database at startup.
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Database:MigrateOnStartup"] = "false" });
            configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
        });
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILogEventSink>(LogSink);
            configureServices?.Invoke(services);
        });
    }
}

public sealed class ApiFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null)
    : HostFactory<TechStrap.Api.Program>(environment, settings, configureServices);

public sealed class WorkerFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null)
    : HostFactory<TechStrap.Worker.Program>(environment, settings);

public sealed class AdminFactory(string environment = "Development")
    : HostFactory<TechStrap.Admin.Program>(environment);

public sealed class PortalFactory(string environment = "Development")
    : HostFactory<TechStrap.Portal.Program>(environment);
```

`HealthEndpointTests` covers live without a database, ready 503 with an unreachable database, ready 200 with Postgres, the correlation id echoed and present on the request log line (the request log event carries a `CorrelationId` property), an oversized caller-supplied correlation id (Review Focus 2), security headers and the anonymous OpenAPI document:

```csharp
using System.Net;
using System.Text.Json;

namespace TechStrap.Api.Tests;

public sealed class HealthEndpointTests(TestPostgres postgres)
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2;Pooling=false";

    private static IReadOnlyDictionary<string, string?> ConnectionString(string value) =>
        new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = value };

    private static async Task<string> ReadStatusAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("status").GetString()!;
    }

    [Fact]
    public async Task Live_returns_200_without_touching_the_database()
    {
        await using var factory = new ApiFactory(settings: ConnectionString(UnreachableDatabase));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadStatusAsync(response)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Ready_returns_503_when_the_database_is_unreachable()
    {
        await using var factory = new ApiFactory(settings: ConnectionString(UnreachableDatabase));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await ReadStatusAsync(response)).ShouldBe("Unhealthy");
    }

    [Fact]
    public async Task Ready_returns_200_when_postgres_is_reachable()
    {
        await using var factory = new ApiFactory(settings: ConnectionString(await postgres.CreateDatabaseAsync()));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadStatusAsync(response)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Responses_echo_the_correlation_id_header()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "corr-from-caller");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Correlation-Id").ShouldBe(["corr-from-caller"]);
    }

    [Fact]
    public async Task The_request_log_line_carries_the_correlation_id()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "corr-in-log");

        await client.SendAsync(request, TestContext.Current.CancellationToken);

        factory.LogSink.Events.ShouldContain(logEvent =>
            logEvent.MessageTemplate.Text.Contains("HTTP", StringComparison.Ordinal)
            && logEvent.Properties.ContainsKey("CorrelationId")
            && logEvent.Properties["CorrelationId"].ToString().Contains("corr-in-log", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_oversized_caller_supplied_correlation_id_does_not_break_the_request()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", new string('x', 4000));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("X-Correlation-Id").ShouldBeTrue();
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }

    [Fact]
    public async Task The_OpenAPI_document_is_served_anonymously()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("\"openapi\"");
    }
}
```

`ObservabilityOptionsTests` proves the OpenTelemetry and Sentry sections bind from the checked-in `appsettings.json` (the "exporter options bind without error" criterion) and that an enabled exporter without an endpoint produces a startup warning rather than a crash:

```csharp
using Microsoft.Extensions.Configuration;
using SyntaxCircus.Observability;

namespace TechStrap.Api.Tests;

/// <summary>
/// The Observability package binds the OpenTelemetry and Sentry sections eagerly in Program.cs, so a
/// factory cannot override them. These tests bind the same sections the host binds, from the Api's
/// checked-in appsettings.json plus explicit values, and prove they bind without error.
/// </summary>
public sealed class ObservabilityOptionsTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] overrides)
    {
        var apiDirectory = Path.Combine(ProjectRoot(), "src", "TechStrap.Api");
        return new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiDirectory, "appsettings.json"), optional: false)
            .AddInMemoryCollection(overrides.ToDictionary(o => o.Key, o => o.Value))
            .Build();
    }

    private static string ProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("TechStrap.slnx not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void The_checked_in_defaults_bind_and_leave_export_and_sentry_disabled()
    {
        var options = SyntaxCircusObservabilityOptions.FromConfiguration(Configuration());

        options.OpenTelemetry.Enabled.ShouldBeFalse();
        options.OpenTelemetry.IsEnabled.ShouldBeFalse();
        options.OpenTelemetry.OtlpProtocol.ShouldBe("grpc");
        options.Sentry.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void An_enabled_exporter_with_a_valid_endpoint_binds_and_turns_on()
    {
        var options = SyntaxCircusObservabilityOptions.FromConfiguration(Configuration(
            ("OpenTelemetry:Enabled", "true"),
            ("OpenTelemetry:OtlpEndpoint", "http://localhost:4317"),
            ("Sentry:Dsn", "https://key@example.invalid/1")));

        options.OpenTelemetry.IsEnabled.ShouldBeTrue();
        options.OpenTelemetry.StartupWarning.ShouldBeNull();
        options.Sentry.IsEnabled.ShouldBeTrue();
    }

    [Fact]
    public void An_enabled_exporter_without_an_endpoint_reports_a_startup_warning_instead_of_failing()
    {
        var options = SyntaxCircusObservabilityOptions.FromConfiguration(Configuration(
            ("OpenTelemetry:Enabled", "true"),
            ("OpenTelemetry:OtlpEndpoint", "")));

        options.OpenTelemetry.IsEnabled.ShouldBeFalse();
        options.OpenTelemetry.StartupWarning.ShouldNotBeNull();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test --project tests/TechStrap.Api.Tests
```

Expected: FAIL to build: `CS0234`/`CS0246` for `Serilog`, `SyntaxCircus.Observability` and related types, because the Api project does not reference those packages yet.

- [ ] **Step 3: Add the Api packages**

Replace `src/TechStrap.Api/TechStrap.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="../TechStrap.Application/TechStrap.Application.csproj" />
    <ProjectReference Include="../TechStrap.Infrastructure/TechStrap.Infrastructure.csproj" />
    <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="GitVersion.MsBuild" PrivateAssets="all" />
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
    <!-- Required in the startup project for the dotnet ef tool; never flows to consumers. -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
    <PackageReference Include="SyntaxCircus.AspNetCore.Common" />
    <PackageReference Include="SyntaxCircus.AspNetCore.Serilog" />
    <PackageReference Include="SyntaxCircus.DotEnv" />
    <PackageReference Include="SyntaxCircus.EntityFrameworkCore.Postgres" />
    <PackageReference Include="SyntaxCircus.Observability" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Write `appsettings.json`**

Observability and Sentry stay disabled by default; the OTLP headers and the Sentry DSN come from deployment secrets only. Serilog writes to the console:

```json
{
  "Sentry": {
    "Dsn": "",
    "Environment": "",
    "Debug": false,
    "TracesSampleRate": 0.0
  },
  "OpenTelemetry": {
    "Enabled": false,
    "ExportLogs": true,
    "ExportTraces": true,
    "ExportMetrics": true,
    "OtlpEndpoint": "",
    "OtlpProtocol": "grpc",
    "Headers": "",
    "TracesSampleRate": 0.05,
    "ServiceName": "",
    "ServiceVersion": "",
    "Environment": ""
  },
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console" ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft.AspNetCore": "Warning" }
    },
    "WriteTo": [ { "Name": "Console" } ]
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 5: Write `Program.cs`**

Order matters: correlation first so every later log line carries the id, request logging inside the correlation scope (its `CorrelationId` property is enriched from `CorrelationContextAccessor`), health and OpenAPI mapped last. The request logger is taken from DI because `AddStandardSerilog` preserves the static logger:

```csharp
using Sentry;
using Serilog;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Infrastructure.Persistence;

const string ServiceName = "techstrap-api";

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
{
    builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
}

var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
builder.AddStandardSerilog(configureEnrichment: telemetry.ConfigureSerilog);
if (telemetry.Options.Sentry.IsEnabled)
{
    builder.WebHost.UseSentry(options =>
    {
        telemetry.ConfigureSentry(options, context =>
            context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase) ? 0d : null);
        options.AutoSessionTracking = false;
    });
}

builder.Services.AddCorrelationId();
builder.Services.AddSecurityHeaders(builder.Configuration);
builder.Services.AddProblemDetailsExceptionHandling();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddTechStrapPersistence();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

app.UseCorrelationId();
app.UseSecurityHeaders();
app.UseProblemDetailsExceptionHandling();
app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.EnrichDiagnosticContext = (diagnosticContext, _) =>
        diagnosticContext.Set("CorrelationId", CorrelationContextAccessor.CurrentCorrelationId);
});

app.MapStandardHealthChecks();
app.MapOpenApi();
app.MapControllers();

app.Run();

namespace TechStrap.Api
{
    public partial class Program;
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Api.Tests
```

Expected: `Test run summary: Passed!` with `total: 11` (8 health and 3 observability tests).

- [ ] **Step 7: Check the version stamp and a real log line**

```bash
dotnet msbuild src/TechStrap.Api/TechStrap.Api.csproj -nologo -verbosity:quiet -target:GetVersion -getProperty:GitVersion_SemVer
```

Expected: a SemVer such as `0.1.0-feat-phase-01-foundation.5`. The correlation-id log line is asserted by `The_request_log_line_carries_the_correlation_id`.

- [ ] **Step 8: Commit**

```bash
git add src/TechStrap.Api tests/TechStrap.Api.Tests
git commit -m "feat(api): compose the API host with logging, observability, correlation id, health and OpenAPI" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 7: API migrate-on-startup and `IDevelopmentDataSeeder`

Maps to P01-T10 (the migrator call in API startup only) and P01-T12. Deliverable: `ApiStartupTasks.RunAsync` (migrate under the advisory lock, then seed development data only in Development with `TECHSTRAP_SEED_DEV_DATA=true`), the `IDevelopmentDataSeeder` interface with its no-op implementation, and tests for every environment and flag combination.

**Files:**
- Create: `src/TechStrap.Application/Seeding/IDevelopmentDataSeeder.cs`
- Create: `src/TechStrap.Infrastructure/Seeding/{DevelopmentDataSeeder,SeedingServiceCollectionExtensions}.cs`
- Create: `src/TechStrap.Api/Startup/ApiStartupTasks.cs`
- Modify: `src/TechStrap.Api/Program.cs`
- Create (test): `tests/TechStrap.Api.Tests/{ApiStartupTasksTests,ApiMigrationOnStartupTests}.cs`

**Interfaces:**
- Consumes: `TechStrapDbContext`, `TechStrapDatabase.MigrationLockKey`, `AddTechStrapPersistence()` (Task 4), `TestPostgres` and `ApiFactory` (Task 6).
- Produces:
  - `IDevelopmentDataSeeder.SeedAsync(CancellationToken) : Task` in `TechStrap.Application.Seeding`; `DevelopmentDataSeeder` (no-op, logs once) in `TechStrap.Infrastructure.Seeding`; `IServiceCollection.AddTechStrapDevelopmentSeeding()` registers it scoped.
  - `ApiStartupTasks.MigrateOnStartupKey` (`"Database:MigrateOnStartup"`, default true), `ApiStartupTasks.SeedDevelopmentDataKey` (`"TECHSTRAP_SEED_DEV_DATA"`), `ApiStartupTasks.ShouldSeedDevelopmentData(IHostEnvironment, IConfiguration) : bool`, `ApiStartupTasks.RunAsync(IServiceProvider, IHostEnvironment, IConfiguration, CancellationToken = default) : Task`.
  - The API calls `RunAsync` right after `builder.Build()`. A migration or seeder failure throws and stops the host (fail fast).

- [ ] **Step 1: Write the failing tests**

`ApiStartupTasksTests` calls `RunAsync` with a hand-built service provider, so every environment and flag combination runs through the production code path without starting a host. It covers: a real migration against an empty database, migration switched off (no database needed), the six environment and flag combinations for the seeder, a blank connection string failing fast (Review Focus 3), a failing seeder aborting startup (Review Focus 4) and the seeder running after the migration:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Npgsql;
using TechStrap.Api.Startup;
using TechStrap.Application.Seeding;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Api.Tests;

/// <summary>
/// Calls ApiStartupTasks.RunAsync directly with a hand-built service provider, so each
/// environment/flag combination runs through the production code path without starting a host.
/// </summary>
public sealed class ApiStartupTasksTests(TestPostgres postgres)
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2;Pooling=false";

    private static IHostEnvironment Environment(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    private static ServiceProvider Services(IConfiguration configuration, IDevelopmentDataSeeder seeder)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddTechStrapPersistence();
        services.AddScoped(_ => seeder);
        return services.BuildServiceProvider();
    }

    private static async Task<int> CountAppliedMigrationsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM \"__EFMigrationsHistory\"";
        return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Startup_migrates_an_empty_database()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var configuration = Configuration(("ConnectionStrings:TechStrap", connectionString));
        await using var services = Services(configuration, Substitute.For<IDevelopmentDataSeeder>());

        await ApiStartupTasks.RunAsync(services, Environment("Production"), configuration, TestContext.Current.CancellationToken);

        (await CountAppliedMigrationsAsync(connectionString)).ShouldBe(1);
    }

    [Fact]
    public async Task Migration_can_be_switched_off()
    {
        var configuration = Configuration(
            ("ConnectionStrings:TechStrap", UnreachableDatabase),
            (ApiStartupTasks.MigrateOnStartupKey, "false"));
        await using var services = Services(configuration, Substitute.For<IDevelopmentDataSeeder>());

        await Should.NotThrowAsync(() =>
            ApiStartupTasks.RunAsync(services, Environment("Production"), configuration, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("Development", "true", 1)]
    [InlineData("Development", "false", 0)]
    [InlineData("Development", null, 0)]
    [InlineData("Production", "true", 0)]
    [InlineData("Production", "false", 0)]
    [InlineData("Production", null, 0)]
    public async Task Seeder_runs_only_in_Development_with_the_flag_set(string environmentName, string? flag, int expectedCalls)
    {
        var seeder = Substitute.For<IDevelopmentDataSeeder>();
        var configuration = Configuration(
            ("ConnectionStrings:TechStrap", UnreachableDatabase),
            (ApiStartupTasks.MigrateOnStartupKey, "false"),
            (ApiStartupTasks.SeedDevelopmentDataKey, flag));
        await using var services = Services(configuration, seeder);

        await ApiStartupTasks.RunAsync(services, Environment(environmentName), configuration, TestContext.Current.CancellationToken);

        await seeder.Received(expectedCalls).SeedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Startup_with_migration_enabled_and_a_blank_connection_string_fails_fast()
    {
        var configuration = Configuration(("ConnectionStrings:TechStrap", ""));
        await using var services = Services(configuration, Substitute.For<IDevelopmentDataSeeder>());

        var attempt = ApiStartupTasks.RunAsync(services, Environment("Production"), configuration, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<Exception>(async () => await attempt.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_failing_seeder_aborts_startup_instead_of_being_swallowed()
    {
        var seeder = Substitute.For<IDevelopmentDataSeeder>();
        seeder.SeedAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("seed failed")));
        var configuration = Configuration(
            (ApiStartupTasks.MigrateOnStartupKey, "false"),
            (ApiStartupTasks.SeedDevelopmentDataKey, "true"));
        await using var services = Services(configuration, seeder);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            ApiStartupTasks.RunAsync(services, Environment("Development"), configuration, TestContext.Current.CancellationToken));

        exception.Message.ShouldBe("seed failed");
    }

    [Fact]
    public async Task Seeder_runs_after_the_migration_has_been_applied()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var configuration = Configuration(
            ("ConnectionStrings:TechStrap", connectionString),
            (ApiStartupTasks.SeedDevelopmentDataKey, "true"));
        var migrationsSeenBySeeder = -1;
        var seeder = Substitute.For<IDevelopmentDataSeeder>();
        seeder.SeedAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
            migrationsSeenBySeeder = await CountAppliedMigrationsAsync(connectionString));
        await using var services = Services(configuration, seeder);

        await ApiStartupTasks.RunAsync(services, Environment("Development"), configuration, TestContext.Current.CancellationToken);

        migrationsSeenBySeeder.ShouldBe(1);
    }
}
```

`ApiMigrationOnStartupTests` starts the real host against an empty database and checks the history table holds exactly the `Initial` migration:

```csharp
using System.Net;
using Npgsql;

namespace TechStrap.Api.Tests;

public sealed class ApiMigrationOnStartupTests(TestPostgres postgres)
{
    [Fact]
    public async Task Starting_the_api_migrates_an_empty_database_to_the_Initial_migration()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?>
        {
            ["ConnectionStrings:TechStrap"] = connectionString,
            ["Database:MigrateOnStartup"] = "true",
        });

        using var client = factory.CreateClient();

        var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
        ready.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"";
        var applied = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            applied.Add(reader.GetString(0));
        }

        applied.Count.ShouldBe(1);
        applied[0].ShouldEndWith("_Initial");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test --project tests/TechStrap.Api.Tests
```

Expected: FAIL to build: `CS0234`/`CS0246` for `TechStrap.Api.Startup`, `ApiStartupTasks`, and `TechStrap.Application.Seeding` / `IDevelopmentDataSeeder`.

- [ ] **Step 3: Write the seeder abstraction and implementation**

The interface lives in Application. It is a host startup step, not a use case, so it is deliberately not named `...Handler` and is exempt from the handler rules:

```csharp
namespace TechStrap.Application.Seeding;

/// <summary>
/// Dev-only startup hook (02-ARCHITECTURE.md section 7.6). The API calls it after migration, only in
/// Development with TECHSTRAP_SEED_DEV_DATA=true. It is a host startup step, not a use-case entry
/// point, so it is exempt from the handler rule and deliberately not named "...Handler".
/// </summary>
public interface IDevelopmentDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}
```

The no-op implementation and its registration live in Infrastructure:

```csharp
using Microsoft.Extensions.Logging;
using TechStrap.Application.Seeding;

namespace TechStrap.Infrastructure.Seeding;

/// <summary>No-op in PHASE-01. Later phases add sample products, agents and tickets here.</summary>
public sealed class DevelopmentDataSeeder(ILogger<DevelopmentDataSeeder> logger) : IDevelopmentDataSeeder
{
    public Task SeedAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Development data seeding requested; no development data is defined yet.");
        return Task.CompletedTask;
    }
}
```

```csharp
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Seeding;

namespace TechStrap.Infrastructure.Seeding;

public static class SeedingServiceCollectionExtensions
{
    /// <summary>Registers the development data seeder. Called by the Api composition root only.</summary>
    public static IServiceCollection AddTechStrapDevelopmentSeeding(this IServiceCollection services) =>
        services.AddScoped<IDevelopmentDataSeeder, DevelopmentDataSeeder>();
}
```

- [ ] **Step 4: Write the startup tasks**

`MigrateWithAdvisoryLockAsync` is the SyntaxCircus extension (verified): it takes `pg_advisory_lock(key)`, runs `Database.MigrateAsync`, then releases the lock. Settings are read from the `IConfiguration` passed in, so a host built by `WebApplicationFactory` sees test overrides when this runs after `builder.Build()`:

```csharp
using SyntaxCircus.EntityFrameworkCore.Postgres;
using TechStrap.Application.Seeding;
using TechStrap.Infrastructure.Persistence;

namespace TechStrap.Api.Startup;

/// <summary>
/// Host startup steps that run once, before the API accepts traffic: migrate the database under a
/// Postgres advisory lock, then (Development only) seed sample data. These are exempt from the
/// handler rule (02-ARCHITECTURE.md section 7.6). Only the API runs them; the Worker, Admin and
/// Portal never migrate.
/// </summary>
public static class ApiStartupTasks
{
    /// <summary>Set to false to skip migration (tests, or a database managed elsewhere).</summary>
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    /// <summary>Set to true, in Development only, to run <see cref="IDevelopmentDataSeeder"/>.</summary>
    public const string SeedDevelopmentDataKey = "TECHSTRAP_SEED_DEV_DATA";

    public static bool ShouldSeedDevelopmentData(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>(SeedDevelopmentDataKey);

    public static async Task RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("TechStrap.Api.Startup");
        await using var scope = services.CreateAsyncScope();

        if (configuration.GetValue(MigrateOnStartupKey, true))
        {
            logger.LogInformation("Applying database migrations.");
            var database = scope.ServiceProvider.GetRequiredService<TechStrapDbContext>();
            await database.MigrateWithAdvisoryLockAsync(TechStrapDatabase.MigrationLockKey, cancellationToken);
        }
        else
        {
            logger.LogWarning("Database migration on startup is disabled ({Key}=false).", MigrateOnStartupKey);
        }

        if (ShouldSeedDevelopmentData(environment, configuration))
        {
            logger.LogInformation("Seeding development data.");
            var seeder = scope.ServiceProvider.GetRequiredService<IDevelopmentDataSeeder>();
            await seeder.SeedAsync(cancellationToken);
        }
    }
}
```

- [ ] **Step 5: Wire it into `Program.cs`**

Replace `src/TechStrap.Api/Program.cs`. New lines: the `Startup` and `Seeding` usings, `AddTechStrapDevelopmentSeeding()`, and the `await ApiStartupTasks.RunAsync(...)` call after `Build()`:

```csharp
using Sentry;
using Serilog;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Api.Startup;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Seeding;

const string ServiceName = "techstrap-api";

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
{
    builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
}

var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
builder.AddStandardSerilog(configureEnrichment: telemetry.ConfigureSerilog);
if (telemetry.Options.Sentry.IsEnabled)
{
    builder.WebHost.UseSentry(options =>
    {
        telemetry.ConfigureSentry(options, context =>
            context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase) ? 0d : null);
        options.AutoSessionTracking = false;
    });
}

builder.Services.AddCorrelationId();
builder.Services.AddSecurityHeaders(builder.Configuration);
builder.Services.AddProblemDetailsExceptionHandling();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddTechStrapPersistence();
builder.Services.AddTechStrapDevelopmentSeeding();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

await ApiStartupTasks.RunAsync(app.Services, app.Environment, app.Configuration);

app.UseCorrelationId();
app.UseSecurityHeaders();
app.UseProblemDetailsExceptionHandling();
app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.EnrichDiagnosticContext = (diagnosticContext, _) =>
        diagnosticContext.Set("CorrelationId", CorrelationContextAccessor.CurrentCorrelationId);
});

app.MapStandardHealthChecks();
app.MapOpenApi();
app.MapControllers();

app.Run();

namespace TechStrap.Api
{
    public partial class Program;
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Api.Tests
```

Expected: `Test run summary: Passed!` with `total: 23` (11 from Task 6 plus 11 startup-task tests and 1 host migration test).

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Application src/TechStrap.Infrastructure src/TechStrap.Api tests/TechStrap.Api.Tests
git commit -m "feat(api): migrate on startup under an advisory lock and add the development data seeder hook" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 8: Forwarded headers, public rate limit and default-deny

Maps to P01-T13. Deliverable: the client-IP and public-endpoint hardening from `CLIENT_IP_RATE_LIMITING.md`: trusted-proxy forwarded headers that fail Production startup when unconfigured, a validated `RateLimiting:Public` policy, a per-IP 429, and a default-deny fallback policy with `AllowAnonymous` on health and OpenAPI only.

**Files:**
- Create: `src/TechStrap.Api/Options/PublicRateLimitOptions.cs`, `src/TechStrap.Api/Security/UnauthenticatedScheme.cs`, `src/TechStrap.Api/appsettings.Development.json`
- Modify: `src/TechStrap.Api/Program.cs`
- Create (test): `tests/TechStrap.Api.Tests/{PublicApiHardeningTests,PublicRateLimitOptionsTests,TrustedProxyStartupTests,SetRemoteIpAddressStartupFilter}.cs`

**Interfaces:**
- Consumes: `ApiFactory(environment, settings, configureServices)` (Task 6) and these `SyntaxCircus.AspNetCore.Common` 0.1.15 APIs, verified against the source tagged `v0.1.15`:
  - `IServiceCollection.AddTrustedProxyForwardedHeaders(IConfiguration)` binds `TrustedProxy` (`TrustedProxies`, `TrustedNetworks`, `RequireTrustedProxiesInProduction`) eagerly, configures `ForwardedHeadersOptions` and registers an `IStartupFilter` that throws `InvalidOperationException` ("TrustedProxy has no TrustedProxies or TrustedNetworks configured ...") outside Development when none are configured. `UseForwardedHeaders()` (framework) applies it.
  - `RateLimiterOptions.AddPerIpFixedWindow(string policyName, int permitLimit, TimeSpan window)` and `RateLimiterOptions.UseProblemDetailsRejection()`.
  - `AddForwardedClientIp()` is the sending side for typed HTTP clients; no outbound client exists in PHASE-01, so it is first used in PHASE-07 and PHASE-09.
- Produces: `PublicRateLimitOptions` (`SectionName` `"RateLimiting:Public"`, `PolicyName` `"public"`, `PermitLimit` default 120, `WindowSeconds` default 60); the `public` rate-limit policy attached to `/openapi/v1.json`; `UnauthenticatedScheme.Name`/`UnauthenticatedSchemeHandler` (placeholder scheme, 401 on challenge, 403 on forbid, never authenticates anyone); `SetRemoteIpAddressStartupFilter(IPAddress)` for tests. The checked-in Development default `TrustedProxy:TrustedNetworks` is `192.0.2.0/24` (TEST-NET-1, never a real client) in `appsettings.Development.json`, so Production has no default and must be configured.

- [ ] **Step 1: Write the failing tests**

`SetRemoteIpAddressStartupFilter` stands in for the connection's peer address (TestServer has none) and runs before everything `Program.cs` adds, including `UseForwardedHeaders`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace TechStrap.Api.Tests;

/// <summary>
/// TestServer has no real peer, so RemoteIpAddress is null. This filter runs before anything Program.cs
/// adds (including UseForwardedHeaders), so it can stand in for the connection's peer address.
/// </summary>
public sealed class SetRemoteIpAddressStartupFilter(IPAddress remoteIp) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = remoteIp;
                return nextMiddleware();
            });
            next(app);
        };
}
```

`PublicApiHardeningTests` proves the per-IP limit (429 past the limit, a different forwarded visitor unaffected, a spoofed `X-Forwarded-For` from an untrusted peer ignored), that malformed `X-Forwarded-For` values never cause a server error (Review Focus 1), that health is anonymous and never limited, that an unknown route answers 401 (Review Focus 5) and that the fallback policy denies anonymous callers. It uses the checked-in default network and the startup filter because `TrustedProxy` is bound eagerly and ignores factory overrides:

```csharp
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Api.Tests;

/// <summary>
/// Forwarded-header and per-client-IP rate-limit behavior, per _template CLIENT_IP_RATE_LIMITING.md.
/// These use the checked-in Development default TrustedProxy network (192.0.2.0/24) and an
/// IStartupFilter for the peer address, because TrustedProxy is bound eagerly and cannot be
/// overridden through the factory's configuration.
/// </summary>
public sealed class PublicApiHardeningTests
{
    private const string PublicEndpoint = "/openapi/v1.json";
    private static readonly IPAddress TrustedPeer = IPAddress.Parse("192.0.2.5");
    private static readonly IPAddress UntrustedPeer = IPAddress.Parse("198.51.100.9");

    private static ApiFactory Factory(IPAddress peer, int permitLimit = 1) =>
        new(
            settings: new Dictionary<string, string?>
            {
                ["RateLimiting:Public:PermitLimit"] = permitLimit.ToString(),
                ["RateLimiting:Public:WindowSeconds"] = "60",
            },
            configureServices: services => services.AddSingleton<IStartupFilter>(new SetRemoteIpAddressStartupFilter(peer)));

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, PublicEndpoint);
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_visitor_past_the_limit_gets_429()
    {
        await using var factory = Factory(TrustedPeer, permitLimit: 2);
        using var client = factory.CreateClient();

        var first = await GetAsync(client, "203.0.113.10");
        var second = await GetAsync(client, "203.0.113.10");
        var third = await GetAsync(client, "203.0.113.10");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        third.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_each_forwarded_visitor_has_their_own_limit()
    {
        await using var factory = Factory(TrustedPeer);
        using var client = factory.CreateClient();

        var visitorOneFirst = await GetAsync(client, "203.0.113.10");
        var visitorOneSecond = await GetAsync(client, "203.0.113.10");
        var visitorTwo = await GetAsync(client, "203.0.113.20");

        visitorOneFirst.StatusCode.ShouldBe(HttpStatusCode.OK);
        visitorOneSecond.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        visitorTwo.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_spoofed_forwarded_for_from_an_untrusted_peer_is_ignored()
    {
        await using var factory = Factory(UntrustedPeer);
        using var client = factory.CreateClient();

        var first = await GetAsync(client, "203.0.113.10");
        var second = await GetAsync(client, "203.0.113.20");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("not-an-ip")]
    [InlineData("")]
    [InlineData("203.0.113.10, , garbage, 2001:db8::1")]
    public async Task A_malformed_forwarded_for_from_a_trusted_proxy_never_causes_a_server_error(string forwardedFor)
    {
        await using var factory = Factory(TrustedPeer, permitLimit: 5);
        using var client = factory.CreateClient();

        var response = await GetAsync(client, forwardedFor);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_endpoints_are_anonymous_and_not_rate_limited()
    {
        await using var factory = Factory(TrustedPeer);
        using var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task An_unknown_route_is_denied_with_401_not_a_server_error()
    {
        // Default-deny covers every request without an AllowAnonymous endpoint, unknown routes included.
        // Without an authentication scheme to challenge with, this used to surface as a 500.
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/no/such/route", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_fallback_authorization_policy_denies_anonymous_callers()
    {
        await using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();

        fallback.ShouldNotBeNull();
        fallback.Requirements.ShouldContain(requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }
}
```

`PublicRateLimitOptionsTests` proves a bad `PermitLimit` or `WindowSeconds` fails the boot with an `OptionsValidationException` naming the key:

```csharp
namespace TechStrap.Api.Tests;

public sealed class PublicRateLimitOptionsTests
{
    [Theory]
    [InlineData("RateLimiting:Public:PermitLimit", "0")]
    [InlineData("RateLimiting:Public:PermitLimit", "-5")]
    [InlineData("RateLimiting:Public:WindowSeconds", "0")]
    [InlineData("RateLimiting:Public:WindowSeconds", "-1")]
    public async Task A_bad_limit_fails_the_boot(string key, string value)
    {
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { [key] = value });

        var exception = Record.Exception(() => factory.CreateClient());

        exception.ShouldNotBeNull();
        exception.ToString().ShouldContain("OptionsValidationException");
        exception.ToString().ShouldContain(key);
    }

    [Fact]
    public async Task The_defaults_boot_cleanly()
    {
        await using var factory = new ApiFactory();

        Should.NotThrow(() => factory.CreateClient().Dispose());
    }
}
```

`TrustedProxyStartupTests` proves Production without trusted-proxy configuration fails startup and starts when configured. The positive case sets the `TrustedProxy__TrustedNetworks__0` environment variable (the only way to influence the eager binding), so the class runs in a collection with parallelization disabled:

```csharp
namespace TechStrap.Api.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TrustedProxyEnvironmentCollection
{
    public const string Name = "TrustedProxy environment variables";
}

/// <summary>
/// Production must fail to start without trusted-proxy configuration, and start with it. TrustedProxy
/// binds eagerly, so the positive case sets a process environment variable; these tests therefore
/// run alone (DisableParallelization) so the variable cannot leak into other hosts.
/// </summary>
[Collection(TrustedProxyEnvironmentCollection.Name)]
public sealed class TrustedProxyStartupTests
{
    private const string TrustedNetworkVariable = "TrustedProxy__TrustedNetworks__0";

    [Fact]
    public async Task Production_without_trusted_proxy_configuration_fails_startup()
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        await using var factory = new ApiFactory(environment: "Production");

        var exception = Record.Exception(() => factory.CreateClient());

        exception.ShouldNotBeNull();
        exception.ToString().ShouldContain("TrustedProxy");
    }

    [Fact]
    public async Task Production_with_a_trusted_network_starts()
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, "10.20.30.0/24");
        try
        {
            await using var factory = new ApiFactory(environment: "Production");
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

            response.IsSuccessStatusCode.ShouldBeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        }
    }

    [Fact]
    public async Task Development_uses_the_checked_in_default_network_and_starts()
    {
        Environment.SetEnvironmentVariable(TrustedNetworkVariable, null);
        await using var factory = new ApiFactory(environment: "Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test --project tests/TechStrap.Api.Tests
```

Expected: FAIL. The tests compile, but the rate-limit, forwarded-header, default-deny and Production-startup tests fail: nothing limits `/openapi/v1.json`, Production starts without trusted-proxy configuration, and a bad `PermitLimit` is accepted.

- [ ] **Step 3: Write the options, the placeholder scheme and the Development default**

```csharp
namespace TechStrap.Api.Options;

/// <summary>
/// Limits for anonymous public endpoints, bound from RateLimiting:Public and validated at startup
/// (a bad value fails the boot instead of 500ing on the first request).
/// </summary>
public sealed class PublicRateLimitOptions
{
    public const string SectionName = "RateLimiting:Public";

    /// <summary>Name of the rate-limit policy that public endpoints attach with RequireRateLimiting.</summary>
    public const string PolicyName = "public";

    public int PermitLimit { get; set; } = 120;

    public int WindowSeconds { get; set; } = 60;
}
```

Without any authentication scheme, the default-deny policy cannot challenge: an unknown route threw `No authenticationScheme was specified` and the package's exception mapper turned it into a 409. This placeholder scheme only supplies the 401 until PHASE-04 replaces it with JWT bearer:

```csharp
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TechStrap.Api.Security;

/// <summary>
/// Placeholder authentication scheme for PHASE-01. The default-deny fallback policy needs a scheme to
/// challenge with; without one, a request that needs authentication throws instead of returning 401.
/// It never authenticates anyone. PHASE-04 replaces it with OIDC JWT bearer authentication.
/// </summary>
public static class UnauthenticatedScheme
{
    public const string Name = "Unauthenticated";
}

public sealed class UnauthenticatedSchemeHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
```

```json
{
  "TrustedProxy": {
    "TrustedProxies": [],
    "TrustedNetworks": [ "192.0.2.0/24" ],
    "RequireTrustedProxiesInProduction": true
  }
}
```

- [ ] **Step 4: Wire it into `Program.cs`**

Replace `src/TechStrap.Api/Program.cs`. The rate-limiter registration is the template's code verbatim; `ValidateOnStart` makes a bad value fail the boot. Health is mapped through a group so `AllowAnonymous` applies to both endpoints:

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Sentry;
using Serilog;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Infrastructure.Persistence;
using TechStrap.Infrastructure.Seeding;

const string ServiceName = "techstrap-api";

var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
{
    builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
}

var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
builder.AddStandardSerilog(configureEnrichment: telemetry.ConfigureSerilog);
if (telemetry.Options.Sentry.IsEnabled)
{
    builder.WebHost.UseSentry(options =>
    {
        telemetry.ConfigureSentry(options, context =>
            context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase) ? 0d : null);
        options.AutoSessionTracking = false;
    });
}

builder.Services.AddCorrelationId();
builder.Services.AddSecurityHeaders(builder.Configuration);
builder.Services.AddProblemDetailsExceptionHandling();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddTechStrapPersistence();
builder.Services.AddTechStrapDevelopmentSeeding();

// Forwarded headers: trust X-Forwarded-* only from the configured proxies and networks, and fail
// startup outside Development when none are configured (CLIENT_IP_RATE_LIMITING.md).
builder.Services.AddTrustedProxyForwardedHeaders(builder.Configuration);

builder.Services.AddOptions<PublicRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(PublicRateLimitOptions.SectionName))
    .Validate(o => o.PermitLimit >= 1, "RateLimiting:Public:PermitLimit must be >= 1.")
    .Validate(o => o.WindowSeconds >= 1, "RateLimiting:Public:WindowSeconds must be >= 1.")
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    var limits = builder.Configuration.GetSection(PublicRateLimitOptions.SectionName).Get<PublicRateLimitOptions>()
        ?? new PublicRateLimitOptions();
    options.AddPerIpFixedWindow(PublicRateLimitOptions.PolicyName, limits.PermitLimit, TimeSpan.FromSeconds(limits.WindowSeconds));
    options.UseProblemDetailsRejection();
});

// Default-deny: every request needs an authenticated user unless its endpoint opts out with AllowAnonymous.
// The placeholder scheme only supplies a 401 challenge until PHASE-04 adds JWT bearer authentication.
builder.Services.AddAuthentication(UnauthenticatedScheme.Name)
    .AddScheme<AuthenticationSchemeOptions, UnauthenticatedSchemeHandler>(UnauthenticatedScheme.Name, _ => { });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

await ApiStartupTasks.RunAsync(app.Services, app.Environment, app.Configuration);

app.UseForwardedHeaders();
app.UseCorrelationId();
app.UseSecurityHeaders();
app.UseProblemDetailsExceptionHandling();
app.UseSerilogRequestLogging(options =>
{
    options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
    options.EnrichDiagnosticContext = (diagnosticContext, _) =>
        diagnosticContext.Set("CorrelationId", CorrelationContextAccessor.CurrentCorrelationId);
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Health endpoints are anonymous and not rate limited.
app.MapGroup(string.Empty).AllowAnonymous().MapStandardHealthChecks();

// The OpenAPI document is the one anonymous public surface in PHASE-01, so it carries the public limit.
app.MapOpenApi().AllowAnonymous().RequireRateLimiting(PublicRateLimitOptions.PolicyName);

app.MapControllers();

app.Run();

namespace TechStrap.Api
{
    public partial class Program;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Api.Tests
```

Expected: `Test run summary: Passed!` with `total: 40` (23 from Task 7 plus 17 new).

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Api tests/TechStrap.Api.Tests
git commit -m "feat(api): add trusted-proxy forwarded headers, a validated public rate limit and default-deny" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 9: Worker, Admin and Portal hosts

Maps to P01-T07 and the "other hosts do not migrate" half of P01-T10. Deliverable: the three remaining hosts wired with the same cross-cutting packages and health endpoints, the Worker with a minimal health endpoint, the Admin and Portal placeholder shells (one static page, brand icons, SCSS compiled at build), and tests proving it.

**Files:**
- Modify: `src/TechStrap.Worker/{TechStrap.Worker.csproj,Program.cs}`, `src/TechStrap.Admin/{TechStrap.Admin.csproj,Program.cs}`, `src/TechStrap.Portal/{TechStrap.Portal.csproj,Program.cs}`
- Create: `src/TechStrap.Worker/appsettings.json`
- Create (Admin and Portal each): `appsettings.json`, `appsettings.Development.json`, `Styles/app.scss`, `Components/{App,Routes,_Imports}.razor`, `Components/Pages/{Home,NotFound}.razor`, `wwwroot/{favicon.ico,favicon-32.png,apple-touch-icon.png,icon-192.png,icon-512.png}`
- Create (test): `tests/TechStrap.Api.Tests/{HostHealthSmokeTests,ShellHostTests,HostMigrationBoundaryTests}.cs`

**Interfaces:**
- Consumes: `HostFactory<TProgram>` and `WorkerFactory`, `AdminFactory`, `PortalFactory`, `TestPostgres` (Task 6), `AddTechStrapPersistence()` (Task 4); `SyntaxCircus.AspNetCore.Common` 0.1.15 `MapRazorComponentsWithStaticAssets<TRootComponent>()` (maps static web assets, then `MapRazorComponents<T>()`; verified in `BlazorStaticAssetEndpointExtensions.cs`), `SyntaxCircus.Blazor.Components` 0.1.3 (referenced; its components are used from PHASE-07 and PHASE-09); `AspNetCore.SassCompiler` 1.105.1 (compiles `Styles/app.scss` to `wwwroot/css/app.css` through the `Directory.Build.targets` target from Task 1).
- Produces: `/health/live` and `/health/ready` on every host (Admin and Portal have no readiness checks, so `/health/ready` is 200 for them); Worker `/health/ready` checks Postgres; Admin and Portal serve `GET /` (heading `TechStrap Admin` / `TechStrap Portal`), the icons and `/css/app.css`; Admin and Portal persist data-protection keys to `DataProtection:KeyRingPath` when set. The brand icons come from `assets/brand/`, committed by the coordinator before this task.

- [ ] **Step 1: Confirm the brand assets are on the branch**

```bash
git ls-files assets/brand
```

Expected: the list includes `assets/brand/favicon.ico`, `favicon-32.png`, `apple-touch-icon.png`, `icon-192.png` and `icon-512.png`. If they are missing, stop and ask the coordinator; do not create or substitute them.

- [ ] **Step 2: Write the failing tests**

`HostHealthSmokeTests` covers live on all four hosts and readiness: the Worker ready with Postgres and 503 without it, Admin and Portal ready 200:

```csharp
using System.Net;

namespace TechStrap.Api.Tests;

/// <summary>
/// Every host answers /health/live with 200. Api and Worker also gate /health/ready on Postgres;
/// Admin and Portal have no readiness checks yet (they report live only until PHASE-07 and PHASE-09
/// add an API reachability check), so their /health/ready is also 200.
/// The Worker is a WebApplication, so a WebApplicationFactory covers it; the compose healthcheck
/// (Task 13) covers the real container.
/// </summary>
public sealed class HostHealthSmokeTests(TestPostgres postgres)
{
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2;Pooling=false";

    private static IReadOnlyDictionary<string, string?> ConnectionString(string value) =>
        new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = value };

    [Fact]
    public async Task Api_is_live()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Worker_is_live_and_ready_with_postgres()
    {
        await using var factory = new WorkerFactory(settings: ConnectionString(await postgres.CreateDatabaseAsync()));
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Worker_is_live_but_not_ready_when_the_database_is_unreachable()
    {
        await using var factory = new WorkerFactory(settings: ConnectionString(UnreachableDatabase));
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Admin_reports_live_only()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Portal_reports_live_only()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
```

`ShellHostTests` asserts, for Admin and Portal, the placeholder page and its icon links, `/favicon.ico` answering 200 with `image/x-icon` (or `image/vnd.microsoft.icon`), the other four icons, the CSS compiled from `Styles/app.scss` at build time, and `_framework/blazor.web.js`:

```csharp
using System.Net;

namespace TechStrap.Api.Tests;

/// <summary>
/// The Admin and Portal placeholder shells serve one static page, the brand icons, and the CSS that
/// the build compiles from Styles/app.scss (compiled CSS is never committed).
/// </summary>
public sealed class ShellHostTests
{
    private static readonly string[] IconContentTypes = ["image/x-icon", "image/vnd.microsoft.icon"];

    [Fact]
    public async Task Admin_serves_the_placeholder_page_icons_and_compiled_css()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        await AssertShellAsync(client, "TechStrap Admin");
    }

    [Fact]
    public async Task Portal_serves_the_placeholder_page_icons_and_compiled_css()
    {
        await using var factory = new PortalFactory();
        using var client = factory.CreateClient();

        await AssertShellAsync(client, "TechStrap Portal");
    }

    private static async Task AssertShellAsync(HttpClient client, string expectedHeading)
    {
        var home = await client.GetAsync("/", TestContext.Current.CancellationToken);
        home.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await home.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.ShouldContain(expectedHeading);
        html.ShouldContain("rel=\"icon\" href=\"favicon.ico\" sizes=\"any\"");
        html.ShouldContain("rel=\"apple-touch-icon\" href=\"apple-touch-icon.png\"");

        var favicon = await client.GetAsync("/favicon.ico", TestContext.Current.CancellationToken);
        favicon.StatusCode.ShouldBe(HttpStatusCode.OK);
        IconContentTypes.ShouldContain(favicon.Content.Headers.ContentType?.MediaType);

        foreach (var path in new[] { "/favicon-32.png", "/apple-touch-icon.png", "/icon-192.png", "/icon-512.png" })
        {
            (await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }

        var css = await client.GetAsync("/css/app.css", TestContext.Current.CancellationToken);
        css.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await css.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain(".shell-placeholder");

        var script = await client.GetAsync("/_framework/blazor.web.js", TestContext.Current.CancellationToken);
        script.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
```

`HostMigrationBoundaryTests` proves the Worker leaves an empty database unmigrated even when the API's migration switch is forced on, and that Admin and Portal cannot reach the database layer:

```csharp
using Npgsql;

namespace TechStrap.Api.Tests;

/// <summary>Only the API migrates the database. Worker, Admin and Portal never do.</summary>
public sealed class HostMigrationBoundaryTests(TestPostgres postgres)
{
    [Fact]
    public async Task Starting_the_worker_leaves_the_database_unmigrated()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = new WorkerFactory(settings: new Dictionary<string, string?>
        {
            ["ConnectionStrings:TechStrap"] = connectionString,
            // Even with the API's switch forced on, the Worker must ignore it.
            ["Database:MigrateOnStartup"] = "true",
        });
        using var client = factory.CreateClient();
        await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT to_regclass('\"__EFMigrationsHistory\"')::text";
        var historyTable = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        historyTable.ShouldBeOfType<DBNull>("the Worker must not create the migrations history table");
    }

    [Theory]
    [InlineData(typeof(TechStrap.Admin.Program))]
    [InlineData(typeof(TechStrap.Portal.Program))]
    public void Admin_and_Portal_cannot_reach_the_database_layer(Type hostProgram)
    {
        var referenced = hostProgram.Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        referenced.ShouldNotContain("TechStrap.Infrastructure");
        referenced.ShouldNotContain("TechStrap.Application");
        referenced.ShouldNotContain("Npgsql");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

```bash
dotnet test --project tests/TechStrap.Api.Tests
```

Expected: FAIL. The projects compile (the hosts are empty), but the new tests fail: every Worker, Admin and Portal health request is a 404 and the shell pages do not exist. The earlier 40 tests still pass.

- [ ] **Step 4: Write the Worker**

A `WebApplication` that only serves health. It registers persistence (for the readiness check) but never migrates. `src/TechStrap.Worker/TechStrap.Worker.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <ItemGroup>
    <ProjectReference Include="../TechStrap.Application/TechStrap.Application.csproj" />
    <ProjectReference Include="../TechStrap.Infrastructure/TechStrap.Infrastructure.csproj" />
    <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="GitVersion.MsBuild" PrivateAssets="all" />
    <PackageReference Include="SyntaxCircus.AspNetCore.Common" />
    <PackageReference Include="SyntaxCircus.AspNetCore.Serilog" />
    <PackageReference Include="SyntaxCircus.DotEnv" />
    <PackageReference Include="SyntaxCircus.Observability" />
  </ItemGroup>

</Project>
```

`src/TechStrap.Worker/Program.cs`:

```csharp
using Sentry;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Infrastructure.Persistence;

const string ServiceName = "techstrap-worker";

// The Worker is a web host only so it can expose /health/*. It never migrates the database: the
// API owns migrations. Background loops arrive in PHASE-05.
var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
{
    builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
}

var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
builder.AddStandardSerilog(configureEnrichment: telemetry.ConfigureSerilog);
if (telemetry.Options.Sentry.IsEnabled)
{
    builder.WebHost.UseSentry(options =>
    {
        telemetry.ConfigureSentry(options, context =>
            context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase) ? 0d : null);
        options.AutoSessionTracking = false;
    });
}

builder.Services.AddCorrelationId();
builder.Services.AddTechStrapPersistence();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

app.UseCorrelationId();
app.MapStandardHealthChecks();

app.Run();

namespace TechStrap.Worker
{
    public partial class Program;
}
```

`src/TechStrap.Worker/appsettings.json`:

```json
{
  "Sentry": {
    "Dsn": "",
    "Environment": "",
    "Debug": false,
    "TracesSampleRate": 0.0
  },
  "OpenTelemetry": {
    "Enabled": false,
    "ExportLogs": true,
    "ExportTraces": true,
    "ExportMetrics": true,
    "OtlpEndpoint": "",
    "OtlpProtocol": "grpc",
    "Headers": "",
    "TracesSampleRate": 0.05,
    "ServiceName": "",
    "ServiceVersion": "",
    "Environment": ""
  },
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console" ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft.AspNetCore": "Warning" }
    },
    "WriteTo": [ { "Name": "Console" } ]
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 5: Write the Admin shell**

Blazor Server (`AddInteractiveServerComponents`). `src/TechStrap.Admin/TechStrap.Admin.csproj` (`RequiresAspNetWebAssets` publishes `_framework/blazor.web.js`; there is no `Microsoft.Web.LibraryManager.Build` reference because libman manifests arrive in PHASE-02):

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <!-- Publishes _framework/blazor.web.js and the other framework static assets. -->
    <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../TechStrap.Contracts/TechStrap.Contracts.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="AspNetCore.SassCompiler" />
    <PackageReference Include="GitVersion.MsBuild" PrivateAssets="all" />
    <PackageReference Include="SyntaxCircus.AspNetCore.Common" />
    <PackageReference Include="SyntaxCircus.AspNetCore.Serilog" />
    <PackageReference Include="SyntaxCircus.Blazor.Components" />
    <PackageReference Include="SyntaxCircus.DotEnv" />
    <PackageReference Include="SyntaxCircus.Observability" />
  </ItemGroup>

</Project>
```

`src/TechStrap.Admin/Program.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using Sentry;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Admin.Components;

const string ServiceName = "techstrap-admin";

// Placeholder shell: this host never touches the database and never migrates. It will call the API
// through typed clients over TechStrap.Contracts once its UI phase lands.
var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
{
    builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
}

var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
builder.AddStandardSerilog(configureEnrichment: telemetry.ConfigureSerilog);
if (telemetry.Options.Sentry.IsEnabled)
{
    builder.WebHost.UseSentry(options =>
    {
        telemetry.ConfigureSentry(options, context =>
            context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase)
                || context.TransactionContext.Name.Contains("/_blazor", StringComparison.OrdinalIgnoreCase) ? 0d : null);
        options.AutoSessionTracking = false;
    });
}

builder.Services.AddCorrelationId();
builder.Services.AddHealthChecks();
// Trust X-Forwarded-* only from the reverse proxy. Production fails to start without configuration.
builder.Services.AddTrustedProxyForwardedHeaders(builder.Configuration);
// Antiforgery and circuit state need a stable key ring; containers mount a volume here.
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

app.UseForwardedHeaders();
app.UseCorrelationId();
app.UseAntiforgery();
app.MapStandardHealthChecks();
app.MapRazorComponentsWithStaticAssets<App>()
    .AddInteractiveServerRenderMode();

app.Run();

namespace TechStrap.Admin
{
    public partial class Program;
}
```

`src/TechStrap.Admin/appsettings.json` (the `SassCompiler` section tells the compiler where to read and write):

```json
{
  "Sentry": {
    "Dsn": "",
    "Environment": "",
    "Debug": false,
    "TracesSampleRate": 0.0
  },
  "OpenTelemetry": {
    "Enabled": false,
    "ExportLogs": true,
    "ExportTraces": true,
    "ExportMetrics": true,
    "OtlpEndpoint": "",
    "OtlpProtocol": "grpc",
    "Headers": "",
    "TracesSampleRate": 0.05,
    "ServiceName": "",
    "ServiceVersion": "",
    "Environment": ""
  },
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console" ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft.AspNetCore": "Warning" }
    },
    "WriteTo": [ { "Name": "Console" } ]
  },
  "AllowedHosts": "*",
  "SassCompiler": {
    "SourceFolder": "Styles",
    "TargetFolder": "wwwroot/css",
    "GenerateSourceMap": false
  }
}
```

`src/TechStrap.Admin/appsettings.Development.json` (the checked-in Development proxy default, as in the API):

```json
{
  "TrustedProxy": {
    "TrustedProxies": [],
    "TrustedNetworks": [ "192.0.2.0/24" ],
    "RequireTrustedProxiesInProduction": true
  }
}
```

`src/TechStrap.Admin/Styles/app.scss` is a minimal placeholder that makes `wwwroot/css/app.css` exist for the Dockerfile assertion; PHASE-02 replaces it with the Bootstrap 5 build and brand tokens. The compiled CSS is generated on every build and is gitignored:

```scss
// PHASE-02 replaces this placeholder with the Bootstrap 5 build and the brand tokens.
// The compiled file (wwwroot/css/app.css) is generated on every build and never committed.
body {
  font-family: system-ui, -apple-system, "Segoe UI", sans-serif;
  margin: 0;
}

.shell-placeholder {
  max-width: 40rem;
  margin: 4rem auto;
  padding: 0 1rem;
}
```

`src/TechStrap.Admin/Components/_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using TechStrap.Admin
@using TechStrap.Admin.Components
```

`src/TechStrap.Admin/Components/Routes.razor`:

```razor
<Router AppAssembly="typeof(Program).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        <RouteView RouteData="routeData" />
    </Found>
</Router>
```

`src/TechStrap.Admin/Components/App.razor` carries the brand icon links the owner asked for (`favicon.ico` with `sizes="any"`, the 32 px PNG and the Apple touch icon):

```razor
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <link rel="icon" href="favicon.ico" sizes="any" />
    <link rel="icon" type="image/png" sizes="32x32" href="favicon-32.png" />
    <link rel="apple-touch-icon" href="apple-touch-icon.png" />
    <link rel="stylesheet" href="@Assets["css/app.css"]" />
    <ImportMap />
    <HeadOutlet />
</head>

<body>
    <Routes />
    <script src="@Assets["_framework/blazor.web.js"]"></script>
</body>

</html>
```

`src/TechStrap.Admin/Components/Pages/Home.razor` (one static page, no logic):

```razor
@page "/"

<PageTitle>TechStrap Admin</PageTitle>
<main class="shell-placeholder">
    <h1>TechStrap Admin</h1>
    <p>This host is a placeholder shell. The real interface arrives in a later phase.</p>
</main>
```

`src/TechStrap.Admin/Components/Pages/NotFound.razor`:

```razor
@page "/not-found"

<PageTitle>Not found</PageTitle>
<main class="shell-placeholder">
    <h1>Page not found</h1>
    <p><a href="/">Back to the start</a></p>
</main>
```

- [ ] **Step 6: Write the Portal shell**

Blazor SSR (no interactive components). These files are byte-for-byte the same as the Admin ones, written to the `src/TechStrap.Portal/` paths: `TechStrap.Portal.csproj` (same content as `TechStrap.Admin.csproj`), `appsettings.json`, `appsettings.Development.json`, `Styles/app.scss`, `Components/Routes.razor`, `Components/App.razor` and `Components/Pages/NotFound.razor`. Three Portal files differ. `src/TechStrap.Portal/Program.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using Sentry;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.AspNetCore.Serilog;
using SyntaxCircus.DotEnv;
using SyntaxCircus.Observability;
using TechStrap.Portal.Components;

const string ServiceName = "techstrap-portal";

// Placeholder shell: this host never touches the database and never migrates. It will call the API
// through typed clients over TechStrap.Contracts once its UI phase lands.
var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration.ShouldLoadDotEnv(builder.Environment))
{
    builder.Configuration.AddSyntaxCircusDotEnvFiles(builder.Environment.ContentRootPath);
}

var telemetry = builder.AddSyntaxCircusObservability(ServiceName);
builder.AddStandardSerilog(configureEnrichment: telemetry.ConfigureSerilog);
if (telemetry.Options.Sentry.IsEnabled)
{
    builder.WebHost.UseSentry(options =>
    {
        telemetry.ConfigureSentry(options, context =>
            context.TransactionContext.Name.Contains("/health", StringComparison.OrdinalIgnoreCase)
                || context.TransactionContext.Name.Contains("/_blazor", StringComparison.OrdinalIgnoreCase) ? 0d : null);
        options.AutoSessionTracking = false;
    });
}

builder.Services.AddCorrelationId();
builder.Services.AddHealthChecks();
// Trust X-Forwarded-* only from the reverse proxy. Production fails to start without configuration.
builder.Services.AddTrustedProxyForwardedHeaders(builder.Configuration);
// Antiforgery and circuit state need a stable key ring; containers mount a volume here.
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"];
if (!string.IsNullOrWhiteSpace(keyRingPath))
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
}

builder.Services.AddRazorComponents();

var app = builder.Build();
telemetry.LogStartupWarning(app.Logger);

app.UseForwardedHeaders();
app.UseCorrelationId();
app.UseAntiforgery();
app.MapStandardHealthChecks();
app.MapRazorComponentsWithStaticAssets<App>();

app.Run();

namespace TechStrap.Portal
{
    public partial class Program;
}
```

`src/TechStrap.Portal/Components/_Imports.razor`:

```razor
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using TechStrap.Portal
@using TechStrap.Portal.Components
```

`src/TechStrap.Portal/Components/Pages/Home.razor`:

```razor
@page "/"

<PageTitle>TechStrap Portal</PageTitle>
<main class="shell-placeholder">
    <h1>TechStrap Portal</h1>
    <p>This host is a placeholder shell. The real interface arrives in a later phase.</p>
</main>
```

- [ ] **Step 7: Copy the brand icons into both shells**

```powershell
foreach ($app in 'Admin', 'Portal') {
    New-Item -ItemType Directory -Force "src/TechStrap.$app/wwwroot" | Out-Null
    foreach ($file in 'favicon.ico', 'favicon-32.png', 'apple-touch-icon.png', 'icon-192.png', 'icon-512.png') {
        Copy-Item "assets/brand/$file" "src/TechStrap.$app/wwwroot/$file"
    }
}
```

The same in Git Bash:

```bash
for app in Admin Portal; do
  mkdir -p "src/TechStrap.$app/wwwroot"
  for file in favicon.ico favicon-32.png apple-touch-icon.png icon-192.png icon-512.png; do
    cp "assets/brand/$file" "src/TechStrap.$app/wwwroot/$file"
  done
done
```

- [ ] **Step 8: Run the tests to verify they pass**

```bash
dotnet test --project tests/TechStrap.Api.Tests
```

Expected: `Test run summary: Passed!` with `total: 50` (40 from Task 8 plus 10 new). If the CSS assertion fails, run `dotnet build src/TechStrap.Admin` and check that `src/TechStrap.Admin/wwwroot/css/app.css` was generated.

- [ ] **Step 9: Confirm no compiled CSS is tracked**

```bash
git status --short
git ls-files "*/wwwroot/css/*"
```

Expected: the second command prints nothing, and `git status` does not list `wwwroot/css/app.css` or `app.css.map` (both are gitignored).

- [ ] **Step 10: Commit**

```bash
git add src tests
git commit -m "feat: wire the Worker, Admin and Portal hosts with health endpoints and placeholder shells" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 10: `.env.example` per host and `EnvExampleCompletenessTests`

Maps to P01-T08. Deliverable: one `.env.example` per host documenting every setting, `.env.local` confirmed gitignored, and tests that fail when a bound setting is undocumented.

**Files:**
- Create: `src/TechStrap.Api/.env.example`, `src/TechStrap.Worker/.env.example`, `src/TechStrap.Admin/.env.example`, `src/TechStrap.Portal/.env.example`
- Create (test): `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`

**Interfaces:**
- Consumes: `TrustedProxyOptions` (`SyntaxCircus.AspNetCore.Common`), `OpenTelemetryOptions` and `SentryOptions` (`SyntaxCircus.Observability`), `PublicRateLimitOptions` and `ApiStartupTasks` keys (Tasks 7 and 8). `.gitignore` already ignores `.env.*` and keeps `.env.example` and `.env.*.example`.
- Produces: the documented environment contract per host. The test derives the required keys from the options classes by reflection (every settable public property, list properties as `__0`), adds the TechStrap-specific variables from the spec (connection string, `TECHSTRAP_AGENT_GROUP`, `TECHSTRAP_ADMIN_GROUP`, `TECHSTRAP_BOOTSTRAP_ADMIN`, OIDC authority and audience, `TRUSTEDPROXY__TRUSTEDNETWORKS__n`, rate-limit keys, SMTP, storage path `/app/storage`, `TECHSTRAP_PORTAL_PUBLIC_URL`, `TECHSTRAP_SEED_DEV_DATA`) and accepts a key as documented when it appears as `KEY=value` or as a commented-out `# KEY=value` line. Key names for features that land later (`AUTHENTICATION__JWTBEARER__*`, `AUTH__*`, `EMAIL__SMTP__*`, `STORAGE__*`) follow the usage pages and `dragon-poop`; they are documented now and bound by the owning phase.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Diagnostics;
using System.Text.RegularExpressions;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.Observability;
using ObservabilitySentryOptions = SyntaxCircus.Observability.SentryOptions;
using TechStrap.Api.Options;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests;

/// <summary>
/// Every .env.example must document each setting the host binds from an options class, plus the
/// TechStrap-specific variables the owner listed. A key counts as documented when it appears as
/// KEY=value or as a commented-out "# KEY=value" line.
/// </summary>
public sealed partial class EnvExampleCompletenessTests
{
    private static readonly Regex KeyLine = KeyLinePattern();

    [GeneratedRegex(@"^\s*#?\s*(?<key>[A-Za-z][A-Za-z0-9_]*)=", RegexOptions.Compiled)]
    private static partial Regex KeyLinePattern();

    private static string RepositoryRoot => FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("TechStrap.slnx not found above " + AppContext.BaseDirectory);
    }

    /// <summary>Turns a configuration key (Section:Key) into the environment-variable form (SECTION__KEY).</summary>
    private static string ToEnvName(string configurationKey) => configurationKey.Replace(":", "__", StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>Environment names for every settable property of an options type bound under <paramref name="section"/>.</summary>
    private static IEnumerable<string> OptionKeys(Type optionsType, string section)
    {
        foreach (var property in optionsType.GetProperties().Where(p => p.SetMethod is { IsPublic: true }))
        {
            var key = $"{section}:{property.Name}";
            yield return typeof(IEnumerable<string>).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(string)
                ? ToEnvName(key + ":0")
                : ToEnvName(key);
        }
    }

    private static IEnumerable<string> ObservabilityKeys() =>
        OptionKeys(typeof(OpenTelemetryOptions), OpenTelemetryOptions.SectionName)
            .Concat(OptionKeys(typeof(ObservabilitySentryOptions), ObservabilitySentryOptions.SectionName));

    private static IEnumerable<string> TrustedProxyKeys() => OptionKeys(typeof(TrustedProxyOptions), TrustedProxyOptions.SectionName);

    public static TheoryData<string, string[]> Hosts() => new()
    {
        {
            "TechStrap.Api",
            [
                .. ObservabilityKeys(),
                .. TrustedProxyKeys(),
                .. OptionKeys(typeof(PublicRateLimitOptions), PublicRateLimitOptions.SectionName),
                ToEnvName(ApiStartupTasks.MigrateOnStartupKey),
                ApiStartupTasks.SeedDevelopmentDataKey,
                "CONNECTIONSTRINGS__TECHSTRAP",
                "TECHSTRAP_AGENT_GROUP",
                "TECHSTRAP_ADMIN_GROUP",
                "TECHSTRAP_BOOTSTRAP_ADMIN",
                "AUTHENTICATION__JWTBEARER__AUTHORITY",
                "AUTHENTICATION__JWTBEARER__AUDIENCES__0",
                "TECHSTRAP_PORTAL_PUBLIC_URL",
                "STORAGE__LOCAL__ROOTPATH",
            ]
        },
        {
            "TechStrap.Worker",
            [
                .. ObservabilityKeys(),
                "CONNECTIONSTRINGS__TECHSTRAP",
                "EMAIL__SMTP__HOST",
                "EMAIL__SMTP__PORT",
                "EMAIL__SMTP__USERNAME",
                "EMAIL__SMTP__PASSWORD",
                "EMAIL__SMTP__DEFAULTFROM",
                "STORAGE__LOCAL__ROOTPATH",
                "TECHSTRAP_PORTAL_PUBLIC_URL",
            ]
        },
        {
            "TechStrap.Admin",
            [
                .. ObservabilityKeys(),
                .. TrustedProxyKeys(),
                "API__BASEURL",
                "AUTH__AUTHORITY",
                "AUTH__CLIENTID",
                "AUTH__CLIENTSECRET",
                "TECHSTRAP_ADMIN_GROUP",
                "DATAPROTECTION__KEYRINGPATH",
            ]
        },
        {
            "TechStrap.Portal",
            [
                .. ObservabilityKeys(),
                .. TrustedProxyKeys(),
                "API__BASEURL",
                "TECHSTRAP_PORTAL_PUBLIC_URL",
                "DATAPROTECTION__KEYRINGPATH",
            ]
        },
    };

    private static HashSet<string> DocumentedKeys(string host)
    {
        var path = Path.Combine(RepositoryRoot, "src", host, ".env.example");
        File.Exists(path).ShouldBeTrue($"{path} must exist");

        return File.ReadAllLines(path)
            .Select(line => KeyLine.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Env_example_documents_every_required_setting(string host, string[] requiredKeys)
    {
        var documented = DocumentedKeys(host);

        var missing = requiredKeys.Select(k => k.ToUpperInvariant()).Where(k => !documented.Contains(k)).Order().ToList();

        missing.ShouldBeEmpty($"{host}/.env.example is missing: {string.Join(", ", missing)}");
    }

    [Fact]
    public void The_key_enumerator_produces_the_expected_names()
    {
        // Guards the helper itself: a wrong enumerator would make the theory above pass vacuously.
        TrustedProxyKeys().ShouldContain("TRUSTEDPROXY__TRUSTEDNETWORKS__0");
        TrustedProxyKeys().ShouldContain("TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION");
        OptionKeys(typeof(PublicRateLimitOptions), PublicRateLimitOptions.SectionName)
            .ShouldBe(["RATELIMITING__PUBLIC__PERMITLIMIT", "RATELIMITING__PUBLIC__WINDOWSECONDS"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("TechStrap.Api")]
    [InlineData("TechStrap.Worker")]
    [InlineData("TechStrap.Admin")]
    [InlineData("TechStrap.Portal")]
    public void Local_env_files_are_gitignored_and_examples_are_not(string host)
    {
        GitCheckIgnore($"src/{host}/.env.local").ShouldBe(0, ".env.local must be ignored");
        GitCheckIgnore($"src/{host}/.env.example").ShouldBe(1, ".env.example must be tracked");
    }

    private static int GitCheckIgnore(string relativePath)
    {
        using var process = Process.Start(new ProcessStartInfo("git", ["check-ignore", "-q", relativePath])
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.WaitForExit();
        return process.ExitCode;
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test --project tests/TechStrap.Api.Tests --filter-class "*EnvExampleCompletenessTests"
```

Expected: FAIL. The four `Env_example_documents_every_required_setting` cases fail with `.../.env.example must exist`; the key-enumerator and gitignore tests already pass.

- [ ] **Step 3: Write the four `.env.example` files**

`src/TechStrap.Api/.env.example`:

```dotenv
# Copy to .env.local in this directory (gitignored). SyntaxCircus.DotEnv loads .env then .env.local in Development only.
# In containers, the same names arrive as real environment variables; see docker-compose.yml.

# --- Database ---
ConnectionStrings__TechStrap=Host=localhost;Port=5432;Database=techstrap;Username=techstrap;Password=replace-me
# The API migrates on startup under an advisory lock. Set to false to skip.
DATABASE__MIGRATEONSTARTUP=true
# Development only: run the development data seeder after migration.
TECHSTRAP_SEED_DEV_DATA=false

# --- Agent authentication (OIDC JWT bearer; used from PHASE-04) ---
AUTHENTICATION__JWTBEARER__AUTHORITY=
AUTHENTICATION__JWTBEARER__AUDIENCES__0=
AUTHENTICATION__JWTBEARER__REQUIREHTTPSMETADATA=true
TECHSTRAP_AGENT_GROUP=techstrap-agents
TECHSTRAP_ADMIN_GROUP=techstrap-admins
# Email of the first administrator; promoted on first sign-in.
TECHSTRAP_BOOTSTRAP_ADMIN=

# --- Public rate limit (fixed window per client IP) ---
RATELIMITING__PUBLIC__PERMITLIMIT=120
RATELIMITING__PUBLIC__WINDOWSECONDS=60

# --- Public portal base URL used in customer links (used from PHASE-05) ---
TECHSTRAP_PORTAL_PUBLIC_URL=http://localhost:8082

# --- Attachment storage (local provider; used from PHASE-05) ---
STORAGE__PROVIDER=Local
STORAGE__LOCAL__ROOTPATH=/app/storage

# --- Trusted reverse proxy (API) ---
# Forwarded headers are trusted only from these addresses. Production refuses to start with none.
# Never trust 172.16.0.0/12 or 0.0.0.0/0. The API also trusts the compose subnet 172.16.31.0/24 as a second entry (TRUSTEDPROXY__TRUSTEDNETWORKS__1 in production).
# Optional: also trust one proxy address.
# TRUSTEDPROXY__TRUSTEDPROXIES__0=192.0.2.1
TRUSTEDPROXY__TRUSTEDNETWORKS__0=192.0.2.0/24
TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION=true
# --- Logging and observability (all optional; secrets such as the Sentry DSN and OTLP headers belong in deployment secrets) ---
SERILOG__MINIMUMLEVEL__DEFAULT=Information
ALLOWEDHOSTS=*
SENTRY__DSN=
SENTRY__ENVIRONMENT=
SENTRY__DEBUG=false
SENTRY__TRACESSAMPLERATE=0.0
OPENTELEMETRY__ENABLED=false
OPENTELEMETRY__EXPORTLOGS=true
OPENTELEMETRY__EXPORTTRACES=true
OPENTELEMETRY__EXPORTMETRICS=true
OPENTELEMETRY__OTLPENDPOINT=
OPENTELEMETRY__OTLPPROTOCOL=grpc
OPENTELEMETRY__HEADERS=
OPENTELEMETRY__TRACESSAMPLERATE=0.05
OPENTELEMETRY__SERVICENAME=
OPENTELEMETRY__SERVICEVERSION=
OPENTELEMETRY__ENVIRONMENT=
```

`src/TechStrap.Worker/.env.example`:

```dotenv
# Copy to .env.local in this directory (gitignored). SyntaxCircus.DotEnv loads .env then .env.local in Development only.
# The Worker never migrates the database; the API owns migrations.

# --- Database ---
ConnectionStrings__TechStrap=Host=localhost;Port=5432;Database=techstrap;Username=techstrap;Password=replace-me

# --- Outbound email (SyntaxCircus.Email SMTP; used from PHASE-05) ---
EMAIL__SMTP__HOST=localhost
EMAIL__SMTP__PORT=1025
EMAIL__SMTP__USERNAME=
EMAIL__SMTP__PASSWORD=
EMAIL__SMTP__DEFAULTFROM=support@example.com
EMAIL__SMTP__MAXRETRYATTEMPTS=3

# --- Attachment storage (shared volume with the API; used from PHASE-05) ---
STORAGE__PROVIDER=Local
STORAGE__LOCAL__ROOTPATH=/app/storage

# --- Public portal base URL used in customer links (used from PHASE-05) ---
TECHSTRAP_PORTAL_PUBLIC_URL=http://localhost:8082

# --- Logging and observability (all optional; secrets such as the Sentry DSN and OTLP headers belong in deployment secrets) ---
SERILOG__MINIMUMLEVEL__DEFAULT=Information
ALLOWEDHOSTS=*
SENTRY__DSN=
SENTRY__ENVIRONMENT=
SENTRY__DEBUG=false
SENTRY__TRACESSAMPLERATE=0.0
OPENTELEMETRY__ENABLED=false
OPENTELEMETRY__EXPORTLOGS=true
OPENTELEMETRY__EXPORTTRACES=true
OPENTELEMETRY__EXPORTMETRICS=true
OPENTELEMETRY__OTLPENDPOINT=
OPENTELEMETRY__OTLPPROTOCOL=grpc
OPENTELEMETRY__HEADERS=
OPENTELEMETRY__TRACESSAMPLERATE=0.05
OPENTELEMETRY__SERVICENAME=
OPENTELEMETRY__SERVICEVERSION=
OPENTELEMETRY__ENVIRONMENT=
```

`src/TechStrap.Admin/.env.example`:

```dotenv
# Copy to .env.local in this directory (gitignored). SyntaxCircus.DotEnv loads .env then .env.local in Development only.
# The Admin app never touches the database; it calls the API.

# --- API ---
API__BASEURL=http://localhost:8080/

# --- Agent sign-in (OIDC code flow, any provider; used from PHASE-07) ---
AUTH__AUTHORITY=
AUTH__CLIENTID=
AUTH__CLIENTSECRET=
TECHSTRAP_ADMIN_GROUP=techstrap-admins

# --- ASP.NET Core data protection key ring (persisted volume in containers) ---
DATAPROTECTION__KEYRINGPATH=

# --- Trusted reverse proxy (Admin) ---
# Forwarded headers are trusted only from these addresses. Production refuses to start with none.
# Never trust 172.16.0.0/12 or 0.0.0.0/0. Admin trusts only the reverse proxy.
# Optional: also trust one proxy address.
# TRUSTEDPROXY__TRUSTEDPROXIES__0=192.0.2.1
TRUSTEDPROXY__TRUSTEDNETWORKS__0=192.0.2.0/24
TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION=true
# --- Logging and observability (all optional; secrets such as the Sentry DSN and OTLP headers belong in deployment secrets) ---
SERILOG__MINIMUMLEVEL__DEFAULT=Information
ALLOWEDHOSTS=*
SENTRY__DSN=
SENTRY__ENVIRONMENT=
SENTRY__DEBUG=false
SENTRY__TRACESSAMPLERATE=0.0
OPENTELEMETRY__ENABLED=false
OPENTELEMETRY__EXPORTLOGS=true
OPENTELEMETRY__EXPORTTRACES=true
OPENTELEMETRY__EXPORTMETRICS=true
OPENTELEMETRY__OTLPENDPOINT=
OPENTELEMETRY__OTLPPROTOCOL=grpc
OPENTELEMETRY__HEADERS=
OPENTELEMETRY__TRACESSAMPLERATE=0.05
OPENTELEMETRY__SERVICENAME=
OPENTELEMETRY__SERVICEVERSION=
OPENTELEMETRY__ENVIRONMENT=
```

`src/TechStrap.Portal/.env.example`:

```dotenv
# Copy to .env.local in this directory (gitignored). SyntaxCircus.DotEnv loads .env then .env.local in Development only.
# The Portal never touches the database; it calls the API.

# --- API ---
API__BASEURL=http://localhost:8080/

# --- Public portal base URL (canonical links, sitemap; used from PHASE-09) ---
TECHSTRAP_PORTAL_PUBLIC_URL=http://localhost:8082

# --- ASP.NET Core data protection key ring (persisted volume in containers) ---
DATAPROTECTION__KEYRINGPATH=

# --- Trusted reverse proxy (Portal) ---
# Forwarded headers are trusted only from these addresses. Production refuses to start with none.
# Never trust 172.16.0.0/12 or 0.0.0.0/0. Portal trusts only the reverse proxy.
# Optional: also trust one proxy address.
# TRUSTEDPROXY__TRUSTEDPROXIES__0=192.0.2.1
TRUSTEDPROXY__TRUSTEDNETWORKS__0=192.0.2.0/24
TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION=true
# --- Logging and observability (all optional; secrets such as the Sentry DSN and OTLP headers belong in deployment secrets) ---
SERILOG__MINIMUMLEVEL__DEFAULT=Information
ALLOWEDHOSTS=*
SENTRY__DSN=
SENTRY__ENVIRONMENT=
SENTRY__DEBUG=false
SENTRY__TRACESSAMPLERATE=0.0
OPENTELEMETRY__ENABLED=false
OPENTELEMETRY__EXPORTLOGS=true
OPENTELEMETRY__EXPORTTRACES=true
OPENTELEMETRY__EXPORTMETRICS=true
OPENTELEMETRY__OTLPENDPOINT=
OPENTELEMETRY__OTLPPROTOCOL=grpc
OPENTELEMETRY__HEADERS=
OPENTELEMETRY__TRACESSAMPLERATE=0.05
OPENTELEMETRY__SERVICENAME=
OPENTELEMETRY__SERVICEVERSION=
OPENTELEMETRY__ENVIRONMENT=
```

- [ ] **Step 4: Run the test to verify it passes, then confirm the ignore rule by hand**

```bash
dotnet test --project tests/TechStrap.Api.Tests --filter-class "*EnvExampleCompletenessTests"
git check-ignore src/TechStrap.Api/.env.local
```

Expected: the test class passes (9 tests); `git check-ignore` prints `src/TechStrap.Api/.env.local` and exits 0. `git check-ignore src/TechStrap.Api/.env.example` prints nothing and exits 1 (the example is tracked).

- [ ] **Step 5: Run the whole Api test project**

```bash
dotnet test --project tests/TechStrap.Api.Tests
```

Expected: `Test run summary: Passed!` with `total: 59`.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Api/.env.example src/TechStrap.Worker/.env.example src/TechStrap.Admin/.env.example src/TechStrap.Portal/.env.example tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs
git commit -m "feat(config): document every host setting in per-host .env.example files" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 11: The four Dockerfiles

Maps to P01-T14. Deliverable: `Dockerfile.{api,admin,portal,worker}` at the repo root. They stay four separate files (the duplication is deliberate: each host may diverge, and the build script owns the shared logic).

**Files:**
- Create: `Dockerfile.api`, `Dockerfile.admin`, `Dockerfile.portal`, `Dockerfile.worker`
- Create (test): `scripts/tests/Dockerfiles.Tests.ps1`

**Interfaces:**
- Consumes: the four host projects, `.dockerignore` (Task 1), the compiled-CSS step of Admin and Portal (Task 9). Build context is the repo root.
- Produces: images exposing port 80 as uid 10001, with build args `BUILD_VERSION`, `BUILD_INFORMATIONAL_VERSION` and `DISABLE_GITVERSION_TASK` (default `true`, so the GitVersion task never needs `.git` in the image). Mount points `/app/storage`, `/app/logs` and `/app/dataprotection-keys` exist and are owned by uid 10001. `curl` is installed for compose health checks. The Admin and Portal builds fail if `wwwroot/css/app.css` is missing after publish.

- [ ] **Step 1: Write the failing Pester test**

The test reads each Dockerfile and asserts the properties the spec lists. It also checks `.dockerignore`:

```powershell
BeforeDiscovery {
    $script:Hosts = @('api', 'admin', 'portal', 'worker')
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

    function Get-DockerfileText {
        param([string]$Name)
        return Get-Content -LiteralPath (Join-Path $script:RepoRoot "Dockerfile.$Name") -Raw
    }
}

Describe 'Dockerfile.<_>' -ForEach $script:Hosts {
    BeforeAll {
        $script:Text = Get-DockerfileText -Name $_
        $script:Project = 'TechStrap.' + (Get-Culture).TextInfo.ToTitleCase($_)
    }

    It 'builds from the repository root with a BuildKit NuGet cache mount and copies the whole tree before restore' {
        $script:Text | Should -Match '# syntax=docker/dockerfile:1\.7'
        $script:Text | Should -Match '(?s)COPY \. \..*dotnet restore'
        $script:Text | Should -Match '--mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages'
    }

    It 'accepts the version build arguments and forwards them to publish' {
        foreach ($argument in 'BUILD_VERSION', 'BUILD_INFORMATIONAL_VERSION', 'DISABLE_GITVERSION_TASK') {
            $script:Text | Should -Match "ARG $argument="
        }
        $script:Text | Should -Match '/p:DisableGitVersionTask=\$\{DISABLE_GITVERSION_TASK\}'
    }

    It 'publishes the right project and starts it' {
        $script:Text | Should -Match "dotnet publish src/$($script:Project)/$($script:Project)\.csproj"
        $script:Text | Should -Match "ENTRYPOINT \[""dotnet"", ""$($script:Project)\.dll""\]"
    }

    It 'runs on the aspnet:10.0 runtime as non-root uid 10001 on port 80 with curl for health checks' {
        $script:Text | Should -Match 'FROM mcr\.microsoft\.com/dotnet/aspnet:10\.0'
        $script:Text | Should -Match 'ENV ASPNETCORE_URLS=http://\+:80'
        $script:Text | Should -Match 'USER 10001:10001'
        $script:Text | Should -Match 'apt-get install -y --no-install-recommends curl'
    }

    It 'pre-creates and chowns the storage, logs and dataprotection-keys mount points' {
        $script:Text | Should -Match 'mkdir -p /app/storage /app/logs /app/dataprotection-keys'
        $script:Text | Should -Match 'chown -R 10001:10001 /app/storage /app/logs /app/dataprotection-keys'
    }
}

Describe 'compiled CSS assertion' {
    It 'Dockerfile.<_> fails the build when wwwroot/css/app.css is missing' -ForEach 'admin', 'portal' {
        (Get-DockerfileText -Name $_) | Should -Match 'RUN test -f /app/publish/wwwroot/css/app\.css'
    }

    It 'Dockerfile.<_> has no CSS assertion because it serves no static assets' -ForEach 'api', 'worker' {
        (Get-DockerfileText -Name $_) | Should -Not -Match 'app\.css'
    }
}

Describe '.dockerignore' {
    It 'keeps secrets, git history and compiled CSS out of the build context' {
        $lines = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.dockerignore')
        $lines | Should -Contain '.git/'
        $lines | Should -Contain '**/.env'
        $lines | Should -Contain '**/wwwroot/css/app.css'
        $lines | Should -Contain '**/bin/'
        $lines | Should -Contain '**/obj/'
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/Dockerfiles.Tests.ps1
```

Expected: FAIL. `Get-Content` cannot find `Dockerfile.api` (and the other three).

- [ ] **Step 3: Write the Dockerfiles**

`Dockerfile.api`:

```dockerfile
# syntax=docker/dockerfile:1.7
# Build context is the repository root. The whole tree is copied before restore so Directory.*.props,
# Directory.Packages.props and every project the host references are present.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG BUILD_VERSION=0.1.0
ARG BUILD_INFORMATIONAL_VERSION=0.1.0
ARG DISABLE_GITVERSION_TASK=true
COPY . .
RUN --mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages \
    dotnet restore src/TechStrap.Api/TechStrap.Api.csproj
RUN --mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages \
    dotnet publish src/TechStrap.Api/TechStrap.Api.csproj -c Release -o /app/publish --no-restore \
    /p:Version=${BUILD_VERSION} /p:InformationalVersion=${BUILD_INFORMATIONAL_VERSION} /p:DisableGitVersionTask=${DISABLE_GITVERSION_TASK}

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
# curl backs the compose health checks. uid/gid 10001 is the fixed non-root runtime user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 techstrap \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin techstrap \
    && mkdir -p /app/storage /app/logs /app/dataprotection-keys \
    && chown -R 10001:10001 /app/storage /app/logs /app/dataprotection-keys
COPY --from=build --chown=10001:10001 /app/publish .
USER 10001:10001
EXPOSE 80
ENTRYPOINT ["dotnet", "TechStrap.Api.dll"]
```

`Dockerfile.worker`:

```dockerfile
# syntax=docker/dockerfile:1.7
# Build context is the repository root. The whole tree is copied before restore so Directory.*.props,
# Directory.Packages.props and every project the host references are present.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG BUILD_VERSION=0.1.0
ARG BUILD_INFORMATIONAL_VERSION=0.1.0
ARG DISABLE_GITVERSION_TASK=true
COPY . .
RUN --mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages \
    dotnet restore src/TechStrap.Worker/TechStrap.Worker.csproj
RUN --mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages \
    dotnet publish src/TechStrap.Worker/TechStrap.Worker.csproj -c Release -o /app/publish --no-restore \
    /p:Version=${BUILD_VERSION} /p:InformationalVersion=${BUILD_INFORMATIONAL_VERSION} /p:DisableGitVersionTask=${DISABLE_GITVERSION_TASK}

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
# curl backs the compose health checks. uid/gid 10001 is the fixed non-root runtime user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 techstrap \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin techstrap \
    && mkdir -p /app/storage /app/logs /app/dataprotection-keys \
    && chown -R 10001:10001 /app/storage /app/logs /app/dataprotection-keys
COPY --from=build --chown=10001:10001 /app/publish .
USER 10001:10001
EXPOSE 80
ENTRYPOINT ["dotnet", "TechStrap.Worker.dll"]
```

`Dockerfile.admin` (adds the compiled-CSS assertion):

```dockerfile
# syntax=docker/dockerfile:1.7
# Build context is the repository root. The whole tree is copied before restore so Directory.*.props,
# Directory.Packages.props and every project the host references are present.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG BUILD_VERSION=0.1.0
ARG BUILD_INFORMATIONAL_VERSION=0.1.0
ARG DISABLE_GITVERSION_TASK=true
COPY . .
RUN --mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages \
    dotnet restore src/TechStrap.Admin/TechStrap.Admin.csproj
RUN --mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages \
    dotnet publish src/TechStrap.Admin/TechStrap.Admin.csproj -c Release -o /app/publish --no-restore \
    /p:Version=${BUILD_VERSION} /p:InformationalVersion=${BUILD_INFORMATIONAL_VERSION} /p:DisableGitVersionTask=${DISABLE_GITVERSION_TASK}
# Compiled CSS is generated by the build and never committed; fail the image if it is missing.
RUN test -f /app/publish/wwwroot/css/app.css

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
# curl backs the compose health checks. uid/gid 10001 is the fixed non-root runtime user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 techstrap \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin techstrap \
    && mkdir -p /app/storage /app/logs /app/dataprotection-keys \
    && chown -R 10001:10001 /app/storage /app/logs /app/dataprotection-keys
COPY --from=build --chown=10001:10001 /app/publish .
USER 10001:10001
EXPOSE 80
ENTRYPOINT ["dotnet", "TechStrap.Admin.dll"]
```

`Dockerfile.portal`:

```dockerfile
# syntax=docker/dockerfile:1.7
# Build context is the repository root. The whole tree is copied before restore so Directory.*.props,
# Directory.Packages.props and every project the host references are present.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG BUILD_VERSION=0.1.0
ARG BUILD_INFORMATIONAL_VERSION=0.1.0
ARG DISABLE_GITVERSION_TASK=true
COPY . .
RUN --mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages \
    dotnet restore src/TechStrap.Portal/TechStrap.Portal.csproj
RUN --mount=type=cache,id=techstrap-nuget,target=/root/.nuget/packages \
    dotnet publish src/TechStrap.Portal/TechStrap.Portal.csproj -c Release -o /app/publish --no-restore \
    /p:Version=${BUILD_VERSION} /p:InformationalVersion=${BUILD_INFORMATIONAL_VERSION} /p:DisableGitVersionTask=${DISABLE_GITVERSION_TASK}
# Compiled CSS is generated by the build and never committed; fail the image if it is missing.
RUN test -f /app/publish/wwwroot/css/app.css

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:80
# curl backs the compose health checks. uid/gid 10001 is the fixed non-root runtime user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 techstrap \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin techstrap \
    && mkdir -p /app/storage /app/logs /app/dataprotection-keys \
    && chown -R 10001:10001 /app/storage /app/logs /app/dataprotection-keys
COPY --from=build --chown=10001:10001 /app/publish .
USER 10001:10001
EXPOSE 80
ENTRYPOINT ["dotnet", "TechStrap.Portal.dll"]
```

- [ ] **Step 4: Run the Pester test to verify it passes**

```bash
pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/Dockerfiles.Tests.ps1
```

Expected: `Tests Passed: 25, Failed: 0`.

- [ ] **Step 5: Build the four images**

Docker must be running. The first build downloads the SDK image and the NuGet packages (a few minutes); later builds reuse the BuildKit cache mount.

```bash
docker build -f Dockerfile.api -t techstrap-api:dev .
docker build -f Dockerfile.admin -t techstrap-admin:dev .
docker build -f Dockerfile.portal -t techstrap-portal:dev .
docker build -f Dockerfile.worker -t techstrap-worker:dev .
```

Expected: each command ends with `naming to docker.io/library/techstrap-<host>:dev done`.

- [ ] **Step 6: Check the runtime user and the tools**

```bash
docker run --rm --entrypoint id techstrap-api:dev -u
docker run --rm --entrypoint sh techstrap-worker:dev -c "curl --version && ls -ld /app/storage /app/logs /app/dataprotection-keys"
```

Expected: the first prints `10001`; the second prints the curl version line and three directories owned by `techstrap techstrap`.

- [ ] **Step 7: Prove the CSS assertion can fail**

Temporarily move the SCSS source away and delete the generated CSS, then build Admin:

```powershell
Move-Item src/TechStrap.Admin/Styles/app.scss src/TechStrap.Admin/Styles/app.scss.bak
Remove-Item src/TechStrap.Admin/wwwroot/css/app.css* -ErrorAction SilentlyContinue
docker build -f Dockerfile.admin -t techstrap-admin:neg .
Move-Item src/TechStrap.Admin/Styles/app.scss.bak src/TechStrap.Admin/Styles/app.scss
```

Expected: the build FAILS at `RUN test -f /app/publish/wwwroot/css/app.css` with `exit code: 1`. After restoring the file, `docker build -f Dockerfile.admin -t techstrap-admin:dev .` succeeds again. (In Git Bash use `mv` and `rm -f` for the same steps.)

- [ ] **Step 8: Commit**

```bash
git add Dockerfile.api Dockerfile.admin Dockerfile.portal Dockerfile.worker scripts/tests/Dockerfiles.Tests.ps1
git commit -m "build: add the four non-root, multi-stage Dockerfiles" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 12: `Build-TechStrapDocker.ps1` and `BuildScriptTests`

Maps to P01-T15. Deliverable: the image build script of spec section 9 (modeled on `Build-SinForgiverDocker.ps1` and the-button's script) with a `-DryRun` switch, and Pester tests for tag selection, SemVer rejection and `-amd64`/`-arm64` suffixing.

**Files:**
- Create: `Build-TechStrapDocker.ps1`
- Create (test): `scripts/tests/BuildScriptTests.Tests.ps1`

**Interfaces:**
- Consumes: the four Dockerfiles (Task 11), `GitVersion.yml`, `GitVersion.MsBuild` in the Api project (Tasks 1 and 2), `scripts/Invoke-ScriptTests.ps1`.
- Produces: `Build-TechStrapDocker.ps1` with parameters `-Targets` (`api`, `admin`, `portal`, `worker`; default all four), `-ImageTag`, `-SemVerTag`, `-Registry` (empty by default), `-Push`, `-PushLatest` (bool, default true), `-NoCache`, `-Platforms` (`linux/amd64`, `linux/arm64`; default both), `-VersionProjectPath` (default `src/TechStrap.Api/TechStrap.Api.csproj`), `-DryRun`. Version resolution order: `-ImageTag`, else `dotnet msbuild -target:GetVersion`, else `dotnet-gitversion`, else `gitversion`; the version must be valid SemVer or the script throws `... is not valid SemVer`. Pure functions, loadable by dot-sourcing (the script returns before `Invoke-Main` when dot-sourced): `Test-SemVer`, `Get-ImageTags -ImageTag -SemVerTag -PushLatest`, `Get-ImageReference`, `Get-PlatformSuffix`, `Get-BuildPlan -Targets -Tags -BuildVersion -InformationalVersion -Registry -Push -NoCache -Platforms` returning objects with `Target`, `Mode` (`push` or `local`), `Platform`, `Images`, `Arguments`. Behavior: with `-Push` and a `-Registry`, one multi-platform `docker buildx build --push` per image; otherwise one `--load` build per platform whose tags carry an `-amd64`/`-arm64` suffix, with the canonical tags added to the amd64 build. Every command carries `--build-arg BUILD_VERSION=...`, `BUILD_INFORMATIONAL_VERSION=...` and `DISABLE_GITVERSION_TASK=true`.

- [ ] **Step 1: Write the failing Pester tests**

They run the script as a child process in `-DryRun` mode (four commands, build args, SemVer rejection, unknown target, GitVersion resolution) and call the pure functions after dot-sourcing (SemVer, tag selection, push versus local plans, suffixes, no-cache):

```powershell
BeforeAll {
    $script:ScriptPath = (Resolve-Path (Join-Path $PSScriptRoot '..' '..' 'Build-TechStrapDocker.ps1')).Path

    # Dot-sourcing loads the functions only; the script returns before Invoke-Main.
    . $script:ScriptPath

    function Invoke-BuildScript {
        param([string[]]$Arguments)
        $output = & pwsh -NoProfile -File $script:ScriptPath @Arguments 2>&1 | Out-String
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    }

    function Get-DockerLines {
        param([string]$Output)
        return @($Output -split "`r?`n" | Where-Object { $_ -match '^docker buildx build' })
    }
}

Describe 'Build-TechStrapDocker.ps1 -DryRun' {
    It 'prints four buildx commands carrying the version build args' {
        $result = Invoke-BuildScript -Arguments @('-DryRun', '-ImageTag', '1.2.3', '-Platforms', 'linux/amd64')
        $result.ExitCode | Should -Be 0

        $lines = @(Get-DockerLines -Output $result.Output)
        $lines.Count | Should -Be 4
        foreach ($line in $lines) {
            $line | Should -Match '--build-arg BUILD_VERSION=1\.2\.3'
            $line | Should -Match '--build-arg BUILD_INFORMATIONAL_VERSION=1\.2\.3'
            $line | Should -Match '--build-arg DISABLE_GITVERSION_TASK=true'
        }
        foreach ($name in 'api', 'admin', 'portal', 'worker') {
            @($lines | Where-Object { $_ -match "-f Dockerfile\.$name " }).Count | Should -Be 1
            @($lines | Where-Object { $_ -match "-t techstrap-${name}:1\.2\.3 " }).Count | Should -Be 1
        }
    }

    It 'rejects an ImageTag that is not SemVer' {
        $result = Invoke-BuildScript -Arguments @('-DryRun', '-ImageTag', 'not-semver')
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'not valid SemVer'
    }

    It 'rejects an unknown target' {
        $result = Invoke-BuildScript -Arguments @('-DryRun', '-ImageTag', '1.2.3', '-Targets', 'bogus')
        $result.ExitCode | Should -Not -Be 0
    }

    It 'resolves the version from GitVersion when no ImageTag is given' {
        $result = Invoke-BuildScript -Arguments @('-DryRun', '-Platforms', 'linux/amd64', '-Targets', 'api')
        $result.ExitCode | Should -Be 0
        @(Get-DockerLines -Output $result.Output)[0] | Should -Match 'BUILD_VERSION=\d+\.\d+\.\d+'
    }
}

Describe 'Test-SemVer' {
    It 'accepts <_>' -ForEach '0.1.0', '1.2.3-rc.1', '1.0.0-uat.dirty.20260823', '2.0.0+build.5' {
        Test-SemVer -Version $_ | Should -BeTrue
    }

    It 'rejects <_>' -ForEach 'not-semver', '1.2', 'v1.2.3', '1.2.3.4', '' {
        Test-SemVer -Version $_ | Should -BeFalse
    }
}

Describe 'Get-ImageTags' {
    It 'adds latest by default' {
        Get-ImageTags -ImageTag '1.2.3' | Should -Be @('1.2.3', 'latest')
    }

    It 'drops latest when PushLatest is false' {
        Get-ImageTags -ImageTag '1.2.3' -PushLatest $false | Should -Be @('1.2.3')
    }

    It 'adds a distinct SemVer tag and removes duplicates' {
        Get-ImageTags -ImageTag '1.2.3-uat.1' -SemVerTag '1.2.3' | Should -Be @('1.2.3-uat.1', '1.2.3', 'latest')
        Get-ImageTags -ImageTag '1.2.3' -SemVerTag '1.2.3' | Should -Be @('1.2.3', 'latest')
    }
}

Describe 'Get-BuildPlan' {
    BeforeAll {
        $script:Common = @{
            Targets = @('api', 'worker')
            Tags = @('1.2.3', 'latest')
            BuildVersion = '1.2.3'
            InformationalVersion = '1.2.3+5.Branch.main'
        }
    }

    It 'builds one multi-platform push per image when pushing to a registry' {
        $plan = Get-BuildPlan @script:Common -Registry 'ghcr.io/syntax-circus/' -Push $true

        $plan.Count | Should -Be 2
        foreach ($step in $plan) {
            $step.Mode | Should -Be 'push'
            $step.Arguments | Should -Contain '--push'
            $step.Arguments | Should -Not -Contain '--load'
            $step.Arguments[($step.Arguments.IndexOf('--platform') + 1)] | Should -Be 'linux/amd64,linux/arm64'
        }
        $plan[0].Images | Should -Be @('ghcr.io/syntax-circus/techstrap-api:1.2.3', 'ghcr.io/syntax-circus/techstrap-api:latest')
    }

    It 'builds each platform separately with --load and suffixes the tags when not pushing' {
        $plan = Get-BuildPlan @script:Common -Push $false

        $plan.Count | Should -Be 4
        $amd64 = $plan | Where-Object { $_.Target -eq 'api' -and $_.Platform -eq 'linux/amd64' }
        $arm64 = $plan | Where-Object { $_.Target -eq 'api' -and $_.Platform -eq 'linux/arm64' }

        $amd64.Arguments | Should -Contain '--load'
        $amd64.Images | Should -Be @('techstrap-api:1.2.3-amd64', 'techstrap-api:latest-amd64', 'techstrap-api:1.2.3', 'techstrap-api:latest')
        $arm64.Images | Should -Be @('techstrap-api:1.2.3-arm64', 'techstrap-api:latest-arm64')
    }

    It 'falls back to local builds when -Push is set without a registry' {
        $plan = Get-BuildPlan @script:Common -Push $true -Registry ''

        $plan | ForEach-Object { $_.Mode | Should -Be 'local' }
    }

    It 'passes the version build args and disables the GitVersion task in every command' {
        $plan = Get-BuildPlan @script:Common

        foreach ($step in $plan) {
            $step.Arguments | Should -Contain 'BUILD_VERSION=1.2.3'
            $step.Arguments | Should -Contain 'BUILD_INFORMATIONAL_VERSION=1.2.3+5.Branch.main'
            $step.Arguments | Should -Contain 'DISABLE_GITVERSION_TASK=true'
        }
    }

    It 'adds --no-cache when requested' {
        (Get-BuildPlan @script:Common -NoCache $true)[0].Arguments | Should -Contain '--no-cache'
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/BuildScriptTests.Tests.ps1
```

Expected: FAIL (`BeforeAll` cannot resolve `Build-TechStrapDocker.ps1`).

- [ ] **Step 3: Write the script**

```powershell
<#
.SYNOPSIS
  Builds (and optionally pushes) the four TechStrap container images.
.DESCRIPTION
  Images are named techstrap-{api,admin,portal,worker}. The version comes from -ImageTag, or from
  GitVersion (dotnet msbuild -target:GetVersion, then dotnet-gitversion, then gitversion) and must be
  valid SemVer.

  -Push with a -Registry runs one multi-platform `docker buildx build --push` per image.
  Otherwise each platform is built separately with --load: tags get an -amd64 / -arm64 suffix, and the
  amd64 build also gets the canonical tags.

  -DryRun prints the docker commands and runs nothing (no Docker needed).
.EXAMPLE
  ./Build-TechStrapDocker.ps1 -Platforms linux/amd64
.EXAMPLE
  ./Build-TechStrapDocker.ps1 -Push -Registry ghcr.io/syntax-circus -ImageTag 0.1.0
#>
[CmdletBinding()]
param(
    [ValidateSet('api', 'admin', 'portal', 'worker')]
    [string[]]$Targets = @('api', 'admin', 'portal', 'worker'),

    [string]$ImageTag = '',

    [string]$SemVerTag = '',

    [string]$Registry = '',

    [switch]$Push,

    [bool]$PushLatest = $true,

    [switch]$NoCache,

    [ValidateSet('linux/amd64', 'linux/arm64')]
    [string[]]$Platforms = @('linux/amd64', 'linux/arm64'),

    [string]$VersionProjectPath = 'src/TechStrap.Api/TechStrap.Api.csproj',

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:ImageNamePrefix = 'techstrap-'
$script:SemVerPattern = '^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'

function Test-SemVer {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Version)
    return $Version -match $script:SemVerPattern
}

function Get-PlatformSuffix {
    param([Parameter(Mandatory)][string]$Platform)
    switch ($Platform) {
        'linux/amd64' { return 'amd64' }
        'linux/arm64' { return 'arm64' }
        default { throw "Unsupported platform '$Platform'." }
    }
}

function Get-ImageReference {
    param(
        [Parameter(Mandatory)][string]$ImageName,
        [Parameter(Mandatory)][string]$Tag,
        [string]$Registry = ''
    )

    $prefix = if ([string]::IsNullOrWhiteSpace($Registry)) { '' } else { $Registry.Trim().TrimEnd('/') + '/' }
    return "$prefix${ImageName}:$Tag"
}

function Get-ImageTags {
    param(
        [Parameter(Mandatory)][string]$ImageTag,
        [string]$SemVerTag = '',
        [bool]$PushLatest = $true
    )

    $tags = @($ImageTag, $SemVerTag)
    if ($PushLatest) {
        $tags += 'latest'
    }

    return @($tags | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
}

function Invoke-GitVersionCli {
    param([string]$Command, [string[]]$Arguments)

    if ($null -eq (Get-Command -Name $Command -ErrorAction SilentlyContinue)) {
        return $null
    }

    $output = & $Command @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    try {
        $parsed = $output | ConvertFrom-Json
    }
    catch {
        return $null
    }

    if ([string]::IsNullOrWhiteSpace($parsed.SemVer)) {
        return $null
    }

    return [pscustomobject]@{ SemVer = [string]$parsed.SemVer; InformationalVersion = [string]$parsed.InformationalVersion }
}

function Resolve-GitVersion {
    param(
        [Parameter(Mandatory)][string]$ProjectFile,
        [Parameter(Mandatory)][string]$ConfigFile
    )

    if (Test-Path -LiteralPath $ProjectFile) {
        $output = & dotnet msbuild $ProjectFile -nologo -verbosity:quiet -target:GetVersion `
            -getProperty:GitVersion_SemVer -getProperty:GitVersion_InformationalVersion 2>&1 | Out-String
        if ($LASTEXITCODE -eq 0) {
            try {
                $properties = ($output | ConvertFrom-Json).Properties
                if (-not [string]::IsNullOrWhiteSpace($properties.GitVersion_SemVer)) {
                    return [pscustomobject]@{
                        SemVer = [string]$properties.GitVersion_SemVer
                        InformationalVersion = [string]$properties.GitVersion_InformationalVersion
                    }
                }
            }
            catch {
                # Fall through to the CLI tools.
            }
        }
    }

    $cliArguments = @('/output', 'json', '/config', $ConfigFile)
    foreach ($command in 'dotnet-gitversion', 'gitversion') {
        $resolved = Invoke-GitVersionCli -Command $command -Arguments $cliArguments
        if ($null -ne $resolved) {
            return $resolved
        }
    }

    throw 'Unable to resolve a version with GitVersion. Install GitVersion or pass -ImageTag explicitly.'
}

function Get-BuildPlan {
    <#
      Pure function: turns the parameters into the ordered list of docker commands. Each result has
      Target, Mode ('push' or 'local'), Platform, Images and Arguments (the arguments after `docker`).
    #>
    param(
        [Parameter(Mandatory)][string[]]$Targets,
        [Parameter(Mandatory)][string[]]$Tags,
        [Parameter(Mandatory)][string]$BuildVersion,
        [Parameter(Mandatory)][string]$InformationalVersion,
        [string]$Registry = '',
        [bool]$Push = $false,
        [bool]$NoCache = $false,
        [string[]]$Platforms = @('linux/amd64', 'linux/arm64')
    )

    $shouldPush = $Push -and -not [string]::IsNullOrWhiteSpace($Registry)
    $buildArguments = @(
        '--build-arg', "BUILD_VERSION=$BuildVersion",
        '--build-arg', "BUILD_INFORMATIONAL_VERSION=$InformationalVersion",
        '--build-arg', 'DISABLE_GITVERSION_TASK=true'
    )

    $plan = New-Object System.Collections.Generic.List[object]

    foreach ($target in ($Targets | Select-Object -Unique)) {
        $imageName = "$($script:ImageNamePrefix)$target"
        $dockerfile = "Dockerfile.$target"
        $canonical = @($Tags | ForEach-Object { Get-ImageReference -ImageName $imageName -Tag $_ -Registry $Registry })

        if ($shouldPush) {
            $arguments = @('buildx', 'build', '--platform', ($Platforms -join ','))
            foreach ($image in $canonical) { $arguments += @('-t', $image) }
            $arguments += $buildArguments
            $arguments += @('-f', $dockerfile)
            if ($NoCache) { $arguments += '--no-cache' }
            $arguments += @('--push', '.')

            $plan.Add([pscustomobject]@{ Target = $target; Mode = 'push'; Platform = ($Platforms -join ','); Images = $canonical; Arguments = $arguments })
            continue
        }

        foreach ($platform in $Platforms) {
            $suffix = Get-PlatformSuffix -Platform $platform
            $images = @($Tags | ForEach-Object { Get-ImageReference -ImageName $imageName -Tag "$_-$suffix" -Registry $Registry })
            if ($platform -eq 'linux/amd64') {
                $images += $canonical
            }

            $arguments = @('buildx', 'build', '--platform', $platform)
            foreach ($image in $images) { $arguments += @('-t', $image) }
            $arguments += $buildArguments
            $arguments += @('-f', $dockerfile)
            if ($NoCache) { $arguments += '--no-cache' }
            $arguments += @('--load', '.')

            $plan.Add([pscustomobject]@{ Target = $target; Mode = 'local'; Platform = $platform; Images = $images; Arguments = $arguments })
        }
    }

    return $plan.ToArray()
}

function Invoke-Main {
    if (-not [string]::IsNullOrWhiteSpace($ImageTag) -and -not (Test-SemVer -Version $ImageTag)) {
        throw "ImageTag '$ImageTag' is not valid SemVer. Use a value such as '0.1.0' or '0.1.0-rc.1'."
    }

    $buildVersion = $ImageTag
    $informationalVersion = $ImageTag
    $semVerTag = $SemVerTag

    if ([string]::IsNullOrWhiteSpace($ImageTag)) {
        $resolved = Resolve-GitVersion -ProjectFile (Join-Path $PSScriptRoot $VersionProjectPath) -ConfigFile (Join-Path $PSScriptRoot 'GitVersion.yml')
        $buildVersion = $resolved.SemVer
        $informationalVersion = if ([string]::IsNullOrWhiteSpace($resolved.InformationalVersion)) { $resolved.SemVer } else { $resolved.InformationalVersion }
        $ImageTag = $resolved.SemVer
        if ([string]::IsNullOrWhiteSpace($semVerTag)) { $semVerTag = $resolved.SemVer }
    }

    if (-not (Test-SemVer -Version $buildVersion)) {
        throw "Build version '$buildVersion' is not valid SemVer."
    }

    if ($Push -and [string]::IsNullOrWhiteSpace($Registry)) {
        Write-Warning 'No -Registry given: -Push is ignored and the images are built locally per platform.'
    }

    $tags = Get-ImageTags -ImageTag $ImageTag -SemVerTag $semVerTag -PushLatest $PushLatest
    $plan = Get-BuildPlan -Targets $Targets -Tags $tags -BuildVersion $buildVersion -InformationalVersion $informationalVersion `
        -Registry $Registry -Push ([bool]$Push) -NoCache ([bool]$NoCache) -Platforms $Platforms

    Write-Host "Version: $buildVersion  Tags: $($tags -join ', ')  Platforms: $($Platforms -join ', ')"

    if (-not $DryRun) {
        & docker info *> $null
        if ($LASTEXITCODE -ne 0) { throw 'Docker is not running.' }
        & docker buildx version *> $null
        if ($LASTEXITCODE -ne 0) { throw 'Docker buildx is required.' }
    }

    Push-Location $PSScriptRoot
    try {
        foreach ($step in $plan) {
            $line = "docker $($step.Arguments -join ' ')"
            if ($DryRun) {
                Write-Host $line
                continue
            }

            Write-Host "`n=== $($step.Target) [$($step.Mode), $($step.Platform)] ===`n$line`n"
            & docker @($step.Arguments)
            if ($LASTEXITCODE -ne 0) {
                throw "docker build failed for $($step.Target) ($($step.Platform)) with exit code $LASTEXITCODE."
            }
        }
    }
    finally {
        Pop-Location
    }
}

# Dot-sourcing (Pester) loads the functions only.
if ($MyInvocation.InvocationName -eq '.') {
    return
}

Invoke-Main
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/BuildScriptTests.Tests.ps1
```

Expected: `Tests Passed: 21, Failed: 0`. The GitVersion test needs a git repository with at least one commit, which this checkout is.

- [ ] **Step 5: Check the dry run and the SemVer rejection by hand**

```bash
pwsh ./Build-TechStrapDocker.ps1 -DryRun -Platforms linux/amd64
pwsh ./Build-TechStrapDocker.ps1 -DryRun -ImageTag not-semver
```

Expected: the first prints a `Version: ...` line and four `docker buildx build --platform linux/amd64 ...` lines (api, admin, portal, worker), each with `--build-arg BUILD_VERSION=...`, `--build-arg BUILD_INFORMATIONAL_VERSION=...` and `--build-arg DISABLE_GITVERSION_TASK=true`; the second throws `ImageTag 'not-semver' is not valid SemVer` and exits non-zero.

- [ ] **Step 6: Run a real local build**

```bash
pwsh ./Build-TechStrapDocker.ps1 -Targets api -Platforms linux/amd64
docker images techstrap-api --format "{{.Repository}}:{{.Tag}}"
```

Expected: the images list contains `techstrap-api:<semver>`, `techstrap-api:latest` and the `-amd64` suffixed variants of both. (The default `-Platforms` also builds `linux/arm64`, which needs QEMU emulation; Docker Desktop has it, a bare Linux host may need `docker run --privileged --rm tonistiigi/binfmt --install arm64`.)

- [ ] **Step 7: Commit**

```bash
git add Build-TechStrapDocker.ps1 scripts/tests/BuildScriptTests.Tests.ps1
git commit -m "build: add the Docker image build script with dry-run and Pester tests" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 13: Compose files and `.env.production.example`

Maps to P01-T16. Deliverable: `docker-compose.yml` (local: Postgres 17 plus the four built images), `docker-compose.uat.yml` and `docker-compose.production.yml` (GHCR images, pinned subnet, required-secret interpolation), `.env.production.example`, and Pester tests over `docker compose config`.

**Files:**
- Create: `docker-compose.yml`, `docker-compose.uat.yml`, `docker-compose.production.yml`, `.env.production.example`
- Create (test): `scripts/tests/ComposeFiles.Tests.ps1`

**Interfaces:**
- Consumes: the four Dockerfiles (Task 11), the host environment contract from the `.env.example` files (Task 10), `GET /health/live` and `/health/ready` (Tasks 6 and 9).
- Produces:
  - Network `default` pinned to `${TECHSTRAP_SUBNET:-172.16.31.0/24}` (D-019). The default is the pinned subnet, chosen outside Docker's auto-assign pool; the variable is only an escape hatch for a host where that range is already taken.
  - The API trusts the subnet as `TRUSTEDPROXY__TRUSTEDNETWORKS__0` (and, in production and UAT, the required `REVERSE_PROXY_CIDR` as `__1`); Admin and Portal trust only the reverse proxy (`__0`: `REVERSE_PROXY_CIDR`, required in production and UAT; a TEST-NET placeholder in the local file). The proxy and TLS stay outside compose.
  - Volume `techstrap-storage` mounted at `/app/storage` on `api` and `worker` only; key-ring volumes `admin-keys` and `portal-keys` at `/app/dataprotection-keys`.
  - Startup order: `postgres` healthy, then `api` healthy (it migrates), then `admin`, `portal`, `worker`. Health checks use `curl`: `/health/ready` for api and worker, `/health/live` for admin and portal.
  - Local file: `docker compose up -d --build`, host ports 8080 (api), 8081 (admin), 8082 (portal), 8083 (worker) on loopback; per-host `.env.local` loaded with `env_file` and `required: false`. Production and UAT files: images `ghcr.io/syntax-circus/techstrap-<host>:${TECHSTRAP_IMAGE_TAG:-latest}`, `POSTGRES_PASSWORD`, `REVERSE_PROXY_CIDR` and `TECHSTRAP_PORTAL_PUBLIC_URL` required (compose refuses to resolve without them), UAT uses project name `techstrap-uat` and host ports 18080 to 18082.
- **Owner action (cross-repo, D-019):** open a PR in `_template` adding the TechStrap `172.16.31.0/24` row to the subnet registry in `docs/patterns/CLIENT_IP_RATE_LIMITING.md`, and confirm the subnet is free on the UAT host. See Step 8.

- [ ] **Step 1: Write the failing Pester tests**

They call `docker compose config --format json` and assert on the resolved model: the pinned subnet, the four services plus `postgres:17`, no wide trusted ranges, the storage volume only on api and worker, the production and UAT trust split, and that both refuse to resolve without `POSTGRES_PASSWORD`:

```powershell
BeforeDiscovery {
    $script:DockerAvailable = $null -ne (Get-Command docker -ErrorAction SilentlyContinue) -and
        ((& docker compose version 2>&1 | Out-String) -match 'Docker Compose')
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:PinnedSubnet = '172.16.31.0/24'

    function Get-ComposeConfig {
        param([string]$File, [string]$EnvFile = '')

        $arguments = @('compose')
        if ($EnvFile) { $arguments += @('--env-file', $EnvFile) }
        $arguments += @('-f', (Join-Path $script:RepoRoot $File), 'config', '--format', 'json')

        $output = & docker @arguments 2>&1 | Out-String
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output   = $output
            Config   = if ($LASTEXITCODE -eq 0) { $output | ConvertFrom-Json } else { $null }
        }
    }

    function New-ProductionEnvFile {
        param([string]$Path, [switch]$WithoutPostgresPassword)

        $lines = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.env.production.example')
        if ($WithoutPostgresPassword) {
            $lines = $lines | ForEach-Object { if ($_ -like 'POSTGRES_PASSWORD=*') { 'POSTGRES_PASSWORD=' } else { $_ } }
        }
        Set-Content -LiteralPath $Path -Value $lines
    }

    Remove-Item Env:TECHSTRAP_SUBNET -ErrorAction SilentlyContinue
}

Describe 'docker-compose files' -Skip:(-not $script:DockerAvailable) {
    It 'local compose pins the subnet and the API trusts it' {
        $result = Get-ComposeConfig -File 'docker-compose.yml'
        $result.ExitCode | Should -Be 0
        $result.Config.networks.default.ipam.config[0].subnet | Should -Be $script:PinnedSubnet
        $result.Config.services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be $script:PinnedSubnet
    }

    It 'local compose has the four app services plus Postgres 17 and never trusts a wide range' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml').Config
        ($config.services.PSObject.Properties.Name | Sort-Object) | Should -Be @('admin', 'api', 'portal', 'postgres', 'worker')
        $config.services.postgres.image | Should -Be 'postgres:17'
        foreach ($service in 'api', 'admin', 'portal') {
            $config.services.$service.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Not -Match '^(172\.16\.0\.0/12|0\.0\.0\.0/0)$'
        }
    }

    It 'local compose mounts the shared storage volume on api and worker only' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml').Config
        foreach ($service in 'api', 'worker') {
            @($config.services.$service.volumes | Where-Object { $_.target -eq '/app/storage' -and $_.source -eq 'techstrap-storage' }).Count | Should -Be 1
        }
        foreach ($service in 'admin', 'portal') {
            @($config.services.$service.volumes | Where-Object { $_.target -eq '/app/storage' }).Count | Should -Be 0
        }
    }

    It '<file> resolves with the example env file, trusts the proxy in Admin and Portal only, and the API also trusts the subnet' -ForEach @(
        @{ file = 'docker-compose.production.yml' }
        @{ file = 'docker-compose.uat.yml' }
    ) {
        $envFile = Join-Path $TestDrive 'env'
        New-ProductionEnvFile -Path $envFile
        $result = Get-ComposeConfig -File $file -EnvFile $envFile
        $result.ExitCode | Should -Be 0
        $services = $result.Config.services

        $result.Config.networks.default.ipam.config[0].subnet | Should -Be $script:PinnedSubnet
        $services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be $script:PinnedSubnet
        $services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__1 | Should -Be '192.168.1.55/32'
        foreach ($service in 'admin', 'portal') {
            $services.$service.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be '192.168.1.55/32'
            $services.$service.environment.PSObject.Properties.Name | Should -Not -Contain 'TRUSTEDPROXY__TRUSTEDNETWORKS__1'
        }
        $services.api.image | Should -Be 'ghcr.io/syntax-circus/techstrap-api:latest'
    }

    It '<file> refuses to resolve without POSTGRES_PASSWORD' -ForEach @(
        @{ file = 'docker-compose.production.yml' }
        @{ file = 'docker-compose.uat.yml' }
    ) {
        $envFile = Join-Path $TestDrive 'env-nopassword'
        New-ProductionEnvFile -Path $envFile -WithoutPostgresPassword
        $result = Get-ComposeConfig -File $file -EnvFile $envFile
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match 'POSTGRES_PASSWORD'
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeFiles.Tests.ps1
```

Expected: FAIL (`docker compose config` reports that the compose files do not exist).

- [ ] **Step 3: Write the local compose file**

```yaml
# Local stack: Postgres 17 plus the four TechStrap images built from this checkout.
#   docker compose up -d --build
# Optional per-host overrides go in src/TechStrap.<Host>/.env.local (gitignored, see .env.example).
# The reverse proxy and TLS are not part of compose.
name: techstrap

services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_DB: techstrap
      POSTGRES_USER: techstrap
      POSTGRES_PASSWORD: techstrap-local
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U techstrap -d techstrap"]
      interval: 5s
      timeout: 3s
      retries: 10

  api:
    build:
      context: .
      dockerfile: Dockerfile.api
    image: techstrap-api:local
    env_file:
      - path: ./src/TechStrap.Api/.env.local
        required: false
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__TechStrap: Host=postgres;Port=5432;Database=techstrap;Username=techstrap;Password=techstrap-local
      # The API trusts the pinned compose subnet (the Portal hop). Add the reverse proxy address as
      # TRUSTEDPROXY__TRUSTEDNETWORKS__1 only when the proxy runs outside this subnet.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${TECHSTRAP_SUBNET:-172.16.31.0/24}
      TECHSTRAP_PORTAL_PUBLIC_URL: http://localhost:8082
      TECHSTRAP_SEED_DEV_DATA: ${TECHSTRAP_SEED_DEV_DATA:-false}
      Storage__Provider: Local
      Storage__Local__RootPath: /app/storage
    depends_on:
      postgres:
        condition: service_healthy
    volumes:
      - techstrap-storage:/app/storage
    ports:
      - "127.0.0.1:8080:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/ready"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 15s

  admin:
    build:
      context: .
      dockerfile: Dockerfile.admin
    image: techstrap-admin:local
    env_file:
      - path: ./src/TechStrap.Admin/.env.local
        required: false
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      Api__BaseUrl: http://api/
      # Admin trusts only the reverse proxy. 192.0.2.0/24 is a placeholder; set REVERSE_PROXY_CIDR.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${REVERSE_PROXY_CIDR:-192.0.2.0/24}
      DataProtection__KeyRingPath: /app/dataprotection-keys
    depends_on:
      api:
        condition: service_healthy
    volumes:
      - admin-keys:/app/dataprotection-keys
    ports:
      - "127.0.0.1:8081:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/live"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 15s

  portal:
    build:
      context: .
      dockerfile: Dockerfile.portal
    image: techstrap-portal:local
    env_file:
      - path: ./src/TechStrap.Portal/.env.local
        required: false
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      Api__BaseUrl: http://api/
      # Portal trusts only the reverse proxy. 192.0.2.0/24 is a placeholder; set REVERSE_PROXY_CIDR.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${REVERSE_PROXY_CIDR:-192.0.2.0/24}
      TECHSTRAP_PORTAL_PUBLIC_URL: http://localhost:8082
      DataProtection__KeyRingPath: /app/dataprotection-keys
    depends_on:
      api:
        condition: service_healthy
    volumes:
      - portal-keys:/app/dataprotection-keys
    ports:
      - "127.0.0.1:8082:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/live"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 15s

  worker:
    build:
      context: .
      dockerfile: Dockerfile.worker
    image: techstrap-worker:local
    env_file:
      - path: ./src/TechStrap.Worker/.env.local
        required: false
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__TechStrap: Host=postgres;Port=5432;Database=techstrap;Username=techstrap;Password=techstrap-local
      TECHSTRAP_PORTAL_PUBLIC_URL: http://localhost:8082
      Storage__Provider: Local
      Storage__Local__RootPath: /app/storage
    depends_on:
      api:
        condition: service_healthy
    volumes:
      - techstrap-storage:/app/storage
    ports:
      - "127.0.0.1:8083:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/ready"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 15s

networks:
  default:
    ipam:
      config:
        # Product-unique pinned subnet (D-019), registered in _template CLIENT_IP_RATE_LIMITING.md.
        # 172.16.0.0/16 is outside Docker's default auto-assign pool; override TECHSTRAP_SUBNET only if this host already uses it.
        - subnet: ${TECHSTRAP_SUBNET:-172.16.31.0/24}

volumes:
  pgdata:
  techstrap-storage:
  admin-keys:
  portal-keys:
```

- [ ] **Step 4: Write the production compose file, the UAT file and the example environment**

`docker-compose.production.yml`:

```yaml
# Production stack: Postgres 17 plus the four published GHCR images. TLS and the reverse proxy stay
# outside compose; the services publish on loopback only for the proxy to reach.
#   cp .env.production.example .env.production   # then edit
#   docker compose --env-file .env.production -f docker-compose.production.yml up -d
# Required values (POSTGRES_PASSWORD, REVERSE_PROXY_CIDR, TECHSTRAP_PORTAL_PUBLIC_URL) make compose
# refuse to start when they are missing.
name: techstrap

services:
  postgres:
    image: postgres:17
    restart: unless-stopped
    environment:
      POSTGRES_DB: ${POSTGRES_DB:-techstrap}
      POSTGRES_USER: ${POSTGRES_USER:-techstrap}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env.production}
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $$POSTGRES_USER -d $$POSTGRES_DB"]
      interval: 10s
      timeout: 5s
      retries: 10

  api:
    image: ghcr.io/syntax-circus/techstrap-api:${TECHSTRAP_IMAGE_TAG:-latest}
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__TechStrap: Host=postgres;Port=5432;Database=${POSTGRES_DB:-techstrap};Username=${POSTGRES_USER:-techstrap};Password=${POSTGRES_PASSWORD}
      # Trust the pinned compose subnet (the Portal hop) and, when the proxy runs outside it, the
      # proxy address. REVERSE_PROXY_CIDR is required, so entry 1 is always the proxy.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${TECHSTRAP_SUBNET:-172.16.31.0/24}
      TRUSTEDPROXY__TRUSTEDNETWORKS__1: ${REVERSE_PROXY_CIDR:?set REVERSE_PROXY_CIDR in .env.production}
      TECHSTRAP_PORTAL_PUBLIC_URL: ${TECHSTRAP_PORTAL_PUBLIC_URL:?set TECHSTRAP_PORTAL_PUBLIC_URL in .env.production}
      TECHSTRAP_AGENT_GROUP: ${TECHSTRAP_AGENT_GROUP:-techstrap-agents}
      TECHSTRAP_ADMIN_GROUP: ${TECHSTRAP_ADMIN_GROUP:-techstrap-admins}
      TECHSTRAP_BOOTSTRAP_ADMIN: ${TECHSTRAP_BOOTSTRAP_ADMIN:-}
      Authentication__JwtBearer__Authority: ${OIDC_AUTHORITY:-}
      Authentication__JwtBearer__Audiences__0: ${OIDC_AUDIENCE:-}
      Storage__Provider: Local
      Storage__Local__RootPath: /app/storage
    depends_on:
      postgres:
        condition: service_healthy
    volumes:
      - techstrap-storage:/app/storage
    ports:
      - "127.0.0.1:${API_PORT:-8080}:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/ready"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

  admin:
    image: ghcr.io/syntax-circus/techstrap-admin:${TECHSTRAP_IMAGE_TAG:-latest}
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Api__BaseUrl: http://api/
      # Admin trusts only the reverse proxy.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${REVERSE_PROXY_CIDR:?set REVERSE_PROXY_CIDR in .env.production}
      DataProtection__KeyRingPath: /app/dataprotection-keys
      Auth__Authority: ${OIDC_AUTHORITY:-}
      Auth__ClientId: ${OIDC_ADMIN_CLIENT_ID:-}
      Auth__ClientSecret: ${OIDC_ADMIN_CLIENT_SECRET:-}
      TECHSTRAP_ADMIN_GROUP: ${TECHSTRAP_ADMIN_GROUP:-techstrap-admins}
    depends_on:
      api:
        condition: service_healthy
    volumes:
      - admin-keys:/app/dataprotection-keys
    ports:
      - "127.0.0.1:${ADMIN_PORT:-8081}:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/live"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

  portal:
    image: ghcr.io/syntax-circus/techstrap-portal:${TECHSTRAP_IMAGE_TAG:-latest}
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Api__BaseUrl: http://api/
      # Portal trusts only the reverse proxy.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${REVERSE_PROXY_CIDR:?set REVERSE_PROXY_CIDR in .env.production}
      TECHSTRAP_PORTAL_PUBLIC_URL: ${TECHSTRAP_PORTAL_PUBLIC_URL:?set TECHSTRAP_PORTAL_PUBLIC_URL in .env.production}
      DataProtection__KeyRingPath: /app/dataprotection-keys
    depends_on:
      api:
        condition: service_healthy
    volumes:
      - portal-keys:/app/dataprotection-keys
    ports:
      - "127.0.0.1:${PORTAL_PORT:-8082}:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/live"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

  worker:
    image: ghcr.io/syntax-circus/techstrap-worker:${TECHSTRAP_IMAGE_TAG:-latest}
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__TechStrap: Host=postgres;Port=5432;Database=${POSTGRES_DB:-techstrap};Username=${POSTGRES_USER:-techstrap};Password=${POSTGRES_PASSWORD}
      TECHSTRAP_PORTAL_PUBLIC_URL: ${TECHSTRAP_PORTAL_PUBLIC_URL:?set TECHSTRAP_PORTAL_PUBLIC_URL in .env.production}
      Storage__Provider: Local
      Storage__Local__RootPath: /app/storage
      Email__Smtp__Host: ${SMTP_HOST:-}
      Email__Smtp__Port: ${SMTP_PORT:-587}
      Email__Smtp__Username: ${SMTP_USERNAME:-}
      Email__Smtp__Password: ${SMTP_PASSWORD:-}
      Email__Smtp__DefaultFrom: ${SMTP_FROM:-}
    depends_on:
      api:
        condition: service_healthy
    volumes:
      - techstrap-storage:/app/storage
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/ready"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

networks:
  default:
    ipam:
      config:
        # Product-unique pinned subnet (D-019), registered in _template CLIENT_IP_RATE_LIMITING.md.
        # Override TECHSTRAP_SUBNET only if another network on this host already uses it.
        - subnet: ${TECHSTRAP_SUBNET:-172.16.31.0/24}

volumes:
  pgdata:
  techstrap-storage:
  admin-keys:
  portal-keys:
```

`docker-compose.uat.yml` is the same shape with project name `techstrap-uat`, header comments for UAT and default host ports 18080 to 18082:

```yaml
# UAT stack: same shape as production, project name techstrap-uat and default host ports 18080-18082.
# It reuses the pinned subnet, so do not run it on the same Docker host as the production stack.
#   cp .env.production.example .env.uat   # then edit (TECHSTRAP_IMAGE_TAG may be a pre-release tag)
#   docker compose --env-file .env.uat -f docker-compose.uat.yml up -d
# Required values (POSTGRES_PASSWORD, REVERSE_PROXY_CIDR, TECHSTRAP_PORTAL_PUBLIC_URL) make compose
# refuse to start when they are missing.
name: techstrap-uat

services:
  postgres:
    image: postgres:17
    restart: unless-stopped
    environment:
      POSTGRES_DB: ${POSTGRES_DB:-techstrap}
      POSTGRES_USER: ${POSTGRES_USER:-techstrap}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in .env.production}
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U $$POSTGRES_USER -d $$POSTGRES_DB"]
      interval: 10s
      timeout: 5s
      retries: 10

  api:
    image: ghcr.io/syntax-circus/techstrap-api:${TECHSTRAP_IMAGE_TAG:-latest}
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__TechStrap: Host=postgres;Port=5432;Database=${POSTGRES_DB:-techstrap};Username=${POSTGRES_USER:-techstrap};Password=${POSTGRES_PASSWORD}
      # Trust the pinned compose subnet (the Portal hop) and, when the proxy runs outside it, the
      # proxy address. REVERSE_PROXY_CIDR is required, so entry 1 is always the proxy.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${TECHSTRAP_SUBNET:-172.16.31.0/24}
      TRUSTEDPROXY__TRUSTEDNETWORKS__1: ${REVERSE_PROXY_CIDR:?set REVERSE_PROXY_CIDR in .env.production}
      TECHSTRAP_PORTAL_PUBLIC_URL: ${TECHSTRAP_PORTAL_PUBLIC_URL:?set TECHSTRAP_PORTAL_PUBLIC_URL in .env.production}
      TECHSTRAP_AGENT_GROUP: ${TECHSTRAP_AGENT_GROUP:-techstrap-agents}
      TECHSTRAP_ADMIN_GROUP: ${TECHSTRAP_ADMIN_GROUP:-techstrap-admins}
      TECHSTRAP_BOOTSTRAP_ADMIN: ${TECHSTRAP_BOOTSTRAP_ADMIN:-}
      Authentication__JwtBearer__Authority: ${OIDC_AUTHORITY:-}
      Authentication__JwtBearer__Audiences__0: ${OIDC_AUDIENCE:-}
      Storage__Provider: Local
      Storage__Local__RootPath: /app/storage
    depends_on:
      postgres:
        condition: service_healthy
    volumes:
      - techstrap-storage:/app/storage
    ports:
      - "127.0.0.1:${API_PORT:-18080}:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/ready"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

  admin:
    image: ghcr.io/syntax-circus/techstrap-admin:${TECHSTRAP_IMAGE_TAG:-latest}
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Api__BaseUrl: http://api/
      # Admin trusts only the reverse proxy.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${REVERSE_PROXY_CIDR:?set REVERSE_PROXY_CIDR in .env.production}
      DataProtection__KeyRingPath: /app/dataprotection-keys
      Auth__Authority: ${OIDC_AUTHORITY:-}
      Auth__ClientId: ${OIDC_ADMIN_CLIENT_ID:-}
      Auth__ClientSecret: ${OIDC_ADMIN_CLIENT_SECRET:-}
      TECHSTRAP_ADMIN_GROUP: ${TECHSTRAP_ADMIN_GROUP:-techstrap-admins}
    depends_on:
      api:
        condition: service_healthy
    volumes:
      - admin-keys:/app/dataprotection-keys
    ports:
      - "127.0.0.1:${ADMIN_PORT:-18081}:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/live"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

  portal:
    image: ghcr.io/syntax-circus/techstrap-portal:${TECHSTRAP_IMAGE_TAG:-latest}
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      Api__BaseUrl: http://api/
      # Portal trusts only the reverse proxy.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${REVERSE_PROXY_CIDR:?set REVERSE_PROXY_CIDR in .env.production}
      TECHSTRAP_PORTAL_PUBLIC_URL: ${TECHSTRAP_PORTAL_PUBLIC_URL:?set TECHSTRAP_PORTAL_PUBLIC_URL in .env.production}
      DataProtection__KeyRingPath: /app/dataprotection-keys
    depends_on:
      api:
        condition: service_healthy
    volumes:
      - portal-keys:/app/dataprotection-keys
    ports:
      - "127.0.0.1:${PORTAL_PORT:-18082}:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/live"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

  worker:
    image: ghcr.io/syntax-circus/techstrap-worker:${TECHSTRAP_IMAGE_TAG:-latest}
    restart: unless-stopped
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__TechStrap: Host=postgres;Port=5432;Database=${POSTGRES_DB:-techstrap};Username=${POSTGRES_USER:-techstrap};Password=${POSTGRES_PASSWORD}
      TECHSTRAP_PORTAL_PUBLIC_URL: ${TECHSTRAP_PORTAL_PUBLIC_URL:?set TECHSTRAP_PORTAL_PUBLIC_URL in .env.production}
      Storage__Provider: Local
      Storage__Local__RootPath: /app/storage
      Email__Smtp__Host: ${SMTP_HOST:-}
      Email__Smtp__Port: ${SMTP_PORT:-587}
      Email__Smtp__Username: ${SMTP_USERNAME:-}
      Email__Smtp__Password: ${SMTP_PASSWORD:-}
      Email__Smtp__DefaultFrom: ${SMTP_FROM:-}
    depends_on:
      api:
        condition: service_healthy
    volumes:
      - techstrap-storage:/app/storage
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/ready"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

networks:
  default:
    ipam:
      config:
        # Product-unique pinned subnet (D-019), registered in _template CLIENT_IP_RATE_LIMITING.md.
        # Override TECHSTRAP_SUBNET only if another network on this host already uses it.
        - subnet: ${TECHSTRAP_SUBNET:-172.16.31.0/24}

volumes:
  pgdata:
  techstrap-storage:
  admin-keys:
  portal-keys:
```

`.env.production.example` (copied to `.env.production` or `.env.uat` on the host and passed with `--env-file`):

```dotenv
# Copy to .env.production on the deployment host and pass it to compose; never commit the copy:
#   docker compose --env-file .env.production -f docker-compose.production.yml up -d
# Compose refuses to start while a required value below is missing.

# --- Images ---
# Release tag to run (SemVer without the leading v, for example 0.1.0). "latest" follows the newest release.
TECHSTRAP_IMAGE_TAG=latest

# --- Database (required) ---
POSTGRES_DB=techstrap
POSTGRES_USER=techstrap
POSTGRES_PASSWORD=replace-with-a-long-random-password

# --- Network and reverse proxy ---
# Pinned compose subnet (D-019). Change only if another network on this host already uses it,
# and update the _template CLIENT_IP_RATE_LIMITING.md subnet registry to match.
TECHSTRAP_SUBNET=172.16.31.0/24
# Address or CIDR of the reverse proxy (Caddy) that forwards client IPs (required). Admin and Portal
# trust only this; the API trusts it in addition to the compose subnet.
# Never use 172.16.0.0/12 or 0.0.0.0/0.
REVERSE_PROXY_CIDR=192.168.1.55/32
# Loopback ports the reverse proxy forwards to.
API_PORT=8080
ADMIN_PORT=8081
PORTAL_PORT=8082

# --- Public URL (required) ---
# Base URL of the customer portal as customers see it (used in emailed links and canonical URLs).
TECHSTRAP_PORTAL_PUBLIC_URL=https://support.example.com

# --- Agent sign-in (OIDC; used from PHASE-04 and PHASE-07) ---
OIDC_AUTHORITY=
OIDC_AUDIENCE=
OIDC_ADMIN_CLIENT_ID=
OIDC_ADMIN_CLIENT_SECRET=
TECHSTRAP_AGENT_GROUP=techstrap-agents
TECHSTRAP_ADMIN_GROUP=techstrap-admins
# Email of the first administrator; promoted on first sign-in.
TECHSTRAP_BOOTSTRAP_ADMIN=

# --- Outbound email (used from PHASE-05) ---
SMTP_HOST=
SMTP_PORT=587
SMTP_USERNAME=
SMTP_PASSWORD=
SMTP_FROM=support@example.com
```

- [ ] **Step 5: Run the Pester tests to verify they pass**

```bash
pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeFiles.Tests.ps1
```

Expected: `Tests Passed: 7, Failed: 0`.

- [ ] **Step 6: Check the resolved configuration by hand**

```bash
docker compose config
docker compose -f docker-compose.production.yml config
```

Expected: the first prints the resolved local model containing `subnet: 172.16.31.0/24` and a `TRUSTEDPROXY__TRUSTEDNETWORKS__0` line per host (`172.16.31.0/24` for api, `192.0.2.0/24` placeholders for admin and portal). The second fails with `required variable POSTGRES_PASSWORD is missing a value: set POSTGRES_PASSWORD in .env.production`.

- [ ] **Step 7: Bring the stack up and verify it is healthy**

If compose reports `Pool overlaps with other one on this address space`, another Docker network already uses `172.16.31.0/24`; list them with `docker network ls` and `docker network inspect`, then choose a free subnet for this run with `TECHSTRAP_SUBNET` (PowerShell: `$env:TECHSTRAP_SUBNET = "10.245.31.0/24"`; Git Bash: `export TECHSTRAP_SUBNET=10.245.31.0/24`).

```bash
docker compose up -d --build --wait --wait-timeout 300
docker compose ps
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8080/health/live
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8080/health/ready
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8081/health/live
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8082/health/live
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8083/health/live
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8083/health/ready
docker compose exec postgres psql -U techstrap -d techstrap -tc 'select "MigrationId" from "__EFMigrationsHistory"'
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8081/
docker compose exec admin sh -c 'ls /app/dataprotection-keys'
docker compose down -v
```

Expected: `docker compose up --wait` returns with five services `healthy`; all six `curl` lines print `200`; the query prints exactly one row ending in `_Initial`; the admin page request prints `200` and the admin key ring then lists a `key-*.xml` file (data protection writes it on first use); `down -v` removes the containers, network and volumes.

- [ ] **Step 8: Record the owner actions**

These cannot be done from this repository; leave the boxes for the owner and mention them in the PR description (Task 16):

- [ ] Owner: open a PR in `_template` adding this row to the subnet registry table in `docs/patterns/CLIENT_IP_RATE_LIMITING.md`: `| techstrap | \`172.16.31.0/24\` | Foundation compose files; see its \`docker-compose.yml\` |`.
- [ ] Owner: confirm `172.16.31.0/24` is free on the UAT host (`docker network ls` plus `docker network inspect`); if not, set `TECHSTRAP_SUBNET` in that host's `.env.uat` and add the real subnet to the registry row.

- [ ] **Step 9: Commit**

```bash
git add docker-compose.yml docker-compose.uat.yml docker-compose.production.yml .env.production.example scripts/tests/ComposeFiles.Tests.ps1
git commit -m "build: add the local, UAT and production compose files with the pinned subnet" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 14: GitHub Actions `ci.yml` and `release.yml`

Maps to P01-T17 and P01-T18. Deliverable: CI on pull requests and pushes to `main` (restore, build, test with Testcontainers, pending-model-changes check, script tests, then an amd64-only Docker build without push), and a release workflow on `v*` tags that builds and pushes `linux/amd64` and `linux/arm64` images to `ghcr.io/syntax-circus`.

**Files:**
- Create: `.github/workflows/ci.yml`, `.github/workflows/release.yml`

**Interfaces:**
- Consumes: `TechStrap.CI.slnf` (Task 2), `.config/dotnet-tools.json` (Task 1), `scripts/Invoke-ScriptTests.ps1` (Task 1), `Build-TechStrapDocker.ps1` (Task 12). The CI job needs full git history (`fetch-depth: 0`) for GitVersion and the build-script tests.
- Produces: workflow `CI` (jobs `build-test` and `docker-build`) and workflow `Release`. Release derives the image version from the tag name (`v0.1.0-rc.1` becomes `0.1.0-rc.1`) and passes it as both `-ImageTag` and `-SemVerTag`, so the published tags are the SemVer and `latest`. The workflow authenticates to GHCR with `GITHUB_TOKEN` (`permissions: packages: write`).

There is no unit test for a workflow file; the checks are a linter now and the real runs in Task 16 and after merge.

- [ ] **Step 1: Write the CI workflow**

`dotnet test --solution` runs Testcontainers against the runner's Docker daemon. The PR image build uses `-Platforms linux/amd64`, a throwaway SemVer tag and `-PushLatest:$false`, and never pushes:

```yaml
name: CI

on:
  pull_request:
  push:
    branches: [main]

concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true

permissions:
  contents: read

jobs:
  build-test:
    name: Build and test
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0 # GitVersion needs full history

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Restore tools and packages
        run: |
          dotnet tool restore
          dotnet restore TechStrap.CI.slnf

      - name: Build
        run: dotnet build TechStrap.CI.slnf -c Release --no-restore

      # Testcontainers uses the runner's Docker daemon.
      - name: Test
        run: dotnet test --solution TechStrap.CI.slnf -c Release --no-build

      - name: Check for pending EF model changes
        run: dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build

      - name: Install Pester
        shell: pwsh
        run: Install-Module Pester -RequiredVersion 6.2.0 -Scope CurrentUser -Force -SkipPublisherCheck

      - name: Script tests (package versions, build script, compose files, Dockerfiles, docs)
        shell: pwsh
        run: ./scripts/Invoke-ScriptTests.ps1

  docker-build:
    name: Docker build (amd64, no push)
    runs-on: ubuntu-latest
    needs: build-test
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: docker/setup-buildx-action@v3

      - name: Build the four images
        shell: pwsh
        run: ./Build-TechStrapDocker.ps1 -Platforms linux/amd64 -ImageTag "0.0.0-ci.${{ github.run_number }}" -PushLatest:$false
```

- [ ] **Step 2: Write the release workflow**

```yaml
name: Release

# Pushing a tag such as v0.1.0 or v0.1.0-rc.1 publishes multi-arch images to GHCR.
on:
  push:
    tags: ['v*']

permissions:
  contents: read
  packages: write

jobs:
  images:
    name: Build and push images (amd64 + arm64)
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: docker/setup-qemu-action@v3

      - uses: docker/setup-buildx-action@v3

      - name: Log in to GHCR
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}

      - name: Build and push the four images
        shell: pwsh
        run: |
          $version = "${{ github.ref_name }}".TrimStart('v')
          ./Build-TechStrapDocker.ps1 -Push -Registry ghcr.io/syntax-circus -ImageTag $version -SemVerTag $version
```

- [ ] **Step 3: Lint the workflows**

`actionlint` validates the YAML, the expressions and the shell snippets:

```bash
docker run --rm -v "${PWD}:/repo" -w /repo rhysd/actionlint:latest
```

(Git Bash on Windows: prefix with `MSYS_NO_PATHCONV=1` and use `"$(pwd -W):/repo"` for the volume; PowerShell: `-v "${PWD}:/repo"` works as written.) Expected: no output and exit code 0. Introducing a YAML error (for example a stray `bad: [` line in a scratch file under `.github/workflows/`) makes it print `could not parse as YAML`; remove the scratch file afterwards.

- [ ] **Step 4: Run the CI commands locally**

These are the same commands the `build-test` job runs, so a green local run predicts a green job:

```bash
dotnet tool restore
dotnet restore TechStrap.CI.slnf
dotnet build TechStrap.CI.slnf -c Release --no-restore
dotnet test --solution TechStrap.CI.slnf -c Release --no-build
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build
pwsh ./scripts/Invoke-ScriptTests.ps1
```

Expected: the build succeeds with no warnings; the test run prints `Test run summary: Passed!`; the EF check prints `No changes have been made to the model since the last migration.`; Pester prints `Tests Passed: 59, Failed: 0` (6 package-version, 21 build-script, 7 compose and 25 Dockerfile tests; the 6 documentation tests arrive in Task 15, which makes the total 65).

- [ ] **Step 5: Dry-run the two image commands the workflows use**

Run these in PowerShell (Git Bash would expand `$false` before the script sees it):

```powershell
pwsh ./Build-TechStrapDocker.ps1 -DryRun -Platforms linux/amd64 -ImageTag 0.0.0-ci.7 -PushLatest:$false
pwsh ./Build-TechStrapDocker.ps1 -DryRun -Push -Registry ghcr.io/syntax-circus -ImageTag 0.1.0-rc.1 -SemVerTag 0.1.0-rc.1
```

Expected: the first prints four `--load` commands for `linux/amd64` tagged `techstrap-<host>:0.0.0-ci.7` (and the `-amd64` variant), no `latest`. The second prints four `--push` commands with `--platform linux/amd64,linux/arm64` and tags `ghcr.io/syntax-circus/techstrap-<host>:0.1.0-rc.1` and `:latest`.

- [ ] **Step 6: Commit**

```bash
git add .github
git commit -m "ci: build and test on pull requests and publish multi-arch images on version tags" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 15: `LICENSE`, `README.md`, `CONTRIBUTING.md` and `SECURITY.md`

Maps to P01-T19. Deliverable: the MIT license, the extended README (logo, status, compose quick start; all existing content kept), the contributor guide and the security policy using GitHub private vulnerability reporting (no email address).

**Files:**
- Create: `LICENSE`, `CONTRIBUTING.md`, `SECURITY.md`
- Modify: `README.md` (four edits; do not overwrite the file)
- Create (test): `scripts/tests/RepositoryDocs.Tests.ps1`

**Interfaces:**
- Consumes: `assets/brand/logo-512.png` (owner-supplied, committed by the coordinator; the README shows it centered at 200 px), the compose files (Task 13) for the quick start commands and ports, the scripts and commands from earlier tasks for CONTRIBUTING.
- Produces: files GitHub recognizes (license detection, the Security tab policy). The README quick-start commands are the ones verified in Task 13 Step 7.

- [ ] **Step 1: Write the failing Pester test**

It checks the license text, that `SECURITY.md` names GitHub private vulnerability reporting and contains no email address, the CONTRIBUTING topics, that the README starts with the centered 200 px logo, keeps its original sections, gains `## Quick start`, and links only to files that exist:

```powershell
BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

    function Get-RepoText {
        param([string]$RelativePath)
        return Get-Content -LiteralPath (Join-Path $script:RepoRoot $RelativePath) -Raw
    }
}

Describe 'open source files' {
    It 'LICENSE is the MIT license for Syntax Circus' {
        $text = Get-RepoText 'LICENSE'
        $text | Should -Match '^MIT License'
        $text | Should -Match 'Copyright \(c\) 2026 Syntax Circus'
    }

    It 'SECURITY.md uses GitHub private vulnerability reporting, with supported versions and scope' {
        $text = Get-RepoText 'SECURITY.md'
        $text | Should -Match 'Report a vulnerability'
        $text | Should -Match '## Supported versions'
        $text | Should -Match '## Scope'
        $text | Should -Not -Match '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[a-z]{2,}'
    }

    It 'CONTRIBUTING.md covers Conventional Commits, test-first and EF-tool-only migrations' {
        $text = Get-RepoText 'CONTRIBUTING.md'
        $text | Should -Match 'Conventional Commits'
        $text | Should -Match 'Test first'
        $text | Should -Match 'dotnet ef migrations add'
        $text | Should -Match 'Never write or edit a migration by hand'
    }
}

Describe 'README.md' {
    It 'shows the logo at the top, centered, 200 pixels wide' {
        $text = Get-RepoText 'README.md'
        $text | Should -Match '(?s)^<p align="center">\s*<img src="assets/brand/logo-512\.png"[^>]*width="200"'
    }

    It 'keeps the original sections and adds the compose quick start' {
        $text = Get-RepoText 'README.md'
        foreach ($heading in '## What it is', '## Tech stack', '## Documentation', '## License', '## Quick start') {
            $text | Should -Match ([regex]::Escape($heading))
        }
        $text | Should -Match 'docker compose up -d --build'
        $text | Should -Match 'docs/architecture/00-DISCOVERY-INDEX\.md'
    }

    It 'links only to files that exist' {
        $text = Get-RepoText 'README.md'
        $links = [regex]::Matches($text, '\]\((?<path>(?!https?:|#)[^)\s]+)\)') | ForEach-Object { $_.Groups['path'].Value }
        foreach ($link in $links) {
            Test-Path -LiteralPath (Join-Path $script:RepoRoot $link) | Should -BeTrue -Because "README links to $link"
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: FAIL (`LICENSE`, `SECURITY.md` and `CONTRIBUTING.md` do not exist; the README has no logo or quick start yet).

- [ ] **Step 3: Write `LICENSE`, `SECURITY.md` and `CONTRIBUTING.md`**

`LICENSE` (MIT, Syntax Circus):

```text
MIT License

Copyright (c) 2026 Syntax Circus

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

`SECURITY.md`:

```markdown
# Security policy

## Reporting a vulnerability

Please report security problems privately through GitHub:

1. Open the repository's **Security** tab.
2. Choose **Report a vulnerability** (GitHub private vulnerability reporting).
3. Describe the issue, the affected version and how to reproduce it.

Do not open a public issue or pull request for a suspected vulnerability. You will get an acknowledgement
within a few days. Fixes are developed in a private advisory and released together with the advisory.

## Supported versions

TechStrap is pre-1.0. Until v1.0.0 only the latest release and the `main` branch receive security fixes.
After v1.0.0 this section will list the supported release lines.

## Scope

In scope:

- The API, Admin app, customer Portal and Worker in this repository, and the container images published
  to `ghcr.io/syntax-circus`.
- The Docker Compose files and example configuration shipped here, where they lead to an insecure
  default.
- The `TechStrap.Contracts`, `TechStrap.Client` and `TechStrap.Client.Maui` packages once published.

Out of scope:

- Vulnerabilities in third-party dependencies that are already public and fixed upstream (please report
  those upstream; tell us if a version bump is needed).
- Problems that need a misconfigured deployment, such as trusting `0.0.0.0/0` as a proxy network or
  exposing Postgres to the internet.
- Denial of service through volumetric traffic against a deployment without a reverse proxy or rate
  limits in front of it.
- Findings from automated scanners without a demonstrated impact.
```

`CONTRIBUTING.md`:

````markdown
# Contributing to TechStrap

Thanks for helping. This file is the short version of how work lands in this repository.

## Before you start

- Read the [architecture set](docs/architecture/00-DISCOVERY-INDEX.md). Work is organized in phases
  ([roadmap](docs/architecture/99-IMPLEMENTATION-ROADMAP.md)); pick a task that belongs to the phase being built.
- Open an issue before a large change so the approach can be agreed first.

## Prerequisites

- .NET SDK 10 (see `global.json`), Docker with buildx, PowerShell 7 (for the scripts).
- `dotnet tool restore` installs the pinned `dotnet-ef` tool.

## Workflow

```bash
git checkout -b feat/short-description
dotnet build TechStrap.slnx
dotnet test --solution TechStrap.slnx      # needs Docker running (Testcontainers starts Postgres 17)
```

### Commits

Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `test:`, `docs:`,
`refactor:`, `build:`, `ci:`, `chore:`. One logical change per commit; the subject says what changed.

### Test first

Write the failing test, watch it fail for the right reason, then write the code that makes it pass.
Handler unit tests use substitutes and no database; infrastructure behaviour is tested against real
Postgres through `PostgresFixture` in `TechStrap.Infrastructure.IntegrationTests`.

### Migrations come from the EF tool only

Never write or edit a migration by hand. Change the model, then generate:

```bash
dotnet ef migrations add <Name> --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api
```

A pull request that edits generated migration code without regenerating it will be asked to redo it.

### Package versions

Versions live only in `Directory.Packages.props` and must match
[the package map](docs/architecture/03-PACKAGE-MAP.md). Project files never carry a `Version`.
`pwsh scripts/Check-PackageVersions.ps1` checks both rules and runs in CI.

### Architecture rules

`TechStrap.Architecture.Tests` enforces the project reference direction and the handler rules (sealed
handlers with an `I...Handler` interface, no EF, HTTP or concrete infrastructure in handler
constructors, controllers bind handlers with `[FromServices]`). If a rule blocks you, the change needs a
decision in [the decision log](docs/architecture/04-DECISION-LOG.md), not a weaker test.

### Style

- Warnings are errors. Keep the build clean.
- Repeated or meaningful literals are named constants; no magic strings for settings keys or paths.
- Compiled CSS is never committed (`wwwroot/css/app.css` is generated by the build).
- Secrets never go in source. Add new settings to the matching `.env.example`; `.env.local` stays local.

## Pull requests

Describe the change, link the task or issue, and paste the output of the commands you ran. CI must be green
(build, tests, script tests, Docker build).

## Security

Do not report vulnerabilities in public issues. See [SECURITY.md](SECURITY.md).
````

- [ ] **Step 4: Extend `README.md` with four edits**

Keep every existing section. Make these four replacements in order.

Edit 1. Put the logo above the title. Old text:

```text
# TechStrap

**Support for Technical Support.**
```

New text:

```text
<p align="center">
  <img src="assets/brand/logo-512.png" alt="TechStrap logo" width="200">
</p>

# TechStrap

**Support for Technical Support.**
```

Edit 2. Replace the status line. Old text:

```text
> **Status:** planning. The architecture and phased implementation plan are written; no application code exists yet.
```

New text:

```text
> **Status:** early development. PHASE-01 (the foundation) is in place: the solution skeleton, health endpoints, database migrations, Docker images, the compose stack and CI. Product features arrive in later phases; see the [roadmap](docs/architecture/99-IMPLEMENTATION-ROADMAP.md).
```

Edit 3. Insert the quick start before the Documentation section. Old text:

```text
## Documentation

Start at the
```

New text (the quick start block, then the original heading and first words):

````markdown
## Quick start

You need Docker with Compose v2. The local stack builds the four images from this checkout and starts Postgres 17.

```bash
git clone https://github.com/Syntax-Circus/techstrap.git
cd techstrap
docker compose up -d --build
docker compose ps
```

When every service shows `healthy`, the hosts answer on loopback:

| Service | URL | Health checks |
| --- | --- | --- |
| API | <http://127.0.0.1:8080> | `/health/live`, `/health/ready` (needs Postgres), `/openapi/v1.json` |
| Admin | <http://127.0.0.1:8081> | `/health/live` |
| Portal | <http://127.0.0.1:8082> | `/health/live` |
| Worker | <http://127.0.0.1:8083> | `/health/live`, `/health/ready` (needs Postgres) |

```bash
curl http://127.0.0.1:8080/health/ready
```

The API applies the database migrations on startup. Stop the stack with `docker compose down` (add `-v` to delete the data).

Troubleshooting: if compose reports `Pool overlaps with other one on this address space`, another Docker network already uses
the pinned subnet `172.16.31.0/24`. Pick a free one for this stack, for example `TECHSTRAP_SUBNET=10.245.31.0/24 docker compose up -d --build`.

### Develop

```bash
dotnet tool restore
dotnet build TechStrap.slnx
dotnet test --solution TechStrap.slnx   # needs Docker running; Testcontainers starts Postgres 17
```

Per-host settings for `dotnet run` go in `src/TechStrap.<Host>/.env.local` (gitignored; copy the `.env.example` next to it).
See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow and [SECURITY.md](SECURITY.md) to report a vulnerability.
````

followed by the original text:

```text
## Documentation

Start at the
```

Edit 4. Replace the license line. Old text:

```text
MIT. A `LICENSE` file is added in [PHASE-01](docs/architecture/PHASE-01-foundation.md).
```

New text:

```text
[MIT](LICENSE).
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
pwsh ./scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: `Tests Passed: 6, Failed: 0`. The test fails on a README link to a missing file, so every relative link must exist.

- [ ] **Step 6: Enable private vulnerability reporting (owner step)**

`SECURITY.md` points reporters at GitHub's private vulnerability reporting, which is off by default. This is a repository setting that cannot be changed from the code:

- [ ] Owner: in `github.com/Syntax-Circus/techstrap`, open **Settings**, then **Code security** (or **Advanced Security**), and enable **Private vulnerability reporting**. Afterwards the **Security** tab shows **Report a vulnerability** and lists `SECURITY.md` as the policy.

- [ ] **Step 7: Commit**

```bash
git add LICENSE README.md CONTRIBUTING.md SECURITY.md scripts/tests/RepositoryDocs.Tests.ps1
git commit -m "docs: add the MIT license, security policy, contributing guide and compose quick start" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 16: Clean-clone verification, tick PHASE-01 and open the PR

Maps to P01-T20. Deliverable: proof that every Success Criteria item holds from a clean clone, PHASE-01 marked complete in the architecture documents, and a pull request with the command output in its description.

**Files:**
- Modify: `docs/architecture/PHASE-01-foundation.md` (tick the boxes, three risk notes), `docs/architecture/00-DISCOVERY-INDEX.md` (phase table gains a `Status` column), `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` (PHASE-01 status)

**Interfaces:**
- Consumes: everything from Tasks 1 to 15, committed on `feat/phase-01-foundation`. The brand assets under `assets/brand/` must be committed too (the coordinator does that).
- Produces: a green clean-clone run, the ticked documents, and a PR against `main`.

- [ ] **Step 1: Confirm everything is committed**

```bash
git status --short
git log --oneline main..HEAD
```

Expected: `git status --short` prints nothing; the log lists the commits from Tasks 1 to 15 (plus the coordinator's brand-asset commit).

- [ ] **Step 2: Clone the branch into a clean directory**

The clone contains only committed files, which is the point. Run the remaining verification steps inside it.

```powershell
git clone --branch feat/phase-01-foundation (Get-Location).Path ../techstrap-verify
Set-Location ../techstrap-verify
```

Git Bash: `git clone --branch feat/phase-01-foundation "$(pwd)" ../techstrap-verify && cd ../techstrap-verify`.

- [ ] **Step 3: Build, test and check the scripts**

Docker must be running.

```bash
dotnet tool restore
dotnet build TechStrap.slnx -c Release
dotnet test --solution TechStrap.slnx -c Release
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api
pwsh ./scripts/Check-PackageVersions.ps1
pwsh ./scripts/Invoke-ScriptTests.ps1
git ls-files "*/wwwroot/css/*"
```

Expected, in order: the build succeeds with `0 Warning(s)` and `0 Error(s)`; the test run prints `Test run summary: Passed!` with `total: 100` (30 architecture, 9 integration, 59 Api, 1 Domain, 1 Application) including `ProjectReferenceDirectionTests`, `HandlerConstructorDependencyTests` and `MigrationStartupTests`; the EF check prints `No changes have been made to the model since the last migration.`; the package check prints `Package version check passed: 41 packages match the package map.`; Pester prints `Tests Passed: 65, Failed: 0`; the last command prints nothing.

- [ ] **Step 4: Bring the stack up and check health and the migration history**

If compose reports `Pool overlaps with other one on this address space`, set `TECHSTRAP_SUBNET` to a free subnet first (see Task 13 Step 7).

```bash
docker compose config
docker compose up -d --build --wait --wait-timeout 300
docker compose ps
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8080/health/live
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8080/health/ready
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8081/health/live
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8082/health/live
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8083/health/live
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:8083/health/ready
docker compose exec postgres psql -U techstrap -d techstrap -tc 'select "MigrationId" from "__EFMigrationsHistory"'
docker compose down -v
```

Expected: `docker compose config` shows `subnet: 172.16.31.0/24` (or your override) and `TRUSTEDPROXY__TRUSTEDNETWORKS__0` for api, admin and portal; five services `healthy`; six `200` lines; exactly one history row, `<timestamp>_Initial`.

- [ ] **Step 5: Build the images with the script**

```bash
pwsh ./Build-TechStrapDocker.ps1 -Platforms linux/amd64
docker images --format "{{.Repository}}:{{.Tag}}" | grep techstrap-
```

Expected: the four images `techstrap-api`, `techstrap-admin`, `techstrap-portal` and `techstrap-worker` exist with `<semver>`, `latest` and `-amd64` tags. (PowerShell: pipe to `Select-String techstrap-` instead of `grep`.) Leave the verification clone afterwards: `cd` back to the real checkout.

- [ ] **Step 6: Mark PHASE-01 complete in the documents**

In `docs/architecture/PHASE-01-foundation.md`, tick every box from `## Deliverables` through `## Boundary Validation` (the `## Risks and Open Questions` boxes stay as they are, except the three edited below):

```bash
sed -i '/^## Deliverables/,/^## Risks and Open Questions/ s/^\( *\)- \[ \]/\1- [x]/' docs/architecture/PHASE-01-foundation.md
```

Then edit three lines in the Risks section. Old, then new:

```text
- [ ] xunit.v3 pin depends on the owner's NCrunch version; confirm before locking (AGENT_GUIDE known constraints).
```

```text
- [x] xunit.v3 pin depends on the owner's NCrunch version; confirm before locking (AGENT_GUIDE known constraints). Resolved 2026-10-02: the owner's NCrunch 5.23 runs the 4.0.x pair; pinned in `Directory.Packages.props`.
```

```text
- [ ] Security reporting address for `SECURITY.md` is not yet chosen.
```

```text
- [x] Security reporting address for `SECURITY.md` is not yet chosen. Resolved 2026-10-02: GitHub private vulnerability reporting, no email address (the owner enables it in repository settings).
```

```text
- [ ] Client.Maui needs MAUI workloads and may break or slow CI; mitigated by the solution filter, install deferred to PHASE-11.
```

```text
- [x] Client.Maui needs MAUI workloads and may break or slow CI; mitigated by the solution filter (`TechStrap.CI.slnf`) and a plain `net10.0` placeholder project, install deferred to PHASE-11.
```

In `docs/architecture/00-DISCOVERY-INDEX.md`, give the phase table a `Status` column. Replace this block:

```text
| # | Phase | Depends on | Unblocks |
| --- | --- | --- | --- |
| 01 | [Foundation](PHASE-01-foundation.md) | — | all |
| 02 | [Brand & UX](PHASE-02-brand-and-ux.md) | 01 | 07, 09 |
| 03 | [Domain & persistence](PHASE-03-domain-and-persistence.md) | 01 | 04 |
| 04 | [Agent auth & admin config](PHASE-04-agent-auth-and-admin-config.md) | 03 | 05 |
| 05 | [Intake, email & worker](PHASE-05-intake-email-worker.md) | 04 | 06, 11 |
| 06 | [Ticket operations](PHASE-06-ticket-operations.md) | 05 | 07, 08, 09 |
| 07 | [Admin app](PHASE-07-admin-app.md) | 02, 06 | 08 (editor UI), 10 |
| 08 | [Knowledge base](PHASE-08-knowledge-base.md) | 06 (07 for the editor UI) | 09 |
| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 |
| 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 |
| 11 | [Client SDK](PHASE-11-client-sdk.md) | 05 | 12 |
| 12 | [Release hardening](PHASE-12-release-hardening.md) | all | v1.0.0 |
```

with:

```text
| # | Phase | Depends on | Unblocks | Status |
| --- | --- | --- | --- | --- |
| 01 | [Foundation](PHASE-01-foundation.md) | — | all | Complete |
| 02 | [Brand & UX](PHASE-02-brand-and-ux.md) | 01 | 07, 09 | Not started |
| 03 | [Domain & persistence](PHASE-03-domain-and-persistence.md) | 01 | 04 | Not started |
| 04 | [Agent auth & admin config](PHASE-04-agent-auth-and-admin-config.md) | 03 | 05 | Not started |
| 05 | [Intake, email & worker](PHASE-05-intake-email-worker.md) | 04 | 06, 11 | Not started |
| 06 | [Ticket operations](PHASE-06-ticket-operations.md) | 05 | 07, 08, 09 | Not started |
| 07 | [Admin app](PHASE-07-admin-app.md) | 02, 06 | 08 (editor UI), 10 | Not started |
| 08 | [Knowledge base](PHASE-08-knowledge-base.md) | 06 (07 for the editor UI) | 09 | Not started |
| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | Not started |
| 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | Not started |
| 11 | [Client SDK](PHASE-11-client-sdk.md) | 05 | 12 | Not started |
| 12 | [Release hardening](PHASE-12-release-hardening.md) | all | v1.0.0 | Not started |
```

In `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, change the PHASE-01 row's last cell from `Not started` to `Complete`.

```bash
git diff --stat
```

Expected: three files changed; `git diff docs/architecture/PHASE-01-foundation.md` shows `[ ]` changing to `[x]` only from Deliverables through Boundary Validation and on the three edited risk lines.

- [ ] **Step 7: Commit the documentation update**

```bash
git add docs/architecture
git commit -m "docs: mark PHASE-01 complete" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

- [ ] **Step 8: Push and open the pull request**

Write the PR description to a scratch file outside the repo (for example `../pr-body.md`) with these sections, pasting the real final lines of each command from Steps 3 to 5 under "Verification":

1. `## Summary`: PHASE-01 foundation, 16 plan tasks, 15 projects, four hosts, compose stack, CI.
2. `## Verification`: one fenced block per command in Steps 3 to 5 with its real output (build summary, `total: 100`, the EF check, the package check, Pester total, empty `git ls-files`, the six `200` lines and the migration history row).
3. `## Success criteria`: the ten items from the spec's Success Criteria, each ticked with the command that proves it.
4. `## Deviations and notes`: the decisions listed at the top of the plan (Client.Maui placeholder, `SyntaxCircus.Common` 0.1.3, `TECHSTRAP_SUBNET` override, OpenAPI carries the public rate limit, placeholder authentication scheme, Worker smoke test via `WebApplicationFactory`).
5. `## Owner actions`: enable GitHub private vulnerability reporting; open the `_template` PR adding the `172.16.31.0/24` registry row; confirm the subnet is free on the UAT host; confirm the GHCR package visibility after the first release.

End the body with these two lines, exactly:

```text
🤖 Generated with [Claude Code](https://claude.com/claude-code)

https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

Then:

```bash
git push -u origin feat/phase-01-foundation
gh pr create --base main --head feat/phase-01-foundation --title "feat: PHASE-01 foundation" --body-file ../pr-body.md
```

Expected: `gh` prints the new PR URL.

- [ ] **Step 9: Watch CI and prove it can turn red**

```bash
gh pr checks --watch
```

Expected: `Build and test` and `Docker build (amd64, no push)` both pass. Then prove an architecture failure turns CI red on a throwaway branch:

```bash
git switch -c chore/ci-red-check
```

Add `<ProjectReference Include="../TechStrap.Application/TechStrap.Application.csproj" />` to `src/TechStrap.Admin/TechStrap.Admin.csproj`, commit it, push the branch and open a draft PR:

```bash
git commit -am "test: deliberately break the reference direction" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
git push -u origin chore/ci-red-check
gh pr create --draft --base feat/phase-01-foundation --head chore/ci-red-check --title "throwaway: CI red check" --body "Proves ProjectReferenceDirectionTests fails CI. Close without merging."
gh pr checks --watch
```

Expected: `Build and test` FAILS on `ProjectReferenceDirectionTests`. Close the draft PR and delete the branch:

```bash
gh pr close chore/ci-red-check --delete-branch
git switch feat/phase-01-foundation
```

- [ ] **Step 10: Release check (only with the owner's go-ahead)**

Pushing a tag publishes images to GHCR, so do this only when the owner agrees (typically after merge):

```bash
git tag v0.1.0-rc.1
git push origin v0.1.0-rc.1
gh run watch
docker buildx imagetools inspect ghcr.io/syntax-circus/techstrap-api:0.1.0-rc.1
```

Expected: the `Release` workflow succeeds; the inspect output lists manifests for `linux/amd64` and `linux/arm64`; `techstrap-api`, `-admin`, `-portal` and `-worker` all carry the `0.1.0-rc.1` and `latest` tags. If the packages are private, the owner sets their visibility under the organization's Packages settings. Also confirm GitHub shows `MIT License` in the repository sidebar and `SECURITY.md` under the Security tab.

---

## Verification notes

How this plan was checked. The planner built the whole result in a scratch clone of this repository: every source file, test and script in this plan was compiled and run from a clean clone of that scratch branch (Windows 11, .NET SDK 10.0.401, Docker 29.8 with buildx, PowerShell 7.6, Pester 6.2.0): `dotnet build TechStrap.slnx -c Release` clean, `dotnet test --solution TechStrap.slnx -c Release` 100 of 100 passing, 65 of 65 Pester tests passing, `docker compose up -d --build --wait` with all five services healthy and the six health URLs answering 200, the `Initial` migration as the only history row, all four images built and the Admin build proven to fail without compiled CSS, `actionlint` clean on both workflows. Intermediate task states were reasoned from the final files rather than replayed, so an executor may see small differences in the "Expected: FAIL" messages of the TDD steps.

### SyntaxCircus APIs verified against the selected versions

Each call below compiled against the restored package and was exercised by a test; the source repositories in `D:\dev\SyntaxCircus\SyntaxCircus.*` were checked out at tags matching the versions (`v0.1.15` for AspNetCore.Common, `v0.1.4` for AspNetCore.Serilog, `v0.1.2` for Observability, `v0.1.3` for DotEnv and EntityFrameworkCore.Postgres and Blazor.Components, `v0.1.4` for Common):

- `SyntaxCircus.DotEnv`: `ShouldLoadDotEnv(IConfiguration, IHostEnvironment)`, `AddSyntaxCircusDotEnvFiles(IConfigurationBuilder, string)`.
- `SyntaxCircus.AspNetCore.Common`: `AddCorrelationId`/`UseCorrelationId` (`X-Correlation-Id`), `AddSecurityHeaders`/`UseSecurityHeaders`, `AddProblemDetailsExceptionHandling`/`UseProblemDetailsExceptionHandling`, `MapStandardHealthChecks()` (`/health/live` runs no checks, `/health/ready` runs checks tagged `ready`; works inside `MapGroup("").AllowAnonymous()`), `AddTrustedProxyForwardedHeaders(IConfiguration)` (eager `TrustedProxy` binding, startup filter that throws in non-Development without configuration), `TrustedProxyOptions`, `RateLimiterOptions.AddPerIpFixedWindow`/`UseProblemDetailsRejection`, `CorrelationContextAccessor.CurrentCorrelationId`, `MapRazorComponentsWithStaticAssets<T>()`.
- `SyntaxCircus.AspNetCore.Serilog`: `AddStandardSerilog(configureFileLogging, configureEnrichment)`; the request logger must be taken from DI (`Serilog.ILogger`) because the static logger is preserved.
- `SyntaxCircus.Observability`: `AddSyntaxCircusObservability`, `ConfigureSerilog`, `Options.Sentry.IsEnabled`, `LogStartupWarning`, `SyntaxCircusObservabilityOptions.FromConfiguration`, `OpenTelemetryOptions`, `SentryOptions` (its name collides with `Sentry.SentryOptions`; the tests use an alias).
- `SyntaxCircus.EntityFrameworkCore.Postgres`: `UseSyntaxCircusSnakeCaseNamingConvention()`, `MigrateWithAdvisoryLockAsync<TContext>(long, CancellationToken)`; the migrations-history table keeps the name `__EFMigrationsHistory`.
- `SyntaxCircus.Common` 0.1.3 is referenced by Application; no `Result` type is used yet.
- `SyntaxCircus.Blazor.Components` 0.1.3 is referenced by Admin and Portal and restores and publishes; none of its components is rendered in PHASE-01.

### Findings that changed the plan

- `SyntaxCircus.AspNetCore.Common` 0.1.15 depends on `SyntaxCircus.Common` exactly `[0.1.3]`; the package map's 0.1.4 fails restore (NU1107). Task 1 fixes the map.
- With a default-deny fallback policy and no authentication scheme, an unknown route threw `No authenticationScheme was specified`, and the package's exception middleware mapped that `InvalidOperationException` to a 409 `conflict` response. Task 8 adds a placeholder scheme and a test. Note for PHASE-04: unexpected exceptions map to 409 by default, so register an explicit status mapping before relying on it.
- `.NET` `WebApplicationFactory` configuration overrides are applied after `Program.cs` reads eagerly bound options, so `TrustedProxy` and the Observability sections cannot be overridden in tests (confirmed; the tests use the checked-in Development default, an environment variable in a non-parallel collection, and option-level tests).
- Razor components need `app.UseAntiforgery()`; without it the first request to the Admin and Portal shells returned 500.
- `Pester -CI` writes `testResults.xml` into the working directory; the plan uses `scripts/Invoke-ScriptTests.ps1` (`-PassThru`) instead.
- Docker on the planning machine had auto-assigned every `172.17`–`172.31` /16, which is why the owner moved the pinned subnet to `172.16.31.0/24` (outside the auto-assign pool). Re-run `docker compose up --wait` with the new default during Task 13.

### Not verified: assumptions the executor should treat as open

1. **GitHub Actions never ran.** The workflows are linted by `actionlint` and their commands were run locally, but the action versions (`actions/checkout@v4`, `actions/setup-dotnet@v4` with `dotnet-version: 10.0.x`, `docker/setup-qemu-action@v3`, `docker/setup-buildx-action@v3`, `docker/login-action@v3`) were chosen from memory and not looked up. Confirm that `setup-dotnet` serves 10.0.x on the runner and that Testcontainers works there.
2. **arm64 images and the GHCR push path were never executed.** `-Push -Registry ghcr.io/syntax-circus` is covered by the dry run and by Pester tests of the command plan; no registry was contacted and no `linux/arm64` build ran (QEMU build time, and whether the Sass compiler's native binary works under arm64, are unknown). GHCR package visibility after the first push is an owner setting.
3. **The tag-triggered release (`v0.1.0-rc.1`) was not pushed.** `docker buildx imagetools inspect` output is therefore unverified.
4. **Environment-variable names for later features** (`AUTHENTICATION__JWTBEARER__*`, `AUTH__*`, `EMAIL__SMTP__*`, `STORAGE__*`) come from the usage pages and dragon-poop's `.env.example`, not from package source; PHASE-04, PHASE-05 and PHASE-07 must confirm them when they bind them. `AddForwardedClientIp()` exists in `v0.1.15` but is not wired (no outbound client in PHASE-01).
5. **Sentry and OTLP at runtime.** The `UseSentry`/`ConfigureSentry` branch is copied from dragon-poop and compiles, but no test enables Sentry, and no collector received OTLP data; only option binding is tested.
6. **Brand assets.** The plan was verified with one-byte placeholder icons because `assets/brand/` is committed by the coordinator. The favicon test accepts `image/x-icon` or `image/vnd.microsoft.icon`; the real `.ico` is expected to be served as `image/x-icon`.
7. **NCrunch** was not run; the xunit.v3 4.0.0 pair was verified to work with Microsoft.Testing.Platform and `dotnet test`.
8. **Linux CI behaviors of Pester tests** (`docker compose config --format json`, GitVersion with `fetch-depth: 0`) were run on Windows only.
9. **The Worker has no `dotnet run` smoke script**; its health is covered by `WebApplicationFactory` and the compose health check (a deliberate deviation from P01-T07's wording).
10. **Public rate limit placement:** the `public` policy limits `/openapi/v1.json` because it is the only anonymous public endpoint; the named policies of `02-ARCHITECTURE.md` section 11.2 arrive with their endpoints.
11. **Cross-repo owner actions** (the `_template` subnet registry row, UAT host subnet check, enabling private vulnerability reporting) are recorded as unchecked boxes in Tasks 13 and 15 and in the PR description.
