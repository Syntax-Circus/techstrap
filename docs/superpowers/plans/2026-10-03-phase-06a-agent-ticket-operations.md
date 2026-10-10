# PHASE-06a Agent Ticket Operations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agents can work tickets headlessly through the API. They can:
- list, search and count the queue;
- read a ticket with its full timeline;
- reply in Markdown, with files and linked KB articles; the customer is emailed a link;
- add internal notes;
- change status (Solved emails the customer), assignee (the assignee is emailed), priority and product;
- add and remove tags;
- mark and clear spam;
- download attachments.

**Architecture:**
- **Thin controllers, one handler each.** Each operation has one named Application handler behind a thin controller. State-changing handlers share one small pattern: `TicketMutation`.
  - It resolves the active agent.
  - It loads the ticket.
  - It compares the client's `RowVersion`.
  - It applies the Domain method.
  - It commits.
  - It returns a fresh `TicketStateDto`.
- **Customer-facing email follows D-033.**
  - An Application `ITicketNotificationPlanner` stages small template-data outbox rows in the caller's transaction.
  - The Worker renders them at send time. Reply emails load the message body by id at send time.
- **Markdown.** Agent text is rendered by Markdig behind `IMarkdownRenderer`, with raw HTML off, and is always followed by `IHtmlSanitizer`.

**Tech Stack:**
- .NET 10, ASP.NET Core controllers, EF Core with Npgsql.
- Markdig 1.4.0, which is already pinned; this phase adds the first reference.
- HtmlSanitizer 9.2.1039.
- SyntaxCircus.Storage, SyntaxCircus.Email (via the PHASE-05 adapters).
- Testing: xUnit v3, Shouldly, NSubstitute, and Testcontainers for Postgres and Mailpit.

**Spec:**
- The core spec is `docs/architecture/PHASE-06-ticket-operations.md`, for the 06a half.
- `docs/architecture/02-ARCHITECTURE.md` section 7.3.
- `docs/architecture/UX-BRIEF-admin.md`, for the queue, detail, reply and conflict flows.
- Decision-log entries: D-006, D-008, D-009, D-014, D-016, D-022, D-024, D-030, D-033 and D-034.
- The owner decisions of 2026-10-03, recorded as D-035 in Task 1.

### Owner decisions (2026-10-03), recorded as D-035 in Task 1
- **Two PRs.** PHASE-06 lands as 06a (this plan) and 06b.
  - 06a is agent operations.
  - 06b covers:
    - customer routes and replies;
    - follow-ups;
    - the lost link;
    - new-ticket and customer-reply alerts;
    - auto-close;
    - delete and erase;
    - dead letters;
    - the customer path of the attachment download.
- **Markdown.** Agent reply bodies are Markdown, rendered and then sanitized. Internal notes use the same composer, so they are Markdown too.
- **Attachments on replies.** Agents may attach files to public replies. The limits match customer uploads: 5 files, 10 MiB each, 25 MiB per message, checked by file content.
- **Solved notice.** When an agent sets Solved, the customer gets a short notice that includes the ticket link.

### Technical decisions this plan makes (recorded as D-036 in Task 1; the owner confirms them at plan review)
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

## Global Constraints

- **Build.** .NET SDK 10.0.401, target `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`.
  - Private fields use `_camelCase`, including `private static readonly`.
  - Constants use PascalCase.
  - Namespaces are file-scoped.
- **Packages.** Versions are managed centrally, and `Directory.Packages.props` must agree with `docs/architecture/03-PACKAGE-MAP.md` (`scripts/Check-PackageVersions.ps1`).
  - No csproj sets `Version`.
  - This phase adds `<PackageReference Include="Markdig" />` to Infrastructure only, and updates the map's phase column for Markdig to `P06, P08`.
- **Project references are fixed.**
  - Domain references nothing.
  - Contracts references nothing, has no attributes and no enums, and every public non-static type ends in `Dto`, `Request` or `Response`.
  - Application references Domain and Contracts, and only the `SyntaxCircus.Common` package.
  - Infrastructure references Application, Domain and Contracts, and may use packages.
  - Api and Worker reference Application, Infrastructure and Contracts. The Worker never references Api.
- **Handlers.**
  - A handler is a `public sealed class XxxHandler : IXxxHandler`, and `HandleAsync(..., CancellationToken)` takes the token last.
  - Constructor parameters may only be Application interfaces, `TimeProvider`, `IOptions<>`, `IOptionsSnapshot<>`, `IOptionsMonitor<>`, `ILogger` or `ILogger<>`.
  - Handlers never return or receive transport types. Handlers are auto-registered by name.
  - Every public `I*` interface in Application ends each async method with a `CancellationToken`, and exposes no `Microsoft.AspNetCore*`, EF, `Npgsql`, `System.Net.Http` or Infrastructure types.
- **Controllers.**
  - A class-level `[Authorize(Policy = AuthorizationPolicies.Agent)]`.
  - No constructor dependencies.
  - Each action takes exactly one `[FromServices]` handler interface and a `CancellationToken`. It calls the handler once, passing the token last, and maps the result with `result.ToActionResult(this, onSuccess)`.
  - Every action gets a row in `ControllerActions.ExpectedSuccess`.
  - Action names are unique per controller.
  - Route parameters are `{id:guid}`, `{tagId:guid}` or `{reference}`.
- **Agent identity.** Use `ICurrentAgentClaims` and `CurrentAgent.RequireActiveAsync`, not `ICurrentUserService`. Use `Actor.ForAgent(agent.Id)` for Domain calls.
- **Results.**
  - Return `new ResultError(code, message, kind, target?)`, where `target` is only for `Validation`.
  - Validation maps to 400, never 422.
  - Convert Domain failures with `domainResult.Error!.ToError()` or `.ToResult()`.
  - Commit conflicts come back as Conflict with the code `concurrency-conflict`, `duplicate` or `reference-violation`.
- **Persistence.**
  - Write inside `await using var scope = await unitOfWork.BeginAsync(ct)` with exactly one `CommitAsync`.
  - Generate migrations only with `dotnet ef migrations add`; never hand-write them. This plan needs no migration.
- **Secrets.**
  - Plaintext access tokens appear only in `email_outbox.payload` (D-033), never in logs, responses or other columns.
  - Outbox payloads are never logged.
  - Agent email and surname never reach customer emails or From/Reply-To (D-024).
  - Customers see `AgentPublicIdentity.Resolve(agent, product.Branding.DisplayName)`.
- **Customer-facing copy (BRAND.md).**
  - Product branding leads.
  - The only TechStrap mark is the "Powered by TechStrap" footer, which follows `EmailBrandingOptions.ShowPoweredBy`.
  - No mascot or puns.
  - The accent comes from `ProductAccent.TryDerive`.
- **Commits.** Use Conventional Commits, each ending with exactly:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden.**
  - `git add -f`.
  - Committing `.superpowers/`.
  - `docker compose down -v`.
  - Real secrets.

  Run `git diff --cached --stat` before every commit.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release` gives 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release` passes (needs Docker).
  - `pwsh -File scripts/Invoke-ScriptTests.ps1` passes.
  - `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api` is clean.

## Review Focus

These are the inputs and conditions most likely to hurt a real agent or customer. Each is pinned by a test in the task that owns it:

1. **Two agents act on one ticket at once.**
   - The second state change with the old `RowVersion` gets `409 concurrency-conflict` and changes nothing.
   - A reply racing a status change still lands, because replies do not require a version.
   - Pinned in Task 11 by `TicketConcurrencyIntegrationTests.A_stale_row_version_changes_nothing_and_a_parallel_reply_still_lands`.
2. **An agent writes Markdown containing HTML or script**, for example `<script>`, `[x](javascript:alert(1))`, or `<img onerror>`. It is stored and emailed as inert text or safe markup, with no live script, and links get `rel=noopener noreferrer nofollow`.
   - Pinned in Task 3 by `MarkdownRendererTests.Raw_html_and_script_links_in_markdown_never_survive_render_and_sanitize`.
3. **An agent replies on a ticket whose requester has no usable address.** This covers an erased requester (06b data) or a requester row that no longer exists. The reply is saved, no email is queued, and the request still succeeds.
   - Pinned in Task 10 by `AddAgentReplyRequestHandlerTests.A_reply_to_an_erased_requester_is_saved_without_an_email`.
4. **A reply with attachments fails at commit.** No orphan files remain in storage, and a retry works.
   - Pinned in Task 10 by `AgentReplyIntegrationTests.A_failed_commit_leaves_no_message_rows_and_no_files`.
5. **A long or hostile search string**, for example 10 000 characters, `'; drop table`, or `&|!:*`. The list returns a 200 with normal paging and never a 500. Ticket-number search still finds `ORB-42` exactly.
   - Pinned in Task 8 by `ListTicketsIntegrationTests.Hostile_and_oversized_search_text_is_safe_and_number_search_is_exact`.

---
## File Structure

- **Docs**
  - Decision log: D-035 (owner decisions) and D-036 (technical decisions).
  - `PHASE-06-ticket-operations.md`: the 06a/06b split, mismatch fixes, and the carried-forward items moved to 06b.
  - `02-ARCHITECTURE.md`: section 7.3 rows, section 3.1 abstractions, section 11.1 config.
  - `PHASE-07-admin-app.md`: transport and concurrency notes.
  - `03-PACKAGE-MAP.md`: Markdig phase.
- **Contracts** (`src/TechStrap.Contracts/Tickets/`)
  - `TicketNames.cs`: the constants classes.
  - `TicketDtos.cs`: summary, detail, message, event, attachment, tag, state and counts.
  - `TicketRequests.cs`.
- **Application**
  - `Content/IMarkdownRenderer.cs`.
  - `Tickets/`
    - `TicketErrors`, `TicketNameParser`, `TicketDtoMapper`, `TicketMutation`, `TicketState`, `TicketViewCounts`.
    - `Notifications/ITicketNotificationPlanner.cs`, `AdminLinkOptions.cs`, `TicketNotices.cs`.
    - One handler file per operation: `ListTicketsRequestHandler`, `CountTicketViewsRequestHandler`, `GetTicketRequestHandler`, `AddAgentReplyRequestHandler`, `AddInternalNoteRequestHandler`, `ChangeTicketStatusRequestHandler`, `AssignTicketRequestHandler`, `ChangeTicketPriorityRequestHandler`, `MoveTicketProductRequestHandler`, `AddTicketTagRequestHandler`, `RemoveTicketTagRequestHandler`, `MarkTicketSpamRequestHandler`, `GetAttachmentRequestHandler`.
  - `Attachments/AttachmentContent.cs`.
  - `Email/`: new kinds and payload records in `EmailTemplates.cs`, new renderer methods, drain dispatch.
  - `Persistence/`: new repository methods.
- **Infrastructure**
  - `Content/MarkdigMarkdownRenderer.cs`, with sanitizer allowlist additions.
  - `Persistence/Repositories/TicketRepository.cs` and `KbRepository.cs`: the new reads.
  - `Attachments/AttachmentStore.cs`: `OpenReadAsync`.
  - `Email/EmailTemplateRenderer.cs`: new templates and the HTML-to-text helper.
  - `Tickets/TicketNotificationPlanner.cs` and `Tickets/TicketOperationsServiceCollectionExtensions.cs`.
- **Api**
  - `Controllers/TicketsController.cs`, `Controllers/AgentReplyForm.cs`, `Controllers/AttachmentsController.cs`, `Controllers/AttachmentDownloadResult.cs`.
  - `Program.cs`: `AddTechStrapTicketOperations`.
  - `.env.example`: `TECHSTRAP_ADMIN_PUBLIC_URL`.
- **Tests**
  - Application handler tests, one file per handler.
  - Infrastructure integration tests: Markdown, repository additions, planner, renderer, drain, reply, concurrency, list.
  - Api endpoint tests (`Tickets/*EndpointTests`).
  - `TicketLifecycleEndToEndTests`.
  - Coverage-harness updates: `ControllerActions` rows; multipart support in `AgentAccessCoverageTests`; a download result in `ResultMappingTests`.

---

### Task 1: Record the decisions and split PHASE-06

