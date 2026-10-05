# Scoped Per-Project Configuration and Image-Only Deployment Compose Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Before PHASE-08 (the knowledge base), make TechStrap's configuration and deployment follow the sibling-repo convention (`sinforgiver`, `who-flung-pu`). That covers:
- **Settings:** each project's `appsettings.json` lists every setting the host reads, with non-secret defaults and blank secrets; each project has its own `.env.example` and a key-only deploy template.
- **Deployment:** one image-only compose, `deploy/docker-compose.yml`, serves UAT and production. Each service loads its own scoped `.env.<app>` from a host directory, and Postgres runs as a separate instance.
- **Guards:** a config contract suite keeps the files in sync, and Production tests prove a host given only the blank template refuses to start and names what is missing.

**Architecture:**
- **One place per setting.** The `appsettings.json` of a host is the list of what it reads. `.env.example` (local) and `deploy/.env.<app>.example` (UAT and production) document the same keys as `SECTION__KEY`. `scripts/tests/ConfigContract.Tests.ps1` compares them case-insensitively, both ways.
- **Compose owns little.** The local compose keeps its own wiring in `environment:`; the deploy compose sets only the environment name, the key-ring and storage paths, the Api address and the trusted networks. Everything else comes from `.env.local` (local) or the scoped env file (deploy).
- **The deploy compose is image-only.** No `build:`, no Postgres, no Mailpit. Image references, the env directory, the loopback ports, the subnet, the proxy CIDR and the Postgres network name are compose inputs, chosen by `deploy/.env.<uat|production>.local`.
- **Fail fast stays.** Required settings are blank in the deploy templates and fail at start, by name. A blank that would break binding (a number, a flag, an enum) or would count as configured (an array element) is never written.

**Tech Stack:** .NET 10, ASP.NET Core configuration and options validation, SyntaxCircus.DotEnv, Docker Compose v2 (2.30 or newer), xUnit v3, Shouldly, `WebApplicationFactory`, and Pester 6.2.0.

**Spec:**
- `docs/architecture/02-ARCHITECTURE.md` section 11 and the roadmap carry-forward "Warn in `.env.production.example` that the Postgres password must be connection-string safe".
- D-019 (the pinned subnet), D-024 (the "Powered by" key), D-029 (group keys), D-040 and D-042 (the shared host wiring).
- The sibling repos' conventions: `sinforgiver` (key-only deploy templates with a header and per-container section headers) and `who-flung-pu` (`env_file` with `path:`, `required:` and `format: raw`, the compose-inputs file, the runbook command order); `syntax-circus-web`'s `ConfigurationContractTests` idea, implemented in Pester.
- The owner decisions of 2026-10-05, recorded as D-043 in Task 1.

### Owner decisions (2026-10-05), recorded as D-043 in Task 1
- **One deploy compose.** `deploy/docker-compose.yml` serves UAT and production. It is image-only: no `build:` and no Postgres. A small, non-secret compose-inputs file chooses the environment: the committed templates `deploy/.env.uat.example` and `deploy/.env.production.example`, copied to `deploy/.env.<env>.local` on the host. They hold the image references, the env directory, the loopback ports, the app subnet, the reverse-proxy CIDR and the Postgres network name.
- **Scoped env files on the host.** `${TECHSTRAP_ENV_DIR}` (for example `/etc/techstrap/uat/`) holds `.env.api`, `.env.worker`, `.env.admin` and `.env.portal`, root-owned, mode 0600, loaded per service through `env_file`. The committed key-only templates `deploy/.env.<app>.example` are kept in sync with each project's `appsettings.json` and `.env.example`.
- **Postgres** runs as a separate instance, reached through a shared external Docker network named by `TECHSTRAP_DB_NETWORK`. Only the Api and the Worker join it. The connection string uses the Postgres host name on that network.
- **Images.** GHCR stays (`ghcr.io/syntax-circus/techstrap-*`). The deploy compose requires an explicit reference per service: there is no `latest` default.
- **Delivery.** One PR, before PHASE-08.

### Technical decisions this plan makes (D-043; the owner confirms at plan review)
- **What an `appsettings.json` may say.** A value equals today's code default or is blank. A blank is allowed only for a string (or a nullable) whose blank form is the default or the required-and-missing state; the allowed blanks are an explicit list in the contract test. Every number, flag and enum carries its real default, because a blank one fails binding (`Failed to convert configuration value '' ...`, shown for `Email:Smtp:Port`, `MaxRetryAttempts`, `RetryMode` and a boolean). A blank nullable (`TlsMode`, `TotalSendTimeout`) binds as null, the code default, and stays blank.
- **Arrays.** An empty array is `[]`. An element key is documented but never left blank in a template (commented out in the deploy templates), because a blank element counts as configured: `TrustedProxy` with one blank network passes the Production check, so a blank template would start. An array with a non-empty library default is not listed: `Auth:Scopes` binding appends to the default, so the four default scopes would be requested twice. (ADMIN-APP.md now says `AUTH__SCOPES__N` adds to the defaults.)
- **Not listed, excluded or owned by compose.** `SassCompiler`, `Serilog:Using`, `WriteTo` and `MinimumLevel:Override` are excluded from the comparison. `SecurityHeaders:ContentSecurityPolicy` and `PathOverrides` (set in code per host), `Auth:TokenCache`, `Storage:S3`, `Storage:Local:PublicBaseUrl` and `AutoClose:Days` are not listed. Compose-owned keys (`STORAGE__LOCAL__ROOTPATH`, the trusted network of each host, `API__BASEURL` and `DATAPROTECTION__KEYRINGPATH` of the Admin, `DATAPROTECTION__KEYRINGPATH` of the Portal) are not in the deploy templates, because `environment:` overrides the env file. `TECHSTRAP_SEED_DEV_DATA` is Development only and is not in the deploy templates.
- **`appsettings.Development.json`.** The Api and Portal files keep the documentation trusted network (`192.0.2.0/24`). The Admin file gains the placeholders the local compose used to set (`https://authentik.invalid/...`, `techstrap-admin`, `not-configured`). The Worker has none. The Development start tests use each host's `.env.example`, which is what a developer has after `cp .env.example .env.local`; the Api needs two required values there (the portal URL and the storage path), as it did before.
- **Local compose.** `environment:` keeps the local Postgres connection string, the Api address, volume paths, the proxy trust, the portal URL it publishes (`TECHSTRAP_PORTAL_PUBLIC_URL` and `Storage__Local__RootPath` for the Api, so a clone with no `.env.local` still starts), `TECHSTRAP_SEED_DEV_DATA` (a compose input) and the Mailpit settings. The Admin `Auth__*`, the group keys, the Worker's storage settings and portal URL, and the Portal's unread `Api__BaseUrl` and public URL are removed. The Worker loses its storage volume (it never registers attachments).
- **Deploy compose.** The project name is an input (`TECHSTRAP_PROJECT`), so UAT and production never share containers or volumes. Every input except the project name is required. The Api trusts the subnet (`__0`) and `REVERSE_PROXY_CIDR` (`__1`); the Admin and Portal trust `REVERSE_PROXY_CIDR` (`__0`), as the old compose did. The reverse-proxy CIDR stays a compose input, so the scope sentence "proxy entries from `__1` onward come from the env file" is not used: extra trusted proxies go in `TRUSTEDPROXY__TRUSTEDPROXIES__0` of the env file. The Portal and the Worker get no `API__BASEURL` (neither reads it yet; PHASE-09 adds it to the Portal). `env_file` uses `format: raw`, so a `$` or a `#` in a value is literal.
- **Contract rules.** Keys are compared in `SECTION__KEY` form, indexes collapse to `__0`, an empty array is its `__0` key, and a commented key counts. "No secret-shaped values": a key containing Password, Secret, Dsn, Token or Key (not `KeyRingPath`) must be blank in a committed file unless its value is a number or a flag (so `PublicKeyPermitLimit` is fine); `OPENTELEMETRY__HEADERS` is added because it carries a token; a connection string may hold only `Password=replace-me`. The Development placeholders must be `.invalid` hosts or `not-configured`. UAT and production share one compose file, so their two input templates must set the same variable names.
- **Production tests.** `ProductionBlankTemplateTests` starts each host in Production from the blank deploy template with none of the `HostFactory` defaults (a portal URL, a storage path, the test issuer). It clears the test issuer from the process environment first and restores it afterwards (every such test is in `ProcessEnvironmentCollection`), and it sets `Database:MigrateOnStartup=false` so the Api does not try to migrate a database that is not there. Alone, the template fails the Api (authority, portal URL, storage), the Worker (SMTP host and sender), the Admin (the three Auth keys and the Api address) and the Portal (only the trusted proxies). With the compose-owned values added, the operator's keys remain. The Portal has no required key until PHASE-09, so with the compose trust it starts; a test says so.
- **Smoke check.** `scripts/Test-ComposeSmoke.ps1` gains `-DeployComposeOnly` and `-CheckDeployCompose`: `docker compose config --quiet` on `deploy/docker-compose.yml` with the UAT template and dummy env files. It never pulls an image and never starts that stack.
- **Scratch-copy evidence.** Every task below was replayed in order in a scratch copy, one commit per task, running that task's commands. The final tree gave `dotnet build TechStrap.slnx -c Release` with 0 warnings, `dotnet test --solution TechStrap.CI.slnf -c Release` with 4046 passed, `pwsh -File scripts/Invoke-ScriptTests.ps1` with 253 passed, and a real `scripts/Test-ComposeSmoke.ps1 -CheckDeployCompose` run that built the four images, started the local stack and reached a healthy Api and Admin. The deploy compose was only ever resolved with `docker compose config`, never started.
- **Deviations from the approved scope.** (1) The four `deploy/.env.<app>.example` templates are created in Task 2, not Task 4, because Task 2's contract compares them and its Production tests start hosts from them. (2) The reverse-proxy CIDR is a compose-owned trusted network (see Deploy compose). (3) The Worker loses its storage volume locally too, and the Portal its `Api__BaseUrl`, because neither reads them. (4) `Auth:Scopes` is not listed in `appsettings.json` (see Arrays). (5) `DOTENV__ENABLED=false` is the only switch compose sets to stop a `.env` load; no `DOTNET_` variable is needed.

## Global Constraints

- **Branch.** Work on `feat/deploy-config` (from `main`) and land it as one PR.
- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`.
  - Private fields are `_camelCase`.
  - Constants are PascalCase.
  - Namespaces are file-scoped.
- **Packages.** Versions are managed centrally. This plan adds no package.
- **Settings.** A new setting goes in four places: the host's `appsettings.json`, its `.env.example`, the matching `deploy/.env.<app>.example` (unless compose owns it or it is Development only) and the compose file if compose owns it. A default equals the code default or is blank, and never a blank number, flag, enum or array element.
- **Compose.**
  - The deploy compose has no `build:`, no Postgres and no `latest` default.
  - Never run `docker compose up` against `deploy/docker-compose.yml`: it needs real images and an external Postgres. `docker compose config` is the check.
  - Never run `docker compose down -v`.
- **Encoding.** Every file this plan writes is ASCII. The Write tool decodes `\uXXXX`, so grep for non-ASCII afterwards.
- **Secrets.** No secret in a committed file. A placeholder is allowed only in `appsettings.Development.json`, and only when it is clearly fake (`not-configured`, an `.invalid` host).
- **Tests.**
  - Write the failing test first, and record RED and GREEN.
  - Prove each pin non-vacuous with a recorded mutation.
  - A test that sets process environment variables runs in `[Collection(ProcessEnvironmentCollection.Name)]`.
  - Every real-tree Pester rule also proves the scan sees the tree, so an empty scan cannot pass.
- **Persistence.** No migration is expected.
- **Commits.** Use Conventional Commits, each ending with exactly:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden.**
  - `git add -f`.
  - Committing `.superpowers/`.
  - Committing a filled env file or a `deploy/.env.*.local`.
  - Killing processes you did not start.
  - Committing without running `git diff --cached --stat` first.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release` gives 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release` passes.
  - `pwsh -File scripts/Invoke-ScriptTests.ps1` passes.
  - `docker compose --env-file deploy/.env.uat.example -f deploy/docker-compose.yml config --no-env-resolution --quiet` resolves from the committed template, and `config` resolves with the dummy env directory the tests make; removing an input fails with its name.

## Review Focus

1. **A Production host that starts from the blank template**, or from one blank array element. Pinned in Task 2 by `ProductionBlankTemplateTests` and the array rules.
2. **A behaviour change hidden in an `appsettings.json` default**: a blank number, flag or enum that fails binding, a default that differs from the code, or a list that is appended twice. Pinned in Task 2.
3. **A secret, or a secret-shaped value, in a committed file**, including the connection string and the OTLP headers. Pinned in Tasks 2 and 4.
4. **The local stack no longer starting**, or the Admin placeholders clashing with `.env.local` again. Pinned in Task 3, including a real compose smoke run.
5. **The deploy compose doing more than the owner decided**: building, running a database, giving the Admin or the Portal the database network, publishing beyond loopback, trusting too much, or defaulting an image to `latest`. Pinned in Task 4.

---

### Task 1: Record D-043 and update the architecture, roadmap and PHASE-12 wording

**Review Focus pin:** none of the five directly. This is the decision record the other four tasks cite; `RepositoryDocs.Tests.ps1` pins that it exists, that the architecture and PHASE-12 no longer name the deleted files, and that the Postgres-password carry-forward is struck through.

**Files:**
- Create: `docs/superpowers/plans/2026-10-05-deploy-config.md` (this plan, saved as the plan of record that D-043 links to)
- Modify: `docs/architecture/04-DECISION-LOG.md`
- Modify: `docs/architecture/02-ARCHITECTURE.md`
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`
- Modify: `docs/architecture/PHASE-12-release-hardening.md`
- Modify: `scripts/tests/RepositoryDocs.Tests.ps1`

**Interfaces:**
- Consumes: `Get-RepoText` and `$script:RepoRoot` (both defined in the `BeforeAll` of `RepositoryDocs.Tests.ps1`).
- Produces: decision `D-043` (date 2026-10-05) with the owner decisions and the technical decisions of this plan. Tasks 2 to 5 cite it in code comments, commit messages and docs.

- [ ] **Step 1: Write the failing test**

Insert this `Describe` into `scripts/tests/RepositoryDocs.Tests.ps1`, after the `README.md` `Describe` (the file ends with it today):

```powershell
Describe 'D-043 (scoped configuration and one image-only deploy compose)' {
    It 'is in the decision log with its date, its status and an index row' {
        $log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
        $log | Should -Match '(?m)^## D-043: Scoped per-project configuration and one image-only deployment compose'
        $log | Should -Match '(?s)## D-043:.*?- \*\*Status:\*\* Approved \(owner 2026-10-05.*?- \*\*Date:\*\* 2026-10-05'
        $log | Should -Match '(?m)^\| D-043 \|.*\| 2026-10-05 \|'
    }

    It 'replaces the old deployment files in the architecture, the roadmap and PHASE-12' {
        $architecture = Get-RepoText 'docs/architecture/02-ARCHITECTURE.md'
        $architecture | Should -Match 'deploy/docker-compose\.yml'
        $phase12 = Get-RepoText 'docs/architecture/PHASE-12-release-hardening.md'
        $phase12 | Should -Match '\*\*P12-T14\*\*.*deploy/docker-compose\.yml'
        foreach ($text in $architecture, $phase12) {
            $text | Should -Not -Match 'docker-compose\.(uat|production)\.yml'
            $text | Should -Not -Match '(?<!deploy/)\.env\.production\.example'
        }
        $roadmap = Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md'
        $roadmap | Should -Match '~~Warn in `\.env\.production\.example` that the Postgres password must be connection-string safe~~'
        $roadmap | Should -Match '\| 8 \|.*deploy/\.env\.<env>\.local'
    }
}
```

- [ ] **Step 2: Run the test and confirm it fails**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1`
Expected: FAIL, 2 failed (`is in the decision log with its date, its status and an index row` and `replaces the old deployment files in the architecture, the roadmap and PHASE-12`) and 6 passed.

- [ ] **Step 3: Save the plan and write the decision and the wording changes**

Save this plan as `docs/superpowers/plans/2026-10-05-deploy-config.md`.

In `docs/architecture/04-DECISION-LOG.md`, replace:

```text
- **Owner decision (2026-10-02, PHASE-02):** D-023
```

with:

```text
- **Owner decision (2026-10-05, scoped configuration and deployment, before PHASE-08):** D-043, the owner decisions on one image-only deploy compose, scoped env files on the host, a separate Postgres and explicit image tags. Its technical decisions were proposed in the plan and approved when the owner approved the plan.
- **Owner decision (2026-10-02, PHASE-02):** D-023
```

In `docs/architecture/04-DECISION-LOG.md`, replace:

```text

---

## D-042:
```

with:

```text
| D-043 | Scoped per-project configuration (every key in `appsettings.json`, a `.env.example` and a deploy env template per host) and one image-only deploy compose for UAT and production, with a separate Postgres | Approved (owner 2026-10-05; technical decisions at plan review) | 2026-10-05 | PHASE-12, PHASE-08, PHASE-09 |

---

## D-042:
```

Append to the end of `docs/architecture/04-DECISION-LOG.md`:

```markdown
---

## D-043: Scoped per-project configuration and one image-only deployment compose

- **Status:** Approved (owner 2026-10-05; technical decisions at plan review)
- **Date:** 2026-10-05
- **Owner:** Jon Seeley
- **Related artifacts:** D-019, D-024, D-029, D-040, D-042, `docs/architecture/02-ARCHITECTURE.md` section 11, `docs/self-hosting/DEPLOYMENT.md`, `deploy/docker-compose.yml`, `docs/superpowers/plans/2026-10-05-deploy-config.md`

### Context
PHASE-07 is merged. Before PHASE-08 (the knowledge base) the owner wants TechStrap's configuration and deployment to follow the sibling repositories (`sinforgiver`, `who-flung-pu`). Reading the code found these facts:
- **Settings.** Each host's `appsettings.json` held only Sentry, OpenTelemetry, Serilog and `AllowedHosts`. ConnectionStrings, Auth, Api, Email, Storage, RateLimiting and the other sections came only from environment variables, so no file listed what a host reads.
- **Deployment.** `docker-compose.production.yml` and `docker-compose.uat.yml` were near copies. They bundled Postgres, had no `env_file`, interpolated every setting from one root `.env.production`, defaulted the image tag to `latest`, and could not set many keys the code reads (Sentry, OpenTelemetry, rate limits, `Api__TimeoutSeconds`, `TECHSTRAP_ADMIN_PUBLIC_URL`, LostLink). A blank `OIDC_*` passed `docker compose config` and then failed `ValidateOnStart` at boot.
- **Examples.** The Worker `.env.example` listed `TECHSTRAP_PORTAL_PUBLIC_URL` and `STORAGE__*`, which the Worker never reads. The Portal `.env.example` listed `API__BASEURL` and the public URL, which it does not read until PHASE-09. The local compose overrode the Admin's `AUTH__*` from a root `.env` that had no example, so `.env.local` clashed with it.
- **Binding.** A blank number, flag or enum fails configuration binding ("Failed to convert configuration value '' ..."); a blank nullable (`Email:Smtp:TlsMode`, `TotalSendTimeout`) binds as null. Binding an array appends to a non-empty default, so listing the defaults of `Auth:Scopes` would duplicate them. A blank array element still counts: `TrustedProxy` with one blank network passes the Production check.

### Decision
**Owner decisions (2026-10-05)**
- **One deploy compose.** `deploy/docker-compose.yml` serves UAT and production. It is image-only: no `build:` and no Postgres. A small compose-inputs file chooses the environment: the committed templates `deploy/.env.uat.example` and `deploy/.env.production.example`, copied to `deploy/.env.<env>.local` on the host. They hold the project name, the image references, the env directory, the loopback ports, the app subnet, the reverse-proxy CIDR and the Postgres network name, and no secret.
- **Scoped env files on the host.** `${TECHSTRAP_ENV_DIR}` (for example `/etc/techstrap/uat/`) holds `.env.api`, `.env.worker`, `.env.admin` and `.env.portal`, root-owned and mode 0600, loaded per service through `env_file`. The committed key-only templates `deploy/.env.<app>.example` are kept in sync with each project's `appsettings.json` and `.env.example`.
- **Separate Postgres.** It runs as its own instance, reached through an existing external Docker network named by `TECHSTRAP_DB_NETWORK`. Only the Api and the Worker join it.
- **Images.** GHCR stays (`ghcr.io/syntax-circus/techstrap-*`). The compose requires an explicit image reference per service: there is no `latest` default.
- **Delivery.** Its own PR before PHASE-08.

**Technical decisions (proposed in the plan; approved when the owner approves it)**
- **Every key in `appsettings.json`, with a real default or blank.** A value equals today's code default or is blank, so behaviour does not change. A blank is allowed only where the setting is a string (or a nullable) whose blank form is the default or the required-and-missing state. Every number, flag and enum carries its real default, because its blank form fails binding. The allowed blanks are an explicit list in the contract test. An array is `[]` in `appsettings.json` and its element key is commented out in the `.env` files, because a blank element is a configured element.
- **Production still fails fast.** Given only the blank deploy template, each host fails to start and names the missing keys; `ProductionBlankTemplateTests` pins it per host. The Portal has no required key yet (PHASE-09 adds them), so only the trusted proxies stop it.
- **What compose owns.** `environment:` sets only `ASPNETCORE_ENVIRONMENT`, `DOTENV__ENABLED=false`, `API__BASEURL` (Admin), the key-ring path (Admin, Portal), the storage path (Api) and the trusted networks (Api: subnet then `REVERSE_PROXY_CIDR`; Admin and Portal: `REVERSE_PROXY_CIDR`). These override the env file, so the deploy templates do not list them. `REVERSE_PROXY_CIDR` stays a compose input, which keeps the old entry layout.
- **Local compose.** It keeps only its own wiring in `environment:` (the local Postgres, the Api address, volume paths, the proxy trust, the portal URL it publishes and the Mailpit settings). The Admin's `Auth__*` and the group keys no longer come from compose: `appsettings.Development.json` holds clearly fake placeholders (`.invalid`, `not-configured`) and `.env.local` replaces them. The Worker loses its storage volume and the stale keys. A root `.env.example` documents the four compose inputs.
- **Contract test.** `scripts/tests/ConfigContract.Tests.ps1` compares keys case-insensitively as `SECTION__KEY`. It excludes `SassCompiler`, `Serilog:Using`, `WriteTo` and `MinimumLevel:Override`, and it does not list the keys set in code or owned by a library (`SecurityHeaders:ContentSecurityPolicy`, `Auth:Scopes`, `Auth:TokenCache`, `Storage:S3`, `AutoClose:Days`). A key whose name contains Password, Secret, Dsn, Token or Key (not `KeyRingPath`), and the OTLP headers, must be blank in a committed file unless its value is a number or a flag; the Development placeholders must be clearly fake. UAT and production inputs must set the same names.
- **Postgres password.** The connection string is a single value, so the deploy templates say to use only letters, digits and `- _ . ~` (`openssl rand -hex 24`). This closes the roadmap carry-forward.

### Alternatives Considered
- **Keep two compose files.** Rejected: they differed by a name and default ports, and drifted.
- **Interpolate secrets from one root env file** (the old way). Rejected: it cannot carry every key, passes blank secrets silently and puts all four apps' secrets in every container's compose environment.
- **Bundle Postgres in the deploy compose.** Rejected by the owner: the database has its own lifecycle and backups.
- **List the library defaults of `Auth:Scopes`.** Rejected: array binding appends, so the defaults would duplicate.
- **Make `.env.local` blank keys win in Development.** Rejected: a blank `AUTH__*` would replace the placeholders and stop the Admin, so the example keeps those three commented out.

### Consequences
- **The old files are gone.** `docker-compose.production.yml`, `docker-compose.uat.yml` and `.env.production.example` are deleted; PHASE-12 T14 deploys UAT with `deploy/docker-compose.yml`.
- **A deploy needs the scoped env files to exist.** `docker compose config` fails while one is missing, so a typo in `TECHSTRAP_ENV_DIR` stops before `pull`.
- **A new setting is four edits** (`appsettings.json`, `.env.example`, `deploy/.env.<app>.example` and, if compose owns it, the compose file), and the contract test fails until they agree.
- **The Worker no longer mounts the storage volume.** It never registered attachments.
- **Still open (owner):** create `/etc/techstrap/uat/` from the templates and run `config --quiet`, `pull` and `up -d --wait` against the external Postgres.

### Approval
- **Approved by:** Jon Seeley (owner, scoped configuration and deploy compose planning)
- **Approved on:** 2026-10-05
```

In `docs/architecture/02-ARCHITECTURE.md`, replace:

```text
  docker-compose.yml  docker-compose.uat.yml  docker-compose.production.yml  .env.*.example
```

with:

```text
  docker-compose.yml  .env.example  deploy/{docker-compose.yml,.env.*.example}
