# PHASE-07: Admin App

## Objective

Deliver `TechStrap.Admin`, the Blazor Server agent app: OIDC sign-in, ticket
queue with views/filters/search, ticket detail with timeline and all agent
actions, and the settings screens (products and branding, API keys, agents,
tags, notification preferences, dead letters, admin audit log). The app is a
pure API consumer: every operation goes through typed clients over
`TechStrap.Contracts` and the Phase 04/05/06 handlers. KB editing is added in
[Phase 08](PHASE-08-knowledge-base.md); live refresh in
[Phase 10](PHASE-10-live-updates.md).

## Dependencies

- **Depends on:** [PHASE-02](PHASE-02-brand-and-ux.md) (BRAND.md, `UX-BRIEF-admin.md`, SCSS tokens), [PHASE-06](PHASE-06-ticket-operations.md) (all ticket, dead-letter and customer-facing handlers; transitively P04/P05 endpoints).
- **Unblocks:** [PHASE-08](PHASE-08-knowledge-base.md) (admin editor part), [PHASE-10](PHASE-10-live-updates.md), [PHASE-12](PHASE-12-release-hardening.md).
- **External prerequisites:** An OIDC client for the admin (confidential, code + PKCE, `offline_access`, group claim in the id token and the access token) in the owner's Authentik; `TECHSTRAP_AGENT_GROUP` / `TECHSTRAP_ADMIN_GROUP` configured; a running API from P06 with seed/demo data.

## Delivery split (D-040)

PHASE-07 lands in three PRs. Each task id below carries its PR in brackets.

| PR | Tasks | Content |
| :-- | :---- | :------ |
| 07a | P07-T01 to T13, T22 | Options, sign-in, shell and primitives, typed clients (agents, products, tags, tickets, requesters), queue with the Spam view, ticket detail, reply composer, sidebar, spam/delete/erase, the attachment pass-through |
| 07b | P07-T14 to T18, T23 | Products, API keys, agents and my settings, tags and the audit log, dead letters |
| 07c | P07-T19, T20 | Brand, responsive and accessibility pass, compose and Dockerfile verification, Admin architecture rules, CSP, shared host wiring, OpenAPI bearer scheme, the command palette |
| PHASE-12 | P07-T21 | Playwright smoke tests (dropped from PHASE-07) |

07a deviations from the text below, all recorded in D-040: the typed clients sit on a small `ApiConnection` (not `ApiClientBase`); there is no `IAttachmentsClient`; the folders are `Auth/`, `Clients/`, `Options/`, `Features/Queue/`, `Features/Tickets/` with primitives in `Components/Ui`; `StatusStamp` and `PriorityMark` are the badges; the sidebar is optimistic-free; the Admin references Contracts and Hosting; the no-access page follows the API's answer to `GET /api/agents/me`.

07b deviations from the text below, all recorded in D-041: roles are read-only (the agents page activates and deactivates only); the product logo is a validated URL field, not an upload; the Admin tag list shows ticket counts from the new `GET /api/tags/summary`; every admin page sits inside an `AdminOnly` guard; the shortcut toggle and the theme are stored in the browser (`preferences.js`); validation errors are 400, not 422. PHASE-07b lands in one PR.

## Architecture Decisions

