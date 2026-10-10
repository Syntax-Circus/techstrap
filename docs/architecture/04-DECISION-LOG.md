# 04 - Decision Log

Format: _template `DECISION_TEMPLATE.md` (status, date, owner, related artifacts, context, decision, alternatives, consequences, approval). In this log **Approved** means the template's "Accepted". Owner of every decision: Jon Seeley (project owner).

Approval basis:
- **Owner Q&A (2026-10-02):** decisions settled directly by the owner (plan table B).
- **Reviewed draft spec:** decisions carried over from `docs/superpowers/specs/2026-10-02-techstrap-core-design.md`, which the owner reviewed and used as the baseline. Approved on that basis.
- **Owner confirmation (2026-10-02, after review):** D-014 and D-016 to D-022 were drafted by Claude (D-014 from plan section C; D-016 to D-022 while reconciling the artifact set) and then explicitly confirmed by the owner. D-018 also confirms the relay mechanism of D-007.
- **Proposed:** none. D-008's default (N = 7 days) was confirmed by the owner on 2026-10-02.
- **Owner decision (2026-10-02, PHASE-03 planning):** D-026 (separate persistence entities) and D-027 (stored generated search vectors); D-028 (Domain result type) approved 2026-10-03.
- **Owner decision (2026-10-03, PHASE-04 planning):** D-029 (roles from IdP groups only; amends D-004), D-030 (deleting a tag in use), D-031 (product accent validation is format only).
- **Owner decision (2026-10-03, PHASE-05 planning):** D-032 (intake rules: honeypot, untrusted external ref, link cap, attachments). D-033 and D-034 were proposed in the PHASE-05 plan and approved when the owner approved the plan.
- **Owner decision (2026-10-03, PHASE-06 planning):** D-035 (PHASE-06 split, Markdown replies, agent attachments, Solved notice). D-036 was proposed in the PHASE-06a plan and approved when the owner approved the plan.
- **Owner decision (2026-10-03, PHASE-06b planning):** D-037 (three-PR split, lost link keeps old links, follow-up dedupe window, no auto-close email). D-038 was proposed in the PHASE-06b plan and approved when the owner approved the plan.
- **Owner decision (2026-10-03, PHASE-06c planning):** D-039, the owner decisions on erase scope, follow-ups of a deleted ticket and outbox retention. Its technical decisions were proposed in the PHASE-06c plan and approved when the owner approved the plan.
- **Owner decision (2026-10-04, PHASE-07 planning):** D-040, the owner decisions on the PHASE-07 split, the identity provider and the shared hosting project. Its technical decisions were proposed in the PHASE-07a plan and approved when the owner approved the plan.
- **Owner decision (2026-10-04, PHASE-07b planning):** D-041, the owner decisions on read-only roles in the Admin, the logo URL field, the tag ticket count and the single PR. Its technical decisions were proposed in the PHASE-07b plan and approved when the owner approved the plan.
- **Owner decision (2026-10-04, PHASE-07c planning):** D-042, the owner decisions on the CSP, the theme flash, the time zone source, the shared host wiring and the single PR. Its defaults were proposed in the PHASE-07c plan and approved when the owner approved the plan.
- **Owner decision (2026-10-05, scoped configuration and deployment, before PHASE-08):** D-043, the owner decisions on one image-only deploy compose, scoped env files on the host, a separate Postgres and explicit image tags. Its technical decisions were proposed in the plan and approved when the owner approved the plan.
- **Owner decision (2026-10-05, PHASE-08 planning):** D-044, the owner decisions on slugs across scopes, the image URL setting, no KB audit and the single PR. Its defaults and technical decisions were proposed in the PHASE-08 plan and approved when the owner approved the plan.
- **Owner decision (2026-10-05, PHASE-09 planning):** D-045, the owner decisions on the three-PR split, vanilla-JS KB suggestions, the two small public API additions, the root page and ticket theming. Its technical rulings were proposed in the PHASE-09a plan and approved when the owner approved the plan.
- **Owner decision (2026-10-07, PHASE-10 planning):** D-046, the owner decisions on the two-pull-request split, the detail page's "New activity" banner, the presence name and the hub token source. Its technical rulings were proposed in the PHASE-10a plan and approved when the owner approved the plan.
- **Owner decision (2026-10-07, PHASE-11 planning):** D-047, the owner decisions on the three-PR split, the JSON-only SDK, the SDK's dependencies and the Common split, and the MAUI CI split. Its technical rulings were proposed in the PHASE-11a plan and approved when the owner approved the plan.
- **Owner decision (2026-10-07, PHASE-11b planning):** D-048, the owner decisions on the single net10.0 target framework, the one-pull-request delivery and the approved MAUI helper design. Its technical rulings were proposed in the PHASE-11b plan and approved when the owner approved the plan.
- **Owner decision (2026-10-08, PHASE-11c planning):** D-049, the owner decisions that nuget.org is ready (IDs, Trusted Publishing policy, NUGET_USER, the release environment), that the XML documentation file is enabled for every package member, that the post-publish check runs against the local compose stack until UAT exists, and the approved publish design. Its technical rulings were proposed in the PHASE-11c plan and approved when the owner approved the plan.
- **Owner decision (2026-10-08, PHASE-11e planning):** D-050, the owner decisions that product hosts are their own small phase before PHASE-12, that the host-to-product mapping lives on the Product and not in configuration, that URLs on a product host are clean while the default host keeps `/p/{key}` and 301s a hosted product to its host, and that D-002 is amended. Its technical rulings were proposed in the PHASE-11e plan and approved when the owner approved the plan.
- **Owner decision (2026-10-08, PHASE-12 planning):** D-051, the owner decisions that PHASE-12 ends at `v0.3.0` (1.0.0 waits for a deliberate lock of the Contracts public surface), that it ships as three pull requests (12a, 12b, 12c), that the load and recovery budgets are accepted, that OpenAPI stays served in Production, that the carried-forward PHASE-03/04 items fold into 12a, and that CI fails on High/Critical scan findings from day one. Its technical rulings were proposed in the PHASE-12a plan and approved when the owner approved the plan.
- **Owner decision (2026-10-02, PHASE-02):** D-023 (visual direction) was chosen by the owner after reviewing the mockups.
- **Owner decision (2026-10-02, after UX briefs):** D-024 (customer-facing identity, spam recovery, portal prefill) settled the open owner questions in UX-BRIEF-admin (Q11) and UX-BRIEF-portal.

**Boundary deviations: none.** Every application entry point maps to a named `I...Handler`, and no decision in this log departs from the mandatory flow in _template `APPLICATION_ARCHITECTURE.md`. D-012 records why the worker shares Infrastructure with the API; it is not a deviation. This log therefore contains no "Boundary Deviation Details" sections.

## Index

| ID | Title | Status | Date | Related |
| --- | --- | --- | --- | --- |
| D-001 | Two API key kinds: trusted and public | Approved (owner Q&A) | 2026-10-02 | PHASE-04, PHASE-05, PHASE-11 |
| D-002 | Single portal domain with per-product theming | Approved (owner Q&A) | 2026-10-02 | PHASE-02, PHASE-09 |
| D-003 | MIT public OSS, GHCR images, GitHub Actions | Approved (owner Q&A) | 2026-10-02 | PHASE-01, PHASE-12 |
| D-004 | Claim-gated agents with bootstrap admin | Approved (owner Q&A); amended by D-029 | 2026-10-02 | PHASE-04 |
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
| D-026 | Separate persistence entities (records); Domain stays pure | Approved (owner 2026-10-02) | 2026-10-02 | PHASE-03, 02-ARCHITECTURE |
| D-027 | Full-text search uses stored generated tsvector columns | Approved (owner 2026-10-02) | 2026-10-02 | PHASE-03, PHASE-06, PHASE-08 |
| D-028 | Domain returns its own result type; Application converts it | Approved (owner 2026-10-03) | 2026-10-02 | PHASE-03, 02-ARCHITECTURE |
| D-029 | Agent roles come from IdP groups only; no bootstrap admin | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-04, PHASE-07, 02-ARCHITECTURE, D-004 |
| D-030 | Deleting a tag in use: reject unless forced; forced delete detaches with events | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-04, PHASE-06 |
| D-031 | Product accent validation is format only | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-02, PHASE-04, D-025 |
| D-032 | Intake rules: honeypot, untrusted external ref, access-link cap, attachment limits | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-05, PHASE-09, PHASE-11 |
| D-033 | Emails render in the Worker at send time; Application owns the sender abstraction | Approved (owner, PHASE-05 plan review) | 2026-10-03 | PHASE-05, PHASE-06, PHASE-12, 02-ARCHITECTURE |
| D-034 | Intake transport: explicit Public policy, ApiKey scheme policy, JSON-only API intake | Approved (owner, PHASE-05 plan review) | 2026-10-03 | PHASE-05, PHASE-09, PHASE-11 |
| D-035 | PHASE-06 split into 06a/06b; Markdown agent replies; agent reply attachments; Solved notice | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-06, PHASE-07, PHASE-09 |
| D-036 | Ticket operations API shape: RowVersion on state changes, TicketStateDto returns, idempotent tags, string enums, multipart replies, lookup by id or number | Approved (owner, PHASE-06a plan review) | 2026-10-03 | PHASE-06, PHASE-07, PHASE-11 |
| D-037 | PHASE-06 lands as 06a/06b/06c; lost link keeps old links; 2-minute follow-up dedupe; auto-close sends no email | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-06, PHASE-09 |
| D-038 | Customer API: Public routes with in-handler token auth, uniform 404, separate customer attachment route, lost-link rules, alert recipients, reopen window from AutoCloseOptions, per-ticket auto-close | Approved (owner, PHASE-06b plan review) | 2026-10-03 | PHASE-06, PHASE-09, PHASE-12 |
| D-039 | PHASE-06c: erase covers subject, metadata and outbox rows; hard delete unlinks follow-ups; outbox retention sweep and lookup index; Serilog redaction enricher; multipart hardening | Approved (owner 2026-10-03; technical decisions at PHASE-06c plan review) | 2026-10-03 | PHASE-06, PHASE-07, PHASE-09, PHASE-12 |
| D-040 | PHASE-07 lands as 07a/07b/07c; Authentik set up later; TechStrap.Hosting project; Admin sign-in, API clients and ticket-handling decisions | Approved (owner 2026-10-04; technical decisions at PHASE-07a plan review) | 2026-10-04 | PHASE-07, PHASE-08, PHASE-09, PHASE-12 |
| D-041 | PHASE-07b: roles are read-only in the Admin; the product logo is a validated URL; the tag list shows ticket counts; Admin guard, browser preferences and client decisions | Approved (owner 2026-10-04; technical decisions at PHASE-07b plan review) | 2026-10-04 | PHASE-07, PHASE-08, PHASE-12 |
| D-042 | PHASE-07c: CSP with `style-src-attr`, theme init script, browser time zone, shared host wiring, command palette, responsive rail, session-expired banner, Sentry search scrub, OpenAPI security schemes | Approved (owner 2026-10-04; defaults at PHASE-07c plan review) | 2026-10-04 | PHASE-07, PHASE-09, PHASE-11, PHASE-12 |
| D-043 | Scoped per-project configuration (every key in `appsettings.json`, a `.env.example` and a deploy env template per host) and one image-only deploy compose for UAT and production, with a separate Postgres | Approved (owner 2026-10-05; technical decisions at plan review) | 2026-10-05 | PHASE-12, PHASE-08, PHASE-09 |
| D-044 | PHASE-08: slugs unique across scopes, a separate KB Markdown profile, image URLs from `TECHSTRAP_API_PUBLIC_URL`, 400 outcomes for publish and image errors, public routes with the product key, reply-link validation and email links | Approved (owner 2026-10-05; defaults at PHASE-08 plan review) | 2026-10-05 | PHASE-08, PHASE-09, PHASE-12 |
| D-045 | PHASE-09: three pull requests, vanilla-JS KB suggestions, two small public API additions, a default-product root page, per-product theming from the DTO, a Portal API client, per-path headers, the real Blazor.Seo API | Approved (owner 2026-10-05; technical rulings at PHASE-09a plan review) | 2026-10-05 | PHASE-09, PHASE-12 |
| D-046 | PHASE-10: two pull requests, identity in the request record, a two-part post-commit hook, header-only hub token, presence by `Agent.Name`, the detail page's "New activity" banner | Approved (owner 2026-10-07; technical rulings at PHASE-10a plan review) | 2026-10-07 | PHASE-10, 02-ARCHITECTURE |
| D-047 | PHASE-11: three pull requests, JSON-only SDK v1, web-neutral SyntaxCircus.Common 0.2.0 + Http.Resilience as dependencies, retry only with an Idempotency-Key through HttpRequestResiliencePipeline | Approved (owner 2026-10-07; technical rulings at PHASE-11a plan review) | 2026-10-07 | PHASE-11, 03-PACKAGE-MAP |
| D-048 | PHASE-11b: TechStrap.Client.Maui on a single net10.0 target with Microsoft.Maui.Essentials, TicketMetadataKeys in Contracts, MauiTicketDraft submit helper, lazy Essentials defaults | Approved (owner 2026-10-07; technical rulings at PHASE-11b plan review) | 2026-10-07 | PHASE-11, 03-PACKAGE-MAP |
| D-049 | PHASE-11c: tag-triggered publish-nuget.yml with NuGet Trusted Publishing and a GitHub Release, version from the tag, XML documentation and AOT-safe JSON in the packages, compiled README snippets and a console sample | Approved (owner 2026-10-08; technical rulings at PHASE-11c plan review) | 2026-10-08 | PHASE-11, PHASE-12, 03-PACKAGE-MAP |
| D-050 | PHASE-11e: product hosts - PortalHost on the Product, HostNameShape, host-aware links, ProductHostMiddleware with clean paths and canonical 301s, PortalLinks, per-host SEO and output cache (amends D-002) | Approved (owner 2026-10-08; technical rulings at PHASE-11e plan review) | 2026-10-08 | PHASE-11e, PHASE-12, 02-ARCHITECTURE, 05-SCHEMA |
| D-051 | PHASE-12: release hardening ends at v0.3.0 (1.0.0 deferred to an API-lock decision); three PRs 12a/12b/12c; budgets accepted; OpenAPI served in Production; carried-forward 03/04 items in 12a; CI fails on High/Critical scans | Approved (owner 2026-10-08; technical rulings at PHASE-12a plan review) | 2026-10-08 | PHASE-12, PHASE-11, 03-PACKAGE-MAP |

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
- **Amended by D-050 (2026-10-08):** products may have their own portal host; the default host keeps `/p/{key}`.
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

- **Status:** Approved; amended by D-029 (2026-10-03)
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-04, 03-PACKAGE-MAP (`AspNetCore.Authentication`)

### Context
Agents sign in via any OIDC provider. Authenticating to the IdP must not be enough to access support data, and the first admin needs a path in without a database edit.

### Decision
The API validates the OIDC JWT and an authorization policy requires a configured group claim: `TECHSTRAP_AGENT_GROUP` maps to `Agent`, `TECHSTRAP_ADMIN_GROUP` to `Admin`; others are rejected. The agent row is provisioned on first call to `GET /api/agents/me` (`GetCurrentAgentRequestHandler`). `TECHSTRAP_BOOTSTRAP_ADMIN` (email or subject) grants Admin to that user on provisioning.

**Amended by D-029 (2026-10-03):** the bootstrap admin is removed. Admin access comes from `TECHSTRAP_ADMIN_GROUP` alone, so the first admin is whoever is in that IdP group; no database edit is needed.

### Alternatives Considered
- Open sign-up with admin approval: more UI, and exposes an approval queue to anyone with an IdP account.
- Roles only in the database: needs a manual first-admin step.

### Consequences
- Setup docs must explain adding the group claim in each IdP.
- Removal from the group should block access at the next token; the agent `active` flag handles deprovisioning inside TechStrap.
- Bootstrap env var must be documented as removable after first sign-in.
- Amended in part by D-040: the Admin does not parse the group claim; its no-access page follows the API's answer to GET /api/agents/me.

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
- Amended in part by D-039: erase also covers the subject, metadata and outbox rows, and outbox retention is no longer deferred.

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
- Amended in part by D-040: the Admin adapter streams through the named read client (not a typed, buffering client) and there is no IAttachmentsClient.

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

---

## D-026: Separate persistence entities (records); Domain stays pure

- **Status:** Approved (owner decision 2026-10-02)
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-03, 02-ARCHITECTURE sections 2, 3 and 5, _template APPLICATION_ARCHITECTURE.md

### Context
PHASE-03 assumed EF Core would map the Domain types directly through fluent configuration. A domain with private setters, collections that raise events, a number that never changes and optimistic-concurrency tokens fits that badly: the entities would grow EF-shaped members (parameterless constructors, public setters, a `Version` property, a `SearchVector` property) that exist only for the database.

### Decision
- `TechStrap.Domain` stays pure: entities, value objects and invariants only, with no EF attributes and no EF-driven shapes.
- `TechStrap.Infrastructure` owns **persistence records**, one per table. **Naming convention:** a `Record` suffix on the singular name (`TicketRecord`, `MessageRecord`, `TicketTagRecord`), in the namespace `TechStrap.Infrastructure.Persistence.Records`, declared `internal sealed`. C# `record` types (such as the Contracts request records, D-016) are unrelated and never carry the suffix.
- Records are mapped with fluent `IEntityTypeConfiguration<T>` classes (`Persistence/Configurations`), snake_case names, string-backed enums and the Postgres `xmin` system column as the optimistic-concurrency token (`Version`).
- Repositories (interfaces in Application, implementations in Infrastructure) map between records and Domain or Application models. They never expose `IQueryable`, records or `DbSet`.
- Architecture tests enforce it: no type in Domain or Application is named `*Record` or references one (`PersistenceRecordBoundaryTests`); no Application abstraction exposes a record, EF, HTTP or Infrastructure type (`AbstractionShapeTests`); and no handler constructor takes a record (`HandlerConstructorDependencyTests`, the rule carried forward from PHASE-01).

### Alternatives Considered
- Map Domain types directly (the PHASE-03 assumption): less code, but the Domain would carry persistence concerns and the architecture rule "no EF-driven shapes in Domain" could not be kept.
- Attribute-based mapping on Domain types: forbidden, Domain references nothing.
- A shared "entity" base used by both: the same coupling under another name.

### Consequences
- A new field touches four places: the Domain type, the record, its configuration and the mapper. Integration tests that must seed or inspect rows directly see the records through `InternalsVisibleTo`.
- The concurrency token travels with the loaded Domain object (`Version`). On update the repository copies the changes onto the record loaded in the current scope and applies the Domain object's `Version` as that record's original `xmin`, so the UPDATE checks the token the caller saw, not the one read in the current request.
- The PHASE-03 risk "Assumption: directly" is resolved by this decision.

### Approval
- **Approved by:** Jon Seeley (owner decision)
- **Approved on:** 2026-10-02

---

## D-027: Full-text search uses stored generated tsvector columns

- **Status:** Approved (owner 2026-10-02)
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-03 (vectors and indexes), PHASE-06 (ticket search), PHASE-08 (KB search), D-011

### Context
Ticket search must cover the subject and the message bodies, which live in two tables, and KB search must weight title above summary above body. The PHASE-03 risk asked whether a trigger or an application-maintained column should keep the vectors current.

### Decision
- Search vectors are Postgres `GENERATED ALWAYS AS (...) STORED` columns with GIN indexes: `tickets.search_vector` (subject, weight A), `messages.search_vector` (sanitized body text, weight B) and `kb_articles.search_vector` (title A, summary B, body C). English configuration (A-07).
- There are no triggers and no application-maintained columns. The application never writes a vector.
- Ticket search joins the two: a ticket matches when its subject, one of its messages (public or internal; ticket search is agent-only) or its exact number matches, and the relevance is the subject rank plus the best message rank plus a boost for an exact number. KB search ranks the single article vector. Queries use `websearch_to_tsquery` and `ts_rank`.

### Implementation finding (verified on Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3)
`HasGeneratedTsVectorColumn(vector, config, properties)` exists in the pinned version, but it takes only a configuration name and a list of properties: it cannot give each column its own weight, and the weights are what this decision is for. The vectors are therefore declared with `HasComputedColumnSql("setweight(to_tsvector('english', coalesce(col, '')), 'A') || ...", stored: true)`, which is the same STORED generated column, still generated into the migration by `dotnet ef` (`computedColumnSql` with `stored: true`, plus a GIN index) and not written by hand. A generated column can read only its own row, so the requester name and email from the PHASE-03 ticket-vector sketch are not in the vector; the exact ticket number is matched by equality instead.

### Alternatives Considered
- A trigger on messages that updates the ticket vector: hidden logic, hand-written SQL in a migration.
- An application-maintained column: every code path that adds a message must remember to update it.
- `HasGeneratedTsVectorColumn` without weights: simple, but a body match would rank like a subject match (ticket) and a body match like a title match (KB).

### Consequences
- Resolves the PHASE-03 full-text-search risk. Index maintenance cost is paid on every write to the three tables.
- Changing the search configuration or weights is a new migration that rebuilds the generated columns.
- `SearchSchemaTests` pin the generated columns, the absence of triggers, the GIN indexes and index usability; `TicketSearchTests` and `KbSearchTests` pin the ranking.

### Approval
- **Approved by:** Jon Seeley (owner, 2026-10-02)
- **Approved on:** 2026-10-02

---

## D-028: Domain returns its own result type; Application converts it

- **Status:** Approved (owner 2026-10-03)
- **Date:** 2026-10-02
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-03, 02-ARCHITECTURE section 2, `ProjectReferenceDirectionTests`

### Context
The PHASE-03 document says Domain methods return `Result` from `SyntaxCircus.Common`. But Domain references no project, package or framework (02-ARCHITECTURE section 2, enforced by `Domain_and_Contracts_reference_no_project_package_or_framework`), and `SyntaxCircus.Common` is referenced by Application only.

### Decision
Domain defines `DomainResult`, `DomainResult<T>` and `DomainError` (kinds Validation, NotFound, Conflict, with a stable code and message), using only the BCL. Application converts them with `ToResult()` to `SyntaxCircus.Common` results; the conversion keeps the code and message, maps the kind one to one, and keeps a validation target only for validation errors, as the Common type requires.

### Alternatives Considered
- Let Domain reference `SyntaxCircus.Common`: one result type, but it changes the reference rules, the "Domain references nothing" statement in 02-ARCHITECTURE and one architecture test.
- Throw exceptions from Domain: invalid transitions are expected outcomes, not exceptional.

### Consequences
- Two small result types and one conversion extension, covered by `DomainResultExtensionsTests`.
- If the owner prefers the first alternative, the change is local: delete `DomainResult.cs`, reference the package from Domain, relax the rule that Domain references nothing (`Domain_and_Contracts_reference_no_project_package_or_framework` and the matching statement in 02-ARCHITECTURE section 2), and drop the statement that only Application references `SyntaxCircus.Common`.

### Approval
- **Approved by:** Jon Seeley (owner, 2026-10-03 plan review)
- **Approved on:** 2026-10-03

---

## D-029: Agent roles come from IdP groups only; no bootstrap admin

- **Status:** Approved (owner 2026-10-03)
- **Date:** 2026-10-03
- **Owner:** Jon Seeley
- **Related artifacts:** D-004, PHASE-04, PHASE-07, 02-ARCHITECTURE section 4

### Context
D-004 gated agents by group claim and added `TECHSTRAP_BOOTSTRAP_ADMIN` so the first admin could get in. PHASE-04 then assumed the stored role could also be edited through the API. Two sources of truth for the Admin role (claim and database) make access hard to reason about. A group claim already gives the first admin a path in without a database edit.

### Decision
- The `Agent` policy requires `TECHSTRAP_AGENT_GROUP` or `TECHSTRAP_ADMIN_GROUP`. The `Admin` policy requires `TECHSTRAP_ADMIN_GROUP`.
- The group claim type is configurable (`TECHSTRAP_GROUP_CLAIM_TYPE`, default `groups`).
- The stored `Agent.Role` mirrors the claim at each `GET /api/agents/me`: Admin when the caller is in the admin group, otherwise Agent. It is informational (lists, audit).
- `TECHSTRAP_BOOTSTRAP_ADMIN` is removed.
- `UpdateAgentRequest` changes only `IsActive`. The API cannot change a role.
- A deactivated agent is refused on every agent endpoint, whatever their claims.
- Deactivating the last active stored Admin is refused (`409 last-active-admin`). The check holds a row lock on the active admins, so two concurrent deactivations cannot both pass.

### Alternatives Considered
- **Claim as a floor plus stored roles** (bootstrap and API promotion): keeps D-004 as written, but Admin access then has two sources.
- **Stored role wins:** IdP group changes stop demoting anyone, so revoking access needs two systems.

### Consequences
- Setup docs explain the agent and admin groups and the claim type (`docs/self-hosting/AGENT-AUTHENTICATION.md`).
- Removing someone from the admin group demotes them at their next token. Their stored role catches up at their next `/me` call.
- The bootstrap env var disappears from `.env.example`, the compose files and the docs.
- PHASE-07's agent screen offers activate and deactivate only.
- Amended in part by D-041: the Admin shows the role as a read-only badge with the note "Roles come from your identity provider's groups."; the agents screen only activates and deactivates.

### Approval
- **Approved by:** Jon Seeley (owner, 2026-10-03 PHASE-04 planning)
- **Approved on:** 2026-10-03

---

## D-030: Deleting a tag in use: reject unless forced; forced delete detaches with events

- **Status:** Approved (owner 2026-10-03)
- **Date:** 2026-10-03
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-04 (`DeleteTagRequestHandler`), PHASE-06 (ticket timeline), `Ticket.DetachDeletedTag`

### Context
Tags are global. A tag that tickets still carry cannot simply disappear: the database refuses (foreign key), and the ticket timeline should explain why a tag vanished.

### Decision
- `DELETE /api/tags/{id}` returns `409 tag-in-use` with the number of tickets that carry the tag.
- `DELETE /api/tags/{id}?force=true` detaches the tag from every ticket that carries it, then deletes it, all in one transaction. Each detach records a `TagRemoved` ticket event with `reason` `tag-deleted`.
- Closed tickets are read-only for agents, but they are detached through `Ticket.DetachDeletedTag`. That method still records the event and leaves the status and last-activity time alone.
- The admin audit records `TagDeleted` with `slug` and `detachedTicketCount`.

### Alternatives Considered
- **Always detach:** one click silently rewrites many tickets.
- **Always reject:** agents must untag every ticket by hand first.

### Consequences
- A forced delete of a widely used tag loads and saves each ticket in one transaction. A concurrent edit to one of those tickets fails the delete with `409 concurrency-conflict`, and the admin retries.
- PHASE-06's timeline must render `TagRemoved` with reason `tag-deleted`.
- Amended in part by D-041: the 409 message still names the count; the Admin tag list and delete confirmation take it from GET /api/tags/summary.

### Approval
- **Approved by:** Jon Seeley (owner, 2026-10-03 PHASE-04 planning)
- **Approved on:** 2026-10-03

---

## D-031: Product accent validation is format only

- **Status:** Approved (owner 2026-10-03)
- **Date:** 2026-10-03
- **Owner:** Jon Seeley
- **Related artifacts:** D-025, PHASE-02, PHASE-04 (`CreateProductRequestHandler`, `UpdateProductRequestHandler`), `ProductAccent`

### Context
PHASE-04 asked for a 400 on a "low-contrast" product accent. The PHASE-02 `ProductAccent` helper (D-025) already derives an on-accent colour (white or black, at least 4.58:1) and an ink colour darkened to 4.5:1 for any accent, so no accent produces unreadable text.

### Decision
- A product accent must be `#RRGGBB`. A malformed value is `400 accent-colour-invalid` from `ProductBranding.Create`.
- Contrast is not validated.
- `ProductBrandingDto` returns the derived `OnAccentColour` and `AccentInkColour`, so the Admin preview shows exactly what customers see.

### Alternatives Considered
- **Reject accents below 3:1 against the white portal page:** stops near-white accents, but refuses brand colours some products really use. The derived ink keeps links and text readable regardless.

### Consequences
- The PHASE-04 "400 low-contrast accent" test is dropped.
- The PHASE-07 branding form shows a live preview built from the derived colours.

- **Amended by D-053 (2026-10-10):** a product that sets skin tokens (PHASE-11g) must satisfy the skin contrast rules; a product that sets none keeps format-only accent validation.

### Approval
- **Approved by:** Jon Seeley (owner, 2026-10-03 PHASE-04 planning)
- **Approved on:** 2026-10-03

---

## D-032: Intake rules: honeypot, untrusted external ref, access-link cap, attachment limits

- **Status:** Approved (owner 2026-10-03)
- **Date:** 2026-10-03
- **Owner:** Jon Seeley
- **Related artifacts:** PHASE-05 (`SubmitTicketRequestHandler`, `IAttachmentStore`, `TicketAccessToken`), D-001, D-020

### Context
PHASE-05 listed four assumptions for intake that need the owner's answer.

### Decision
- **Honeypot.** If the hidden web-form field is filled, the API answers with the normal 201 and a plausible ticket number. It creates no ticket and sends no email.
- **Untrusted external reference.** A public key or the web form may send an external user reference. The reference is dropped, and the response carries the warning `external-user-ref-ignored`. Only trusted keys store it (D-001).
- **Access-link lifetime.** Customer access tokens still slide 90 days on each use. They never live past 365 days after issue (`TicketAccessToken.MaxLifetime`).
- **Attachments.** 10 MiB per file, 25 MiB per message, at most 5 files. Allowed types are PNG, JPEG, GIF, WebP, PDF, plain text, `.log`, CSV and ZIP. Each file must match both its declared type and its leading bytes. The stored content type is the canonical type for the matched kind, never the client's value.

### Alternatives Considered
- **Visible honeypot error:** it teaches bots which field to skip.
- **Rejecting an untrusted external reference:** an app that sends the field by mistake would lose tickets.
- **A 180-day or 2-year cap:** a shorter cap means more refresh requests; a longer one means longer exposure if a link leaks.

### Consequences
- `ticket_access_tokens` gains an `issued_at` column (migration `AddAccessTokenIssuedAt`).
- PHASE-09's lost-link flow issues a fresh token when a link hits its cap.
- `IntakeLimits` (Contracts) holds the limits, so the Portal form and the SDK can validate before upload.

### Approval
- **Approved by:** Jon Seeley (owner, 2026-10-03 PHASE-05 planning)
- **Approved on:** 2026-10-03

---

## D-033: Emails render in the Worker at send time; Application owns the sender abstraction

- **Status:** Approved (owner, PHASE-05 plan review)
- **Date:** 2026-10-03
- **Owner:** Jon Seeley
- **Related artifacts:** D-010, D-024, 02-ARCHITECTURE 6.4 and 7.2, PHASE-05 (`DrainEmailOutboxHandler`), `EmailOutboxItem`

### Context
PHASE-05 assumed emails would render when queued, storing subject, text and HTML in the outbox row. 02-ARCHITECTURE section 6.4 and the D-024 consequences instead say the Worker renders at send time and reads `TECHSTRAP_PORTAL_SHOW_POWERED_BY`. The outbox payload is capped at 16 000 characters, which a branded HTML email with a text part can exceed.

