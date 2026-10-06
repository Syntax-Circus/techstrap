# TechStrap Architecture

Status: Draft for review (2026-10-02). Requirements: [01-REQUIREMENTS.md](01-REQUIREMENTS.md). Decision log: `04-DECISION-LOG.md` (D-001 to D-031, referenced below). Follows the _template APPLICATION_ARCHITECTURE.md and RAZOR_COMPONENT_ARCHITECTURE.md (named by file; guides are not copied into this repo).

## 1. Topology

One solution, one repository, four application images plus Postgres.

| Container | Image | Role | Exposure | Talks to |
| --- | --- | --- | --- | --- |
| `techstrap-api` | `ghcr.io/syntax-circus/techstrap-api` | Only request-time DB client. HTTP API (Controllers), business logic via handlers, SignalR `TicketHub`, Postgres `LISTEN` relay, runs migrations on startup (advisory lock), writes attachments and KB images to storage | Public intake and customer endpoints; agent endpoints need OIDC JWT | Postgres, storage volume |
| `techstrap-admin` | `ghcr.io/syntax-circus/techstrap-admin` | Blazor Server agent app. Typed API clients; forwards the agent's OIDC token (`Blazor.Auth`); connects to the SignalR hub server-side | Behind reverse proxy (agents only) | API, OIDC provider |
| `techstrap-portal` | `ghcr.io/syntax-circus/techstrap-portal` | Blazor SSR public site: product home, contact form, customer ticket view, lost-link, KB. Typed API clients, forwards visitor IP | Public via reverse proxy | API |
| `techstrap-worker` | `ghcr.io/syntax-circus/techstrap-worker` | Drains email outbox, auto-closes Solved tickets, emits `NOTIFY` (Postgres implementation of `ITicketChangeBroadcaster`, D-018). Runs Application handlers directly against the DB (D-012). Later hosts IMAP poller and workflow engine | None | Postgres, SMTP, storage volume |
| `postgres` | `postgres:17` | Single data store including FTS and `LISTEN/NOTIFY` | Internal network only | n/a |
| Reverse proxy (Caddy) | external | TLS, forwards client IP | Public | portal, admin, api |
| OIDC provider | external | Agent identity | Public | n/a |

Rules:

- Admin and Portal never reference Application, Infrastructure or Domain and never touch the database. They call the API through typed clients over `TechStrap.Contracts` (D-005 for the SDK reuse of Contracts). Their only server-hosted API pass-throughs are the two attachment download adapters (D-017).
- Worker and API both compose Application + Infrastructure. Neither calls the other over HTTP; the worker notifies the API through Postgres only (D-007).
- Only the API container migrates the database. Start order: API healthy, then the others (compose `depends_on`). In the local compose the API also waits for its Postgres; the deploy compose uses a separate Postgres (D-043).
- The storage volume (`techstrap-storage`, mounted at `/app/storage`) is mounted by the API only (D-043; A-19 amended). The API is the only reader and writer of intake attachments and KB images; the Worker mounts it again when it needs files (inbound email, PHASE-11).

## 2. Project layout and reference direction

```text
techstrap/
  TechStrap.slnx  Directory.Build.props/targets  Directory.Packages.props  global.json  GitVersion.yml
  Build-TechStrapDocker.ps1  Dockerfile.{api,admin,portal,worker}
  docker-compose.yml  .env.example  deploy/{docker-compose.yml,.env.*.example}
  docs/  (architecture/, BRAND.md, runbooks/)
  src/
    TechStrap.Domain          entities, enums, status transition rules, domain constants
    TechStrap.Application     handlers (I...Handler), request models, repository/service interfaces
    TechStrap.Infrastructure  EF Core (DbContext, migrations), repositories, outbox store, storage,
                              hashing, sanitizer, Markdown, email templates, notification planner
    TechStrap.Contracts       DTOs/Request/Response shared by Api, Admin, Portal, Client, tests; wire constants
    TechStrap.Hosting         log redaction (PiiRedactionEnricher) and Sentry header scrubbing shared by Api, Worker, Admin and Portal
    TechStrap.Api             Controllers, TicketHub, LISTEN hosted service, auth, rate limits, OpenAPI
    TechStrap.Admin           Blazor Server, typed clients, ViewModels, SCSS
    TechStrap.Portal          Blazor SSR, typed clients, ViewModels, SCSS
    TechStrap.Worker          Hosted loops: outbox drain, auto-close
    TechStrap.Client          SDK over Contracts (NuGet)
    TechStrap.Client.Maui     MAUI helper over Client (NuGet)
  tests/
    TechStrap.Domain.Tests  TechStrap.Application.Tests  TechStrap.Infrastructure.IntegrationTests
    TechStrap.Api.Tests  TechStrap.Architecture.Tests
    TechStrap.Admin.Tests  TechStrap.Portal.Tests  TechStrap.Client.Tests
```

The first five test projects are created in PHASE-01; `TechStrap.Admin.Tests` (bUnit), `TechStrap.Portal.Tests` (bUnit and host tests) and `TechStrap.Client.Tests` are created in their owning phases (PHASE-07, PHASE-09, PHASE-11).

Allowed project references (enforced by `TechStrap.Architecture.Tests`, PHASE-01):

| Project | May reference |
| --- | --- |
| Domain | none (BCL only) |
| Contracts | none (BCL only; no MVC, no EF) |
| Hosting | none (packages only: Serilog and Observability) |
| Application | Domain, Contracts |
| Infrastructure | Application, Domain, Contracts |
| Api | Application, Infrastructure (composition root only), Contracts, Hosting |
| Worker | Application, Infrastructure (composition root only), Contracts, Hosting |
| Admin | Contracts, Hosting |
| Portal | Contracts, Hosting |
| Client | Contracts |
| Client.Maui | Client, Contracts |

`TechStrap.Hosting` is a leaf with the log redactor and the Sentry header scrubber that every request-serving host shares (D-039, D-040). It references no TechStrap project. Admin and Portal still never reference Application, Infrastructure or Domain.

`TechStrap.Contracts` is a dependency-free leaf: no MVC, EF or ASP.NET references and no attributes. Handlers accept Contracts request records directly; transport-only details (multipart files, `X-Api-Key`, `X-Ticket-Token`, `Idempotency-Key`, honeypot, channel and trust level) are mapped at the controller into the request record or a small Application-owned model (D-016, resolves A-15).

Forbidden: Application to Infrastructure, anything to Api/Admin/Portal/Worker, Admin or Portal to Domain/Application/Infrastructure, Contracts to anything, EF or ASP.NET types in Application or Domain.

Other conventions: handlers sealed with `I…Handler` interfaces; constants and enums scoped to the narrowest project (status strings crossing the wire live in Contracts); ViewModels only in Admin/Portal feature folders; DTOs end in `Dto`/`Request`/`Response`; `TimeProvider` injected, never `DateTime.UtcNow`.

## 3. Component responsibilities

| Component | Responsibility |
| --- | --- |
| Controllers (Api) | Bind input, apply authorization policy and rate-limit policy, call one handler via `[FromServices]`, map `Result` through `SyntaxCircus.AspNetCore.Common` to ProblemDetails, select the success response |
| Handlers (Application) | Own use-case flow and business rules; depend only on application interfaces; return `Result`/`Result<T>`; propagate cancellation |
| Domain | Ticket status transition table, ticket number format, token and key value rules, event types, constants |
| Repositories (Infrastructure) | EF-backed persistence behind `ITicketRepository`, `IRequesterRepository`, `IProductRepository`, `IAgentRepository`, `ITagRepository`, `IKbRepository`; FTS queries; no `IQueryable` leaks |
| `IUnitOfWork` | Atomic multi-write (ticket + message + event + token + outbox rows) in one DB transaction |
| `ITicketNumberAllocator` | Per-product atomic sequence increment inside the creating transaction |
| `IEmailOutbox` / `IEmailOutboxStore` | Handlers enqueue rows in the same transaction; worker claims, acks, retries, dead-letters (`SKIP LOCKED`) |
| `ITicketNotificationPlanner` | Decides which emails and alerts a change produces (customer reply, agent reply, new ticket alerts, assignment, solved notice) and enqueues them; called by ticket handlers. Introduced in PHASE-06, which also adds it to `SubmitTicketRequestHandler` (PHASE-05 queues only the requester confirmation) |
| `IAttachmentStore` | Over `SyntaxCircus.Storage`; size/type checks, streams, deletion on erase |
| `IAccessTokenService` | Generate 256-bit tokens, hash, validate, slide expiry, revoke |
| `IApiKeyHasher` | Generate and hash product API keys; constant-time verify; kind-aware |
| `IHtmlSanitizer` / `IMarkdownRenderer` | HtmlSanitizer for message bodies; Markdig plus sanitizer for KB (D-014) |
| `IEmailTemplateRenderer` | Text and HTML templates with product branding |
| `ITicketChangeBroadcaster` | Application interface with two implementations: SignalR groups (API) and Postgres `NOTIFY` (Worker). Called by the Infrastructure post-commit interceptor `TicketChangePublishingInterceptor` and by `RelayTicketChangeHandler`; ticket handlers never call it and never see the hub (D-018) |
| `ICurrentUserService` | Identity for the API-key and customer principals (PHASE-05), from `SyntaxCircus.Common`; ASP.NET implementation owns claims. Agent identity is `ICurrentAgentClaims` (see its row below) |
| `TicketHub` (Api) | Agent-JWT SignalR hub; `JoinTicket`/`LeaveTicket`/`SetComposing` delegate to `UpdateTicketPresenceHandler` |
| Postgres listener (Api hosted service) | Dedicated connection `LISTEN techstrap_ticket_changes`, passes payloads to `RelayTicketChangeHandler`, reconnects with backoff |
| Worker loops | Poll outbox (`EmailOutboxWorker` to `DrainEmailOutboxHandler`), scheduled auto-close (`AutoCloseWorker` to `AutoCloseSolvedTicketsHandler`); own cadence, cancellation, error logging; map handler outcome to loop behavior |
| Admin / Portal | Presentation only; typed clients with `Http.Resilience`; Portal adds `.AddForwardedClientIp()` (D-019) |
| Client / Client.Maui | Submit-ticket SDK and device/app metadata capture |