**Files:**
- Modify: `docs/architecture/04-DECISION-LOG.md`. Add the approval-basis bullet, index rows D-035 and D-036, and their sections at the end.
- Modify: `docs/architecture/PHASE-06-ticket-operations.md`
- Modify: `docs/architecture/02-ARCHITECTURE.md`. Change sections 3.1, 7.3 and 11.1.
- Modify: `docs/architecture/PHASE-07-admin-app.md`. Change the reply transport and concurrency lines.
- Modify: `docs/architecture/03-PACKAGE-MAP.md`. Set the Markdig row's phase column to `P06, P08`.
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` and `docs/architecture/00-DISCOVERY-INDEX.md`. Set PHASE-05 to `Complete` (PR #5 merged) and PHASE-06 to `In progress (06a)`.

**Interfaces:** none (docs only).

- [ ] **Step 1: Add D-035 and D-036**

Follow the format of D-032 to D-034 (Status, Date, Owner, Context, Decision, Alternatives Considered, Consequences, Related). Approval-basis bullet: "**Owner decision (2026-10-03, PHASE-06 planning):** D-035 (PHASE-06 split, Markdown replies, agent attachments, Solved notice). D-036 was proposed in the PHASE-06a plan and approved when the owner approved the plan."

Index rows:

```
| D-035 | PHASE-06 split into 06a/06b; Markdown agent replies; agent reply attachments; Solved notice | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-06, PHASE-07, PHASE-09 |
| D-036 | Ticket operations API shape: RowVersion on state changes, TicketStateDto returns, idempotent tags, string enums, multipart replies, lookup by id or number | Approved (owner, PHASE-06a plan review) | 2026-10-03 | PHASE-06, PHASE-07, PHASE-11 |
```

**D-035 Decision**, one bullet per owner decision from the plan header:
- the split, with the full 06b list;
- Markdown for replies and notes;
- agent attachments with the customer limits;
- the Solved notice with the ticket link.

**D-035 Consequences:**
- 06a introduces `IMarkdownRenderer`, which PHASE-08 reuses.
- The sanitizer allowlist grows by `h5 h6 hr del s`. PHASE-08 adds tables and images.
- `ITicketNotificationPlanner` lands in 06a with three cases: reply, solved and assigned. 06b adds the new-ticket, customer-reply and lost-link cases.

**D-036 Decision:** copy the "Technical decisions" bullets from the plan header verbatim, from "Concurrency" to "Spam on Closed tickets".

**D-036 Consequences:**
- PHASE-07's typed client sends `RowVersion` in the body and replaces its cached state with the `TicketStateDto` returned by every write.
- Replies are posted as multipart.
- 06b must change `GET /api/attachments/{id}` from the `Agent` policy to a policy model that admits both credentials. Two options are a separate customer route, `GET /api/customer/attachments/{id}`, or a combined scheme policy. Decide that in 06b; prefer the separate route, because it keeps "Public stands alone" (D-034).
- The Worker drain handler gains `ITicketRepository` to load reply bodies at send time.

- [ ] **Step 2: Align PHASE-06**

In `PHASE-06-ticket-operations.md`:

- **Objective.** Add a final paragraph: "Delivered as two PRs (D-035): **06a** agent operations (T01–T09, the spam half of T10, the agent half of T12, T21, the reply half of T22, and the 06a parts of T20); **06b** everything else."
- **Architecture Decisions.** Make these replacements:
  - **`ICurrentUserService`** becomes "`ICurrentAgentClaims` plus `CurrentAgent.RequireActiveAsync` (PHASE-04)".
  - **"Worker scheduled work uses constructor injection"** becomes "Worker loops resolve their scoped handler from a fresh DI scope per iteration (as `EmailOutboxWorker`)".
  - **`AgentPolicy` / `AdminPolicy`** become "`AuthorizationPolicies.Agent` / `.Admin`".
  - **The anonymous customer endpoints wording** becomes "customer endpoints use the explicit `Public` policy plus rate limits (D-034) and are authorized by the token inside the handler".
  - **The notification planner bullet:** change "implemented in Infrastructure over `IEmailOutbox` and `IEmailTemplateRenderer`" to "implemented in Infrastructure over `IEmailOutbox`; it stages template data only, and the Worker renders at send time (D-033)".
  - **The concurrency bullet** becomes "State-changing requests carry `RowVersion` in the body; replies and notes accept it optionally; every write returns `TicketStateDto` with the new `RowVersion` (D-036)".
  - **The tags rows** become "idempotent (D-036)".
  - **The success codes for status, assignee, priority, product, tags and spam** become "200 `TicketStateDto`".
  - **Reply and note** become "201 `AgentMessageResponse`".
  - **"409 invalid transition"** stays; add "(never 422)".
- **Application Boundaries table.** Update the dependency cells to the constructors this plan builds:
  - `ListTicketsRequestHandler`: `ITicketRepository, IAgentRepository, IProductRepository, ITagRepository, ICurrentAgentClaims`.
  - `GetTicketRequestHandler`: adds `IRequesterRepository, IAgentRepository, IProductRepository, ITagRepository, ICurrentAgentClaims`.
  - `AddAgentReplyRequestHandler`: drops `IAccessTokenService`, because the planner issues the token, and adds `ILogger`.
  - Add a row `GET /api/tickets/counts` → `CountTicketViewsRequestHandler`, returning `200 TicketViewCountsResponse`.
  - Change `GET /api/tickets/{id}` to `GET /api/tickets/{reference}` (id or number).
- **Risks and Open Questions.** Tick these items with "Resolved (D-035)": the Markdown question, attachments on agent replies, and the Solved email. Add these bullets:
  - "[ ] Spam on a Closed ticket is rejected with `ticket-closed` (Domain rule; known limit, D-036)."
  - "[ ] Date-range filter dropped from v1 (D-036)."
- Mark each carried-forward bullet with its target: "(06b)" for erase and hard-delete, "(PHASE-12)" for search on realistic data, and "(06b with PHASE-09)" for customer DTOs.

- [ ] **Step 3: Align 02-ARCHITECTURE and PHASE-07**

**`02-ARCHITECTURE.md`:**
- **Section 7.3.** Make the rows match the PHASE-06 table as edited in Step 2: the same codes and DTO names, and `{reference}` for detail. Add the counts row. Mark the reply row "Agent; multipart (text fields + files)".
- **Section 3.1.** Add these abstraction rows:
  - `IMarkdownRenderer`: Markdig, raw HTML off, always followed by `IHtmlSanitizer`.
  - `ITicketNotificationPlanner`: stages outbox template data in the caller's transaction.
- **Section 11.1.** Add `TECHSTRAP_ADMIN_PUBLIC_URL`: "optional; when set, agent assignment emails link to `{url}/tickets/{number}`".

**`PHASE-07-admin-app.md`:**
- Change "multipart-free reply submit" to "multipart reply submit (D-036)".
- Change "concurrency-token header/body handling" to "`RowVersion` in the request body; replace local state with the returned `TicketStateDto` (D-036)".
- Change "422" to "409".

- [ ] **Step 4: Check and commit**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS, which includes the package map check.

Then run `grep -n "ICurrentUserService\|AgentPolicy\|422" docs/architecture/PHASE-06-ticket-operations.md`.
Expected: no hits outside quoted history.

```bash
git add docs
git commit -m "docs: record PHASE-06 split and ticket API decisions (D-035, D-036)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 2: Contracts for ticket operations

**Files:**
- Create: `src/TechStrap.Contracts/Tickets/TicketNames.cs`
- Create: `src/TechStrap.Contracts/Tickets/TicketDtos.cs`
- Create: `src/TechStrap.Contracts/Tickets/TicketRequests.cs`
- Create: `tests/TechStrap.Application.Tests/Tickets/TicketNamesParityTests.cs`
- Create: `tests/TechStrap.Api.Tests/Tickets/TicketDtoSerializationTests.cs`

**Interfaces:**
- Produces (exact; later tasks use these names):

```csharp
namespace TechStrap.Contracts.Tickets;

/// <summary>Wire names for ticket enums. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class TicketStatuses
{
    public const string New = "New";
    public const string Open = "Open";
    public const string Pending = "Pending";
    public const string Solved = "Solved";
    public const string Closed = "Closed";
}

public static class TicketPriorities
{
    public const string Low = "Low";
    public const string Normal = "Normal";
    public const string High = "High";
    public const string Urgent = "Urgent";
}

/// <summary>Queue views (D-024). Spam lists only spam; every other view excludes it.</summary>
public static class TicketViews
{
    public const string Unassigned = "Unassigned";
    public const string Mine = "Mine";
    public const string Open = "Open";
    public const string Pending = "Pending";
    public const string All = "All";
    public const string Spam = "Spam";
    public const string Default = All;
}

public static class TicketEventTypes
{
    public const string Created = "Created";
    public const string MessageAdded = "MessageAdded";
    public const string StatusChanged = "StatusChanged";
    public const string Assigned = "Assigned";
    public const string ProductChanged = "ProductChanged";
    public const string PriorityChanged = "PriorityChanged";
    public const string TagAdded = "TagAdded";
    public const string TagRemoved = "TagRemoved";
    public const string MarkedSpam = "MarkedSpam";
    public const string FollowUpCreated = "FollowUpCreated";
}

public static class MessageAuthorTypes
{
    public const string Requester = "Requester";
    public const string Agent = "Agent";
    public const string System = "System";
}

public static class MessageVisibilities
{
    public const string Public = "Public";
    public const string Internal = "Internal";
}

/// <summary>Limits on agent ticket operations.</summary>
public static class TicketOperationLimits
{
    public const int MaxLinkedArticles = 10;
}
```

```csharp
namespace TechStrap.Contracts.Tickets;

public sealed record TicketTagDto(Guid Id, string Name, string Colour);

public sealed record TicketSummaryDto(
    Guid Id, string Number, string Subject, string Status, string Priority,
    Guid ProductId, string ProductName,
    Guid RequesterId, string RequesterEmail, string? RequesterName,
    Guid? AssigneeId, string? AssigneeName,
    bool IsSpam, IReadOnlyList<TicketTagDto> Tags,
    DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt);

public sealed record TicketViewCountsResponse(int Unassigned, int Mine, int Open, int Pending, int All, int Spam);

public sealed record AttachmentDto(Guid Id, string FileName, string ContentType, long Size);

public sealed record LinkedArticleDto(Guid Id, string Title, string Slug);

/// <summary>A timeline message. BodyHtml is sanitized HTML. AuthorName is the agent's own name (agent views only) or the requester's name or email.</summary>
public sealed record MessageDto(
    Guid Id, string AuthorType, Guid? AuthorId, string? AuthorName, string Visibility, string BodyHtml,
    DateTimeOffset CreatedAt, IReadOnlyList<AttachmentDto> Attachments, IReadOnlyList<LinkedArticleDto> LinkedArticles);

/// <summary>A ticket event. PayloadJson holds ids and enum names only (never free text).</summary>
public sealed record TicketEventDto(
    Guid Id, string Type, string ActorType, Guid? ActorId, string? ActorName, string PayloadJson, DateTimeOffset OccurredAt);

public sealed record TicketRequesterDto(Guid Id, string Email, string? Name, string? ExternalUserRef);

public sealed record TicketDetailDto(
    Guid Id, string Number, string Subject, string Status, string Priority,
    Guid ProductId, string ProductName, TicketRequesterDto Requester,
    Guid? AssigneeId, string? AssigneeName, bool IsSpam, IReadOnlyList<TicketTagDto> Tags,
    string Channel, Guid? ParentTicketId, string? MetadataJson, bool MetadataTrusted,
    DateTimeOffset CreatedAt, DateTimeOffset? FirstResponseAt, DateTimeOffset? SolvedAt, DateTimeOffset? ClosedAt,
    DateTimeOffset LastActivityAt, uint RowVersion,
    IReadOnlyList<MessageDto> Messages, IReadOnlyList<TicketEventDto> Events);

/// <summary>Returned by every ticket write (D-036): the fresh state and the RowVersion for the next write.</summary>
public sealed record TicketStateDto(
    Guid Id, string Number, string Status, string Priority, Guid ProductId, Guid? AssigneeId,
    bool IsSpam, IReadOnlyList<Guid> TagIds, DateTimeOffset LastActivityAt, uint RowVersion);

public sealed record AgentMessageResponse(MessageDto Message, TicketStateDto Ticket);
```

```csharp
namespace TechStrap.Contracts.Tickets;

public sealed record ListTicketsRequest(
    string? View, Guid? ProductId, string? Status, string? Priority, Guid? AssigneeId,
    Guid? TagId, Guid? RequesterId, string? Search, int Page, int PageSize);

/// <summary>StatusAfter: null or "Pending" (default Domain behavior) or "Solved" ("Send and solve"). Files travel beside this record (D-016).</summary>
public sealed record AddAgentReplyRequest(string? Body, IReadOnlyList<Guid>? LinkedArticleIds, string? StatusAfter, uint? RowVersion);

public sealed record AddInternalNoteRequest(string? Body, uint? RowVersion);
public sealed record ChangeTicketStatusRequest(string? Status, uint? RowVersion);
public sealed record AssignTicketRequest(Guid? AssigneeId, uint? RowVersion);
public sealed record ChangeTicketPriorityRequest(string? Priority, uint? RowVersion);
public sealed record MoveTicketProductRequest(Guid? ProductId, uint? RowVersion);
public sealed record AddTicketTagRequest(Guid? TagId, uint? RowVersion);
public sealed record MarkTicketSpamRequest(bool? IsSpam, uint? RowVersion);
```

`RemoveTicketTag` takes `rowVersion` from the query string, because DELETE has no body. No request record is needed for it.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Application.Tests/Tickets/TicketNamesParityTests.cs`. Application.Tests references Domain and Contracts through Application.

```csharp
using TechStrap.Contracts.Tickets;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tickets;

public sealed class TicketNamesParityTests
{
    [Fact]
    public void Status_names_match_the_domain_enum() =>
        Names(typeof(TicketStatuses)).ShouldBe(Enum.GetNames<TicketStatus>(), ignoreOrder: true);

    [Fact]
    public void Priority_names_match_the_domain_enum() =>
        Names(typeof(TicketPriorities)).ShouldBe(Enum.GetNames<TicketPriority>(), ignoreOrder: true);

    [Fact]
    public void View_names_match_the_application_enum() =>
        Names(typeof(TicketViews)).ShouldBe(Enum.GetNames<TechStrap.Application.Tickets.TicketView>(), ignoreOrder: true);

    [Fact]
    public void Event_type_names_match_the_domain_enum() =>
        Names(typeof(TicketEventTypes)).ShouldBe(Enum.GetNames<TicketEventType>(), ignoreOrder: true);

    [Fact]
    public void Author_and_visibility_names_match_the_domain_enums()
    {
        Names(typeof(MessageAuthorTypes)).ShouldBe(Enum.GetNames<AuthorType>(), ignoreOrder: true);
        Names(typeof(MessageVisibilities)).ShouldBe(Enum.GetNames<MessageVisibility>(), ignoreOrder: true);
    }