There is also a layering problem. The architecture rules let Application reference only `SyntaxCircus.Common`, and a handler may depend only on Application interfaces. A handler therefore cannot depend on `SyntaxCircus.Email.IEmailSender` directly.

### Decision
- **What the outbox stores.** Each outbox row stores template data: a `kind`, such as `ticket-confirmation`, and a small JSON payload, such as the ticket number, subject, requester name and portal link.
- **Rendering and sending.** `DrainEmailOutboxHandler` loads the product's branding, renders with `IEmailTemplateRenderer` and sends through `IOutboundEmailSender`. Both interfaces belong to Application. Infrastructure's `SmtpOutboundEmailSender` adapts the second to `SyntaxCircus.Email.IEmailSender`.
- **Branding at send time.** Branding is read when the email is sent, so a branding change between queueing and sending shows the new branding.
- **The portal link.** The plaintext access token exists only when the email is queued, so the link is captured in the payload. A sent row's payload therefore holds a working link until a retention sweep removes it, carried forward to PHASE-12 (see D-039).
- **Nowhere else.** `email_outbox.payload` is the only column that may hold a plaintext token. The idempotency store (D-020) keeps the response without the link; a replay issues a fresh access token for the original ticket and returns a new link.

### Alternatives Considered
- **Render when queued, and raise the payload cap.** This leaves the link in the row just the same, freezes branding, and contradicts D-024's statement that the Worker reads the Powered-by setting.
- **Let Application reference `SyntaxCircus.Email`.** This breaks the project reference rules.

### Consequences
- The Worker reads `TECHSTRAP_PORTAL_SHOW_POWERED_BY`; the Api does not need to.
- PHASE-06 notification emails follow the same pattern: a payload kind plus a renderer template.
- The retention sweep for sent rows moved to PHASE-06c and deletes the rows after 90 days (D-039); there is no payload scrub.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-05 plan review)
- **Approved on:** 2026-10-03

---

## D-034: Intake transport: explicit Public policy, ApiKey scheme policy, JSON-only API intake

- **Status:** Approved (owner, PHASE-05 plan review)
- **Date:** 2026-10-03
- **Owner:** Jon Seeley
- **Related artifacts:** D-001, D-022, D-029, PHASE-04 `ControllerBoundaryRules`, PHASE-05 controllers

### Context
Every Api controller declares an authorization policy (PHASE-04 `ControllerBoundaryTests`). The new public routes allow anonymous callers. The intake route authenticates with an API key, not the default bearer scheme.

### Decision
- **Public policy.** `AuthorizationPolicies.Public` admits everyone, including anonymous callers. Public controllers declare it at class level and rely on rate limits and size limits.
- **ApiKey policy.** `AuthorizationPolicies.ApiKey` authenticates with the `ApiKey` scheme only and requires a product-id claim. A missing, unknown or revoked key gets the same empty 401.
- **Fallback.** The fallback policy stays "any authenticated caller".
- **API intake body.** `POST /api/intake/tickets` takes a JSON `SubmitTicketRequest` with no attachments in v1. The web form (`POST /api/public/products/{key}/tickets`) takes multipart with attachments.
- **Follow-ups.** Intake takes no ticket number. Follow-up submission is added by the Portal (PHASE-09), which must check that the ticket belongs to the caller.

### Alternatives Considered
- **`[AllowAnonymous]` with no policy.** This needs an exception in the boundary rule.
- **Multipart API intake now.** The SDK (PHASE-11) has not asked for attachments yet.

### Consequences
- **Test scope.** `AgentAccessCoverageTests` covers only routes with the Agent or Admin policy. Public and ApiKey routes get their own coverage tests.
- **SDK attachments.** PHASE-11 adds API attachments if the SDK needs them.
- **Rate-limit partitions.** The key policy runs before authentication, so a key id is not available; it partitions on the raw key prefix. Trusted keys are partitioned per prefix and client IP, so spoofing a known prefix (prefixes appear in the Admin UI and audit logs) costs only the spoofer's own IP partition. Invented prefixes still each get a partition per IP.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-05 plan review)
- **Approved on:** 2026-10-03

---

## D-035: PHASE-06 split into 06a/06b; Markdown agent replies; agent reply attachments; Solved notice

- **Status:** Approved (owner 2026-10-03)
- **Date:** 2026-10-03
- **Owner:** Jon Seeley
- **Related artifacts:** D-008, D-014, D-016, D-022, D-024, D-033, PHASE-06, PHASE-07, PHASE-09, `docs/superpowers/plans/2026-10-03-phase-06a-agent-ticket-operations.md`

### Context
PHASE-06 as written covers about 21 handlers across agent operations, customer routes, background work and admin tools. That is too large for one reviewable PR. It also left three questions open as assumptions: Markdown in replies, attachments on agent replies, and the Solved email.

### Decision
- **Two PRs.** PHASE-06 lands as 06a and 06b.
  - 06a is agent operations.
  - 06b covers customer routes and replies, follow-ups, the lost link, new-ticket and customer-reply alerts, auto-close, and the customer path of the attachment download. Delete and erase and dead letters move to 06c.
  - Superseded in part by D-037 (06c).
- **Markdown.** Agent reply bodies are Markdown, rendered and then sanitised. Internal notes use the same composer, so they are Markdown too.
- **Attachments on replies.** Agents may attach files to public replies. The limits match customer uploads: 5 files, 10 MiB each, 25 MiB per message, checked by file content.
- **Owner decision (2026-10-03):** tickets flagged as spam never email the customer (replies and Solved notices are saved but not emailed); agent alerts are unaffected.
- **Solved notice.** When an agent sets Solved, the customer gets a short notice that includes the ticket link.

### Alternatives Considered
- **One PHASE-06 PR.** Rejected: too large to review well.
- **Plain text replies.** Rejected: agents need lists, links and code in answers.
- **No agent attachments.** Rejected by the owner.
- **No Solved email.** Rejected: customers would not learn that the ticket was resolved.

### Consequences
- **Markdown renderer.** 06a introduces `IMarkdownRenderer`, which PHASE-08 reuses.
- **Sanitizer allowlist.** It grows by `h5 h6 hr del s`. PHASE-08 adds tables and images.
- **Notification planner.** `ITicketNotificationPlanner` lands in 06a with three cases: reply, solved and assigned. 06b adds the new-ticket, customer-reply and lost-link cases.

### Approval
- **Approved by:** Jon Seeley (owner, 2026-10-03 PHASE-06 planning)
- **Approved on:** 2026-10-03

---

## D-036: Ticket operations API shape: RowVersion on state changes, TicketStateDto returns, idempotent tags, string enums, multipart replies, lookup by id or number

- **Status:** Approved (owner, PHASE-06a plan review)
- **Date:** 2026-10-03
- **Owner:** Jon Seeley
- **Related artifacts:** D-016, D-024, D-033, D-034, 02-ARCHITECTURE section 7.3, PHASE-06, PHASE-07, `docs/superpowers/plans/2026-10-03-phase-06a-agent-ticket-operations.md`

### Context
02-ARCHITECTURE 7.3 mixed 200 and 204 results and left the concurrency token and reply transport unspecified. The Admin app (PHASE-07) and the SDK (PHASE-11) need one predictable shape for ticket writes.

### Decision
- **Concurrency.** Every state-changing request (status, assignee, priority, product, tags, spam) carries `RowVersion` (`uint`, the ticket's `Version`).
  - The handler compares it before mutating. A stale version gets `409 concurrency-conflict`. A missing version gets `400 row-version-required`.
  - Replies and notes accept an optional `RowVersion`. They are appends, so a parallel edit should not block a reply.
  - Every mutation returns `200 TicketStateDto`, which carries the new `RowVersion`. A reply or note returns `201 AgentMessageResponse(MessageDto Message, TicketStateDto Ticket)`.
  - This replaces the mixed 200/204 codes in 02-ARCHITECTURE 7.3. Clients always get the token they need for the next write.
- **Tags are idempotent.** This follows the Domain and 02-ARCHITECTURE: adding a present tag or removing an absent one returns 200 with no event. An unknown tag id is a 404 `tag-not-found`. The PHASE-06 "409 duplicate / 404 absent" wording is superseded.
- **Contracts carry no enums.** Status, priority, view, event type, author type and visibility travel as strings. Constants classes hold the stable names, and handlers parse them; an unknown value is a 400 with a target.
- **Reply transport.** A reply is `multipart/form-data`, with text fields plus files. A note is JSON. The PHASE-07 "multipart-free reply submit" note is superseded. `AgentAccessCoverageTests` sends an empty multipart body to multipart routes.
- **Ticket lookup.** `GET /api/tickets/{reference}` accepts either a ticket id (Guid) or a ticket number such as `ORB-42`, which serves the Admin `/tickets/{number}` route. Mutation routes take `{id:guid}`.
- **Queue counts.** `GET /api/tickets/counts` returns per-view counts for the signed-in agent. The date-range filter named in PHASE-06 is dropped from v1, because the repository has none and the UX brief does not use it.
- **Reply email.** The outbox payload holds the message id, not the body (D-033 and the 16 000-character cap). The Worker loads the message body at send time. If the message no longer exists, the row fails with `message-missing`.
- **Assignment alerts.** These go to the assignee's own email and never to the acting agent. They link into the Admin app only when the optional `TECHSTRAP_ADMIN_PUBLIC_URL` is set.
- **Attachment download in 06a is agent-only.** It uses the `Agent` policy. 06b changes the policy model for the customer token path (see the D-036 note).
- **Spam on Closed tickets.** The Domain rejects every mutation on a Closed ticket, including spam, with `409 ticket-closed`. This is kept and documented as a known limit.

### Alternatives Considered
- **Concurrency token in an `If-Match` header.** Rejected: the typed clients and OpenAPI describe a body field more simply.
- **Mixed 200/204 results.** Rejected: clients would need an extra read to get the next `RowVersion`.
- **Enums in Contracts.** Rejected: the Contracts rules allow none.

### Consequences
- **Typed client.** PHASE-07's typed client sends `RowVersion` in the body and replaces its cached state with the `TicketStateDto` returned by every write.
- **Replies.** Replies are posted as multipart.
- **Attachment download in 06b.** 06b must change `GET /api/attachments/{id}` from the `Agent` policy to a policy model that admits both credentials. Two options are a separate customer route, `GET /api/customer/attachments/{id}`, or a combined scheme policy. Decide that in 06b; prefer the separate route, because it keeps "Public stands alone" (D-034).
- **Worker.** The Worker drain handler gains `ITicketRepository` to load reply bodies at send time.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-06a plan review)
- **Approved on:** 2026-10-03

---

## D-037: PHASE-06 lands as 06a/06b/06c; lost link keeps old links; 2-minute follow-up dedupe; auto-close sends no email

- **Status:** Approved (owner 2026-10-03)
- **Date:** 2026-10-03
- **Owner:** Jon Seeley
- **Related artifacts:** D-008, D-024, D-033, D-035, PHASE-06, PHASE-09, `docs/superpowers/plans/2026-10-03-phase-06b-customer-access-alerts-autoclose.md`

### Context
D-035 split PHASE-06 into 06a and 06b. 06b grew to include customer routes, alerts and auto-close, so the admin tools (delete, erase, dead letters) need their own PR. PHASE-06 also left three customer-side questions open as assumptions: whether a lost-link request revokes old links, how to avoid duplicate follow-ups, and whether auto-close emails the customer.

### Decision
- **PHASE-06 lands in three PRs.**
  - 06a is already merged.
  - 06b (this plan) covers the customer side, alerts and auto-close.
  - 06c covers hard delete, erasing a requester, dead letters and Serilog PII redaction.
- **Lost link.** Requesting a new link revokes nothing. Old links stay valid until they expire: 90 days sliding, with a 1-year cap.
- **Follow-up dedupe.** Within 2 minutes, the same link plus the same message text returns the follow-up that was already created, instead of creating a second one.
- **Auto-close sends no email.** The Solved notice has already told the customer about the window. This supersedes the "closed notice" in PHASE-06, 02-ARCHITECTURE and FR-TKT-14.

### Alternatives Considered
- **Keep two PRs.** Rejected: 06b would be too large to review well.
- **Lost link revokes old links.** Rejected by the owner: a stray request would lock a customer out of links they already hold.
- **No dedupe on follow-ups.** Rejected: a double submit would create two tickets.
- **A closed notice on auto-close.** Rejected: the Solved notice has already told the customer about the window.

### Consequences
- **06c scope.** Hard delete, requester erasure, dead letters and Serilog PII redaction move to 06c.
- **Superseded wording.** The "closed notice" in PHASE-06, 02-ARCHITECTURE, FR-TKT-14 and UX-BRIEF-portal.md is removed.
- **Old links.** They stay valid until they expire (90 days sliding, 1-year cap), so a lost-link email adds links without revoking any.

### Approval
- **Approved by:** Jon Seeley (owner, 2026-10-03 PHASE-06b planning)
- **Approved on:** 2026-10-03

---

## D-038: Customer API: Public routes with in-handler token auth, uniform 404, separate customer attachment route, lost-link rules, alert recipients, reopen window from AutoCloseOptions, per-ticket auto-close

- **Status:** Approved (owner, PHASE-06b plan review)
- **Date:** 2026-10-03
- **Owner:** Jon Seeley
- **Related artifacts:** D-008, D-024, D-033, D-034, D-036, D-037, PHASE-06, PHASE-09, PHASE-12, `docs/superpowers/plans/2026-10-03-phase-06b-customer-access-alerts-autoclose.md`

### Context
The customer side authenticates with a ticket token rather than an agent credential, and D-034 requires the `Public` policy to stand alone. D-036 left the customer attachment route, and several alert and lost-link details, to 06b.

### Decision
- **Routes.** Each controller carries the `Public` policy and its own rate limit:
  - `GET /api/customer/ticket` (token-access limit).
  - `POST /api/customer/ticket/replies`, multipart (token-access limit).
  - `GET /api/customer/attachments/{id}` (token-access limit). This is a separate route from the agent's `/api/attachments/{id}`, as D-036 prefers.
  - `POST /api/customer/access-link`, JSON (lost-link limit).

  The token travels only in `X-Ticket-Token`.
- **Uniform 404.** Every failure gives the same 404 body with code `not-found`. That covers:
  - a missing, malformed, unknown, revoked or expired token;
  - an erased requester or a missing ticket;
  - an attachment on another ticket or on an internal note;
  - a stored file that is missing.

  Tests compare the response bodies byte for byte.
- **The link slides on use.** A successful ticket read or reply records the token's use, which slides its expiry within the 1-year cap. An attachment download does not, because it is a read with no write.
- **Customer-reply alerts.** These go to the assignee if the ticket is assigned and the assignee is active. Otherwise they go to the agents opted in for the product, reusing the existing per-product "new ticket" preference, since there is no separate preference.
  - Spam tickets send no customer-reply alerts. Their replies sit in the Spam view.
  - New-ticket and assignment alerts are unaffected by this rule.
- **Follow-up mechanics.** A reply on a Closed ticket creates a follow-up through `Ticket.CreateFollowUp`, with a new number from the ticket's current product.
  - The reply becomes the follow-up's first message, and the files are stored against the follow-up.
  - The customer gets a confirmation email, and opted-in agents get a new-ticket alert.
  - The response carries a view URL backed by a freshly issued token.
  - A dedupe replay issues a fresh token for the existing follow-up and sends no email or alert.
  - If a concurrent duplicate loses on commit, it re-checks the dedupe window and replays.
- **Lost link.**
  - The request takes a JSON `{ email }`.
  - A malformed address is `400 email-invalid`. Any well-formed address gets the same empty `202`.
  - A known, non-erased requester gets one email to their own address. It holds links to their 5 most recently active non-spam tickets, each with a freshly issued token.
  - A per-address cap of 3 emails per hour is enforced inside the handler by counting recent `access-links` outbox rows. Over the cap, the response is still 202 and nothing is sent.
  - There is also a per-IP limit at the host.
- **Accepted residual risks (lost link).**
  - **Timing.** Known addresses do more work than unknown ones (tens of milliseconds). The per-address cap and the per-IP limit bound it. A possible hardening is to move the send off the request path.
  - **Concurrent requests.** Parallel requests can overshoot the per-address cap inside the count-to-commit window. The per-IP limit bounds it. A possible hardening is an advisory lock per address.
  - **Dedupe replay tokens.** Each dedupe replay issues a fresh access token. The per-IP token-access limit bounds it.
  - **Lost-link branding.** The email's branding comes from the most recent ticket's product even when the list spans products (same requester only).
  - **Mixed-case stored addresses.** The cap count is an exact match on `to_address`. This is safe because the Domain always lowercases addresses; data that bypasses the Domain would escape the cap.
- **Reopen window.** The window shown in emails comes from `AutoCloseOptions.Days`, bound from `TECHSTRAP_AUTOCLOSE_DAYS`.
  - `TicketSolvedEmail` already carries it. `AgentReplyEmail` gains `ReopenDays`.
  - Rows queued before the upgrade have no value (`ReopenDays = 0`). The renderer falls back to `TicketNotices.DefaultReopenDays`.
- **Auto-close runs one unit of work per ticket.**
  - That way a concurrent customer reply makes only that ticket conflict, and it is retried on the next run.
  - The query excludes spam, so Solved spam tickets cannot starve the batch.
  - Tickets are closed with `Actor.System`, and no email is sent (D-037).

### Alternatives Considered
- **A combined scheme policy on `/api/attachments/{id}`.** Rejected: a separate customer route keeps "Public stands alone" (D-034), as D-036 preferred.
- **Distinct 404 codes per failure.** Rejected: they would let a caller probe which tokens or tickets exist.
- **A separate customer-reply alert preference.** Rejected: the existing per-product "new ticket" preference is reused.
- **One unit of work for the whole auto-close batch.** Rejected: one concurrent customer reply would conflict the whole batch.

### Consequences
- **Customer DTOs.** They stay inside the D-024 limits.
- **Worker.** Auto-close runs in the Worker, one unit of work per ticket.
- **Portal.** PHASE-09 forwards attachment downloads to the customer route with `X-Ticket-Token`.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-06b plan review)
- **Approved on:** 2026-10-03

---

## D-039: PHASE-06c: erase scope, delete and follow-ups, outbox retention, log redaction, multipart hardening

- **Status:** Approved (owner 2026-10-03; technical decisions at PHASE-06c plan review)
- **Date:** 2026-10-03
- **Owner:** Jon Seeley
- **Related artifacts:** D-006, D-022, D-033, D-036, D-037, D-038, PHASE-06, PHASE-07, PHASE-09, PHASE-12, `docs/superpowers/plans/2026-10-03-phase-06c-delete-erase-dead-letters.md`

### Context
D-006 said what erase and delete are for, but left the detail open. Reading the PHASE-03 to 06b code shows personal data in places D-006 does not list: the ticket subject and its customer metadata, the email outbox (rows addressed to the requester, and agent alerts that carry the requester's name or email), and the plaintext portal link inside every sent row's payload (D-033). `tickets.parent_ticket_id` is `RESTRICT`, so a ticket with follow-ups could not be deleted at all. D-006 deferred retention and D-033 deferred the outbox sweep to PHASE-12, but the lost-link limit (D-038) needs an index on the outbox and sent rows keep a working link for ever.

### Decision
**Owner decisions (2026-10-03)**
- **Erase replaces the subject too.** The subject of every ticket the requester opened becomes the erasure marker `[erased]`, and `tickets.metadata` and `tickets.custom_fields` are cleared (customer-supplied, so they may hold personal data).
- **Only the requester's own messages are replaced.** Messages with author type `Requester` get the marker. Agent replies and internal notes stay.
- **Erase deletes outbox rows.** Every `email_outbox` row addressed to the requester (any status, including Sent), and every row whose `ticket_id` is one of the requester's tickets (agent alerts carry the requester's name or email).
- **Outbox retention.** Add an index on (`kind`, `to_address`, `created_at`) and a Worker sweep that deletes `Sent` and `Discarded` rows older than N days (default 90). `DeadLettered`, `Pending` and `Sending` rows are never swept. This supersedes D-033's "PHASE-12 retention sweep with a payload scrub" (the rows are deleted, so there is nothing to scrub) and narrows D-006's "retention deferred" for the outbox only.
- **Deleting a ticket with follow-ups.** The follow-ups survive and are unlinked (`parent_ticket_id` becomes NULL). Their own `Created` events keep `parentTicketId`, because events are append-only history.

**Technical decisions (proposed in the plan)**
- **One migration**, generated by `dotnet ef`: `tickets.parent_ticket_id` foreign key becomes `ON DELETE SET NULL`, plus the index `ix_email_outbox_kind_to_address_created_at`. Nothing else.
- **Delete ticket.** `DELETE /api/tickets/{id}`, Admin, 204 or 404 `ticket-not-found`, no row version. One transaction removes the ticket (the database cascades messages, attachments, events, tokens, tags, linked articles and idempotency keys), deletes the outbox rows for the ticket, and writes `TicketDeleted` with counts only. Attachment files are deleted after the commit, best effort (an orphan file is better than a row that points at a missing file).
- **Erase requester.** `POST /api/requesters/{id}/erase`, Admin, 204 or 404 `requester-not-found`, idempotent (each call writes an `AdminEvent`). Bulk updates run through a new Application port `IRequesterErasure` inside the handler's transaction, and `Message.Body` stays immutable in the Domain. Files are deleted after the commit. Ticket numbers, ticket events and the requester row (a tombstone) survive.
- **Dead letters.** `GET /api/dead-letters`, `POST /api/dead-letters/{id}/retry` and `DELETE /api/dead-letters/{id}` (discard), all Admin. A retry or discard of a row that is not dead-lettered is `409 outbox-not-dead-lettered`. `DeadLetterDto` shows a masked recipient and never the payload (it holds a portal link).
- **Retention clock.** Retention counts from `created_at` for both Sent and Discarded rows (one index, one rule).
- **Serilog redaction.** A `PiiRedactionEnricher` in Infrastructure, applied by the Api and the Worker, rewrites every log property value (including nested ones): email addresses become `[email]`, 43-character base64url tokens `[token]`, and `sha256:` plus 64 hex characters `[hash]`. Exceptions rely on Npgsql's default (no `Detail` unless "Include Error Detail" is set) and on EF sensitive logging staying off; an architecture test guards both. Admin and Portal handle no requester data yet, so wiring them is a PHASE-07/PHASE-09 follow-up.
- **Multipart hardening.** A truncated body that is not a 413 answers `400 request-malformed`, not 500. A non-multipart body on the three multipart routes answers 415 from the shared filter, after the route's own policy ran (so the customer route answers 415, not the fallback 401).
- **Follow-up dedupe** compares sanitised file names, through a Domain `AttachmentFileName.Sanitize` shared with the attachment store.
- **Portal Sentry scrub** is deferred to PHASE-09: the Portal makes no API calls yet and there is no shared project to hold the processor.

### Alternatives Considered
- **Keep the subject and metadata on erase.** Rejected by the owner: both are customer-written.
- **Replace agent replies on erase.** Rejected by the owner: agent text is the company's record, not the requester's data.
- **Refuse to delete a ticket with follow-ups, or delete them too.** Rejected by the owner: a follow-up is a separate conversation.
- **Outbox sweep with a payload scrub (D-033).** Rejected: deleting the finished rows is simpler and removes the link entirely.
- **A column `dead_lettered_at`.** Rejected: the list orders by `created_at` and the DTO does not need it.

### Consequences
- **Superseded wording.** PHASE-06 ("Automatic retention is out of scope"), 02-ARCHITECTURE 6.8, FR-PRIV-01 and the PHASE-12 carry-forward for the sweep change.
- **Row versions.** Erasing a requester changes the row version of that requester's tickets, so an agent holding an old copy gets a 409 and reloads.
- **Backups** still hold erased and deleted data until they expire (D-006, PHASE-12 runbook).
- **Outbox history** older than the retention window is gone: the dead-letter list and the per-address lost-link count see only recent rows.
- Amended in part by D-040: PiiRedactionEnricher and the Sentry header scrubber moved to TechStrap.Hosting, and the Admin host is wired in PHASE-07a.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-06c planning)
- **Approved on:** 2026-10-03

---

## D-040: PHASE-07 lands as 07a/07b/07c; Authentik later; TechStrap.Hosting; Admin sign-in, clients and ticket handling

- **Status:** Approved (owner 2026-10-04; technical decisions at PHASE-07a plan review)
- **Date:** 2026-10-04
- **Owner:** Jon Seeley
- **Related artifacts:** D-004, D-016, D-017, D-022, D-024, D-029, D-036, D-039, PHASE-07, PHASE-08, PHASE-09, PHASE-12, UX-BRIEF-admin, `docs/superpowers/plans/2026-10-04-phase-07a-admin-shell-and-tickets.md`

### Context
PHASE-07 has 23 tasks, too many for one review. The owner's identity provider (Authentik) is not set up yet, so nothing can be signed in against it. The Admin needs the log redactor and the Sentry header scrubber of PHASE-06c, but they live in Infrastructure and the Api, which the Admin may not reference (D-005, 02-ARCHITECTURE section 2). Reading the Admin placeholder and the packages shows gaps in the PHASE-07 spec. `SyntaxCircus.Http.Resilience` `ApiClientBase` drops the `errorCodes` the API sends for validation failures and has no `Result` mapping. `AddResilientHttpClient` retries every HTTP method, not only GETs. A circuit gets its token only from a named client created by `IBlazorCircuitHttpClientFactory`. Every ticket call is a 403 until `GET /api/agents/me` has created the agent row. `TicketStateDto` carries ids, not names. The API has no per-requester counts and no list of a ticket's follow-ups.

### Decision
**Owner decisions (2026-10-04)**
- **PHASE-07 lands in three PRs.** 07a (T01 to T13 and T22): sign-in, shell, typed clients, queue, and ticket detail with every agent action. 07b (T14 to T18 and T23): the settings and admin pages. 07c (T19 and T20): the brand, responsive and accessibility pass, the compose and Dockerfile checks, the Admin architecture rules, the CSP, shared host wiring and the OpenAPI bearer scheme.
- **Authentik is not set up yet.** OIDC is wired from configuration for real. Tests use a fake authentication scheme and a stub API, and the docs get an Authentik setup note.
- **A new `TechStrap.Hosting` project** holds `PiiRedactionEnricher` and `SensitiveHeaderSentryProcessor` (moved, behaviour unchanged). Api, Worker, Admin and Portal may reference it. Infrastructure drops its Serilog package.
- **No Playwright in PHASE-07.** The optional browser smoke tests (P07-T21) move to PHASE-12.

**Technical decisions (proposed in the plan)**
- **OIDC configuration** keeps the existing `Auth__*` keys (`AddBlazorTokenForwarding(configuration, "Auth")`). The scopes default to `openid profile email offline_access`. The group keys are the API's flat names `TECHSTRAP_AGENT_GROUP`, `TECHSTRAP_ADMIN_GROUP` and `TECHSTRAP_GROUP_CLAIM_TYPE`, with the API's defaults. Options are validated on start and read lazily, so a missing key stops the start with a message naming the variable, and test factories supply values through in-memory configuration.
- **UI gating uses the API as the source of truth.** An `AgentSession` calls `GET /api/agents/me` once per scope. That call also creates the agent row, so no ticket call comes before it. A 403 (`agent-access-required`, `agent-inactive`, `agent-email-required`, `agent-identity-invalid`) shows the no-access page, a 401 asks for a new sign-in. The Admin does not copy the group-claim parsing of the API. Delete and erase show only for the role the API reports; the API still enforces them.
- **Sign-in plumbing.** The cookie scheme is literally `Cookies` (SyntaxCircus.Blazor.Auth reads tokens from it) plus OpenIdConnect with the code flow, PKCE, `SaveTokens`, `offline_access`, `MapInboundClaims=false` and the userinfo claims. `GET /signin` (the anonymous landing page), `GET /signin/start` (the challenge, local return URLs only) and `POST /signout` (antiforgery) are minimal-API endpoints, so an anonymous visitor never opens a circuit. The fallback authorization policy requires a signed-in user. The error and not-found pages, `/signin*`, the health checks and the static assets stay anonymous, and `/_styleguide` stays anonymous in Development only. Tests use a `Test` authentication scheme and never touch OIDC.
- **Render mode.** `Routes` and `HeadOutlet` render `InteractiveServer`. Prerendering stays on, so the first render also asks the API; reads are doubled until a later phase persists the state across the prerender.
- **HTTP clients.** There are two named clients with the auth handler and the forwarded client IP. `techstrap-api-read` retries a failed read twice (transport errors, timeouts, 408, 502, 503 and 504; no circuit breaker, because every circuit shares the client); `techstrap-api-write` has no retry and no circuit breaker, so a transient failure can never duplicate a reply or a note. The typed clients sit on a small Admin `ApiConnection`, not on `ApiClientBase`. It reads problem details, including `errorCodes`, into `SyntaxCircus.Common` `Result`: 400 is Validation, 401 Unauthenticated, 403 Forbidden, 404 NotFound, 409 Conflict, anything else Failure. Cancellation by the caller propagates and is never mapped.
- **Attachments (D-017).** `GET /attachments/{id}` streams `GET api/attachments/{id}` through the read client without buffering, forces `Content-Disposition: attachment`, `nosniff` and `Cache-Control: private, no-store`, and answers 404 for any upstream 404. There is no `IAttachmentsClient`: a buffering typed client would defeat the streaming.
- **Sidebar.** It is optimistic-free (the PHASE-07 table wins over the UX brief): the control shows a pending state, then re-renders from the returned `TicketStateDto`. A failure shows an inline error and the previous value. Every write sends the current `RowVersion`. A 409 `concurrency-conflict` raises a non-blocking banner with Reload and keeps drafts.
- **Queue.** The default view is Unassigned (UX), not `TicketViews.Default` (All). The page size is 25, the search debounce 300 ms, and all filter state lives in the query string.
- **Keyboard.** One ES module drives the shortcuts. A status bar with a `role="status"` slot, the shortcut help dialog and the Not spam key `u` are in 07a. The command palette (Ctrl+K) moves to 07c.
- **Dialogs.** Native `<dialog>`; Enter never confirms. Spam asks for confirmation naming the ticket, delete needs the ticket number typed, erase the requester's email. Not spam has no dialog.
- **Reply composer.** Separate drafts for a public reply and an internal note, kept per ticket for the life of the circuit. Selected files are kept until a send succeeds. `LinkedArticleIds` stays empty until PHASE-08.
- **Spec deviations.** `Components/{Ui,Pages,Layout}` stay; the new folders are `Auth/`, `Clients/`, `Options/`, `Features/Queue/` and `Features/Tickets/` (the spec said `Shared/` and `Features/TicketDetail`). `StatusStamp` and `PriorityMark` are the spec's `StatusBadge` and `PriorityBadge`, with a mapper for the API's status strings. The spec's `TicketView.Spam` is `TicketViews.Spam`. The queue mapping stays in the page code-behind; the PHASE-07 table wins over the `TicketQueueViewModelFactory` of 02-ARCHITECTURE section 8.1. `IDeadLettersClient` and `IAdminEventsClient` arrive in 07b with their pages.
- **Forced by Blazor or the API, found while prototyping.**
  - Component view models are `public`. Razor components are always public, so an internal parameter type does not compile; helpers stay `internal`.
  - The composer enforces file size, type and count itself, because `InputFile` has no size limit parameter.
  - Not spam from the Spam view first reads the ticket, because the queue row has no RowVersion.
  - A Closed ticket shows a read-only facts panel instead of the sidebar.
  - Times show in UTC; local-zone display moves to 07c.
  - The conflict banner offers Reload, but not the UX's "Apply my change again".
  - `MessageThread` is merged into `TicketTimeline` and `MessageBubble`.
  - Queue tabs are real links, so each view can be bookmarked.