- **No new server entry points.** Admin never touches the database and does not reference `TechStrap.Application` or `TechStrap.Infrastructure`; it references `TechStrap.Contracts` and `TechStrap.Hosting` only (enforced by the P01 architecture test, see [02-ARCHITECTURE.md](02-ARCHITECTURE.md)). PHASE-07b adds one read endpoint to the API, GET /api/tags/summary (D-041); the Admin host itself still adds none.
- **Sign-in is app-owned**, per `SyntaxCircus.Blazor.Auth`: cookie + OIDC with `SaveTokens = true` and `offline_access`; `AddBlazorTokenForwarding` + `UseBlazorTokenCache` supply the bearer token to the API. Users without the agent group claim see a "no access" page (the API also rejects them, so this is UX only). Redis token cache is **not** used (single admin instance, [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md)). **Assumption.**
- **Typed clients**: one client per API area (`IAgentsClient`, `IProductsClient`, `ITagsClient`, `ITicketsClient`, `IDeadLettersClient`, `IAdminEventsClient`) in `TechStrap.Admin/Clients/`, over a small Admin `ApiConnection` (D-040). `AddTechStrapApiClients` registers two named clients that run `ApiAuthHandler` (the bearer token) and forward the client IP; the connection obtains each from `IBlazorCircuitHttpClientFactory` (circuit-safe), so components never see an HttpClient. Clients return `Result<T>`/DTOs; ProblemDetails (RFC 7807), including the `errorCodes` of a 400, are mapped to `Result` failures so components never catch HTTP exceptions. `ApiClientBase` is not used: it drops `errorCodes` and has no `Result` mapping.
- **Resilience budget**: the read client retries a failed GET twice (`ApiClientRegistration.ReadRetryCount`: exponential backoff with jitter, for transport errors, timeouts, 408 and 502/503/504) and has no circuit breaker, because every circuit shares it; every mutating call uses the write client, which has **no automatic retry** and no breaker (non-idempotent). See D-040.
- **Optimistic concurrency**: ticket mutations send the ticket's concurrency token (`RowVersion` in `TicketDetailDto`); a 409 renders a "this ticket changed, reload" banner and keeps the agent's unsent reply draft.
- **Presentation layering** follows _template RAZOR_COMPONENT_ARCHITECTURE.md: Razor markup -> code-behind -> feature-local state/presentation service -> typed client. ViewModels are `internal` records in the owning feature folder (`Features/Tickets`, `Features/Settings`, ...); DTOs from Contracts are never renamed to ViewModels.
- **Feature-folder layout**: `Features/{Queue,TicketDetail,Settings,DeadLetters,Shell}`; shared presentation primitives in `Shared/`.
- **State/loading/error/empty pattern**: every data component renders one of Loading / Error (retry) / Empty / Content using shared inline components `LoadingState`, `ErrorState`, `EmptyState`. Unhandled render failures are caught by `GlobalErrorBoundary`.
- **Queue state in the URL**: view, filters, search text and page are query-string parameters (`[SupplyParameterFromQuery]`) so links and back/forward work; no server-side per-user queue state. **Assumption.**
- **Attachment download** uses a thin admin-hosted pass-through (`GET /attachments/{id}`) that streams the API response for the signed-in agent (the bearer token is server-side and cannot be put in a browser link). Decision D-017 (exempt pass-through adapter); adapter only, no business logic (see Application Boundaries).
- **Destructive actions** (delete ticket, erase requester, revoke key, discard dead letter, deactivate agent) use a shared `ConfirmDialog` requiring explicit confirmation; erase-requester requires typing the requester's email. **Assumption.**
- **Role-gated UI (D-022):** delete ticket, erase requester, dead letters, products, API keys, agents, tags and the audit log are Admin-only and hidden from Agents (the API enforces the policy); mark spam, ticket handling, KB articles and the notification preferences are available to every Agent. The assignee picker uses the Agent-readable active-agent list.
- **API key secrets are shown once** (returned by `CreateProductApiKeyRequestHandler`), never persisted in component state beyond the dialog's lifetime, never logged.
- **Tests**: bUnit for component behavior in the new `tests/TechStrap.Admin.Tests` project (bUnit and `Microsoft.Extensions.TimeProvider.Testing` versions are in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md)). Playwright smoke tests move to PHASE-12 (D-040, P07-T21); PHASE-07 has no Playwright dependency.

## Application Boundaries

Follow _template APPLICATION_ARCHITECTURE.md. This phase adds **no new server
entry points** and **no new handlers**. The table lists every API entry point
the admin consumes (owned by the cited phase) and the admin-hosted framework or
adapter endpoints.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| `GET /api/agents/me` (on sign-in, role/active check) | `GetCurrentAgentRequestHandler` (P04) | Admin: `IAgentsClient` | `AgentsClient` over `ApiConnection` (HTTP) | `AgentDto`; 403 -> "no access" page | Consumed; no new server entry point |
| `GET /api/agents`, `PUT /api/agents/{id}`, `PUT /api/agents/me/notification-preferences` | `ListAgentsRequestHandler`, `UpdateAgentRequestHandler`, `UpdateNotificationPreferencesRequestHandler` (P04) | `IAgentsClient` | `AgentsClient` | `Result<T>` from ProblemDetails; 403 hides Admin-only UI | Consumed; no new server entry point |
| `PUT /api/agents/me/profile` | `UpdateMyProfileRequestHandler` (P04, D-024) | `IAgentsClient` | `AgentsClient` | `Result` from ProblemDetails; 400 shown as a field error (target public-display-name) | Consumed; no new server entry point |
| Products: list/get/create/update (incl. branding) | `ListProductsRequestHandler`, `GetProductRequestHandler`, `CreateProductRequestHandler`, `UpdateProductRequestHandler` (P04) | `IProductsClient` | `ProductsClient` | 400 validation errors mapped to field messages | Consumed; no new server entry point |
| Product API keys: list/create/revoke | `ListProductApiKeysRequestHandler`, `CreateProductApiKeyRequestHandler`, `RevokeProductApiKeyRequestHandler` (P04) | `IProductsClient` | `ProductsClient` | Secret shown once from create response | Consumed; no new server entry point |
| Tags: list/create/update/delete | `ListTagsRequestHandler`, `ListTagSummariesRequestHandler` (07b, D-041), `CreateTagRequestHandler`, `UpdateTagRequestHandler`, `DeleteTagRequestHandler` (P04) | `ITagsClient` | `TagsClient` | 409 on duplicate slug -> field error | Consumed; one new read endpoint, GET /api/tags/summary (D-041) |
| `GET /api/admin-events` | `ListAdminEventsRequestHandler` (P04) | `IAdminEventsClient` | `AdminEventsClient` | Paged `AdminEventDto` | Consumed; no new server entry point |
| Ticket queue and search | `ListTicketsRequestHandler` (P06) | `ITicketsClient` | `TicketsClient` | `PagedResponse<TicketSummaryDto>` | Consumed; no new server entry point |
| Ticket detail + timeline | `GetTicketRequestHandler` (P06) | `ITicketsClient` | `TicketsClient` | `TicketDetailDto`; 404 -> NotFound view | Consumed; no new server entry point |
| Reply / internal note | `AddAgentReplyRequestHandler`, `AddInternalNoteRequestHandler` (P06) | `ITicketsClient` | `TicketsClient` | 409 -> concurrency banner; 400 -> composer errors | Consumed; no new server entry point |
| Status / assign / priority / product move | `ChangeTicketStatusRequestHandler`, `AssignTicketRequestHandler`, `ChangeTicketPriorityRequestHandler`, `MoveTicketProductRequestHandler` (P06) | `ITicketsClient` | `TicketsClient` | 409 concurrency; 409 illegal transition (e.g. edits on Closed) -> inline error | Consumed; no new server entry point |
| Tags on ticket | `AddTicketTagRequestHandler`, `RemoveTicketTagRequestHandler` (P06) | `ITicketsClient` | `TicketsClient` | Idempotent success | Consumed; no new server entry point |
| Spam / delete / erase requester | `MarkTicketSpamRequestHandler` (Agent), `DeleteTicketRequestHandler`, `EraseRequesterRequestHandler` (Admin only, D-022) (P06) | `ITicketsClient` | `TicketsClient` | Spam: 200 `TicketStateDto` (D-036); delete and erase hidden from Agents | Consumed; no new server entry point |
| Attachment view/download | `GetAttachmentRequestHandler` (P06) | `IAttachmentsClient` | `AttachmentsClient` | Stream + content-type; 404 uniform | Consumed. Admin-hosted `GET /attachments/{id}` is a pass-through adapter (no workflow, no persistence), exempt per D-017 ([04-DECISION-LOG.md](04-DECISION-LOG.md)) |
| Dead letters: list/retry/discard | `ListDeadLettersRequestHandler`, `RetryDeadLetterRequestHandler`, `DiscardDeadLetterRequestHandler` (P06) | `IDeadLettersClient` | `DeadLettersClient` | `Result`; list refresh | Consumed; no new server entry point |
| OIDC sign-in/out (`/signin-oidc`, `/signout-callback-oidc`, login/logout redirects), `/_blazor`, static assets | Exempt | Framework authentication middleware | ASP.NET Core OIDC/cookie handlers | Redirects | Exempt: framework-owned; no application workflow |
| `/health/live`, `/health/ready` | Exempt | `SyntaxCircus.AspNetCore.Common` | Package health endpoints | 200/503 | Exempt operational endpoint (P01) |

