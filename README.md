<p align="center">
  <img src="assets/brand/logo-512.png" alt="TechStrap logo" width="200">
</p>

# TechStrap

**Support for Technical Support.** A lightweight, open-source, self-hosted helpdesk for one company that supports many products.

> **Status:** early development. PHASE-01 (the foundation) is in place: the solution skeleton, health endpoints, database migrations, Docker images, the compose stack and CI. Product features arrive in later phases; see the [roadmap](docs/architecture/99-IMPLEMENTATION-ROADMAP.md).

## What it is

- **One installation, many products.** Each product has its own ticket prefix (`ACME-142`), web form, knowledge base, and branding on the portal and in emails. It is deliberately not multi-tenant: other organisations run their own copy.
- **Customers need no account.** They submit a ticket from a per-product web form or from inside your apps, then follow the conversation through a private emailed link.
- **Agents work in one app.** Queues, search, replies and internal notes, tags, assignment, and live updates, behind OIDC sign-in (any provider; Authentik is the worked example).
- **Knowledge base.** Searchable public articles per product (or shared), suggested to customers as they type a ticket.
- **Small footprint.** API, Blazor admin app, Blazor public portal, and a background worker, all as Docker images on a single Postgres. Started with `docker compose up`.
- **Client SDK.** A .NET client and a .NET MAUI helper for submitting tickets from your own apps.

Out of scope for the core, and planned as later sub-projects: inbound email, custom fields and saved replies, a workflow rules engine, and passkey sign-in.

## Tech stack

.NET 10 · ASP.NET Core controllers · Blazor Server (admin) and Blazor SSR (portal) · EF Core with PostgreSQL (full-text search) · Bootstrap 5 SCSS · Serilog and OpenTelemetry · xUnit v3, Shouldly, NSubstitute, Testcontainers · [SyntaxCircus](https://www.nuget.org/profiles/syntaxcircus) NuGet packages.

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

Compose reads a few optional inputs (`TECHSTRAP_SUBNET`, `REVERSE_PROXY_CIDR`, `TECHSTRAP_MAILPIT_PORT`, `TECHSTRAP_SEED_DEV_DATA`) from a root `.env`; copy [.env.example](.env.example) to change them.
Every other setting comes from the host's `appsettings.json` and, if you add one, its `src/TechStrap.<Host>/.env.local`.

Troubleshooting: if compose reports `Pool overlaps with other one on this address space`, another Docker network already uses
the pinned subnet `172.16.31.0/24`. Pick a free one for this stack, for example `TECHSTRAP_SUBNET=10.245.31.0/24 docker compose up -d --build`.

For production, `REVERSE_PROXY_CIDR` must be the address the containers see the reverse proxy from. With the loopback-only
published ports and a proxy on the same host, that is the compose gateway (`172.16.31.1/32`), never a wide range.

UAT and production run from one image-only compose, `deploy/docker-compose.yml`: pinned GHCR image tags (one tag for all four), a scoped env file per service under `/etc/techstrap/<env>/`,
a required and per-environment `TECHSTRAP_PROJECT`, and a separate Postgres on an external Docker network. The runbook is [DEPLOYMENT.md](docs/self-hosting/DEPLOYMENT.md).

### Submit a test ticket

With `TECHSTRAP_SEED_DEV_DATA=true` set when you start the stack, `pwsh -File scripts/Send-TestTicket.ps1` submits a ticket
through the API-key intake endpoint. The confirmation email appears in Mailpit at <http://localhost:8025> (set `TECHSTRAP_MAILPIT_PORT` if that port is taken). The routes,
idempotency, attachments and the email outbox are described in [INTAKE.md](docs/development/INTAKE.md).

### Develop

```bash
dotnet tool restore
dotnet build TechStrap.slnx
dotnet test --solution TechStrap.slnx   # needs Docker running; Testcontainers starts Postgres 17
```

Each project's `appsettings.json` lists every setting the host reads, with its default (secrets are blank). Per-host settings for `dotnet run` go in
`src/TechStrap.<Host>/.env.local` (gitignored; copy the `.env.example` next to it, which documents the same keys as `SECTION__KEY`).
See [CONTRIBUTING.md](CONTRIBUTING.md) for the workflow and [SECURITY.md](SECURITY.md) to report a vulnerability.

## Documentation

Start at the [discovery index](docs/architecture/00-DISCOVERY-INDEX.md).

| Document | Contents |
| --- | --- |
| [Requirements](docs/architecture/01-REQUIREMENTS.md) | Goals, personas, scope, functional and non-functional requirements |
| [Architecture](docs/architecture/02-ARCHITECTURE.md) | Topology, data model, flows, application boundaries |
| [Package map](docs/architecture/03-PACKAGE-MAP.md) | Dependencies with pinned versions |
| [Decision log](docs/architecture/04-DECISION-LOG.md) | Material decisions and their status |
| [Admin UX brief](docs/architecture/UX-BRIEF-admin.md) · [Portal UX brief](docs/architecture/UX-BRIEF-portal.md) | Designer handoffs |
| [Agent authentication](docs/self-hosting/AGENT-AUTHENTICATION.md) | Self-hosting: OIDC issuer, audience and group settings |
| [Development seed data](docs/development/DEV-DATA.md) | What the Development seeder creates and when |
| [Ticket operations](docs/development/TICKET-OPERATIONS.md) | Agent ticket API: curl examples, RowVersion, email kinds |
| [Admin app](docs/development/ADMIN-APP.md) | The agent app: configuration, sign-in, Authentik setup, shortcuts, known limits |
| [Implementation roadmap](docs/architecture/99-IMPLEMENTATION-ROADMAP.md) | Phases 01–12, task index, validation commands |

## License

[MIT](LICENSE).
