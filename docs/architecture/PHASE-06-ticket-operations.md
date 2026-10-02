# PHASE-06: Ticket Operations

## Objective

The complete ticket-handling API: agents list, search, read and act on tickets (reply, note, status, assign, priority, product move, tags, spam, delete, erase requester, attachments); customers read and reply through an access token, including lost-link recovery and follow-up tickets after Closed; the Worker auto-closes Solved tickets; assignee and new-ticket alerts are queued; admins manage dead-lettered emails. After this phase the whole helpdesk works headlessly through the API.

## Dependencies

- **Depends on:** [PHASE-05](PHASE-05-intake-email-worker.md) (intake, outbox, Worker, token service, attachment store, branded email renderer).
- **Unblocks:** [PHASE-07](PHASE-07-admin-app.md) (needs also PHASE-02), PHASE-08 (KB link in replies), PHASE-09 (customer endpoints; needs also 02 and 08), PHASE-10 (via 07).
- **External prerequisites:** Mailpit or SMTP capture from PHASE-05; none new.

## Architecture Decisions

- Every operation is a named handler (catalog for this phase) behind a controller action with `[FromServices]` injection; handlers accept `TechStrap.Contracts` request records directly, with transport-only details (multipart files, `X-Ticket-Token`) mapped at the controller (D-016); identity via `ICurrentUserService`; outcomes as `Result`/`Result<T>`; mapping through `SyntaxCircus.AspNetCore.Common`. Worker scheduled work uses constructor injection.
- Endpoint shape (fixed in `02-ARCHITECTURE.md` section 7.3, the source of truth): `GET /api/tickets`, `GET /api/tickets/{id}`, `POST /api/tickets/{id}/replies`, `POST /api/tickets/{id}/notes`, `PUT /api/tickets/{id}/status`, `PUT /api/tickets/{id}/assignee`, `PUT /api/tickets/{id}/priority`, `PUT /api/tickets/{id}/product`, `POST /api/tickets/{id}/tags`, `DELETE /api/tickets/{id}/tags/{tagId}`, `PUT /api/tickets/{id}/spam`, `DELETE /api/tickets/{id}`, `POST /api/requesters/{id}/erase`, `GET /api/attachments/{id}`, customer routes `GET /api/customer/ticket`, `POST /api/customer/ticket/replies`, `POST /api/customer/access-link`, and dead letters under `/api/dead-letters`. Customer routes carry the access token in the `X-Ticket-Token` header, never in the path, so it stays out of API access logs (A-16).
- Authorization (D-022): agent endpoints require `AgentPolicy`, including `MarkTicketSpamRequestHandler`; `DeleteTicketRequestHandler`, `EraseRequesterRequestHandler`, and the dead-letter handlers require `AdminPolicy`. Customer endpoints are anonymous and authorised only by the access token inside the handler.
- Queue views: Unassigned, Mine, Open, Pending, All with filters (product, status, priority, assignee, tag, requester, spam, date range) and paging; default sort `last_activity_at` desc; page size is a named constant with a hard maximum. Full-text search uses the PHASE-03 vectors through `ITicketRepository`. Spam tickets are excluded from all views unless the spam filter is chosen. **Assumption.**
- `GetTicketRequestHandler` returns detail plus timeline composed from `TicketEvent` and messages (internal notes only for agents). Attachment metadata included; bytes through `GetAttachmentRequestHandler`.
- Status and workflow rules live in Domain (PHASE-03). Public agent reply: adds message and event, sets `Pending`, sets `first_response_at` if first, queues the requester email with the access link. Internal note: no email. Optional `linkedKbArticleIds` on a reply records `TicketArticle` rows; PHASE-08 supplies the KB UI and validates articles exist (until then the handler accepts ids that resolve through `IKbRepository`).
- Customer reply: `Pending` or `Solved` becomes `Open`; notifies the assignee (or, when unassigned, opted-in agents for the product). On a `Closed` ticket it creates a follow-up ticket (new number from the original product, `parent_ticket_id` set, copy of the requester, new token, `FollowUpCreated` event on the parent, confirmation email) and returns the follow-up's view link. The Closed ticket stays read-only.
- Customer access: token validated through `IAccessTokenService` (hash lookup, not expired, not revoked); invalid, expired or revoked tokens return a uniform 404; successful use slides the expiry. Customers read public messages only and never see tags, internal notes, assignee identity beyond a display name or other requesters' data. Lost link: `RequestNewAccessLinkRequestHandler` always returns the same 202 whatever the address matched, sends only to that requester's own address, is limited per IP and per address, and revokes nothing automatically (**Assumption**: old tokens stay valid until expiry or manual revoke).
- Notification planning: `ITicketNotificationPlanner` (Application abstraction, implemented in Infrastructure over `IEmailOutbox` and `IEmailTemplateRenderer`) decides who is alerted and queues outbox rows in the caller's transaction. Cases: requester on public agent reply, assignee on assignment and on customer reply, opted-in agents on new ticket (per product preference from PHASE-04), requester on status Solved (**Assumption**: yes, short notice). `SubmitTicketRequestHandler` from PHASE-05 is changed here to call the planner for new-ticket alerts. There is no separate entry point for alerts.
- Auto-close: `AutoCloseSolvedTicketsHandler`, invoked by a Worker scheduled loop, closes tickets `Solved` for at least N days (`TECHSTRAP_AUTOCLOSE_DAYS`, default 7, **Assumption**), writes a `StatusChanged` event with actor `System`, and queues a notice. It is idempotent and batch-limited. The Worker's own changes are broadcast live in PHASE-10 (NOTIFY); nothing live is added here.
- Privacy: `MarkTicketSpamRequestHandler` toggles the flag (event written); `DeleteTicketRequestHandler` hard-deletes the ticket, messages, events, attachments (files and rows) and pending outbox rows; `EraseRequesterRequestHandler` anonymises the requester's messages (body replaced with a fixed marker, author name cleared), deletes their attachments, nulls requester name and email to a tombstone while keeping ticket numbers and event ids, revokes tokens, and writes an `AdminEvent`; `DeleteTicketRequestHandler` also writes an `AdminEvent` (without ticket content) in the same transaction. PII redaction in Serilog is a configuration task here (P06-T19). Automatic retention is out of scope.
- Product move: ticket number never changes; the move writes a `ProductChanged` event; branding of subsequent emails follows the new product.
- Dead letters: list outbox rows in `DeadLettered` state; retry resets attempts and schedules now; discard marks them discarded; each writes an `AdminEvent`.
- Concurrency: ticket mutations carry the concurrency token (If-Match style `rowVersion` in the request) and return 409 on stale writes.
- Live updates are not a handler concern: the PHASE-10 post-commit interceptor publishes changes (D-018), so none of the handlers here reference `ITicketChangeBroadcaster`.
- Abstractions added beyond the catalog: none beyond `ITicketNotificationPlanner` (already catalogued) and the options type `AutoCloseOptions`; attachment download uses an application-owned `AttachmentContent` result type, not `FileStreamResult`.