    // Distinct values of every public const string, so an alias such as TicketViews.Default does not count twice.
    private static string[] Names(Type holder) =>
        [.. holder.GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!).Distinct()];
}
```

`tests/TechStrap.Api.Tests/Tickets/TicketDtoSerializationTests.cs`: round-trip a fully populated `TicketDetailDto` and an `AgentMessageResponse` through `JsonSerializer` with `JsonSerializerDefaults.Web`. Assert:
- equality on the scalar members;
- `rowVersion` serializes as a JSON number;
- `status` serializes as the string `"Pending"`.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter TicketNamesParityTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the three Contracts files exactly as shown under Interfaces, with an XML summary on each type.

- [ ] **Step 4: Run the tests, then the architecture tests**

Run: the two filtered test classes, then `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`.
Expected: PASS. `ContractNamingTests` accepts static classes and `*Dto`, `*Request` and `*Response` records.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(contracts): ticket operation DTOs, requests and wire names" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 3: Markdown renderer and sanitizer allowlist

**Files:**
- Create: `src/TechStrap.Application/Content/IMarkdownRenderer.cs`
- Create: `src/TechStrap.Infrastructure/Content/MarkdigMarkdownRenderer.cs`
- Modify: `src/TechStrap.Infrastructure/Content/HtmlSanitizerAdapter.cs`. Add `h5`, `h6`, `hr`, `del` and `s` to `_tags`.
- Modify: `src/TechStrap.Infrastructure/Content/ContentServiceCollectionExtensions.cs`. Register the renderer.
- Modify: `src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj`. Add `<PackageReference Include="Markdig" />`.
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/MarkdownRendererTests.cs`

**Interfaces:**

```csharp
namespace TechStrap.Application.Content;

/// <summary>
/// Markdown to HTML (D-014, D-035). Raw HTML in the source is not passed through. The output is NOT safe on its own:
/// callers always pass it through <see cref="IHtmlSanitizer"/>. PHASE-08 reuses this for KB articles.
/// </summary>
public interface IMarkdownRenderer
{
    string ToHtml(string markdown);
}
```

Infrastructure `internal sealed class MarkdigMarkdownRenderer : IMarkdownRenderer` builds the pipeline once:

```csharp
private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
    .DisableHtml()                          // raw HTML is escaped, never emitted
    .UseSoftlineBreakAsHardlineBreak()      // a single line break stays a line break (agents type plain prose)
    .UseAutoLinks()                         // bare https://... becomes a link
    .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
    .Build();

public string ToHtml(string markdown) => Markdown.ToHtml(markdown ?? string.Empty, _pipeline);
```

Register it with `services.TryAddSingleton<IMarkdownRenderer, MarkdigMarkdownRenderer>();` inside `AddTechStrapContent`. Check the extension method names, such as `UseEmphasisExtras` and `UseAutoLinks`, against Markdig 1.4.0 after restore. If one is named differently, use the Markdig equivalent and say so in your report.

- [ ] **Step 1: Write the failing tests**

`MarkdownRendererTests` (no Docker needed) composes the real renderer and the real sanitizer, as the handlers will:

```csharp
public sealed class MarkdownRendererTests
{
    private readonly MarkdigMarkdownRenderer _markdown = new();
    private readonly HtmlSanitizerAdapter _sanitizer = new();

    private string Render(string markdown) => _sanitizer.Sanitize(_markdown.ToHtml(markdown));

    [Fact]
    public void Common_formatting_survives()
    {
        var html = Render("Hi **Ann**,\nline two\n\n- one\n- two\n\n`code` and ~~old~~\n\n> quoted\n\n---\n\n##### small heading");
        html.ShouldContain("<strong>Ann</strong>");
        html.ShouldContain("<br");
        html.ShouldContain("<ul>");
        html.ShouldContain("<code>code</code>");
        html.ShouldContain("<del>old</del>");
        html.ShouldContain("<blockquote>");
        html.ShouldContain("<hr");
        html.ShouldContain("<h5>");
    }

    [Fact]
    public void Links_get_safe_rel_and_bare_urls_are_linked()
    {
        var html = Render("See [docs](https://example.com/docs) or https://example.com/faq");
        html.ShouldContain("href=\"https://example.com/docs\"");
        html.ShouldContain("href=\"https://example.com/faq\"");
        html.ShouldContain("rel=\"noopener noreferrer nofollow\"");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("![x](javascript:alert(1))")]
    [InlineData("<a href=\"https://e.com\" onclick=\"alert(1)\">x</a>")]
    [InlineData("[x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)")]
    [InlineData("<iframe src=\"https://evil\"></iframe>")]
    [InlineData("<style>body{display:none}</style>")]
    public void Raw_html_and_script_links_in_markdown_never_survive_render_and_sanitize(string markdown)
    {
        var html = Render(markdown);
        html.ShouldNotContain("<script", Case.Insensitive);
        html.ShouldNotContain("javascript:", Case.Insensitive);
        html.ShouldNotContain("onerror", Case.Insensitive);
        html.ShouldNotContain("onclick", Case.Insensitive);
        html.ShouldNotContain("<img", Case.Insensitive);
        html.ShouldNotContain("<iframe", Case.Insensitive);
        html.ShouldNotContain("<style", Case.Insensitive);
        html.ShouldNotContain("data:text/html", Case.Insensitive);
    }

    [Fact]
    public void Raw_html_is_shown_as_text()
    {
        var html = Render("<b>bold?</b>");
        html.ShouldContain("&lt;b&gt;bold?&lt;/b&gt;");
    }

    [Fact]
    public void Empty_and_whitespace_input_render_to_nothing_visible() =>
        Render("   \n  ").Trim().ShouldBeEmpty();
}
```

Also add to `HtmlSanitizerTests` (existing): `[InlineData("<h6>x</h6>")]`, `[InlineData("<hr>")]`, `[InlineData("<del>x</del>")]` as kept tags. Check how the existing test class is laid out and add one "allowed tags survive" theory. The existing XSS corpus must stay green.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter "MarkdownRendererTests|HtmlSanitizerTests"`
Expected: a build failure.

- [ ] **Step 3: Implement**

Add the package reference, the interface, the renderer and its registration, and the five new allowlist tags. The plan header pins this as Review Focus item 2: if any attack theory row fails, fix the pipeline or the allowlist, never the test.

- [ ] **Step 4: Run the tests and the package check**

Run: the filtered tests, then `pwsh -File scripts/Check-PackageVersions.ps1`, then `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`.
Expected: PASS.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(content): Markdig renderer behind IMarkdownRenderer with sanitized output" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 4: Repository and store reads for ticket operations

**Files:**
- Modify: `src/TechStrap.Application/Tickets/TicketQuery.cs`. Extend `TicketSummary` and add `TicketViewCounts` and `TicketState`.
- Modify: `src/TechStrap.Application/Persistence/ITicketRepository.cs`. Add four methods.
- Modify: `src/TechStrap.Application/Persistence/IKbRepository.cs`. Add one method.
- Modify: `src/TechStrap.Application/Attachments/IAttachmentStore.cs`. Add `OpenReadAsync`.
- Modify: `src/TechStrap.Infrastructure/Persistence/Repositories/TicketRepository.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.cs`
- Modify: `src/TechStrap.Infrastructure/Attachments/AttachmentStore.cs`
- Modify: every existing `new TicketSummary(...)` call site, found with `grep -rn "new TicketSummary(" src tests`.
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/TicketOperationReadTests.cs`
- Modify: `tests/TechStrap.Infrastructure.IntegrationTests/AttachmentStoreTests.cs`. Add the `OpenReadAsync` cases.

**Interfaces:**
- Produces:

```csharp
namespace TechStrap.Application.Tickets;

// TicketSummary gains RequesterName and TagIds (tag chips on queue rows, UX brief):
public sealed record TicketSummary(Guid Id, string Number, string Subject, TicketStatus Status, TicketPriority Priority,
    Guid ProductId, Guid RequesterId, string RequesterEmail, string? RequesterName, Guid? AssigneeId, bool IsSpam,
    IReadOnlyList<Guid> TagIds, DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt);

public sealed record TicketViewCounts(int Unassigned, int Mine, int Open, int Pending, int All, int Spam);

/// <summary>A fresh, untracked read of the ticket's mutable state and concurrency token, taken after a commit.</summary>
public sealed record TicketState(Guid Id, string Number, TicketStatus Status, TicketPriority Priority, Guid ProductId,
    Guid? AssigneeId, bool IsSpam, IReadOnlyList<Guid> TagIds, DateTimeOffset LastActivityAt, uint Version);
```

```csharp
// ITicketRepository additions
/// <summary>Per-view counts for the queue tabs; view membership exactly as ListAsync (Mine uses agentId).</summary>
Task<TicketViewCounts> CountViewsAsync(Guid agentId, CancellationToken cancellationToken);
/// <summary>An attachment by its own id (agent download), or null.</summary>
Task<Attachment?> GetAttachmentByIdAsync(Guid attachmentId, CancellationToken cancellationToken);
/// <summary>A message by its own id (the Worker renders reply emails from it at send time, D-033), or null.</summary>
Task<Message?> GetMessageAsync(Guid messageId, CancellationToken cancellationToken);
/// <summary>An untracked read straight from the database; use after a commit to return the new Version.</summary>
Task<TicketState?> GetStateAsync(Guid id, CancellationToken cancellationToken);

// IKbRepository addition
/// <summary>Every ticket-to-article link of a ticket with its message id (per-message linked articles).</summary>
Task<IReadOnlyList<TicketArticle>> ListTicketArticlesAsync(Guid ticketId, CancellationToken cancellationToken);

// IAttachmentStore addition
/// <summary>Opens a stored file for reading, or null when the object is missing. The caller disposes the stream.</summary>
Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
```

**Implementation notes:**
- **`ListAsync`.** Fill in `RequesterName` from the requester join it already does, and `TagIds` from `ticket_tags`. Both the plain and the search projections build `TicketSummary` (around lines 112 and 125); change both.
  - Load the tag ids for the page in one extra query: `WHERE ticket_id = ANY(@pageIds)`.
  - Do not add a per-row query.
- **`CountViewsAsync`.**
  - Reuse the existing `ViewQuery` predicates so the counts can never drift from the lists.
  - Make one `CountAsync` per view (six small indexed counts).
  - Keep the method simple; do not build a single grouped SQL statement.
- **`GetStateAsync`.**
  - Use `AsNoTracking()`, plus the tag ids.
  - It must read the committed row, not the identity map. The test below proves the `Version` changes after a commit in the same scope's context.
- **`AttachmentStore.OpenReadAsync`.**
  - Call `IStorageProvider.ReadAsync(key, ct)`. When it returns null, return null.
  - Otherwise return a stream that disposes the `StorageReadResult` when it is disposed. Write a small `internal sealed class OwnedReadStream(Stream inner, IAsyncDisposable owner) : Stream` that delegates reads and disposes both.
  - Check the exact `ReadAsync` and `StorageReadResult` member names in the SyntaxCircus.Storage 0.2.1 package. Look at the restored package's XML docs or decompile it, and record what you found in your report.
  - An empty or whitespace key returns null.

- [ ] **Step 1: Write the failing tests**

`TicketOperationReadTests` is a `PostgresIntegrationTestBase` that uses `PersistenceTestHost`. Look at `TicketQueueTests` and `TicketScenario` for seeding helpers and reuse them.

```csharp
[Fact] public async Task Summaries_carry_the_requester_name_and_the_ticket_tags()
// seed a requester "Ann Lee", a ticket with two tags; ListAsync(All) row has RequesterName "Ann Lee" and both TagIds.

[Fact] public async Task View_counts_match_the_lists_for_every_view()
// seed ~12 tickets across statuses, assignees (agent A, agent B, none) and one spam;
// for each TicketView, CountViewsAsync(agentA)[view] == ListAsync(new TicketQuery(view, AgentId: agentA, PageSize: 100)).TotalCount.

[Fact] public async Task An_attachment_and_a_message_can_be_read_by_their_own_ids()
// seed a ticket with a message carrying one attachment; GetAttachmentByIdAsync / GetMessageAsync return them; unknown ids return null.

[Fact] public async Task Ticket_state_after_a_commit_has_the_new_version()
// in one host scope: load ticket, ChangePriority, Update, commit; then GetStateAsync(id) in the same scope:
// Version != the version loaded before the change, Priority == High.

[Fact] public async Task Ticket_article_links_are_listed_per_message()
// seed a published KB article and two messages, link the article to the second message only;
// ListTicketArticlesAsync returns exactly one TicketArticle with that MessageId.
```

Add these to `AttachmentStoreTests`:
- `A_stored_file_can_be_opened_and_read_back`: save a PNG, open it, and assert the bytes are equal.
- `Opening_a_missing_key_returns_null`.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter "TicketOperationReadTests|AttachmentStoreTests"`
Expected: a build failure.

- [ ] **Step 3: Implement**

Add the members, the implementations, and the `TicketSummary` call-site fixes. Run `TicketQueueTests` and `TicketQueryPlanTests` too. The extra tag query must not change the plans they pin.

- [ ] **Step 4: Run all affected tests**

Run:
- `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release`
- `dotnet test --project tests/TechStrap.Application.Tests -c Release`
- `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`

Expected: PASS. Then `dotnet ef migrations has-pending-model-changes ...` must be clean, because there is no schema change.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(persistence): ticket reads for counts, state, messages, attachments and article links" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 5: Email kinds for replies, Solved and assignment, and drain dispatch

**Files:**
- Modify: `src/TechStrap.Application/Email/EmailTemplates.cs`. Add the kinds and payload records.
- Modify: `src/TechStrap.Application/Email/IEmailTemplateRenderer.cs`. Add three methods.
- Modify: `src/TechStrap.Infrastructure/Email/EmailTemplateRenderer.cs`. Add three templates and the HTML-to-text helper.
- Modify: `src/TechStrap.Application/Email/DrainEmailOutboxHandler.cs`. Dispatch by kind, and add `ITicketRepository`.
- Modify: `tests/TechStrap.Infrastructure.IntegrationTests/EmailTemplateRendererTests.cs`
- Modify: `tests/TechStrap.Application.Tests/Email/DrainEmailOutboxHandlerTests.cs`

**Interfaces:**
- Consumes: `ITicketRepository.GetMessageAsync` (Task 4), the PHASE-05 renderer pieces, `ProductAccent.TryDerive`, and `EmailBrandingOptions`.
- Produces:

```csharp
namespace TechStrap.Application.Email;

