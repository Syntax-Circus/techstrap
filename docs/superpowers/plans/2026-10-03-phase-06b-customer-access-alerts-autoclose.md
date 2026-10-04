# PHASE-06b Customer Access, Alerts and Auto-Close Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Customers can use their emailed link to:
- read their ticket;
- reply, with files;
- start a follow-up ticket after the original is Closed;
- download public attachments;
- ask for a fresh link.

Agents are alerted about new tickets and customer replies. The Worker closes Solved tickets after N days.

**Architecture:**
- Every customer route uses the explicit `Public` policy with a rate limit (D-034), and authorises by the `X-Ticket-Token` header inside the handler. All handlers share one resolver, `CustomerAccess`, which turns a token into a ticket and requester or returns one uniform 404, whatever the reason.
- Customer DTOs are separate from the agent DTOs and carry only public data, with agent names resolved through `AgentPublicIdentity` (D-024).
- The PHASE-06a notification planner gains new-ticket, customer-reply, follow-up and lost-link cases. Like the existing cases, these stage template data only (D-033).
- Auto-close follows the `EmailOutboxWorker` shape: a Worker loop resolves a scoped handler per iteration.

**Tech Stack:** .NET 10, ASP.NET Core controllers, EF Core with Npgsql, the PHASE-05/06a email outbox and storage, xUnit v3, Shouldly, NSubstitute, Testcontainers.

**Spec:**
- `docs/architecture/PHASE-06-ticket-operations.md` (the 06b part).
- `02-ARCHITECTURE.md` section 7.3 and its line on the Worker auto-close loop.
- `PHASE-09-public-portal.md` and `UX-BRIEF-portal.md`. The Portal is the consumer of these APIs.
- D-008, D-024, D-032, D-033, D-034, D-035 and D-036.
- The owner decisions of 2026-10-03, recorded as D-037 in Task 1.

### Owner decisions (2026-10-03), recorded as D-037 in Task 1
- **PHASE-06 lands in three PRs.**
  - 06a is already merged.
  - 06b (this plan) covers the customer side, alerts and auto-close.
  - 06c covers hard delete, erasing a requester, dead letters and Serilog PII redaction.
- **Lost link.** Requesting a new link revokes nothing. Old links stay valid until they expire: 90 days sliding, with a 1-year cap.
- **Follow-up dedupe.** Within 2 minutes, the same link plus the same message text returns the follow-up that was already created, instead of creating a second one.
- **Auto-close sends no email.** The Solved notice has already told the customer about the window. This supersedes the "closed notice" in PHASE-06, 02-ARCHITECTURE and FR-TKT-14.

### Technical decisions this plan makes (recorded as D-038 in Task 1; the owner confirms them at plan review)
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
- **Reopen window.** The window shown in emails comes from `AutoCloseOptions.Days`, bound from `TECHSTRAP_AUTOCLOSE_DAYS`.
  - `TicketSolvedEmail` already carries it. `AgentReplyEmail` gains `ReopenDays`.
  - Rows queued before the upgrade have no value (`ReopenDays = 0`). The renderer falls back to `TicketNotices.DefaultReopenDays`.
- **Auto-close runs one unit of work per ticket.**
  - That way a concurrent customer reply makes only that ticket conflict, and it is retried on the next run.
  - The query excludes spam, so Solved spam tickets cannot starve the batch.
  - Tickets are closed with `Actor.System`, and no email is sent (D-037).

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`. `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on.
  - Private fields use `_camelCase`.
  - Constants use PascalCase.
  - Namespaces are file-scoped.
- **Packages.** Versions are managed centrally. This plan adds no package.
- **Project references are fixed.**
  - Domain references nothing.
  - Contracts references nothing and has no attributes or enums. Its public non-static types end in `Dto`, `Request` or `Response`.
  - Application references only Domain, Contracts and `SyntaxCircus.Common`.
  - Infrastructure references Application, Domain, Contracts and packages.
  - Api and Worker reference Application, Infrastructure and Contracts. The Worker never references Api.
- **Handlers.**
  - Shape: `public sealed class XxxHandler : IXxxHandler`, with `HandleAsync(..., CancellationToken)` taking the token last.
  - Constructor parameters may only be Application interfaces, `TimeProvider`, `IOptions<>`, `IOptionsSnapshot<>`, `IOptionsMonitor<>` or `ILogger<>`. Never a concrete options class.
  - No transport types.
  - Worker-only handlers are listed in `ApplicationHandlerRegistration._workerOnly` and registered by the Worker.
- **Controllers.**
  - A class-level `[Authorize(Policy = AuthorizationPolicies.Public)]` plus `[EnableRateLimiting(...)]`. `Public` stands alone and is never `[AllowAnonymous]`.
  - No constructor dependencies.
  - Each action takes exactly one `[FromServices]` handler interface and a `CancellationToken`.
  - Responses use `ToActionResult`.
  - Each action gets a row in `ControllerActions.ExpectedSuccess`.
  - Every customer response sets `Cache-Control: no-store`.
- **Results.**
  - Return `new ResultError(code, message, kind, target?)`, where `target` is only for `Validation`.
  - Validation maps to 400, never 422.
- **Persistence.**
  - Write inside `await using var scope = await unitOfWork.BeginAsync(ct)` with one `CommitAsync`.
  - Generate migrations only with `dotnet ef`. This plan expects none.
- **Secrets.**
  - Plaintext tokens appear only in `email_outbox.payload` and in the one response that hands a follow-up its view URL (D-033).
  - They never appear in logs or any other column.
  - Never log an outbox payload, an email address or a token.
  - Customer DTOs never carry agent ids, emails or surnames, tags, internal notes, events, `LastActivityAt` or other requesters' data (D-024).
- **Customer copy (BRAND.md).**
  - Product branding leads.
  - Powered-by follows `EmailBrandingOptions.ShowPoweredBy`.
  - No mascot.
  - Agent alert emails omit Powered-by.
- **Commits.** Use Conventional Commits, each ending with exactly:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden.**
  - `git add -f`.
  - Committing `.superpowers/`.
  - `docker compose down -v`.
  - Killing processes you did not start.
  - Run `git diff --cached --stat` before every commit.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release` gives 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release` passes.
  - `pwsh -File scripts/Invoke-ScriptTests.ps1` passes.
  - The EF pending-model check is clean.

## Review Focus

1. **A stranger probes customer routes with guessed, stale, revoked or other-ticket tokens.** Every failure looks identical: the same status and the same body bytes. This holds whether the token was missing, garbage, unknown, revoked or expired, the requester was erased, or the attachment belongs to another ticket or an internal note. A successful read leaks no internal data.
   - Pinned in Task 8 by `CustomerUniformNotFoundTests.Every_failure_mode_returns_the_same_404_bytes`.
   - Pinned in Task 10 by `CustomerAttachmentEndpointTests.Other_ticket_and_internal_note_attachments_look_like_missing_ones`.
2. **A customer double-clicks "send" on a Closed ticket, so two requests race.** Exactly one follow-up ticket exists afterwards, and both responses carry its number.
   - Pinned in Task 9 by `CustomerReplyIntegrationTests.Two_concurrent_replies_on_a_closed_ticket_create_one_follow_up`.
3. **Someone uses the lost-link form to learn whether an address has tickets, or to flood an inbox.**
   - Known and unknown addresses get identical responses, with coarse timing parity.
   - The fourth request within an hour for one address sends nothing and still returns 202.
   - Pinned in Task 11 by `LostLinkUniformityTests.Known_and_unknown_addresses_are_indistinguishable` and `RequestNewAccessLinkRequestHandlerTests.Over_the_per_address_cap_nothing_is_sent_and_the_answer_is_the_same`.
4. **The customer view or a customer email leaks agent or internal data.**
   - The serialized `CustomerTicketDto` contains no internal note text, agent email, agent surname, tag, event or other requester.
   - The lost-link and follow-up emails go only to the requester's own address.
   - Pinned in Task 13 by `SensitiveDataLeakTests.The_customer_view_and_customer_emails_carry_no_internal_or_agent_private_data`.
5. **Auto-close meets many Solved spam tickets, or a customer replies at the moment it runs.**
   - Spam does not block real tickets from closing.
   - A reply that races the close is never lost: either the reply reopens the ticket, or the close fails for that ticket only and the next run re-evaluates it.
   - Pinned in Task 12 by `AutoCloseIntegrationTests.Spam_does_not_starve_the_batch_and_a_racing_reply_is_never_lost`.

---
## File Structure

- **Docs**
  - Decision log: D-037 (owner decisions) and D-038 (technical decisions).
  - `PHASE-06-ticket-operations.md`: the 06b/06c split, and auto-close sends no notice.
  - `02-ARCHITECTURE.md`: section 7.3 and the Worker loop line.
  - `01-REQUIREMENTS.md`: FR-TKT-14.
  - `PHASE-09-public-portal.md`: the customer attachment route.
  - `docs/development/TICKET-OPERATIONS.md`: a new "Customer access" section.
- **Contracts**
  - `Tickets/CustomerDtos.cs`: `CustomerTicketDto`, `CustomerMessageDto`, `CustomerReplyResponse`.
  - `Tickets/CustomerRequests.cs`: `AddCustomerReplyRequest`, `RequestNewAccessLinkRequest`.
