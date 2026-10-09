# PHASE-12b Release hardening (part 2): docs and scripts: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the operator-facing half of PHASE-12 as one pull request (PHASE-12b, recorded under D-051): encrypted backup and restore scripts with a runbook and one local rehearsal (P12-T13), k6 load scenarios with a runner and a results skeleton (P12-T11), the generic OIDC self-host guide with an environment reference that is checked against the deploy templates (P12-T16), and the Authentik worked example with consistent OIDC guidance (P12-T17). No .NET code, no Contracts change, no migration, no new CI job.

**Architecture:** Scripts and docs only. Bash scripts (`deploy/backup.sh`, `deploy/restore.sh`) run on the Linux hosts and drive `docker run` containers: `pg_dump -Fc` runs inside a `postgres:17` container so the client matches the server, volumes are archived by `alpine tar`, files are encrypted with `openssl enc -aes-256-cbc -pbkdf2`. The k6 suite lives in `tests/load/` (`lib/` helpers, one module per scenario, `scenarios/sustained.js` and `scenarios/spike.js` composing them) and is started by `scripts/Invoke-LoadTest.ps1`. Everything that runs in CI is a fast static Pester file under `scripts/tests/` (no Docker, no network, no k6, no bash execution): the k6 runs and backup drills are on-demand only. The env reference table in `SELF-HOSTING.md` is kept honest by `SelfHostDocs.Tests.ps1`, which compares it with every `deploy/.env.<app>.example`.

**Tech Stack:** bash, Docker, `postgres:17`, `alpine`, openssl, k6 (JavaScript), PowerShell 7, Pester 6.2.0, Markdown, Caddy (examples only), systemd (example only).

**Spec:** `docs/architecture/PHASE-12-release-hardening.md` (P12-T11, P12-T13, P12-T16, P12-T17; the Load test, Backup/restore runbook and Self-host docs bullets; the "Corrections (D-051, 2026-10-08)" block, which wins where it differs); `docs/architecture/04-DECISION-LOG.md` D-051, D-029, D-039, D-043, D-044.

### Owner decisions (2026-10-08 / 2026-10-09), recorded as D-051
1. PHASE-12 ends at `v0.3.0`; `v1.0.0` is a later API-lock decision. 12b is the second of three pull requests.
2. Nothing new runs in CI beyond static checks. k6 runs and backup drills are on-demand scripts; the only CI addition is Pester under `scripts/tests/`, which must stay static and fast.
3. 0.x stays lean: no SBOMs and no extra workflows.
4. The load and recovery budgets are accepted as stated (sustain 20 req/s for 10 min with p95 < 500 ms and 0 server errors; spike to 100 req/s for 60 s with 429, not 5xx, and recovery within 30 s). The UAT drill and the UAT load run are 12c (T15, T12).

