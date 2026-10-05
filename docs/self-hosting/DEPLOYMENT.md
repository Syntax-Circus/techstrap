# Deployment: UAT and production

The deployment stack runs the Api, the Worker, the Admin and the Portal from explicit image references (D-043). Postgres, the identity provider and the reverse proxy are provisioned separately.
The root `docker-compose.yml` is the local development stack and builds from source; deployment commands MUST name `-f deploy/docker-compose.yml`.

**Tooling.** The deploy compose needs Docker Compose 2.30 or newer: the scoped env files use `format: raw` (2.30), and `gw_priority` on the project network needs a recent Compose too.
It was tested here with Compose v5.5.1 and Docker Engine 29.8.1; treat that pair as known-good.

## Configuration layout

Each project has its own `appsettings.json`, `appsettings.Development.json` (local overrides only) and `.env.example` under `src/TechStrap.Api`, `src/TechStrap.Worker`, `src/TechStrap.Admin` and `src/TechStrap.Portal`.
`appsettings.json` lists every setting the host reads: real, non-secret defaults, and blank for a secret or an environment-specific value. The `.env.example` documents the same keys as `SECTION__KEY`.

- **Development:** copy a project's `.env.example` to `.env.local` beside it. `SyntaxCircus.DotEnv` loads `.env` then `.env.local` in Development only.
- **Deployment, app settings:** copy each `deploy/.env.<app>.example` to `.env.<app>` in a host directory such as `/etc/techstrap/uat` or `/etc/techstrap/production`
  (`TECHSTRAP_ENV_DIR`). The four files (`.env.api`, `.env.worker`, `.env.admin`, `.env.portal`) are root-owned, mode 0600, loaded per service through `env_file` and never baked into an image.
  A container sees only its own file. The templates list the same keys as the project's `appsettings.json`, minus the few keys compose owns; each section header names the containers that read the key.
- **Deployment, compose inputs:** copy `deploy/.env.uat.example` to `deploy/.env.uat.local` (or the production example to `deploy/.env.production.local`, both git-ignored) and set the project name, the four image
  references, `TECHSTRAP_ENV_DIR`, the loopback ports, the subnet, `REVERSE_PROXY_CIDR` and `TECHSTRAP_DB_NETWORK`. These are compose inputs, not secrets. The two templates set the same variable names.

Compose interpolates only the inputs file. The per-service files are read with `format: raw`: write `KEY=value` with no quotes and no inline `# comment` (a `#` after a value becomes part of the value). `$` is literal.

**The project name is required and must differ per environment.** `TECHSTRAP_PROJECT` has no default: `techstrap-uat` for UAT, `techstrap` for production. Compose derives the container and volume names from it.
If UAT and production shared one project name, a UAT deploy would replace the production containers and reuse production's volumes (attachments, cookie keys). Compose refuses to resolve while it is unset.

**Image references.** The four `TECHSTRAP_*_IMAGE` values are explicit and never `latest`, and all four carry the same tag (one release, one tag).

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

   If UAT and production share one Docker host and one db network, their Api and Worker containers can reach each other on that network. Use a separate db network per environment
   (a different `TECHSTRAP_DB_NETWORK`) where the Postgres layout allows it.

2. **Postgres password.** It sits inside a connection string, so use only letters, digits and `- _ . ~`, for example `openssl rand -hex 24`. A semicolon, equals sign, quote or space breaks the string.
   Set `ConnectionStrings__TechStrap` in `.env.api` and in `.env.worker` to `Host=<postgres host name on that network>;Port=5432;Database=techstrap;Username=techstrap;Password=<password>`.
   The Api and the Worker refuse to start outside Development while it is blank.
3. **Env files.**

   ```bash
   sudo install -d -m 0700 /etc/techstrap/uat
   for app in api worker admin portal; do
     sudo install -m 0600 -o root -g root deploy/.env.$app.example /etc/techstrap/uat/.env.$app
   done
   sudoedit /etc/techstrap/uat/.env.api    # and .env.worker, .env.admin, .env.portal
   ```

   Fill in the blanks. Each host refuses to start while a required value is blank: the container stops and its log names that key.

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

   Edit it: set `TECHSTRAP_PROJECT` (`techstrap-uat`; production uses `techstrap`), pin the four `TECHSTRAP_*_IMAGE` values to one release tag (never `latest`), set `TECHSTRAP_ENV_DIR`, `TECHSTRAP_DB_NETWORK` and `REVERSE_PROXY_CIDR`.
   With loopback-only ports and a proxy on the same host, the proxy appears as the compose gateway (`172.16.31.1/32`); a proxy on another machine needs its own address (a single address, never a wide range such as `172.16.0.0/12` or `0.0.0.0/0`).
   UAT and production on one host need different subnets and ports; change `TECHSTRAP_SUBNET` only after checking the subnet registry in the `_template` `CLIENT_IP_RATE_LIMITING.md`.
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

## First deploy: verify the client IP path

The Api and the Worker sit on the project default network with `gw_priority: 1`, and also on the external db network. Docker chooses the default gateway by network name, and the db network can sort first;
`gw_priority: 1` makes the project network the gateway, so a published-port request reaches the Api from the trusted subnet's gateway and the client IP survives the proxy (the Api honours `X-Forwarded-For` only from a trusted peer).
The compose file and the tests pin this, but only a real Linux host proves it. At the first UAT deploy:

```bash
docker inspect -f '{{json .NetworkSettings.Networks}}' <project>-api-1
```

1. Confirm the Api's gateway is on the project network (`<project>_default`, the pinned `TECHSTRAP_SUBNET`), not on the db network.
2. Send a request through the reverse proxy to the published Api port and confirm it reaches the Api from the trusted subnet's gateway (the address in `REVERSE_PROXY_CIDR`), so the real client address, not the proxy's, is what the Api sees.

If either check fails, do not go live: the Api would ignore the forwarded client address.

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

Record the image references and take a database backup and a copy of the `admin-keys` volume before a rollout. To roll back the application, set the previous tags in `deploy/.env.<env>.local` (the same tag in all four images), then run `pull` and `up -d --wait` again.
The Api's migrations only move forward: rolling back past a migration needs a database restore, so restore from backup as a separate, deliberate recovery decision. To upgrade, set the new tags and run the same two commands.
Do not use `down --volumes`: the `techstrap-storage`, `admin-keys` and `portal-keys` volumes hold attachments and the cookie keys (deleting a key volume signs every agent out).

## Checking without deploying

`pwsh scripts/Test-ComposeSmoke.ps1 -DeployComposeOnly` resolves `deploy/docker-compose.yml` with the UAT template and dummy env files (`config` only: it never pulls an image and never starts the stack, which needs real images and an external Postgres).
`pwsh scripts/Invoke-ScriptTests.ps1` runs the config-contract and compose suites.