```

In `docs/architecture/02-ARCHITECTURE.md`, replace:

```text
- Files: `docker-compose.yml` (local), `docker-compose.uat.yml`, `docker-compose.production.yml`, `.env.production.example`, plus `.env.example` per host (`api`, `admin`, `portal`, `worker`) and gitignored `.env.local`, loaded by `SyntaxCircus.DotEnv`.
```

with:

```text
- Files (D-043): `docker-compose.yml` (local: builds the four images, Postgres 17 and Mailpit) with a root `.env.example` for its inputs (`TECHSTRAP_SUBNET`, `REVERSE_PROXY_CIDR`, `TECHSTRAP_MAILPIT_PORT`, `TECHSTRAP_SEED_DEV_DATA`); `deploy/docker-compose.yml`, one image-only compose for UAT and production, with the compose-input templates `deploy/.env.uat.example` and `deploy/.env.production.example` and the key-only app templates `deploy/.env.{api,worker,admin,portal}.example`; and per project an `appsettings.json` that lists every setting the host reads (real non-secret defaults, blank secrets), an `appsettings.Development.json` with the local overrides, a `.env.example` and a gitignored `.env.local`, loaded by `SyntaxCircus.DotEnv` in Development only. `scripts/tests/ConfigContract.Tests.ps1` keeps the four in sync.
```

In `docs/architecture/02-ARCHITECTURE.md`, replace:

```text
- Volumes: `pgdata`, `techstrap-storage` (API and Worker, mounted at `/app/storage`), `dataprotection-keys` per ASP.NET host (shared key ring needs only matter within one app; Admin and Portal have separate rings).
```

with:

```text
- Volumes: `techstrap-storage` (API only, mounted at `/app/storage`; the Worker registers no attachment storage), `admin-keys` and `portal-keys` (the ASP.NET data protection key ring of each Blazor host, mounted at `/app/dataprotection-keys`); the local compose adds `pgdata`.
```

In `docs/architecture/02-ARCHITECTURE.md`, replace:

```text
- Startup order: `postgres` healthy, `api` healthy (runs migrations), then `admin`, `portal`, `worker`.
```

with:

```text
- Startup order: `api` healthy (it runs migrations against the Postgres that is already up), then `admin`, `portal` and `worker`. The local compose waits for its own `postgres` first.
- Deployment (D-043): `deploy/docker-compose.yml` is image-only: GHCR images, each named in full by an input (no `latest`), `pull_policy: always`, loopback ports, a pinned app subnet. Each service loads its own scoped env file, `${TECHSTRAP_ENV_DIR}/.env.<app>` (root-owned, mode 0600, `format: raw`), and `environment:` holds only what compose owns (`ASPNETCORE_ENVIRONMENT`, `DOTENV__ENABLED`, the Api address, the key-ring and storage paths, the trusted networks). Postgres is a separate instance on an external Docker network (`TECHSTRAP_DB_NETWORK`) that only the Api and the Worker join. The runbook is `docs/self-hosting/DEPLOYMENT.md`.
```

In `docs/architecture/02-ARCHITECTURE.md`, replace:

```text
- **Pinned subnet:** `docker-compose.yml` network `default` uses `ipam` subnet `172.16.31.0/24` (A-09). Registering this subnet in the pattern's subnet registry is a cross-repo owner action in the _template, recorded as a PHASE-01 compose task (D-019).
```

with:

```text
- **Pinned subnet:** the `default` network of `docker-compose.yml` and of `deploy/docker-compose.yml` uses `ipam` subnet `172.16.31.0/24` (A-09; `TECHSTRAP_SUBNET`). Registering this subnet in the pattern's subnet registry is a cross-repo owner action in the _template, recorded as a PHASE-01 compose task (D-019).
```

In `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, replace:

```text
- The portal base URL variable is `TECHSTRAP_PORTAL_PUBLIC_URL`; the storage mount is `/app/storage` (volume `techstrap-storage`), shared by the API and the Worker.
```

with:

```text
- The portal base URL variable is `TECHSTRAP_PORTAL_PUBLIC_URL`; the storage mount is `/app/storage` (volume `techstrap-storage`), mounted by the API only (D-043).
```

In `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, replace:

```text
| 4 | Open the cross-repo PR registering subnet `172.16.31.0/24` in the `_template` `CLIENT_IP_RATE_LIMITING.md` registry, and confirm it is free on the UAT host (D-019) | P01-T16 |
```

with:

```text
| 4 | Open the cross-repo PR registering subnet `172.16.31.0/24` in the `_template` `CLIENT_IP_RATE_LIMITING.md` registry, and confirm it is free on the UAT host (the `TECHSTRAP_SUBNET` input of `deploy/docker-compose.yml`, D-019, D-043) | P01-T16 |
```

In `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, replace:

```text
| 8 | Supply the reverse-proxy address and trusted network values for UAT and production (Q-08) and an SMTP relay for UAT | P01-T16, P12-T14 |
```

with:

```text
| 8 | Supply the reverse-proxy address and trusted network values for UAT and production (Q-08: the `REVERSE_PROXY_CIDR` input of `deploy/.env.<env>.local`), create the shared Postgres Docker network and the scoped env files under `/etc/techstrap/<env>/` (D-043), and an SMTP relay for UAT | P01-T16, P12-T14 |
```

In `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, replace:

```text
| Warn in `.env.production.example` that the Postgres password must be connection-string safe | first UAT deploy |
```

with:

```text
| ~~Warn in `.env.production.example` that the Postgres password must be connection-string safe~~ | Done in D-043: `deploy/.env.api.example` and `deploy/.env.worker.example` say so |
```

In `docs/architecture/PHASE-12-release-hardening.md`, replace:

```text
- **UAT deployment:** release candidate tag `v1.0.0-rc.N` builds images and packages; `docker-compose.uat.yml` deploys on the UAT box; soak for at least 48 hours with seed-free real usage by the owner (**Assumption**); dashboards/alerts in the existing observability stack cover health, error rate, outbox lag, dead letters and listener reconnects.
```

with:

```text
- **UAT deployment:** release candidate tag `v1.0.0-rc.N` builds images and packages; `deploy/docker-compose.yml` with `deploy/.env.uat.example` deploys on the UAT box (D-043: image-only, scoped env files under `/etc/techstrap/uat/`, a separate Postgres; runbook `docs/self-hosting/DEPLOYMENT.md`); soak for at least 48 hours with seed-free real usage by the owner (**Assumption**); dashboards/alerts in the existing observability stack cover health, error rate, outbox lag, dead letters and listener reconnects.
```

In `docs/architecture/PHASE-12-release-hardening.md`, replace:

```text
- [ ] **P12-T14** Deploy a release candidate (`v1.0.0-rc.1`) to UAT with the UAT compose and `.env.production.example`-derived env; wire dashboards and alerts
```

with:

```text
- [ ] **P12-T14** Deploy a release candidate (`v1.0.0-rc.1`) to UAT with `deploy/docker-compose.yml`, the env files made from the `deploy/.env.<app>.example` templates and `deploy/.env.uat.example` (D-043); wire dashboards and alerts
```

In `docs/architecture/PHASE-12-release-hardening.md`, replace:

```text
- [ ] **P12-T16** Write `docs/self-hosting.md` (generic OIDC, env reference, proxy/subnet/forwarded headers, SMTP, volumes, admin group, upgrade/rollback, PgBouncer note)
```

with:

```text
- [ ] **P12-T16** Extend `docs/self-hosting/DEPLOYMENT.md` (D-043) and write `docs/self-hosting.md` (generic OIDC, an env reference built from the `deploy/.env.<app>.example` templates and the contract test, proxy/subnet/forwarded headers, SMTP, volumes, admin group, upgrade/rollback, PgBouncer note)
```

- [ ] **Step 4: Run the test and confirm it passes**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1`
Expected: PASS, 8 tests.

To see the pins guard (do not commit these edits), run `git add docs scripts/tests/RepositoryDocs.Tests.ps1` first, so `git checkout -- <file>` restores the passing version. Make each change below, rerun the command, expect the failure named, and restore the file. These were run in the scratch copy:

- `99-IMPLEMENTATION-ROADMAP.md`: remove the `~~` around the carry-forward row ("Warn in `.env.production.example` ..."). Fails `replaces the old deployment files in the architecture, the roadmap and PHASE-12`.
- `PHASE-12-release-hardening.md`: write `docker-compose.uat.yml` back into P12-T14 instead of `deploy/docker-compose.yml`. Fails the same test.
- `04-DECISION-LOG.md`: change `| D-043 | Scoped` in the index row to `| D-044 | Scoped`. Fails `is in the decision log with its date, its status and an index row`.

- [ ] **Step 5: Whole-project check**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS (the docs tests only read Markdown; nothing is built in this task).

- [ ] **Step 6: Commit**

```bash
git add docs/architecture \
  docs/superpowers/plans/2026-10-05-deploy-config.md \
  scripts/tests/RepositoryDocs.Tests.ps1
git diff --cached --stat
git commit -m "docs: record D-043 (scoped configuration, one image-only deploy compose)" -m "The owner decisions of 2026-10-05 (one deploy compose, scoped env files on the host, a separate Postgres, explicit image tags) and the technical decisions of the plan: every key in appsettings.json with a real default or blank, a config contract, Production fail-fast tests. The architecture section 11, the roadmap (owner actions 4 and 8, the Postgres password carry-forward) and PHASE-12 T14 and T16 name deploy/docker-compose.yml instead of the files this change deletes." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 2: Every setting in appsettings.json, a `.env.example` and a deploy env template per host, the config contract, and the Production fail-fast tests

**Review Focus pin:**
- **(1)** Production must not start from the blank template. Pinned by `ProductionBlankTemplateTests` (per host, naming the keys), by the "array element" rules (`TrustedProxy` with one blank network passes the check) and by `No_deploy_template_leaves_an_array_element_blank`.
- **(2)** No behaviour change. Pinned by the "blank only where blank is valid" contract rule (a blank number, flag or enum fails binding), by `DevelopmentStartupTests.A_host_starts_in_Development_from_its_appsettings_and_only_the_settings_it_cannot_default`, and by leaving `Auth:Scopes` out (array binding appends to a non-empty default).
- **(3)** No secret in a committed file. Pinned by the secret-shaped rule and the connection-string rule.

**Files:**
- Modify: `src/TechStrap.Api/appsettings.json`, `src/TechStrap.Worker/appsettings.json`, `src/TechStrap.Admin/appsettings.json`, `src/TechStrap.Portal/appsettings.json`
- Modify: `src/TechStrap.Admin/appsettings.Development.json`
- Modify: `src/TechStrap.Api/.env.example`, `src/TechStrap.Worker/.env.example`, `src/TechStrap.Admin/.env.example`, `src/TechStrap.Portal/.env.example`
- Create: `deploy/.env.api.example`, `deploy/.env.worker.example`, `deploy/.env.admin.example`, `deploy/.env.portal.example`
- Create: `scripts/tests/ConfigContract.Tests.ps1` (Tasks 3 and 4 append to it)
- Create: `tests/TechStrap.Api.Tests/Config/ConfigHostSupport.cs`, `ScopedEnvironment.cs`, `DevelopmentStartupTests.cs`, `ProductionBlankTemplateTests.cs`
- Modify: `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`
- Modify: `CONTRIBUTING.md`

The four deploy app templates are created here, not with the compose in Task 4, because this task's contract compares them with `appsettings.json` and `ProductionBlankTemplateTests` starts hosts from them.

**Interfaces:**
- Consumes: `HostFactory`'s conventions (a host is started with `WebApplicationFactory<TProgram>`, `Record.Exception(() => factory.CreateClient())`), `ProcessEnvironmentCollection` (in `TrustedProxyStartupTests.cs`), `SyntaxCircus.DotEnv.AddSyntaxCircusDotEnvFiles` and the options validation already in the hosts (`ValidateOnStart` in `AgentAuthenticationSetup`, `IntakeServiceCollectionExtensions`, `AttachmentServiceCollectionExtensions`, `EmailServiceCollectionExtensions`, `AdminOptionsValidators`).
- Produces (names are fixed; Tasks 3 and 4 add rows to the Pester lists):

```csharp
namespace TechStrap.Api.Tests.Config;

public enum HostKind { Api, Worker, Admin, Portal }

public static class ConfigFiles
{
    public static string RepositoryRoot { get; }
    public static string HostExample(HostKind host);                       // src/TechStrap.<Host>/.env.example
    public static string DeployTemplate(HostKind host);                    // deploy/.env.<host>.example
    public static IReadOnlyDictionary<string, string?> ReadEnvFile(string path);   // uncommented keys as A:B, read by the DotEnv loader the hosts use
}

public sealed class ConfigHostFactory<TProgram>(string environment, IReadOnlyDictionary<string, string?> settings);   // no HostFactory defaults
public static class ConfigHosts { public static Task<Exception?> TryStartAsync(HostKind host, string environment, IReadOnlyDictionary<string, string?> settings); }
public sealed class ScopedEnvironment : IDisposable { public ScopedEnvironment(params (string Name, string? Value)[] variables); }
```

```powershell
# scripts/tests/ConfigContract.Tests.ps1 (helpers in the first BeforeAll; the documented exclusion and ownership lists live there too)
Get-JsonLeaves, Get-AppsettingsLeaves, Get-AppsettingsKeys, Get-EnvEntries, Get-EnvKeys, ConvertTo-IndexlessKey
$script:ExcludedPrefixes, $script:ComposeOwned, $script:ComposeNonSettings, $script:DevelopmentOnly, $script:BlankKeys, $script:SecretWords
```

**Rules (each one was found by running the hosts in the scratch copy; they are the content of D-043):**
1. **Defaults.** Every value in an `appsettings.json` equals today's code default or is blank. Behaviour does not change.
2. **Blank versus validation.** Check each blank against its options' `ValidateOnStart` and binding.
   - A blank string is allowed only where it is the default or the required-and-missing state (`ConnectionStrings:TechStrap`, an authority, a host, a public URL, `Storage:Local:RootPath`, `Api:BaseUrl`, the secrets, the observability strings).
   - A blank number, flag or enum fails binding at start (`Failed to convert configuration value '' at 'Email:Smtp:Port'`). So `Port`, `MaxRetryAttempts`, `UseStartTls`, `RetryMode`, every rate limit, `MigrateOnStartup`, `TECHSTRAP_SEED_DEV_DATA`, `TECHSTRAP_AUTOCLOSE_DAYS` and the group keys carry their real default. A blank group key would fail `ValidateOnStart` too.
   - A blank nullable (`Email:Smtp:TlsMode`, `TotalSendTimeout`) binds as null, which is the code default, so it stays blank.
   - The allowed blanks are an explicit list (`$script:BlankKeys`) that the contract pins both ways.
3. **Arrays.** An empty array is `[]` in `appsettings.json`, and the element key (`TRUSTEDPROXY__TRUSTEDNETWORKS__0`, `AUTHENTICATION__JWTBEARER__AUDIENCES__0`) is documented but never left blank in a template (commented out in a deploy template): a blank element counts as configured, so one blank trusted network would defeat the Production check. An array with a non-empty library default (`Auth:Scopes`) is not listed at all, because binding appends to the default and would duplicate it.
4. **Keys.** Compared case-insensitively in `SECTION__KEY` form; every array index counts as `__0`; an empty array counts as its `__0` key. A key in an env file counts whether it is commented out (`# KEY=`, one space) or not.
5. **Not listed, by design:** `SecurityHeaders:ContentSecurityPolicy` and `PathOverrides` (set in code per host), `Auth:TokenCache`, `Storage:S3`, `Storage:Local:PublicBaseUrl` (library settings TechStrap does not use), `AutoClose:Days` (the section form of `TECHSTRAP_AUTOCLOSE_DAYS`, which wins). Excluded from the comparison: `SassCompiler`, `Serilog:Using`, `Serilog:WriteTo`, `Serilog:MinimumLevel:Override`.

- [ ] **Step 1: Write the failing tests**

Create `scripts/tests/ConfigContract.Tests.ps1`:

```powershell
BeforeDiscovery {
    $script:HostCases = @(
        @{ Name = 'Api' }
        @{ Name = 'Worker' }
        @{ Name = 'Admin' }
        @{ Name = 'Portal' }
    )
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

    # --- The documented exclusion list -------------------------------------------------------------------------------------------------------------------
    # Keys are compared case-insensitively in SECTION__KEY form (appsettings "A:B:C" is A__B__C; an array element is __0, and every index counts as __0, because
    # the settings are the same one setting however many entries an operator gives it). An empty JSON array counts as its __0 key.
    # These appsettings keys are not settings an operator sets, so no .env file lists them:
    $script:ExcludedPrefixes = @(
        'SASSCOMPILER'                     # build-time stylesheet compiler (Admin, Portal)
        'SERILOG__USING'                   # sink assembly list
        'SERILOG__WRITETO'                 # sink definitions
        'SERILOG__MINIMUMLEVEL__OVERRIDE'  # keys hold dots (Microsoft.AspNetCore) that an environment variable name cannot carry
    )
    # Not listed anywhere, on purpose (each is either set in code or must not be listed):
    #   SecurityHeaders:ContentSecurityPolicy and PathOverrides  the Content-Security-Policy is set per host in code (TechStrapCsp), overriding any setting
    #   Auth:Scopes                       an array with a non-empty library default: binding appends to the default, so listing the defaults would duplicate them
    #   Auth:TokenCache, Storage:S3, Storage:Local:PublicBaseUrl  library settings TechStrap does not use
    #   AutoClose:Days                    the section form of TECHSTRAP_AUTOCLOSE_DAYS, which wins
    # Compose owns these per host (deploy/docker-compose.yml environment:), so the deploy env templates must not list them:
    $script:ComposeOwned = @{
        Api    = @('STORAGE__LOCAL__ROOTPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
        Worker = @()
        Admin  = @('API__BASEURL', 'DATAPROTECTION__KEYRINGPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
        Portal = @('DATAPROTECTION__KEYRINGPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
    }
    # Names compose sets that are not appsettings keys (the host environment and the switch that stops a container loading a .env file):
    $script:ComposeNonSettings = @('ASPNETCORE_ENVIRONMENT', 'DOTENV__ENABLED')
    # Development only, so the deploy env templates must not list them:
    $script:DevelopmentOnly = @{ Api = @('TECHSTRAP_SEED_DEV_DATA'); Worker = @(); Admin = @(); Portal = @() }
    # The only keys whose committed value may be blank in appsettings.json. Every number, flag and enum carries its real default instead, because a blank
    # value fails binding ("Failed to convert configuration value '' to type Int32"); the nullable settings TlsMode and TotalSendTimeout bind a blank as null.
    $script:CommonBlank = @('SENTRY__DSN', 'SENTRY__ENVIRONMENT', 'OPENTELEMETRY__OTLPENDPOINT', 'OPENTELEMETRY__HEADERS', 'OPENTELEMETRY__SERVICENAME', 'OPENTELEMETRY__SERVICEVERSION', 'OPENTELEMETRY__ENVIRONMENT')
    $script:BlankKeys = @{
        Api    = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'CONNECTIONSTRINGS__TECHSTRAP', 'AUTHENTICATION__JWTBEARER__AUTHORITY', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_ADMIN_PUBLIC_URL', 'STORAGE__LOCAL__ROOTPATH')
        Worker = $script:CommonBlank + @('CONNECTIONSTRINGS__TECHSTRAP', 'EMAIL__SMTP__HOST', 'EMAIL__SMTP__USERNAME', 'EMAIL__SMTP__PASSWORD', 'EMAIL__SMTP__DEFAULTFROM', 'EMAIL__SMTP__TLSMODE', 'EMAIL__SMTP__TOTALSENDTIMEOUT', 'EMAILOUTBOX__WORKERID')
        Admin  = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'API__BASEURL', 'AUTH__AUTHORITY', 'AUTH__CLIENTID', 'AUTH__CLIENTSECRET', 'DATAPROTECTION__KEYRINGPATH')
        Portal = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'DATAPROTECTION__KEYRINGPATH')
    }
    # A key containing one of these words is secret-shaped: its committed value must be blank (a number or a flag cannot be a secret, so PublicKeyPermitLimit is fine).
    # OPENTELEMETRY__HEADERS is added because the OTLP headers carry a token. KeyRingPath is a directory, not a key.
    $script:SecretWords = 'password|secret|dsn|token|key|(^|__)headers$'

    function ConvertTo-KeyName {
        param([string]$Path)
        return ($Path -replace ':', '__').ToUpperInvariant()
    }

    function ConvertTo-IndexlessKey {
        param([string]$Key)
        return ($Key -replace '__\d+(?=__|$)', '__0')
    }

    # Every leaf of a JSON object as @{ Key = 'A__B__C'; Value = '...' }; an array element is __<index>, and an empty array is its __0 key with a null value.
    function Get-JsonLeaves {
        param($Node, [string]$Prefix = '')
        $leaves = @()
        if ($Node -is [System.Management.Automation.PSCustomObject]) {
            foreach ($property in $Node.PSObject.Properties) {
                $name = if ($Prefix) { "$Prefix`__$($property.Name)" } else { $property.Name }
                $leaves += Get-JsonLeaves -Node $property.Value -Prefix $name
            }
        }
        elseif ($Node -is [System.Array]) {
            if ($Node.Count -eq 0) { $leaves += [pscustomobject]@{ Key = "$Prefix`__0".ToUpperInvariant(); Value = $null; Empty = $true } }
            for ($i = 0; $i -lt $Node.Count; $i++) {
                $leaves += Get-JsonLeaves -Node $Node[$i] -Prefix "$Prefix`__$i"
            }
        }
        else {
            $leaves += [pscustomobject]@{ Key = $Prefix.ToUpperInvariant(); Value = if ($null -eq $Node) { '' } else { [string]$Node }; Empty = $false }
        }
        return $leaves
    }

    function Test-Excluded {
        param([string]$Key)
        foreach ($prefix in $script:ExcludedPrefixes) { if ($Key -eq $prefix -or $Key.StartsWith("$prefix`__")) { return $true } }
        return $false
    }

    function Get-AppsettingsLeaves {
        param([string]$HostName, [switch]$IncludeDevelopment)
        $directory = Join-Path $script:RepoRoot 'src' "TechStrap.$HostName"
        $files = @(Join-Path $directory 'appsettings.json')
        if ($IncludeDevelopment -and (Test-Path (Join-Path $directory 'appsettings.Development.json'))) { $files += Join-Path $directory 'appsettings.Development.json' }
        $leaves = foreach ($file in $files) { Get-JsonLeaves -Node (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -NoEnumerate) }
        return @($leaves | Where-Object { -not (Test-Excluded $_.Key) })
    }

    function Get-AppsettingsKeys {
        param([string]$HostName)
        return @(Get-AppsettingsLeaves -HostName $HostName -IncludeDevelopment | ForEach-Object { ConvertTo-IndexlessKey $_.Key } | Sort-Object -Unique)
    }

    # Every KEY=value line of an env file, commented out or not. A commented key is "# KEY=" with one space; a line like "#   Host=db" is prose.
    function Get-EnvEntries {
        param([string]$Path)
        $entries = @()
        foreach ($line in (Get-Content -LiteralPath $Path)) {
            if ($line -match '^(?<hash>#\s?)?(?<key>[A-Za-z][A-Za-z0-9_]*)=(?<value>.*)$') {
                $entries += [pscustomobject]@{ Key = $Matches['key'].ToUpperInvariant(); Value = $Matches['value'].Trim(); Commented = [bool]$Matches['hash'] }
            }
        }
        return $entries
    }

    function Get-EnvKeys {
        param([string]$Path)
        return @(Get-EnvEntries -Path $Path | ForEach-Object { ConvertTo-IndexlessKey $_.Key } | Sort-Object -Unique)
    }

    # The environment: keys of every service of a compose file, by service (a small reader for the 2/4/6-space layout these files use).
    function Get-ComposeEnvironmentKeys {
        param([string]$File)
        $result = @{}
        $service = $null
        $inEnvironment = $false
        foreach ($line in (Get-Content -LiteralPath (Join-Path $script:RepoRoot $File))) {
            if ($line -match '^  (?<name>[a-z][a-z0-9-]*):\s*$') { $service = $Matches['name']; $inEnvironment = $false; continue }
            if ($line -match '^    environment:\s*$') { $inEnvironment = $true; $result[$service] = @(); continue }
            if ($inEnvironment -and $line -match '^      (?<key>[A-Za-z_][A-Za-z0-9_]*):') { $result[$service] += $Matches['key'].ToUpperInvariant(); continue }
            if ($inEnvironment -and $line -match '^\s*#') { continue }
            if ($inEnvironment -and $line -match '^\s{0,4}\S') { $inEnvironment = $false }
        }
        return $result
    }

    function Test-GitIgnored {
        param([string]$RelativePath)
        & git -C $script:RepoRoot check-ignore -q -- $RelativePath
        return ($LASTEXITCODE -eq 0)
    }
}