## Application Boundaries

Follow _template `APPLICATION_ARCHITECTURE.md` (not copied into this repo). Common dependencies on every write handler are `IUnitOfWork`, `ICurrentUserService` (agent routes) and `TimeProvider`, abbreviated "UoW/ID/clock" in the table. All handlers return `Result`/`Result<T>`; controllers pass `RequestAborted`.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| `GET /api/tickets` | `ListTicketsRequestHandler` (views, filters, FTS search, paging) | `ITicketRepository`, `ICurrentUserService` | EF `TicketRepository` (FTS via Npgsql) | 200 paged `TicketSummaryDto`; 400 bad filter | Mandatory flow |
| `GET /api/tickets/{id}` | `GetTicketRequestHandler` (detail and timeline) | `ITicketRepository`, `IKbRepository` | EF `TicketRepository` | 200 `TicketDetailDto`; 404 | Mandatory flow |
| `POST /api/tickets/{id}/replies` | `AddAgentReplyRequestHandler` | `ITicketRepository`, `IKbRepository`, `IAttachmentStore`, `IHtmlSanitizer`, `IMarkdownRenderer` (reply body), `ITicketNotificationPlanner`, `IAccessTokenService`, UoW/ID/clock | EF repositories, `AttachmentStore`, sanitizer, `TicketNotificationPlanner`, `AccessTokenService` | 201 `MessageDto`; 404; 409 concurrency or Closed; 400 | Mandatory flow |
| `POST /api/tickets/{id}/notes` | `AddInternalNoteRequestHandler` | `ITicketRepository`, `IHtmlSanitizer`, UoW/ID/clock | EF repository, sanitizer | 201; 404; 409 Closed | Mandatory flow |
| `PUT /api/tickets/{id}/status` | `ChangeTicketStatusRequestHandler` | `ITicketRepository`, `ITicketNotificationPlanner`, UoW/ID/clock | EF repository, planner | 200; 404; 409 invalid transition or stale | Mandatory flow |
| `PUT /api/tickets/{id}/assignee` | `AssignTicketRequestHandler` | `ITicketRepository`, `IAgentRepository`, `ITicketNotificationPlanner`, UoW/ID/clock | EF repositories, planner | 200; 404 ticket or agent; 409 | Mandatory flow |
| `PUT /api/tickets/{id}/priority` | `ChangeTicketPriorityRequestHandler` | `ITicketRepository`, UoW/ID/clock | EF repository | 200; 404; 409 | Mandatory flow |
| `PUT /api/tickets/{id}/product` | `MoveTicketProductRequestHandler` | `ITicketRepository`, `IProductRepository`, UoW/ID/clock | EF repositories | 200 (number unchanged); 404; 409 | Mandatory flow |
| `POST /api/tickets/{id}/tags` | `AddTicketTagRequestHandler` | `ITicketRepository`, `ITagRepository`, UoW/ID/clock | EF repositories | 200; 404; 409 duplicate | Mandatory flow |
| `DELETE /api/tickets/{id}/tags/{tagId}` | `RemoveTicketTagRequestHandler` | `ITicketRepository`, `ITagRepository`, UoW/ID/clock | EF repositories | 204; 404 | Mandatory flow |
| `PUT /api/tickets/{id}/spam` (Agent, D-022) | `MarkTicketSpamRequestHandler` | `ITicketRepository`, UoW/ID/clock | EF repository | 200; 404 | Mandatory flow |
| `DELETE /api/tickets/{id}` | `DeleteTicketRequestHandler` (Admin, D-022) | `ITicketRepository`, `IAttachmentStore`, `IEmailOutboxStore`, `IAdminEventRepository`, UoW/ID/clock | EF repositories, `AttachmentStore`, outbox store | 204; 404 | Mandatory flow |
| `POST /api/requesters/{id}/erase` | `EraseRequesterRequestHandler` (Admin, D-022) | `IRequesterRepository`, `ITicketRepository`, `IAttachmentStore`, `IEmailOutboxStore`, `IAccessTokenService`, `IAdminEventRepository`, UoW/ID/clock | EF repositories, `AttachmentStore`, outbox store, `AccessTokenService` | 204; 404 | Mandatory flow |
| `GET /api/attachments/{id}` (agent JWT, or customer `X-Ticket-Token`; Admin and Portal call it through their pass-through adapters, D-017) | `GetAttachmentRequestHandler` | `ITicketRepository`, `IAttachmentStore`, `IAccessTokenService`, `ICurrentUserService` (agent callers only), `TimeProvider` | EF repository, `AttachmentStore`, `AccessTokenService` | 200 file stream with safe `Content-Disposition` and `nosniff`; 404 uniform for customers | One route, two caller kinds; authorisation differs by credential |
| `GET /api/customer/ticket` (`X-Ticket-Token`) | `GetCustomerTicketRequestHandler` | `ITicketRepository`, `IAccessTokenService`, `TimeProvider`, UoW (slide expiry) | EF repository, `AccessTokenService`, `UnitOfWork` | 200 `CustomerTicketDto` (public messages only); 404 uniform | Mandatory flow |
| `POST /api/customer/ticket/replies` (`X-Ticket-Token`) | `AddCustomerReplyRequestHandler` (on Closed creates follow-up) | `ITicketRepository`, `IRequesterRepository`, `ITicketNumberAllocator`, `IAccessTokenService`, `IAttachmentStore`, `IHtmlSanitizer`, `ITicketNotificationPlanner`, UoW/clock | EF repositories, allocator, `AttachmentStore`, sanitizer, planner | 201 `CustomerReplyResponse` (message, or follow-up number and link); 404 uniform; 400; 429 at host | Mandatory flow |
| `POST /api/customer/access-link` | `RequestNewAccessLinkRequestHandler` | `IRequesterRepository`, `ITicketRepository`, `IAccessTokenService`, `ITicketNotificationPlanner`, UoW/clock | EF repositories, `AccessTokenService`, planner | Always 202 with identical body; 429 at host | Mandatory flow; uniform response by design |
| `GET /api/dead-letters` | `ListDeadLettersRequestHandler` (Admin, D-022) | `IEmailOutboxStore` | EF `EmailOutboxStore` | 200 paged `DeadLetterDto` | Mandatory flow |
| `POST /api/dead-letters/{id}/retry` | `RetryDeadLetterRequestHandler` (Admin) | `IEmailOutboxStore`, `IAdminEventRepository`, UoW/ID/clock | outbox store, EF | 204; 404; 409 not dead-lettered | Mandatory flow |
| `DELETE /api/dead-letters/{id}` | `DiscardDeadLetterRequestHandler` (Admin) | `IEmailOutboxStore`, `IAdminEventRepository`, UoW/ID/clock | outbox store, EF | 204; 404 | Mandatory flow |
| Worker scheduled loop: auto-close | `AutoCloseSolvedTicketsHandler` | `ITicketRepository`, `ITicketNotificationPlanner`, `IUnitOfWork`, `TimeProvider`, `IOptions<AutoCloseOptions>` | EF repository, planner, `UnitOfWork` | Host maps `Result` to log and next-run delay; handler idempotent | Mandatory flow; Worker constructor injection |
| New-ticket and assignee alerts | No separate entry point: queued by the handlers above through `ITicketNotificationPlanner` | `ITicketNotificationPlanner` | `TicketNotificationPlanner` over `IEmailOutbox` and `IEmailTemplateRenderer` | n/a | Per plan |
| `/health/live`, `/health/ready`, `/openapi/v1.json` | Exempt: framework operational endpoints (PHASE-01) | n/a | n/a | n/a | Exempt |

