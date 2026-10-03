# 04 - Decision Log

Format: _template `DECISION_TEMPLATE.md` (status, date, owner, related artifacts, context, decision, alternatives, consequences, approval). In this log **Approved** means the template's "Accepted". Owner of every decision: Jon Seeley (project owner).

Approval basis:
- **Owner Q&A (2026-10-02):** decisions settled directly by the owner (plan table B).
- **Reviewed draft spec:** decisions carried over from `docs/superpowers/specs/2026-10-02-techstrap-core-design.md`, which the owner reviewed and used as the baseline. Approved on that basis.
- **Owner confirmation (2026-10-02, after review):** D-014 and D-016 to D-022 were drafted by Claude (D-014 from plan section C; D-016 to D-022 while reconciling the artifact set) and then explicitly confirmed by the owner. D-018 also confirms the relay mechanism of D-007.
- **Proposed:** none. D-008's default (N = 7 days) was confirmed by the owner on 2026-10-02.
- **Owner decision (2026-10-02, PHASE-02):** D-023 (visual direction) was chosen by the owner after reviewing the mockups.
- **Owner decision (2026-10-02, after UX briefs):** D-024 (customer-facing identity, spam recovery, portal prefill) settled the open owner questions in UX-BRIEF-admin (Q11) and UX-BRIEF-portal.

**Boundary deviations: none.** Every application entry point maps to a named `I...Handler`, and no decision in this log departs from the mandatory flow in _template `APPLICATION_ARCHITECTURE.md`. D-012 records why the worker shares Infrastructure with the API; it is not a deviation. This log therefore contains no "Boundary Deviation Details" sections.

## Index

| ID | Title | Status | Date | Related |
| --- | --- | --- | --- | --- |
| D-001 | Two API key kinds: trusted and public | Approved (owner Q&A) | 2026-10-02 | PHASE-04, PHASE-05, PHASE-11 |
| D-002 | Single portal domain with per-product theming | Approved (owner Q&A) | 2026-10-02 | PHASE-02, PHASE-09 |
| D-003 | MIT public OSS, GHCR images, GitHub Actions | Approved (owner Q&A) | 2026-10-02 | PHASE-01, PHASE-12 |
| D-004 | Claim-gated agents with bootstrap admin | Approved (owner Q&A) | 2026-10-02 | PHASE-04 |
| D-005 | Client SDK and MAUI helper live in the core repo | Approved (owner Q&A) | 2026-10-02 | PHASE-11, 03-PACKAGE-MAP |
| D-006 | Privacy basics: erase requester, spam/delete, PII log redaction; retention deferred | Approved (owner Q&A) | 2026-10-02 | PHASE-01, PHASE-06 |
| D-007 | SignalR hub on the API with Postgres LISTEN/NOTIFY relay from the worker | Approved (live updates: owner Q&A; relay via D-018 confirmation) | 2026-10-02 | PHASE-10 |
| D-008 | Auto-close Solved to Closed after N days, with follow-up tickets | Approved (owner Q&A; N = 7 confirmed) | 2026-10-02 | PHASE-06 |
| D-009 | Immutable ticket numbers across product moves | Approved (owner Q&A) | 2026-10-02 | PHASE-03, PHASE-06 |
| D-010 | Transactional outbox with at-least-once delivery | Approved (reviewed draft spec) | 2026-10-02 | PHASE-05 |
| D-011 | Postgres full-text search instead of a search engine | Approved (reviewed draft spec) | 2026-10-02 | PHASE-03, PHASE-06, PHASE-08 |
| D-012 | Worker runs Application handlers directly against the DB | Approved (reviewed draft spec); not a boundary deviation | 2026-10-02 | PHASE-05, PHASE-06 |
| D-013 | xUnit v3 + Shouldly + NSubstitute + Testcontainers | Approved (reviewed draft spec, sibling-repo convention); NCrunch pin to verify | 2026-10-02 | PHASE-01, 03-PACKAGE-MAP |
| D-014 | HtmlSanitizer + Markdig | Approved (owner confirmation) | 2026-10-02 | PHASE-05, PHASE-08 |
| D-015 | `docs/architecture` supersedes the superpowers spec | Approved (owner Q&A) | 2026-10-02 | 00-DISCOVERY-INDEX |
| D-016 | Handlers accept `TechStrap.Contracts` request records directly | Approved (owner confirmation) | 2026-10-02 | 02-ARCHITECTURE, PHASE-04, PHASE-05, PHASE-06 |
| D-017 | Attachment downloads in Admin and Portal are pass-through streaming proxies (exempt adapters) | Approved (owner confirmation) | 2026-10-02 | PHASE-07, PHASE-09 |
| D-018 | Live-update broadcast through an EF post-commit interceptor; Worker uses Postgres NOTIFY | Approved (owner confirmation) | 2026-10-02 | PHASE-10, 02-ARCHITECTURE |
| D-019 | Real client IP: Portal forwards `X-Forwarded-For`; API trusts only the pinned compose subnet | Approved (owner confirmation) | 2026-10-02 | PHASE-01, PHASE-05, PHASE-09 |
| D-020 | Optional `Idempotency-Key` on intake so the SDK can retry submits safely | Approved (owner confirmation) | 2026-10-02 | PHASE-05, PHASE-11 |
| D-021 | KB live preview through `POST /api/kb/preview`; KB images are exempt public-read static assets | Approved (owner confirmation) | 2026-10-02 | PHASE-08, 02-ARCHITECTURE |
| D-022 | Destructive and system operations are Admin-only; mark-spam stays Agent | Approved (owner confirmation) | 2026-10-02 | 01-REQUIREMENTS, PHASE-04, PHASE-06, PHASE-07, UX-BRIEF-admin |
| D-023 | Visual direction: Carbon Copy v2; mascot only in Admin brand moments; portal shows Powered-by only | Approved (owner) | 2026-10-02 | PHASE-02, PHASE-07, PHASE-09, BRAND.md |
| D-024 | Customer-facing identity, spam recovery and portal prefill | Approved (owner 2026-10-02) | 2026-10-02 | 01-REQUIREMENTS, 02-ARCHITECTURE, PHASE-03 to PHASE-07, PHASE-09, UX-BRIEF-admin, UX-BRIEF-portal, BRAND.md |
| D-025 | The product-accent derivation helper lives in `TechStrap.Contracts` | Approved (owner 2026-10-02) | 2026-10-02 | PHASE-02, PHASE-04, PHASE-05, PHASE-09, BRAND.md |