- **Known API gaps, recorded.** The erase dialog shows no ticket or attachment counts, because no endpoint returns them. The follow-up children of a ticket are not listed, because the API has no field for them; a follow-up shows "Follow-up to {number}" by fetching its parent.
- **Hosting.** The shared project is a leaf: it references no TechStrap project, only `SyntaxCircus.AspNetCore.Serilog` and `SyntaxCircus.Observability`. The architecture tests allow Api, Worker, Admin and Portal to reference it and nothing else.

### Alternatives Considered
- **One PR for PHASE-07.** Rejected by the owner: 23 tasks cannot be reviewed well together.
- **Wait for Authentik.** Rejected by the owner: the app can be built and tested against fakes now.
- **Admin references Infrastructure for the redactor.** Rejected: it breaks D-005 and the architecture test.
- **Copy the redactor into Admin.** Rejected: two copies of a privacy control drift apart.
- **`ApiClientBase` with `AddResilientHttpClient`.** Rejected: it drops the validation codes, and it retries POST, PUT and DELETE.
- **Copy the API's group parsing into the Admin.** Rejected: a second implementation can disagree with the API about who has access. The API's answer is the only one that matters.
- **An interactive `/signin` page.** Rejected: an anonymous visitor would open a circuit that the fallback policy refuses.

### Consequences
- **Superseded wording.** PHASE-07 ("references `TechStrap.Contracts` only", "built on `ApiClientBase`", `Shared/`, `IAttachmentsClient`, optional Playwright), 02-ARCHITECTURE section 2 (reference table) and the UX-BRIEF-admin "optimistic" sidebar for 07a.
- **Local compose** needs real or dummy OIDC values for the Admin to start. A dummy authority starts the app; signing in then fails until Authentik exists.
- **Prerendering doubles the first reads** of a page (the prerender scope and the circuit scope each ask the API).
- **Row versions.** The sidebar and the composer send the version they loaded; an agent holding an old copy gets a 409 and reloads.
- Corrected by D-041: the read client retries twice (ApiClientRegistration.ReadRetryCount), not three times, and has no circuit breaker.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-07 planning)
- **Approved on:** 2026-10-04

---

## D-041: PHASE-07b: read-only roles, a logo URL field, tag ticket counts, the Admin guard and browser preferences

- **Status:** Approved (owner 2026-10-04; technical decisions at PHASE-07b plan review)
- **Date:** 2026-10-04
- **Owner:** Jon Seeley
- **Related artifacts:** D-022, D-024, D-029, D-030, D-036, D-039, D-040, PHASE-07, UX-BRIEF-admin, `docs/superpowers/plans/2026-10-04-phase-07b-admin-settings.md`

### Context
PHASE-07a merged the sign-in, the shell, the typed clients and the ticket pages. PHASE-07b builds the settings and admin pages (T14 to T18 and T23). Reading the 07a code and the API shows places where the PHASE-07 table and UX-BRIEF-admin no longer match.
- **Roles.** The UX brief and the PHASE-07 table have an agents screen that changes roles and stops the last admin from being demoted. Since D-029 the role comes from the IdP groups and `UpdateAgentRequest` carries `IsActive` only.
- **Logo.** The UX brief has a logo upload. The API has no upload endpoint and no file storage for branding. `ProductBranding.LogoPath` is a plain string of up to 500 characters with no URL check. The email renderer uses it as an image source, and `GET /api/public/products/{key}` hands it to the portal, which will do the same in PHASE-09.
- **Tag counts.** The tag list should show how many tickets use each tag. `GET /api/tags` has no count. The count of a `409 tag-in-use` exists only in the message text, because the `ResultError` of `SyntaxCircus.Common` has no field for it.
- **Test gaps.** No endpoint test covers the admin fields of `GET /api/agents`. `AdminEvent` type and subject names, and the `#RRGGBB` colour pattern, exist only in Domain, which the Admin cannot reference.
- **Guard and session.** `AgentSession.ReloadAsync` drops to NotLoaded until the answer arrives, so a page that reloads the session after a save would be replaced by the "Checking" gate. The Admin has no admin-only guard: D-040 decided that the API's answer, not the group claim, says who is an admin.
- **Stale text.** The PHASE-07 lines on the typed clients still say `ApiClientBase` and three retries, and D-040 says three retries. The read client retries twice (`ApiClientRegistration.ReadRetryCount`), only on transport errors, timeouts, 408 and 502/503/504, and has no circuit breaker. The PHASE-07 text says validation errors arrive as 422, but the API answers 400.

### Decision
**Owner decisions (2026-10-04)**
- **Roles are read-only in the Admin.** The agents screen shows each role as a badge with the note "Roles come from your identity provider's groups." (D-029). An admin can only activate or deactivate an agent.
- **The product logo is a URL field**, with validation and a preview. There is no upload.
- **The Admin tag list shows a ticket count per tag**, and the delete confirmation shows the same count.
- **PHASE-07b lands in one PR.**

**Technical decisions (proposed in the plan)**
- **Tag summary.** A new Admin-only `GET /api/tags/summary` returns `TagSummaryDto(Id, Slug, Name, Colour, TicketCount)`, ordered by name. It uses one grouped count in `ITagRepository.ListWithTicketCountsAsync` (tickets of any status, spam included, the same set a forced delete detaches). No migration is needed. `TagDto` and `GET /api/tags` do not change, so the tag picker and the queue filter pay for no count. The Admin tag list and the delete confirmation use the summary. A `409 tag-in-use` still carries its message.
- **Logo URL validation on the server.** The Domain branding guard accepts a blank value (no logo), an absolute `https` URL with a host and no user info, or an `http` URL whose host is `localhost` or `127.0.0.1`. Everything else, including `javascript:`, `data:`, `file:`, relative and protocol-relative values, is a validation error on the field `logo-path` with the code `logo-path-invalid` (400). The Domain cannot know the environment, so the loopback `http` form is allowed in every environment. This is harmless: the email renderer already renders only `https` logos, and a loopback address is not reachable by a customer. A logo stored before this change still loads (`Restore` does not validate) but must be corrected before the product can be saved again. The Admin editor mirrors the rule with Contracts `BrandingRules.IsAcceptableLogoUrl`, so the agent sees the error before submitting, and a parity test keeps the two equal.
- **Contracts constants.** `AdminEventTypes` (12 values) and `AdminSubjectTypes` (7 values) carry the wire names of the Domain enums. `BrandingRules.ColourPattern` (`^#[0-9A-Fa-f]{6}$`), `BrandingRules.LogoUrlMaxLength` and `BrandingRules.IsAcceptableLogoUrl` carry the branding rules. Domain cannot reference Contracts, so parity tests in Application.Tests compare each constant with the Domain enum or guard, in the style of `TicketNamesParityTests`.
- **Agent list test.** `AgentManagementEndpointTests` gets the admin case: `Email`, `Role`, `IsActive` and `LastSeenAt` are filled for every agent.
- **Typed clients.** The 07a `ApiConnection` pattern, no new package. `IProductsClient` gains `GetAsync`, `CreateAsync`, `UpdateAsync`, `ListApiKeysAsync`, `CreateApiKeyAsync` and `RevokeApiKeyAsync`. `IAgentsClient` gains `ListPageAsync`, `SetActiveAsync`, `UpdateMyProfileAsync`, `GetNotificationPreferencesAsync` and `UpdateNotificationPreferencesAsync`. `ITagsClient` gains `ListSummaryAsync`, `CreateAsync`, `UpdateAsync` and `DeleteAsync(id, force)`. `IAdminEventsClient` and `IDeadLettersClient` (list, retry, discard and `CountAsync`, a page of one) are new. Every write uses the write client: no retry, and a page never cancels it. `ApiErrorCodes` gains the codes the pages branch on, and `ApiFields` names the kebab-case targets of a 400.
- **Admin guard and session.** An `AdminOnly` component wraps the content of each admin page. It shows "Checking" while the session has not loaded, the page-level no-access page for an agent who is not an admin (the content is never built, so nothing inside it runs), and the content for an admin. `AgentSession.ReloadAsync` keeps a Ready session, and its current agent, until the answer arrives, then raises `Changed` once, so a My settings save causes no gate flicker. An answer of 403 or 401 still wins, and only a transient failure keeps the old session. The rail shows the admin links (Products, Agents, Tags, Audit, Failed emails with a count badge) only for an admin, and My settings for everyone. The count comes from `IDeadLettersClient.CountAsync`, called only for an admin and only after the first render.
- **Browser preferences.** `wwwroot/js/preferences.js` (an ES module) stores the single-key shortcut switch and the theme (`auto`, `light`, `dark`) in `localStorage`. Its functions never throw. `auto` removes the `data-bs-theme` attribute, so the system preference decides. A scoped `PreferencesService` loads them on the first interactive render and sets `ShortcutService.SingleKeyEnabled`. The theme is therefore applied after the first interactive render, so a dark-mode agent can see one light frame; an inline script would fix that but conflicts with the 07c CSP, so the decision moves to 07c.
- **Pages.** Products (editor with `Version` conflict banner, `IsActive` always sent, preview with `ProductAccent`), API keys (show-once dialog that cannot close until "I have stored this key" is ticked), agents, tags, audit (paged, `asOf` carried through) and failed emails. Revoking a key, deactivating an agent and discarding a failed email use a medium confirm. Deleting a tag in use needs the tag name typed and sends `force=true`.
- **Docs corrected.** The PHASE-07 client and resilience lines, the T14, T16 and T23 wording, UX-BRIEF-admin on roles, the logo, the last-admin rule, the discard reason and the audit filters, and the D-040 retry count.

### Alternatives Considered
- **Role editing in the Admin.** Rejected by the owner: D-029 makes the IdP group the only source of a role.
- **A logo upload.** Rejected by the owner: it needs file storage, content checks and a serving route for a rarely changed value.
- **A count field on `TagDto`.** Rejected: every tag picker and queue filter call would run a count query, and every `new TagDto(...)` call in the tests would change.
- **A structured count on the `409 tag-in-use` problem.** Rejected: it needs an extension field in `SyntaxCircus.Common` `ResultError`, a package change; the summary gives the same number before the delete.
- **Counting with `ListTicketIdsWithTagAsync`.** Rejected: it loads every ticket id of a tag to count them.
- **Validating the logo only in the Admin editor.** Rejected: the API is reachable without the Admin, and the logo becomes an image source in customer emails and, from PHASE-09, on the portal.
- **An admin policy that reads the group claim in the Admin.** Rejected in D-040 for the same reason: a second implementation can disagree with the API.
- **Dropping the session to NotLoaded during a reload.** Rejected: it unmounts the page and loses what the agent was typing.

### Consequences
- **Superseded wording.** UX-BRIEF-admin (change role, the logo upload, the UI preventing the last-admin demotion, discard reason, audit filter by date), the PHASE-07 table rows for the agents page and the typed clients, and the PHASE-07 validation text of T14, T16 and T23.
- **D-030 and D-040.** The count of a `tag-in-use` conflict is still in its message, and the Admin takes it from the summary. D-040 said the read client retries three times; it retries twice.
- **A new read endpoint.** `GET /api/tags/summary` (Admin). No migration.
- **Existing products** with a relative or `http` logo keep working (the email renderer ignores a logo that is not `https`). Saving such a product requires a valid URL or a blank one.
- **Failed-email badge.** The rail calls the dead-letters list once per admin circuit and again after a retry or a discard.
- **Preferences are per browser**, not per agent, and the assignment-alert toggle of the UX brief is out of scope because the API has no such preference.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-07b planning)
- **Approved on:** 2026-10-04

---

## D-042: PHASE-07c: the CSP, the theme script, local time, shared host wiring, the command palette and the responsive Admin

- **Status:** Approved (owner 2026-10-04; defaults at PHASE-07c plan review)
- **Date:** 2026-10-04
- **Owner:** Jon Seeley
- **Related artifacts:** D-017, D-024, D-034, D-040, D-041, PHASE-07, UX-BRIEF-admin, `docs/development/ADMIN-APP.md`, `docs/superpowers/plans/2026-10-04-phase-07c-admin-polish.md`

### Context
PHASE-07a and 07b are merged. 07c is the polish pass (T19), the compose and architecture checks (T20) and the items D-040 and D-041 moved to it: the CSP, the shared host wiring, the OpenAPI security scheme, the command palette, local time and the theme flash.
Reading the code for the plan found these facts:
- **Headers.** Only the Api sends security headers, and only the package defaults, whose CSP has no `default-src`, `script-src` or `style-src`. The Admin and the Portal send none. The Admin uses `style` attributes for the product accent and tag colours (`AccentPreview`, `TagChip`), and `<ImportMap />` renders an inline script (it is removed in 07c).
- **The sign-in redirects.** Chrome applies `form-action` to the redirect that follows a form submission, so the sign-in and sign-out forms, which answer with a 302 to the identity provider, need the provider's origin in `form-action`. Firefox does not enforce it, so a Firefox-only check would miss it.
- **Sessions.** Only the first `GET /api/agents/me` could move the app to "session expired". A 401 on any later call showed a generic error string and left the shell looking signed in.
- **Logging.** Only the Admin removed the `HttpClient` factory's logging handlers, which write each request header (`Authorization`, an OTLP `x-api-key`) at Trace. The Api, Worker and Portal use the same factory. The 07b claim that the OTLP exporter goes through it was not reproduced in 07c; what was found is that the factory's own logging default leaks header values in its structured state, and the OTLP leak tests remain as an end-to-end guard with a positive control (a TCP listener that sees the export attempt).
- **Search.** The queue keeps its search text in the address and sends it to the API as `?search=`. Sentry scrubbed request headers only.
- **Time.** Every time is formatted in UTC. The API stores `timestamptz`, the agent profile has no zone, and the browser is the only source.

### Decision
**Owner decisions (2026-10-04)**
- **CSP.** `script-src 'self'` stays strict. `style-src 'self'` plus `style-src-attr 'unsafe-inline'`: the colours are admin data, so classes cannot carry them, and a nonce does not cover attributes.
- **Theme flash.** An external blocking script, `wwwroot/js/theme-init.js`, in the page head, loaded before the stylesheets.
- **Time zone.** From the browser (`Intl`) through a JS module, per circuit. The prerender shows UTC. No API change.
- **Host wiring.** A shared helper in `TechStrap.Hosting` is adopted by the Admin and the Portal, and the Portal gains a reference to Hosting. The `ConfigureHttpClientDefaults(b => b.RemoveAllLoggers())` default goes into all four hosts.
- **One PR.**

**Defaults (proposed in the plan, approved with it)**
- **Queue search stays in the URL** and is masked in Sentry: a `SensitiveQuerySentryProcessor` in `TechStrap.Hosting` replaces the value of `search` and `q` with `[redacted]` in the URL and query string, every request header (including `Referer`), the message and its parameters, the request body and cookies (text), extras, tags, exceptions, span descriptions, data and tags, and breadcrumbs (through the `BeforeBreadcrumb` hook); names are matched after URL-decoding. It is registered with the header scrubber, so every host that has one gets it.
- **`Retry-After` is honoured but capped at 2 seconds** on the read client.
- **OpenAPI documents three schemes**: `Bearer` (HTTP bearer, JWT) for the Agent and Admin policies, `ApiKey` (header `X-Api-Key`) for intake, and `TicketToken` (header `X-Ticket-Token`) for the customer routes. A document and an operation transformer add them per operation; public operations name none. It is documentation only. A product update now looks the product up before it validates the body, so an unknown product with an invalid body answers 404 (it was 400).
- **A 401 in the middle of a session** moves `AgentSession` to "session expired" from one place (`ApiConnection`). First load: the full page. Mid-session: a banner with "Sign in again" (a link to `/signin/start` for the current page), and the page stays mounted so an unsent draft survives. **Review ruling:** once the session has lapsed, `ApiConnection` sends no further request: every call returns a local "session expired" failure and the session stays expired for the rest of the circuit, because the auth package logs `PathAndQuery` (which includes the search text) on an unauthenticated call. A sign-in in another tab does not revive the old one; the agent uses the banner.