public static class EmailTemplates
{
    public const string TicketConfirmation = "ticket-confirmation";
    public const string AgentReply = "agent-reply";
    public const string TicketSolved = "ticket-solved";
    public const string TicketAssigned = "ticket-assigned";
}

/// <summary>Payload of an agent-reply row: no body (D-033, 16 000 cap); the Worker loads the message by id at send time.</summary>
public sealed record AgentReplyEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink,
    string AgentPublicName, Guid MessageId, bool Solved);

public sealed record TicketSolvedEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink, int ReopenDays);

/// <summary>Internal alert to an agent; AdminLink is null when TECHSTRAP_ADMIN_PUBLIC_URL is not set.</summary>
public sealed record TicketAssignedEmail(string TicketNumber, string Subject, string ProductName, string? AssignedByName, string? AdminLink);

public interface IEmailTemplateRenderer
{
    RenderedEmail RenderTicketConfirmation(TicketConfirmationEmail model, EmailBranding branding);
    /// <param name="messageHtml">The stored, already-sanitized message body.</param>
    RenderedEmail RenderAgentReply(AgentReplyEmail model, string messageHtml, EmailBranding branding);
    RenderedEmail RenderTicketSolved(TicketSolvedEmail model, EmailBranding branding);
    RenderedEmail RenderTicketAssigned(TicketAssignedEmail model, EmailBranding branding);
}
```

**Templates.** Each follows the confirmation template's layout, encoding, accent and Powered-by rules.

- **Agent reply.**
  - Subject: `[{TicketNumber}] Re: {Subject}`.
  - HTML: greeting, then `{AgentPublicName} replied:`, then the message HTML inserted as-is (already sanitized; never re-encoded), then the button "View your request" linking `PortalLink`.
  - When `Solved`, add the line "We've marked this request as solved. Reply within {TicketNotices.ReopenDays} days if you need anything else." `TicketNotices` is defined in Task 7; until then use the literal 7 behind a renderer constant, and Task 7 points the renderer at `TicketNotices`.
  - Text part: the same content, with the message body converted by `HtmlText.ToPlainText(messageHtml)`. That helper is an internal static in Infrastructure/Email, built on AngleSharp. AngleSharp is already a transitive dependency via HtmlSanitizer, so add no new package.
    - Block elements become line breaks.
    - Links become `text (url)`.
    - Entities are decoded.
- **Solved.**
  - Subject: `[{TicketNumber}] Solved: {Subject}`.
  - Body: "We've marked your request as solved. If you need anything else, reply within {ReopenDays} days, or use the link below." plus the button.
- **Assigned.**
  - Subject: `[{TicketNumber}] Assigned to you: {Subject}`.
  - Body: the product name and "Assigned by {AssignedByName}" when it is set.
  - When `AdminLink` is set, a button "Open in TechStrap", linking only to that https or http URL. Otherwise the line "Open TechStrap to work on it."
  - This email goes to an agent, so the Powered-by line is omitted. Branding still uses the ticket's product.
- **From and Reply-To** follow the same rules as the confirmation template.

**Drain dispatch.** Replace the single-kind guard in `SendOneAsync` with a `switch (item.Kind)`. Each case deserializes its own payload type with `JsonSerializerDefaults.Web`, validates the required fields (a blank `TicketNumber` or `PortalLink` gives `payload-invalid`), and calls its renderer method.

The `AgentReply` case also needs the message:
1. `var message = await tickets.GetMessageAsync(model.MessageId, ct)`.
2. If the message is null, or `message.Visibility != MessageVisibility.Public`, return a new `DrainFailures.MessageMissing = "message-missing"`.
3. Otherwise render with `message.Body`.

Unknown kinds keep `unknown-kind`. Add `ITicketRepository tickets` to the constructor; the Worker registers persistence already.

- [ ] **Step 1: Write the failing tests**

`EmailTemplateRendererTests` gets these tests, using the existing fixture style:

```csharp
[Fact] public void An_agent_reply_shows_the_public_name_the_body_and_the_link()
// RenderAgentReply(new("ORB-42","Printer jam","Ann","https://help.test/t/abc","Sam from Orbitly Support", id, false),
//   "<p>Try <strong>this</strong></p>", branding):
// Subject "[ORB-42] Re: Printer jam"; Html contains "Sam from Orbitly Support", "<strong>this</strong>", "https://help.test/t/abc";
// Text contains "Try this" and the link; no "solved" sentence.

[Fact] public void A_send_and_solve_reply_says_it_is_solved()

[Fact] public void The_reply_body_is_inserted_as_given_but_model_fields_are_encoded()
// RequesterName "<b>Ann</b>" is encoded; the message html is inserted verbatim.

[Fact] public void A_solved_notice_has_the_reopen_window_and_link()

[Fact] public void An_assignment_alert_links_into_admin_only_when_configured()
// AdminLink "https://admin.test/tickets/ORB-42" appears; with null AdminLink there is no <a> button and the fallback line appears;
// no "Powered by" line in either.

[Fact] public void Html_to_text_keeps_paragraphs_lists_and_link_targets()
// HtmlText.ToPlainText("<p>a</p><ul><li>b</li></ul><p><a href=\"https://x.test\">x</a> &amp; y</p>")
// contains "a", "b", "x (https://x.test)", "& y" on separate lines.
```

`DrainEmailOutboxHandlerTests` gets these tests:

```csharp
[Fact] public async Task An_agent_reply_row_renders_with_the_message_loaded_at_send_time()
// tickets.GetMessageAsync(id) returns a public agent message; renderer.RenderAgentReply receives its Body; MarkSent.

[Fact] public async Task An_agent_reply_whose_message_is_gone_or_internal_fails_as_message_missing()
// returns null, then an internal message: MarkFailedAsync(..., "message-missing", ...), sender not called.

[Theory] [InlineData("ticket-solved")] [InlineData("ticket-assigned")]
public async Task Solved_and_assignment_rows_render_and_send(string kind)

[Fact] public async Task Each_kind_with_a_payload_missing_required_fields_fails_as_payload_invalid()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run:
- `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter EmailTemplateRendererTests`
- `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter DrainEmailOutboxHandlerTests`

Expected: a build failure.

- [ ] **Step 3: Implement**

Add the kinds, records, renderer methods, `HtmlText`, and the drain dispatch. Keep `ticket-confirmation` behavior byte-for-byte. Its existing tests must stay green unchanged.

- [ ] **Step 4: Run the tests**

Run:
- the two filters;
- `EmailDrainIntegrationTests` (Mailpit), which must stay green;
- `EmailServiceRegistrationTests`. The drain handler now resolves `ITicketRepository`, which `AddTechStrapPersistence` provides.
- Architecture tests.

Expected: PASS.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(email): reply, solved and assignment emails rendered at send time" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 6: Ticket handler foundations

**Files:**
- Create: `src/TechStrap.Application/Tickets/TicketErrors.cs`
- Create: `src/TechStrap.Application/Tickets/TicketNameParser.cs`
- Create: `src/TechStrap.Application/Tickets/TicketDtoMapper.cs`
- Create: `src/TechStrap.Application/Tickets/TicketMutation.cs`
- Create: `tests/TechStrap.Application.Tests/Support/TicketBuilder.cs`
- Create: `tests/TechStrap.Application.Tests/Tickets/TicketMutationTests.cs`
- Create: `tests/TechStrap.Application.Tests/Tickets/TicketNameParserTests.cs`

**Interfaces:**
- Consumes: Task 2's Contracts and Task 4's `TicketState` and `GetStateAsync`. From PHASE-04: `CurrentAgent.RequireActiveAsync`, `AgentErrors`, `ProductErrors.NotFound()` and `TagErrors.NotFound()`; check their exact names with a grep.
- Produces (all `internal`; Application.Tests has `InternalsVisibleTo`):

```csharp
namespace TechStrap.Application.Tickets;

internal static class TicketErrors
{
    public static ResultError NotFound() => new("ticket-not-found", "That ticket does not exist.", ResultErrorKind.NotFound);
    public static ResultError RowVersionRequired() =>
        new("row-version-required", "Send the ticket's rowVersion with this change.", ResultErrorKind.Validation, "rowVersion");
    public static ResultError Stale() =>
        new(PersistenceErrorCodes.ConcurrencyConflict, "Someone else changed this ticket. Reload it and try again.", ResultErrorKind.Conflict);
    public static ResultError Invalid(string target, string code, string message) => new(code, message, ResultErrorKind.Validation, target);
    public static ResultError AgentNotFound() => new("agent-not-found", "That agent does not exist.", ResultErrorKind.NotFound);
    public static ResultError AttachmentNotFound() => new("attachment-not-found", "That attachment does not exist.", ResultErrorKind.NotFound);
}

internal static class TicketNameParser
{
    public static bool TryStatus(string? value, out TicketStatus status);      // case-insensitive, must be a defined name, digits rejected
    public static bool TryPriority(string? value, out TicketPriority priority);
    public static bool TryView(string? value, out TicketView view);            // null/blank -> TicketViews.Default
}

internal static class TicketDtoMapper
{
    public static TicketStateDto ToDto(this TicketState state);
    public static string ToWire(this TicketStatus status);   // Enum name; same for priority, event type, author type, visibility, channel
    public static AttachmentDto ToDto(this Attachment attachment);
}

internal static class TicketMutation
{
    /// <summary>Resolves the active agent and loads the ticket, enforcing the client's RowVersion (D-036).</summary>
    public static async Task<Result<(Agent Agent, Ticket Ticket)>> LoadAsync(
        Guid ticketId, uint? rowVersion, bool rowVersionRequired,
        ICurrentAgentClaims currentAgent, IAgentRepository agents, ITicketRepository tickets, CancellationToken cancellationToken);

    /// <summary>Commits the scope and returns the fresh state (new RowVersion).</summary>
    public static async Task<Result<TicketStateDto>> CommitAsync(
        IUnitOfWorkScope scope, Guid ticketId, ITicketRepository tickets, CancellationToken cancellationToken);
}
```

**`LoadAsync` order:**
1. If `rowVersionRequired` and `rowVersion` is null, return `RowVersionRequired`. Do this before any database work.
2. Call `CurrentAgent.RequireActiveAsync`, returning its failure if there is one.
3. Load the ticket with `tickets.GetByIdAsync`. If it is null, return `NotFound`.
4. If `rowVersion` is set and differs from `ticket.Version`, return `Stale`.
5. Otherwise return Success.

**`CommitAsync`:**
1. `var committed = await scope.CommitAsync(ct)`. On failure, return its first error (a `concurrency-conflict` from a race arrives here unchanged).
2. Call `GetStateAsync`. If it is null, return `NotFound`, because the ticket was deleted in between.
3. Return `state.ToDto()`.

`TicketBuilder` (tests) creates tickets in a given status through Domain calls, for example:

```csharp
internal static class TicketBuilder
{
    public static Ticket New(TimeProvider clock, Guid? productId = null, Guid? requesterId = null, int sequence = 42);
    public static Ticket InStatus(TicketStatus status, TimeProvider clock); // walks New->Open->Pending->Solved->Closed via ChangeStatus as needed, then AcceptChanges()
}
```

Set the `Version` of a built ticket with `Ticket.Restore` if needed. Read `Ticket.Restore`'s 21-parameter signature. Add `TicketBuilder.WithVersion(Ticket ticket, uint version)`, which restores a copy with that version, so the stale-version tests can use it.

- [ ] **Step 1: Write the failing tests**

`TicketNameParserTests`:
- statuses parse in any case;
- `"5"`, `"pending "` (after trim, accepted) and `"Closedx"` behave as their cases say;
- a null view gives `All`;
- an unknown view returns false.

`TicketMutationTests` (substitutes):

```csharp
[Fact] public async Task A_required_row_version_that_is_missing_is_a_field_error_before_any_lookup()
// tickets.GetByIdAsync never called.
[Fact] public async Task An_unknown_ticket_is_not_found()
[Fact] public async Task A_stale_row_version_is_a_concurrency_conflict()
[Fact] public async Task A_matching_or_omitted_optional_row_version_loads_the_ticket()
[Fact] public async Task An_unprovisioned_or_inactive_agent_is_refused()
[Fact] public async Task Commit_returns_the_fresh_state_and_passes_commit_conflicts_through()
// UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("concurrency-conflict")) -> Conflict; success -> GetStateAsync result mapped.
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter "TicketMutationTests|TicketNameParserTests"`
Expected: a build failure.

- [ ] **Step 3: Implement**

Implement exactly as specified.

- [ ] **Step 4: Run the tests**

Run: the filters, then the architecture tests.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(tickets): shared ticket mutation, errors, name parsing and DTO mapping" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 7: Ticket notification planner

**Files:**
- Create: `src/TechStrap.Application/Tickets/Notifications/ITicketNotificationPlanner.cs`
- Create: `src/TechStrap.Application/Tickets/Notifications/AdminLinkOptions.cs`
- Create: `src/TechStrap.Application/Tickets/Notifications/TicketNotices.cs`
- Create: `src/TechStrap.Infrastructure/Tickets/TicketNotificationPlanner.cs`
- Create: `src/TechStrap.Infrastructure/Tickets/TicketOperationsServiceCollectionExtensions.cs`
- Modify: `src/TechStrap.Infrastructure/Email/EmailTemplateRenderer.cs`. Point the reopen window at `TicketNotices.ReopenDays`.
- Modify: `src/TechStrap.Api/Program.cs`. Call `AddTechStrapTicketOperations(builder.Configuration)` after `AddTechStrapIntake`.
- Modify: `src/TechStrap.Api/.env.example` and `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`. Add `TECHSTRAP_ADMIN_PUBLIC_URL`.
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/TicketNotificationPlannerTests.cs`

**Interfaces:**
- Consumes:
  - From Task 5: the payload records and kinds.
  - From PHASE-05: `IAccessTokenService.Issue`, `ITicketRepository.AddAccessToken`, `PortalLinkOptions.TicketLink`, `EmailOutboxItem.Enqueue` and `IEmailOutbox.Enqueue`.
  - `AgentPublicIdentity.Resolve`.
- Produces:

```csharp
namespace TechStrap.Application.Tickets.Notifications;