---

## D-001: Two API key kinds: trusted and public

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** 02-ARCHITECTURE (intake), PHASE-04 (key management), PHASE-05 (intake), PHASE-11 (SDK)

### Context
Apps submit tickets via an API key that maps to a product. A key embedded in a shipped client (MAUI, SPA) is extractable, so it cannot be trusted to assert who the user is. Server-side callers can.

### Decision
Each product has keys of one of two kinds:
- **Trusted** (server-side): may set the external user reference and trusted metadata.
- **Public** (client-embedded): create-only, rate-limited per key and client IP, metadata stored but flagged untrusted, external user ref ignored.

Keys are stored hashed (`IApiKeyHasher`); the plaintext is shown once at creation. `POST /api/intake/tickets` handles both kinds via `SubmitTicketRequestHandler`; the kind comes from the authenticated principal, not the request body.

### Alternatives Considered
- Single key kind: forces either unsafe trust of client-supplied identity or a server proxy for every app.
- Per-request signed tokens: more secure but heavy for the first release; can be added later.

### Consequences
- Admin UI and `CreateProductApiKeyRequestHandler` need a kind selector.
- Per-key + per-IP rate limiting requires correct forwarded-header setup.
- Agent UI must display untrusted metadata distinctly.

### Approval
- **Approved by:** Jon Seeley (owner Q&A)
- **Approved on:** 2026-10-02

---

## D-002: Single portal domain with per-product theming

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** UX-BRIEF-portal, PHASE-02, PHASE-09, 02-ARCHITECTURE

### Context
Several products share one support install. Per-product domains multiply TLS, DNS and configuration burden.

### Decision
One portal domain serves all products under `/p/{key}`; customer ticket links use `/t/{token}`. Branding (name, logo, accent colour) is stored on the product, served by `GetPublicProductRequestHandler`, and applied to portal pages and outbound emails.

### Alternatives Considered
- One domain per product: more branding freedom, much more ops overhead; not needed for v1.
- Unthemed shared portal: simplest, but customers would not recognise the product.

### Consequences
- Theming is runtime CSS-variable driven on top of the compiled Bootstrap SCSS.
- Product keys become public identifiers and must be URL-safe and stable.
- Logo upload/storage rules need to be defined in PHASE-04.

### Approval
- **Approved by:** Jon Seeley (owner Q&A)
- **Approved on:** 2026-10-02

---

## D-003: MIT public OSS, GHCR images, GitHub Actions

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-01 (CI, Dockerfiles), PHASE-12 (release)

### Context
TechStrap is intended to be self-hostable by others, not only internal.

### Decision
Public repository from day one under the MIT license, with README, CONTRIBUTING and SECURITY.md. GitHub Actions runs build and tests and, on tag, publishes images to `ghcr.io/syntax-circus/techstrap-{api,admin,portal,worker}` (the build script still accepts `-Registry`) and the SDK packages to nuget.org. Self-host docs target any OIDC provider with Authentik as the worked example.

### Alternatives Considered
- Private until v1: hides unfinished work but delays feedback and requires a later history/secret audit.
- Azure Pipelines (used by some siblings): not chosen; GitHub is the host for a public project.

### Consequences
- No secrets or internal hostnames may appear in the repo from the first commit.
- Public CI must work without private NuGet feeds. SyntaxCircus packages are on nuget.org (verified in 03-PACKAGE-MAP).

### Approval
- **Approved by:** Jon Seeley (owner Q&A)
- **Approved on:** 2026-10-02

---

## D-004: Claim-gated agents with bootstrap admin

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-04, 03-PACKAGE-MAP (`AspNetCore.Authentication`)

### Context
Agents sign in via any OIDC provider. Authenticating to the IdP must not be enough to access support data, and the first admin needs a path in without a database edit.

### Decision
The API validates the OIDC JWT and an authorization policy requires a configured group claim: `TECHSTRAP_AGENT_GROUP` maps to `Agent`, `TECHSTRAP_ADMIN_GROUP` to `Admin`; others are rejected. The agent row is provisioned on first call to `GET /api/agents/me` (`GetCurrentAgentRequestHandler`). `TECHSTRAP_BOOTSTRAP_ADMIN` (email or subject) grants Admin to that user on provisioning.

### Alternatives Considered
- Open sign-up with admin approval: more UI, and exposes an approval queue to anyone with an IdP account.
- Roles only in the database: needs a manual first-admin step.

