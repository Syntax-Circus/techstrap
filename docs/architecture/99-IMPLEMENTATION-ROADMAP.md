# 99 - Implementation Roadmap

Status: Draft for review (2026-10-02). Entry point for implementation. Companion artifacts: [00-DISCOVERY-INDEX.md](00-DISCOVERY-INDEX.md), [01-REQUIREMENTS.md](01-REQUIREMENTS.md), [02-ARCHITECTURE.md](02-ARCHITECTURE.md), [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md), [04-DECISION-LOG.md](04-DECISION-LOG.md).

## 1. Purpose

This file tells an implementer (human or agent) in what order to build TechStrap, which tasks each phase contains, how to verify work, when a phase is done, how a phase is handed off, and which actions only the owner can take. It does not restate phase content. The authority order when documents disagree is: `04-DECISION-LOG.md`, then `02-ARCHITECTURE.md` (routes, handler names, Contracts type names, abstractions), then `03-PACKAGE-MAP.md` (versions), then the PHASE document, then the UX briefs.

Cross-cutting conventions every phase follows (fixed during the consistency review):

- Routes, handler names and Contracts type names come from `02-ARCHITECTURE.md` section 7 and 8.
- Header names are Contracts constants and are the same everywhere: `X-Api-Key`, `X-Ticket-Token`, `Idempotency-Key`.
- The portal base URL variable is `TECHSTRAP_PORTAL_PUBLIC_URL`; the storage mount is `/app/storage` (volume `techstrap-storage`), mounted by the API only (D-043).
- Handlers accept `TechStrap.Contracts` request records directly (D-016); Admin and Portal reference only Contracts and Hosting (D-040).

## 2. Phase order