Describe 'the config contract of <Name>' -ForEach $script:HostCases {
    BeforeAll {
        $script:HostName = $Name
        $script:Example = Join-Path $script:RepoRoot 'src' "TechStrap.$Name" '.env.example'
        $script:Template = Join-Path $script:RepoRoot 'deploy' ".env.$($Name.ToLowerInvariant()).example"
        $script:SettingKeys = Get-AppsettingsKeys -HostName $Name
    }

    It 'the scan sees the settings (a vacuous pass would hide a broken reader)' {
        $script:SettingKeys.Count | Should -BeGreaterThan 15
        $script:SettingKeys | Should -Contain 'SENTRY__DSN'
        $script:SettingKeys | Should -Contain 'SERILOG__MINIMUMLEVEL__DEFAULT'
    }

    It 'appsettings.json plus appsettings.Development.json and .env.example list the same keys, both ways' {
        $example = Get-EnvKeys -Path $script:Example
        $missing = @($script:SettingKeys | Where-Object { $_ -notin $example })
        $extra = @($example | Where-Object { $_ -notin $script:SettingKeys })
        $missing | Should -BeNullOrEmpty -Because "src/TechStrap.$Name/.env.example is missing: $($missing -join ', ')"
        $extra | Should -BeNullOrEmpty -Because "src/TechStrap.$Name/.env.example lists keys the host does not read: $($extra -join ', ')"
    }

    It 'the deploy env template lists the same keys minus the ones compose owns and the Development-only ones, both ways' {
        $expected = @($script:SettingKeys | Where-Object { $_ -notin $script:ComposeOwned[$Name] -and $_ -notin $script:DevelopmentOnly[$Name] })
        $template = Get-EnvKeys -Path $script:Template
        $missing = @($expected | Where-Object { $_ -notin $template })
        $extra = @($template | Where-Object { $_ -notin $expected })
        $missing | Should -BeNullOrEmpty -Because "deploy/.env.$($Name.ToLowerInvariant()).example is missing: $($missing -join ', ')"
        $extra | Should -BeNullOrEmpty -Because "deploy/.env.$($Name.ToLowerInvariant()).example lists keys the host does not read or compose owns: $($extra -join ', ')"
    }

    It 'no env file lists a key twice (commented or not)' {
        foreach ($path in $script:Example, $script:Template) {
            $names = @(Get-EnvEntries -Path $path | ForEach-Object { $_.Key })
            $duplicates = @($names | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
            $duplicates | Should -BeNullOrEmpty -Because "$path repeats: $($duplicates -join ', ')"
        }
    }

    It 'appsettings.json is blank only where blank is valid, and nowhere else' {
        $blank = @(Get-AppsettingsLeaves -HostName $Name | Where-Object { -not $_.Empty -and $_.Value -eq '' } | ForEach-Object { $_.Key } | Sort-Object -Unique)
        $allowed = @($script:BlankKeys[$Name] | Sort-Object -Unique)
        $unexpected = @($blank | Where-Object { $_ -notin $allowed })
        $absent = @($allowed | Where-Object { $_ -notin $blank })
        $unexpected | Should -BeNullOrEmpty -Because "a blank number, flag or enum fails binding at start; these are blank in appsettings.json: $($unexpected -join ', ')"
        $absent | Should -BeNullOrEmpty -Because "these are expected blank but carry a value: $($absent -join ', ')"
    }

    It 'no array element is blank in appsettings.json (an empty array stays [], so a blank trusted network cannot defeat the Production check)' {
        $blankElements = @(Get-AppsettingsLeaves -HostName $Name | Where-Object { -not $_.Empty -and $_.Value -eq '' -and $_.Key -match '__\d+$' })
        $blankElements | Should -BeNullOrEmpty
    }

    It 'a blank value in .env.example or the deploy template is on the blank list, and an array element is never left blank' {
        foreach ($path in $script:Example, $script:Template) {
            $blank = @(Get-EnvEntries -Path $path | Where-Object { -not $_.Commented -and $_.Value -eq '' })
            $unexpected = @($blank | Where-Object { $_.Key -notin $script:BlankKeys[$Name] } | ForEach-Object { $_.Key })
            $unexpected | Should -BeNullOrEmpty -Because "$path leaves these blank, but only a blank-valid setting may be: $($unexpected -join ', ')"
            @($blank | Where-Object { $_.Key -match '__\d+$' }) | Should -BeNullOrEmpty -Because "$path has a blank array element; comment the key out instead"
        }
    }

    It 'no committed file holds a non-blank secret-shaped value' {
        $problems = @()
        foreach ($path in $script:Example, $script:Template) {
            foreach ($entry in (Get-EnvEntries -Path $path)) {
                $isNumberOrFlag = $entry.Value -match '^(\d+(\.\d+)?|true|false)$'
                if ($entry.Key -match $script:SecretWords -and $entry.Key -notmatch 'KEYRINGPATH' -and $entry.Value -ne '' -and -not $isNumberOrFlag) { $problems += "$path $($entry.Key)" }
            }
        }
        foreach ($leaf in (Get-AppsettingsLeaves -HostName $Name)) {
            $isNumberOrFlag = $leaf.Value -match '^(\d+(\.\d+)?|true|false)$'
            if ($leaf.Key -match $script:SecretWords -and $leaf.Key -notmatch 'KEYRINGPATH' -and $leaf.Value -and -not $isNumberOrFlag) { $problems += "appsettings.json $($leaf.Key)" }
        }
        $problems | Should -BeNullOrEmpty
    }

    It 'a connection string in a committed env file carries no password but replace-me' {
        foreach ($path in $script:Example, $script:Template) {
            foreach ($entry in (Get-EnvEntries -Path $path | Where-Object { $_.Key -eq 'CONNECTIONSTRINGS__TECHSTRAP' })) {
                if ($entry.Value -ne '') { $entry.Value | Should -Match 'Password=replace-me(;|$)' -Because "$path must not hold a real password" }
            }
        }
    }

    It 'the deploy env template has the key-only header and the sync rule' {
        $text = Get-Content -LiteralPath $script:Template -Raw
        $text | Should -Match "(?m)^# Key-only template for the $Name container: copy to /etc/techstrap/<uat\|production>/\.env\.$($Name.ToLowerInvariant())\r?$"
        $text | Should -Match "(?m)^# Keep this file in sync with src/TechStrap\.$Name/appsettings\.json and src/TechStrap\.$Name/\.env\.example"
        $text | Should -Match '(?m)^# -- .+ \[[A-Za-z, ]+\]'
    }

    It 'the .env.example header says to copy it to .env.local, loaded in Development only' {
        $text = Get-Content -LiteralPath $script:Example -Raw
        $text | Should -Match 'Copy to \.env\.local in this directory \(gitignored\)\. SyntaxCircus\.DotEnv loads \.env then \.env\.local in Development only\.'
        $text | Should -Match 'Use SECTION__KEY \(ALL_CAPS\) naming'
    }
}

Describe 'the config contract of the committed files that belong to no single host' {
    It 'the Admin Development placeholders are clearly fake' {
        $development = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'src' 'TechStrap.Admin' 'appsettings.Development.json') -Raw | ConvertFrom-Json
        ([uri]$development.Auth.Authority).Host | Should -Match '\.invalid$'
        $development.Auth.ClientSecret | Should -Be 'not-configured'
    }

    It 'no Development file holds a secret-shaped value other than a clear placeholder' {
        foreach ($host_ in 'Api', 'Admin', 'Portal') {
            $path = Join-Path $script:RepoRoot 'src' "TechStrap.$host_" 'appsettings.Development.json'
            foreach ($leaf in (Get-JsonLeaves -Node (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -NoEnumerate))) {
                if ($leaf.Key -match $script:SecretWords -and $leaf.Value) { $leaf.Value | Should -Match '^(not-configured|.*\.invalid.*)$' -Because "$path $($leaf.Key)" }
            }
        }
    }

    It 'the deploy env templates warn that the Postgres password must be safe inside a connection string' {
        foreach ($app in 'api', 'worker') {
            $text = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'deploy' ".env.$app.example") -Raw
            $text | Should -Match 'use only letters, digits and - _ \. ~'
            $text | Should -Match 'openssl rand -hex 24'
        }
    }
}
```

Create `tests/TechStrap.Api.Tests/Config/ConfigHostSupport.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using SyntaxCircus.DotEnv;

namespace TechStrap.Api.Tests.Config;

/// <summary>The four TechStrap hosts, named as their project folders and deploy templates are.</summary>
public enum HostKind
{
    Api,
    Worker,
    Admin,
    Portal,
}

/// <summary>Finds committed files and reads them the way a host does, so a test starts a host from exactly what an operator copies.</summary>
public static class ConfigFiles
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string HostExample(HostKind host) => Path.Combine(RepositoryRoot, "src", $"TechStrap.{host}", ".env.example");

    public static string DeployTemplate(HostKind host) => Path.Combine(RepositoryRoot, "deploy", $".env.{host.ToString().ToLowerInvariant()}.example");

    /// <summary>
    /// Every uncommented setting of an env file as configuration keys (<c>A__B</c> becomes <c>A:B</c>), read by the same loader the hosts use for <c>.env.local</c>.
    /// A blank value stays a blank value, exactly like a blank line in a container's <c>env_file</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ReadEnvFile(string path)
    {
        File.Exists(path).ShouldBeTrue($"{path} must exist");
        var directory = Directory.CreateTempSubdirectory("techstrap-envfile-");
        try
        {
            File.Copy(path, Path.Combine(directory.FullName, ".env.local"));
            var configuration = new ConfigurationBuilder().AddSyntaxCircusDotEnvFiles(directory.FullName).Build();
            return configuration.AsEnumerable().Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("TechStrap.slnx not found above " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// Starts a host with nothing but its committed appsettings and the settings given: none of the defaults <see cref="HostFactory{TProgram}"/> adds
/// (a portal URL, a storage path, the test issuer), so a missing required setting is missing here too. Eager reads (trusted proxies) cannot come from
/// <paramref name="settings"/>; set them as environment variables, as the compose files do.
/// </summary>
public sealed class ConfigHostFactory<TProgram>(string environment, IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
    }
}

/// <summary>Creates the right <see cref="ConfigHostFactory{TProgram}"/> for a <see cref="HostKind"/>, and starts it.</summary>
public static class ConfigHosts
{
    /// <summary>Starts the host and returns whatever the start threw (null when it started).</summary>
    public static async Task<Exception?> TryStartAsync(HostKind host, string environment, IReadOnlyDictionary<string, string?> settings)
    {
        switch (host)
        {
            case HostKind.Api:
                await using (var factory = new ConfigHostFactory<TechStrap.Api.Program>(environment, settings))
                {
                    return Record.Exception(() => factory.CreateClient());
                }

            case HostKind.Worker:
                await using (var factory = new ConfigHostFactory<TechStrap.Worker.Program>(environment, settings))
                {
                    return Record.Exception(() => factory.CreateClient());
                }

            case HostKind.Admin:
                await using (var factory = new ConfigHostFactory<TechStrap.Admin.Program>(environment, settings))
                {
                    return Record.Exception(() => factory.CreateClient());
                }

            case HostKind.Portal:
                await using (var factory = new ConfigHostFactory<TechStrap.Portal.Program>(environment, settings))
                {
                    return Record.Exception(() => factory.CreateClient());
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(host), host, null);
        }
    }
}
```

Create `tests/TechStrap.Api.Tests/Config/ScopedEnvironment.cs`:

```csharp
namespace TechStrap.Api.Tests.Config;

/// <summary>
/// Sets (or, with a null value, removes) process environment variables and puts every one of them back on dispose. Only for tests in
/// <see cref="ProcessEnvironmentCollection"/>: every host built meanwhile reads them. The static <c>HostFactory</c> constructor leaves the test issuer in the
/// environment for the whole run, so a test that needs a host without it removes it here and restores it afterwards.
/// </summary>
public sealed class ScopedEnvironment : IDisposable
{
    private readonly List<(string Name, string? Original)> _originals = [];

    public ScopedEnvironment(params (string Name, string? Value)[] variables)
    {
        foreach (var (name, value) in variables)
        {
            _originals.Add((name, Environment.GetEnvironmentVariable(name)));
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, original) in Enumerable.Reverse(_originals))
        {
            Environment.SetEnvironmentVariable(name, original);
        }
    }
}
```

Create `tests/TechStrap.Api.Tests/Config/DevelopmentStartupTests.cs`:

```csharp
namespace TechStrap.Api.Tests.Config;

/// <summary>
/// Every host starts in Development from what a developer has after <c>cp .env.example .env.local</c>: the committed appsettings files plus the host's
/// <c>.env.example</c>, nothing else. This pins that the settings the files list are real defaults that pass the hosts' start-up validation, and that the
/// Admin starts from the Development placeholders alone for its sign-in settings (the example leaves them commented out).
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class DevelopmentStartupTests
{
    // The background loops talk to a database and an SMTP server that do not exist in a test; the rest of the host is what this test starts.
    private static readonly Dictionary<string, string?> TestOnlyOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Database:MigrateOnStartup"] = "false",
        ["EmailOutbox:Enabled"] = "false",
        ["AutoClose:Enabled"] = "false",
        ["OutboxRetention:Enabled"] = "false",
    };

    public static TheoryData<HostKind> Hosts() => new() { HostKind.Api, HostKind.Worker, HostKind.Admin, HostKind.Portal };

    [Theory]
    [MemberData(nameof(Hosts))]
    public async Task A_host_starts_in_Development_from_its_appsettings_and_its_env_example(HostKind host)
    {
        using var environment = new ScopedEnvironment(
            ("DotEnv__Enabled", "false"),
            ("TrustedProxy__TrustedNetworks__0", null),
            ("Authentication__JwtBearer__Authority", null),
            ("Authentication__JwtBearer__Audiences__0", null));
        var settings = new Dictionary<string, string?>(ConfigFiles.ReadEnvFile(ConfigFiles.HostExample(host)), StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in TestOnlyOverrides)
        {
            settings[key] = value;
        }

        var failure = await ConfigHosts.TryStartAsync(host, "Development", settings);

        failure.ShouldBeNull($"{host} did not start in Development: {failure}");
    }

    // The few settings a host cannot default (an address, a path): everything else must come from the committed appsettings alone.
    public static TheoryData<HostKind, string[]> HostsAndTheirOnlyRequiredSettings() => new()
    {
        { HostKind.Api, ["TECHSTRAP_PORTAL_PUBLIC_URL=http://localhost:8082", "Storage:Local:RootPath=storage"] },
        { HostKind.Worker, [] },
        { HostKind.Admin, ["Api:BaseUrl=http://localhost:8080/"] },
        { HostKind.Portal, [] },
    };

    [Theory]
    [MemberData(nameof(HostsAndTheirOnlyRequiredSettings))]
    public async Task A_host_starts_in_Development_from_its_appsettings_and_only_the_settings_it_cannot_default(HostKind host, string[] required)
    {
        // This is what a blank number, flag or enum in appsettings.json breaks: the value is bound and validated at start, and a blank fails to convert.
        using var environment = new ScopedEnvironment(
            ("DotEnv__Enabled", "false"),
            ("TrustedProxy__TrustedNetworks__0", null),
            ("Authentication__JwtBearer__Authority", null),
            ("Authentication__JwtBearer__Audiences__0", null));
        var settings = new Dictionary<string, string?>(TestOnlyOverrides, StringComparer.OrdinalIgnoreCase);
        foreach (var setting in required)
        {
            var parts = setting.Split('=', 2);
            settings[parts[0]] = parts[1];
        }

        var failure = await ConfigHosts.TryStartAsync(host, "Development", settings);

        failure.ShouldBeNull($"{host} did not start from its appsettings: {failure}");
    }

    [Fact]
    public async Task The_Admin_starts_in_Development_without_any_sign_in_setting_of_its_own()
    {
        using var environment = new ScopedEnvironment(("DotEnv__Enabled", "false"), ("TrustedProxy__TrustedNetworks__0", null));
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Api:BaseUrl"] = "http://localhost:8080/" };

        var failure = await ConfigHosts.TryStartAsync(HostKind.Admin, "Development", settings);

        failure.ShouldBeNull($"The Admin did not start from appsettings.Development.json alone: {failure}");
    }

    [Fact]
    public async Task The_Admin_does_not_start_in_Development_when_the_example_sign_in_settings_are_uncommented_and_blank()
    {
        // The reason the example keeps AUTH__* commented out: a blank value from .env.local replaces the Development placeholder.
        using var environment = new ScopedEnvironment(("DotEnv__Enabled", "false"), ("TrustedProxy__TrustedNetworks__0", null));
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Api:BaseUrl"] = "http://localhost:8080/",
            ["Auth:Authority"] = string.Empty,
            ["Auth:ClientId"] = string.Empty,
            ["Auth:ClientSecret"] = string.Empty,
        };

        var failure = await ConfigHosts.TryStartAsync(HostKind.Admin, "Development", settings);

        failure.ShouldNotBeNull();
        failure.ToString().ShouldContain("Auth:Authority");
    }
}
```

Create `tests/TechStrap.Api.Tests/Config/ProductionBlankTemplateTests.cs`:

```csharp
namespace TechStrap.Api.Tests.Config;

/// <summary>
/// A Production host given only the blank deploy template (<c>deploy/.env.&lt;app&gt;.example</c> as an operator copies it, before filling it in) must refuse to start and
/// name what is missing. The compose file supplies a few values of its own (the trusted networks, the Api address, the storage path); a second test adds them, so the
/// failure that remains is exactly the operator's to fix. Nothing here may start a host that is half configured.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ProductionBlankTemplateTests
{
    // What deploy/docker-compose.yml sets in environment: for TrustedProxy (read eagerly, so it must be a real environment variable, as in a container).
    private const string ComposeSubnet = "172.16.31.0/24";

    // The test issuer HostFactory leaves in the environment would hide the missing Authority and audience.
    private static readonly (string, string?)[] CleanEnvironment =
    [
        ("DotEnv__Enabled", "false"),
        ("TrustedProxy__TrustedNetworks__0", null),
        ("TrustedProxy__TrustedNetworks__1", null),
        ("Authentication__JwtBearer__Authority", null),
        ("Authentication__JwtBearer__Audiences__0", null),
    ];

    public static TheoryData<HostKind> WebHosts() => new() { HostKind.Api, HostKind.Admin, HostKind.Portal };

    // What each host reports when the template is the only configuration. The Portal reads nothing required yet, so only the trusted proxies (which compose supplies) stop it.
    public static TheoryData<HostKind, string[]> HostsAndTheKeysTheyReportAlone() => new()
    {
        { HostKind.Api, ["Authentication:JwtBearer:Authority", "TECHSTRAP_PORTAL_PUBLIC_URL", "Storage:Local:RootPath"] },
        { HostKind.Worker, ["Email:Smtp:Host", "Email:Smtp:DefaultFrom"] },
        { HostKind.Admin, ["Auth:Authority", "Auth:ClientId", "Auth:ClientSecret", "Api:BaseUrl"] },
        { HostKind.Portal, ["TrustedProxy"] },
    };

    // What remains once compose has set its own values: exactly what the operator must fill in.
    public static TheoryData<HostKind, string[]> HostsAndTheKeysTheOperatorMustFill() => new()
    {
        { HostKind.Api, ["Authentication:JwtBearer:Authority", "TECHSTRAP_PORTAL_PUBLIC_URL"] },
        { HostKind.Worker, ["Email:Smtp:Host", "Email:Smtp:DefaultFrom"] },
        { HostKind.Admin, ["Auth:Authority", "Auth:ClientId", "Auth:ClientSecret"] },
    };

    /// <summary>The blank template as the host sees it, plus the Database setting every test host needs so the Api does not migrate a database that is not there.</summary>
    private static Dictionary<string, string?> BlankTemplate(HostKind host)
    {
        var settings = new Dictionary<string, string?>(ConfigFiles.ReadEnvFile(ConfigFiles.DeployTemplate(host)), StringComparer.OrdinalIgnoreCase)
        {
            ["Database:MigrateOnStartup"] = "false",
        };

        return settings;
    }

    [Theory]
    [MemberData(nameof(HostsAndTheKeysTheyReportAlone))]
    public async Task The_blank_template_alone_fails_start_naming_what_is_missing(HostKind host, string[] expectedKeys)
    {
        using var environment = new ScopedEnvironment(CleanEnvironment);

        var failure = await ConfigHosts.TryStartAsync(host, "Production", BlankTemplate(host));

        failure.ShouldNotBeNull($"{host} started in Production from the blank template alone");
        AssertNames(failure, host, expectedKeys);
    }

    [Theory]
    [MemberData(nameof(HostsAndTheKeysTheOperatorMustFill))]
    public async Task With_the_compose_values_the_blank_template_still_fails_start_naming_every_key_the_operator_must_fill(HostKind host, string[] requiredKeys)
    {
        using var environment = new ScopedEnvironment(CleanEnvironment);
        using var composeTrust = new ScopedEnvironment(("TrustedProxy__TrustedNetworks__0", ComposeSubnet));
        var settings = BlankTemplate(host);
        settings["Storage:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "techstrap-blank-template-storage");
        settings["Api:BaseUrl"] = "http://api/";

        var failure = await ConfigHosts.TryStartAsync(host, "Production", settings);

        failure.ShouldNotBeNull($"{host} started in Production from the blank template");
        AssertNames(failure, host, requiredKeys);
    }

    private static void AssertNames(Exception failure, HostKind host, string[] keys)
    {
        var text = failure.ToString();
        foreach (var key in keys)
        {
            text.ShouldContain(key, customMessage: $"The {host} start-up failure must name {key}:{Environment.NewLine}{text}");
        }
    }

    [Fact]
    public async Task The_Portal_has_no_required_setting_yet_so_the_blank_template_with_the_compose_trust_starts()
    {
        // PHASE-09 adds API__BASEURL and TECHSTRAP_PORTAL_PUBLIC_URL to the Portal and to this list; until then the Portal reads only optional settings.
        using var environment = new ScopedEnvironment(CleanEnvironment);
        using var composeTrust = new ScopedEnvironment(("TrustedProxy__TrustedNetworks__0", ComposeSubnet));

        var failure = await ConfigHosts.TryStartAsync(HostKind.Portal, "Production", BlankTemplate(HostKind.Portal));

        failure.ShouldBeNull($"The Portal did not start from the blank template with the compose trust: {failure}");
    }

    [Theory]
    [MemberData(nameof(WebHosts))]
    public void No_deploy_template_leaves_an_array_element_blank(HostKind host)
    {
        // A blank element still counts as configured: TrustedProxy then sees one entry and Production stops failing fast.
        var blankElements = File.ReadAllLines(ConfigFiles.DeployTemplate(host))
            .Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-Za-z][A-Za-z0-9_]*__\d+=\s*$"))
            .ToList();

        blankElements.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_blank_array_element_defeats_the_trusted_proxy_check_which_is_why_the_templates_comment_it_out()
    {
        // Documents the trap: this host starts in Production with a blank trusted network, so the template must never contain one.
        using var environment = new ScopedEnvironment(CleanEnvironment);
        using var blankElement = new ScopedEnvironment(("TrustedProxy__TrustedProxies__0", " "));

        var failure = await ConfigHosts.TryStartAsync(HostKind.Portal, "Production", BlankTemplate(HostKind.Portal));

        failure.ShouldBeNull($"A blank element was expected to satisfy the check: {failure}");
    }
}
```

Update the stale-key expectations of `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs` (the Worker reads no storage or portal URL; the Portal reads no Api address or public URL until PHASE-09):

In `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`, replace:

```text
                .. OptionKeys(typeof(EmailOutboxWorkerOptions), EmailOutboxWorkerOptions.SectionName),
                "STORAGE__LOCAL__ROOTPATH",
                "TECHSTRAP_PORTAL_PUBLIC_URL",
                "TECHSTRAP_PORTAL_SHOW_POWERED_BY",
```

with:

```text
                .. OptionKeys(typeof(EmailOutboxWorkerOptions), EmailOutboxWorkerOptions.SectionName),
                "TECHSTRAP_PORTAL_SHOW_POWERED_BY",
```

In `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`, replace:

```text
                .. TrustedProxyKeys(),
                "API__BASEURL",
                "TECHSTRAP_PORTAL_PUBLIC_URL",
                "DATAPROTECTION__KEYRINGPATH",
            ]
```

with:

```text
                .. TrustedProxyKeys(),
                "TECHSTRAP_PORTAL_SHOW_POWERED_BY",
                "DATAPROTECTION__KEYRINGPATH",
            ]
```

In `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`, replace:

```text
    [Fact]
    public void The_key_enumerator_produces_the_expected_names()
```

with:

```text
    [Theory]
    [InlineData("TechStrap.Worker", "STORAGE__LOCAL__ROOTPATH")]
    [InlineData("TechStrap.Worker", "TECHSTRAP_PORTAL_PUBLIC_URL")]
    [InlineData("TechStrap.Portal", "API__BASEURL")]
    [InlineData("TechStrap.Portal", "TECHSTRAP_PORTAL_PUBLIC_URL")]
    public void Env_example_does_not_document_a_key_the_host_never_reads(string host, string staleKey)
    {
        // The Worker registers no attachment storage and builds no portal links; the Portal reads no Api address or public URL until PHASE-09 adds them.
        DocumentedKeys(host).ShouldNotContain(staleKey);
    }

    [Fact]
    public void The_key_enumerator_produces_the_expected_names()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then `dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter "FullyQualifiedName~Config|EnvExampleCompleteness"`
