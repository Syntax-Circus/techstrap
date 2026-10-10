# Self-hosting TechStrap

This guide is for an operator who runs TechStrap with their own OpenID Connect (OIDC) provider, SMTP relay and reverse proxy. It is provider-neutral; [AUTHENTIK.md](AUTHENTIK.md) is a worked example. The step-by-step first deploy (host setup, env files, the four compose commands) is in [DEPLOYMENT.md](DEPLOYMENT.md); this guide is the reference you keep open next to it.

## Overview and requirements

Four containers make up a TechStrap installation:

- **Api**: the REST API and the live-update hub. It owns the database and runs the migrations.
- **Worker**: sends the email outbox, closes Solved tickets and prunes old outbox rows.
- **Admin**: the agent app (Blazor). Agents sign in here through your OIDC provider.
- **Portal**: the public customer portal (Blazor): web forms, the knowledge base and the private ticket links.

You provide:

- Docker Engine 28 or later with Docker Compose 2.33.1 or later (see the tooling note in [DEPLOYMENT.md](DEPLOYMENT.md)).
- An external PostgreSQL 17 with a role, a database and an owner all named `techstrap`. Compose does not run Postgres.
- An OIDC provider (see [OIDC requirements](#oidc-requirements)).
- An SMTP relay for outbound email (see [SMTP](#smtp)), unless you run without email.
- A reverse proxy that terminates TLS and forwards to the loopback ports (see [Reverse proxy](#reverse-proxy)).

The images are tagged by release (`ghcr.io/syntax-circus/techstrap-<app>:<tag>`, one tag for all four) and the project is pre-1.0: pin an explicit `0.x` tag, never `latest`.

## Compose layout

`deploy/docker-compose.yml` runs four services, `api`, `admin`, `portal` and `worker`, each from `${TECHSTRAP_<APP>_IMAGE}` and each with `env_file: ${TECHSTRAP_ENV_DIR}/.env.<app>`. The per-app files hold the settings and secrets; a separate file, `deploy/.env.uat.local` or `deploy/.env.production.local`, holds the compose inputs (see the last table in the [Environment reference](#environment-reference)).

- **Ports.** The Api, Admin and Portal publish `127.0.0.1:${TECHSTRAP_{API,ADMIN,PORTAL}_PORT}:80`, on loopback only. The templates use 18080 to 18082 for UAT and 8080 to 8082 for production. The Worker publishes nothing.
- **Networks.** All services join a default project network on the pinned `${TECHSTRAP_SUBNET}`. The Api and the Worker also join the external network named by `${TECHSTRAP_DB_NETWORK}` (`techstrap-db`) to reach Postgres; create it once with `docker network create techstrap-db`.
- **No Postgres service.** The database is yours to provide, run and back up.
- **Volumes.** `techstrap-storage`, `admin-keys` and `portal-keys` (see [Volumes](#volumes)).

## OIDC requirements

TechStrap works with any provider that satisfies this contract:

- **Authority.** A discoverable issuer URL. It must be https outside Development. The same issuer goes in `AUTH__AUTHORITY` (Admin) and `AUTHENTICATION__JWTBEARER__AUTHORITY` (Api), exactly as the provider publishes it.
- **Client.** The Admin is a **confidential** client using the authorization code flow with **PKCE**. Create one client; its client id and secret go in `AUTH__CLIENTID` and `AUTH__CLIENTSECRET`.
- **Scopes.** `openid profile email offline_access`.
- **Redirect URI.** `https://<admin-host>/signin-oidc`.
- **Post-logout redirect URI.** `https://<admin-host>/signout-callback-oidc`.
- **Api token validation.** The Api validates the access token with `Authentication:JwtBearer` Authority and Audiences. The audience is the Admin client id, so `AUTHENTICATION__JWTBEARER__AUDIENCES__0` equals `AUTH__CLIENTID`.
- **Claims.** The access token must carry `sub`, `email` and the groups claim. The groups claim is `groups` by default; set `TECHSTRAP_GROUP_CLAIM_TYPE` if your provider names it differently.
- **Groups.** Two provider groups decide access: `TECHSTRAP_AGENT_GROUP` (agents) and `TECHSTRAP_ADMIN_GROUP` (administrators). They must differ, and the three group keys (`TECHSTRAP_AGENT_GROUP`, `TECHSTRAP_ADMIN_GROUP`, `TECHSTRAP_GROUP_CLAIM_TYPE`) must have identical values in `.env.api` and `.env.admin`; the Api decides who has access.
- **Email.** An agent whose token has no email is refused (`agent-email-required`).
- **Session.** The Admin cookie `techstrap.admin` slides for 8 hours; the refresh token (`offline_access`) renews the access token.

The worked example for Authentik is [AUTHENTIK.md](AUTHENTIK.md).

## Environment reference

The source of truth is the `deploy/.env.<app>.example` templates and, for which keys the operator must fill, the required-key lists pinned by `ProductionBlankTemplateTests`. A Pester test (`scripts/tests/SelfHostDocs.Tests.ps1`) fails when a table below and its template disagree. Each template is key-only: copy it to `${TECHSTRAP_ENV_DIR}/.env.<app>` (root-owned, mode 0600), fill the required keys and leave the rest at their defaults.

Notes for reading the tables:

- **Required** is `yes` only for the keys the operator must fill; a blank required value stops the container at start and its log names the key. Everything else, including keys the templates ship commented out (`# KEY=`), is `no`.
- Secrets show `<set by operator>`; never commit a filled env file.
- The files are read raw: no quotes, no `$` interpolation, no inline comments.
- Compose owns and overrides some keys, so they are not in the per-app tables: `ASPNETCORE_ENVIRONMENT`, `DOTENV__ENABLED`, `STORAGE__LOCAL__ROOTPATH` (Api), `API__BASEURL` and `DATAPROTECTION__KEYRINGPATH` (Admin and Portal), and `TRUSTEDPROXY__TRUSTEDNETWORKS__<n>`. The Api trusts `TECHSTRAP_SUBNET` (`__0`) and `REVERSE_PROXY_CIDR` (`__1`); the Admin and the Portal trust `REVERSE_PROXY_CIDR` (`__0`).
- Keys that appear in several files (the group keys, `TECHSTRAP_PORTAL_PUBLIC_URL`, `TECHSTRAP_AUTOCLOSE_DAYS`, `TECHSTRAP_PORTAL_SHOW_POWERED_BY`, logging and security headers) must carry the same value everywhere they are set.

### .env.api

| Key | Required | Default or example | Meaning |
| --- | --- | --- | --- |
| `ConnectionStrings__TechStrap` | yes | `<set by operator>` | Npgsql connection string, for example `Host=postgres;Port=5432;Database=techstrap;Username=techstrap;Password=<password>`. Password characters: letters, digits and - _ . ~ only. |
| `DATABASE__MIGRATEONSTARTUP` | no | `true` | The Api migrates the database on startup under an advisory lock; false skips it. The Worker never migrates. |
| `AUTHENTICATION__JWTBEARER__AUTHORITY` | yes | `https://<issuer>` | Issuer URL of the OIDC provider; the Api refuses to start without it. |
| `AUTHENTICATION__JWTBEARER__AUDIENCES__0` | yes | `<admin client id>` | Expected token audience: the Admin client id. Present and blank in the template; it must be filled. |
| `AUTHENTICATION__JWTBEARER__REQUIREHTTPSMETADATA` | no | `true` | Require https for the provider metadata. Leave true outside local development. |
| `TECHSTRAP_AGENT_GROUP` | no | `techstrap-agents` | IdP group whose members are agents. Same value in .env.api and .env.admin; must differ from the admin group. |
| `TECHSTRAP_ADMIN_GROUP` | no | `techstrap-admins` | IdP group whose members are administrators. Same value in .env.api and .env.admin. |
| `TECHSTRAP_GROUP_CLAIM_TYPE` | no | `groups` | Name of the token claim that carries group names. |
| `RATELIMITING__PUBLIC__PERMITLIMIT` | no | `120` | Public read endpoints: requests per window per client IP. |
| `RATELIMITING__PUBLIC__WINDOWSECONDS` | no | `60` | Public read endpoints: window in seconds. |
| `RATELIMITING__INTAKE__WEBFORMPERMITLIMIT` | no | `5` | Web form submissions per window per client IP. |
| `RATELIMITING__INTAKE__WEBFORMWINDOWSECONDS` | no | `600` | Web form window in seconds. |
| `RATELIMITING__INTAKE__PUBLICKEYPERMITLIMIT` | no | `10` | Intake with a public key: requests per window. |
| `RATELIMITING__INTAKE__PUBLICKEYWINDOWSECONDS` | no | `60` | Public-key intake window in seconds. |
| `RATELIMITING__INTAKE__TRUSTEDKEYPERMITLIMIT` | no | `120` | Intake with a trusted key: requests per window. |
| `RATELIMITING__INTAKE__TRUSTEDKEYWINDOWSECONDS` | no | `60` | Trusted-key intake window in seconds. |
| `RATELIMITING__CUSTOMER__TOKENACCESSPERMITLIMIT` | no | `60` | Customer link (token) access: requests per window. |
| `RATELIMITING__CUSTOMER__TOKENACCESSWINDOWSECONDS` | no | `60` | Token access window in seconds. |
| `RATELIMITING__CUSTOMER__LOSTLINKPERMITLIMIT` | no | `5` | Lost-link requests per window. |
| `RATELIMITING__CUSTOMER__LOSTLINKWINDOWSECONDS` | no | `3600` | Lost-link window in seconds. |
| `LOSTLINK__MAXLINKS` | no | `5` | Links sent per lost-link email (1..10). |
| `LOSTLINK__PERADDRESSLIMIT` | no | `3` | Lost-link emails per address within the window (1..20). |
| `LOSTLINK__PERADDRESSWINDOWMINUTES` | no | `60` | Window for the per-address lost-link cap in minutes (1..1440). |
| `TECHSTRAP_PORTAL_PUBLIC_URL` | yes | `https://<host>` | Public address of the Portal as customers see it; absolute http or https, no query or fragment. Used for emailed links, canonical URLs and the sitemap. Same value in every file that sets it. |
| `TECHSTRAP_API_PUBLIC_URL` | yes | `https://<host>` | Public address of the Api as portal readers reach it; knowledge-base images load from `{url}/kb-images/{name}` and uploaded product logos from `{url}/product-logos/{name}`. The Api refuses to start without it. An Api behind plain http in Production shows no uploaded logo anywhere: the Portal and the email layout accept https logos only. |
| `TECHSTRAP_ADMIN_PUBLIC_URL` | no | (blank) | Admin base URL; when set, assignment emails link to `{url}/tickets/{number}`. |
| `TECHSTRAP_AUTOCLOSE_DAYS` | no | `7` | Days a Solved ticket stays open to a customer reply (1..365). Keep equal in the Api and the Worker. |
| `STORAGE__PROVIDER` | no | `Local` | Attachment storage provider; Local stores in the `techstrap-storage` volume. |
| `TRUSTEDPROXY__TRUSTEDPROXIES__0` | no | (blank) | Optional: also trust one proxy address. Compose already sets the trusted networks. |
| `TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION` | no | `true` | Refuse to run in production without a trusted proxy setting. Leave true. |
| `SECURITYHEADERS__REFERRERPOLICY` | no | `strict-origin-when-cross-origin` | Referrer-Policy response header. |
| `SECURITYHEADERS__FRAMEOPTIONS` | no | `DENY` | X-Frame-Options response header. |
| `SECURITYHEADERS__CONTENTTYPEOPTIONS` | no | `nosniff` | X-Content-Type-Options response header. |
| `SECURITYHEADERS__PERMISSIONSPOLICY` | no | `camera=(), geolocation=(), microphone=()` | Permissions-Policy response header. |
| `SECURITYHEADERS__STRICTTRANSPORTSECURITY` | no | `max-age=31536000; includeSubDomains` | Strict-Transport-Security (HSTS) response header. |
| `SECURITYHEADERS__ROBOTSTAG` | no | (blank) | Optional X-Robots-Tag value; blank sends none. |
| `SERILOG__MINIMUMLEVEL__DEFAULT` | no | `Information` | Minimum log level. |
| `ALLOWEDHOSTS` | no | `*` | Host filtering. `*` accepts any host behind the proxy; if narrowed, list every public host and keep `localhost` for the health probe. |
| `SENTRY__DSN` | no | `<set by operator>` | Sentry DSN (a secret). Blank disables Sentry. |
| `SENTRY__ENVIRONMENT` | no | (blank) | Sentry environment name. |
| `SENTRY__DEBUG` | no | `false` | Sentry SDK debug output. |
| `SENTRY__TRACESSAMPLERATE` | no | `0.0` | Sentry trace sample rate (0.0 to 1.0). |
| `OPENTELEMETRY__ENABLED` | no | `false` | Turn OpenTelemetry export on. |
| `OPENTELEMETRY__EXPORTLOGS` | no | `true` | Export logs when OpenTelemetry is enabled. |
| `OPENTELEMETRY__EXPORTTRACES` | no | `true` | Export traces when OpenTelemetry is enabled. |
| `OPENTELEMETRY__EXPORTMETRICS` | no | `true` | Export metrics when OpenTelemetry is enabled. |
| `OPENTELEMETRY__OTLPENDPOINT` | no | (blank) | OTLP collector endpoint. |
| `OPENTELEMETRY__OTLPPROTOCOL` | no | `grpc` | OTLP protocol, grpc or http/protobuf. |
| `OPENTELEMETRY__HEADERS` | no | `<set by operator>` | OTLP request headers (a secret). |
| `OPENTELEMETRY__TRACESSAMPLERATE` | no | `0.05` | OpenTelemetry trace sample rate (0.0 to 1.0). |
| `OPENTELEMETRY__SERVICENAME` | no | (blank) | Service name reported to the collector; blank uses the default. |
| `OPENTELEMETRY__SERVICEVERSION` | no | (blank) | Service version reported to the collector; blank uses the default. |
| `OPENTELEMETRY__ENVIRONMENT` | no | (blank) | Deployment environment reported to the collector. |

### .env.worker

| Key | Required | Default or example | Meaning |
| --- | --- | --- | --- |
| `ConnectionStrings__TechStrap` | yes | `<set by operator>` | Npgsql connection string, for example `Host=postgres;Port=5432;Database=techstrap;Username=techstrap;Password=<password>`. Password characters: letters, digits and - _ . ~ only. |
| `EMAIL__SMTP__HOST` | yes | `<smtp host>` | SMTP relay host. Required while the outbox is enabled. |
| `EMAIL__SMTP__PORT` | no | `587` | SMTP port. |
| `EMAIL__SMTP__USERNAME` | no | (blank) | SMTP user name; blank for none. |
| `EMAIL__SMTP__PASSWORD` | no | `<set by operator>` | SMTP password (a secret). |
| `EMAIL__SMTP__USESTARTTLS` | no | `true` | Legacy STARTTLS switch; TLSMODE is the explicit setting. |
| `EMAIL__SMTP__DEFAULTFROM` | yes | `helpdesk@example.com` | Sender address of outbound mail. Required while the outbox is enabled. |
| `EMAIL__SMTP__MAXRETRYATTEMPTS` | no | `1` | Send attempts after the first failure. |
| `EMAIL__SMTP__TLSMODE` | no | `StartTls` | One of None, Auto, StartTls, SslOnConnect or StartTlsWhenAvailable. |
| `EMAIL__SMTP__RETRYMODE` | no | `TransientOnly` | Retry only transient failures (TransientOnly). |
| `EMAIL__SMTP__TOTALSENDTIMEOUT` | no | `00:00:30` | Overall deadline for one send (hh:mm:ss). |
| `EMAILOUTBOX__ENABLED` | no | `true` | false runs the Worker without email. |
| `EMAILOUTBOX__POLLINTERVALSECONDS` | no | `5` | Seconds between outbox polls. |
| `EMAILOUTBOX__BATCHSIZE` | no | `20` | Messages per batch. |
| `EMAILOUTBOX__LEASESECONDS` | no | `900` | Lease for one batch; at least BATCHSIZE x send timeout seconds + 60 or the Worker fails to boot. |
| `EMAILOUTBOX__WORKERID` | no | (blank) | Worker identity; blank uses the machine name plus a random suffix. |
| `TECHSTRAP_API_PUBLIC_URL` | no | (blank) | The Api's public address, the same value as the Api's own. When set, emails show a product's uploaded logo; blank keeps the linked logo address. |
| `TECHSTRAP_PORTAL_SHOW_POWERED_BY` | no | `true` | Show the "Powered by TechStrap" mark on portal pages and customer email. Installation-wide; same value in .env.worker and .env.portal. |
| `TECHSTRAP_AUTOCLOSE_DAYS` | no | `7` | Days a Solved ticket stays open to a customer reply (1..365). Keep equal in the Api and the Worker. |
| `AUTOCLOSE__ENABLED` | no | `true` | Run the auto-close job. |
| `AUTOCLOSE__INTERVALMINUTES` | no | `15` | Minutes between auto-close runs. |
| `AUTOCLOSE__BATCHSIZE` | no | `50` | Tickets closed per run. |
| `OUTBOXRETENTION__ENABLED` | no | `true` | Delete old Sent and Discarded outbox rows; dead letters are kept. |
| `OUTBOXRETENTION__DAYS` | no | `90` | Age in days before deletion (1..3650). |
| `OUTBOXRETENTION__INTERVALMINUTES` | no | `60` | Minutes between retention runs. |
| `OUTBOXRETENTION__BATCHSIZE` | no | `500` | Rows deleted per run. |
| `SERILOG__MINIMUMLEVEL__DEFAULT` | no | `Information` | Minimum log level. |
| `ALLOWEDHOSTS` | no | `*` | Host filtering. `*` accepts any host behind the proxy; if narrowed, list every public host and keep `localhost` for the health probe. |
| `SENTRY__DSN` | no | `<set by operator>` | Sentry DSN (a secret). Blank disables Sentry. |
| `SENTRY__ENVIRONMENT` | no | (blank) | Sentry environment name. |
| `SENTRY__DEBUG` | no | `false` | Sentry SDK debug output. |
| `SENTRY__TRACESSAMPLERATE` | no | `0.0` | Sentry trace sample rate (0.0 to 1.0). |
| `OPENTELEMETRY__ENABLED` | no | `false` | Turn OpenTelemetry export on. |
| `OPENTELEMETRY__EXPORTLOGS` | no | `true` | Export logs when OpenTelemetry is enabled. |
| `OPENTELEMETRY__EXPORTTRACES` | no | `true` | Export traces when OpenTelemetry is enabled. |
| `OPENTELEMETRY__EXPORTMETRICS` | no | `true` | Export metrics when OpenTelemetry is enabled. |
| `OPENTELEMETRY__OTLPENDPOINT` | no | (blank) | OTLP collector endpoint. |
| `OPENTELEMETRY__OTLPPROTOCOL` | no | `grpc` | OTLP protocol, grpc or http/protobuf. |
| `OPENTELEMETRY__HEADERS` | no | `<set by operator>` | OTLP request headers (a secret). |
| `OPENTELEMETRY__TRACESSAMPLERATE` | no | `0.05` | OpenTelemetry trace sample rate (0.0 to 1.0). |
| `OPENTELEMETRY__SERVICENAME` | no | (blank) | Service name reported to the collector; blank uses the default. |
| `OPENTELEMETRY__SERVICEVERSION` | no | (blank) | Service version reported to the collector; blank uses the default. |
| `OPENTELEMETRY__ENVIRONMENT` | no | (blank) | Deployment environment reported to the collector. |

### .env.admin

| Key | Required | Default or example | Meaning |
| --- | --- | --- | --- |
| `API__TIMEOUTSECONDS` | no | `30` | Timeout for a request to the Api (1..300 seconds). |
| `LIVEUPDATES__ENABLED` | no | `true` | Kill switch for the live connection to the Api ticket hub; false draws no indicator, banner or presence bar. |
| `AUTH__AUTHORITY` | yes | `https://<issuer>` | OIDC issuer URL (https). The Admin refuses to start without it. |
| `AUTH__CLIENTID` | yes | `<client id>` | Client id of the confidential OIDC client; also the Api audience. |
| `AUTH__CLIENTSECRET` | yes | `<set by operator>` | Client secret (a secret). |
| `TECHSTRAP_AGENT_GROUP` | no | `techstrap-agents` | IdP group whose members are agents. Same value in .env.api and .env.admin; must differ from the admin group. |
| `TECHSTRAP_ADMIN_GROUP` | no | `techstrap-admins` | IdP group whose members are administrators. Same value in .env.api and .env.admin. |
| `TECHSTRAP_GROUP_CLAIM_TYPE` | no | `groups` | Name of the token claim that carries group names. |
| `TECHSTRAP_PORTAL_PUBLIC_URL` | no | `https://<host>` | Public address of the Portal as customers see it; absolute http or https, no query or fragment. Used for emailed links, canonical URLs and the sitemap. Same value in every file that sets it. |
| `TRUSTEDPROXY__TRUSTEDPROXIES__0` | no | (blank) | Optional: also trust one proxy address. Compose already sets the trusted networks. |
| `TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION` | no | `true` | Refuse to run in production without a trusted proxy setting. Leave true. |
| `SECURITYHEADERS__REFERRERPOLICY` | no | `strict-origin-when-cross-origin` | Referrer-Policy response header. |
| `SECURITYHEADERS__FRAMEOPTIONS` | no | `DENY` | X-Frame-Options response header. |
| `SECURITYHEADERS__CONTENTTYPEOPTIONS` | no | `nosniff` | X-Content-Type-Options response header. |
| `SECURITYHEADERS__PERMISSIONSPOLICY` | no | `camera=(), geolocation=(), microphone=()` | Permissions-Policy response header. |
| `SECURITYHEADERS__STRICTTRANSPORTSECURITY` | no | `max-age=31536000; includeSubDomains` | Strict-Transport-Security (HSTS) response header. |
| `SECURITYHEADERS__ROBOTSTAG` | no | (blank) | Optional X-Robots-Tag value; blank sends none. |
| `SERILOG__MINIMUMLEVEL__DEFAULT` | no | `Information` | Minimum log level. |
| `ALLOWEDHOSTS` | no | `*` | Host filtering. `*` accepts any host behind the proxy; if narrowed, list every public host and keep `localhost` for the health probe. |
| `SENTRY__DSN` | no | `<set by operator>` | Sentry DSN (a secret). Blank disables Sentry. |
| `SENTRY__ENVIRONMENT` | no | (blank) | Sentry environment name. |
| `SENTRY__DEBUG` | no | `false` | Sentry SDK debug output. |
| `SENTRY__TRACESSAMPLERATE` | no | `0.0` | Sentry trace sample rate (0.0 to 1.0). |
| `OPENTELEMETRY__ENABLED` | no | `false` | Turn OpenTelemetry export on. |
| `OPENTELEMETRY__EXPORTLOGS` | no | `true` | Export logs when OpenTelemetry is enabled. |
| `OPENTELEMETRY__EXPORTTRACES` | no | `true` | Export traces when OpenTelemetry is enabled. |
| `OPENTELEMETRY__EXPORTMETRICS` | no | `true` | Export metrics when OpenTelemetry is enabled. |
| `OPENTELEMETRY__OTLPENDPOINT` | no | (blank) | OTLP collector endpoint. |
| `OPENTELEMETRY__OTLPPROTOCOL` | no | `grpc` | OTLP protocol, grpc or http/protobuf. |
| `OPENTELEMETRY__HEADERS` | no | `<set by operator>` | OTLP request headers (a secret). |
| `OPENTELEMETRY__TRACESSAMPLERATE` | no | `0.05` | OpenTelemetry trace sample rate (0.0 to 1.0). |
| `OPENTELEMETRY__SERVICENAME` | no | (blank) | Service name reported to the collector; blank uses the default. |
| `OPENTELEMETRY__SERVICEVERSION` | no | (blank) | Service version reported to the collector; blank uses the default. |
| `OPENTELEMETRY__ENVIRONMENT` | no | (blank) | Deployment environment reported to the collector. |

### .env.portal

| Key | Required | Default or example | Meaning |
| --- | --- | --- | --- |
| `TECHSTRAP_PORTAL_PUBLIC_URL` | yes | `https://<host>` | Public address of the Portal as customers see it; absolute http or https, no query or fragment. Used for emailed links, canonical URLs and the sitemap. Same value in every file that sets it. |
| `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` | no | (blank) | Product key; when set the Portal root redirects to `/p/<key>`. |
| `TECHSTRAP_PORTAL_LANDING` | no | `Neutral` | `Neutral` shows the neutral root page; `Products` lists the products whose "Listed on the landing page" flag is on, as cards (name, logo, tagline). Not allowed together with `TECHSTRAP_PORTAL_DEFAULT_PRODUCT`. |
| `CANONICALHOST__CANONICALHOST` | no | (blank) | Host name (no scheme) that legacy hosts redirect to; blank turns the redirect off. |
| `CANONICALHOST__LEGACYHOSTS__0` | no | (blank) | Optional host to redirect; add more as __1, __2. |
| `CANONICALHOST__FORCEHTTPS` | no | `false` | Make the redirect target https. |
| `CANONICALHOST__PERMANENT` | no | `true` | true sends 301, false sends 302. |
| `TECHSTRAP_PORTAL_SHOW_POWERED_BY` | no | `true` | Show the "Powered by TechStrap" mark on portal pages and customer email. Installation-wide; same value in .env.worker and .env.portal. |
| `TRUSTEDPROXY__TRUSTEDPROXIES__0` | no | (blank) | Optional: also trust one proxy address. Compose already sets the trusted networks. |
| `TRUSTEDPROXY__REQUIRETRUSTEDPROXIESINPRODUCTION` | no | `true` | Refuse to run in production without a trusted proxy setting. Leave true. |
| `SECURITYHEADERS__REFERRERPOLICY` | no | `strict-origin-when-cross-origin` | Referrer-Policy response header. |
| `SECURITYHEADERS__FRAMEOPTIONS` | no | `DENY` | X-Frame-Options response header. |
| `SECURITYHEADERS__CONTENTTYPEOPTIONS` | no | `nosniff` | X-Content-Type-Options response header. |
| `SECURITYHEADERS__PERMISSIONSPOLICY` | no | `camera=(), geolocation=(), microphone=()` | Permissions-Policy response header. |
| `SECURITYHEADERS__STRICTTRANSPORTSECURITY` | no | `max-age=31536000; includeSubDomains` | Strict-Transport-Security (HSTS) response header. |
| `SECURITYHEADERS__ROBOTSTAG` | no | (blank) | Optional X-Robots-Tag value; blank sends none. |
| `SERILOG__MINIMUMLEVEL__DEFAULT` | no | `Information` | Minimum log level. |
| `ALLOWEDHOSTS` | no | `*` | Host filtering. `*` accepts any host behind the proxy; if narrowed, list every public host and keep `localhost` for the health probe. |
| `SENTRY__DSN` | no | `<set by operator>` | Sentry DSN (a secret). Blank disables Sentry. |
| `SENTRY__ENVIRONMENT` | no | (blank) | Sentry environment name. |
| `SENTRY__DEBUG` | no | `false` | Sentry SDK debug output. |
| `SENTRY__TRACESSAMPLERATE` | no | `0.0` | Sentry trace sample rate (0.0 to 1.0). |
| `OPENTELEMETRY__ENABLED` | no | `false` | Turn OpenTelemetry export on. |
| `OPENTELEMETRY__EXPORTLOGS` | no | `true` | Export logs when OpenTelemetry is enabled. |
| `OPENTELEMETRY__EXPORTTRACES` | no | `true` | Export traces when OpenTelemetry is enabled. |
| `OPENTELEMETRY__EXPORTMETRICS` | no | `true` | Export metrics when OpenTelemetry is enabled. |
| `OPENTELEMETRY__OTLPENDPOINT` | no | (blank) | OTLP collector endpoint. |
| `OPENTELEMETRY__OTLPPROTOCOL` | no | `grpc` | OTLP protocol, grpc or http/protobuf. |
| `OPENTELEMETRY__HEADERS` | no | `<set by operator>` | OTLP request headers (a secret). |
| `OPENTELEMETRY__TRACESSAMPLERATE` | no | `0.05` | OpenTelemetry trace sample rate (0.0 to 1.0). |
| `OPENTELEMETRY__SERVICENAME` | no | (blank) | Service name reported to the collector; blank uses the default. |
| `OPENTELEMETRY__SERVICEVERSION` | no | (blank) | Service version reported to the collector; blank uses the default. |
| `OPENTELEMETRY__ENVIRONMENT` | no | (blank) | Deployment environment reported to the collector. |

### Compose inputs (deploy/.env.uat.example and deploy/.env.production.example)

| Key | Required | Default or example | Meaning |
| --- | --- | --- | --- |
| `TECHSTRAP_PROJECT` | yes | `techstrap-uat` | Compose project name; UAT and production never share containers or volumes. |
| `TECHSTRAP_API_IMAGE` | yes | `ghcr.io/syntax-circus/techstrap-api:0.2.0` | Api image, pinned to one release tag, never latest. |
| `TECHSTRAP_WORKER_IMAGE` | yes | `ghcr.io/syntax-circus/techstrap-worker:0.2.0` | Worker image, same tag as the others. |
| `TECHSTRAP_ADMIN_IMAGE` | yes | `ghcr.io/syntax-circus/techstrap-admin:0.2.0` | Admin image, same tag as the others. |
| `TECHSTRAP_PORTAL_IMAGE` | yes | `ghcr.io/syntax-circus/techstrap-portal:0.2.0` | Portal image, same tag as the others. |
| `TECHSTRAP_ENV_DIR` | yes | `/etc/techstrap/uat` | Host directory that holds .env.api, .env.worker, .env.admin and .env.portal (root-owned, mode 0600). |
| `TECHSTRAP_SUBNET` | yes | `172.16.31.0/24` | Pinned compose subnet; the Api trusts it for forwarded headers. Change together with REVERSE_PROXY_CIDR. |
| `REVERSE_PROXY_CIDR` | yes | `172.16.31.1/32` | Address of the reverse proxy as the containers see it (the subnet gateway as /32 for a proxy on this host). Always a single address, never a wide range. |
| `TECHSTRAP_API_PORT` | yes | `18080` | Loopback port the proxy forwards to for the Api. |
| `TECHSTRAP_ADMIN_PORT` | yes | `18081` | Loopback port the proxy forwards to for the Admin. |
| `TECHSTRAP_PORTAL_PORT` | yes | `18082` | Loopback port the proxy forwards to for the Portal. |
| `TECHSTRAP_DB_NETWORK` | yes | `techstrap-db` | Existing Docker network the Postgres container is on; only the Api and the Worker join it. |

Compose refuses to resolve while a required value is missing. The templates ship working values for every required key except the image tags, which you must pin to a release.

## Reverse proxy

Caddy, nginx or any proxy that terminates TLS works. The proxy forwards to the loopback ports, passes the client address in `X-Forwarded-For` and `X-Forwarded-Proto`, and routes `/kb-images/` and `/product-logos/` on the Api host to the Api.

### Default-site Caddy block

Caddy with the placeholder hosts below and the production ports (use 18080 to 18082 for UAT). `reverse_proxy` keeps the Host header and sets `X-Forwarded-For` and `X-Forwarded-Proto` itself.

```caddyfile
api.example.com {
    encode zstd gzip
    request_body {
        max_size 26MiB
    }
    # /kb-images/ and /product-logos/ are served by the Api; this site already sends them there.
    reverse_proxy 127.0.0.1:8080
}

admin.example.com {
    encode zstd gzip
    reverse_proxy 127.0.0.1:8081
}

app.example.com {
    encode zstd gzip
    request_body {
        max_size 26MiB
    }
    reverse_proxy 127.0.0.1:8082
}
```

`api.example.com` is `TECHSTRAP_API_PUBLIC_URL`, `admin.example.com` is the Admin address and `app.example.com` is `TECHSTRAP_PORTAL_PUBLIC_URL`. A product with its own host needs one more site block per host; see "Product hosts" in [DEPLOYMENT.md](DEPLOYMENT.md#product-hosts).

### Request body size

A ticket carries up to 25 MiB of attachments in total, plus form overhead. The Portal buffers a form of up to 27,262,976 bytes (26 MiB) before its antiforgery check runs, so the proxy is the first place to refuse an oversized body. Use `26MiB` (27,262,976 bytes), not `26MB`: Caddy's `MB` is 10^6 bytes (26,000,000) and would refuse a legal 25 MiB submission. A per-IP rate limit on the Portal form routes (`/p/{key}/contact`, `/p/{key}/lost-link` and `/t/{token}`) is also the proxy's job.

### Knowledge-base images

`/kb-images/{name}` and `/product-logos/{name}` are served by the Api itself from the `techstrap-storage` volume. The Portal builds image URLs from `TECHSTRAP_API_PUBLIC_URL`, so the proxy must route both paths on the Api host to the Api. The default-site block above already does, because it sends the whole Api host to the Api. No static-file mapping is needed.

### Forwarded headers and the pinned subnet

The apps trust `X-Forwarded-For` and `X-Forwarded-Proto` only from `TECHSTRAP_SUBNET` and `REVERSE_PROXY_CIDR`. Rate limits and logs use the forwarded client IP. A wrong `REVERSE_PROXY_CIDR` makes every client share the proxy's address and therefore one rate limit. With the proxy on the same host, `REVERSE_PROXY_CIDR` is the subnet's gateway as a `/32` (the `.1` address), because published ports reach the containers through the Docker gateway. Change `TECHSTRAP_SUBNET` and `REVERSE_PROXY_CIDR` together, and never use a wide range. Verify the path at the first deploy with the checks in DEPLOYMENT.md, "First deploy: verify the client IP path" ([DEPLOYMENT.md](DEPLOYMENT.md#first-deploy-verify-the-client-ip-path)).

## TLS

TLS terminates at the reverse proxy, outside compose; the containers speak plain http on the loopback ports. The apps send HSTS themselves (`SECURITYHEADERS__STRICTTRANSPORTSECURITY`). Obtaining and renewing certificates, including those for product hosts, is the operator's concern.

## SMTP

The Worker sends all mail. `EMAIL__SMTP__HOST` and `EMAIL__SMTP__DEFAULTFROM` are required while the outbox is enabled; to run without email set `EMAILOUTBOX__ENABLED=false`. `EMAIL__SMTP__TLSMODE` selects the transport security (the template ships `StartTls`), and failed sends are retried according to `EMAIL__SMTP__MAXRETRYATTEMPTS` and `EMAIL__SMTP__RETRYMODE`. The outbox lease must cover a whole batch (see `EMAILOUTBOX__LEASESECONDS`). Customer links in emails are built from `TECHSTRAP_PORTAL_PUBLIC_URL`.

## Volumes

| Volume | Holds | Used by |
| --- | --- | --- |
| `techstrap-storage` | Ticket attachments, knowledge-base images and uploaded product logos. This is the only place they live. | Api |
| `admin-keys` | The Admin's data-protection key ring (cookie keys). | Admin |
| `portal-keys` | The Portal's data-protection key ring (antiforgery and link keys). | Portal |

All three must persist across upgrades and be backed up with the database. Losing a key volume signs every agent out (Admin) or invalidates in-flight forms (Portal); losing `techstrap-storage` loses attachments. Never remove them with `docker compose down --volumes`.

## Portal appearance

The Portal has five built-in theme packs (D-053). There is no environment key for the look: it is data.

| Pack | Key | Look |
| --- | --- | --- |
| Classic | `classic` | Today's look: white page, IBM Plex Sans, soft corners, flat buttons. The default; a deployment that never touches the setting looks exactly as before. |
| Slate | `slate` | Cool off-white page, navy ink and a solid dark header, soft shadow. |
| Paper | `paper` | Warm cream page, Source Serif 4 headings with Nunito body text, a dark band under the header. |
| Contrast | `contrast` | Pure black on white, Atkinson Hyperlegible, 3px borders, square corners, outline buttons, a solid black header. The strongest contrast. |
| Midnight | `midnight` | A dark page (`#0F1420`) with light text and a blue brand. It sets its own variables and never follows the visitor's operating system setting. |

- **Choose the deployment default.** An Admin sets it with `PUT api/settings/site` (body `{"defaultPack":"midnight","version":<the version `GET api/settings/site` returned>}`; a stale version is a 409; a pack key the Api does not know is refused with 400 `skin-pack-unknown`; if a stored key is ever one the Portal does not know, the Portal renders Classic). The Admin page for it arrives with PHASE-11h. The migration seeds `classic`. There is no `.env` key, so changing the pack never needs a restart.
- **What it changes.** The default pack styles the Portal root and the neutral 404 and error pages, and every product page and ticket page whose product has no pack of its own. The Portal reads the setting through `GET api/public/site` and keeps the answer for 60 seconds, so a change shows within about a minute on pages that are not cached, and up to about two minutes on the help-center pages, which are output-cached for their own minute; if the Api cannot be read the Portal keeps the last good value (Classic when it has never had one) and never shows an error page for it.
- **Product skins.** A product may carry its own skin: an optional `pack` plus token overrides (colors, fonts from the built-in list, radius, border width, shadow, button and header presets). Resolution order is Classic, then the deployment default, then the product pack, then the product's tokens. Set it on the product (`skin` in `POST api/products` and `PUT api/products/{id}`, or the interim "Skin (JSON)" field under "Appearance (advanced)" in the Admin product editor); see [docs/skins/README.md](../skins/README.md) for the grammar, a worked sample and its known gaps. A product with no skin looks as it did before: its pages, the neutral pages and its emails are unchanged. On the landing page each card is drawn in its own product's scope, so its hover and focus border use that product's accent instead of the Classic blue.
- **Contrast is enforced.** A skin that puts ink on background, ink on surface or muted on background below 4.5:1, or the focus ring on background below 3:1, is refused with 400 `skin-contrast-invalid` naming the pair. A later change of the default pack can make a saved override fail; at render the failing value is dropped for the pack value and nothing is rewritten.
- **Emails.** Emails take colors only: the product's accent and, when its skin sets a chrome color or a pack, that chrome color in the header bar. No web fonts or CSS are sent. The Worker cannot read the deployment default pack, so emails treat the default as Classic.
- **Fonts.** The five font families are open-license faces restored at image build time from the pinned fontsource packages; nothing is loaded from a CDN at run time.

## First administrator

There is no seeded account. The first administrator is whoever signs in with the provider's admin group (`TECHSTRAP_ADMIN_GROUP`) in their token (D-029). Agents are provisioned at first sign-in from the agent group. An agent is deactivated in the Admin app, and the last-admin guard refuses to deactivate or demote the last active administrator.

## Upgrade and rollback

1. Take a backup first (see [Backups](#backups)) and record the current image references.
2. Set the new tag in all four `TECHSTRAP_<APP>_IMAGE` lines of `deploy/.env.<env>.local`.
3. Run `docker compose --env-file deploy/.env.<env>.local -f deploy/docker-compose.yml pull`, then the same command with `up -d --wait`.

The Api applies pending migrations on start under an advisory lock (`DATABASE__MIGRATEONSTARTUP`, default true), so a single Api instance migrates and the rest wait. To roll back the application, set the previous tags and run `pull` and `up -d --wait` again. Migrations only move forward: going back past a migration needs a database restore. See "Rollback and upgrade" in [DEPLOYMENT.md](DEPLOYMENT.md#rollback-and-upgrade).

## Health checks

| Service | Endpoint |
| --- | --- |
| Api | `/health/ready` (Postgres reachable and migrated) |
| Worker | `/health/ready` (container health only; no published port) |
| Admin | `/health/live` |
| Portal | `/health/live` |

Compose runs these as container health checks, and `docker compose ps` shows every service healthy once the stack is up.

## PgBouncer and LISTEN

Live updates use a long-lived Postgres `LISTEN` connection in the Api. A PgBouncer in transaction-pooling mode breaks `LISTEN`. Connect the Api directly to Postgres or through a pooler in session mode. Run one Api instance. Details are in "Live updates" in [DEPLOYMENT.md](DEPLOYMENT.md#live-updates-phase-10).

## Backups

The procedure, the encrypted backup and restore scripts and the drill are in [backup-restore.md](../runbooks/backup-restore.md). The targets are an RPO of 24 hours and an RTO of 4 hours. Two scripts do the work: one takes an encrypted backup of the database, the attachments volume and both key-ring volumes; the other restores it into a scratch project to prove it. Back up before every upgrade.

## Other identity providers

Authentik is the worked example (verified against a live Authentik in 12c) ([AUTHENTIK.md](AUTHENTIK.md)). Keycloak and other providers are untested; the contract in [OIDC requirements](#oidc-requirements) is what they must satisfy.