## Razor Component Boundaries

Follow _template RAZOR_COMPONENT_ARCHITECTURE.md. "Paired" = `.razor` +
`.razor.cs`; "inline" = `.razor` only within the ceiling (simple parameters plus
at most one trivial `EventCallback` forwarder). Class names are
feature-local; ViewModels are `internal` records in the same folder.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| `App.razor` / `Routes.razor` (error boundary, reconnect, router) | Inline (markup + `GlobalErrorBoundary`, `ReconnectModal`, router only) | None | None | None |
| `NotFoundPage` (router `NotFoundPage` adapter over `NotFoundView`) | Inline | None | Stateless | None |
| `MainLayout`, `NavMenu` | Paired (injection of auth state, `NavigationManager`, current agent) | `NavMenuViewModel` (display name, role flags) built inline in code-behind | Reads `AuthenticationState` | `AgentDto` mapped locally |
| `SignedOutPage` / `NoAccessPage` | Inline | None | Stateless | None |
| `TicketQueuePage` (`/queue/{view?}`; `/` opens the queue) | Paired (query params, injection, async load, paging) | `TicketQueueViewModel` + `TicketRowViewModel`; mapping is simple -> built in code-behind | Loading/Error/Empty/Content; filters in query string | `TicketSummaryDto`, `PagedResponse<>`, `ListTicketsRequest` via `ITicketsClient` |
| `QueueViewTabs` | Inline (renders tabs; one `EventCallback<string>` forwarder) | None | Stateless | View names are Contracts constants (`TicketViews`) |
| `QueueFilterBar` | Paired (debounced search input, local form state) | `QueueFilterViewModel` | Local mutable state; raises one callback with a filter value | Builds `ListTicketsRequest` filter fields |
| `TicketRow`, `StatusBadge`, `PriorityBadge`, `TagChip` | Inline (parameters only) | Take `TicketRowViewModel` or primitives | Stateless | None |
| `PagerControl` | Inline (page, count, one `EventCallback<int>`) | None | Stateless | None |
| `TicketDetailPage` (`/tickets/{Number}`) | Paired (route param, load, action orchestration, concurrency token) | `TicketDetailViewModel`; async assembly from `TicketDetailDto` + products/agents/tags lookups -> **feature-local `TicketDetailPresenter`** (multiple dependencies) | Loading/Error/NotFound/Content; holds concurrency token; reload on 409 | `TicketDetailDto`, `TicketEventDto`, `MessageDto` |
| `TicketTimeline` | Paired (event-type -> icon/text mapping, helper methods) | `TimelineEntryViewModel` list built by `TimelineEntryFactory` (non-trivial mapping of ~9 event types) | Stateless | `TicketEventDto` (jsonb payload) |
| `MessageThread` / `MessageBubble` | `MessageThread` paired (helpers, body rendering); `MessageBubble` inline | `MessageViewModel` | Stateless; renders **sanitized** server body only via `MarkupString` (documented single site) | `MessageDto` (body already sanitized by API) |
| `ReplyComposer` (public reply / internal note toggle) | Paired (injection, async submit, draft state, validation) | `ReplyComposerViewModel` (form model) | Local draft; preserved on 409; submit disabled in flight | `AddAgentReplyRequest`, `AddInternalNoteRequest` (KB article ids added in P08) |
| `TicketSidebar` (status, assignee, priority, product) | Paired (injection, async, multiple callbacks) | `TicketSidebarViewModel` | Optimistic-free: shows pending, then re-renders from response | `Change*Request`/`Assign*Request`/`Move*Request` DTOs |
| `TagPicker` | Paired (async tag lookup, add/remove) | None (uses `TagDto` directly — **direct-DTO decision:** read-only display, no reshaping) | Local selection | `TagDto` |
| `RequesterCard` + `EraseRequesterDialog` | `RequesterCard` inline; dialog paired (confirmation text state, async) | None | Dialog local state | `RequesterDto`; `EraseRequesterRequest` |
| `AttachmentList` | Inline | `AttachmentViewModel` (size text, icon) built by caller | Stateless | `AttachmentDto`; links to `/attachments/{id}` |
| `ConfirmDialog` | Paired (modal state, focus handling, JS-free) | None | Local open/closed | None |
| `LoadingState`, `ErrorState`, `EmptyState` | Inline (parameters + one retry `EventCallback`) | None | Stateless | None |
| `ProductsPage` | Paired | `ProductRowViewModel` | Loading/Error/Empty/Content | `ProductDto` |
| `ProductEditorPage` (details + branding: name, logo URL, accent colour, from/reply-to) | Paired (form state, validation, async save) | `ProductEditorViewModel` (form model; accent colour validated with `BrandingRules.ColourPattern`, logo URL with `BrandingRules.IsAcceptableLogoUrl` (D-041)) | Dirty tracking; server validation errors mapped to fields | `CreateProductRequest`/`UpdateProductRequest` |
| `ApiKeysPanel` + `NewApiKeyDialog` | Paired | `ApiKeyRowViewModel`; secret held only in dialog field | Show-once secret, cleared on close | `ProductApiKeyDto`, `CreateProductApiKeyRequest/Response` |
| `AgentsPage` | Paired | `AgentRowViewModel` | Read-only role badge with the IdP note; activate and deactivate inline (deactivate confirms); Admin-only (D-041) | `AgentListItemDto`, `UpdateAgentRequest` |
| `TagsPage` | Paired | `TagRowViewModel` | Inline edit; delete confirm (the tag name typed when the tag is in use, with its ticket count) | `TagSummaryDto`, `CreateTagRequest`/`UpdateTagRequest` |
| `NotificationPreferencesPage` | Paired | `NotificationPreferencesViewModel` (product x opt-in toggles) | Dirty tracking; save | `NotificationPreferenceDto` |
| `PublicDisplayNameField` (in My settings) | Paired | `MyProfileViewModel` (draft name, preview text, save state) | Live preview from the Contracts format constant; dirty tracking; save on blur or Enter | `UpdateMyProfileRequest`, `AgentDto` |
| `DeadLettersPage` | Paired | `DeadLetterRowViewModel` (last error truncated) | Retry/discard per row with confirm | `DeadLetterDto` |
| `AdminEventsPage` | Paired | `AdminEventRowViewModel` built by `AdminEventSummaryFactory` (payload -> sentence) | Paged | `AdminEventDto` |