### Consequences
- Setup docs must explain adding the group claim in each IdP.
- Removal from the group should block access at the next token; the agent `active` flag handles deprovisioning inside TechStrap.
- Bootstrap env var must be documented as removable after first sign-in.

### Approval
- **Approved by:** Jon Seeley (owner Q&A)
- **Approved on:** 2026-10-02

---

## D-005: Client SDK and MAUI helper in core

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-11, 03-PACKAGE-MAP (`Maui.Environments` Not applicable)

### Context
The main intake source is the owner's own apps, mostly MAUI. A typed client avoids each app hand-rolling HTTP and metadata capture.

### Decision
This repo ships `TechStrap.Client` (typed client over `TechStrap.Contracts`, using an API key) and `TechStrap.Client.Maui` (device/app metadata capture and a submit-ticket helper), both published to nuget.org from CI. Contracts are shared by the API, the Blazor apps and the SDK. The MAUI helper takes base URL and key from the host app and does not select package-level environment handling (see 03-PACKAGE-MAP).

### Alternatives Considered
- Separate SDK repo: contract drift and double release cadence.
- No SDK, document the HTTP API only: pushes boilerplate to every app.

### Consequences
- `TechStrap.Contracts` becomes a public, semver-stable surface; breaking changes need a major bump.
- SDK builds against the published OpenAPI document and must not reference Application or Infrastructure.

### Approval
- **Approved by:** Jon Seeley (owner Q&A)
- **Approved on:** 2026-10-02

---

## D-006: Privacy basics

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-01 (log redaction), PHASE-06 (handlers)

### Context
Tickets hold personal data. Full compliance tooling is out of scope for v1, but basic controls are required for a public OSS product.

### Decision
- **Erase requester** (`EraseRequesterRequestHandler`): anonymise the requester and their messages, delete attachments through `IAttachmentStore`.
- **Spam / delete:** `MarkTicketSpamRequestHandler` sets `is_spam`; `DeleteTicketRequestHandler` hard-deletes.
- **PII redaction in Serilog:** email addresses and similar identifiers are masked in logs.
- **Deferred:** automatic retention rules.

### Alternatives Considered
- Retention rules in v1: needs policy per product and a scheduler; deferred.
- Soft-delete only: leaves personal data in place, defeating erasure.

### Consequences
- Erase and delete write an `AdminEvent`/audit record without the erased data.
- Backups still hold erased data until rotated; to be stated in the PHASE-12 runbook.
- Retention remains an explicit future item, not forgotten.

### Approval
- **Approved by:** Jon Seeley (owner Q&A)
- **Approved on:** 2026-10-02

---

## D-007: SignalR hub on the API with Postgres LISTEN/NOTIFY relay from the worker

- **Status:** Approved (live updates and SignalR from owner Q&A; LISTEN/NOTIFY relay confirmed with D-018)
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-10, 02-ARCHITECTURE

### Context
Agents want live queue/detail refresh and a "viewing/replying" hint. Some changes originate in the worker (auto-close, outbox status), a separate process from the API that hosts the hub.

### Decision
`TicketHub` is hosted on the API, authenticated with the agent JWT. API-originated changes are broadcast in-process via `ITicketChangeBroadcaster`. The worker issues Postgres `NOTIFY techstrap_ticket_changes`; an API hosted listener runs `RelayTicketChangeHandler`, which calls the broadcaster. Presence (`JoinTicket`/`LeaveTicket`/`SetComposing`) goes through `UpdateTicketPresenceHandler`. Admin (Blazor Server) connects server-side with `Microsoft.AspNetCore.SignalR.Client`.

### Alternatives Considered
- Redis backplane / message broker: extra infrastructure for self-hosters.
- Polling only: simpler but stale and wasteful; kept as the fallback if the hub is down.
- Hub in the worker: agents would need a second public endpoint.

### Consequences
- Postgres NOTIFY payloads are small (ids only); clients re-fetch.
- Notifications are lost while the API is down; the UI must refetch on reconnect.
- With several API replicas each relays to its own clients, which works because every replica receives every NOTIFY.

### Approval
- **Approved by:** Jon Seeley (owner Q&A for SignalR live updates; relay confirmed with D-018)
- **Approved on:** 2026-10-02

---

## D-008: Auto-close Solved to Closed, with follow-up tickets

- **Status:** Approved (behaviour and N = 7 days)
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-06, 01-REQUIREMENTS

### Context
Tickets in `Solved` otherwise stay re-openable forever. Support practice is to close them after a grace period.

### Decision
`AutoCloseSolvedTicketsHandler` (worker, scheduled) moves tickets from `Solved` to `Closed` after **N days** since `solved_at`; default **N = 7** (owner-confirmed), configurable by environment variable. A `Closed` ticket is read-only. A customer reply to a Closed ticket (`AddCustomerReplyRequestHandler`) creates a new ticket linked by `parent_ticket_id`. Closing writes a `TicketEvent`, and the change is relayed to the hub (D-007).

### Alternatives Considered
- Never auto-close: queue clutter and no clean follow-up semantic.
- Reopen on reply: loses the clean audit boundary of a closed conversation.

### Consequences
- The value of N is the only unconfirmed part; changing it needs no migration.
- Follow-up tickets get new numbers (D-009 applies per ticket).

### Approval
- **Approved by:** Jon Seeley (behaviour from owner Q&A; N = 7 days confirmed)
- **Approved on:** 2026-10-02

---