### Decisions made while drafting (the 12b rulings; Task 5 records them as the D-051 addendum "12b rulings (2026-10-09)")
- **Ruling 1, k6 layout:** `tests/load/lib/{auth,payloads,clients,checks,profile}.js`; scenario modules `tests/load/{intake-trusted,intake-public,portal-form,customer-view,kb-search}.js`; composers `tests/load/scenarios/{sustained,spike}.js`; `tests/load/README.md`; runner `scripts/Invoke-LoadTest.ps1`; results skeleton `docs/load-test-results.md`; run output in `tests/load/results/` (gitignored). Thresholds are code: `p(95)<500` and a custom `server_errors` rate with `rate==0`. (`profile.js` is the one helper beyond the four the brief names: it turns `TS_RATE`/`TS_DURATION` and the mix weights into k6 executors.)
- **Why a custom `server_errors` metric:** k6 tag selectors in thresholds match exact tag values, so `http_req_failed{status:5xx}` cannot select a status class. `checks.js` adds a `Rate` named `server_errors` (true for status >= 500) and a `Counter` named `throttled_429`; the thresholds use those. The spike thresholds are `server_errors: ['rate==0']`, `throttled_429{phase:spike}: ['count>0']` and `http_req_duration{phase:recovery}: ['p(95)<500']`.
- **Ruling 2, rate limits vs the budget:** k6 models many clients by rotating `X-Forwarded-For` over a /24 (`TS_FORWARDED_PREFIX`, default `10.99.0.`, host octets 1..`TS_IP_COUNT` with default 60) and the stack under test trusts the runner as a proxy. Sustained load of 20 req/s is split 10 trusted, 2 public, 6 KB search, 2 customer view (mix 50/10/30/10); each rotated IP then sees about 10 trusted requests a minute (limit 120 per 60 s), 2 public-key requests a minute (limit 10 per 60 s) and 2 token reads a minute (limit 60 per 60 s). The portal form runs at 1 request per minute, so each of the 60 IPs submits once an hour (limit 5 per 10 min). The spike pins ONE IP (prefix + `250`) on the trusted key so the 120 per 60 s limit must produce 429s; the recovery phase uses rotated IPs, because the throttled IP is blocked for the rest of its 60 s window by design.
- **Ruling 3, outbox and dead letters** are counted by SQL after the run: the runner prints the counts through `docker compose exec -T postgres psql` for `-Target local`; for UAT the README gives the operator the same SQL. Not automated in CI.
- **Ruling 4, backup shape:** bash, `set -euo pipefail`, no extra installs beyond Docker and openssl; the same scripts work against the dev compose (project `techstrap`, volumes `techstrap_techstrap-storage`, `techstrap_admin-keys`, `techstrap_portal-keys`) and the deploy compose (`TECHSTRAP_PROJECT`, external Postgres 17). `age` is mentioned in the runbook as an optional alternative only.
- **Database access modes of `backup.sh`:** exactly one of `--db-url <url>` (deploy: the URL, or env `TECHSTRAP_DB_URL` so the password stays out of `ps`; the dump container joins `--db-network`, default `techstrap-db`), `--db-container <name>` (dev: the dump container joins that container's network namespace with `--network container:<name>` and reads `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` from it with `docker exec`), or `--db-host <host>` with `--db-network`, `--db-user`, `--db-name` and the password from env `PGPASSWORD` (forwarded by name with `-e PGPASSWORD`). The URL or password never appears on a command line that `ps` shows; it is passed by `-e NAME` and read inside the container.
- **Backup layout:** `<out>/<project>/<UTC yyyyMMddTHHmmssZ>/` holding `db.dump.enc`, `techstrap-storage.tar.gz.enc`, `admin-keys.tar.gz.enc`, `portal-keys.tar.gz.enc` and `manifest.txt` (plain `key=value` lines: `created_utc`, `project`, `format=1`, `encrypted`, `migrations_count`, `ticket_count`, `attachment_count`, then `file.<name>.bytes=`/`file.<name>.sha256=` of each stored file). The manifest holds no secret. With `--no-encrypt` the suffix `.enc` is dropped.
- **Restore shape:** `restore.sh --from <backup dir> --target-project <name>` restores into a SCRATCH stack: a throwaway `postgres:17` container `<target>-postgres` on network `<target>_default` and volumes `<target>_pgdata`, `<target>_techstrap-storage`, `<target>_admin-keys`, `<target>_portal-keys`. Promotion to a real stack is a deliberate second step in the runbook (stop apps, restore with `--db-url ... --yes --confirm-project <live project> --overwrite`). The script refuses a target project that already owns any of those volumes unless `--overwrite` is given, and refuses a manifest that lacks any of the three volume archives (it never silently skips the keys volumes; the only way to skip one is the explicit, loudly warned `--skip-volume <name>`).
- **Teardown:** `restore.sh --teardown --target-project <name>` removes only that project's scratch container, network and the four named volumes with `docker volume rm <explicit names>`; it refuses `techstrap`, `techstrap-uat`, `techstrap-prod` and the project named in the manifest. No script anywhere uses `down -v` or `--volumes`.
- **Row counts:** `ticket_count` and `attachment_count` are `SELECT count(*)` over the ticket and attachment tables; the implementer confirms the real table names from the model snapshot (`src/TechStrap.Infrastructure/Migrations/*ModelSnapshot.cs`) and puts them in two named constants at the top of the script (`TICKETS_TABLE`, `ATTACHMENTS_TABLE`).
- **Ruling 5, runbook:** RPO 24 h, RTO 4 h; the consistency caveat is stated as fact: ticket hard delete and requester erase delete files after commit, so "attachments are append-only" (spec) is false and a restore RESURRECTS data erased after the backup time; the operator re-runs those erasures (list them from `admin_events`).
- **Ruling 6, self-host docs:** `docs/self-hosting/SELF-HOSTING.md` and `docs/self-hosting/AUTHENTIK.md` (not `docs/self-hosting.md` / `docs/self-hosting-authentik.md`); the Syntax Circus repo `syntax-circus-authentik` is private, so AUTHENTIK.md is self-sufficient and names that repo and its docs only by title, with no link.
- **Caddy body limit:** the brief says `max_size 26MB`; Caddy's `MB` is 10^6 bytes (26,000,000), which is smaller than a legal 25 MiB submission (26,214,400 bytes plus multipart overhead) and than the Portal's own 27,262,976-byte buffer (DEPLOYMENT.md). The guide therefore uses `max_size 26MiB` (27,262,976 bytes) so the proxy never refuses a submission the app would accept. Task 5 records this in the addendum.
- **Ruling 8, close-out pins:** the unticked set becomes T12, T14, T15, T18, T20, T21; the roadmap and discovery row reads "12a merged (PR #28); 12b complete (pending merge)".
- **Helper reuse in Pester:** `ConfigContract.Tests.ps1` defines its helpers inside `BeforeAll`, so they cannot be dot-sourced; `SelfHostDocs.Tests.ps1` carries a small commented copy of `Get-EnvEntries`, `Get-EnvKeys` and `ConvertTo-IndexlessKey` (same regex, same behaviour).

## Global Constraints
- No .NET change: no file under `src/` or `tests/TechStrap.*` is touched, so `dotnet build TechStrap.slnx -c Release` is unaffected. No migration (`dotnet ef migrations has-pending-model-changes` still reports "No changes"). Nothing in 12b touches `TechStrap.Contracts`.
- NO new CI jobs and no workflow edits. `scripts/tests/*.Tests.ps1` already runs in CI (`Invoke-ScriptTests.ps1`), so every new Pester file is static and fast: it reads files, never starts Docker, never calls the network, never runs k6, never executes `backup.sh` or `restore.sh`. The only script a test executes is `Invoke-LoadTest.ps1 -DryRun`, which prints a plan and calls nothing external. k6 runs, the backup rehearsal and the UAT drill are on-demand.
- Security: no secrets in the repo. The only key-like strings allowed are the `NotASecret` dev keys (`tsk_devOrbitlyServerKeyNotASecret00000000000000`, `tsp_devOrbitlyAppKeyNotASecret00000000000000000`). The load runner and k6 scripts never print key values; passphrases are read from files (`--passphrase-file`), never from a flag value, and the Authentik guide contains placeholders only.
- Encoding: new files LF and ASCII only (the repo's `.gitattributes` already sets `*.sh` and `*.ps1` to `eol=lf`; `*.js` and `*.md` are written LF and pinned LF by the tests where noted). Existing files are changed with the Edit tool so their line endings are preserved.
- Scripts: bash with `#!/usr/bin/env bash` and `set -euo pipefail`; PowerShell with comment-based help, `[CmdletBinding()]`, `Set-StrictMode -Version Latest`, `$ErrorActionPreference = 'Stop'` and a `-DryRun` switch (the `Send-TestTicket.ps1` shape). No script uses `down -v`, `--volumes` or `docker volume prune`.
- Tests: Pester 6.2.0 through `pwsh -File scripts/Invoke-ScriptTests.ps1`. TDD: the failing Pester pins come first with the RED command and expected failure recorded; mutations run against committed code and are restored with `git checkout -- <file>`.
- Versioning stays pre-1.0: no document tells the reader to install `1.0.0` or an `rc`; examples use `0.2.0`.
- Commits: Conventional Commits, staged by explicit path (never `git add -A`/`-f`, never `.superpowers/`), `git diff --cached --stat` first, each ending with exactly:
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- Branch: `feat/phase-12b-docs-scripts` (already checked out). One pull request, "PHASE-12b: docs and scripts (D-051)".
- Verification (whole PR): see the section below.

## Review Focus
1. A restore must not silently skip the keys volumes (losing them signs every agent out) - Task 1 `BackupScripts.Tests.ps1` `restore.sh restores all three volumes and refuses a manifest that lacks one`.
2. A k6 scenario without thresholds must fail the static check, and a script that reads an undocumented env var must fail - Task 2 `LoadScripts.Tests.ps1` `every scenario module and composer declares thresholds` / `the README documents every environment variable the scripts read`.
3. An env key added to a deploy template without a row in the SELF-HOSTING table (or a table key that is in no template) must fail - Task 3 `SelfHostDocs.Tests.ps1` `lists every key of every deploy template` / `lists no key that is not in a template`.
4. A secret value (or the stale `techstrap-admin` issuer slug) in the Authentik guide must fail - Task 4 `SelfHostDocs.Tests.ps1` `AUTHENTIK.md contains no client secret value` / `no self-hosting doc uses the techstrap-admin issuer slug`.
5. Nothing added runs in CI beyond static Pester: no workflow file changes, no Docker in a test - Task 5 `RepositoryDocs.Tests.ps1` `adds no workflow file and no CI job for 12b` and the Global Constraints.

---

### Task 1: Backup and restore scripts and the runbook (P12-T13)

**Files:**
- Create: `deploy/backup.sh`, `deploy/restore.sh`, `docs/runbooks/backup-restore.md`, `scripts/tests/BackupScripts.Tests.ps1`
- Modify: `scripts/tests/RepositoryDocs.Tests.ps1` (new `Describe 'Backup and restore runbook (PHASE-12b)'`)

**Interfaces:**
- Produces: `deploy/backup.sh` flags `--project`, `--out`, `--keep-days`, `--dry-run`, `--passphrase-file`, `--no-encrypt`, `--db-url`, `--db-container`, `--db-host`, `--db-network`, `--db-user`, `--db-name`, `--help`; `deploy/restore.sh` flags `--from`, `--target-project`, `--passphrase-file`, `--no-encrypt`, `--db-url`, `--db-network`, `--confirm-project`, `--yes`, `--overwrite`, `--skip-volume`, `--teardown`, `--dry-run`, `--help`; the backup directory layout and `manifest.txt` keys from "Decisions made while drafting"; the runbook that Task 3 (SELF-HOSTING.md "Backups") and Task 5 link to.

- [x] **Step 1: Pins first (RED).** Create `scripts/tests/BackupScripts.Tests.ps1`:
  ```powershell
  BeforeAll {
      $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
      $script:Backup = Join-Path $script:RepoRoot 'deploy/backup.sh'
      $script:Restore = Join-Path $script:RepoRoot 'deploy/restore.sh'
      $script:Volumes = 'techstrap-storage', 'admin-keys', 'portal-keys'
  }

  Describe 'deploy/backup.sh and deploy/restore.sh (static pins, PHASE-12b)' {
      It 'both scripts exist, are LF and ASCII and start with the bash shebang' {
          foreach ($path in $script:Backup, $script:Restore) {
              Test-Path -LiteralPath $path | Should -BeTrue -Because $path
              $bytes = [System.IO.File]::ReadAllBytes($path)
              ($bytes -contains 13) | Should -BeFalse -Because "$path must be LF"
              $text = [System.IO.File]::ReadAllText($path)
              $text.StartsWith("#!/usr/bin/env bash`n") | Should -BeTrue
              ([regex]::IsMatch($text, '[^\x00-\x7F]')) | Should -BeFalse -Because "$path must be ASCII"
          }
      }

      It 'both scripts fail fast with set -euo pipefail' {
          foreach ($path in $script:Backup, $script:Restore) {
              ([System.IO.File]::ReadAllText($path)) | Should -Match '(?m)^set -euo pipefail\s*$'
          }
      }

      It 'backup.sh dumps with pg_dump -Fc inside a postgres:17 container' {
          $text = [System.IO.File]::ReadAllText($script:Backup)
          $text | Should -Match 'pg_dump -Fc'
          $text | Should -Match 'postgres:17'
          $text | Should -Match 'docker run --rm'
      }

      It 'backup.sh encrypts with openssl aes-256-cbc pbkdf2 from a passphrase file' {
          $text = [System.IO.File]::ReadAllText($script:Backup)
          $text | Should -Match 'openssl enc -aes-256-cbc -pbkdf2 -salt'
          $text | Should -Match '-pass "?file:'
          $text | Should -Match '--passphrase-file'
          $text | Should -Match '--no-encrypt'
      }

      It 'backup.sh has retention and dry-run flags' {
          $text = [System.IO.File]::ReadAllText($script:Backup)
          $text | Should -Match '--keep-days'
          $text | Should -Match '--dry-run'
      }

      It 'backup.sh dumps the database before it archives the volumes' {
          $text = [System.IO.File]::ReadAllText($script:Backup)
          $text.IndexOf('pg_dump -Fc') | Should -BeGreaterThan -1
          $text.IndexOf('pg_dump -Fc') | Should -BeLessThan $text.IndexOf('tar czf -') -Because 'dump first, then volumes (runbook consistency caveat)'
      }

      It 'both scripts name all three volumes' {
          foreach ($path in $script:Backup, $script:Restore) {
              $text = [System.IO.File]::ReadAllText($path)
              foreach ($volume in $script:Volumes) { $text | Should -Match ([regex]::Escape($volume)) -Because "$path must handle $volume" }
          }
      }

      It 'backup.sh archives volumes read-only through alpine tar' {
          $text = [System.IO.File]::ReadAllText($script:Backup)
          $text | Should -Match ':/v:ro'
          $text | Should -Match 'alpine'
          $text | Should -Match 'tar czf -'
      }

      It 'restore.sh restores all three volumes and refuses a manifest that lacks one' {
          $text = [System.IO.File]::ReadAllText($script:Restore)
          $text | Should -Match 'manifest is missing volume'
          $text | Should -Match '--skip-volume'
          $text | Should -Match 'WARNING: skipping volume'
          $text | Should -Match 'pg_restore'
          $text | Should -Match 'postgres:17'
      }

      It 'restore.sh verifies the migration history count against the manifest and prints the ticket and attachment counts' {
          $text = [System.IO.File]::ReadAllText($script:Restore)
          $text | Should -Match '__EFMigrationsHistory'
          $text | Should -Match 'migrations_count'
          $text | Should -Match 'ticket_count'
          $text | Should -Match 'attachment_count'
      }

      It 'restore.sh refuses to overwrite an existing stack and has a scoped teardown' {
          $text = [System.IO.File]::ReadAllText($script:Restore)
          $text | Should -Match '--overwrite'
          $text | Should -Match '--teardown'
          $text | Should -Match 'refusing'
      }

      It 'neither script removes volumes wholesale' {
          foreach ($path in $script:Backup, $script:Restore) {
              $text = [System.IO.File]::ReadAllText($path)
              $text | Should -Not -Match 'down\s+-v'
              $text | Should -Not -Match '--volumes'
              $text | Should -Not -Match 'volume prune'
          }
      }

      It 'neither script puts a passphrase or password value on a command line' {
          foreach ($path in $script:Backup, $script:Restore) {
              $text = [System.IO.File]::ReadAllText($path)
              $text | Should -Not -Match '-pass pass:'
              $text | Should -Not -Match '--passphrase\s+\S'
          }
      }
  }
  ```
  Append to `RepositoryDocs.Tests.ps1`:
  ```powershell
  Describe 'Backup and restore runbook (PHASE-12b)' {
      BeforeAll { $script:Runbook = Get-RepoText 'docs/runbooks/backup-restore.md' }

      It 'has the required sections' {
          foreach ($heading in 'Scope', 'RPO and RTO', 'Prerequisites and secrets', 'Taking a backup', 'Encryption and off-box copy', 'Retention', 'Restoring', 'Verification checklist', 'Consistency caveat', 'Disaster scenarios', 'Keys volumes', 'Client and server version', '12b rehearsal record') {
              $script:Runbook | Should -Match ('(?m)^## ' + [regex]::Escape($heading) + '\s*$') -Because "the runbook needs a $heading section"
          }
      }

      It 'states the targets, the migration check and the resurrection caveat' {
          foreach ($phrase in 'RPO', 'RTO', '24 h', '4 h', '__EFMigrationsHistory', 'resurrect', 'admin_events', 'deploy/backup.sh', 'deploy/restore.sh', 'OnCalendar', 'crontab') {
              $script:Runbook | Should -Match ([regex]::Escape($phrase)) -Because $phrase
          }
      }

      It 'never tells the operator to remove volumes and is ASCII' {
          $script:Runbook | Should -Not -Match 'down\s+-v(\s|$)'
          $script:Runbook | Should -Not -Match '--volumes'
          ([regex]::IsMatch($script:Runbook, '[^\x00-\x7F]')) | Should -BeFalse
      }

      It 'has no TODO and no passphrase value' {
          $script:Runbook | Should -Not -Match 'TODO'
          $script:Runbook | Should -Not -Match '(?i)passphrase\s*[=:]\s*[A-Za-z0-9]{8,}'
      }

      It 'keeps the backups folder out of git' {
          (Get-RepoText '.gitignore') | Should -Match '(?m)^backups/\s*$'
      }

      It 'links only to files that exist' {
          $links = [regex]::Matches($script:Runbook, '\]\((?<path>(?!https?:|#)[^)\s#]+)') | ForEach-Object { $_.Groups['path'].Value }
          foreach ($link in $links) {
              Test-Path -LiteralPath (Join-Path $script:RepoRoot 'docs' 'runbooks' $link) | Should -BeTrue -Because "backup-restore.md links to $link"
          }
      }
  }
  ```
  Run `pwsh -File scripts/Invoke-ScriptTests.ps1`: RED. Expected failures: every `BackupScripts.Tests.ps1` test fails (the files do not exist; `Test-Path` is false and `ReadAllText` throws) and the runbook `BeforeAll` throws (file missing). The `.gitignore` pin already passes.

- [x] **Step 2: Write `deploy/backup.sh`** (LF, ASCII; record the executable bit with `git update-index --chmod=+x` after `git add`). Structure, top to bottom:
  1. `#!/usr/bin/env bash`, a usage comment block, `set -euo pipefail`, `export MSYS_NO_PATHCONV=1` (Git Bash on Windows must not rewrite `/v` paths).
  2. Constants: `POSTGRES_IMAGE=postgres:17`, `ALPINE_IMAGE=alpine:3.20` (the implementer pins the newest `alpine:3.x` tag that `docker pull` resolves), `VOLUMES=(techstrap-storage admin-keys portal-keys)`, `TICKETS_TABLE`/`ATTACHMENTS_TABLE` (names confirmed from the model snapshot), defaults `PROJECT=techstrap`, `OUT_DIR=./backups`, `KEEP_DAYS=` (empty means no pruning), `DB_NETWORK=techstrap-db`.
  3. `usage()` and an argument loop (`while [[ $# -gt 0 ]]; do case "$1" in ... esac`) for the flags in Interfaces; a value flag with no value dies with `--x needs a value`; an unknown flag dies with usage. `--keep-days` must match `^[0-9]+$` and be at least 1. Exactly one of `--db-url` (or env `TECHSTRAP_DB_URL`), `--db-container`, `--db-host` must resolve, else `die "choose one database mode"`. Unless `--no-encrypt`, `--passphrase-file` is required, must be a readable regular file with at least 16 bytes (`wc -c`), else `die`.
  4. `run()` helper: prints `+ <command>` and executes, or under `--dry-run` prints `DRY-RUN: <command>` and returns 0 (dry-run touches no docker, creates no directory, writes no file). The passphrase path is printed, never its contents.
  5. `encrypt()` function: `openssl enc -aes-256-cbc -pbkdf2 -salt -pass "file:${PASSPHRASE_FILE}"` reading stdin and writing stdout, or `cat` under `--no-encrypt`.
  6. Database mode setup. `--db-url`: `DUMP_NETWORK="$DB_NETWORK"`, URL kept in the variable `TS_DB_URL` and forwarded with `-e TS_DB_URL`. `--db-container NAME`: `DUMP_NETWORK="container:NAME"`, and `TS_DB_URL` is built in the shell from `docker exec NAME printenv POSTGRES_USER|POSTGRES_PASSWORD|POSTGRES_DB` against `localhost:5432`, never echoed. `--db-host`: `TS_DB_URL="postgresql://${DB_USER}@${DB_HOST}:5432/${DB_NAME}"` and `-e PGPASSWORD`.
  7. Dump: `docker run --rm --network "$DUMP_NETWORK" -e TS_DB_URL -e PGPASSWORD "$POSTGRES_IMAGE" sh -c 'pg_dump -Fc --no-owner --dbname "$TS_DB_URL"' | encrypt > "$DEST/db.dump.enc"`. The counts come from `docker run ... "$POSTGRES_IMAGE" sh -c 'psql -At "$TS_DB_URL" -c "select count(*) from \"__EFMigrationsHistory\""'` (and the two tables), captured into variables.
  8. Volumes, strictly after the dump, in `VOLUMES` order: `docker run --rm -v "${PROJECT}_${v}:/v:ro" "$ALPINE_IMAGE" tar czf - -C /v . | encrypt > "$DEST/${v}.tar.gz.enc"`. A volume that does not exist (`docker volume inspect` fails) is fatal (`die "volume ${PROJECT}_${v} not found"`): a backup that quietly lacks the keys volumes is a failed backup.
  9. Manifest: `manifest.txt` with the keys in "Backup layout" (sizes from `wc -c`, sha256 from `sha256sum`, falling back to `shasum -a 256`), `encrypted=true|false`, `created_utc=$(date -u +%Y-%m-%dT%H:%M:%SZ)`. Never write a key, passphrase or URL into it.
  10. Retention (only with `--keep-days`): list `"$OUT_DIR/$PROJECT"` children whose name matches `^[0-9]{8}T[0-9]{6}Z$` and sorts below the cut-off name computed with `date -u -d "-N days" +%Y%m%dT%H%M%SZ` (GNU) or `date -u -v-Nd` (BSD); print `prune: <dir>` and `rm -rf -- "$dir"` it (the only `rm -rf`, on a path just matched against the name pattern under `$OUT_DIR/$PROJECT/`). Under `--dry-run` it prints `DRY-RUN: would prune <dir>` only.
  11. Final line: `backup complete: <dest>` and the three counts. `trap` on error removes a half-written `$DEST` (guarded by `[[ -n "${DEST:-}" ]]`), so a failed run never leaves a directory that retention or a restore could mistake for good.