Expected: FAIL, 19 of 40 failed (the deploy templates do not exist, the Worker and Portal examples still list the stale keys, and a Production host starts without the template).

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1`
Expected: FAIL, 38 of 47 failed (the appsettings files do not list the keys).

- [ ] **Step 3: Add the settings, the examples and the deploy templates**

Replace each `appsettings.json` (the Sentry, OpenTelemetry, Serilog, `AllowedHosts` and `SassCompiler` blocks are unchanged; the new sections come first):

`src/TechStrap.Api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "TechStrap": ""
  },
  "Database": {
    "MigrateOnStartup": true
  },
  "TECHSTRAP_SEED_DEV_DATA": "false",
  "Authentication": {
    "JwtBearer": {
      "Authority": "",
      "Audiences": [],
      "RequireHttpsMetadata": true
    }
  },
  "TECHSTRAP_AGENT_GROUP": "techstrap-agents",
  "TECHSTRAP_ADMIN_GROUP": "techstrap-admins",
  "TECHSTRAP_GROUP_CLAIM_TYPE": "groups",
  "RateLimiting": {
    "Public": {
      "PermitLimit": 120,
      "WindowSeconds": 60
    },
    "Intake": {
      "WebFormPermitLimit": 5,
      "WebFormWindowSeconds": 600,
      "PublicKeyPermitLimit": 10,
      "PublicKeyWindowSeconds": 60,
      "TrustedKeyPermitLimit": 120,
      "TrustedKeyWindowSeconds": 60
    },
    "Customer": {
      "TokenAccessPermitLimit": 60,
      "TokenAccessWindowSeconds": 60,
      "LostLinkPermitLimit": 5,
      "LostLinkWindowSeconds": 3600
    }
  },
  "LostLink": {
    "MaxLinks": 5,
    "PerAddressLimit": 3,
    "PerAddressWindowMinutes": 60
  },
  "TECHSTRAP_PORTAL_PUBLIC_URL": "",
  "TECHSTRAP_ADMIN_PUBLIC_URL": "",
  "TECHSTRAP_AUTOCLOSE_DAYS": "7",
  "Storage": {
    "Provider": "Local",
    "Local": {
      "RootPath": ""
    }
  },
  "TrustedProxy": {
    "TrustedProxies": [],
    "TrustedNetworks": [],
    "RequireTrustedProxiesInProduction": true
  },
  "SecurityHeaders": {
    "ReferrerPolicy": "strict-origin-when-cross-origin",
    "FrameOptions": "DENY",
    "ContentTypeOptions": "nosniff",
    "PermissionsPolicy": "camera=(), geolocation=(), microphone=()",
    "StrictTransportSecurity": "max-age=31536000; includeSubDomains",
    "RobotsTag": ""
  },
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
      "Override": {
        "Microsoft.AspNetCore": "Warning"
      }
    },
    "WriteTo": [ { "Name": "Console" } ]
  },
  "AllowedHosts": "*"
}
```

`src/TechStrap.Worker/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "TechStrap": ""
  },
  "Email": {
    "Smtp": {
      "Host": "",
      "Port": 587,
      "Username": "",
      "Password": "",
      "UseStartTls": true,
      "DefaultFrom": "",
      "MaxRetryAttempts": 3,
      "TlsMode": "",
      "RetryMode": "Legacy",
      "TotalSendTimeout": ""
    }
  },
  "EmailOutbox": {
    "Enabled": true,
    "PollIntervalSeconds": 5,
    "BatchSize": 20,
    "LeaseSeconds": 900,
    "WorkerId": ""
  },
  "TECHSTRAP_PORTAL_SHOW_POWERED_BY": "true",
  "TECHSTRAP_AUTOCLOSE_DAYS": "7",
  "AutoClose": {
    "Enabled": true,
    "IntervalMinutes": 15,
    "BatchSize": 50
  },
  "OutboxRetention": {
    "Enabled": true,
    "Days": 90,
    "IntervalMinutes": 60,
    "BatchSize": 500
  },
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
      "Override": {
        "Microsoft.AspNetCore": "Warning"
      }
    },
    "WriteTo": [ { "Name": "Console" } ]
  },
  "AllowedHosts": "*"
}
```

`src/TechStrap.Admin/appsettings.json`:

```json
{
  "Api": {
    "BaseUrl": "",
    "TimeoutSeconds": 30
  },
  "Auth": {
    "Authority": "",
    "ClientId": "",
    "ClientSecret": ""
  },
  "TECHSTRAP_AGENT_GROUP": "techstrap-agents",
  "TECHSTRAP_ADMIN_GROUP": "techstrap-admins",
  "TECHSTRAP_GROUP_CLAIM_TYPE": "groups",
  "DataProtection": {
    "KeyRingPath": ""
  },
  "TrustedProxy": {
    "TrustedProxies": [],
    "TrustedNetworks": [],
    "RequireTrustedProxiesInProduction": true
  },
  "SecurityHeaders": {
    "ReferrerPolicy": "strict-origin-when-cross-origin",
    "FrameOptions": "DENY",
    "ContentTypeOptions": "nosniff",
    "PermissionsPolicy": "camera=(), geolocation=(), microphone=()",
    "StrictTransportSecurity": "max-age=31536000; includeSubDomains",
    "RobotsTag": ""
  },
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
      "Override": {
        "Microsoft.AspNetCore": "Warning"
      }
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

`src/TechStrap.Portal/appsettings.json`:

```json
{
  "TECHSTRAP_PORTAL_SHOW_POWERED_BY": "true",
  "DataProtection": {
    "KeyRingPath": ""
  },
  "TrustedProxy": {
    "TrustedProxies": [],
    "TrustedNetworks": [],
    "RequireTrustedProxiesInProduction": true
  },
  "SecurityHeaders": {
    "ReferrerPolicy": "strict-origin-when-cross-origin",
    "FrameOptions": "DENY",
    "ContentTypeOptions": "nosniff",
    "PermissionsPolicy": "camera=(), geolocation=(), microphone=()",
    "StrictTransportSecurity": "max-age=31536000; includeSubDomains",
    "RobotsTag": ""
  },
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
      "Override": {
        "Microsoft.AspNetCore": "Warning"
      }
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

`src/TechStrap.Admin/appsettings.Development.json` (the Api and Portal files keep only their trusted-proxy documentation network; the Worker has none). The placeholders are the ones the local compose set until now, moved here so `.env.local` and compose stop clashing:

```json
{
  "TrustedProxy": {
    "TrustedProxies": [],
    "TrustedNetworks": [ "192.0.2.0/24" ],
    "RequireTrustedProxiesInProduction": true
  },
  "Auth": {
    "Authority": "https://authentik.invalid/application/o/techstrap-admin/",
    "ClientId": "techstrap-admin",
    "ClientSecret": "not-configured"
  }
}
```

Replace each `.env.example`. The Admin file keeps `AUTH__*` commented out on purpose: a blank value from `.env.local` replaces the Development placeholder and stops the Admin.

`src/TechStrap.Api/.env.example`:

```text
# Copy to .env.local in this directory (gitignored). SyntaxCircus.DotEnv loads .env then .env.local in Development only.
# In containers, the same names arrive as real environment variables; see docker-compose.yml (local) and deploy/docker-compose.yml.
# Use SECTION__KEY (ALL_CAPS) naming for ASP.NET Core config binding.
# Keep this file in sync with appsettings.json and deploy/.env.api.example: scripts/tests/ConfigContract.Tests.ps1 checks all three.
# A commented-out key (# KEY=) is documented but optional or compose-supplied; leave an array element (__0) out rather than blank, because a blank element still counts as configured.

# --- Database ---
ConnectionStrings__TechStrap=Host=localhost;Port=5432;Database=techstrap;Username=techstrap;Password=replace-me
# The API migrates on startup under an advisory lock. Set to false to skip.
DATABASE__MIGRATEONSTARTUP=true
# Development only: run the development data seeder after migration.
TECHSTRAP_SEED_DEV_DATA=false

# --- Agent authentication (OIDC JWT bearer) ---
# Required outside Development: the Authority (issuer URL) and the first audience. Production refuses to start without both.
AUTHENTICATION__JWTBEARER__AUTHORITY=
# AUTHENTICATION__JWTBEARER__AUDIENCES__0=
AUTHENTICATION__JWTBEARER__REQUIREHTTPSMETADATA=true
TECHSTRAP_AGENT_GROUP=techstrap-agents
TECHSTRAP_ADMIN_GROUP=techstrap-admins
# Claim that carries IdP group names (D-029). Authentik and most IdPs use "groups".
TECHSTRAP_GROUP_CLAIM_TYPE=groups

# --- Public rate limit (fixed window per client IP) ---
RATELIMITING__PUBLIC__PERMITLIMIT=120
RATELIMITING__PUBLIC__WINDOWSECONDS=60
# Intake limits (02-ARCHITECTURE 11.2): web form per IP; public keys per key and IP; trusted keys per key.
RATELIMITING__INTAKE__WEBFORMPERMITLIMIT=5
RATELIMITING__INTAKE__WEBFORMWINDOWSECONDS=600
RATELIMITING__INTAKE__PUBLICKEYPERMITLIMIT=10
RATELIMITING__INTAKE__PUBLICKEYWINDOWSECONDS=60
RATELIMITING__INTAKE__TRUSTEDKEYPERMITLIMIT=120
RATELIMITING__INTAKE__TRUSTEDKEYWINDOWSECONDS=60
# Customer link routes (D-038): per client IP.
RATELIMITING__CUSTOMER__TOKENACCESSPERMITLIMIT=60
RATELIMITING__CUSTOMER__TOKENACCESSWINDOWSECONDS=60
RATELIMITING__CUSTOMER__LOSTLINKPERMITLIMIT=5
RATELIMITING__CUSTOMER__LOSTLINKWINDOWSECONDS=3600
# Lost-link email (D-038): links per email (1..10) and the per-address cap (1..20 emails per 1..1440 minutes). Over the cap the request still answers 202 and sends nothing.
LOSTLINK__MAXLINKS=5
LOSTLINK__PERADDRESSLIMIT=3
LOSTLINK__PERADDRESSWINDOWMINUTES=60

# --- Public portal base URL used in customer links (required, absolute http or https) ---
TECHSTRAP_PORTAL_PUBLIC_URL=http://localhost:8082

# Optional: Admin app base URL; when set, assignment emails link to {url}/tickets/{number}.
# Example: http://localhost:8081
TECHSTRAP_ADMIN_PUBLIC_URL=

# --- Auto-close ---
# Days a Solved ticket stays open to a customer reply before auto-close (1..365). Also shown in customer emails; keep equal to the Worker value.
TECHSTRAP_AUTOCLOSE_DAYS=7

# --- Attachment storage (local provider) ---
STORAGE__PROVIDER=Local
STORAGE__LOCAL__ROOTPATH=/app/storage

# --- Trusted reverse proxy (API) ---
# Forwarded headers are trusted only from these addresses. Production refuses to start with none.
# Never trust 172.16.0.0/12 or 0.0.0.0/0. The compose files set index 0 to the compose subnet (172.16.31.0/24) and, in the deploy compose, index 1 to REVERSE_PROXY_CIDR.
# Optional: also trust one proxy address.
# TRUSTEDPROXY__TRUSTEDPROXIES__0=192.0.2.1
TRUSTEDPROXY__TRUSTEDNETWORKS__0=192.0.2.0/24
TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION=true

# --- Security headers (the package defaults; the Content-Security-Policy is set in code per host and is not configurable here) ---
SECURITYHEADERS__REFERRERPOLICY=strict-origin-when-cross-origin
SECURITYHEADERS__FRAMEOPTIONS=DENY
SECURITYHEADERS__CONTENTTYPEOPTIONS=nosniff
SECURITYHEADERS__PERMISSIONSPOLICY=camera=(), geolocation=(), microphone=()
SECURITYHEADERS__STRICTTRANSPORTSECURITY=max-age=31536000; includeSubDomains
# Optional X-Robots-Tag value; blank sends none.
SECURITYHEADERS__ROBOTSTAG=

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

```text
# Copy to .env.local in this directory (gitignored). SyntaxCircus.DotEnv loads .env then .env.local in Development only.
# In containers, the same names arrive as real environment variables; see docker-compose.yml (local) and deploy/docker-compose.yml.
# Use SECTION__KEY (ALL_CAPS) naming for ASP.NET Core config binding.
# Keep this file in sync with appsettings.json and deploy/.env.worker.example: scripts/tests/ConfigContract.Tests.ps1 checks all three.
# The Worker never migrates the database (the API owns migrations) and reads no storage or portal-URL setting.

# --- Database ---
ConnectionStrings__TechStrap=Host=localhost;Port=5432;Database=techstrap;Username=techstrap;Password=replace-me

# --- Outbound email (SyntaxCircus.Email SMTP) ---
# Host and DefaultFrom are required while the outbox is enabled; the Worker refuses to start without them.
EMAIL__SMTP__HOST=localhost
EMAIL__SMTP__PORT=1025
EMAIL__SMTP__USERNAME=
EMAIL__SMTP__PASSWORD=
EMAIL__SMTP__USESTARTTLS=true
EMAIL__SMTP__DEFAULTFROM=support@example.com
EMAIL__SMTP__MAXRETRYATTEMPTS=1
# Mailpit has no TLS; use StartTls or SslOnConnect for a real server. Blank keeps the library default (StartTls).
EMAIL__SMTP__TLSMODE=None
EMAIL__SMTP__RETRYMODE=TransientOnly
# Blank means no overall send deadline.
EMAIL__SMTP__TOTALSENDTIMEOUT=00:00:30

# --- Email outbox loop ---
# the lease must cover sending one whole batch: at least BATCHSIZE x EMAIL__SMTP__TOTALSENDTIMEOUT seconds + 60 (20 x 30 + 60 = 660); boot fails otherwise
EMAILOUTBOX__ENABLED=true
EMAILOUTBOX__POLLINTERVALSECONDS=5
EMAILOUTBOX__BATCHSIZE=20
EMAILOUTBOX__LEASESECONDS=900
# Blank: machine name plus a random suffix.
EMAILOUTBOX__WORKERID=

# --- "Powered by TechStrap" mark (D-024) ---
# Links to https://github.com/Syntax-Circus/techstrap and is shown by default. Set to false to hide it on every customer email (installation-wide); the Portal reads the same key.
TECHSTRAP_PORTAL_SHOW_POWERED_BY=true

# --- Auto-close ---
# Days a Solved ticket stays open to a customer reply (1..365); keep equal to the API value, as it is shown in emails.
TECHSTRAP_AUTOCLOSE_DAYS=7
AUTOCLOSE__ENABLED=true
AUTOCLOSE__INTERVALMINUTES=15
AUTOCLOSE__BATCHSIZE=50

# --- Outbox retention (D-039) ---
# Sent and Discarded email rows older than DAYS are deleted (1..3650); dead letters are never deleted. The sweep runs every INTERVALMINUTES.
OUTBOXRETENTION__ENABLED=true
OUTBOXRETENTION__DAYS=90
OUTBOXRETENTION__INTERVALMINUTES=60
OUTBOXRETENTION__BATCHSIZE=500

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

```text
# Copy to .env.local in this directory (gitignored). SyntaxCircus.DotEnv loads .env then .env.local in Development only.
# In containers, the same names arrive as real environment variables; see docker-compose.yml (local) and deploy/docker-compose.yml.
# Use SECTION__KEY (ALL_CAPS) naming for ASP.NET Core config binding.
# Keep this file in sync with appsettings.json, appsettings.Development.json and deploy/.env.admin.example: scripts/tests/ConfigContract.Tests.ps1 checks all of them.
# The Admin app never touches the database; it calls the API.

# --- API ---
# Required. The Admin calls the API with the signed-in agent's token. A request that takes longer than the timeout (1..300 seconds) fails.
API__BASEURL=http://localhost:8080/
API__TIMEOUTSECONDS=30

# --- Agent sign-in (OpenID Connect code flow with PKCE, any provider; docs/development/ADMIN-APP.md) ---
# Required: the Admin refuses to start without all three. A confidential client with redirect URI {admin url}/signin-oidc,
# post-logout URI {admin url}/signout-callback-oidc and the scopes openid profile email offline_access (the scopes are the library default and are not listed here).
# In Development, appsettings.Development.json supplies placeholders (https://authentik.invalid/..., not-configured) so the app starts without an identity provider.
# Uncomment and fill these in .env.local to sign in for real; a blank value would replace the placeholder and stop the start.
# AUTH__AUTHORITY=
# AUTH__CLIENTID=
# AUTH__CLIENTSECRET=

# --- Agent and admin groups (the same keys and defaults as the API; the API decides who has access) ---
TECHSTRAP_AGENT_GROUP=techstrap-agents
TECHSTRAP_ADMIN_GROUP=techstrap-admins
TECHSTRAP_GROUP_CLAIM_TYPE=groups

# --- ASP.NET Core data protection key ring (persisted volume in containers; blank keeps keys in memory) ---
DATAPROTECTION__KEYRINGPATH=

# --- Trusted reverse proxy (Admin) ---
# Forwarded headers are trusted only from these addresses. Production refuses to start with none.
# Never trust 172.16.0.0/12 or 0.0.0.0/0. Admin trusts only the reverse proxy.
# Optional: also trust one proxy address.
# TRUSTEDPROXY__TRUSTEDPROXIES__0=192.0.2.1
TRUSTEDPROXY__TRUSTEDNETWORKS__0=192.0.2.0/24
TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION=true

# --- Security headers (the package defaults; the Content-Security-Policy is set in code per host and is not configurable here) ---
SECURITYHEADERS__REFERRERPOLICY=strict-origin-when-cross-origin
SECURITYHEADERS__FRAMEOPTIONS=DENY
SECURITYHEADERS__CONTENTTYPEOPTIONS=nosniff
SECURITYHEADERS__PERMISSIONSPOLICY=camera=(), geolocation=(), microphone=()
SECURITYHEADERS__STRICTTRANSPORTSECURITY=max-age=31536000; includeSubDomains
# Optional X-Robots-Tag value; blank sends none.
SECURITYHEADERS__ROBOTSTAG=

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

```text
# Copy to .env.local in this directory (gitignored). SyntaxCircus.DotEnv loads .env then .env.local in Development only.
# In containers, the same names arrive as real environment variables; see docker-compose.yml (local) and deploy/docker-compose.yml.
# Use SECTION__KEY (ALL_CAPS) naming for ASP.NET Core config binding.
# Keep this file in sync with appsettings.json and deploy/.env.portal.example: scripts/tests/ConfigContract.Tests.ps1 checks all three.
# The Portal never touches the database; it calls the API. It reads no API address and no public URL yet: PHASE-09 adds API__BASEURL and TECHSTRAP_PORTAL_PUBLIC_URL here.

# --- "Powered by TechStrap" mark (D-024) ---
# Links to https://github.com/Syntax-Circus/techstrap and is shown by default. Set to false to hide it on every portal page and customer email (installation-wide).
TECHSTRAP_PORTAL_SHOW_POWERED_BY=true

# --- ASP.NET Core data protection key ring (persisted volume in containers; blank keeps keys in memory) ---
DATAPROTECTION__KEYRINGPATH=

# --- Trusted reverse proxy (Portal) ---
# Forwarded headers are trusted only from these addresses. Production refuses to start with none.
# Never trust 172.16.0.0/12 or 0.0.0.0/0. Portal trusts only the reverse proxy.
# Optional: also trust one proxy address.
# TRUSTEDPROXY__TRUSTEDPROXIES__0=192.0.2.1
TRUSTEDPROXY__TRUSTEDNETWORKS__0=192.0.2.0/24
TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION=true

# --- Security headers (the package defaults; the Content-Security-Policy is set in code per host and is not configurable here) ---
SECURITYHEADERS__REFERRERPOLICY=strict-origin-when-cross-origin
SECURITYHEADERS__FRAMEOPTIONS=DENY
SECURITYHEADERS__CONTENTTYPEOPTIONS=nosniff
SECURITYHEADERS__PERMISSIONSPOLICY=camera=(), geolocation=(), microphone=()
SECURITYHEADERS__STRICTTRANSPORTSECURITY=max-age=31536000; includeSubDomains
# Optional X-Robots-Tag value; blank sends none.
SECURITYHEADERS__ROBOTSTAG=

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

Create the four deploy templates. They list the same keys as the project's `appsettings.json` minus the keys compose owns (Task 4) and the Development-only `TECHSTRAP_SEED_DEV_DATA`. Numbers carry their real default; the production values the old compose set (`TlsMode` StartTls, `RetryMode` TransientOnly, one retry, a 30 s total timeout) are the Worker template's values. Blank means the operator must fill it in.

`deploy/.env.api.example`:

```text
# Key-only template for the Api container: copy to /etc/techstrap/<uat|production>/.env.api
# (root-owned, mode 0600). deploy/docker-compose.yml loads it as this container's env_file (format raw: no quotes, no $ interpolation).
# Keep this file in sync with src/TechStrap.Api/appsettings.json and src/TechStrap.Api/.env.example: scripts/tests/ConfigContract.Tests.ps1 checks all three.
# Compose owns and overrides ASPNETCORE_ENVIRONMENT, DOTENV__ENABLED, STORAGE__LOCAL__ROOTPATH and TRUSTEDPROXY__TRUSTEDNETWORKS__0 and __1 (the subnet and REVERSE_PROXY_CIDR),
# so they are not listed here. TECHSTRAP_SEED_DEV_DATA is Development only and is not listed either.
# A blank required value stops the container at start and its log names the missing key. Never commit a filled copy.
# A commented-out key (# KEY=) is optional or compose-supplied; an array element (__0) is never left blank, because a blank element still counts as configured.

# -- Database [Api, Worker] --
# Required for every container that reaches the database: the Postgres host name on the shared Docker network (TECHSTRAP_DB_NETWORK), for example
#   Host=postgres;Port=5432;Database=techstrap;Username=techstrap;Password=<password>
# The password sits inside a connection string: use only letters, digits and - _ . ~ (for example the output of openssl rand -hex 24).
# A semicolon, equals sign, quote, space or other punctuation breaks the string or lets it inject an option. The env file is read raw, so write no quotes around the value.
ConnectionStrings__TechStrap=

# -- Migrations [Api] --
# The Api migrates on startup under an advisory lock (the Worker never migrates). Set to false to skip.
DATABASE__MIGRATEONSTARTUP=true

# -- Agent authentication [Api] (OIDC JWT bearer) --
# Required: the issuer URL and the first audience. The Api refuses to start without both.
AUTHENTICATION__JWTBEARER__AUTHORITY=
# AUTHENTICATION__JWTBEARER__AUDIENCES__0=
AUTHENTICATION__JWTBEARER__REQUIREHTTPSMETADATA=true

# -- Agent and admin groups [Api, Admin] --
# The same three keys and values in the Api and the Admin; the Api decides who has access. The two groups must differ.
TECHSTRAP_AGENT_GROUP=techstrap-agents
TECHSTRAP_ADMIN_GROUP=techstrap-admins
# Claim that carries IdP group names (D-029). Authentik and most IdPs use "groups".
TECHSTRAP_GROUP_CLAIM_TYPE=groups

# -- Rate limits [Api] (fixed window per client IP unless noted) --
RATELIMITING__PUBLIC__PERMITLIMIT=120
RATELIMITING__PUBLIC__WINDOWSECONDS=60
RATELIMITING__INTAKE__WEBFORMPERMITLIMIT=5
RATELIMITING__INTAKE__WEBFORMWINDOWSECONDS=600
RATELIMITING__INTAKE__PUBLICKEYPERMITLIMIT=10
RATELIMITING__INTAKE__PUBLICKEYWINDOWSECONDS=60
RATELIMITING__INTAKE__TRUSTEDKEYPERMITLIMIT=120
RATELIMITING__INTAKE__TRUSTEDKEYWINDOWSECONDS=60
RATELIMITING__CUSTOMER__TOKENACCESSPERMITLIMIT=60
RATELIMITING__CUSTOMER__TOKENACCESSWINDOWSECONDS=60
RATELIMITING__CUSTOMER__LOSTLINKPERMITLIMIT=5
RATELIMITING__CUSTOMER__LOSTLINKWINDOWSECONDS=3600
# Lost-link email (D-038): links per email (1..10) and the per-address cap (1..20 emails per 1..1440 minutes).
LOSTLINK__MAXLINKS=5
LOSTLINK__PERADDRESSLIMIT=3
LOSTLINK__PERADDRESSWINDOWMINUTES=60

# -- Public URLs [Api] --
# Required: the customer portal base URL as customers see it, absolute http or https (emailed links and canonical URLs).
TECHSTRAP_PORTAL_PUBLIC_URL=
# Optional: the Admin base URL; when set, assignment emails link to {url}/tickets/{number}.
TECHSTRAP_ADMIN_PUBLIC_URL=

# -- Auto-close [Api, Worker] --
# Days a Solved ticket stays open to a customer reply (1..365). Shown in customer emails, so keep it equal in the Api and the Worker.
TECHSTRAP_AUTOCLOSE_DAYS=7

# -- Attachment storage [Api] --
STORAGE__PROVIDER=Local

# -- Trusted reverse proxy [Api, Admin, Portal] --
# Compose sets the trusted networks. Optional: also trust one proxy address.
# TRUSTEDPROXY__TRUSTEDPROXIES__0=
TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION=true

# -- Security headers [Api, Admin, Portal] --
# The package defaults; the Content-Security-Policy is set in code per host and is not configurable here.
SECURITYHEADERS__REFERRERPOLICY=strict-origin-when-cross-origin
SECURITYHEADERS__FRAMEOPTIONS=DENY
SECURITYHEADERS__CONTENTTYPEOPTIONS=nosniff
SECURITYHEADERS__PERMISSIONSPOLICY=camera=(), geolocation=(), microphone=()
SECURITYHEADERS__STRICTTRANSPORTSECURITY=max-age=31536000; includeSubDomains
# Optional X-Robots-Tag value; blank sends none.
SECURITYHEADERS__ROBOTSTAG=

# -- Logging and observability [Api, Worker, Admin, Portal] (all optional) --
# The Sentry DSN and the OTLP headers are secrets: they stay in this root-owned file, never in the repository.
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

`deploy/.env.worker.example`:

```text
# Key-only template for the Worker container: copy to /etc/techstrap/<uat|production>/.env.worker
# (root-owned, mode 0600). deploy/docker-compose.yml loads it as this container's env_file (format raw: no quotes, no $ interpolation).
# Keep this file in sync with src/TechStrap.Worker/appsettings.json and src/TechStrap.Worker/.env.example: scripts/tests/ConfigContract.Tests.ps1 checks all three.
# Compose owns and overrides ASPNETCORE_ENVIRONMENT and DOTENV__ENABLED. The Worker never migrates the database and reads no storage or portal-URL setting.
# A blank required value stops the container at start and its log names the missing key. Never commit a filled copy.
# A commented-out key (# KEY=) is optional or compose-supplied; an array element (__0) is never left blank, because a blank element still counts as configured.

# -- Database [Api, Worker] --
# Required for every container that reaches the database: the Postgres host name on the shared Docker network (TECHSTRAP_DB_NETWORK), for example
#   Host=postgres;Port=5432;Database=techstrap;Username=techstrap;Password=<password>
# The password sits inside a connection string: use only letters, digits and - _ . ~ (for example the output of openssl rand -hex 24).
# A semicolon, equals sign, quote, space or other punctuation breaks the string or lets it inject an option. The env file is read raw, so write no quotes around the value.
ConnectionStrings__TechStrap=

# -- Outbound email [Worker] (SyntaxCircus.Email SMTP) --
# Required while the outbox is enabled: the SMTP host and the sender address. The Worker refuses to start without them.
EMAIL__SMTP__HOST=
EMAIL__SMTP__PORT=587
EMAIL__SMTP__USERNAME=
EMAIL__SMTP__PASSWORD=
EMAIL__SMTP__USESTARTTLS=true
EMAIL__SMTP__DEFAULTFROM=
EMAIL__SMTP__MAXRETRYATTEMPTS=1
# One of None, Auto, StartTls, SslOnConnect or StartTlsWhenAvailable.
EMAIL__SMTP__TLSMODE=StartTls
EMAIL__SMTP__RETRYMODE=TransientOnly
EMAIL__SMTP__TOTALSENDTIMEOUT=00:00:30

# -- Email outbox loop [Worker] --
# The lease must cover sending one whole batch: at least BATCHSIZE x EMAIL__SMTP__TOTALSENDTIMEOUT seconds + 60 (20 x 30 + 60 = 660; the default 900 leaves margin).
# The Worker fails to boot otherwise. To run without email, set EMAILOUTBOX__ENABLED=false.
EMAILOUTBOX__ENABLED=true
EMAILOUTBOX__POLLINTERVALSECONDS=5
EMAILOUTBOX__BATCHSIZE=20
EMAILOUTBOX__LEASESECONDS=900
# Blank: machine name plus a random suffix.
EMAILOUTBOX__WORKERID=

# -- "Powered by TechStrap" mark [Worker, Portal] (D-024) --
# Links to https://github.com/Syntax-Circus/techstrap and is shown by default. Set to false to hide it on every portal page and customer email (installation-wide).
TECHSTRAP_PORTAL_SHOW_POWERED_BY=true

# -- Auto-close [Api, Worker] --
# Days a Solved ticket stays open to a customer reply (1..365). Shown in customer emails, so keep it equal in the Api and the Worker.
TECHSTRAP_AUTOCLOSE_DAYS=7
AUTOCLOSE__ENABLED=true
AUTOCLOSE__INTERVALMINUTES=15
AUTOCLOSE__BATCHSIZE=50

# -- Outbox retention [Worker] (D-039) --
# Sent and Discarded outbox rows older than DAYS are deleted (1..3650); dead letters are kept until an admin retries or discards them.
OUTBOXRETENTION__ENABLED=true
OUTBOXRETENTION__DAYS=90
OUTBOXRETENTION__INTERVALMINUTES=60
OUTBOXRETENTION__BATCHSIZE=500

# -- Logging and observability [Api, Worker, Admin, Portal] (all optional) --
# The Sentry DSN and the OTLP headers are secrets: they stay in this root-owned file, never in the repository.
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

`deploy/.env.admin.example`:

```text
# Key-only template for the Admin container: copy to /etc/techstrap/<uat|production>/.env.admin
# (root-owned, mode 0600). deploy/docker-compose.yml loads it as this container's env_file (format raw: no quotes, no $ interpolation).
# Keep this file in sync with src/TechStrap.Admin/appsettings.json and src/TechStrap.Admin/.env.example: scripts/tests/ConfigContract.Tests.ps1 checks all three.
# Compose owns and overrides ASPNETCORE_ENVIRONMENT, DOTENV__ENABLED, API__BASEURL, DATAPROTECTION__KEYRINGPATH and TRUSTEDPROXY__TRUSTEDNETWORKS__0 (REVERSE_PROXY_CIDR),
# so they are not listed here. The Admin never touches the database; it calls the Api.
# A blank required value stops the container at start and its log names the missing key. Never commit a filled copy.
# A commented-out key (# KEY=) is optional or compose-supplied; an array element (__0) is never left blank, because a blank element still counts as configured.


# -- Api client [Admin] --
# A request that takes longer than the timeout (1..300 seconds) fails.
API__TIMEOUTSECONDS=30

# -- Agent sign-in [Admin] (OpenID Connect code flow with PKCE, any provider; docs/development/ADMIN-APP.md) --
# Required: the Admin refuses to start without all three. A confidential client with redirect URI {admin url}/signin-oidc, post-logout URI
# {admin url}/signout-callback-oidc and the scopes openid profile email offline_access (the library default, not listed here). The Authority must be https.
AUTH__AUTHORITY=
AUTH__CLIENTID=
AUTH__CLIENTSECRET=

# -- Agent and admin groups [Api, Admin] --
# The same three keys and values in the Api and the Admin; the Api decides who has access. The two groups must differ.
TECHSTRAP_AGENT_GROUP=techstrap-agents
TECHSTRAP_ADMIN_GROUP=techstrap-admins
# Claim that carries IdP group names (D-029). Authentik and most IdPs use "groups".
TECHSTRAP_GROUP_CLAIM_TYPE=groups

# -- Trusted reverse proxy [Api, Admin, Portal] --
# Compose sets the trusted network. Optional: also trust one proxy address.
# TRUSTEDPROXY__TRUSTEDPROXIES__0=
TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION=true

# -- Security headers [Api, Admin, Portal] --
# The package defaults; the Content-Security-Policy is set in code per host and is not configurable here.
SECURITYHEADERS__REFERRERPOLICY=strict-origin-when-cross-origin
SECURITYHEADERS__FRAMEOPTIONS=DENY
SECURITYHEADERS__CONTENTTYPEOPTIONS=nosniff
SECURITYHEADERS__PERMISSIONSPOLICY=camera=(), geolocation=(), microphone=()
SECURITYHEADERS__STRICTTRANSPORTSECURITY=max-age=31536000; includeSubDomains
# Optional X-Robots-Tag value; blank sends none.
SECURITYHEADERS__ROBOTSTAG=

# -- Logging and observability [Api, Worker, Admin, Portal] (all optional) --
# The Sentry DSN and the OTLP headers are secrets: they stay in this root-owned file, never in the repository.
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

`deploy/.env.portal.example`:

```text
# Key-only template for the Portal container: copy to /etc/techstrap/<uat|production>/.env.portal
# (root-owned, mode 0600). deploy/docker-compose.yml loads it as this container's env_file (format raw: no quotes, no $ interpolation).
# Keep this file in sync with src/TechStrap.Portal/appsettings.json and src/TechStrap.Portal/.env.example: scripts/tests/ConfigContract.Tests.ps1 checks all three.
# Compose owns and overrides ASPNETCORE_ENVIRONMENT, DOTENV__ENABLED, DATAPROTECTION__KEYRINGPATH and TRUSTEDPROXY__TRUSTEDNETWORKS__0 (REVERSE_PROXY_CIDR),
# so they are not listed here. The Portal never touches the database and reads no Api address or public URL yet: PHASE-09 adds API__BASEURL and TECHSTRAP_PORTAL_PUBLIC_URL.
# A blank required value stops the container at start and its log names the missing key. Never commit a filled copy.
# A commented-out key (# KEY=) is optional or compose-supplied; an array element (__0) is never left blank, because a blank element still counts as configured.


# -- "Powered by TechStrap" mark [Worker, Portal] (D-024) --
# Links to https://github.com/Syntax-Circus/techstrap and is shown by default. Set to false to hide it on every portal page and customer email (installation-wide).
TECHSTRAP_PORTAL_SHOW_POWERED_BY=true

# -- Trusted reverse proxy [Api, Admin, Portal] --
# Compose sets the trusted network. Optional: also trust one proxy address.
# TRUSTEDPROXY__TRUSTEDPROXIES__0=
TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION=true

# -- Security headers [Api, Admin, Portal] --
# The package defaults; the Content-Security-Policy is set in code per host and is not configurable here.
SECURITYHEADERS__REFERRERPOLICY=strict-origin-when-cross-origin
SECURITYHEADERS__FRAMEOPTIONS=DENY
SECURITYHEADERS__CONTENTTYPEOPTIONS=nosniff
SECURITYHEADERS__PERMISSIONSPOLICY=camera=(), geolocation=(), microphone=()
SECURITYHEADERS__STRICTTRANSPORTSECURITY=max-age=31536000; includeSubDomains
# Optional X-Robots-Tag value; blank sends none.
SECURITYHEADERS__ROBOTSTAG=

# -- Logging and observability [Api, Worker, Admin, Portal] (all optional) --
# The Sentry DSN and the OTLP headers are secrets: they stay in this root-owned file, never in the repository.
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

In `CONTRIBUTING.md`, replace:

```text
- Secrets never go in source. Add new settings to the matching `.env.example`; `.env.local` stays local.
```

with:

```text
- Secrets never go in source. A new setting goes in four places: the host's `appsettings.json` (the real default, or blank for a secret or an environment-specific value), its `.env.example`,
  the matching `deploy/.env.<app>.example` (unless compose owns it or it is Development only) and, for a compose-owned value, the compose file. A blank number, flag or enum fails binding, so give it its real default.
  `scripts/tests/ConfigContract.Tests.ps1` fails until the files agree; `.env.local` stays local.
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then `dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter "FullyQualifiedName~Config|EnvExampleCompleteness"`
Expected: PASS, 40 tests (`DevelopmentStartupTests` and `ProductionBlankTemplateTests` run in `ProcessEnvironmentCollection`: they set process environment variables).

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1`
Expected: PASS, 47 tests.

To see the pins guard (do not commit these edits), run `git add src deploy tests scripts/tests` first, so `git checkout -- <file>` restores the passing version. Make each change below, rerun the Pester command (the `dotnet test` command for the `X` lines), expect the failures named, and restore the file. These were run in the scratch copy:

- `src/TechStrap.Worker/appsettings.json`: change `"Port": 587` to `"Port": ""`. Fails the `blank only where blank is valid` rule for the Worker and, in `dotnet test`, `A_host_starts_in_Development_from_its_appsettings_and_only_the_settings_it_cannot_default` (Worker): the blank cannot be converted to an integer. The same holds for `"TimeoutSeconds": ""` in the Admin file (3 `DevelopmentStartupTests` fail) and `"RetryMode": ""` in the Worker file. A blank `RequireHttpsMetadata` binds as null, so only the contract catches it.
- `src/TechStrap.Admin/appsettings.json`: add `"Scopes": ["openid","profile","email","offline_access"]` to `Auth`. Fails the two key-parity rules for the Admin (the library would append the defaults twice).
- `src/TechStrap.Api/appsettings.json`: change `"TrustedNetworks": []` to `"TrustedNetworks": [""]`. Fails `blank only where blank is valid` and `no array element is blank in appsettings.json` for the Api. In `src/TechStrap.Portal/appsettings.json`, `"TrustedNetworks": [" "]` fails `ProductionBlankTemplateTests.The_blank_template_alone_fails_start_naming_what_is_missing` for the Portal: the Production check passes with one blank network.
- `deploy/.env.api.example`: `SENTRY__DSN=https://abc@sentry.example/1` fails `no committed file holds a non-blank secret-shaped value`. Removing `DATABASE__MIGRATEONSTARTUP=true` fails the deploy-template parity rule.
- `deploy/.env.api.example`: `TECHSTRAP_PORTAL_PUBLIC_URL=https://support.example.com` fails both `ProductionBlankTemplateTests` Api cases (the failure no longer names it). `deploy/.env.admin.example`: `AUTH__CLIENTSECRET=hunter2` fails both Admin cases.
- `deploy/.env.api.example`: uncomment `AUTHENTICATION__JWTBEARER__AUDIENCES__0=` (blank). Fails `a blank value in .env.example or the deploy template is on the blank list, and an array element is never left blank`. `deploy/.env.admin.example`: uncomment `TRUSTEDPROXY__TRUSTEDPROXIES__0=`. Fails `No_deploy_template_leaves_an_array_element_blank` (Admin).
- `src/TechStrap.Worker/.env.example`: add `STORAGE__LOCAL__ROOTPATH=/app/storage`. Fails the key-parity rule for the Worker. `src/TechStrap.Portal/.env.example`: add `API__BASEURL=http://localhost:8080/`. Fails `Env_example_does_not_document_a_key_the_host_never_reads` (Portal, `API__BASEURL`).
- `src/TechStrap.Admin/appsettings.Development.json`: rename `"Auth"` to `"AuthX"`. Fails `The_Admin_starts_in_Development_without_any_sign_in_setting_of_its_own` and the Admin case of both `A_host_starts_in_Development_...` theories. Changing the Authority to `https://auth.example.com/...` fails `the Admin Development placeholders are clearly fake`.

- [ ] **Step 5: Whole-project check**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then `dotnet test --solution TechStrap.CI.slnf -c Release` and `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS. The Admin tests that start the host with `AdminTestSettings` are unaffected by the new Development placeholders (they override every Auth key). The Postgres-backed tests can fail once with `Failed to connect to 127.0.0.1:<port> ... lacked sufficient buffer space` on Windows right after a full rebuild; that is socket exhaustion, not this change: rerun.

- [ ] **Step 6: Commit**

```bash
git add src \
  deploy \
  tests/TechStrap.Api.Tests \
  scripts/tests/ConfigContract.Tests.ps1 \
  CONTRIBUTING.md
git diff --cached --stat
git commit -m "feat(config): every host setting in appsettings.json, scoped .env examples, a config contract and Production fail-fast tests (D-043)" -m "Each host's appsettings.json now lists every setting it reads: the code default, or blank for a secret or an environment-specific value. A blank number, flag or enum fails binding, so those keep their real default; an array stays empty because a blank element counts as configured. The four .env.example files and the four deploy templates carry the same keys, the Worker and Portal examples lose the keys they never read, and the Admin Development placeholders move from compose into appsettings.Development.json. ConfigContract.Tests.ps1 pins the key parity both ways, the blank rules and the secret rule; ProductionBlankTemplateTests starts each host in Production from the blank template and expects the missing keys by name." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 3: The local compose takes its settings from `.env.local` and appsettings

**Review Focus pin:**
- **(4)** The local stack must still start the dev stack and the Admin must not clash with `.env.local`. Pinned by `ComposeFiles.Tests.ps1` (the Admin environment has no `Auth__*` or group keys, every app builds from this checkout, the stack resolves with no `.env`), by the contract rules on the local compose, and by running `scripts/Test-ComposeSmoke.ps1` (Step 5), which builds the four images and starts the stack.

**Files:**
- Modify: `docker-compose.yml`
- Create: `.env.example` (root: the four compose inputs)
- Modify: `scripts/tests/ConfigContract.Tests.ps1`, `scripts/tests/ComposeFiles.Tests.ps1`
- Modify: `README.md`, `docs/development/ADMIN-APP.md`

**Interfaces:**
- Consumes: `Get-ComposeEnvironmentKeys`, `Get-AppsettingsKeys`, `Get-EnvEntries` and `$script:ComposeNonSettings` from Task 2; `Get-ComposeConfig` in `ComposeFiles.Tests.ps1`.
- Produces: the local compose `environment:` keeps only the wiring compose owns. Per service: api `ASPNETCORE_ENVIRONMENT`, `ConnectionStrings__TechStrap`, `TRUSTEDPROXY__TRUSTEDNETWORKS__0` (the subnet), `TECHSTRAP_PORTAL_PUBLIC_URL` (the portal port compose publishes), `TECHSTRAP_SEED_DEV_DATA` (a compose input), `Storage__Local__RootPath`; admin `ASPNETCORE_ENVIRONMENT`, `Api__BaseUrl`, `TRUSTEDPROXY__TRUSTEDNETWORKS__0` (`REVERSE_PROXY_CIDR`), `DataProtection__KeyRingPath`; portal the same without `Api__BaseUrl`; worker the connection string and the Mailpit `Email__Smtp__*` values. The Worker loses its storage volume and the keys it never read. The root `.env.example` documents `TECHSTRAP_SUBNET`, `REVERSE_PROXY_CIDR`, `TECHSTRAP_MAILPIT_PORT`, `TECHSTRAP_SEED_DEV_DATA`.

- [ ] **Step 1: Write the failing tests**

Append to `scripts/tests/ConfigContract.Tests.ps1`:

```powershell
Describe 'the config contract of the local compose' {
    It 'every local compose environment: key is a known setting of its host' {
        $environment = Get-ComposeEnvironmentKeys -File 'docker-compose.yml'
        foreach ($name in 'Api', 'Worker', 'Admin', 'Portal') {
            $service = $name.ToLowerInvariant()
            $environment[$service] | Should -Not -BeNullOrEmpty
            $known = Get-AppsettingsKeys -HostName $name
            $unknown = @($environment[$service] | ForEach-Object { ConvertTo-IndexlessKey $_ } | Where-Object { $_ -notin $known -and $_ -notin $script:ComposeNonSettings })
            $unknown | Should -BeNullOrEmpty -Because "docker-compose.yml $service sets keys $name does not read: $($unknown -join ', ')"
        }
    }

    It 'the local compose no longer overrides the Admin sign-in or the group keys (the clash between .env.local and compose is gone)' {
        $admin = (Get-ComposeEnvironmentKeys -File 'docker-compose.yml')['admin']
        @($admin | Where-Object { $_ -like 'AUTH__*' -or $_ -like 'TECHSTRAP_*GROUP*' -or $_ -like 'TECHSTRAP_GROUP_CLAIM_TYPE' }) | Should -BeNullOrEmpty
        @($admin | Sort-Object) | Should -Be @('API__BASEURL', 'ASPNETCORE_ENVIRONMENT', 'DATAPROTECTION__KEYRINGPATH', 'TRUSTEDPROXY__TRUSTEDNETWORKS__0')
    }

    It 'the root .env.example documents the local compose inputs and nothing else' {
        $keys = @(Get-EnvEntries -Path (Join-Path $script:RepoRoot '.env.example') | ForEach-Object { $_.Key } | Sort-Object)
        $keys | Should -Be @('REVERSE_PROXY_CIDR', 'TECHSTRAP_MAILPIT_PORT', 'TECHSTRAP_SEED_DEV_DATA', 'TECHSTRAP_SUBNET')
        $compose = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'docker-compose.yml') -Raw
        foreach ($key in $keys) { $compose | Should -Match ('\$\{' + $key + ':-') }
    }
}
```

Edit `scripts/tests/ComposeFiles.Tests.ps1` (the local cases only; Task 4 rewrites the production and UAT cases):

In `scripts/tests/ComposeFiles.Tests.ps1`, replace:

```text
    It 'local compose mounts the shared storage volume on api and worker only' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml').Config
        foreach ($service in 'api', 'worker') {
            @($config.services.$service.volumes | Where-Object { $_.target -eq '/app/storage' -and $_.source -eq 'techstrap-storage' }).Count | Should -Be 1
        }
        foreach ($service in 'admin', 'portal') {
            @($config.services.$service.volumes | Where-Object { $_.target -eq '/app/storage' }).Count | Should -Be 0
        }
    }
```

with:

```text
    It 'local compose mounts the storage volume on the api only (the worker registers no attachment storage)' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml').Config
        @($config.services.api.volumes | Where-Object { $_.target -eq '/app/storage' -and $_.source -eq 'techstrap-storage' }).Count | Should -Be 1
        foreach ($service in 'worker', 'admin', 'portal') {
            @($config.services.$service.volumes | Where-Object { $_.target -eq '/app/storage' }).Count | Should -Be 0
        }
    }

    It 'local compose builds every app image from this checkout and loads an optional per-host .env.local' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml' -NoEnvResolution).Config
        foreach ($service in 'api', 'worker', 'admin', 'portal') {
            $config.services.$service.build.dockerfile | Should -Be "Dockerfile.$service"
            @($config.services.$service.env_file | Where-Object { $_.path -match "(?i)src[\\/]TechStrap\.$service[\\/]\.env\.local$" }).Count | Should -Be 1
            @($config.services.$service.env_file)[0].required | Should -BeFalse
        }
    }

    It 'local compose still resolves when every compose input is left out (the dev stack needs no .env)' {
        (Get-ComposeConfig -File 'docker-compose.yml').ExitCode | Should -Be 0
    }
```

In `scripts/tests/ComposeFiles.Tests.ps1`, replace:

```text
    It '<file> passes the Admin its OIDC client, the API address and the three group keys' -ForEach @(
        @{ file = 'docker-compose.yml'; withEnv = $false }
        @{ file = 'docker-compose.uat.yml'; withEnv = $true }
```

with:

```text
    It '<file> passes the Admin its OIDC client, the API address and the three group keys' -ForEach @(
        @{ file = 'docker-compose.uat.yml'; withEnv = $true }
```

In `scripts/tests/ComposeFiles.Tests.ps1`, replace:

```text
    It 'local compose gives the Admin placeholder OIDC values so the container starts without an identity provider' {
        $admin = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.admin.environment
        $admin.Auth__Authority | Should -Match '^https://'
        $admin.Auth__ClientId | Should -Not -BeNullOrEmpty
        $admin.Auth__ClientSecret | Should -Not -BeNullOrEmpty
    }
```

with:

```text
    It 'local compose leaves the Admin sign-in and the group keys to .env.local and appsettings.Development.json, so the two no longer clash' {
        $admin = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.admin
        $names = @($admin.environment.PSObject.Properties.Name)
        @($names | Where-Object { $_ -like 'Auth__*' -or $_ -like 'TECHSTRAP_*' }) | Should -BeNullOrEmpty
        $admin.environment.Api__BaseUrl | Should -Be 'http://api/'
    }
```

In `scripts/tests/ComposeFiles.Tests.ps1`, replace:

```text
    function Get-ComposeConfig {
        param([string]$File, [string]$EnvFile = '')

        $arguments = @('compose')
        if ($EnvFile) { $arguments += @('--env-file', $EnvFile) }
        $arguments += @('-f', (Join-Path $script:RepoRoot $File), 'config', '--format', 'json')
```

with:

```text
    function Get-ComposeConfig {
        param([string]$File, [string]$EnvFile = '', [switch]$NoEnvResolution)

        $arguments = @('compose')
        if ($EnvFile) { $arguments += @('--env-file', $EnvFile) }
        $arguments += @('-f', (Join-Path $script:RepoRoot $File), 'config')
        if ($NoEnvResolution) { $arguments += '--no-env-resolution' }
        $arguments += @('--format', 'json')
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1`
Expected: FAIL, 3 failed (the root `.env.example` does not exist, and the local compose still lists keys it should not).

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeFiles.Tests.ps1`
Expected: FAIL, 2 failed (the Admin still gets `Auth__*` and the group keys; the Worker still mounts the storage volume).

- [ ] **Step 3: Clean up the compose and document the inputs**

Replace `docker-compose.yml`:

```yaml
# Local stack: Postgres 17, Mailpit (SMTP capture) plus the four TechStrap images built from this checkout.
#   docker compose up -d --build
# Per-host settings go in src/TechStrap.<Host>/.env.local (gitignored; copy the .env.example next to it); without one, the checked-in
# appsettings.json and appsettings.Development.json defaults apply (the Admin signs in against a placeholder identity provider).
# Compose inputs (all optional, see the root .env.example): TECHSTRAP_SUBNET, REVERSE_PROXY_CIDR, TECHSTRAP_MAILPIT_PORT and TECHSTRAP_SEED_DEV_DATA.
# The environment: entries below are the wiring compose owns (the local Postgres, the Api address, volume paths, the proxy trust and Mailpit);
# everything else a host reads comes from its .env.local or its appsettings. The reverse proxy and TLS are not part of compose.
# The image-only UAT and production stack is deploy/docker-compose.yml (docs/self-hosting/DEPLOYMENT.md).
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

  mailpit:
    image: axllent/mailpit:v1.31.4
    # Web UI only; SMTP (1025) stays on the compose network for the worker.
    ports:
      - "127.0.0.1:${TECHSTRAP_MAILPIT_PORT:-8025}:8025"
    healthcheck:
      test: ["CMD", "/mailpit", "readyz"]
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
      # The sign-in settings (Auth__*) are not set here: appsettings.Development.json holds placeholders so the container starts without an identity provider,
      # and src/TechStrap.Admin/.env.local replaces them (docs/development/ADMIN-APP.md).
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
      # Portal trusts only the reverse proxy. 192.0.2.0/24 is a placeholder; set REVERSE_PROXY_CIDR.
      # The Portal reads no Api address or public URL yet; PHASE-09 adds Api__BaseUrl and TECHSTRAP_PORTAL_PUBLIC_URL here.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${REVERSE_PROXY_CIDR:-192.0.2.0/24}
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
      Email__Smtp__Host: mailpit
      Email__Smtp__Port: "1025"
      Email__Smtp__TlsMode: None
      Email__Smtp__MaxRetryAttempts: "1"
      Email__Smtp__RetryMode: TransientOnly
      Email__Smtp__TotalSendTimeout: "00:00:30"
      Email__Smtp__DefaultFrom: support@techstrap.localhost
    depends_on:
      api:
        condition: service_healthy
      mailpit:
        condition: service_healthy
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
        # 172.16.31.0/24 is outside Docker's default auto-assign pool; override TECHSTRAP_SUBNET only if this host already uses it.
        - subnet: ${TECHSTRAP_SUBNET:-172.16.31.0/24}

volumes:
  pgdata:
  techstrap-storage:
  admin-keys:
  portal-keys:
```

Create `.env.example` in the repository root:

```text
# Compose inputs for the local stack (docker-compose.yml). Copy to .env, which compose reads automatically (gitignored). All are optional.
# Per-host settings are not set here: they go in src/TechStrap.<Host>/.env.local (see the .env.example next to it).
# The image-only UAT and production stack takes its inputs from deploy/.env.<uat|production>.example instead.

# Pinned compose subnet (D-019). Change only if another Docker network on this machine already uses it.
TECHSTRAP_SUBNET=172.16.31.0/24
# Address the Admin and Portal trust as the reverse proxy. 192.0.2.0/24 is a documentation placeholder; use the proxy's address (a single address, never a wide range).
REVERSE_PROXY_CIDR=192.0.2.0/24
# Host port for the Mailpit web UI (loopback only).
TECHSTRAP_MAILPIT_PORT=8025
# Run the development data seeder after the Api migrates (docs/development/DEV-DATA.md).
TECHSTRAP_SEED_DEV_DATA=false
```

In `README.md`, replace:

```text
Troubleshooting: if compose reports `Pool overlaps with other one on this address space`, another Docker network already uses
the pinned subnet `172.16.31.0/24`. Pick a free one for this stack, for example `TECHSTRAP_SUBNET=10.245.31.0/24 docker compose up -d --build`.
```

with:

```text
Compose reads a few optional inputs (`TECHSTRAP_SUBNET`, `REVERSE_PROXY_CIDR`, `TECHSTRAP_MAILPIT_PORT`, `TECHSTRAP_SEED_DEV_DATA`) from a root `.env`; copy [.env.example](.env.example) to change them.
Every other setting comes from the host's `appsettings.json` and, if you add one, its `src/TechStrap.<Host>/.env.local`.

Troubleshooting: if compose reports `Pool overlaps with other one on this address space`, another Docker network already uses
the pinned subnet `172.16.31.0/24`. Pick a free one for this stack, for example `TECHSTRAP_SUBNET=10.245.31.0/24 docker compose up -d --build`.
```

In `README.md`, replace:

```text
Per-host settings for `dotnet run` go in `src/TechStrap.<Host>/.env.local` (gitignored; copy the `.env.example` next to it).
```

with:

```text
Each project's `appsettings.json` lists every setting the host reads, with its default (secrets are blank). Per-host settings for `dotnet run` go in
`src/TechStrap.<Host>/.env.local` (gitignored; copy the `.env.example` next to it, which documents the same keys as `SECTION__KEY`).
```

In `docs/development/ADMIN-APP.md`, replace:

````text
`.env.local` is read in Development only and is git-ignored. With Docker Compose the Admin listens on `http://127.0.0.1:8081`. Compose reads the three provider
values from the root `.env` (not from `src/TechStrap.Admin/.env.local`, whose `AUTH__*` keys would clash with the compose ones):

```bash
OIDC_AUTHORITY=https://auth.example.com/application/o/techstrap-admin/
OIDC_ADMIN_CLIENT_ID=techstrap-admin
OIDC_ADMIN_CLIENT_SECRET=...
```

Without them the container starts on placeholder values and the sign-in page works, but the redirect to the provider fails.
````

with:

````text
`.env.local` is read in Development only and is git-ignored. With Docker Compose the Admin listens on `http://127.0.0.1:8081` and reads the same
`src/TechStrap.Admin/.env.local`: compose no longer sets the sign-in values itself, so there is one place to put them. Uncomment and fill the three keys in that file:

```bash
AUTH__AUTHORITY=https://auth.example.com/application/o/techstrap-admin/
AUTH__CLIENTID=techstrap-admin
AUTH__CLIENTSECRET=...
```

Without them the Admin starts on the placeholders in `src/TechStrap.Admin/appsettings.Development.json` (`https://authentik.invalid/...`, `techstrap-admin`, `not-configured`)
and the sign-in page works, but the redirect to the provider fails. Leave the three keys commented out rather than blank: a blank value replaces the placeholder and the Admin stops at start.
Every setting the Admin reads is listed, with its default, in `src/TechStrap.Admin/appsettings.json`.
````

In `docs/development/ADMIN-APP.md`, replace:

```text
| `AUTH__SCOPES__0`, `AUTH__SCOPES__1`, ... | no | `openid profile email offline_access` | Must keep `openid` and `offline_access` (the API token is refreshed with the refresh token). |
```

with:

```text
| `AUTH__SCOPES__0`, `AUTH__SCOPES__1`, ... | no | `openid profile email offline_access` | Added to the defaults, not a replacement (array binding appends), so leave them out unless you need another scope. `openid` and `offline_access` must stay (the API token is refreshed with the refresh token). |
```

In `docs/development/ADMIN-APP.md`, replace:

```text
5. **Local compose uses placeholder values.** Without `OIDC_AUTHORITY`, `OIDC_ADMIN_CLIENT_ID` and `OIDC_ADMIN_CLIENT_SECRET` in the root `.env`, the Admin container starts on `https://authentik.invalid/...`, the sign-in page renders, and sign-in fails. That is expected until Authentik exists.
```

with:

```text
5. **Local runs use placeholder values.** Without `AUTH__AUTHORITY`, `AUTH__CLIENTID` and `AUTH__CLIENTSECRET` in `src/TechStrap.Admin/.env.local`, the Admin (in compose or under `dotnet run`) starts on the placeholders of `appsettings.Development.json`, the sign-in page renders, and sign-in fails. That is expected until Authentik exists.
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1`
Expected: PASS, 50 tests.

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeFiles.Tests.ps1`
Expected: PASS, 28 tests.

Run: `docker compose config --quiet`
Expected: no output, exit 0 (no `.env` is needed).

To see the pins guard (do not commit these edits), run `git add docker-compose.yml .env.example scripts/tests` first, so `git checkout -- <file>` restores the passing version. Make each change below, rerun the Pester command, expect the failure named, and restore the file. These were run in the scratch copy:

- `docker-compose.yml`: add `Auth__ClientId: techstrap-admin` back to the admin `environment:`. Fails `the local compose no longer overrides the Admin sign-in or the group keys` (ConfigContract) and `local compose leaves the Admin sign-in and the group keys ...` (ComposeFiles).
- `docker-compose.yml`: add `volumes: - techstrap-storage:/app/storage` to the worker. Fails `local compose mounts the storage volume on the api only`.

- [ ] **Step 5: Run the local stack (the compose smoke)**

Run: `pwsh -File scripts/Test-ComposeSmoke.ps1`
Expected: `ok   Api http://127.0.0.1:<port>/health/ready -> 200`, `ok   Admin ... -> 200`, `ok   Admin container is healthy`, `Compose smoke passed.` It builds the four images (the settings baked into them are the new `appsettings.json` files), uses its own project name and free loopback ports, and never runs `down -v`. The scratch copy ran it after Task 5 against the final tree; the Admin container started on the Development placeholders from `appsettings.Development.json`, with no `.env.local` and no `.env`.

- [ ] **Step 6: Whole-project check**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add docker-compose.yml \
  .env.example \
  README.md \
  docs/development/ADMIN-APP.md \
  scripts/tests
git diff --cached --stat
git commit -m "feat(compose): the local stack takes its settings from .env.local and appsettings, not from compose (D-043)" -m "The local compose keeps only the wiring it owns (the local Postgres, the Api address, volume paths, the proxy trust, the portal URL it publishes and the Mailpit settings). The Admin sign-in placeholders and the group keys no longer come from compose, so .env.local no longer clashes with it; the placeholders live in appsettings.Development.json. The Worker loses the storage volume and the keys it never read. A root .env.example documents the four compose inputs." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 4: The image-only deploy compose, its input templates and the compose tests

**Review Focus pin:**
- **(5)** The deploy compose must build nothing, run no database, give only the Api and the Worker the database network, publish on loopback only, trust only what it should and take explicit image references. Pinned by `ComposeFiles.Tests.ps1` (`docker compose config` on both inputs templates, 55 tests) and by the deploy-compose rules of the contract suite.
- **(1)** again: an env file that is missing stops `config` before `pull`.

**Files:**
- Create: `deploy/docker-compose.yml`, `deploy/.env.uat.example`, `deploy/.env.production.example`
- Delete: `docker-compose.production.yml`, `docker-compose.uat.yml`, `.env.production.example`
- Modify: `.gitignore`
- Modify: `scripts/tests/ComposeFiles.Tests.ps1` (rewritten), `scripts/tests/ConfigContract.Tests.ps1`

**Interfaces:**
- Consumes: the four `deploy/.env.<app>.example` templates from Task 2.
- Produces: the compose inputs `TECHSTRAP_PROJECT`, `TECHSTRAP_API_IMAGE`, `TECHSTRAP_WORKER_IMAGE`, `TECHSTRAP_ADMIN_IMAGE`, `TECHSTRAP_PORTAL_IMAGE`, `TECHSTRAP_ENV_DIR`, `TECHSTRAP_SUBNET`, `REVERSE_PROXY_CIDR`, `TECHSTRAP_API_PORT`, `TECHSTRAP_ADMIN_PORT`, `TECHSTRAP_PORTAL_PORT`, `TECHSTRAP_DB_NETWORK`. All are required (`${NAME:?...}`) except the project name, which defaults to `techstrap`. Compose-owned `environment:` per service: `ASPNETCORE_ENVIRONMENT`, `DOTENV__ENABLED`, plus api `STORAGE__LOCAL__ROOTPATH` and `TRUSTEDPROXY__TRUSTEDNETWORKS__0` (the subnet) and `__1` (`REVERSE_PROXY_CIDR`); admin `API__BASEURL`, `DATAPROTECTION__KEYRINGPATH`, `TRUSTEDPROXY__TRUSTEDNETWORKS__0` (`REVERSE_PROXY_CIDR`); portal `DATAPROTECTION__KEYRINGPATH` and the same trust.
- `docker compose config` checks that every `env_file` with `required: true` exists, so the tests build a directory of dummy env files (copies of the committed templates) and point `TECHSTRAP_ENV_DIR` at it. `config --no-env-resolution` shows each service's `env_file` path and `format`.

- [ ] **Step 1: Write the failing tests**

Replace `scripts/tests/ComposeFiles.Tests.ps1`:

```powershell
BeforeDiscovery {
    $script:DockerAvailable = $null -ne (Get-Command docker -ErrorAction SilentlyContinue) -and
        ((& docker compose version 2>&1 | Out-String) -match 'Docker Compose')
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:PinnedSubnet = '172.16.31.0/24'
    $script:DeployCompose = 'deploy/docker-compose.yml'

    function Get-ComposeConfig {
        param([string]$File, [string]$EnvFile = '', [switch]$NoEnvResolution)

        $arguments = @('compose')
        if ($EnvFile) { $arguments += @('--env-file', $EnvFile) }
        $arguments += @('-f', (Join-Path $script:RepoRoot $File), 'config')
        if ($NoEnvResolution) { $arguments += '--no-env-resolution' }
        $arguments += @('--format', 'json')

        $output = & docker @arguments 2>&1 | Out-String
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output   = $output
            Config   = if ($LASTEXITCODE -eq 0) { $output | ConvertFrom-Json } else { $null }
        }
    }

    # A host directory of scoped env files (copies of the committed templates, which is what an operator starts from) and a compose inputs file that points at it.
    # The compose inputs come from deploy/.env.<env>.example with TECHSTRAP_ENV_DIR replaced; -Without drops one variable so a test can show that compose refuses.
    function New-DeployInputs {
        param([string]$Directory, [ValidateSet('uat', 'production')][string]$Environment = 'uat', [string[]]$Without = @(), [string[]]$WithoutEnvFile = @())

        $envDirectory = Join-Path $Directory "env-$Environment"
        New-Item -ItemType Directory -Path $envDirectory -Force | Out-Null
        foreach ($app in 'api', 'worker', 'admin', 'portal') {
            if ($app -in $WithoutEnvFile) { continue }
            Copy-Item -LiteralPath (Join-Path $script:RepoRoot 'deploy' ".env.$app.example") -Destination (Join-Path $envDirectory ".env.$app")
        }

        $portableDirectory = $envDirectory -replace '\\', '/'
        $lines = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'deploy' ".env.$Environment.example") |
            ForEach-Object { if ($_ -like 'TECHSTRAP_ENV_DIR=*') { "TECHSTRAP_ENV_DIR=$portableDirectory" } else { $_ } } |
            Where-Object { $name = ($_ -split '=', 2)[0]; $name -notin $Without }
        $inputs = Join-Path $Directory "inputs-$Environment.env"
        Set-Content -LiteralPath $inputs -Value $lines
        return [pscustomobject]@{ Inputs = $inputs; EnvDirectory = $envDirectory; PortableDirectory = $portableDirectory }
    }

    function Get-InputValue {
        param([string]$Environment, [string]$Name)
        $line = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'deploy' ".env.$Environment.example") | Where-Object { $_ -like "$Name=*" } | Select-Object -First 1
        return ($line -split '=', 2)[1]
    }

    Remove-Item Env:TECHSTRAP_SUBNET -ErrorAction SilentlyContinue
}

Describe 'the local docker-compose.yml' -Skip:(-not $script:DockerAvailable) {
    It 'pins the subnet and the API trusts it' {
        $result = Get-ComposeConfig -File 'docker-compose.yml'
        $result.ExitCode | Should -Be 0
        $result.Config.networks.default.ipam.config[0].subnet | Should -Be $script:PinnedSubnet
        $result.Config.services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be $script:PinnedSubnet
    }

    It 'has the four app services plus Postgres 17 and never trusts a wide range' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml').Config
        ($config.services.PSObject.Properties.Name | Sort-Object) | Should -Be @('admin', 'api', 'mailpit', 'portal', 'postgres', 'worker')
        $config.services.postgres.image | Should -Be 'postgres:17'
        foreach ($service in 'api', 'admin', 'portal') {
            $config.services.$service.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Not -Match '^(172\.16\.0\.0/12|0\.0\.0\.0/0)$'
        }
    }

    It 'builds every app image from this checkout and loads an optional per-host .env.local' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml' -NoEnvResolution).Config
        foreach ($service in 'api', 'worker', 'admin', 'portal') {
            $config.services.$service.build.dockerfile | Should -Be "Dockerfile.$service"
            @($config.services.$service.env_file | Where-Object { $_.path -match "(?i)src[\\/]TechStrap\.$service[\\/]\.env\.local$" }).Count | Should -Be 1
            @($config.services.$service.env_file)[0].required | Should -BeFalse
        }
    }

    It 'publishes its web UI on loopback only and Mailpit has no SMTP port' {
        $mailpit = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.mailpit
        $ports = @($mailpit.ports)
        $ports.Count | Should -Be 1
        [int]$ports[0].target | Should -Be 8025
        $ports[0].host_ip | Should -Be '127.0.0.1'
        [int]$ports[0].published | Should -Be 8025
        @($ports | Where-Object { [int]$_.target -eq 1025 }).Count | Should -Be 0
    }

    It 'has the worker send through Mailpit without TLS and with outbox-safe retries' {
        $worker = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.worker
        $worker.environment.Email__Smtp__Host | Should -Be 'mailpit'
        $worker.environment.Email__Smtp__Port | Should -Be '1025'
        $worker.environment.Email__Smtp__TlsMode | Should -Be 'None'
        $worker.environment.Email__Smtp__MaxRetryAttempts | Should -Be '1'
        $worker.environment.Email__Smtp__RetryMode | Should -Be 'TransientOnly'
        $worker.depends_on.mailpit.condition | Should -Be 'service_healthy'
    }

    It 'mounts the storage volume on the api only (the worker registers no attachment storage)' {
        $config = (Get-ComposeConfig -File 'docker-compose.yml').Config
        @($config.services.api.volumes | Where-Object { $_.target -eq '/app/storage' -and $_.source -eq 'techstrap-storage' }).Count | Should -Be 1
        foreach ($service in 'worker', 'admin', 'portal') {
            @($config.services.$service.volumes | Where-Object { $_.target -eq '/app/storage' }).Count | Should -Be 0
        }
    }

    It 'leaves the Admin sign-in and the group keys to .env.local and appsettings.Development.json, so the two no longer clash' {
        $admin = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.admin
        $names = @($admin.environment.PSObject.Properties.Name)
        @($names | Where-Object { $_ -like 'Auth__*' -or $_ -like 'TECHSTRAP_*' }) | Should -BeNullOrEmpty
        $admin.environment.Api__BaseUrl | Should -Be 'http://api/'
    }

    It 'starts the Admin only after a healthy Api and gives it a health check on /health/live and a persistent key ring' {
        $admin = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.admin
        $admin.depends_on.api.condition | Should -Be 'service_healthy'
        ($admin.healthcheck.test -join ' ') | Should -Match '/health/live'
        @($admin.volumes | Where-Object { $_.target -eq '/app/dataprotection-keys' }).Count | Should -Be 1
    }

    It 'still resolves when every compose input is left out (the dev stack needs no .env)' {
        (Get-ComposeConfig -File 'docker-compose.yml').ExitCode | Should -Be 0
    }
}

Describe 'the image-only deploy compose (<envName>)' -Skip:(-not $script:DockerAvailable) -ForEach @(
    @{ envName = 'uat' }
    @{ envName = 'production' }
) {
    BeforeAll {
        $script:Run = New-DeployInputs -Directory $TestDrive -Environment $envName
        $script:Result = Get-ComposeConfig -File $script:DeployCompose -EnvFile $script:Run.Inputs
        $script:Config = $script:Result.Config
    }

    It 'resolves with the committed input template and dummy env files' {
        $script:Result.ExitCode | Should -Be 0 -Because $script:Result.Output
    }

    It 'has exactly the four app services, builds nothing and runs no database or mail catcher' {
        ($script:Config.services.PSObject.Properties.Name | Sort-Object) | Should -Be @('admin', 'api', 'portal', 'worker')
        foreach ($service in $script:Config.services.PSObject.Properties.Value) {
            $service.PSObject.Properties.Name | Should -Not -Contain 'build'
        }
        (Get-Content -LiteralPath (Join-Path $script:RepoRoot $script:DeployCompose) -Raw) | Should -Not -Match '(?im)^\s*(build:|image:\s*(postgres|axllent))'
    }

    It 'runs the images the inputs name and pulls them on every deploy' {
        foreach ($app in 'api', 'worker', 'admin', 'portal') {
            $script:Config.services.$app.image | Should -Be (Get-InputValue -Environment $envName -Name "TECHSTRAP_$($app.ToUpperInvariant())_IMAGE")
            $script:Config.services.$app.image | Should -Not -Match ':latest$'
            $script:Config.services.$app.pull_policy | Should -Be 'always'
        }
    }

    It 'loads every service from its own scoped env file under TECHSTRAP_ENV_DIR, raw and required' {
        $unresolved = (Get-ComposeConfig -File $script:DeployCompose -EnvFile $script:Run.Inputs -NoEnvResolution).Config
        foreach ($app in 'api', 'worker', 'admin', 'portal') {
            $files = @($unresolved.services.$app.env_file)
            $files.Count | Should -Be 1
            $files[0].path | Should -Be "$($script:Run.PortableDirectory)/.env.$app"
            $files[0].format | Should -Be 'raw'
        }
        ([regex]::Matches((Get-Content -LiteralPath (Join-Path $script:RepoRoot $script:DeployCompose) -Raw), '(?m)^        required: true\r?$')).Count | Should -Be 4
    }

    It 'passes the template values into each container, and the values compose owns override the env file' {
        $api = $script:Config.services.api.environment
        $api.TECHSTRAP_AUTOCLOSE_DAYS | Should -Be '7'
        $api.ASPNETCORE_ENVIRONMENT | Should -Be 'Production'
        $api.DOTENV__ENABLED | Should -Be 'false'
        $script:Config.services.worker.environment.EMAIL__SMTP__RETRYMODE | Should -Be 'TransientOnly'
        $script:Config.services.worker.environment.EMAIL__SMTP__TLSMODE | Should -Be 'StartTls'
        $script:Config.services.admin.environment.API__BASEURL | Should -Be 'http://api/'

        # An operator who lists a compose-owned key in the env file does not win: environment: is applied last.
        Add-Content -LiteralPath (Join-Path $script:Run.EnvDirectory '.env.api') -Value 'STORAGE__LOCAL__ROOTPATH=/somewhere/else'
        $overridden = (Get-ComposeConfig -File $script:DeployCompose -EnvFile $script:Run.Inputs).Config
        $overridden.services.api.environment.STORAGE__LOCAL__ROOTPATH | Should -Be '/app/storage'
    }

    It 'trusts the subnet and the proxy in the Api, and only the proxy in the Admin and the Portal, never a wide range' {
        $services = $script:Config.services
        $script:Config.networks.default.ipam.config[0].subnet | Should -Be (Get-InputValue -Environment $envName -Name 'TECHSTRAP_SUBNET')
        $services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be (Get-InputValue -Environment $envName -Name 'TECHSTRAP_SUBNET')
        $services.api.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__1 | Should -Be (Get-InputValue -Environment $envName -Name 'REVERSE_PROXY_CIDR')
        foreach ($service in 'admin', 'portal') {
            $services.$service.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Be (Get-InputValue -Environment $envName -Name 'REVERSE_PROXY_CIDR')
            $services.$service.environment.PSObject.Properties.Name | Should -Not -Contain 'TRUSTEDPROXY__TRUSTEDNETWORKS__1'
        }
        foreach ($service in 'api', 'admin', 'portal') {
            $services.$service.environment.TRUSTEDPROXY__TRUSTEDNETWORKS__0 | Should -Not -Match '^(172\.16\.0\.0/12|0\.0\.0\.0/0)$'
        }
    }

    It 'joins the external database network from the api and the worker only' {
        $script:Config.networks.db.external | Should -BeTrue
        $script:Config.networks.db.name | Should -Be (Get-InputValue -Environment $envName -Name 'TECHSTRAP_DB_NETWORK')
        foreach ($service in 'api', 'worker') {
            ($script:Config.services.$service.networks.PSObject.Properties.Name | Sort-Object) | Should -Be @('db', 'default')
        }
        foreach ($service in 'admin', 'portal') {
            $script:Config.services.$service.networks.PSObject.Properties.Name | Should -Not -Contain 'db'
        }
    }

    It 'publishes the api, admin and portal on loopback only, on the input ports, and the worker nowhere' {
        foreach ($app in 'api', 'admin', 'portal') {
            $ports = @($script:Config.services.$app.ports)
            $ports.Count | Should -Be 1
            $ports[0].host_ip | Should -Be '127.0.0.1'
            [int]$ports[0].target | Should -Be 80
            [string]$ports[0].published | Should -Be (Get-InputValue -Environment $envName -Name "TECHSTRAP_$($app.ToUpperInvariant())_PORT")
        }
        $script:Config.services.worker.PSObject.Properties.Name | Should -Not -Contain 'ports'
    }

    It 'keeps the storage volume on the api and a key ring on each Blazor host' {
        $services = $script:Config.services
        @($services.api.volumes | Where-Object { $_.target -eq '/app/storage' -and $_.source -eq 'techstrap-storage' }).Count | Should -Be 1
        @($services.admin.volumes | Where-Object { $_.target -eq '/app/dataprotection-keys' -and $_.source -eq 'admin-keys' }).Count | Should -Be 1
        @($services.portal.volumes | Where-Object { $_.target -eq '/app/dataprotection-keys' -and $_.source -eq 'portal-keys' }).Count | Should -Be 1
        $services.worker.PSObject.Properties.Name | Should -Not -Contain 'volumes'
        ($script:Config.volumes.PSObject.Properties.Name | Sort-Object) | Should -Be @('admin-keys', 'portal-keys', 'techstrap-storage')
    }

    It 'restarts unless stopped, starts the others only after a healthy api and checks health' {
        foreach ($app in 'api', 'worker', 'admin', 'portal') {
            $service = $script:Config.services.$app
            $service.restart | Should -Be 'unless-stopped'
            ($service.healthcheck.test -join ' ') | Should -Match '/health/(ready|live)'
        }
        foreach ($app in 'admin', 'portal', 'worker') {
            $script:Config.services.$app.depends_on.api.condition | Should -Be 'service_healthy'
        }
        ($script:Config.services.api.healthcheck.test -join ' ') | Should -Match '/health/ready'
        ($script:Config.services.admin.healthcheck.test -join ' ') | Should -Match '/health/live'
    }

    It 'refuses to resolve without <variable>, and says which one' -ForEach @(
        @{ variable = 'TECHSTRAP_API_IMAGE' }
        @{ variable = 'TECHSTRAP_WORKER_IMAGE' }
        @{ variable = 'TECHSTRAP_ADMIN_IMAGE' }
        @{ variable = 'TECHSTRAP_PORTAL_IMAGE' }
        @{ variable = 'TECHSTRAP_ENV_DIR' }
        @{ variable = 'TECHSTRAP_SUBNET' }
        @{ variable = 'REVERSE_PROXY_CIDR' }
        @{ variable = 'TECHSTRAP_DB_NETWORK' }
        @{ variable = 'TECHSTRAP_API_PORT' }
        @{ variable = 'TECHSTRAP_ADMIN_PORT' }
        @{ variable = 'TECHSTRAP_PORTAL_PORT' }
    ) {
        $run = New-DeployInputs -Directory (Join-Path $TestDrive "without-$variable") -Environment $envName -Without @($variable)
        $result = Get-ComposeConfig -File $script:DeployCompose -EnvFile $run.Inputs
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match $variable
    }

    It 'refuses to resolve while a scoped env file is missing' {
        $run = New-DeployInputs -Directory (Join-Path $TestDrive 'without-worker-file') -Environment $envName -WithoutEnvFile @('worker')
        $result = Get-ComposeConfig -File $script:DeployCompose -EnvFile $run.Inputs
        $result.ExitCode | Should -Not -Be 0
        $result.Output | Should -Match '\.env\.worker'
    }
}

Describe 'the deploy compose is one file for both environments' -Skip:(-not $script:DockerAvailable) {
    It 'resolves UAT and production inputs to the same service graph' {
        function ConvertTo-NormalisedModel {
            param([string]$Environment)

            $run = New-DeployInputs -Directory (Join-Path $TestDrive "parity-$Environment") -Environment $Environment
            $result = Get-ComposeConfig -File $script:DeployCompose -EnvFile $run.Inputs
            $result.ExitCode | Should -Be 0 -Because $result.Output
            $model = $result.Config

            # Legitimate differences, removed before comparing: the project name, the published host ports, the image tags and the project-derived
            # prefix of the network and volume names. Everything else (env, volumes, networks, healthchecks, depends_on) must match.
            $model.PSObject.Properties.Remove('name')
            foreach ($service in $model.services.PSObject.Properties.Value) {
                $service.PSObject.Properties.Remove('ports')
                $service.image = ($service.image -replace ':[^:]+$', ':tag')
            }
            $text = $model | ConvertTo-Json -Depth 30
            return ($text -replace 'techstrap(-uat)?_', 'techstrap_')
        }

        ConvertTo-NormalisedModel -Environment 'uat' | Should -BeExactly (ConvertTo-NormalisedModel -Environment 'production')
    }

    It 'is the only deploy compose file' {
        Test-Path (Join-Path $script:RepoRoot 'docker-compose.production.yml') | Should -BeFalse
        Test-Path (Join-Path $script:RepoRoot 'docker-compose.uat.yml') | Should -BeFalse
        (Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot 'deploy') -Filter 'docker-compose*.yml').Name | Should -Be @('docker-compose.yml')
    }
}
```

Append to `scripts/tests/ConfigContract.Tests.ps1`:

```powershell
Describe 'the config contract of the deploy compose' {
    It 'the deploy compose environment: lists exactly what compose owns for each host, and every key is a known setting' {
        $environment = Get-ComposeEnvironmentKeys -File 'deploy/docker-compose.yml'
        foreach ($name in 'Api', 'Worker', 'Admin', 'Portal') {
            $service = $name.ToLowerInvariant()
            $keys = @($environment[$service] | ForEach-Object { ConvertTo-IndexlessKey $_ } | Sort-Object -Unique)
            $expected = @($script:ComposeOwned[$name] + $script:ComposeNonSettings | Sort-Object -Unique)
            $keys | Should -Be $expected -Because "deploy/docker-compose.yml $service environment:"
            $known = Get-AppsettingsKeys -HostName $name
            @($keys | Where-Object { $_ -notin $known -and $_ -notin $script:ComposeNonSettings }) | Should -BeNullOrEmpty
        }
    }

    It 'the UAT and production compose input templates set the same variable names' {
        $uat = @(Get-EnvEntries -Path (Join-Path $script:RepoRoot 'deploy' '.env.uat.example') | Where-Object { -not $_.Commented } | ForEach-Object { $_.Key } | Sort-Object)
        $production = @(Get-EnvEntries -Path (Join-Path $script:RepoRoot 'deploy' '.env.production.example') | Where-Object { -not $_.Commented } | ForEach-Object { $_.Key } | Sort-Object)
        $uat.Count | Should -BeGreaterThan 10
        $uat | Should -Be $production
    }

    It 'the compose input templates set every variable the deploy compose requires, and never latest' {
        $compose = Get-Content -LiteralPath (Join-Path $script:RepoRoot 'deploy' 'docker-compose.yml') -Raw
        $required = [regex]::Matches($compose, '\$\{(?<name>[A-Z][A-Z0-9_]*):\?') | ForEach-Object { $_.Groups['name'].Value } | Sort-Object -Unique
        $required.Count | Should -BeGreaterThan 8
        foreach ($file in '.env.uat.example', '.env.production.example') {
            $entries = Get-EnvEntries -Path (Join-Path $script:RepoRoot 'deploy' $file) | Where-Object { -not $_.Commented }
            foreach ($name in $required) { $entries.Key | Should -Contain $name -Because "$file must set $name" }
            foreach ($image in ($entries | Where-Object { $_.Key -like 'TECHSTRAP_*_IMAGE' })) {
                $image.Value | Should -Match '^ghcr\.io/syntax-circus/techstrap-(api|worker|admin|portal):\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$' -Because "$file pins an explicit release tag"
            }
        }
    }

    It 'git ignores a filled env file and tracks every example' -ForEach @(
        @{ Path = 'deploy/.env.uat.local'; Ignored = $true }
        @{ Path = 'deploy/.env.production.local'; Ignored = $true }
        @{ Path = 'deploy/.env.api'; Ignored = $true }
        @{ Path = '.env'; Ignored = $true }
        @{ Path = 'src/TechStrap.Api/.env.local'; Ignored = $true }
        @{ Path = '.env.example'; Ignored = $false }
        @{ Path = 'deploy/.env.uat.example'; Ignored = $false }
        @{ Path = 'deploy/.env.production.example'; Ignored = $false }
        @{ Path = 'deploy/.env.api.example'; Ignored = $false }
        @{ Path = 'deploy/.env.worker.example'; Ignored = $false }
        @{ Path = 'deploy/.env.admin.example'; Ignored = $false }
        @{ Path = 'deploy/.env.portal.example'; Ignored = $false }
        @{ Path = 'src/TechStrap.Portal/.env.example'; Ignored = $false }
    ) {
        Test-GitIgnored $Path | Should -Be $Ignored
    }

    It 'the old root production compose files and env example are gone' {
        foreach ($file in 'docker-compose.production.yml', 'docker-compose.uat.yml', '.env.production.example') {
            Test-Path (Join-Path $script:RepoRoot $file) | Should -BeFalse
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeFiles.Tests.ps1`
Expected: FAIL, 46 of 55 failed (`deploy/docker-compose.yml` does not exist).

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1`
Expected: FAIL, 4 of 67 failed (the compose and the input templates do not exist; the old files are still there).

- [ ] **Step 3: Add the deploy compose and its input templates, delete the old files**

Create `deploy/docker-compose.yml`:

```yaml
# Image-only deployment stack for UAT and production (D-043). One file serves both: the compose inputs file chooses the environment.
# It builds nothing and runs no database: Postgres is a separate instance reached over an external Docker network. TLS and the reverse
# proxy stay outside compose; the services publish on loopback only for the proxy to reach.
#
#   cp deploy/.env.<uat|production>.example deploy/.env.<uat|production>.local   # compose inputs (not secrets), then edit
#   sudo install -d -m 0700 /etc/techstrap/<uat|production>                         # copy each deploy/.env.<app>.example to
#                                                                                   # /etc/techstrap/<env>/.env.<app>: root-owned, mode 0600
#   docker compose --env-file deploy/.env.<env>.local -f deploy/docker-compose.yml config --quiet
#   docker compose --env-file deploy/.env.<env>.local -f deploy/docker-compose.yml pull
#   docker compose --env-file deploy/.env.<env>.local -f deploy/docker-compose.yml up -d --wait
#
# Settings reach each container from its own scoped env file (env_file, format raw: no interpolation and no quote handling). The
# environment: entries below are the values compose itself owns; they override the env file. docs/self-hosting/DEPLOYMENT.md is the runbook.
# Client IPs: ports publish on 127.0.0.1 only, so a same-host reverse proxy appears to the containers as the compose gateway (172.16.31.1).
# Set REVERSE_PROXY_CIDR=172.16.31.1/32 for that setup, or the proxy's own address if it runs on another machine (never a wide range).
name: ${TECHSTRAP_PROJECT:-techstrap}

services:
  api:
    image: ${TECHSTRAP_API_IMAGE:?set TECHSTRAP_API_IMAGE to an explicit image reference, for example ghcr.io/syntax-circus/techstrap-api:1.2.3}
    pull_policy: always
    restart: unless-stopped
    env_file:
      - path: ${TECHSTRAP_ENV_DIR:?set TECHSTRAP_ENV_DIR to the host directory that holds .env.api, .env.worker, .env.admin and .env.portal}/.env.api
        required: true
        format: raw
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      DOTENV__ENABLED: "false"
      STORAGE__LOCAL__ROOTPATH: /app/storage
      # Trust the pinned compose subnet (the Portal hop) and the reverse proxy. Both are required, so entry 1 is always the proxy.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${TECHSTRAP_SUBNET:?set TECHSTRAP_SUBNET to the pinned compose subnet, for example 172.16.31.0/24}
      TRUSTEDPROXY__TRUSTEDNETWORKS__1: ${REVERSE_PROXY_CIDR:?set REVERSE_PROXY_CIDR to the reverse proxy address, for example 172.16.31.1/32}
    networks:
      - default
      - db
    volumes:
      - techstrap-storage:/app/storage
    ports:
      - "127.0.0.1:${TECHSTRAP_API_PORT:?set TECHSTRAP_API_PORT}:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/ready"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

  admin:
    image: ${TECHSTRAP_ADMIN_IMAGE:?set TECHSTRAP_ADMIN_IMAGE to an explicit image reference, for example ghcr.io/syntax-circus/techstrap-admin:1.2.3}
    pull_policy: always
    restart: unless-stopped
    env_file:
      - path: ${TECHSTRAP_ENV_DIR:?set TECHSTRAP_ENV_DIR to the host directory that holds .env.api, .env.worker, .env.admin and .env.portal}/.env.admin
        required: true
        format: raw
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      DOTENV__ENABLED: "false"
      API__BASEURL: http://api/
      DATAPROTECTION__KEYRINGPATH: /app/dataprotection-keys
      # Admin trusts only the reverse proxy.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${REVERSE_PROXY_CIDR:?set REVERSE_PROXY_CIDR to the reverse proxy address, for example 172.16.31.1/32}
    depends_on:
      api:
        condition: service_healthy
    volumes:
      - admin-keys:/app/dataprotection-keys
    ports:
      - "127.0.0.1:${TECHSTRAP_ADMIN_PORT:?set TECHSTRAP_ADMIN_PORT}:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/live"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

  portal:
    image: ${TECHSTRAP_PORTAL_IMAGE:?set TECHSTRAP_PORTAL_IMAGE to an explicit image reference, for example ghcr.io/syntax-circus/techstrap-portal:1.2.3}
    pull_policy: always
    restart: unless-stopped
    env_file:
      - path: ${TECHSTRAP_ENV_DIR:?set TECHSTRAP_ENV_DIR to the host directory that holds .env.api, .env.worker, .env.admin and .env.portal}/.env.portal
        required: true
        format: raw
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      DOTENV__ENABLED: "false"
      DATAPROTECTION__KEYRINGPATH: /app/dataprotection-keys
      # Portal trusts only the reverse proxy.
      TRUSTEDPROXY__TRUSTEDNETWORKS__0: ${REVERSE_PROXY_CIDR:?set REVERSE_PROXY_CIDR to the reverse proxy address, for example 172.16.31.1/32}
    depends_on:
      api:
        condition: service_healthy
    volumes:
      - portal-keys:/app/dataprotection-keys
    ports:
      - "127.0.0.1:${TECHSTRAP_PORTAL_PORT:?set TECHSTRAP_PORTAL_PORT}:80"
    healthcheck:
      test: ["CMD", "curl", "--fail", "--silent", "http://localhost/health/live"]
      interval: 15s
      timeout: 5s
      retries: 5
      start_period: 20s

  worker:
    image: ${TECHSTRAP_WORKER_IMAGE:?set TECHSTRAP_WORKER_IMAGE to an explicit image reference, for example ghcr.io/syntax-circus/techstrap-worker:1.2.3}
    pull_policy: always
    restart: unless-stopped
    env_file:
      - path: ${TECHSTRAP_ENV_DIR:?set TECHSTRAP_ENV_DIR to the host directory that holds .env.api, .env.worker, .env.admin and .env.portal}/.env.worker
        required: true
        format: raw
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      DOTENV__ENABLED: "false"
    networks:
      - default
      - db
    depends_on:
      api:
        condition: service_healthy
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
        # Change TECHSTRAP_SUBNET only if another network on this host already uses it (UAT and production on one host need two subnets).
        - subnet: ${TECHSTRAP_SUBNET:?set TECHSTRAP_SUBNET to the pinned compose subnet, for example 172.16.31.0/24}
  db:
    # Pre-existing shared network owned by the separate Postgres compose project; only the api and the worker join it.
    name: ${TECHSTRAP_DB_NETWORK:?set TECHSTRAP_DB_NETWORK to the name of the Docker network the Postgres container is on}
    external: true

volumes:
  techstrap-storage:
  admin-keys:
  portal-keys:
```

`deploy/.env.uat.example`:

```text
# These are Compose inputs, not app secrets (D-043). Copy to deploy/.env.uat.local (gitignored) and pass it to compose:
#   docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml config --quiet
# The app settings and secrets live in the scoped env files under TECHSTRAP_ENV_DIR (deploy/.env.api.example and its siblings).
# deploy/.env.uat.example and deploy/.env.production.example set the same variable names; scripts/tests/ComposeFiles.Tests.ps1 pins that.
# Compose refuses to resolve while a required value below is missing.

# --- Project ---
# Names the compose project, so UAT and production never share containers or volumes.
TECHSTRAP_PROJECT=techstrap-uat

# --- Images (required, explicit; never latest) ---
# Pin each image to one release tag (SemVer without the leading v). To roll back, set the previous tag, then run pull and up -d --wait.
TECHSTRAP_API_IMAGE=ghcr.io/syntax-circus/techstrap-api:1.0.0-rc.1
TECHSTRAP_WORKER_IMAGE=ghcr.io/syntax-circus/techstrap-worker:1.0.0-rc.1
TECHSTRAP_ADMIN_IMAGE=ghcr.io/syntax-circus/techstrap-admin:1.0.0-rc.1
TECHSTRAP_PORTAL_IMAGE=ghcr.io/syntax-circus/techstrap-portal:1.0.0-rc.1

# --- Scoped env files ---
# Host directory that holds .env.api, .env.worker, .env.admin and .env.portal (root-owned, mode 0600).
TECHSTRAP_ENV_DIR=/etc/techstrap/uat

# --- Network and reverse proxy ---
# Pinned compose subnet (D-019). Change only if another network on this host already uses it,
# and update the _template CLIENT_IP_RATE_LIMITING.md subnet registry to match.
TECHSTRAP_SUBNET=172.16.31.0/24
# Address or CIDR of the reverse proxy (Caddy) that forwards client IPs (required). Admin and Portal trust only this; the API trusts it
# in addition to the compose subnet. The ports below publish on 127.0.0.1 only, so a proxy on the same host reaches the containers
# through Docker port publishing and they see the compose gateway (172.16.31.1) as the peer. For that default setup use the gateway.
# A proxy on another machine needs the ports published on a reachable interface and its own address here. Always a single address,
# never a wide range (never 172.16.0.0/12 or 0.0.0.0/0).
REVERSE_PROXY_CIDR=172.16.31.1/32
# Loopback ports the reverse proxy forwards to.
TECHSTRAP_API_PORT=18080
TECHSTRAP_ADMIN_PORT=18081
TECHSTRAP_PORTAL_PORT=18082

# --- Postgres ---
# Name of the existing Docker network that the separate Postgres container is attached to. Only the api and the worker join it.
# Create it once if it does not exist: docker network create techstrap-db
TECHSTRAP_DB_NETWORK=techstrap-db
```

`deploy/.env.production.example`:

```text
# These are Compose inputs, not app secrets (D-043). Copy to deploy/.env.production.local (gitignored) and pass it to compose:
#   docker compose --env-file deploy/.env.production.local -f deploy/docker-compose.yml config --quiet
# The app settings and secrets live in the scoped env files under TECHSTRAP_ENV_DIR (deploy/.env.api.example and its siblings).
# deploy/.env.uat.example and deploy/.env.production.example set the same variable names; scripts/tests/ComposeFiles.Tests.ps1 pins that.
# Compose refuses to resolve while a required value below is missing.

# --- Project ---
# Names the compose project, so UAT and production never share containers or volumes.
TECHSTRAP_PROJECT=techstrap

# --- Images (required, explicit; never latest) ---
# Pin each image to one release tag (SemVer without the leading v). To roll back, set the previous tag, then run pull and up -d --wait.
TECHSTRAP_API_IMAGE=ghcr.io/syntax-circus/techstrap-api:1.0.0
TECHSTRAP_WORKER_IMAGE=ghcr.io/syntax-circus/techstrap-worker:1.0.0
TECHSTRAP_ADMIN_IMAGE=ghcr.io/syntax-circus/techstrap-admin:1.0.0
TECHSTRAP_PORTAL_IMAGE=ghcr.io/syntax-circus/techstrap-portal:1.0.0

# --- Scoped env files ---
# Host directory that holds .env.api, .env.worker, .env.admin and .env.portal (root-owned, mode 0600).
TECHSTRAP_ENV_DIR=/etc/techstrap/production

# --- Network and reverse proxy ---
# Pinned compose subnet (D-019). Change only if another network on this host already uses it,
# and update the _template CLIENT_IP_RATE_LIMITING.md subnet registry to match.
TECHSTRAP_SUBNET=172.16.31.0/24
# Address or CIDR of the reverse proxy (Caddy) that forwards client IPs (required). Admin and Portal trust only this; the API trusts it
# in addition to the compose subnet. The ports below publish on 127.0.0.1 only, so a proxy on the same host reaches the containers
# through Docker port publishing and they see the compose gateway (172.16.31.1) as the peer. For that default setup use the gateway.
# A proxy on another machine needs the ports published on a reachable interface and its own address here. Always a single address,
# never a wide range (never 172.16.0.0/12 or 0.0.0.0/0).
REVERSE_PROXY_CIDR=172.16.31.1/32
# Loopback ports the reverse proxy forwards to.
TECHSTRAP_API_PORT=8080
TECHSTRAP_ADMIN_PORT=8081
TECHSTRAP_PORTAL_PORT=8082

# --- Postgres ---
# Name of the existing Docker network that the separate Postgres container is attached to. Only the api and the worker join it.
# Create it once if it does not exist: docker network create techstrap-db
TECHSTRAP_DB_NETWORK=techstrap-db
```

In `.gitignore`, replace:

```text
!.env.*.example
```

with:

```text
!.env.*.example
# Deploy compose inputs and scoped env files filled in on a host (never committed; the .example templates are)
deploy/.env.*.local
deploy/.env.api
deploy/.env.worker
deploy/.env.admin
deploy/.env.portal
```

The root `.dockerignore` already keeps `**/.env*` out of the image contexts except `**/.env.example`, so nothing under `deploy/` reaches an image.

Delete the old files:

```bash
git rm docker-compose.production.yml docker-compose.uat.yml .env.production.example
```

Check the ignore rules for every path:

```bash
git check-ignore -v deploy/.env.uat.local deploy/.env.production.local deploy/.env.api .env src/TechStrap.Api/.env.local
git check-ignore -v deploy/.env.uat.example deploy/.env.api.example .env.example || echo "examples are tracked"
```

Expected: the first command prints the ignoring rule for all five paths; the second prints nothing and then `examples are tracked`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeFiles.Tests.ps1`
Expected: PASS, 55 tests (9 for the local compose, 22 for each of `uat` and `production`, and 2 that compare them).

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1`
Expected: PASS, 67 tests.

Run (for the owner, with dummy env files; `config` only, nothing is pulled or started): `pwsh -File scripts/Test-ComposeSmoke.ps1 -DeployComposeOnly` once Task 5 has added the switch. Do NOT run `docker compose up` against the deploy compose: it needs real images and an external Postgres.

To see the pins guard (do not commit these edits), run `git add deploy .gitignore scripts/tests docker-compose.yml` first, so `git checkout -- <file>` restores the passing version. Make each change below, rerun the Pester command named (the ComposeFiles one unless the rule is a contract rule), expect the failures named, and restore the file. These were run in the scratch copy:

- `deploy/docker-compose.yml`: make the db network optional (`name: ${TECHSTRAP_DB_NETWORK:-techstrap-db}`). Fails `refuses to resolve without TECHSTRAP_DB_NETWORK` (both environments).
- `deploy/docker-compose.yml`: give the admin `networks: [default, db]`. Fails `joins the external database network from the api and the worker only`.
- `deploy/docker-compose.yml`: default the Api image to `:latest` (`${TECHSTRAP_API_IMAGE:-ghcr.io/syntax-circus/techstrap-api:latest}`). Fails `refuses to resolve without TECHSTRAP_API_IMAGE`.
- `deploy/docker-compose.yml`: publish the Api as `"${TECHSTRAP_API_PORT:?set TECHSTRAP_API_PORT}:80"` (all interfaces). Fails `publishes the api, admin and portal on loopback only`.
- `deploy/docker-compose.yml`: add `build: .` to the worker. Fails `has exactly the four app services, builds nothing and runs no database or mail catcher`.
- `deploy/docker-compose.yml`: drop `format: raw` from the portal `env_file`. Fails `loads every service from its own scoped env file ... raw and required`.
- `deploy/.env.uat.example`: change `TECHSTRAP_SUBNET` only. Fails `resolves UAT and production inputs to the same service graph`; removing `TECHSTRAP_PROJECT=techstrap-uat` fails the contract's `set the same variable names`.
- `deploy/docker-compose.yml`: add `STORAGE__LOCAL__ROOTPATH: /app/storage` to the worker `environment:`. Fails the contract's `the deploy compose environment: lists exactly what compose owns for each host`.

- [ ] **Step 5: Whole-project check**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS (nothing in .NET reads these files, so the earlier `dotnet` results stand).

- [ ] **Step 6: Commit**

```bash
git add deploy \
  .gitignore \
  scripts/tests
git diff --cached --stat
git commit -m "feat(deploy): one image-only compose for UAT and production with scoped env files and an external Postgres (D-043)" -m "deploy/docker-compose.yml replaces docker-compose.uat.yml and docker-compose.production.yml. It builds nothing and runs no database: every image reference is an explicit input (no latest default, pull always), each service loads its own env file from TECHSTRAP_ENV_DIR (raw format, required), the api and the worker join an external Docker network that the separate Postgres is on, and compose keeps only what it owns in environment: (the environment name, the key-ring and storage paths, the Api address and the trusted networks). Two input templates, deploy/.env.uat.example and deploy/.env.production.example, set the same names. The old root production compose files and .env.production.example are deleted." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 5: The deployment runbook, the config-only deploy check and the full verification

**Review Focus pin:** none of the five directly. The runbook is the operator's route to everything pinned in Tasks 2 to 4, and the smoke script gains a way to prove the deploy compose resolves without an image or a database.

**Files:**
- Create: `docs/self-hosting/DEPLOYMENT.md`
- Modify: `README.md`
- Modify: `scripts/Test-ComposeSmoke.ps1`, `scripts/tests/ComposeSmoke.Tests.ps1`, `scripts/tests/RepositoryDocs.Tests.ps1`

**Interfaces:**
- Consumes: `deploy/docker-compose.yml` and the templates from Tasks 2 and 4.
- Produces: `Test-ComposeSmoke.ps1 -DeployComposeOnly` (resolve the deploy compose with the UAT template and dummy env files, then stop: no build, no pull, no stack) and `-CheckDeployCompose` (the same check before the local stack runs; `-DryRun` prints it).

- [ ] **Step 1: Write the failing tests**

Append to `scripts/tests/RepositoryDocs.Tests.ps1`:

```powershell
Describe 'the deployment runbook' {
    BeforeAll { $script:Runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md' }

    It 'gives the config, pull, up and ps commands for the deploy compose with an inputs file' {
        foreach ($verb in 'config --quiet', 'pull', 'up -d --wait', 'ps') {
            $script:Runbook | Should -Match ([regex]::Escape("docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml $verb"))
        }
    }

    It 'covers the shared Postgres network, the 0600 env directory, the registry login, health checks, rollback by image tag and migrations' {
        foreach ($phrase in 'docker network create techstrap-db', 'install -d -m 0700 /etc/techstrap/uat', 'docker login ghcr.io', '/health/ready', 'set the previous tags', 'DATABASE__MIGRATEONSTARTUP', 'openssl rand -hex 24') {
            $script:Runbook | Should -Match ([regex]::Escape($phrase))
        }
    }

    It 'never tells the operator to remove volumes and is linked from the README' {
        $script:Runbook | Should -Not -Match 'down -v(\s|$)'
        (Get-RepoText 'README.md') | Should -Match ([regex]::Escape('(docs/self-hosting/DEPLOYMENT.md)'))
    }

    It 'links only to files that exist' {
        $links = [regex]::Matches($script:Runbook, '\]\((?<path>(?!https?:|#)[^)\s]+)\)') | ForEach-Object { $_.Groups['path'].Value }
        $links.Count | Should -BeGreaterThan 1
        foreach ($link in $links) {
            Test-Path -LiteralPath (Join-Path $script:RepoRoot 'docs' 'self-hosting' $link) | Should -BeTrue -Because "DEPLOYMENT.md links to $link"
        }
    }
}
```

Replace `scripts/tests/ComposeSmoke.Tests.ps1`:

```powershell
BeforeDiscovery {
    $script:DockerAvailable = $null -ne (Get-Command docker -ErrorAction SilentlyContinue) -and
        ((& docker compose version 2>&1 | Out-String) -match 'Docker Compose')
}

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:SmokeScript = Join-Path $script:RepoRoot 'scripts' 'Test-ComposeSmoke.ps1'
    $script:SmokeText = Get-Content -LiteralPath $script:SmokeScript -Raw
    $script:DryRun = & $script:SmokeScript -DryRun | Out-String
}

Describe 'Test-ComposeSmoke.ps1' {
    It 'parses without errors' {
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($script:SmokeScript, [ref]$null, [ref]$errors) | Out-Null
        $errors | Should -BeNullOrEmpty
    }

    It 'under -DryRun prints the commands it would run and runs none of them' {
        $script:DryRun | Should -Match 'up -d --wait'
        $script:DryRun | Should -Match 'port api 80'
        $script:DryRun | Should -Match 'port admin 80'
        $script:DryRun | Should -Match 'health/ready'
        $script:DryRun | Should -Match ' down\r?\n'
    }

    It 'uses its own project name, so it can never stop the stack run by hand' {
        $script:DryRun | Should -Match 'compose -p techstrap-smoke '
        $script:DryRun | Should -Not -Match 'compose -p techstrap '
        { & $script:SmokeScript -ProjectName 'techstrap' -DryRun } | Should -Throw '*Refusing*'
    }

    It 'never removes volumes' {
        # The one place it stops the stack passes the single argument 'down': no -v and no --volumes after it.
        $script:SmokeText | Should -Match "Invoke-Compose -Arguments @\('down'\)"
        $script:SmokeText | Should -Not -Match "'down'\s*,"
        $script:SmokeText | Should -Not -Match "'(-v|--volumes)'"
        $script:SmokeText | Should -Not -Match '(?i)docker volume (rm|prune)'
        $script:DryRun | Should -Not -Match '(?i)\sdown\s+(-v|--volumes)'
    }

    It 'publishes every port on a free loopback port and names its own images' {
        $script:DryRun | Should -Match '(?s)api:\s+image: techstrap-smoke-api:local\s+ports: !override\s+- "127\.0\.0\.1::80"'
        $script:DryRun | Should -Match '(?s)admin:\s+image: techstrap-smoke-admin:local\s+ports: !override\s+- "127\.0\.0\.1::80"'
        $script:DryRun | Should -Match '(?s)portal:\s+image: techstrap-smoke-portal:local\s+ports: !override\s+- "127\.0\.0\.1::80"'
        $script:DryRun | Should -Match '(?s)mailpit:\s+ports: !override\s+- "127\.0\.0\.1::8025"'
        $ports = [regex]::Matches($script:DryRun, '(?m)^\s+- "([^"]+)"\s*$') | ForEach-Object { $_.Groups[1].Value }
        @($ports).Count | Should -Be 4
        foreach ($port in $ports) { $port | Should -Match '^127\.0\.0\.1::\d+$' }
    }

    It 'builds the four images one after another, never with up --build (parallel restores corrupt the shared NuGet cache mount)' {
        foreach ($service in 'api', 'worker', 'admin', 'portal') {
            $script:DryRun | Should -Match "build $service"
        }
        $script:DryRun | Should -Not -Match 'up [^\r\n]*--build'
        $quiet = & $script:SmokeScript -DryRun -NoBuild | Out-String
        $quiet | Should -Not -Match ' build '
    }

    It 'checks the deploy compose only on request, with config alone: no pull and no up' {
        $script:DryRun | Should -Not -Match 'deploy/docker-compose.yml'
        $checked = & $script:SmokeScript -DryRun -CheckDeployCompose | Out-String
        $checked | Should -Match 'deploy/docker-compose\.yml config --quiet'
        $checked | Should -Match 'no pull, no up'
        # The script's only docker call that names the deploy file is the config check.
        $script:SmokeText | Should -Match '& docker compose --env-file \$inputs -f \$deployFile config --quiet'
        $script:SmokeText | Should -Not -Match '\$deployFile[^\r\n]*\b(pull|up)\b'
    }

    It 'resolves the deploy compose with the UAT template and dummy env files (-DeployComposeOnly, needs Docker)' -Skip:(-not $script:DockerAvailable) {
        $output = & $script:SmokeScript -DeployComposeOnly | Out-String
        $output | Should -Match 'deploy/docker-compose\.yml resolves'
    }

    It 'leaves the stack running only when asked to' {
        (& $script:SmokeScript -DryRun -KeepRunning | Out-String) | Should -Not -Match ' down'
    }
}

Describe 'the compose smoke in CI' {
    BeforeAll { $script:Workflow = Get-Content -LiteralPath (Join-Path $script:RepoRoot '.github' 'workflows' 'ci.yml') -Raw }

    It 'is a manually started job, so a pull request does not build four images twice' {
        $script:Workflow | Should -Match '(?m)^  workflow_dispatch:'
        $script:Workflow | Should -Match '(?s)compose-smoke:\s+name: Compose smoke \(manual\)\s+if: github\.event_name == ''workflow_dispatch'''
        $script:Workflow | Should -Match 'scripts/Test-ComposeSmoke\.ps1'
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1`
Expected: FAIL, 4 failed (the runbook does not exist).

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeSmoke.Tests.ps1`
Expected: FAIL, 2 failed (`A parameter cannot be found that matches parameter name 'DeployComposeOnly'`).

- [ ] **Step 3: Write the runbook and the check**

Create `docs/self-hosting/DEPLOYMENT.md`:

````markdown
# Deployment: UAT and production

The deployment stack runs the Api, the Worker, the Admin and the Portal from explicit image references (D-043). Postgres, the identity provider and the reverse proxy are provisioned separately.
The root `docker-compose.yml` is the local development stack and builds from source; deployment commands MUST name `-f deploy/docker-compose.yml`.

## Configuration layout

Each project has its own `appsettings.json`, `appsettings.Development.json` (local overrides only) and `.env.example` under `src/TechStrap.Api`, `src/TechStrap.Worker`, `src/TechStrap.Admin` and `src/TechStrap.Portal`.
`appsettings.json` lists every setting the host reads: real, non-secret defaults, and blank for a secret or an environment-specific value. The `.env.example` documents the same keys as `SECTION__KEY`.

- **Development:** copy a project's `.env.example` to `.env.local` beside it. `SyntaxCircus.DotEnv` loads `.env` then `.env.local` in Development only.
- **Deployment, app settings:** copy each `deploy/.env.<app>.example` to `.env.<app>` in a host directory such as `/etc/techstrap/uat` or `/etc/techstrap/production`
  (`TECHSTRAP_ENV_DIR`). The four files (`.env.api`, `.env.worker`, `.env.admin`, `.env.portal`) are root-owned, mode 0600, loaded per service through `env_file` and never baked into an image.
  A container sees only its own file. The templates list the same keys as the project's `appsettings.json`, minus the few keys compose owns; each section header names the containers that read the key.
- **Deployment, compose inputs:** copy `deploy/.env.uat.example` to `deploy/.env.uat.local` (or the production example to `deploy/.env.production.local`, both git-ignored) and set the project name, the four image
  references, `TECHSTRAP_ENV_DIR`, the loopback ports, the subnet, `REVERSE_PROXY_CIDR` and `TECHSTRAP_DB_NETWORK`. These are compose inputs, not secrets. The two templates set the same variable names.

Compose interpolates only the inputs file. The per-service files are read with `format: raw` (Compose 2.30 or newer): write `KEY=value` with no quotes; `$` and `#` are literal.

`environment:` in `deploy/docker-compose.yml` sets what compose owns and overrides the env file: `ASPNETCORE_ENVIRONMENT=Production`, `DOTENV__ENABLED=false`, the Api address and key-ring path of the Admin,
the key-ring path of the Portal, the storage path of the Api and the trusted networks (the Api trusts the compose subnet and `REVERSE_PROXY_CIDR`; the Admin and the Portal trust only `REVERSE_PROXY_CIDR`).
Do not list those keys in an env file.

Keep a filled env file out of the repository. A new setting is added to `appsettings.json`, the `.env.example` and the deploy template together; `scripts/tests/ConfigContract.Tests.ps1` fails until they agree.

## One-time host setup

1. **Postgres.** Run Postgres 17 as its own instance, and create a database and a user for TechStrap (the role needs connect and schema rights; the Api applies migrations at start). Attach the Postgres container to a
   Docker network that exists before TechStrap starts, and give that network a stable name. The Api and the Worker join it; the Admin and the Portal never do:

   ```bash
   docker network create techstrap-db
   ```

2. **Postgres password.** It sits inside a connection string, so use only letters, digits and `- _ . ~`, for example `openssl rand -hex 24`. A semicolon, equals sign, quote or space breaks the string.
   Set `ConnectionStrings__TechStrap` in `.env.api` and in `.env.worker` to `Host=<postgres host name on that network>;Port=5432;Database=techstrap;Username=techstrap;Password=<password>`.
3. **Env files.**

   ```bash
   sudo install -d -m 0700 /etc/techstrap/uat
   for app in api worker admin portal; do
     sudo install -m 0600 -o root -g root deploy/.env.$app.example /etc/techstrap/uat/.env.$app
   done
   sudoedit /etc/techstrap/uat/.env.api    # and .env.worker, .env.admin, .env.portal
   ```

   Fill in the blanks. Each host refuses to start while a required value is blank, and its log names the key:

   | File | Required | Notes |
   | --- | --- | --- |
   | `.env.api` | `ConnectionStrings__TechStrap`, `AUTHENTICATION__JWTBEARER__AUTHORITY`, `AUTHENTICATION__JWTBEARER__AUDIENCES__0` (uncomment it), `TECHSTRAP_PORTAL_PUBLIC_URL` | The Api trusts the compose subnet and `REVERSE_PROXY_CIDR`. `TECHSTRAP_ADMIN_PUBLIC_URL` is optional. |
   | `.env.worker` | `ConnectionStrings__TechStrap`, `EMAIL__SMTP__HOST`, `EMAIL__SMTP__DEFAULTFROM` | To run without email set `EMAILOUTBOX__ENABLED=false`. Keep `TECHSTRAP_AUTOCLOSE_DAYS` equal to the Api value. |
   | `.env.admin` | `AUTH__AUTHORITY` (https), `AUTH__CLIENTID`, `AUTH__CLIENTSECRET` | See [ADMIN-APP.md](../development/ADMIN-APP.md) and [AGENT-AUTHENTICATION.md](AGENT-AUTHENTICATION.md). The group keys must match the Api. |
   | `.env.portal` | none yet | PHASE-09 adds the Api address and the public URL. |

   Set `ALLOWEDHOSTS` in each file to the real public host names if you want host filtering (keep the health-probe hosts `localhost`). Sentry and OpenTelemetry are optional and disabled by default; their DSN and OTLP headers are secrets.
4. **Compose inputs.**

   ```bash
   cp deploy/.env.uat.example deploy/.env.uat.local
   ```

   Edit it: pin the four `TECHSTRAP_*_IMAGE` values to one release tag (never `latest`), set `TECHSTRAP_ENV_DIR`, `TECHSTRAP_DB_NETWORK` and `REVERSE_PROXY_CIDR`. With loopback-only ports and a proxy on the same host, the proxy appears
   as the compose gateway (`172.16.31.1/32`); a proxy on another machine needs its own address (a single address, never a wide range such as `172.16.0.0/12` or `0.0.0.0/0`).
   UAT and production on one host need different subnets and ports; change `TECHSTRAP_SUBNET` only after checking the _template CLIENT_IP_RATE_LIMITING.md registry.
5. **Registry login.** The images are public or private on GHCR. If private: `echo <token> | docker login ghcr.io -u <user> --password-stdin`.

## Deploy

```bash
# UAT, after the env files and the inputs are in place:
docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml config --quiet
docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml pull
docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml up -d --wait
docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml ps

# Production: the same four commands with deploy/.env.production.local.
```

`config --quiet` fails with a message naming the missing input or the missing env file (for example `.env.worker`), before anything is pulled. The compose starts the Api first; the Admin, the Portal and the Worker wait for a healthy Api.
`restart: unless-stopped` is set, and each container has a `curl` health check.

**Migrations.** The Api migrates the database at start under an advisory lock when `DATABASE__MIGRATEONSTARTUP` is `true` (the default and the template value). The Worker never migrates. Set it to `false` in `.env.api` to migrate by another route.

## Health checks and acceptance

```bash
curl --fail http://127.0.0.1:<api port>/health/ready     # Api: Postgres reachable and migrated
curl --fail http://127.0.0.1:<admin port>/health/live
curl --fail http://127.0.0.1:<portal port>/health/live
docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml ps   # every service healthy
```

Also check that the ports are bound to `127.0.0.1` only (`docker compose ... ps`), that the Admin sign-in redirects to the provider and back, and that a test ticket reaches the queue and sends its email.
The Worker has no published port: its health is the container health (`/health/ready`).

## Rollback and upgrade

Record the image references and take a database backup and a copy of the `admin-keys` volume before a rollout. To roll back the application, set the previous tags in `deploy/.env.<env>.local`, then run `pull` and `up -d --wait` again.
A database backup is only restored as a separate, deliberate recovery decision: the Api's migrations only move forward. To upgrade, set the new tags and run the same two commands. Do not use `down --volumes`: the `techstrap-storage`,
`admin-keys` and `portal-keys` volumes hold attachments and the cookie keys (deleting a key volume signs every agent out).

## Checking without deploying

`pwsh scripts/Test-ComposeSmoke.ps1 -DeployComposeOnly` resolves `deploy/docker-compose.yml` with the UAT template and dummy env files (`config` only: it never pulls an image and never starts the stack, which needs real images and an external Postgres).
`pwsh scripts/Invoke-ScriptTests.ps1` runs the config-contract and compose suites.
````

Replace `scripts/Test-ComposeSmoke.ps1`:

```powershell
<#
.SYNOPSIS
Starts the local compose stack, checks that the Api and the Admin answer, and stops it again.
.DESCRIPTION
The PHASE-07 T20 check: "docker compose up" gives a healthy Admin and an Api whose /health/ready answers 200. It is opt-in (it builds four images and needs Docker),
so it is not part of the default CI run; run it by hand before merging a change to a Dockerfile, a compose file or the host wiring, or start the "Compose smoke" workflow.

It never touches a stack you already run. It uses its own compose project name (techstrap-smoke, never the default "techstrap"), publishes every host port on a free
port chosen by Docker instead of 8080 to 8082 and 8025, and stops only that project. It never removes volumes: "docker compose down" is run without -v, so the Postgres and
key-ring volumes of the smoke project stay for the next run (they are named techstrap-smoke_*; remove them yourself when you want them gone).

The Admin starts with the placeholder OIDC settings of src/TechStrap.Admin/appsettings.Development.json, so nothing here signs in; it checks the container's health and its /health/ready endpoint.

With -CheckDeployCompose it also resolves deploy/docker-compose.yml (the image-only UAT and production stack) with the committed UAT input template and dummy scoped env files:
"docker compose config --quiet" only. It never pulls an image and never starts that stack, which needs real images and an external Postgres.
.PARAMETER ProjectName
The compose project name. "techstrap" is refused, because that is the name of the stack you may be running.
.PARAMETER NoBuild
Do not rebuild the images; use the ones that exist (techstrap-smoke-*:local, left by an earlier run).
.PARAMETER KeepRunning
Leave the stack running after the checks (for looking at it); stop it later with: docker compose -p techstrap-smoke down
.PARAMETER CheckDeployCompose
Also run "docker compose config --quiet" on deploy/docker-compose.yml against a temporary copy of the UAT input template and dummy env files. Needs no images and no network.
.PARAMETER DeployComposeOnly
Run only the deploy compose check and nothing else: no build, no stack. Implies -CheckDeployCompose.
.PARAMETER DryRun
Print the commands and the override file and run nothing.
.EXAMPLE
pwsh ./scripts/Test-ComposeSmoke.ps1
.EXAMPLE
pwsh ./scripts/Test-ComposeSmoke.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    [string] $ComposeFile = (Join-Path $PSScriptRoot '..' 'docker-compose.yml'),
    [string] $ProjectName = 'techstrap-smoke',
    [int] $TimeoutSeconds = 900,
    [switch] $NoBuild,
    [switch] $KeepRunning,
    [switch] $CheckDeployCompose,
    [switch] $DeployComposeOnly,
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ProjectName -eq 'techstrap') {
    throw "Refusing to use the project name 'techstrap': it is the name of the stack you run by hand, and this script stops its project when it finishes."
}

# Every published port becomes "a free port on loopback", so a stack that already holds 8080 to 8082 or 8025 is not in the way. "!override" replaces the list instead of adding to it.
# The images get their own names too (techstrap-smoke-*:local), so building them never replaces the techstrap-*:local images of the stack you run by hand.
$overrideText = @'
services:
  api:
    image: techstrap-smoke-api:local
    ports: !override
      - "127.0.0.1::80"
  worker:
    image: techstrap-smoke-worker:local
  admin:
    image: techstrap-smoke-admin:local
    ports: !override
      - "127.0.0.1::80"
  portal:
    image: techstrap-smoke-portal:local
    ports: !override
      - "127.0.0.1::80"
  mailpit:
    ports: !override
      - "127.0.0.1::8025"
'@

$overridePath = Join-Path ([System.IO.Path]::GetTempPath()) "techstrap-smoke-$([guid]::NewGuid().ToString('N')).override.yml"
$composeArguments = @('compose', '-p', $ProjectName, '-f', (Resolve-Path -LiteralPath $ComposeFile).Path, '-f', $overridePath)

function Invoke-Compose {
    param([Parameter(Mandatory)][string[]] $Arguments, [switch] $AllowFailure)

    $output = & docker @composeArguments @Arguments 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        throw "docker $($composeArguments -join ' ') $($Arguments -join ' ') failed ($LASTEXITCODE):`n$output"
    }

    return $output
}