## D-009: Immutable ticket numbers across product moves

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-03 (numbering), PHASE-06 (`MoveTicketProductRequestHandler`)

### Context
Numbers look like `ACME-142`. Agents can move a ticket to another product, and customers hold emails and links that cite the number.

### Decision
The ticket number is assigned once by `ITicketNumberAllocator` (per-product sequence in the creating transaction) and never changes, keeping its original prefix after a move. Number uniqueness is enforced globally by the stored full number; lookup is by number and by the customer token, not by product.

### Alternatives Considered
- Renumber on move: breaks customer references and email subjects.
- Global numbers without prefix: loses the readable product hint.

### Consequences
- A ticket's prefix may differ from its current product; the UI shows both.
- The per-product sequence is not reused after a move.

### Approval
- **Approved by:** Jon Seeley (owner Q&A)
- **Approved on:** 2026-10-02

---

## D-010: Transactional outbox with at-least-once delivery

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-05, 02-ARCHITECTURE, 03-PACKAGE-MAP (`Email`)

### Context
Email must not fail a user's request, and must not be lost if the process crashes after the database commit.

### Decision
Email rows are written to the outbox table in the same transaction (`IUnitOfWork`) as the change that triggered them. The worker (`DrainEmailOutboxHandler`) claims rows with `FOR UPDATE SKIP LOCKED` through `IEmailOutboxStore`, sends via `IEmailSender`, retries with backoff, and dead-letters after N failures. Delivery is at-least-once; the outbox id is placed in the `Message-ID` header so duplicates are recognisable. Dead letters are listed, retried or discarded through P06 handlers.

### Alternatives Considered
- Send inline: user request fails with SMTP; no retry.
- Message broker: extra infrastructure; the DB is already transactional.
- Exactly-once: not achievable over SMTP.

### Consequences
- Occasional duplicate emails are possible and accepted.
- The worker is required for email to flow (health surfaced via outbox depth).

### Approval
- **Approved by:** Jon Seeley (reviewed draft spec)
- **Approved on:** 2026-10-02

---

## D-011: Postgres full-text search instead of a search engine

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-03 (vectors and indexes), PHASE-06 (ticket search), PHASE-08 (KB search and deflection)

### Context
Agents search tickets and customers search the KB. Self-hosters should not need another service.

### Decision
Use Postgres `tsvector` columns with GIN indexes via Npgsql, for tickets and KB articles. Query handling stays inside repositories (`ITicketRepository`, `IKbRepository`); handlers receive paged results.

### Alternatives Considered
- Elasticsearch/OpenSearch/Meilisearch: better relevance, but extra deployment and sync complexity.
- `LIKE` queries: poor relevance and performance.

### Consequences
- English configuration only at first (matches the English-only scope).
- Relevance tuning is limited; revisit if volume grows.

### Approval
- **Approved by:** Jon Seeley (reviewed draft spec)
- **Approved on:** 2026-10-02

---

## D-012: Worker runs Application handlers directly against the DB

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** 02-ARCHITECTURE (boundary table), PHASE-05, PHASE-06

### Context
The worker drains the email outbox and runs the auto-close job. It could call the API over HTTP or invoke the Application layer itself. Mandatory boundaries require every entry point to delegate to a named handler without leaking persistence or transport into the edge.

### Decision
The worker is an entry point like a controller: its hosted services (`PeriodicBackgroundService` ticks) delegate to `DrainEmailOutboxHandler` and `AutoCloseSolvedTicketsHandler`, which depend only on Application abstractions (`IEmailOutboxStore`, `ITicketRepository`, `IUnitOfWork`, `TimeProvider`). The worker references Application and Infrastructure to supply the implementations, exactly as the API does. This **is not a boundary deviation**: no handler touches `DbContext`, and the edge holds no business logic.

Why share Infrastructure rather than call the API: both jobs are system work with no end-user identity; an HTTP hop would add a service-to-service credential, couple worker availability to the API, and make `SKIP LOCKED` claiming across HTTP awkward. Only the API runs migrations (advisory lock), so the worker waits for the schema through the readiness check.

### Alternatives Considered
- Worker calls API endpoints: new internal auth surface, extra failure mode, no benefit.
- Logic in the hosted service: would violate the entry-point rule.

### Consequences
- Worker and API versions must match the same schema; compose deploys them together.
- Worker changes reach the API's live hub through `NOTIFY` (D-007), not via in-process calls.

### Approval
- **Approved by:** Jon Seeley (reviewed draft spec)
- **Approved on:** 2026-10-02

---

## D-013: xUnit v3 + Shouldly + NSubstitute + Testcontainers

- **Status:** Approved (sibling-repo convention; draft spec left it open); NCrunch pin to verify
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-01 (test projects and fixture), 03-PACKAGE-MAP

### Context
The draft spec left the framework open. All sibling repos use the same stack.

### Decision
xUnit v3, Shouldly, NSubstitute, and `Testcontainers.PostgreSql` (real Postgres 17) for Infrastructure and API tests. No EF InMemory provider. Test projects: Domain, Application, Infrastructure.IntegrationTests, Api.Tests, Architecture.Tests. `xunit.v3` and runner default to 4.0.0 as in dragon-poop, subject to the NCrunch rule in AGENT_GUIDE.md: if the owner's installed NCrunch predates the fix, pin 3.2.2 / 3.1.5 with a comment.

### Alternatives Considered
- MSTest/NUnit: not the org convention.
- InMemory EF: hides SKIP LOCKED, FTS and NOTIFY behaviour.