- [x] **Step 3: Write `deploy/restore.sh`** (LF, ASCII, executable). Structure:
  1. Same header, constants and helpers as the backup script (`run`, `decrypt()` = `openssl enc -d -aes-256-cbc -pbkdf2 -pass "file:${PASSPHRASE_FILE}"` or `cat`).
  2. Arguments per Interfaces. `--from` (the timestamp directory) and `--target-project` are required (`--teardown` needs only `--target-project`). The target project name must match `^[a-z0-9][a-z0-9_-]*$`.
  3. Manifest load: parse `manifest.txt` with `grep '^key='`; check `format=1`; verify size and sha256 of every stored file against the manifest BEFORE restoring anything (`die "checksum mismatch for <file>"`); for each name in `VOLUMES` require its `file.<name>...` entries in the manifest and the file on disk, else `die "manifest is missing volume <name>"`; a name given with `--skip-volume` instead prints `WARNING: skipping volume <name> (a skipped keys volume signs every agent out)` and continues. `--skip-volume` may repeat and accepts only the three volume names.
  4. Safety: print `refusing: ...` and exit 1 when the target project owns any of the four target volumes and `--overwrite` is absent; refuse a target project equal to the manifest's `project` unless `--db-url`, `--yes` and `--confirm-project <that project>` are all present (the promotion path); refuse `--teardown` for `techstrap`, `techstrap-uat`, `techstrap-prod` and the manifest's project.
  5. Scratch database (default, no `--db-url`): create network `${TARGET}_default` if absent; create volume `${TARGET}_pgdata`; start `docker run -d --name "${TARGET}-postgres" --network "${TARGET}_default" -e POSTGRES_USER=techstrap -e POSTGRES_DB=techstrap -e POSTGRES_PASSWORD -v "${TARGET}_pgdata:/var/lib/postgresql/data" postgres:17` with a generated throwaway password held in a shell variable (`openssl rand -hex 16`) and forwarded by name; wait with a bounded loop on `pg_isready` through `docker exec` (60 tries, 1 s apart). `--db-url` (promotion): `RESTORE_NETWORK="${DB_NETWORK:-techstrap-db}"`, URL by `-e TS_DB_URL`.
  6. Database restore: `decrypt < "$FROM/db.dump.enc" | docker run --rm -i --network "$RESTORE_NETWORK" -e TS_DB_URL "$POSTGRES_IMAGE" sh -c 'pg_restore --clean --if-exists --no-owner --dbname "$TS_DB_URL"'`.
  7. Volumes: for each non-skipped volume `docker volume create "${TARGET}_${v}"` then `decrypt < "$FROM/${v}.tar.gz.enc" | docker run --rm -i -v "${TARGET}_${v}:/v" "$ALPINE_IMAGE" tar xzf - -C /v`.
  8. Verification: `psql -At` for `select count(*) from "__EFMigrationsHistory"` compared with `migrations_count` (mismatch: `die "__EFMigrationsHistory count <n> does not match the manifest <m>"`); ticket and attachment counts compared with `ticket_count` and `attachment_count` and printed as `tickets: <n> (manifest <m>) ok|MISMATCH`; the attachment FILE count in the restored storage volume (`find /v/attachments -type f | wc -l` in an alpine container) printed next to the DB attachment count. Exit 1 on any migration or table-count mismatch.
  9. `--teardown`: `docker rm -f "${TARGET}-postgres"`, `docker network rm "${TARGET}_default"`, then `docker volume rm` of the four explicit names (`${TARGET}_pgdata`, `${TARGET}_techstrap-storage`, `${TARGET}_admin-keys`, `${TARGET}_portal-keys`), each tolerating "not found". Never a wildcard, never the real project.
  10. Print the next steps (starting apps against the scratch stack is described in the runbook, not automated).