## Syntax Circus Packages

Exact versions and release links live in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md);
all are locked in `Directory.Packages.props` (P01).

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.Blazor.Auth` | Token forwarding, refresh, circuit-safe HTTP, session expiry | Admin calls a protected API as the signed-in agent | Integration-style test with fake OIDC tokens; manual sign-in against Authentik; 401 flips `SessionStateService` and shows re-sign-in prompt |
| `SyntaxCircus.Blazor.Components` | `GlobalErrorBoundary`, `ReconnectModal`, `NotFoundView` | Uniform error/reconnect/not-found UI without bespoke boilerplate | bUnit: faulty child renders fallback; reconnect modal present once in `App.razor` |
| `SyntaxCircus.Http.Resilience` | Brings the resilience pipeline; the read client uses its retry strategy only (no `ApiClientBase`, no circuit breaker, D-040) | Admin -> API typed clients | Unit tests with a stub handler: retry on GET 503, none on POST, ProblemDetails -> `Result` failure |
| `SyntaxCircus.Common` | `Result`/`Result<T>`, `ICurrentUserService` abstractions | Clients and presenters return `Result` | Compile + unit tests |
| `SyntaxCircus.AspNetCore.Common` | Health, security headers, correlation id | Admin host operational endpoints (wired in P01; verified here) | `/health/*` smoke; CSP/headers asserted in a host test |
| `SyntaxCircus.DotEnv`, `.AspNetCore.Serilog`, `.Observability` | Config and telemetry | Already wired in P01; confirm admin-specific env keys | `.env.example` complete; log redaction test for tokens |

Not used here: `Blazor.Seo` (no public pages), `Blazor.Tracking` (Not applicable), `Storage`/`Email` (API/worker only).

## Deliverables

- [ ] `TechStrap.Admin` host with OIDC cookie sign-in, token forwarding, claim-gated "no access" page, `.env.example`.
- [ ] Typed clients and `Result` mapping for all consumed endpoints, with unit tests.
- [ ] Shell (layout, nav, error boundary, reconnect, not-found) styled with BRAND.md tokens.
- [ ] Queue page with views, filters, search and paging (URL-driven).
- [ ] Ticket detail with timeline, reply/internal note, sidebar actions, tags, attachments, spam/delete/erase.
- [ ] Settings: products + branding, API keys, agents, tags, notification preferences.
- [ ] Dead letters and admin-events pages.
- [ ] `tests/TechStrap.Admin.Tests` (bUnit) and CI step (Playwright smoke moved to PHASE-12, D-040).
- [ ] Admin Dockerfile run verified; compose service healthy.

## Actionable Tasks

- [x] **P07-T01** [07a] Add `TechStrap.Admin/.env.example` keys and options classes (OIDC authority/client id/secret, API base URL, group names, cookie settings) with constants for config section names
  - **Depends on:** P01 (host skeleton)
  - **Validation:** App fails fast with a clear message when a required key is missing (unit test on options validation); `.env.example` lists every key.
  - **07a evidence:** the Task 3 options tests, `EnvExampleCompletenessTests`
- [ ] **P07-T02** [07a] Wire cookie + OIDC sign-in with `SaveTokens`, `offline_access`, `AddBlazorTokenForwarding`, `UseBlazorTokenCache` in the documented middleware order
  - **Depends on:** P07-T01
  - **Validation:** Manual sign-in against UAT/local Authentik reaches the shell; `GET /api/agents/me` succeeds with the forwarded token; request log shows no token values.
  - **07a evidence (automated part):** `AdminSignInTests`, `AgentSessionTests`, `AgentGateTests`, `AgentAccessHostTests`, `AdminHostSmokeTests`, `AdminLeakTests`. The manual sign-in against a real provider is still open (Authentik is not set up; docs/development/ADMIN-APP.md)
- [x] **P07-T03** [07a] Extend the `tests/TechStrap.Admin.Tests` project (skeleton created in PHASE-02; add bUnit) and the `Shared/` primitives: `LoadingState`, `ErrorState`, `EmptyState`, `ConfirmDialog`, `StatusBadge`, `PriorityBadge`, `TagChip`, `PagerControl`
  - **Depends on:** P07-T02, P02 SCSS tokens
  - **Validation:** bUnit tests per component (render states, callback fires once, focus lands in dialog); no component beyond the inline ceiling lacks a `.razor.cs`.
  - **07a evidence:** `StateComponentTests`, `TagChipAndPagerTests`, `ConfirmDialogTests`, `TicketDisplayTests` (the spec's StatusBadge/PriorityBadge are the existing StatusStamp/PriorityMark plus TicketDisplay, D-040)
- [x] **P07-T04** [07a] Build `App.razor`, `Routes.razor`, `MainLayout`, `NavMenu`, `NotFoundPage`, `NoAccessPage` with `GlobalErrorBoundary` and `ReconnectModal`
  - **Depends on:** P07-T03
  - **Validation:** bUnit: throwing child shows fallback and "Try again" recovers; unknown route renders not-found; non-agent principal sees no-access page.
  - **07a evidence:** `MainLayoutTests`, `ShellComponentTests`, `AgentGateTests`, `ReconnectAndErrorTests`
- [ ] **P07-T05** [07a] Implement typed clients (`IAgentsClient`, `IProductsClient`, `ITagsClient`, `IAdminEventsClient`) over `ApiConnection` with ProblemDetails -> `Result` mapping, a retrying read client and a never-retrying write client (D-040)
  - **Depends on:** P07-T02
  - **Validation:** Unit tests with stub `HttpMessageHandler`: success, 400 field errors, 403, 409, 503 retry on GET only, cancellation token propagated.
  - **07a evidence:** 07a: agents, products and tags clients (`ApiConnectionTests`, `ReferenceDataClientTests`); `IAdminEventsClient` is in the app (07b)
- [ ] **P07-T06** [07a] Implement `ITicketsClient`, `IAttachmentsClient`, `IDeadLettersClient` (including `RowVersion` in the request body; replace local state with the returned `TicketStateDto` (D-036) and multipart reply submit (D-036))
  - **Depends on:** P07-T05
  - **Validation:** Same stub-handler suite; 409 maps to a distinct `Result` error code constant used by components.
  - **07a evidence:** 07a: `ITicketsClient`, `IRequestersClient`, the attachment pass-through (`TicketsClientTests`, `AttachmentPassThroughTests`); there is no `IAttachmentsClient` (the pass-through is an endpoint, D-017) and `IDeadLettersClient` is in the app (07b)
- [x] **P07-T07** [07a] Build `TicketQueuePage`, `QueueViewTabs`, `QueueFilterBar`, `TicketRow`, paging, with query-string state
  - **Depends on:** P07-T06, P07-T03
  - **Validation:** bUnit with fake `ITicketsClient`: each view tab requests the right filter; search debounce issues one call; empty/error/loading render; page change preserves filters.
  - **07a evidence:** `TicketQueuePageTests` (default Unassigned, only the Spam tab asks for spam, one debounced call, paging keeps filters)
- [x] **P07-T08** [07a] Build `TicketDetailPage` and `TicketDetailPresenter` (async assembly of detail + lookups) with loading/NotFound/error states and concurrency-token handling
  - **Depends on:** P07-T06, P07-T03
  - **Validation:** bUnit: 404 shows NotFound view; presenter unit test maps a `TicketDetailDto` fixture to the view model; closed ticket renders read-only (no composer/actions).
  - **07a evidence:** `TicketDetailPageTests`, `TicketDetailPresenterTests`
- [x] **P07-T09** [07a] Build `TicketTimeline` + `TimelineEntryFactory` covering every `TicketEvent` type (Created, MessageAdded, StatusChanged, Assigned, ProductChanged, PriorityChanged, TagAdded, TagRemoved, plus spam/follow-up events defined in P06)
  - **Depends on:** P07-T08
  - **Validation:** Unit test is a theory over every event-type constant; unknown type renders a generic entry rather than throwing.
  - **07a evidence:** `TimelineEntryFactoryTests` (every `TicketEventTypes` constant)
- [x] **P07-T10** [07a] Build `MessageThread`/`MessageBubble`/`AttachmentList` and the admin-hosted `GET /attachments/{id}` pass-through
  - **Depends on:** P07-T08
  - **Validation:** bUnit: internal notes visually distinct; the only `MarkupString` site renders the sanitized body; host test: pass-through streams bytes, sets `Content-Disposition: attachment` and `X-Content-Type-Options: nosniff`, returns 404 for an unknown id and never reaches the DB (no Infrastructure reference).
  - **07a evidence:** `TicketDetailPageTests`, `MarkupStringSiteTests`, `AttachmentPassThroughTests`, `AdminHostSmokeTests`
- [x] **P07-T11** [07a] Build `ReplyComposer` (public reply / internal note) with draft preservation and validation
  - **Depends on:** P07-T08
  - **Validation:** bUnit: submit calls the correct client method per mode; double-submit prevented; draft kept on 409/failed submit; cleared on success.
  - **07a evidence:** `ReplyComposerTests` (`Draft_and_files_survive_a_conflict`)
- [x] **P07-T12** [07a] Build `TicketSidebar` and `TagPicker` for status/assignee/priority/product/tags, honoring legal transitions from the API error response
  - **Depends on:** P07-T08
  - **Validation:** bUnit: each action calls its client method with the current concurrency token; failure shows inline error and reverts display; Closed hides actions.
  - **07a evidence:** `TicketSidebarTests`, `TicketDetailConflictTests`
- [x] **P07-T13** [07a] Build spam/delete ticket and `EraseRequesterDialog` flows (typed-email confirmation); spam is available to Agents, delete and erase are shown to Admins only (D-022)
  - **Depends on:** P07-T12, P07-T03
  - **Validation:** bUnit: confirm button disabled until the email matches; success navigates to the queue; cancel makes no call; delete and erase are absent for an Agent principal and present for an Admin, spam is present for both.
  - **07a evidence:** `DestructiveActionTests`
- [x] **P07-T14** [07b] Build `ProductsPage` and `ProductEditorPage` including branding fields and accent-colour validation
  - **Depends on:** P07-T05, P07-T03
  - **Validation:** bUnit: an invalid accent is rejected client-side with `BrandingRules.ColourPattern` (the server pattern, pinned by a parity test); a logo URL that is not https (or http for localhost) is rejected client-side and by the API; server 400 errors map to fields by their kebab-case target; the page sits inside `AdminOnly`, so a plain agent sees the no-access page.
  - **07b evidence:** `ProductEditorTests`, `ProductEditorRealApiTests`, `AccentPreviewTests`, `ProductsPageTests`, `AdminSettingsHostTests`
- [x] **P07-T15** [07b] Build `ApiKeysPanel` + `NewApiKeyDialog` (kind selection Trusted/Public, show-once secret, revoke with confirm)
  - **Depends on:** P07-T14
  - **Validation:** bUnit: secret visible only in the dialog and gone after close; revoked keys render as revoked; public/trusted badges distinct.
  - **07b evidence:** `NewApiKeyDialogTests`, `ApiKeysPanelTests`, `RevokeApiKeyTests`, `ProductKeysPageTests`, `AdminLeakTests`
- [x] **P07-T16** [07b] Build `AgentsPage` and `NotificationPreferencesPage`
  - **Depends on:** P07-T05, P07-T03
  - **Validation:** bUnit: the agents page shows the role as a read-only badge with the note "Roles come from your identity provider's groups." and has no role control; activate calls `SetActiveAsync(id, true)` without a confirm, deactivate calls `SetActiveAsync(id, false)` after a confirm, and a 409 `last-active-admin` shows inline; a plain agent sees the no-access page; the preferences save posts the full toggle set.
  - **07b evidence:** `AgentsPageTests`, `NotificationPreferencesPageTests`, `AdminOnlyTests`, `AdminSettingsHostTests`
- [x] **P07-T17** [07b] Build `TagsPage` (CRUD with colour) and `AdminEventsPage` + `AdminEventSummaryFactory`
  - **Depends on:** P07-T05, P07-T03
  - **Validation:** bUnit: duplicate-slug 409 shows a field error; factory theory covers every admin-event type constant.
  - **07b evidence:** `TagsPageTests`, `DeleteTagTests`, `AdminEventSummaryFactoryTests`, `AdminEventsPageTests`
- [x] **P07-T18** [07b] Build `DeadLettersPage` (retry/discard with confirm, last-error preview)
  - **Depends on:** P07-T06, P07-T03
  - **Validation:** bUnit: retry/discard call the matching client method and refresh; empty state shown when none.
  - **07b evidence:** `DeadLettersPageTests`, `AdminSettingsHostTests`
- [ ] **P07-T19** [07c] Apply BRAND.md tokens/SCSS, responsive layout and accessibility pass per `UX-BRIEF-admin.md` (focus order, ARIA on dialogs/badges, contrast)
  - **Depends on:** P07-T07, P07-T08, P07-T14
  - **Validation:** Manual checklist from UX-BRIEF-admin completed; keyboard-only run through queue -> reply -> solve; axe (browser extension) run has no critical findings (record in PR).
  - **Validation (PHASE-02 carry-over):** Admin must not use semantic `.text-{color}` or `.link-{color}` utilities in dark mode (they fail contrast there); use brand tokens or `.text-*-emphasis`. Add a `BrandWindow` heading-level parameter so standalone brand pages (404, sign-in) render an `h1`. The keyboard walk-through deferred from PHASE-02 happens here.
- [ ] **P07-T20** [07c] Add Admin to compose and verify Dockerfile run; add admin architecture rules (no reference to Application/Infrastructure/EF; no `HttpClient` use in `.razor` files)
  - **Depends on:** P07-T02
  - **Validation:** `docker compose up` -> `/health/ready` 200; Architecture.Tests fail when a forbidden reference or `[Inject] HttpClient` in a component is introduced (verified by a deliberate failing sample).
- [ ] **P07-T21** Moved to PHASE-12 (D-040): optional Playwright smoke tests for sign-in-free paths.
  - **Depends on:** P07-T11, P07-T20
  - **Validation:** Smoke run passes against compose with seed data in CI nightly (not blocking PRs).
- [x] **P07-T22** [07a] (D-024) Add the **Spam** view to the rail and `QueueViewTabs` (requests `TicketView.Spam`; plain "No spam" empty state; muted count) and the **Not spam** action: key `u` on the selected Spam-view row or an open flagged ticket, plus the overflow menu and palette command; status-bar hint, status message "Restored ACME-142 from spam" and shortcut help entry. `u` follows the typing guard and the My settings shortcut toggle
  - **Depends on:** P07-T07, P07-T13, P06-T21
  - **Validation:** bUnit with fake `ITicketsClient`: the Spam tab requests the Spam view and the five normal tabs never request it; `u` sends `IsSpam = false` once, removes the row and shows the status message; `u` does nothing while typing, with shortcuts off, or on a ticket that is not flagged; Not spam is present for an Agent principal; the help dialog lists `u`; no clash with `j`, `k`, `r`, `n`, `e`, `/`
  - **07a evidence:** `TicketQueuePageTests`, `NotSpamQueueTests`, `DestructiveActionTests`, `ShortcutServiceTests` (the palette command is part of the 07c palette)
- [x] **P07-T23** [07b] (D-024) Add the optional **Public display name** field to My settings (`PublicDisplayNameField`, `MyProfileViewModel`, `IAgentsClient.UpdateMyProfile`) with the live preview line "Customers see: Sam from Orbitly Support", helper text that email is never shown, and inline save confirmation
  - **Depends on:** P07-T16, P04-T15
  - **Validation:** bUnit: the preview shows "Sam from Orbitly Support" by default, "Samantha from Orbitly Support" while typing "Samantha", and returns to the default when cleared; save calls `UpdateMyProfile` once and shows confirmation; an over-long name or one containing `@` shows the field error from a 400 (target public-display-name); the field is optional
  - **07b evidence:** `PublicDisplayNameFieldTests`, `PublicNamePreviewTests`, `NotificationPreferencesPageTests`


## Success Criteria

- [x] An agent in the configured group signs in, sees the queue with all five views plus the Spam view, filters and searches, pages results, and opens a ticket. (07a evidence: `TicketQueuePageTests`, `AdminHostSmokeTests`; the host tests sign in with the test scheme, the real provider is pending T02)
- [x] From ticket detail an agent can reply publicly, add an internal note, change status/assignee/priority/product, tag, mark spam, delete, and erase the requester; every action is reflected in the timeline after reload. (07a evidence: `ReplyComposerTests`, `TicketSidebarTests`, `DestructiveActionTests`)
- [x] A non-agent user is rejected by the API and sees the no-access page; an expired session prompts re-sign-in instead of a blank error. (07a evidence: `AgentSessionTests`, `AgentGateTests`, `AgentAccessHostTests`; test scheme, real provider pending T02)
- [x] Admin can create/edit products with branding, create (show-once) and revoke Trusted/Public keys, manage agents/tags/notification preferences, and retry/discard dead letters. (07b evidence: `ProductEditorTests`, `NewApiKeyDialogTests`, `RevokeApiKeyTests`, `AgentsPageTests`, `TagsPageTests`, `NotificationPreferencesPageTests`, `DeadLettersPageTests`, `AdminSettingsHostTests`, `AdminLeakTests`)
- [x] The Spam view lists flagged tickets and `u` restores one (Not spam) without a dialog; normal views never show spam (D-024). (07a evidence: `NotSpamQueueTests`)
- [x] An agent can set a public display name in My settings and sees the live preview "Customers see: ..." (D-024). (07b evidence: `PublicDisplayNameFieldTests`, `PublicNamePreviewTests`, `AdminSettingsHostTests`)
- [x] A 409 concurrency conflict never loses a typed reply draft. (07a evidence: `ReplyComposerTests`, `TicketDetailConflictTests`)
- [ ] `dotnet build`, `dotnet test` (including bUnit and architecture tests) are green; admin container is healthy under compose.
- [ ] Admin project references only Contracts and Hosting (plus packages; D-040), never Application, Infrastructure or Domain, verified by the architecture test (`ProjectReferenceDirectionTests`).

## Boundary Validation

- [ ] Application use-case entry points delegate to the named handlers listed above. (No new entry points; the `/attachments/{id}` pass-through is an endpoint over the read client, D-017.)
- [ ] Framework-owned operational or static exemptions execute no application workflow.
- [ ] Handler constructor dependencies contain only approved abstractions. (N/A here: no handlers added; verified unchanged in P04–P06.)
- [ ] Persistence and integration entities do not cross infrastructure boundaries. (Admin has no Infrastructure/Domain reference.)
- [ ] Cancellation reaches asynchronous handler dependencies. (Components pass the circuit/component `CancellationToken` into every read; writes deliberately pass `CancellationToken.None` so a write already sent is never cancelled by leaving the screen.)
- [ ] Expected outcomes and transport mapping have focused tests. (Client `Result` mapping tests in P07-T05/T06.)
- [ ] Infrastructure implementations have integration coverage where applicable. (N/A: typed clients covered by stub-handler tests.)
- [ ] Inline Razor components contain only simple parameters and, at most, one trivial synchronous `EventCallback`-forwarding callback.
- [ ] Every component beyond the inline ceiling uses paired `.razor` and `.razor.cs` files, with all C# in code-behind.
- [ ] Each Razor ViewModel is feature-local and presentation-only; the recorded direct-model decision (`TagPicker` uses `TagDto`) does not expose an API ViewModel.
- [ ] A factory or presentation service is used only for non-trivial mapping, asynchronous assembly, or multiple dependencies (`TicketDetailPresenter`, `TimelineEntryFactory`, `AdminEventSummaryFactory` only).
- [ ] API request and response contracts use DTO names and contracts, never Razor ViewModels.
- [ ] Repeated or business-meaningful literals are named constants at the right scope (view names, status/event-type strings from Contracts; accent-colour pattern; debounce interval; page size).
- [ ] Duplicated-looking logic across flows was evaluated for genuine divergence before extracting (e.g. reply composer vs. note composer share one component by design; tag admin vs. ticket tag picker intentionally separate).

## Risks and Open Questions

- [ ] **bUnit** (version in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md)) must be added to `Directory.Packages.props` in P07-T03, with the admin component tests in the new `tests/TechStrap.Admin.Tests` project (listed in `02-ARCHITECTURE.md`).
- [ ] The `/attachments/{id}` pass-through is an admin-hosted exempt adapter (D-017, approved); it is not in the handler catalog by design.
- [ ] `HubConnection` and live refresh are deliberately out of scope until P10; queue is manual-refresh until then.
- [ ] Blazor Server circuit memory with many open tabs; keep component state small and dispose subscriptions.
- [ ] Agent token lifetime vs. long-lived circuits: relies on Blazor.Auth refresh; verify behavior with Authentik's access-token lifetime in UAT.
- [x] Carried forward from the PHASE-04 final review: Mark the Admin `/error` page `[AllowAnonymous]` when Admin auth lands. (done in PHASE-07a)
- [x] [07b] Carried forward from the PHASE-04 final review: Return the `tag-in-use` count as a structured field, not only in the message text. (resolved differently in PHASE-07b, D-041: the count comes from `GET /api/tags/summary`; the 409 still carries it only in its message.)
- [x] [07b] Carried forward from the PHASE-04 final review: Add an endpoint test for the admin agent-list fields (`Email`, `Role`, `IsActive`, `LastSeenAt`). (done in PHASE-07b, D-041)
- [ ] [07c] Carried forward from the PHASE-07b final review: `AccentPreview` styles itself with an inline `style` attribute (validated `#RRGGBB` values only), so the 07c CSP needs `style-src-attr` or a CSSOM approach instead of `style-src 'self'` alone.
- [ ] Carried forward from the PHASE-04 final review: Add an OpenAPI bearer security scheme so generated clients know the endpoints need a token (also needed by PHASE-11).
- [x] Carried forward from PHASE-06c (D-039): wire `PiiRedactionEnricher` into the Admin host's `AddStandardSerilog` call once the Admin handles requester data (done in PHASE-07a through TechStrap.Hosting, D-040; the Sentry header scrub is wired too).

## Handoff

Before [PHASE-08](PHASE-08-knowledge-base.md) (admin editor) and
[PHASE-10](PHASE-10-live-updates.md) start: the admin shell, typed-client
pattern, `ReplyComposer` and `TicketQueuePage`/`TicketDetailPage` are merged
and green; the typed-client registration pattern and shared primitives are
documented in `TechStrap.Admin/README` or the PR description so later phases
add clients and components the same way. Phase 08's API work may start as soon
as Phase 06 is done.