/// <summary>
/// Decides who is emailed about an agent action and stages outbox rows (template data only, D-033) in the caller's
/// unit of work. Never throws for an email problem: a skipped or failed notice is logged by code and the action stands.
/// </summary>
public interface ITicketNotificationPlanner
{
    Task PlanAgentReplyAsync(Ticket ticket, Message message, Agent author, bool solved, CancellationToken cancellationToken);
    Task PlanSolvedAsync(Ticket ticket, CancellationToken cancellationToken);
    Task PlanAssignedAsync(Ticket ticket, Agent assignee, Agent actor, CancellationToken cancellationToken);
}

public static class TicketNotices
{
    /// <summary>Days a Solved ticket stays open to a reply before auto-close (D-008 default; 06b binds the option).</summary>
    public const int ReopenDays = 7;
}

public sealed class AdminLinkOptions
{
    public const string PublicUrlKey = "TECHSTRAP_ADMIN_PUBLIC_URL";
    public string? PublicUrl { get; set; }
    public string? TicketLink(string ticketNumber) =>
        string.IsNullOrWhiteSpace(PublicUrl) ? null : $"{PublicUrl.TrimEnd('/')}/tickets/{Uri.EscapeDataString(ticketNumber)}";
}
```

**Planner rules.** The implementation is `internal sealed class TicketNotificationPlanner` in Infrastructure. Its constructor takes `IRequesterRepository`, `IProductRepository`, `ITicketRepository`, `IAccessTokenService`, `IEmailOutbox`, `IOptions<PortalLinkOptions>`, `IOptions<AdminLinkOptions>`, `TimeProvider` and `ILogger<TicketNotificationPlanner>`.

- **Reply and Solved (customer).**
  - Load the requester. If it is missing or `IsErased`, log at Information "Skipped {Kind} for ticket {TicketId}: requester unavailable" and return.
  - Load the product. If it is missing, log and return.
  - Issue a token. On failure, log the code and return. On success, call `tickets.AddAccessToken(issued.Value.Token)`, then `link = portal.TicketLink(issued.Value.PlaintextToken)`.
  - For a reply, the public name is `AgentPublicIdentity.Resolve(author, product.Branding.DisplayName)`.
  - Serialize the payload with `JsonSerializerDefaults.Web` and call `EmailOutboxItem.Enqueue(kind, requester.Email, json, ticket.ProductId, ticket.Id, clock)`. The ticket's **current** product goes on the row.
  - If enqueueing fails, log the code and return. On success, call `outbox.Enqueue(item.Value)`.
  - Never log the payload or the link.
- **Assigned (agent).**
  - If `assignee.Id == actor.Id` (self-assignment) or `!assignee.IsActive`, skip.
  - Send to `assignee.Email`.
  - The payload is `TicketAssignedEmail(number, subject, product.Name, actor.Name, adminLinks.TicketLink(number))`.
  - No token is issued.

**Registration** (`AddTechStrapTicketOperations(this IServiceCollection, IConfiguration)`):

```csharp
services.AddOptions<AdminLinkOptions>()
    .Configure(options => options.PublicUrl = configuration[AdminLinkOptions.PublicUrlKey]?.Trim())
    .Validate(options => string.IsNullOrWhiteSpace(options.PublicUrl)
        || (Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)),
        $"{AdminLinkOptions.PublicUrlKey} must be an absolute http or https URL when set.")
    .ValidateOnStart();
services.TryAddScoped<ITicketNotificationPlanner, TicketNotificationPlanner>();
return services;
```

Add this to `.env.example`, after `TECHSTRAP_PORTAL_PUBLIC_URL`:

```
# Optional: Admin app base URL; when set, assignment emails link to {url}/tickets/{number}.
# TECHSTRAP_ADMIN_PUBLIC_URL=http://localhost:8081
```

Add the key to the Api row of `EnvExampleCompletenessTests.Hosts()`.

- [ ] **Step 1: Write the failing tests**

`TicketNotificationPlannerTests` is a `PostgresIntegrationTestBase` that uses the real outbox, tokens and repositories through `PersistenceTestHost`. Register `AddTechStrapIntake` (portal URL `https://help.test`), `AddTechStrapTicketOperations` (admin URL `https://admin.test`) and logging.

```csharp
[Fact] public async Task A_reply_queues_one_branded_email_to_the_requester_with_a_fresh_link_and_the_public_name()
// commit (inside host.CommitAsync): planner.PlanAgentReplyAsync(ticket, message, agent "Sam Taylor", solved:false).
// One email_outbox row: kind "agent-reply", to_address the requester, product_id the ticket's product,
// payload has portalLink starting "https://help.test/t/", agentPublicName "Sam from Orbitly Support", messageId; no agent email anywhere in the payload;
// one new ticket_access_tokens row.

[Fact] public async Task The_outbox_row_and_token_roll_back_with_the_caller()
// plan inside a scope that is disposed without commit -> 0 outbox rows, 0 new tokens.

[Fact] public async Task An_erased_or_missing_requester_gets_no_email_and_no_token()

[Fact] public async Task A_solved_notice_carries_the_reopen_window()

[Fact] public async Task Assignment_emails_the_assignee_with_the_admin_link_but_never_on_self_assignment_or_to_an_inactive_agent()

[Fact] public async Task After_a_product_move_the_row_carries_the_new_product()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter TicketNotificationPlannerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Implement exactly as specified.

- [ ] **Step 4: Run the tests**

Run:
- the filter;
- `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "EnvExampleCompletenessTests|HostHealthSmokeTests"` (the Api must still boot with the new options);
- architecture tests.

Expected: PASS.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(tickets): notification planner for replies, solved notices and assignment alerts" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 8: List, search and count the queue

**Files:**
- Create: `src/TechStrap.Application/Tickets/ListTicketsRequestHandler.cs`
- Create: `src/TechStrap.Application/Tickets/CountTicketViewsRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/TicketsController.cs`. It holds the `List` and `Counts` actions; later tasks add the rest.
- Modify: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`. Add the rows `TicketsController.List=200` and `TicketsController.Counts=200`.
- Create: `tests/TechStrap.Application.Tests/Tickets/ListTicketsRequestHandlerTests.cs`
- Create: `tests/TechStrap.Application.Tests/Tickets/CountTicketViewsRequestHandlerTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/ListTicketsIntegrationTests.cs`
- Create: `tests/TechStrap.Api.Tests/Tickets/TicketListEndpointTests.cs`
- Create: `tests/TechStrap.Api.Tests/Tickets/TicketTestData.cs`. Seeding helpers for the ticket endpoint tests; later tasks reuse them.

**Interfaces:**
- Consumes: Task 2 DTOs and requests, Task 4 `TicketSummary` and `CountViewsAsync`, Task 6 `TicketNameParser` and `TicketDtoMapper`, `CurrentAgent.RequireActiveAsync`, `Paging`.
- Produces:

```csharp
public interface IListTicketsRequestHandler
{
    Task<Result<PagedResponse<TicketSummaryDto>>> HandleAsync(ListTicketsRequest request, CancellationToken cancellationToken);
}
public interface ICountTicketViewsRequestHandler
{
    Task<Result<TicketViewCountsResponse>> HandleAsync(CancellationToken cancellationToken);
}
```

The `ListTicketsRequestHandler` constructor takes `ICurrentAgentClaims`, `IAgentRepository`, `ITicketRepository`, `IProductRepository` and `ITagRepository`.

**List rules:**
1. Parse the view with `TicketNameParser.TryView`. On failure return 400, target `view`, code `view-invalid`.
2. Status and priority are optional. A value that does not parse is a 400 with target `status` or `priority` and the code `status-invalid` or `priority-invalid`.
3. Check paging:
   - `page < 1` is a 400 with target `page` and code `page-invalid`.
   - A `pageSize` that is non-zero and outside `1..Paging.MaxPageSize` is a 400 with target `pageSize` and code `page-size-invalid`.
   - A `pageSize` of 0 means `Paging.DefaultPageSize`.
4. Call `CurrentAgent.RequireActiveAsync`. `Mine` needs the agent id, and every list is agent-only.
5. Build a `TicketQuery` with `AgentId` set to the agent's id. A search string longer than `DomainLimits.SearchTextMaxLength` is cut to that length, not rejected.
6. Enrich the page with batch lookups, never one per row:
   - products: `ListAsync(activeOnly: false)` into a dictionary;
   - assignees: `agents.GetByIdsAsync(distinct assignee ids)`;
   - tags: `ListAsync` into a dictionary.

   Unknown ids map to an empty name and no tag chip.
7. Return a `PagedResponse<TicketSummaryDto>` built from `PagedResult`.

**Counts rules:** call `RequireActiveAsync`, then `CountViewsAsync(agent.Id)`, and map the result to `TicketViewCountsResponse`.

**Controller:**

```csharp
[ApiController]
[Route("api/tickets")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class TicketsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? view, [FromQuery] Guid? productId, [FromQuery] string? status, [FromQuery] string? priority,
        [FromQuery] Guid? assigneeId, [FromQuery] Guid? tagId, [FromQuery] Guid? requesterId, [FromQuery] string? search,
        [FromServices] IListTicketsRequestHandler handler, CancellationToken cancellationToken,
        [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await handler.HandleAsync(new ListTicketsRequest(view, productId, status, priority, assigneeId, tagId, requesterId, search, page, pageSize), cancellationToken))
            .ToActionResult(this, Ok);

    [HttpGet("counts")]
    public async Task<IActionResult> Counts([FromServices] ICountTicketViewsRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(cancellationToken)).ToActionResult(this, Ok);
}
```

The `Paging` reference must come from Contracts or Api, because `Paging` lives in Application.Persistence. Controllers may reference Application, so import it. Check how `AgentsController` handles its default page size, and copy that.

**`TicketTestData`** (Api.Tests). It seeds through the real repositories in a `factory.Services` scope, following the same pattern as `IntakeTestData`. It provides:

```csharp
internal sealed record TicketSeed(Product Orbitly, Product Paperplane, Requester Ann, Agent Sam, Agent Kim, Tag Billing, IReadOnlyList<Ticket> Tickets);
internal static class TicketTestData
{
    // Signs Sam (agent) and Kim (agent) in via GET /api/agents/me first so their agent rows exist, then seeds tickets.
    public static async Task<TicketSeed> SeedAsync(ApiFactory factory, CancellationToken cancellationToken);
    public static HttpClient AgentClient(ApiFactory factory, string subject);   // "sam" or "kim", TestJwt agent group
}
```

- [ ] **Step 1: Write the failing tests**

`ListTicketsRequestHandlerTests` (substitutes):

```csharp
[Theory]
[InlineData("Unassigned", TicketView.Unassigned)] [InlineData("mine", TicketView.Mine)] [InlineData("SPAM", TicketView.Spam)]
[InlineData(null, TicketView.All)]
public async Task The_view_is_parsed_case_insensitively_and_defaults_to_all(string? view, TicketView expected)
// asserts tickets.ListAsync received a TicketQuery with View == expected and AgentId == the signed-in agent id.

[Theory] [InlineData("Later")] [InlineData("3")]
public async Task An_unknown_view_is_a_field_error(string view)

[Fact] public async Task Unknown_status_or_priority_filters_are_field_errors()
[Fact] public async Task Out_of_range_paging_is_a_field_error_and_zero_page_size_means_the_default()
[Fact] public async Task Rows_are_enriched_with_product_assignee_and_tag_names_in_three_batch_lookups()
// asserts products.ListAsync, agents.GetByIdsAsync, tags.ListAsync each received exactly once for a 3-row page.
[Fact] public async Task An_overlong_search_is_cut_to_the_search_limit()
[Fact] public async Task An_unprovisioned_agent_is_refused()
```

`CountTicketViewsRequestHandlerTests` covers the mapping and the refused agent.

`ListTicketsIntegrationTests` is a `PostgresIntegrationTestBase`. It seeds with `TicketBulkSeed`, which exists; look at its API. Seed 500 tickets across statuses, products, assignees and tags, with about 10 spam tickets. The handler runs on the real repository; build it directly with real repositories from the host, plus a stub `ICurrentAgentClaims`.

```csharp
[Fact] public async Task Each_view_holds_exactly_its_tickets_and_spam_only_appears_in_the_spam_view()
[Fact] public async Task Search_matches_subject_body_and_exact_number()
[Fact] public async Task Paging_is_stable_and_complete()
// walk all pages of All with pageSize 37: the ids are distinct and their count == TotalCount.
[Fact] public async Task Hostile_and_oversized_search_text_is_safe_and_number_search_is_exact()
// search = new string('a', 10_000), "'; drop table tickets; --", "&|!:*()<>", "ORB-42":
// none throws, each returns a Success; the last returns ORB-42 first.
```

The GIN `EXPLAIN` check already lives in `TicketQueryPlanTests` (PHASE-03). Reference it; do not duplicate it.

`TicketListEndpointTests` (Api, real Postgres, `TicketTestData`):

```csharp
[Fact] public async Task An_agent_lists_the_unassigned_view_with_names_and_tag_chips()
[Fact] public async Task Mine_lists_only_the_callers_tickets()
[Fact] public async Task Counts_return_a_number_for_every_view()
[Fact] public async Task A_bad_view_is_400_with_the_view_field()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter "ListTicketsRequestHandlerTests|CountTicketViewsRequestHandlerTests"`
Expected: a build failure.