- **Application**
  - `Tickets/Customer/`
    - Shared pieces: `CustomerAccess`, `CustomerErrors`, `CustomerEmailAddress`.
    - Handlers: `GetCustomerTicketRequestHandler`, `AddCustomerReplyRequestHandler`, `GetCustomerAttachmentRequestHandler`, `RequestNewAccessLinkRequestHandler`.
    - Options: `LostLinkOptions`.
  - `Tickets/AutoClose/`: `AutoCloseOptions`, `AutoCloseSolvedTicketsHandler`.
  - `Tickets/Notifications/`: new planner methods; `TicketNotices.DefaultReopenDays`.
  - `Email/EmailTemplates.cs`: kinds `new-ticket-alert`, `customer-reply-alert`, `access-links`, plus their payloads. `AgentReplyEmail.ReopenDays`.
  - `Persistence/`: the new repository and store reads.
- **Infrastructure**
  - Repository and store reads.
  - Planner cases.
  - New renderer templates.
  - `AutoClose/AutoCloseServiceCollectionExtensions.cs`.
- **Api**
  - `Controllers/CustomerTicketsController.cs` (ticket, replies, attachments) and `CustomerAccessLinkController.cs`.
  - `Controllers/CustomerReplyForm.cs`.
  - `Options/CustomerRateLimitOptions.cs`.
  - `Startup/CustomerRateLimiting.cs`.
  - `Startup/AttachmentSandbox.cs`: the CSP middleware moved out of `Program.cs`, now covering both download routes.
  - `.env.example`.
- **Worker**
  - `AutoClose/AutoCloseWorker.cs`.
  - `Program.cs`.
  - `.env.example`.
- **Tests**
  - Application handler tests.
  - Infrastructure integration tests: repository reads, planner, renderer, drain, customer reply, auto-close.
  - Api endpoint tests: customer routes, uniform 404, rate limits, lost-link uniformity.
  - Worker tests: the auto-close loop and its boundary.
  - The end-to-end lifecycle and the leak test, both extended.

---

### Task 1: Record the decisions and align the docs