### Consequences
- Docker is required to run integration tests locally and in CI.
- The NCrunch version check is a PHASE-01 task.

### Approval
- **Approved by:** Jon Seeley (reviewed draft spec)
- **Approved on:** 2026-10-02

---

## D-014: HtmlSanitizer + Markdig

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-05 (message sanitization), PHASE-08 (KB rendering), 03-PACKAGE-MAP

### Context
Customer message bodies and KB content are rendered in the portal and admin, so stored HTML must be safe against XSS.

### Decision
`HtmlSanitizer` implements `IHtmlSanitizer`, applied to message bodies before storage and to rendered KB HTML. `Markdig` implements `IMarkdownRenderer` for KB articles. Markdown output is always sanitized afterward. Both sit in Infrastructure behind Application interfaces.

### Alternatives Considered
- Encode-only plain text: loses KB formatting.
- Custom sanitizer: high risk, no benefit.

### Consequences
- The allowlist of tags/attributes needs tests, including known XSS vectors, in PHASE-12 security review.
- Adds two dependencies; HtmlSanitizer 10.x is beta-only, so 9.2.x is pinned.

### Approval
- **Approved by:** Jon Seeley (owner confirmation)
- **Approved on:** 2026-10-02

---

## D-015: `docs/architecture` supersedes the superpowers spec

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** 00-DISCOVERY-INDEX, `docs/superpowers/specs/2026-10-02-techstrap-core-design.md`

### Context
The draft spec predates the template discovery workflow and lacks several decisions made in Q&A. Two sources of truth would drift.

### Decision
The indexed set in `docs/architecture/` is authoritative. The draft spec is kept as superseded input, its status line updated to "Superseded by docs/architecture" and linked from 00-DISCOVERY-INDEX. Where they differ, `docs/architecture` wins.

### Alternatives Considered
- Edit the spec in place: does not match the template's artifact contract.
- Delete the spec: loses rationale history.

### Consequences
- Changes after approval go into `docs/architecture` only; the spec is not maintained.

### Approval
- **Approved by:** Jon Seeley (owner Q&A)
- **Approved on:** 2026-10-02

---

## D-016: Handlers accept `TechStrap.Contracts` request records directly

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** 02-ARCHITECTURE (section 2, A-15 resolved), PHASE-04, PHASE-05, PHASE-06, 01-REQUIREMENTS (A-15, Q-12)

### Context
Handlers need request types. Duplicating every wire shape as an Application-owned record plus a mapping step adds code with no behaviour. The risk of using Contracts directly is leaking MVC or serialization concerns into Application.

### Decision
`TechStrap.Contracts` is a dependency-free leaf project: no MVC, EF or ASP.NET references and no attributes. `TechStrap.Application` references Contracts, and handlers accept Contracts request records (and return Contracts DTOs) directly. Transport-only details are mapped by the controller into the request record or into a small Application-owned model passed beside it: multipart files (as streams with name and content type), the honeypot flag, channel and trust level, and header values (`X-Api-Key`, `X-Ticket-Token`, `Idempotency-Key`). Resolves 02-ARCHITECTURE assumption A-15.

### Alternatives Considered
- Application-owned request records mapped in every controller: duplicates every type and adds a mapping layer.
- Contracts with validation attributes: couples the public SDK surface to a validation framework.

### Consequences
- Contracts changes are visible to handlers, the SDK and the Blazor apps at once; the public package semver rules (D-005) apply.
- `ProjectReferenceDirectionTests` allow Application to Contracts and forbid Contracts references to anything.
- Validation lives in handlers (and in client-side form ViewModels), not in Contracts attributes.

### Approval
- **Approved by:** Jon Seeley (owner confirmation)
- **Approved on:** 2026-10-02

---

## D-017: Attachment downloads in Admin and Portal are pass-through streaming proxies

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-07 (Admin `/attachments/{id}`), PHASE-09 (Portal `/t/{token}/attachments/{id}`), PHASE-06 (`GetAttachmentRequestHandler`), 02-ARCHITECTURE (section 7.6)

### Context
The agent bearer token and the customer access token are held server-side by Admin and Portal, so a browser link cannot call the API directly with the right credential.

### Decision
Admin exposes `GET /attachments/{id}` and Portal exposes `GET /t/{token}/attachments/{id}`. Each is a pass-through streaming proxy to `GET /api/attachments/{id}` through the typed client, forwarding the credential server-side and streaming the response with `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`. They execute no application workflow and touch no persistence, so they are exempt adapters, not new use-case entry points. Authorization is enforced by the API's `GetAttachmentRequestHandler`.

### Alternatives Considered
- Short-lived signed API URLs: needs a new signing handler and exposes the API download route publicly.
- Putting the token in the browser link to the API: leaks credentials into browser history, logs and referrers.

### Consequences
- The two adapters differ in credential and path, so they stay separate (PHASE-09 duplication note).
- Architecture tests assert neither host references Infrastructure and each adapter calls only the attachments client.
- Not a boundary deviation.

### Approval
- **Approved by:** Jon Seeley (owner confirmation)
- **Approved on:** 2026-10-02

---

## D-018: Live-update broadcast through an EF post-commit interceptor

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-10, PHASE-06, 02-ARCHITECTURE (sections 3, 6.6, 7.5), D-007

### Context
API-originated ticket changes must reach hub groups after commit, without touching the P06 handlers and without notifying on rolled-back work. Worker-originated changes must reach the API process (D-007).