`SubmitTicketRequestHandler` (PHASE-05) is changed in this phase only to add `ITicketNotificationPlanner`; its row stays in PHASE-05's table.

Handlers must not depend on HTTP objects, EF types, concrete infrastructure, or transport response types. Link an approved decision for every exception.

## Razor Component Boundaries

Follow _template `RAZOR_COMPONENT_ARCHITECTURE.md`. ViewModels are Razor-only and feature-local; API contracts use DTO names.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| N/A — no Razor in this phase | n/a | n/a | n/a | `TicketSummaryDto`, `TicketDetailDto`, `MessageDto`, `TimelineEntryDto`, `CustomerTicketDto`, `CustomerReplyResponse`, `DeadLetterDto` and request types live in `TechStrap.Contracts`; Admin (PHASE-07) and Portal (PHASE-09) map them to their own ViewModels |

## Syntax Circus Packages

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.Common` | `Result`, `ICurrentUserService` | All handler outcomes and agent identity | Handler unit tests assert Result kinds |
| `SyntaxCircus.AspNetCore.Common` | ProblemDetails mapping, rate limiter helpers, forwarded IP, file response helpers if provided | Uniform error mapping; limits on customer and lost-link endpoints | `ResultMappingTests`, `CustomerRateLimitTests` |
| `SyntaxCircus.AspNetCore.Authentication` | JWT policies (agent/admin) | Agent endpoints; customer endpoints stay anonymous with in-handler token auth | `TicketAuthorizationTests`: no token 401, no group 403, agent on Admin route 403 |
| `SyntaxCircus.Storage` | Attachment bytes | Download, delete on ticket delete and erase | `AttachmentDownloadIntegrationTests`, `EraseRequesterIntegrationTests` |
| `SyntaxCircus.Email` | Outbound SMTP (via PHASE-05 Worker) | Planned alerts reach the outbox and Worker | `NotificationDeliveryIntegrationTests` against SMTP capture |
| `SyntaxCircus.AspNetCore.Serilog` | Logging with PII redaction | Redact requester email and names in logs | `LogRedactionTests` assert fixture email never appears in captured logs |

Markdig (via the map) renders agent reply Markdown if replies accept Markdown (**Assumption**: yes, rendered and sanitised through `IMarkdownRenderer`). Versions are in `03-PACKAGE-MAP.md`.

Record the exact package version in the linked package map. In the foundation phase, lock every selected version in `Directory.Packages.props`.

## Deliverables

- [ ] Contracts DTOs and requests for tickets, timeline, customer view, dead letters
- [ ] 21 handlers (20 request handlers plus the scheduled `AutoCloseSolvedTicketsHandler`) with interfaces, listed in the boundary table
- [ ] `TicketsController`, `RequesterAdminController`, `AttachmentsController`, `CustomerTicketsController`, `DeadLettersController`
- [ ] `ITicketNotificationPlanner` and implementation; planner wired into `SubmitTicketRequestHandler`
- [ ] Worker scheduled auto-close loop with options and health
- [ ] Notification email templates (agent reply, assignment, new ticket, customer-reply alert, solved notice, follow-up created, lost link, auto-close notice)
- [ ] PII redaction in Serilog configuration
- [ ] Rate limiting for customer routes and lost-link
- [ ] Integration test suite for the full ticket lifecycle; `.env.example` updated (`TECHSTRAP_AUTOCLOSE_DAYS` and others)

## Actionable Tasks

Write the named handler test class first for every handler task (substitutes for repositories and fake `TimeProvider`), then implement.

- [ ] **P06-T01** Add Contracts DTOs and request types; extend `ContractNamingTests`
  - **Depends on:** none (inside this phase)
  - **Validation:** `ContractNamingTests` pass; DTO round-trip serialisation tests pass; no internal-only field (note bodies, hashes) in `Customer*` DTOs (`CustomerDtoShapeTests`)
- [ ] **P06-T02** Implement `ITicketNotificationPlanner` with `TicketNotificationPlannerTests` (recipient rules per case, opt-in preferences, no email to the acting agent, no internal notes sent, branding of the ticket's current product) and `TicketNotificationPlanner` over `IEmailOutbox` and the renderer; add the new templates
  - **Depends on:** P06-T01
  - **Validation:** `TicketNotificationPlannerTests` pass; template snapshot tests for all new templates; outbox rows are created in the caller's transaction (integration test with rollback)
- [ ] **P06-T03** `ListTicketsRequestHandlerTests` and `ListTicketsRequestHandler`: five views, filters, FTS, paging, spam exclusion; add `TicketsController.List`
  - **Depends on:** P06-T01
  - **Validation:** handler tests per view; `ListTicketsIntegrationTests` over a seeded dataset of 500 tickets verify view membership, FTS match by subject, body and number, stable paging, and `EXPLAIN` uses the GIN index
- [ ] **P06-T04** `GetTicketRequestHandlerTests` and `GetTicketRequestHandler` composing detail and timeline (events plus messages, notes visible to agents)
  - **Depends on:** P06-T01
  - **Validation:** tests assert chronological timeline, event types rendered, internal notes present for agents; 404 for unknown id
- [ ] **P06-T05** `AddAgentReplyRequestHandlerTests` and handler: message, event, `Pending`, `first_response_at` once, linked KB ids, requester email via planner, attachments, sanitised Markdown, Closed rejected
  - **Depends on:** P06-T02
  - **Validation:** handler tests cover all branches; `AgentReplyIntegrationTests` assert message, event, status, `TicketArticle` rows and outbox row commit together
- [ ] **P06-T06** `AddInternalNoteRequestHandlerTests` and handler (no email, event `MessageAdded` with internal visibility)
  - **Depends on:** P06-T04
  - **Validation:** tests assert no outbox row, internal visibility, Closed rejected
- [ ] **P06-T07** `ChangeTicketStatusRequestHandlerTests` and handler using Domain transitions, solved notice via planner, concurrency conflict mapping
  - **Depends on:** P06-T02
  - **Validation:** every allowed and forbidden transition is covered; stale `rowVersion` yields 409; `solved_at` set and cleared per rules
- [ ] **P06-T08** `AssignTicketRequestHandlerTests`, `ChangeTicketPriorityRequestHandlerTests`, `MoveTicketProductRequestHandlerTests` and handlers
  - **Depends on:** P06-T02
  - **Validation:** assign notifies the assignee only when not self-assigning; inactive agent rejected; product move keeps the number and writes `ProductChanged`; priority event written
- [ ] **P06-T09** `AddTicketTagRequestHandlerTests`, `RemoveTicketTagRequestHandlerTests` and handlers
  - **Depends on:** P06-T04
  - **Validation:** duplicate add is 409; remove of absent tag is 404; `TagAdded`/`TagRemoved` events written
- [ ] **P06-T10** `MarkTicketSpamRequestHandlerTests` and `DeleteTicketRequestHandlerTests` plus handlers (`DeleteTicketIntegrationTests`: messages, events, attachment rows and files, and pending outbox rows are removed; an `AdminEvent` without ticket content is written)
  - **Depends on:** P06-T04
  - **Validation:** spam toggles and hides from views; delete leaves no rows or files for the ticket; delete is Admin-only and spam is available to Agents (`TicketAuthorizationTests`, D-022)
- [ ] **P06-T11** `EraseRequesterRequestHandlerTests` and handler; `EraseRequesterIntegrationTests`
  - **Depends on:** P06-T10
  - **Validation:** after erase no message body, name, email or attachment file for that requester remains; ticket numbers and event ids survive; tokens revoked; `AdminEvent` written; a search for the old email finds nothing
- [ ] **P06-T12** `GetAttachmentRequestHandlerTests` and handler with `AttachmentsController` (one route, `GET /api/attachments/{id}`, agent JWT or `X-Ticket-Token`); safe headers (`Content-Disposition: attachment`, `X-Content-Type-Options: nosniff`)
  - **Depends on:** P06-T05
  - **Validation:** agent can download any ticket's attachment; customer token only that ticket's public attachments; internal-note attachments denied (404 uniform); forged or expired token gets 404
- [ ] **P06-T13** `GetCustomerTicketRequestHandlerTests` and handler; `CustomerTicketsController.Get`
  - **Depends on:** P06-T01
  - **Validation:** invalid, expired and revoked tokens give byte-identical 404 bodies (`UniformNotFoundTests`); no tags, notes or other requesters in output; expiry slides on success
- [ ] **P06-T14** `AddCustomerReplyRequestHandlerTests` and handler incl. status reopen, assignee notification and follow-up creation on Closed; `CustomerReplyIntegrationTests`
  - **Depends on:** P06-T02, P06-T13
  - **Validation:** Pending and Solved become Open; Closed creates a linked ticket with new number, new token, `FollowUpCreated` on the parent and the parent unchanged; concurrent replies do not duplicate follow-ups for one click (idempotency window noted in tests)
- [ ] **P06-T15** `RequestNewAccessLinkRequestHandlerTests` and handler; rate limits per IP and per address
  - **Depends on:** P06-T02
  - **Validation:** known and unknown addresses return identical status, body and approximate timing (`LostLinkUniformityTests` compare responses); email goes only to the requester's own address; `CustomerRateLimitTests` return 429 after the limit
- [ ] **P06-T16** `ListDeadLettersRequestHandlerTests`, `RetryDeadLetterRequestHandlerTests`, `DiscardDeadLetterRequestHandlerTests` and handlers with `DeadLettersController`
  - **Depends on:** P06-T01
  - **Validation:** retry makes the row claimable by the Worker (integration test drains it); discard removes it from the list; both write `AdminEvent`; Admin-only
- [ ] **P06-T17** `AutoCloseSolvedTicketsHandlerTests` and handler; Worker `AutoCloseWorker` scheduled loop with `AutoCloseOptions` (days, interval, batch size) validated on start
  - **Depends on:** P06-T02, P06-T07
  - **Validation:** only tickets Solved at least N days are closed; spam and already-Closed skipped; running twice is a no-op; `StatusChanged` event has actor `System`; `AutoCloseWorkerTests` check the loop passes the stopping token; integration test with a fake clock closes a seeded ticket
- [ ] **P06-T18** Wire `ITicketNotificationPlanner` into `SubmitTicketRequestHandler` for new-ticket alerts and update its tests
  - **Depends on:** P06-T02
  - **Validation:** updated `SubmitTicketRequestHandlerTests` assert alerts go only to agents opted in for that product; PHASE-05 tests still pass
- [ ] **P06-T19** Configure Serilog PII redaction (requester email, names, tokens) and rate limits for customer routes; add `LogRedactionTests` and options validation tests
  - **Depends on:** P06-T13
  - **Validation:** a fixture email and token submitted through the API never appear in captured log output; bad limit options fail startup
- [ ] **P06-T20** Extend architecture tests (controller delegation, handler constructor rules, `Customer*` DTO shape, no handler returns transport types) and add `TicketLifecycleEndToEndTests` (submit, list, reply, customer reply, solve, auto-close, customer reply creating a follow-up) plus OpenAPI surface and `.env.example` updates
  - **Depends on:** P06-T05, P06-T11, P06-T14, P06-T16, P06-T17
  - **Validation:** `dotnet test` green; the end-to-end test passes on Testcontainers with a fake clock; `OpenApiSurfaceTests` lists every route in the table

## Success Criteria

- [ ] All 21 handlers in the boundary table exist with interfaces and each controller action or Worker loop delegates to exactly one of them (`ControllerBoundaryTests`).
- [ ] `TicketLifecycleEndToEndTests` pass: submit, triage, reply, customer reply, solve, auto-close, reply-after-close creating a follow-up with `parent_ticket_id`.
- [ ] Ticket number is unchanged across a product move; Closed tickets reject every agent mutation.
- [ ] Customer endpoints return a uniform 404 for invalid, expired and revoked tokens and a uniform 202 for lost-link requests; customers never receive internal notes, tags or other requesters' data.
- [ ] Erase requester leaves no personal data behind (message bodies, names, emails, attachment files) and ticket history remains consistent; hard delete removes all ticket data and files.
- [ ] Assignee, new-ticket and requester alerts reach the outbox in the same transaction and are delivered by the Worker.
- [ ] Dead letters can be listed, retried and discarded by an Admin only.
- [ ] Logs contain no requester email, name or token (`LogRedactionTests`).
- [ ] CI is green; `/openapi/v1.json` lists all new routes.

## Boundary Validation

- [ ] Application use-case entry points delegate to the named handlers listed above.
- [ ] Framework-owned operational or static exemptions execute no application workflow.
- [ ] Handler constructor dependencies contain only approved abstractions.
- [ ] Persistence and integration entities do not cross infrastructure boundaries.
- [ ] Cancellation reaches asynchronous handler dependencies.
- [ ] Expected outcomes and transport mapping have focused tests.
- [ ] Infrastructure implementations have integration coverage where applicable.
- [ ] Inline Razor components contain only simple parameters and, at most, one
      trivial synchronous `EventCallback`-forwarding callback. (N/A.)
- [ ] Every component beyond the inline ceiling uses paired `.razor` and
      `.razor.cs` files, with all C# in code-behind. (N/A.)
- [ ] Each Razor ViewModel is feature-local and presentation-only; the recorded
      direct-model decision does not expose an API ViewModel. (N/A.)
- [ ] A factory or presentation service is used only for non-trivial mapping,
      asynchronous assembly, or multiple dependencies. (N/A.)
- [ ] API request and response contracts use DTO names and contracts, never
      Razor ViewModels.
- [ ] Repeated or business-meaningful literals are named constants at the
      right scope, not bare magic values (page sizes, auto-close days, token slide days, marker text for erased content, status names).
- [ ] Duplicated-looking logic across flows was evaluated for genuine
      divergence before extracting (or intentionally not extracting) a shared
      abstraction (agent reply versus customer reply, and the two attachment routes: shared handler only for attachments; replies stay separate since rules, notifications and follow-up behaviour diverge).

## Risks and Open Questions

- [ ] The handler count is large; keep each task small and merge handler-by-handler to avoid a monolithic PR.
- [ ] Uniform lost-link timing is hard to guarantee; enqueue work off the response path or do constant-work steps, and test coarse timing only.
- [ ] Follow-up ticket idempotency on double submit (**Assumption**: short idempotency window keyed by token and body hash).
- [ ] Erase versus audit trail: event payloads must not contain PII (confirm in PHASE-03 events and add a test here).
- [ ] Who may delete or erase: Admin only (D-022, resolved).
- [ ] Whether Solved sends a requester email (**Assumption**: yes, short notice); confirm.
- [ ] Reply bodies accept Markdown (**Assumption**) versus plain text/HTML; confirm in the UX brief for Admin.
- [x] Auto-close default of 7 days is confirmed (D-008); it is a per-installation option only (not per product) for the core.
- [ ] Attachments on agent replies are an **Assumption** (not stated in the plan); drop `IAttachmentStore` from `AddAgentReplyRequestHandler` if the owner declines.
- [ ] Live refresh of Worker-driven changes (auto-close) is deferred to PHASE-10.

## Handoff

Before PHASE-07 and PHASE-08 start: the full ticket lifecycle passes end-to-end in CI, the OpenAPI document is complete for tickets, customers and dead letters, and Contracts DTOs are stable for typed clients. PHASE-07 additionally needs PHASE-02; PHASE-09 additionally needs PHASE-02 and PHASE-08. Next: [PHASE-07-admin-app.md](PHASE-07-admin-app.md) and [PHASE-08-knowledge-base.md](PHASE-08-knowledge-base.md).
