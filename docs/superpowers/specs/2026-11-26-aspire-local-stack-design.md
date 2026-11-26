# TechStrap: self-contained .NET Aspire local stack (design and plan)

## Context
Sibling projects now start with .NET Aspire. TechStrap should too: one `aspire run`, fully self-contained, no port collisions. Today's local path is `docker compose`. It has no identity provider, so Admin sign-in needs an outside one, and the apps read `.env` files.

**Goal:** a cold clone gives you a working, signed-in, seeded TechStrap with nothing external.
**Non-goals:** changing deployment (`deploy/`), `docker-compose.yml`, production behavior, or adding CI jobs. MAUI/SDK work is out.

## Decisions
**Yours:** keep `docker-compose.yml` alongside. Include a reset script, an opt-in smoke test, a sample-client resource and a webhook catcher. Demo data is seeded by default and can be turned off. The webhook catcher captures everything, like Mailpit.

**My calls (change any):**
- New projects go in `tools/`, not `src/` as in sibling projects. In `src/`, the architecture tests reject unknown projects, and `ConfigContract.Tests.ps1` would fail on the dev passwords.
- Capture-switch behavior tests run only locally (Debug). CI builds Release and runs only the "absent in Release" and guard tests.
- The catcher shows only the original destination **host**, because webhook paths contain tokens.
- Users: `sam` (admin), `riley` (agent), `casey` (no group, to see the no-access page). Fixed fake passwords are committed in the AppHost's `appsettings.Development.json`.
- No opt-in switches to real services. Mail always goes to Mailpit.
- The four hosts get an `aspire` launch profile. Side effect: a plain `dotnet run` will then use it (Development, 552xx ports). Check the `--urls` commands in `ADMIN-APP.md` and `PORTAL-APP.md`.
- `aspire.config.json` goes at the repo root.

## Approaches considered
- **Follow the sibling reference implementation (chosen):** isolated AppHost, own port block, `.env` loading off, reset script, smoke test.
- **Follow an older sibling setup:** its hosts still load `.env.local`, so it isn't isolated.
- **Webhook catcher (you chose):** capture everything, rather than a localhost exception in the SSRF checks (riskier in a shipped product) or no catcher.

## Layout
- `tools/TechStrap.AppHost`: `AppHost.cs`, `LocalStackPorts.cs`, `LocalStackComposition.cs`, `HostWiring.cs`, `Authentik/{AuthentikResources.cs, blueprints/techstrap.yaml}`, `appsettings.Development.json`, `Properties/launchSettings.json`, csproj (plain SDK, `IsAspireHost`, new `UserSecretsId`). `AppHost.cs` throws in Release.
- `tools/TechStrap.WebhookCatcher`, `tests/TechStrap.LocalStack.Tests`.
- Add all three to `TechStrap.slnx` **and** `TechStrap.CI.slnf`. Add `tools/` to `.dockerignore`.
- `Aspire.Hosting.{AppHost,Dotnet,PostgreSQL}` 17.0.0 go in `Directory.Packages.props` and as Selected rows in `docs/architecture/03-PACKAGE-MAP.md`, in one commit (`Check-PackageVersions.ps1`). Don't change the shipped pins. No transitive-pinning opt-out is needed here. Don't directly reference 10.0.13 `Microsoft.Extensions.*` packages from the new projects (downgrade errors).

Reuse by porting from the sibling reference implementation's AppHost (`HostWiring.cs`, `LocalStackComposition.cs`, `Authentik\`), plus its `Reset-LocalStack.ps1`, smoke test and spike `findings.md`.

## Ports (block 55200-55299)
| Port | Use |
|---|---|
| 55200 / 55201 / 55202 | Dashboard / OTLP / resource service |
| 55210 | Postgres |
| 55220 / 55221 | Mailpit SMTP / UI |
| 55230 / 55231 | API https / http |
| 55240 / 55241 | Admin https / http |
| 55250 / 55251 | Portal https / http |
| 55260 | Worker (health only) |
| 55270 | Webhook catcher |
| 55290 | Authentik |