### Decision
An Infrastructure EF `SaveChanges` post-commit interceptor (`TicketChangePublishingInterceptor`) reads the `TicketEvent` rows committed in the unit of work and calls `ITicketChangeBroadcaster` once per affected ticket. The P05 and P06 handlers are unchanged and never see the broadcaster. The API registers the SignalR implementation; the Worker registers a Postgres `NOTIFY` implementation (`PgNotifyTicketChangeBroadcaster`) of the same interface, which the API listener relays through `RelayTicketChangeHandler`. Broadcast failures are logged and never fail the request.

### Alternatives Considered
- Each handler calls the broadcaster explicitly: every handler needs the dependency and a test, and a forgotten call silently loses updates.
- Outbox-style change table polled by the API: durable, but more schema and latency for a best-effort feature.

### Consequences
- Cross-cutting behaviour lives in Infrastructure; it needs integration tests for commit, rollback and failure isolation (P10-T07).
- Delivery stays at-most-once, best effort (D-007).
- If the owner prefers explicit calls, remove the interceptor and add the broadcaster to the handler tables in 02-ARCHITECTURE.

### Approval
- **Approved by:** Jon Seeley (owner confirmation)
- **Approved on:** 2026-10-02

---

## D-019: Real client IP through Portal to API

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-01 (compose and subnet), PHASE-05 (rate limits), PHASE-09 (forwarding), 02-ARCHITECTURE (section 11.2), _template `CLIENT_IP_RATE_LIMITING.md`

### Context
Public intake and customer routes are rate-limited by client IP in the API, but the API sees the Portal container as the caller for anything the Portal proxies.

### Decision
The Portal forwards the original client IP (taken from the trusted proxy header it received) in `X-Forwarded-For` on every API call through `.AddForwardedClientIp()`. The API's ForwardedHeaders trusts only the pinned compose subnet (`172.16.31.0/24`, owner-confirmed 2026-10-02; outside Docker's default auto-assign pool), plus the single proxy `/32` when the reverse proxy runs outside that subnet (deployment-specific, Q-08); it never trusts `172.16.0.0/12` or `0.0.0.0/0`. Rate-limit partitioning uses the resolved client IP. PHASE-05 (limits) and PHASE-09 (forwarding) both reference this decision. A PHASE-01 compose task registers TechStrap's subnet in the `_template` CLIENT_IP_RATE_LIMITING.md subnet registry (cross-repo PR, owner action).

### Alternatives Considered
- Rate-limit by connection IP: collapses every visitor into the Portal's address.
- Trust all private ranges: lets any container spoof the client IP.

### Consequences
- Compose must pin the subnet; changing it needs a matching trusted-network change.
- Tests use the default checked-in trusted network and an `IStartupFilter` for `RemoteIpAddress`.
- The _template registry row is an owner action outside this repo.

### Approval
- **Approved by:** Jon Seeley (owner confirmation)
- **Approved on:** 2026-10-02

---

## D-020: Optional `Idempotency-Key` on intake

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-05 (P05-T17), PHASE-11 (P11-T04, P11-T17), 02-ARCHITECTURE (sections 5, 7.2)

### Context
Ticket creation is not naturally idempotent, so the SDK could not retry a submit after a timeout without risking duplicate tickets (previously open in PHASE-11).

### Decision
`POST /api/intake/tickets` accepts an optional `Idempotency-Key` header. Keys are scoped per API key (so per product) and retained for 24 hours (**Assumption**). A repeat with the same key returns the original ticket response without creating a ticket. The SDK sends a key per submit call and enables retries for submit only when a key is set. Keys are stored through `IIntakeIdempotencyStore`.

### Alternatives Considered
- No retries on submit: simple, but a transient failure loses the report.
- A body field (`ClientRequestId`): puts a transport concern in the wire model; the header is the common convention.

### Consequences
- Adds a small table and a retention sweep (P05-T17).
- A key reused with a different body returns the original response (documented); changing that to a conflict is a later option.

### Approval
- **Approved by:** Jon Seeley (owner confirmation)
- **Approved on:** 2026-10-02

---

## D-021: KB live preview through the API; KB images are exempt static assets

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-08, 02-ARCHITECTURE (sections 7.4, 7.6, 8.1), D-014

### Context
The Admin editor needs a live Markdown preview. Rendering locally in Admin would duplicate the Markdig and sanitizer pipeline and risk parity drift. KB images must be readable by anonymous portal visitors.

### Decision
`POST /api/kb/preview` is served by `RenderKbPreviewRequestHandler` (Agent policy only), so a single Markdig plus HtmlSanitizer pipeline (`IMarkdownRenderer`, `IHtmlSanitizer`) renders both previews and published pages. Admin has no local renderer; its preview pane calls the API through the KB client. KB images are written under a public-read prefix (`kb-images/`) in storage and served by the API as static assets; they are exempt operational/static endpoints that execute no application workflow. Ticket attachments never live under that prefix.

### Alternatives Considered
- Admin-local `KbPreviewRenderer` with a parity test: no round trip, but two pipelines to keep identical.
- A handler that serves KB images: unnecessary for public, non-sensitive files.

### Consequences
- Each preview costs an API round trip; the editor debounces (PHASE-08).
- One XSS corpus covers preview and published rendering.
- Public image exposure is limited to the prefix; uploads are validated by `UploadKbImageRequestHandler`.