- [ ] **Step 3: Implement**

Implement the handlers and the controller, and add the `ControllerActions` rows.

- [ ] **Step 4: Run the tests**

Run:
- `dotnet test --project tests/TechStrap.Application.Tests -c Release`
- `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter ListTicketsIntegrationTests`
- `dotnet test --project tests/TechStrap.Api.Tests -c Release`
- the architecture tests

Expected: PASS. `AgentAccessCoverageTests`, `ResultMappingTests`, `CancellationPropagationTests` and `OpenApiSurfaceTests` pick up the two new routes automatically.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(tickets): queue list with views, filters, search and per-view counts" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 9: Ticket detail and timeline

**Files:**
- Create: `src/TechStrap.Application/Tickets/GetTicketRequestHandler.cs`
- Modify: `src/TechStrap.Api/Controllers/TicketsController.cs`. Add `Get`.
- Modify: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`. Add the row `TicketsController.Get=200`.
- Create: `tests/TechStrap.Application.Tests/Tickets/GetTicketRequestHandlerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Tickets/TicketDetailEndpointTests.cs`

**Interfaces:**
- Consumes:
  - From Task 4: `ListTicketArticlesAsync` and `GetAttachmentsAsync`.
  - From Task 6: the mapper.
  - The existing `GetByIdAsync`, `GetByNumberAsync`, `GetMessagesAsync` and `GetEventsAsync`.
- Produces:

```csharp
public interface IGetTicketRequestHandler
{
    /// <param name="reference">A ticket id (Guid) or a ticket number such as ORB-42.</param>
    Task<Result<TicketDetailDto>> HandleAsync(string reference, CancellationToken cancellationToken);
}
```

The constructor takes `ICurrentAgentClaims`, `IAgentRepository`, `ITicketRepository`, `IRequesterRepository`, `IProductRepository`, `ITagRepository` and `IKbRepository`.

**Rules:**
1. Check the caller as `ListAgentsRequestHandler` does: `currentAgent.Current is null` returns `AgentErrors.AccessRequired()`, with no database hit. Detail is a read.
2. Resolve the reference:
   - `Guid.TryParse` succeeds: call `GetByIdAsync`.
   - Otherwise `TicketNumber.TryParse` succeeds: call `GetByNumberAsync`.
   - Otherwise, or when the ticket is null: `TicketErrors.NotFound()`.
3. Load the timeline:
   - `GetMessagesAsync(id, publicOnly: false)`
   - `GetEventsAsync(id)`
   - `GetAttachmentsAsync(id, publicOnly: false)`, grouped by `MessageId`
   - `ListTicketArticlesAsync(id)`, grouped by `MessageId`, with each distinct article loaded once through `GetArticleAsync`. Missing articles are skipped.
4. Load names in one batch each:
   - `agents.GetByIdsAsync`, for the assignee, agent message authors and agent event actors;
   - the requester, through `GetByIdAsync`;
   - the product;
   - tags, through `ListAsync`.
5. Build `MessageDto`:
   - `AuthorName` is the agent's `Name` for Agent authors, `requester.Name ?? requester.Email` for Requester authors, and `null` for System.
   - `BodyHtml` is `message.Body`.
6. Build `TicketEventDto`:
   - `ActorName` is resolved the same way, with `"System"` for System actors.
   - `PayloadJson` is passed through as stored.
7. Keep both lists oldest first. Clients merge them; `MessageAdded` events stay in the list (D-036 note).
8. `RowVersion` is `ticket.Version`.
9. This DTO is agent-only. Internal notes and their events are included.

**Controller:**

```csharp
[HttpGet("{reference}")]
public async Task<IActionResult> Get(string reference, [FromServices] IGetTicketRequestHandler handler, CancellationToken cancellationToken) =>
    (await handler.HandleAsync(reference, cancellationToken)).ToActionResult(this, Ok);
```

The literal route `counts` must still win. Assert this in the endpoint test: `GET /api/tickets/counts` is answered by `Counts`.

- [ ] **Step 1: Write the failing tests**

`GetTicketRequestHandlerTests` (substitutes, `TicketBuilder`):

```csharp
[Fact] public async Task A_ticket_is_found_by_id_or_by_number_case_insensitively()
[Fact] public async Task An_unknown_or_malformed_reference_is_not_found()
[Fact] public async Task The_timeline_is_oldest_first_with_internal_notes_and_attachments_on_their_messages()
[Fact] public async Task Author_and_actor_names_resolve_for_agents_requesters_and_system()
[Fact] public async Task Linked_articles_appear_on_the_message_that_linked_them()
[Fact] public async Task Names_are_loaded_in_batches_not_per_message()
// agents.GetByIdsAsync received once for a ticket with 5 agent messages.
[Fact] public async Task An_anonymous_caller_is_refused_without_a_database_lookup()
```

`TicketDetailEndpointTests`:

```csharp
[Fact] public async Task An_agent_reads_a_ticket_by_number_and_by_id()
[Fact] public async Task The_counts_route_is_not_mistaken_for_a_ticket_reference()
[Fact] public async Task An_unknown_ticket_is_404_problem_details()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter GetTicketRequestHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Implement the handler, the controller action and the `ControllerActions` row.

- [ ] **Step 4: Run the tests**

Run Application.Tests, Api.Tests and the architecture tests.
Expected: PASS.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(tickets): ticket detail with timeline by id or number" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 10: Agent reply (Markdown, attachments, KB links, email)

**Files:**
- Create: `src/TechStrap.Application/Tickets/AddAgentReplyRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/AgentReplyForm.cs`
- Modify: `src/TechStrap.Api/Controllers/TicketsController.cs`. Add `Reply`.
- Modify: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`. Add the row `TicketsController.Reply=201`.
- Modify: `tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs`. Send an empty multipart body to routes that consume multipart.
- Create: `tests/TechStrap.Application.Tests/Tickets/AddAgentReplyRequestHandlerTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/AgentReplyIntegrationTests.cs`
- Create: `tests/TechStrap.Api.Tests/Tickets/AgentReplyEndpointTests.cs`

**Interfaces:**
- Consumes:
  - Task 3: `IMarkdownRenderer`.
  - Task 6: `TicketMutation`, `TicketErrors` and the mapper.
  - Task 7: `ITicketNotificationPlanner`.
  - PHASE-05: `IAttachmentStore.SaveAsync`, `IncomingAttachment`, `IntakeLimits`, and the internal `IntakeErrors.BodyTooLong`, `TooManyFiles` and `MessageTooLarge`. These are reused; they are in the same assembly.
  - `IKbRepository.GetArticleAsync` and `AddTicketArticle`.
- Produces:

```csharp
public interface IAddAgentReplyRequestHandler
{
    Task<Result<AgentMessageResponse>> HandleAsync(
        Guid ticketId, AddAgentReplyRequest request, IReadOnlyList<IncomingAttachment> attachments, CancellationToken cancellationToken);
}
```

The constructor takes `ICurrentAgentClaims`, `IAgentRepository`, `ITicketRepository`, `IKbRepository`, `IAttachmentStore`, `IMarkdownRenderer`, `IHtmlSanitizer`, `ITicketNotificationPlanner`, `IUnitOfWork`, `TimeProvider` and `ILogger<AddAgentReplyRequestHandler>`.

**Flow.** Implement it exactly; the tests pin each step.

1. **Shape checks.** Return the first error found, before any database work:
   - A raw body longer than `DomainLimits.MessageBodyMaxLength` fails with `IntakeErrors.BodyTooLong()`.
   - More than `IntakeLimits.MaxFiles` files fails with `TooManyFiles`.
   - A declared total over `IntakeLimits.MaxMessageBytes` fails with `MessageTooLarge`.
   - `LinkedArticleIds` are de-duplicated. More than `TicketOperationLimits.MaxLinkedArticles` fails with a 400, target `linkedArticleIds`, code `linked-articles-too-many`.
   - `StatusAfter`:
     - `null` and `"Pending"` mean no extra change;
     - `"Solved"` means solve after the reply;
     - anything else is a 400, target `statusAfter`, code `status-after-invalid`.
2. Open the unit of work: `await using var scope = await unitOfWork.BeginAsync(ct)`.
3. Call `TicketMutation.LoadAsync(ticketId, request.RowVersion, rowVersionRequired: false, ...)` and return its failure if there is one.
4. Check every linked article with `kb.GetArticleAsync(id)`. A missing article is a 400, target `linkedArticleIds`, code `article-not-found`.
5. Render the body: `html = sanitizer.Sanitize(markdown.ToHtml(request.Body ?? string.Empty))`.
6. Call `message = ticket.AddAgentReply(agent.Id, html, clock)` and convert its failure. This gives `ticket-closed` 409, and `body-required` 400 for an empty or blank body.
7. **Attachments.** Use the PHASE-05 pattern, including its compensation, with a `List<string> stored`:
   1. `saved = await attachments.SaveAsync(ticket.Id, file, ct)`. On failure, delete the stored files and return the error.
   2. Add `saved.Value.StorageKey` to `stored`.
   3. Call `message.Value.AddAttachment(...)`. On failure, delete the stored files and return the converted error.
8. If solving, call `ticket.ChangeStatus(TicketStatus.Solved, Actor.ForAgent(agent.Id), clock)`. On failure, delete the stored files and return the converted error.
9. Stage the changes:
   - `tickets.Update(ticket)`;
   - `kb.AddTicketArticle(new TicketArticle(ticket.Id, message.Value.Id, articleId))` for each article;
   - `await planner.PlanAgentReplyAsync(ticket, message.Value, agent, solved, ct)`.

   A solved reply sends one email: the reply email with `Solved = true`. There is no separate solved notice.
10. Commit with `TicketMutation.CommitAsync`.
    - On failure, delete the stored files and return the failure.
    - On success, clear `stored`, then return `new AgentMessageResponse(messageDto, state)`.
    - Build `messageDto` from `message.Value`, its attachments (`NewAttachments` mapped with `ToDto`), the linked-article DTOs, and `AuthorName = agent.Name`.
11. Wrap steps 7 to 10 in `try { ... } catch { await DeleteStoredAsync(stored); throw; }`. `DeleteStoredAsync` passes `CancellationToken.None`. It logs a warning with the storage key and exception type and swallows the exception, the same as PHASE-05.

**Form and controller.** Copy `PublicIntakeController`'s multipart handling: the attributes and the stream disposal in `finally`.

```csharp
public sealed class AgentReplyForm
{
    public string? Body { get; set; }
    public List<Guid>? LinkedArticleIds { get; set; }
    public string? StatusAfter { get; set; }
    public uint? RowVersion { get; set; }
    public List<IFormFile>? Attachments { get; set; }
}

[HttpPost("{id:guid}/replies")]
[Consumes("multipart/form-data")]
[ReadFormBeforeBinding]
[RequestSizeLimit(IntakeRequestLimits.FormBodyBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = IntakeRequestLimits.FormBodyBytes * 2)]
public async Task<IActionResult> Reply(Guid id, [FromForm] AgentReplyForm form, [FromServices] IAddAgentReplyRequestHandler handler, CancellationToken cancellationToken)
{
    // map files to IncomingAttachment (dispose streams in finally), build AddAgentReplyRequest(form.Body, form.LinkedArticleIds, form.StatusAfter, form.RowVersion)
    // success: StatusCode(201, response)
}
```

`ReadFormBeforeBindingAttribute` is `internal` in Api; that is fine, because the controller is in Api.

**Coverage harness.** `AgentAccessCoverageTests` sends JSON `{}` to every non-GET and non-DELETE route. A multipart route would answer 415 before authorization ran.
- For each route, detect `[Consumes("multipart/form-data")]` through endpoint metadata: `IAcceptsMetadata` content types contain `multipart/form-data`.
- Send `new MultipartFormDataContent()` with one empty string field to those routes instead.
- Add a test that pins this: `Multipart_routes_are_probed_with_a_multipart_body`, which asserts the reply route appears in the multipart set.

- [ ] **Step 1: Write the failing tests**

`AddAgentReplyRequestHandlerTests` (substitutes; a `markdown` substitute returning `"<p>" + input + "</p>"`; a pass-through sanitizer; `TicketBuilder`):

```csharp
[Fact] public async Task A_reply_renders_markdown_sanitises_saves_the_message_and_plans_the_email()
// asserts markdown.ToHtml("Hi **Ann**"), sanitizer.Sanitize("<p>Hi **Ann**</p>"), tickets.Update(ticket),
// planner.PlanAgentReplyAsync(ticket, message with Visibility Public, agent, false), response 201 data has the state from GetStateAsync.
[Fact] public async Task A_first_reply_on_an_open_ticket_moves_it_to_pending_and_sets_first_response_once()
[Fact] public async Task Send_and_solve_solves_after_the_reply_and_plans_one_email_marked_solved()
[Theory] [InlineData("Closed")] public async Task A_reply_on_a_closed_ticket_is_409_ticket_closed_and_nothing_is_stored(string status)
[Fact] public async Task An_empty_body_is_400_body_required()
[Fact] public async Task An_over_long_body_is_rejected_before_markdown_runs()
[Fact] public async Task Too_many_files_or_too_many_bytes_are_rejected_before_anything_is_stored()
[Fact] public async Task An_unknown_linked_article_is_400_and_nothing_is_stored()
[Fact] public async Task Linked_articles_are_deduplicated_and_recorded_against_the_reply()
[Fact] public async Task A_bad_status_after_is_a_field_error()
[Fact] public async Task A_stale_optional_row_version_is_409_and_an_omitted_one_is_accepted()
[Fact] public async Task A_rejected_attachment_removes_files_already_stored()
[Fact] public async Task A_failed_commit_deletes_the_stored_attachments_and_returns_the_conflict()
[Fact] public async Task A_reply_to_an_erased_requester_is_saved_without_an_email()
// planner substitute: assert PlanAgentReplyAsync is still called (the planner decides; see Task 7) and the result is success —
// the end-to-end "no email" is proven in AgentReplyIntegrationTests below with the real planner.
[Fact] public async Task Cancellation_reaches_the_store_the_repositories_and_the_planner()
```

`AgentReplyIntegrationTests` is a `PostgresIntegrationTestBase`. It uses the real handler, real services (`AddTechStrapIntake`, `AddTechStrapTicketOperations`, content and attachments), a temp storage root and a stub `ICurrentAgentClaims`.

```csharp
[Fact] public async Task A_reply_commits_message_attachment_event_article_link_token_and_outbox_together()
// SQL scalars: +1 public message, +1 attachment row and file, MessageAdded and StatusChanged(Open->Pending) events,
// +1 ticket_articles row, +1 ticket_access_tokens row, +1 email_outbox row kind "agent-reply" whose payload has the message id.
[Fact] public async Task A_failed_commit_leaves_no_message_rows_and_no_files()
// decorate IEmailOutbox (as in SubmitTicketIntegrationTests) to throw on Enqueue; reply with one PNG -> exception;
// assert 0 new messages, 0 attachment rows, storage root has no files; a second reply (normal outbox) succeeds.
[Fact] public async Task A_reply_to_an_erased_requester_is_saved_without_an_email()
// erase the requester via Requester.Erase + Update before replying; assert +1 message, 0 outbox rows, 0 new tokens.
```

`AgentReplyEndpointTests` (Api, `TicketTestData`):

```csharp
[Fact] public async Task An_agent_replies_with_markdown_and_a_png_and_gets_201_with_the_message_and_new_row_version()
[Fact] public async Task A_reply_with_an_executable_attachment_is_400_attachment_type_not_allowed()
[Fact] public async Task A_reply_to_a_closed_ticket_is_409()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter AddAgentReplyRequestHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Implement the handler, the form, the action, the `ControllerActions` row and the coverage harness change.