### 3.1 Application abstractions

Interfaces live in `TechStrap.Application`; implementations in `TechStrap.Infrastructure` unless noted. Handlers depend only on this set (enforced by `HandlerConstructorDependencyTests`).

| Abstraction | Implemented by | Purpose and notes |
| --- | --- | --- |
| `ITicketRepository`, `IRequesterRepository`, `IProductRepository`, `IAgentRepository`, `ITagRepository`, `IKbRepository` | EF repositories | Persistence per aggregate; FTS inside repositories |
| `IAdminEventRepository` | EF repository | Append and query `admin_events` (config, erase, delete and dead-letter audit). Used by config handlers and `ListAdminEventsRequestHandler`. Resolves OQ-1 |
| `IUnitOfWork` | EF unit of work | Atomic multi-write; translates concurrency conflicts to `Result` |
| `ITicketNumberAllocator` | EF allocator | Per-product atomic sequence |
| `IEmailOutbox`, `IEmailOutboxStore` | EF outbox | Enqueue in the caller's transaction; worker claim, ack, retry, dead-letter |
| `IOutboundEmailSender` | `SmtpOutboundEmailSender` over `SyntaxCircus.Email.IEmailSender` (SMTP; in-memory sender in tests) | Sends a rendered message; used only by `DrainEmailOutboxHandler` (D-033) |
| `IEmailTemplateRenderer` | Template renderer | Text and HTML templates with product branding |
| `ITicketNotificationPlanner` | `TicketNotificationPlanner` over `IEmailOutbox` | Stages outbox template data in the caller's transaction; the Worker renders at send time (D-033). Recipient rules (PHASE-06) |
| `IMarkdownRenderer` | Markdig renderer | Markdig, raw HTML off, always followed by `IHtmlSanitizer` (D-035) |
| `IAttachmentStore` | Over `SyntaxCircus.Storage` | Ticket attachments: size and type checks, streams, deletion |
| `IKbImageStore` | Over `SyntaxCircus.Storage` | KB images under the public-read `kb-images/` prefix, served by `GET /kb-images/{name}` (PHASE-08, D-044) |
| `IKbContentRenderer` | KB Markdig profile and sanitiser | The KB preview and the public article page share it; agent replies keep `IMarkdownRenderer` and `IHtmlSanitizer` (D-044) |
| `IApiKeyHasher` | `ApiKeyHasher` (Infrastructure, PHASE-04) | Generate, hash, verify (constant time). `IAccessTokenService` follows in PHASE-05 |
| `IIntakeIdempotencyStore` | EF store | `Idempotency-Key` lookups, 24 h retention (D-020) |
| `IHtmlSanitizer` | HtmlSanitizer | D-014 |
| `ITicketChangeBroadcaster` | SignalR (API), Postgres `NOTIFY` (Worker) | D-018. Resolves OQ-2 |
| `ITicketPresenceStore` | In-memory store (API, single instance) | Viewing and composing state with TTL for `UpdateTicketPresenceHandler`. Resolves OQ-3 |
| `IDevelopmentDataSeeder` | `DevelopmentDataSeeder` | Dev-only startup step (section 7.6); not a use-case entry point |
| `ICurrentAgentClaims`, `TimeProvider` | `ClaimsCurrentAgentClaims` (Api, over `IHttpContextAccessor`); framework clock | Agent identity (subject, name, email, group-derived role, D-029) and time. `SyntaxCircus.Common.ICurrentUserService` is not used because it carries no groups |
| Options types | Bound in each host | `AgentAccessOptions`, `PortalLinkOptions`, `AutoCloseOptions`, `EmailBrandingOptions`, `EmailOutboxWorkerOptions`, rate-limit options; validated on start |

## 4. Authentication and authorization schemes

| Caller | Scheme | Credential location | Power | Policy / limits |
| --- | --- | --- | --- | --- |
| Agent / Admin | OIDC JWT bearer; Admin app does the code flow and forwards the token (`Blazor.Auth`) | `Authorization: Bearer` | Agent surface; Admin-only operations (product, key, agent and tag management, erase, delete, dead letters, audit; D-022) | Policy `Agent` requires `TECHSTRAP_AGENT_GROUP` or `TECHSTRAP_ADMIN_GROUP`; policy `Admin` requires `TECHSTRAP_ADMIN_GROUP`; the claim type is `TECHSTRAP_GROUP_CLAIM_TYPE` (default `groups`); no claim means 403; deactivated agent rejected. Roles come from IdP groups only (D-004, D-029) |
| Product app, Trusted key | `SyntaxCircus.AspNetCore.Authentication` API key scheme | `X-Api-Key` | Create a ticket for that product; may set external user ref and trusted metadata | Server-side use only; per key + client IP limit (generous) (D-001); optional `Idempotency-Key` (D-020) (`ApiKey` policy; JSON body in v1, D-034) |
| Product app, Public key | Same scheme, key kind `Public` | `X-Api-Key` | Create-only; metadata flagged untrusted; cannot set external ref | Per key + client IP rate limit (D-001); optional `Idempotency-Key` (D-020) (`ApiKey` policy; JSON body in v1, D-034) |
| Customer | Per-ticket access token (hashed, revocable, sliding expiry) | Portal sends in header `X-Ticket-Token` to API (A-16); `/t/{token}` in browser URL | Read public messages, post reply, download public attachments | Uniform 404 on invalid; per-IP limit; token redacted from logs |
| Anonymous public | None; the explicit `Public` policy, declared by each public controller (D-034), on explicit endpoints only | n/a | Submit via web form for a product key in the route, read public product branding, read published KB, lost-link request, and customer-token routes (ticket read, reply, attachment, lost link; token checked in the handler, D-038) | Honeypot, per-IP limits, size limits; default-deny fallback elsewhere |

Authorization split: host policies at the controller; resource checks (key belongs to product, token belongs to ticket, attachment visibility) in handlers.

Header names are constants in `TechStrap.Contracts` and are the same everywhere: `X-Api-Key`, `X-Ticket-Token`, `Idempotency-Key`. Policy split (D-022): delete ticket, erase requester, dead-letter list/retry/discard, product, API key, agent and tag management and the audit log are Admin-only; mark spam, ticket handling, KB articles and category create/update are Agent; product, tag and active-agent lists are Agent-readable.

## 5. Data model summary

All tables snake_case, UTC `timestamptz`, `uuid` primary keys unless noted (**Assumption**: v7 GUIDs). Tickets, products, requesters and KB articles carry the Postgres `xmin` system column as their concurrency token (mapped as `Version`; there is no separate `row_version` column).

| Entity (table) | Key columns | Indexes and constraints |
| --- | --- | --- |
| `products` | `id`, `key` (unique slug), `name`, `number_prefix` (unique), branding (`display_name`, `logo`, `accent_colour`, `from_address`, `reply_to`), `is_active`, `xmin` (concurrency token) | unique `key`, unique `number_prefix` |
| `product_ticket_sequences` | `product_id` (PK, FK to `products`), `next_number` | the per-product ticket counter, separate from `products` so taking a number never changes the product row's `xmin` (D-009) |
| `product_api_keys` | `id`, `product_id`, `kind` (`Trusted`/`Public`), `key_hash`, `key_prefix`, `label`, `created_at`, `revoked_at`, `last_used_at` | unique `key_hash`; index `product_id` |
| `agents` | `id`, `oidc_subject` (unique), `name`, `email`, `role` (`Agent`/`Admin`), `is_active`, `last_seen_at`, `public_display_name` (nullable, max 60; customer-facing name override, D-024) | unique `oidc_subject`; index `email` |
| `agent_notification_preferences` | `agent_id`, `product_id`, `notify_new_ticket` | PK (`agent_id`, `product_id`) |
| `requesters` | `id`, `email` (citext unique), `name`, `external_user_ref`, `erased_at`, `xmin` (concurrency token) | unique `email`; index `external_user_ref` |
| `tickets` | `id`, `number` (stored full number, e.g. `ACME-142`), `product_id`, `requester_id`, `subject`, `status`, `priority`, `assignee_id`, `channel` (`Web`/`Api`), `is_spam`, `parent_ticket_id` (FK to `tickets`, ON DELETE SET NULL, D-039), `metadata` jsonb, `metadata_trusted` bool, `custom_fields` jsonb (reserved), `created_at`, `first_response_at`, `solved_at`, `closed_at`, `last_activity_at`, `search_vector`, `xmin` (concurrency token) | unique `number` (global, D-009); indexes (`product_id`, `status`, `last_activity_at`), (`assignee_id`, `status`), `requester_id`, `parent_ticket_id`, partial `status = Solved` on `solved_at`; partial `is_spam = true` on `last_activity_at` (Spam view); GIN `search_vector` |
| `messages` | `id`, `ticket_id`, `author_type` (`Requester`/`Agent`/`System`), `author_id`, `visibility` (`Public`/`Internal`), `body` (sanitized), `message_id`, `in_reply_to` (reserved, nullable), `created_at`, `search_vector` | index (`ticket_id`, `created_at`); GIN `search_vector` |
| `attachments` | `id`, `ticket_id`, `message_id`, `file_name`, `content_type`, `size`, `storage_key`, `created_at` | index `message_id`, `ticket_id` |
| `ticket_events` (append-only) | `id`, `ticket_id`, `type`, `actor_type`, `actor_id`, `payload` jsonb, `occurred_at` | index (`ticket_id`, `occurred_at`); no update or delete except hard-delete cascade |
| `tags` / `ticket_tags` | `tags(id, slug unique, name, colour)`; `ticket_tags(ticket_id, tag_id)` | PK (`ticket_id`, `tag_id`); index `tag_id` |
| `ticket_access_tokens` | `id`, `ticket_id`, `requester_id`, `token_hash`, `expires_at`, `revoked_at`, `last_used_at` | unique `token_hash`; index (`ticket_id`, `requester_id`) |
| `email_outbox` | `id`, `kind`, `to_address`, `payload` jsonb, `product_id`, `ticket_id`, `status` (`Pending`/`Sending`/`Sent`/`DeadLettered`/`Discarded`), `attempts`, `next_attempt_at`, `claimed_by`, `locked_until`, `last_error`, `created_at`, `sent_at` | partial index (`next_attempt_at`) where `status = Pending`; index `status`; (`kind`, `to_address`, `created_at`) `ix_email_outbox_kind_to_address_created_at`; partial (`created_at`) where `status IN (Sent, Discarded)` `ix_email_outbox_created_at_when_finished` (retention sweep) |
| `kb_categories` | `id`, `product_id` (nullable = shared), `name`, `slug`, `sort_order` | unique (`product_id`, `slug`) |
| `kb_articles` | `id`, `product_id` (nullable), `category_id`, `slug`, `title`, `summary`, `body_markdown`, `status`, `author_id`, `created_at`, `updated_at`, `published_at`, `search_vector`, `xmin` (concurrency token) | unique (`product_id`, `slug`) with nulls treated equal; GIN `search_vector`; index (`status`, `published_at`) |
| `ticket_articles` | `ticket_id`, `message_id`, `article_id` | PK (`message_id`, `article_id`) |
| `admin_events` | `id`, `type`, `actor_id`, `subject_type`, `subject_id`, `payload` jsonb (never holds erased values or secrets), `occurred_at`. Types cover product, key, agent and tag changes plus erase-requester, delete-ticket and dead-letter retry/discard (D-006) | index (`occurred_at`), (`subject_type`, `subject_id`) |
| `intake_idempotency_keys` | `id`, `api_key_id`, `key_hash`, `ticket_id`, `response` jsonb, `created_at` (24 h retention, D-020) | unique (`api_key_id`, `key_hash`); index `created_at` |