- [x] **Step 4: Write `docs/runbooks/backup-restore.md`.** Title `# Backup and restore runbook`; all sections are `##` in this order, with the facts each states:
  - `## Scope`: backed up = the TechStrap Postgres database (`pg_dump -Fc`), volume `techstrap-storage` (`/app/storage`: `attachments/{ticketId:N}/{guid:N}` and `kb-images/{guid}.{ext}`), `admin-keys` and `portal-keys` (ASP.NET Data Protection keys, `/app/dataprotection-keys`), plus `manifest.txt`. NOT backed up = observability data (logs, traces, Sentry), the `/etc/techstrap/<env>/` env files and their secrets (keep them in the operator's secret store), reverse-proxy config and TLS material, container images (re-pullable by tag), the external Postgres server configuration and roles. The outbox is in the database (90-day retention, D-039; dead letters kept).
  - `## RPO and RTO`: RPO 24 h (daily backup) and RTO 4 h (restore plus verification), the accepted 12b targets (D-051), to be proven on UAT in 12c (T15).
  - `## Prerequisites and secrets`: Docker and openssl on the host; the Docker engine reaches the external Postgres through the `techstrap-db` network; a passphrase file (`install -m 0600 /dev/null /etc/techstrap/backup.pass` then `openssl rand -base64 36 > /etc/techstrap/backup.pass`); the database URL in env `TECHSTRAP_DB_URL` (same secret store as `.env.api`); keep the passphrase off the box (a lost passphrase makes every backup unreadable).
  - `## Taking a backup`: the deploy command (`deploy/backup.sh --project techstrap-uat --out /var/backups/techstrap --keep-days 14 --passphrase-file /etc/techstrap/backup.pass --db-url "$TECHSTRAP_DB_URL"`), the dev command (`deploy/backup.sh --project techstrap --db-container <postgres container from docker ps> --no-encrypt --out ./backups`), `--dry-run` first; fenced `techstrap-backup.service` (oneshot, `EnvironmentFile=/etc/techstrap/backup.env`) and `techstrap-backup.timer` (`OnCalendar=*-*-* 02:30:00`, `Persistent=true`, `RandomizedDelaySec=10m`) examples, and the cron alternative (a `crontab -e` line `30 2 * * *`).
  - `## Encryption and off-box copy`: AES-256-CBC with PBKDF2 via openssl (no extra install); decrypt one file by hand (`openssl enc -d -aes-256-cbc -pbkdf2 -pass file:... -in db.dump.enc | pg_restore -l`); `age` as an optional alternative the scripts do not use; copy the timestamp directory off the box (`rsync`/`rclone`) after the script succeeds.
  - `## Retention`: `--keep-days N` removes timestamp directories older than N days under `<out>/<project>/` after a successful backup only; suggest 14 days local plus the operator's off-box policy.
  - `## Restoring`: (1) pick the backup directory, (2) restore into a scratch project (`deploy/restore.sh --from <dir> --target-project techstrap-restore --passphrase-file ...`), (3) read the verification output, (4) optionally start the apps against the scratch stack (a separate `TECHSTRAP_PROJECT`, `ConnectionStrings__TechStrap` pointing at `Host=techstrap-restore-postgres`), (5) `deploy/restore.sh --teardown --target-project techstrap-restore`; promotion: stop the apps (`docker compose ... stop api worker admin portal`, never `down -v`), restore into the real stack with `--db-url ... --yes --confirm-project <project> --overwrite`, start, run the checklist.
  - `## Verification checklist`: checkbox list: `__EFMigrationsHistory` count equals the manifest; ticket and attachment counts match; one attachment downloads in the Admin; an admin signs in; `/health/ready` is 200 on api and worker and `/health/live` on admin and portal; the outbox drains; erasures re-run (see the caveat); keys volumes present (agents stay signed in).
  - `## Consistency caveat`: the dump is taken first, then the volumes; a file written between the two is in the volume but not the dump (an orphan file, harmless), the reverse cannot happen. Ticket hard delete and requester erase delete files after commit, so a restore can resurrect rows and files erased after the backup time: list erasures since the backup from `admin_events` (a `select` over `admin_events` where the timestamp is after `created_utc` and the type is a hard delete or an erase; the real column and event-type names are confirmed from the model when writing) and re-run them. "Attachments are append-only" is false.
  - `## Disaster scenarios`: DB corruption (restore the dump into a new database, repoint the apps), volume loss (restore only the lost volume: `--skip-volume` for the others, read the warning), host loss (new host, restore all, re-create env files from the secret store, redeploy images by tag).
  - `## Keys volumes`: losing `admin-keys` signs every agent out (cookies cannot be decrypted); `portal-keys` invalidates in-flight portal antiforgery tokens and form posts only; both are restored by default and a restore never skips them silently.
  - `## Client and server version`: `pg_dump` and `pg_restore` run in a `postgres:17` container so the client matches the PG17 server (a newer client on the host, such as 18, writes a dump format an older server's tools may refuse); bump `POSTGRES_IMAGE` in both scripts when the server is upgraded.
  - `## 12b rehearsal record`: written in Step 7.
- [x] **Step 5: GREEN (static).** `bash -n deploy/backup.sh && bash -n deploy/restore.sh`; `shellcheck deploy/backup.sh deploy/restore.sh` when installed (record if not); `pwsh -File scripts/Invoke-ScriptTests.ps1`; `git diff --check`. Declare green only after Step 7 has written the real rehearsal section.
- [x] **Step 6: Dry-run proof.** `deploy/backup.sh --project techstrap --db-container <dev postgres container> --no-encrypt --out ./backups --keep-days 7 --dry-run` prints `DRY-RUN:` lines for the dump, the three volume archives and the prune, creates nothing and exits 0. `deploy/backup.sh` with no database mode exits non-zero with `choose one database mode`; with a missing passphrase file it exits non-zero.
- [x] **Step 7: Rehearsal (on-demand, one local run, not CI).** With the dev stack up and seeded (`docker compose up -d --wait`): (a) `deploy/backup.sh --project techstrap --db-container <postgres container> --passphrase-file <a throwaway 0600 file in the scratchpad> --out ./backups`; (b) `deploy/restore.sh --from ./backups/techstrap/<timestamp> --target-project techstrap-restore --passphrase-file <same file>`; (c) read the verification lines (migration count equal, tickets, attachments) and confirm an attachment file exists in `techstrap-restore_techstrap-storage`; (d) `deploy/restore.sh --teardown --target-project techstrap-restore`; (e) `docker volume ls --filter name=techstrap` shows the real dev volumes untouched. Fill `## 12b rehearsal record` with the date, the exact commands (no passphrase value), the printed counts, the elapsed time and any deviation. On a failure, fix the script and repeat; the record states the final successful run. Delete `./backups/` afterwards (gitignored).
- [x] **Step 8: Mutations** (each against the committed files, restored with `git checkout -- <file>`): remove `set -euo pipefail` from `backup.sh` (`both scripts fail fast with set -euo pipefail` dies); add `--volumes` to a comment in `restore.sh` (`neither script removes volumes wholesale` dies); delete the `manifest is missing volume` guard from `restore.sh` (`restore.sh restores all three volumes and refuses a manifest that lacks one` dies); swap the dump and the volume loop in `backup.sh` (`backup.sh dumps the database before it archives the volumes` dies); delete the `## Consistency caveat` section of the runbook (the section pin dies); remove the word `resurrect` (the phrase pin dies).
- [x] **Step 9: Commit** `feat(ops): encrypted backup and restore scripts with a runbook (P12-T13)`; run `git update-index --chmod=+x deploy/backup.sh deploy/restore.sh`; stage `deploy/backup.sh deploy/restore.sh docs/runbooks/backup-restore.md scripts/tests/BackupScripts.Tests.ps1 scripts/tests/RepositoryDocs.Tests.ps1`.

### Task 2: k6 load scripts and runner (P12-T11)

**Files:**
- Create: `tests/load/lib/auth.js`, `tests/load/lib/payloads.js`, `tests/load/lib/clients.js`, `tests/load/lib/checks.js`, `tests/load/lib/profile.js`, `tests/load/intake-trusted.js`, `tests/load/intake-public.js`, `tests/load/portal-form.js`, `tests/load/customer-view.js`, `tests/load/kb-search.js`, `tests/load/scenarios/sustained.js`, `tests/load/scenarios/spike.js`, `tests/load/README.md`, `scripts/Invoke-LoadTest.ps1`, `docs/load-test-results.md`, `scripts/tests/LoadScripts.Tests.ps1`
- Modify: `.gitignore` (add `tests/load/results/`)

**Interfaces:**
- Produces: environment variables read by the k6 scripts (all documented in the README): `TS_BASE_URL` (default `http://localhost:8080`), `TS_PORTAL_URL` (default `http://localhost:8082`), `TS_PRODUCT_KEY` (default `orbitly`), `TS_TRUSTED_KEY` (default the dev Orbitly server key), `TS_PUBLIC_KEY` (default the dev Orbitly app key), `TS_FORWARDED_PREFIX` (default `10.99.0.`), `TS_IP_COUNT` (default `60`), `TS_RATE` (default `20`, total req/s of the sustained mix), `TS_DURATION` (default `10m`), `TS_SPIKE_PEAK` (default `100`). The runner `scripts/Invoke-LoadTest.ps1 -Target local|uat -Scenario sustained|spike [-Rate <int>] [-Duration <string>] [-BaseUrl] [-PortalUrl] [-UseDocker] [-SkipDbCounts] [-DryRun]`. Module exports: `intake-trusted.js` exports `intakeTrusted`, `intake-public.js` exports `intakePublic`, `portal-form.js` exports `portalForm`, `customer-view.js` exports `customerView` and `captureViewToken`, `kb-search.js` exports `kbSearch`; every module also exports `options` with `thresholds` so it runs standalone (`k6 run tests/load/intake-trusted.js`).

- [x] **Step 1: Pins first (RED).** Create `scripts/tests/LoadScripts.Tests.ps1`:
  ```powershell
  BeforeAll {
      $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
      $script:Load = Join-Path $script:RepoRoot 'tests/load'
      $script:Runner = Join-Path $script:RepoRoot 'scripts/Invoke-LoadTest.ps1'
      $script:Modules = 'intake-trusted', 'intake-public', 'portal-form', 'customer-view', 'kb-search'
      $script:Composers = 'sustained', 'spike'
      $script:Libs = 'auth', 'payloads', 'clients', 'checks', 'profile'
      function Read-LoadFile { param([string]$Relative) [System.IO.File]::ReadAllText((Join-Path $script:Load $Relative)) }
      function Get-AllLoadJs { Get-ChildItem -LiteralPath $script:Load -Recurse -Filter *.js | Where-Object { $_.FullName -notmatch '[\\/]results[\\/]' } }
  }

  Describe 'k6 load suite (static pins, PHASE-12b)' {
      It 'has every library, scenario module, composer, README and runner' {
          foreach ($name in $script:Libs) { Test-Path (Join-Path $script:Load "lib/$name.js") | Should -BeTrue -Because "lib/$name.js" }
          foreach ($name in $script:Modules) { Test-Path (Join-Path $script:Load "$name.js") | Should -BeTrue -Because "$name.js" }
          foreach ($name in $script:Composers) { Test-Path (Join-Path $script:Load "scenarios/$name.js") | Should -BeTrue -Because "scenarios/$name.js" }
          Test-Path (Join-Path $script:Load 'README.md') | Should -BeTrue
          Test-Path $script:Runner | Should -BeTrue
          Test-Path (Join-Path $script:RepoRoot 'docs/load-test-results.md') | Should -BeTrue
      }

      It 'every scenario module and composer declares thresholds' {
          foreach ($name in $script:Modules) { (Read-LoadFile "$name.js") | Should -Match 'thresholds\s*:' -Because "$name.js must declare thresholds" }
          foreach ($name in $script:Composers) { (Read-LoadFile "scenarios/$name.js") | Should -Match 'thresholds\s*:' -Because "scenarios/$name.js must declare thresholds" }
      }

      It 'encodes the 500 ms p95 budget and a zero server-error rate in code' {
          foreach ($name in $script:Modules) { (Read-LoadFile "$name.js") | Should -Match ([regex]::Escape('p(95)<500')) -Because "$name.js" }
          (Read-LoadFile 'scenarios/sustained.js') | Should -Match ([regex]::Escape('p(95)<500'))
          (Read-LoadFile 'scenarios/sustained.js') | Should -Match "server_errors'?\s*:\s*\[\s*'rate==0'"
          (Read-LoadFile 'scenarios/spike.js') | Should -Match "server_errors'?\s*:\s*\[\s*'rate==0'"
      }

      It 'the spike asserts 429s during the spike and a 500 ms p95 after it' {
          $spike = Read-LoadFile 'scenarios/spike.js'
          $spike | Should -Match ([regex]::Escape('throttled_429{phase:spike}'))
          $spike | Should -Match ([regex]::Escape('http_req_duration{phase:recovery}'))
          $spike | Should -Match 'count>0'
      }

      It 'the sustained mix is 50/10/30/10 plus the portal form at one request a minute' {
          $sustained = Read-LoadFile 'scenarios/sustained.js'
          foreach ($pattern in 'intakeTrusted.*0\.5', 'intakePublic.*0\.1', 'kbSearch.*0\.3', 'customerView.*0\.1', 'portalForm') { $sustained | Should -Match $pattern }
          $sustained | Should -Match "timeUnit:\s*'1m'"
      }

      It 'sends the Portal form with the antiforgery token, the handler, the submit id and a blank honeypot' {
          $form = Read-LoadFile 'portal-form.js'
          foreach ($needle in '__RequestVerificationToken', '_handler', 'contact', 'Form.SubmitId', 'Form.Website') { $form | Should -Match ([regex]::Escape($needle)) }
      }

      It 'rotates X-Forwarded-For and reads the viewUrl token from the intake response' {
          (Read-LoadFile 'lib/clients.js') | Should -Match 'X-Forwarded-For'
          (Read-LoadFile 'customer-view.js') | Should -Match 'viewUrl'
          (Read-LoadFile 'customer-view.js') | Should -Match 'X-Ticket-Token'
      }

      It 'the README documents every environment variable the scripts read' {
          $readme = Read-LoadFile 'README.md'
          $names = foreach ($file in (Get-AllLoadJs)) {
              [regex]::Matches([System.IO.File]::ReadAllText($file.FullName), '__ENV(?:\.|\[\x27)(?<n>[A-Z][A-Z0-9_]*)') | ForEach-Object { $_.Groups['n'].Value }
          }
          $names = @($names | Sort-Object -Unique)
          $names.Count | Should -BeGreaterOrEqual 9
          foreach ($name in $names) { $readme | Should -Match ([regex]::Escape($name)) -Because "README.md must document $name" }
      }

      It 'the README covers proxy trust, outbox and dead-letter counts, results and the Verified note' {
          $readme = Read-LoadFile 'README.md'
          foreach ($needle in 'REVERSE_PROXY_CIDR', 'X-Forwarded-For', 'email_outbox', 'dead', 'psql', 'tests/load/results', 'Verified', 'Invoke-LoadTest.ps1') { $readme | Should -Match ([regex]::Escape($needle)) -Because $needle }
      }

      It 'contains no key value except the NotASecret dev keys' {
          $files = @(Get-AllLoadJs) + (Get-Item (Join-Path $script:Load 'README.md')) + (Get-Item $script:Runner) + (Get-Item (Join-Path $script:RepoRoot 'docs/load-test-results.md'))
          foreach ($file in $files) {
              foreach ($match in [regex]::Matches([System.IO.File]::ReadAllText($file.FullName), '\b(?:tsk|tsp)_[A-Za-z0-9]{16,}')) {
                  $match.Value | Should -Match 'NotASecret' -Because "$($file.Name) holds a key-like value"
              }
          }
      }

      It 'all load files are LF and ASCII' {
          $files = @(Get-AllLoadJs) + (Get-Item (Join-Path $script:Load 'README.md')) + (Get-Item $script:Runner)
          foreach ($file in $files) {
              $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
              ($bytes -contains 13) | Should -BeFalse -Because "$($file.Name) must be LF"
              @($bytes | Where-Object { $_ -gt 127 }) | Should -BeNullOrEmpty -Because "$($file.Name) must be ASCII"
          }
      }

      It 'keeps the results folder out of git' {
          [System.IO.File]::ReadAllText((Join-Path $script:RepoRoot '.gitignore')) | Should -Match '(?m)^tests/load/results/\s*$'
      }
  }

  Describe 'scripts/Invoke-LoadTest.ps1 (static and dry-run)' {
      It 'never writes a key variable to any output stream' {
          foreach ($line in [System.IO.File]::ReadAllLines($script:Runner)) {
              if ($line -match 'TS_TRUSTED_KEY|TS_PUBLIC_KEY') { $line | Should -Not -Match 'Write-(Host|Output|Verbose|Warning|Information)|echo|\|\s*Out-' -Because $line }
          }
      }

      It 'passes keys to docker by name, never by value' {
          $text = [System.IO.File]::ReadAllText($script:Runner)
          $text | Should -Match "'-e', 'TS_TRUSTED_KEY'"
          $text | Should -Not -Match "TS_TRUSTED_KEY='"
      }

      It 'under -DryRun prints the plan, calls nothing and never prints a key' {
          $env:TS_TRUSTED_KEY = 'tsk_canaryLoadKeyValue00000000'
          $env:TS_PUBLIC_KEY = 'tsp_canaryLoadKeyValue00000000'
          try {
              $output = & $script:Runner -Target local -Scenario sustained -Rate 2 -Duration 30s -DryRun | Out-String
              $output | Should -Match 'sustained'
              $output | Should -Match 'DRY-RUN'
              $output | Should -Match ([regex]::Escape('tests/load/results/'))
              $output | Should -Not -Match 'canaryLoadKeyValue'
          } finally {
              Remove-Item Env:TS_TRUSTED_KEY, Env:TS_PUBLIC_KEY -ErrorAction SilentlyContinue
          }
      }

      It 'requires explicit keys for the uat target' {
          Remove-Item Env:TS_TRUSTED_KEY, Env:TS_PUBLIC_KEY -ErrorAction SilentlyContinue
          { & $script:Runner -Target uat -Scenario spike -BaseUrl 'https://api.uat.example' -PortalUrl 'https://uat.example' -DryRun } | Should -Throw '*TS_TRUSTED_KEY*'
      }
  }
  ```
  Run `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/LoadScripts.Tests.ps1`: RED. Expected: the first test fails because `tests/load` does not exist; `Read-LoadFile` calls throw for the rest; the `.gitignore` pin fails; the runner tests fail on a missing script.
- [x] **Step 2: Libraries.**
  - `lib/auth.js`: `export const TRUSTED_KEY = __ENV.TS_TRUSTED_KEY || 'tsk_devOrbitlyServerKeyNotASecret00000000000000'; export const PUBLIC_KEY = __ENV.TS_PUBLIC_KEY || 'tsp_devOrbitlyAppKeyNotASecret00000000000000000';` and `export function keyHeaders(key, extra)` returning `{ 'X-Api-Key': key, ...extra }`. No `console.log` of either value anywhere in the suite.
  - `lib/clients.js`: `PREFIX = __ENV.TS_FORWARDED_PREFIX || '10.99.0.'`, `IP_COUNT = Number(__ENV.TS_IP_COUNT || 60)` (bounded 1..240), `export function clientIp(n)` returning `PREFIX + (1 + (n % IP_COUNT))`, `export const SPIKE_IP = PREFIX + '250'`, `export function forwarded(ip)` returning `{ 'X-Forwarded-For': ip }`, and `export function nextIp()` using `__VU * 100003 + __ITER` so concurrent VUs spread over the /24.
  - `lib/payloads.js`: arrays of 12 first names, 12 surnames, 15 subjects and 10 body paragraphs (product-support language); `export function ticket(n)` returns `{ email: 'load+' + n + '@example.com', name, subject, body, externalUserRef: 'load-user-' + (n % 500), metadata: { source: 'k6' } }` with subject <= 200, name <= 100, email <= 320 and body 200..2000 characters (far under the 100000 limit), chosen by index (deterministic, no `Math.random`); `export function attachment(n)` returns a 1 KiB `text/plain` `http.file` named `note-<n>.txt`; `export function idempotencyKey()` returns `'ts-load-' + __VU + '-' + __ITER + '-' + Date.now()` (<= 200 characters).
  - `lib/checks.js`: `import { Rate, Counter } from 'k6/metrics'`; `export const serverErrors = new Rate('server_errors'); export const throttled = new Counter('throttled_429');` and `export function record(res, expected, phase)` that adds `res.status >= 500` to `serverErrors`, adds 1 to `throttled` with tag `{ phase }` when the status is 429, and runs `check(res, { ['status is ' + expected]: (r) => r.status === expected || r.status === 429 })` (a 429 is a valid answer under the limits). `export const baseThresholds = { http_req_duration: ['p(95)<500'], server_errors: ['rate==0'] }`.
  - `lib/profile.js`: reads `TS_RATE` (default 20), `TS_DURATION` (default `10m`); `export function share(weight, exec)` returns a `constant-arrival-rate` executor: per-second rate = `TS_RATE * weight`; when at least 1 use `rate: Math.round(r), timeUnit: '1s'`, otherwise `rate: 1, timeUnit: Math.max(1, Math.round(1 / r)) + 's'`; `preAllocatedVUs: 20`, `maxVUs: 200`, `duration: DURATION`, the given `exec`. `export const DURATION`.
- [x] **Step 3: Scenario modules.** Each imports `http`, the libs, defines `export const options = { thresholds: { ...baseThresholds } }` (plus its own named threshold) so it runs standalone, and exports its function; its `export default` runs the function once so `k6 run tests/load/<name>.js` is a one-iteration smoke:
  - `intake-trusted.js` `intakeTrusted()`: `POST ${BASE}/api/intake/tickets`, JSON `ticket(n)` fields, headers `Content-Type: application/json`, `X-Api-Key: TRUSTED_KEY`, `Idempotency-Key`, `X-Forwarded-For: clientIp(...)`, tag `{ name: 'intake-trusted' }`; expects 201 and parses `ticketNumber`.
  - `intake-public.js` `intakePublic()`: `POST ${BASE}/api/public/products/${PRODUCT}/tickets` multipart (`Email`, `Name`, `Subject`, `Body`, `Website: ''`, and one `Attachments` file on every fourth iteration), header `X-Api-Key: PUBLIC_KEY`, rotated forwarded IP; expects 201.
  - `portal-form.js` `portalForm()`: `GET ${PORTAL}/p/${PRODUCT}/contact` with the forwarded header, parse the antiforgery token and `Form.SubmitId` with `inputValue(html, name)` (finds the `<input ...>` tag whose `name="..."` equals the argument and returns its `value`; fails the iteration with a clear check message when absent), then `POST ${PORTAL}/p/${PRODUCT}/contact` multipart with `_handler: 'contact'`, `__RequestVerificationToken`, `Form.SubmitId`, `Form.Name`, `Form.Email`, `Form.Subject`, `Form.Body`, `Form.Website: ''` (honeypot blank); the per-VU cookie jar carries the antiforgery cookie; the PRG redirect is followed and the check is that the final URL ends with `/contact/received`.
  - `customer-view.js`: `captureViewToken()` posts one intake call (same as trusted, idempotency key `ts-load-setup-<timestamp>`), takes `viewUrl`, extracts the token with `/\/t\/([^/?#]+)/` and returns it (never logged); `customerView(token)` does `GET ${BASE}/api/customer/ticket` with `X-Ticket-Token` and expects 200; standalone `export function setup()` calls `captureViewToken()` and `export default` calls `customerView(data.token)`.
  - `kb-search.js` `kbSearch()`: `GET ${BASE}/api/public/kb/${PRODUCT}/search?q=<term>&page=1&pageSize=10` with terms from the seeded articles (`welcome`, `password`, `dark mode`, `reset`) and a rotated forwarded IP; expects 200. Responses are cached 60 s, which the README states as a caveat.
- [x] **Step 4: Composers.**
  - `scenarios/sustained.js`: re-exports the scenario functions (`export { intakeTrusted } from '../intake-trusted.js'` and so on) and defines `setup()` returning `{ token: captureViewToken() }` and `export function customerViewScenario(data) { customerView(data.token); }` as the exec target. `options.scenarios`: `intakeTrusted: share(0.5, 'intakeTrusted')`, `intakePublic: share(0.1, 'intakePublic')`, `kbSearch: share(0.3, 'kbSearch')`, `customerView: share(0.1, 'customerViewScenario')`, plus `portalForm` as `constant-arrival-rate` with `rate: 1, timeUnit: '1m'` (the 5 per 10 min per IP limit; with 60 rotated IPs each IP submits once an hour). `options.thresholds`: `http_req_duration: ['p(95)<500']`, `server_errors: ['rate==0']`, `http_req_failed: ['rate<0.01']`, `checks: ['rate>0.99']`, plus per-scenario `http_req_duration{name:...}` thresholds with `p(95)<500`.
  - `scenarios/spike.js`: `scenarios.spike`: `ramping-arrival-rate`, `startRate: 5`, stages `[{ target: SPIKE_PEAK, duration: '10s' }, { target: SPIKE_PEAK, duration: '60s' }, { target: 0, duration: '5s' }]`, `preAllocatedVUs: 200`, `maxVUs: 500`, exec `spikeCall` which sends the trusted intake request from `SPIKE_IP` with tag `{ phase: 'spike' }`; `scenarios.recovery`: `constant-arrival-rate` 5 req/s from rotated IPs, `startTime: '75s'`, `duration: '30s'`, exec `recoveryCall` with tag `{ phase: 'recovery' }`. `thresholds`: `server_errors: ['rate==0']`, `'throttled_429{phase:spike}': ['count>0']`, `'http_req_duration{phase:recovery}': ['p(95)<500']`. A comment explains why the spike phase may see failed requests (429 is the correct answer) and why recovery uses other IPs.
- [x] **Step 5: Runner `scripts/Invoke-LoadTest.ps1`** (LF, ASCII, comment-based help with `.SYNOPSIS`, `.DESCRIPTION`, `.PARAMETER`, `.EXAMPLE`; `[CmdletBinding()]`; `Set-StrictMode -Version Latest`; `$ErrorActionPreference = 'Stop'`).
  - Parameters: `[Parameter(Mandatory)][ValidateSet('local','uat')][string]$Target`, `[Parameter(Mandatory)][ValidateSet('sustained','spike')][string]$Scenario`, `[int]$Rate = 20`, `[string]$Duration = '10m'`, `[string]$BaseUrl`, `[string]$PortalUrl`, `[switch]$UseDocker`, `[switch]$SkipDbCounts`, `[switch]$DryRun`.
  - Defaults: local gives `http://localhost:8080` and `http://localhost:8082`; uat requires both (`throw "-BaseUrl and -PortalUrl are required for -Target uat"`). For uat, `TS_TRUSTED_KEY` and `TS_PUBLIC_KEY` must be set in the environment (`throw "TS_TRUSTED_KEY and TS_PUBLIC_KEY must be set for -Target uat"`); local falls back to the k6 defaults (the NotASecret dev keys). Presence is checked with `Test-Path Env:`; values are never read into a printed string.
  - Environment for k6: sets `$env:TS_BASE_URL`, `$env:TS_PORTAL_URL`, `$env:TS_RATE`, `$env:TS_DURATION`; `TS_FORWARDED_PREFIX`, `TS_IP_COUNT`, `TS_SPIKE_PEAK`, `TS_PRODUCT_KEY` pass through when already set.
  - Command: local binary `k6 run --summary-export <results file> tests/load/scenarios/<scenario>.js`; Docker (`-UseDocker`, or automatically when `k6` is not on PATH): `docker run --rm -i --add-host=host.docker.internal:host-gateway -v "<repo>/tests/load:/load" -e TS_BASE_URL -e TS_PORTAL_URL -e TS_RATE -e TS_DURATION -e TS_TRUSTED_KEY -e TS_PUBLIC_KEY -e TS_FORWARDED_PREFIX -e TS_IP_COUNT -e TS_SPIKE_PEAK -e TS_PRODUCT_KEY <image> run --summary-export /load/results/<file> /load/scenarios/<scenario>.js` with `localhost` in the two URLs rewritten to `host.docker.internal`; the image lives in one variable `$K6Image` pinned to the newest `grafana/k6` tag that `docker manifest inspect` resolves when implementing (never `latest`). Keys are forwarded by name only (the argument array holds `'-e', 'TS_TRUSTED_KEY'`).
  - Results: `tests/load/results/<yyyyMMddTHHmmssZ>-<scenario>.json` (directory created if absent); the printed plan names the file, the target, the scenario, rate and duration, and the k6 command with the key variable NAMES only.
  - `-DryRun`: prints `DRY-RUN: <command line>` and the results path, then returns without creating the directory or starting anything.
  - After a local run (unless `-SkipDbCounts`): prints, via `docker compose exec -T postgres psql -U techstrap -d techstrap -At -c "<sql>"`, the outbox rows by status and the dead-letter count; the SQL is verified against `\d email_outbox` while implementing and copied verbatim into the README. For uat it prints "run the SQL from tests/load/README.md with your psql". The exit code is k6's.
- [x] **Step 6: `tests/load/README.md`** (ASCII, LF). Sections: What this is (the budgets from the spec; not run in CI, only the static Pester pins are); Prerequisites (k6 or Docker; the seeded dev stack or a UAT stack); Environment variables (a table of all ten `TS_*` names with default and meaning, plus the runner parameters); Scenarios (the sustained mix and why 60 IPs, the spike and why one IP, the 429-versus-5xx rule, recovery uses rotated IPs); Making the stack trust the runner (local: read `deploy/.env.api.example` and `deploy/.env.portal.example` and the dev `docker-compose.yml` for the exact trusted-proxy key, set it to the Docker gateway of the dev network as `/32` obtained with `docker network inspect techstrap_default --format "{{(index .IPAM.Config 0).Gateway}}"`, and `REVERSE_PROXY_CIDR` for the deploy compose; UAT: set `REVERSE_PROXY_CIDR` and the trusted-proxy network on the Api and Portal to the k6 runner's address as `/32` in the `TECHSTRAP_ENV_DIR` files, redeploy, and remove it after the run; warn that a trusted proxy can forge client IPs, so this is a test-only setting); Running (`pwsh scripts/Invoke-LoadTest.ps1 -Target local -Scenario sustained ...`, a UAT example, `-UseDocker`); Reading the results (the JSON in `tests/load/results/`, which thresholds failed, how to read p95); After the run: outbox drain and dead letters (the SQL, the local `docker compose exec -T postgres psql -U techstrap -d techstrap -c ...` command, the UAT `psql "$TECHSTRAP_DB_URL" -c ...` command, and the budget: outbox drained within 2 min of the spike end, no dead letters caused by load); Cleaning up (load tickets use `load+<n>@example.com`; erase the requester or hard-delete the tickets); a `Verified` note written in Step 8.
- [x] **Step 7: `docs/load-test-results.md` skeleton** (ASCII, LF): `# Load test results`; a paragraph on purpose and budgets; `## Box specs` as a table (host, CPU, RAM, disk, Docker version, Postgres version, image tags, k6 version) whose value cells read `recorded in 12c (T12)`; `## Runs` with a table header (`Date | Scenario | Rate | Duration | p95 | Server errors | 429s | Outbox drained | Dead letters | Result`) and one row reading `no run recorded yet (first run is 12c T12)`; `## Tuning` listing what to record (pool size, rate-limit overrides, proxy settings). The text must not contain the word `TODO`.
- [x] **Step 8: Smoke (on-demand).** With the dev stack up and seeded and the runner trusted as a proxy per the README, run `pwsh -File scripts/Invoke-LoadTest.ps1 -Target local -Scenario sustained -Duration 30s -Rate 2`; fix script problems until the run completes with thresholds green (a portal-form 429 at this size is not expected). Record in the README `Verified` note: the date, the exact command, the result lines (iterations, p95, `server_errors`), how the stack was made to trust the runner, and the k6 version. If the stack cannot be made to trust the runner locally, record the exact failure and what was proved instead (for example the single-module `k6 run tests/load/kb-search.js`) and flag it in the final report.
- [x] **Step 9: GREEN.** `pwsh -File scripts/Invoke-ScriptTests.ps1`; `git diff --check`; `k6 inspect tests/load/scenarios/sustained.js` and `spike.js` parse without error (on-demand check, recorded).
- [x] **Step 10: Mutations:** delete the `thresholds` block from `intake-public.js` (`every scenario module and composer declares thresholds` dies); change `p(95)<500` to `p(95)<5000` in `kb-search.js` (`encodes the 500 ms p95 budget` dies); add `__ENV.TS_SECRET_SAUCE` to `lib/clients.js` (`the README documents every environment variable the scripts read` dies); paste a 36-character `tsk_` value without `NotASecret` into the runner (`contains no key value except the NotASecret dev keys` dies); add `Write-Host $env:TS_TRUSTED_KEY` to the runner (`never writes a key variable to any output stream` dies). Restore each.
- [x] **Step 11: Commit** `feat(load): k6 scenarios and runner for the PHASE-12 budgets (P12-T11)`; stage the files listed under Files by explicit path (never `tests/load/results/`).

### Task 3: Self-host guide and environment reference (P12-T16)

**Files:**
- Create: `docs/self-hosting/SELF-HOSTING.md`, `scripts/tests/SelfHostDocs.Tests.ps1`
- Modify: `docs/self-hosting/DEPLOYMENT.md` (portal required key at l.86, links, product-host cross-link), `deploy/.env.uat.example` and `deploy/.env.production.example` (image tags to `0.2.0`), `README.md` (l.9 and a link), plus any existing pin that names the old image tags (found with `grep -rn "1.0.0-rc\|techstrap-api:1.0.0" scripts tests deploy docs`)

**Interfaces:**
- Produces: the section headings and the env tables that `SelfHostDocs.Tests.ps1` parses. The tables live under `## Environment reference` as `### .env.api`, `### .env.worker`, `### .env.admin`, `### .env.portal` and `### Compose inputs (deploy/.env.uat.example and deploy/.env.production.example)`. Each table row is ``| `KEY` | yes or no | default or example | meaning |`` (column 1 is the key in backticks, column 2 the required flag).

- [x] **Step 1: Pins first (RED).** Create `scripts/tests/SelfHostDocs.Tests.ps1`:
  ```powershell
  BeforeAll {
      $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
      function Get-RepoText { param([string]$RelativePath) Get-Content -LiteralPath (Join-Path $script:RepoRoot $RelativePath) -Raw }

      # Copies of the ConfigContract.Tests.ps1 helpers (same regexes; those live inside its BeforeAll and cannot be dot-sourced).
      function ConvertTo-IndexlessKey { param([string]$Key) return ($Key -replace '__\d+(?=__|$)', '__0') }
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
      function Get-EnvKeys { param([string]$Path) return @(Get-EnvEntries -Path $Path | ForEach-Object { ConvertTo-IndexlessKey $_.Key } | Sort-Object -Unique) }

      # Rows of the table under a "### <heading>" in SELF-HOSTING.md: Key (indexless, upper case) and Required (yes or no).
      function Get-TableRows {
          param([string]$Document, [string]$Heading)
          $section = [regex]::Match($Document, '(?ms)^### ' + [regex]::Escape($Heading) + '\s*$(.*?)(?=^### |^## |\z)').Groups[1].Value
          $rows = foreach ($line in ($section -split "`n")) {
              if ($line -match '^\|\s*`(?<key>[A-Za-z][A-Za-z0-9_]*)`\s*\|\s*(?<req>yes|no)\s*\|') {
                  [pscustomobject]@{ Key = (ConvertTo-IndexlessKey $Matches['key'].ToUpperInvariant()); Required = $Matches['req'] }
              }
          }
          return @($rows)
      }

      $script:Guide = Get-RepoText 'docs/self-hosting/SELF-HOSTING.md'
      $script:Apps = @(
          @{ Heading = '.env.api'; Example = 'deploy/.env.api.example' },
          @{ Heading = '.env.worker'; Example = 'deploy/.env.worker.example' },
          @{ Heading = '.env.admin'; Example = 'deploy/.env.admin.example' },
          @{ Heading = '.env.portal'; Example = 'deploy/.env.portal.example' }
      )
      # The operator-required set mirrors ProductionBlankTemplateTests.cs (tests/TechStrap.Api.Tests, the required-key lists near the top of the file). Keep in step with that C# test.
      $script:Required = @{
          '.env.api'    = @('CONNECTIONSTRINGS__TECHSTRAP', 'AUTHENTICATION__JWTBEARER__AUTHORITY', 'AUTHENTICATION__JWTBEARER__AUDIENCES__0', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_API_PUBLIC_URL')
          '.env.worker' = @('CONNECTIONSTRINGS__TECHSTRAP', 'EMAIL__SMTP__HOST', 'EMAIL__SMTP__DEFAULTFROM')
          '.env.admin'  = @('AUTH__AUTHORITY', 'AUTH__CLIENTID', 'AUTH__CLIENTSECRET')
          '.env.portal' = @('TECHSTRAP_PORTAL_PUBLIC_URL')
      }
      $script:ComposeHeading = 'Compose inputs (deploy/.env.uat.example and deploy/.env.production.example)'
  }

  Describe 'SELF-HOSTING.md environment reference (PHASE-12b)' {
      It 'lists every key of every deploy template' {
          foreach ($app in $script:Apps) {
              $documented = (Get-TableRows -Document $script:Guide -Heading $app.Heading).Key
              foreach ($key in (Get-EnvKeys -Path (Join-Path $script:RepoRoot $app.Example))) {
                  $documented | Should -Contain $key -Because "$($app.Example) has $key but the $($app.Heading) table does not"
              }
          }
      }

      It 'lists no key that is not in a template' {
          foreach ($app in $script:Apps) {
              $templateKeys = Get-EnvKeys -Path (Join-Path $script:RepoRoot $app.Example)
              foreach ($key in (Get-TableRows -Document $script:Guide -Heading $app.Heading).Key) {
                  $templateKeys | Should -Contain $key -Because "the $($app.Heading) table lists $key but $($app.Example) does not"
              }
          }
      }

      It 'lists each key once per table' {
          foreach ($app in $script:Apps) {
              $keys = (Get-TableRows -Document $script:Guide -Heading $app.Heading).Key
              @($keys | Group-Object | Where-Object Count -gt 1) | Should -BeNullOrEmpty -Because $app.Heading
          }
      }

      It 'marks exactly the operator-required keys as required' {
          foreach ($app in $script:Apps) {
              $rows = Get-TableRows -Document $script:Guide -Heading $app.Heading
              $required = @($rows | Where-Object Required -eq 'yes' | ForEach-Object Key | Sort-Object)
              $required | Should -Be @($script:Required[$app.Heading] | Sort-Object) -Because "required set of $($app.Heading)"
          }
      }

      It 'documents the compose inputs of both environment templates' {
          $uat = Get-EnvKeys -Path (Join-Path $script:RepoRoot 'deploy/.env.uat.example')
          $production = Get-EnvKeys -Path (Join-Path $script:RepoRoot 'deploy/.env.production.example')
          $uat | Should -Be $production -Because 'the two compose-input templates carry the same keys'
          $documented = (Get-TableRows -Document $script:Guide -Heading $script:ComposeHeading).Key
          foreach ($key in $uat) { $documented | Should -Contain $key }
          foreach ($key in $documented) { $uat | Should -Contain $key }
      }
  }

  Describe 'SELF-HOSTING.md content (PHASE-12b)' {
      It 'has the section headings' {
          foreach ($heading in 'Overview and requirements', 'Compose layout', 'OIDC requirements', 'Environment reference', 'Reverse proxy', 'TLS', 'SMTP', 'Volumes', 'First administrator', 'Upgrade and rollback', 'Health checks', 'PgBouncer and LISTEN', 'Backups', 'Other identity providers') {
              $script:Guide | Should -Match ('(?m)^## ' + [regex]::Escape($heading) + '\s*$') -Because $heading
          }
          foreach ($heading in 'Default-site Caddy block', 'Request body size', 'Forwarded headers and the pinned subnet', 'Knowledge-base images') {
              $script:Guide | Should -Match ('(?m)^### ' + [regex]::Escape($heading) + '\s*$') -Because $heading
          }
      }

      It 'states the OIDC contract' {
          foreach ($phrase in 'audience', 'sub', 'email', 'groups', 'offline_access', '/signin-oidc', '/signout-callback-oidc', 'PKCE', 'confidential', 'TECHSTRAP_AGENT_GROUP', 'TECHSTRAP_ADMIN_GROUP') {
              $script:Guide | Should -Match ([regex]::Escape($phrase)) -Because $phrase
          }
      }

      It 'gives a default-site Caddy block with the body limit, the kb-images route and the forwarded headers' {
          foreach ($phrase in 'request_body', 'max_size 26MiB', 'reverse_proxy 127.0.0.1:8080', 'reverse_proxy 127.0.0.1:8081', 'reverse_proxy 127.0.0.1:8082', '/kb-images/', 'REVERSE_PROXY_CIDR', 'TECHSTRAP_SUBNET', 'X-Forwarded-For') {
              $script:Guide | Should -Match ([regex]::Escape($phrase)) -Because $phrase
          }
      }

      It 'links the runbook, the Authentik example and the deployment guide, and every link resolves' {
          foreach ($link in '../runbooks/backup-restore.md', 'AUTHENTIK.md', 'DEPLOYMENT.md') { $script:Guide | Should -Match ([regex]::Escape("]($link")) -Because $link }
          foreach ($m in [regex]::Matches($script:Guide, '\]\((?<path>(?!https?:|#)[^)\s#]+)')) {
              Test-Path -LiteralPath (Join-Path $script:RepoRoot 'docs' 'self-hosting' $m.Groups['path'].Value) | Should -BeTrue -Because $m.Groups['path'].Value
          }
      }

      It 'does not tell the reader to install 1.0.0 or an rc and is ASCII' {
          $script:Guide | Should -Not -Match '1\.0\.0'
          $script:Guide | Should -Not -Match '(?i)-rc\.?\d'
          $script:Guide | Should -Not -Match 'down\s+-v(\s|$)'
          ([regex]::IsMatch($script:Guide, '[^\x00-\x7F]')) | Should -BeFalse
      }
  }

  Describe 'deploy templates and README after 12b' {
      It 'the two compose-input templates pin 0.2.0 images, no rc and no 1.0.0' {
          foreach ($file in 'deploy/.env.uat.example', 'deploy/.env.production.example') {
              $text = Get-RepoText $file
              $text | Should -Not -Match '-rc\.?\d' -Because $file
              $text | Should -Not -Match '1\.0\.0' -Because $file
              foreach ($app in 'api', 'worker', 'admin', 'portal') { $text | Should -Match ("(?m)^TECHSTRAP_$($app.ToUpper())_IMAGE=ghcr\.io/syntax-circus/techstrap-${app}:0\.2\.0\s*$") -Because "$file $app" }
          }
      }

      It 'DEPLOYMENT.md names the portal required key and links the new guides' {
          $text = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md'
          $text | Should -Not -Match '\| none yet \|'
          $text | Should -Match '`\.env\.portal` \| `TECHSTRAP_PORTAL_PUBLIC_URL`'
          $text | Should -Match ([regex]::Escape('](SELF-HOSTING.md)'))
          $text | Should -Match ([regex]::Escape('](../runbooks/backup-restore.md)'))
      }

      It 'README says v0.3.0 next and links the self-hosting guide' {
          $text = Get-RepoText 'README.md'
          $text | Should -Not -Match 'then `?v1\.0\.0`?'
          $text | Should -Match ([regex]::Escape('then v0.3.0 (1.0.0 is a later API-lock decision)'))
          $text | Should -Match ([regex]::Escape('(docs/self-hosting/SELF-HOSTING.md)'))
      }
  }
  ```
  Run `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/SelfHostDocs.Tests.ps1`: RED. Expected: `BeforeAll` throws because `SELF-HOSTING.md` does not exist (every test fails with the file-not-found error).
- [x] **Step 2: Write `docs/self-hosting/SELF-HOSTING.md`** (ASCII, LF, links relative to `docs/self-hosting/`). Headings and the facts each states:
  - `## Overview and requirements`: what runs (Api, Worker, Admin, Portal), what the operator provides (Docker with Compose 2.33.1 or later per DEPLOYMENT.md, an external PostgreSQL 17 with role, database and owner `techstrap`, an OIDC provider, an SMTP relay, a reverse proxy with TLS), versions (images tagged by release, currently 0.x), and a pointer to DEPLOYMENT.md for the step-by-step first deploy.
  - `## Compose layout`: `deploy/docker-compose.yml`; services `api`, `admin`, `portal`, `worker` from `${TECHSTRAP_<APP>_IMAGE}`; `env_file ${TECHSTRAP_ENV_DIR}/.env.<app>`; ports `127.0.0.1:${TECHSTRAP_{API,ADMIN,PORTAL}_PORT}:80` (UAT 18080-18082, production 8080-8082); the pinned `${TECHSTRAP_SUBNET}` default network plus the external `${TECHSTRAP_DB_NETWORK}` (`techstrap-db`); no Postgres service; volumes `techstrap-storage`, `admin-keys`, `portal-keys`.
  - `## OIDC requirements`: the provider-neutral contract: authority (https outside Development); the Admin is a confidential client using authorization code with PKCE; scopes `openid profile email offline_access`; redirect URI `https://<admin-host>/signin-oidc`; post-logout `https://<admin-host>/signout-callback-oidc`; the Api validates the access token with `Authentication:JwtBearer` Authority and Audiences (audience = the Admin client id); the access token must carry `sub`, `email` and the groups claim (`groups` by default, `TECHSTRAP_GROUP_CLAIM_TYPE` renames it); group names go in `TECHSTRAP_AGENT_GROUP` and `TECHSTRAP_ADMIN_GROUP` (they must differ; identical values in `.env.api` and `.env.admin`); an agent without an email is refused (`agent-email-required`); the Admin cookie `techstrap.admin` slides for 8 h. Links to [AUTHENTIK.md](AUTHENTIK.md).
  - `## Environment reference`: an intro (the source of truth is the `deploy/.env.<app>.example` templates and the operator-required set pinned by `ProductionBlankTemplateTests`; a Pester test fails when this table and a template disagree; commented template keys are optional and listed with `no`), then the five `###` tables with columns `Key | Required | Default or example | Meaning`. One row for every key in the template. The implementer builds each table by running `Get-EnvEntries` over the template (so no key is forgotten) and copying the default or example value; secrets show `<set by operator>`, never a real value. Required is `yes` only for the pinned set (listed in the test).
  - `## Reverse proxy`, with `### Default-site Caddy block` (a fenced Caddyfile with three site blocks using the placeholder hosts `api.example.com`, `admin.example.com`, `app.example.com`; each `encode zstd gzip` and `reverse_proxy 127.0.0.1:8080`, `:8081`, `:8082`; the Api and Portal blocks carry `request_body { max_size 26MiB }`), `### Request body size` (25 MiB total attachments plus form overhead; the Portal buffers up to 27,262,976 bytes before its antiforgery check; why `26MiB` and not `26MB`; a per-IP rate limit on the portal form routes is the proxy's job), `### Knowledge-base images` (`/kb-images/{name}` is served by the Api itself from `techstrap-storage`; `TECHSTRAP_API_PUBLIC_URL` is how the Portal builds image URLs; the proxy must route that path on the Api host to the Api; no static-file mapping is needed), `### Forwarded headers and the pinned subnet` (the apps trust `X-Forwarded-For` and `X-Forwarded-Proto` only from `TECHSTRAP_SUBNET` and `REVERSE_PROXY_CIDR`; rate limits and logs use the forwarded client IP; a wrong CIDR makes every client share the proxy's address and one rate limit; the check in DEPLOYMENT.md "First deploy: verify the client IP path"), and a link to the DEPLOYMENT.md "Product hosts" section.
  - `## TLS`: TLS terminates at the proxy outside compose; HSTS is set by the apps; certificates are the operator's concern.
  - `## SMTP`: the Worker sends mail (`EMAIL__SMTP__HOST` and `EMAIL__SMTP__DEFAULTFROM` required; `EMAILOUTBOX__ENABLED=false` runs without email); `TLSMODE` and retries; customer links use `TECHSTRAP_PORTAL_PUBLIC_URL`.
  - `## Volumes`: what each holds; `techstrap-storage` is the only place attachments and KB images live; the keys volumes must persist; never `down -v`.
  - `## First administrator`: D-029: the first admin is whoever signs in with the IdP admin group; agents are provisioned at first sign-in; deactivation in the Admin; the last-admin guard.
  - `## Upgrade and rollback`: set the new tags, `pull`, `up -d --wait`; migrations run in the Api under an advisory lock (`DATABASE__MIGRATEONSTARTUP`); rollback by previous tags; back up first; link to the DEPLOYMENT.md "Rollback and upgrade" section.
  - `## Health checks`: `/health/ready` (api, worker) and `/health/live` (admin, portal).
  - `## PgBouncer and LISTEN`: live updates use Postgres `LISTEN`; a transaction-pooling PgBouncer breaks it; connect the Api directly or use session pooling (link the DEPLOYMENT.md section).
  - `## Backups`: link [backup-restore.md](../runbooks/backup-restore.md), the RPO 24 h and RTO 4 h targets, the two scripts.
  - `## Other identity providers`: Keycloak and others are untested; the contract above is what to satisfy.
- [x] **Step 3: Edit the existing files** (Edit tool, line endings preserved): `deploy/.env.uat.example` and `deploy/.env.production.example`: the four image lines end `:0.2.0` and the comment line says "release tag (for example 0.2.0)". `docs/self-hosting/DEPLOYMENT.md` l.86: the portal row becomes ``| `.env.portal` | `TECHSTRAP_PORTAL_PUBLIC_URL` | The public address of the Portal (every optional key is in [SELF-HOSTING.md](SELF-HOSTING.md)). |``; add near the top "For the provider-neutral guide and the full env reference see [SELF-HOSTING.md](SELF-HOSTING.md); for backups see [backup-restore.md](../runbooks/backup-restore.md); for Authentik see [AUTHENTIK.md](AUTHENTIK.md)." and, in "Rollback and upgrade", a sentence pointing to the runbook (keep the pinned phrases `restore from backup` and `sudo docker volume ls --filter name=`; keep the `down -v` pattern out). `README.md` l.9: replace "then `v1.0.0`" with "then v0.3.0 (1.0.0 is a later API-lock decision)" and add `[self-hosting guide](docs/self-hosting/SELF-HOSTING.md)` next to the DEPLOYMENT link. Then run the grep named under Files and update any pin or doc that asserted the old tags (check `scripts/Test-ComposeSmoke.ps1`).
- [x] **Step 4: GREEN.** `pwsh -File scripts/Invoke-ScriptTests.ps1`; also `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1` (the templates were edited; the contract must stay green); `git diff --check`.
- [x] **Step 5: Mutations:** delete the `ALLOWEDHOSTS` row from the `.env.api` table (`lists every key of every deploy template` dies); add a row `BOGUS__KEY` to `.env.worker` (`lists no key that is not in a template` dies); flip `TECHSTRAP_API_PUBLIC_URL` to `no` in `.env.api` (`marks exactly the operator-required keys as required` dies); add `TECHSTRAP_NEW_KEY=1` to `deploy/.env.portal.example` (`lists every key of every deploy template` dies, and `ConfigContract` dies too, the intended second net); put `-rc.1` back in `.env.uat.example` (the image pin dies); remove `request_body` from the Caddy block (the proxy pin dies). Restore each.
- [x] **Step 6: Commit** `docs(self-hosting): generic OIDC self-host guide with an env reference checked against the deploy templates (P12-T16)`; stage `docs/self-hosting/SELF-HOSTING.md docs/self-hosting/DEPLOYMENT.md deploy/.env.uat.example deploy/.env.production.example README.md scripts/tests/SelfHostDocs.Tests.ps1` plus any pin file edited in Step 3.

### Task 4: Authentik worked example and consistent OIDC guidance (P12-T17)

**Files:**
- Create: `docs/self-hosting/AUTHENTIK.md`
- Modify: `docs/self-hosting/AGENT-AUTHENTICATION.md` (pointer plus claim requirements), `docs/development/ADMIN-APP.md` (l.104-125 region), `scripts/tests/SelfHostDocs.Tests.ps1` (new `Describe 'Authentik guide (PHASE-12b)'`)

**Interfaces:**
- Consumes: Task 3's `SELF-HOSTING.md` (linked from the new guide). Produces: the values the guide states (issuer slug `techstrap`, groups `techstrap-agents` and `techstrap-admins`).

- [x] **Step 1: Pins first (RED).** Append to `SelfHostDocs.Tests.ps1`:
  ```powershell
  Describe 'Authentik guide (PHASE-12b)' {
      BeforeAll {
          $script:Authentik = Get-RepoText 'docs/self-hosting/AUTHENTIK.md'
          $script:AgentAuth = Get-RepoText 'docs/self-hosting/AGENT-AUTHENTICATION.md'
          $script:AdminApp = Get-RepoText 'docs/development/ADMIN-APP.md'
      }

      It 'has the sections' {
          foreach ($heading in 'Overview', 'Prerequisites', 'Create the groups', 'Create the provider', 'Create the application', 'The groups claim', 'Where each value goes', 'Verification', 'Troubleshooting', 'Reference') {
              $script:Authentik | Should -Match ('(?m)^## ' + [regex]::Escape($heading) + '\s*$') -Because $heading
          }
      }

      It 'names the groups, the redirect paths, the scope and PKCE' {
          foreach ($phrase in 'techstrap-agents', 'techstrap-admins', '/signin-oidc', '/signout-callback-oidc', 'offline_access', 'PKCE', 'Confidential', '/application/o/techstrap/', 'TECHSTRAP_AGENT_GROUP', 'TECHSTRAP_ADMIN_GROUP', 'AUTH__CLIENTID', 'AUTHENTICATION__JWTBEARER__AUDIENCES__0', 'syntax-circus-authentik') {
              $script:Authentik | Should -Match ([regex]::Escape($phrase)) -Because $phrase
          }
      }

      It 'says the default profile scope mapping carries groups and gives the explicit-mapping fallback' {
          $script:Authentik | Should -Match '(?is)default.{0,80}profile.{0,200}groups'
          $script:Authentik | Should -Match '(?i)fallback'
      }

      It 'verifies the three outcomes: a member signs in, a non-member is refused, the token carries groups and email' {
          $section = ($script:Authentik -split '(?m)^## Verification\s*$')[1]
          $section | Should -Match '(?i)member'
          $section | Should -Match '(?i)not a member|non-member|refused'
          $section | Should -Match 'groups'
          $section | Should -Match 'email'
      }

      It 'AUTHENTIK.md contains no client secret value' {
          $script:Authentik | Should -Not -Match '(?i)client_secret\s*[=:]\s*\S'
          $script:Authentik | Should -Not -Match 'AUTH__CLIENTSECRET=(?!<|\s*$)'
          $script:Authentik | Should -Not -Match '[A-Za-z0-9+/]{40,}'
          $script:Authentik | Should -Not -Match '(?i)(secret|password)\s*[=:]\s*[A-Za-z0-9]{10,}'
      }

      It 'does not link the private provisioning repository' {
          $script:Authentik | Should -Not -Match 'dev\.azure\.com'
          $script:Authentik | Should -Not -Match '\]\(https?://[^)]*syntax-circus-authentik'
      }

      It 'AGENT-AUTHENTICATION.md points at the guide and states the claim requirements' {
          $script:AgentAuth | Should -Match ([regex]::Escape('](AUTHENTIK.md)'))
          $script:AgentAuth | Should -Match ([regex]::Escape('](SELF-HOSTING.md)'))
          foreach ($phrase in 'sub', 'email', 'groups', 'TECHSTRAP_GROUP_CLAIM_TYPE') { $script:AgentAuth | Should -Match ([regex]::Escape($phrase)) }
          $script:AgentAuth | Should -Not -Match '(?i)add a scope mapping'
      }

      It 'ADMIN-APP.md points to the guide and does not claim the provider flow is verified yet' {
          $script:AdminApp | Should -Match ([regex]::Escape('](../self-hosting/AUTHENTIK.md)'))
          $script:AdminApp | Should -Match '(?i)verified against a live Authentik in 12c'
      }

      It 'no self-hosting doc uses the techstrap-admin issuer slug' {
          $files = @(Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot 'docs/self-hosting') -Filter *.md) + (Get-Item (Join-Path $script:RepoRoot 'docs/development/ADMIN-APP.md'))
          foreach ($file in $files) {
              $text = [System.IO.File]::ReadAllText($file.FullName)
              $text | Should -Not -Match 'techstrap-admin/' -Because "$($file.Name) must use the techstrap slug"
              $text | Should -Not -Match 'application/o/techstrap-admin' -Because $file.Name
          }
      }

      It 'is ASCII only' { ([regex]::IsMatch($script:Authentik, '[^\x00-\x7F]')) | Should -BeFalse }
  }
  ```
  Run `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/SelfHostDocs.Tests.ps1`: RED. Expected: the new `Describe`'s `BeforeAll` throws (AUTHENTIK.md missing); the earlier `Describe`s stay green.
- [x] **Step 2: Read before writing.** Read `docs/development/ADMIN-APP.md` l.100-130 and `docs/self-hosting/AGENT-AUTHENTICATION.md` (35 lines) so the guide settles the contradictions: one issuer slug `techstrap`; groups come from Authentik's default `profile` scope mapping (it emits `groups`); an explicit groups property mapping is the fallback only when the default mapping was customised or removed. Read `ClaimsCurrentAgent` / `AgentAccessOptions` to state exactly how the admin group and agent group combine (whether an admin must also be an agent-group member).
- [x] **Step 3: Write `docs/self-hosting/AUTHENTIK.md`** (ASCII, LF, placeholders only). Sections:
  - `## Overview`: what is configured (two groups, one OAuth2/OpenID provider, one application), what is not (no blueprint ships; no provisioning code is copied); a pointer to [SELF-HOSTING.md](SELF-HOSTING.md) for the provider-neutral contract.
  - `## Prerequisites`: a running Authentik with an admin account; the public host of the Admin (`https://admin.example.com`) and of Authentik (`https://auth.example.com`); the Api and Admin env files from DEPLOYMENT.md.
  - `## Create the groups`: Directory, Groups, Create: `techstrap-agents` and `techstrap-admins`; the membership rule verified in Step 2; users added to the groups.
  - `## Create the provider`: Applications, Providers, Create, OAuth2/OpenID Provider: name `TechStrap`; client type **Confidential**; client ID (generated, copied to `AUTH__CLIENTID`); client secret (generated, copied to `AUTH__CLIENTSECRET` in `.env.admin` only, never committed); strict redirect URI `https://admin.example.com/signin-oidc`; post-logout redirect `https://admin.example.com/signout-callback-oidc`; any RS256 signing key; scopes `openid`, `profile`, `email`, `offline_access`; subject mode default; issuer mode per provider (application slug); token validity (access 5-10 minutes, refresh 30 days are fine); PKCE: the Admin uses authorization code with PKCE and Authentik accepts it for confidential clients with no extra setting.
  - `## Create the application`: Applications, Applications, Create: name `TechStrap`, slug `techstrap` (the slug is the issuer path), provider `TechStrap`; optionally a policy binding restricting the application to the two groups (the Api also enforces membership, so a user outside both groups can sign in at the IdP but is refused by TechStrap); the resulting issuer `https://auth.example.com/application/o/techstrap/` and the discovery URL ending `.well-known/openid-configuration`.
  - `## The groups claim`: the default Authentik `profile` scope mapping already emits `groups` (the list of group names), so nothing is added while defaults are intact; the fallback when the default mapping was customised or removed: Customisation, Property Mappings, create a Scope Mapping with scope name `groups` returning the user's group names (`[g.name for g in request.user.ak_groups.all()]`) and attach it to the provider; set `TECHSTRAP_GROUP_CLAIM_TYPE` only if a different claim name is used.
  - `## Where each value goes`: a table: issuer to `AUTH__AUTHORITY` (`.env.admin`) and `AUTHENTICATION__JWTBEARER__AUTHORITY` (`.env.api`); client ID to `AUTH__CLIENTID` and `AUTHENTICATION__JWTBEARER__AUDIENCES__0` (audience = the Admin client id); client secret to `AUTH__CLIENTSECRET`; group names to `TECHSTRAP_AGENT_GROUP` and `TECHSTRAP_ADMIN_GROUP` in BOTH files (identical); fenced `.env.admin` and `.env.api` excerpts with angle-bracket placeholders only (`AUTH__CLIENTSECRET=<paste the client secret here>`).
  - `## Verification`: (1) a user in `techstrap-agents` signs in to the Admin and lands on the ticket list; (2) a user who is not a member of either group signs in at Authentik but TechStrap refuses them (the exact observed behaviour is taken from the `AdminSignInTests` / `AgentAuthTests` expectations); (3) decode the access token (Authentik's token view, or an offline decoder; never paste production tokens into a website) and confirm `sub`, `email`, `groups` containing the group names, `aud` equal to the client ID, `iss` equal to the issuer; (4) a user without an email is refused (`agent-email-required`). The text does not claim a live check happened (that is 12c).
  - `## Troubleshooting`: a table of symptom, cause, fix: redirect URI mismatch; `invalid_client` (secret or client type); signed in but 403 (groups missing from the access token: check the scope mapping and that `profile` is requested; names are case-sensitive and must match exactly); every restart signs agents out (keys volume lost); 401 from the Api (audience or authority mismatch; the issuer's trailing slash must match exactly); no refresh (`offline_access` not selected on the provider); clock skew.
  - `## Reference`: Syntax Circus keeps an internal repository named `syntax-circus-authentik` (private, Azure DevOps) with deeper provider material, by document title: "OIDC provider and application", "Scopes, roles and authorization", "First run and install", "Machine clients (M2M)"; named only because it is private; this guide stands alone. No link.
- [x] **Step 4: Rewrite `AGENT-AUTHENTICATION.md`** as a short pointer: one paragraph (what it is for), a "Claim requirements" table (`sub` required, `email` required, `groups` (or `TECHSTRAP_GROUP_CLAIM_TYPE`) with the two group names, `aud` = the Admin client id, issuer exactly as in `AUTH__AUTHORITY`), the settings table from the old file if the README or a pin relies on it (run `grep -rn "AGENT-AUTHENTICATION" scripts docs README.md` and keep every pinned phrase), and links to [AUTHENTIK.md](AUTHENTIK.md) and [SELF-HOSTING.md](SELF-HOSTING.md). It drops the "add a scope mapping" step and the `techstrap-admin` slug.
- [x] **Step 5: Edit `docs/development/ADMIN-APP.md` l.104-125** (Edit tool): replace the duplicated step list with a short paragraph and the link `[Authentik worked example](../self-hosting/AUTHENTIK.md)`; replace the "not verified" wording with "The provider flow is documented in the worked example and is verified against a live Authentik in 12c (T14)." Keep the local-development parts of that section untouched. If the owner confirms a live check before merge, replace the sentence with the date and evidence and update this pin and the Task 5 note.
- [x] **Step 6: GREEN.** `pwsh -File scripts/Invoke-ScriptTests.ps1`; `git diff --check`; `grep -rn "techstrap-admin/" docs/` finds nothing.
- [x] **Step 7: Mutations:** add `AUTH__CLIENTSECRET=abc123def456` to AUTHENTIK.md (`AUTHENTIK.md contains no client secret value` dies); add a 44-character base64 string (the same test dies); write `https://auth.example.com/application/o/techstrap-admin/` in AGENT-AUTHENTICATION.md (`no self-hosting doc uses the techstrap-admin issuer slug` dies); delete the `## Troubleshooting` heading (`has the sections` dies); delete the `](AUTHENTIK.md)` link from AGENT-AUTHENTICATION.md (the pointer pin dies). Restore each.
- [x] **Step 8: Commit** `docs(self-hosting): Authentik worked example and consistent OIDC guidance (P12-T17)`; stage `docs/self-hosting/AUTHENTIK.md docs/self-hosting/AGENT-AUTHENTICATION.md docs/development/ADMIN-APP.md scripts/tests/SelfHostDocs.Tests.ps1`.

### Task 5: Close-out (D-051 12b rulings, spec ticks, roadmap, pins)

**Files:**
- Modify: `docs/architecture/04-DECISION-LOG.md`, `docs/architecture/PHASE-12-release-hardening.md`, `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, `docs/architecture/00-DISCOVERY-INDEX.md`, `scripts/tests/RepositoryDocs.Tests.ps1`, this plan

**Interfaces:**
- Consumes: Tasks 1-4. Produces: the closed 12b record that 12c starts from.

- [x] **Step 1: Pins first (RED).** In `RepositoryDocs.Tests.ps1` change the existing `Describe 'PHASE-12a close-out'` (the unticked-set loop and the roadmap/discovery rows) and add a new Describe. Edits to the 12a block:
  - In the test `ticks P12-T01 to P12-T10 and P12-T19 with an as-built note and leaves the later tasks open`: rename it `ticks P12-T01 to P12-T10 and P12-T19 with an as-built note (12a)`, keep the first loop and delete the second loop (the unticked set moves to the 12b block).
  - In `marks phase 12 as 12a complete in the roadmap and the discovery index`: rename it `marks phase 12 as 12a merged in the roadmap and the discovery index` and change both patterns to `12a merged \(PR #28\)`.
  Append:
  ```powershell
  BeforeDiscovery {
      $script:MainResolvable = [bool](& git rev-parse --verify --quiet main 2>$null)
  }

  Describe 'PHASE-12b close-out' {
      BeforeAll {
          $script:Spec = Get-RepoText 'docs/architecture/PHASE-12-release-hardening.md'
          $script:Plan = Get-RepoText 'docs/superpowers/plans/2026-10-09-phase-12b-docs-scripts.md'
          $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md'
      }

      It 'ticks P12-T11, T13, T16 and T17 with an as-built (12b) note' {
          foreach ($id in 'P12-T11', 'P12-T13', 'P12-T16', 'P12-T17') {
              $script:Spec | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is done in 12b"
              $block = [regex]::Match($script:Spec, '(?ms)^- \[x\] \*\*' + $id + '\*\*.*?(?=^- \[|^## |\z)').Value
              $block | Should -Match '\*\*As built \(12b\):\*\*' -Because "$id needs an as-built note"
          }
      }

      It 'leaves the 12c tasks open' {
          foreach ($id in 'P12-T12', 'P12-T14', 'P12-T15', 'P12-T18', 'P12-T20', 'P12-T21') {
              $script:Spec | Should -Match ('(?m)^- \[ \] \*\*' + $id + '\*\*') -Because "$id is 12c"
          }
      }

      It 'records the 12b corrections in the spec' {
          $corrections = ($script:Spec -split '(?m)^### Corrections \(D-051, 2026-10-08\)\s*$')[1]
          foreach ($phrase in 'SELF-HOSTING.md', 'AUTHENTIK.md', 'docs/runbooks/backup-restore.md', 'append-only', 'not in CI', 'bash', 'openssl', 'private') {
              $corrections | Should -Match ([regex]::Escape($phrase)) -Because $phrase
          }
      }

      It 'adds the D-051 addendum 12b rulings' {
          $script:Log | Should -Match '12b rulings \(2026-10-09\)'
          $addendum = ($script:Log -split '12b rulings \(2026-10-09\)')[1]
          foreach ($phrase in 'tests/load', 'X-Forwarded-For', 'postgres:17', 'openssl', 'rehearsal', 'drill', 'SELF-HOSTING.md', 'syntax-circus-authentik', '26MiB') {
              $addendum | Should -Match ([regex]::Escape($phrase)) -Because $phrase
          }
      }

      It 'marks phase 12 as 12b complete in the roadmap and the discovery index' {
          (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 12 \|.*D-051.*12a merged \(PR #28\); 12b complete \(pending merge\)'
          (Get-RepoText 'docs/architecture/00-DISCOVERY-INDEX.md') | Should -Match '(?m)^\| 12 \|.*12a merged \(PR #28\); 12b complete \(pending merge\)'
      }

      It 'adds no workflow file and no CI job for 12b' -Skip:(-not $script:MainResolvable) {
          $changed = @(& git -C $script:RepoRoot diff --name-only main...HEAD) + @(& git -C $script:RepoRoot diff --name-only)
          @($changed | Where-Object { $_ -like '.github/*' }) | Should -BeNullOrEmpty -Because '12b adds no CI job'
      }

      It 'has the 12b plan fully ticked with an As built section' {
          $script:Plan | Should -Not -Match '(?m)^\s*- \[ \]'
          $script:Plan | Should -Match '(?m)^## As built\s*$'
      }
  }
  ```
  Run `pwsh -File scripts/Invoke-ScriptTests.ps1`: RED. Expected: the ticks, corrections, addendum, roadmap and plan-ticked tests fail (the CI-files test passes or is skipped).
- [x] **Step 2: Decision log.** Edit `docs/architecture/04-DECISION-LOG.md`: under D-051 append a dated block `12b rulings (2026-10-09)` condensing rulings 1-8: (1) k6 layout `tests/load/`, thresholds in code, not in CI; (2) `X-Forwarded-For` rotation over a /24 with the runner trusted as a proxy, the spike pinned to one IP, the 60-IP portal-form arithmetic; (3) outbox and dead letters measured by SQL after the run; (4) bash scripts, `pg_dump -Fc` inside `postgres:17`, `alpine tar`, `openssl enc -aes-256-cbc -pbkdf2`, `--keep-days`, `--dry-run`, scratch-project restore; (5) rehearsal on the dev stack in 12b, the UAT drill in 12c (T15); (6) file names `docs/self-hosting/SELF-HOSTING.md`, `docs/self-hosting/AUTHENTIK.md`, `docs/runbooks/backup-restore.md`, and the `syntax-circus-authentik` repository is private so the guide is self-sufficient; (7) "attachments are append-only" is corrected (hard delete and requester erase remove files; a restore resurrects them; re-run erasures from `admin_events`), and the Caddy body limit is `26MiB` not `26MB` with the reason; (8) the pin updates. The block is append-only: earlier D-051 text is not edited.
- [x] **Step 3: Spec.** Edit `docs/architecture/PHASE-12-release-hardening.md`: add to `### Corrections (D-051, 2026-10-08)` a sub-list "12b additions (2026-10-09)": the file names (`docs/self-hosting/SELF-HOSTING.md`, `docs/self-hosting/AUTHENTIK.md`, `docs/runbooks/backup-restore.md`, results in `docs/load-test-results.md`); "attachments are append-only" is corrected; k6 is not in CI (static Pester only); the backup scripts are bash with openssl encryption; the rehearsal is 12b and the drill is 12c; `syntax-circus-authentik` is private so the guide does not depend on it. Tick `- [x] **P12-T11**`, `T13`, `T16`, `T17` and add to each task block a line `  - **As built (12b):** ...` stating what shipped (file paths, flags, the rehearsal and smoke results from Tasks 1 and 2) and any deviation (the `26MiB` limit; the `server_errors` metric; `docs/self-hosting.md` named `SELF-HOSTING.md`). Leave T12, T14, T15, T18, T20, T21 unticked.
- [x] **Step 4: Roadmap, discovery index and README.** Edit the phase 12 rows in `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` and `docs/architecture/00-DISCOVERY-INDEX.md`: replace "12a complete (pending merge)" with "12a merged (PR #28); 12b complete (pending merge)" and keep the rest of the row; mark the P12 task rows for T11, T13, T16 and T17 done where the roadmap lists tasks. Update the README status sentence (PHASE-12 progress: hardening merged, docs and scripts in review, UAT and release next, then v0.3.0) without touching the phrases pinned in Task 3.
- [x] **Step 5: GREEN.** `pwsh -File scripts/Invoke-ScriptTests.ps1` (whole folder); `git diff --check`; every changed file ASCII and every new file LF.
- [x] **Step 6: Tick this plan.** Tick every checkbox in this file and fill `## As built` with per-task deviations: the real rehearsal numbers, the smoke result, the trusted-proxy and table names that were verified, the image tags pinned, anything that differed from the plan.
- [x] **Step 7: Mutations:** untick `P12-T13` in the spec (the tick pin dies); remove `12b complete` from the roadmap row (the roadmap pin dies); delete the `12b rulings (2026-10-09)` heading (the addendum pin dies); add a file under `.github/workflows/` on a branch where `main` resolves (the CI pin dies). Restore each.
- [x] **Step 8: Commit** `docs: PHASE-12b close-out (D-051 12b rulings, spec ticks, roadmap, pins)`; stage the six modified files by path.

## Verification (whole PR)
```
pwsh -File scripts/Invoke-ScriptTests.ps1                          # whole Pester folder green (static and fast)
bash -n deploy/backup.sh && bash -n deploy/restore.sh              # syntax; shellcheck deploy/*.sh when installed
pwsh -File scripts/Invoke-LoadTest.ps1 -Target local -Scenario spike -DryRun    # prints the plan, runs nothing
git diff --stat main...HEAD -- src tests/TechStrap.* .github       # empty: no .NET change, no workflow change
git diff --check
```
On-demand evidence recorded in the PR: the Task 1 rehearsal (backup of the dev stack, restore into `techstrap-restore`, verification output, teardown) in `docs/runbooks/backup-restore.md`, and the Task 2 smoke (`-Scenario sustained -Duration 30s -Rate 2`) in `tests/load/README.md`. No `dotnet` command is part of this PR's verification because no .NET file changes; the existing CI jobs run unchanged. Then `superpowers:finishing-a-development-branch`: PR "PHASE-12b: docs and scripts (D-051)" against `main` from `feat/phase-12b-docs-scripts`; confirm the CI Pester step passes on the PR (the only CI-visible change). No tag is cut here (`v0.3.0` is 12c).

## Risks / open items
- **Trusted-proxy key names differ between sources.** The rulings name `TRUSTEDPROXY__TRUSTEDNETWORKS`; the templates show `TRUSTEDPROXY__TRUSTEDPROXIES__0`. Task 2 Step 6 resolves it by reading the options class and the templates, and the README states the verified name; the SELF-HOSTING table follows the template (the Pester pin enforces that).
- **Dev-stack proxy trust may not be achievable without an override.** Docker Desktop on Windows presents requests to the Api from the VM gateway address. If the runner cannot be trusted locally, the Task 2 smoke records exactly what was proved instead and the 12c UAT run is the real proof; do not weaken a rate limit or edit tracked compose files to force it.
- **Table and column names in the SQL** (tickets, attachments, `email_outbox`, dead letters, `admin_events` event types) are verified from the model snapshot and `\d` output while implementing; the plan deliberately does not guess them. Task 1 Step 2 and Task 2 Step 5 hold them in named constants.
- **Rehearsal on Windows** runs the bash scripts in Git Bash with Docker Desktop; `MSYS_NO_PATHCONV=1` is set inside the scripts and all data moves through pipes (no bind mounts), so no path translation is involved. If Git Bash mangles a path anyway, run the rehearsal from WSL and say so in the record.
- **`age` is not used.** openssl keeps the install footprint at zero; the runbook mentions `age` as optional. A passphrase file protects against theft of the backup, not of the host; the runbook says to keep it off the box.
- **Caddy `26MiB` vs the rulings' `26MB`** is a deliberate correction (Decisions); if the owner prefers the literal value, change the guide and the pin together.
- **Authentik behaviour is documented from the internal repository's notes and Authentik's defaults, not exercised in 12b.** ADMIN-APP.md therefore says "verified against a live Authentik in 12c (T14)". If the owner runs the flow before merge, record the date and evidence and flip the wording and its pin.
- **Image tag pins in other tests.** Changing the example tags to `0.2.0` may break a pin in `Test-ComposeSmoke.ps1`, `ConfigContract.Tests.ps1` or a docs pin; Task 3 Step 3 greps for them and updates the pins, never the contract.
- **The k6 image tag** is chosen when implementing (newest resolvable `grafana/k6` tag, never `latest`); record it in the README.
- **Static-only CI** means a script can regress without a test noticing (bash behaviour, k6 logic); the on-demand rehearsal and smoke recorded in the PR are the evidence, and 12c's UAT drill and load run are the real exercise.

## As built

Recorded at close-out (Task 5, Step 6): per-task deviations from this plan, with the rehearsal and smoke figures.

- **Task 1 (backup and restore, P12-T13).** Beyond the plan: `--db-url` restores require `--yes`; `--overwrite` clears the target volume before extracting; restore reads encrypted or plain from the manifest and rejects a mismatched flag; the failure trap also removes a partial backup directory, and `DONE=1` is set right after the manifest so a retention failure can only warn and never delete a finished backup; `DEST` is assigned after `mkdir` succeeds; the passphrase path is converted with `cygpath -m` when present (Git Bash openssl). Rehearsal on the dev stack in Git Bash: backup 7.3 s, restore 13.2 s into `techstrap-restore`; migration, ticket and attachment counts matched; the refusals were exercised and teardown left the real volumes. The `--db-url` promotion path and the systemd timer were not exercised (12c, T15). Lesson: a test file planted into the dev storage volume as root left `/app/storage/attachments` root-owned (the Api runs as uid 10001) and uploads answered 403 until a `chown` inside the container; the runbook now carries the ownership warning, the rehearsal note and a post-restore `stat` check (final fix wave: the database password no longer reaches `ps`, scratch restores are label-guarded, promotion is single-transaction; see the "Final review fix wave" bullet).
- **Task 2 (k6 suite and runner, P12-T11).** The plan's Ruling 2 arithmetic applied the public read limit (10 per 60 s) to public intake; the applicable limit is public submit, 5 per 600 s per IP, so at the default profile (2 req/s public over 60 IPs) about 75% of public-intake calls (about 7.5% of all requests) answer 429 by design. The sustained composer therefore relies on `server_errors` (5xx or no response, status 0 included, rate == 0), `checks` and p95 < 500 ms, not on `http_req_failed`, exactly like the spike; `TS_IP_COUNT=240` keeps public intake within its limit; the 0-server-errors budget is unchanged. Forwarded-IP trust was proven locally for the Api (5 x 201 then 429 from one rotated IP while a neighbour got 201) but not for the Portal, which trusts only `REVERSE_PROXY_CIDR`; the README documents the `<gateway>/32` step and the 12c run (T12) is the real proof. The k6 image is pinned `grafana/k6:2.1.0`; `.gitattributes` gained `tests/load/** text eol=lf` so the LF pin holds on Windows. Smoke (recorded in `tests/load/README.md`): sustained 30 s at 2 rps p95 18 ms; spike 6534 x 429 during the spike and recovery p95 12 ms; `-UseDocker` p95 19 ms; a 2-minute default-rate sustained run exited 0 with p95 12.92 ms, 0 server_errors of 2409, 10 x 429 and 0 dead-lettered in the outbox afterwards.
- **Task 3 (SELF-HOSTING.md, P12-T16).** The Caddy example uses `max_size 26MiB` (Caddy's MB is 10^6, too small for a 25 MiB submission); the compose-inputs table marks all 12 `${NAME:?}` keys required, pinned from `deploy/docker-compose.yml`; the stale image tags were only in `deploy/.env.uat.example` and `deploy/.env.production.example` and are now `0.2.0`; `SelfHostDocs.Tests.ps1` carries a marked copy of the ConfigContract env-parsing helpers because they live inside a `BeforeAll` and cannot be dot-sourced.
- **Task 4 (AUTHENTIK.md, P12-T17).** The groups fallback mapping must use the scope name `profile` (Authentik emits only mappings whose scope the client requests; the Admin requests openid, profile, email and offline_access), or the operator appends `AUTH__SCOPES__0=groups`. The stale `techstrap-admin` issuer slug was removed from `docs/development/ADMIN-APP.md` (two places). The provider flow is documented, not exercised: it is verified against a live Authentik in 12c (T14).
- **Task 5 (close-out).** Pester totals across the tasks: 441 after Task 1, 457 after Task 2, 471 after Task 3, 483 after Task 4, all static and green. The Review Focus 5 pin was made static and then, in the final fix wave, a policy pin: `no workflow runs the load or backup scripts (on-demand only)` asserts that no `run:` line in any `.github/workflows/*.yml` matches `k6`, `backup.sh`, `restore.sh` or `Invoke-LoadTest`; the exact file-set equality and the `git diff main...HEAD` test (and its `BeforeDiscovery` helper) were removed. No file under `src/`, `tests/TechStrap.*` or `.github/` changed in 12b. The roadmap lists P12 tasks without a status column, so only the phase row and the discovery row changed.
- **Final review fix wave.**
  - C1: the database password no longer reaches the host `ps`; both scripts refuse a `--db-url` with a password (exit 2) and forward `PGPASSWORD` to the containers by name; restore.sh gained the `TECHSTRAP_DB_URL` fallback.
  - C2: a scratch restore refuses protected and manifest projects even with `--overwrite`; scratch objects carry the `techstrap.restore-scratch=1` label and only labelled objects are overwritten or torn down; promotion needs `--confirm-project` equal to the target.
  - I1: the sustained and spike composers fail on any unexpected 429 (trusted intake, KB search, customer view, spike recovery); proven with `TS_IP_COUNT=1` (exit 99) against the default run (exit 0).
  - I2: the UAT proxy recipe is to run k6 on the UAT host against the loopback ports (compose owns the trusted networks; `--network host` with `-UseDocker` on Linux).
  - I3: the scratch Postgres joins `<target>-db` (the external `TECHSTRAP_DB_NETWORK` the deploy compose expects) and the runbook lists the commands to start the apps on it.
  - I4: restored row counts below the manifest warn instead of failing (counts are read after the dump); more rows or a different migrations count still fails.
  - I5: the promotion `pg_restore` runs `--single-transaction --exit-on-error`; the runbook adds a fresh backup first and prefers a new empty database over `--clean`.
  - I6: the workflow pin is a policy pin on `run:` lines, the git-diff test is gone, and the anchor scan skips fenced code; SELF-HOSTING.md and AUTHENTIK.md wording defers to the 12c live Authentik check.