**Technical decisions made in the plan**
- **CSP details.** The directives are `default-src 'self'; script-src 'self'; style-src 'self'; style-src-attr 'unsafe-inline'; img-src 'self' https: data:; connect-src 'self'; font-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self' <the OIDC authority origin>`. `img-src` allows `https:` because product logos are `https` addresses chosen by an admin, loopback http only in Development, and `data:` because Bootstrap's compiled CSS uses data: SVG icons. There is no `upgrade-insecure-requests`. `connect-src` has no explicit `ws:` or `wss:`; that `'self'` covers the circuit's websocket is an owner browser check. `<ImportMap />` is removed from both apps (an architecture rule flags it). Origins are written with `IdnHost` and only printable ASCII is accepted. A styleguide inline handler was removed, and the architecture rules also flag `on*=` attributes and `<ImportMap>` in every form. The owner browser checks are the websocket under `connect-src 'self'`, the OIDC `form-action` redirects and the reconnect modal. One builder in `TechStrap.Hosting` (`TechStrapCsp.ForBlazorApp`) serves the Admin and the Portal; the Api uses `TechStrapCsp.ForApi` (`default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'`), and successful (2xx) attachment downloads also get `sandbox` (in the Api and in the Admin's `/attachments` pass-through; an upstream 404 re-executes the ordinary not-found page, which is not sandboxed and keeps the full policy).
- **Shared host wiring** (`TechStrap.Hosting.Wiring`). `AddTechStrapHttpClientDefaults` (the `RemoveAllLoggers` default) applies in all four hosts; `AddTechStrapWebHost`, `UseTechStrapWebHost` and `UseTechStrapErrorPages` apply to the Admin and the Portal; the Portal gets `AddTechStrapObservability`, which calls `AddSensitiveHeaderScrubbing`. The Portal references exactly Contracts and Hosting. `FactoryClientLeakTests` covers all four hosts. The OTLP probe in the host tests answers 200 (a probe that never answered made each test wait out the exporter's timeout). HSTS is not sent in Development. The CSP override is applied with `PostConfigure`, and the attachment `sandbox` matches the exact directive and replaces a weaker `sandbox`, and is added only to a 2xx answer, as in the Api. The OTLP probe reads a request body by its length or its chunk sizes and answers `Expect: 100-continue`.
- **Session resilience.** `SessionExpiry` (scoped) receives `Report()` from `ApiConnection` on every 401 and then answers `IsLapsed`, which makes `ApiConnection` answer locally. `AgentSession` gains `ExpiredWhileWorking`, `IsAdmitted` and `MarkUnavailable()`; `IsAdmin` follows `IsAdmitted`, so it stays true while the session is expired mid-session, and a late `/me` success does not return a lapsed session to Ready. The `SessionExpiredBanner` (`role="alert"`) links to `/signin/start?returnUrl=<current>`. The read retry's cap is `ApiClientRegistration.ReadRetryAfterCap` (2 s).
- **Command palette.** Ctrl+K or Cmd+K, a chord rather than a single key, so the My settings switch does not turn it off (WCAG 2.1.4). It is a native `dialog` with a combobox and a listbox. Commands come from a scoped `CommandRegistry`: built-in navigation (the six queue views, My settings), admin pages (admin only, checked when listed and again when chosen), and commands a screen registers while it is mounted (the ticket page: reply, internal note, assign to me, not spam). Ticket commands raise the same shortcut action as their key. The palette opens only when `State == Ready`; admin commands are listed only when `IsAdmin` and `AdminOnly` is checked again when a command is chosen; choosing a command and running it after the dialog has closed both require `Ready` again, so nothing runs once the session has lapsed. It closes before a command runs, runs it once, never opens over a confirmation dialog or before the session is known, and catches every exception a command throws (a cancellation too: nothing awaits one), logging the type only. Destructive actions are not palette commands.
- **Local time.** `LocalTimeService` (scoped) reads the zone once after the first interactive render and resolves it with `TimeZoneInfo.FindSystemTimeZoneById`; an unknown or missing zone is UTC. The zone load has its own guard and runs after the shortcuts start. `RelativeTime` shows relative text (zone independent), a bare date from a week on (UTC before the zone loads; the zone label is only in the tooltip), a tooltip with the local and the UTC time (a zone with base offset 0 and no daylight saving, such as `Etc/UTC` or `Atlantic/Reykjavik`, shows the single UTC tooltip), and a UTC `datetime` attribute. The Admin image installs `tzdata`; moving the base image to Debian trixie needs `tzdata-legacy` for legacy zone names.
- **Responsive layout.** The rail folds behind a Menu button below 992 px (`aria-expanded`, `display: none` when closed, closes on navigation). The queue drops three columns from 768 to 992 px and becomes cards below 768 px. Tables scroll in a named, focusable `ScrollRegion` (7 ledger tables), and carry explicit table, rowgroup, row, columnheader and cell roles so the phone cards keep their table semantics; how a screen reader announces them is an owner check. Focus that was inside the rail panel moves to the Menu button when a navigation folds it. On a phone the composer follows the conversation. `prefers-reduced-motion` and `forced-colors` rules are tested against the compiled CSS. Breakpoints were chosen to match Bootstrap's `lg` and `md`.
- **Menu accessibility.** The ticket actions menu follows the menu button pattern (roles, arrow keys, Home and End, Escape returns focus, Tab closes). The rail links carry `aria-current="page"`. `BrandWindow` takes a heading level, so the 404 and the sign-in page have an `h1`.
- **07b minors.** An uncertain write is held until a read that started after it finishes (`UncertainMarks`, a load id stored with each mark); the audit page draws its events without waiting for the agent list; retyping the committed display name after an uncertain save says to reload; a product saved with a relative logo before the logo rule can be edited when the logo is left alone (`ProductBranding.CreateForUpdate` skips the logo rule for an unchanged stored value; the Admin editor mirrors it).

### Alternatives Considered
- **A strict `style-src 'self'` with the colours set from script** (CSSOM). Rejected: the prerender and a page without script would have no colours, and it is more code for the same protection of a validated hex value.
- **A per-request nonce or hash for inline script.** Rejected: there is no inline script to cover once `theme-init.js` is external, and a nonce does not cover style attributes.
- **A theme cookie rendered on `<html>`.** Rejected: a second source of truth and a cookie on every request.
- **Taking the time zone from the API or a stored preference.** Rejected: it needs an API change and a migration (the Admin adds no entry points, D-040), and a stored zone goes stale when an agent travels.
- **Taking the queue search out of the address.** Rejected: it loses bookmarks and back and forward for searches and reverses a recorded assumption; the masking in Sentry covers the exposure that mattered.
- **Ignoring `Retry-After`.** Rejected: a polite client waits a little when the API asks it to. Honouring it uncapped could stall a page for the whole client timeout.
- **Tearing the page down on a mid-session 401.** Rejected: it loses unsent replies and notes.
- **Per-page 401 handling.** Rejected: every page would need it, and one would be forgotten.

### Consequences
- **Superseded wording.** The D-040 and D-041 lines that moved the palette, local time, the theme flash, the CSP and the OpenAPI scheme to 07c are now done. ADMIN-APP.md describes them. The PHASE-07 package-table lines about "401 flips the session" and "CSP asserted in a host test" are now true.
- **The CSP relaxes one thing**, `style-src-attr 'unsafe-inline'`, so an injected `style` attribute would run. It cannot run script, and every value the Admin writes into a `style` attribute is a validated hex colour.
- **The Portal depends on Hosting** (the architecture tests allow it) and gets the PII enricher, the Sentry scrubbers and the headers early, before PHASE-09 builds on it.
- **A stored logo is untrusted.** A logo stored before 07b was never validated, and an unchanged stored logo is accepted on save, so any stored `LogoPath` must stay untrusted in every renderer: the Portal (PHASE-09) and the email renderer, which accepts only `https://`.
- **Other tab, old session.** After a mid-session 401 the circuit stays expired and sends nothing, so a sign-in in another tab does not revive it; the agent uses the banner.
- **The compose smoke is opt-in**, because it builds four images.
- **Still open:** the manual sign-in against a real identity provider (P07-T02, owner action 7), and with it the keyboard walk, the axe run and the CSP check of the redirects. The queue search is still in the browser history and in the proxy's access log.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-07c planning)
- **Approved on:** 2026-10-04

---

## D-043: Scoped per-project configuration and one image-only deployment compose

- **Status:** Approved (owner 2026-10-05; technical decisions at plan review)
- **Date:** 2026-10-05
- **Owner:** Jon Seeley
- **Related artifacts:** D-019, D-024, D-029, D-040, D-042, `docs/architecture/02-ARCHITECTURE.md` section 11, `docs/self-hosting/DEPLOYMENT.md`, `deploy/docker-compose.yml`, `docs/superpowers/plans/2026-10-05-deploy-config.md`

### Context
PHASE-07 is merged. Before PHASE-08 (the knowledge base) the owner wants TechStrap's configuration and deployment to follow the sibling repositories (`sinforgiver`, `who-flung-pu`). Reading the code found these facts:
- **Settings.** Each host's `appsettings.json` held only Sentry, OpenTelemetry, Serilog and `AllowedHosts`. ConnectionStrings, Auth, Api, Email, Storage, RateLimiting and the other sections came only from environment variables, so no file listed what a host reads.
- **Deployment.** `docker-compose.production.yml` and `docker-compose.uat.yml` were near copies. They bundled Postgres, had no `env_file`, interpolated every setting from one root `.env.production`, defaulted the image tag to `latest`, and could not set many keys the code reads (Sentry, OpenTelemetry, rate limits, `Api__TimeoutSeconds`, `TECHSTRAP_ADMIN_PUBLIC_URL`, LostLink). A blank `OIDC_*` passed `docker compose config` and then failed `ValidateOnStart` at boot.
- **Examples.** The Worker `.env.example` listed `TECHSTRAP_PORTAL_PUBLIC_URL` and `STORAGE__*`, which the Worker never reads. The Portal `.env.example` listed `API__BASEURL` and the public URL, which it does not read until PHASE-09. The local compose overrode the Admin's `AUTH__*` from a root `.env` that had no example, so `.env.local` clashed with it.
- **Binding.** A blank number, flag or enum fails configuration binding ("Failed to convert configuration value '' ..."); a blank nullable (`Email:Smtp:TlsMode`, `TotalSendTimeout`) binds as null. Binding an array appends to a non-empty default, so listing the defaults of `Auth:Scopes` would duplicate them. A blank array element still counts: `TrustedProxy` with one blank network passes the Production check.

### Decision
**Owner decisions (2026-10-05)**
- **One deploy compose.** `deploy/docker-compose.yml` serves UAT and production. It is image-only: no `build:` and no Postgres. A small compose-inputs file chooses the environment: the committed templates `deploy/.env.uat.example` and `deploy/.env.production.example`, copied to `deploy/.env.<env>.local` on the host. They hold the project name, the image references, the env directory, the loopback ports, the app subnet, the reverse-proxy CIDR and the Postgres network name, and no secret.
- **Scoped env files on the host.** `${TECHSTRAP_ENV_DIR}` (for example `/etc/techstrap/uat/`) holds `.env.api`, `.env.worker`, `.env.admin` and `.env.portal`, root-owned and mode 0600, loaded per service through `env_file`. The committed key-only templates `deploy/.env.<app>.example` are kept in sync with each project's `appsettings.json` and `.env.example`.
- **Separate Postgres.** It runs as its own instance, reached through an existing external Docker network named by `TECHSTRAP_DB_NETWORK`. Only the Api and the Worker join it.
- **Images.** GHCR stays (`ghcr.io/syntax-circus/techstrap-*`). The compose requires an explicit image reference per service: there is no `latest` default.
- **Delivery.** Its own PR before PHASE-08.

**Technical decisions (proposed in the plan; approved when the owner approves it)**
- **Every key in `appsettings.json`, with a real default or blank.** A value equals today's code default or is blank, so behaviour does not change. A blank is allowed only where the setting is a string (or a nullable) whose blank form is the default or the required-and-missing state. Every number, flag and enum carries its real default, because its blank form fails binding. The allowed blanks are an explicit list in the contract test. An array is `[]` in `appsettings.json` and its element key is commented out in the `.env` files, because a blank element is a configured element.
- **Production still fails fast.** Given only the blank deploy template, each host fails to start and names the missing keys; `ProductionBlankTemplateTests` pins it per host. The Portal has no required key yet (PHASE-09 adds them), so only the trusted proxies stop it.
- **What compose owns.** `environment:` sets only `ASPNETCORE_ENVIRONMENT`, `DOTENV__ENABLED=false`, `API__BASEURL` (Admin), the key-ring path (Admin, Portal), the storage path (Api) and the trusted networks (Api: subnet then `REVERSE_PROXY_CIDR`; Admin and Portal: `REVERSE_PROXY_CIDR`). These override the env file, so the deploy templates do not list them. `REVERSE_PROXY_CIDR` stays a compose input, which keeps the old entry layout.
- **Local compose.** It keeps only its own wiring in `environment:` (the local Postgres, the Api address, volume paths, the proxy trust, the portal URL it publishes and the Mailpit settings). The Admin's `Auth__*` and the group keys no longer come from compose: `appsettings.Development.json` holds clearly fake placeholders (`.invalid`, `not-configured`) and `.env.local` replaces them. The Worker loses its storage volume and the stale keys. A root `.env.example` documents the four compose inputs.
- **Contract test.** `scripts/tests/ConfigContract.Tests.ps1` compares keys case-insensitively as `SECTION__KEY`. It excludes `SassCompiler`, `Serilog:Using`, `WriteTo` and `MinimumLevel:Override`, and it does not list the keys set in code or owned by a library (`SecurityHeaders:ContentSecurityPolicy`, `Auth:Scopes`, `Auth:TokenCache`, `Storage:S3`, `AutoClose:Days`). A key whose name contains Password, Secret, Dsn, Token or Key (not `KeyRingPath`), and the OTLP headers, must be blank in a committed file unless its value is a number or a flag; the Development placeholders must be clearly fake. UAT and production inputs must set the same names.
- **Postgres password.** The connection string is a single value, so the deploy templates say to use only letters, digits and `- _ . ~` (`openssl rand -hex 24`). This closes the roadmap carry-forward.

### Alternatives Considered
- **Keep two compose files.** Rejected: they differed by a name and default ports, and drifted.
- **Interpolate secrets from one root env file** (the old way). Rejected: it cannot carry every key, passes blank secrets silently and puts all four apps' secrets in every container's compose environment.
- **Bundle Postgres in the deploy compose.** Rejected by the owner: the database has its own lifecycle and backups.
- **List the library defaults of `Auth:Scopes`.** Rejected: array binding appends, so the defaults would duplicate.
- **Make `.env.local` blank keys win in Development.** Rejected: a blank `AUTH__*` would replace the placeholders and stop the Admin, so the example keeps those three commented out.

### Consequences
- **The old files are gone.** `docker-compose.production.yml`, `docker-compose.uat.yml` and `.env.production.example` are deleted; PHASE-12 T14 deploys UAT with `deploy/docker-compose.yml`.
- **A deploy needs the scoped env files to exist.** `docker compose config` fails while one is missing, so a typo in `TECHSTRAP_ENV_DIR` stops before `pull`.
- **A new setting is four edits** (`appsettings.json`, `.env.example`, `deploy/.env.<app>.example` and, if compose owns it, the compose file), and the contract test fails until they agree.
- **The Worker no longer mounts the storage volume.** It never registered attachments.
- **Still open (owner):** create `/etc/techstrap/uat/` from the templates and run `config --quiet`, `pull` and `up -d --wait` against the external Postgres.

### Approval
- **Approved by:** Jon Seeley (owner, scoped configuration and deploy compose planning)
- **Approved on:** 2026-10-05

---

## D-044: PHASE-08: the knowledge base (scope, slugs, Markdown profile, images, public API, reply links)

- **Status:** Approved (owner 2026-10-05; defaults at PHASE-08 plan review)
- **Date:** 2026-10-05
- **Owner:** Jon Seeley
- **Related artifacts:** D-011, D-014, D-017, D-021, D-026, D-027, D-035, D-043, `docs/architecture/PHASE-08-knowledge-base.md`, `docs/architecture/UX-BRIEF-admin.md`, `docs/architecture/PHASE-09-public-portal.md`, `docs/superpowers/plans/2026-10-05-phase-08-knowledge-base.md`

### Context
PHASE-03 already delivered the KB tables, the unique `(product_id, slug)` indexes, the weighted `search_vector` and the dev seed, and PHASE-06a delivered `IMarkdownRenderer` and `IHtmlSanitizer`. Reading the code before PHASE-08 found these gaps:
- **Domain.** An Archived article could not be edited, `Publish` validated nothing, and `KbCategory` had no concurrency version.
- **Markdown.** The sanitiser allow-list had no `img` or table tags, and Markdig had no pipe tables. The same sanitiser renders agent replies that customers read in email and in the portal.
- **API.** There were no KB handlers or controllers, no image store, no public image route, no setting for the API's own public address, no `ts_headline` snippet and no cache headers.
- **Reply links.** The reply handler only checked that a linked article exists, and the customer email carried no article links.
- **Slugs.** The database lets a product article and a shared article use the same slug, so `/p/{key}/kb/{category}/{slug}` could be ambiguous.

### Decision
**Owner decisions (2026-10-05)**
- **Slug uniqueness is blocked across scopes.** A product article cannot reuse a shared article's slug, and a shared article cannot reuse any product's slug. It is one extra repository check on create; there is no index change.
- **Image URLs.** A new setting `TECHSTRAP_API_PUBLIC_URL`; images live at `https://<api public host>/kb-images/<key>`. It is blank in Development (the request's own origin is used) and required in Production. The owner's reverse proxy exposes `/kb-images/` publicly.
- **No audit.** KB changes stay out of the audit log in this phase; articles carry an author and timestamps.
- **Delivery.** One PR.

**Defaults (proposed in the plan; approved when the owner approves it)**
- **Scope model.** An article with a null `ProductId` is shared. `ProductId` and `Slug` are immutable after create. Categories are single-level and either product-scoped or shared; a shared article may use only a shared category. The category slug `search` is reserved (the portal's `/p/{key}/kb/search` page).
- **Lifecycle.** Draft, then Published, then Archived. Updating an Archived article returns it to Draft. Edits to a Published article are live immediately. There is no revision history and no hard delete.
- **Publish validation.** Title, slug, body and category must all be set. A failure is a 400 with the code `kb-publish-incomplete` and the missing field as the target.
- **Roles.** Agents manage articles, previews, images, and category create and update. Category delete is Admin only and is refused with a 409 while the category holds articles.
- **Markdown.** Raw HTML stays off. The knowledge base gets its own content profile: pipe tables, `img` (http and https only, with `alt`) and the table tags. Agent replies keep the existing pipeline unchanged.
- **Images.** 5 MB limit; png, jpeg, gif and webp only, recognised by their first bytes; SVG is rejected. The key is `kb-images/{guid}.{ext}`. The first 1024 bytes are scanned (case-insensitively) for markup such as `<script` and `<svg`; the scan is a leading-window speed bump only, because it covers what a browser content-sniffs and a whole-file scan refuses about 1.25% of genuine high-entropy 5 MB images. The real defences are the sniffed `image/*` type, `nosniff`, the sandbox CSP and the Api origin. There is no rate limit on `/kb-images`; rely on the reverse proxy or CDN, because per-IP limits would break image-heavy pages. There is no database row and no orphan cleanup. They are served anonymously with `nosniff`, `Content-Security-Policy: default-src 'none'; sandbox` and `Cache-Control: public, max-age=31536000, immutable`.
- **Public API.** Anonymous, with the existing `Public` rate limiter. `Cache-Control: public, max-age=60` on a hit and `no-store` on a 404; the sitemap uses `max-age=300`. Search uses `websearch_to_tsquery` and `ts_rank`, page size 10, at most 25. The snippet comes from `ts_headline` over the summary and is PLAIN TEXT (no highlight markup; `ts_headline` does not sanitise, so it can still hold characters such as `<`). Public DTO text fields are plain text; only `PublishedKbArticleDto.Html` is HTML; PHASE-09 must encode every text field it shows. Route and query values (product key, category slug, article slug) must have the slug shape and search text has control characters and unpaired surrogates removed, so a NUL or a lone surrogate never reaches the database or the UTF-8 encoder. An unknown or inactive product returns an empty result. An article lookup checks the product's own scope first, then the shared one. The DTOs carry no author and no ids of unpublished items.
- **Reply linking.** At most 10 links. Each article must be Published and either shared or in the ticket's product. The customer email lists links of the form `{PortalPublicUrl}/p/{key}/kb/{category}/{slug}`. The Admin picker offers Published articles only.
- **Admin.** A rail link "Knowledge base" for every agent and the palette command `go-kb`. Routes `/kb`, `/kb/new`, `/kb/{id}` and `/kb/categories`. A toolbar with bold, italic, link, list, code and image. A live preview with a 300 ms debounce through one sanitised preview pane, which is the one new `MarkupString` site. Image upload uses the file picker and appends the snippet (no caret insertion, paste or drag-and-drop yet). `NavigationLock` guards unsaved changes, a 409 keeps the draft and shows a banner, and `ConfirmDialog` guards archive and category delete.
- **Config (D-043).** `TECHSTRAP_API_PUBLIC_URL` goes through the four D-043 edits for the Api. An optional Admin setting `TECHSTRAP_PORTAL_PUBLIC_URL`, blank by default, powers a "View on portal" link that hides when it is unset.
- **Seed.** One Paperplane category and one published Paperplane article.

**Technical decisions (proposed in the plan; approved when the owner approves it)**
- **A separate KB content profile, not a wider shared sanitiser.** `IKbContentRenderer` (Markdig with pipe tables, then a KB sanitiser) serves the preview and the public article. `IMarkdownRenderer` and `IHtmlSanitizer` are untouched, so a tracking image or a table in an agent reply is still removed. A corpus test pins that the message pipeline output is unchanged. Both profiles keep raw HTML off.
- **No `422`, `415` or `413` from a handler.** `Result` has no such kinds, so an incomplete publish, a wrong image type and an oversize image inside the request limit are 400 with their own codes (`kb-publish-incomplete`, `kb-image-type-not-allowed`, `kb-image-too-large`). A body beyond the request limit is still stopped by Kestrel as 413.
- **Cross-scope slugs for categories too.** The same rule applies to category slugs (`kb-category-slug-taken`), so a category address is never ambiguous. The `search` slug is `kb-category-reserved-slug`.
- **A category gets a description and a version.** `kb_categories` gains a nullable `description` (300 characters) and the `xmin` concurrency token, in one generated migration. An update with a stale version is a 409 `concurrency-conflict`.
- **Public routes carry the product key.** `GET api/public/kb/{productKey}/search`, `/categories`, `/articles/{categorySlug}/{slug}` and `/sitemap`, plus `GET kb-images/{key}` at the root of the Api. The article route carries the category slug because the portal URL does.
- **Archiving hides, editing reopens.** Archive keeps the row and its first publication date. Publishing an Archived article goes straight to Published, subject to the publish validation.
- **No render cache.** The article is rendered on each read; a cache is added only if a measurement asks for one.
- **A render complexity cap.** The sanitiser is roughly quadratic in element count and pipe tables turn each character into about two elements (measured: a 200,000-character table took 152 s). `KbLimits.MaxRenderedElements` is 5,000 blocks plus inlines, counted after a Markdig parse in one linear pass (`IKbContentRenderer.IsTooComplex`). The preview, and article create, update and publish (Task 3), refuse a body over the cap with a 400 `kb-body-too-complex` (target `body`), so it is never stored. `Render` never sanitises an over-cap body: it returns the text HTML-encoded in one paragraph, so a public read cannot be made expensive.

### Alternatives Considered
- **Widen the shared sanitiser.** Rejected: agent replies reach customers by email and in the portal, so a widened list would let a reply carry a tracking image or a table.
- **Per-scope slugs with the product winning at read time.** Rejected by the owner: a shared article could be hidden silently.
- **Relative `/kb-images/<key>` served on the portal origin.** Rejected by the owner: it needs a proxy route or a new Portal adapter that PHASE-09 does not plan.
- **Audit publish, archive and category changes.** Rejected for this phase (owner).
- **Return `422` for an incomplete publish.** Rejected: `Result` cannot produce it, and the Admin already maps 400 to field errors.

### Consequences
- **The API needs one new setting in Production.** `TECHSTRAP_API_PUBLIC_URL` is required; the Api refuses to start without it. The owner must route `/kb-images/` through the reverse proxy.
- **Existing tests that published a category-less article now need a category.** Publishing validates it.
- **Two concurrent creates of one slug in different scopes can both succeed.** The unique index separates the scopes, so only the create-time check blocks a cross-scope duplicate; the owner accepts this narrow race.
- **Images may point at any http or https address.** That includes internal-network hosts and mixed-content `http://` sources. A KB article is written by agents only, so this is part of the accepted tracking-pixel risk; the sanitiser adds `referrerpolicy="no-referrer"` and `loading="lazy"`.
- **Public DTO text is plain text.** The portal must HTML-encode the search snippet, titles and names; only the article `Html` is markup that is already safe.
- **Still open (owner):** the reverse proxy route for `/kb-images/`, and checking an uploaded image loads from the API public URL.
- **Admin as built.** Publish and archive send the loaded version, so publishing text another agent has changed is a 409. A write whose answer is lost is held until a reload, except a picture upload, which changes no article until its address is added, so the agent just picks it again. The picker is behind a button and searches only Published articles of the ticket's product and the shared ones. "View on portal" is built from the Admin's own optional `TECHSTRAP_PORTAL_PUBLIC_URL` (the same key and four edits as the Api's; blank hides the link) and uses the first active product by name for a shared article. The Admin CSP needed no change: its Development `img-src` already allows loopback on any port and Production allows https.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-08 planning)
- **Approved on:** 2026-10-05

## D-045: PHASE-09: the public portal (delivery, KB suggestions, API additions, theming, client, headers, SEO)

- **Status:** Approved (owner 2026-10-05; technical rulings at PHASE-09a plan review)
- **Date:** 2026-10-05
- **Owner:** Jon Seeley
- **Related artifacts:** D-002, D-017, D-019, D-024, D-032, D-038, D-040, D-042, D-043, D-044, `docs/architecture/PHASE-09-public-portal.md`, `docs/architecture/UX-BRIEF-portal.md`, `docs/superpowers/plans/2026-10-05-phase-09a-portal-foundation.md`

### Context
PHASE-02, PHASE-06 and PHASE-08 are merged, so PHASE-09 can start. Reading the code before planning found these gaps between the spec and what exists:
- **No public product list.** The root page, the sitemap and any product chooser need to enumerate active products, and the API only answers one key at a time.
- **No list of a category's articles.** `GET api/public/kb/{productKey}/search` returns an empty page for a blank query, so a category page cannot be built.
- **`CustomerTicketDto` has no product.** The ticket page at `/t/{token}` has no product key in its route, so it cannot be themed.
- **The deflection island loses the visitor's IP.** `AddForwardedClientIp()` does nothing without an `HttpContext`, and an InteractiveServer circuit has none, so every suggestion search would share one rate-limit bucket (D-019).
- **The package API differs from the spec.** `SyntaxCircus.Blazor.Seo` 0.1.4 has `AddSyntaxCircusSeo`, `UseSyntaxCircusSeo`, `MapSeoSitemap` and `MapSeoRobotsTxt`; the spec's `UseCanonicalHost`, `MapRobotsTxt`, `MapSitemap` and `ISitemapEntryProvider` do not exist, and the sitemap is not cached server side.
- **The shared security-header middleware overwrites a per-page header.** It sets `Referrer-Policy` and the CSP when the response starts, so an endpoint cannot set them itself.
- **The lost-link timing test conflicts with D-038.** D-038 accepts a residual timing difference of tens of milliseconds between a known and an unknown address.
- **There is no way to carry the ticket number to the "received" page** without a cookie or an access token.
- **`BrandingThemeFactory` would duplicate logic.** `PublicProductDto` already carries the derived accent colours and the Portal already has `AccentScope`.

### Decision
**Owner decisions (2026-10-05)**
- **Delivery.** Three pull requests, each with its own branch, plan and review: 09a the foundation, 09b the customer flows, 09c the knowledge base, SEO and polish.
- **KB suggestions are vanilla JS, not an InteractiveServer island.** There is no framework, no Blazor interactivity and no build step. The server renders a `<ts-kb-suggestions>` custom element with fallback markup inside it (a help-centre search link). A module in `wwwroot/js` defines the element: `connectedCallback` attaches a debounced listener to the subject field and fetches suggestions, and `disconnectedCallback` removes it and aborts any request in flight. It fetches from a Portal-hosted `GET /p/{key}/suggest?q=` adapter (moved from `/p/{key}/kb/suggest` by the 2026-10-06 addendum below), which forwards the real client IP to the API's public search. The adapter is exempt like D-017 (no workflow) and returns plain-text JSON that the module renders with `textContent`. There is no SignalR, no WebAssembly and no CSP change.
- **Two small public API additions** (anonymous, the Public rate limit, the same cache headers as D-044), made in 09c: a paged list of a category's articles (published only, the product's plus shared, newest updated first), and `GET api/public/products` returning the active products (key and display name only), for the sitemap.
- **Amended by D-053 (2026-10-10):** the Portal may be themed beyond the accent trio by a deployment default pack and a per-product skin of validated tokens; neutral pages use only the default pack, so their body is still identical for every address.
- **`/` redirects to `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` when it is set.** Otherwise it shows a neutral page with no product list, so nothing can be enumerated.
- **Amended by D-052 (2026-10-09):** `TECHSTRAP_PORTAL_LANDING=Products` lists the active, listed products on the root; `Neutral` (the default) keeps this behaviour.
- **Ticket theming.** `CustomerTicketDto` gains `ProductKey` (additive), in 09b. `/t/{token}` loads that product's branding. `CustomerDtoShapeTests` is updated.
- **T20 (Playwright end-to-end) is deferred.** It was optional in the spec and stays out of all three pull requests.

**Technical rulings (proposed in the 09a plan; approved when the owner approves it)**
- **Portal API client.** The Portal gets its own small `ApiConnection` and `ProblemMapping`, modelled on the Admin's. It does not share code with the Admin: Hosting stays a leaf and the Portal references exactly Contracts and Hosting. A read client retries transport errors, 408 and 502 to 504 and honours `Retry-After` up to 2 seconds; a write client never retries. Both use `AddForwardedClientIp()`, `RemoveAllLoggers()` and a 5-minute pooled connection lifetime. `ProblemMapping` maps 400 (field errors), 404, 413 and 415 (the attachment errors), 429 (rate limited) and 5xx or a transport error (`api-unavailable`) explicitly. A customer call sets `X-Ticket-Token` on the request itself; the value is never logged and never put in a URL.
- **The ticket and KB clients arrive with their pages.** 09a ships `IPublicProductClient` and the token capability of `ApiConnection`; `IPublicTicketClient` and `ICustomerTicketClient` come with 09b and `IPublicKbClient` with 09c (YAGNI).
- **`SyntaxCircus.Blazor.Seo` 0.1.4, with its real API.** `AddSyntaxCircusSeo(config)` and `UseSyntaxCircusSeo()`; `MapSeoRobotsTxt(extraDirectives: ["Disallow: /t/"])`; in 09c `MapSeoSitemap(static, provider)`, where the provider is backed by the Portal's own 15-minute cache. The article JSON-LD is a local POCO. 03-PACKAGE-MAP and the PHASE-09 spec use the real names.
- **One key for the public address.** `Seo:BaseUrl` is derived in code from `TECHSTRAP_PORTAL_PUBLIC_URL` (a post-configure step), so there is one setting, required outside Development. `CanonicalHost__*` stay their own optional keys (blank means no redirect), because the package's redirect is an allow-list of legacy hosts and a Portal with one address needs none. The sitemap is not mapped in 09a: it needs the 09c products endpoint.
- **Per-path headers.** `UseTechStrapWebHost` accepts per-path rules (a path predicate and header overrides) that run after the shared security headers, so they win. `/t/*` gets `Referrer-Policy: no-referrer`, `Cache-Control: no-store` and `X-Robots-Tag: noindex`; the sandbox CSP applies only to `/t/{token}/attachments/{id}`, so the ticket page keeps the normal CSP. The Admin's download prefix is expressed as one such rule, with unchanged behaviour.
- **Lost link.** The Portal's responses are byte-identical whatever the address. Timing is out of scope: D-038 accepts the residual difference, and the spec's timing test is relaxed to the byte comparison.
- **The "received" page.** The ticket number travels in a data-protection-protected `?ref=` value that expires after 10 minutes. There is no cookie and no access token. An expired or tampered value shows the generic confirmation.
- **Theming.** There is no `BrandingThemeFactory`. A thin `ProductThemeViewModel` reuses `AccentScope` and the DTO's derived colours, and re-checks the logo address (https only, or loopback http in Development, matching `TechStrapCsp.ForBlazorApp`); an unacceptable logo is omitted. NotFound and Error stay neutral (no enumeration, and the PHASE-04 no-brand guard): an unknown, inactive or malformed product key is answered exactly like an unknown route.
- **Shared KB articles.** The canonical URL is the product path the visitor is on, because each product's help centre is its own site. The sitemap lists a shared article under each product.
- **Forms.** Static-SSR forms use `[SupplyParameterFromForm]` and antiforgery. Attachments use a plain `<input type="file" multiple>`, which works without script, and are streamed into the multipart request. The Portal enforces the request size limit with Contracts `IntakeLimits`.

### Alternatives Considered
- **Keep the InteractiveServer island.** Rejected by the owner: it adds a public SignalR circuit, loses the visitor's IP and needs a custom handler to work around it.
- **A configured list of products for the root page and sitemap.** Rejected: it diverges from "active products" and needs a redeploy to change.
- **Share `ApiConnection` and `ProblemMapping` with the Admin through Hosting.** Rejected: it gives Hosting a Contracts and Common reference, and the Admin's version carries a session-expiry concern the Portal does not have.
- **A `BrandingThemeFactory`.** Rejected: `ProductAccent` is the single implementation of the accent rule and the DTO already carries its output.
- **A separate `Seo__BaseUrl` key.** Rejected: two keys for one value would drift.
- **A cookie for the "received" page.** Rejected: a protected, short-lived query value needs no state and no consent notice.

### Consequences
- **The Portal needs two settings in Production.** `API__BASEURL` (set by the deploy compose) and `TECHSTRAP_PORTAL_PUBLIC_URL` (the operator's). The Portal refuses to start without them and names the key.
- **The PHASE-09 spec text was corrected where it named things that do not exist.** The Seo names, `BrandingThemeFactory`, the lost-link timing test and "Portal references Contracts only" (it is Contracts plus Hosting).
- **A malformed or unknown product key is a 404 without calling the API.** Only a well-formed key is sent on.
- **The suggest adapter has no slug to reserve.** The 09b addendum below puts it at `/p/{key}/suggest`, beside the KB rather than under it, so no category slug can shadow it and the API's reserved slug stays `search` only.
- **09a leaves the sitemap unmapped.** `/robots.txt` is served and disallows `/t/`; its `Sitemap:` line points at `/sitemap.xml`, which answers 404 until 09c.
- **As built in 09a: log and Sentry redaction.** The shared PII redactor masks the value of a `name` or `email` query parameter (the contact page prefill) in every host's logs, and the Sentry processors mask the same, plus a 43-character token directly under `/t/` in any address they scrub. The Api and the Admin get this too; masking a `name=` value in their logs is accepted.
- **As built in 09a: T17 redaction of the contact prefill.** The `name` and `email` query values of the contact page prefill are masked in logs and in Sentry (see above), pinned by `RequestLogRedactionHostTests`, `PiiRedactionQueryValueTests` and `SensitiveQuerySentryProcessorTests`.
- **As built in 09a: host tests assert the final /t headers.** The host tests check the headers a `/t/...` response finally carries (`Referrer-Policy: no-referrer`, `Cache-Control: no-store`, `X-Robots-Tag: noindex`), not only the rule table that produces them.
- **As built in 09a: `GlobalErrorBoundary` is not used.** Its retry button needs interactivity, and catching a render error in the page would answer 200 with a branded page instead of the plain 500 error page.
- **Known: a legacy-host redirect decodes percent-escapes.** `UseSyntaxCircusSeo` builds the target with `Uri.ToString()`, so an encoded `&` or `#` in a query value changes meaning. It affects only hosts listed in `CANONICALHOST__LEGACYHOSTS`; it is to be reported upstream.
- **Known: Sentry has no general email rule.** The Sentry processors mask the `name` and `email` query values and the `/t/{token}` path, but do not pattern-match email addresses elsewhere in an event (Serilog's email pattern does catch them in logs).
- **Known: OpenTelemetry server spans would carry the ticket path.** If OpenTelemetry tracing were enabled, a server span would carry `url.path=/t/<token>`. Tracing is off by default; masking it is a follow-up.
- **Known: the product pages link ahead.** Contact support and the footer's lost-link link (09b) and the search box (09c) answer 404 until their pages exist.

### Addendum (2026-10-06, PHASE-09b customer flows)
The owner's rulings for PHASE-09b, and what the plan's spike proved. They extend D-045; where they differ from the text above, they win. There is no new decision number.

**Rulings**
- **The suggest adapter is `GET /p/{key}/suggest?q=`.** It replaces `/p/{key}/kb/suggest`, so no category slug can collide with it and nothing needs reserving in the Domain, the Admin, Contracts or the docs. The route constants are `PortalRoutes.SuggestTemplate` and `PortalRoutes.Suggest(key)`.
- **`IPublicKbClient` has `SearchAsync` only in 09b; 09c extends it.** The adapter returns plain JSON `{title, snippet, href}`, each `href` built with `PortalRoutes.KbArticle` from the product key the visitor is on. It caps the items, cuts the query at the API's 200 characters and answers an empty list for blank text. An API 429 is passed through as a 429; any other failure is an empty list. The response is `no-store`.
- **The form limits move into Contracts.** `IntakeLimits` gains `NameMaxLength` (100), `EmailMaxLength` (320), `SubjectMaxLength` (200), `BodyMaxLength` (100,000) and `FormBodyBytes` (27,262,976, the API's own request limit). `IntakeLimitsParityTests` keeps them equal to `DomainLimits`. The contact form, the reply form and the prefill `maxlength` attributes use them.
- **Static SSR forms** use `[SupplyParameterFromForm]`, `<AntiforgeryToken/>`, a plain `<input type="file" multiple>` and a redirect after the post. Attachments are sent on as `StreamContent`; the framework buffers an upload above 64 KB to a temporary file, so "streamed" means not held in memory.
- **The honeypot is passed through.** The Portal sends the `Website` field to the API, which validates the product and answers a believable 201 without creating a ticket, so the response to a bot is the real one and product validation stays uniform. This replaces the spec's "silently success page without calling the API".
- **The "received" page.** The ticket number travels in `?ref=`, protected with `CreateProtector("TechStrap.Portal.ContactReceived.v1").ToTimeLimitedDataProtector()` for 10 minutes. An expired or tampered value shows the generic confirmation. Production needs a persisted key ring (`DATAPROTECTION__KEYRINGPATH`, which both compose files already set) or a restart makes every pending reference generic. The page shows the ticket number only: not the subject and not the address, so the value carries nothing personal.
- **The ticket page.** `CustomerTicketDto` gains `ProductKey` (a one-line handler change; the product was already loaded). The page parses the token first (a malformed one is the uniform 404 with no API call), loads the ticket, then loads the product's theme from `ProductKey`; an inactive or unknown product gets the neutral theme, never a 404. `CustomerMessageBody` is the first `MarkupString` site (the API sanitises the HTML). Status wording: New "Received", Open "In progress", Pending "Waiting for your reply", Solved "Solved", Closed "Closed".
- **Replies.** A reply on a ticket that is not Closed redirects to the same page. A reply on a Closed ticket redirects to the follow-up: the token is the last segment of `FollowUpViewUrl`, run through `TicketToken.TryParse`, and the redirect is `PortalRoutes.Ticket(newToken)`, so a visitor is never sent to another host; if it does not parse, the page shows a generic confirmation. A 409 (`reply-conflict`) has its own friendly message (`ProblemMapping` maps it; it was `api-error`).
- **The attachment pass-through** `GET /t/{token}/attachments/{id}` uses a new `ApiConnection.OpenStreamAsync(uri, TicketToken, ct)`, which reads only the response headers (through the read client) before streaming. A bad token or a non-GUID id is the uniform 404; an upstream 404 is the uniform 404; a transport failure is 502. The response is `Content-Disposition: attachment` with `nosniff`; the existing sandbox rule covers the CSP.
- **Lost link.** For any well-formed address the Portal shows the same byte-identical confirmation; a malformed address shows the validation message; a 429 shows a friendly message.
- **noindex and no-store** are added by a path rule to `/p/{key}/contact`, `/p/{key}/contact/received`, `/p/{key}/lost-link` and `/p/{key}/suggest`.
- **Redaction.** The `subject` and `ref` query values are masked in Serilog and in Sentry, and `q` in Serilog (Sentry already masks it).
- **T18.** `scripts/Test-ComposeSmoke.ps1` gains a Portal readiness check and a check that the rate limit sees the real client address through `/p/{key}/suggest`; the DryRun output and its Pester pins are updated.
- **Vanilla JS.** `wwwroot/js/kb-suggestions.js` defines `<ts-kb-suggestions>`; its dependencies (`fetch`, timers, `AbortController`) are passed in so `node --test` can run it, through the new `scripts/tests/PortalScripts.Tests.ps1`. It loads as `<script type="module" src=...>`, so the CSP does not change.

**Spike findings (proven in a scratch copy before the plan was written)**
- **File binding.** A model class with `IReadOnlyList<IBrowserFile>? Files` binds `<input type="file" name="Form.Files" multiple>`: two files arrive with their `Name`, `Size` and `ContentType`. An empty file input (the part a browser sends with `filename=""`) and a form with no file part both give a count of 0, so an empty part needs no handling.
- **Reading a file.** `IBrowserFile.OpenReadStream()` throws above 512,000 bytes; the Portal always passes `IntakeLimits.MaxFileBytes`.
- **Forms.** A `[SupplyParameterFromForm]` property may not have an initializer (analyzer BL0008): the page sets it in `OnInitialized` when the framework left it null. A missing or invalid antiforgery token answers 400.
- **Request size limit.** `[RequestSizeLimit(IntakeLimits.FormBodyBytes)]` on the page component is read as endpoint metadata and applied by endpoint routing, before the antiforgery middleware and before the form is read (the limit feature is already `2000` and not read-only in a middleware placed before `UseAntiforgery`, tested with `UseKestrel(0)` because `TestServer` has no such feature). A body over the limit is rejected without being buffered, but the framework reports it as a 400 with the antiforgery text, so a small middleware answers a declared `Content-Length` over the applied limit with a plain 413 first; a chunked body over the limit is still the 400.
- **Redirect after post.** `NavigationManager.NavigateTo` in a static SSR handler answers 302 with an absolute `Location`; the handler returns straight after it.

**Consequences of the addendum (as built in 09b)**
- **As built in 09b: a post to an unknown product is a 400, not a 404.** The product page asks the API for the product before the form handler runs and ends in `NavigationManager.NotFound()`; `NotFound()` renders the not-found page in the same request (it is not a re-execution of the post: the request has one dependency scope, `ContactPostHostTests` counts it, and only a plain 404 that reaches the host's status-code middleware is re-executed), and the framework then rejects the post, because no form named `contact` was rendered in that response. So a post to an unknown, inactive or malformed product (with a valid antiforgery token) gets the framework's own plain-text 400, "Cannot submit the form 'contact' because no form on the page currently has that name.". Nothing is created. A GET is the neutral 404 page. A post to a malformed or an unknown ticket token is the same thing for the form `reply`: an identical body whichever part was wrong, so it tells a visitor nothing (pinned by `TicketReplyHostTests`). The 09a note that "an unknown product 404s before the form handler runs" was right about the order and wrong about the status.
- **As built in 09b: the request size limit.** `[RequestSizeLimit(IntakeLimits.FormBodyBytes)]` on the contact and ticket pages is the limit; `RequestTooLargeMiddleware` (before `UseAntiforgery`) turns a declared `Content-Length` over it into a plain 413 with the Portal's own sentence, because the framework reports the failed form read as an antiforgery 400. A chunked body over the limit is still that 400. A file is read with `OpenReadStream(IntakeLimits.MaxFileBytes)` (the default is 512,000 bytes); Cmsify's media upload uses the same pattern (an explicit maximum, the stream passed on without buffering, `[RequestSizeLimit]` on the receiving endpoint).
- **As built in 09b: the honeypot** is sent to the API as `Website` and a filled one gets the API's believable 201; the Portal never short-circuits, so product validation and the response stay uniform. The PHASE-09 text that said otherwise is corrected.
- **As built in 09b: the ticket page.** The token is parsed first; the API is never asked for a malformed one; every way to fail to find a ticket (and a wrong attachment id) is the same neutral 404, byte for byte. An inactive or unknown product on a valid ticket is the neutral theme, not a 404. `CustomerMessageBody` is the only place the Portal renders markup. The follow-up redirect is built from the last segment of `FollowUpViewUrl` by `FollowUpLink` and `PortalRoutes.Ticket`, so it never leaves the site; an unreadable link shows a generic confirmation.
- **As built in 09b: the suggest adapter** is `GET /p/{key}/suggest`, a minimal-API endpoint in `Suggestions/` (it calls `IPublicKbClient`, so `PortalRules` allows it outside `Clients/`), and the element is `wwwroot/js/kb-suggestions.js`, tested with `node --test` through `scripts/tests/PortalScripts.Tests.ps1`.
- **As built in 09b: the smoke** (`scripts/Test-ComposeSmoke.ps1`) starts the Portal, checks its `/health/ready` and proves the real client address through `/p/smoke/suggest`: its override lowers the Api's public limit to 3 and makes the Portal trust the compose subnet, and the calls are made from inside the network (`docker compose exec api curl`), because a call from the host arrives from the Docker gateway, whose address differs between Linux and Docker Desktop. It never runs `down -v`.
- **As built in 09b: redaction.** The `subject` and `ref` query values are masked in Serilog and in Sentry, and `q` in Serilog (Hosting, shared by every host: masking those parameter names in the Api's and the Admin's logs is accepted).
- **Known in 09b: no copy button, counter or sending state.** The ticket number has no copy affordance, the message has no character counter and the submit button has no in-flight state, because each needs script and every 09b flow is script-free; a double click can send twice. The 09c polish pass decides. **Resolved in 09d:** `portal-forms.js` adds the three, and the one-time form id and `SubmitGuard` make a double click send once (see the 09d addendum).
- **Known in 09b: `TicketToken.Value` is internal** and read in two places only (`ApiConnection` for the header and `PortalRoutes` for a link).
- **Known in 09b: `ProductKey` sits in the middle of the positional `CustomerTicketDto`** (after `Number`, before `Subject`). Anything that builds or deconstructs that record by position (the PHASE-11 SDK, a generated client) must follow the new order; a JSON reader by name is not affected.

### Addendum (2026-10-06, PHASE-09c knowledge base pages, SEO and caching)
The owner's rulings for PHASE-09c, and what the plan's spike proved. They extend D-045 and the 09b addendum; where they differ from the text above, they win. There is no new decision number.

**Rulings**
- **API: a category's articles.** `GET api/public/kb/{productKey}/categories/{categorySlug}/articles?page=&pageSize=` (anonymous, the Public rate limit, `public, max-age=60` on success and `no-store` on an error) returns `PagedResponse<PublicKbArticleSummaryDto>`: `Slug`, `Title`, `Summary`, `CategorySlug`, `CategoryName`, `ProductKey` (null for a shared article) and `UpdatedAt`, all plain text, newest update first, 10 a page and at most 25. It is built on the one `PublishedIn` predicate of `KbRepository` (Published, the product's plus the shared space, in a category the product can see). An unknown or inactive product, a malformed or unknown slug, another product's category and a category with no published article are the same 404 (`kb-category-not-found`), like the article route; a page past the end is a 200 with no items. The handler is `ListPublicKbCategoryArticlesRequestHandler`; the repository method is `ListPublicCategoryArticlesAsync`. There is no schema change.
- **API: the active products.** `GET api/public/products` (anonymous, the Public rate limit, `public, max-age=300`) returns `PublicProductSummaryDto(Key, DisplayName)` for the ACTIVE products, ordered by key and capped at `PublicProductLimits.MaxListed` (1,000). Listing the keys is accepted because the sitemap publishes them anyway; it carries no logo, colour, id or address, and the Portal's root page still lists nothing.
- **The Portal client.** `IPublicKbClient` gains `ListCategoriesAsync`, `ListCategoryArticlesAsync`, `GetArticleAsync`, `GetSitemapAsync` and a `SearchAsync` overload with a page; `IPublicProductClient` gains `ListAsync`. All are reads through the read client (retried, the visitor's address forwarded). A key or slug that is not a slug is the uniform not-found error and no call is made.
- **Pages** (static SSR, each on `ProductPageBase`): `/p/{key}/kb` (the categories with their counts and descriptions), `/p/{key}/kb/{category}` (the article list, paged with `?page=`, which works without script), `/p/{key}/kb/search?q=&page=` (a GET form: an empty query shows a prompt, no result shows a contact link, a snippet is plain text and encoded, a query makes the page noindex, and it is never cached) and `/p/{key}/kb/{category}/{slug}` (the article). An unknown product, an unknown category and an unpublished article are the one neutral 404, byte for byte. The shared pieces are `KbArticleCard`, `KbBreadcrumbs`, `Pager` and `StateMessage`; the words are in `KbCopy`. `KbArticleBody` is the second and last place the Portal renders markup (the API sanitises it); `PortalRules.MarkupStringSites` lists exactly `CustomerMessageBody` and `KbArticleBody`. The `page` query value is read as text and parsed by the Portal (the framework's own binding to a number answers 500 for `?page=abc`).
- **SEO.** `SeoHead` sets the title, the description (the article summary, else the first sentence of the body as plain text), the canonical address (the current product path, so a shared article is canonical under each product), Open Graph and `NoIndex`; the image is a Portal asset. The structured data is a `BreadcrumbList` and an `Article`, both Portal records.
  - **Escaping.** `SyntaxCircus.Blazor.Seo` 0.1.4 renders `JsonLd` through a markup string with an encoder that leaves `<`, `>` and `&` alone, so a `</script>` in an article title ends the script block and injects markup (the spike reproduced it). Escaping the text before it reaches the package's schema records would be double-escaped by the serialiser (the JSON would then read back as the backslash text), so the Portal's own records carry every string as a `JsonLdText`, a value type whose converter writes the string with the strict encoder: `<`, `>`, `&`, `'`, `+` and every non-ASCII character become `\uXXXX`, and the JSON reads back as the original text. `JsonLdText.Safe(text)` is the only way to make one. The problem is to be reported upstream as a package issue.
- **Sitemap.** `MapSeoSitemap` with a provider: the products list, then one sitemap call per product, giving the product home, the KB home, the categories (derived from the entries, with the newest update as `lastmod`) and the articles; a shared article is listed under each product. The URLs are absolute, built from `TECHSTRAP_PORTAL_PUBLIC_URL`. The provider is backed by one `IMemoryCache` entry for 15 minutes with single-flight: the build runs with its own cancellation token (a crawler that goes away cannot poison the cache), a failure is remembered for 1 minute and the last good value is served meanwhile, and at most 50,000 URLs are listed (a cut is logged). The build's API calls carry the address of the visitor whose request started it (accepted and recorded as a known gap). The static `/` entry is listed only when no default product is configured; with one, `/` redirects to it.
- **Output caching** with the framework's `AddOutputCache` (no new package): one base policy with a path predicate (`PortalCachePaths`), 60 seconds, varying by the `page` query value only and not by host. Cached: `/p/{key}/kb`, a category page and an article page. Never cached: the search page, `/t/*`, the form pages, the suggest adapter, `/not-found` and every response that is not a 200. `UseOutputCache` sits after the error pages and before the endpoints, so the security headers are applied on a hit; a small step before it restores the request's own `X-Correlation-Id`, because a stored copy replays the first request's. Browsers get `Cache-Control: public, max-age=60` on a KB page from a new success-only Hosting rule, `PathHeaderRule.SetOnSuccess`, and `no-store` on the search page; a 404, 429 or 503 never gets a public header.
- **KB images.** A plain-http image is blocked in Production by `img-src 'self' https: data:`. That is the correct posture; it is documented, not changed.
- **Docs.** PORTAL-APP.md, the PHASE-09 ticks (T02, T04, T12 to T15 and the deliverables), the roadmap and discovery rows ("09c complete (pending merge); 09d not started") and the as-built notes below. The spec handoff is corrected too: the PHASE-09 spec, `02-ARCHITECTURE.md`, `UX-BRIEF-portal.md` and Q-11 of `01-REQUIREMENTS.md` named a `GetSitemapEntriesRequestHandler`, an `ISitemapEntryProvider` and an `ApiSitemapEntryProvider` that never existed; the handoff is `GetKbSitemapRequestHandler` (one product) and `ListPublicProductsRequestHandler`, read through the Portal provider (`PortalSitemapBuilder`). The PHASE-09 spec also said JSON-LD sits on the KB pages and the article page; it is on the article page only (the Portal's own `BreadcrumbListLd` and `ArticleSchema`).

**Spike findings (proven in a scratch copy before the plan was written)**
- **The cache policy.** `AddBasePolicy(p => p.With(predicate).Expire(60 s).SetVaryByQuery("page").SetVaryByHost(false))` caches only the KB paths: the same request twice makes one API call, and the product home and any other path make two. A 404, a 429 and a 503 are never stored (an unknown product asked twice reaches the API twice).
- **The default key.** Without the explicit rules the key holds the whole query string and the host: six requests with different `utm` or `page` values made five API calls, and two other `Host` values made two more. With `SetVaryByQuery("page")` and `SetVaryByHost(false)` the same requests made three and none. The Portal also caches a request only when its `page` value is acceptable, so `?page=<garbage>` cannot fill the store (the rule as built is under "As built in 09c: the cache").
- **A hit is not fully fresh.** The security headers and a rule's headers are right on a hit, but the stored copy replays the first request's `X-Correlation-Id` (a request that sent `cid-two` got `cid-one`). A step before `UseOutputCache` that sets the header again when the response starts (from `HttpContext.Items`) fixes it; the spike proved it.
- **Query binding.** The framework binds `[SupplyParameterFromQuery]` case-insensitively and takes the first of two values, but `?page=abc` and `?page=99999999999` answer 500 for an `int?`; the pages bind text.
- **Head and JSON-LD.** `SeoHead` works in static SSR (the layout already has `<HeadOutlet />`). The ld+json block is rendered as `type="application/ld&#x2B;json"`, which a browser reads as `application/ld+json`, so the tests parse the page with an HTML parser. The block is data, not script, so the CSP is unchanged (`script-src 'self'`). The package's `JsonLd` lets a hostile title inject an element (`<img src=x onerror=...>` became part of the page); the converter-based `JsonLdText` keeps the same title inside the string, and the Razor-encoded meta tags and title were already safe. A Razor file cannot hold the text `</script` in a string (the Razor parser reads it as a tag), so the hostile texts live in C#.
- **The sitemap.** `MapSeoSitemap` accepts a provider that reads an `IMemoryCache` with single-flight: twenty concurrent requests made one build, the result is `application/xml` with each `<loc>` XML-escaped, and `cacheDuration` only sets the client's `Cache-Control`. `MemoryCacheOptions` has no `TimeProvider` (only the obsolete `ISystemClock`), so the cache takes its two lifetimes as constructor values and the tests use short real ones.

**Consequences of the addendum (as built in 09c)**
- **As built in 09c: the category list and the product list.** `ListPublicKbCategoryArticlesRequestHandler` and `ListPublicProductsRequestHandler` are as ruled; the repository method asks `PublishedIn` for the product's articles in a category of that slug and returns null when there is none, which the handler turns into the one `kb-category-not-found` 404. The Portal also treats a page past the end (a 200 with no items) as the neutral 404 on the category page and on the search page (a page after the first with no items but a total above zero), so there is one page per real page number and no empty page for a crawler to find; a search with no result at all stays the "No articles found" page. `ListPublicProductsRequestHandler` filters on `IsActive` twice on purpose (the repository's `activeOnly` and its own filter, as defence in depth) and applies the cap in memory. No migration was needed (`has-pending-model-changes` is clean).
- **As built in 09c: the pages never fail on a visitor's text.** The `page` and `q` query values are read as text. `KbPaging.Parse` turns anything that is not a whole number of one or more into page one (the framework's own binding to a number answered 500 for `?page=abc`), `KbSearchText.Clean` cuts a text at 200 characters without splitting a surrogate pair, and `PortalRoutes.KbSearch(key, text, page)` escapes the text in every paging link. A key or slug that is not a slug is refused by the client without a call (`KbSlugShape`, like `ProductKeyShape`).
- **As built in 09c: the cache.** `PortalCachePaths.IsCacheable` is the one predicate: a help-centre page whose `page` value is absent, or on a category page a number from two to 9999 (the home and an article ignore `page`, so any value there is not kept, and `?page=1` is page one under another key, so it is not kept either). The search page, the form pages, the suggest adapter, `/t/*`, `/not-found`, the sitemap and every non-200 answer are never kept; the search page also gets `Cache-Control: no-store` from a header rule, and a delivered KB page gets `public, max-age=60` from `PathHeaderRule.SetOnSuccess` (Hosting; the Admin's use of the shared wiring is unchanged and pinned by `PublicCacheHeaderPinTests`). `UsePortalOutputCache` also sets the request's own `X-Correlation-Id` again when the response starts, because a stored copy replays the first request's. `ProgramOrderTests` pins the order of the pipeline.
- **As built in 09c: structured data.** `JsonLdText` and the Portal's own `BreadcrumbListLd` and `ArticleSchema` records replace the package's `BreadcrumbListSchema` for the article page, because the package's strings cannot carry the converter. The hostile-title tests pin the head, the heading, the breadcrumb and both JSON-LD blocks. `KbPlainText.Describe` takes the description (the summary, else the first sentence of the body's first paragraph as plain text, at most 160 characters).
- **As built in 09c: the sitemap.** `PortalSitemapBuilder` makes one products call and one sitemap call per product, and fails whole when any call fails (a half sitemap kept for 15 minutes would be worse than none). `PortalSitemapCache` holds the entries for 15 minutes with single-flight, its own cancellation token, a 2-minute build timeout, a failure remembered for 1 minute and the last good sitemap served meanwhile; with none, the request is the 500 error page. The static root entry counts against the 50,000-address limit, so the whole file never exceeds it. The build runs without the request's execution context and with a stand-in `HttpContext` that carries only the first visitor's address.
- **As built in 09c: the second markup site.** `KbArticleBody` renders the API's sanitised HTML byte for byte (`KbArticleHostTests` compares it with the API's string); `PortalRules.MarkupStringSites` lists exactly it and `CustomerMessageBody`, and a test counts that the word appears in those two files and nowhere else.
- **As built in 09c: deviations from the plan, driven by the spike.** (1) The Portal has its own JSON-LD records (`BreadcrumbListLd`, `ArticleSchema`) instead of the package's `BreadcrumbListSchema`. (2) Their strings are `JsonLdText`, a value type with a converter that writes the strict-encoded text, rather than escaping the text before it reaches the package's records (that would be escaped twice by the serialiser). (3) The static `/` sitemap entry is conditional: listed only when no default product is configured, because with one `/` redirects to it. The plan said otherwise in each case; the spike proved these.
- **As built in 09c: the cache key and its case.** A category page keeps a `page` value from two up; the home and an article keep none, and `?page=1` is never kept (it is the page with no value). Only all-lowercase paths are kept: `PortalCachePaths.IsCacheable` is false for a path with an upper-case letter, so the cache neither stores nor looks it up. The output cache's key compares the path without regard to case (the framework's default), so without that rule `/p/ACME/kb` would be answered from a stored `/p/acme/kb` instead of the neutral 404; with it there is no exception: an unknown capitalised key or slug is always the byte-identical 404 (and a 404 is never stored anyway), and a capitalised fixed segment (`/p/acme/KB`) is a 200 that is never stored. `UseCaseSensitivePaths = true` was not used. `KbPageCacheHostTests` and `PortalCachePathsTests` pin it.
- **As built in 09c: the sitemap cache is stale-while-revalidate.** While a rebuild runs and an earlier sitemap exists, every request except the one that started the rebuild gets the earlier sitemap at once; there is still one build at a time. The flight's own exception is observed, so a build nobody waits for raises no `UnobservedTaskException`.
- **As built in 09c: layering and plain text.** `KbPaging` and `KbSearchText` live in `TechStrap.Portal.Kb`, not in the Components layer. `KbPlainText` patterns have a 1 s match timeout (a body that exceeds it has no description; it was 100 ms in the first build, which a busy runner could exceed on a small body, and 09d raised it). A `>` inside a quoted attribute value can leave stray words in the description; accepted as cosmetic, because the text is encoded and never markup and a real HTML parser (not a Portal package) would be needed.
- **Known in 09c: the sitemap build's calls carry the first crawler's address.** The build's `X-Forwarded-For` is the address of the visitor whose request started it, so each sitemap build makes 1 + N API calls under one forwarded IP, and the API's public limit is 120 per minute per IP: with about 120 or more active products the sitemap build is rate-limited and the sitemap goes stale or becomes unavailable. Accepted for the products this install has; a bulk sitemap endpoint, or exempting the Portal's own build traffic from the limit, is the possible later fix.
- **Known in 09c: a plain-http image in an article is blocked** by `img-src 'self' https: data:` in Production. That is the right posture and is documented, not changed.
- **Known in 09c: the category page shows no description** (the article list does not carry it, and a second call per page was not worth it), the product home still has no category list, and the visual polish, the no-JS and accessibility pass, the double-send guard and the JavaScript niceties of 09b's known gaps were PHASE-09d (done there; the category description and the product home's category list are still open).
- **Known in 09c: the package's `JsonLd` is still unsafe for any other caller.** The Portal avoids it for strings; the problem (a `</script>` in a value ends the block) is to be reported upstream to `SyntaxCircus.Blazor.Seo`.
- **Resolved in 09c: the product pages no longer link ahead.** The search box, the KB links on the contact and received pages and the sitemap named by robots.txt all answer.

### Addendum (2026-10-06, PHASE-09d portal polish)
The owner's rulings for PHASE-09d, and what the plan's spike proved. They extend D-045 and the 09b and 09c addenda; where they differ from the text above, they win. There is no new decision number.

**Rulings**
- **A double click sends once (P09-T09).** Each of the reply, contact and lost-link forms carries a fresh 128-bit random `SubmitId` (22 base64url characters from `RandomNumberGenerator`) in a hidden input right after `<AntiforgeryToken />`, rendered by the `FormGuard` component and made anew on every render (a form shown again after an error carries a new id, never the posted one). The handler claims the id with the process-local `SubmitGuard` only after validation passes and before the API call.
  - The guard has its own `MemoryCache` with a size cap of 10,000 claims (never the shared `IMemoryCache`, which holds the sitemap and has no size); a claim is made atomically under a lock and lives 6 minutes, which is longer than the guard's own 295-second deadline and shorter than the 10-minute reference it may hold. The key is a hash of the form name, the product key (or the ticket's access token), the id and a digest of the posted content, so an id cannot be used on another form, product or ticket, and the cache never holds a token or a word the visitor wrote.
  - A repeat of an id never writes. It waits for the first request's answer when that is still running, or reads the stored one, and redirects to the same place. The stored target is memory-only and never logged (the guard takes no logger); for a follow-up reply it holds the new ticket's token, which is accepted for the 6 minutes.
  - **Cancellation.** The claimed write runs on its own token with the guard's own deadline, the write client's 300-second timeout (`ApiClientRegistration.WriteTimeoutSeconds`, for 25 MB uploads) less 5 seconds, never on `RequestAborted`, because a real double click makes the browser abort the first POST while the API may already have the ticket. The first request waits for its own write without its abort token (bounded by that timeout), so what it is reading, the uploaded files, stays valid (a precaution; losing them was not reproduced). The guard's deadline is deliberately the shorter of the two, so it always fires first: a timeout is now always "unknown", the claim is held and a repeat goes to the safe fallback. (Equal deadlines made the outcome a race: the client's own timeout could win and release the claim as a failure, so a repeat would write a second ticket for an upload that then committed.) `SubmitGuardTests` pins the guard's deadline below the client's, the claim's life above the guard's, and the reference's life above the claim's.
  - **Unknown answers.** If the write times out or throws, nobody knows whether it happened. The claim is kept as unknown: a repeat is sent to the safe fallback and never sends again (a reply: the same ticket page; contact: the received page with no reference, which shows its generic confirmation; lost link: the sent page, which is a constant). The first request shows its own calm notice with a 503: "We could not confirm it was sent. Check your email before sending again." (`FormCopy.Unknown`; "try again" would be wrong when the message may have arrived).
  - **Failures.** An API failure (429, 409, 503 or any other, a 5xx or a transport error too) releases the claim, so a retry sends; a repeat that was already waiting gets the same failure and does not write. Accepted: a 5xx after which the API had in fact applied the write can still lead to a duplicate on a deliberate retry (holding the claim would instead show a false "received" on a refresh).
  - **Not guarded.** A missing or malformed id means the post goes through as it always did (old pages and the test kits keep working); when the cache is full a new post is not guarded, never refused.
  - **A resend after the back button (owner ruling, final review).** A form brought back by the back button could carry its sent id and text, and within 6 minutes an edited resend would have been silently answered with the first message's page. Two changes, by the owner's ruling: the key also hashes a digest of what was posted (`SubmitContent.Digest`: the trimmed text fields, and each attachment's file name and size in order), so a re-post of the same id with edited text or another attachment is a new message (only a hash is kept, never the text); and `portal-forms.js` writes a fresh 22-character id (from `crypto.getRandomValues`, the shape `SubmitIds.IsWellFormed` accepts) into every `input[name$=".SubmitId"]` on a `pageshow` that is `persisted`. The `FormGuard` input is `autocomplete="off"` so the browser does not restore an old id either. Net effect, confirmed in Edge (two ticket POSTs): after Back the visitor always has a fresh id (the page is fetched again with a server-rendered id, or the cache copy gets a new one from the script), so any resend after Back, edited or identical, is a new message; the guard covers a double click and a refresh that re-posts the same id. The owner's checklist has the step.
  - **Known limits.** The guard is per instance and is lost on restart, like the sitemap cache; the API still has no idempotency key for `/api/customer` or the public ticket route.
- **One small script, `portal-forms.js` (loaded once from the document shell).** Vanilla JavaScript with two custom elements and two document listeners: the "Sending..." state (the submit button is disabled and shows the form's own `data-sending-label`; it comes back on `pageshow` after the back button and after a minute, and a fresh one-time id is written into every form on a `pageshow` from the cache), `<ts-copy-text>` (a copy button for the ticket number that selects the number when the clipboard is refused) and `<ts-char-count>` (a counter on a long text field that appears at 80 percent of the limit and counts a line break as two characters, because the browser posts CR LF while `maxlength` counts one). Every word comes from a `*Copy` constant through a data attribute, text is put on the page with `textContent` only, there is no inline script and no `on*` attribute, and the CSP does not change.
- **T16 styling, accessibility and the no-JS pass.**
  - **Widths.** One token for the reading column (`--ts-reading-width`, 40rem) and one for the wide container (`--ts-wide-width`, 64rem; the header, the footer, search, the categories and the product home); one column below 768px, category cards two across from 768px and three from 1200px.
  - **Tokens.** BRAND.md gains six Portal tokens: `--p-error`, `--p-success` and `--p-warn` (the same red, green and amber as the Admin's `--st-spam`, `--st-open` and `--st-pending`) and their grounds `--p-error-bg`, `--p-success-bg` and `--p-warn-bg`. The old `var(--p-error, #b3261e)` pointed at a token that did not exist. A control's edge is `--p-ink2` (3:1), never the decorative `--p-line`.
  - **Landmarks and headings.** A skip link and `main id="main" tabindex="-1"`; a named `nav` in the product header (the help centre and contact) and one in the footer, and one contentinfo (the "Powered by" footer); one `h1` per page, the search page's being "Search the help centre"; an `h1` inside an article or a message body is shown as an `h2` (`BodyHeadings`, the only change the Portal makes to those bodies).
  - **Targets and states.** Every link a visitor has to hit is at least 44px tall; required is said in words above each form; forced colours keep the 3px focus outline (the accent halo is dropped) and what colour alone would say; reduced motion switches every animation and transition off, and smooth scrolling is off in the build.
  - **The `<base href="/">` bug.** A bare `#field` link in the error summary resolved to `/#field`, the home page. The links are written as the current root-relative path plus the fragment by `PageLinks.ToFragment`, and carry `data-enhance-nav="false"` (see the spike). The skip link and the "jump to your reply" link use the same helper.
  - **Search box.** The product home's duplicate search box is replaced by `KbSearchBox`.
  - **Tests.** `ResponsiveStyleTests`, `TokenContrastTests` and `CspStyleTests` in the Admin's pattern, `HeadingHostTests`, `LayoutLandmarkHostTests` and `ErrorSummaryLinkHostTests` (which resolve each link against the document's base, as a browser does).
  - **Manual T16 evidence** (the axe run, Lighthouse accessibility of at least 90, the JavaScript-off walk, screenshots at 360, 768 and 1280, and the T18 smoke) is the owner's checklist in PORTAL-APP.md, as in ADMIN-APP.md, and is recorded in the pull request.
- **Test hardening.** `tests/Shared/StartupFailure.cs` is generic over `WebApplicationFactory<TEntry>` and takes the log events of the factory's sink; the Portal, Admin and Api tests all use it, so a host that must refuse to start on options validation is read from the log when `CreateClient()` loses its race with the host's disposal, and a start that does not fail on validation still fails the test. The twelve start-failure sites (three Admin, seven Api, two Portal) are converted. `Seen` compares every response header except the per-request ones.
- **Docs.** The wording of the 09b note about a post to an unknown product is corrected (above), PORTAL-APP.md, the PHASE-09 ticks, the roadmap and discovery rows and the as-built notes below.

**Spike findings (proven in a scratch copy, in Edge headless and in the test host, before the plan was written)**
- **Two concurrent posts with one id make one API call and both redirect to the same place.** Eight concurrent posts did too (`SubmitGuardTests`, `DoubleSendHostTests`).
- **An aborted first post.** A client that cancels its call does not get an answer from the test server until the server's handler has finished, so the first request must not stop waiting for its write. The write's own token is not cancelled when the browser goes away (`CancelledWhenAnswered` is false), it completes, and a repeat that arrives meanwhile, or after, gets the same redirect. A write that times out is unknown, and the repeat goes to the fallback.
- **Enhanced navigation does not run a script that arrives with swapped content.** `kb-suggestions.js` was loaded by the contact page's own body (09b), so for a visitor who reached the contact page by clicking a link (enhanced navigation) `customElements.get('ts-kb-suggestions')` was undefined and the suggestions never worked. A module in the document shell (`App.razor`) runs once, and the browser then calls `connectedCallback` for every element the swap inserts (the count rose on each navigation), so both modules are loaded there and the contact page no longer carries a script. The swap also rewrites the attributes of an element it keeps, which wiped an attribute set from script, so the module keeps its state in a `WeakMap`. A listener on the document survives navigations.
- **The `<base href="/">` bug is real, and removing the tag is not safe.** In Edge a click on a summary link went to the home page (the page then showed "Support"). The stylesheet, the favicon and `blazor.web.js` are written relative to the base, and Blazor reads the base for navigation, so the tag stays. A root-relative path plus the fragment is a jump within the page. Blazor still takes the click (`defaultPrevented` is true), scrolls and leaves the focus on the link; with `data-enhance-nav="false"` the browser moves the focus to the field (`INPUT#email`, with a real mouse click and with Enter). The skip link works the same way and keeps the prefill.
- **`StartupFailure` works through every factory.** The Admin factory gains a `CollectingSink`, the Api, Worker and Portal factories had one, and each puts its failed start on the sink as a "Hosting failed to start" event that carries the `OptionsValidationException` (`StartFailureLogTests`). A failure thrown eagerly while the host is built (the Portal's sitemap mapping reads an option) throws the validation exception directly and logs nothing, so it never races.

**Deviations from the brief, each with its reason**
- `Seen` also ignores the antiforgery `Set-Cookie` (any other cookie is compared by name) and ignores `Pragma` only when the antiforgery cookie is in play, set by the response or sent with the request: widening it to every header failed two tests on `Pragma: no-cache`, which the framework's antiforgery step adds to a response that rendered a form (a post to a product that vanished after its form was served, where the cookie was already in the browser and is not set again). A visitor holding that form already knew the product, so nothing leaks, and a stray `Pragma` on any other response is still a difference.
- The skip link is rendered only on a product page (with the product header it skips). It is built from the address, and the neutral 404 must stay byte for byte the same for every address.
- The skip link keeps only the query parameters the page itself reads (`PageLinks.KnownParameters`), in the address bar's own raw spelling: keeping all of them echoed the extra parameters of the contact page into its markup, and the output cache would have shown the first visitor's to the next.
- An `h1` in an article or a message body becomes an `h2` (`BodyHeadings`), so "the Portal does not change a byte of the API's HTML" is now "except that".
- `kb-suggestions.js` is fixed (the brief said only if it breaks): the spike showed it does.
- Required is said in one sentence above each form, not in each label, so the pinned label markup does not change.
- The product home still has no category list (the header's help-centre link is the way in).

**Consequences of the addendum (as built in 09d)**
- **As built in 09d: the double-send guard.** `SubmitIds` (the id), `SubmitKey` (the hash of the form, the product key or ticket token, the id and the content digest of `SubmitContent`), `SubmitGuard` (the cache, the lock, the claims) and `FormGuard` (the hidden input) are in `Forms/` and `Components/Ui/`; the three pages call `Guard.RunAsync(key, fallback, write, requestAborted)`, where `write` is the API call mapped to a redirect target (`Result<SubmitTarget>`). A claim's `Lifetime` (6 minutes) is longer than the guard's deadline (`SubmitGuard.WriteTimeout` is `ApiClientRegistration.WriteTimeoutSeconds` less 5 seconds, so it is pinned below the write client's timeout and a timeout is always unknown) and pinned below `ReceivedReference.Lifetime`; `SubmitGuardTests` pins every rule above with a gate that holds the write, and `DoubleSendHostTests` pins the three forms, including the aborted first post, the unknown answer, the failure that releases the claim, the id that cannot cross forms, products or tickets, and a log scan at every level. A 5xx or a transport failure releases the claim (the accepted trade-off, recorded in the rulings above).
- **As built in 09d: the form helpers.** `portal-forms.js` and its two custom elements; both modules are loaded from `App.razor`; the contact page no longer carries a script, which also fixes the suggestions for a visitor who arrived by an enhanced navigation (a 09b defect found by the spike). The element names and the data attributes are pinned by `PortalFormsHostTests`.
- **As built in 09d: links to the current page.** `PageLinks.ToFragment` is the only way the Portal links to a place on the current page, and it keeps the address bar's own raw query spelling for the parameters the page reads (so a skip link stays a same-document jump); `PageKit.Resolve` in the tests resolves a link against the base as a browser does, and a control test proves that the old bare fragment would have been caught. The ticket page's links to itself contain the access token in the path (`href="/t/{token}#main"`), the same address the visitor is on; `TicketPageHostTests` pins exactly four places the token appears.
- **As built in 09d: styles.** `_layout.scss` and `_a11y.scss` after `_components.scss`; six new Portal tokens in BRAND.md and `_brand-tokens.scss` (the Admin's style guide palette lists them and its token-count test is 11); `$enable-smooth-scroll: false`. `ResponsiveStyleTests` scans every `.razor`, `.cs` and `.js` file of the Portal for `ts-*` classes and fails on one with no rule.
- **As built in 09d: test hardening.** `tests/Shared/StartupFailure.cs` and `tests/Shared/CollectingSink.cs` are linked by the Admin, Api and Portal test projects (the Portal's own copies are gone); `AdminFactory` gains a `LogSink`; the twelve sites keep their original assertions about the key or the message, and the ones that asserted on `ToString()` now assert on the captured `OptionsValidationException`'s message. Not converted, because their failure does not come from `ValidateOnStart` or does not race: the four `ConfigHostSupport` hosts, `ApiPublicUrlTests`, `TrustedProxyStartupTests` and the Infrastructure tests (they resolve `IOptions<T>` and do not start a host). `Seen` compares every header except `Date`, `X-Correlation-Id`, `Content-Length` and `Transfer-Encoding`, and of `Set-Cookie` ignores only the antiforgery cookie (`.AspNetCore.Antiforgery.*`), comparing any other cookie by name, and ignores `Pragma` only when that cookie is in play; `SeenTests` pin all three halves.
- **As built in 09d (final fix wave): same-page enhanced navigation.** Blazor's DOM diff keeps `ts-char-count`, `ts-copy-text` and `ts-kb-suggestions` but strips the children their scripts rendered (and puts the no-script fallback link back into `ts-kb-suggestions`), and `connectedCallback` does not run again. Each element now watches its own children with a `MutationObserver` and rebuilds them when they are gone (the counter also when its textarea was replaced); `data-permanent` was rejected because it would keep the previous page's `for`, `src` and limits through a navigation to another product's page; `ts-kb-suggestions` also watches its `src` and `field` attributes and, when either changes (or its field element was replaced), disconnects and connects again, so it follows a new product instead of using the old suggest path. The node tests simulate the stripping; `PortalFormsHostTests` pins that the elements are not `data-permanent` and that the modules carry the observer; in Edge headless, after `Blazor.navigateTo` to the same address, the counter, the suggestions and the copy button work in the same document.
- **As built in 09d (final fix wave): cache rule and the small things.** A category page is kept only when its raw `Request.QueryString` is empty or literally `?page=` and 2 to 9999 (so `?PAGE=2`, `?pa%67e=2`, `?page=%32` and any extra parameter are answered but never stored; the home and an article still ignore other parameters); a cached category page would otherwise bake one visitor's query spelling into the skip link and the pager for the next. A submit that was already cancelled does not disable the button; the "the source" pin also catches `.onX =`. The unknown-outcome notice has its own sentence.
- **Known in 09d: an address with unknown parameters** gets a skip link that reloads to the cleaned address (it keeps only the parameters the page reads); focus still lands on the main content.
- **Known in 09d: the guard is per instance** and is lost on a restart; with two Portal replicas a double click that reaches both is not caught.
- **Known in 09d: the owner's evidence for P09-T16 is open**: the axe run, the Lighthouse score, the JavaScript-off walk and the screenshots (PORTAL-APP.md, "Manual checks"), and the owner's compose run of the smoke closes P09-T18. P09-T20 stays deferred.
- **Known in 09d: the sitemap build's 1 + N API calls** still count against the API's 120 per minute per IP; that is the 09c note above, unchanged by 09d.
- **Known in 09d: an `h1` in a body is shown as an `h2`**, so an author's top heading is not the biggest heading on the page; an editor who wants a real section starts at `##`.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-09 planning)
- **Approved on:** 2026-10-05

## D-046: PHASE-10: live updates (two pull requests, identity in the request, a two-part post-commit hook, header-only hub token, presence names, the detail banner)

- **Status:** Approved (owner 2026-10-07; technical rulings at PHASE-10a plan review)
- **Date:** 2026-10-07
- **Owner:** Jon Seeley
- **Related artifacts:** D-007, D-016, D-018, D-040, D-043, D-045, `docs/architecture/PHASE-10-live-updates.md`, `docs/superpowers/plans/2026-10-07-phase-10a-live-server.md`

### Context
PHASE-09 is merged, so PHASE-10 can start. Reading the code before planning found these gaps between the spec and what exists:
- **`ICurrentUserService` does not exist.** The Application abstraction is `ICurrentAgentClaims`, backed by `IHttpContextAccessor`, which is not reliable inside a hub.
- **`LiveConnectionState` cannot live in Contracts.** `ContractNamingRules` allows only `*Dto`, `*Request`, `*Response` and static constant classes there, and Contracts carries no enums.
- **A SaveChanges interceptor alone is not post-commit.** `UnitOfWork` commits the transaction after SaveChanges, so SaveChanges' own "saved" event fires before the commit.
- **`DrainEmailOutboxHandler` writes no `TicketEvent`.** Only `AutoCloseSolvedTicketsHandler` publishes from the Worker.
- **Browsers never reach `/hubs`.** The Admin connects server to server (`API__BASEURL: http://api/`), so the reverse proxy needs nothing new for the hub.
- **The hub token needs no query string.** The .NET client sends `Authorization: Bearer` on every transport; the query form is a browser habit and a leak surface.
- **`TicketChangedDto` has no event id** for the client's de-duplication.
- **No custom meter exists**, and `AddSyntaxCircusObservability` exports a custom meter only when its name is passed to it.
- **The spec and 02-ARCHITECTURE disagree on a join for an unknown ticket** (`HubException` or an aborted connection).
- **Presence names and the detail page's behaviour when another agent changes the ticket** were open.

### Decision
**Owner decisions (2026-10-07)**
- **Delivery.** Two pull requests: **10a**, the server (T01 to T10), and **10b**, the Admin (T11 to T17), which starts only after a package prerequisite.
- **Detail page on another agent's change (10b).** A "New activity - refresh" banner. When the agent clicks it the page reloads the timeline and status and takes the new row version. The draft is never touched. Until the click a send still gets the existing 409, so an agent never acts on an unseen version.
- **Presence name (10b).** The agent's own `Agent.Name` (the internal name; the email when there is none), shown to other agents only. The customer-facing `PublicDisplayName` is never used.
- **Hub token source (10b).** `SyntaxCircus.Blazor.Auth` (sibling repository) gains a public token-provider API and is published as a new package version before 10b starts.

**Technical rulings (proposed in the 10a plan; approved when the owner approves it)**
- **Identity travels in the request.** The hub builds an `UpdateTicketPresenceRequest` (action, agent subject, connection id, ticket id, composing flag) from `Context.User` and `Context.ConnectionId`; handlers never read claims or the HTTP context. The Agent policy is on the route and on the hub class.
- **The post-commit hook has two parts.** `TicketChangeCaptureInterceptor` (a `SaveChangesInterceptor`) notes the `TicketEvent` rows being inserted and stages them per context; `TicketChangePublishingInterceptor` (a `DbTransactionInterceptor`) publishes after `TransactionCommitted` and drops the staging on a rollback or failure. A save with no explicit transaction publishes from its own "saved" event. One change per ticket per commit; a deleted ticket, or an event for a ticket the context does not track, becomes one `Resync`. A broadcaster failure or a hang (5 seconds) is logged by exception type and never fails the request. `TechStrapDatabase.Configure` keeps its two-argument shape; the interceptors are added where the container is, in `AddTechStrapPersistence`, and `NullTicketChangeBroadcaster` is the default until the Api or the Worker replaces it.
- **Header token only.** An `access_token` query value is not read on `/hubs` (the JWT bearer default); tests pin the refusal and that no token reaches a log at any level. `CloseOnAuthenticationExpiration` closes a connection when its token expires, and a deactivated agent is refused at the handshake by the Agent policy.
- **Groups.** `TicketChanged` goes to group `queue` only and clients filter by ticket id; presence goes to `ticket:{id}`. `JoinTicket` checks the ticket exists through `ITicketRepository.GetStateAsync` and answers an unknown id with `HubException("Ticket not found")` before any group is joined. `JoinTicket` returns the current presence to its caller, so a second tab of the same agent (for whom nothing changed) still learns the state.
- **Presence.** In memory, one lock, one entry per agent however many tabs; Composing wins. A composing lease lasts 10 seconds. "Changed" means what other agents would see differs or a lease was extended: a refresh is the heartbeat the client's clear-after-TTL depends on, so it is sent; a repeat that moves nothing is not.
- **The relay.** The Worker's `PgNotifyTicketChangeBroadcaster` sends `pg_notify('techstrap_ticket_changes', json)` after the commit; the Api's `TicketChangeListener` holds one dedicated, unpooled, named connection with a keepalive, hands each payload to `RelayTicketChangeHandler` (validate, 2,048-byte cap, forward; bad input is a failure Result, never an exception), reconnects with a 1, 2, 4, up to 30 second backoff and relays one `Resync` after each reconnect. The listener never notifies, so nothing echoes.
- **Metrics.** `TechStrapMetrics` (meter `TechStrap`): `techstrap.live.connected_agents` (distinct agents), `techstrap.live.changes_relayed`, `techstrap.live.relay_failures`, `techstrap.live.listener_reconnects`. The Api and the Worker pass the meter name to `AddSyntaxCircusObservability`. Handlers cannot take a meter, so the hub and the listener record.
- **No new setting, no migration.** Every number is a constant. `Microsoft.AspNetCore.SignalR.Client` is added to `TechStrap.Api.Tests` only (already pinned); `Npgsql` becomes a direct reference of Infrastructure.

### Alternatives Considered
- **Accept `access_token` on `/hubs`.** Rejected: the header works on every transport the Admin uses (proved over real WebSockets), and a query token would reach logs, spans and proxies.
- **Publish from SaveChanges' "saved" event.** Rejected: it fires before the commit, so a rolled-back conflict could announce a change that never happened.
- **A `Removed` change kind for a deleted ticket.** Rejected for now: a `Resync` already makes every queue reload, and delete is an Admin-only rare operation.
- **Server-side expiry of a composing hint.** Rejected: it needs a timer and a handler action; the lease plus the heartbeat gives the same result.
- **Reading the identity through `IHttpContextAccessor` in the hub.** Rejected: it is not reliable there, and the request record keeps handlers transport-free.

### Consequences
- **Spec corrections.** `PHASE-10-live-updates.md` and `02-ARCHITECTURE.md` carry the corrections above; `LiveConnectionState` is an Admin type (10b).
- **A deleted ticket publishes a `Resync`** (queues reload; an open detail page shows "gone" on its next load).
- **Single Api instance.** Presence and the in-process publish do not span instances (D-007). The listener needs a direct Postgres connection: PgBouncer in transaction mode breaks `LISTEN` (DEPLOYMENT.md).
- **As built in 10a: the proof.** The hub is exercised through a real `HubConnection` on the in-memory server (long polling) and, for the header token, over real WebSockets on a loopback Kestrel port; the hook, the broadcaster and the listener run against Postgres (Testcontainers), including `pg_terminate_backend` of the listener's backend, which ends in a reconnect and one `Resync`.
- **As built in 10a: the Admin is not changed.** 10b adds the client, the indicator, the banners and the presence bar; the hub contract (Contracts `Live`) is what it builds on.
- **Known limits (10a).**
  - A deactivated agent's already-open socket keeps receiving `TicketChanged` (ids and numbers) and the presence of tickets it joined until its access token expires; the handshake, `JoinTicket`, `SetComposing` and REST all refuse it.
  - The hub is not rate-limited, like the agent REST endpoints; SignalR runs one invocation at a time per connection.
  - The erasure `ExecuteUpdate`/`ExecuteDelete` bypasses the change tracker, so it publishes nothing; an open detail page shows the old text until its next load.
  - The Worker's `connected_agents` gauge is always 0 (only the Api has a hub).
  - `TechStrapMetrics` creates its meter with `new Meter`, not `IMeterFactory`, so it is process-wide (the next bullet is the consequence); moving to `IMeterFactory` is a 10b-or-later change.
- **Known: the first 10a host test of a meter needs an async disposal.** A meter provider subscribes to every meter of that name in the process, so a host that is only disposed synchronously keeps observing the next one's instruments; the registration test disposes each host before it starts the next.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-10 planning)
- **Approved on:** 2026-10-07

### Addendum (2026-10-07, PHASE-10b live updates in the Admin)
The owner's rulings for PHASE-10b, and what the plan's spikes proved. They extend D-046; where they differ from the text above, they win. There is no new decision number, as for the PHASE-09c and 09d addenda of D-045.

**Rulings**
- **One kill switch.** `LiveUpdates:Enabled` (`LIVEUPDATES__ENABLED`, default `true`) is the one new setting, so "No new setting" above holds for the Api and the Worker only. Off, `NullTicketLiveClient` is registered: no connection, no indicator, no banner, no presence bar, and the pages behave exactly as before. The hub address is `API__BASEURL` plus `TicketHubRoutes.Path`; backoff, debounce and throttle are constants. The four edits of D-043 are `appsettings.json`, the host `.env.example`, `deploy/.env.admin.example` and `scripts/tests/ConfigContract.Tests.ps1`; compose is not edited because the switch is an operator setting (an `environment:` entry would override the operator's env file), and a new pin asserts that.
- **T17.** An automated in-process integration test (`AdminLiveClientHostTests`, in `TechStrap.Api.Tests`) plus a short manual real-identity-provider checklist in ADMIN-APP.md. The compose smoke script is unchanged.
- **Own changes are ignored.** A `TicketChanged` whose `ActorAgentId` is the signed-in agent raises no banner (a `Resync`, and a change with no actor, never count as own). Another tab of the same agent still meets the 409 on a send.
- **The queue banner.** One "Queue updated – refresh" banner for any other agent's change or a `Resync`, after a one-second window. Nothing is reordered or reloaded until the agent clicks; the click is the existing load with the current filters.
- **Carried from D-046.** The detail banner does not take the new row version until it is clicked, so a send before that gets the 409; the draft is never touched; the presence name is `Agent.Name`, shown to other agents only, and the agent's own entry is removed by `AgentSession.Agent.Id`.

**Spike findings (proved in a scratch clone before the plan was written)**
- **The start.** Pages are prerendered (`InteractiveServer` with prerendering): the first render runs on the HTTP request with no circuit, and `OnAfterRenderAsync` does not run there. `LiveConnectionIndicator` in `MainLayout` is the one starter: it asks only from `OnAfterRender`, only when `AgentSession` is Ready (the layout is outside `AgentGate`, so it waits for `Session.Changed`), and the client's own guard (`_starting ??= Task.Run(RunAsync)`, on the pool so the start loop never runs on the renderer's context) keeps a scope to one connection however often it is asked. A host test with the real client and a counting connection factory proves a prerender of `/` and `/queue/mine` creates none.
- **The token.** `IUserAccessTokenProvider` (0.2.0, scoped, `ValueTask<string?> GetAccessTokenAsync(CancellationToken)`) goes into the hub's `AccessTokenProvider`, so SignalR asks for a token on every start and every reconnect: a reconnect gets a fresh one (proved against the real hub: a token that expires after four seconds is closed by the server, and the second token is used). A null token stops the client for good (a hub request without a token is refused, so every retry would be); it does not sign the agent out: the Admin's own session and the pages carry on, only the live connection stays "Offline"; a throw is logged by type and retried. The hub connection does not use the named API `HttpClient`s (they carry the retry and the auth handler); `HubLiveConnectionFactory` is a singleton that holds the validated `Api` options only and has a test seam for the message handler and the transport.
- **The seam.** `ITicketLiveClient` is what components see; `ILiveConnection` hides `HubConnection` from the client's own logic, so the unit tests script the reconnect, the duplicates and the disposal. A `FakeTicketLiveClient` is registered once in `AdminComponentTest`; one test class that derives `BunitContext` directly (`LayoutResilienceTests`) needed its own registration.
- **T17.** `TechStrap.Api.Tests` references the Api, the Worker and the Admin, and the Architecture rules constrain the source projects only, so it is the one place the whole chain can run: Testcontainers Postgres, the Worker's real `AutoCloseSolvedTicketsHandler` and NOTIFY publisher, the Api's real listener and hub on a `TestServer`, and the Admin's real `SignalRTicketLiveClient` with a fixed test token over long polling.

**Consequences of the addendum (as built in 10b)**
- **As built in 10b: the client.** `SignalRTicketLiveClient` is public, scoped and `IAsyncDisposable`; `LiveRetryPolicy` never gives up (0, 2, 5, 10, then 30 seconds) and stops for a lapsed `SessionExpiry` or a missing token; after a reconnect it joins its tickets again and raises a synthetic `Resync`; it remembers the last 256 event ids; every failure is logged by exception type and shows as a state, never as an exception in a page.
- **As built in 10b: the pages.** The queue's banner is a one-second window that the first change opens (a window that restarted on every change would never fire in a steady trickle). The detail page joins after each load, leaves on navigation and disposal, keeps the banner when a change arrives while its reload runs, and shows a "replying" hint as "viewing" when the 10-second lease passes with no news (the hub does not push the lapse). The composer sends its hint from the `Text` setter at most every four seconds and clears it on blur, send, empty text, another ticket and disposal.
- **As built in 10b: packages.** `SyntaxCircus.Blazor.Auth` 0.2.0; `Microsoft.AspNetCore.SignalR.Client` is now also an Admin package (`AdminRules.AllowedPackages`); nothing else is new and there is no migration.
- **Known limits (10b).** See "Known gaps in 10b" in ADMIN-APP.md: one connection per tab, both banners after any reconnect, no reconnect jitter, a hub that ends because the session ended, or that refuses the connection for good, leaves "Offline" until the page is reloaded, and the failed-emails badge is not driven by the hub. The D-046 limits (a deactivated agent's open socket, erasure publishing nothing, one Api instance) stand.

## D-047: PHASE-11: client SDK (three pull requests, JSON-only v1, web-neutral Common, retry only with an Idempotency-Key through the pipeline)

- **Status:** Approved (owner 2026-10-07; technical rulings at PHASE-11a plan review)
- **Date:** 2026-10-07
- **Owner:** Jon Seeley
- **Related artifacts:** D-001, D-005, D-016, D-020, D-034, D-045, `docs/architecture/PHASE-11-client-sdk.md`, `docs/superpowers/plans/2026-10-07-phase-11a-client-sdk.md`, `docs/development/CLIENT-SDK.md`

### Context
PHASE-05 is merged, so PHASE-11 can start. Reading the code and the packages before planning found these gaps between the spec and what exists:
- **`SyntaxCircus.Common` 0.1.3 carries a `Microsoft.AspNetCore.App` framework reference.** It is there for `ICurrentUserService` over `IHttpContextAccessor`. An SDK consumer cannot take it, and a MAUI app above all cannot, yet the SDK returns `Result<T>` from Common.
- **The package map said the SDK uses a plain `HttpClient`.** Submit is not naturally idempotent, so the SDK needs retries that are safe per request, which a plain client does not give.
- **`AddResilientHttpClient` retries every request, POST included,** on 408, 429, 500, 502, 503 and 504. For a ticket create that means duplicate tickets.
- **The intake endpoint is JSON-only.** `IntakeController` binds `[FromBody] SubmitTicketRequest` (D-034), so the spec's `TicketAttachment` and multipart submit have nothing to talk to.
- **The spec's constants do not match the code.** The header names are `HeaderNames` and the limits `IntakeLimits` in Contracts, not `TechStrapHeaders` and `TicketMetadataLimits`; the route was a literal in the controller; `TicketMetadataKeys` does not exist.
- **Contracts is not packable yet** and has no pack metadata, README or guard against the wire literals being copied elsewhere.
- **`ResultError` has no slot for `Retry-After`,** although the server sends the header on a 429.
- **The spec's `SubmitOptions` and `retryCount` were not adopted,** and the package's `ApiClientBase` and `AddResilientHttpClient` were not used: the pipeline's own options are `MaxAttempts` and a per-call replay flag.

### Decision
**Owner decisions (2026-10-07)**
- **Delivery.** Three pull requests. **11a**: `TechStrap.Client`, the packaging of Contracts and Client, the real-API tests and the pack dry run in CI (P11-T01, T02, T03, T04, T06, T10, T17). **11b**: `TechStrap.Client.Maui` (T07 to T09). **11c**: READMEs, samples, `publish-nuget.yml`, package validation, nuget.org and `v1.0.0-rc.1` (T11 to T16). 11b and 11c wait on owner actions #10 (macOS) and #9 (nuget.org).
- **Attachments.** The SDK v1 is JSON-only (D-034): no `TicketAttachment`, no multipart. P11-T05 and the MAUI screenshot adapter are deferred to a later 11d that first adds multipart intake in a PHASE-05 amendment.
- **Dependencies and the Common split.** The SDK depends on `SyntaxCircus.Http.Resilience` and `SyntaxCircus.Common`. Common is fixed first instead of writing an own result type: it became web-neutral in 0.2.0 (no `Microsoft.AspNetCore.App` framework reference) and `ICurrentUserService` moved to the existing `SyntaxCircus.AspNetCore.Common` 0.1.16. Both were released before 11a (SyntaxCircus.Common#5, SyntaxCircus.AspNetCore.Common#17) and are pinned here.
- **MAUI CI (11b).** `net10.0` and Android on the existing runner; iOS only on a `v*` tag on macOS.

**Technical rulings (proposed in the 11a plan; approved when the owner approves it)**
- **The pipeline, not `AddResilientHttpClient`.** The SDK owns one `HttpRequestResiliencePipeline("techstrap-submit")` and passes a replay flag on every send. A submit with an `Idempotency-Key` is `Replayable`: a fresh request and the identical key on every attempt. `SubmitTicketOnceAsync` sends no key and is `NotReplayable`: exactly one send. Retryable: transport errors, timeouts and 408, 502, 503, 504. A 500 is the API's own answer and is not retried. A 429 is not retried either: the pipeline cannot honour or cap `Retry-After` per response, and its status set also drives the circuit.
- **Three methods, not a `SubmitOptions` type.** `SubmitTicketAsync(request, ct)` (the SDK generates a key as a GUID in "N" format), `SubmitTicketAsync(request, idempotencyKey, ct)` (the caller's stable key, which must be non-blank, visible ASCII and at most `IntakeLimits.MaxIdempotencyKeyLength`, else `ArgumentException`) and `SubmitTicketOnceAsync(request, ct)`. A caller that may retry a failed submit itself must supply its own key: the generated key is not returned, so a second call with no key can create a duplicate. `api-unavailable` means the ticket may or may not have been created; `rate-limited` is retried later with the same key. No default interface methods: adding a member to `ITechStrapClient` before 1.0 breaks implementers (fakes). `MaxAttempts` (attempts in total, 1 to 10, default 3) replaces the spec's `retryCount`.
- **Options.** `TechStrapClientOptions` is a sealed class (a record's `ToString` would print the key): `BaseAddress`, `ApiKey`, `Timeout` 30 s (the total budget, retries and delays included), `MaxAttempts`, `RetryBaseDelay` 500 ms, `MaxRetryDelay` 5 s. A validator rejects a base address that is not absolute http/https, has userinfo, query or fragment, or is plain http for a non-loopback host; a blank or header-unsafe key; a timeout over 10 minutes; and bad delay order. It fails with `OptionsValidationException` on first use (no `ValidateOnStart`: MAUI has no generic host) and its messages never contain the key. `AddTechStrapClient(IConfiguration)` fails at registration, naming the key, when `BaseAddress` is not an absolute http or https URL or `Timeout`, `MaxAttempts`, `RetryBaseDelay` or `MaxRetryDelay` does not parse.
- **The key never leaves the header.** `ApiKeyHandler` sets `X-Api-Key` (replacing a caller value) and refuses a request whose authority differs from `BaseAddress`. The named client has no logging handlers, its primary handler is `SocketsHttpHandler { AllowAutoRedirect = false }` (a redirect would replay the key to another host), and the response buffer is capped at 1 MiB. The client does no logging.
- **Host-wide HttpClient defaults do not reach the SDK's client.** A handler a host adds with `ConfigureHttpClientDefaults` (the Aspire ServiceDefaults template adds a standard resilience handler) would sit outside `ApiKeyHandler`, retry a call that has no `Idempotency-Key` and could see the key header. `AddTechStrapClient` therefore removes every handler that precedes `ApiKeyHandler` in the named client's chain; a handler of the host's own is registered on `TechStrapClientDefaults.HttpClientName` after `AddTechStrapClient`, where it is kept. A `BaseAddress` with a path gets a trailing slash so `api/intake/tickets` resolves under it.
- **Result mapping mirrors the Portal's.** 201 with a readable body is success; a 2xx with no usable body is `api-unexpected-response`; 400 and 422 map per field from `errorCodes` and `errors`; 401 and 403 are `invalid-api-key`; 413, 415 and 429 have their own codes; 408 and every status from 500 up are `api-unavailable`, as are transport errors, a timeout, an open circuit and a cancellation that is not the caller's; anything else is `api-error`. Caller cancellation always propagates as `OperationCanceledException`; programmer errors throw. Server text surfaces only for a 400 or 422. The Portal's parsing is copied into the Client because it is `internal` to a host app and Contracts is dependency-free.
- **Error codes live in the Client.** `TechStrapClientErrorCodes` holds the codes the SDK produces; they never cross the wire, so they are not Contracts constants (the Portal and Admin keep local `ApiErrorCodes` for the same reason). Wire codes from a 400 pass through unchanged.
- **The wire is named once.** The spec's `TechStrapHeaders` is `HeaderNames` and its `TicketMetadataLimits` is `IntakeLimits`. `IntakeRoutes.Tickets` is new and the controller uses it. An architecture rule (`WireLiteralRules`) allows the quoted literals `"X-Api-Key"`, `"X-Ticket-Token"`, `"Idempotency-Key"`, `"api/intake/tickets"` and its leading-slash form `"/api/intake/tickets"` in `src/` only in `HeaderNames.cs`, `IntakeRoutes.cs` and the named exemption `SensitiveHeaderSentryProcessor.cs`. `TicketMetadataKeys` belongs to 11b.
- **Packaging in 11a is the minimum.** `eng/Packaging.props` is imported by Contracts and Client only; the root `Directory.Build.props` sets `IsPackable=false`. The props file may hold no runtime `PackageReference`, and a test fails if one is not `PrivateAssets=all`. `GitVersion.MsBuild`, `GenerateDocumentationFile` and a SourceLink package are left to 11c: GitVersion in Contracts would run under every image build, and CS1591 under `TreatWarningsAsErrors` would fail every undocumented Contracts member. CI packs both projects with `-p:Version=0.0.0-ci` and `scripts/Test-PackageContents.ps1` checks README, license expression, repository URL, symbol package and the exact dependency set (Contracts none; Client the six ids).
- **The Client's dependencies are pinned by a rule.** Contracts, `SyntaxCircus.Http.Resilience`, `SyntaxCircus.Common`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Options` and `Microsoft.Extensions.DependencyInjection.Abstractions`; no other project reference, no framework reference.
- **Tests.** `tests/TechStrap.Client.Tests` references `src/TechStrap.Api` directly and links the reusable `Api.Tests` helpers (`Api.Tests` is an xUnit v3 executable with an assembly fixture); its `ClientApiFactory` copies about 30 lines of `HostFactory` defaults. The unit tests use a scripted handler and `FakeTimeProvider`; the Docker tests (`[Trait("Integration","Docker")]`) run the SDK against the real Api and Postgres. `AddTechStrapClient` uses `TryAddSingleton` for `TimeProvider` and `ITechStrapClient`, so a host can substitute a fake client.

### Alternatives Considered
- **`AddResilientHttpClient`.** Rejected: it has no per-request replay safety, so a ticket create would be retried without a key and could be duplicated.
- **A plain `HttpClient` with no retries** (the package map's earlier assumption). Rejected: a lost response on a flaky mobile network would force every caller to write its own retry, and the server already deduplicates by `Idempotency-Key` (D-020).
- **An own result type in the Client.** Rejected: one `Result` type across Syntax Circus packages is worth more than a smaller dependency list. Keeping the framework reference in Common was rejected too, because a MAUI app cannot take it.
- **One pull request for the whole phase.** Rejected: MAUI and publishing wait on a macOS runner and the nuget.org setup; the SDK core does not.
- **Multipart and attachments now.** Rejected: the intake endpoint is JSON-only (D-034); the SDK must not run ahead of the server.
- **Error codes in Contracts.** Rejected: the SDK produces them and they never cross the wire.
- **A `SubmitOptions` type.** Rejected: a key overload and a second method say the same thing with less surface, and `SubmitTicketOnceAsync` makes "exactly one send" visible at the call site.

### Consequences
- **Contracts is a public API from its first package.** Breaking DTO changes after 1.0.0 need semantic-versioning discipline; `EnablePackageValidation` is on and has its baseline after 1.0.0.
- **Spec corrections.** `PHASE-11-client-sdk.md` carries a Corrections block; `03-PACKAGE-MAP.md` and `02-ARCHITECTURE.md` are updated; `CLIENT-SDK.md` documents the SDK for maintainers. `TechStrap.Client.Tests` is created in 11a.
- **Two upstream releases are prerequisites.** `SyntaxCircus.Common` 0.2.0 and `SyntaxCircus.AspNetCore.Common` 0.1.16 are pinned in `Directory.Packages.props` and the package map; Check-PackageVersions keeps them in step.
- **As built in 11a: what the spike measured** (`SyntaxCircus.Http.Resilience` 0.2.2). The pipeline returns the final failing response (the caller disposes it) and disposes earlier ones; after `MaxAttempts` exceptions it rethrows the original `HttpRequestException` unwrapped; `TotalRequestTimeout` is one deadline over the sends and the backoff; the circuit counts logical calls, not attempts, and opens when at least half of the last 5+ calls in a 30 s window failed (the package defaults) and stays open 30 s. The circuit constants (ratio 0.5, minimum throughput 5, sampling 30 s, break 30 s) are the package defaults, made explicit in `ResilienceDefaults`.
- **As built in 11a: the proof.** 179 tests in `TechStrap.Client.Tests`. The real-API tests cover a Trusted and a Public key (metadata trust flag, external user ref, channel), a revoked, unknown and deactivated key, a validation error on `email`, a 413 over Kestrel and the rate limit. P11-T17: a lost first response followed by a retry with the same key leaves exactly one ticket row and returns the same ticket number; with `SubmitTicketOnceAsync` a lost response leaves one attempt, `api-unavailable` and one row. The OpenAPI contract test pins the POST operation, its `application/json` request properties (equal to the camelCase constructor parameters of `SubmitTicketRequest`), the `ApiKey` security scheme, the `Idempotency-Key` header parameter and that nothing is multipart.
- **As built in 11a: mutation substitution.** Changing `HeaderNames.IdempotencyKey` cannot be caught by design, because client and server share the constant; a client-side literal mutation stood in and was killed.
- **Known limits**
  - A 429 is surfaced as `rate-limited` and not retried, and `Retry-After` is not carried: the server does send it, but `ResultError` has no slot for it and the pipeline cannot honour it per response. A caller may retry a rate-limited result itself. Follow-up: add a slot in `SyntaxCircus.Common`, then surface it.
  - The Api documents no response schemas (no controller uses `ProducesResponseType`), so the intake POST appears in OpenAPI as a 200 with no body. The contract test cannot pin the 201 `SubmitTicketResponse` schema; the real-API tests pin the response shape instead. No server change was made in 11a; a PHASE-05 follow-up.
  - One circuit per DI container (per `TechStrapClient` singleton): every caller that resolves the same `ITechStrapClient` shares it, so a failing API opens the circuit for all of them. It counts calls, not attempts, and closes after the 30 s break.
  - The Client and Contracts target `net10.0` only; multi-targeting is a post-1.0 question.
  - Attachments are deferred (P11-T05) to 11d, which first needs multipart intake.
  - GitVersion, the documentation file and a SourceLink package are deferred to 11c, so a local pack needs `-p:Version=`.
  - Idempotency keys expire on the server (24 hours, D-020): a key replayed after the retention window creates one new ticket. A caller that supplies its own key and keeps it longer than that gets a duplicate, not the first ticket.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-11 planning)
- **Approved on:** 2026-10-07

## D-048: PHASE-11b: MAUI helper (single net10.0 target, Essentials abstractions, TicketMetadataKeys, MauiTicketDraft, lazy Essentials defaults)

- **Status:** Approved (owner 2026-10-07; technical rulings at PHASE-11b plan review)
- **Date:** 2026-10-07
- **Owner:** Jon Seeley
- **Related artifacts:** D-001, D-005, D-047, `docs/architecture/PHASE-11-client-sdk.md`, `docs/superpowers/plans/2026-10-07-phase-11b-maui.md`, `docs/development/CLIENT-SDK.md`

### Context
The PHASE-11 spec and D-047 assumed `TechStrap.Client.Maui` multi-targets `net10.0-android;net10.0-ios;net10.0`, with Android built on the existing runner and iOS only on a `v*` tag on a macOS runner (owner action #10). A spike on 2026-10-07 in `mcr.microsoft.com/dotnet/sdk:10.0`, with no workloads installed, found:
- **Restore, build and test pass with `Microsoft.Maui.Essentials` 10.0.0 on plain `net10.0`.** The Essentials interfaces (`IAppInfo`, `IDeviceInfo`, `IConnectivity`, `IDeviceDisplay`, `IBattery`) are all the helper needs.
- **`UseMaui` fails with NETSDK1147,** because it needs a workload. A MAUI workload build would need `dotnet workload restore` on every runner, and macOS for iOS.
- **`Microsoft.Maui.Controls` would carry about 19 transitive packages;** `Microsoft.Maui.Essentials` carries 3. The helper has no UI.
- **`DeviceInfo.Current.Model` on plain `net10.0` throws `NotImplementedInReferenceAssemblyException`** (an internal type). The statics must not be touched at registration or in tests.
- **`TicketMetadataKeys` does not exist** (D-047 left it to 11b), and the spec's positional `SubmitAsync` with a `FileResult` has nothing to send to: the intake endpoint is JSON-only (D-034, D-047).

### Decision
**Owner decisions (2026-10-07)**
- **Single target framework.** Single net10.0 for `TechStrap.Client.Maui`. The spec's multi-target and D-047's MAUI CI split (Android on the existing runner, iOS on macOS on a tag) are withdrawn. No workload, no macOS runner, no `UseMaui`: owner action #10 is no longer needed.
- **Delivery.** One pull request for P11-T07, T08 and T09 plus the close-out.
- **Design.** The MAUI helper design below is approved.

**Technical rulings (proposed in the 11b plan; approved when the owner approved it)**
- **Essentials, not Controls.** The package depends on `Microsoft.Maui.Essentials` 10.0.0 (it replaces the unused `Microsoft.Maui.Controls` pin in `Directory.Packages.props`), `Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options`, and references `TechStrap.Client` and `TechStrap.Contracts`. The nuspec has five dependencies: those three packages plus `TechStrap.Client` and `TechStrap.Contracts`. `ClientMauiRules` pins the package set, the project references (Client and Contracts only), no framework reference and no `UseMaui` (read from the csproj text). `Test-PackageContents.ps1` checks the five ids in the CI pack dry run, which now packs all three packages.
- **`TicketMetadataKeys` lives in Contracts.** It holds 13 default keys (`app.name`, `app.version`, `app.build`, `app.package`, `os.platform`, `os.version`, `device.manufacturer`, `device.model`, `device.idiom`, `device.type`, `locale`, `timezone`, `network.access`) and 6 extras (`display.width`, `display.height`, `display.density`, `display.orientation`, `battery.state`, `battery.level`), with `Defaults` and `All` as frozen ordinal sets. `IntakeLimits.MaxMetadataJsonLength` is 16,000, parity-tested against `DomainLimits.MetadataMaxLength`.
- **The collector.** `IDeviceContextCollector.Collect()` is synchronous and never throws. `MauiDeviceContextCollector` takes the five Essentials interfaces and `IOptions<DeviceContextOptions>` by injection. `DeviceContextOptions` is `IncludeDeviceContext` (true), `IncludeDisplay` (false), `IncludeBattery` (false) and a `Redact(key, value)` callback. Each field is read in its own try/catch, and a failing field is skipped. Values are trimmed, blank values dropped, values cut to `IntakeLimits.MaxMetadataValueLength` and trimmed again, then passed to `Redact` (null drops the field; a throwing redactor drops only that field; its result is validated again). `Redact` is applied to each collected device-context value only; the draft's own `Metadata` is not passed through it. Numbers use the invariant culture: `display.density` as "2.625", `battery.level` as a rounded whole percent, left out when the charge level is negative. Never collected: advertising or device ids, location, contacts, IP address, user names.
- **The submit helper takes a draft.** `IMauiTicketSubmitter.SubmitAsync(MauiTicketDraft draft, CancellationToken ct = default)` returns `Task<Result<SubmitTicketResponse>>`. `MauiTicketDraft` is a record: `Subject`, `Message`, `RequesterEmail`, `RequesterName`, `Metadata`, `IdempotencyKey`. 11d adds `Attachments` as an init-only (`{ get; init; }`) property, never a new positional parameter (which would replace the primary constructor and `Deconstruct` and break compiled callers). A null draft throws `ArgumentNullException`; a blank, non-ASCII or over-200-character `IdempotencyKey` throws `ArgumentException`; cancellation throws `OperationCanceledException`.
- **Merge rule.** Collected keys and every key in `TicketMetadataKeys.All` are reserved: an app value for one is ignored (reject-versus-ignore: ignore, as the spec says). Every entry, app or collected (a custom `IDeviceContextCollector` included), gets the same checks: a blank key or one over 64 characters is a failure; a blank value is skipped; a value is cut to 1000 characters. The collected keys (up to 19) count toward the 50, so an app can rely on 31 of its own. More than 50 keys, or more than 16,000 characters serialized (`JsonSerializerDefaults.Web`), is a local failure with code `metadata-invalid` (`TechStrapMauiErrorCodes.MetadataInvalid`, `ResultErrorKind.Validation`, target `metadata`) and nothing is sent. That constant repeats the server's wire string on purpose. The request goes through `ITechStrapClient`: the unkeyed `SubmitTicketAsync` without an `IdempotencyKey`, the keyed overload with one. The client's result is returned unchanged.
- **Registration and lazy defaults.** `AddTechStrapMaui(Action<DeviceContextOptions>? configure = null)` needs `AddTechStrapClient` first (resolving the submitter without it throws `InvalidOperationException` naming `AddTechStrapClient`). `AddTechStrapMaui(Action<TechStrapClientOptions> configureClient, Action<DeviceContextOptions>? configure = null)` calls `AddTechStrapClient` first. The Essentials defaults (`AppInfo.Current`, `DeviceInfo.Current`, `Connectivity.Current`, `DeviceDisplay.Current`, `Battery.Default`) are registered with `TryAddSingleton` as factories, so nothing reads them at registration, an app's own registration wins. Service registrations are idempotent; configure delegates stack like any Options configure.
- **Tests need no device and no Docker.** `tests/TechStrap.Client.Maui.Tests` (plain `net10.0`, NSubstitute over the Essentials interfaces, 52 tests) covers the collector, the merger, the submitter, registration and options. `DeviceContextOptionsTests` exists because an empty test project makes `dotnet test` exit 8. `NotImplementedInReferenceAssemblyException` is internal, so the throwing-accessor test uses other exception types (the collector catches `Exception`). The lazy-defaults pin asserts the descriptors are factories. The size-boundary test includes escaped characters, because a PascalCase policy would not rename dictionary keys.

### Alternatives Considered
- **Multi-target `net10.0-android;net10.0-ios;net10.0`.** Rejected: the helper has no platform-specific code, and a workload build costs a workload restore on every runner and a macOS runner for iOS.
- **A dependency on `Microsoft.Maui.Controls`.** Rejected: about 19 transitive packages for a helper that has no UI; Essentials carries 3.
- **A positional `SubmitAsync(subject, message, ..., attachments)`.** Rejected: 11d adds attachments, and every added parameter would be a new overload. A draft record grows without that, provided the new member is init-only.
- **Eager Essentials defaults** (`services.AddSingleton(AppInfo.Current)` and the like). Rejected: they cannot be tested on a build host, and they throw on desktop at registration.
- **Rejecting a reserved key an app supplies in its metadata.** Rejected: the spec says ignore, and a silent drop of an app's own value for a key the helper owns does less harm than a failed submit.

### Consequences
- **Spec corrections.** `PHASE-11-client-sdk.md` carries a second Corrections block and ticks T07, T08 and T09; `03-PACKAGE-MAP.md`, `02-ARCHITECTURE.md` and the roadmap are updated; `CLIENT-SDK.md` has a `TechStrap.Client.Maui` section. `TechStrap.Client.Maui.Tests` is created in 11b. Owner action #10 (macOS runner) is no longer needed; 11c still waits on #9 (nuget.org).
- **Known limits**
  - Consumers need MAUI >= 10.0.0: the floor is the Essentials pin (10.0.0, the lowest version with every API the helper uses), so no patch upgrade of Core, Graphics or WindowsAppSDK is forced.
  - A Public key is extractable, so metadata from one is stored but flagged untrusted (D-001); the helper does not change that.
  - No screenshot and no attachments until 11d, which first needs multipart intake.
  - Collection skips a failing field silently (the package does no logging). `MainDisplayInfo` is read per field, so a rotation between reads can mix values; this is accepted.
  - There are no platform target frameworks. Platform-specific code would need them later; adding them is non-breaking.
  - A caller who retries a failed submit must supply a stable `IdempotencyKey` (the same rule as `TechStrap.Client`).

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-11b planning)
- **Approved on:** 2026-10-07

## D-049: PHASE-11c: publishing (tag-triggered publish-nuget.yml, Trusted Publishing, version from the tag, XML documentation, source-generated JSON, compiled README snippets, console sample)

- **Status:** Approved (owner 2026-10-08; technical rulings at PHASE-11c plan review)
- **Date:** 2026-10-08
- **Owner:** Jon Seeley
- **Related artifacts:** D-003, D-005, D-047, D-048, PHASE-11, PHASE-12, `docs/superpowers/plans/2026-10-08-phase-11c-publish.md`, `docs/development/RELEASING.md`, `docs/development/CLIENT-SDK.md`

### Context
D-047 and D-048 left the publishing half of PHASE-11 to 11c: per-package READMEs, a sample, the publish workflow, the nuget.org setup, XML documentation, GitVersion and SourceLink for the packages, and the first publish (`v0.1.0`, see the addendum). On 2026-10-08:
- **nuget.org is ready.** Owner action #9 is done (see decision 1 below).
- **UAT is not deployed.** The post-publish check that the spec ran against UAT has nothing to run against.
- **`release.yml` builds and pushes the GHCR images on a `v*` tag** and publishes nothing to nuget.org. Until 11c, `ci.yml` only dry-runs the pack on pull requests (`-p:Version=0.0.0-ci`).
- **Contracts had 154 undocumented public members;** Client and Client.Maui were already documented. The Client and Maui packages serialized with reflection, which is not AOT-safe.

### Decision
**Owner decisions (2026-10-08)**
1. **nuget.org is ready.** The `TechStrap.*` package IDs are reserved. Each package has a Trusted Publishing policy bound to the repository `Syntax-Circus/techstrap`, the workflow file `publish-nuget.yml` and the environment `release`. `NUGET_USER` is set as an organization secret available to the repository (not a repository secret). The GitHub environment `release` has a required reviewer and a deployment rule "Selected branches and tags" with the tag pattern `v*` ("Protected branches only" would block tag refs). The controller created the environment on 2026-10-08 and verified both with `gh api repos/Syntax-Circus/techstrap/environments/release`.
2. **XML documentation is on for every package member.** `GenerateDocumentationFile` is enabled in `eng/Packaging.props`, and every public member is documented.
3. **The post-publish check runs against the local compose stack.** UAT is not deployed, so the UAT submit moves to PHASE-12 (P12-T14).
4. **The publish design is approved:** a tag-triggered `publish-nuget.yml` plus a GitHub Release, the version from the tag, a console sample with compiled README snippets, no MAUI sample project, and source-generated JSON.

**Technical rulings (proposed in the 11c plan; approved when the owner approved it)**
- **Version from the tag.** The packages take their version from the tag through `dotnet pack -p:Version=<semver>`, not from `GitVersion.MsBuild`; the hosts keep GitVersion. There is no `Microsoft.SourceLink.GitHub` package, because SourceLink is bundled in the SDK.
- **Triggers.** `publish-nuget.yml` runs on a `v*` tag push and on `workflow_dispatch`. It has no `pull_request` trigger; `ci.yml` keeps the pack dry run on pull requests. A dispatch is a dry run: the version is `0.0.0-dryrun.<run number>`, and the `publish` job is skipped.
- **Jobs.** The `pack` job takes the version from the tag (a tag that is not `v<semver>` fails), runs `dotnet test --solution TechStrap.CI.slnf`, packs the three packages, runs `scripts/Test-PackageContents.ps1` and uploads them. The `publish` job needs `pack`, runs in the `release` environment, and checks that every package file name carries the version.
- **Publish guard.** The `publish` job runs only if `github.ref_type == 'tag' && github.event_name == 'push'`, so a `workflow_dispatch` started on a tag ref stays a dry run.
- **Contracts stability scope.** `v1.0.0` locks the SDK-facing surface of `TechStrap.Contracts`: the `TechStrap.Contracts.Intake` namespace (`SubmitTicketRequest`, `SubmitTicketResponse`, `IntakeLimits`, `IntakeRoutes`, `IntakeWarnings`, `TicketMetadataKeys`) and `TechStrap.Contracts.Http.HeaderNames`. The other namespaces (Admin, Agents, ApiKeys, Kb, Live, AdminEvents, Tickets and so on) are TechStrap's own app wire shapes, shared with its Admin and Portal, and may change in minor versions. The Contracts README and `CLIENT-SDK.md` say the same.
- **Trusted Publishing only.** `NuGet/login@v1` exchanges the OIDC token (secret `NUGET_USER`) for a short-lived key, and `dotnet nuget push --skip-duplicate` uses it. There is no `NUGET_API_KEY` fallback, and `PublishWorkflow.Tests.ps1` fails if any workflow reads that secret.
- **The GitHub Release comes from `publish-nuget.yml`:** `gh release create` with `--generate-notes`, the three packages and their symbol packages attached, and `--prerelease` when the version has a hyphen. `release.yml` still builds and pushes the GHCR images; it only gained a comment that points to the new workflow.
- **Documentation file.** `GenerateDocumentationFile` is set in `eng/Packaging.props`. `scripts/Test-PackageContents.ps1` fails when `lib/net10.0/<id>.xml` is missing, and the build fails on a missing doc comment (CS1591 under `TreatWarningsAsErrors`).
- **Source-generated JSON.** `TechStrap.Client.Json.TechStrapJsonContext` (public) has camelCase names, case-insensitive reading and numbers readable from strings, and covers `SubmitTicketRequest`, `SubmitTicketResponse` and `Dictionary<string,string>`. `TechStrapClient` and `MauiMetadataMerger` use its typed infos. `IsAotCompatible` is on for Client and Client.Maui with the AOT analyzers clean and no suppressions. Parity tests prove the output is byte-identical to `JsonSerializerDefaults.Web`, which the server uses.
- **`TicketViews` docs corrected.** `Open` is New and Open; `Unassigned` and `Mine` are New, Open and Pending; none includes spam, which matches the repository's status sets.
- **Compiled README snippets.** The snippets are `#region readme:*` blocks: `client-register`, `client-submit` and `client-errors` in the console sample's `Program.cs`, `maui-register` and `maui-submit` in `tests/TechStrap.Client.Maui.Tests/Snippets/MauiReadmeSnippets.cs`. `scripts/tests/ReadmeSnippets.Tests.ps1` extracts each region and fails if a README block differs.
- **The console sample.** `samples/TechStrap.Client.Samples.Console` uses `Host.CreateApplicationBuilder`, takes `--base-address` and `--api-key` (or `TECHSTRAP__BASEADDRESS` and `TECHSTRAP__APIKEY`), calls `AddTechStrapClient`, and exits 0 with `Ticket <n>` and `View: <url>`, 1 with an SDK failure, or 2 for a configuration problem. It never prints the key. It is in `TechStrap.slnx` and `TechStrap.CI.slnf` (built, not a test). Its one new central pin is `Microsoft.Extensions.Hosting` 10.0.12.
- **Dispatch needs `main`.** GitHub can only dispatch a workflow that is on the default branch, so the first dry run of `publish-nuget.yml` happens right after the merge and before the first tag (`docs/development/RELEASING.md`).
- **Tests.** `scripts/tests/PublishWorkflow.Tests.ps1` pins the workflow, `RepositoryDocs.Tests.ps1` pins this decision and the spec ticks, and `Test-PackageContents.ps1` is fixture-tested.

### Alternatives Considered
- **`GitVersion.MsBuild` in the packages.** Rejected: the tag already is the version, and a second source could disagree with it. The `Build` step passes `-p:Version=<tag>`, so the package and the assembly versions both carry the tag.
- **Publishing on pull requests.** Rejected: nothing may reach nuget.org from an unreviewed change. `ci.yml` dry-runs the pack on pull requests instead.
- **A `NUGET_API_KEY` fallback.** Rejected: a long-lived key in a secret is the risk Trusted Publishing removes.
- **Creating the GitHub Release from `release.yml`.** Rejected: that workflow builds images; the Release lists the packages, so it belongs with the workflow that makes them.
- **A MAUI sample project.** Rejected: it needs a workload and a macOS runner (D-048 withdrew both). The MAUI snippets compile in `TechStrap.Client.Maui.Tests`.
- **README snippets as plain text.** Rejected: they drift from the API. Compiled regions with a README check fail the build instead.
- **A UAT post-publish check now.** Deferred: UAT is not deployed. The check runs against the local compose stack, and the UAT submit is PHASE-12 (P12-T14).

### Consequences
- **Spec corrections.** `PHASE-11-client-sdk.md` carries a third Corrections block, ticks T11 to T15 with as-built notes and leaves T16 open; `03-PACKAGE-MAP.md`, `02-ARCHITECTURE.md`, the roadmap and the discovery index are updated; owner action #9 is done. `RELEASING.md` is the release guide, `CLIENT-SDK.md` gains "Running the sample", and the root README names the packages.
- **P11-T16 is post-merge.** The owner pushes `v0.1.0`, approves the `release` environment, and checks that the packages restore from nuget.org. Then the post-publish check runs against the compose stack and T16 is ticked in a follow-up docs change.
- **Known limits**
  - The `v*` tag also publishes the GHCR images at that tag (`release.yml`). That is intended: PHASE-12 deploys the first version to UAT.
  - Packages are immutable on nuget.org: a fix is a new version, and a bad version is unlisted, not deleted.
  - Each publish waits for an approval in the `release` environment. The environment's deployment rule must allow `v*` tags; "Protected branches only" would block the tag ref. `NUGET_USER` is an organization secret, so the repository only sees it while the organization grants access.
  - The `v1.0.0` SemVer promise covers only the SDK-facing surface of `TechStrap.Contracts` (the `Intake` namespace and `Http.HeaderNames`); the other namespaces are TechStrap's own wire shapes and may change in minor versions.
  - `Test-PackageContents.ps1` checks the README, license, repository URL, dependency set, symbol package and xml documentation. It does not check for secrets or package size; those checks remain open (PHASE-12 or a later 11c follow-up).
  - `--generate-notes` builds the release notes from pull request titles; PHASE-12 may replace them with a Conventional Commits changelog.
  - The UAT submit (the spec's original T16 validation) moves to PHASE-12 (P12-T14).
  - Attachments remain 11d, which first needs multipart intake.
  - Source generation has no encoder option, so the SDK and the server both use the default encoder; parity holds because they match.
  - A new workflow can only be dispatched once it is on `main`, so the first dry run follows the merge.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-11c planning)
- **Approved on:** 2026-10-08

### Addendum (2026-10-08, first version)
The owner's decision on the first published version. It extends D-049; where it differs from the text above, it wins. There is no new decision number.

- **The first published version is `v0.1.0`.** Not `v1.0.0-rc.1`. The packages stay in the 0.x range, like the other Syntax Circus packages, until the SDK-facing surface of `TechStrap.Contracts` is locked; `v1.0.0` follows then.
- **The release logic is unchanged.** `0.1.0` has no hyphen, so it is a normal release on nuget.org and a normal (non-`--prerelease`) GitHub Release, as for the other Syntax Circus packages. `publish-nuget.yml` derives the version from the tag and needs no change.
- **Published.** v0.1.0 was published on 2026-10-08 (run 37793512118); the post-publish check passed against the local compose stack (ORB-8 trusted, ORB-9 untrusted).

## D-050: PHASE-11e: product hosts (PortalHost on the Product, HostNameShape, host-aware links, ProductHostMiddleware, PortalLinks, per-host SEO and output cache; amends D-002)

- **Status:** Approved (owner 2026-10-08; technical rulings at PHASE-11e plan review)
- **Date:** 2026-10-08
- **Owner:** Jon Seeley
- **Related artifacts:** D-002, D-043, D-045, PHASE-11e, PHASE-12, `docs/superpowers/plans/2026-10-08-phase-11e-product-hosts.md`, `docs/self-hosting/DEPLOYMENT.md`, `docs/development/PORTAL-APP.md`

### Context
D-002 chose one portal domain for every product, with each product under `/p/{key}` and its own theme. A customer-facing product that wants its own address (for example `support.dragonpoop.com`) could not have one. On 2026-10-08, during PHASE-11e planning, the owner asked for it:
- **The default host is unchanged.** `TECHSTRAP_PORTAL_PUBLIC_URL` (for example `support.syntaxcircus.com`) keeps serving every product under `/p/{key}` and the default product at `/`.
- **A product may add its own host,** served by the same Portal deployment, with clean paths.
- **Emails, Admin links, canonical URLs, JSON-LD and the sitemap** have to follow the product host for that product.
- **The Contracts package is published as `v0.1.0`,** so the DTO changes have to stay additive.

### Decision
**Owner decisions (2026-10-08)**
1. **Product hosts are their own small phase, PHASE-11e, before PHASE-12.** They are not folded into PHASE-12.
2. **The host-to-product mapping lives on the Product** (an Admin-editable `PortalHost`), not in configuration. No new setting is added.
3. **URLs on a product host are clean** (`/`, `/contact`, `/kb/...`). The default host keeps `/p/{key}/...` and answers the long form of a hosted product with a 301 to its host.
4. **D-050 amends D-002.** One portal deployment still serves every product, but a product may have its own host.

**Technical rulings (proposed in the 11e plan; approved when the owner approved it; the as-built corrections are marked)**
- **Host shape.** `HostNameShape.TryNormalize(string? input, out string? host)` in `TechStrap.Domain.Rules` trims and lowercases, then rejects a scheme, a port, a path, userinfo, whitespace, non-ASCII, underscores, labels that are not 1 to 63 characters of `[a-z0-9-]` or that start or end with `-`, a name with fewer than two labels and a name over `DomainLimits.HostNameMaxLength` (253). Blank input normalises to `null`, which clears the host. *As built:* an all-digit last label is also rejected, so an IP literal is never a product host.
- **Storage and uniqueness.** `Product.PortalHost` and `Product.SetPortalHost(string?)` (error `product-host-invalid`); EF column `portal_host varchar(253)` with the unique index `ix_products_portal_host` (migration `AddProductPortalHost`). The handlers also pre-check through `IProductRepository.IsPortalHostTakenAsync(host, exceptProductId, ct)` and answer `product-host-taken` (409) deterministically; the index is the safety net.
- **Contracts stay additive.** `string? PortalHost = null` is the last parameter of `ProductDto`, `CreateProductRequest`, `UpdateProductRequest`, `PublicProductDto` and `PublicProductSummaryDto`, so 0.1.0 positional construction still compiles. The Admin references Contracts only, so the shape rule is copied into Contracts with a parity test, as for the intake limits. *As built:* the copy is `TechStrap.Contracts.Products.ProductHostRules` (with `HostNameMaxLength`), not `ProductLimits`. `ProductErrors.HostInvalid()` is a 400 with the kebab-case target `portal-host`; `ProductErrors.HostTaken()` is a 409 with no target.
- **Links are built from stored values only.** `PortalLinkOptions.TicketLink(string? portalHost, string token)`, `ArticleLink(string? portalHost, key, category, slug)`, `ArticlePathOnHost` and `ProductHostBase(host) => https://{host}`. A null or blank host gives the unchanged default-host shape. Product hosts are always https. *As built:* the overloads take the host string, not the `Product` type (the spec said `TicketLink(Product, ...)`). The Worker's `EndsWith` check became `PortalLinkOptions.IsArticleLink(url, key, category, slug)`, which matches either shape; the host shape must be exactly `https://{host}/kb/...`. The Admin's `PortalUrlOptions.ArticleUrl(portalHost, ...)` gives `https://{host}/kb/...` for a hosted product even when the portal public URL is blank.
- **The raw Host header is a lookup key, never a value.** `ProductHostMiddleware` lowercases the request host and looks it up in the map of stored hosts. Every URL, redirect target and canonical is built from a stored `PortalHost` or `TECHSTRAP_PORTAL_PUBLIC_URL`; the header never reaches a URL, a header or a log line. An unknown host behaves as the default host. *As built:* a host that can never be a product host (a single label such as `localhost`, or an IP literal) is neither looked up nor redirected.
- **Host map.** `ProductHostMap` (singleton) holds a `volatile` immutable snapshot built from `IPublicProductClient.ListAsync` (the summaries carry `PortalHost`), with `ProductHostMapOptions.Ttl = 60 s` and `MissRefreshInterval = 10 s`. *As built:* it is a snapshot driven by `TimeProvider`, not an `IMemoryCache` entry. It is stale-while-revalidate: an expired snapshot is served while at most one background reload runs on a non-request token, and only a cold start awaits. A miss inside the 10 s interval never touches the lock. A read has a deadline of `ProductHostMapOptions.ReadTimeout = 30 s` (a linked token on the map's clock), so a hung call cannot freeze the map; the key and the host of one lookup come from a single snapshot (`FindAsync`). The background read runs under a synthetic `DefaultHttpContext` that carries only the visitor IP, as the sitemap build does. `ProductHostContext` (`Key`, `Host`, `IsProductHost`) is scoped, stored on `HttpContext.Features` and resolved through `IHttpContextAccessor`, so re-executed 404 and error pages see it.
- **Middleware order and rewrite table.** `UseProductHosts()` (`ProductHostMiddleware`) runs directly after `UsePortalSeo()` and before `UseTechStrapErrorPages()`, and it calls `UseRouting()` so endpoint selection follows the rewrite. On a product host `/` becomes `/p/{key}`, and `/contact`, `/contact/received`, `/lost-link`, `/kb...` and `/suggest` become `/p/{key}/...`. It passes through, by segment, `/t`, `/_framework`, `/_blazor`, `/_content`, `/css`, `/js`, `/img`, `/favicon*` (name prefix), `/sitemap.xml`, `/robots.txt`, `/health`, `/not-found`, `/error` and `/_styleguide`. An unknown clean path on a product host passes through unrouted and is a 404.
- **Redirects.** Only GET and HEAD are redirected (a 301 would drop a POST body; a POST on the wrong path is rewritten and served). `/p/{sameKey}/x` gets a 301 to `https://{storedHost}/x`; `/p/{otherKey}/x` gets a 301 to that product's canonical URL (its host with a clean path, else the default host with `/p/{otherKey}/x`); on the default host `/p/{key}/x` of a hosted product gets a 301 to `https://{host}/x`. The query string is kept. *As built:* the same-host canonical redirect is absolute (a relative Location from `/p/key//evil` would be protocol-relative), and no redirect is ever built from `Request.Host`.
- **`PortalLinks`** (scoped, `TechStrap.Portal/Routing/PortalLinks.cs`) is the single owner of the clean-path rule: the same key as `ProductHostContext.Key` gives the `/p/{key}` path with the prefix stripped (`/` for the home); another product with a host gives `https://{host}` plus the stripped path; otherwise `/p/{key}/...`. `Ticket` and `TicketAttachment` are always relative. `Absolute(path)` is `https://{Context.Host}{path}` on a product host and `PublicBaseUrl + path` otherwise. `PortalLinks.ToFragment` keeps the skip link, the error-summary field links and the ticket reply jump link clean. An architecture rule (`PortalRules`, `PortalLinkRuleTests`) forbids `PortalRoutes.<builder>(` in Portal `.razor`, `.razor.cs` and `Seo/*` except in `Routing/PortalLinks.cs` and `Routing/PageLinks.cs`; the `*Template`, `*Parameter`, `*Segment`, `*Prefix` and `*Path` constants stay allowed.
- **SEO per host.** The canonical URL and the JSON-LD URLs come from `PortalLinks.Absolute` (the package's `ISeoUrlBuilder.AbsoluteUrl` passes an absolute URL through, so `SyntaxCircus.Blazor.Seo` is unchanged). `/sitemap.xml` on a product host lists only that product, with clean paths on `https://{host}`; on the default host it lists only the products without a host plus the static `/` entry (that entry moved from `MapSeoSitemap` into the provider so product hosts do not list the default `/`). `PortalSitemapCache` is keyed by `Context.Host ?? "default"` with the same 15-minute TTL. The Open Graph image stays on the default host (`/icon-512.png` is not served on product hosts). **Amended 2026-10-08:** see the robots.txt amendment below: the OG image now follows the product host (`/icon-512.png` is served on every host) and Blazor.Seo 0.1.5 (published 2026-10-08, pinned here) makes the builder registration replaceable.
- **Output cache.** `PortalOutputCache` uses `SetVaryByHost(true)`; the former "never vary by host" test became "two hosts, two entries".
- **Admin.** `ProductEditorViewModel.PortalHost` is normalised through `ProductHostRules.TryNormalize` (blank to `null`); a 400 `product-host-invalid` is routed by its target `portal-host` to the field; a 409 `product-host-taken` is shown as a field error ("Another product already uses this hostname."); the product list has a "Portal host" column.
- **Ticket pages are host-neutral.** `/t/{token}` is served on every host, so the page does not depend on which host it is asked on. An emailed link names the product's host (`https://{oldHost}/t/{token}`), so it keeps working after a host change only while the old host's DNS and Caddy site remain; nothing sends a customer to the default host.
- **The default host is reserved.** The Api refuses a `PortalHost` equal to the host of `TECHSTRAP_PORTAL_PUBLIC_URL` on create and update (`product-host-reserved`, 400, target `portal-host`; no check when the URL is blank). `ProductHostMap` also drops such an entry and logs the product key once per read, so a bad row cannot make the default host redirect a product's help centre to a host that 404s.
- **Header rules see the arriving path.** `UseTechStrapWebHost` evaluates the per-path header rules before `UseProductHosts()` rewrites the path, so `PortalHeaderRules.IsFormPagePath`, `PortalCachePaths.IsKbPage` and `PortalCachePaths.IsKbSearchPath` accept both the `/p/{key}/...` shape and the clean shape (`/contact`, `/kb/...`). On the default host the clean shapes are 404s: the help-centre public cache header is set only on success and so is skipped, while a `/contact` or `/kb/search` 404 merely carries `no-store` (and `noindex` for a form page), which is harmless. The output-cache predicate `IsCacheable` still matches the `/p/{key}` shape only (the cache runs after the rewrite).
- **The Portal is static server-side rendering.** It has no interactive circuit: `POST /_blazor/negotiate` is 405 on every host, so the circuit check reduced to `blazor.web.js` answering 200 on a product host plus negotiate parity.
- **Tests.** `scripts/tests/RepositoryDocs.Tests.ps1` pins this decision, the D-002 amendment, the roadmap and discovery rows, the Portal and runbook wording and the spec ticks.

### Alternatives Considered
- **The mapping in configuration or an environment variable.** Rejected: a new product host would need a deploy, and the Admin could not show it. The Product already is the place for per-product data.
- **A reverse-proxy path rewrite instead of middleware.** Rejected: the proxy is outside the compose files and does not know the product map; the Portal also has to build correct links, canonicals and sitemaps, so it needs the map anyway.
- **Keeping `/p/{key}` paths on product hosts.** Rejected: the owner wants clean URLs on a product's own address.
- **A wildcard or any-host mode** (serve any host that points at the Portal). Rejected: the host would become a value instead of a lookup key, and an unknown host could then reach a URL.
- **`IMemoryCache` for the host map.** Rejected as built: a plain snapshot with `TimeProvider` gives stale-while-revalidate and a testable clock, which a cache entry's eviction does not.

### Consequences
- **Spec and docs.** `PHASE-11e-product-hosts.md` carries a Corrections block and ticks T01 to T07; `02-ARCHITECTURE.md`, `05-SCHEMA.md`, the roadmap, the discovery index, `UX-BRIEF-portal.md`, `PORTAL-APP.md` and `DEPLOYMENT.md` are updated. D-002 carries an "Amended by D-050" line.
- **Operator work per host.** A product host needs a DNS record and one Caddy site with a working TLS certificate that proxies to the Portal, and only then is the host set in the Admin, because saving it switches links and redirects at once (`DEPLOYMENT.md`, "Product hosts"). The Portal needs no new setting: the host is set on the product in the Admin. UAT configuration (PHASE-12) adds the real hosts.
- **The Contracts change is additive.** The next package version (0.2.0) is tagged when the owner decides; it is not part of the PHASE-11e pull request. **Amended 2026-10-08:** the 0.2.0 tag is decided: TechStrap packages 0.2.0 are published after the D-050 limits PR merges, with no compatibility shims. The break is both binary (a positional `PortalHost` parameter was added to `ProductDto`, `CreateProductRequest`, `UpdateProductRequest`, `PublicProductDto` and `PublicProductSummaryDto`, so consumers must recompile) and behavioural against the unreleased PHASE-11e build on main (null clears -> null leaves the host unchanged); no released version ever cleared on null. The Version notes in the Contracts README carry this text.
- **Known limits**
  - A unique-index race that gets past the pre-check surfaces as `product-key-taken` on Create (the Duplicate mapping) and as the raw persistence Duplicate error on Update.
  - `IsArticleLink` does not check that the host belongs to the product; the stored URL was built by the planner.
  - Single-label and IP hosts are never product hosts, so on `localhost` a hosted product's `/p/{key}` is not 301'd.
  - The 301s set no `Cache-Control` of their own (a 301 on a help-centre path has none; on a form page it inherits that page's `no-store` from the per-path rules). Changing or removing a product's host leaves cached redirects and emailed links pointing at the old host; there is no redirect table. Keeping the old host's DNS and Caddy site preserves `/t/{token}` links only: the old host is then an unknown host, so old help-centre links on it (emailed, or a cached 301) are not rewritten and answer 404, and `/` shows the default root. **Amended 2026-10-08:** a canonical product-host 301 now sets `Cache-Control: public, max-age=3600` (`PortalCachePaths.RedirectCacheControl`), so a changed or removed host reaches visitors in about an hour. The exception is accepted and pinned: a 301 from a form page or the KB search (`/contact`, `/contact/received`, `/lost-link`, `/suggest`, `/kb/search`) keeps that page's `no-store`. The rest of the bullet (no redirect table; old help-centre links on an old host answer 404) stands.
  - A host that misses while a map refresh is in flight is the default host for that one request.
  - `robots.txt` on a product host names the default host's sitemap, because `SyntaxCircus.Blazor.Seo` builds the line from `SeoOptions.BaseUrl`; the host's own `/sitemap.xml` is correct. Fixing it is a package enhancement. **Amended 2026-10-08:** fixed. The Portal registers a host-aware `ProductHostSeoUrlBuilder` (a decorator over `SeoUrlBuilder`, driven by `ProductHostContext`, never the Host header), so `robots.txt`, the canonical fallback and the Open Graph image follow the product host; an unknown or hostile host still names the default host. `SyntaxCircus.Blazor.Seo` 0.1.5 registers it with `TryAddScoped` (published 2026-10-08 from PR Syntax-Circus/SyntaxCircus.Blazor.Seo#5; TechStrap pins 0.1.5) so a consumer's `ISeoUrlBuilder` wins on either side of `AddSyntaxCircusSeo` (the Portal also worked on 0.1.4, where the last registration wins). The Open Graph sentence in the SEO ruling (image stays on the default host) no longer holds: `/icon-512.png` is served on every host.
  - The output cache keys per raw Host value, so unknown hosts each get entries. A bound is PHASE-12 hardening.
  - The sitemap takes each product's host from the fresh product list, so a host change shows in it at the next build (the sitemap is cached for 15 minutes); on a product host only the product whose stored host is that host is listed; a product host whose product is not in the fresh list (or no longer has a host) yields an empty sitemap.
  - Until the Portal's first successful read of the product list, every product host behaves as the default host (an unreachable Api at cold start); a failed read is retried at most once per 10 s, and an existing map is kept when a read fails.
  - `UpdateProductRequest.PortalHost = null` means "clear the host". A caller built before PHASE-11e (an older Admin image during a rolling deploy, an integration on Contracts 0.1.0) does not send the field, so saving a product through it clears that product's host. The API semantics are unchanged; deploy the Api and the Admin together. **Amended 2026-10-08:** a null leaves the stored host unchanged; an empty or whitespace value clears it; any other value is normalised, validated and set. The Admin sends `""` for a blanked field and the typed value otherwise (so the Api's 400 for an invalid host lands on the field). Create is unchanged (null or blank = no host). On main before this amendment an explicit null cleared the host; no released client relied on it. A pre-11e client no longer wipes a host.
  - Adding the `PortalHost` parameter to the positional records changes their constructor and `Deconstruct` signatures. That is source-compatible (the spec's definition of additive) but not binary-compatible: a consumer compiled against Contracts 0.1.0 must be recompiled against 0.2.0. **Amended 2026-10-08:** 0.2.0 is published after the D-050 limits PR merges (owner tags `v0.2.0`), with the break named in the Contracts README Version notes and pasted into the GitHub Release; there are no compatibility shims.
  - The Blazor form-post redirect Location is absolute on the request host (framework behaviour, pre-existing).
  - The Portal is static SSR, so there is no circuit to test on a product host.
  - HSTS `includeSubDomains` on a parent domain also covers a product host under it (`DEPLOYMENT.md`).

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-11e planning)
- **Approved on:** 2026-10-08

## D-051: PHASE-12: release hardening at 0.3.0 (three pull requests, accepted budgets, UAT state, OpenAPI served, carried-forward items, fail-closed scans)

- **Status:** Approved (owner 2026-10-08; technical rulings at PHASE-12a plan review)
- **Date:** 2026-10-08
- **Owner:** Jon Seeley
- **Related artifacts:** D-003, D-022, D-029, D-043, D-049, D-050, PHASE-12, `docs/superpowers/plans/2026-10-08-phase-12a-hardening.md` (to be written)

### Context
PHASE-12 was specced as the path to `v1.0.0`. The Contracts package is published at `v0.2.0` and, like every Syntax Circus repo, stays 0.x until its API is locked. On 2026-10-08, during PHASE-12 planning, the owner made the decisions below. Planning also found facts where the spec is wrong; they are listed in the PHASE-12 spec's Corrections block.

### Decision
**Owner decisions (2026-10-08)**
1. **The end state is `v0.3.0`, not `v1.0.0`.** PHASE-12 ships the security review, the drills, the docs and the UAT soak, and tags `v0.3.0`. `v1.0.0` becomes its own later decision once the Contracts public surface (`TechStrap.Contracts.Intake` and `Http.HeaderNames`) is deliberately locked. There is no release candidate: UAT deploys the latest published tag at the time (currently `v0.2.0`), the soak runs it, and `v0.3.0` is tagged after the soak.
2. **Three pull requests.** 12a hardening (P12-T01 to T10, T19 and the carried-forward items below); 12b docs and scripts (T11 k6 scripts, T13 backup runbook and script, T16 self-host and OIDC guide, T17 Authentik guide); 12c UAT and release (T12 load run, T14 deploy, T15 restore drill, T18 48 h soak, T20 docs pass, T21 tag `v0.3.0`).
3. **The load and recovery budgets are accepted as specced:** 20 req/s for 10 min with p95 under 500 ms and no 5xx; a spike of 100 req/s for 60 s answered with 429s and recovery within 30 s; the outbox drains within 2 min; RPO 24 h and RTO 4 h.
4. **UAT state.** The UAT box and its Postgres exist (owner actions #8 and #11 are largely done). The Authentik clients and groups (owner action #7) are not set up yet and must be before 12c's deploy (T14).
5. **OpenAPI stays served anonymously in Production** (SDK consumers and the contract test use it). 12a adds a test that the document and `/health/ready` reveal no secrets.
6. **The carried-forward PHASE-03/04 items fold into 12a:** the last-admin guard reads the actor after the lock (or re-checks) with a deterministic lock-path test; a double-revoke end-to-end test (or an xmin token) on `ProductApiKey`; batched forced-tag-delete ticket loads and batched notification-preference product lookups, with a cap on the list. The GIN search-plan check moves to 12c with the load run.
7. **Scan policy: fail CI on High/Critical from day one.** `dotnet list package --vulnerable --include-transitive` fails on any vulnerability; the Trivy image scan fails on High/Critical, with a `.trivyignore` for documented waivers. Nistify is optional and not wired. **Amended 2026-10-09:** no SBOM is published (the sbom.yml workflow was removed before merge as excessive for 0.x; generate one on demand with `trivy image --format cyclonedx` (CycloneDX) if ever needed). The fail-closed scans stand.

**Technical rulings (12a design; approved when the owner approved the plan)**
- **The review document** is `docs/security/SECURITY-REVIEW.md`, a living document with a "Release 0.3.0" section (not `SECURITY-REVIEW-1.0.md`).
- **Findings are evidence-based.** Each checklist item cites the test or tests that prove it, or records a finding with severity and status.
- **The conformance gate (T19)** is an Architecture test that reflects controller actions, hub methods and hosted loops and compares them to the catalog tables in `02-ARCHITECTURE.md`, failing with a diff; its output is the report.
- **Shared fixtures.** The XSS and hostile-upload corpora become shared test fixtures.

**Spec corrections (where the spec and D-051 differ, D-051 wins)**
- `v1.0.0` becomes `v0.3.0` as the phase's end state (Objective, Deliverables, T14, T21, Success Criteria, Handoff); `v1.0.0-rc.N` is dropped; T14 deploys "the latest published tag", not `v0.1.0`.
- `MessageThread` is `MessageBubble` (`Features/Tickets/MessageBubble.razor`). The Admin rule lives in `tests/TechStrap.Admin.Tests/MarkupStringSiteTests`; the Portal rule in Architecture.Tests `PortalRules`.
- API keys are looked up by the full SHA-256 hash (`src/TechStrap.Api/Security/ProductApiKeyValidator.cs`), not by prefix; the stored prefix is for display, audit and the rate-limit partition.
- Customer access tokens are compared by a hashed database lookup (no `FixedTimeEquals` on that path); the review records why that is acceptable.
- `docs/self-hosting.md` and `docs/self-hosting-authentik.md` become the existing `docs/self-hosting/` folder (`DEPLOYMENT.md` and `AGENT-AUTHENTICATION.md` exist); 12b decides the file names.
- The tooling assumption is confirmed: `dotnet list package --vulnerable`, Trivy; Nistify optional (no SBOM published, see the amendment to decision 7).

### Alternatives Considered
- **`v1.0.0` now.** Rejected: the API lock is a separate decision, and packages stay 0.x until then.
- **One pull request for the whole phase.** Rejected: the UAT gates (Authentik setup, the 48 h soak) would block the hardening work.
- **Report-only scans.** Rejected: a scan that cannot fail the build is not a gate.
- **Hiding OpenAPI in Production.** Rejected: SDK consumers and the contract test use the served document.

### Consequences
- **Spec and docs.** `PHASE-12-release-hardening.md` carries a Corrections block; the roadmap and the discovery index show row 12 as in progress and carry the v0.3.0 wording.
- **Owner action.** Action #7 (Authentik clients and groups) is needed before 12c's T14.
- **Later decision.** `v1.0.0` waits for a decision that locks the Contracts public surface.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-12 planning)
- **Approved on:** 2026-10-08

### 12b rulings (2026-10-09)

Recorded at the close of the 12b pull request (docs and scripts: P12-T11, T13, T16, T17). Append-only: the text above is not edited.

- **Load suite layout.** The k6 suite lives in `tests/load/` with the thresholds encoded in the scripts and a runner, `scripts/Invoke-LoadTest.ps1`. It is not in CI (static Pester pins only); the on-demand smoke is recorded in `tests/load/README.md` and the real run is 12c (T12). The k6 image is pinned to `grafana/k6:2.1.0`, and `.gitattributes` keeps `tests/load/**` LF.
- **Per-IP load.** The runner rotates `X-Forwarded-For` over a /24 and is trusted as a proxy; the spike is pinned to one IP so the 429 behaviour is visible. The portal-form arithmetic uses 60 IPs. The first arithmetic applied the public read limit (10 per 60 s) to public intake; the applicable limit is public submit, 5 per 600 s per IP, so at the default profile about 75% of public-intake calls (about 7.5% of all requests) answer 429 by design, and `TS_IP_COUNT=240` keeps public intake within its limit. The sustained composer therefore relies on `server_errors` (5xx or no response, status 0 included), `checks` and p95 < 500 ms, and does not use `http_req_failed` (it counts 429 as failed), exactly like the spike. The budget of 0 server errors is unchanged. Forwarded-IP trust was proven locally for the Api; for the Portal (it trusts only `REVERSE_PROXY_CIDR`) it is documented in `tests/load/README.md` and proven by the 12c run.
- **Outbox and dead letters** are measured by SQL after the run, not by the k6 scripts.
- **Backup and restore.** `deploy/backup.sh` and `deploy/restore.sh` are bash scripts: `pg_dump -Fc` run inside `postgres:17`, volumes archived with `alpine tar`, encryption with `openssl enc -aes-256-cbc -pbkdf2` (no new tool to install; `age` is optional in the runbook), `--keep-days` retention and `--dry-run`. Restores go to a scratch compose project; `--db-url` restores require `--yes` and `--overwrite` clears the target volume first.
- **Rehearsal in 12b, drill in 12c.** The rehearsal on the dev stack is part of 12b (backup 7.3 s, restore 13.2 s into `techstrap-restore`, migration, ticket and attachment counts matched, refusals exercised; recorded in the runbook). The restore drill from a UAT backup (T15), the `--db-url` promotion path and the systemd timer are 12c.
- **File names.** The guides are `docs/self-hosting/SELF-HOSTING.md` and `docs/self-hosting/AUTHENTIK.md` (not `docs/self-hosting.md` and `docs/self-hosting-authentik.md`), the runbook is `docs/runbooks/backup-restore.md`, and results go to `docs/load-test-results.md`. The `syntax-circus-authentik` repository is private, so the Authentik guide is self-sufficient and does not depend on it. The groups fallback mapping uses the scope name `profile` (Authentik emits only mappings whose scope the client requests) or the operator appends `AUTH__SCOPES__0=groups`; the provider flow is verified against a live Authentik in 12c (T14).
- **Corrections to earlier wording.** "Attachments are append-only" is wrong: hard delete and requester erase remove files, and a restore resurrects them, so erasures are re-run from `admin_events` after a restore. The Caddy request-body limit is `26MiB`, not `26MB`: Caddy's MB is 10^6 bytes, too small for a 25 MiB submission.
- **Pin updates.** The example image tags in `deploy/.env.uat.example` and `deploy/.env.production.example` are `0.2.0`; the compose-inputs table marks all 12 `${NAME:?}` keys required, pinned from `deploy/docker-compose.yml`; the 12a close-out pins now read "12a merged (PR #28)" and the unticked-task set moved to the new 12b close-out block. Versioning is unchanged: PHASE-12 ends at `v0.3.0`.
- **Images are `linux/amd64` only (owner decision 2026-10-09, after 12b).** The `v0.2.1` release (the Admin OIDC scheme-name fix, PR #30) failed three times before building anything: `docker/setup-qemu-action` pulls `tonistiigi/binfmt` from Docker Hub anonymously and GitHub's shared runners exhaust Docker Hub's anonymous pull limit (`toomanyrequests`). The owner chose to drop arm64 rather than add Docker Hub credentials: `release.yml` and the CI docker job use the daemon's own BuildKit (`driver: docker`) and pull nothing from Docker Hub; the base images come from `mcr.microsoft.com`. `Build-TechStrapDocker.ps1` keeps its two-platform default for local use. arm64 may return on native arm64 runners (free for public repositories), never through QEMU. Supersedes the "linux/amd64 and linux/arm64" portability line in 01-REQUIREMENTS and the package map row for the images.

## D-052: PHASE-11f: Portal landing page and product logos (TECHSTRAP_PORTAL_LANDING, ListedOnLanding, Tagline, uploaded logo over the KB-image pipeline; amends D-045)

- **Status:** Approved (owner 2026-10-09; technical rulings at PHASE-11f plan review)
- **Date:** 2026-10-09
- **Owner:** Jon Seeley
- **Related artifacts:** D-043, D-044, D-045, D-050, D-051, PHASE-11f, PHASE-12, `docs/superpowers/plans/2026-10-09-phase-11f-landing-and-logos.md`, `docs/self-hosting/SELF-HOSTING.md`, `docs/security/SECURITY-REVIEW.md`

### Context
D-045 made the Portal root a neutral page unless `TECHSTRAP_PORTAL_DEFAULT_PRODUCT` redirects it, so that no product could be enumerated. On the first UAT deploy (2026-10-09, after 12b) the owner saw `support.syntaxcircus.com` as a bare page with a product configured and asked for the opposite for their company:
- **A landing page that lists the products**, configurable per deployment, with a flag on the product to keep one off the list.
- **A short tagline per product** on its card.
- **An uploaded logo per product**, superseding the linked logo URL, instead of linking to an image hosted elsewhere.
- Keys were already public through the sitemap and the anonymous product list, so the enumeration argument of D-045 no longer carries weight for active products.

### Decision
**Owner decisions (2026-10-09)**
1. **PHASE-11f ships before PHASE-12c**, so the UAT soak and `v0.3.0` include it.
2. **A card shows name, logo and a new tagline.**
3. **Products with their own portal host are listed** and their card links to `https://{host}/`.
4. **Logos are PNG, JPEG or WebP, at most 1 MiB.** No SVG (script-bearing, would need a sanitiser), no GIF.
5. **D-052 amends D-045.** The root still shows the neutral page by default; `TECHSTRAP_PORTAL_LANDING=Products` lists the active, listed products.

**Technical rulings (proposed in the 11f plan; approved with it)**
- **Landing mode is configuration, listing is data.** `TECHSTRAP_PORTAL_LANDING` (`Neutral` | `Products`, default `Neutral`; an enum, so `appsettings.json` carries the default) in `PortalOptions`; `Products` with a non-blank default product is a startup validation error naming both keys. `Product.ListedOnLanding` (default true) is edited in the Admin.
- **Unlisted means the landing card only.** The product keeps `/p/{key}`, its host and its sitemap entries; a product that must not be public is deactivated. The public list `GET api/public/products` keeps returning every active product (the host map and the sitemap depend on it) and each summary carries `ListedOnLanding`, `Tagline`, `LogoUrl` (effective) and `AccentColour`; the Portal filters for the landing only. `GET api/public/products/{productKey}` is unchanged.
- **Hosted products link to their host** through `PortalLinks.ForListedProduct` and `PortalLinks.Absolute`; the list renders only when `ProductHostContext.IsProductHost` is false (the middleware already rewrites a product host's `/`). Fail soft: an empty list, a 429 or an Api failure renders the `Neutral` copy with 200.
- **SEO and cache.** In `Products` mode the root has an indexable `SeoHead` with the canonical `/`, the sitemap lists `/`, and the root is output-cached with `Cache-Control: public, max-age=60` for `/` with no query string only. `PortalHeaderRules.Rules` takes `PortalOptions`; the output-cache policy reads the options from the request services. No form, so no antiforgery cookie.
- **Tagline.** `ProductBranding.Tagline` (`string?`, trimmed, plain text, no line breaks, `DomainLimits.TaglineMaxLength = 160`, errors `tagline-invalid` (a control character) and `tagline-too-long` (over 160), target `tagline`), mirrored by `BrandingRules.IsAcceptableTagline` with a parity test; part of branding equality.
- **The uploaded logo is a file name.** `ProductBranding.UploadedLogo` holds `{32 hex}.{png|jpg|webp}` under `product-logos/` on the storage volume (Api only, D-043). `IProductLogoUrls.UrlFor(name)` (Application) builds the absolute URL at read time: the Api from `TECHSTRAP_API_PUBLIC_URL`, the Worker from its new optional `TECHSTRAP_API_PUBLIC_URL` (null when blank, so emails keep the linked logo). The Worker never references the Api project. Effective logo = uploaded URL ?? `LogoPath`, computed in Application mapping (`ProductBrandingDto.UploadedLogoUrl` read-only; the public DTOs' `LogoPath`/`LogoUrl` carry the effective value), so the Portal, the Open Graph image, the Admin preview and `EmailBranding` need no new logic. `ProductBranding.CreateForUpdate` carries `UploadedLogo` over, so `PUT api/products/{id}` never drops it.
- **Store.** `IProductLogoStore`/`ProductLogoStore` mirror the KB image store: both lengths capped at `ProductLogoLimits.MaxBytes = 1 MiB` (`product-logo-too-large`), type from the bytes via `KbImageSignatures.Identify` restricted to png, jpeg and webp (gif, SVG, HTML and zero bytes answer `product-logo-type-not-allowed`), version 7 GUID names, delete-on-failed-store. The capped read and sniff move into a shared `CappedImageIntake`; `ProductLogoName` and `ProductLogoLimits` live in Contracts for the Admin pre-check. Upload: store, set, commit, then delete the old file best effort; a failed commit deletes the new file. Remove: clear, commit, delete best effort. Both audited as `ProductUpdated` with `changed: ["uploadedLogo"]`.
- **Routes.** `POST api/products/{id:guid}/logo` (Admin, multipart `file`, `[ReadFormBeforeBinding]`, `RequestSizeLimit(MaxBytes + 1 MiB)`, 200 `ProductDto`) and `DELETE api/products/{id:guid}/logo` (Admin, 200 `ProductDto`), in the D-022 list and 02-ARCHITECTURE 7.1 with `UploadProductLogoRequestHandler` and `RemoveProductLogoRequestHandler`; anonymous `GET`/`HEAD /product-logos/{name}` mirrors `/kb-images/{name}` (nosniff, immutable, CORP, sandbox, 404 `no-store`) as a 7.6 static exempt route.
- **Contracts 0.3.0.** Trailing optional parameters: `ProductDto(..., bool ListedOnLanding = true)`, `CreateProductRequest(..., bool? ListedOnLanding = null)` (null = true), `UpdateProductRequest(..., bool? ListedOnLanding = null)` (null = unchanged), `ProductBrandingDto(..., string? Tagline = null, string? UploadedLogoUrl = null)`, `ProductBrandingRequest(..., string? Tagline = null)`, `PublicProductDto(..., string? Tagline = null)`, `PublicProductSummaryDto(..., string? Tagline = null, string? LogoUrl = null, string? AccentColour = null, bool ListedOnLanding = true)`. The binary break (constructor and `Deconstruct` signatures) is named in the README `### 0.3.0` Version notes; the package is published by the `v0.3.0` tag at the end of 12c; no shims (D-050 rule).
- **Admin.** Tagline field, "Listed on the landing page" checkbox on create and edit (create sends the value explicitly), `ProductLogoUploadButton` on edit only (preview, upload, remove; client-side pre-check; the response's `UploadedLogoUrl` and `Version` are spliced into the live model so pending edits survive and the next save does not 409); the create form says to save first; a "Listed" column in the list.
- **Persistence.** `tagline varchar(160) null`, `uploaded_logo varchar(64) null`, `listed_on_landing boolean not null default true`; migration `AddProductLandingAndLogo`.
- **Security.** The upload checklist rows of the security review cite the new tests; SR-05 notes that logo keys are public by design, as KB image keys are; the hostile corpus gains a `productLogo` expectation. A Production Api behind plain http shows no uploaded logo anywhere (https-only logo rules in the Portal and the email layout); documented.
- **Tests.** `scripts/tests/RepositoryDocs.Tests.ps1` pins this decision, the D-045 amendment, the roadmap and discovery rows and the spec ticks.

### Alternatives Considered
- **Hiding an unlisted product from the sitemap and the public list too.** Rejected: the key stays reachable, so it would be theatre, it would drop the product's KB from search, and the host map needs every hosted product.
- **A separate landing endpoint and DTO.** Rejected: the existing list already feeds the sitemap and the host map; four trailing fields on the summary are cheaper than a second endpoint, a second row in the catalog and a second test surface.
- **Storing the uploaded logo's absolute URL** (as KB Markdown does, D-044). Rejected: an Api hostname move would orphan every logo; the Worker's optional setting is the price.
- **Allowing SVG.** Rejected by the owner: a sanitiser and a sandboxed serving policy for a logo format nobody asked for.
- **A `bool` on `UpdateProductRequest`.** Rejected: an omitted property would read as false and unlist products saved by a 0.2.0 client; `bool?` with null = unchanged follows `PortalHost`.

### Consequences
- **Spec and docs.** `PHASE-11f-landing-and-logos.md`; D-045 carries an "Amended by D-052" line; `02-ARCHITECTURE.md` (7.1, 7.6, 8.2, 11.2, the products column list), `05-SCHEMA.md`, `SELF-HOSTING.md` (two settings, the `/product-logos/` proxy path, the plain-http note), `DEPLOYMENT.md`, the backup runbook (prefix and ownership check), `SECURITY-REVIEW.md`, `ADMIN-APP.md`, `PORTAL-APP.md`, the roadmap and the discovery index.
- **Operator work.** Set `TECHSTRAP_PORTAL_LANDING=Products` on the Portal to use the landing; set `TECHSTRAP_API_PUBLIC_URL` on the Worker for uploaded logos in emails; proxy `/product-logos/` on the Api site as `/kb-images/`. `backup.sh` needs no change.
- **Known limits.** An orphan file can remain if the process dies between the store and the commit (no sweeper, as D-044); an unlisted product remains discoverable by design; the tagline is shown on the card only in this phase.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-11f planning)
- **Approved on:** 2026-10-09

## D-053: PHASE-11g/11h: Portal theme packs and per-product skins (structured tokens, five packs, Admin-editable default, SkinResolver; amends D-031, D-045 and the BRAND.md portal rules)

- **Status:** Approved (owner 2026-10-10, plan approval)
- **Date:** 2026-10-10
- **Owner:** Jon Seeley
- **Related artifacts:** D-025, D-031, D-041, D-045, D-050, D-052, PHASE-11g, PHASE-11h, `docs/BRAND.md`, `docs/architecture/UX-BRIEF-portal.md`, the sibling repository `dragon-poop` (validation skin)

### Context
After the first UAT deploy the owner found the Portal bland even as a default and asked for built-in theme packs (with a per-product override) and per-product skins that match a product's marketing look. The current rules allow a product to set only three accent variables, keep the Portal light only, and forbid per-product stylesheets.

### Decision
**Owner decisions (2026-10-10)**
1. Skins are **structured tokens**, not raw CSS, validated against dragon-poop's skin to find holes early.
2. The **deployment default pack** is an Admin-editable setting, not an environment variable.
3. **Five packs** ship first: Classic, Slate, Paper, Contrast, Midnight (a dark pack).
4. **Emails take colours only**; no background images in v1.
5. **Two pull requests**: 11g the engine and Portal rendering, 11h the Admin editor and the dragon-poop skin.

**Technical rulings**
- **Token grammar and resolver.** `ProductSkin` (pack key, eight colours, two fonts from `SkinFonts`, radius, border width, shadow, button and header presets) with every field optional. `SkinResolver` in Contracts merges Classic < deployment default < product pack < product tokens and derives the on-colours and inks; it is the only implementation, and `ProductAccent.TryDerive` remains its light-background case.
- **Contrast.** `Ink` on `Background` and `Surface`, and `Muted` on `Background`, at least 4.5:1; derived `OnBrand` on `Brand` at least 4.5:1; `Focus` at least 3:1 against `Background` (`Border` is decorative and has no rule). A failing save is 400 `skin-contrast-invalid` naming the pair; a failing stored value is never rendered. Amends D-031 only for products that set skin tokens.
- **Delivery.** Resolved variables travel in the existing single `style=` carrier (`AccentScope`, class `ts-accent-scope`); presets travel as `data-ts-*` attributes; packs are resolved on the server into custom properties and SCSS holds only preset rules. No `<style>`, no new inline-style element, no CSP change.
- **Neutral pages** use the deployment default pack only, so their body stays identical for every address (D-045 unchanged in that respect).
- **Storage and Api.** `Product.Skin` as one `skin` column validated by the Contracts grammar in Application (Domain guards only the JSON length); singleton `SiteSettings` (`site_settings`, seeded `classic`); `GET/PUT api/settings/site` (Admin), `GET api/public/site` (anonymous, cached 300 s); product DTOs gain `Skin` as trailing optionals (null means unchanged on update). Contracts 0.4.0.
- **Amended text.** BRAND.md "Portal tokens (light only in v1)", the mascot-palette prohibition's portal-theme sentence, and the "Product-accent override rule"; UX-BRIEF-portal "light-only in v1" and "do not generate per-product stylesheets"; the 2026-10-02 light-only decision. Each carries an "Amended by D-053" line. Still forbidden: TechStrap colours, mascot or name on a customer page beyond the footer, raw CSS, background images (deferred), a theme that follows the OS.
- **Known limits.** Pixel art and composition, hero and marketing sections, copy and voice, multi-layer ornament, a product's own display face outside the built-in list, WIP-label provenance, background images, third-party chrome.
- **Tests.** `scripts/tests/RepositoryDocs.Tests.ps1` pins this decision, the amended lines and the roadmap rows.

### Alternatives Considered
- **Raw per-product CSS.** Rejected: needs a CSS sanitiser, a new XSS corpus and a review row, and can break layout and accessibility; the CSP only permits a same-origin stylesheet, which would make skin changes a file-serving feature.
- **A per-product stylesheet route with hashed URLs.** Rejected for v1: legal under the CSP, but tokens already express what dragon-poop's shared chrome needs, and a closed grammar keeps contrast enforceable.
- **Environment variable for the default pack.** Rejected by the owner: an Admin should change it without a restart.
- **Only the accent trio as today.** Rejected: it cannot change neutrals, type, radius or depth, which is the reported problem.

### Consequences
- **Spec and docs.** `PHASE-11g-theme-packs.md`; amended BRAND.md and UX-BRIEF lines; SELF-HOSTING and PORTAL-APP in T08; `05-SCHEMA.md`, the architecture routes and the roadmap.
- **Operations.** The default pack is set in the Admin after 11h ships; until then `PUT api/settings/site` is the way.
- **Known limits.** See above; 11h records the gaps found by building the dragon-poop skin.

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-11g planning)
- **Approved on:** 2026-10-10