### Approval
- **Approved by:** Jon Seeley (owner confirmation)
- **Approved on:** 2026-10-02

---

## D-022: Destructive and system operations are Admin-only

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** 01-REQUIREMENTS (FR-TKT-10, FR-TKT-15, FR-PRIV), 02-ARCHITECTURE (sections 4, 7.3), PHASE-04, PHASE-06, PHASE-07, UX-BRIEF-admin, D-006

### Context
Delete, erase and system-health operations are high impact. Several drafts left who may perform them as an assumption.

### Decision
These require the `Admin` policy: delete ticket (`DeleteTicketRequestHandler`), erase requester (`EraseRequesterRequestHandler`), dead-letter list, retry and discard, and product, API key and agent management (`UpdateAgentRequestHandler` and the product and key handlers). Mark spam (`MarkTicketSpamRequestHandler`) stays available to Agents. Reads that agents need for daily work (product list, tag list, and the active-agent list for assignment) are Agent-readable.

### Alternatives Considered
- Any Agent may delete or erase: simpler UI, but a single compromised agent account could destroy data.
- Admin-only spam: slows triage for no safety gain, since spam is reversible.

### Consequences
- The Admin UI hides delete and erase from Agents; the API enforces the policy (`TicketAuthorizationTests`).
- Admin-role actions write `AdminEvent` records, including delete, erase and dead-letter actions (see D-006).

### Approval
- **Approved by:** Jon Seeley (owner confirmation)
- **Approved on:** 2026-10-02

---

## D-023: Visual direction: Carbon Copy v2; mascot only in Admin brand moments; portal shows Powered-by only

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** `docs/BRAND.md`, `docs/design/directions.md`, `docs/design/mockups/direction-carbon-copy-v2.html`, PHASE-02, PHASE-07, PHASE-09, PHASE-05, UX-BRIEF-admin, UX-BRIEF-portal, D-002

### Context
PHASE-02 required an owner-selected visual direction before any UI work. Three directions were mocked up (Beige Box, Night Shift Console, Carbon Copy). The owner picked Carbon Copy as the base and requested revisions, producing v2.

### Decision
The visual direction is **Carbon Copy v2**, as defined in `docs/BRAND.md` (reference implementation: `docs/design/mockups/direction-carbon-copy-v2.html`):
- Carbon Copy base: ruled-ledger queue, numbered form header on the ticket view only, and the white / canary / pink carbon tint code (customer / public reply / internal note), in light and dark themes.
- The Night Shift keyboard layer re-skinned (keycaps, j/k, Ctrl/Cmd+K palette, status bar), with the rule that single-key shortcuts never fire while typing.
- Calmer stamps: straight and single-border in queue rows; tilted with a stamp-down animation only on the ticket view.
- Beige Box retro-window frames and the mascot palette (`--bm-*`) are used **only on Admin brand moments**: all-caught-up, agent sign-in and 404, plus the style guide and README/GitHub. The mascot never appears on working screens.
- The portal stays plain and product-led and light-only in v1; TechStrap appears only as "Powered by TechStrap" with an optional 16px head mark. It uses no carbon tints, stamps or windows.
- Product accents may set only `--accent`, `--on-accent` and `--accent-ink`, derived by one function enforced at product save (PHASE-04) and reused by email rendering (PHASE-05).
- Fonts (IBM Plex Sans, IBM Plex Mono, Source Serif 4) are self-hosted; no CDN at runtime.

### Alternatives Considered
- Beige Box as the whole look: closest to the mascot, but boxy at density and prone to Windows 95 parody on working screens.
- Night Shift Console: strongest keyboard model, but cold and mono-heavy; kept as the keyboard layer only.
- Original Carbon Copy: kept as the base; tilted stamps in dense lists and the lack of a keyboard model led to v2.
- Mascot or TechStrap styling in the portal: rejected; customers deal with the product, not with us.

### Consequences
- BRAND.md is complete and is the constraint for PHASE-07 and PHASE-09. The mockups are throwaway reference.
- P02-T05 builds the token layer from BRAND.md section 12; P02-T08 delivers SVG mascot assets (PNG until then).
- PHASE-04 must implement and test the accent derivation and contrast rejection; PHASE-05 reuses it.
- Adding a brand moment or a new use of a carbon tint needs a new owner decision.
- The published artifact links are private; the repo mockups are the durable record.

### Approval
- **Approved by:** Jon Seeley (owner)
- **Approved on:** 2026-10-02

---

## D-024: Customer-facing identity, spam recovery and portal prefill

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** 01-REQUIREMENTS (FR-TKT-18, FR-TKT-19, FR-CUST-07 to FR-CUST-09, FR-ADMIN-07, FR-EMAIL-09), 02-ARCHITECTURE (sections 5, 7.1, 7.3, 8.2, 11.1), PHASE-03, PHASE-04, PHASE-05, PHASE-06, PHASE-07, PHASE-09, UX-BRIEF-admin (Q11), UX-BRIEF-portal, BRAND.md, D-006, D-022, D-023

### Context
Four open UX questions blocked PHASE-07 and PHASE-09: whether v1 ships a Spam view (Admin Q11), how agents are named to customers, what the "Powered by TechStrap" mark links to and whether it can be hidden, and which parameters an in-app link may prefill on the portal contact form.