| # | Phase | Depends on | Unblocks | Parallelism | Key decisions | Status |
| --- | --- | --- | --- | --- | --- | --- |
| 01 | [Foundation](PHASE-01-foundation.md) | none | all | Must go first | D-003, D-006, D-013, D-019 | Complete (CI/release verification pending) |
| 02 | [Brand and UX](PHASE-02-brand-and-ux.md) | 01 | 07, 09 | Runs alongside 03 to 06 (no shared files); blocks the UI phases | D-002, D-023, D-024, D-025 | Complete |
| 03 | [Domain and persistence](PHASE-03-domain-and-persistence.md) | 01 | 04 | Alongside 02 | D-009, D-010, D-011, D-026, D-027, D-028 | Complete |
| 04 | [Agent auth and admin config](PHASE-04-agent-auth-and-admin-config.md) | 03 | 05 | Alongside 02 | D-001, D-004, D-016, D-022, D-029 (roles from IdP groups only), D-030 (tag delete), D-031 (accent format only) | Complete |
| 05 | [Intake, email and worker](PHASE-05-intake-email-worker.md) | 04 | 06, 11 | Alongside 02 | D-001, D-010, D-012, D-014, D-019, D-020 | Complete (PR #5 merged) |
| 06 | [Ticket operations](PHASE-06-ticket-operations.md) | 05 | 07, 08, 09 | Alongside 02 and 11 | D-006, D-008, D-009, D-022, D-035, D-036, D-037, D-038, D-039 | Complete (PRs #6, #7 and #8 merged) |
| 07 | [Admin app](PHASE-07-admin-app.md) | 02, 06 | 08 (editor UI), 10 | 11 alongside; 08 API work alongside | D-017, D-022, D-040, D-041 | Complete: 07a merged (PR #9), 07b merged (PR #10), 07c merged (PR #11); owner action 7 (Authentik) still open, so P07-T02 stays unticked |
| 08 | [Knowledge base](PHASE-08-knowledge-base.md) | 06 (07 for the editor UI) | 09 | API tasks T01 to T12 alongside 07; editor tasks wait for 07 | D-011, D-014, D-021, D-044 | PHASE-08 merged (PR #13): the API (Tasks 1-8) and the Admin (editor, categories, article picker); the owner's manual checks are open |
| 09 | [Public portal](PHASE-09-public-portal.md) | 02, 06, 08 | 12 | 10 and 11 alongside | D-002, D-017, D-019, D-045 | 09a merged (PR #14); 09b merged (PR #15); 09c merged (PR #16); 09d merged (PR #17); PHASE-09 complete: the owner evidence for P09-T16 (axe, Lighthouse, the JavaScript-off walk, screenshots) and the compose run of P09-T18 are open, P09-T20 is deferred |
| 10 | [Live updates](PHASE-10-live-updates.md) | 07 | 12 | 08, 09, 11 alongside | D-007, D-018, D-046 | 10a merged (PR #18); 10b merged (PR #19); PHASE-10 complete: the owner's manual checks with a real identity provider (two browsers, a worker auto-close, the kill switch) are open |
| 11 | [Client SDK](PHASE-11-client-sdk.md) | 05 | 12 | Alongside 06 to 10 | D-005, D-020, D-047, D-048, D-049 | 11a merged (PR #20); 11b merged (PR #21); 11c merged (PR #22); v0.1.0 published 2026-10-08 (PHASE-11 complete); T05 (attachments) deferred to 11d, which first needs multipart intake |
| 11e | [Product hosts](PHASE-11e-product-hosts.md) | 09, 05, 04, 07 | 12 | After 11 | D-050 | 11e merged (PR #25): T01 to T07 |
| 12 | [Release hardening](PHASE-12-release-hardening.md) | all | v1.0.0 (v0.3.0 per D-051; 1.0.0 is a later API-lock decision) | Last; security, load, restore and UAT tasks can overlap once their inputs exist | D-003, D-022, D-051 | D-051 recorded; 12a (hardening) in progress |

Edges: 01 to 02 and 03; 03 to 04 to 05 to 06; 05 to 11; 02 and 06 to 07; 06 and 07 to 08; 02, 06 and 08 to 09; 07 to 10; all to 12.

Suggested order for one implementer: 01, 03, 04, 05, 06, 02 (any time before 07), 07, 08, 09, 10, 11, 12. With two or more implementers: 02 and 11 (after 05) run beside the backend chain, and 08 API, 09, 10 split after 07.

## 3. Task index

Task IDs and one-line titles from each PHASE document. Each task's dependencies and validation are in the PHASE document and are the source of truth.

### PHASE-01 Foundation

| ID | Title |
| --- | --- |
| P01-T01 | Create the solution and the 10 `src/` and 5 `tests/` project skeletons with the reference direction |
| P01-T02 | Add `global.json`, `Directory.Build.props/targets`, GitVersion, editorconfig and ignore files |
| P01-T03 | Write `Directory.Packages.props` locking every version from the package map |
| P01-T04 | Write `ProjectReferenceDirectionTests` (architecture tests) |
| P01-T05 | Write `HandlerConstructorDependencyTests` and `HandlerShapeTests` |
| P01-T06 | Write `HealthEndpointTests` and wire cross-cutting packages and health in the API |
| P01-T07 | Wire the same packages and health endpoints in Admin, Portal and Worker |
| P01-T08 | Add `.env.example` per host and confirm `.env.local` is ignored |
| P01-T09 | Add `TechStrapDbContext` and `MigrationStartupTests` |
| P01-T10 | Generate the initial empty migration and add the migrator to API startup only |
| P01-T11 | Create `PostgresFixture` and `PostgresIntegrationTestBase` |
| P01-T12 | Add `IDevelopmentDataSeeder`, the no-op implementation and `DevSeedGatingTests` |
| P01-T13 | Add forwarded-headers and public rate-limit scaffolding in the API |
| P01-T14 | Write the four Dockerfiles |
| P01-T15 | Write `Build-TechStrapDocker.ps1` with a `-DryRun` switch |
| P01-T16 | Write the three compose files and `.env.production.example` (pinned subnet; owner action to register it) |
| P01-T17 | Add the CI workflow (build, test, docker build) |
| P01-T18 | Add the release workflow (GHCR push on `v*`) |
| P01-T19 | Add LICENSE, README, CONTRIBUTING and SECURITY |
| P01-T20 | Run the clean-clone verification and mark PHASE-01 complete |

### PHASE-02 Brand and UX

| ID | Title |
| --- | --- |
| P02-T01 | Audit identity inputs and write the identity sections of `BRAND.md` |
| P02-T02 | Produce 2 to 3 candidate visual directions with rendered mockups |
| P02-T03 | Record the chosen direction and finish `BRAND.md` |
| P02-T04 | Finalise `UX-BRIEF-admin.md` and `UX-BRIEF-portal.md` |
| P02-T05 | Add `libman.json` and `sasscompiler.json` to Admin and Portal |
| P02-T06 | Implement `_tokens.scss` and `app.scss` in each app |
| P02-T07 | Build the Development-only style-guide page in each app |
| P02-T08 | Add logo, wordmark and favicon assets |
| P02-T09 | Prove the product-accent override and contrast rule |
| P02-T10 | Run the visual critique loop and tick the Definition of Done |

### PHASE-03 Domain and persistence

| ID | Title |
| --- | --- |
| P03-T01 | Ticket status enum and transition rules with `TicketStatusTransitionTests` |
| P03-T02 | Product, API key, tag and agent domain types |
| P03-T03 | Requester, message, attachment and access token domain types |
| P03-T04 | Ticket aggregate creation and mutations raising `TicketEvent`s |
| P03-T05 | KB category, article and ticket-article domain types |
| P03-T06 | `AdminEvent` and `EmailOutboxItem` domain types |
| P03-T07 | Declare the Application abstractions and `AbstractionShapeTests` |
| P03-T08 | `SchemaConventionTests` and EF configurations for all entities |
| P03-T09 | Generate the migrations with `dotnet ef` |
| P03-T10 | `TicketNumberAllocator` with concurrency tests |
| P03-T11 | Full-text search columns, GIN indexes and search tests |
| P03-T12 | `UnitOfWork` with atomicity and concurrency tests |
| P03-T13 | The six repositories with integration tests |
| P03-T14 | `IEmailOutbox` and `EmailOutboxStore` (`SKIP LOCKED` claiming) |
| P03-T15 | Extend the dev seeder with demo data |
| P03-T16 | ER diagram and schema notes |
| P03-T17 | `public_display_name` on Agent, `AgentPublicIdentity` resolver and migration (D-024) |

### PHASE-04 Agent auth and admin config

| ID | Title |
| --- | --- |
| P04-T01 | Contracts DTOs and requests for agents, products, keys, tags, admin events |
| P04-T02 | `AgentAccessOptions`, JWT bearer wiring, the two policies and `AgentAuthTests` |
| P04-T03 | ASP.NET `ICurrentUserService` |
| P04-T04 | `GetCurrentAgentRequestHandler` and `AgentsController.GetMe` |
| P04-T05 | `ListAgentsRequestHandler` and `UpdateAgentRequestHandler` with audit and last-admin protection |
| P04-T06 | `UpdateNotificationPreferencesRequestHandler` |
| P04-T07 | Product handlers (list, get, create, update with branding) and `ProductsController` |
| P04-T08 | `IApiKeyHasher` implementation |
| P04-T09 | API key handlers (list, create, revoke) |
| P04-T10 | Tag handlers and `TagsController` |
| P04-T11 | `IAdminEventRepository` and `ListAdminEventsRequestHandler` |
| P04-T12 | `ResultMappingTests` and `CancellationPropagationTests` for all controllers |
| P04-T13 | Extend architecture tests with controller and handler rules |
| P04-T14 | Update `.env.example`, OpenAPI and the dev seeder; self-host auth note |
| P04-T15 | `UpdateMyProfileRequestHandler` and public display name contracts (D-024) |
| P04-T16 | Production exception handler with a plain error page in Admin and Portal |

### PHASE-05 Intake, email and worker

| ID | Title |
| --- | --- |
| P05-T01 | Contracts DTOs, limit constants and header-name constants |
| P05-T02 | `IAccessTokenService` |
| P05-T03 | `IHtmlSanitizer` adapter with an XSS corpus |
| P05-T04 | `IAttachmentStore` over `SyntaxCircus.Storage` |
| P05-T05 | `IEmailTemplateRenderer` (confirmation template) |
| P05-T06 | `SubmitTicketRequestHandler` (test-first) |
| P05-T07 | `SubmitTicketIntegrationTests` (single transaction, rollback, concurrency) |
| P05-T08 | API-key authentication scheme, `IntakeController` and `PublicIntakeController` |
| P05-T09 | Rate limiting per the client-IP pattern |
| P05-T10 | `GetPublicProductRequestHandler` and `PublicProductsController` |
| P05-T11 | `DrainEmailOutboxHandler` (test-first) |
| P05-T12 | Worker `EmailOutboxWorker` |
| P05-T13 | `EmailDrainIntegrationTests` against an SMTP capture container |
| P05-T14 | Extend compose files (Worker, Mailpit, storage volume, env keys) |
| P05-T15 | Extend architecture tests for the new handlers and controllers |
| P05-T16 | Dev seeder keys and the intake smoke script |
| P05-T17 | `Idempotency-Key` support on API-key intake (D-020) |
| P05-T18 | Email renderer: resolved agent name and Powered-by link/setting (D-024) |

### PHASE-06 Ticket operations

| ID | Title |
| --- | --- |
| P06-T01 | Contracts DTOs and requests for tickets, timeline, customer view, dead letters |
| P06-T02 | `ITicketNotificationPlanner` and notification templates |
| P06-T03 | `ListTicketsRequestHandler` (views, filters, FTS, paging) |
| P06-T04 | `GetTicketRequestHandler` (detail and timeline) |
| P06-T05 | `AddAgentReplyRequestHandler` |
| P06-T06 | `AddInternalNoteRequestHandler` |
| P06-T07 | `ChangeTicketStatusRequestHandler` |
| P06-T08 | Assign, priority and product-move handlers |
| P06-T09 | Add and remove ticket tag handlers |
| P06-T10 | Mark-spam (Agent) and delete-ticket (Admin) handlers |
| P06-T11 | `EraseRequesterRequestHandler` |
| P06-T12 | `GetAttachmentRequestHandler` and `AttachmentsController` |
| P06-T13 | `GetCustomerTicketRequestHandler` |
| P06-T14 | `AddCustomerReplyRequestHandler` including follow-up on Closed |
| P06-T15 | `RequestNewAccessLinkRequestHandler` with rate limits |
| P06-T16 | Dead-letter list, retry and discard handlers |
| P06-T17 | `AutoCloseSolvedTicketsHandler` and the Worker `AutoCloseWorker` |
| P06-T18 | Wire `ITicketNotificationPlanner` into `SubmitTicketRequestHandler` |
| P06-T19 | Serilog PII redaction and customer-route rate limits |
| P06-T20 | Architecture tests, `TicketLifecycleEndToEndTests`, OpenAPI and env updates |
| P06-T21 | Spam view in `ListTickets` and idempotent Not spam (D-024) |
| P06-T22 | Resolved agent name in customer DTO and agent-reply email (D-024) |

### PHASE-07 Admin app

| ID | Title |
| --- | --- |
| P07-T01 | Admin `.env.example` keys and options classes |
| P07-T02 | Cookie and OIDC sign-in with token forwarding |
| P07-T03 | `TechStrap.Admin.Tests` project and `Shared/` primitives |
| P07-T04 | App shell: routes, layout, nav, not-found and no-access pages |
| P07-T05 | Typed clients for agents, products, tags and admin events |
| P07-T06 | Typed clients for tickets, attachments and dead letters |
| P07-T07 | Queue page, view tabs, filter bar and paging |
| P07-T08 | Ticket detail page and `TicketDetailPresenter` |
| P07-T09 | Ticket timeline and `TimelineEntryFactory` |
| P07-T10 | Message thread, attachment list and the `/attachments/{id}` pass-through (D-017) |
| P07-T11 | `ReplyComposer` (public reply and internal note) |
| P07-T12 | Ticket sidebar and tag picker |
| P07-T13 | Spam, delete and erase-requester flows (delete and erase Admin-only) |
| P07-T14 | Products and product editor pages |
| P07-T15 | API keys panel and one-time secret dialog |
| P07-T16 | Agents and notification preferences pages |
| P07-T17 | Tags page and admin events (audit) page |
| P07-T18 | Dead letters page |
| P07-T19 | Brand tokens, responsive layout and accessibility pass |
| P07-T20 | Compose service, Dockerfile verification and Admin architecture rules |
| P07-T21 | Optional Playwright smoke tests |
| P07-T22 | Spam view, Not spam key `u` (D-024) |
| P07-T23 | My settings public display name field with live preview (D-024) |

### PHASE-08 Knowledge base

| ID | Title |
| --- | --- |
| P08-T01 | Verify KB entities, constraints and FTS from PHASE-03 |
| P08-T02 | KB DTOs and constants in Contracts (including preview request and response) |
| P08-T03 | `IMarkdownRenderer`, sanitizer allow-list and `RenderKbPreviewRequestHandler` |
| P08-T04 | Agent article handlers (list, get, create, update) |
| P08-T05 | Publish and archive handlers |
| P08-T06 | KB category handlers |
| P08-T07 | `UploadKbImageRequestHandler` and `IKbImageStore` |
| P08-T08 | Public KB handlers (search, article, categories, sitemap) |
| P08-T09 | KB controllers with policies, rate limits and cache headers |
| P08-T10 | Validate linked article ids in `AddAgentReplyRequestHandler` |
| P08-T11 | KB seed data |
| P08-T12 | OpenAPI check for KB operations |
| P08-T13 | Admin `IKbClient` |
| P08-T14 | `IKbClient.PreviewAsync` for the editor preview (D-021) |
| P08-T15 | KB article list page and status badge |
| P08-T16 | Markdown editor and preview pane |
| P08-T17 | KB article editor page and presenter |
| P08-T18 | KB image upload button |
| P08-T19 | KB categories page |
| P08-T20 | Article picker and linked-article chips in the reply composer |

### PHASE-09 Public portal

| ID | Title |
| --- | --- |
| P09-T01 | `TechStrap.Portal.Tests` project, portal options, `.env.example` and constants |
| P09-T02 | Typed clients with client-IP forwarding (D-019) |
| P09-T03 | `BrandingThemeFactory`, layout and product scope resolution |
| P09-T04 | Wire `Blazor.Seo`, robots, sitemap, security headers |
| P09-T05 | App shell, not-found page and product home page |
| P09-T06 | Contact page, honeypot, attachments and the received page |
| P09-T07 | KB deflection island |
| P09-T08 | Customer ticket page and presenter with security headers |
| P09-T09 | Customer reply form including Closed follow-up flow |
| P09-T10 | Lost-link page |
| P09-T11 | `/t/{token}/attachments/{id}` pass-through adapter (D-017) |
| P09-T12 | KB home and category pages |
| P09-T13 | KB search page |
| P09-T14 | KB article page with SEO and JSON-LD |
| P09-T15 | Output caching for KB pages and sitemap |
| P09-T16 | Brand styling, responsive layout, no-JS and accessibility pass |
| P09-T17 | Token redaction in portal request logs |
| P09-T18 | Compose service and end-to-end rate-limit check |
| P09-T19 | Portal architecture rules |
| P09-T20 | Optional Playwright end-to-end test |
| P09-T21 | Contact-page prefill of subject, name and email (D-024) |
| P09-T22 | Powered-by link and `TECHSTRAP_PORTAL_SHOW_POWERED_BY` setting (D-024) |
| P09-T23 | Resolved agent name on the customer ticket view (D-024) |

### PHASE-10 Live updates

| ID | Title |
| --- | --- |
| P10-T01 | Live-update DTOs and hub constants in Contracts |
| P10-T02 | `ITicketChangeBroadcaster`, `ITicketPresenceStore` and the in-memory store |
| P10-T03 | `UpdateTicketPresenceHandler` |
| P10-T04 | `RelayTicketChangeHandler` |
| P10-T05 | `TicketHub` and the SignalR broadcaster |
| P10-T06 | Hub JWT query-token handling and log redaction |
| P10-T07 | Post-commit publishing interceptor (D-018) |
| P10-T08 | `PgNotifyTicketChangeBroadcaster` in the Worker |
| P10-T09 | `TicketChangeListener` hosted service |
| P10-T10 | Observability instruments |
| P10-T11 | Admin `ITicketLiveClient` |
| P10-T12 | Live connection indicator |
| P10-T13 | Queue live refresh and banner |
| P10-T14 | Ticket detail live refresh |
| P10-T15 | Presence bar and throttled composing signal |
| P10-T16 | Reverse-proxy WebSocket requirements and env keys |
| P10-T17 | End-to-end verification of a worker-originated update |

### PHASE-11 Client SDK

| ID | Title |
| --- | --- |
| P11-T01 | Shared SDK constants in Contracts |
| P11-T02 | `TechStrap.Client` project with pack metadata |
| P11-T03 | `TechStrapClientOptions` and `ApiKeyHandler` |
| P11-T04 | `ITechStrapClient.SubmitTicketAsync` and DI extension with resilience |
| P11-T05 | `TicketAttachment` helpers (deferred, D-047) |
| P11-T06 | `TechStrap.Client.Tests` with real-API integration and OpenAPI contract test |
| P11-T07 | `TechStrap.Client.Maui` project |
| P11-T08 | `MauiDeviceContextCollector` |
| P11-T09 | `IMauiTicketSubmitter` and `AddTechStrapMaui` |
| P11-T10 | `TechStrap.Contracts` pack metadata and README |
| P11-T11 | Per-package READMEs |
| P11-T12 | Console sample (and optional MAUI sample) |
| P11-T13 | `publish-nuget.yml` workflow |
| P11-T14 | Package content validation script |
| P11-T15 | Reserve package IDs and configure Trusted Publishing |
| P11-T16 | Publish `v0.1.0` and run the post-publish check |
| P11-T17 | Idempotent submit retries through `Idempotency-Key` (D-020) |

### PHASE-11e Product hosts

| ID | Title |
| --- | --- |
| P11e-T01 | `Product.PortalHost`, `HostNameShape`, mapping and migration `AddProductPortalHost` |
| P11e-T02 | Contracts and Api: `PortalHost` on the product DTOs, handler validation, public projections |
| P11e-T03 | Links: host-aware `PortalLinkOptions`, call sites, Worker compatibility, Admin "View on portal" |
| P11e-T04 | Admin: "Portal host" editor field and list column |
| P11e-T05 | Portal host resolution: `ProductHostMap`, `ProductHostContext`, `ProductHostMiddleware` |
| P11e-T06 | Portal links, SEO and cache: `PortalLinks`, per-host sitemap and canonical, vary by host |
| P11e-T07 | Docs and close-out: D-050 (amends D-002), deployment and Portal docs, pins |

### PHASE-12 Release hardening

| ID | Title |
| --- | --- |
| P12-T01 | Security-review checklist document |
| P12-T02 | Review and test the customer access-token path |
| P12-T03 | Review and test the API-key path |
| P12-T04 | Review and test upload paths |
| P12-T05 | Review and test sanitizer and rendering paths |
| P12-T06 | Audit authorization |
| P12-T07 | Verify privacy handling |
| P12-T08 | Verify headers, CSP, CORS, forwarded headers and error detail |
| P12-T09 | Dependency scan, image scan and SBOM |
| P12-T10 | Fix findings with regression tests |
| P12-T11 | k6 load scripts |
| P12-T12 | Execute load tests on UAT and record results |
| P12-T13 | Backup and restore runbook and scripts |
| P12-T14 | Deploy the release candidate to UAT |
| P12-T15 | Restore drill into a scratch stack |
| P12-T16 | `docs/self-hosting.md` |
| P12-T17 | `docs/self-hosting-authentik.md` |
| P12-T18 | 48-hour soak of the RC |
| P12-T19 | Architecture conformance gate |
| P12-T20 | Final documentation pass |
| P12-T21 | Tag and publish `v1.0.0` (v0.3.0 per D-051; 1.0.0 is a later API-lock decision) |

## 4. Standard validation commands

Run from the repository root. PHASE-01 creates the files these commands need; before then, only the documentation checks apply. Host ports are those published in `docker-compose.yml` (P01-T16); examples use `$API_PORT`, `$ADMIN_PORT`, `$PORTAL_PORT`, `$WORKER_PORT`.

| Purpose | Command | Expect |
| --- | --- | --- |
| Build, warnings as errors | `dotnet build TechStrap.slnx -warnaserror` (releases: add `-c Release`) | Exit 0; `TechStrap.Client.Maui` is part of the solution filter (single `net10.0` target, D-048) |
| Full test run | `dotnet test` | All pass; Docker must be running for Testcontainers |
| One test project | `dotnet test tests/TechStrap.Architecture.Tests` (or any test project) | Pass |
| One test class | `dotnet test --filter "FullyQualifiedName~MigrationStartupTests"` | Pass |
| Package versions | `pwsh ./scripts/Check-PackageVersions.ps1` | Exit 0: props match `03-PACKAGE-MAP.md`, no inline `Version` |
| EF drift | `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api` | Exit 0 (project paths are an **Assumption**; adjust to the final layout) |
| New migration (tool only) | `dotnet ef migrations add <Name> --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api` | Generated files only; never hand-edit |
| Version stamp | `dotnet msbuild src/TechStrap.Api -target:GetVersion -getProperty:GitVersion_SemVer` | Prints a SemVer |
| Compose config | `docker compose config` | Shows subnet `172.16.31.0/24` and `TRUSTEDPROXY__*` per host |
| Stack up | `docker compose up -d` then `docker compose ps` | Postgres 17 and four app containers healthy |
| Liveness | `curl -fsS http://localhost:$API_PORT/health/live` (repeat for Admin, Portal, Worker ports) | 200 |
| Readiness | `curl -fsS http://localhost:$API_PORT/health/ready` (and the Worker port) | 200 |
| OpenAPI | `curl -fsS http://localhost:$API_PORT/openapi/v1.json` | JSON listing the phase's routes |
| Image build script, dry run | `pwsh ./Build-TechStrapDocker.ps1 -DryRun` | Four `docker buildx build` commands |
| Image build, one target | `pwsh ./Build-TechStrapDocker.ps1 -Targets api` | Local `techstrap-api:<semver>` and `:latest` |
| Build script tests | `pwsh -Command "Invoke-Pester ./scripts"` (Pester from `03-PACKAGE-MAP.md`; path is an **Assumption**) | Pass |
| Non-root image | `docker run --rm --entrypoint id <image> -u` | `10001` |
| No tracked CSS | `git ls-files '*/wwwroot/css/*'` | Empty |
| Env file ignored | `git check-ignore src/TechStrap.Api/.env.local` | Prints the path |
| Package pack (PHASE-11) | `dotnet pack -c Release` | `.nupkg` and `.snupkg` for Contracts, Client, Client.Maui |
| Multi-arch images (tag builds) | `docker buildx imagetools inspect ghcr.io/syntax-circus/techstrap-api:<semver>` | Lists `linux/amd64` and `linux/arm64` |
| Doc links | check every relative link in `docs/architecture/*.md` resolves | No broken links |

## 5. Definition of done per phase

A phase is done only when all three groups below are satisfied and the evidence (command output) is in the PR description.

1. **Success criteria:** every item in the phase's "Success Criteria" section is ticked, and `dotnet build TechStrap.slnx -warnaserror` and `dotnet test` are green in CI.
2. **Boundary validation:** every item in the phase's "Boundary Validation" checklist is ticked or marked N/A with the reason already written in the document. In particular: each entry point delegates to exactly one named handler, handler constructors take only approved abstractions, cancellation reaches dependencies, expected outcomes have focused tests, and Admin and Portal reference only Contracts and Hosting (D-040).
3. **Docs updated:** the PHASE document's task checkboxes are ticked; `02-ARCHITECTURE.md` reflects any changed route, handler, abstraction or Contracts type; `03-PACKAGE-MAP.md` and `Directory.Packages.props` change together; any new or changed decision is in `04-DECISION-LOG.md` before the code relies on it; `.env.example` files and the OpenAPI document are current; the status column in section 2 and `00-DISCOVERY-INDEX.md` are updated.

Phase-specific gates (in addition to the phase's own criteria):

| Phase | Extra gate |
| --- | --- |
| 01 | Clean-clone build, test and `docker compose up` with all health checks; CI green; initial migration produced by the tool; subnet registry PR opened (owner action) |
| 02 | `BRAND.md` approved by the owner; both UX briefs final; tokens compile in both apps; accent contrast tests pass |
| 03 | Migrations apply on fresh Postgres 17; ticket numbers unique and gap-free under concurrency; every mutation writes its `TicketEvent` in the same transaction |
| 04 | JWT policies work against a test issuer; both key kinds can be created through the API; `ResultMappingTests` and `ControllerBoundaryTests` green |
| 05 | Intake by all three routes yields a branded confirmation through the Worker; two Workers never double-claim; idempotency tests green if D-020 is approved |
| 06 | `TicketLifecycleEndToEndTests` pass; customer endpoints give uniform 404 and 202; delete and erase are Admin-only, mark spam is Agent |
| 07 | Admin references only Contracts and Hosting (architecture test); a 409 never loses a reply draft; component tests in `TechStrap.Admin.Tests` green |
| 08 | XSS corpus passes through publish and preview paths; drafts and archived articles never leak publicly; image rules enforced |
| 09 | Token pages send `no-store`, `no-referrer`, `noindex`; rate limits see the real client IP; no-JS contact and reply flows work |
| 10 | A worker-originated change reaches an open admin queue; rollbacks never broadcast; hub rejects unauthenticated callers |
| 11 | Contract test proves the SDK matches `/openapi/v1.json`; packages pack with README and symbols; publish workflow verified by a dry run |
| 12 | No open High or Critical findings; load budgets met on UAT; restore drill passed; 48-hour soak clean; `v1.0.0` images and packages published (v0.3.0 per D-051; 1.0.0 is a later API-lock decision) |

## 6. Phase-selection handoff

How an implementation session starts and finishes.

1. **The owner names a phase** (for example "implement PHASE-04"). Confirm in section 2 that every dependency phase is merged and its "Handoff" section conditions hold. If not, stop and report what is missing.
2. **Read the inputs:** the named PHASE document, then `02-ARCHITECTURE.md`, `03-PACKAGE-MAP.md` and `04-DECISION-LOG.md`. Read `01-REQUIREMENTS.md` for the requirement IDs the phase covers and the relevant UX brief for UI phases. Re-verify package versions against nuget.org on the day PHASE-01 starts.
3. **Resolve open items:** list the phase's Assumptions and Proposed decisions that affect the work and ask the owner to confirm any that block it.
4. **Create a detailed TDD implementation plan** with `superpowers:writing-plans`, one plan per phase, derived from the PHASE document's tasks (keep task IDs, order and "Depends on"). Each plan step is test first: write the named test, watch it fail, implement, watch it pass.
5. **Work on a feature branch** (for example `feat/phase-04-agent-auth`) in a worktree (`superpowers:using-git-worktrees`), with Conventional Commits. Never commit to `main` directly and never hand-write migrations.
6. **Implement** with `superpowers:test-driven-development` (and `superpowers:subagent-driven-development` for independent tasks). After each task run its Validation from the PHASE document and the standard commands in section 4.
7. **Verify** with `superpowers:verification-before-completion`: run the full build and test, tick the Success Criteria and Boundary Validation checklists, and update the docs listed in section 5.
8. **Open a pull request** against `main` whose description lists the task IDs completed, the success-criteria evidence (command output), documentation changes, and any new decisions or Assumptions. The owner reviews and merges; update the status column afterward.
9. **If a design question appears mid-phase,** record it in `04-DECISION-LOG.md` (and `02-ARCHITECTURE.md` if it changes a boundary) before continuing, rather than deciding silently in code.

## 7. Owner action items

These need the owner (credentials, accounts, other repositories or decisions). Phase references show when each is first needed.

| # | Action | Needed by |
| --- | --- | --- |
| 1 | ~~Confirm decisions~~ Done: D-001 to D-022 approved on 2026-10-02 | — |
| 2 | Verify the installed NCrunch version for the `xunit.v3` pin: if it predates the fix, pin `xunit.v3` 3.2.2 and `xunit.runner.visualstudio` 3.1.5 with a comment in `Directory.Packages.props` (D-013) | P01-T03 |
| 3 | ~~Create the GitHub repository with GHCR enabled~~ Done: `Syntax-Circus/techstrap` exists; enable GHCR package write permission for `GITHUB_TOKEN` | PHASE-01 |
| 4 | Open the cross-repo PR registering subnet `172.16.31.0/24` in the `_template` `CLIENT_IP_RATE_LIMITING.md` registry, and confirm it is free on the UAT host (the `TECHSTRAP_SUBNET` input of `deploy/docker-compose.yml`, D-019, D-043) | P01-T16 |
| 5 | Choose the `SECURITY.md` private reporting address | P01-T19 |
| 6 | Choose the visual direction and approve `docs/BRAND.md` | P02-T01 to P02-T03 |
| 7 | Set up the Authentik application and groups per the `syntax-circus-authentik` repo: a confidential OIDC client for Admin (code plus PKCE, `offline_access`, group claim in the id and access tokens), a provider for the API audience, and groups mapped to `TECHSTRAP_AGENT_GROUP` and `TECHSTRAP_ADMIN_GROUP`; the first admin is whoever is in the admin group (D-029) Needed before 12c's T14 (D-051) | P04-T14, P07-T02, P12-T17 |
| 8 | Supply the reverse-proxy address and trusted network values for UAT and production (Q-08: the `REVERSE_PROXY_CIDR` input of `deploy/.env.<env>.local`), create the shared Postgres Docker network and the scoped env files under `/etc/techstrap/<env>/` (D-043), and an SMTP relay for UAT | P01-T16, P12-T14 |
| 9 | ~~Create the nuget.org publishing setup~~ Done 2026-10-08 (D-049): the `TechStrap.*` package IDs are reserved, each package has a Trusted Publishing policy, `NUGET_USER` is set as an organization secret available to the repository (no API-key fallback) and the GitHub environment `release` has a required reviewer and a "Selected branches and tags" deployment rule for `v*` (verified with `gh api repos/Syntax-Circus/techstrap/environments/release`) | P11-T15 |
| 10 | Provide a macOS runner (or approve the macOS CI cost) for the MAUI workload build. Withdrawn (D-048): single net10.0, no macOS runner | P11-T07 |
| 11 | Provide a scratch environment for the restore drill and agree load-test scheduling on the shared UAT host | P12-T12, P12-T15 |
| 12 | Confirm the flagged Assumptions that need an owner answer (for example honeypot fake success, dropping a Public key's external user ref, Solved notice email) | PHASE-05, PHASE-06 |
| 13 | Review the discovery set and select the first phase (expected: PHASE-01) | Now |
| 14 | For each product host (D-050): add a DNS record and one Caddy site that proxies to the Portal, then set the host on the product in the Admin. UAT needs DNS and one Caddy site per product host (`docs/self-hosting/DEPLOYMENT.md`, "Product hosts") | P12-T14 |
| 15 | Done 2026-10-08: tag v0.2.0 after this PR merges (`publish-nuget.yml`) and paste the Contracts README `## Version notes` entry into the GitHub Release - `v0.2.0` was published by run 37845739172 and Release 0.2.0 carries the Version notes; the owner decision was to publish 0.2.0 with the binary and behavioural break named (D-050 amendment) | D-050 |
| 16 | Done 2026-10-08: SyntaxCircus.Blazor.Seo 0.1.5 published and pinned (its `TryAddScoped` registration keeps the Portal's host-aware builder in charge; the Portal also worked on 0.1.4 by last registration) | D-050 |

## 8. Decisions still open

None. All decisions (D-001 to D-025) were approved on 2026-10-02 (D-023 visual direction and D-024 customer-facing identity, spam recovery and portal prefill were settled by the owner after the first plan).

## 9. Carried forward from PHASE-01

Items found during PHASE-01 reviews that later phases own. Fold each into the named phase's plan.

| Item | Owning phase |
| --- | --- |
| Replace the `PlaceholderTests` in `TechStrap.Domain.Tests` and `TechStrap.Application.Tests` with real tests | PHASE-03 / PHASE-04 (done in PHASE-03: both placeholders replaced) |
| Add a `HandlerConstructorDependencyTests` rule forbidding persistence entity types | PHASE-03 (done: `*Record` rule, D-026) |
| `PostgresIntegrationTestBase.DisposeAsync` null-deref when initialisation threw | PHASE-03 (done) |
| Handler rules: inspect abstract/base controllers and inherited actions; narrow `IsHandlerType` to Application types | PHASE-04 |
| Replace `UnauthenticatedScheme` with JWT bearer and an explicit problem-details challenge | PHASE-04 |
| Admin/Portal security headers with a Blazor-aware CSP; extract the shared Admin/Portal host wiring | PHASE-07 / PHASE-09 (done in PHASE-07c, D-042) |
| Admin/Portal client IP behind the reverse proxy: same-host proxy = compose gateway (`172.16.31.1/32`); revisit if `TECHSTRAP_SUBNET` changes or the proxy moves off-host; raise upstream in the `_template` CLIENT_IP_RATE_LIMITING pattern | PHASE-09 / first UAT deploy |
| `SyntaxCircus.AspNetCore.Common` echoes inbound `X-Correlation-Id` with no length/charset cap (upstream fix) | PHASE-12 |
| Release: CI gate before publishing; hotfix tags must not move `latest` backwards; SHA-pin actions / Dependabot | before first stable tag / PHASE-12 |
| Runtime images: app binaries owned by uid 10001 (only mount points need it) | PHASE-12 |
| ~~Warn in `.env.production.example` that the Postgres password must be connection-string safe~~ | Done in D-043: `deploy/.env.api.example` and `deploy/.env.worker.example` say so |
