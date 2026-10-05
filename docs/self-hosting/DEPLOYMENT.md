# Deployment: UAT and production

The deployment stack runs the Api, the Worker, the Admin and the Portal from explicit image references (D-043). Postgres, the identity provider and the reverse proxy are provisioned separately.
The root `docker-compose.yml` is the local development stack and builds from source; deployment commands MUST name `-f deploy/docker-compose.yml`.

**Tooling.** The deploy compose needs Compose 2.33.1 or later and Docker Engine 28 or later: `gw_priority` on a service network needs both, and the scoped env files use `format: raw` (Compose 2.30).
It was tested here with Compose v5.5.1 and Engine 29.8.1; treat that pair as known-good. Check the host first:

```bash
sudo docker compose version
sudo docker version --format '{{.Server.Version}}'
```

**Run as root.** Every deploy compose command below runs as root (`sudo docker compose ...`, for config, pull, up, ps and logs): the env files are root-owned, mode 0600, in a 0700 directory, and Compose reads `env_file` as the user who invokes it, so a docker-group user would get permission denied.

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

1. **Postgres.** Run Postgres 17 as its own instance. As a Postgres superuser on that instance, create the role and the database (generate the password first, see step 2):

   ```sql
   CREATE ROLE techstrap LOGIN PASSWORD '<hex-password>';
   CREATE DATABASE techstrap OWNER techstrap;
   ```

   The Api applies migrations at start, and the first migration runs `CREATE EXTENSION citext`. On Postgres 15 and later the `public` schema is not writable by roles that do not own it,
   so a role that only has connect rights cannot create tables there. The database owner can both create objects in `public` and create `citext`, which is a trusted extension, so no superuser is needed at migration time.

   Attach the Postgres container to a Docker network that exists before TechStrap starts, and give that network a stable name. The Api and the Worker join it; the Admin and the Portal never do. Create the network, then put Postgres on it:

   ```bash
   sudo docker network create techstrap-db                         # or the name you set in TECHSTRAP_DB_NETWORK
   sudo docker network connect techstrap-db <postgres container>   # for a running container
   ```

   Or, in the Postgres compose file, give the Postgres service `networks: { techstrap-db: { external: true } }`. If the Postgres compose creates the network itself, Compose names it
   `<project>_<network>` unless the network sets `name:`; `TECHSTRAP_DB_NETWORK` must be the real Docker network name (`docker network ls`).

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
   | `.env.api` | `ConnectionStrings__TechStrap`, `AUTHENTICATION__JWTBEARER__AUTHORITY`, `AUTHENTICATION__JWTBEARER__AUDIENCES__0` (present and blank in the template: fill it), `TECHSTRAP_PORTAL_PUBLIC_URL`, `TECHSTRAP_API_PUBLIC_URL` | The Api trusts the compose subnet and `REVERSE_PROXY_CIDR`. `TECHSTRAP_ADMIN_PUBLIC_URL` is optional. `TECHSTRAP_API_PUBLIC_URL` is the Api address as portal readers reach it (D-044): knowledge-base images load from `{url}/kb-images/{name}`, so the reverse proxy must route `/kb-images/` to the Api and the Api refuses to start without the setting. |
   | `.env.worker` | `ConnectionStrings__TechStrap`, `EMAIL__SMTP__HOST`, `EMAIL__SMTP__DEFAULTFROM` | To run without email set `EMAILOUTBOX__ENABLED=false`. Keep `TECHSTRAP_AUTOCLOSE_DAYS` equal to the Api value. |
   | `.env.admin` | `AUTH__AUTHORITY` (https), `AUTH__CLIENTID`, `AUTH__CLIENTSECRET` | See [ADMIN-APP.md](../development/ADMIN-APP.md) and [AGENT-AUTHENTICATION.md](AGENT-AUTHENTICATION.md). The group keys must match the Api. Optional: `TECHSTRAP_PORTAL_PUBLIC_URL` (the same value as in `.env.api`), which turns on the "View on portal" link of a published article; blank hides the link. A public URL with a path prefix needs the reverse proxy to strip that prefix before it reaches the portal. |
   | `.env.portal` | none yet | PHASE-09 adds the Api address and the public URL. |

   Set `ALLOWEDHOSTS` in each file to the real public host names if you want host filtering (keep the health-probe hosts `localhost`). Sentry and OpenTelemetry are optional and disabled by default; their DSN and OTLP headers are secrets.