Full-text search (D-011):

- `tickets.search_vector`: subject (weight A); an exact ticket number is matched by equality, and the requester is not in the vector because a generated column reads only its own row (D-027). `messages.search_vector`: body text (B), Public and Internal both indexed; internal hits only returned to agents (all ticket search is agent-only).
- `kb_articles.search_vector`: title (A), summary (B), body (C). Public search filters `status = Published`.
- Config `english` (A-07); stored generated columns with GIN indexes, no triggers (D-027). Queries use `websearch_to_tsquery` and `ts_rank`.
- No external search engine.

Ticket number: `ITicketNumberAllocator` runs `INSERT INTO product_ticket_sequences ... ON CONFLICT (product_id) DO UPDATE SET next_number = next_number + 1 ... RETURNING` (the counter row is created on the product's first ticket) inside the creating transaction; the stored `tickets.number` is `{products.number_prefix}-{sequence}`, immutable (D-009), and unique globally on that stored value (a product move keeps the original prefix).

Status machine (Domain): `New -> Open | Pending | Solved`, `Open <-> Pending`, `Open | Pending -> Solved`, `Solved -> Open` (customer reply or agent), `Solved -> Closed` (auto-close or agent), `Closed` terminal. `is_spam` is orthogonal to status.

The ER diagram, the rules the database enforces and the migration list are in [05-SCHEMA.md](05-SCHEMA.md) (PHASE-03).

## 6. Main data flows

### 6.1 Intake (web form or app API)

1. Portal form posts multipart to `POST /api/public/products/{key}/tickets` (anonymous, honeypot, IP limit); apps call `POST /api/intake/tickets` with a key. Both call `SubmitTicketRequestHandler`; the controller supplies channel, trust level (from the principal, D-001), honeypot flag and files beside the Contracts request (D-016). An optional `Idempotency-Key` on the API-key route makes a repeat return the original ticket number with a fresh link (D-020, D-033).
2. Handler validates (limits, allowlist), sanitizes body, then in one `IUnitOfWork` transaction: upsert requester by email, allocate number, create ticket, first message, attachments (`IAttachmentStore`), `Created` event, access token and confirmation outbox row. PHASE-05 queues only the requester confirmation; PHASE-06 adds `ITicketNotificationPlanner` to this handler for new-ticket alert rows for opted-in agents.
3. Response (201 `SubmitTicketResponse`): API-key callers get the ticket number and view URL; the web form gets the number only, and the view link reaches the customer by email. Metadata from a Public key is stored with `metadata_trusted = false`.
4. After commit, the Infrastructure post-commit interceptor broadcasts to queue subscribers (D-018).

### 6.2 Agent reply

`AddAgentReplyRequestHandler`: authorize agent; reject if ticket Closed; sanitize body; one transaction: add Public message, `MessageAdded` event, status to Pending (A-13), set `first_response_at` if first, record linked KB articles, enqueue customer email with link; commit (the post-commit interceptor broadcasts, D-018). `AddInternalNoteRequestHandler` is the same without status change, link or email.

### 6.3 Customer reply

Portal posts to the API with the token header. `AddCustomerReplyRequestHandler`: validate token (uniform not-found on failure), sanitize, add message and event, Pending or Solved becomes Open, slide token expiry, planner enqueues assignee alert; commit (broadcast by the interceptor, D-018). If the ticket is Closed, see 6.7.

### 6.4 Outbox

Rows are written in the same transaction as their cause. `DrainEmailOutboxHandler` (worker loop, short poll interval, **Assumption** 5 s): claim a batch with `FOR UPDATE SKIP LOCKED` (`IEmailOutboxStore`), render with `IEmailTemplateRenderer`, send via `SyntaxCircus.Email`, ack on success; on failure increment attempts, set `next_attempt_at` by exponential backoff; after N attempts mark `DeadLetter`. At-least-once; outbox id in `Message-ID` (D-010). Admins retry or discard dead letters through their handlers (each writes an `AdminEvent`, D-022).

### 6.5 Auto-close

Worker scheduled loop (**Assumption** every 15 min) calls `AutoCloseSolvedTicketsHandler`: selects Solved tickets with `solved_at` older than `TECHSTRAP_AUTOCLOSE_DAYS`, sets Closed with `StatusChanged` event by System actor in one transaction per ticket (D-038); no customer email (D-037); the Worker's `NOTIFY techstrap_ticket_changes` publisher emits after commit (D-008, D-018).

### 6.6 Live updates

- API changes: the handler commits; the Infrastructure post-commit interceptor reads the `TicketEvent` rows committed in that unit of work and calls `ITicketChangeBroadcaster` (SignalR implementation), which sends to hub groups (`queue`, `ticket:{id}`). Ticket handlers are unchanged (D-018).
- Worker changes: the same interceptor runs in the Worker with the Postgres `NOTIFY` implementation of `ITicketChangeBroadcaster` (`pg_notify`, delivered on commit); the API hosted listener receives it and calls `RelayTicketChangeHandler`, which calls the SignalR `ITicketChangeBroadcaster`.
- Presence: `TicketHub` methods call `UpdateTicketPresenceHandler`, which holds viewing/composing state with TTL in `ITicketPresenceStore` (in-memory, single API instance) and broadcasts hints.
- Admin connects to the hub server-side with the agent's token and refreshes list and detail on events. Best-effort; UI also refetches after own actions and reconnects (D-007).

### 6.7 Follow-up after Closed

Customer reply to a Closed ticket reaches `AddCustomerReplyRequestHandler`, which creates a **new** ticket in the same product for the same requester: new number, `parent_ticket_id` = closed ticket, subject copied, first message = the reply, `Created` event with parent reference, new access token, confirmation email. The old ticket is unchanged. The response returns the new view URL; the portal redirects to it (D-008).

### 6.8 Erasure

`EraseRequesterRequestHandler` (Admin): in one transaction anonymize the requester (`email` to `erased-{id}@invalid`, `name` cleared, `erased_at`), replace the bodies of their own messages and the subjects of their tickets with the erasure marker `[erased]`, clear ticket metadata and custom fields, delete their attachments (rows in the transaction, files after the commit), revoke their tokens, delete every `email_outbox` row addressed to them or belonging to their tickets, and write an `AdminEvent` with counts only (D-006, D-039). `DeleteTicketRequestHandler` (hard delete of the ticket, with its messages, events, tokens and attachments; its outbox rows; surviving follow-ups are unlinked) likewise writes an `AdminEvent`. Both are Admin-only (D-022). The Worker also deletes `Sent` and `Discarded` outbox rows older than 90 days (D-039).

## 7. Application boundary table

Conventions:

- This section is the source of truth for routes, handler names and Contracts type names; phase documents match it.
- All controller handlers are bound as `[FromServices]` action parameters. Worker hosted services resolve scoped handlers from a fresh DI scope per iteration via `IServiceScopeFactory` (host types without per-method DI).
- Outcome mapping: handlers return `Result`/`Result<T>`; controllers call `ToActionResult` (`SyntaxCircus.AspNetCore.Common`) and pick the success response. Error kinds map: Validation 400, Unauthenticated 401, Forbidden 403, NotFound 404, Conflict 409 (includes concurrency conflicts), unexpected exceptions 500 via centralized ProblemDetails. Only the success shape and any non-default mapping are listed per row.
- Tests legend: **H** handler unit tests (`TechStrap.Application.Tests`, substitutes, no DB); **C** entry-point tests (`TechStrap.Api.Tests`: delegation, cancellation, policy, mapping); **I** integration with real Postgres (`TechStrap.Infrastructure.IntegrationTests`, Testcontainers); **W** worker host test.
- Infrastructure column abbreviations: **EF repos** = EF-backed implementations of the named repository interfaces; **UoW** = EF `IUnitOfWork`; **Outbox** = EF outbox enqueue and store.
- All rows also depend on `TimeProvider`; rows needing agent identity use `ICurrentAgentClaims` (PHASE-04 rows built that way), and rows needing the API-key or customer principal use `ICurrentUserService`. Admin events are written through `IAdminEventRepository` in the same transaction by the config handlers and by the erase, delete and dead-letter handlers (D-006, D-022). Ticket handlers do not depend on `ITicketChangeBroadcaster`; the Infrastructure post-commit interceptor broadcasts (D-018). Request records come from `TechStrap.Contracts` (D-016).
- Decision column: D-004 for agent-claim authorization, D-001 for key kinds, etc. "none" means no deviation from the template flow.

### 7.1 Agent auth and admin config (PHASE-04)

| Entry point/use case | Named handler | Application dependencies | Infrastructure implementations | Outcome mapping | Tests | Decision |
| --- | --- | --- | --- | --- | --- | --- |
| `GET /api/agents/me` (Agent policy) | `GetCurrentAgentRequestHandler` (provisions agent on first call; mirrors the group-derived role) | `IAgentRepository`, `ICurrentAgentClaims` | EF repos | 200 `AgentDto`; 403 deactivated agent | H, C, I | D-004, D-029 |
| `GET /api/agents` (Agent: active agents only, for assignment; Admin: all) | `ListAgentsRequestHandler` | `IAgentRepository`, `ICurrentAgentClaims` | EF repos | 200 paged `AgentDto` | H, C, I | D-004, D-022 |
| `PUT /api/agents/{id}` (Admin) active only | `UpdateAgentRequestHandler` | `IAgentRepository`, `IAdminEventRepository`, `ICurrentAgentClaims` | EF repos, UoW (agent + admin event) | 200 `AgentDto`; 409 on last-active-admin deactivation or concurrency | H, C, I | D-029, D-022 |
| `PUT /api/agents/me/notification-preferences` (Agent) | `UpdateNotificationPreferencesRequestHandler` | `IAgentRepository`, `IProductRepository`, `ICurrentAgentClaims` | EF repos | 204 | H, C, I | none |
| `GET /api/agents/me/notification-preferences` (Agent) | `GetMyNotificationPreferencesRequestHandler` | `IAgentRepository`, `IProductRepository`, `ICurrentAgentClaims` | EF repos | 200 `NotificationPreferenceDto[]` (every active product, default off) | H, C, I | none |
| `PUT /api/agents/me/profile` (Agent; sets or clears `public_display_name`) | `UpdateMyProfileRequestHandler` | `IAgentRepository`, `ICurrentAgentClaims` | EF repos | 204; 400 invalid name | H, C, I | D-024 |
| `GET /api/products` (Agent) | `ListProductsRequestHandler` | `IProductRepository`, `ICurrentAgentClaims` | EF repos | 200 `ProductDto[]` | H, C | none |
| `GET /api/products/{id}` (Agent) | `GetProductRequestHandler` | `IProductRepository`, `ICurrentAgentClaims` | EF repos | 200 `ProductDto`; 404 | H, C | none |
| `POST /api/products` (Admin) | `CreateProductRequestHandler` | `IProductRepository`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims` | EF repos, UoW | 201 `ProductDto`; 409 duplicate key or prefix | H, C, I | D-009 |
| `PUT /api/products/{id}` (Admin; incl. branding) | `UpdateProductRequestHandler` | `IProductRepository`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims` | EF repos, UoW | 200 `ProductDto`; 404; 409 | H, C, I | D-002 |
| `GET /api/products/{id}/api-keys` (Admin) | `ListProductApiKeysRequestHandler` | `IProductRepository` | EF repos | 200 `ProductApiKeyDto[]` (no secrets) | H, C | D-001 |
| `POST /api/products/{id}/api-keys` (Admin) | `CreateProductApiKeyRequestHandler` | `IProductRepository`, `IApiKeyHasher`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims` | EF repos, UoW, hasher | 201 `CreateProductApiKeyResponse` (plain key shown once); 404; 400 invalid kind | H, C, I | D-001 |
| `DELETE /api/products/{id}/api-keys/{keyId}` (Admin) | `RevokeProductApiKeyRequestHandler` | `IProductRepository`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims` | EF repos, UoW | 204; 404 | H, C, I | D-001 |
| `GET /api/tags` (Agent) | `ListTagsRequestHandler` | `ITagRepository` | EF repos | 200 `TagDto[]` | H, C | none |
| `GET /api/tags/summary` (Admin) | `ListTagSummariesRequestHandler` | `ITagRepository` | EF repos (one grouped count) | 200 `TagSummaryDto[]` | H, C | D-041 |
| `POST /api/tags` (Admin) | `CreateTagRequestHandler` | `ITagRepository`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims` | EF repos, UoW | 201 `TagDto`; 409 duplicate slug | H, C, I | none |
| `PUT /api/tags/{id}` (Admin) | `UpdateTagRequestHandler` | `ITagRepository`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims` | EF repos, UoW | 200 `TagDto`; 404; 409 | H, C | none |
| `DELETE /api/tags/{id}` (Admin) | `DeleteTagRequestHandler` | `ITagRepository`, `ITicketRepository`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims` | EF repos, UoW | 204; 404; 409 in use | H, C, I | none |
| `GET /api/admin-events` (Admin) | `ListAdminEventsRequestHandler` | `IAdminEventRepository`, `IAgentRepository` | EF repos | 200 paged `AdminEventDto` | H, C, I | none |

### 7.2 Intake, email and worker (PHASE-05)

| Entry point/use case | Named handler | Application dependencies | Infrastructure implementations | Outcome mapping | Tests | Decision |
| --- | --- | --- | --- | --- | --- | --- |
| `POST /api/public/products/{key}/tickets` (anonymous web form, multipart, `public-submit` limit) | `SubmitTicketRequestHandler` (channel `Web`, untrusted) | `IProductRepository`, `IRequesterRepository`, `ITicketRepository`, `ITicketNumberAllocator`, `IAccessTokenService`, `IAttachmentStore`, `IHtmlSanitizer`, `IEmailOutbox`, `IIntakeIdempotencyStore`, `IUnitOfWork`, `TimeProvider`, `IOptions<PortalLinkOptions>`, `ILogger`; `ITicketNotificationPlanner` is added in PHASE-06 | EF repos, UoW, allocator, token service, storage store, sanitizer, idempotency store, Outbox | 201 `SubmitTicketResponse` (ticket number only; the view link arrives by email); 400 limits/allowlist; 413 oversize; 404 unknown or inactive product; honeypot returns the same 201 shape to bots without creating a ticket (**Assumption**) | H, C, I | D-001, D-010, D-016 |
| `POST /api/intake/tickets` (Trusted or Public API key) | `SubmitTicketRequestHandler` (same use case; channel `Api`; trust from key kind) | same as above, plus `ICurrentUserService` for key principal (product id, kind) and `IIntakeIdempotencyStore` (optional `Idempotency-Key`) | same as above, plus idempotency store | 201 `SubmitTicketResponse` (number, view URL; a repeated `Idempotency-Key` returns the original ticket number with a fresh link; the stored response never holds the link, D-033); 400; 401 uniform for a bad key; external ref from a Public key is dropped with a warning and its metadata flagged untrusted (rule fixed in PHASE-05) | H, C, I | D-001, D-016, D-020 |
| Worker outbox loop (`EmailOutboxWorker` hosted service, resolves the scoped handler from a fresh DI scope per iteration via `IServiceScopeFactory`) | `DrainEmailOutboxHandler` (plain `Task` or `Result`; no caller branching other than loop delay) | `IEmailOutboxStore`, `IEmailTemplateRenderer`, `IProductRepository`, `IOutboundEmailSender`, `IOptions<EmailOutboxWorkerOptions>`, `ILogger` | Outbox store (`SKIP LOCKED`), SMTP sender | Loop: batch processed means poll again; empty or failure means delay; unexpected exception logged and loop continues | H, I, W | D-010, D-012 |
| `GET /api/public/products/{key}` (anonymous, `public` limit, Cache-Control) | `GetPublicProductRequestHandler` | `IProductRepository` | EF repos | 200 `PublicProductDto` (name, logo, accent; no secrets) with `Cache-Control: public, max-age`; 404 `no-store` | H, C, I | D-002 |
| `GET /api/public/products` (anonymous, `public` limit; for the Portal sitemap) | `ListPublicProductsRequestHandler` | `IProductRepository` | EF repos | 200 `PublicProductSummaryDto[]` (key and display name of the active products, by key, at most 1,000) with `Cache-Control: public, max-age=300` | H, C, I | D-045 |

### 7.3 Ticket operations (PHASE-06)

| Entry point/use case | Named handler | Application dependencies | Infrastructure implementations | Outcome mapping | Tests | Decision |
| --- | --- | --- | --- | --- | --- | --- |
| `GET /api/tickets` (Agent; views Unassigned, Mine, Open, Pending, All and Spam, filters, FTS, paging; the first five exclude `is_spam`, D-024) | `ListTicketsRequestHandler` | `ITicketRepository`, `IAgentRepository`, `IProductRepository`, `ITagRepository`, `ICurrentAgentClaims` | EF repos (FTS) | 200 `PagedResponse<TicketSummaryDto>`; 400 bad filter | H, C, I | D-011 |
| `GET /api/tickets/counts` (Agent; per-view counts for the signed-in agent) | `CountTicketViewsRequestHandler` | `ITicketRepository`, `ICurrentAgentClaims` | EF repos | 200 `TicketViewCountsResponse` | H, C, I | D-036 |
| `GET /api/tickets/{reference}` (Agent; ticket id or number) | `GetTicketRequestHandler` | `ITicketRepository`, `IKbRepository`, `IRequesterRepository`, `IAgentRepository`, `IProductRepository`, `ITagRepository`, `ICurrentAgentClaims` | EF repos | 200 `TicketDetailDto` (messages, events, tags, linked articles); 404 | H, C, I | none |
| `POST /api/tickets/{id}/replies` (Agent; multipart (text fields + files)) | `AddAgentReplyRequestHandler` | `ITicketRepository`, `IKbRepository`, `IAttachmentStore`, `IHtmlSanitizer`, `IMarkdownRenderer`, `ITicketNotificationPlanner`, `ILogger`, `IUnitOfWork`, `ICurrentAgentClaims` | EF repos, storage store, sanitizer, Markdig renderer, planner, Outbox, UoW | 201 `AgentMessageResponse`; 404; 409 Closed or concurrency | H, C, I | D-008, D-010 |
| `POST /api/tickets/{id}/notes` (Agent) | `AddInternalNoteRequestHandler` | `ITicketRepository`, `IHtmlSanitizer`, `IUnitOfWork`, `ICurrentAgentClaims` | EF repos, sanitizer, UoW | 201 `AgentMessageResponse`; 404; 409 | H, C, I | none |
| `PUT /api/tickets/{id}/status` (Agent) | `ChangeTicketStatusRequestHandler` | `ITicketRepository`, `ITicketNotificationPlanner`, `IUnitOfWork`, `ICurrentAgentClaims` | EF repos, UoW | 200 `TicketStateDto`; 404; 409 invalid transition (never 422) or concurrency | H, C, I | D-008 |
| `PUT /api/tickets/{id}/assignee` (Agent) | `AssignTicketRequestHandler` | `ITicketRepository`, `IAgentRepository`, `ITicketNotificationPlanner`, `IUnitOfWork`, `ICurrentAgentClaims` | EF repos, UoW, planner | 200 `TicketStateDto`; 404; 400 `assignee-inactive`; 409 concurrency | H, C, I | none |
| `PUT /api/tickets/{id}/priority` (Agent) | `ChangeTicketPriorityRequestHandler` | `ITicketRepository`, `IUnitOfWork`, `ICurrentAgentClaims` | EF repos, UoW | 200 `TicketStateDto`; 404; 409 | H, C | none |
| `PUT /api/tickets/{id}/product` (Agent) | `MoveTicketProductRequestHandler` | `ITicketRepository`, `IProductRepository`, `IUnitOfWork`, `ICurrentAgentClaims` | EF repos, UoW | 200 `TicketStateDto`; 404 product; 409; number unchanged | H, C, I | D-009 |
| `POST /api/tickets/{id}/tags` (Agent) | `AddTicketTagRequestHandler` | `ITicketRepository`, `ITagRepository`, `IUnitOfWork`, `ICurrentAgentClaims` | EF repos, UoW | 200 `TicketStateDto`; 404; idempotent (D-036) | H, C | D-036 |
| `DELETE /api/tickets/{id}/tags/{tagId}` (Agent) | `RemoveTicketTagRequestHandler` | `ITicketRepository`, `ITagRepository`, `IUnitOfWork`, `ICurrentAgentClaims` | EF repos, UoW | 200 `TicketStateDto`; 404; idempotent (D-036) | H, C | D-036 |
| `PUT /api/tickets/{id}/spam` (Agent, D-022; body `IsSpam`, `false` is "Not spam", D-024) | `MarkTicketSpamRequestHandler` | `ITicketRepository`, `IUnitOfWork`, `ICurrentAgentClaims` | EF repos, UoW | 200 `TicketStateDto`; 404; 409 `ticket-closed` | H, C | D-006 |
| `DELETE /api/tickets/{id}` (Admin) | `DeleteTicketRequestHandler` | `ITicketRepository`, `IAttachmentStore`, `IEmailOutboxStore`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims`, `IUnitOfWork`, `TimeProvider`, `ILogger` | EF repos, storage store, Outbox store, UoW | 204; 404 | H, C, I | D-006, D-022 |
| `POST /api/requesters/{id}/erase` (Admin) | `EraseRequesterRequestHandler` | `IRequesterRepository`, `IRequesterErasure`, `IAttachmentStore`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims`, `IUnitOfWork`, `TimeProvider`, `ILogger` | EF repos, `RequesterErasure`, storage store, UoW | 204; 404 | H, C, I | D-006, D-022 |
| `GET /api/attachments/{id}` (Agent JWT; Admin reaches it through its pass-through adapter, D-017) | `GetAttachmentRequestHandler` | `ITicketRepository`, `IAttachmentStore`, `ICurrentAgentClaims`, `TimeProvider` | EF repos, storage store | 200 file stream (`Content-Disposition: attachment`, `nosniff`); 404 | H, C, I | none |
| `GET /api/customer/attachments/{id}` (customer token header; `Public` policy, `token-access` limit; Portal reaches it through its pass-through adapter, D-017) | `GetCustomerAttachmentRequestHandler` | `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `IAttachmentStore`, `TimeProvider`, `ILogger` | EF repos, storage store, token service | 200 file stream (`Content-Disposition: attachment`, `nosniff`); 404 uniform | H, C, I | D-001, D-038 |
| `GET /api/customer/ticket` (customer token header; `token-access` limit) | `GetCustomerTicketRequestHandler` | `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `IAgentRepository`, `IProductRepository`, `IUnitOfWork`, `TimeProvider` | EF repos, token service | 200 `CustomerTicketDto` (public messages only; carries the product key); 404 uniform | H, C, I | D-001 (token scheme) |
| `POST /api/customer/ticket/replies` (customer token header; multipart) | `AddCustomerReplyRequestHandler` (Closed ticket creates follow-up) | `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `ITicketNumberAllocator`, `IAttachmentStore`, `IHtmlSanitizer`, `ITicketNotificationPlanner`, `IUnitOfWork`, `TimeProvider`, `IOptions<PortalLinkOptions>`, `ILogger` | EF repos, allocator, token service, storage store, sanitizer, planner, Outbox, UoW | 201 `CustomerReplyResponse` (message id, or new ticket view URL for follow-up); 404 uniform; 400 | H, C, I | D-008 |
| `POST /api/customer/access-link` (`Public` policy, `lost-link` limit) | `RequestNewAccessLinkRequestHandler` | `IRequesterRepository`, `ITicketRepository`, `IEmailOutboxStore`, `ITicketNotificationPlanner`, `IUnitOfWork`, `TimeProvider`, `IOptions<LostLinkOptions>`, `ILogger` | EF repos, token service, Outbox, UoW | 202 uniform response always; 429 via limiter | H, C, I | D-006 |
| `GET /api/dead-letters` (Admin) | `ListDeadLettersRequestHandler` | `IEmailOutboxStore` | Outbox store | 200 paged `DeadLetterDto` | H, C, I | D-010, D-022 |
| `POST /api/dead-letters/{id}/retry` (Admin) | `RetryDeadLetterRequestHandler` | `IEmailOutboxStore`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims`, `IUnitOfWork`, `TimeProvider` | Outbox store, EF repos, UoW | 204; 404; 409 not dead-lettered | H, C, I | D-010, D-022 |
| `DELETE /api/dead-letters/{id}` (Admin) | `DiscardDeadLetterRequestHandler` | `IEmailOutboxStore`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims`, `IUnitOfWork`, `TimeProvider` | Outbox store, EF repos, UoW | 204; 404; 409 not dead-lettered | H, C, I | D-010, D-022 |
| Worker scheduled loop (`OutboxRetentionWorker` hosted service) | `PurgeEmailOutboxHandler` | `IEmailOutboxStore`, `TimeProvider`, `IOptions<OutboxRetentionOptions>`, `ILogger` | Outbox store | Loop: deleted count logged; failure logged, next tick retries | H, I, W | D-039 |
| Worker scheduled loop (`AutoCloseWorker` hosted service) | `AutoCloseSolvedTicketsHandler` | `ITicketRepository`, `IUnitOfWork`, `TimeProvider`, `IOptions<AutoCloseOptions>`, `ILogger` | EF repos, UoW; the Worker's `PgNotifyTicketChangeBroadcaster` runs from the post-commit interceptor (D-018) | Loop: closed count logged; failure logged, next tick retries | H, I, W | D-008, D-012, D-007 |
| Alerts (new ticket, assignment, customer reply) | no entry point: queued by the handlers above through `ITicketNotificationPlanner` | n/a | n/a | n/a | covered by calling handlers' H tests plus planner unit tests | D-010 |

### 7.4 Knowledge base (PHASE-08)

| Entry point/use case | Named handler | Application dependencies | Infrastructure implementations | Outcome mapping | Tests | Decision |
| --- | --- | --- | --- | --- | --- | --- |
| `GET /api/kb/articles` (Agent) | `ListKbArticlesRequestHandler` | `IKbRepository` | EF repos (FTS) | 200 paged `KbArticleListItemDto`; 400 `status-invalid` | H, C, I | D-011 |
| `GET /api/kb/articles/{id}` (Agent) | `GetKbArticleRequestHandler` | `IKbRepository` | EF repos | 200 `KbArticleDto` (Markdown source); 404 | H, C | none |
| `POST /api/kb/articles` (Agent) | `CreateKbArticleRequestHandler` | `IKbRepository`, `IProductRepository`, `ICurrentAgentClaims` | EF repos | 201 `KbArticleDto`; 400; 409 `kb-slug-taken` (slugs are unique across scopes, D-044) | H, C, I | D-044 |
| `PUT /api/kb/articles/{id}` (Agent) | `UpdateKbArticleRequestHandler` | `IKbRepository` | EF repos | 200 `KbArticleDto` (an Archived article returns to Draft); 404; 409 `concurrency-conflict` | H, C, I | D-044 |
| `POST /api/kb/articles/{id}/publish` (Agent) | `PublishKbArticleRequestHandler` | `IKbRepository` | EF repos | 200 `KbArticleDto`; 400 `kb-publish-incomplete`; 404; 409 | H, C | D-044 |
| `POST /api/kb/articles/{id}/archive` (Agent) | `ArchiveKbArticleRequestHandler` | `IKbRepository` | EF repos | 200 `KbArticleDto`; 404; 409 | H, C | none |
| `POST /api/kb/preview` (Agent) | `RenderKbPreviewRequestHandler` | `IKbContentRenderer` | KB Markdig profile, KB sanitiser | 200 `KbPreviewResponse` (sanitized HTML); 400 oversize | H, C | D-021, D-044 |
| `POST /api/kb/images` (Agent; multipart) | `UploadKbImageRequestHandler` | `IKbImageStore`, `IKbImageUrls` | KB image store (public-read `kb-images/` prefix) | 201 `KbImageUploadResponse` (key and URL); 400 type or size (SVG rejected) | H, C, I | D-044 |
| `GET /api/kb/categories` (Agent) | `ListKbCategoriesRequestHandler` | `IKbRepository` | EF repos | 200 `KbCategoryDto[]` | H, C | none |
| `POST /api/kb/categories` (Agent) | `CreateKbCategoryRequestHandler` | `IKbRepository` | EF repos | 201 `KbCategoryDto`; 409 | H, C | none |
| `PUT /api/kb/categories/{id}` (Agent) | `UpdateKbCategoryRequestHandler` | `IKbRepository` | EF repos | 200 `KbCategoryDto`; 404; 409 | H, C | none |
| `DELETE /api/kb/categories/{id}` (Admin) | `DeleteKbCategoryRequestHandler` | `IKbRepository` | EF repos | 204; 404; 409 not empty | H, C | none |
| `GET /api/public/kb/{productKey}/search?q=&category=` (anonymous, `public` limit; also deflection) | `SearchPublicKbArticlesRequestHandler` | `IKbRepository`, `IProductRepository` | EF repos (FTS) | 200 `PagedResponse<PublicKbSearchResultDto>` with `Cache-Control: public, max-age=60` | H, C, I | D-011, D-044 |
| `GET /api/public/kb/{productKey}/articles/{categorySlug}/{slug}` (anonymous, `public` limit) | `GetPublishedKbArticleRequestHandler` | `IKbRepository`, `IProductRepository`, `IKbContentRenderer` | EF repos, KB Markdig profile and sanitiser | 200 `PublishedKbArticleDto` (sanitized HTML) with `Cache-Control: public, max-age=60`; 404 `no-store` | H, C, I | D-014, D-044 |
| `GET /api/public/kb/{productKey}/categories` (anonymous, `public` limit) | `ListPublicKbCategoriesRequestHandler` | `IKbRepository`, `IProductRepository` | EF repos | 200 `PublicKbCategoryDto[]` | H, C | none |
| `GET /api/public/kb/{productKey}/categories/{categorySlug}/articles?page=&pageSize=` (anonymous, `public` limit) | `ListPublicKbCategoryArticlesRequestHandler` | `IKbRepository`, `IProductRepository` | EF repos | 200 `PagedResponse<PublicKbArticleSummaryDto>` (published only, newest update first) with `Cache-Control: public, max-age=60`; 404 `no-store` for an unknown, invisible or empty category | H, C, I | D-045 |
| `GET /api/public/kb/{productKey}/sitemap` (anonymous, `public` limit) | `GetKbSitemapRequestHandler` | `IKbRepository`, `IProductRepository` | EF repos | 200 `KbSitemapEntryDto[]` | H, C | none |

### 7.5 Live updates (PHASE-10)

| Entry point/use case | Named handler | Application dependencies | Infrastructure implementations | Outcome mapping | Tests | Decision |
| --- | --- | --- | --- | --- | --- | --- |
| SignalR `TicketHub.JoinTicket` / `LeaveTicket` / `SetComposing` (Agent JWT) | `UpdateTicketPresenceHandler` | `ITicketPresenceStore`, `ITicketRepository` (existence check), `ITicketChangeBroadcaster`, `ICurrentUserService`, `TimeProvider` | `InMemoryTicketPresenceStore`, SignalR broadcaster | Hub method returns void; failure results are logged and the hub call returns without a client error; unauthorized joins abort the connection | H, C (hub test) | D-007 |
| Postgres `NOTIFY techstrap_ticket_changes` to API hosted listener | `RelayTicketChangeHandler` (listener constructor-injected) | `ITicketChangeBroadcaster` | SignalR broadcaster | Listener: handled means continue; malformed payload logged and dropped; connection loss means reconnect with backoff | H, I (NOTIFY round trip) | D-007 |

Not entry points: `TicketChangePublishingInterceptor` (Infrastructure post-commit hook in the API and Worker, D-018) and `PgNotifyTicketChangeBroadcaster` (Worker implementation of `ITicketChangeBroadcaster`). They are infrastructure implementations invoked by committed `TicketEvent` rows, not use cases.

### 7.6 Exempt operational endpoints

| Endpoint | Why exempt | Constraint |
| --- | --- | --- |
| `/health/live`, `/health/ready` | Framework health checks (`AspNetCore.Common`); readiness may check DB connectivity through the health-check registration, not through handlers | No application workflow, no business data returned; anonymous |
| `/openapi/v1.json` | Framework-generated OpenAPI document | No application workflow; anonymous or Development-only, configurable (**Assumption**: anonymous, no secrets) |
| Static assets (Admin, Portal `wwwroot`, compiled SCSS output) | Static file middleware | No application workflow |
| KB images under the public-read `kb-images/` prefix (API) | Public static assets served from storage by the API (D-021); written only by `UploadKbImageRequestHandler` | No application workflow; the prefix never holds ticket attachments |
| Admin `GET /attachments/{id}` and Portal `GET /t/{token}/attachments/{id}` | Pass-through streaming proxies: Admin to `GET /api/attachments/{id}`, Portal to `GET /api/customer/attachments/{id}` (D-038), because bearer and customer tokens are server-side (D-017) | No application workflow, no persistence; authorization is enforced by `GetAttachmentRequestHandler` (agent) and `GetCustomerAttachmentRequestHandler` (customer); `Content-Disposition: attachment` and `nosniff` |
| `/hubs/tickets` handshake (API) | SignalR framework connection setup; the hub methods are the use cases (section 7.5) | JWT bearer and the Agent policy at handshake only |
| API host startup: migrate database (advisory lock) and dev data seeder (`IDevelopmentDataSeeder`) | Host startup steps with no request input and no transport outcome; the seeder runs only in Development with `TECHSTRAP_SEED_DEV_DATA=true` | No application workflow; API only |

Admin and Portal framework endpoints (Blazor `_blazor` hub, OIDC callback, antiforgery) are framework-owned and execute no TechStrap workflow. Portal `/sitemap.xml` and `robots.txt` are presentation endpoints that call the API through the typed client (`GetKbSitemapRequestHandler` and `ListPublicProductsRequestHandler` at the API); they hold no business logic.

## 8. Razor presentation-boundary tables

Rules applied (_template RAZOR_COMPONENT_ARCHITECTURE.md): any injection, lifecycle or async work, state, navigation or callbacks means a paired `.razor` and `.razor.cs`; all C# in code-behind; ViewModels are feature-local to the app and Razor-only; API contracts are DTOs from `TechStrap.Contracts`; no factory unless non-trivial mapping, async assembly or multiple dependencies. Pure display child components with parameters only (rows, badges, status pills) stay inline. Admin and Portal code-behind depends only on typed API clients (interfaces local to each app), never on Domain, Application or EF. State ownership is described as who owns loading, error, empty and mutable state.

### 8.1 Admin app (Blazor Server)

| Feature/page | Component pair decision | ViewModel or direct model | Factory/presentation-service decision | State ownership and behavior | API DTO boundary |
| --- | --- | --- | --- | --- | --- |
| Queue (`/queue/{view?}`; `/` opens the queue) | Paired page; inline child `TicketRow`, `StatusBadge`, `PriorityBadge` | `TicketQueueViewModel` with `TicketRowViewModel` (formatted age, badges, "viewing" hint) | Factory `TicketQueueViewModelFactory`: non-trivial mapping and merges live presence hints | Page owns filter, paging, search text, selection; loading skeleton, empty state per view, error banner with retry; refreshes on hub events and after actions | Consumes `PagedResponse<TicketSummaryDto>` through the tickets client; sends list query parameters |
| Ticket detail (`/tickets/{number}`) | Paired page; paired child `ReplyComposer`; paired child `Timeline`; inline `MessageBubble`, `EventLine` | `TicketDetailViewModel` (timeline merges messages and events), `ReplyDraftViewModel` | Factory `TicketDetailViewModelFactory`: timeline merge and presence | Page owns ticket, draft, concurrency token, composing state; sends `SetComposing`; on 409 shows reload prompt keeping the draft; errors per action; delete and erase actions are offered to Admins only (D-022); loading and not-found states | `TicketDetailDto`, `AddAgentReplyRequest`, status/assign/priority/tag/product requests |
| Products (`/settings/products`, `/settings/products/{id}`) | Paired list page and paired `ProductEditor` form component | `ProductEditorViewModel` (form, branding preview) | None; simple local mapping in code-behind | Page owns list and edit mode; validation messages from ProblemDetails; empty state "create your first product" | `ProductDto`, `CreateProductRequest`, `UpdateProductRequest` |
| Product API keys (`/settings/products/{id}/keys`) | Paired page; paired `NewKeyDialog` | `ApiKeyRowViewModel`, `NewKeyViewModel` | None | One-time plain key held in component state only until dismissed (never persisted or logged); kind explained in UI; revoke needs confirmation | `ProductApiKeyDto`, `CreateProductApiKeyRequest`, `CreateProductApiKeyResponse` |
| Agents (`/settings/agents`) | Paired page | direct model for rows plus `AgentEditViewModel` for inline edit | None | Page owns list; role/active changes optimistic with rollback on error; guard on last admin; empty state unlikely | `AgentDto`, `UpdateAgentRequest` |
| Tags (`/settings/tags`) | Paired page | `TagEditViewModel` | None | Page owns list and editing state; delete confirmation; empty state | `TagDto`, tag requests |
| KB list (`/kb`) | Paired page | `KbListItemViewModel` | None | Page owns filters (product, status, category, search), paging; empty and error states | `KbArticleSummaryDto`, `KbCategoryDto` |
| KB editor (`/kb/new`, `/kb/{id:guid}`) | Paired page; paired `MarkdownEditor` (live preview, JS interop for image paste); `PreviewPane` paired (sanitized render) | `KbArticleEditorViewModel` | None; preview calls `POST /api/kb/preview` through the KB client and renders the sanitized result (D-021) | Page owns dirty state, save conflict handling, publish/archive actions; unsaved-changes guard; upload progress | `KbArticleDto`, create/update requests, `KbPreviewRequest`/`KbPreviewResponse`, `KbImageDto` |
| KB categories (`/kb/categories`) | Paired page | `KbCategoryRowViewModel` | None | Page owns list and inline edit; delete (Admin) blocked with a message on 409 when the category holds articles | `KbCategoryDto`, category requests |
| Dead letters (`/ops/dead-letters`, Admin) | Paired page | direct `DeadLetterDto` rows | None | Page owns list and per-row retry/discard in-flight state; confirm on discard; empty state "no failed emails" | `DeadLetterDto` |
| Admin events (`/settings/audit`, Admin) | Paired page | direct `AdminEventDto` rows | None | Page owns paging and filters; read-only; empty state | `AdminEventDto` paged |
| Notification preferences (`/account/notifications`) | Paired page | `NotificationPreferencesViewModel` (per-product toggles) | None | Page owns toggles and saved indicator; error retry | `AgentDto` (preferences) and `UpdateNotificationPreferencesRequest` |
| Shell, sign-in and error boundary | Paired layout components; error boundary and reconnect UI from `Blazor.Components` | direct | None | Layout owns connection state, nav, signed-in agent; hub connection service shared | `AgentDto` from `GetCurrentAgentRequestHandler` |

### 8.2 Portal (Blazor SSR)

| Feature/page | Component pair decision | ViewModel or direct model | Factory/presentation-service decision | State ownership and behavior | API DTO boundary |
| --- | --- | --- | --- | --- | --- |
| Portal root (`/`) | Paired page | direct list of active products, or redirect to `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` when set | None | Server-rendered; minimal product chooser; no enumeration of inactive products | `PublicProductDto` |
| Product home (`/p/{key}`) | Paired page; inline `CategoryCard`, `ArticleLink` | `ProductHomeViewModel` (branding, categories, featured articles) | Factory `ProductHomeViewModelFactory`: assembles from several API calls | Server-rendered; unknown product renders themed 404; API failure renders friendly error without caching; applies per-product theme (name, logo, accent) via CSS variables | `PublicProductDto`, `PublicKbCategoryDto`, `KbSearchResponse` |
| Contact form with deflection (`/p/{key}/contact`) | Paired page; paired interactive island `DeflectionPanel` (debounced search); paired `AttachmentInput` | `ContactFormViewModel` (validated form model), `DeflectionResultViewModel` | None; simple mapping in code-behind | SSR form post with antiforgery and honeypot; optional `subject`, `name`, `email` query-string prefill is bound into the same validated form model (visible, editable, same limits as typed input, no hidden fields, D-024); validation errors inline; island owns query text, debounce, loading and empty states ("no suggestions"); submit success redirects to `/p/{key}/contact/received`, which shows the ticket number only (the view link arrives by email); rate-limit 429 shown as friendly message | `SubmitTicketRequest` multipart, `SubmitTicketResponse`, `KbSearchResponse` |
| Ticket view (`/t/{token}`) | Paired page; paired `ReplyForm`; inline `PublicMessage` | `CustomerTicketViewModel` | None | Token read server-side and sent only in header to API; agent names arrive already resolved from the API (first name plus product support name, or the public display name override, D-024; never agent email); invalid shows uniform not-found page; Closed shows read-only banner and explains that a reply starts a new ticket; reply success refreshes; follow-up response redirects to the new `/t/{newToken}`; `noindex` and `Referrer-Policy: no-referrer` | `CustomerTicketDto`, `AddCustomerReplyRequest`, `CustomerReplyResponse`; attachment download through the Portal pass-through adapter `/t/{token}/attachments/{id}` (D-017) |
| Lost-link (`/p/{key}/lost-link`) | Paired page | `LostLinkViewModel` | None | Single email field; always shows the same confirmation; 429 shown generically | `RequestNewAccessLinkRequest` |
| KB home (`/p/{key}/kb`) | Paired page | `KbHomeViewModel` | None | Server-rendered category list and search box; empty state | `PublicKbCategoryDto` |
| KB category (`/p/{key}/kb/{category}`; the slug `search` is reserved for KB search) | Paired page | `KbCategoryViewModel` | None | Server-rendered list; empty category state; 404 themed; SEO meta via `Blazor.Seo` | `PublicKbCategoryDto`, `KbSearchResponse` (category filter, OQ-5) |
| KB article (`/p/{key}/kb/{category}/{slug}`) | Paired page; inline `ArticleBody` that renders trusted sanitized HTML | `KbArticleViewModel` | None | Server-rendered; SEO meta and canonical link; 404 not-found state; cached by API response | `PublishedKbArticleDto` |
| KB search (`/p/{key}/kb/search`) | Paired page | `KbSearchViewModel` | None | Query string driven (GET form, works without JS); empty and no-results states | `KbSearchResponse` |

## 9. Error, abuse and concurrency

- **Errors:** RFC 7807 ProblemDetails from `AspNetCore.Common`, with stable `ResultError` codes; correlation id in every response and log. Unexpected exceptions return a generic 500. Email failures never fail user requests (outbox).
- **Uniform responses:** invalid, expired or revoked token returns identical 404; lost-link returns identical 202; honeypot hit returns normal-looking 201 without creating a ticket (**Assumption**).
- **Abuse controls:** see section 11 rate limits; body and attachment size limits at Kestrel and handler; extension and content-type allowlist; spam flag; sanitization; downloads forced as attachments with `nosniff`.
- **Concurrency:** optimistic concurrency token on `tickets` (and `products`); handlers return Conflict on stale write; Admin offers reload and preserves drafts. Number allocation and outbox claim are atomic SQL. Auto-close and agent actions race safely: the conflict path wins for the agent, the next tick retries.
- **Idempotency:** tag add/remove and spam mark are idempotent; outbox repeats tolerated (D-010); an `Idempotency-Key` on API-key intake returns the original ticket response for 24 h (D-020).

## 10. Observability

- Serilog (`AspNetCore.Serilog`) JSON logs with correlation id; PII redaction destructuring and enricher (emails, token, API key patterns); request logging excludes `/t/{token}` and token headers.
- OpenTelemetry traces and metrics via `SyntaxCircus.Observability` to the existing stack on the UAT box: request metrics, EF spans, handler spans, SMTP spans.
- Custom metrics: outbox pending depth, oldest pending age, dead-letter count, send success/failure, tickets created per product and channel, auto-closed count, hub connections, listener reconnects.
- Health: `/health/live` (process) and `/health/ready` (DB reachable; API also migrated; worker last-loop heartbeat). Each container has a compose `healthcheck` using `curl`.
- Alert suggestions (documented, not built): dead letters above zero, oldest pending outbox above 10 minutes.

## 11. Deployment

### 11.1 Compose and environments

- Files (D-043): `docker-compose.yml` (local: builds the four images, Postgres 17 and Mailpit) with a root `.env.example` for its inputs (`TECHSTRAP_SUBNET`, `REVERSE_PROXY_CIDR`, `TECHSTRAP_MAILPIT_PORT`, `TECHSTRAP_SEED_DEV_DATA`); `deploy/docker-compose.yml`, one image-only compose for UAT and production, with the compose-input templates `deploy/.env.uat.example` and `deploy/.env.production.example` and the key-only app templates `deploy/.env.{api,worker,admin,portal}.example`; and per project an `appsettings.json` that lists every setting the host reads (real non-secret defaults, blank secrets), an `appsettings.Development.json` with the local overrides, a `.env.example` and a gitignored `.env.local`, loaded by `SyntaxCircus.DotEnv` in Development only. `scripts/tests/ConfigContract.Tests.ps1` keeps the four in sync.
- Dockerfiles at repo root (`Dockerfile.{api,admin,portal,worker}`), full source copy before restore, BuildKit NuGet cache mount, base `mcr.microsoft.com/dotnet/aspnet:10.0`, non-root uid 10001, pre-created writable mounts (`storage`, `logs`, `dataprotection-keys`), `curl` for health checks, `ASPNETCORE_URLS=http://+:80`.
- `Build-TechStrapDocker.ps1`: `-Targets`, `-ImageTag`, `-SemVerTag`, `-Registry` (default `ghcr.io/syntax-circus`), `-Push`, `-PushLatest`, `-NoCache`, `-Platforms` (default `linux/amd64,linux/arm64`), `-VersionProjectPath`; GitVersion SemVer tags (D-003). GitHub Actions: build and test on PR; push to GHCR and pack NuGet on tag.
- Public URL: `TECHSTRAP_PORTAL_PUBLIC_URL` is the single portal base URL (API and Worker for `/t/{token}` links in responses and emails, Portal for canonical URLs and sitemap).
- Portal configuration, "Powered by TechStrap" (D-024): `TECHSTRAP_PORTAL_SHOW_POWERED_BY` (default `true`) shows the mark, linked to https://github.com/Syntax-Circus/techstrap, on every Portal page and in customer emails; `false` hides it installation-wide. Read by the Portal host and by email rendering in the Worker (both `.env.example` files); no per-product override.
- Volumes: `techstrap-storage` (API only, mounted at `/app/storage`; the Worker registers no attachment storage), `admin-keys` and `portal-keys` (the ASP.NET data protection key ring of each Blazor host, mounted at `/app/dataprotection-keys`); the local compose adds `pgdata`.
- Startup order: `api` healthy (it runs migrations against the Postgres that is already up), then `admin`, `portal` and `worker`. The local compose waits for its own `postgres` first.
- Deployment (D-043): `deploy/docker-compose.yml` is image-only: GHCR images, each named in full by an input (no `latest`), `pull_policy: always`, loopback ports, a pinned app subnet. Each service loads its own scoped env file, `${TECHSTRAP_ENV_DIR}/.env.<app>` (root-owned, mode 0600, `format: raw`), and `environment:` holds only what compose owns (`ASPNETCORE_ENVIRONMENT`, `DOTENV__ENABLED`, the Api address, the key-ring and storage paths, the trusted networks). Postgres is a separate instance on an external Docker network (`TECHSTRAP_DB_NETWORK`) that only the Api and the Worker join. The runbook is `docs/self-hosting/DEPLOYMENT.md`.
- Admin link: `TECHSTRAP_ADMIN_PUBLIC_URL`: optional; when set, agent assignment emails link to `{url}/tickets/{number}`.
- Postgres 17 tuned for the small footprint (`shared_buffers` about 256 MB, **Assumption**).

### 11.2 Pinned subnet, forwarded headers and rate limiting

Per _template pattern CLIENT_IP_RATE_LIMITING.md (reverse proxy in front of Dockerized containers with an anonymous public API surface):

- **Pinned subnet:** the `default` network of `docker-compose.yml` and of `deploy/docker-compose.yml` uses `ipam` subnet `172.16.31.0/24` (A-09; `TECHSTRAP_SUBNET`). Registering this subnet in the pattern's subnet registry is a cross-repo owner action in the _template, recorded as a PHASE-01 compose task (D-019).
- **API trusted proxies (D-019):** the API trusts the pinned subnet `172.16.31.0/24` (`TRUSTEDPROXY__TRUSTEDNETWORKS__0`, the Portal hop) and adds `TRUSTEDPROXY__TRUSTEDNETWORKS__1=<caddy-ip>/32` only when Caddy runs outside that subnet. **Admin and Portal trust only the proxy** (`...__0` only). Never trust `172.16.0.0/12` or `0.0.0.0/0`. The proxy address is deployment-specific (Q-08).
- **Portal and Admin to API (D-019):** every typed `HttpClient` that calls the API uses `.AddForwardedClientIp()`; the Portal forwards the original client IP in `X-Forwarded-For` so the API rate-limits real visitors, not the Portal container.
- **Policies** (named, bound under `RateLimiting:*`, validated with `ValidateOnStart` so bad values fail boot; defaults per A-08):

| Policy | Applies to | Partition | Default |
| --- | --- | --- | --- |
| `public` | `GET /api/public/products`, `GET /api/public/products/{key}`, public KB endpoints, sitemap | client IP | 120 / min |
| `public-submit` | `POST /api/public/products/{key}/tickets` | client IP | 5 / 10 min |
| `intake-key` (Public key) | `POST /api/intake/tickets` with a Public key | key prefix + client IP | 10 / min |
| `intake-key` (Trusted key) | `POST /api/intake/tickets` with a Trusted key | key prefix + client IP (a key id is impossible before authentication; the limiter reads the raw `X-Api-Key` prefix, so a spoofer who knows a prefix exhausts only their own IP's partition) | 120 / min |
| `token-access` | customer ticket read, reply, attachment | client IP | 60 / min |
| `lost-link` | `POST /api/customer/access-link` | client IP and a second limiter by normalized address | 5 / hour per IP, 3 / hour per address |

- Rate-limit rejections use `UseProblemDetailsRejection` (429). IP auto-ban (`AddIpBanTracking`) is optional and not in core (**Assumption**); if added, its allowlist stays separate from trusted-proxy config (pattern anti-pattern).
- **Default-deny** fallback authorization policy requiring an authenticated user; health and OpenAPI use `[AllowAnonymous]`; web-form submit, the public product, public KB, sitemap, customer-token and lost-link endpoints declare the explicit `Public` policy at class level instead (D-034) (customer-token endpoints authenticate by token inside the handler path, not by default scheme).
- **Cache-Control:** `public, max-age=<n>` on public product and published KB hits; `no-store` on 404, token and customer responses.
- **Portal short memory cache** for branding and category lists (configurable seconds, 0 disables), never caching failures.
- **Verification:** `docker compose config` shows the pinned subnet and trusted-proxy values; API logs show the real visitor IP for direct and Portal-proxied calls; exceeding a limit from one IP returns 429 while another IP is unaffected; tests use the default checked-in trusted network and an `IStartupFilter` for `RemoteIpAddress` (pattern testing gotcha, since trusted-proxy options bind eagerly).

### 11.3 Backups and recovery

- Nightly `pg_dump` (custom format) plus a copy of the storage volume, retained 14 days (A-18), scripted in `docs/runbooks/backup-restore.md` (PHASE-12).
- Restore rehearsal documented: stop app containers, restore DB, restore volume, start API (migrations run), verify health and a sample ticket and attachment.
- Backups hold erased data until they expire; documented under privacy.

## 12. Decision references

Full text in `04-DECISION-LOG.md` (D-001 to D-031).

| ID | Decision (short) | Used in |
| --- | --- | --- |
| D-001 | Two API key kinds: Trusted and Public | 4, 7.1, 7.2, 11.2 |
| D-002 | Single portal domain with product theming (`/p/{key}`) | 7.1, 8.2 |
| D-003 | MIT public OSS, GHCR images, GitHub Actions | 11.1 |
| D-004 | Claim-gated agents (roles from IdP groups only, amended by D-029) | 4, 7.1 |
| D-005 | Client SDK and MAUI helper in core, over Contracts | 1, 2 |
| D-006 | Privacy basics: erase, spam/delete, PII redaction; retention deferred | 6.8, 7.3 |
| D-007 | SignalR on API with `LISTEN/NOTIFY` relay from worker | 6.6, 7.5 |
| D-008 | Auto-close and follow-up tickets after Closed | 6.5, 6.7 |
| D-009 | Immutable ticket numbers | 5, 7.3 |
| D-010 | Transactional outbox, at-least-once | 6.4, 7.2 |
| D-011 | Postgres FTS, no search engine | 5, 7.3, 7.4 |
| D-012 | Worker runs Application handlers directly against the DB | 1, 7.2, 7.3 |
| D-013 | xUnit v3, Shouldly, NSubstitute, Testcontainers | 7 (tests legend) |
| D-014 | HtmlSanitizer plus Markdig | 3, 7.4 |
| D-015 | Docs layout in `docs/architecture/` supersedes the superpowers spec | header |
| D-016 | Handlers accept Contracts request records directly | 2, 3.1, 7 |
| D-017 | Attachment downloads in Admin and Portal are pass-through proxies | 7.6, 8.2 |
| D-018 | Post-commit interceptor broadcasts live updates | 3, 6.6, 7.5 |
| D-019 | Portal forwards client IP; API trusts the pinned subnet | 11.2 |
| D-020 | Optional `Idempotency-Key` on intake | 4, 5, 6.1, 7.2 |
| D-021 | KB preview via the API; KB images are exempt static assets | 7.4, 7.6, 8.1 |
| D-022 | Destructive and system operations are Admin-only | 4, 7.1, 7.3 |
| D-023 | Visual direction Carbon Copy v2; portal shows Powered-by only | 8.2 |
| D-024 | Customer-facing agent identity, Spam view and Not spam, Powered-by link and setting, portal prefill | 5, 7.1, 7.3, 8.2, 11.1 |
| D-029 | Agent roles come from IdP groups only; no bootstrap admin | 4, 7.1 |
| D-030 | Deleting a tag in use: reject unless forced; forced delete detaches with events | 7.1 |
| D-031 | Product accent validation is format only | 7.1 |

No boundary deviations are proposed. The two places that look like deviations are within the rules: the worker hosted service resolves handlers from a per-iteration DI scope (allowed for hosts without per-method DI) and the SignalR hub is a transport adapter calling one handler. The Admin and Portal attachment downloads are exempt pass-through adapters (D-017), not use-case entry points.

## 13. Open questions for this document

Resolved during the consistency review:

| ID | Resolution |
| --- | --- |
| OQ-1 | `ListAdminEventsRequestHandler` reads through `IAdminEventRepository` (section 3.1), added in PHASE-04 |
| OQ-2 | `ITicketChangeBroadcaster` has a SignalR implementation (API) and a Postgres `NOTIFY` implementation (Worker); the post-commit interceptor publishes (D-018) |
| OQ-3 | Presence state lives behind `ITicketPresenceStore`, in-memory in the API (section 3.1) |
| OQ-4 | The KB editor preview calls `POST /api/kb/preview` handled by `RenderKbPreviewRequestHandler` (D-021) |
| OQ-7 | Handlers accept Contracts request records directly (D-016, A-15) |
| OQ-8 | Customer routes are `/api/customer/ticket`, `/api/customer/ticket/replies`, `/api/customer/access-link`, `/api/customer/attachments/{id}` with the `X-Ticket-Token` header (section 7.3, D-038) |

Still open:

| ID | Question | Default |
| --- | --- | --- |
| OQ-5 | Is a category filter on `SearchPublicKbArticlesRequestHandler` enough for the KB category page? | Yes (Q-10) |
| OQ-6 | Possible missing catalog entries (not added): a ticket-count-per-view handler for queue badges (Q-09); a tag-usage query (covered by `GetTicketRequestHandler`); an admin product-scoped notification-prefs read (covered by `GetCurrentAgentRequestHandler`) | Raise with the owner before PHASE-06 starts |
