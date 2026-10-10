# Load tests (k6)

On-demand k6 scenarios for the PHASE-12 performance budgets: p95 under 500 ms for the intake, KB search, customer view and Portal form
calls, zero 5xx responses, and an email outbox that drains within 2 minutes of the end of a spike with no dead letters caused by load.
These runs are not part of CI. Only the static Pester pins in `scripts/tests/LoadScripts.Tests.ps1` run there; they read files and never
start k6, Docker or the network. Results of real runs are recorded in `docs/load-test-results.md`.

## Prerequisites

- k6 on PATH, or Docker (the runner then uses the pinned `grafana/k6:2.1.0` image and rewrites `localhost` to `host.docker.internal`).
- A running stack: the local dev stack (`docker compose up -d`, with the seeded Orbitly product and dev keys) or a UAT stack.
- PowerShell 7 for the runner.

## Environment variables

All variables are optional for the local dev stack. The runner sets the first four from its parameters; the rest pass through when set.

| Variable | Default | Meaning |
| --- | --- | --- |
| `TS_BASE_URL` | `http://localhost:8080` | Api base URL |
| `TS_PORTAL_URL` | `http://localhost:8082` | Portal base URL |
| `TS_PRODUCT_KEY` | `orbitly` | Product key used in the public and Portal URLs |
| `TS_TRUSTED_KEY` | dev Orbitly server key (`...NotASecret...`) | Trusted (server) API key for `/api/intake/tickets`; required for UAT |
| `TS_PUBLIC_KEY` | dev Orbitly app key (`...NotASecret...`) | Public (app) API key for the public intake; required for UAT |
| `TS_FORWARDED_PREFIX` | `10.99.0.` | Prefix of the synthetic client IPs sent in `X-Forwarded-For` |
| `TS_IP_COUNT` | `60` | How many synthetic IPs to rotate through (1 to 240) |
| `TS_RATE` | `20` | Total requests per second of the sustained mix |
| `TS_DURATION` | `10m` | Duration of the sustained run |
| `TS_SPIKE_PEAK` | `100` | Peak requests per second of the spike |

Keys are sent in the `X-Api-Key` header, never in a URL, and no script prints them. The runner forwards them to Docker by name only.

Runner: `pwsh scripts/Invoke-LoadTest.ps1 -Target local|uat -Scenario sustained|spike [-Rate <int>] [-Duration <string>] [-BaseUrl <url>] [-PortalUrl <url>] [-UseDocker] [-SkipDbCounts] [-DryRun]`.
`-Target uat` requires `-BaseUrl`, `-PortalUrl` and both key variables in the environment. `-DryRun` prints the plan and the command and starts nothing.

## Scenarios

- `scenarios/sustained.js`: `TS_RATE` requests per second for `TS_DURATION`: 50% trusted intake, 10% public intake (a file on every fourth call), 30% KB search,
  10% customer ticket view, plus the Portal contact form at one request a minute. Each request carries a rotated `X-Forwarded-For` taken from 60 addresses, because the
  Api and Portal rate-limit per client IP (public submit 5 per 10 minutes per IP, Portal form likewise); one runner address would be throttled long before the budgets mean anything.
  Thresholds: p95 under 500 ms overall and per call type, `server_errors` rate 0, checks above 99%. `http_req_failed` is not asserted because a 429 is the correct answer under the rate limits.
  Unexpected throttling is bounded to zero: `http_reqs{name:intake-trusted,status:429}`, `http_reqs{name:kb-search,status:429}` and `http_reqs{name:customer-view,status:429}` must each have `count==0`. With the
  forwarded-address rotation trusted none of these calls reaches a limit, so a 429 there means the rotation is not honored (every request shares one address) and the run proves nothing; without these
  thresholds such a run would report green. Public intake and the Portal form are excluded because they answer 429 by design (see below).
- `scenarios/spike.js`: ramps to `TS_SPIKE_PEAK` requests per second for 60 seconds against the trusted intake from ONE address (`<prefix>250`), crossing the 120 per 60 s limit.
  A 429 is the correct answer there; a 5xx never is. The thresholds assert at least one 429 during the spike and zero server errors. A recovery phase starts at 75 s with
  5 requests per second from rotated addresses (the spike address stays throttled until its window ends) and must keep p95 under 500 ms with no 429 at all
  (`http_reqs{phase:recovery,status:429}` `count==0`, for the same reason as above: a 429 there means the rotation is not trusted). The spike has a fixed 105 s profile; `-Rate` and `-Duration` do not apply and the dry-run prints `TS_SPIKE_PEAK`.
