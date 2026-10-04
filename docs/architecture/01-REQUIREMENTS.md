# TechStrap Requirements

Status: Draft for review (2026-10-02). Supersedes the requirements parts of
`../superpowers/specs/2026-10-02-techstrap-core-design.md`. Companion:
[02-ARCHITECTURE.md](02-ARCHITECTURE.md).

Tags used below: **Requirement**, **Decision** (owner decided, see decision log `D-nnn`), **Assumption** (default the owner has not explicitly decided), **Risk**, **Open question**.

## 1. Project brief

Fields follow the _template PROJECT_BRIEF_TEMPLATE.md.

### Project identity

- **Name:** TechStrap ("Support for Technical Support").
- **One-sentence problem:** A single company that supports many products has no light, self-hostable way to receive, triage and answer support requests (from web forms and from inside its own apps) without running a heavy helpdesk.
- **Desired outcome:** A small open-source helpdesk, shipped as four Docker images plus Postgres, that replaces ad-hoc email for support. Measurable criteria are in section 2.
- **Owner/stakeholders:** Jon Seeley / Syntax Circus (owner, first operator, first self-hoster). Future self-hosters are secondary stakeholders.
- **Target release or milestones:** Twelve phases (PHASE-01 to PHASE-12) ending in a `v1.0.0` tag after UAT deploy. See `99-IMPLEMENTATION-ROADMAP.md`.

### Users and selected topology

- **Personas:** agent, admin, customer/requester, product-app developer, self-hoster (section 3).
- **User-facing applications:** `TechStrap.Portal` (Blazor SSR, public).
- **Internal/admin applications:** `TechStrap.Admin` (Blazor Server, agents and admins).
- **API:** `TechStrap.Api` (Controllers; the only request-time DB client; hosts SignalR).
- **Background workers or scheduled jobs:** `TechStrap.Worker` (email outbox drain, auto-close, later inbound email and workflows).
- **Expected client platforms:** desktop and mobile browsers (Portal), desktop browsers (Admin), .NET apps including MAUI (via `TechStrap.Client` / `TechStrap.Client.Maui`).
- **Deployment topology/environments:** Docker Compose, three environments: local, UAT, production. TLS and reverse proxy (Caddy) are outside the compose files. Detail in [02-ARCHITECTURE.md](02-ARCHITECTURE.md).

### Integrations