## Resources
| Resource | Notes | Waits for |
|---|---|---|
| postgres | image `17`, tag set **before** the volume `techstrap-local-postgres`; databases `techstrap`, `authentik` | - |
| mailpit | `axllent/mailpit:v1.31.4`, `/readyz` | - |
| authentik server / worker | `2026.8.5`; health includes the `techstrap` discovery document; worker uses `WaitForStart(server)`; volume `techstrap-local-authentik-media` | postgres |
| webhook-catcher | - | - |
| api | `/health/ready` | database, authentik |
| worker | - | healthy api (it migrates), mailpit, catcher |
| admin, portal | - | api |
| sample-client | explicit start; Api https URL and the Orbitly trusted key | api |

Containers are persistent. The Postgres password, Authentik secret key and OIDC client secret are generated and persisted.

## Host wiring
- **All four:** `DotEnv__Enabled=false`, Development, blank `Sentry__Dsn`. Map Aspire's OTLP values to `OpenTelemetry__*` (the apps ignore `OTEL_*`), protocol grpc, sample rate 1.
- **Api:** connection string; JWT authority, `Audiences__0=techstrap`, https metadata off; the Api, Admin and Portal public URLs; storage under `local/.state`; trust `127.0.0.1/32` and `::1/128` for the Portal's forwarded visitor IP.
- **Worker:** connection string; Mailpit SMTP (`TlsMode=None`, compose's retry values); Api and Admin public URLs; capture URL.
- **Admin:** `Auth__*`, `Api__BaseUrl`, Portal public URL, key ring under state.
- **Portal:** `Api__BaseUrl`, its public URL, key ring under state.
- **Demo data:** `LocalStack:SeedDevData` (default true) sets `TECHSTRAP_SEED_DEV_DATA`; invalid values fail clearly. Turning it off doesn't delete data. With it off, the sample gets no key and a warning. Link `DevelopmentApiKeys.cs` into the AppHost as a source file so keys can't drift.

## Authentik blueprint
- One confidential provider: slug and client ID `techstrap`, issuer `http://localhost:55290/application/o/techstrap/`, `per_provider`, RS256, `authorization_code` + `refresh_token`, scopes `openid profile email offline_access` (built-in `profile` already emits `groups`), strict `/signin-oidc` and `/signout-callback-oidc` URIs, `default-invalidation-flow` so sign-out really ends the session.
- **Matching seeded agents:** `sub_mode: user_upn` with user attribute `upn` = `dev|sam` / `dev|riley`. Names and emails must equal the seeder's (Sam Whitfield `sam@example.com`, Riley Chen `riley@example.com`), because sign-in overwrites them. A wrong `sub` silently creates a duplicate agent, so test it. Groups: `techstrap-admins`, `techstrap-agents`.
- This differs from the deployment guidance on purpose; comment it, and reword the "no blueprint ships" line in `docs/self-hosting/AUTHENTIK.md`.

## Webhook capture (the only change to shipped code)
- New `src/TechStrap.Infrastructure/Webhooks/LocalCapture/WebhookCaptureRegistration.cs`: wholly inside `#if DEBUG`, plus `Compile Remove` for non-Debug in the csproj.
- It keeps the real `WebhookSender` and replaces only its HTTP handler, through the existing internal constructor. `WebhookPolicy.cs` and `WebhookSender.cs` stay unchanged.
- Key `TECHSTRAP_WEBHOOK_CAPTURE_URL`. Blank: same behavior as today. Set: throw unless Development; require plain http on loopback with no user info, query or fragment; rewrite every request to it; pass the original host only in a header; log a warning.
- `src/TechStrap.Worker/Program.cs:57`: Debug calls the extension. Release keeps today's registration and **fails startup if the key is set**, so it never silently sends real webhooks.
- Don't add the key to appsettings, env examples, deploy templates or compose.
- **Tests:** Release build contains no capture type; source guard (`#if`, `Compile Remove`, Dockerfiles publish Release); Release Worker refuses the key; Debug-only behavior tests (rewrite, headers, Discord body, rejected URLs, private destinations still invalid).
- **Catcher:** Development-only, loopback, in memory, last 200, body size cap; `POST /capture` (204), HTML list with **all values HTML-encoded**, `/api/requests`, `/clear`, `/health`.

## Extras
- `scripts/Reset-LocalStack.ps1`: removes only this stack's containers (proven by our volumes or network), network, two volumes and `local/.state`. `-Secrets`, `-WhatIf`, `-Force`; refuses while port 55200 is in use. Pester test: it can never match compose resources.
- **Smoke test** (`TECHSTRAP_LOCALSTACK_SMOKE=1`, never in CI): launch the AppHost as a child process to keep fixed ports; check discovery, health, the Portal and Admin sign-in pages; file a ticket and find its email in Mailpit.

## Risks, tested first
- **Sign-in:** `sub_mode: user_upn` is from reading Authentik source, not run yet. Fallback: a `sub` scope mapping. If that fails too, stop and ask.
- **https Admin with http Authentik:** cookies, the `form_post` callback and sign-out are unproven. Fallback: a Development-only setting or https for Authentik. This may need you at the browser.
- Aspire's packages enter the all-severity NuGet audit, so a future advisory in them could block CI. No blanket suppression.
- Cookies ignore ports: running this alongside a sibling stack may clash on Authentik's `localhost` cookies. Document it.

## Tasks
Implementation uses Sonnet subagents with test-first work; I review each phase. First, create `feat/aspire-local-stack` from `main`, save this design to `docs/superpowers/specs/2026-11-26-aspire-local-stack-design.md`, and expand the tasks with `superpowers:writing-plans`.

0. **Spikes** (throwaway; record findings): (a) Authentik on a fresh Postgres 17: `sub` exact in ID token, access token and userinfo; `email`, `name`, `groups`, `aud`; refresh; blueprint re-apply; cold-start gate. (b) Aspire in this repo: restore and audit, Linux Release model tests without the Aspire CLI, concurrent first build (GitVersion, libman, Sass), Debug config reaching the apps, OTLP, real network name. (c) After a and b: browser sign-in, refresh, sign-out and Api token acceptance.
1. Webhook capture, Worker guard and tests (Debug and Release). Independent of the spikes.
2. Catcher and the LocalStack test project.
3. Packages, AppHost skeleton, ports, `.dockerignore`.
4. Postgres, Mailpit, Authentik and blueprint tests.
5. Four launch profiles, host wiring, catcher, seed switch, sample resource.
6. Reset script and Pester test.
7. Smoke test.
8. New `docs/development/LOCAL-STACK.md` (prerequisites: Docker, Aspire CLI, `dotnet dev-certs https --trust`; logins, ports, capture, reset, manual checklist), decision D-059 (verify it's still the next number), README, CONTRIBUTING, DEV-DATA, AUTHENTIK.

## Verification
- `dotnet test --solution TechStrap.CI.slnf -c Release`; Debug run of the capture tests; `pwsh ./scripts/Invoke-ScriptTests.ps1`; `pwsh ./scripts/Check-PackageVersions.ps1`; the CI vulnerability check.
- A Docker image build still works, and `docker compose up -d --build` is unaffected (its ports don't overlap).
- From a reset, cold `aspire run`: all resources green. Sam and Riley sign in as the **existing** seeded agents (no duplicates) with the right roles; Casey gets no access; sign-out ends the Authentik session.
- A ticket's email shows in Mailpit; a saved webhook test appears in the catcher; the sample resource files a ticket; telemetry shows in the dashboard; the KB images load.
- A stray `.env` or `.env.local` has no effect; a restart creates no duplicate data; `Reset-LocalStack.ps1 -WhatIf` lists only this stack's resources; the smoke test passes.