- Every module (`intake-trusted.js`, `intake-public.js`, `portal-form.js`, `customer-view.js`, `kb-search.js`) also runs alone as a one-iteration smoke, for example `k6 run tests/load/kb-search.js`.
- Caveat: KB search responses are cached for 60 seconds, so that call type mostly measures the cache.

### Expected 429s at the default profile

A 429 is expected rate-limit policy, not a failure, so the sustained thresholds do not assert `http_req_failed`. Public intake runs at 2 requests per second over `TS_IP_COUNT`=60 addresses, which is 20 submissions per address per 10 minutes against the public-submit limit of 5 per 600 s per address. About 75% of public-intake calls (roughly 7.5% of all requests) therefore answer 429 by design once each address has used its allowance. `TS_IP_COUNT=240` (the maximum) brings public intake about to its limit (240 x 5 / 600 s = 2 requests per second); the address choice is not round-robin in time, so a few 429s remain possible. The mix and the limits are not changed to avoid this.

## Making the stack trust the runner

The `X-Forwarded-For` rotation only takes effect when the request arrives from a trusted proxy. The Api and Portal read the trusted addresses from the
`TRUSTEDPROXY__TRUSTEDNETWORKS__<n>` environment variables (CIDR ranges) and `TRUSTEDPROXY__TRUSTEDPROXIES__<n>` (single addresses); see `docker-compose.yml`,
`deploy/docker-compose.yml`, `deploy/.env.api.example` and `deploy/.env.portal.example`. If the header is not trusted, every request shares the runner's own address and the
per-IP limits answer 429 for most of the run.

Warning: a trusted proxy can set any client IP, which defeats per-IP limits and audit data. Treat this as a test-only setting and remove it after the run.

- Local: the dev `docker-compose.yml` trusts the whole compose subnet on the Api (`TRUSTEDPROXY__TRUSTEDNETWORKS__0` is `TECHSTRAP_SUBNET`, default `172.16.31.0/24`). On Docker Desktop a request from the host
  arrives from the network gateway, which is inside that subnet, so the Api works without changes. The Portal (and the Admin) trust only `REVERSE_PROXY_CIDR`, a placeholder by default. For a Portal form run longer than a few minutes,
  set it to the gateway as a /32 and recreate the portal: get the gateway with
  `docker network inspect techstrap_default --format "{{(index .IPAM.Config 0).Gateway}}"` and set `REVERSE_PROXY_CIDR=<gateway>/32` in the shell or the root `.env` before `docker compose up -d portal`.
- UAT: do not try to trust a remote runner. The deploy compose sets `TRUSTEDPROXY__TRUSTEDNETWORKS__0` and `__1` itself from `TECHSTRAP_SUBNET` and `REVERSE_PROXY_CIDR` (values in the
  `TECHSTRAP_ENV_DIR` files are overridden), and the Api honors only the rightmost forwarded address. The recipe is to run k6 ON the UAT host against the loopback-published ports
  (`-BaseUrl http://127.0.0.1:<TECHSTRAP_API_PORT> -PortalUrl http://127.0.0.1:<TECHSTRAP_PORTAL_PORT>`): those requests reach the containers from the compose gateway, which is already
  `REVERSE_PROXY_CIDR`, so the rotated `X-Forwarded-For` is trusted with no configuration change. Use the local k6 binary on the UAT host; `-UseDocker` is not supported
  for this recipe (the runner rewrites 127.0.0.1 to host.docker.internal, which cannot reach loopback-published ports). This path does not measure TLS through Caddy or any real network latency.

## Running

```powershell
# Local dev stack, 30 second smoke
pwsh scripts/Invoke-LoadTest.ps1 -Target local -Scenario sustained -Rate 2 -Duration 30s

# Local, full sustained run using the Docker image
pwsh scripts/Invoke-LoadTest.ps1 -Target local -Scenario sustained -Rate 20 -Duration 10m -UseDocker

# UAT spike (keys come from the environment)
$env:TS_TRUSTED_KEY = '<server key>'; $env:TS_PUBLIC_KEY = '<app key>'
pwsh scripts/Invoke-LoadTest.ps1 -Target uat -Scenario spike -BaseUrl https://api.uat.example -PortalUrl https://uat.example -UseDocker

# Show the plan only
pwsh scripts/Invoke-LoadTest.ps1 -Target local -Scenario spike -DryRun
```

The exit code is k6's: non-zero when a threshold failed.

## Reading the results