- [ ] **Step 4: Run all affected tests**

Run Application.Tests, `--filter AgentReplyIntegrationTests` (Infrastructure), Api.Tests and the architecture tests.
Expected: PASS.

`ResultMappingTests` samples `AgentReplyForm` with `GetUninitializedObject`, so all its properties are null. The action must tolerate that with `form.Attachments ?? []`.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(tickets): agent replies with markdown, attachments, KB links and customer email" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 11: Internal notes and status changes, with the concurrency integration test

**Files:**
- Create: `src/TechStrap.Application/Tickets/AddInternalNoteRequestHandler.cs`
- Create: `src/TechStrap.Application/Tickets/ChangeTicketStatusRequestHandler.cs`
- Modify: `src/TechStrap.Api/Controllers/TicketsController.cs`. Add `AddNote` and `ChangeStatus`.
- Modify: `ControllerActions.ExpectedSuccess`. Add `TicketsController.AddNote=201` and `TicketsController.ChangeStatus=200`.
- Create: `tests/TechStrap.Application.Tests/Tickets/AddInternalNoteRequestHandlerTests.cs`
- Create: `tests/TechStrap.Application.Tests/Tickets/ChangeTicketStatusRequestHandlerTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/TicketConcurrencyIntegrationTests.cs`
- Create: `tests/TechStrap.Api.Tests/Tickets/TicketStatusEndpointTests.cs`

**Interfaces:**

```csharp
public interface IAddInternalNoteRequestHandler
{
    Task<Result<AgentMessageResponse>> HandleAsync(Guid ticketId, AddInternalNoteRequest request, CancellationToken cancellationToken);
}
public interface IChangeTicketStatusRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken);
}
```

**Note rules.**
- The constructor takes `ICurrentAgentClaims`, `IAgentRepository`, `ITicketRepository`, `IMarkdownRenderer`, `IHtmlSanitizer`, `IUnitOfWork` and `TimeProvider`.
- The body is checked against the raw length limit first, using `IntakeErrors.BodyTooLong()`.
- Load with `TicketMutation.LoadAsync(rowVersionRequired: false)`.
- Render the Markdown, then sanitize it, then call `ticket.AddInternalNote(agent.Id, html, clock)`.
- Then `tickets.Update`, then `TicketMutation.CommitAsync`.
- Return a 201 `AgentMessageResponse` with `Visibility "Internal"`.
- No planner call, so no email and no token. The status is unchanged.

**Status rules.**
- The constructor takes `ICurrentAgentClaims`, `IAgentRepository`, `ITicketRepository`, `ITicketNotificationPlanner`, `IUnitOfWork` and `TimeProvider`.
- Parse the status with `TicketNameParser.TryStatus`. On failure return a 400, target `status`, code `status-invalid`.
- Load with `rowVersionRequired: true`.
- Call `ticket.ChangeStatus(to, Actor.ForAgent(agent.Id), clock)` and convert its failure. Possible Conflicts are `invalid-status-transition` and `ticket-closed`.
- If `to == Solved`, call `await planner.PlanSolvedAsync(ticket, ct)`.
- Then `Update`, then `CommitAsync`, and return a 200 `TicketStateDto`.

**Controller:**

```csharp
[HttpPost("{id:guid}/notes")]
public async Task<IActionResult> AddNote(Guid id, [FromBody] AddInternalNoteRequest request, [FromServices] IAddInternalNoteRequestHandler handler, CancellationToken cancellationToken) =>
    (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));

[HttpPut("{id:guid}/status")]
public async Task<IActionResult> ChangeStatus(Guid id, [FromBody] ChangeTicketStatusRequest request, [FromServices] IChangeTicketStatusRequestHandler handler, CancellationToken cancellationToken) =>
    (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);
```

- [ ] **Step 1: Write the failing tests**

`AddInternalNoteRequestHandlerTests`:

```csharp
[Fact] public async Task A_note_is_internal_never_emails_and_never_changes_status_or_first_response()
// planner not received; ticket.Status unchanged; FirstResponseAt null; Visibility Internal.
[Fact] public async Task A_note_on_a_closed_ticket_is_409()
[Fact] public async Task An_over_long_note_is_rejected_before_markdown_runs()
```

`ChangeTicketStatusRequestHandlerTests` covers every transition from `TicketStatusRules`:

```csharp
[Theory]
[MemberData(nameof(AllowedTransitions))]    // every (from, to) in TicketStatusRules.AllowedFrom
public async Task Every_allowed_transition_succeeds(TicketStatus from, TicketStatus to)
[Theory]
[MemberData(nameof(ForbiddenTransitions))]  // every other pair, including same-status
public async Task Every_forbidden_transition_is_409_and_nothing_is_committed(TicketStatus from, TicketStatus to)
[Fact] public async Task Solving_plans_the_solved_notice_and_other_changes_do_not()
[Fact] public async Task Solved_at_is_set_on_solve_and_cleared_on_reopen()
[Fact] public async Task A_missing_row_version_is_400_and_a_stale_one_is_409()
[Theory] [InlineData("Done")] [InlineData("")] public async Task An_unknown_status_is_a_field_error(string status)
```

`TicketConcurrencyIntegrationTests` is a `PostgresIntegrationTestBase` with the real handlers. It pins Review Focus item 1:

```csharp
[Fact] public async Task A_stale_row_version_changes_nothing_and_a_parallel_reply_still_lands()
// load the ticket's version v0. Agent A: ChangeStatus(Pending, v0) succeeds -> v1.
// Agent B: ChangeStatus(Solved, v0): 409 concurrency-conflict,
// and the database still shows Pending, no new StatusChanged event beyond A's.
// Then Agent B: AddInternalNote with no row version succeeds (+1 message).
[Fact] public async Task Two_simultaneous_status_changes_with_the_same_version_let_exactly_one_win()
// two scopes, same v0, released together through a start gate: one Success, one Conflict "concurrency-conflict".
```

`TicketStatusEndpointTests`:

```csharp
[Fact] public async Task Solving_returns_200_with_the_new_row_version_and_queues_a_solved_email()
[Fact] public async Task A_stale_row_version_is_409_problem_details_with_concurrency_conflict()
[Fact] public async Task A_note_is_201_and_queues_nothing()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter "AddInternalNoteRequestHandlerTests|ChangeTicketStatusRequestHandlerTests"`
Expected: a build failure.

- [ ] **Step 3: Implement**

Implement both handlers, the two actions and the `ControllerActions` rows.

- [ ] **Step 4: Run the tests**

Run:
- Application.Tests;
- `--filter TicketConcurrencyIntegrationTests` five times, to check stability;
- Api.Tests;
- the architecture tests.

Expected: PASS.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(tickets): internal notes and status changes with row-version concurrency" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 12: Assign, priority and product move

**Files:**
- Create these three handlers in `src/TechStrap.Application/Tickets/`:
  - `AssignTicketRequestHandler.cs`
  - `ChangeTicketPriorityRequestHandler.cs`
  - `MoveTicketProductRequestHandler.cs`
- Modify `TicketsController`: add the actions `Assign`, `ChangePriority` and `MoveProduct`.
- Modify `ControllerActions.ExpectedSuccess`: add a row for each new action, all three with status 200.
- Create one handler test class per handler in `tests/TechStrap.Application.Tests/Tickets/`.
- Create: `tests/TechStrap.Api.Tests/Tickets/TicketFieldEndpointTests.cs`

**Interfaces:**

```csharp
public interface IAssignTicketRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken);
}
public interface IChangeTicketPriorityRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, ChangeTicketPriorityRequest request, CancellationToken cancellationToken);
}
public interface IMoveTicketProductRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, MoveTicketProductRequest request, CancellationToken cancellationToken);
}
```

**Common shape.** All three handlers:
1. Load the ticket with `TicketMutation.LoadAsync(rowVersionRequired: true)`.
2. Validate the referenced row.
3. Call the Domain method with `Actor.ForAgent(agent.Id)`, converting any failure.
4. Call `tickets.Update`.
5. Commit with `TicketMutation.CommitAsync` and return the 200 `TicketStateDto`.

**Assign:**
- The constructor adds `ITicketNotificationPlanner`.
- A null `AssigneeId` means unassign.
- Otherwise:
  - `assignee = agents.GetByIdAsync(id)`.
  - If the assignee is null, return `TicketErrors.AgentNotFound()` (404).
  - If `!assignee.IsActive`, return a 400 with target `assigneeId` and code `assignee-inactive`.
- Record `var before = ticket.AssigneeId;` and call `ticket.Assign(...)`.
- Plan the alert only when `assignee is not null && before != assignee.Id`: `await planner.PlanAssignedAsync(ticket, assignee, agent, ct)`. The planner itself skips self-assignment.

**Priority:**
- Parse with `TicketNameParser.TryPriority`. On failure return a 400 with target `priority` and code `priority-invalid`.
- Then call `ticket.ChangePriority`. Setting the same priority is a no-op and still returns 200.

**Product:**
- A null `ProductId` returns a 400 with target `productId` and code `product-required`.
- Look the product up with `products.GetByIdAsync`. If it is null, return `ProductErrors.NotFound()` (404).
- If `!IsActive`, return a 400 with target `productId` and code `product-inactive`.
- Then call `ticket.MoveToProduct`. The ticket number never changes.

**Controller** (one pattern for all three):

```csharp
[HttpPut("{id:guid}/assignee")]
public async Task<IActionResult> Assign(Guid id, [FromBody] AssignTicketRequest request, [FromServices] IAssignTicketRequestHandler handler, CancellationToken cancellationToken) =>
    (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);
// ChangePriority -> [HttpPut("{id:guid}/priority")], MoveProduct -> [HttpPut("{id:guid}/product")]
```

- [ ] **Step 1: Write the failing tests**

```csharp
// AssignTicketRequestHandlerTests
[Fact] public async Task Assigning_another_agent_writes_the_event_and_plans_their_alert()
[Fact] public async Task Self_assignment_writes_the_event_and_the_planner_is_still_called_once()   // planner skips internally (Task 7 covers)
[Fact] public async Task Reassigning_the_current_assignee_is_a_no_op_with_no_alert()
[Fact] public async Task Unassigning_writes_the_event_and_plans_nothing()
[Fact] public async Task An_unknown_agent_is_404_and_an_inactive_one_is_400()
[Fact] public async Task A_closed_ticket_is_409_and_a_stale_row_version_is_409()
// ChangeTicketPriorityRequestHandlerTests
[Fact] public async Task A_new_priority_writes_PriorityChanged_and_returns_the_new_state()
[Theory] [InlineData("Critical")] [InlineData("")] public async Task An_unknown_priority_is_a_field_error(string priority)
// MoveTicketProductRequestHandlerTests
[Fact] public async Task A_move_keeps_the_number_and_writes_ProductChanged()
[Fact] public async Task An_unknown_product_is_404_an_inactive_one_is_400_and_a_missing_one_is_400()
```

`TicketFieldEndpointTests`:

```csharp
[Fact] public async Task Assign_then_priority_then_product_chain_row_versions_and_the_number_never_changes()
// each call uses the RowVersion returned by the previous one; final GET shows the new assignee, priority, product and the original number.
[Fact] public async Task Assigning_another_agent_queues_one_ticket_assigned_email_to_them()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter "AssignTicketRequestHandlerTests|ChangeTicketPriorityRequestHandlerTests|MoveTicketProductRequestHandlerTests"`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the handlers, the controller actions and the `ControllerActions` rows.

- [ ] **Step 4: Run the tests**

Run Application.Tests, Api.Tests and the architecture tests.
Expected: PASS.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(tickets): assign, priority and product move with assignment alerts" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 13: Tags and spam

**Files:**
- Create these three handlers in `src/TechStrap.Application/Tickets/`:
  - `AddTicketTagRequestHandler.cs`
  - `RemoveTicketTagRequestHandler.cs`
  - `MarkTicketSpamRequestHandler.cs`