function Get-PublishedPort {
    param([Parameter(Mandatory)][string] $Service, [Parameter(Mandatory)][int] $ContainerPort)

    $mapping = (Invoke-Compose -Arguments @('port', $Service, "$ContainerPort")).Trim()
    if ($mapping -notmatch ':(?<port>\d+)\s*$') {
        throw "Could not read the published port of $Service from '$mapping'."
    }

    return [int] $Matches['port']
}

function Assert-Ready {
    param([Parameter(Mandatory)][string] $Name, [Parameter(Mandatory)][int] $Port)

    $uri = "http://127.0.0.1:$Port/health/ready"
    $response = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 30 -SkipHttpErrorCheck
    if ($response.StatusCode -ne 200) {
        throw "$Name answered $($response.StatusCode) at $uri (expected 200)."
    }

    Write-Output "ok   $Name $uri -> 200"
}

# The deploy compose is only ever resolved here, never started: it needs real GHCR images and an external Postgres. The scoped env files are dummy copies of the committed
# templates in a temporary directory, which is also what makes "required: true" resolve.
function Test-DeployCompose {
    $deployFile = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..' 'deploy' 'docker-compose.yml')).Path
    $deployDirectory = Split-Path -Parent $deployFile
    $envDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "techstrap-deploy-check-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $envDirectory | Out-Null
    try {
        foreach ($app in 'api', 'worker', 'admin', 'portal') {
            Copy-Item -LiteralPath (Join-Path $deployDirectory ".env.$app.example") -Destination (Join-Path $envDirectory ".env.$app")
        }

        $portable = $envDirectory -replace '\\', '/'
        $inputs = Join-Path $envDirectory 'inputs.env'
        Get-Content -LiteralPath (Join-Path $deployDirectory '.env.uat.example') |
            ForEach-Object { if ($_ -like 'TECHSTRAP_ENV_DIR=*') { "TECHSTRAP_ENV_DIR=$portable" } else { $_ } } |
            Set-Content -LiteralPath $inputs
        $output = & docker compose --env-file $inputs -f $deployFile config --quiet 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "deploy/docker-compose.yml does not resolve with the UAT input template ($LASTEXITCODE):`n$output"
        }

        Write-Output 'ok   deploy/docker-compose.yml resolves (config only; nothing pulled or started)'
    }
    finally {
        Remove-Item -LiteralPath $envDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($DeployComposeOnly) {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue) -or ((& docker compose version 2>&1 | Out-String) -notmatch 'Docker Compose')) {
        throw 'Docker with the compose plugin is required.'
    }

    Test-DeployCompose
    return
}