Each run writes `tests/load/results/<timestamp>-<scenario>.json` (a k6 `--summary-export`; the folder is gitignored). Read `metrics.http_req_duration["p(95)"]` for the p95 in
milliseconds, `metrics.server_errors.rate` (must be 0) and `metrics.throttled_429.count`. The console summary marks each failed threshold with a cross. Copy the figures into
`docs/load-test-results.md`.

## After the run: outbox drain and dead letters

Load creates tickets and queues confirmation emails in `email_outbox`. The budget: the outbox drains (no `Pending` or `Sending` rows) within 2 minutes of the end of the spike, and no
row is dead-lettered because of load. The statuses are `Pending`, `Sending`, `Sent` and `DeadLettered`.

```sql
select status, count(*) from email_outbox group by status order by status;
select count(*) from email_outbox where status = 'DeadLettered';
```

- Local (the runner prints both after a local run unless `-SkipDbCounts`):
  `docker compose exec -T postgres psql -U techstrap -d techstrap -c "select status, count(*) from email_outbox group by status order by status"`
- UAT: `psql "$TECHSTRAP_DB_URL" -c "select status, count(*) from email_outbox group by status order by status"` and the same with the dead-letter query.

## Cleaning up

Load tickets use `load+<n>@example.com` as the requester and `load-user-<n>` as the external reference. Erase those requesters through the Admin app or hard-delete the tickets
before handing the stack back. Never run these scenarios against production.

## Verified

Verified 2026-10-09 against the local dev stack (compose project `techstrap`, seeded Orbitly product), k6 v0.55.0 (local binary) and grafana/k6:2.1.0 (Docker image):

- `pwsh -File scripts/Invoke-LoadTest.ps1 -Target local -Scenario sustained -Duration 30s -Rate 2`: 62 iterations, 65 requests, p95 18.03 ms, `server_errors` 0.00% (0 of 63), checks 100%, all thresholds green; outbox afterwards 10 Pending, 155 Sent, 0 dead-lettered.
- `pwsh -File scripts/Invoke-LoadTest.ps1 -Target local -Scenario spike`: 6925 requests, 6534 answered 429 during the spike (`throttled_429{phase:spike}`), `server_errors` 0.00%, recovery-phase p95 12.38 ms, thresholds green; outbox afterwards 8 Pending, 951 Sent, 0 dead-lettered.
- `-UseDocker` with grafana/k6:2.1.0 (20 s, rate 2): exit 0, `server_errors` 0.00%, p95 19.18 ms.
- Proxy trust: no change to the dev stack was needed for the Api. Six public submissions from one forwarded address `10.99.0.231` returned 201 five times and then 429 (the 5 per 600 s per-IP limit),
  while a first request from `10.99.0.232` still returned 201, proving the forwarded address is honored as the rate-limit key. The Portal's `REVERSE_PROXY_CIDR` was left at its placeholder, so the Portal form
  was only exercised at one request per run window; use the `REVERSE_PROXY_CIDR=<gateway>/32` step above for longer Portal runs (not exercised here).
- Sustained at the default profile (`pwsh -File scripts/Invoke-LoadTest.ps1 -Target local -Scenario sustained -Duration 2m`, rate 20, 60 IPs): exit 0, 2406 iterations, 2413 requests, p95 12.92 ms, `server_errors` 0 of 2409, checks 100%, `throttled_429` 10 (the dev IPs had little public-submit history left from earlier runs, so the steady-state 429 share described above was not reached in two minutes); outbox afterwards 108 Pending, 3777 Sent, 0 dead-lettered.
- Environment note: the Api container's `/app/storage/attachments` was created root-owned because the Task 1 backup rehearsal planted a test file into the dev storage volume as root, while the Api runs as uid 10001 (`techstrap`). Attachment uploads then failed with 403 (an UnauthorizedAccessException mapped to a problem response). It was corrected with `chown` inside the container, and the backup/restore runbook warns against writing into the volume as root. A failure like this shows up as a `status is 201` check failure in `intake-public.js`.
- Throttled-run guard (final fix wave, 2026-10-09): `TS_IP_COUNT=1 pwsh -File scripts/Invoke-LoadTest.ps1 -Target local -Scenario sustained -Duration 1m` exited 99 (482 x 429 on `intake-trusted`, 241 on `kb-search`, 61 on `customer-view`; every other threshold was green, so the run would have passed before the new thresholds). The default one-minute run exited 0 with all three 429 submetrics at 0 (a submetric with no samples still reports and passes). The spike with `TS_IP_COUNT=1` exited 99 on `http_reqs{phase:recovery,status:429}` (31 x 429); the default spike exited 0.
