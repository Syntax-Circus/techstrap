# TechStrap

**Support for Technical Support.** A lightweight, open-source, self-hosted helpdesk for one company that supports many products.

> **Status:** planning. The architecture and phased implementation plan are written; no application code exists yet.

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

## Documentation

Start at the [discovery index](docs/architecture/00-DISCOVERY-INDEX.md).

| Document | Contents |
| --- | --- |
| [Requirements](docs/architecture/01-REQUIREMENTS.md) | Goals, personas, scope, functional and non-functional requirements |
| [Architecture](docs/architecture/02-ARCHITECTURE.md) | Topology, data model, flows, application boundaries |
| [Package map](docs/architecture/03-PACKAGE-MAP.md) | Dependencies with pinned versions |
| [Decision log](docs/architecture/04-DECISION-LOG.md) | Material decisions and their status |
| [Admin UX brief](docs/architecture/UX-BRIEF-admin.md) · [Portal UX brief](docs/architecture/UX-BRIEF-portal.md) | Designer handoffs |
| [Implementation roadmap](docs/architecture/99-IMPLEMENTATION-ROADMAP.md) | Phases 01–12, task index, validation commands |

## License

MIT. A `LICENSE` file is added in [PHASE-01](docs/architecture/PHASE-01-foundation.md).