- Modify `TicketsController`: add the actions `AddTag`, `RemoveTag` and `MarkSpam`, each returning 200.
- Modify `ControllerActions`: add a row for each new action.
- Create one handler test class per handler.
- Create: `tests/TechStrap.Api.Tests/Tickets/TicketTagAndSpamEndpointTests.cs`

**Interfaces:**

```csharp
public interface IAddTicketTagRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, AddTicketTagRequest request, CancellationToken cancellationToken);
}
public interface IRemoveTicketTagRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, Guid tagId, uint? rowVersion, CancellationToken cancellationToken);
}
public interface IMarkTicketSpamRequestHandler
{
    Task<Result<TicketStateDto>> HandleAsync(Guid ticketId, MarkTicketSpamRequest request, CancellationToken cancellationToken);
}
```

**Rules:**
- **All three** load the ticket with `TicketMutation.LoadAsync(rowVersionRequired: true)`, then `Update`, then `CommitAsync`, and return a 200 `TicketStateDto`.
- **Tags are idempotent (D-036).**
  - **Add:**
    1. A null `TagId` returns a 400 with target `tagId` and code `tag-required`.
    2. `tags.GetByIdAsync` returning null gives `TagErrors.NotFound()` (404).
    3. Then call `ticket.AddTag`. Adding a tag that is already present is Ok, with no event.
  - **Remove:**
    1. `tags.GetByIdAsync` returning null gives a 404.
    2. Then call `ticket.RemoveTag`. Removing a tag that is absent is Ok, with no event.
- **Spam:**
  - A null `IsSpam` returns a 400 with target `isSpam` and code `is-spam-required`.
  - Then call `ticket.MarkSpam(isSpam, ...)`. Repeating the current value is a no-op.
  - Spam never notifies anyone, so there is no planner.
  - A Closed ticket returns 409 `ticket-closed`. This is the Domain rule and a known limit (D-036).

**Controller:**

```csharp
[HttpPost("{id:guid}/tags")]
public async Task<IActionResult> AddTag(Guid id, [FromBody] AddTicketTagRequest request, [FromServices] IAddTicketTagRequestHandler handler, CancellationToken cancellationToken) =>
    (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

[HttpDelete("{id:guid}/tags/{tagId:guid}")]
public async Task<IActionResult> RemoveTag(Guid id, Guid tagId, [FromQuery] uint? rowVersion, [FromServices] IRemoveTicketTagRequestHandler handler, CancellationToken cancellationToken) =>
    (await handler.HandleAsync(id, tagId, rowVersion, cancellationToken)).ToActionResult(this, Ok);

[HttpPut("{id:guid}/spam")]
public async Task<IActionResult> MarkSpam(Guid id, [FromBody] MarkTicketSpamRequest request, [FromServices] IMarkTicketSpamRequestHandler handler, CancellationToken cancellationToken) =>
    (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);
```

`ControllerActions` sampling passes `null` for `uint?`. Check that its sampler handles `Nullable<uint>`; if it does not, extend the sampler.

- [ ] **Step 1: Write the failing tests**

```csharp
// AddTicketTagRequestHandlerTests
[Fact] public async Task Adding_a_tag_writes_TagAdded_and_returns_its_id_in_the_state()
[Fact] public async Task Adding_a_present_tag_is_200_with_no_event()
[Fact] public async Task An_unknown_tag_is_404_and_a_missing_tag_id_is_400()
// RemoveTicketTagRequestHandlerTests
[Fact] public async Task Removing_a_tag_writes_TagRemoved()
[Fact] public async Task Removing_an_absent_tag_is_200_with_no_event()
[Fact] public async Task A_missing_row_version_on_remove_is_400()
// MarkTicketSpamRequestHandlerTests
[Fact] public async Task Marking_spam_sets_the_flag_writes_MarkedSpam_and_plans_nothing()
[Fact] public async Task Not_spam_clears_the_flag_and_keeps_the_status()
[Fact] public async Task Repeating_the_current_value_is_a_no_op()
[Fact] public async Task A_closed_ticket_is_409_ticket_closed()
```

`TicketTagAndSpamEndpointTests`:

```csharp
[Fact] public async Task A_spammed_ticket_leaves_the_normal_views_and_not_spam_brings_it_back_with_its_status()
// list Open contains it; PUT spam {isSpam:true}; Open no longer contains it, Spam does; PUT spam {isSpam:false}; Open contains it again, status unchanged.
[Fact] public async Task Tag_add_and_remove_round_trip_with_row_versions()
[Fact] public async Task An_agent_without_admin_may_mark_spam()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter "AddTicketTagRequestHandlerTests|RemoveTicketTagRequestHandlerTests|MarkTicketSpamRequestHandlerTests"`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the handlers, the controller actions and the `ControllerActions` rows.

- [ ] **Step 4: Run the tests**

Run Application.Tests, Api.Tests and the architecture tests.
Expected: PASS.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(tickets): idempotent ticket tags and spam flag" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 14: Attachment download (agents)

**Files:**
- Create: `src/TechStrap.Application/Attachments/AttachmentContent.cs`
- Create: `src/TechStrap.Application/Tickets/GetAttachmentRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/AttachmentsController.cs`
- Create: `src/TechStrap.Api/Controllers/AttachmentDownloadResult.cs`
- Modify: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`. Add the row `AttachmentsController.Get=200`, and extend the sampler if needed (see below).
- Create: `tests/TechStrap.Application.Tests/Tickets/GetAttachmentRequestHandlerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Tickets/AttachmentDownloadEndpointTests.cs`

**Interfaces:**

```csharp
namespace TechStrap.Application.Attachments;

/// <summary>An opened attachment for download (application-owned; never a transport type). The receiver disposes Content.</summary>
public sealed record AttachmentContent(Stream Content, string FileName, string ContentType, long Size);

public interface IGetAttachmentRequestHandler
{
    Task<Result<AttachmentContent>> HandleAsync(Guid attachmentId, CancellationToken cancellationToken);
}
```

**Handler rules:**
- The constructor takes `ICurrentAgentClaims`, `ITicketRepository`, `IAttachmentStore` and `ILogger<GetAttachmentRequestHandler>`.
- If `currentAgent.Current is null`, return `AgentErrors.AccessRequired()`.
- Load the attachment with `GetAttachmentByIdAsync`. If it is null, return `TicketErrors.AttachmentNotFound()`.
- Open the file with `stream = await store.OpenReadAsync(attachment.StorageKey, ct)`. If the stream is null:
  - log a warning: "Attachment {AttachmentId} has no stored file.";
  - return `AttachmentNotFound`.
- Otherwise return `new AttachmentContent(stream, attachment.FileName, attachment.ContentType, attachment.Size)`.
- Agents may read any ticket's attachment, including attachments on internal notes. The customer path is 06b (D-036).

**Result type.** `FileResult` does not implement `IStatusCodeActionResult`, so `ResultMappingTests` would read status -1. Use a small custom result:

```csharp
internal sealed class AttachmentDownloadResult(AttachmentContent content) : IActionResult, IStatusCodeActionResult
{
    public int? StatusCode => StatusCodes.Status200OK;

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        await using var stream = content.Content;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = content.ContentType;
        response.ContentLength = content.Size;
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.CacheControl = "private, no-store";
        response.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileNameStar = content.FileName,
            FileName = SafeAsciiName(content.FileName),
        }.ToString();
        await stream.CopyToAsync(response.Body, context.HttpContext.RequestAborted);
    }

    // ASCII fallback for old clients: anything outside printable ASCII, plus quote and backslash, becomes '_'.
    private static string SafeAsciiName(string name) =>
        string.Concat(name.Select(c => c is >= ' ' and <= '~' and not '"' and not '\' ? c : '_'));
}
```

`ContentDispositionHeaderValue` is in `Microsoft.Net.Http.Headers`. If the stored content type is `text/html` or any `text/*`, keep it as is. `attachment` and `nosniff` already stop it rendering inline. Document this in the XML comment.

**Controller:**

```csharp
[ApiController]
[Route("api/attachments")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class AttachmentsController : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromServices] IGetAttachmentRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, content => new AttachmentDownloadResult(content));
}
```

**Harness.** `ControllerActions` samples `AttachmentContent` with `GetUninitializedObject`. That gives a null stream, which is harmless, because `ResultMappingTests` only constructs the result and reads `StatusCode`; it never executes it. Confirm this, and say so in your report.

- [ ] **Step 1: Write the failing tests**

`GetAttachmentRequestHandlerTests`:

```csharp
[Fact] public async Task An_agent_gets_the_stream_name_type_and_size()
[Fact] public async Task An_unknown_attachment_is_404()
[Fact] public async Task A_missing_stored_file_is_404_and_logged()
[Fact] public async Task An_anonymous_caller_is_refused_without_a_lookup()
```

`AttachmentDownloadEndpointTests` (Api, real storage). Reply with a PNG through the Task 10 endpoint, read the attachment id from the response, then:

```csharp
[Fact] public async Task An_agent_downloads_the_exact_bytes_as_an_attachment_with_nosniff()
// 200, body bytes equal the PNG, Content-Disposition starts "attachment", X-Content-Type-Options "nosniff", Cache-Control contains "no-store".
[Fact] public async Task A_file_name_with_quotes_and_unicode_is_encoded_safely()
// upload "re\"port ü.txt" (text/plain): header has filename*=UTF-8''... and an ASCII filename without a raw quote.
[Fact] public async Task An_unknown_attachment_is_404()
[Fact] public async Task An_anonymous_caller_is_401()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter GetAttachmentRequestHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the record, the handler, the result type, the controller, the `ControllerActions` row and the sampler check.

- [ ] **Step 4: Run the tests**

Run Application.Tests, Api.Tests and the architecture tests.
Expected: PASS. `HandlerRules` must accept `AttachmentContent`: it is an Application type, not a transport type.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(attachments): agent attachment download with safe headers" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 15: End-to-end lifecycle, leak checks, OpenAPI and closing docs

**Files:**
- Create: `tests/TechStrap.Api.Tests/Tickets/TicketLifecycleEndToEndTests.cs`
- Modify: `tests/TechStrap.Api.Tests/Intake/SensitiveDataLeakTests.cs`. Add the agent-reply case.
- Modify: `docs/architecture/PHASE-06-ticket-operations.md`. Tick the 06a deliverables and link this plan.
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` and `docs/architecture/00-DISCOVERY-INDEX.md`. Set PHASE-06 to `06a implemented (pending merge); 06b not started`.
- Create: `docs/development/TICKET-OPERATIONS.md`. It holds `curl` examples for each 06a route with a dev bearer token, explains `RowVersion`, lists the email kinds, and links INTAKE.md.

**Interfaces:**
- Consumes everything above.

- [ ] **Step 1: Write the failing tests**

`TicketLifecycleEndToEndTests` uses `ApiFactory` with Postgres and a temp storage root. It starts the Worker's drain against Mailpit through the Infrastructure `MailpitContainer` fixture, if the Api.Tests project can reach it. If it cannot, assert on the outbox rows and leave the Mailpit check to `EmailDrainIntegrationTests`, and say so in your report.

```csharp
[Fact] public async Task A_ticket_goes_from_submission_to_solved_through_the_agent_api()
// 1. POST /api/public/products/orbitly/tickets (form) -> ORB-1.
// 2. agent GET /api/tickets?view=Unassigned contains ORB-1; GET /api/tickets/counts Unassigned >= 1.
// 3. agent PUT assignee (self) with rowVersion from GET /api/tickets/ORB-1 -> 200, no ticket-assigned row (self).
// 4. agent POST note (Markdown) -> 201 internal; GET detail shows it with Visibility Internal.
// 5. agent POST reply (Markdown + PNG, linked article) -> 201; status Pending; email_outbox has kinds ticket-confirmation and agent-reply.
// 6. agent PUT priority High, POST tag, PUT product paperplane (number still ORB-1).
// 7. agent PUT status Solved -> 200; email_outbox gains ticket-solved.
// 8. GET detail: events in order Created, Assigned, MessageAdded(Internal), MessageAdded(Public), StatusChanged(Open->Pending)...,
//    PriorityChanged, TagAdded, ProductChanged, StatusChanged(->Solved); SolvedAt set; FirstResponseAt set once.
// 9. agent downloads the PNG -> identical bytes.
```

Add to `SensitiveDataLeakTests`:

```csharp
[Fact] public async Task An_agent_reply_keeps_the_agent_email_out_of_the_outbox_and_the_link_only_in_the_payload()
// after a reply, email_outbox.payload contains the resolved public name and no "@" of the agent's address;
// the reply's plaintext token appears only in email_outbox.payload (reuse the catalog scan helper).
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "TicketLifecycleEndToEndTests|SensitiveDataLeakTests"`.
Expected: PASS if Tasks 1 to 14 are correct. If a test fails, fix the code it exposes, not the test, and record each fix in your report.

- [ ] **Step 3: Close the docs**

Write `docs/development/TICKET-OPERATIONS.md` and link it from README "Development". Tick the 06a items in PHASE-06, each pointing at its test class. Update the roadmap and index rows.

- [ ] **Step 4: Run every check**

Run:
- `dotnet build TechStrap.slnx -c Release`, expecting 0 warnings;
- `dotnet test --solution TechStrap.CI.slnf -c Release`;
- `pwsh -File scripts/Invoke-ScriptTests.ps1`;
- `pwsh -File scripts/Check-PackageVersions.ps1`;
- `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api`, expecting a clean result.

`OpenApiSurfaceTests` must list all 13 new routes:
- tickets list, counts, get, reply, notes, status, assignee, priority, product, tags add, tag remove and spam;
- the attachment download.

- [ ] **Step 5: Commit**

```bash
git add src tests docs README.md
git commit -m "test(tickets): end-to-end agent lifecycle, leak checks and PHASE-06a docs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```