# The four images are built one after another, never by "up --build": compose builds them in parallel, and four restores into the one shared NuGet cache mount
# (Dockerfile --mount=type=cache,id=techstrap-nuget) can corrupt each other ("Could not find file .../markdig/...").
$services = @('api', 'worker', 'admin', 'portal')
$upArguments = @('up', '-d', '--wait', '--wait-timeout', "$TimeoutSeconds")

if ($DryRun) {
    Write-Output "# override file ($overridePath)"
    Write-Output $overrideText
    if (-not $NoBuild) {
        foreach ($service in $services) {
            Write-Output "docker $($composeArguments -join ' ') build $service"
        }
    }

    Write-Output "docker $($composeArguments -join ' ') $($upArguments -join ' ')"
    if ($CheckDeployCompose) {
        Write-Output "docker compose --env-file <UAT input template with dummy env files> -f deploy/docker-compose.yml config --quiet   (config only: no pull, no up)"
    }

    Write-Output "docker $($composeArguments -join ' ') port api 80"
    Write-Output "docker $($composeArguments -join ' ') port admin 80"
    Write-Output "GET /health/ready on the Api and on the Admin, expecting 200"
    Write-Output "docker $($composeArguments -join ' ') ps admin --format json   (Health must be healthy)"
    if (-not $KeepRunning) {
        Write-Output "docker $($composeArguments -join ' ') down"
    }

    return
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue) -or ((& docker compose version 2>&1 | Out-String) -notmatch 'Docker Compose')) {
    throw 'Docker with the compose plugin is required.'
}