| System | Purpose | Direction | Authentication | Notes |
| --- | --- | --- | --- | --- |
| OIDC provider (Authentik for the owner; any OIDC IdP for self-hosters) | Agent and admin sign-in | Inbound (JWT validation), Admin does the code flow | OIDC / JWT bearer | Group claim gates access (D-004) |
| SMTP server | Outbound email | Outbound | SMTP credentials via env | Via `SyntaxCircus.Email`; delivery through the outbox (D-010) |
| Product apps (company's own) | Create tickets from inside apps | Inbound | Per-product API key, Trusted or Public (D-001) | Via `TechStrap.Client` |
| Reverse proxy (Caddy) | TLS, forwards client IP | Inbound | Trusted proxy config | Pinned compose subnet |
| Observability stack on UAT box (OpenTelemetry, Serilog sinks) | Logs, traces, metrics | Outbound | Per stack | Via `SyntaxCircus.Observability` |
| GHCR, NuGet.org, GitHub Actions | Image and package publishing, CI | Outbound | GitHub tokens | D-003 |
| IMAP mailbox | Inbound email | Inbound | n/a | Later sub-project, not in core |

### Data and security (summary; detail in section 8)

- **Primary data store:** one Postgres 17 database. Attachments and KB images on a local volume via `SyntaxCircus.Storage`.
- **Sensitive data:** requester name and email, free-text ticket bodies, attachments, app metadata (may include device info).
- **Retention/deletion:** requester erasure and ticket hard-delete in core; automatic retention rules deferred (D-006).
- **Authentication provider:** external OIDC. **Authorization model:** group claim to `Agent`/`Admin` policies; per-product API keys; per-ticket customer tokens.
- **Compliance or residency:** none mandated. Privacy basics only (D-006). Self-hoster owns their own compliance.

### Technology preferences

- **Framework/runtime:** .NET 10. **Frontend:** Blazor Server (Admin), Blazor SSR (Portal), Bootstrap 5 SCSS via libman + AspNetCore.SassCompiler, no compiled CSS committed.
- **API style:** Controllers; handlers via `[FromServices]`; `Result` mapped to ProblemDetails. OpenAPI document published.
- **Database:** Postgres 17, snake_case naming, EF Core migrations generated only by `dotnet ef`, migrate-on-startup in API only (advisory lock). UTC timestamps.
- **Authentication:** OIDC JWT (agents), API keys, customer tokens.
- **Logging:** Serilog with PII redaction, OpenTelemetry via `SyntaxCircus.Observability`.
- **Testing:** xUnit v3, Shouldly, NSubstitute, Testcontainers.PostgreSql (D-013). Pin rule for xunit.v3 and NCrunch per the _template AGENT_GUIDE.md.
- **Styling/design system:** `docs/BRAND.md` plus UX briefs (PHASE-02).
- **Configuration/secrets:** `.env.example` per host, `.env.local` gitignored, via `SyntaxCircus.DotEnv`.
- **Syntax Circus packages to consider:** DotEnv, Common, AspNetCore.Common, AspNetCore.Authentication, AspNetCore.Serilog, EntityFrameworkCore.Postgres, Email, Storage, Observability, Blazor.Seo, Blazor.Auth, Blazor.Components, Http.Resilience. Final selection and versions live in `03-PACKAGE-MAP.md`.

## 2. Problem, goals and measurable success criteria

### Problem

Support requests for the company's products arrive through mixed channels, are not tracked per product, and lose context. Heavy helpdesks (Zammad) are too large for the UAT box; FreeScout (PHP) is the reference for the right weight but is not .NET and not integrated with the company's apps. TechStrap is a revival of a college WinForms call-logging tool, rebuilt as an API plus Blazor apps.

TechStrap is deliberately **not multi-tenant**: one installation serves one company's products. Others self-host their own copy.

### Goals and success criteria

| ID | Goal | Measurable criterion |
| --- | --- | --- |
| G-1 | Customers can raise a ticket without an account, from the web or from inside an app | A ticket submitted via the portal form, via a Trusted key and via a Public key each produce a ticket, a confirmation email in the outbox and a readable `/t/{token}` view, verified by integration tests and a UAT walkthrough |
| G-2 | Customers follow the conversation without an account | Magic link opens the ticket, shows public messages only, accepts a reply; invalid, expired or revoked tokens return a uniform 404 |
| G-3 | Agents triage and resolve across all products from one app | An agent can filter the queue by view, product, status, tag and free text, reply, assign, tag, change status and priority from the Admin app, signed in through OIDC |
| G-4 | Self-service reduces tickets | Published KB articles are searchable on the portal and shown as deflection suggestions on the contact form; agents can link articles in replies |
| G-5 | Light footprint | Whole stack runs on the UAT box within the footprint in section 6 with p95 targets met at the traffic assumption |
| G-6 | Easy to run and adopt | `docker compose up` brings up a working stack with seed data; a self-hoster with any OIDC provider can follow the docs to a working install |
| G-7 | Safe by default | Security review (PHASE-12) passes for token, key, upload and sanitizer paths; no PII in logs; erasure works end to end |
| G-8 | Room to grow | Inbound email, extras, workflows and passkeys can be added without schema rewrites (reserved columns, `TicketEvent` log, worker host) |
| G-9 | Open source from day one | MIT license, README, CONTRIBUTING, SECURITY.md, CI on GitHub Actions, images on GHCR |

## 3. Personas

| Persona | Description | Main needs | Surface |
| --- | --- | --- | --- |
| Agent | Company staff member answering tickets (fewer than 50) | Fast queue, search, reply, internal notes, live updates, alerts for new tickets and assignments | Admin app (agent group claim) |
| Admin | Agent with configuration rights; first one is whoever is in the IdP admin group (D-029) | Manage products, branding, API keys, agents, tags, KB categories, dead letters; see admin audit trail; erase requesters, delete tickets | Admin app (admin group claim) |
| Customer / requester | End user of a product; no account | Submit a problem easily, find answers first, get confirmation, follow and reply by link, recover a lost link | Portal; email |
| Product-app developer | Company developer embedding support in a product | Simple SDK, key per product, safe client-embedded key, device/app metadata capture | `TechStrap.Client`, `TechStrap.Client.Maui`, OpenAPI |
| Self-hoster | Another company running its own copy | Clear compose setup, any OIDC IdP, backup/restore guidance, env-var configuration, upgradable images | GHCR images, docs, `.env.example` files |

## 4. Scope and non-scope

### In scope (core, sub-project 1)

Products with branding; agents (claim-gated); requesters; tickets with events, tags, priorities, assignment, product moves, spam and delete; messages with attachments; two API key kinds; customer access tokens; transactional email outbox and worker; knowledge base (agent editing, public browse, search, deflection, SEO); live updates and presence; auto-close and follow-up tickets; admin audit trail; client SDK and MAUI helper; privacy basics; Docker publishing, CI, UAT deploy, backup runbook.

### Non-scope for the core

| Item | Later sub-project / note |
| --- | --- |
| Inbound email (IMAP), threading, quoted-text stripping, bounce handling | Sub-project 2 (reserved `message_id` / `in_reply_to` columns exist) |
| Custom fields, saved replies, custom folders, reports, SLA timers | Sub-project 3 (extras); `custom_fields` jsonb reserved, unused |
| Workflows (rules engine) | Sub-project 4; subscribes to `TicketEvent` |
| Passkeys / WebAuthn for agents | Sub-project 5; core is OIDC only |
| Customer accounts or passwords, live chat, multi-tenancy, native mobile app for agents | Not planned |
| Automatic retention rules | Deferred (D-006) |
| KB revision history, helpfulness feedback, translations, view counts | Cut for core |
| CAPTCHA | Not in core; Turnstile can be added if spam appears |
| Localization of UI and emails | English only, with an i18n seam (**Assumption**) |
| Multi-instance API scale-out (SignalR backplane) | Not required at target traffic (D-007) |

## 5. Functional requirements

Priority is **M**ust for core unless marked **S**hould. IDs are stable; phases reference them.

### 5.1 Intake (FR-INTAKE)

| ID | Requirement | Pri |
| --- | --- | --- |
| FR-INTAKE-01 | A customer can submit a ticket from a per-product web form at the portal (multipart: subject, body, name, email, attachments) | M |
| FR-INTAKE-02 | A product app can submit a ticket through `POST /api/intake/tickets` with a per-product API key | M |
| FR-INTAKE-03 | Web form and app intake run the same use case (`SubmitTicketRequestHandler`); channel (`Web`, `Api`) and trust level differ | M |
| FR-INTAKE-04 | In one transaction: validate, find or create the requester by email (case-insensitive), allocate the ticket number, create ticket, first message, `Created` event, access token and confirmation-email outbox row | M |
| FR-INTAKE-05 | The API-key response carries the ticket number and the customer view URL so apps can show it; the web-form response carries the number only and the view link is emailed | M |
| FR-INTAKE-06 | A Trusted key may set external user reference and trusted metadata; a Public key cannot, and its metadata is stored flagged untrusted | M |
| FR-INTAKE-07 | Public keys are create-only and rate-limited per key and client IP; Trusted keys are server-side only | M |
| FR-INTAKE-08 | Web form has a honeypot field, per-IP rate limit, body size limit and attachment limits (default 10 MB per file, 25 MB per message) with a file-type allowlist | M |
| FR-INTAKE-09 | Ticket number is `{PRODUCT_KEY_PREFIX}-{n}`, per-product sequence, immutable; moving a product keeps the original number and prefix | M |
| FR-INTAKE-10 | The portal fetches product branding through `GET /api/public/products/{key}` | M |
| FR-INTAKE-11 | Priority on intake defaults to Normal; customers cannot set priority | M |
| FR-INTAKE-12 | Intake works when email is down; email failure never fails the request | M |
| FR-INTAKE-13 | `POST /api/intake/tickets` accepts an optional `Idempotency-Key` header (per API key, 24 h retention); a repeat returns the original ticket response (D-020) | S |

### 5.2 Email and outbox (FR-EMAIL)

| ID | Requirement | Pri |
| --- | --- | --- |
| FR-EMAIL-01 | Emails are written to the outbox in the same transaction as the change that triggers them | M |
| FR-EMAIL-02 | The worker claims rows with `FOR UPDATE SKIP LOCKED`, sends through the email sender, retries with backoff, dead-letters after N failures (default 8, **Assumption**) | M |
| FR-EMAIL-03 | Delivery is at-least-once; the outbox id is placed in the `Message-ID` header so repeats are recognizable | M |
| FR-EMAIL-04 | Email templates exist in text and HTML, carry per-product branding (name, logo, accent colour, from-address, reply-to) | M |
| FR-EMAIL-05 | Outbound emails tell the customer to reply through the ticket link (until inbound email exists) | M |
| FR-EMAIL-06 | Email kinds: intake confirmation, agent public reply, new access link, new-ticket alert to opted-in agents, assignment alert, customer-reply alert to assignee, solved notice to the requester (no closed notice, D-037). PHASE-05 sends only the intake confirmation; PHASE-06 adds the rest through `ITicketNotificationPlanner` | M |
| FR-EMAIL-07 | Agents can opt in per product to new-ticket alerts via notification preferences | M |
| FR-EMAIL-08 | Alerts are queued by ticket handlers through `ITicketNotificationPlanner`; there is no separate alert entry point | M |
| FR-EMAIL-09 | Customer emails render the resolved agent name (FR-CUST-07) and honour the Powered-by setting (FR-CUST-08): link in HTML, bare URL in text, omitted when hidden (D-024) | M |

### 5.3 Ticket operations (FR-TKT)

| ID | Requirement | Pri |
| --- | --- | --- |
| FR-TKT-01 | Agents see queue views: Unassigned, Mine, Open, Pending, All (all exclude spam) and a separate Spam view (FR-TKT-18) | M |
| FR-TKT-02 | Queue supports filters (product, status, priority, assignee, tag, spam), sorting and paging | M |
| FR-TKT-03 | Full-text ticket search over subject, requester and message bodies (Postgres FTS, D-011) | M |
| FR-TKT-04 | Ticket detail shows the timeline built from messages and `TicketEvent` | M |
| FR-TKT-05 | Agent public reply: adds message and event, sets Pending, sets `first_response_at` if first, queues email with the link; may link KB articles | M |
| FR-TKT-06 | Internal note: no email, never visible to the customer | M |
| FR-TKT-07 | Status changes among `New, Open, Pending, Solved, Closed` follow a transition table in Domain; a Solved ticket sets `solved_at` | M |
| FR-TKT-08 | Assign, change priority, add and remove tags; each writes an event in the same transaction | M |
| FR-TKT-09 | Move a ticket to another product (event, number unchanged) | M |
| FR-TKT-10 | Mark spam (`is_spam` flag, hidden from default views, no notifications; any Agent) and hard-delete a ticket (Admin only, D-022) | M |
| FR-TKT-11 | Optimistic concurrency on ticket updates; a conflict returns a clear error and the UI offers refresh | M |
| FR-TKT-12 | Customer reply on Pending or Solved sets Open and notifies the assignee (or opted-in agents if unassigned) | M |
| FR-TKT-13 | A Closed ticket is read-only. A customer reply to it creates a new ticket linked by `parent_ticket_id` and returns its link | M |
| FR-TKT-14 | Auto-close: Solved tickets older than N days (`TECHSTRAP_AUTOCLOSE_DAYS`, default 7, **Assumption**) become Closed through a worker job, with a `StatusChanged` event by the System actor; no customer email (D-037) | M |
| FR-TKT-15 | Admin (only, D-022) can list, retry and discard dead-lettered outbox rows; retry and discard write an `AdminEvent` | M |
| FR-TKT-16 | Attachment download is authorized for agents and for the ticket's customer token; internal-note attachments never reach customers | M |
| FR-TKT-17 | Every ticket mutation writes a `TicketEvent` in the same transaction | M |
| FR-TKT-18 | A dedicated Spam view lists tickets with `is_spam = true` in all statuses; the five normal views exclude them. Any Agent can open it (D-024) | M |
| FR-TKT-19 | A one-key "Not spam" action (`u`, also menu and palette) clears `is_spam` and restores the ticket to its normal views with its status unchanged; it writes a `TicketEvent` and sends no notification (D-024) | M |

### 5.4 Customer access (FR-CUST)

| ID | Requirement | Pri |
| --- | --- | --- |
| FR-CUST-01 | One access token per ticket and requester: 256-bit random, stored hashed, revocable, 90-day expiry that slides on activity (**Assumption**, from draft spec) | M |
| FR-CUST-02 | `/t/{token}` on the portal shows public messages and allows a reply, nothing else | M |
| FR-CUST-03 | Invalid, expired or revoked tokens return a uniform 404 | M |
| FR-CUST-04 | Lost-link form always returns the same response regardless of match, and only emails the requester's own address; rate limited per IP and per address | M |
| FR-CUST-05 | Customer ticket view shows ticket status and number, attachments they or agents attached to public messages | M |
| FR-CUST-06 | Tokens never appear in logs (redacted in path and query) | M |
| FR-CUST-07 | Customers see an agent as the agent's first name plus the product support name ("Sam from Orbitly Support"), or the agent's optional public display name in place of the first name ("Samantha from Orbitly Support"; suffix kept, **Assumption**), in the ticket view and emails. Agent emails and ids are never exposed to customers (D-024) | M |
| FR-CUST-08 | "Powered by TechStrap" links to https://github.com/Syntax-Circus/techstrap and shows by default on every portal page and customer email; `TECHSTRAP_PORTAL_SHOW_POWERED_BY=false` hides it installation-wide on the Portal and in Worker-rendered emails (D-024) | M |
| FR-CUST-09 | The portal contact URL may prefill `subject`, `name` and `email`; all three stay visible and editable, no hidden fields, and prefilled values get the same validation and length limits as typed input. App context travels through the SDK/API (D-024) | M |

### 5.5 Knowledge base (FR-KB)

| ID | Requirement | Pri |
| --- | --- | --- |
| FR-KB-01 | Agents create, edit, publish and archive articles in a Markdown editor with live preview | M |
| FR-KB-02 | Articles belong to a product or are shared (null product); status `Draft`, `Published`, `Archived`; slug unique within product | M |
| FR-KB-03 | Single-level categories (name, slug, sort order), product-scoped or shared | M |
| FR-KB-04 | Image upload for articles through storage | M |
| FR-KB-05 | Search is Postgres FTS with weights title > summary > body | M |
| FR-KB-06 | Portal renders published articles server-side, browsable by product and category and searchable, with SEO meta tags and sitemap (`Blazor.Seo`) | M |
| FR-KB-07 | Deflection: contact form suggests matching published articles while the customer types a subject, using the public search | M |
| FR-KB-08 | Agent replies can link articles; links are recorded (`TicketArticle`) | M |
| FR-KB-09 | Rendered Markdown is sanitized (Markdig plus HtmlSanitizer, D-014) | M |
| FR-KB-10 | Admin can delete a category only when empty or reassign (rule fixed in PHASE-08) | S |

### 5.6 Agent auth and administration (FR-AUTH, FR-ADMIN)

| ID | Requirement | Pri |
| --- | --- | --- |
| FR-AUTH-01 | Agents authenticate with OIDC JWT; policies require the configured group claims `TECHSTRAP_AGENT_GROUP` and `TECHSTRAP_ADMIN_GROUP` (D-004) | M |
| FR-AUTH-02 | Callers without the claim are rejected (403) even with a valid token | M |
| FR-AUTH-03 | The agent record is provisioned on first `GET /api/agents/me`; role follows the claim | M |
| FR-AUTH-04 | Roles come from IdP groups only; the first admin is whoever is in `TECHSTRAP_ADMIN_GROUP` (D-029) | M |
| FR-AUTH-05 | Admin can deactivate an agent; a deactivated agent is rejected on the next request | M |
| FR-ADMIN-01 | Admin manages products: key, name, branding (display name, logo, accent colour, from-address, reply-to), active flag | M |
| FR-ADMIN-02 | Admin creates and revokes Trusted and Public API keys per product; the plain key is shown once; only a hash is stored | M |
| FR-ADMIN-03 | Admin manages tags (slug, colour); agents apply them; customers never see tags | M |
| FR-ADMIN-04 | Admin changes agent role and active flag | M |
| FR-ADMIN-05 | Every product, key, agent and tag change, and every erase-requester, delete-ticket and dead-letter retry/discard action, writes an `AdminEvent` (audit trail, D-006), viewable by admins | M |
| FR-ADMIN-06 | Each agent edits their own notification preferences | M |
| FR-ADMIN-07 | Each agent can set or clear an optional public display name (max 60 characters, plain text, no `@`) in "My settings" with a live preview of what customers see (D-024) | M |

### 5.7 Live updates (FR-LIVE)

| ID | Requirement | Pri |
| --- | --- | --- |
| FR-LIVE-01 | Queue and ticket detail refresh when tickets change, without manual reload | M |
| FR-LIVE-02 | A "viewing / replying" presence hint shows other agents on the same ticket | S |
| FR-LIVE-03 | The SignalR hub on the API is authenticated by the agent JWT | M |
| FR-LIVE-04 | Changes made by the worker (auto-close) reach clients through Postgres `LISTEN/NOTIFY` and the API relay (D-007) | M |
| FR-LIVE-05 | Live updates are best-effort; correctness never depends on them (UI also refetches on action and on reconnect) | M |

### 5.8 Client SDK (FR-SDK)

| ID | Requirement | Pri |
| --- | --- | --- |
| FR-SDK-01 | `TechStrap.Client` NuGet: typed client over `TechStrap.Contracts` for submitting tickets with an API key | M |
| FR-SDK-02 | `TechStrap.Client.Maui`: captures device and app metadata and offers a submit-ticket helper | M |
| FR-SDK-03 | SDK targets the published OpenAPI contract; versioned and published to NuGet.org by CI | M |
| FR-SDK-04 | Sample usage documented for Trusted (server) and Public (client) keys | M |
| FR-SDK-05 | The SDK sends an `Idempotency-Key` per submit and retries submit only when a key is set (D-020) | S |

### 5.9 Privacy (FR-PRIV)

| ID | Requirement | Pri |
| --- | --- | --- |
| FR-PRIV-01 | Admin (only, D-022) can erase a requester: anonymize requester identity and their messages, delete their attachments, revoke their tokens; tickets and events remain for statistics | M |
| FR-PRIV-02 | Admin (only, D-022) can hard-delete a ticket and its messages, attachments, events | M |
| FR-PRIV-03 | Serilog output redacts PII (emails, tokens, API keys) | M |
| FR-PRIV-04 | Erasure and deletion are recorded as `AdminEvent` records without retaining the erased values | M |

### 5.10 Operations (FR-OPS)

| ID | Requirement | Pri |
| --- | --- | --- |
| FR-OPS-01 | Every container exposes `/health/live` and `/health/ready` | M |
| FR-OPS-02 | API runs EF migrations on startup under an advisory lock; no other host migrates | M |
| FR-OPS-03 | Seed and demo data for local development | M |
| FR-OPS-04 | `Build-TechStrapDocker.ps1` builds multi-arch images for api, admin, portal, worker; GitVersion drives tags | M |
| FR-OPS-05 | Compose files for local, UAT, production with a pinned subnet | M |
| FR-OPS-06 | Backup and restore runbook (`pg_dump` plus storage volume) | M |
| FR-OPS-07 | OpenAPI document served at `/openapi/v1.json` | M |

## 6. Non-functional requirements

### Traffic and scale (assumption supplied for sizing)

- Small company: fewer than 50 agents (concurrent agents fewer than 20), fewer than 5,000 tickets per month (about 170 per day), peak intake well under 1 request per second.
- Data growth: about 60k tickets per year plus messages and attachments. Postgres FTS with GIN indexes is sufficient; no external search engine (D-011).
- Single instance of each container is the supported deployment. SignalR presence is in-memory (D-007).

### Targets (all **Assumption** unless noted)

| Area | Target |
| --- | --- |
| Performance | p95 queue list (50 rows, filters, search) under 500 ms at 100k tickets; p95 ticket detail under 300 ms; p95 intake under 1 s excluding attachment upload time |
| Live update latency | Under 2 s from commit to browser, best effort |
| Email | 95% of outbox rows sent within 2 minutes when SMTP is up |
| UAT footprint | Whole stack (4 .NET containers + Postgres) within about 1.5 GB RAM and 2 vCPU at idle-to-light load, alongside the existing observability stack and CI/CD on the same box; each .NET container idle under about 250 MB |
| Availability | Single-node, best-effort; 99% monthly target for the owner's instance. Planned downtime allowed for upgrades. Email and live updates degrade independently of intake |
| Recovery | RPO 24 h (nightly `pg_dump` plus storage volume copy); RTO 4 h from backup. Restore is rehearsed in PHASE-12 |
| Observability | Structured Serilog logs with correlation id, OpenTelemetry traces and metrics through `SyntaxCircus.Observability`, health endpoints, outbox depth and dead-letter count metrics, hub connection count |
| Security | OWASP ASVS L1 as a working checklist; security headers via `AspNetCore.Common`; default-deny authorization fallback; secrets only in env files |
| Portability | Linux amd64 and arm64 images; any OIDC IdP; SMTP server of choice |
| Maintainability | Architecture tests enforce project-reference direction; handler and boundary tests per phase; no EF types outside Infrastructure |
| Accessibility | Portal and Admin target WCAG 2.1 AA for core flows (**Assumption**; detail in UX briefs) |

## 7. Data, security and privacy

- **PII held:** requester email and name, message bodies, attachment files, ticket metadata (device and app info, possibly personal), agent name and email. IP addresses appear only in transient logs and rate-limit state, not in the database (**Assumption**).
- **Hashing:** access tokens and API keys are stored as hashes (`IAccessTokenService`, `IApiKeyHasher`); only a short display prefix of a key is kept.
- **Content safety:** message bodies are sanitized on write (HtmlSanitizer); KB Markdown is rendered with Markdig and sanitized on output (D-014). Attachment type allowlist and size limits at intake. Files are served with `Content-Disposition: attachment` and `nosniff` (**Assumption**).
- **Spam:** honeypot, per-IP and per-key rate limits, body and attachment limits, spam flag. No CAPTCHA in core.
- **Erasure:** FR-PRIV-01 to -04. Backups may still contain erased data until they age out; the runbook states a backup retention period (**Assumption**: 14 days).
- **Retention:** no automatic purge in core; deferred (D-006). The owner decides retention periods later.
- **Transport:** TLS terminates at the reverse proxy outside compose. Internal network traffic is plain HTTP on the pinned compose subnet.
- **Authorization:** claim-gated agents (D-004), Admin-only operations (product, key, agent and tag management, erase, delete, dead letters, audit; D-022). Mark spam is an Agent action. Resource-level checks (token to ticket, key to product) live in handlers.
- **Anti-enumeration:** uniform 404 for tokens, uniform response for lost-link, no ticket-number lookup without a token.
- **Compliance:** no formal regime in scope. Self-hosters are responsible for their own legal basis and privacy notice. The docs ship a short privacy guidance page (PHASE-12, **Assumption**).

## 8. Assumptions

| ID | Assumption |
| --- | --- |
| A-01 | Traffic: fewer than 50 agents, fewer than 5k tickets per month; single instance per container |
| A-02 | Auto-close default is 7 days after Solved (confirmed, D-008) |
| A-03 | Customer token expiry is 90 days sliding |
| A-04 | Attachment limits are 10 MB per file and 25 MB per message with a type allowlist |
| A-05 | Outbox dead-letters after 8 attempts with exponential backoff |
| A-06 | English only for UI and email, with an i18n seam |
| A-07 | Postgres FTS uses the `english` configuration |
| A-08 | Default rate limits: public form 5 submits per 10 min per IP; Public key 10 creates per min per key+IP; token endpoints 60 per min per IP; lost-link 3 per hour per address; all configurable and validated at startup |
| A-09 | Compose subnet `172.16.31.0/24` (needs a row added to the _template pattern registry) |
| A-10 | Assignee notification on customer reply is an email through the outbox; in-app badges later |
| A-11 | Priorities are `Low, Normal, High, Urgent`; default Normal |
| A-12 | Spam-flagged tickets are hidden from default views and excluded from alerts; spam does not auto-close |
| A-13 | Agent reply to a New or Open ticket sets Pending; customer reply to Pending or Solved sets Open |
| A-14 | The Deflection panel on the contact form is an interactive island; the rest of the portal is static SSR |
| A-15 | Resolved by D-016: handlers accept `TechStrap.Contracts` request records directly; Contracts is dependency-free (no MVC, EF, ASP.NET, attributes); transport-only details are mapped at the controller |
| A-16 | Customer-token endpoints receive the token in the `X-Ticket-Token` header, not the URL path, between portal and API so it stays out of API access logs |
| A-17 | Logo for product branding is stored as an uploaded image via storage, or an HTTPS URL (final choice in PHASE-04) |
| A-18 | Backups retained 14 days |
| A-19 | The storage volume (`/app/storage`) is shared by the API and the Worker; the API is the only writer of intake attachments and KB images, and the Worker mounts it for cleanup and later inbound-email work |

## 9. Risks

| ID | Risk | Impact | Mitigation |
| --- | --- | --- | --- |
| R-01 | Spam or abuse through public form and Public keys | Inbox flood, email cost | Honeypot, rate limits, limits on size, spam flag; Turnstile if needed |
| R-02 | Client-embedded Public key is extractable | Ticket flood for one product | Create-only, key+IP rate limit, untrusted metadata, instant revoke and rotation |
| R-03 | Email deliverability (SPF, DKIM, reply-to) | Customers miss links | Document SMTP setup; lost-link flow; dead-letter visibility |
| R-04 | Stored XSS through message bodies or KB | Agent session compromise | Sanitize on write and output; CSP via security headers; tests |
| R-05 | Token leakage through logs, referrers, forwarded email | Ticket exposure | Redaction, `Referrer-Policy: no-referrer`, sliding expiry, revoke, hashed storage |
| R-06 | LISTEN/NOTIFY dropped connection loses worker-originated live updates | Stale UI | Reconnect with resync, UI refetch, best-effort contract (FR-LIVE-05) |
| R-07 | In-memory presence breaks if API is scaled out | Wrong hints | Single instance supported; backplane is a later decision |
| R-08 | Heavy Blazor Server circuits on small box | Memory pressure | Agent count is small; circuit limits; measure in PHASE-12 |
| R-09 | Migrate-on-startup in API conflicts with rolling restarts | Startup stall | Advisory lock; single instance; documented upgrade order |
| R-10 | Package versions unverified at write time | Build breaks | Versions locked and verified in `03-PACKAGE-MAP.md` and PHASE-01 |
| R-11 | Scope creep from the later sub-projects | Delay | Non-scope list; reserved columns only |
| R-12 | Erasure incomplete (backups, logs, outbox bodies) | Privacy gap | Outbox rows purged on erasure; backup retention stated; log redaction |
| R-13 | OIDC group-claim shape differs between IdPs | Self-hosters blocked | Claim name and values configurable; documented for Authentik and generic OIDC |

## 10. Open questions

| ID | Question | Default until resolved |
| --- | --- | --- |
| Q-01 | Which handler covers listing/downloading the KB articles linked to a ticket reply for the customer view? | Included in `GetCustomerTicketRequestHandler` and `GetTicketRequestHandler` output |
| Q-02 | Is a handler needed for periodic expired-token cleanup? | Not in core; expired tokens are inert and cleaned in a later maintenance task |
| Q-03 | Should an agent be able to resend or revoke a customer's access link? | Not in core; customer uses lost-link |
| Q-04 | Should customers receive an email when a ticket auto-closes? | No. Auto-close sends no email; the Solved notice already told the customer about the window (D-037) |
| Q-05 | Which claim name and value format will Authentik emit for groups (`groups` array)? | Configurable via env; default claim `groups` |
| Q-06 | Product logo: upload or URL (A-17)? | URL or upload, decided in PHASE-04 |
| Q-07 | Registry visibility and package ownership for NuGet publishing of the SDK (org vs personal key)? | CI uses an org secret |
| Q-08 | Reverse proxy address and trusted network values for UAT and production | Supplied through env at deploy time; Unknown |
| Q-09 | Is a queue-count handler (badges per view) needed beyond `ListTicketsRequestHandler`? | Counts come from `ListTicketsRequestHandler` response |
| Q-10 | Should portal-side KB category article listing use `SearchPublicKbArticlesRequestHandler` with a category filter, or need a dedicated handler? | Use the search handler with category filter; raise a new handler through the decision log if it proves wrong |
| Q-11 | Sitemap needs: is `GetSitemapEntriesRequestHandler` enough for products plus articles plus categories? | Yes |
| Q-12 | Handler request types: Contracts types directly (A-15) or Application-owned records mapped in controllers? | Resolved: Contracts types directly (D-016) |
| Q-13 | Does the portal need a handler for fetching a ticket's attachments list separately from `GetCustomerTicketRequestHandler`? | No; attachments are part of the ticket view DTO |
| Q-14 | Should an agent be able to reopen a Closed ticket? | No; Closed is read-only, follow-ups are new tickets |