### Decision
1. **Spam view.** The Admin rail has a dedicated **Spam** view listing tickets with `is_spam = true` (all statuses). Normal views (Unassigned, Mine, Open, Pending, All) exclude spam. A one-key **Not spam** action restores a ticket; the key is `u` (no clash with `j`, `k`, `r`, `n`, `e`, `/`, `?`; layout-independent, no Shift). Restoring is available to any Agent, like marking spam (D-022).
2. **Agent identity shown to customers** (portal ticket view and emails). Default: the agent's first name plus the product's support name, "Sam from Orbitly Support" (`{first name} from {Product display name} Support`). An agent may set an optional **public display name**; when set it replaces the first name and is used as-is ("Samantha"; "Sam W. from Orbitly Support" is never generated). **Assumption:** the " from {Product} Support" suffix is still appended to the override ("Samantha from Orbitly Support"). Agent email addresses and surnames are never shown to customers unless the agent typed a surname into their own override. The agent edits the override in "My settings". Stored as nullable `agents.public_display_name` (PHASE-03).
3. **"Powered by TechStrap"** links to https://github.com/Syntax-Circus/techstrap and is shown by default on every portal page and in customer emails. An installation-level setting hides it: env var `TECHSTRAP_PORTAL_SHOW_POWERED_BY` (default `true`), read by the Portal host and by email rendering in the Worker. It is not per product.
4. **Portal contact-form prefill.** The contact URL may prefill `subject`, `name` and `email` (query string). All three stay visible and editable; there are no hidden fields. App context (version, device, user reference) travels through the SDK and API, never the query string. Prefilled values are validated and length-limited exactly like typed input (same model attributes, same server rules).

### Alternatives Considered
- Spam recovery by search and filters only: rejected; spam false positives would be hard to find.
- Key `!` for Not spam: needs Shift and varies by keyboard layout; `u` chosen.
- Always showing the full agent name, or a product-level "Support" only: rejected; first name is friendly and the override gives agents control.
- Dropping the suffix when an override is set: viable, but keeping it keeps every sender recognisably from the product (Assumption, reversible in one constant).
- Powered-by as plain text, always on: not chosen; a link and an installation-level hide switch were chosen. A per-product switch was not requested.
- Hidden prefill fields for app context: rejected; they invite PII in URLs and cannot be seen or corrected by the customer.

### Consequences
- Contracts: `TicketView.Spam`, `AgentDto.PublicDisplayName`, `UpdateMyProfileRequest`, and a shared format constant for the public name. A pure resolver in Domain produces the customer-facing name; handlers pass the resolved string (never the agent's email or full name) to customer DTOs and email payloads.
- `UpdateNotificationPreferencesRequestHandler` is per-product alert opt-in, so it does not fit; a new `UpdateMyProfileRequestHandler` (`PUT /api/agents/me/profile`) is added in PHASE-04 (PHASE-04 then has 17 handlers).
- Email rendering takes the resolved name and the Powered-by flag. Per 02-ARCHITECTURE 6.4 the Worker renders at drain, so the Worker reads the variable. If PHASE-05 keeps its enqueue-time rendering Assumption, the API host must read it too and its `.env.example` gains the key (decided in P05-T18).
- Changing an agent's override affects emails queued afterwards; already queued rows keep the name captured at enqueue.
- Portal contact page needs prefill binding with the same validation as posts; the SDK contract carries any extra context.

### Approval
- **Approved by:** Jon Seeley (owner)
- **Approved on:** 2026-10-02

---

## D-025: The product-accent derivation helper lives in `TechStrap.Contracts`

- **Status:** Approved
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** `docs/BRAND.md` section 22, PHASE-02 (P02-T09), PHASE-04 (product save validation), PHASE-05 (email rendering), PHASE-09 (portal theming), D-016

### Context
BRAND.md section 22 requires one pure function that turns a product accent (`#RRGGBB`) into `--ts-accent`, `--ts-on-accent` and `--ts-accent-ink`. Three hosts need it: the Portal (runtime properties), the API (reject a malformed accent at save, PHASE-04) and email rendering (PHASE-05, in the Worker or Api). The architecture tests fix the reference direction: Application references only Domain, Contracts and `SyntaxCircus.Common`; Api and Worker reference Application, Infrastructure and Contracts; Admin and Portal reference Contracts only.

### Decision
`TechStrap.Contracts.Branding.ProductAccent` (a static class with `TryDerive` and `ContrastRatio`, plus the `ProductAccentColors` record) lives in `TechStrap.Contracts`. Contracts is the only project every consumer already references, it stays dependency-free (the helper uses only the BCL), and the rule is part of the wire contract: the stored accent and the values derived from it must agree in every host.

### Alternatives Considered
- Domain: Portal and Admin may not reference Domain (architecture tests), so the Portal would need its own copy.
- A new `TechStrap.Branding` project: needs an `AllowedProjectReferences` entry for every consumer and a change to the "ten source projects" test, for roughly 100 lines of code.
- A Portal-local helper plus a second copy in Application: two implementations of one rule, which BRAND.md forbids.

### Consequences
- `TechStrap.Contracts` is published as a NuGet package (PHASE-11), so `ProductAccent` becomes public surface of that package and is covered by its semver promise. It is small and stable; revisit if the surface grows.
- The architecture tests need no change: Contracts still has no project, package or framework reference.
- PHASE-04 calls `ProductAccent.TryDerive` in `UpdateProductRequestHandler`; PHASE-05 calls it in the email renderer; PHASE-09 calls it in the portal product theme.

### Approval
- **Approved by:** Jon Seeley (owner, 2026-10-02 plan review)
- **Approved on:** 2026-10-02