if ($CheckDeployCompose) {
    Test-DeployCompose
}

Set-Content -LiteralPath $overridePath -Value $overrideText -Encoding utf8
$failed = $true
try {
    Write-Output "Starting project $ProjectName (this builds the images unless -NoBuild is given)..."
    if (-not $NoBuild) {
        foreach ($service in $services) {
            Write-Output "Building $service..."
            Invoke-Compose -Arguments @('build', $service) | Out-Null
        }
    }

    Invoke-Compose -Arguments $upArguments | Out-Null

    $apiPort = Get-PublishedPort -Service 'api' -ContainerPort 80
    $adminPort = Get-PublishedPort -Service 'admin' -ContainerPort 80
    Assert-Ready -Name 'Api' -Port $apiPort
    Assert-Ready -Name 'Admin' -Port $adminPort

    $admin = (Invoke-Compose -Arguments @('ps', 'admin', '--format', 'json') | ConvertFrom-Json)
    if ($admin.Health -ne 'healthy') {
        throw "The Admin container reports health '$($admin.Health)' (expected healthy)."
    }

    Write-Output 'ok   Admin container is healthy'
    $failed = $false
}
finally {
    if ($failed) {
        Write-Output (Invoke-Compose -Arguments @('logs', '--tail', '60', 'api', 'admin') -AllowFailure)
    }

    if (-not $KeepRunning) {
        # Never -v: the volumes are not this script's to delete.
        Invoke-Compose -Arguments @('down') -AllowFailure | Out-Null
    }

    Remove-Item -LiteralPath $overridePath -Force -ErrorAction SilentlyContinue
}

