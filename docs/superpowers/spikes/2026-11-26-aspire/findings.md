# Aspire local stack: spike findings (2026-11-26)

Throwaway spikes for Task 0 of `../../plans/2026-11-26-aspire-local-stack.md`. **Where this file and the spec or plan differ, this file wins.** `authentik-blueprint.yaml` beside it is the verified blueprint. No shipped code was changed.

Machine: Windows 11, Docker, .NET SDK 10.0.403, Aspire CLI 17.0.0, trusted .NET dev certificate already present. "Verified" means it was run in a spike.

## Changes to the design

1. **`sub_mode: user_upn` works.** No `sub` scope mapping, https for Authentik, Development-only setting or shipped-code change is needed.
2. **Restarting the Authentik worker does not re-apply an unchanged blueprint.** It re-applies after the file changes and the worker restarts; `ak apply_blueprint custom/techstrap.yaml` also works. Inference, not tested: changing only a `TS_*` value leaves the file hash unchanged, so a restart will not apply it. Do not document that a restart picks up changed secrets; a secrets reset must also remove the Authentik database.
3. **No transitive-pinning opt-out.** The AppHost resolves `Microsoft.Extensions.*` 10.0.15. Do not copy the sibling reference implementation's `CentralPackageTransitivePinningEnabled` property.
4. **Telemetry appears under two names.** Traces and spans: `techstrap-api`, `techstrap-admin`, `techstrap-portal`, `techstrap-worker`. Structured logs: `api`, `admin`, `portal`, `worker`. Document it. Setting `OpenTelemetry__ServiceName` might unify them; not tried.
5. **Network name (Task 6):** `aspire-persistent-network-<8 hex>-TechStrap`. Match the whole name. The compose network `techstrap_default` and volumes `techstrap_*` and `techstrap-smoke_*` exist on this machine and must never match. The container and network remain after `aspire stop`.
6. **Generated secrets (Task 6)** are under `~/.aspire/secrets/<UserSecretsId>/`.
7. **Build:** a hidden resource, `__dotnet-project-build`, builds the apps in Debug with `BuildInParallel=true`. CI uses `-m:1` because of a GitVersion mutex race on Linux; the parallel build was not tried on Linux.
8. A fresh database logs one `ERR` line from the Api (EF Core querying the missing `__EFMigrationsHistory`). Migration then succeeds. Not a stack fault.

## 0a. Authentik 2026.8.5 on Postgres 17 (empty volumes, no Redis)

All verified, with a script running authorization code + PKCE through the flow executor API:

- The blueprint applies; three users, one provider.
- `sub` is exactly `dev|sam` / `dev|riley` in the ID token, access token and userinfo, before and after refresh.
- The access token has `email`, `name`, `groups`, `aud=techstrap`, `azp=techstrap`, `iss=http://localhost:55290/application/o/techstrap/`, RS256, 10-minute lifetime, `email_verified=false`. Refresh rotates the refresh token and keeps the claims.
- `casey` gets `groups=[]` and a hashed `sub`.
- Server `/-/health/ready/` turns 200 after about 60 s while the `techstrap` discovery document stays 404 until the worker has run. So server health needs both checks, and the worker must wait only for the server to **start** (waiting for healthy deadlocks).
- In the image source, `user_upn` is `user.attributes.get("upn", user.uid)`; the built-in `profile` mapping emits `groups`.

## 0b. Aspire 17.0.0 in this repo (scratch worktree)

| Check | Result |
|---|---|
| Restore with NuGet audit (`all`, `low`, warnings as errors) and the CI vulnerability list | Verified: no problems across 24 projects |
| `Mvc.Testing` 10.0.13 with Aspire in one test project (it had a `Microsoft.AspNetCore.App` framework reference) | Verified |
| Release model test without the Aspire CLI or DCP on PATH | Verified on Windows and in a Linux SDK 10.0.401 container. Does not prove the DCP NuGet package is unused |
| Clean concurrent first build (GitVersion, libman, Sass) | Verified on Windows, including 5/5 repeats. Cold libman cache and Linux not tested |
| `AddDotnetProject` builds in Debug | Verified, so the Debug-only capture is compiled in |
| OTLP through `OpenTelemetry__*` | Verified for logs, traces and spans; metrics not checked |
| Persistent network name | `aspire-persistent-network-d66cd33d-TechStrap` for the worktree's AppHost; the real path's hash may differ |
| Listening ports | Apps listen on random ports behind Aspire's fixed-port proxy |

Not verified: `dotnet build TechStrap.CI.slnf -c Release -m:1` with the new projects; `Check-PackageVersions.ps1` with new map rows; default Aspire container names (sibling stacks suggest `postgres-<same hex as the network>`; Task 6 confirms).

## 0c. Browser sign-in with the unmodified Api and Admin

Debug builds, environment variables only (`DotEnv__Enabled=false`, Task 5's values), `https://localhost:55230` and `:55240`, **headless Chromium** (certificate errors not ignored).

- Verified: sign-in with the `form_post` callback; Sam and Riley are the seeded agents (exactly two agent rows); Casey sees "You don't have access to TechStrap" and gets no row; live updates connect; the Api accepted tokens past the first token's expiry; sign-out ends the Authentik session and the next sign-in asks for credentials; no CSP or app cookie errors.
- Not tested: a refresh first triggered by a full HTTP request, an Admin restart mid-session, headed Chrome, Edge or Firefox (keep the manual check in Task 8), and the shared-`localhost` cookie clash with a sibling stack.

## Cleanup

Spike containers, volumes, network, worktree, processes and user-secrets folder were removed; the machine's container, volume and network lists matched the pre-spike lists.