**Files:**
- Modify: `docs/architecture/04-DECISION-LOG.md`. Add the approval-basis bullet, index rows D-037 and D-038, and their sections.
- Modify: `docs/architecture/PHASE-06-ticket-operations.md`
- Modify: `docs/architecture/02-ARCHITECTURE.md`
- Modify: `docs/architecture/01-REQUIREMENTS.md`
- Modify: `docs/architecture/PHASE-09-public-portal.md`
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`
- Modify: `docs/architecture/00-DISCOVERY-INDEX.md`

**Interfaces:** none (docs only).

- [ ] **Step 1: Add D-037 and D-038**

Follow the D-035 and D-036 format (Status, Date, Owner, Context, Decision, Alternatives Considered, Consequences, Related).

- The approval-basis bullet reads: "**Owner decision (2026-10-03, PHASE-06b planning):** D-037 (three-PR split, lost link keeps old links, follow-up dedupe window, no auto-close email). D-038 was proposed in the PHASE-06b plan and approved when the owner approved the plan."
- Add these index rows:

```
| D-037 | PHASE-06 lands as 06a/06b/06c; lost link keeps old links; 2-minute follow-up dedupe; auto-close sends no email | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-06, PHASE-09 |
| D-038 | Customer API: Public routes with in-handler token auth, uniform 404, separate customer attachment route, lost-link rules, alert recipients, reopen window from AutoCloseOptions, per-ticket auto-close | Approved (owner, PHASE-06b plan review) | 2026-10-03 | PHASE-06, PHASE-09, PHASE-12 |
```

- D-037 Decision lists the four owner bullets from the plan header.
- D-038 Decision is the plan header's "Technical decisions" bullets, copied verbatim.
- D-035's 06b scope list moves "delete and erase, dead letters" to 06c. Add a line under D-035: "Superseded in part by D-037 (06c)".

- [ ] **Step 2: Align the spec docs**

1. **`PHASE-06-ticket-operations.md`**
   - Objective: add "06b: customer routes, follow-ups, lost link, new-ticket and customer-reply alerts, auto-close, customer rate limits; 06c: delete, erase, dead letters, Serilog redaction (D-037)."
   - Architecture Decisions: the auto-close bullet becomes "...writes a `StatusChanged` event with actor `System`; no customer email (D-037)".
   - Replace the customer-access bullet's "**Assumption**: old tokens stay valid" with "(D-037)".
   - In the boundary table:
     - the attachment row splits into the agent route (06a) and `GET /api/customer/attachments/{id}` → `GetCustomerAttachmentRequestHandler` (06b);
     - the auto-close row's dependencies become `ITicketRepository, IUnitOfWork, TimeProvider, IOptions<AutoCloseOptions>, ILogger` (no planner);
     - the customer reply and lost-link rows get the dependency lists this plan builds (Tasks 9 and 11).
   - Tasks:
     - P06-T17 drops "queues a notice".
     - The "auto-close notice" template is removed from the Deliverables list.
   - Risks: tick the follow-up idempotency item with "Resolved (D-037: 2-minute window)".
2. **`02-ARCHITECTURE.md`**
   - Section 7.3: the customer rows and the auto-close row match the PHASE-06 table. Add the customer attachment row.
   - The Worker loop line drops "queues a closed notice".
3. **`01-REQUIREMENTS.md`, FR-TKT-14:** "...closes the ticket; no customer email (D-037)".
4. **`PHASE-09-public-portal.md`:** the attachment pass-through forwards to `GET /api/customer/attachments/{id}` with `X-Ticket-Token`.
5. **Roadmap and index:** the PHASE-06 row becomes "06a complete; 06b in progress; 06c not started", and the decisions column gains D-037 and D-038.

- [ ] **Step 3: Check and commit**

Run `pwsh -File scripts/Invoke-ScriptTests.ps1` (expect PASS). Then run this grep, which should return no hits outside quoted history:

`grep -n "closed notice\|auto-close notice\|queues a notice" docs/architecture/*.md`

```bash
git add docs
git commit -m "docs: record PHASE-06b decisions (D-037, D-038) and align specs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 2: Customer contracts

**Files:**
- Create: `src/TechStrap.Contracts/Tickets/CustomerDtos.cs`
- Create: `src/TechStrap.Contracts/Tickets/CustomerRequests.cs`
- Create: `tests/TechStrap.Architecture.Tests/CustomerDtoShapeTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace TechStrap.Contracts.Tickets;

/// <summary>
/// What a customer sees through their link (D-024, D-038). Public data only: no agent ids, emails or surnames, tags, internal
/// notes, events, LastActivityAt or other requesters. Status is a TicketStatuses name; the Portal maps it to words.
/// </summary>
public sealed record CustomerTicketDto(
    string Number, string Subject, string Status, DateTimeOffset CreatedAt, IReadOnlyList<CustomerMessageDto> Messages);

/// <summary>
/// One public message. AuthorType is a MessageAuthorTypes name; AuthorDisplayName is the resolved public agent name for agent
/// messages (AgentPublicIdentity) and null for the customer's own and system messages. BodyHtml is sanitised HTML.
/// </summary>
public sealed record CustomerMessageDto(
    Guid Id, string AuthorType, string? AuthorDisplayName, string BodyHtml, DateTimeOffset CreatedAt, IReadOnlyList<AttachmentDto> Attachments);

/// <summary>
/// Result of a customer reply. For a reply on an open ticket: TicketNumber is that ticket, MessageId the new message, and
/// FollowUpViewUrl null. For a reply on a Closed ticket: TicketNumber is the follow-up's number, MessageId its first message,
/// and FollowUpViewUrl the follow-up's link (a fresh token; the only response that carries a link, D-038).
/// </summary>
public sealed record CustomerReplyResponse(string TicketNumber, Guid MessageId, bool FollowUpCreated, string? FollowUpViewUrl);

public sealed record AddCustomerReplyRequest(string? Body);

public sealed record RequestNewAccessLinkRequest(string? Email);
```

Reuse the existing `AttachmentDto(Id, FileName, ContentType, Size)`.

- [ ] **Step 1: Write the failing test**

`CustomerDtoShapeTests` (in the Architecture tests, which reference Contracts):

```csharp
public sealed class CustomerDtoShapeTests
{
    private static readonly string[] _forbidden =
        ["AgentId", "AuthorId", "Email", "Tags", "TagIds", "Events", "LastActivityAt", "AssigneeId", "AssigneeName",
         "RequesterId", "RowVersion", "Visibility", "MetadataJson", "ProductId", "StorageKey", "TokenHash"];

    [Theory]
    [InlineData(typeof(CustomerTicketDto))]
    [InlineData(typeof(CustomerMessageDto))]
    [InlineData(typeof(CustomerReplyResponse))]
    public void Customer_dtos_expose_no_internal_or_agent_private_fields(Type dto) =>
        Properties(dto).ShouldNotContain(name => _forbidden.Contains(name));

    [Fact]
    public void Every_type_reachable_from_a_customer_dto_is_customer_safe()
    // walk property types recursively from CustomerTicketDto: every record type reached is one of
    // CustomerTicketDto, CustomerMessageDto, AttachmentDto (and no agent DTO such as MessageDto or TicketEventDto).

    private static IEnumerable<string> Properties(Type type) => type.GetProperties().Select(p => p.Name);
}
```

- [ ] **Step 2: Run the test and confirm it fails**

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release --filter CustomerDtoShapeTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write both files as shown, with XML docs.

- [ ] **Step 4: Run the tests**

Run the filtered tests, then the full Architecture tests (including `ContractNamingTests`).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(contracts): customer ticket, message, reply and access-link contracts" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 3: Repository and store reads for the customer side

**Files:**
- Modify: `src/TechStrap.Application/Persistence/ITicketRepository.cs` and `src/TechStrap.Infrastructure/Persistence/Repositories/TicketRepository.cs`
- Modify: `src/TechStrap.Application/Persistence/IEmailOutboxStore.cs` and its EF implementation
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/CustomerReadTests.cs`
- Modify: the existing `ListSolvedBeforeAsync` tests. Find them with `grep -rn ListSolvedBeforeAsync tests`.

**Interfaces:**
- Produces:

```csharp
namespace TechStrap.Application.Tickets;
/// <summary>A follow-up created from a parent, with its first public message body (sanitised HTML) for the dedupe check.</summary>
public sealed record FollowUpCandidate(Guid TicketId, string Number, Guid FirstMessageId, string FirstMessageBody, DateTimeOffset CreatedAt);
/// <summary>A requester's ticket for the lost-link email.</summary>
public sealed record RequesterTicketLink(Guid TicketId, Guid ProductId, string Number, string Subject, DateTimeOffset LastActivityAt);

// ITicketRepository additions
/// <summary>Follow-ups of a parent created at or after <paramref name="since"/>, newest first, each with its first public message.</summary>
Task<IReadOnlyList<FollowUpCandidate>> ListRecentFollowUpsAsync(Guid parentTicketId, DateTimeOffset since, CancellationToken cancellationToken);
/// <summary>A requester's non-spam tickets, most recently active first, at most <paramref name="limit"/>.</summary>
Task<IReadOnlyList<RequesterTicketLink>> ListRecentTicketsForRequesterAsync(Guid requesterId, int limit, CancellationToken cancellationToken);

// ListSolvedBeforeAsync: now also requires !IsSpam (so Solved spam can never starve auto-close); document it.

// IEmailOutboxStore addition
/// <summary>How many rows of <paramref name="kind"/> to <paramref name="toAddress"/> (case-insensitive) were created at or after <paramref name="since"/>, any status.</summary>
Task<int> CountRecentAsync(string kind, string toAddress, DateTimeOffset since, CancellationToken cancellationToken);
```

All the new reads are `AsNoTracking`. `ListSolvedBeforeAsync` stays tracked, because the auto-close handler calls `Update` on its results.

- [ ] **Step 1: Write the failing tests**

`CustomerReadTests` is a `PostgresIntegrationTestBase` that uses `PersistenceTestHost` and the existing seeding helpers.

```csharp
[Fact] public async Task Recent_follow_ups_of_a_parent_come_with_their_first_public_message()
// a Closed parent with two follow-ups (one created 5 min ago, one 30 s ago, each with a first customer message);
// ListRecentFollowUpsAsync(parent, now-2min) returns only the newer one, with its number, first message id and body.
[Fact] public async Task A_requesters_recent_tickets_exclude_spam_and_are_capped()
// 7 tickets for the requester (one spam), 1 for someone else; limit 5 -> 5 non-spam rows, most recent activity first.
[Fact] public async Task Solved_spam_is_never_returned_for_auto_close()
[Fact] public async Task Recent_outbox_rows_are_counted_per_kind_and_address_case_insensitively()
// 2 "access-links" rows to ANN@example.com (one sent, one pending), 1 older than the window, 1 of another kind -> CountRecentAsync = 2.
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter CustomerReadTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the methods and records, plus the spam filter. Update the existing `ListSolvedBeforeAsync` test so it asserts that spam is excluded.

- [ ] **Step 4: Run the tests**

Run the full Infrastructure.IntegrationTests and Application.Tests, then the EF pending-model check (expect clean).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(persistence): follow-up, requester-ticket and outbox-count reads; auto-close excludes spam" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 4: Customer access core

**Files:**
- Create: `src/TechStrap.Application/Tickets/Customer/CustomerErrors.cs`
- Create: `src/TechStrap.Application/Tickets/Customer/CustomerAccess.cs`
- Create: `src/TechStrap.Application/Tickets/Customer/CustomerEmailAddress.cs`
- Create: `tests/TechStrap.Application.Tests/Tickets/Customer/CustomerAccessTests.cs`
- Create: `tests/TechStrap.Application.Tests/Tickets/Customer/CustomerEmailAddressTests.cs`

**Interfaces:**
- Consumes: `IAccessTokenService.Hash`; `ITicketRepository.GetAccessTokenByHashAsync` and `GetByIdAsync`; `IRequesterRepository.GetByIdAsync`; `TicketAccessToken.IsValid`.
- Produces:

```csharp
namespace TechStrap.Application.Tickets.Customer;

internal static class CustomerErrors
{
    public const string NotFoundCode = "not-found";
    /// <summary>The one failure every customer route returns for any access problem (D-038). Never vary the text.</summary>
    public static ResultError NotFound() => new(NotFoundCode, "Not found.", ResultErrorKind.NotFound);
    public static ResultError EmailInvalid() => new("email-invalid", "Enter a valid email address.", ResultErrorKind.Validation, "email");
    public static ResultError ReplyConflict() =>
        new("reply-conflict", "Your reply could not be saved. Please try again.", ResultErrorKind.Conflict);
}

internal sealed record CustomerContext(TicketAccessToken Token, Ticket Ticket, Requester Requester);

internal static class CustomerAccess
{
    /// <summary>Hard cap on the header value before hashing (a real token is 43 characters).</summary>
    public const int MaxTokenLength = 128;

    /// <summary>
    /// Resolves a raw X-Ticket-Token value to its ticket and requester, or the uniform NotFound. Always performs the hash lookup
    /// for a non-empty value of sane length so a well-formed guess and a real token cost the same work. Loads tracked entities
    /// (callers may RecordUse/Update in their unit of work).
    /// </summary>
    public static async Task<Result<CustomerContext>> ResolveAsync(
        string? rawToken, IAccessTokenService tokens, ITicketRepository tickets, IRequesterRepository requesters,
        TimeProvider clock, CancellationToken cancellationToken);
}

internal static class CustomerEmailAddress
{
    /// <summary>Trims and lower-cases; true when the address has the shape the Domain accepts (same rule as Guard.Email, max 320).</summary>
    public static bool TryNormalize(string? value, out string email);
}
```

**`ResolveAsync` rules:**
1. A blank token, or one longer than `MaxTokenLength`, returns NotFound.
2. Hash the token with `tokens.Hash(raw.Trim())` and call `GetAccessTokenByHashAsync`. A null result returns NotFound.
3. `!token.IsValid(clock)` (revoked or expired) returns NotFound.
4. Load the ticket by `token.TicketId`. Null returns NotFound.
5. Load the requester by `token.RequesterId`. Null, or `IsErased`, returns NotFound.
6. Otherwise return Success.

Every failure returns the same `CustomerErrors.NotFound()` instance. Nothing is logged that contains the token.

`CustomerEmailAddress`: copy the Domain's email regex. Grep `Guard.Email` for the pattern and the 320-character limit, and add a parity test against `Requester.Create` for a sample of addresses, so the two rules cannot drift.

- [ ] **Step 1: Write the failing tests**

```csharp
// CustomerAccessTests (substitutes; real TicketAccessToken via TicketAccessToken.Issue, FakeTimeProvider)
[Theory] [InlineData(null)] [InlineData("")] [InlineData("   ")]
public async Task A_missing_token_is_not_found_without_a_lookup(string? raw)
[Fact] public async Task An_overlong_token_is_not_found_without_a_lookup()
[Fact] public async Task An_unknown_token_is_not_found()
[Fact] public async Task A_revoked_or_expired_token_is_not_found()
[Fact] public async Task A_token_whose_ticket_is_gone_or_whose_requester_is_erased_is_not_found()
[Fact] public async Task Every_failure_is_the_same_error()
// collect the ResultError from each failure case: all have the same Code, Message and Kind.
[Fact] public async Task A_valid_token_resolves_its_ticket_and_requester()

// CustomerEmailAddressTests
[Theory] [InlineData(" Ann@Example.com ", "ann@example.com")] public void Valid_addresses_are_normalised(string input, string expected)
[Theory] [InlineData("ann")] [InlineData("ann@")] [InlineData("a b@c.d")] [InlineData(null)] public void Malformed_addresses_are_rejected(string? input)
[Fact] public void The_rule_matches_the_domain()
// for each address in a shared list, TryNormalize succeeds exactly when Requester.Create succeeds.
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter "CustomerAccessTests|CustomerEmailAddressTests"`
Expected: a build failure.

- [ ] **Step 3: Implement**

Implement the three types as specified above.

- [ ] **Step 4: Run the tests**

Run the filter and the Architecture tests.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(tickets): customer access resolver with one uniform not-found" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 5: Auto-close options, reopen window and new email kinds

**Files:**
- Create: `src/TechStrap.Application/Tickets/AutoClose/AutoCloseOptions.cs`
- Modify: `src/TechStrap.Application/Tickets/Notifications/TicketNotices.cs`. Rename `ReopenDays` to `DefaultReopenDays`.
- Modify: `src/TechStrap.Application/Email/EmailTemplates.cs`, `IEmailTemplateRenderer.cs` and `DrainEmailOutboxHandler.cs`
- Modify: `src/TechStrap.Infrastructure/Email/EmailTemplateRenderer.cs`
- Modify: `src/TechStrap.Infrastructure/Tickets/TicketOperationsServiceCollectionExtensions.cs`. Bind `AutoCloseOptions`.
- Modify: `src/TechStrap.Api/.env.example`, `src/TechStrap.Worker/.env.example` and `EnvExampleCompletenessTests`
- Modify: the renderer and drain tests

**Interfaces:**
- Produces:

```csharp
namespace TechStrap.Application.Tickets.AutoClose;

/// <summary>Auto-close (D-008, D-037). Days comes from the flat key TECHSTRAP_AUTOCLOSE_DAYS; the rest from section AutoClose.</summary>
public sealed class AutoCloseOptions
{
    public const string DaysKey = "TECHSTRAP_AUTOCLOSE_DAYS";
    public const string SectionName = "AutoClose";
    public int Days { get; set; } = 7;               // 1..365
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 15;   // 1..1440
    public int BatchSize { get; set; } = 50;         // 1..Paging.MaxBatchSize
}
```

**`EmailTemplates` additions:**

```csharp
public const string NewTicketAlert = "new-ticket-alert";
public const string CustomerReplyAlert = "customer-reply-alert";
public const string AccessLinks = "access-links";

/// <summary>Internal alert to an opted-in agent. RequesterLabel is the requester's name or email (agents may see it).</summary>
public sealed record NewTicketAlertEmail(string TicketNumber, string Subject, string ProductName, string RequesterLabel, bool IsFollowUp, string? AdminLink);
/// <summary>Internal alert: a customer replied. Reopened is true when the reply moved Pending/Solved to Open.</summary>
public sealed record CustomerReplyAlertEmail(string TicketNumber, string Subject, string ProductName, bool Reopened, string? AdminLink);
/// <summary>Lost-link email to the requester's own address (D-038): at most LostLinkOptions.MaxLinks entries.</summary>
public sealed record AccessLinksEmail(string? RequesterName, IReadOnlyList<AccessLinkEntry> Links);
public sealed record AccessLinkEntry(string TicketNumber, string Subject, string PortalLink);
// AgentReplyEmail gains a trailing `int ReopenDays = 0` (0 = row queued before 06b; renderer uses TicketNotices.DefaultReopenDays).
```

**`IEmailTemplateRenderer` additions:**
- `RenderNewTicketAlert(NewTicketAlertEmail, EmailBranding)`
- `RenderCustomerReplyAlert(CustomerReplyAlertEmail, EmailBranding)`
- `RenderAccessLinks(AccessLinksEmail, EmailBranding)`

The two alerts follow `RenderTicketAssigned`: no Powered-by, and an Admin button only for http(s) links. `RenderAccessLinks` is customer-facing: Powered-by follows the option, and there is one button per link plus a fallback line.

**Subjects:**
- `[{n}] New ticket: {subject}`, or `[{n}] New follow-up: {subject}` when `IsFollowUp` is true.
- `[{n}] Customer replied: {subject}`.
- `Your request links`, using the product display name in the layout header.

**Drain.** Add three `Parse` arms. The required fields are:
- new-ticket alert: number, subject, product name and requester label;
- customer-reply alert: number, subject and product name;
- access links: at least one link, each with a number and a link.

Add three render arms.

`AgentReply` rendering uses `model.ReopenDays > 0 ? model.ReopenDays : TicketNotices.DefaultReopenDays` instead of the constant.

**Binding** in `AddTechStrapTicketOperations`:

```csharp
services.AddOptions<AutoCloseOptions>()
    .Configure(options =>
    {
        configuration.GetSection(AutoCloseOptions.SectionName).Bind(options);
        if (int.TryParse(configuration[AutoCloseOptions.DaysKey], out var days)) options.Days = days;
    })
    .Validate(o => o.Days is >= 1 and <= 365, $"{AutoCloseOptions.DaysKey} must be between 1 and 365.")
    .Validate(o => o.IntervalMinutes is >= 1 and <= 1440, "AutoClose:IntervalMinutes must be between 1 and 1440.")
    .Validate(o => o.BatchSize is >= 1 and <= Paging.MaxBatchSize, $"AutoClose:BatchSize must be between 1 and {Paging.MaxBatchSize}.")
    .ValidateOnStart();
```

Task 12 makes the Worker register the same options. Factor the block into one `internal static void AddAutoCloseOptions(IServiceCollection, IConfiguration)`, so the Api and the Worker share it. A non-numeric `TECHSTRAP_AUTOCLOSE_DAYS` must fail startup: add a `.Validate` that the raw value is blank or parses as an int.

**`.env.example` keys:**
- Api: `TECHSTRAP_AUTOCLOSE_DAYS=7`, with a comment saying it is also shown in emails.
- Worker: `TECHSTRAP_AUTOCLOSE_DAYS=7`, `AUTOCLOSE__ENABLED=true`, `AUTOCLOSE__INTERVALMINUTES=15` and `AUTOCLOSE__BATCHSIZE=50`.

Add all of them to `EnvExampleCompletenessTests`.

- [ ] **Step 1: Write the failing tests**

Renderer tests:
- one per new template, with subject and content;
- alerts carry no Powered-by;
- access links has one button per entry and HTML-encodes every field;
- an agent reply with `ReopenDays` 10 says "10 days";
- an agent reply with `ReopenDays` 0 says "7 days".

Drain tests:
- each new kind renders and sends;
- each kind missing a required field gives `payload-invalid`;
- an access-links payload with no links gives `payload-invalid`.

Options tests, with a small new class `AutoCloseOptionsTests` in Infrastructure.IntegrationTests:
- the defaults;
- `TECHSTRAP_AUTOCLOSE_DAYS=10` is bound;
- each of these fails validation: 0, 366, "abc", and an interval of 0.

- [ ] **Step 2: Run the tests and confirm they fail**

Run the renderer, drain and options test filters.
Expected: a build failure.

- [ ] **Step 3: Implement**

Write everything specified under this task's Interfaces.

- [ ] **Step 4: Run the tests**

Run: Infrastructure.IntegrationTests (filters), Application.Tests, Api.Tests (env-example and host smoke), and the Architecture tests.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(email): new-ticket, customer-reply and access-link emails; reopen window from auto-close options" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 6: Planner cases for alerts, follow-ups and lost links

**Files:**
- Modify: `src/TechStrap.Application/Tickets/Notifications/ITicketNotificationPlanner.cs`
- Modify: `src/TechStrap.Infrastructure/Tickets/TicketNotificationPlanner.cs`. Add the `IAgentRepository` and `IOptions<AutoCloseOptions>` dependencies.
- Create: `src/TechStrap.Application/Tickets/Customer/LostLinkOptions.cs`. Bind it in `AddTechStrapTicketOperations`.
- Modify: `tests/TechStrap.Infrastructure.IntegrationTests/TicketNotificationPlannerTests.cs`

**Interfaces:**
- Produces, as new interface members:

```csharp
/// <summary>New-ticket alert to agents opted in for the ticket's product (active only). Not for spam (a new ticket is never spam).</summary>
Task PlanNewTicketAsync(Ticket ticket, Requester requester, bool isFollowUp, CancellationToken cancellationToken);
/// <summary>Customer replied: to the assignee if set and active, else to agents opted in for the product. Nothing for spam tickets (D-038).</summary>
Task PlanCustomerReplyAsync(Ticket ticket, bool reopened, CancellationToken cancellationToken);
/// <summary>Confirmation to the requester for a follow-up ticket (ticket-confirmation kind, fresh token). Skips spam and erased, as for any customer email.</summary>
Task PlanFollowUpConfirmationAsync(Ticket followUp, CancellationToken cancellationToken);
/// <summary>One access-links email to the requester's own address with a fresh link per ticket (D-037: nothing revoked). Skips erased requesters and empty lists.</summary>
Task PlanAccessLinksAsync(Requester requester, IReadOnlyList<RequesterTicketLink> tickets, CancellationToken cancellationToken);
```

```csharp
namespace TechStrap.Application.Tickets.Customer;
public sealed class LostLinkOptions
{
    public const string SectionName = "LostLink";
    public int MaxLinks { get; set; } = 5;               // 1..10
    public int PerAddressLimit { get; set; } = 3;        // 1..20
    public int PerAddressWindowMinutes { get; set; } = 60; // 1..1440
}
```

**Rules:**
- The existing `PlanSolvedAsync` and `PlanAgentReplyAsync` now pass `autoClose.Value.Days` as the reopen window: `TicketSolvedEmail.ReopenDays` and the new `AgentReplyEmail.ReopenDays`.
- **Agent alerts** (`PlanNewTicketAsync` and `PlanCustomerReplyAsync`):
  - They stage one row per recipient and issue no token.
  - Their links come from `adminOptions.Value.TicketLink(number)`.
  - The product must exist; if it doesn't, skip.
  - Duplicate recipients are removed by agent id.
  - `RequesterLabel` is `requester.Name ?? requester.Email`.
- **`PlanCustomerReplyAsync`:**
  - Spam tickets get nothing.
  - If the ticket is assigned, load the assignee. If the assignee is active, alert only them.
  - Otherwise alert `ListAgentsToAlertForProductAsync(ticket.ProductId)`.
- **`PlanFollowUpConfirmationAsync`:** reuse the existing private customer path (`PlanCustomerAsync`) with kind `TicketConfirmation` and payload `TicketConfirmationEmail(number, subject, requester.Name, link, null)`.
- **`PlanAccessLinksAsync`:**
  - Skip an erased requester or an empty list.
  - Take at most `lostLink.Value.MaxLinks` tickets.
  - For each ticket, issue a token. If issuing fails, skip that ticket. Otherwise build the link.
  - Branding uses the product of the first ticket, the most recently active.
  - Call `EmailOutboxItem.Enqueue(AccessLinks, requester.Email, json, firstProductId, firstTicketId, clock)`.
  - Add each token only after the row is valid. This keeps the PHASE-06a ordering rule: all tokens or none.
  - If the payload would exceed the 16 000-character cap, drop links from the end until it fits, and log the count dropped.

- [ ] **Step 1: Write the failing tests**

Add these to `TicketNotificationPlannerTests` (real Postgres; all the services):

```csharp
[Fact] public async Task A_new_ticket_alerts_only_agents_opted_in_for_its_product()
// agents: A opted in for Orbitly, B opted in for Paperplane, C opted in but inactive -> one row to A; kind new-ticket-alert; no token.
[Fact] public async Task A_customer_reply_alerts_the_active_assignee_only()
[Fact] public async Task A_customer_reply_on_an_unassigned_or_inactive_assignee_ticket_alerts_the_opted_in_agents()
[Fact] public async Task A_customer_reply_on_a_spam_ticket_alerts_nobody()
[Fact] public async Task A_follow_up_confirmation_goes_to_the_requester_with_a_fresh_link()
[Fact] public async Task Access_links_send_one_email_with_a_fresh_link_per_ticket_and_revoke_nothing()
// requester with 3 tickets and an existing token: one row, 3 links, 3 new tokens, the old token still IsValid.
[Fact] public async Task Access_links_are_capped_and_skip_erased_requesters()
[Fact] public async Task Solved_and_reply_emails_carry_the_configured_reopen_window()
// TECHSTRAP_AUTOCLOSE_DAYS=10 -> payload reopenDays 10.
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter TicketNotificationPlannerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the members and rules above. Validate `LostLinkOptions` on start with the ranges in its comments.

- [ ] **Step 4: Run the tests**

Run the planner tests, Application.Tests, Api.Tests and the Architecture tests.
Expected: PASS.

Some Application handler tests substitute the planner interface. Adding members needs no change to them.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(tickets): plan new-ticket, customer-reply, follow-up and access-link emails" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 7: New-ticket alerts on intake

**Files:**
- Modify: `src/TechStrap.Application/Intake/SubmitTicketRequestHandler.cs`. Add the `ITicketNotificationPlanner` dependency.
- Modify: `tests/TechStrap.Application.Tests/Intake/SubmitTicketRequestHandlerTests.cs`
- Modify: any integration or Api test that constructs the handler or its container. Find them with `grep -rn "SubmitTicketRequestHandler(" tests`.

**Interfaces:**
- Consumes `PlanNewTicketAsync` (Task 6).

**Rules:**
- In `AttemptAsync`, after the confirmation is enqueued and before commit, call `await planner.PlanNewTicketAsync(ticket.Value, requester, isFollowUp: false, ct)`.
- No alert is sent for:
  - the honeypot;
  - an idempotent replay;
  - any failed attempt (the rows roll back with the scope).
- A retry attempt stages its own rows in its own scope, so no duplicates survive.

`SubmitTicketIntegrationTests` builds its container with `AddTechStrapIntake`. It now also needs `AddTechStrapTicketOperations`, because the planner is registered there; add it to that test host.

- [ ] **Step 1: Write the failing tests**

```csharp
// SubmitTicketRequestHandlerTests
[Fact] public async Task A_new_ticket_plans_the_new_ticket_alert_once()
[Fact] public async Task The_honeypot_and_an_idempotent_replay_plan_no_alert()
[Fact] public async Task A_retried_attempt_plans_the_alert_in_the_attempt_that_commits()
// UnitOfWorkSubstitute.Create(Conflict("duplicate"), Success): PlanNewTicketAsync received twice (once per attempt; the first rolled back).
```

Also add an integration assertion in `SubmitTicketIntegrationTests`: with an agent opted in for the product, a submission commits one `new-ticket-alert` row.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter SubmitTicketRequestHandlerTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

Add the planner dependency and the call.

- [ ] **Step 4: Run the tests**

Run Application.Tests, Infrastructure.IntegrationTests (filter `SubmitTicketIntegrationTests`) and Api.Tests.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(intake): alert opted-in agents about new tickets" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 8: Customer ticket view, customer rate limits and the uniform 404

**Files:**
- Create: `src/TechStrap.Application/Tickets/Customer/GetCustomerTicketRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/CustomerTicketsController.cs`. It holds the `Get` action; Tasks 9 and 10 add more actions.
- Create: `src/TechStrap.Api/Options/CustomerRateLimitOptions.cs`
- Create: `src/TechStrap.Api/Startup/CustomerRateLimiting.cs`
- Modify: `src/TechStrap.Api/Program.cs`. Add the options validation and the policies inside `AddRateLimiter`.
- Modify: `src/TechStrap.Api/.env.example` and `EnvExampleCompletenessTests`
- Modify: `ControllerActions.ExpectedSuccess`. Add `["CustomerTicketsController.Get"] = 200`.
- Create: `tests/TechStrap.Application.Tests/Tickets/Customer/GetCustomerTicketRequestHandlerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Customer/CustomerTestData.cs`
- Create: `tests/TechStrap.Api.Tests/Customer/CustomerTicketEndpointTests.cs`
- Create: `tests/TechStrap.Api.Tests/Customer/CustomerUniformNotFoundTests.cs`
- Create: `tests/TechStrap.Api.Tests/Customer/CustomerRateLimitTests.cs`

**Interfaces:**
- Consumes:
  - Task 2: `CustomerTicketDto` and `CustomerMessageDto`.
  - Task 4: `CustomerAccess` and `CustomerErrors`.
  - `ITicketRepository.GetMessagesAsync(publicOnly: true)` and `GetAttachmentsAsync(publicOnly: true)`.
  - `IAgentRepository.GetByIdsAsync`.
  - `IProductRepository.GetByIdAsync`.
  - `AgentPublicIdentity.Resolve`.
  - `TicketDtoMapper.ToWire` and `ToDto(Attachment)`.
- Produces:

```csharp
public interface IGetCustomerTicketRequestHandler
{
    Task<Result<CustomerTicketDto>> HandleAsync(string? token, CancellationToken cancellationToken);
}
```

```csharp
namespace TechStrap.Api.Options;
public sealed class CustomerRateLimitOptions
{
    public const string SectionName = "RateLimiting:Customer";
    public const string TokenAccessPolicyName = "token-access";
    public const string LostLinkPolicyName = "lost-link";
    public int TokenAccessPermitLimit { get; set; } = 60;     // per client IP
    public int TokenAccessWindowSeconds { get; set; } = 60;
    public int LostLinkPermitLimit { get; set; } = 5;         // per client IP
    public int LostLinkWindowSeconds { get; set; } = 3600;
}
```

`CustomerRateLimiting.AddCustomerPolicies(this RateLimiterOptions, CustomerRateLimitOptions)` registers both policies with `AddPerIpFixedWindow`. The Program.cs wiring follows the intake pattern exactly: a `.Validate` for each value `>= 1`, `.ValidateOnStart`, and a lazy read inside `AddRateLimiter`.

**Handler rules.** The constructor takes `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `IAgentRepository`, `IProductRepository`, `IUnitOfWork` and `TimeProvider`.
1. `await using var scope = await unitOfWork.BeginAsync(ct)`.
2. `access = await CustomerAccess.ResolveAsync(...)`. On failure, return `CustomerErrors.NotFound()`.
3. `token.RecordUse(clock)`. A failure, which cannot happen after a successful resolve, maps to NotFound. Then `tickets.UpdateAccessToken(token)`.
4. Load the public messages and public attachments, the product, and the agents for agent-authored messages, in a single `GetByIdsAsync` batch.
5. Build each `CustomerMessageDto`:
   - **Agent author:** `AgentPublicIdentity.Resolve(agent, product.Branding.DisplayName)`. If the agent is unknown, use `$"{product.Branding.DisplayName} Support"`. Check that `Resolve` produces exactly this for a nameless agent, and reuse the same expression.
   - **Requester or System author:** null.
   - `BodyHtml` is `message.Body`.
   - Attachments are grouped by message id.
6. Commit with `TicketMutation.CommitAsync(scope, ct)`. On a commit conflict, the token row raced another use; return the DTO anyway, because the read is valid. On any other commit failure, return NotFound.
7. Return `new CustomerTicketDto(number, subject, status.ToWire(), createdAt, messages)`.

**Controller:**

```csharp
[ApiController]
[Route("api/customer")]
[Authorize(Policy = AuthorizationPolicies.Public)]
[EnableRateLimiting(CustomerRateLimitOptions.TokenAccessPolicyName)]
public sealed class CustomerTicketsController : ControllerBase
{
    [HttpGet("ticket")]
    public async Task<IActionResult> Get(
        [FromHeader(Name = HeaderNames.TicketToken)] string? token,
        [FromServices] IGetCustomerTicketRequestHandler handler, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await handler.HandleAsync(token, cancellationToken)).ToActionResult(this, Ok);
    }
}
```

**`CustomerTestData`** (Api.Tests) seeds through real repositories in a scope:
- a product;
- a requester;
- a ticket that has a customer message, an agent reply with an attachment, and an internal note with an attachment;
- an agent named "Sam Hargreaves".

It issues tokens with the real `IAccessTokenService` and returns their plaintext values:
- a valid token;
- a revoked token;
- an expired token: issue it, then move the clock forward 400 days with the host's `FakeTimeProvider` if `ApiFactory` exposes one; otherwise update `expires_at` by SQL;
- a token for a second ticket of another requester;
- a token whose requester is then erased.

```csharp
internal sealed record CustomerSeed(Guid TicketId, string Number, string ValidToken, string RevokedToken, string ExpiredToken,
    string OtherTicketToken, string ErasedRequesterToken, Guid PublicAttachmentId, Guid InternalAttachmentId, Guid OtherTicketAttachmentId);
internal static class CustomerTestData
{
    public static async Task<CustomerSeed> SeedAsync(ApiFactory factory, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
// GetCustomerTicketRequestHandlerTests
[Fact] public async Task A_valid_token_returns_only_public_messages_with_resolved_agent_names()
[Fact] public async Task The_view_slides_the_token_expiry()
[Fact] public async Task Any_access_failure_is_the_uniform_not_found()
[Fact] public async Task An_unknown_agent_author_shows_the_product_support_name()

// CustomerTicketEndpointTests
[Fact] public async Task The_customer_sees_the_ticket_with_public_messages_and_no_store()
// no internal note text in the body; agent message AuthorDisplayName "Sam from <Product> Support"; Cache-Control no-store.
[Fact] public async Task A_bearer_or_api_key_on_the_request_changes_nothing()

// CustomerUniformNotFoundTests  (Review Focus 1)
[Fact] public async Task Every_failure_mode_returns_the_same_404_bytes()
// GET /api/customer/ticket with: no header, "", "garbage", 200 'x', unknown well-formed token, revoked, expired, erased requester.
// All 404; bodies byte-identical after removing per-request fields the ProblemDetails writer adds (inspect one body: if it has
// "traceId"/"instance", normalise those keys exactly as ApiKeyAuthTests.All_401_responses_are_identical does; if not, compare raw).
// Headers identical apart from Date and correlation/request ids.

// CustomerRateLimitTests
[Fact] public async Task Token_access_is_limited_per_ip()
[Fact] public async Task Bad_customer_rate_limit_options_fail_startup()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter GetCustomerTicketRequestHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the handler, options, policies, controller, `ControllerActions` row and `.env.example` keys:

```
# Customer link routes (D-038): per client IP.
RATELIMITING__CUSTOMER__TOKENACCESSPERMITLIMIT=60
RATELIMITING__CUSTOMER__TOKENACCESSWINDOWSECONDS=60
RATELIMITING__CUSTOMER__LOSTLINKPERMITLIMIT=5
RATELIMITING__CUSTOMER__LOSTLINKWINDOWSECONDS=3600
```

- [ ] **Step 4: Run the tests**

Run Application.Tests, Api.Tests and the Architecture tests.
Expected: PASS. `RoutePolicyCoverageTests` checks that the Public route is rate limited.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(customer): ticket view by link with uniform not-found and per-IP limits" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 9: Customer reply and follow-up tickets

**Files:**
- Create: `src/TechStrap.Application/Tickets/Customer/AddCustomerReplyRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/CustomerReplyForm.cs`
- Modify: `CustomerTicketsController`. Add `Reply`.
- Modify: `ControllerActions.ExpectedSuccess`. Add `["CustomerTicketsController.Reply"] = 201`.
- Create: `tests/TechStrap.Application.Tests/Tickets/Customer/AddCustomerReplyRequestHandlerTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/CustomerReplyIntegrationTests.cs`
- Create: `tests/TechStrap.Api.Tests/Customer/CustomerReplyEndpointTests.cs`

**Interfaces:**
- Consumes:
  - Task 3: `ListRecentFollowUpsAsync`.
  - Task 4: `CustomerAccess`.
  - Task 6: the planner methods.
  - `ITicketNumberAllocator`, `IAttachmentStore`, `IHtmlSanitizer`, `CustomerText.ToHtml`, `IAccessTokenService.Issue` and `PortalLinkOptions.TicketLink`.
  - From Application/Intake: `IntakeErrors.BodyTooLong`, `TooManyFiles` and `MessageTooLarge`, and `IntakeLimits`.
- Produces:

```csharp
public interface IAddCustomerReplyRequestHandler
{
    Task<Result<CustomerReplyResponse>> HandleAsync(
        string? token, AddCustomerReplyRequest request, IReadOnlyList<IncomingAttachment> attachments, CancellationToken cancellationToken);
}
```

The constructor takes:
- `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `ITicketNumberAllocator`, `IAttachmentStore`, `IHtmlSanitizer`, `ITicketNotificationPlanner` and `IUnitOfWork`;
- `TimeProvider`;
- `IOptions<PortalLinkOptions>`;
- `ILogger<AddCustomerReplyRequestHandler>`.

`public static readonly TimeSpan FollowUpDedupeWindow = TimeSpan.FromMinutes(2);` holds the D-037 window.

**Flow.** Implement it exactly. Tests pin each step.

1. **Shape checks** run before any database work: the raw body length, the file count and the total file size, using the `IntakeErrors` codes. An invalid shape returns 400 even for a bad token. That is acceptable, because shape errors reveal nothing about the token.
2. **Render the body:** `html = sanitizer.Sanitize(CustomerText.ToHtml(request.Body ?? ""))`. An empty result is left to the Domain's `body-required`.
3. **Attempt loop.** At most 2 attempts. The second attempt runs only after a `concurrency-conflict` on commit and only when there are no attachments, because the streams may not be re-readable.
   1. Open a unit of work. Resolve access. On failure, return `CustomerErrors.NotFound()`.
   2. Call `token.RecordUse` and `tickets.UpdateAccessToken`.
   3. **If the ticket is not Closed:**
      1. `before = ticket.Status`.
      2. `message = ticket.AddCustomerReply(requester.Id, html, clock)`, converting any failure.
      3. Save the attachments against `ticket.Id`, using the same compensation pattern as `AddAgentReplyRequestHandler`, including `DeleteStoredAsync` with `CancellationToken.None`.
      4. Call `tickets.Update(ticket)`.
      5. `reopened = before is Pending or Solved && ticket.Status == Open`.
      6. `await planner.PlanCustomerReplyAsync(ticket, reopened, ct)`.
      7. Commit. On success, call `stored.Clear()` and return `new CustomerReplyResponse(number, message.Id, false, null)`.
   4. **If the ticket is Closed:**
      1. Dedupe check: `candidates = await tickets.ListRecentFollowUpsAsync(ticket.Id, now - FollowUpDedupeWindow, ct)`, then `match = candidates.FirstOrDefault(c => c.FirstMessageBody == html)`.
      2. If there is a match, replay. Issue a token for `match.TicketId` and `requester.Id`, and add it. Commit. Return `new CustomerReplyResponse(match.Number, match.FirstMessageId, true, portal.TicketLink(plaintext))`. Do not save files, send email or alert anyone. If files were sent, ignore them; the first request stored its own.
      3. Otherwise:
         - `number = await allocator.AllocateAsync(ticket.ProductId, ct)`;
         - `followUp = ticket.CreateFollowUp(number, clock)`;
         - `message = followUp.AddCustomerReply(requester.Id, html, clock)`;
         - save the attachments against `followUp.Id`, with compensation;
         - `tickets.Add(followUp)` and `tickets.Update(ticket)`;
         - issue a token for the follow-up and add it;
         - `await planner.PlanFollowUpConfirmationAsync(followUp, ct)`;
         - `await planner.PlanNewTicketAsync(followUp, requester, isFollowUp: true, ct)`;
         - commit;
         - return `new CustomerReplyResponse(followUp.Number.ToString(), message.Id, true, portal.TicketLink(plaintext))`.
   5. **On commit failure:** delete the stored files.
      - If the failure is `concurrency-conflict` and this is attempt 1, retry. The retry re-resolves, and for a Closed parent it re-runs the dedupe check, so a concurrent duplicate now finds the winner's follow-up and replays it.
      - If the conflict happened with attachments present, try the dedupe check once in a fresh scope. On a match, replay. Otherwise return `CustomerErrors.ReplyConflict()` (409).
      - On any other failure, return that failure. A `reference-violation` maps to NotFound, because the ticket is gone.
   6. Wrap each attempt in `try { ... } catch { delete stored; throw; }`.

**Form and controller.** Copy the `PublicIntakeController` multipart attributes:
- `[Consumes("multipart/form-data")]`
- `[ReadFormBeforeBinding]`
- `[RequestSizeLimit(IntakeRequestLimits.FormBodyBytes)]`
- `[RequestFormLimits(MultipartBodyLengthLimit = IntakeRequestLimits.FormBodyBytes * 2)]`

```csharp
public sealed class CustomerReplyForm
{
    public string? Body { get; set; }
    public List<IFormFile>? Attachments { get; set; }
}

[HttpPost("ticket/replies")]
public async Task<IActionResult> Reply(
    [FromHeader(Name = HeaderNames.TicketToken)] string? token, [FromForm] CustomerReplyForm form,
    [FromServices] IAddCustomerReplyRequestHandler handler, CancellationToken cancellationToken)
// no-store; open streams from form.Attachments ?? [], dispose in finally; 201 on success.
```

- [ ] **Step 1: Write the failing tests**

```csharp
// AddCustomerReplyRequestHandlerTests (substitutes; TicketBuilder; TicketRepositorySubstitute)
[Theory] [InlineData(TicketStatus.Pending)] [InlineData(TicketStatus.Solved)]
public async Task A_reply_on_pending_or_solved_reopens_and_alerts_with_reopened_true(TicketStatus status)
[Theory] [InlineData(TicketStatus.New)] [InlineData(TicketStatus.Open)]
public async Task A_reply_on_new_or_open_keeps_the_status_and_alerts_with_reopened_false(TicketStatus status)
[Fact] public async Task The_reply_body_is_plain_text_escaped_then_sanitised()
[Fact] public async Task A_bad_token_is_the_uniform_not_found()
[Fact] public async Task A_reply_on_a_closed_ticket_creates_a_follow_up_with_a_new_number_and_link()
// planner.PlanFollowUpConfirmationAsync and PlanNewTicketAsync(followUp, requester, true) received; tickets.Add(followUp);
// parent Update; parent unchanged except the FollowUpCreated event; response FollowUpCreated true, link https://help.test/t/...
[Fact] public async Task The_same_text_within_two_minutes_replays_the_existing_follow_up()
// ListRecentFollowUpsAsync returns a candidate with the same sanitised body -> no allocator, no Add, no planner;
// a fresh token is added for the candidate; response carries its number.
[Fact] public async Task Different_text_or_an_older_follow_up_creates_a_new_one()
[Fact] public async Task A_failed_commit_deletes_stored_files_and_a_conflict_retries_once()
[Fact] public async Task Too_many_files_or_an_over_long_body_is_rejected_before_any_lookup()
[Fact] public async Task Cancellation_reaches_the_repositories_store_and_planner()

// CustomerReplyIntegrationTests (real Postgres and storage; Review Focus 2)
[Fact] public async Task A_reply_reopens_a_solved_ticket_and_commits_message_event_and_alert_together()
[Fact] public async Task A_reply_on_a_closed_ticket_commits_the_follow_up_its_message_tokens_and_emails()
// SQL: new ticket with parent_ticket_id = parent, Created and MessageAdded on child, FollowUpCreated on parent,
// one ticket-confirmation row to the requester, one new-ticket-alert row per opted-in agent.
[Fact] public async Task Two_concurrent_replies_on_a_closed_ticket_create_one_follow_up()
// two scopes released together through a rendezvous after the dedupe read (decorate ITicketRepository.ListRecentFollowUpsAsync as the
// PHASE-06a race tests decorate GetByIdAsync); both succeed; one child ticket; both responses carry the same follow-up number.

// CustomerReplyEndpointTests
[Fact] public async Task A_customer_replies_with_a_png_and_gets_201()
[Fact] public async Task A_reply_on_a_closed_ticket_returns_the_follow_up_link()
[Fact] public async Task An_executable_attachment_is_400()
[Fact] public async Task A_bad_token_is_404_with_the_uniform_body()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter AddCustomerReplyRequestHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the handler, the form and the action.

- [ ] **Step 4: Run the tests**

Run:
- Application.Tests;
- `--filter CustomerReplyIntegrationTests` five times;
- Api.Tests;
- Architecture.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(customer): customer replies with reopen, alerts and deduplicated follow-ups" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 10: Customer attachment download

**Files:**
- Create: `src/TechStrap.Application/Tickets/Customer/GetCustomerAttachmentRequestHandler.cs`
- Modify: `CustomerTicketsController`. Add `GetAttachment`.
- Create: `src/TechStrap.Api/Startup/AttachmentSandbox.cs`. Move the CSP middleware out of `Program.cs` into `app.UseAttachmentSandbox()`, matching both `/api/attachments` and `/api/customer/attachments`. Keep the "append to the existing policy" behaviour.
- Modify: `src/TechStrap.Api/Program.cs`. Call the extension in the same position, before `UseSecurityHeaders`.
- Modify: `ControllerActions.ExpectedSuccess`. Add `["CustomerTicketsController.GetAttachment"] = 200`.
- Create: `tests/TechStrap.Application.Tests/Tickets/Customer/GetCustomerAttachmentRequestHandlerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Customer/CustomerAttachmentEndpointTests.cs`

**Interfaces:**

```csharp
public interface IGetCustomerAttachmentRequestHandler
{
    Task<Result<AttachmentContent>> HandleAsync(string? token, Guid attachmentId, CancellationToken cancellationToken);
}
```

The constructor takes `IAccessTokenService`, `ITicketRepository`, `IRequesterRepository`, `IAttachmentStore`, `TimeProvider` and `ILogger<GetCustomerAttachmentRequestHandler>`.

**Rules:**
1. Resolve the token with `CustomerAccess`. This is a read, so there is no unit of work and no slide.
2. Look the attachment up with `tickets.GetAttachmentAsync(context.Ticket.Id, attachmentId, publicOnly: true)`. A null result is the uniform NotFound. That covers another ticket's attachment and an attachment on an internal note.
3. Open the file with `OpenReadAsync`. If it returns null, log a warning with the attachment id only, and return the uniform NotFound.
4. Otherwise return the `AttachmentContent`.

Its signature takes the token before the attachment id; the token header is the first action parameter, by convention.

**Controller:**

```csharp
[HttpGet("attachments/{id:guid}")]
public async Task<IActionResult> GetAttachment(
    [FromHeader(Name = HeaderNames.TicketToken)] string? token, Guid id,
    [FromServices] IGetCustomerAttachmentRequestHandler handler, CancellationToken cancellationToken) =>
    (await handler.HandleAsync(token, id, cancellationToken)).ToActionResult(this, content =>
    {
        Response.RegisterForDisposeAsync(content.Content);
        return new AttachmentDownloadResult(content);
    });
```

For 404s, set `no-store` before the call. `AttachmentDownloadResult` sets it on success.

- [ ] **Step 1: Write the failing tests**

```csharp
// GetCustomerAttachmentRequestHandlerTests
[Fact] public async Task A_public_attachment_of_the_tokens_ticket_streams()
[Fact] public async Task Another_tickets_or_an_internal_note_attachment_is_the_uniform_not_found()
[Fact] public async Task A_missing_file_is_the_uniform_not_found_and_logged()
[Fact] public async Task A_bad_token_is_the_uniform_not_found_without_an_attachment_lookup()

// CustomerAttachmentEndpointTests (Review Focus 1, second half)
[Fact] public async Task The_customer_downloads_a_public_attachment_with_safe_headers_and_sandbox()
// 200, bytes equal, Content-Disposition attachment, nosniff, private no-store, CSP contains sandbox and frame-ancestors 'none'.
[Fact] public async Task Other_ticket_and_internal_note_attachments_look_like_missing_ones()
// internal-note attachment id, other ticket's attachment id and a random id with a valid token, plus a valid id with a bad token:
// all 404 with byte-identical bodies (same normalisation as Task 8).
[Fact] public async Task The_agent_download_still_has_sandbox_after_the_middleware_move()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter GetCustomerAttachmentRequestHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the handler, the action, the extension (moved verbatim, plus the second prefix) and the `ControllerActions` row.

- [ ] **Step 4: Run the tests**

Run Application.Tests, Api.Tests (including the existing `AttachmentDownloadEndpointTests`) and the Architecture tests.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(customer): public attachment download by link with sandboxed headers" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 11: Lost link

**Files:**
- Create: `src/TechStrap.Application/Tickets/Customer/RequestNewAccessLinkRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/CustomerAccessLinkController.cs`
- Modify: `ControllerActions.ExpectedSuccess`. Add `["CustomerAccessLinkController.RequestLink"] = 202`.
- Create: `tests/TechStrap.Application.Tests/Tickets/Customer/RequestNewAccessLinkRequestHandlerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Customer/LostLinkUniformityTests.cs`
- Modify: `CustomerRateLimitTests`. Add the lost-link limit test.

**Interfaces:**

```csharp
public interface IRequestNewAccessLinkRequestHandler
{
    /// <summary>Success (no value) for every well-formed address, known or not (D-038); Validation only for a malformed address.</summary>
    Task<Result> HandleAsync(RequestNewAccessLinkRequest request, CancellationToken cancellationToken);
}
```

The constructor takes `IRequesterRepository`, `ITicketRepository`, `IEmailOutboxStore`, `ITicketNotificationPlanner`, `IUnitOfWork`, `TimeProvider`, `IOptions<LostLinkOptions>` and `ILogger<RequestNewAccessLinkRequestHandler>`.

**Flow.** Every well-formed address runs the same steps until the last branch, so the two paths cost about the same.

1. If `CustomerEmailAddress.TryNormalize(request.Email, out var email)` fails, return `CustomerErrors.EmailInvalid()` (400).
2. `recent = await outboxStore.CountRecentAsync(EmailTemplates.AccessLinks, email, now - window, ct)`.
3. `requester = await requesters.GetByEmailAsync(email, ct)`.
4. If `recent >= PerAddressLimit`, or the requester is null or erased, return `Result.Success()`. Log at Information "Access-link request ignored" with no address.
5. Otherwise:
   1. `links = await tickets.ListRecentTicketsForRequesterAsync(requester.Id, MaxLinks, ct)`. If it is empty, return Success.
   2. Open a unit of work, then `await planner.PlanAccessLinksAsync(requester, links, ct)`, then commit.
   3. A commit failure is logged by code and still returns Success. The answer never changes.
6. Return Success.

Never log the email address.

**Controller:**

```csharp
[ApiController]
[Route("api/customer/access-link")]
[Authorize(Policy = AuthorizationPolicies.Public)]
[EnableRateLimiting(CustomerRateLimitOptions.LostLinkPolicyName)]
public sealed class CustomerAccessLinkController : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> RequestLink(
        [FromBody] RequestNewAccessLinkRequest request, [FromServices] IRequestNewAccessLinkRequestHandler handler, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await handler.HandleAsync(request, cancellationToken)).ToActionResult(this, Accepted);
    }
}
```

The action is named `RequestLink` because `Request` would hide `ControllerBase.Request`. A success must produce an empty 202. Check what `ToActionResult(this, Accepted)` emits for a plain `Result`, and use `() => Accepted()` if the overload needs it.

- [ ] **Step 1: Write the failing tests**

```csharp
// RequestNewAccessLinkRequestHandlerTests
[Fact] public async Task A_known_requester_gets_one_access_links_email()
[Fact] public async Task An_unknown_or_erased_address_returns_success_and_plans_nothing()
[Fact] public async Task Over_the_per_address_cap_nothing_is_sent_and_the_answer_is_the_same()
// CountRecentAsync returns 3 -> Success, planner not received, unitOfWork not begun.
[Fact] public async Task A_malformed_address_is_400_email_invalid()
[Fact] public async Task The_count_and_lookup_run_for_every_well_formed_address()
// for unknown and known addresses both CountRecentAsync and GetByEmailAsync are received once (same work up to the branch).
[Fact] public async Task A_failed_commit_still_returns_success()

// LostLinkUniformityTests (Api; Review Focus 3)
[Fact] public async Task Known_and_unknown_addresses_are_indistinguishable()
// POST for a known and an unknown address: both 202, identical (empty) bodies and identical headers (apart from Date/ids).
// Timing: run each 10 times after a warm-up; assert the medians differ by less than 250 ms (coarse; documents intent, not a side channel proof).
[Fact] public async Task The_email_goes_only_to_the_requesters_own_address()
// a known request queues one access-links row whose to_address is that requester's email; no other rows.

// CustomerRateLimitTests
[Fact] public async Task Lost_link_requests_are_limited_per_ip()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter RequestNewAccessLinkRequestHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the handler, the controller and the `ControllerActions` row.

- [ ] **Step 4: Run the tests**

Run Application.Tests, Api.Tests and the Architecture tests.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(customer): lost-link requests with uniform answers and a per-address cap" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 12: Auto-close

**Files:**
- Create: `src/TechStrap.Application/Tickets/AutoClose/AutoCloseSolvedTicketsHandler.cs`
- Create: `src/TechStrap.Infrastructure/AutoClose/AutoCloseServiceCollectionExtensions.cs`. It provides `AddTechStrapAutoClose(services, configuration)`, which registers the options (shared helper from Task 5) and `TryAddScoped<IAutoCloseSolvedTicketsHandler, AutoCloseSolvedTicketsHandler>()`.
- Create: `src/TechStrap.Worker/AutoClose/AutoCloseWorker.cs`
- Modify: `src/TechStrap.Worker/Program.cs`. Add `AddTechStrapAutoClose` and `AddHostedService<AutoCloseWorker>()`.
- Modify: `src/TechStrap.Api/Startup/ApplicationHandlerRegistration.cs`. Add `nameof(IAutoCloseSolvedTicketsHandler)` to `_workerOnly`.
- Modify: `tests/TechStrap.Api.Tests/HostFactory.cs`. `WorkerFactory` defaults `AutoClose:Enabled=false`, as it already does for EmailOutbox.
- Create: `tests/TechStrap.Application.Tests/Tickets/AutoCloseSolvedTicketsHandlerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Worker/AutoCloseWorkerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Worker/AutoCloseWorkerBoundaryTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/AutoCloseIntegrationTests.cs`

**Interfaces:**

```csharp
public sealed record AutoCloseResult(int Examined, int Closed, int Conflicts);
public interface IAutoCloseSolvedTicketsHandler
{
    Task<Result<AutoCloseResult>> HandleAsync(CancellationToken cancellationToken);
}
```

The constructor takes `ITicketRepository`, `IUnitOfWork`, `TimeProvider`, `IOptions<AutoCloseOptions>` and `ILogger<AutoCloseSolvedTicketsHandler>`.

**Handler rules:**
- `cutoff = now - TimeSpan.FromDays(Days)`.
- Read `candidates = ListSolvedBeforeAsync(cutoff, BatchSize)` in a short read scope. Read only the ids, numbers and versions you need. If the repository returns tracked entities, resolve them per ticket in step 2.
- For each candidate id, use one unit of work:
  1. `await using var scope = ...`.
  2. `ticket = await tickets.GetByIdAsync(id)`.
  3. Skip the ticket if it is null, no longer Solved, `IsSpam`, or `SolvedAt >= cutoff`.
  4. `ticket.ChangeStatus(TicketStatus.Closed, Actor.System, clock)`, then `tickets.Update(ticket)`, then `CommitAsync`.
     - Success counts as Closed.
     - A `concurrency-conflict` counts as Conflicts and is logged at Information. The next run re-evaluates the ticket.
- No planner and no email (D-037).
- Return the counts.

`ListSolvedBeforeAsync` returns tracked tickets in whatever scope it runs in. Simplest correct shape: call it once at the start without a unit of work, then reload each ticket inside its own unit of work. Reloading is cheap at batch sizes of 50.

**Worker.** Copy `EmailOutboxWorker`:
- The constructor takes exactly `(IServiceScopeFactory, IOptions<AutoCloseOptions>, TimeProvider, ILogger<AutoCloseWorker>)`.
- Return immediately when `!Enabled`.
- Each iteration creates a fresh scope and resolves `IAutoCloseSolvedTicketsHandler`.
- If `Closed + Conflicts == BatchSize`, run again immediately. Otherwise wait `IntervalMinutes` with `Task.Delay(…, clock, stoppingToken)`.
- Exceptions are logged and followed by the wait.
- On stopping, break out of the loop.

`AutoCloseWorkerBoundaryTests` copies `EmailOutboxWorkerBoundaryTests` for the new worker. It checks the exact constructor parameter set, and that no constructor parameter or field is a repository, store or DbContext.

- [ ] **Step 1: Write the failing tests**

```csharp
// AutoCloseSolvedTicketsHandlerTests (substitutes)
[Fact] public async Task Tickets_solved_at_least_n_days_ago_are_closed_by_the_system()
// StatusChanged Solved->Closed with ActorType System; ClosedAt set; planner never involved (no planner dependency at all).
[Fact] public async Task Spam_reopened_or_recently_solved_candidates_are_skipped()
[Fact] public async Task A_concurrency_conflict_on_one_ticket_does_not_stop_the_others()
[Fact] public async Task Running_twice_is_a_no_op_the_second_time()

// AutoCloseWorkerTests (FakeTimeProvider; SignallingTimeProvider pattern from EmailOutboxWorkerTests)
[Fact] public async Task It_runs_again_immediately_when_the_batch_was_full_and_waits_otherwise()
[Fact] public async Task It_passes_the_stopping_token_and_stops_promptly()
[Fact] public async Task A_disabled_worker_never_runs()

// AutoCloseIntegrationTests (real Postgres; fake clock; Review Focus 5)
[Fact] public async Task A_seeded_solved_ticket_closes_after_n_days_and_no_email_is_queued()
[Fact] public async Task Spam_does_not_starve_the_batch_and_a_racing_reply_is_never_lost()
// seed BatchSize+5 Solved spam tickets older than N days and 2 real ones: one run closes both real ones.
// Race: rendezvous the handler's per-ticket GetByIdAsync and a customer reply's load on the same ticket (decorator as in PHASE-06a);
// afterwards either the ticket is Open with the reply (handler counted a conflict) or Closed with the reply present as a follow-up — never a lost message.
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter AutoCloseSolvedTicketsHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the handler, the registration, the Worker loop, the `_workerOnly` entry and the `WorkerFactory` default. Update the Worker `Program.cs` comment to cover both loops.

- [ ] **Step 4: Run the tests**

Run:
- Application.Tests;
- Infrastructure (filter `AutoCloseIntegrationTests`, three times);
- Api.Tests, including the worker, boundary, host smoke and `ApplicationHandlerRegistrationTests`;
- Architecture.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(worker): auto-close solved tickets after the configured days" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 13: End-to-end lifecycle, leak checks and closing docs

**Files:**
- Modify: `tests/TechStrap.Api.Tests/Tickets/TicketLifecycleEndToEndTests.cs`. Add a customer-side test.
- Modify: `tests/TechStrap.Api.Tests/Intake/SensitiveDataLeakTests.cs`
- Modify: `docs/development/TICKET-OPERATIONS.md`. Add a "Customer access" section: routes, the uniform 404, the lost link, follow-ups, rate limits and auto-close.
- Modify: `docs/architecture/PHASE-06-ticket-operations.md`. Tick the 06b items, using "06c remaining" notes where a task is only partly delivered.
- Modify: the roadmap and index rows.

**Interfaces:** none.

- [ ] **Step 1: Write the tests**

```csharp
// TicketLifecycleEndToEndTests
[Fact] public async Task A_customer_follows_a_ticket_from_link_to_follow_up()
// 1. submit via public form; read the ticket-confirmation outbox payload's portalLink -> token.
// 2. GET /api/customer/ticket with the token: 1 message.
// 3. agent replies (Markdown) -> customer GET shows the agent message with "Sam from ... Support".
// 4. customer replies -> ticket Open, customer-reply-alert queued to the assignee (assign first) .
// 5. agent sets Solved; run the auto-close handler with the clock advanced past N days -> Closed (resolve IAutoCloseSolvedTicketsHandler
//    from a Worker-style registration in the test, or call the handler through Infrastructure registration; say which).
// 6. customer replies on Closed -> 201 with FollowUpCreated and a link; the follow-up's GET shows the reply; parent unchanged.
// 7. POST /api/customer/access-link for the requester -> 202 and one access-links row.

// SensitiveDataLeakTests (Review Focus 4)
[Fact] public async Task The_customer_view_and_customer_emails_carry_no_internal_or_agent_private_data()
// seed: internal note "INTERNAL-CANARY", agent "Sam Hargreaves" <sam.private@example.com>, a tag "TAG-CANARY", another requester.
// The serialized CustomerTicketDto contains none of: INTERNAL-CANARY, Hargreaves, sam.private@example.com, TAG-CANARY, the other
// requester's email, "lastActivityAt", "rowVersion". Every access-links / ticket-confirmation row for the follow-up goes to the
// requester's own address only. Plaintext customer tokens appear only in email_outbox.payload and in the follow-up response.
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "TicketLifecycleEndToEndTests|SensitiveDataLeakTests"`
Expected: PASS. If a test fails, fix the product code, not the test, and record each fix.

- [ ] **Step 3: Close the docs**

Write the TICKET-OPERATIONS section and the PHASE-06 ticks, each citing its test class. Update the roadmap so PHASE-06 reads "06a complete; 06b implemented (pending merge); 06c not started".

- [ ] **Step 4: Run every check**

Run:
- the Release build (0 warnings);
- `dotnet test --solution TechStrap.CI.slnf -c Release`;
- the script tests;
- the package check;
- the EF pending-model check.

All must pass.

- [ ] **Step 5: Commit**

```bash
git add tests docs
git commit -m "test(customer): end-to-end customer lifecycle, leak checks and PHASE-06b docs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```