Write-Output 'Compose smoke passed.'
```

In `README.md`, replace:

```text
published ports and a proxy on the same host, that is the compose gateway (`172.16.31.1/32`), never a wide range.
```

with:

```text
published ports and a proxy on the same host, that is the compose gateway (`172.16.31.1/32`), never a wide range.

UAT and production run from one image-only compose, `deploy/docker-compose.yml`: pinned GHCR image tags, a scoped env file per service under `/etc/techstrap/<env>/`
and a separate Postgres on an external Docker network. The runbook is [DEPLOYMENT.md](docs/self-hosting/DEPLOYMENT.md).
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1`
Expected: PASS, 12 tests.

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ComposeSmoke.Tests.ps1`
Expected: PASS, 10 tests.

Run: `pwsh -File scripts/Test-ComposeSmoke.ps1 -DeployComposeOnly`
Expected: `ok   deploy/docker-compose.yml resolves (config only; nothing pulled or started)`.

To see the pin guards (do not commit these edits), run `git add docs scripts README.md` first, so `git checkout -- <file>` restores the passing version. Make the change, rerun the Pester command, expect the failure named, and restore the file. This was run in the scratch copy:

- `docs/self-hosting/DEPLOYMENT.md`: change `set the previous tags` to `set the old ones`. Fails `covers the shared Postgres network, the 0600 env directory, the registry login, health checks, rollback by image tag and migrations`.
- `docs/self-hosting/DEPLOYMENT.md`: replace "Do not use `down --volumes`" with "Run docker compose down -v to reset". Fails `never tells the operator to remove volumes and is linked from the README`.

- [ ] **Step 5: Full verification**

Run, in this order:
- `dotnet build TechStrap.slnx -c Release` -> Expected: `0 Warning(s)`, `0 Error(s)`.
- `dotnet test --solution TechStrap.CI.slnf -c Release` -> Expected: PASS, 4046 tests (4020 before this change plus 26 new). The Postgres-backed tests can fail once on Windows with `lacked sufficient buffer space` right after a full rebuild; that is socket exhaustion, not this change: rerun.
- `pwsh -File scripts/Invoke-ScriptTests.ps1` -> Expected: PASS, 253 tests (the config contract is 67, the compose cases 55).
- `pwsh -File scripts/Test-ComposeSmoke.ps1 -CheckDeployCompose` -> Expected: `ok   deploy/docker-compose.yml resolves ...`, then the local stack builds and starts: `ok   Api ... -> 200`, `ok   Admin ... -> 200`, `ok   Admin container is healthy`, `Compose smoke passed.`
- `docker compose --env-file deploy/.env.uat.example -f deploy/docker-compose.yml config --no-env-resolution --quiet` -> Expected: exit 0 with the committed template values. `--no-env-resolution` skips reading the env files, so `/etc/techstrap/uat` need not exist here; without it, `config` needs the four env files and fails naming a missing one. Removing an input from a copy of the template fails with its name (pinned by `ComposeFiles.Tests.ps1`).

- [ ] **Step 6: Owner actions (record the result in the PR)**

1. Create `/etc/techstrap/uat/` from the four templates, mode 0600 (`docs/self-hosting/DEPLOYMENT.md`, "One-time host setup"), and the shared Docker network for the separate Postgres.
2. Copy `deploy/.env.uat.example` to `deploy/.env.uat.local`, pin the four image tags, then run `config --quiet`, `pull`, `up -d --wait` and `ps` against the external Postgres, and the health checks.
3. Confirm the Admin signs in once Authentik exists (owner action 7), and that a test ticket sends its email.

- [ ] **Step 7: Commit**

```bash
git add docs/self-hosting/DEPLOYMENT.md \
  README.md \
  scripts
git diff --cached --stat
git commit -m "docs(deploy): the deployment runbook and a config-only deploy compose check (D-043)" -m "docs/self-hosting/DEPLOYMENT.md follows the sibling runbooks: the layout of the scoped env files, the one-time host setup (the shared Postgres network, a connection-string-safe password, mode 0600 env files, registry login), config --quiet then pull then up -d --wait then ps, health checks, rollback by image tag and migrations. Test-ComposeSmoke.ps1 gains -DeployComposeOnly and -CheckDeployCompose, which resolve deploy/docker-compose.yml with the UAT template and dummy env files and never pull an image or start that stack." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