4. **Compose inputs.**

   ```bash
   cp deploy/.env.uat.example deploy/.env.uat.local
   ```

   Edit it: set `TECHSTRAP_PROJECT` (`techstrap-uat`; production uses `techstrap`), pin the four `TECHSTRAP_*_IMAGE` values to one release tag (never `latest`), set `TECHSTRAP_ENV_DIR`, `TECHSTRAP_DB_NETWORK` and `REVERSE_PROXY_CIDR`.
   With loopback-only ports and a proxy on the same host, the proxy appears as the compose gateway (`172.16.31.1/32`); a proxy on another machine needs its own address (a single address, never a wide range such as `172.16.0.0/12` or `0.0.0.0/0`).
   UAT and production on one host need different subnets and ports; change `TECHSTRAP_SUBNET` only after checking the subnet registry in the `_template` `CLIENT_IP_RATE_LIMITING.md`.
   `REVERSE_PROXY_CIDR` (the subnet's `.1` address as `/32`) changes together with `TECHSTRAP_SUBNET`.
5. **Registry login.** The images are public or private on GHCR. If private, log in as root, because every compose command runs under `sudo`:

   ```bash
   echo <token> | sudo docker login ghcr.io -u <user> --password-stdin
   ```

   `pull_policy: always` pulls with root's credentials in `/root/.docker/config.json`; a login made as your own user is not used.
6. **Release checkout.** On the host, check out the git tag that matches the image tag (for example `git checkout v1.2.3` for images tagged `1.2.3`), so `deploy/docker-compose.yml` and the `deploy/.env.*.example` templates match the images you run.

## Deploy

```bash
# UAT, after the env files and the inputs are in place:
sudo docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml config --quiet
sudo docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml pull
sudo docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml up -d --wait
sudo docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml ps

# Production: the same four commands with deploy/.env.production.local.
```

`config --quiet` fails with a message naming the missing input or the missing env file (for example `.env.worker`), before anything is pulled. The compose starts the Api first; the Admin, the Portal and the Worker wait for a healthy Api.
`restart: unless-stopped` is set, and each container has a `curl` health check.

**No guard on `latest` or mixed tags.** The deploy compose cannot refuse `:latest` or four different tags in your `.local` file: it only requires the four values to be set. Set one explicit tag on all four images. A pre-flight check:

```bash
grep -E '^TECHSTRAP_(API|WORKER|ADMIN|PORTAL)_IMAGE=' deploy/.env.uat.local
grep -E '^TECHSTRAP_.*_IMAGE=.*:latest' deploy/.env.uat.local && echo "latest is not allowed"
grep -E '^TECHSTRAP_.*_IMAGE=' deploy/.env.uat.local | sed 's/.*://' | sort -u | wc -l   # must print 1
```

**Migrations.** The Api migrates the database at start under an advisory lock when `DATABASE__MIGRATEONSTARTUP` is `true` (the default and the template value). The Worker never migrates. Set it to `false` in `.env.api` to migrate by another route.

## First deploy: verify the client IP path

The Api and the Worker sit on the project default network with `gw_priority: 1`, and also on the external db network. Docker chooses the default gateway by network name, and the db network can sort first;
`gw_priority: 1` makes the project network the gateway, so a published-port request reaches the Api from the trusted subnet's gateway and the client IP survives the proxy (the Api honours `X-Forwarded-For` only from a trusted peer).
The compose file and the tests pin this, but only a real Linux host proves it. At the first UAT deploy:

```bash
sudo docker inspect -f '{{json .NetworkSettings.Networks}}' <project>-api-1
```

Use root throughout. Both checks read the container's own kernel tables, because the images have `curl` but no `ss`.

1. **Gateway.** The default route must go through the project network's gateway, not the db network's:

   ```bash
   sudo docker network inspect <project>_default --format '{{(index .IPAM.Config 0).Gateway}}'
   sudo docker exec <project>-api-1 cat /proc/net/route
   ```

   In `/proc/net/route` the `Gateway` column of the row whose `Destination` is `00000000` is little-endian hex. For the example subnet, `172.16.31.1` is `AC.10.1F.01` read backwards: `011F10AC`.
   `011F10AC` means the project network won. Any other value means the db network won, which is a failure.
2. **Peer address.** Hold a connection to the published port open and read the container's TCP table while it is open:

   ```bash
   exec 3<>/dev/tcp/127.0.0.1/<api port>
   sudo docker exec <project>-api-1 cat /proc/net/tcp
   exec 3>&-
   ```

   Find the ESTABLISHED row (`st` is `01`) whose local port is `0050` (port 80). Its remote address must be the project gateway in hex (`011F10AC`:`xxxx` for the example), which lies inside `REVERSE_PROXY_CIDR`.

This proves the Docker path only. The proxy's `X-Forwarded-For` handling has no observation point short of a temporary debug log; add one if you need to see it.
If either check fails, do not go live: the Api would ignore the forwarded client address.

## Health checks and acceptance

```bash
curl --fail http://localhost:<api port>/health/ready     # Api: Postgres reachable and migrated
curl --fail http://localhost:<admin port>/health/live
curl --fail http://localhost:<portal port>/health/live
sudo docker compose --env-file deploy/.env.uat.local -f deploy/docker-compose.yml ps   # every service healthy
```

Also check that the ports are bound to `127.0.0.1` only (`sudo docker compose ... ps`), that the Admin sign-in redirects to the provider and back, and that a test ticket reaches the queue and sends its email.
The Worker has no published port: its health is the container health (`/health/ready`).

## Rollback and upgrade

Record the image references before a rollout, and take a Postgres backup together with a copy of the storage volume and both key-ring volumes (`<project>_techstrap-storage`, `<project>_admin-keys`, `<project>_portal-keys`; list them with `sudo docker volume ls --filter name=<project>_`). To roll back the application, set the previous tags in `deploy/.env.<env>.local` (the same tag in all four images), then run `pull` and `up -d --wait` again.
The Api's migrations only move forward: rolling back past a migration needs a database restore, so restore from backup as a separate, deliberate recovery decision. To upgrade, set the new tags and run the same two commands.
Backup examples (run from a backup directory; repeat the tar for `techstrap-storage` and `portal-keys`):

```bash
# Postgres (from any host that can reach it):
pg_dump -h <postgres host> -U techstrap -Fc techstrap > techstrap-$(date +%F).dump
# A named volume, read-only:
sudo docker run --rm -v <project>_admin-keys:/data:ro -v "$PWD":/backup alpine tar czf /backup/admin-keys.tgz -C /data .
sudo docker run --rm -v <project>_techstrap-storage:/data:ro -v "$PWD":/backup alpine tar czf /backup/techstrap-storage.tgz -C /data .
sudo docker run --rm -v <project>_portal-keys:/data:ro -v "$PWD":/backup alpine tar czf /backup/portal-keys.tgz -C /data .
```

Do not use `down --volumes`: those three volumes hold attachments and the cookie keys (deleting a key volume signs every agent out).

## Checking without deploying

`pwsh scripts/Test-ComposeSmoke.ps1 -DeployComposeOnly` resolves `deploy/docker-compose.yml` with the UAT template and dummy env files (`config` only: it never pulls an image and never starts the stack, which needs real images and an external Postgres).
`pwsh scripts/Invoke-ScriptTests.ps1` runs the config-contract and compose suites.
