# TechStrap Core: Design Spec

**Status:** Superseded by [`docs/architecture/`](../../architecture/00-DISCOVERY-INDEX.md) (2026-10-02). Kept as historical input; where the two differ, `docs/architecture/` wins.
**Project:** TechStrap, "Support for Technical Support"
**Scope of this spec:** Sub-project 1, the core. Later sub-projects are outlined only so the core leaves room for them.

## 1. Purpose

TechStrap is a lightweight, open-source, self-hosted helpdesk for one company that supports **many products**. It is a revival of a college project (a WinForms call-logging tool with a SQL backend), rebuilt as an API plus Blazor apps and shipped as Docker images.

It is deliberately **not** multi-tenant: one installation serves one company's products. Anyone else self-hosts their own copy.

It is meant to be small enough to run on a UAT box that already hosts an observability stack and CI/CD. FreeScout (PHP) was the reference for what a light helpdesk looks like; Zammad was rejected as too heavy.

### Success criteria for the core

- A customer can submit a ticket from a per-product web form or from inside any of the company's apps, receive a confirmation email, and follow the conversation in a readable browser view without an account.
- Agents can triage, reply, tag, assign and solve tickets across all products from one agent app, signed in through OIDC (Authentik for the owner).
- Public knowledge-base articles exist per product (or shared), are searchable, and can be linked from replies.
- The system runs from `docker compose up` and uses a single Postgres.

### Non-goals for the core

- Inbound email (IMAP) and email threading.
- Custom fields, saved replies, custom folders, reports, SLA timers.
- Workflows (rules engine), though the core is designed to accept it.
- Passkey login for agents (OIDC only in the core).
- Customer accounts or passwords, live chat, multi-tenancy, and a mobile app.

## 2. Sub-project map

| # | Sub-project | Notes |
|---|---|---|
| 1 | **Core** (this spec) | Products, tickets, event log, tags, KB, intake, customer view, outbound email, agent OIDC, Docker publishing |
| 2 | Inbound email | IMAP per product mailbox, threading, quoted-text stripping, bounce and auto-reply handling |
| 3 | Extras | Custom fields, saved replies, custom folders, reports |
| 4 | Workflows | Rules engine over `TicketEvent` |
| 5 | Passkeys | WebAuthn for agents, with email bootstrap and recovery, so self-hosters need no IdP |

Each later sub-project gets its own spec and plan.

## 3. Topology

One solution, one repository, separate container images.

| Container | Role | Exposure |
|---|---|---|
| `techstrap-api` | Only component that talks to the DB for requests. Owns all business logic (handlers) and the HTTP API. Runs migrations on startup. | Public intake and customer-token endpoints; agent endpoints behind OIDC |
| `techstrap-admin` | Blazor Server agent app. Calls the API through a typed client. | Internal or behind the reverse proxy |
| `techstrap-portal` | Blazor SSR public site: customer ticket view, per-product web form, KB. Calls the API. | Public |
| `techstrap-worker` | Drains the email outbox. Later hosts the IMAP poller and the workflow engine. Runs the Application handlers in-process against the DB. | Not exposed |
| `postgres` | Single data store, including full-text search. | Internal network |

Rules:

- Admin and portal never access the database; everything goes through the API.
- Layers: `Domain`, `Application` (handlers), `Infrastructure` (EF Core, email, storage), one host project per container. Project and namespace prefix is `TechStrap`.
- Build to the conventions in `D:\dev\SyntaxCircus\_template` (server-side entry-point and handler boundaries, Razor component and code-behind boundaries).
- Reuse published `SyntaxCircus.*` packages instead of re-implementing: `DotEnv`, `AspNetCore.Common` (correlation id, problem details, security headers, health), `AspNetCore.Authentication` (JWT bearer and API keys), `AspNetCore.Serilog`, `EntityFrameworkCore.Postgres` (migrate-on-startup), `Email` (outbound SMTP), `Storage` (attachments), `Observability`, `Blazor.Seo`, `Blazor.Auth` (admin forwarding the agent's OIDC tokens to the API).

### Authentication schemes (API)

| Caller | Scheme | Power |
|---|---|---|
| Agent | JWT bearer from an OIDC provider (Authentik for the owner). Roles via claim: `Admin`, `Agent`. | Full agent surface |
| Product app | Per-product API key | Create a ticket for that product, nothing else |
| Customer | Per-ticket access token | Read public messages on that ticket, post a reply |

Public endpoints sit behind the reverse proxy and follow the client-IP rate-limiting pattern in the template's `docs/patterns/CLIENT_IP_RATE_LIMITING.md`.

## 4. Domain model

### Core entities

- **Product:** key (`acme`), name, email branding (display name, from-address, reply-to), active flag. A ticket belongs to exactly one product. Routing in the core is the intake source: an API key maps to a product, and the web form is per product (`/p/{key}/contact`). An agent can move a ticket to another product.
- **Agent:** OIDC subject, name, email, role, active flag. Created on first sign-in. No password.
- **Requester:** email (unique, case-insensitive), name, optional external user reference from the calling app. No credentials.
- **Ticket:** number like `ACME-142` (per-product sequence allocated in the creating transaction), product, requester, subject, status (`New`, `Open`, `Pending`, `Solved`), priority, optional assignee, channel (`Web`, `Api`; `Email` later), `metadata` jsonb (app context such as version and device), `custom_fields` jsonb (reserved, unused in the core), timestamps `created_at`, `first_response_at`, `solved_at`, `last_activity_at`, a full-text search vector, and a concurrency token.
- **Message:** ticket, author type (`Requester`, `Agent`, `System`) and author id, visibility (`Public` or `Internal`), body (stored sanitized), attachments, and reserved nullable columns for email `Message_Id` and `In_Reply_To` so inbound email needs no migration later.
- **Attachment:** metadata row; bytes stored through `SyntaxCircus.Storage` on a local volume. Size limit and file-type allowlist enforced at intake.
- **TicketEvent** (append-only): ticket, type (`Created`, `MessageAdded`, `StatusChanged`, `Assigned`, `ProductChanged`, `PriorityChanged`, `TagAdded`, `TagRemoved`), actor, jsonb payload, `occurred_at`. Every ticket mutation writes its event in the same transaction. The timeline reads from it, later reports aggregate it, and the future Workflows engine subscribes to it. This is the designed-in hook for Workflows.
- **Tag / TicketTag:** global tags with a unique slug and optional color. Agent-only; customers never see them. Changes emit events.
- **KbCategory:** single level (name, slug, sort order), belongs to a product or is shared.
- **KbArticle:** product (nullable means shared), category, slug (unique within product), title, summary, Markdown body (rendered and sanitized on output), status (`Draft`, `Published`, `Archived`), author, created/updated/published timestamps, full-text search vector.
- **TicketArticle:** link recording which article an agent referenced in a ticket reply.

### Customer access

- **TicketAccessToken:** one per ticket and requester; 256-bit random, stored hashed, revocable, with an expiry that slides on activity.
- The emailed link is `/t/{token}` on the portal. It permits reading public messages and posting a reply only.
- Lost link: a "send me a new link" form always returns the same response regardless of whether the address matched, and only ever emails the requester's own address.

### Intake keys

- **ProductApiKey:** hashed, per product, scope "create ticket".

### Reserved, not built in the core

Saved replies, custom folders, report tables, workflow rules, and passkey credentials.

## 5. Knowledge base

- Search is Postgres full-text over title (highest weight), summary and body.
- Editing is a Markdown editor with live preview in the admin app; images upload through `SyntaxCircus.Storage`.
- The portal renders articles server-side, browsable by product and category and searchable, with meta tags and sitemap from `SyntaxCircus.Blazor.Seo`.
- Deflection: the web form shows matching published articles as the customer types a subject, using the same search endpoint.
- Cut for the core: revision history, "was this helpful" feedback, translations, view counts.

## 6. Main flows

1. **Intake (web form or app API).** One transaction: validate; find or create the requester by email; allocate the next ticket number; create the ticket, first message, `Created` event and access token; write an outbox row for the confirmation email containing the magic link. The API response carries the ticket number and view URL so apps can show it.
2. **Agent reply.** Public reply: add message and event, set `Pending`, set `first_response_at` if first, queue the email with the link. Internal note: no email.
3. **Customer reply (portal).** Validate token; add message and event; `Pending` or `Solved` becomes `Open`; notify the assignee.
4. **Outbox.** Email rows are written in the same transaction as the change that triggered them. The worker claims rows with `FOR UPDATE SKIP LOCKED`, sends via `IEmailSender`, retries with backoff, and dead-letters after N failures; dead letters are visible in the admin app. Delivery is at-least-once, with the outbox id placed in the `Message-ID` header so repeats are recognizable.
5. **Inbound-email fallback until sub-project 2.** Outbound emails say to reply through the ticket link. The reply-to address design is settled in sub-project 2.

## 7. Errors, abuse and concurrency

- API errors are RFC 7807 problem details from `AspNetCore.Common`.
- Token endpoints return a uniform 404 for invalid, expired or revoked tokens.
- Email failures never fail the user's request, because of the outbox.
- Web form: honeypot field, per-IP rate limits, body and attachment size limits, file-type allowlist. No CAPTCHA in the core; Turnstile can be added if spam appears.
- Ticket updates use optimistic concurrency so two agents cannot silently overwrite each other.

## 8. Testing

- Unit tests for handlers and domain rules (status transitions, token validation, numbering). Shouldly assertions and NSubstitute, matching the other projects.
- Integration tests against real Postgres via Testcontainers (intake to reply cycle, outbox, full-text search). The database is not mocked.
- The test framework (xUnit or MSTest) is chosen in the plan by matching the owner's existing repos, using the corresponding testing skill.

## 9. Deployment and Docker publishing

### Runtime

- Compose files follow the-button's layout: `docker-compose.yml` (local), `docker-compose.uat.yml`, `docker-compose.production.yml`, plus `.env.production.example`. TLS and the reverse proxy sit outside the compose files.
- Configuration comes from env files through `SyntaxCircus.DotEnv`. Only the API container runs migrations (advisory-lock migrate-on-startup).
- Serilog and OpenTelemetry via `SyntaxCircus.Observability` feed the existing observability stack. Every container exposes health endpoints.
- Backups are documented: `pg_dump` plus the attachments volume.

### Image publishing (matches the owner's existing projects)

Modeled on `Build-SinForgiverDocker.ps1` and `Build-SyntaxCircusDocker.ps1`; the same layout is used in the-button and dragon-poop.

- **Script:** `Build-TechStrapDocker.ps1` at the repo root. Parameters: `-Targets` (default `api, admin, portal, worker`), `-ImageTag`, `-SemVerTag`, `-Registry`, `-Push`, `-PushLatest` (default true), `-NoCache`, `-Platforms`, `-VersionProjectPath`.
- **Platforms:** default `linux/amd64` and `linux/arm64`, as in `syntax-circus-web`. Sinforgiver and the-button currently restrict the parameter to `linux/amd64` but keep the ARM64 handling, so TechStrap carries the full multi-arch behavior from the start. With `-Push` and a registry, one multi-platform `buildx --push` per image. Without it, per-platform `--load` builds tagged with an `-amd64` or `-arm64` suffix (amd64 also gets the canonical tag), because `--load` cannot load a multi-platform manifest.
- **Versioning:** GitVersion resolved with `dotnet msbuild -target:GetVersion`, falling back to the `dotnet-gitversion` and `gitversion` CLIs. The result is validated as SemVer, and the image is tagged with the SemVer, an explicit `-ImageTag` if given, and `latest`. Build args: `BUILD_VERSION`, `BUILD_INFORMATIONAL_VERSION`, `DISABLE_GITVERSION_TASK=true`.
- **Dockerfiles:** `Dockerfile.api`, `Dockerfile.admin`, `Dockerfile.portal`, `Dockerfile.worker` at the repo root. Each copies the whole source tree before restore (never a hand-maintained `.csproj` list) and uses a BuildKit NuGet cache mount. Runtime stage: `mcr.microsoft.com/dotnet/aspnet:10.0`, a non-root user (uid 10001), writable mount points created and chowned up front (`storage`, `logs`, `dataprotection-keys`), `curl` for health checks, and `ASPNETCORE_URLS=http://+:80`.
- **Repo conventions copied from the other projects:** `GitVersion.yml`, `Directory.Build.props` (nullable, `TreatWarningsAsErrors`), `Directory.Build.targets`, `Directory.Packages.props` (central versions), `global.json`, `.slnx` solution file, `.editorconfig` (4-space, Allman), Conventional Commits.

## 10. Decisions deferred to the plan

Each has a default so nothing blocks the plan:

- **Container registry** for `-Registry`: passed as a parameter, as in the other projects. The plan documents the owner's registry; there is no default in the script.
- **Assignee notification channel** on a customer reply: default is an email through the same outbox; in-app badges are a later enhancement.
- **Attachment limits:** default 10 MB per file and 25 MB per message, with an allowlist of common document and image types.
- **Token expiry:** default 90 days sliding.
- **Test framework:** see section 8.
