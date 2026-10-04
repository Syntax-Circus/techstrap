# PHASE-06c Delete, Erase, Dead Letters and Log Redaction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close PHASE-06 with the privacy and operations features:
- an Admin can hard-delete a ticket;
- an Admin can erase a requester's personal data;
- an Admin can list, retry and discard dead-lettered emails;
- the Worker sweeps old finished outbox rows;
- logs carry no requester email, token or hash.

It also clears the hardening items carried forward from the 06b review.

**Architecture:**
- **Delete and erase** are Admin-only handlers in the Api, each running in one unit of work that writes an `AdminEvent`. Stored files are deleted best-effort after the commit.
  - Delete removes the tracked ticket and lets the database cascade remove the rest. Follow-ups are unlinked by a `SET NULL` foreign key.
  - Erase uses a dedicated persistence port (`IRequesterErasure`). It bulk-updates customer messages and subjects, revokes tokens and deletes outbox rows inside the caller's transaction. It lives in a file that never touches the append-only event tables.
- **Dead letters** reuse the existing `EmailOutboxItem.Retry` and `Discard`, behind a masked DTO.
- **Retention** is a Worker-only loop shaped like `AutoCloseWorker`.
- **Log redaction** is a Serilog enricher in Infrastructure, applied by the Api and Worker hosts. It rewrites property values before any sink sees them.

**Tech Stack:** .NET 10, ASP.NET Core controllers, EF Core with Npgsql, Serilog, `SyntaxCircus.Storage`, xUnit v3, Shouldly, NSubstitute and Testcontainers.

**Spec:**
- `docs/architecture/PHASE-06-ticket-operations.md`. The relevant parts are P06-T10, T11, T16, T19 and T20, the Privacy and Dead letters bullets, and the carry-forwards.
- `docs/architecture/02-ARCHITECTURE.md`, section 7.3 (routes) and the erase and delete flow paragraph.
- D-006, D-022, D-033, D-037 and D-038.
- The owner decisions of 2026-10-03, recorded as D-039 in Task 1.

### Owner decisions (2026-10-03), recorded as D-039 in Task 1
- **Erase also replaces the subject** of each of the requester's tickets with the erasure marker. The ticket's customer-supplied `metadata` and `custom_fields` are cleared too.
- **Erase replaces only requester-authored message bodies.** Agent replies and internal notes stay.
- **Erase deletes every `email_outbox` row** addressed to the requester, in any status including Sent. It also deletes every row whose `ticket_id` is one of the requester's tickets, because agent alerts carry the requester's label.
- **The outbox gets an index on (`kind`, `to_address`, `created_at`)** and a Worker retention sweep. The sweep deletes Sent and Discarded rows older than N days (default 90). This supersedes D-033's PHASE-12 sweep and narrows D-006's "retention deferred", for the outbox only.
- **Follow-ups survive a hard delete of their parent.** They lose the link (`parent_ticket_id` becomes NULL), while their own `Created` events keep the original parent id.

### Technical decisions this plan makes (recorded as D-039 in Task 1; the owner confirms at plan review)
- **Schema.** One migration: `tickets.parent_ticket_id` becomes `ON DELETE SET NULL`, plus the outbox index.
- **Delete ticket: `DELETE /api/tickets/{id}`** (Admin, 204 or 404). No `RowVersion` is needed.
  - It removes outbox rows for the ticket in every status, not only pending ones. A hard delete removes all of the ticket's data.
  - The `AdminEvent` payload holds the ticket number and counts only.
- **Erase requester: `POST /api/requesters/{id}/erase`** (Admin, 204 or 404). It is idempotent: a re-run repeats the cleanup and writes another `AdminEvent`.
  - The marker is a Domain constant, `ErasureMarker.Text = "[erased]"`.
  - Tokens are revoked in bulk. The handler does not need `IAccessTokenService`, so this deviates from the spec's dependency list.
  - "Requester-authored" means messages with `AuthorType.Requester`.
  - The audit payload records the number of revoked tokens under the key `links`. The `AdminEvent` guard refuses any key containing "token".
- **Dead letters.**
  - `GET /api/dead-letters` returns a paged `DeadLetterDto` with the recipient masked (`a***@example.com`). The payload is never exposed.
  - Retry and discard return 204, or 404 for an unknown id, or 409 `outbox-not-dead-lettered` when the row is not dead-lettered. Each writes an `AdminEvent`.
- **Retention options:** `OutboxRetentionOptions` with `Enabled`, `Days` (default 90), `IntervalMinutes` and `BatchSize`. It measures age from `created_at` and never touches DeadLettered, Pending or Sending rows.
- **Log redaction:** `PiiRedactionEnricher` replaces emails with `[email]`, 43-character base64url tokens with `[token]` and `sha256:` hashes with `[hash]`, in every property value, recursively.
  - Exception text relies on two defaults, both pinned by a guard test: Npgsql hides error detail unless `Include Error Detail` is set, and EF sensitive-data logging is off.
  - The Admin and Portal hosts are not wired, because they hold no requester data yet. They follow in PHASE-07 and PHASE-09.
- **Multipart routes.**
  - A truncated multipart body answers 400 instead of 500.
  - A non-multipart body answers 415 instead of the fallback 401. The `[Consumes]` check moves into the shared `ReadFormBeforeBinding` filter, so each route's own policy runs first.
  - OpenAPI keeps documenting the multipart bodies.
- **Follow-up dedupe** compares sanitised incoming file names, through a Domain `AttachmentFileName.Sanitize` that `AttachmentStore` also uses.
- **The Portal Sentry header scrub is deferred to PHASE-09.** The Portal proxies no tokens yet, and it has no shared home for the scrubber without a new project.

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
  - Admin handlers re-check the actor with `CurrentAgent.RequireActiveAsync`.
- **Controllers.**
  - A class-level `[Authorize(Policy = ...)]`. An action-level Admin override is allowed on an Agent controller.
  - No constructor dependencies.
  - Each action takes exactly one `[FromServices]` handler interface and a `CancellationToken`.
  - Responses use `ToActionResult`.
  - Each action gets a row in `ControllerActions.ExpectedSuccess`.
- **Results.**
  - Return `new ResultError(code, message, kind, target?)`, where `target` is only for `Validation`.
  - Validation maps to 400, never 422.
- **Persistence.**
  - Write inside `await using var scope = await unitOfWork.BeginAsync(ct)` with one `CommitAsync`.
  - Bulk `ExecuteUpdate` and `ExecuteDelete` are allowed only in Infrastructure files that never mention `TicketEventRecord`, `AdminEventRecord`, `ticket_events` or `admin_events` (`AppendOnlyBypassRules`).
  - Generate migrations only with `dotnet ef`. Never hand-edit a migration or the snapshot. This plan expects exactly ONE migration (Task 2).
- **Audit.**
  - `AdminAudit.Record` payloads hold ids, numbers, kinds and counts only.
  - The payload guard refuses names such as `email`, `name`, `subject`, `body`, `token` and `address`.
- **Files.** Collect storage keys before the commit. Delete files only after a successful commit, best-effort, with `CancellationToken.None`, logging the storage key and the exception type name only.
- **Secrets and PII.**
  - Never log an email address, a requester name, a token, a token hash or an outbox payload.
  - Never expose an outbox payload or a raw recipient address through the API.
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
  - `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api` is clean.

## Review Focus

1. **Erase leaves personal data somewhere.** The places to check:
   - a subject;
   - a customer message;
   - an attachment file or row;
   - an outbox row, found either by address or by ticket;
   - a still-valid token;
   - the search index.

   After an erase, no text or jsonb column holds the requester's email, name or a body canary, apart from the tombstone itself. Pinned in Task 5 by `EraseRequesterIntegrationTests.Nothing_personal_remains_after_erase`.
2. **Hard delete leaves a dangling row or an orphan file, or fails on a ticket with follow-ups, events, tokens or tags.** Everything is gone and the follow-up survives, unlinked. Pinned in Task 4 by `DeleteTicketIntegrationTests.A_ticket_with_everything_attached_is_gone_and_its_follow_up_survives`.
3. **A plain agent or a deactivated admin deletes, erases or acts on a dead letter.** Every new route is Admin-only, and the handlers refuse an inactive actor. Pinned by `AgentAccessCoverageTests`, whose admin-only route count rises by 5, and by the inactive-actor handler tests in Tasks 4–6.
4. **Retention deletes a row it must keep:** a DeadLettered, Pending or Sending row, or a Sent row younger than N days. Pinned in Task 7 by `PurgeEmailOutboxIntegrationTests.Only_finished_rows_older_than_the_window_are_deleted`.
5. **A requester email, token or hash reaches a log line,** including through a nested property or an exception. Pinned in Task 8 by `LogRedactionTests.Nested_and_exception_paths_carry_no_email_or_token` and the host-level flow test.

---

### Task 1: Record the decisions and align the docs

**Files:**
- Modify: `docs/architecture/04-DECISION-LOG.md`. Add the approval-basis bullet, the index row D-039, its section, and one-line supersession notes under D-006 and D-033.
- Modify: `docs/architecture/PHASE-06-ticket-operations.md`
- Modify: `docs/architecture/02-ARCHITECTURE.md`
- Modify: `docs/architecture/01-REQUIREMENTS.md`
- Modify: `docs/architecture/PHASE-07-admin-app.md`, `docs/architecture/PHASE-09-public-portal.md` and `docs/architecture/PHASE-12-release-hardening.md` (carry-forward lines only)
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`
- Modify: `docs/architecture/00-DISCOVERY-INDEX.md`

**Interfaces:** none (docs only).

- [ ] **Step 1: Add D-039**

Follow the D-037 and D-038 format (Status, Date, Owner, Related artifacts, Context, Decision, Alternatives Considered, Consequences, Approval).

- Add this bullet to the approval-basis list after the D-037/D-038 bullet: "**Owner decision (2026-10-03, PHASE-06c planning):** D-039, the owner decisions on erase scope, follow-ups of a deleted ticket and outbox retention. Its technical decisions were proposed in the PHASE-06c plan and approved when the owner approved the plan."
- Add this index row after D-038:

```
| D-039 | PHASE-06c: erase covers subject, metadata and outbox rows; hard delete unlinks follow-ups; outbox retention sweep and lookup index; Serilog redaction enricher; multipart hardening | Approved (owner 2026-10-03; technical decisions at PHASE-06c plan review) | 2026-10-03 | PHASE-06, PHASE-07, PHASE-09, PHASE-12 |
```

- Append this section after D-038 (before any following `---` rule, keeping the log's `---` separators):

```markdown
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

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-06c planning)
- **Approved on:** 2026-10-03
```

- Under D-006 "Consequences" add the line: "- Amended in part by D-039: erase also covers the subject, metadata and outbox rows, and outbox retention is no longer deferred."
- Under D-033 "Consequences" replace "PHASE-12 adds a retention sweep for sent rows (proposed: 30 days), with a payload scrub." with "The retention sweep for sent rows moved to PHASE-06c and deletes the rows after 90 days (D-039); there is no payload scrub."

- [ ] **Step 2: Align the spec docs**

1. **`PHASE-06-ticket-operations.md`**
   - Line 27 (the Privacy bullet): replace the whole bullet with: "Privacy: `MarkTicketSpamRequestHandler` sets or clears the flag (06a); `DeleteTicketRequestHandler` hard-deletes the ticket (messages, events, tokens, tags and attachment rows cascade in the database), deletes its attachment files after the commit and all `email_outbox` rows of the ticket, and unlinks surviving follow-ups (`parent_ticket_id` becomes NULL, D-039); `EraseRequesterRequestHandler` replaces the requester's own message bodies and the ticket subjects with `ErasureMarker.Text`, clears ticket metadata and custom fields, deletes the requester's attachments (files and rows), revokes their tokens, deletes every `email_outbox` row addressed to them or belonging to their tickets, tombstones the requester, and writes an `AdminEvent` with counts only; agent replies and notes stay. Both write an `AdminEvent` in the same transaction. Serilog PII redaction is `PiiRedactionEnricher` (P06-T19). Automatic retention is out of scope except the email outbox: the Worker deletes `Sent` and `Discarded` rows older than 90 days (D-039, P06-T22)."
   - Boundary table, `DELETE /api/tickets/{id}` row dependencies: `ITicketRepository, IAttachmentStore, IEmailOutboxStore, IAdminEventRepository, IAgentRepository, ICurrentAgentClaims, IUnitOfWork, TimeProvider, ILogger`.
   - Boundary table, erase row dependencies: `IRequesterRepository, IRequesterErasure, IAttachmentStore, IAdminEventRepository, IAgentRepository, ICurrentAgentClaims, IUnitOfWork, TimeProvider, ILogger`; infrastructure column: `EF repositories, RequesterErasure, AttachmentStore`.
   - Discard row outcome: `204; 404; 409 not dead-lettered`. Add one row after the dead-letter rows: "Worker scheduled loop (`OutboxRetentionWorker` hosted service) | `PurgeEmailOutboxHandler` | `IEmailOutboxStore`, `TimeProvider`, `IOptions<OutboxRetentionOptions>`, `ILogger` | outbox store | deleted count logged (D-039)".
   - P06-T10 and P06-T11 validation: add "(subject, metadata, outbox rows by address and by ticket, D-039)" to T11 and "follow-ups survive unlinked" to T10.
   - T19: "(06c or PHASE-09)" for the Portal scrub becomes "Portal scrub deferred to PHASE-09 (D-039)".
   - Add task "**P06-T22** `OutboxRetentionOptions`, `PurgeEmailOutboxHandler` and `OutboxRetentionWorker`: the Worker deletes `Sent` and `Discarded` outbox rows older than N days (D-039). **Validation:** `PurgeEmailOutboxIntegrationTests` (dead letters and unfinished rows are never deleted)". Check the existing last task id first (`grep -n "P06-T2" ...`) and use the next free number if T22 is taken.
   - Carry-forward lines (the `(06c)` ones): do not tick them here (Task 11 does). Change the Portal Sentry line's "(06c or PHASE-09)" to "(PHASE-09, D-039)", and the outbox line to "an `email_outbox` index on (`kind`, `to_address`, `created_at`) and outbox retention (06c, D-039)".
2. **`02-ARCHITECTURE.md`**
   - Section 6.8 (line 216): replace the first sentence with: "`EraseRequesterRequestHandler` (Admin): in one transaction anonymize the requester (`email` to `erased-{id}@invalid`, `name` cleared, `erased_at`), replace the bodies of their own messages and the subjects of their tickets with the erasure marker `[erased]`, clear ticket metadata and custom fields, delete their attachments (rows in the transaction, files after the commit), revoke their tokens, delete every `email_outbox` row addressed to them or belonging to their tickets, and write an `AdminEvent` with counts only (D-006, D-039)." Replace "(hard delete of ticket, messages, events, attachments and pending outbox rows)" with "(hard delete of the ticket, with its messages, events, tokens and attachments; its outbox rows; surviving follow-ups are unlinked)". Add one sentence: "The Worker also deletes `Sent` and `Discarded` outbox rows older than 90 days (D-039)."
   - Section 7.3: the delete and erase rows get the dependency lists above; add the Worker row for `OutboxRetentionWorker`; the discard row outcome gets `409`.
   - Section 11.3 (backups) needs no change.
3. **`01-REQUIREMENTS.md`**
   - FR-PRIV-01: "...anonymize requester identity, their own messages and their ticket subjects, delete their attachments and the outbox rows about them, revoke their tokens; tickets and events remain for statistics".
   - Line 47: "automatic retention rules deferred (D-006), except the email outbox (90 days, D-039)". Line 110 and line 294 likewise.
4. **Carry-forward lines**
   - `PHASE-12-release-hardening.md` line 210: replace with "Carried forward from PHASE-05 (D-033): done in PHASE-06c (D-039): the Worker deletes sent outbox rows after 90 days. Only the backup-retention statement remains here."
   - `PHASE-09-public-portal.md`, in its carry-forward list: "- [ ] Carried forward from PHASE-06c (D-039): add the Sentry header scrub for `X-Ticket-Token`, `X-Api-Key`, `Authorization` and `Cookie` to the Portal (the Api has it), and wire `PiiRedactionEnricher` into the Portal's `AddStandardSerilog` call once the Portal handles requester data."
   - `PHASE-07-admin-app.md`, in its carry-forward list (grep `Carried forward`; add one if none): "- [ ] Carried forward from PHASE-06c (D-039): wire `PiiRedactionEnricher` into the Admin host's `AddStandardSerilog` call once the Admin handles requester data."
5. **Roadmap and index:** the PHASE-06 row becomes "06a complete; 06b complete; 06c in progress" (keep whatever 06b wording the row has after its merge, and use the same wording style), and its decisions column gains D-039. Do the same in `00-DISCOVERY-INDEX.md`.

- [ ] **Step 3: Check and commit**

Run `pwsh -File scripts/Invoke-ScriptTests.ps1` (expect PASS). Then run these greps; the first two should return no hits outside quoted history, and the third should show only the new wording:

```bash
grep -n "Automatic retention is out of scope\|retention sweep for sent\|PHASE-12 adds a retention sweep" docs/architecture/*.md
grep -n "D-039" docs/architecture/*.md
```

```bash
git diff --cached --stat   # after git add, before committing
git add docs
git commit -m "docs: record PHASE-06c decisions (D-039) and align specs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 2: Schema migration: unlink follow-ups on delete, and the outbox lookup index

**Files:**
- Modify: `src/TechStrap.Infrastructure/Persistence/Configurations/TicketRecordConfiguration.cs` (line 28: the `ParentTicketId` foreign key)
- Modify: `src/TechStrap.Infrastructure/Persistence/Configurations/OperationsRecordConfigurations.cs` (`EmailOutboxRecordConfiguration`)
- Generated: `src/TechStrap.Infrastructure/Migrations/<timestamp>_Phase06cDeleteAndOutboxIndexes.cs`, its `.Designer.cs`, and the updated `TechStrapDbContextModelSnapshot.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/Phase06cSchemaTests.cs`

**Interfaces:** none new. `IEmailOutboxStore.CountRecentAsync` keeps its signature and result; the index only serves it.

**Rules:**
1. **Never hand-edit** the migration, its designer, or the snapshot. They are produced only by `dotnet ef`. If the generated `Up` contains anything beyond the three statements below, stop and fix the model, then remove the migration with `dotnet ef migrations remove` and generate again.
2. Expected `Up`: `DropForeignKey("fk_tickets_tickets_parent_ticket_id", "tickets")`, `AddForeignKey(same name, onDelete: ReferentialAction.SetNull)`, and `CreateIndex("ix_email_outbox_kind_to_address_created_at", "email_outbox", columns: ["kind", "to_address", "created_at"])`. `Down` reverses them.
3. The foreign key keeps its generated name. The index name follows the repo's `ix_{table}_{cols}` convention, so the snake-case schema tests stay green.

- [ ] **Step 1: Write the failing tests**

`Phase06cSchemaTests` (a `PostgresIntegrationTestBase`; the first two tests need no database, as in `TicketSchemaTests`):

```csharp
public sealed class Phase06cSchemaTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void A_deleted_parent_ticket_unlinks_its_follow_ups()
    {
        var foreignKey = ModelInspector.Table("tickets").GetForeignKeys()
            .Single(fk => fk.Properties.Single().GetColumnName() == "parent_ticket_id");

        foreignKey.DeleteBehavior.ShouldBe(DeleteBehavior.SetNull);
    }

    [Fact]
    public void The_outbox_has_a_kind_address_created_at_index()
    {
        var index = ModelInspector.Table("email_outbox").GetIndexes()
            .Single(i => i.GetDatabaseName() == "ix_email_outbox_kind_to_address_created_at");

        index.Properties.Select(p => p.GetColumnName()).ShouldBe(["kind", "to_address", "created_at"]);
        index.IsUnique.ShouldBeFalse();
        index.GetFilter().ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_a_parent_leaves_the_follow_up_with_a_null_parent_and_its_history()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var parent = await scenario.CreateTicketAsync();
        await scenario.UpdateAsync(parent.Id, t =>
        {
            t.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            t.ChangeStatus(TicketStatus.Closed, Actor.System, host.Clock).IsSuccess.ShouldBeTrue();
        });
        Guid followUpId = default;
        (await host.CommitAsync(async sp =>
        {
            var repository = sp.GetRequiredService<ITicketRepository>();
            var loaded = (await repository.GetByIdAsync(parent.Id, Ct))!;
            var number = (await sp.GetRequiredService<ITicketNumberAllocator>().AllocateAsync(loaded.ProductId, Ct)).Value;
            var followUp = loaded.CreateFollowUp(number, host.Clock).Value;
            followUp.AddCustomerReply(scenario.Requester.Id, "<p>again</p>", host.Clock).IsSuccess.ShouldBeTrue();
            repository.Update(loaded);
            repository.Add(followUp);
            followUpId = followUp.Id;
        })).IsSuccess.ShouldBeTrue();

        await ExecuteAsync($"DELETE FROM tickets WHERE id = '{parent.Id}'");

        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{parent.Id}'")).ShouldBe(0);
        (await ScalarAsync($"SELECT count(*) FROM tickets WHERE id = '{followUpId}' AND parent_ticket_id IS NULL")).ShouldBe(1);
        (await ScalarAsync($"SELECT count(*) FROM ticket_events WHERE ticket_id = '{followUpId}' AND type = 'Created' AND payload->>'parentTicketId' = '{parent.Id}'")).ShouldBe(1);
    }

    [Fact]
    public async Task The_per_address_count_is_still_correct_and_can_use_the_new_index()
    {
        await using var host = new PersistenceTestHost(Database);
        (await host.CommitAsync(sp =>
        {
            var outbox = sp.GetRequiredService<IEmailOutbox>();
            outbox.Enqueue(EmailOutboxItem.Enqueue("access-links", "Ann@Example.com", "{}", null, null, host.Clock).Value);
            outbox.Enqueue(EmailOutboxItem.Enqueue("access-links", "bob@example.com", "{}", null, null, host.Clock).Value);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        var since = host.Clock.GetUtcNow().AddHours(-1);

        (await host.ReadAsync(sp => sp.GetRequiredService<IEmailOutboxStore>().CountRecentAsync("access-links", "ann@example.com", since, Ct))).ShouldBe(1);

        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (var off = new NpgsqlCommand("SET LOCAL enable_seqscan = off", connection, transaction)) { await off.ExecuteNonQueryAsync(Ct); }
        await using var explain = new NpgsqlCommand(
            "EXPLAIN SELECT count(*) FROM email_outbox WHERE kind = 'access-links' AND to_address = 'ann@example.com' AND created_at >= now() - interval '1 hour'",
            connection, transaction);
        var plan = new List<string>();
        await using var reader = await explain.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct)) { plan.Add(reader.GetString(0)); }
        string.Join('\n', plan).ShouldContain("ix_email_outbox_kind_to_address_created_at");
    }

    // ExecuteAsync and ScalarAsync: the same two small Npgsql helpers CustomerReadTests uses (copy them; ScalarAsync returns long).
}
```

Usings: `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.DependencyInjection`, `Npgsql`, `TechStrap.Application.Persistence`, `TechStrap.Domain.Outbox`, `TechStrap.Domain.Tickets`.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter Phase06cSchemaTests`
Expected: the two model tests and the delete test fail (`Restrict` instead of `SetNull`, no such index, an FK violation on delete); the count test fails on the plan assertion.

- [ ] **Step 3: Change the model and generate the migration**

1. In `TicketRecordConfiguration`, change the line to:

```csharp
// Hard-deleting a parent ticket (Admin only, D-006, D-039) unlinks its follow-ups; their own events keep parentTicketId.
builder.HasOne<TicketRecord>().WithMany().HasForeignKey(t => t.ParentTicketId).OnDelete(DeleteBehavior.SetNull);
```

2. In `EmailOutboxRecordConfiguration`, after the `TicketId` index, add:

```csharp
// The per-address lost-link count (CountRecentAsync) filters on exactly these three columns (D-038, D-039).
builder.HasIndex(e => new { e.Kind, e.ToAddress, e.CreatedAt }).HasDatabaseName("ix_email_outbox_kind_to_address_created_at");
```

3. From the repo root, generate (never hand-write the files):

```bash
dotnet tool restore
dotnet ef migrations add Phase06cDeleteAndOutboxIndexes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api
```

4. Read the generated `Up` and `Down` and confirm Rule 2. Do not edit them.

- [ ] **Step 4: Run the tests**

Run the filter, then `SchemaConventionTests`, `MigrationStartupTests` and `TicketSchemaTests`, then the pending-model check:

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter "Phase06cSchemaTests|SchemaConventionTests|MigrationStartupTests|TicketSchemaTests"
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build
```

Expected: PASS and "No changes have been made to the model since the last migration." The Api tests that count migrations read them from the assembly (`ExpectedMigrations`), so they need no edit.

- [ ] **Step 5: Commit**

```bash
git diff --cached --stat   # after git add, before committing
git add src tests
git commit -m "feat(persistence): unlink follow-ups on ticket delete; index the outbox by kind, address and time" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 3: Persistence ports for delete, erase and retention

**Files:**
- Modify: `src/TechStrap.Application/Persistence/ITicketRepository.cs` and `src/TechStrap.Infrastructure/Persistence/Repositories/TicketRepository.cs`
- Modify: `src/TechStrap.Application/Persistence/IEmailOutbox.cs` (the `IEmailOutboxStore` interface) and `src/TechStrap.Infrastructure/Persistence/Repositories/EmailOutboxStore.cs`
- Create: `src/TechStrap.Domain/Requesters/ErasureMarker.cs`
- Create: `src/TechStrap.Application/Requesters/IRequesterErasure.cs`
- Create: `src/TechStrap.Infrastructure/Persistence/Repositories/RequesterErasure.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/TicketRemoveTests.cs`, `OutboxDeletionTests.cs` and `RequesterErasureTests.cs`

**Interfaces:**
- Consumes: `TrackedRecords.FindLoaded`; the records `TicketRecord`, `MessageRecord`, `AttachmentRecord`, `TicketAccessTokenRecord`, `EmailOutboxRecord`; `Paging.NormalizeBatchSize`.
- Produces:

```csharp
// ITicketRepository
/// <summary>
/// Stages the hard delete of a ticket the current scope loaded (<see cref="GetByIdAsync"/>). The database cascades its messages,
/// attachments, events, access tokens, tags, linked articles and idempotency keys; follow-ups are unlinked (D-039). Never uses a bulk
/// delete: the ticket's events are append-only and only a tracked ticket delete is allowed to remove them.
/// </summary>
void Remove(Ticket ticket);

// IEmailOutboxStore
/// <summary>Deletes every row of the ticket, any status. Runs in the caller's unit of work when one is active (admin delete), so it rolls back with it.</summary>
Task<int> DeleteForTicketAsync(Guid ticketId, CancellationToken cancellationToken);
/// <summary>
/// Deletes at most <paramref name="batchSize"/> (normalized through <see cref="Paging.NormalizeBatchSize"/>) Sent or Discarded rows whose
/// <c>created_at</c> is before <paramref name="before"/>, oldest first, as one short statement of its own (the retention sweep, D-039).
/// Pending, Sending and DeadLettered rows are never touched. Returns how many rows it deleted.
/// </summary>
Task<int> DeleteFinishedBeforeAsync(DateTimeOffset before, int batchSize, CancellationToken cancellationToken);
```

```csharp
namespace TechStrap.Domain.Requesters;
/// <summary>The text that replaces erased content (D-006, D-039). A named constant, never a literal at a call site.</summary>
public static class ErasureMarker { public const string Text = "[erased]"; }

namespace TechStrap.Application.Requesters;
public sealed record RequesterErasureResult(int Tickets, int Messages, int Attachments, int Tokens, int OutboxRows, IReadOnlyList<string> StorageKeys);

/// <summary>
/// The bulk half of erasing a requester (D-039). It runs in the caller's unit of work, never commits, and is the only writer that changes
/// <c>Message.Body</c> after a message is created: the Domain has no way to change a body, on purpose, so this port updates the rows directly.
/// The caller deletes the returned storage keys through the attachment store after its own commit.
/// </summary>
public interface IRequesterErasure
{
    Task<RequesterErasureResult> EraseDataAsync(Guid requesterId, string email, DateTimeOffset now, CancellationToken cancellationToken);
}
```

**Rules:**
- `TicketRepository.Remove`: `context.Set<TicketRecord>().Remove(context.FindLoaded<TicketRecord>(ticket.Id));`, the same pattern as `TagRepository.Remove`. It does not call `ApplyOriginalVersion` (the spec has no row version on delete). Do not add any `ExecuteDelete` to this file: `AppendOnlyBypassRules` forbids it in a file that mentions `TicketEventRecord`.
- `EmailOutboxStore.DeleteForTicketAsync`: `context.Set<EmailOutboxRecord>().Where(e => e.TicketId == ticketId).ExecuteDeleteAsync(ct)`.
- `EmailOutboxStore.DeleteFinishedBeforeAsync`: select up to `limit` ids (`Status == Sent || Status == Discarded`, `CreatedAt < before`, `OrderBy(CreatedAt)`), then `ExecuteDeleteAsync` where `ids.Contains(Id)` and the same status condition (the pattern of `IntakeIdempotencyStore.PruneAsync`). The comparison is `<`: a row created exactly at `before` is kept.
- `RequesterErasure` (new file; the words `TicketEventRecord`, `AdminEventRecord`, `ticket_events` and `admin_events` must not appear in it, even in comments, or the architecture scan fails). It uses `ExecuteUpdateAsync` and `ExecuteDeleteAsync` in this order:
  1. `ticketIds` = ids of tickets where `RequesterId == requesterId` (a list).
  2. Customer messages: `MessageRecord` where `AuthorType == AuthorType.Requester && AuthorId == requesterId`. Select the `StorageKey` of their attachments (`AttachmentRecord` whose `MessageId` is in that set) into `StorageKeys`, then `ExecuteDelete` those attachment rows.
  3. `ExecuteUpdate` those messages: `SetProperty(m => m.Body, ErasureMarker.Text)`. `search_vector` is generated, so it refreshes itself.
  4. `ExecuteUpdate` the tickets of `ticketIds`: `SetProperty(t => t.Subject, ErasureMarker.Text).SetProperty(t => t.Metadata, (string?)null).SetProperty(t => t.CustomFields, (string?)null)`. Both jsonb columns are nullable (`string?` in `TicketRecord`), so NULL is right; `MetadataTrusted` is left alone.
  5. `ExecuteUpdate` the `TicketAccessTokenRecord` rows where `RequesterId == requesterId && RevokedAt == null`: `SetProperty(t => t.RevokedAt, now)`. The count is `Tokens`.
  6. `ExecuteDelete` the `EmailOutboxRecord` rows where `ToAddress == address || (e.TicketId != null && ticketIds.Contains(e.TicketId.Value))`, with `address = email.Trim().ToLowerInvariant()` (outbox addresses are stored lower-cased, as `CountRecentAsync` relies on). The count is `OutboxRows`.
  7. Return the counts (`Tickets` = rows updated in step 4, `Messages` = rows updated in step 3, `Attachments` = rows deleted in step 2).
- Register `services.AddScoped<IRequesterErasure, RequesterErasure>();` in `AddTechStrapPersistence`.

- [ ] **Step 1: Write the failing tests**

```csharp
// TicketRemoveTests (PostgresIntegrationTestBase; TicketScenario; a CountAsync helper like DeleteTagIntegrationTests)
[Fact] public async Task Removing_a_loaded_ticket_deletes_it_and_the_database_cascades_everything_attached()
{
    await using var host = new PersistenceTestHost(Database);
    var scenario = await TicketScenario.CreateAsync(host);
    var tag = Tag.Create("bug", "Bug", "#DC2626", host.Clock).Value;
    (await host.CommitAsync(sp => { sp.GetRequiredService<ITagRepository>().Add(tag); return Task.CompletedTask; })).IsSuccess.ShouldBeTrue();
    var ticket = await scenario.CreateTicketAsync(change: t =>
    {
        t.AddTag(tag.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
        t.PendingMessages[0].AddAttachment("a.png", "image/png", 5, "attachments/k/1", host.Clock).IsSuccess.ShouldBeTrue();
    });
    (await host.CommitAsync(sp =>
    {
        sp.GetRequiredService<ITicketRepository>().AddAccessToken(TicketAccessToken.Issue(ticket.Id, scenario.Requester.Id, "hash-1", host.Clock).Value);
        return Task.CompletedTask;
    })).IsSuccess.ShouldBeTrue();

    var result = await host.CommitAsync(async sp =>
    {
        var repository = sp.GetRequiredService<ITicketRepository>();
        repository.Remove((await repository.GetByIdAsync(ticket.Id, Ct))!);
    });

    result.IsSuccess.ShouldBeTrue();
    foreach (var table in new[] { "tickets", "messages", "attachments", "ticket_events", "ticket_tags", "ticket_access_tokens" })
    {
        var column = table == "tickets" ? "id" : "ticket_id";
        (await CountAsync($"SELECT count(*) FROM {table} WHERE {column} = '{ticket.Id}'")).ShouldBe(0, table);
    }
    (await CountAsync($"SELECT count(*) FROM tags WHERE id = '{tag.Id}'")).ShouldBe(1);
    (await CountAsync($"SELECT count(*) FROM requesters WHERE id = '{scenario.Requester.Id}'")).ShouldBe(1);
}
[Fact] public async Task Removing_a_ticket_that_this_scope_never_loaded_is_a_programming_error()
// await Should.ThrowAsync<InvalidOperationException>(() => host.CommitAsync(sp => { sp.GetRequiredService<ITicketRepository>().Remove(ticket); return Task.CompletedTask; }));
// `ticket` is the Domain object returned by CreateTicketAsync (never loaded in the new scope).
[Fact] public async Task Rolling_back_the_scope_keeps_the_ticket()
// BeginAsync a scope, GetByIdAsync + Remove, dispose without CommitAsync; the ticket row still exists.

// OutboxDeletionTests (helpers: EnqueueAsync(host, kind, to, ticketId) returns the id; SetAsync(id, status, createdAt) updates status and created_at by SQL with parameters)
[Fact] public async Task DeleteForTicket_removes_rows_of_every_status_for_that_ticket_only_inside_the_callers_transaction()
// rows for ticket A: Pending, Sent, DeadLettered; one Pending for ticket B; one with a null ticket.
// var deleted = 0; (await host.CommitAsync(async sp => deleted = await sp.GetRequiredService<IEmailOutboxStore>().DeleteForTicketAsync(a, Ct))).IsSuccess.ShouldBeTrue();
// deleted == 3; the B row and the null-ticket row remain.
[Fact] public async Task DeleteForTicket_rolls_back_with_the_unit_of_work()
// BeginAsync a scope, call DeleteForTicketAsync, dispose uncommitted; all 3 rows are still there.
[Fact] public async Task DeleteFinishedBefore_deletes_only_sent_and_discarded_rows_older_than_the_cutoff()
// cutoff = clock now. Rows (created_at): Sent at cutoff-1s (deleted), Sent exactly at cutoff (kept), Discarded at cutoff-1d (deleted),
// DeadLettered/Pending/Sending at cutoff-100d (all kept). Returns 2; 4 rows remain.
[Fact] public async Task DeleteFinishedBefore_honours_the_batch_size()
// 3 old Sent rows, batchSize 2: returns 2, then 1, then 0.

// RequesterErasureTests (arrange helper below; each test runs the port inside host.CommitAsync)
```

`RequesterErasureTests` arrange: `TicketScenario` gives Ann (`ann@example.com`), an agent Sam and two products. The helper creates a second requester Bob with one ticket (copy the allocate, `Ticket.Create`, `AddCustomerReply` and `Add` steps from `TicketScenario.CreateTicketAsync`, using Bob's id). Then, for Ann: two tickets (A and B). On ticket A, a second customer message with an attachment (`storageKey` `attachments/a/customer`), an agent reply with an attachment (`attachments/a/agent`) and an internal note, all added through `scenario.UpdateAsync(a.Id, t => ...)` (reply and note on the Domain ticket, `message.AddAttachment(...)`). Tokens: one active and one already revoked on A, one active token on Bob's ticket. SQL: `UPDATE tickets SET metadata = '{"note":"ann@example.com"}', custom_fields = '{"plan":"pro"}' WHERE id IN (A, B)`. Outbox rows (`IEmailOutbox.Enqueue`): `ticket-confirmation` to `ann@example.com` (Pending), a second to `Ann@Example.com` set to Sent by SQL, a `new-ticket-alert` to `sam@example.com` with `ticketId` A and payload `{"requesterLabel":"ann@example.com"}`, and two rows for Bob's side (to `bob@example.com`, and an alert to `sam@example.com` with `ticketId` = Bob's ticket).

```csharp
[Fact] public async Task The_port_erases_exactly_the_requesters_data_and_reports_what_it_did()
{
    var arranged = await ArrangeAsync();   // host, scenario, ids
    RequesterErasureResult? result = null;

    (await arranged.Host.CommitAsync(async sp =>
        result = await sp.GetRequiredService<IRequesterErasure>().EraseDataAsync(arranged.AnnId, "ann@example.com", arranged.Host.Clock.GetUtcNow(), Ct)))
        .IsSuccess.ShouldBeTrue();

    result!.Tickets.ShouldBe(2);
    result.Attachments.ShouldBe(1);
    result.StorageKeys.ShouldBe(["attachments/a/customer"]);
    result.Tokens.ShouldBe(1);                 // the already revoked token is not counted
    result.OutboxRows.ShouldBe(3);             // two to Ann (any status, any case) and the alert about her ticket
    (await ScalarAsync($"SELECT count(*) FROM messages WHERE author_type = 'Requester' AND author_id = '{arranged.AnnId}' AND body <> '{ErasureMarker.Text}'")).ShouldBe(0);
    (await ScalarAsync($"SELECT count(*) FROM messages WHERE author_type = 'Agent' AND body = '{ErasureMarker.Text}'")).ShouldBe(0);
    (await ScalarAsync($"SELECT count(*) FROM tickets WHERE requester_id = '{arranged.AnnId}' AND subject = '{ErasureMarker.Text}' AND metadata IS NULL AND custom_fields IS NULL")).ShouldBe(2);
    (await ScalarAsync($"SELECT count(*) FROM attachments WHERE storage_key = 'attachments/a/agent'")).ShouldBe(1);
    (await ScalarAsync($"SELECT count(*) FROM ticket_access_tokens WHERE requester_id = '{arranged.AnnId}' AND revoked_at IS NULL")).ShouldBe(0);
    (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(2);   // only Bob's two rows remain
    (await ScalarAsync($"SELECT count(*) FROM ticket_access_tokens WHERE requester_id = '{arranged.BobId}' AND revoked_at IS NULL")).ShouldBe(1);
    (await ScalarAsync($"SELECT count(*) FROM tickets WHERE requester_id = '{arranged.BobId}' AND subject <> '{ErasureMarker.Text}'")).ShouldBe(1);
    (await ScalarAsync($"SELECT count(*) FROM ticket_events WHERE ticket_id IN ('{arranged.TicketA}', '{arranged.TicketB}')")).ShouldBeGreaterThan(0);   // history is untouched
}
[Fact] public async Task A_second_run_deletes_and_revokes_nothing_more()
// run twice; the second result has Attachments 0, Tokens 0, OutboxRows 0 and no StorageKeys.
[Fact] public async Task The_erasure_rolls_back_with_the_unit_of_work()
// BeginAsync a scope, run the port, dispose uncommitted: bodies, subjects, tokens and outbox rows are unchanged.
[Fact] public async Task A_requester_with_no_tickets_is_a_no_op()
// a requester with no tickets and no mail: every count is 0 and nothing else changes.
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter "TicketRemoveTests|OutboxDeletionTests|RequesterErasureTests"`
Expected: a build failure (the members do not exist).

- [ ] **Step 3: Implement**

Write the interface members, `ErasureMarker`, `RequesterErasureResult`, `IRequesterErasure`, `RequesterErasure`, the registration, and the two store methods and `Remove`, following the Rules above.

- [ ] **Step 4: Run the tests**

Run the filter, then the whole Infrastructure.IntegrationTests project and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release` (the `AppendOnlyBypassRules` scan must pass over the new file).
Expected: PASS. `TicketRemoveTests.Removing_a_loaded_ticket_...` also proves `AppendOnlyEventInterceptor` accepts the delete: the interceptor only inspects tracked events, and the database cascade deletes the untracked ones.

- [ ] **Step 5: Commit**

```bash
git diff --cached --stat   # after git add, before committing
git add src tests
git commit -m "feat(persistence): ticket remove, outbox deletion and requester erasure ports" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 4: Delete a ticket

**Files:**
- Create: `src/TechStrap.Application/Attachments/StoredFileCleanup.cs`
- Modify: `src/TechStrap.Application/Attachments/StoredAttachmentBatch.cs` (its delete loop delegates to `StoredFileCleanup`)
- Create: `src/TechStrap.Application/Tickets/DeleteTicketRequestHandler.cs`
- Modify: `src/TechStrap.Api/Controllers/TicketsController.cs`
- Modify: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`
- Create: `tests/TechStrap.Application.Tests/Tickets/DeleteTicketRequestHandlerTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/DeleteTicketIntegrationTests.cs`
- Create: `tests/TechStrap.Api.Tests/Tickets/DeleteTicketEndpointTests.cs`

**Interfaces:**
- Consumes: Task 3 (`ITicketRepository.Remove`, `IEmailOutboxStore.DeleteForTicketAsync`); `ITicketRepository.GetByIdAsync`, `GetMessagesAsync`, `GetAttachmentsAsync(publicOnly: false)`; `IAttachmentStore.DeleteAsync`; `AdminAudit.Record`; `CurrentAgent.RequireActiveAsync`; `TicketErrors.NotFound()`.
- Produces:

```csharp
namespace TechStrap.Application.Tickets;
public interface IDeleteTicketRequestHandler
{
    Task<Result> HandleAsync(Guid ticketId, CancellationToken cancellationToken);
}

namespace TechStrap.Application.Attachments;
/// <summary>Deletes stored files after a commit (delete ticket, erase requester). Best effort: never throws, ignores cancellation, logs the key and the exception type only.</summary>
internal static class StoredFileCleanup
{
    public static async Task DeleteAllAsync(IAttachmentStore store, IEnumerable<string> storageKeys, ILogger logger);
}
```

Route: `DELETE /api/tickets/{id}` on `TicketsController`, action `Delete`, with an action-level `[Authorize(Policy = AuthorizationPolicies.Admin)]` (as `TagsController.Delete`). Success 204. Failures: 404 `ticket-not-found` (the code every ticket handler uses), 403 for an inactive actor (`agent-inactive`), 409 on a commit conflict (the existing mapping).

**Handler rules.** The constructor takes `ITicketRepository`, `IAttachmentStore`, `IEmailOutboxStore`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims`, `IUnitOfWork`, `TimeProvider` and `ILogger<DeleteTicketRequestHandler>`, in that order.
1. `await using var scope = await unitOfWork.BeginAsync(ct)`.
2. `CurrentAgent.RequireActiveAsync(...)`. On failure return `Result.Failure(actor.Errors[0])`. The check comes first, so a deactivated admin learns nothing about the ticket.
3. `tickets.GetByIdAsync`. Null returns `TicketErrors.NotFound()`.
4. Collect the storage keys: `GetAttachmentsAsync(id, publicOnly: false)` (internal-note files too). Count messages with `GetMessagesAsync(id, publicOnly: false)`. Both reads are `AsNoTracking`, so they do not disturb the delete.
5. `tickets.Remove(ticket)`, then `await outbox.DeleteForTicketAsync(ticketId, ct)` (any status; inside this transaction).
6. `AdminAudit.Record(adminEvents, AdminEventType.TicketDeleted, actor.Value, AdminSubjectType.Ticket, ticket.Id, new { number = ticket.Number.ToString(), messageCount, attachmentCount }, clock)`. No subject, body or requester appears. The audit row has no foreign key, so it outlives the ticket.
7. `var committed = await scope.CommitAsync(ct)`. If it failed, return it: nothing was deleted, so no file is touched.
8. After a successful commit, `await StoredFileCleanup.DeleteAllAsync(attachments, keys, logger)`, then `Result.Success()`. A failed file delete never changes the result.

`StoredFileCleanup` loops over the keys and calls `store.DeleteAsync(key, CancellationToken.None)` in a `try`. On `Exception ex` it logs `logger.LogWarning("Attachment cleanup failed for {StorageKey} ({ExceptionType}).", key, ex.GetType().Name)`. This is the message `StoredAttachmentBatch` uses today. **Do not leave two copies of this loop:** change `StoredAttachmentBatch.DeleteAllAsync` to call `StoredFileCleanup.DeleteAllAsync(store, keys, logger)`, keeping its behaviour. The existing intake, agent-reply and customer-reply tests must pass unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
// DeleteTicketRequestHandlerTests (substitutes, as DeleteTagRequestHandlerTests; helper Handler() builds the real handler with UnitOfWorkSubstitute.Create())
[Fact] public async Task A_ticket_is_removed_with_its_outbox_rows_and_audited_with_counts_only()
// _tickets.GetByIdAsync returns TicketBuilder.New(_clock); GetAttachmentsAsync returns 2 attachments; GetMessagesAsync returns 3 messages.
// Received(1).Remove(ticket); Received(1) _outbox.DeleteForTicketAsync(ticket.Id, ...);
// _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.Type == AdminEventType.TicketDeleted && e.SubjectType == AdminSubjectType.Ticket
//     && e.SubjectId == ticket.Id && e.PayloadJson == "{\"number\":\"TS-42\",\"messageCount\":3,\"attachmentCount\":2}"));
[Fact] public async Task The_files_are_deleted_only_after_a_successful_commit()
// UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict("concurrency-conflict")): result is that conflict, _attachments.DidNotReceive().DeleteAsync(...);
// with a success commit: DeleteAsync received once per storage key.
[Fact] public async Task A_failing_file_delete_is_logged_and_does_not_fail_the_delete()
// _attachments.DeleteAsync throws IOException for the first key: the result is still success and the second key is still attempted.
[Fact] public async Task An_unknown_ticket_is_not_found_and_nothing_is_staged()
// error.Code == "ticket-not-found", Kind NotFound; DidNotReceive Remove, DeleteForTicketAsync and _events.Add.
[Fact] public async Task A_deactivated_actor_is_refused_before_anything_is_read()   // Review Focus 3, handler half
// admin.SetActive(false): error.Code == AgentErrors.Codes.Inactive; _tickets.DidNotReceive().GetByIdAsync(...).
[Fact] public async Task A_missing_agent_identity_is_refused()
// _claims.Current returns null: error.Code == AgentErrors.Codes.AccessRequired.
```

`DeleteTicketIntegrationTests` (real `PersistenceTestHost`, real handler built like `DeleteTagIntegrationTests.DeleteAsync`, with `new AttachmentStore(new LocalFileStorageProvider(Options.Create(new LocalStorageOptions { RootPath = root })))` over a temp directory removed in `Dispose`):

```csharp
[Fact] public async Task A_ticket_with_everything_attached_is_gone_and_its_follow_up_survives()   // Review Focus 2
{
    // Arrange: a ticket (customer message, agent reply, internal note) with one real stored file on each message
    // (SaveAsync three PNGs under a throwaway ticket id, then message.AddAttachment with the returned key/name/type/size),
    // a tag, one active token, an Admin agent, then Solved and Closed, then a follow-up created with CreateFollowUp.
    // Outbox rows with this ticket id in three states (Pending, Sent, DeadLettered by SQL) and one row for an unrelated ticket id.
    var result = await DeleteAsync(host, parent.Id);

    result.IsSuccess.ShouldBeTrue();
    foreach (var table in new[] { "messages", "attachments", "ticket_events", "ticket_access_tokens", "ticket_tags", "ticket_articles" })
    {
        (await CountAsync($"SELECT count(*) FROM {table} WHERE ticket_id = '{parent.Id}'")).ShouldBe(0, table);
    }
    (await CountAsync($"SELECT count(*) FROM tickets WHERE id = '{parent.Id}'")).ShouldBe(0);
    keys.ShouldAllBe(key => !File.Exists(Path.Combine(root, key)));                        // no orphan file
    (await CountAsync($"SELECT count(*) FROM email_outbox WHERE ticket_id = '{parent.Id}'")).ShouldBe(0);   // any status
    (await CountAsync($"SELECT count(*) FROM email_outbox WHERE ticket_id = '{unrelated}'")).ShouldBe(1);
    // the follow-up survives, unlinked, with its history and its own message
    (await CountAsync($"SELECT count(*) FROM tickets WHERE id = '{followUp.Id}' AND parent_ticket_id IS NULL")).ShouldBe(1);
    (await CountAsync($"SELECT count(*) FROM ticket_events WHERE ticket_id = '{followUp.Id}' AND type = 'Created' AND payload->>'parentTicketId' = '{parent.Id}'")).ShouldBe(1);
    (await CountAsync($"SELECT count(*) FROM messages WHERE ticket_id = '{followUp.Id}'")).ShouldBe(1);
    // the audit row has counts and no content
    (await CountAsync($"SELECT count(*) FROM admin_events WHERE type = 'TicketDeleted' AND subject_id = '{parent.Id}' AND (payload->>'messageCount')::int = 3 AND (payload->>'attachmentCount')::int = 3")).ShouldBe(1);
    (await CountAsync($"SELECT count(*) FROM admin_events WHERE payload::text ILIKE '%{parent.Subject}%'")).ShouldBe(0);
}
[Fact] public async Task Deleting_an_unknown_ticket_is_not_found_and_changes_nothing()
[Fact] public async Task Deleting_a_ticket_with_an_attachment_whose_file_is_already_missing_still_succeeds()
```

`DeleteTicketEndpointTests` (Api.Tests; `ApiFactory`, `TicketTestData.SeedAsync`, an admin client as in `TagEndpointTests.StartAsync`):

```csharp
[Fact] public async Task An_admin_deletes_a_ticket_and_it_is_gone()
// DELETE /api/tickets/{id} as admin -> 204; GET /api/tickets/{id} -> 404; second DELETE -> 404 with problem code ticket-not-found;
// GET /api/admin-events?subjectType=Ticket lists one TicketDeleted event whose Payload contains no subject text.
[Fact] public async Task An_agent_who_is_not_an_admin_cannot_delete()   // Review Focus 3, route half
// as "sam" -> 403; the ticket still exists.
[Fact] public async Task An_anonymous_caller_is_refused() // 401
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter DeleteTicketRequestHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write `StoredFileCleanup`, the handler, and the controller action:

```csharp
/// <summary>Hard-deletes a ticket, its outbox rows and its files (Admin only, D-006, D-022, D-039). Follow-ups survive, unlinked.</summary>
[HttpDelete("{id:guid}")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
public async Task<IActionResult> Delete(Guid id, [FromServices] IDeleteTicketRequestHandler handler, CancellationToken cancellationToken) =>
    (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, NoContent);
```

Add `["TicketsController.Delete"] = 204` to `ControllerActions.ExpectedSuccess`. The handler is registered automatically by `AddApplicationHandlers`.

- [ ] **Step 4: Run the tests**

Run: Application.Tests, `DeleteTicketIntegrationTests`, `DeleteTicketEndpointTests`, then Api.Tests in full (`ResultMappingTests`, `RoutePolicyCoverageTests`, `OpenApiSurfaceTests` and `AgentAccessCoverageTests` pick the new route up: the deactivated-admin, outsider and agent-group probes all hit `DELETE /api/tickets/{guid}`) and the Architecture tests.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git diff --cached --stat   # after git add, before committing
git add src tests
git commit -m "feat(tickets): admin hard delete of a ticket with its files and outbox rows" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 5: Erase a requester

**Files:**
- Create: `src/TechStrap.Application/Requesters/RequesterErrors.cs`
- Create: `src/TechStrap.Application/Requesters/EraseRequesterRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/RequestersController.cs`
- Modify: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`
- Create: `tests/TechStrap.Application.Tests/Requesters/EraseRequesterRequestHandlerTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/EraseRequesterIntegrationTests.cs`
- Create: `tests/TechStrap.Api.Tests/Requesters/EraseRequesterEndpointTests.cs`

**Interfaces:**
- Consumes: Task 3 (`IRequesterErasure`, `RequesterErasureResult`, `ErasureMarker`); Task 4 (`StoredFileCleanup`); `IRequesterRepository.GetByIdAsync` and `Update`; `Requester.Erase(TimeProvider)`; `AdminAudit.Record`; `CurrentAgent.RequireActiveAsync`.
- Produces:

```csharp
namespace TechStrap.Application.Requesters;
public interface IEraseRequesterRequestHandler
{
    Task<Result> HandleAsync(Guid requesterId, CancellationToken cancellationToken);
}

internal static class RequesterErrors
{
    public static ResultError NotFound() => new("requester-not-found", "That requester does not exist.", ResultErrorKind.NotFound);
}
```

Route: `POST /api/requesters/{id}/erase` on a new `RequestersController` (`[Route("api/requesters")]`, class-level `[Authorize(Policy = AuthorizationPolicies.Admin)]`). Success 204; 404 `requester-not-found`; 403 for an inactive actor; 409 on a commit conflict.

**Handler rules.** The constructor takes `IRequesterRepository`, `IRequesterErasure`, `IAttachmentStore`, `IAdminEventRepository`, `IAgentRepository`, `ICurrentAgentClaims`, `IUnitOfWork`, `TimeProvider` and `ILogger<EraseRequesterRequestHandler>`, in that order.
1. `await using var scope = await unitOfWork.BeginAsync(ct)`; `CurrentAgent.RequireActiveAsync` (failure returns `actor.Errors[0]`).
2. `requesters.GetByIdAsync`. Null returns `RequesterErrors.NotFound()`.
3. **Capture `requester.Email` before anything else**: after `Erase` it is the tombstone and the old address could no longer be matched.
4. `var erased = await erasure.EraseDataAsync(requester.Id, email, clock.GetUtcNow(), ct)`.
5. `requester.Erase(clock)`, then `requesters.Update(requester)`. `Erase` is idempotent (it keeps the first `ErasedAt`), so erasing an erased requester re-runs the cleanup and still returns 204.
6. `AdminAudit.Record(adminEvents, AdminEventType.RequesterErased, actor.Value, AdminSubjectType.Requester, requester.Id, new { tickets = erased.Tickets, messages = erased.Messages, attachments = erased.Attachments, links = erased.Tokens, outboxRows = erased.OutboxRows }, clock)`. Counts only. The key is `links`, not `tokens`: `AdminEvent` refuses any property name containing "token", and `AdminAudit.Record` would throw.
7. `var committed = await scope.CommitAsync(ct)`; a failure returns it (the files stay). After a successful commit, `StoredFileCleanup.DeleteAllAsync(attachments, erased.StorageKeys, logger)`, then success.
8. A ticket's row version changes when its subject is replaced, so an agent holding an older copy gets a 409 on their next change. That is expected (D-039).

- [ ] **Step 1: Write the failing tests**

```csharp
// EraseRequesterRequestHandlerTests (substitutes)
[Fact] public async Task The_old_email_reaches_the_erasure_port_before_the_requester_is_anonymised()
// requester ann@example.com; _erasure.EraseDataAsync(requester.Id, "ann@example.com", _clock.GetUtcNow(), ...) returns a result;
// after HandleAsync: Received(1) with exactly that email; requester.IsErased is true; _requesters.Received(1).Update(requester).
[Fact] public async Task The_erasure_is_audited_with_counts_only()
// result (2, 5, 1, 3, 4, ["k"]): payload == {"tickets":2,"messages":5,"attachments":1,"links":3,"outboxRows":4}; type RequesterErased, subject Requester.
[Fact] public async Task Files_are_deleted_only_after_a_successful_commit() // as the delete test
[Fact] public async Task An_unknown_requester_is_not_found_and_nothing_is_staged()
[Fact] public async Task Erasing_an_erased_requester_runs_again_and_succeeds()
// requester already erased: result is success, EraseDataAsync received once, a new AdminEvent is added.
[Fact] public async Task A_deactivated_actor_is_refused_before_anything_is_read()   // Review Focus 3, handler half
[Fact] public async Task A_commit_conflict_is_returned_and_no_file_is_deleted()
```

`EraseRequesterIntegrationTests` (real handler, as `DeleteTagIntegrationTests`, with a real `AttachmentStore` over a temp directory and an Admin agent). Arrange helper `ArrangeAsync()` seeds:
- Requester **Pat Rivera** `pat@example.com` (name needle `Pat Rivera`), another requester **Bob** with one ticket of his own that must stay untouched.
- Pat's tickets A and B. Subject of A: `subject-canary-91c2 cannot sign in`. First customer message of A: `<p>canary-7f3a pat@example.com</p>`. A second customer message on A with a real stored PNG `pat-passport.png`. An agent reply with a real stored PNG (`agent.png`) and an internal note, neither containing a needle. `UPDATE tickets SET metadata = '{"who":"pat@example.com"}', custom_fields = '{"name":"Pat Rivera"}'`.
- One active access token on A and one on B; a `new-ticket-alert` outbox row to `sam@example.com` with `ticketId` A and payload `{"requesterLabel":"pat@example.com"}`; a `ticket-confirmation` to `pat@example.com` set to Sent by SQL; a Pending one to `Pat@Example.com`.

```csharp
[Fact] public async Task Nothing_personal_remains_after_erase()   // Review Focus 1
{
    var arranged = await ArrangeAsync();
    var result = await EraseAsync(arranged.Host, arranged.PatId);

    result.IsSuccess.ShouldBeTrue();
    var needles = new[] { "pat@example.com", "Pat Rivera", "canary-7f3a", "subject-canary-91c2" };
    await using var connection = new NpgsqlConnection(Database.ConnectionString);
    await connection.OpenAsync(Ct);
    var leaks = new List<string>();
    foreach (var (table, column) in await TextColumnsAsync(connection))   // text, varchar, citext, jsonb, json, arrays: copy SensitiveDataLeakTests.TextColumnsAsync
    {
        foreach (var needle in needles)
        {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\" WHERE \"{column}\"::text ILIKE '%' || @needle || '%'", connection);
            command.Parameters.AddWithValue("needle", needle);
            if ((long)(await command.ExecuteScalarAsync(Ct))! > 0) { leaks.Add($"{table}.{column} holds '{needle}'"); }
        }
    }
    leaks.ShouldBeEmpty();    // the only form of the requester left is the tombstone erased-{id}@invalid, which contains no needle
    arranged.StoredKeysOfPat.ShouldAllBe(key => !File.Exists(Path.Combine(arranged.Root, key)));
}
[Fact] public async Task Agent_replies_internal_notes_and_their_files_are_untouched()
// the agent reply and note bodies are unchanged, the agent attachment row and its file still exist.
[Fact] public async Task Tickets_numbers_events_and_the_requester_row_survive()
// both tickets keep number and every ticket_events row; the requester row exists with email erased-{id}@invalid, name NULL, erased_at set; Bob's ticket, messages and token are unchanged.
[Fact] public async Task Outbox_rows_by_address_and_by_ticket_are_gone_and_others_stay()
// no row to pat@ (either case, any status), no row with Pat's ticket ids; Bob's rows remain.
[Fact] public async Task Tokens_are_revoked()
// every token of Pat has revoked_at set; Bob's token is still active.
[Fact] public async Task Erasing_twice_succeeds_and_each_run_writes_one_audit_row()
// second EraseAsync is success; admin_events has 2 RequesterErased rows for the requester, each with the five count keys only.
[Fact] public async Task The_audit_row_has_counts_and_no_personal_data()
// payload keys are exactly tickets, messages, attachments, links, outboxRows; payload text has no needle.
```

`EraseRequesterEndpointTests` (Api.Tests; `ApiFactory`, `CustomerTestData.SeedAsync`, an admin client; get Ann's id with `database.ScalarAsync<Guid>("SELECT id FROM requesters WHERE email = 'ann@example.com'")`):

```csharp
[Fact] public async Task An_admin_erases_a_requester_and_the_customer_link_stops_working()
// before: GET /api/customer/ticket with X-Ticket-Token = seed.ValidToken -> 200. POST /api/requesters/{ann}/erase as admin -> 204.
// after: the same GET -> 404 problem code not-found (uniform with every other customer failure).
[Fact] public async Task A_search_for_the_old_email_finds_nothing()
// GET /api/tickets?search=ann@example.com as an agent: TotalCount 0 after the erase (a canary body "ann@example.com" was seeded in a customer message first).
[Fact] public async Task An_unknown_requester_is_404_requester_not_found()
[Fact] public async Task An_agent_who_is_not_an_admin_cannot_erase_and_an_anonymous_caller_is_refused()   // 403 and 401; Review Focus 3, route half
[Fact] public async Task Erasing_twice_returns_204_both_times()
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter EraseRequesterRequestHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write `RequesterErrors`, the handler, and the controller:

```csharp
/// <summary>Erasing a requester (Admin only, D-006, D-022, D-039).</summary>
[ApiController]
[Route("api/requesters")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
public sealed class RequestersController : ControllerBase
{
    /// <summary>Anonymises the requester and their data; ticket numbers and events remain. Safe to repeat.</summary>
    [HttpPost("{id:guid}/erase")]
    public async Task<IActionResult> Erase(Guid id, [FromServices] IEraseRequesterRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, NoContent);
}
```

Add `["RequestersController.Erase"] = 204` to `ControllerActions.ExpectedSuccess`. If the pin test finds a leak, fix `RequesterErasure` (Task 3), never the test's needle list.

- [ ] **Step 4: Run the tests**

Run: Application.Tests, `EraseRequesterIntegrationTests`, `EraseRequesterEndpointTests`, the whole Api.Tests project (the generic Admin-only, deactivated-admin and route-policy tests now cover `POST /api/requesters/{guid}/erase`) and the Architecture tests.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git diff --cached --stat   # after git add, before committing
git add src tests
git commit -m "feat(requesters): admin erase of a requester across messages, subjects, files, tokens and outbox" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 6: Dead letters: list, retry, discard

**Files:**
- Create: `src/TechStrap.Contracts/DeadLetters/DeadLetterDtos.cs`
- Create: `src/TechStrap.Application/DeadLetters/MaskedRecipient.cs`
- Create: `src/TechStrap.Application/DeadLetters/DeadLetterErrors.cs`
- Create: `src/TechStrap.Application/DeadLetters/ListDeadLettersRequestHandler.cs`, `RetryDeadLetterRequestHandler.cs` and `DiscardDeadLetterRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/DeadLettersController.cs`
- Modify: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`
- Create: `tests/TechStrap.Application.Tests/DeadLetters/MaskedRecipientTests.cs`, `ListDeadLettersRequestHandlerTests.cs`, `RetryDeadLetterRequestHandlerTests.cs` and `DiscardDeadLetterRequestHandlerTests.cs`
- Create: `tests/TechStrap.Architecture.Tests/DeadLetterDtoShapeTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/DeadLetterIntegrationTests.cs`
- Create: `tests/TechStrap.Api.Tests/DeadLetters/DeadLetterEndpointTests.cs`
- Modify: `tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs` (one added test)

**Interfaces:**
- Consumes: `IEmailOutboxStore.ListDeadLettersAsync`, `GetAsync` and `Update` (06a/06b, unchanged); `EmailOutboxItem.Retry(clock)` and `Discard()`; `PersistenceErrorCodes.OutboxNotFound`; `PagedResponse<T>`; `AdminAudit.Record`; `CurrentAgent.RequireActiveAsync`.
- Produces:

```csharp
namespace TechStrap.Contracts.DeadLetters;

/// <summary>
/// A dead-lettered email as an admin sees it. Recipient is masked (first character of the local part, then ***@ and the domain); the
/// payload, which holds a portal link, is never exposed (D-039). LastError is a sanitised category code, never server text.
/// </summary>
public sealed record DeadLetterDto(
    Guid Id, string Kind, string Recipient, Guid? TicketId, Guid? ProductId, int Attempts, string? LastError, DateTimeOffset CreatedAt);

namespace TechStrap.Application.DeadLetters;
internal static class MaskedRecipient
{
    /// <summary>"ann@example.com" gives "a***@example.com"; a one-character local part gives "***@example.com"; null, blank, or anything that is not exactly one address with a non-empty local part and domain gives "***".</summary>
    public static string Mask(string? address);
}

public interface IListDeadLettersRequestHandler
{
    Task<Result<PagedResponse<DeadLetterDto>>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken);
}
public interface IRetryDeadLetterRequestHandler { Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken); }
public interface IDiscardDeadLetterRequestHandler { Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken); }
```

Routes (`DeadLettersController`, `[Route("api/dead-letters")]`, class-level Admin policy):
- `GET api/dead-letters?page&pageSize` returns 200 `PagedResponse<DeadLetterDto>` (action `List`, defaults `1` and `Paging.DefaultPageSize`, as `AdminEventsController`).
- `POST api/dead-letters/{id:guid}/retry` returns 204 (action `Retry`).
- `DELETE api/dead-letters/{id:guid}` returns 204 (action `Discard`).
- Both write actions return 404 `outbox-not-found` for an unknown id and 409 `outbox-not-dead-lettered` (the Domain's code) when the row is in any other state. The spec says only 204/404 for discard; 409 is the same Domain conflict as retry and is kept (D-039).

**Rules:**
- `MaskedRecipient.Mask`: null, blank, no `@`, more than one `@`, an empty local part or an empty domain gives `"***"`. A one-character local part gives `"***@" + domain` (showing it would reveal the whole local part). Otherwise `local[0] + "***@" + domain`.
- `ListDeadLettersRequestHandler(IEmailOutboxStore store)` has no unit of work and no actor check (the Admin policy guards the route, as `ListAdminEventsRequestHandler` does). It maps `store.ListDeadLettersAsync(page, pageSize, ct)` to `PagedResponse<DeadLetterDto>(items, found.Page, found.PageSize, found.TotalCount)`. The mapping never reads `PayloadJson` or `ToAddress` except through `MaskedRecipient.Mask`.
- `RetryDeadLetterRequestHandler(IEmailOutboxStore store, IAdminEventRepository adminEvents, IAgentRepository agents, ICurrentAgentClaims currentAgent, IUnitOfWork unitOfWork, TimeProvider clock)`:

```csharp
await using var scope = await unitOfWork.BeginAsync(cancellationToken);
var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
if (actor.IsFailure) { return Result.Failure(actor.Errors[0]); }

var item = await store.GetAsync(id, cancellationToken);
if (item is null) { return Result.Failure(DeadLetterErrors.NotFound()); }

var attemptsBefore = item.Attempts;          // Retry resets the count to 0
var retried = item.Retry(clock);
if (retried.IsFailure) { return retried.ToResult(); }

store.Update(item);
AdminAudit.Record(adminEvents, AdminEventType.DeadLetterRetried, actor.Value, AdminSubjectType.EmailOutbox, item.Id,
    new { kind = item.Kind, attempts = attemptsBefore }, clock);
return await scope.CommitAsync(cancellationToken);
```

  `DeadLetterErrors.NotFound()` is `new(PersistenceErrorCodes.OutboxNotFound, "That email does not exist.", ResultErrorKind.NotFound)`. `DiscardDeadLetterRequestHandler` is identical except for `item.Discard()` (no attempt reset) and `AdminEventType.DeadLetterDiscarded`.
- `GetAsync` is tracked and takes no row lock, and `Update` needs that earlier `GetAsync` in the same scope. Do not call `ClaimBatchAsync`, `MarkSentAsync` or `MarkFailedAsync` from these handlers: they open their own transaction and would throw inside a unit of work.
- Retry does not clear `LastError`; it stays on the row until the next attempt overwrites it.

- [ ] **Step 1: Write the failing tests**

```csharp
// MaskedRecipientTests
[Theory]
[InlineData("ann@example.com", "a***@example.com")]
[InlineData("Ann.Lovelace@Example.com", "A***@Example.com")]
[InlineData("a@example.com", "***@example.com")]            // one-character local part
[InlineData("", "***")] [InlineData("   ", "***")] [InlineData(null, "***")]
[InlineData("no-at-sign", "***")] [InlineData("@example.com", "***")] [InlineData("ann@", "***")] [InlineData("a@b@c.com", "***")]
public void Recipients_are_masked(string? input, string expected) => MaskedRecipient.Mask(input).ShouldBe(expected);
[Fact] public void The_mask_never_contains_more_than_the_first_character_of_the_local_part()

// ListDeadLettersRequestHandlerTests (store substitute returning a PagedResult of two items built with EmailOutboxItem.Restore)
[Fact] public async Task A_page_maps_masked_recipient_ids_attempts_and_error_and_never_the_payload()
// DeadLetterDto fields as expected; Recipient == "a***@example.com"; JsonSerializer.Serialize(page) contains no payload text or full address.
[Fact] public async Task Paging_values_come_from_the_store_result()

// RetryDeadLetterRequestHandlerTests / DiscardDeadLetterRequestHandlerTests (substitutes, DeleteTagRequestHandlerTests style)
[Fact] public async Task A_dead_letter_is_retried_and_audited_with_kind_and_the_attempts_it_had()
// item restored DeadLettered, Attempts 5: Update received; item.Status Pending, Attempts 0; AdminEvent DeadLetterRetried, SubjectType EmailOutbox, SubjectId item.Id, PayloadJson {"kind":"ticket-confirmation","attempts":5}.
[Fact] public async Task A_row_that_is_not_dead_lettered_is_a_409_and_nothing_is_staged()
// error.Code == "outbox-not-dead-lettered", Kind Conflict; DidNotReceive Update and _events.Add. (Theory over Pending, Sending, Sent, Discarded.)
[Fact] public async Task An_unknown_id_is_404_outbox_not_found()
[Fact] public async Task A_deactivated_actor_is_refused_before_the_row_is_read()   // AgentErrors.Codes.Inactive; DidNotReceive GetAsync
[Fact] public async Task A_commit_conflict_is_returned()
// Discard tests: Status Discarded, DeadLetterDiscarded event, the same four failures.

// DeadLetterDtoShapeTests (Architecture tests; like CustomerDtoShapeTests)
[Fact] public void The_dead_letter_dto_exposes_no_address_payload_or_link_fields()
// property names of DeadLetterDto contain none of: ToAddress, Address, Email, Payload, PayloadJson, Link, Token.
```

`DeadLetterIntegrationTests` (real `PersistenceTestHost`; a `RecordingSender : IOutboundEmailSender` registered as a singleton *before* `AddTechStrapEmail`, whose `TryAddSingleton` then keeps it; copy `EmailDrainIntegrationTests.CreateHost`'s SMTP settings with host `localhost`, since only the sender is faked; seed one product with branding through `ProductRepository`):

```csharp
[Fact] public async Task A_retried_dead_letter_is_drained_on_the_next_worker_pass_and_a_discarded_one_never_is()
{
    // Arrange: two ticket-confirmation rows with a valid payload (TicketConfirmationEmail, as EmailDrainIntegrationTests.QueueAsync);
    // drive each to DeadLettered: loop OutboxRetryPolicy.MaxAttempts times { host.Clock.Advance(2 hours); claim = ClaimBatchAsync; MarkFailedAsync(row, worker, "smtp-permanent") }.
    // Act: RetryAsync(host, rowA); DiscardAsync(host, rowB), each through the real handler (Admin agent seeded, StubClaims as in DeleteTagIntegrationTests).
    // Assert:
    var drained = await DrainAsync(host, "w1");           // real IDrainEmailOutboxHandler
    drained.Sent.ShouldBe(1);
    sender.Sent.ShouldHaveSingleItem();                    // only row A was sent
    (await RowAsync(host, rowA)).Status.ShouldBe(OutboxStatus.Sent);
    (await RowAsync(host, rowB)).Status.ShouldBe(OutboxStatus.Discarded);
    (await ScalarAsync("SELECT count(*) FROM admin_events WHERE type IN ('DeadLetterRetried','DeadLetterDiscarded')")).ShouldBe(2);
}
[Fact] public async Task Retry_and_discard_of_a_live_row_change_nothing()   // Pending and Sent rows: 409 and the rows are unchanged, no admin event
```

`DeadLetterEndpointTests` (Api.Tests; `ApiFactory`, an admin client signed in via `GET /api/agents/me`, rows seeded through `IEmailOutbox` in a unit of work and then `UPDATE email_outbox SET status = 'DeadLettered', attempts = 5, last_error = 'smtp-permanent' WHERE id = ...` with `ApiTestDatabase.ExecuteAsync`):

```csharp
[Fact] public async Task The_list_shows_masked_recipients_newest_first_and_never_the_payload()
// GET /api/dead-letters -> 200, DeadLetterDto items in CreatedAt-descending order; raw body has no "payload" key, no "@example.com" other than after "***@", and the seeded portal link is absent.
[Fact] public async Task Retry_returns_204_and_the_row_leaves_the_list()
[Fact] public async Task Discard_returns_204_and_the_row_leaves_the_list()
[Fact] public async Task Retry_or_discard_of_a_pending_row_is_409_and_an_unknown_id_is_404()
[Fact] public async Task An_agent_who_is_not_an_admin_gets_403_on_all_three_routes()   // and anonymous gets 401
[Fact] public async Task Paging_is_honoured_and_clamped()   // pageSize=1 returns one item and TotalCount 2; pageSize=100000 is clamped to Paging.MaxPageSize
```

Add to `AgentAccessCoverageTests` (Review Focus 3, the route half; the generic tests there already probe each new route with a deactivated admin, an outsider and an agent-group member):

```csharp
[Fact]
public async Task The_five_06c_routes_are_admin_only()
{
    var database = await ApiTestDatabase.CreateAsync(postgres);
    await using var factory = new ApiFactory(settings: database.Settings);
    var adminOnly = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
        .Where(endpoint => IsAdminOnly(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.MethodInfo))
        .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
        .ToHashSet();

    adminOnly.ShouldBeSupersetOf(
    [
        "DELETE api/tickets/{id:guid}", "POST api/requesters/{id:guid}/erase",
        "GET api/dead-letters", "POST api/dead-letters/{id:guid}/retry", "DELETE api/dead-letters/{id:guid}",
    ]);
}
```

Add the usings the file lacks (`Microsoft.AspNetCore.Mvc.Controllers` is already there). If the raw route texts differ (for example constraint spelling), print `adminOnly` once and copy the real strings.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter "MaskedRecipientTests|ListDeadLettersRequestHandlerTests|RetryDeadLetterRequestHandlerTests|DiscardDeadLetterRequestHandlerTests"`
Expected: a build failure.

- [ ] **Step 3: Implement**

Write the DTO (with XML docs), the helper, the errors, the three handlers and the controller. Add `["DeadLettersController.List"] = 200`, `["DeadLettersController.Retry"] = 204` and `["DeadLettersController.Discard"] = 204` to `ControllerActions.ExpectedSuccess`.

```csharp
/// <summary>Dead-lettered emails (Admin only, D-006, D-022). Recipients are masked and payloads are never returned (D-039).</summary>
[ApiController]
[Route("api/dead-letters")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
public sealed class DeadLettersController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromServices] IListDeadLettersRequestHandler handler, CancellationToken cancellationToken,
        [FromQuery] int page = 1, [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await handler.HandleAsync(page, pageSize, cancellationToken)).ToActionResult(this, Ok);

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> Retry(Guid id, [FromServices] IRetryDeadLetterRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, NoContent);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Discard(Guid id, [FromServices] IDiscardDeadLetterRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, NoContent);
}
```

(`Paging` is `TechStrap.Application.Persistence.Paging`, imported as in `AdminEventsController`.)

- [ ] **Step 4: Run the tests**

Run: Application.Tests, the Architecture tests (`ContractNamingTests` accepts `DeadLetterDto`, and the handler rules accept the new handlers), `DeadLetterIntegrationTests`, `DeadLetterEndpointTests`, then Api.Tests in full (`ResultMappingTests`, `RoutePolicyCoverageTests`, `OpenApiSurfaceTests`, `AgentAccessCoverageTests`).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git diff --cached --stat   # after git add, before committing
git add src tests
git commit -m "feat(dead-letters): admin list, retry and discard of dead-lettered emails" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 7: Outbox retention (Worker)

**Review Focus pin (4):** retention must never delete a row it has to keep. Pinned by `PurgeEmailOutboxIntegrationTests.Only_finished_rows_older_than_the_window_are_deleted`.

**Files:**
- Create: `src/TechStrap.Application/Email/OutboxRetentionOptions.cs`
- Create: `src/TechStrap.Application/Email/PurgeEmailOutboxHandler.cs`
- Create: `src/TechStrap.Infrastructure/Email/OutboxRetentionServiceCollectionExtensions.cs`
- Create: `src/TechStrap.Worker/Outbox/OutboxRetentionWorker.cs`
- Modify: `src/TechStrap.Worker/Program.cs`. Add `AddTechStrapOutboxRetention` and `AddHostedService<OutboxRetentionWorker>()`, and change the header comment from "two background loops" to "three", naming the retention sweep (PHASE-06c).
- Modify: `src/TechStrap.Api/Startup/ApplicationHandlerRegistration.cs`. Add `nameof(IPurgeEmailOutboxHandler)` to `_workerOnly`.
- Modify: `tests/TechStrap.Api.Tests/HostFactory.cs`. `WorkerFactory` also defaults `["OutboxRetention:Enabled"] = "false"`.
- Modify: `src/TechStrap.Worker/.env.example`, `.env.production.example`, `docker-compose.production.yml`, `docker-compose.uat.yml`, `scripts/tests/ComposeFiles.Tests.ps1`
- Modify: `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`
- Create: `tests/TechStrap.Application.Tests/Email/PurgeEmailOutboxHandlerTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/OutboxRetentionOptionsTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/PurgeEmailOutboxIntegrationTests.cs`
- Create: `tests/TechStrap.Api.Tests/Worker/OutboxRetentionWorkerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Worker/OutboxRetentionWorkerBoundaryTests.cs`

**Interfaces:**
- Consumes: `IEmailOutboxStore.DeleteFinishedBeforeAsync(DateTimeOffset before, int batchSize, CancellationToken)` returning `int` (Task 3). It deletes `Sent` and `Discarded` rows with `created_at < before`, at most `batchSize` of them. Never `DeadLettered`, `Pending` or `Sending`.
- Produces:

```csharp
namespace TechStrap.Application.Email;

/// <summary>Outbox retention (D-039). Sent and Discarded rows older than Days are deleted; the age is measured from created_at for both.</summary>
public sealed class OutboxRetentionOptions
{
    public const string SectionName = "OutboxRetention";
    public bool Enabled { get; set; } = true;
    public int Days { get; set; } = 90;             // 1..3650
    public int IntervalMinutes { get; set; } = 60;  // 1..1440
    public int BatchSize { get; set; } = 500;       // 1..Paging.MaxBatchSize
}

public sealed record PurgeEmailOutboxResult(int Deleted);

public interface IPurgeEmailOutboxHandler
{
    Task<Result<PurgeEmailOutboxResult>> HandleAsync(CancellationToken cancellationToken);
}
```

**Rules:**
1. `PurgeEmailOutboxHandler(IEmailOutboxStore, TimeProvider, IOptions<OutboxRetentionOptions>, ILogger<PurgeEmailOutboxHandler>)`. `cutoff = clock.GetUtcNow() - TimeSpan.FromDays(Days)`. One call to `DeleteFinishedBeforeAsync(cutoff, BatchSize, ct)`. Log the count only, at Debug for 0 and Information otherwise. Never log a row, address or payload.
2. `created_at` decides for both statuses, so the index `ix_email_outbox_*` and the query stay simple. A row created exactly N days ago is kept (`<`, not `<=`).
3. This supersedes D-033's "payload scrub" for rows younger than N days: a Sent row's payload still holds a working portal link until the sweep removes the row. Do not add a scrub.
4. `AddTechStrapOutboxRetention(services, configuration)` is public, in `TechStrap.Infrastructure.Email`. It binds the section and validates on start (the messages name the key and range), then `TryAddScoped<IPurgeEmailOutboxHandler, PurgeEmailOutboxHandler>()`. The Api never calls it.
5. `OutboxRetentionWorker` is `AutoCloseWorker` with the names changed (same file shape: `BackgroundService`, constructor exactly `(IServiceScopeFactory, IOptions<OutboxRetentionOptions>, TimeProvider, ILogger<OutboxRetentionWorker>)`, `Enabled` check, one scope per iteration, `Task.Delay(idle, _clock, stoppingToken)`, error logging with a retry after the interval). The only logic change is the rerun rule: `return result.Value.Deleted >= _options.BatchSize;`. A failed result logs `result.Errors[0].Code` and waits.

- [ ] **Step 1: Write the failing tests**

```csharp
// PurgeEmailOutboxHandlerTests (Application.Tests/Email; substitutes, FakeTimeProvider)
[Fact] public async Task It_deletes_rows_older_than_the_configured_days_in_one_batch()
{
    var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
    var store = Substitute.For<IEmailOutboxStore>();
    store.DeleteFinishedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(7);
    var handler = new PurgeEmailOutboxHandler(store, clock,
        Options.Create(new OutboxRetentionOptions { Days = 30, BatchSize = 100 }), NullLogger<PurgeEmailOutboxHandler>.Instance);

    var result = await handler.HandleAsync(TestContext.Current.CancellationToken);

    result.Value.ShouldBe(new PurgeEmailOutboxResult(7));
    await store.Received(1).DeleteFinishedBeforeAsync(clock.GetUtcNow() - TimeSpan.FromDays(30), 100, TestContext.Current.CancellationToken);
}
[Fact] public async Task The_cancellation_token_reaches_the_store()   // a CancellationTokenSource token, Received with exactly it
```

```csharp
// OutboxRetentionOptionsTests (Infrastructure.IntegrationTests; same shape as AutoCloseOptionsTests, but through the public AddTechStrapOutboxRetention)
[Fact] public void Defaults_apply_when_nothing_is_configured()      // Enabled true, Days 90, IntervalMinutes 60, BatchSize 500
[Fact] public void Section_values_are_bound()                       // "OutboxRetention:Days" = "30" ...
[Theory]
[InlineData("OutboxRetention:Days", "0")] [InlineData("OutboxRetention:Days", "3651")] [InlineData("OutboxRetention:Days", "abc")]
[InlineData("OutboxRetention:IntervalMinutes", "0")] [InlineData("OutboxRetention:IntervalMinutes", "1441")]
[InlineData("OutboxRetention:BatchSize", "0")] [InlineData("OutboxRetention:BatchSize", "501")]
public void Invalid_values_fail_validation(string key, string value)   // Should.Throw<OptionsValidationException>(() => options.Value)
```

`PurgeEmailOutboxIntegrationTests` is a `PostgresIntegrationTestBase` (real Postgres, `PersistenceTestHost` with a `FakeTimeProvider`). Rows are inserted with raw SQL, because the Domain cannot create an old `Sent` row:

```csharp
public sealed class PurgeEmailOutboxIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private PersistenceTestHost NewHost(int batchSize = 500) => new(Database, configure: services =>
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OutboxRetention:Days"] = "90", ["OutboxRetention:BatchSize"] = batchSize.ToString() })
            .Build();
        services.AddTechStrapOutboxRetention(configuration);
    });

    private async Task<Guid> InsertAsync(string status, DateTimeOffset createdAt, Guid? ticketId = null)
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO email_outbox (id, kind, to_address, payload, ticket_id, status, attempts, next_attempt_at, created_at)
            VALUES (@id, 'ticket-confirmation', 'ann@example.com', '{}', @ticket, @status, 5, @created, @created)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("ticket", (object?)ticketId ?? DBNull.Value);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("created", createdAt);
        await command.ExecuteNonQueryAsync(Ct);
        return id;
    }

    private async Task<HashSet<Guid>> RemainingAsync()
    {
        await using var connection = new NpgsqlConnection(Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT id FROM email_outbox", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var ids = new HashSet<Guid>();
        while (await reader.ReadAsync(Ct)) { ids.Add(reader.GetGuid(0)); }
        return ids;
    }

    private static async Task<PurgeEmailOutboxResult> RunAsync(PersistenceTestHost host)
    {
        await using var scope = host.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IPurgeEmailOutboxHandler>().HandleAsync(Ct)).Value;
    }

    [Fact]
    public async Task Only_finished_rows_older_than_the_window_are_deleted()
    {
        await using var host = NewHost();
        var now = host.Clock.GetUtcNow();
        var sentOld = await InsertAsync("Sent", now.AddDays(-91));
        var discardedOld = await InsertAsync("Discarded", now.AddDays(-91));
        var sentAtCutoff = await InsertAsync("Sent", now.AddDays(-90));          // exactly N days: kept
        var sentRecent = await InsertAsync("Sent", now.AddDays(-89));
        var deadOld = await InsertAsync("DeadLettered", now.AddDays(-400));
        var pendingOld = await InsertAsync("Pending", now.AddDays(-400));
        var sendingOld = await InsertAsync("Sending", now.AddDays(-400));

        var result = await RunAsync(host);

        result.Deleted.ShouldBe(2);
        (await RemainingAsync()).ShouldBe([sentAtCutoff, sentRecent, deadOld, pendingOld, sendingOld], ignoreOrder: true);
        (await RemainingAsync()).ShouldNotContain(sentOld);
        (await RemainingAsync()).ShouldNotContain(discardedOld);
    }

    [Fact]
    public async Task A_row_one_tick_inside_the_window_is_kept_and_one_tick_outside_is_deleted()
    {
        await using var host = NewHost();
        var cutoff = host.Clock.GetUtcNow() - TimeSpan.FromDays(90);
        var inside = await InsertAsync("Sent", cutoff.AddTicks(10));    // 1 microsecond newer than the cutoff
        var outside = await InsertAsync("Sent", cutoff.AddTicks(-10));

        (await RunAsync(host)).Deleted.ShouldBe(1);
        (await RemainingAsync()).ShouldBe([inside]);
        (await RemainingAsync()).ShouldNotContain(outside);
    }

    [Fact]
    public async Task A_large_backlog_is_deleted_one_batch_per_run_and_a_second_run_is_a_no_op_when_empty()
    {
        await using var host = NewHost(batchSize: 2);
        for (var i = 0; i < 5; i++) { await InsertAsync("Sent", host.Clock.GetUtcNow().AddDays(-200 - i)); }

        (await RunAsync(host)).Deleted.ShouldBe(2);
        (await RunAsync(host)).Deleted.ShouldBe(2);
        (await RunAsync(host)).Deleted.ShouldBe(1);
        (await RunAsync(host)).Deleted.ShouldBe(0);
    }

    [Fact]
    public async Task A_retried_dead_letter_is_pending_again_and_is_still_not_purged()
    // insert DeadLettered (-400 d), resolve IEmailOutboxStore + IUnitOfWork, GetAsync -> Retry(clock) -> Update -> commit
    // (the same staging the Task 6 retry handler does), run the purge: 0 deleted, the row is Pending.
}
```

```csharp
// OutboxRetentionWorkerTests (Api.Tests/Worker). Copy AutoCloseWorkerTests with these changes: the handler is IPurgeEmailOutboxHandler,
// results are Result<PurgeEmailOutboxResult>.Success(new(n)), options are new OutboxRetentionOptions { Enabled, IntervalMinutes = 2, BatchSize = 3 },
// and the SignallingTimeProvider, WaitForAsync, WaitForTimerAsync and CollectingLogger helpers are copied verbatim.
[Fact] public async Task It_runs_again_immediately_when_the_batch_was_full_and_waits_otherwise()
// queue Done(3), Done(3), Done(1), Done(0): three runs and three scopes, then the timer; advancing 2 minutes gives the fourth run.
[Fact] public async Task It_passes_the_stopping_token_and_stops_promptly()
[Fact] public async Task An_unexpected_exception_is_logged_and_the_loop_resumes_after_the_interval()
[Fact] public async Task A_disabled_worker_never_runs()

// OutboxRetentionWorkerBoundaryTests: copy AutoCloseWorkerBoundaryTests with OutboxRetentionWorker and IOptions<OutboxRetentionOptions>.
```

Env and compose tests: extend `EnvExampleCompletenessTests` (Worker row) with `.. OptionKeys(typeof(OutboxRetentionOptions), OutboxRetentionOptions.SectionName),`. In `ComposeFiles.Tests.ps1` add, next to the EmailOutbox case:

```powershell
    It 'production and uat workers pass the outbox retention settings through with defaults (<file>)' -ForEach @(
        @{ file = 'docker-compose.production.yml' }
        @{ file = 'docker-compose.uat.yml' }
    ) {
        $envFile = Join-Path $TestDrive 'env-retention'
        New-ProductionEnvFile -Path $envFile
        $worker = (Get-ComposeConfig -File $file -EnvFile $envFile).Config.services.worker
        $worker.environment.OutboxRetention__Enabled | Should -Be 'true'
        $worker.environment.OutboxRetention__Days | Should -Be '90'
    }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter "PurgeEmailOutboxHandlerTests"`
Expected: a build failure (`OutboxRetentionOptions`, `PurgeEmailOutboxHandler` do not exist).

- [ ] **Step 3: Implement**

Application, `PurgeEmailOutboxHandler.cs` (the options file is as in Interfaces):

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Email;

public sealed record PurgeEmailOutboxResult(int Deleted);

public interface IPurgeEmailOutboxHandler
{
    Task<Result<PurgeEmailOutboxResult>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Deletes Sent and Discarded outbox rows older than <see cref="OutboxRetentionOptions.Days"/> (D-039), one batch per call. DeadLettered,
/// Pending and Sending rows are never touched. Logs counts only.
/// </summary>
public sealed class PurgeEmailOutboxHandler(
    IEmailOutboxStore store, TimeProvider clock, IOptions<OutboxRetentionOptions> options, ILogger<PurgeEmailOutboxHandler> logger) : IPurgeEmailOutboxHandler
{
    public async Task<Result<PurgeEmailOutboxResult>> HandleAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var cutoff = clock.GetUtcNow() - TimeSpan.FromDays(settings.Days);
        var deleted = await store.DeleteFinishedBeforeAsync(cutoff, settings.BatchSize, cancellationToken);
        logger.Log(deleted == 0 ? LogLevel.Debug : LogLevel.Information, "Outbox retention deleted {Deleted} finished rows.", deleted);
        return Result<PurgeEmailOutboxResult>.Success(new PurgeEmailOutboxResult(deleted));
    }
}
```

Infrastructure, `OutboxRetentionServiceCollectionExtensions.cs`:

```csharp
public static class OutboxRetentionServiceCollectionExtensions
{
    /// <summary>Everything the Worker needs for the outbox retention sweep: the validated options and the handler (D-039).</summary>
    public static IServiceCollection AddTechStrapOutboxRetention(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxRetentionOptions>()
            .Bind(configuration.GetSection(OutboxRetentionOptions.SectionName))
            .Validate(o => o.Days is >= 1 and <= 3650, "OutboxRetention:Days must be between 1 and 3650.")
            .Validate(o => o.IntervalMinutes is >= 1 and <= 1440, "OutboxRetention:IntervalMinutes must be between 1 and 1440.")
            .Validate(o => o.BatchSize is >= 1 and <= Paging.MaxBatchSize, $"OutboxRetention:BatchSize must be between 1 and {Paging.MaxBatchSize}.")
            .ValidateOnStart();
        services.TryAddScoped<IPurgeEmailOutboxHandler, PurgeEmailOutboxHandler>();
        return services;
    }
}
```

Then the worker, the `_workerOnly` entry, the `WorkerFactory` default, and the config files:
- `src/TechStrap.Worker/.env.example`, after the Auto-close block:

```
# --- Outbox retention (D-039) ---
# Sent and Discarded email rows older than DAYS are deleted (1..3650); dead letters are never deleted. The sweep runs every INTERVALMINUTES.
OUTBOXRETENTION__ENABLED=true
OUTBOXRETENTION__DAYS=90
OUTBOXRETENTION__INTERVALMINUTES=60
OUTBOXRETENTION__BATCHSIZE=500
```

- `.env.production.example`, after the Auto-close block:

```
# --- Outbox retention (D-039) ---
# Sent and Discarded outbox rows older than this many days are deleted by the Worker (1..3650). Dead letters are kept until an admin retries or discards them.
TECHSTRAP_OUTBOX_RETENTION_ENABLED=true
TECHSTRAP_OUTBOX_RETENTION_DAYS=90
```

- `docker-compose.production.yml` and `docker-compose.uat.yml`, worker `environment:` only, after the `EmailOutbox__PollIntervalSeconds` line:

```yaml
      OutboxRetention__Enabled: ${TECHSTRAP_OUTBOX_RETENTION_ENABLED:-true}
      OutboxRetention__Days: ${TECHSTRAP_OUTBOX_RETENTION_DAYS:-90}
```

The local `docker-compose.yml` needs no change (the defaults apply).

- [ ] **Step 4: Run the tests**

Run: Application.Tests; `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter "PurgeEmailOutboxIntegrationTests|OutboxRetentionOptionsTests"`; Api.Tests (`OutboxRetentionWorker*`, `EnvExampleCompletenessTests`, `ApplicationHandlerRegistrationTests`, `HostHealthSmokeTests`); Architecture tests; `pwsh -File scripts/Invoke-ScriptTests.ps1`.
Expected: PASS. `HandlerConstructorDependencyTests` and `HandlerShapeTests` accept the new handler.

- [ ] **Step 5: Commit**

```bash
git add src tests scripts docker-compose.production.yml docker-compose.uat.yml .env.production.example
git diff --cached --stat
git commit -m "feat(worker): sweep finished email outbox rows after the retention window" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 8: Serilog PII redaction

**Review Focus pin (5):** a requester email, token or hash must not reach a log line, including through an exception or a nested property. Pinned by `LogRedactionTests.Nested_and_exception_paths_carry_no_email_or_token` and the host-level flow test below.

**Verified against the packages (SyntaxCircus.AspNetCore.Serilog 0.1.4, Observability 0.1.2, Serilog 4.3):**
- `AddStandardSerilog(this IHostApplicationBuilder, Action<SerilogFileLoggingOptions>? configureFileLogging, Action<LoggerConfiguration>? configureEnrichment)`. `configureEnrichment` is an `Action<LoggerConfiguration>`, **not** an `Action<LoggerEnrichmentConfiguration>`.
- `telemetry.ConfigureSerilog` is `void ConfigureSerilog(LoggerConfiguration)`. So the wiring is a lambda that calls it and then adds the enricher with `logger.Enrich.With<PiiRedactionEnricher>()`.
- An enricher runs once per event before every sink, including the DI-registered `CollectingSink` the tests read, and `RenderMessage()` renders from the property values. So rewriting property values covers the rendered message too.
- An enricher **cannot** change `LogEvent.Exception` or the message template. That is the one residual path: an exception object attached to an event (`LogError(ex, ...)`) is rendered unchanged. The mitigations are in the Rules.

**Files:**
- Modify: `src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj`. Add `<PackageReference Include="SyntaxCircus.AspNetCore.Serilog" />` (the version is already central; no new package or version, and it brings the Serilog types in).
- Create: `src/TechStrap.Infrastructure/Logging/PiiRedactionEnricher.cs`
- Modify: `src/TechStrap.Api/Program.cs` and `src/TechStrap.Worker/Program.cs`
- Create: `tests/TechStrap.Api.Tests/Redaction/LogRedactionTests.cs`
- Create: `tests/TechStrap.Architecture.Tests/LoggingSafetyRules.cs` and `LoggingSafetyTests.cs`

**Interfaces:**
- Produces: `public sealed partial class PiiRedactionEnricher : ILogEventEnricher` in `TechStrap.Infrastructure.Logging`, with constants `EmailMarker = "[email]"`, `TokenMarker = "[token]"`, `HashMarker = "[hash]"`.

**Rules:**
1. Rewrite every property of every event, recursively: `ScalarValue` holding a `string`, every element of a `SequenceValue`, every property value of a `StructureValue` (type tag and property names unchanged) and both keys and values of a `DictionaryValue`. Other scalars (numbers, Guids, dates) are untouched. An object that Serilog stringified (an `Exception` passed as a template argument, any `ToString()` result) is a string scalar and is covered.
2. Replacements, applied in this order: `sha256:` plus 64 hex characters becomes `[hash]`; an email address becomes `[email]`; a 43-character base64url run (a whole run, not part of a longer one) becomes `[token]`.
3. Idempotent: the markers match none of the patterns. Properties that did not change keep their original `LogEventPropertyValue` instance (no allocation on the common path).
4. Residual risk, stated in the type's XML doc: names cannot be pattern-redacted, and an attached `Exception` is not rewritten. Application code never logs a requester name (verified by the host-level test) and logs exceptions only by type name; the two worker loops that attach an exception (`EmailOutboxWorker`, `AutoCloseWorker`, now also `OutboxRetentionWorker`) get Npgsql's default, which hides `PostgresException.Detail` unless `Include Error Detail` is set. `LoggingSafetyTests` guard that nothing sets it.
5. Admin and Portal handle no requester data yet. Do not wire them; the follow-up is recorded in D-039 (Task 1) for PHASE-07 and PHASE-09.

- [ ] **Step 1: Write the failing tests**

`LogRedactionTests` (Api.Tests, namespace `TechStrap.Api.Tests.Redaction`, constructor `(TestPostgres postgres)`, `IDisposable` deleting a temp `_storage` directory, and a `Ct` field, exactly as `SensitiveDataLeakTests` and `CustomerReplyEndpointTests` do). Use `Serilog.ILogger` fully qualified to avoid the `Microsoft.Extensions.Logging` name clash. Usings: `Serilog`, `Serilog.Events`, `TechStrap.Api.Tests.Intake` (`IntakeTestData`), `TechStrap.Api.Tests.Auth` (`ApiTestDatabase`), `TechStrap.Contracts.Http`, `TechStrap.Contracts.Intake`, `TechStrap.Contracts.Tickets` (`RequestNewAccessLinkRequest`), `TechStrap.Infrastructure.Logging`.

```csharp
private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE";   // exactly 43 base64url characters
private static readonly string Hash = "sha256:" + new string('a', 64);

private sealed class Sink : Serilog.Core.ILogEventSink
{
    public List<Serilog.Events.LogEvent> Events { get; } = [];
    public void Emit(Serilog.Events.LogEvent logEvent) => Events.Add(logEvent);
}

private static (Serilog.ILogger Log, Sink Sink) NewLogger()
{
    var sink = new Sink();
    return (new LoggerConfiguration().Enrich.With<PiiRedactionEnricher>().WriteTo.Sink(sink).CreateLogger(), sink);
}

private static string Everything(Serilog.Events.LogEvent e) =>
    string.Join('\n', [e.RenderMessage(), .. e.Properties.Values.Select(v => v.ToString())]);

[Theory]
[InlineData("ada@example.com", "[email]")]
[InlineData("mail Ada.Lovelace+tag@sub.example.co.uk now", "mail [email] now")]
public void Emails_in_scalar_values_are_redacted(string input, string expected)
{
    var (log, sink) = NewLogger();
    log.Information("Value {Value}", input);
    sink.Events.Single().Properties["Value"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(expected);
}

[Fact]
public void Tokens_and_hashes_are_redacted_but_other_text_is_not()
{
    var (log, sink) = NewLogger();
    var guid = Guid.NewGuid();
    log.Information("{Token} {Hash} {Id} {Number} {N32} {Long} {Short} {Count}",
        Token, Hash, guid, "ORB-42", guid.ToString("N"), Token + "x", Token[..42], 7);
    var e = sink.Events.Single();
    Value(e, "Token").ShouldBe("[token]");
    Value(e, "Hash").ShouldBe("[hash]");
    e.Properties["Id"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(guid);
    Value(e, "Number").ShouldBe("ORB-42");
    Value(e, "N32").ShouldBe(guid.ToString("N"));      // 32 characters: not a token
    Value(e, "Long").ShouldBe(Token + "x");            // a 44-character run is not a token
    Value(e, "Short").ShouldBe(Token[..42]);
    e.Properties["Count"].ShouldBeOfType<ScalarValue>().Value.ShouldBe(7);
    static object? Value(Serilog.Events.LogEvent e, string name) => ((ScalarValue)e.Properties[name]).Value;
}

[Fact]
public void Nested_and_exception_paths_carry_no_email_or_token()
{
    var (log, sink) = NewLogger();
    var person = new { Name = "Ada", Email = "ada@example.com", Aliases = new[] { "ada@example.com" } };
    log.Information("nested {@Person} {Seq} {Dict}", person,
        new[] { "ada@example.com", "ok" }, new Dictionary<string, string> { ["ada@example.com"] = Token });
    // an exception passed as a template argument is stringified by Serilog into a scalar, message and all
    log.Warning("failed {Error}", new InvalidOperationException($"duplicate key (email)=(ada@example.com) token {Token} {Hash}"));

    foreach (var e in sink.Events)
    {
        var text = Everything(e);
        text.ShouldNotContain("ada@example.com");
        text.ShouldNotContain(Token);
        text.ShouldNotContain(Hash);
    }

    Everything(sink.Events[0]).ShouldContain("Name: \"Ada\"");     // structure shape and non-PII values survive
    Everything(sink.Events[0]).ShouldContain("ok");
}

[Fact]
public void Redaction_is_idempotent()   // log "x {V}" with "ada@example.com", feed the produced LogEvent through the enricher again: same rendered text
```

Wiring and host-level tests:

```csharp
[Fact]
public async Task The_api_host_redacts_what_application_code_logs()
{
    await using var factory = new ApiFactory();
    var logger = factory.Services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("RedactionProbe");

    logger.LogWarning("Probe {Email} {Token} {Hash}", "ada@example.com", Token, Hash);

    var probe = factory.LogSink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Probe ", StringComparison.Ordinal));
    probe.RenderMessage().ShouldBe("Probe \"[email]\" \"[token]\" \"[hash]\"");
}

[Fact] public async Task The_worker_host_redacts_what_application_code_logs()
// same probe; host = new WorkerFactory(settings: new Dictionary<string, string?> { ["ConnectionStrings:TechStrap"] = await postgres.CreateDatabaseAsync() })

[Fact]
public async Task The_intake_customer_and_lost_link_flow_logs_no_email_name_or_token()
{
    var database = await ApiTestDatabase.CreateAsync(postgres);
    var settings = new Dictionary<string, string?>(database.Settings) { ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test", ["Storage:Local:RootPath"] = _storage };
    await using var factory = new ApiFactory(settings: settings);
    var seed = await IntakeTestData.SeedAsync(factory.Services, Ct);
    using var client = factory.CreateClient();

    // 1. intake with a key (the same request shape SensitiveDataLeakTests uses)
    using var intake = new HttpRequestMessage(HttpMethod.Post, "/api/intake/tickets")
    {
        Content = JsonContent.Create(new SubmitTicketRequest("ada@example.com", "Ada Lovelace", "Help", "Please help", null, null)),
    };
    intake.Headers.Add(HeaderNames.ApiKey, seed.OrbitlyTrusted);
    using var submitted = await client.SendAsync(intake, Ct);
    submitted.StatusCode.ShouldBe(HttpStatusCode.Created);
    var viewUrl = (await submitted.Content.ReadFromJsonAsync<SubmitTicketResponse>(Ct))!.ViewUrl!;
    var token = viewUrl[(viewUrl.IndexOf("/t/", StringComparison.Ordinal) + 3)..].Split('?', '#', '/')[0];

    // 2. the customer reads the ticket with the token
    using var read = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket");
    read.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
    (await client.SendAsync(read, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

    // 3. a lost-link request for the same address, and one for an unknown address
    foreach (var address in new[] { "ada@example.com", "nobody@example.com" })
    {
        using var lost = await client.PostAsJsonAsync("/api/customer/access-link", new RequestNewAccessLinkRequest(address), Ct);
        lost.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    factory.LogSink.Events.ShouldNotBeEmpty();
    foreach (var e in factory.LogSink.Events)
    {
        var text = Everything(e) + "\n" + e.Exception;
        foreach (var needle in new[] { "ada@example.com", "nobody@example.com", "Ada Lovelace", token })
        {
            text.ShouldNotContain(needle, Case.Insensitive);
        }
    }
}
```

`LoggingSafetyRules` (pure, like `AppendOnlyBypassRules`):

```csharp
public static partial class LoggingSafetyRules
{
    [GeneratedRegex(@"EnableSensitiveDataLogging|Include\s*Error\s*Detail|IncludeErrorDetail", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Forbidden();

    public static IReadOnlyList<string> FindViolations(IEnumerable<(string Path, string Text)> files) =>
        [.. files.Where(f => Forbidden().IsMatch(f.Text)).Select(f => $"{f.Path} turns on sensitive EF logging or Npgsql error detail (PII can reach logs).")];

    /// <summary>Every .cs, .json, .yml, .props and .csproj under src (not bin, obj or node_modules) plus the root compose files and .env examples.</summary>
    public static IEnumerable<(string Path, string Text)> Sources(string repositoryRoot)
    {
        var skipped = new[] { "bin", "obj", "node_modules" }.Select(d => $"{Path.DirectorySeparatorChar}{d}{Path.DirectorySeparatorChar}").ToArray();
        string[] extensions = [".cs", ".json", ".yml", ".yaml", ".props", ".csproj", ".example"];
        var src = Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*", SearchOption.AllDirectories)
            .Where(f => !skipped.Any(f.Contains) && (extensions.Contains(Path.GetExtension(f)) || Path.GetFileName(f).StartsWith(".env", StringComparison.Ordinal)));
        var root = Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(f => Path.GetFileName(f).StartsWith("docker-compose", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith(".env", StringComparison.Ordinal));
        return src.Concat(root).Select(f => (Path.GetRelativePath(repositoryRoot, f), File.ReadAllText(f)));
    }
}
```

```csharp
// LoggingSafetyTests (Architecture.Tests; ProjectGraph.FindRepositoryRoot())
[Fact] public void No_source_or_deployment_file_enables_sensitive_logging_or_npgsql_error_detail()
// sources = LoggingSafetyRules.Sources(root).ToList(); sources.ShouldContain(s => s.Path.EndsWith("TechStrapDatabase.cs")); FindViolations(sources).ShouldBeEmpty()
[Theory]
[InlineData("options.EnableSensitiveDataLogging();")]
[InlineData("Host=db;Include Error Detail=true")]
[InlineData("ConnectionStrings__TechStrap: Host=db;IncludeErrorDetail=true")]
public void A_setting_that_leaks_data_is_flagged(string text)      // FindViolations([("Bad.cs", text)]).ShouldNotBeEmpty()
[Fact] public void Ordinary_logging_configuration_passes()           // "options.EnableDetailedErrors" is NOT in the pattern; "Serilog" text passes
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "LogRedactionTests"` then `dotnet test --project tests/TechStrap.Architecture.Tests -c Release --filter "LoggingSafetyTests"`
Expected: build failures (`PiiRedactionEnricher`, `LoggingSafetyRules` do not exist).

- [ ] **Step 3: Implement**

```csharp
using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace TechStrap.Infrastructure.Logging;

/// <summary>
/// Rewrites PII-shaped text in every property value before any sink sees the event (D-039): email addresses, 43-character access tokens and
/// "sha256:" hashes. It cannot touch LogEvent.Exception or the template, and it cannot recognise a name; application code never logs either
/// (exceptions are logged by type name, requesters by id).
/// </summary>
public sealed partial class PiiRedactionEnricher : ILogEventEnricher
{
    public const string EmailMarker = "[email]";
    public const string TokenMarker = "[token]";
    public const string HashMarker = "[hash]";

    [GeneratedRegex(@"sha256:[0-9a-fA-F]{64}")]
    private static partial Regex HashPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_\-])[A-Za-z0-9_\-]{43}(?![A-Za-z0-9_\-])")]
    private static partial Regex TokenPattern();

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToArray())
        {
            var redacted = Redact(value);
            if (!ReferenceEquals(redacted, value))
            {
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
            }
        }
    }

    internal static string RedactText(string text) =>
        TokenPattern().Replace(EmailPattern().Replace(HashPattern().Replace(text, HashMarker), EmailMarker), TokenMarker);

    private static LogEventPropertyValue Redact(LogEventPropertyValue value)
    {
        switch (value)
        {
            case ScalarValue { Value: string text }:
                var clean = RedactText(text);
                return clean == text ? value : new ScalarValue(clean);
            case SequenceValue sequence:
                var elements = sequence.Elements.Select(Redact).ToArray();
                return Same(elements, sequence.Elements) ? value : new SequenceValue(elements);
            case StructureValue structure:
                var properties = structure.Properties.Select(p => new LogEventProperty(p.Name, Redact(p.Value))).ToArray();
                return Same(properties.Select(p => p.Value), structure.Properties.Select(p => p.Value)) ? value : new StructureValue(properties, structure.TypeTag);
            case DictionaryValue dictionary:
                var entries = dictionary.Elements.Select(pair => KeyValuePair.Create((ScalarValue)Redact(pair.Key), Redact(pair.Value))).ToArray();
                return Same(entries.Select(e => (LogEventPropertyValue)e.Key), dictionary.Elements.Keys) && Same(entries.Select(e => e.Value), dictionary.Elements.Values)
                    ? value
                    : new DictionaryValue(entries);
            default:
                return value;
        }
    }

    private static bool Same(IEnumerable<LogEventPropertyValue> left, IEnumerable<LogEventPropertyValue> right) =>
        left.Zip(right, ReferenceEquals).All(same => same);
}
```

(`Same(entries.Select(e => (LogEventPropertyValue)e.Key), dictionary.Elements.Keys)`: the second argument is `IEnumerable<ScalarValue>`, which converts by covariance. If the compiler objects, project both sides with `.Cast<LogEventPropertyValue>()`.)

Wiring, in both `Program.cs` files (add `using TechStrap.Infrastructure.Logging;`):

```csharp
builder.AddStandardSerilog(configureEnrichment: logger =>
{
    telemetry.ConfigureSerilog(logger);
    logger.Enrich.With<PiiRedactionEnricher>();
});
```

- [ ] **Step 4: Run the tests**

Run: the two filters, then the full Api.Tests (`SensitiveDataLeakTests` and the host smoke tests must still pass), Architecture tests (`ProjectReferenceDirectionTests` allow Infrastructure to reference packages) and `pwsh -File scripts/Check-PackageVersions.ps1` (no inline versions, nothing new).
Expected: PASS. If the host-level flow test finds a personal value in a log event, fix the product code that logged it; do not weaken the test.

- [ ] **Step 5: Commit**

```bash
git add src tests
git diff --cached --stat
git commit -m "feat(logging): redact emails, access tokens and hashes from every log property" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 9: Multipart hardening

**Files:**
- Modify: `src/TechStrap.Api/Startup/IntakeHosting.cs`
- Modify: `src/TechStrap.Api/Controllers/CustomerTicketsController.cs`, `PublicIntakeController.cs`, `TicketsController.cs`. Remove `[Consumes("multipart/form-data")]` from `Reply`, `Submit` and `Reply`. Keep every other attribute.
- Modify: `tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs` (`IsMultipart`)
- Modify: `tests/TechStrap.Api.Tests/Customer/CustomerErrorPathCacheTests.cs` (the `reply-415` case)
- Modify: `tests/TechStrap.Api.Tests/OpenApiSurfaceTests.cs`
- Create: `tests/TechStrap.Api.Tests/Intake/ReadFormBeforeBindingTests.cs`
- Create: `tests/TechStrap.Api.Tests/Intake/MultipartRouteContractTests.cs`

**Interfaces:**
- Produces, in `IntakeHosting.cs`: `ReadFormBeforeBindingAttribute` gains `IApiRequestMetadataProvider`, plus constants `MalformedCode = "request-malformed"` and `UnsupportedMediaTypeCode = "unsupported-media-type"`. `RequestTooLargeMiddleware.IsTooLarge` becomes `internal static` so the filter reuses it.

**Findings that drive the design (checked by building a scratch copy of the repo):**
- Removing `[Consumes]` alone makes `/openapi/v1.json` document the three routes as `application/x-www-form-urlencoded` (it was `multipart/form-data`). The `OpenApiSurfaceTests` that exists only compares route lists, so it would not notice.
- A custom `IApiRequestMetadataProvider` on the filter restores `multipart/form-data` in the document and adds no endpoint-matching constraint. `[Consumes]` is what made the endpoint carry no authorization metadata on a content-type mismatch (so the fallback policy answered 401); the metadata provider does not.
- A body cut short by the client cannot be seen end to end: Kestrel treats the half-closed connection as an abort and the request is cancelled, so no response can be read. The `IOException` rule is therefore tested by invoking the filter directly with a throwing body stream (this also pins the 413 pass-through).
- `AgentAccessCoverageTests.IsMultipart` reads `IAcceptsMetadata`, which disappears with `[Consumes]`. It must detect the routes by the filter attribute instead (`ReadFormBeforeBindingAttribute` is visible to `TechStrap.Api.Tests` through `InternalsVisibleTo`). Authorization runs before resource filters, so the 401, 403 and `agent-inactive` probes are unaffected.

**Rules for the filter:**
1. `!request.HasFormContentType` → short-circuit **415** problem+json, code `unsupported-media-type` (the problem `type`, as `request-too-large` does). The route's own authorization policy has already run, so the customer route (`Public`) answers 415 and an unauthenticated agent probe still answers 401.
2. `ReadFormAsync` throws `InvalidDataException` → unchanged (model binding reports 400).
3. `ReadFormAsync` throws an `IOException` for which `RequestTooLargeMiddleware.IsTooLarge` is false → short-circuit **400** problem+json, code `request-malformed`. A 413 (also an `IOException`, possibly wrapped) is rethrown to the middleware.
4. Short-circuit by setting `context.Result = new ObjectResult(new ProblemDetails { Status, Type = code, Title, Detail }) { StatusCode = status }` and returning without calling `next`.

- [ ] **Step 1: Write the failing tests**

`ReadFormBeforeBindingTests` (no database; it invokes the filter directly):

```csharp
public sealed class ReadFormBeforeBindingTests
{
    private sealed class ThrowingStream(Exception exception) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw exception;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw exception;
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw exception;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<(ResourceExecutingContext Context, bool NextCalled)> RunAsync(string? contentType, Stream body)
    {
        var http = new DefaultHttpContext();
        http.Request.ContentType = contentType;
        http.Request.Body = body;
        var context = new ResourceExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), [], []);
        var nextCalled = false;
        await new ReadFormBeforeBindingAttribute().OnResourceExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResourceExecutedContext(context, []));
        });
        return (context, nextCalled);
    }

    [Fact]
    public async Task A_body_cut_short_is_a_400_request_malformed()
    {
        var (context, next) = await RunAsync("multipart/form-data; boundary=xyz",
            new ThrowingStream(new BadHttpRequestException("Unexpected end of request content.", StatusCodes.Status400BadRequest)));

        next.ShouldBeFalse();
        var result = context.Result.ShouldBeOfType<ObjectResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        result.Value.ShouldBeOfType<ProblemDetails>().Type.ShouldBe("request-malformed");
    }

    [Fact]
    public async Task A_413_is_left_for_the_request_too_large_middleware()
    {
        await Should.ThrowAsync<BadHttpRequestException>(() => RunAsync("multipart/form-data; boundary=xyz",
            new ThrowingStream(new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge))));
    }

    [Fact]
    public async Task A_413_wrapped_in_another_io_exception_is_still_left_alone()
    {
        await Should.ThrowAsync<IOException>(() => RunAsync("multipart/form-data; boundary=xyz",
            new ThrowingStream(new IOException("wrapped", new BadHttpRequestException("too big", StatusCodes.Status413PayloadTooLarge)))));
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData(null)]
    public async Task A_body_that_is_not_a_form_is_a_415_unsupported_media_type(string? contentType)
    {
        var (context, next) = await RunAsync(contentType, new MemoryStream());

        next.ShouldBeFalse();
        var result = context.Result.ShouldBeOfType<ObjectResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status415UnsupportedMediaType);
        result.Value.ShouldBeOfType<ProblemDetails>().Type.ShouldBe("unsupported-media-type");
    }

    [Theory]
    [InlineData(typeof(TechStrap.Api.Controllers.CustomerTicketsController), "Reply")]
    [InlineData(typeof(TechStrap.Api.Controllers.PublicIntakeController), "Submit")]
    [InlineData(typeof(TechStrap.Api.Controllers.TicketsController), "Reply")]
    public void All_three_multipart_routes_carry_the_filter_and_no_consumes_constraint(Type controller, string action)
    {
        var method = controller.GetMethod(action)!;

        method.IsDefined(typeof(ReadFormBeforeBindingAttribute), inherit: false).ShouldBeTrue();
        method.IsDefined(typeof(ConsumesAttribute), inherit: false).ShouldBeFalse();
    }
}
```

`MultipartRouteContractTests` (routes answer 415 for JSON; the first two need no database):

```csharp
public sealed class MultipartRouteContractTests(TestPostgres postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static HttpRequestMessage Json(string path) =>
        new(HttpMethod.Post, path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData("/api/customer/ticket/replies")]                 // Public policy: 415, no longer the fallback policy's 401
    [InlineData("/api/public/products/orbitly/tickets")]
    public async Task A_json_body_on_a_public_multipart_route_is_415_problem_json(string path)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var request = Json(path);

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        problem.Type.ShouldBe("unsupported-media-type");
    }

    [Fact]
    public async Task The_agent_reply_route_runs_its_policy_before_the_415()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var anonymous = factory.CreateClient();
        using var agent = TicketTestData.AgentClient(factory, "sam");
        (await agent.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        var path = $"/api/tickets/{Guid.NewGuid()}/replies";

        using var unauthenticated = Json(path);
        using var withAgent = Json(path);

        (await anonymous.SendAsync(unauthenticated, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await agent.SendAsync(withAgent, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }
}
```

In `CustomerErrorPathCacheTests`, change `[InlineData("reply-415", HttpStatusCode.Unauthorized)]` to `HttpStatusCode.UnsupportedMediaType` and replace the two-line comment above it with `// The filter answers a non-multipart body with 415 after the route's own (Public) policy has run.`

In `AgentAccessCoverageTests`, replace `IsMultipart` and its summary:

```csharp
    /// <summary>True when the action reads multipart/form-data (it carries ReadFormBeforeBinding). Authorization runs before that filter, so a JSON probe would also reach 401/403; the multipart body keeps the probe realistic.</summary>
    private static bool IsMultipart(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.MethodInfo
            .IsDefined(typeof(TechStrap.Api.Startup.ReadFormBeforeBindingAttribute), inherit: false) == true;
```

Add to `OpenApiSurfaceTests`:

```csharp
    [Theory]
    [InlineData("/api/customer/ticket/replies")]
    [InlineData("/api/public/products/{productKey}/tickets")]
    [InlineData("/api/tickets/{id}/replies")]
    public async Task The_multipart_routes_document_a_multipart_request_body(string path)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));

        var content = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("post").GetProperty("requestBody").GetProperty("content");

        content.EnumerateObject().Select(media => media.Name).ShouldBe(["multipart/form-data"]);
    }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "ReadFormBeforeBindingTests|MultipartRouteContractTests"`
Expected: the unit tests fail (a cut-short body propagates, JSON is not rejected by the filter) and the 415 tests answer 401 or 409. `OpenApiSurfaceTests` passes now and must still pass after the change.

- [ ] **Step 3: Implement**

In `IntakeHosting.cs` add `using Microsoft.AspNetCore.Mvc;`, `using Microsoft.AspNetCore.Mvc.ApiExplorer;`, `using Microsoft.AspNetCore.Mvc.Formatters;`, change `IsTooLarge` to `internal static`, and replace the attribute's body:

```csharp
[AttributeUsage(AttributeTargets.Method)]
internal sealed class ReadFormBeforeBindingAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter, IApiRequestMetadataProvider
{
    public const string MalformedCode = "request-malformed";
    public const string UnsupportedMediaTypeCode = "unsupported-media-type";

    // RequestFormLimits (and RequestSizeLimit) filters default to Order 900 and lower Order runs first. This must run after them, or the
    // form is parsed under Kestrel's 30 MB / 128 MB defaults instead of the endpoint limits.
    public int Order => 1000;

    /// <summary>Documents the body as multipart/form-data in OpenAPI without [Consumes], which would hide the route's authorization policy behind a 401.</summary>
    public void SetContentTypes(MediaTypeCollection contentTypes)
    {
        contentTypes.Clear();
        contentTypes.Add("multipart/form-data");
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!request.HasFormContentType)
        {
            context.Result = Problem(StatusCodes.Status415UnsupportedMediaType, UnsupportedMediaTypeCode, "Unsupported media type", "This endpoint accepts multipart/form-data.");
            return;
        }

        try
        {
            await request.ReadFormAsync(context.HttpContext.RequestAborted);
        }
        catch (InvalidDataException)
        {
            // Malformed multipart: model binding reports it as a 400.
        }
        catch (IOException ex) when (!RequestTooLargeMiddleware.IsTooLarge(ex))
        {
            // A body cut short (BadHttpRequestException is an IOException) is the client's fault, not a 500. A 413 is left to RequestTooLargeMiddleware.
            context.Result = Problem(StatusCodes.Status400BadRequest, MalformedCode, "Malformed request", "The request body could not be read.");
            return;
        }

        await next();
    }

    private static ObjectResult Problem(int status, string code, string title, string detail) =>
        new(new ProblemDetails { Status = status, Type = code, Title = title, Detail = detail }) { StatusCode = status };
}
```

Then remove `[Consumes(...)]` from the three actions and make the two test edits above.

- [ ] **Step 4: Run the tests**

Run: the Task 9 filter; then the full Api.Tests, in particular `AgentAccessCoverageTests`, `RoutePolicyCoverageTests`, `CustomerErrorPathCacheTests`, `PublicIntakeEndpointTests` (the filter-order test still finds the attribute by name), `OpenApiSurfaceTests`, `CustomerReplyEndpointTests` and `AgentReplyEndpointTests`; then the Architecture tests.
Expected: PASS. If `ControllerBoundaryTests` complains about the extra interface on the attribute, it does not (it inspects controller members, not filters).

- [ ] **Step 5: Commit**

```bash
git add src tests
git diff --cached --stat
git commit -m "fix(api): answer a cut-short multipart body 400 and a non-multipart body 415 on all three form routes" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 10: Sanitised-name dedupe

**Files:**
- Create: `src/TechStrap.Domain/Tickets/AttachmentFileName.cs`
- Modify: `src/TechStrap.Infrastructure/Attachments/AttachmentStore.cs`. Delete `SafeDisplayName`, `SafeCut` and `FallbackName`, call `AttachmentFileName.Sanitize`, and remove `using` lines the build reports as unused (`System.Globalization`, `System.Text`, `TechStrap.Domain.Rules`), adding `using TechStrap.Domain.Tickets;`.
- Modify: `src/TechStrap.Application/Tickets/Customer/AddCustomerReplyRequestHandler.cs` (`SameFiles`)
- Create: `tests/TechStrap.Domain.Tests/Tickets/AttachmentFileNameTests.cs`
- Modify: `tests/TechStrap.Application.Tests/Tickets/Customer/AddCustomerReplyRequestHandlerTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace TechStrap.Domain.Tickets;

/// <summary>The one rule for the display name stored with an attachment (D-039). The store saves this form, so anything that compares a
/// customer-supplied name with a stored one must sanitise the customer's first.</summary>
public static class AttachmentFileName
{
    public const string Fallback = "attachment";
    public static string Sanitize(string? fileName);
}
```

**Rules:**
- `Sanitize` is `AttachmentStore.SafeDisplayName` moved verbatim: take the last path segment after turning `\` into `/`; drop control and Unicode Format characters; trim; `""`, `"."` and `".."` become `Fallback`; a name longer than `DomainLimits.FileNameMaxLength` is cut, keeping the extension when it is shorter than half the limit, and never in the middle of a surrogate pair. Behaviour must not change, so the existing `AttachmentStoreTests` pass untouched.
- `AddCustomerReplyRequestHandler.SameFiles` compares the stored names with `incoming.Select(file => AttachmentFileName.Sanitize(file.FileName))`.

- [ ] **Step 1: Write the failing tests**

```csharp
// AttachmentFileNameTests (Domain.Tests/Tickets)
[Theory]
[InlineData("shot.png", "shot.png")]
[InlineData("C:\\fakepath\\shot.png", "shot.png")]
[InlineData("../../etc/passwd.txt", "passwd.txt")]
[InlineData("  spaced.png  ", "spaced.png")]
[InlineData("a\u0007b\u200Ec.png", "abc.png")]          // control and Format characters are dropped
[InlineData("", "attachment")]
[InlineData(null, "attachment")]
[InlineData(".", "attachment")]
[InlineData("..", "attachment")]
[InlineData("folder/", "attachment")]
public void Names_are_reduced_to_a_safe_display_name(string? input, string expected) =>
    AttachmentFileName.Sanitize(input).ShouldBe(expected);

[Fact]
public void A_long_name_keeps_its_extension_and_is_never_cut_inside_a_surrogate_pair()
{
    var name = new string('a', 250) + "😀" + new string('b', 10) + ".png";

    var sanitised = AttachmentFileName.Sanitize(name);

    sanitised.Length.ShouldBeLessThanOrEqualTo(DomainLimits.FileNameMaxLength);
    sanitised.ShouldEndWith(".png");
    Should.NotThrow(() => new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(sanitised));
}

[Fact]
public void A_name_with_a_very_long_extension_is_cut_without_one()   // "x" + ".".PadRight(300, 'e') -> length <= limit, no exception
[Fact]
public void Sanitising_twice_changes_nothing()                       // for every case above: Sanitize(Sanitize(x)) == Sanitize(x)
```

```csharp
// AddCustomerReplyRequestHandlerTests additions (reuse GivenTicket, Png, Reply and ListRecentFollowUpsAsync setup from the existing replay test)
[Theory]
[InlineData("C:\\fakepath\\shot.png")]
[InlineData("../shot.png")]
[InlineData("  shot.png ")]
public async Task A_double_submit_replays_when_the_store_rewrote_the_name(string incomingName)
{
    var parent = GivenTicket(TicketStatus.Closed);
    var followUpId = Guid.NewGuid();
    _tickets.ListRecentFollowUpsAsync(parent.Id, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
        .Returns([new FollowUpCandidate(followUpId, "ORB-70", Guid.NewGuid(), "<p>It broke again</p>", _clock.GetUtcNow(), ["shot.png"])]);

    var result = await Reply("It broke again", [Png(incomingName)]);

    result.Value.TicketNumber.ShouldBe("ORB-70");
    _tickets.DidNotReceive().Add(Arg.Any<Ticket>());
    await _store.DidNotReceive().SaveAsync(Arg.Any<Guid>(), Arg.Any<IncomingAttachment>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task Two_names_that_both_sanitise_to_the_fallback_still_compare_by_count_and_name()
// existing ["attachment", "attachment"]; incoming ".." and "." -> replays; incoming ".." only -> a new follow-up.
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Domain.Tests -c Release --filter AttachmentFileNameTests`
Expected: a build failure (`AttachmentFileName` does not exist). After Step 3 the handler theory is the one that proves the fix: before the `SameFiles` change it creates a second follow-up.

- [ ] **Step 3: Implement**

Create `AttachmentFileName` by moving the body of `SafeDisplayName` and `SafeCut` into it (`using System.Globalization; using System.Text; using TechStrap.Domain.Rules;`, `Fallback` in place of `FallbackName`). Make the store and the handler changes listed under Files.

- [ ] **Step 4: Run the tests**

Run: Domain.Tests; Application.Tests (`AddCustomerReplyRequestHandlerTests`); `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter "AttachmentStoreTests|CustomerReplyIntegrationTests"`; Architecture tests.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests
git diff --cached --stat
git commit -m "fix(tickets): compare sanitised attachment names when deduplicating follow-ups" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 11: End-to-end, leak checks and closing docs

**Files:**
- Modify: `tests/TechStrap.Api.Tests/Tickets/TicketLifecycleEndToEndTests.cs`. Add three tests and two helpers.
- Modify: `docs/development/TICKET-OPERATIONS.md`
- Modify: `docs/architecture/PHASE-06-ticket-operations.md`
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` and `docs/architecture/00-DISCOVERY-INDEX.md`

**Interfaces:**
- Consumes: `DELETE /api/tickets/{id}` (204, Admin), `POST /api/requesters/{id}/erase` (204, Admin), `GET /api/dead-letters`, `POST /api/dead-letters/{id}/retry` and `DELETE /api/dead-letters/{id}` (Task 4 to 6); `DeadLetterDto` in `TechStrap.Contracts.DeadLetters`; `AdminEventDto` in `TechStrap.Contracts.AdminEvents`; the existing class helpers `TokenFromPortalLink`, `TokenFromLink`, `ScalarStringAsync`, `CustomerViewAsync`, `CustomerReplyAsync`, `OutboxKindsAsync`.

- [ ] **Step 1: Write the end-to-end tests**

Add the usings `TechStrap.Contracts.AdminEvents` and `TechStrap.Contracts.DeadLetters`, then these helpers to the class:

```csharp
    private async Task<(ApiFactory Factory, ApiTestDatabase Database)> StartAsync()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings)
        {
            ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://help.test",
            ["Storage:Local:RootPath"] = _storage,
        };
        var factory = new ApiFactory(settings: settings);
        await IntakeTestData.SeedAsync(factory.Services, Ct);
        return (factory, database);
    }

    /// <summary>A client in the admin group; GET /api/agents/me provisions its agent row.</summary>
    private static async Task<HttpClient> AdminClientAsync(ApiFactory factory)
    {
        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com", name: "Ada Admin"));
        (await admin.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        return admin;
    }

    private static async Task SubmitAsync(HttpClient anonymous, string body, bool withPng = false)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent("ada@example.com"), "email" }, { new StringContent("Ada"), "name" },
            { new StringContent("Cannot log in"), "subject" }, { new StringContent(body), "body" },
        };
        if (withPng)
        {
            var file = new ByteArrayContent(Png);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
            form.Add(file, "attachments", "shot.png");
        }

        using var submitted = await anonymous.PostAsync("/api/public/products/orbitly/tickets", form, Ct);
        submitted.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
```

**Test A, delete.**

```csharp
    [Fact]
    public async Task An_admin_deletes_a_closed_ticket_and_its_follow_up_survives_unlinked()
    {
        var (factory, database) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        (await sam.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        using var admin = await AdminClientAsync(factory);
        using var anonymous = factory.CreateClient();

        await SubmitAsync(anonymous, "Please help");
        var token = TokenFromPortalLink(await ScalarStringAsync(database, "SELECT payload::text FROM email_outbox WHERE kind = 'ticket-confirmation'"));
        var parent = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/ORB-1", Ct))!;
        foreach (var status in new[] { "Solved", "Closed" })
        {
            using var changed = await sam.PutAsJsonAsync(
                $"/api/tickets/{parent.Id}/status", new ChangeTicketStatusRequest(status, await TicketTestData.VersionAsync(sam, parent.Id)), Ct);
            changed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        CustomerReplyResponse followUp;
        using (var reply = await CustomerReplyAsync(anonymous, token, "It broke again"))
        {
            reply.StatusCode.ShouldBe(HttpStatusCode.Created);
            followUp = (await reply.Content.ReadFromJsonAsync<CustomerReplyResponse>(Ct))!;
        }

        var followUpToken = TokenFromLink(followUp.FollowUpViewUrl!);

        // An agent may not delete; an admin may.
        using (var refused = await sam.DeleteAsync($"/api/tickets/{parent.Id}", Ct)) { refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden); }
        using (var deleted = await admin.DeleteAsync($"/api/tickets/{parent.Id}", Ct)) { deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent); }

        foreach (var table in new[] { "messages", "ticket_events", "ticket_access_tokens", "attachments", "ticket_tags", "email_outbox" })
        {
            (await database.ScalarAsync<long>($"SELECT count(*) FROM {table} WHERE ticket_id = '{parent.Id}'")).ShouldBe(0L, table);
        }

        (await database.ScalarAsync<long>($"SELECT count(*) FROM tickets WHERE id = '{parent.Id}'")).ShouldBe(0L);
        (await database.ScalarAsync<bool>($"SELECT parent_ticket_id IS NULL FROM tickets WHERE number = '{followUp.TicketNumber}'")).ShouldBeTrue();
        (await database.ScalarAsync<long>(
            $"SELECT count(*) FROM ticket_events e JOIN tickets t ON t.id = e.ticket_id WHERE t.number = '{followUp.TicketNumber}' AND e.payload::text LIKE '%parentTicketId%'"))
            .ShouldBe(1L, "the follow-up's own Created event keeps its history");
        using (var gone = await sam.GetAsync($"/api/tickets/{parent.Id}", Ct)) { gone.StatusCode.ShouldBe(HttpStatusCode.NotFound); }
        (await CustomerViewAsync(anonymous, followUpToken)).Number.ShouldBe(followUp.TicketNumber);
        using (var stale = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket"))
        {
            stale.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
            (await anonymous.SendAsync(stale, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var audit = (await admin.GetFromJsonAsync<PagedResponse<AdminEventDto>>("/api/admin-events?subjectType=Ticket", Ct))!;
        var entry = audit.Items.Single(e => e.Type == "TicketDeleted" && e.SubjectId == parent.Id);
        entry.Payload.ShouldNotContain("Cannot log in");
        entry.Payload.ShouldNotContain("ada@example.com");
    }
```

**Test B, erase.**

```csharp
    [Fact]
    public async Task An_admin_erases_a_requester_and_the_customer_view_and_agent_queue_show_no_trace()
    {
        var (factory, database) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        (await sam.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        using var admin = await AdminClientAsync(factory);
        using var anonymous = factory.CreateClient();

        await SubmitAsync(anonymous, "zebracanary reach me at ada@example.com", withPng: true);
        var token = TokenFromPortalLink(await ScalarStringAsync(database, "SELECT payload::text FROM email_outbox WHERE kind = 'ticket-confirmation'"));
        var before = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/ORB-1", Ct))!;
        using (var form = new MultipartFormDataContent { { new StringContent("Agent reply stays"), "body" } })
        using (var replied = await sam.PostAsync($"/api/tickets/{before.Id}/replies", form, Ct)) { replied.StatusCode.ShouldBe(HttpStatusCode.Created); }
        before = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/ORB-1", Ct))!;
        (await sam.GetFromJsonAsync<PagedResponse<TicketSummaryDto>>("/api/tickets?view=All&search=zebracanary", Ct))!.Items.ShouldHaveSingleItem();
        Directory.EnumerateFiles(_storage, "*", SearchOption.AllDirectories).ShouldNotBeEmpty();
        var requesterId = await database.ScalarAsync<Guid>("SELECT id FROM requesters WHERE email = 'ada@example.com'");

        using (var refused = await sam.PostAsync($"/api/requesters/{requesterId}/erase", null, Ct)) { refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden); }
        using (var erased = await admin.PostAsync($"/api/requesters/{requesterId}/erase", null, Ct)) { erased.StatusCode.ShouldBe(HttpStatusCode.NoContent); }
        using (var again = await admin.PostAsync($"/api/requesters/{requesterId}/erase", null, Ct)) { again.StatusCode.ShouldBe(HttpStatusCode.NoContent); }

        var after = (await sam.GetFromJsonAsync<TicketDetailDto>("/api/tickets/ORB-1", Ct))!;
        after.Number.ShouldBe("ORB-1");
        after.Subject.ShouldBe("[erased]");
        after.Events.Count.ShouldBe(before.Events.Count);
        after.Messages.Where(m => m.AuthorType == "Requester").ShouldAllBe(m => m.BodyHtml == "[erased]");
        after.Messages.Single(m => m.AuthorType == "Agent").BodyHtml.ShouldContain("Agent reply stays");
        (await sam.GetFromJsonAsync<PagedResponse<TicketSummaryDto>>("/api/tickets?view=All&search=zebracanary", Ct))!.Items.ShouldBeEmpty();
        Directory.EnumerateFiles(_storage, "*", SearchOption.AllDirectories).ShouldBeEmpty();
        using (var stale = new HttpRequestMessage(HttpMethod.Get, "/api/customer/ticket"))
        {
            stale.Headers.TryAddWithoutValidation(HeaderNames.TicketToken, token);
            (await anonymous.SendAsync(stale, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        foreach (var sql in new[]
        {
            "SELECT count(*) FROM requesters WHERE email = 'ada@example.com' OR name = 'Ada'",
            "SELECT count(*) FROM email_outbox WHERE to_address = 'ada@example.com' OR ticket_id IS NOT NULL",
            "SELECT count(*) FROM attachments",
            "SELECT count(*) FROM messages WHERE body LIKE '%ada@example.com%' OR body LIKE '%zebracanary%'",
            "SELECT count(*) FROM tickets WHERE subject <> '[erased]'",
        })
        {
            (await database.ScalarAsync<long>(sql)).ShouldBe(0L, sql);
        }

        var audit = (await admin.GetFromJsonAsync<PagedResponse<AdminEventDto>>("/api/admin-events?subjectType=Requester", Ct))!;
        audit.Items.ShouldAllBe(e => e.Type == "RequesterErased" && e.SubjectId == requesterId);
        audit.Items.ShouldAllBe(e => !e.Payload.Contains("ada@example.com") && !e.Payload.Contains("Ada"));
    }
```

**Test C, dead letters.**

```csharp
    [Fact]
    public async Task An_admin_lists_retries_and_discards_dead_letters_and_an_agent_cannot()
    {
        var (factory, database) = await StartAsync();
        await using var _ = factory;
        using var sam = TicketTestData.AgentClient(factory, "sam");
        (await sam.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        using var admin = await AdminClientAsync(factory);
        var retryId = Guid.NewGuid();
        var discardId = Guid.NewGuid();
        foreach (var id in new[] { retryId, discardId })
        {
            await database.ExecuteAsync($$"""
                INSERT INTO email_outbox (id, kind, to_address, payload, status, attempts, next_attempt_at, last_error, created_at)
                VALUES ('{{id}}', 'ticket-confirmation', 'ada@example.com', '{"portalLink":"https://help.test/t/secret"}', 'DeadLettered', 5, now(), 'render-failed', now())
                """);
        }

        using (var refused = await sam.GetAsync("/api/dead-letters", Ct)) { refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden); }
        var raw = await admin.GetStringAsync("/api/dead-letters", Ct);
        raw.ShouldNotContain("ada@example.com");
        raw.ShouldNotContain("secret");
        raw.ShouldNotContain("payload", Case.Insensitive);
        var list = System.Text.Json.JsonSerializer.Deserialize<PagedResponse<DeadLetterDto>>(raw, System.Text.Json.JsonSerializerOptions.Web)!;
        list.TotalCount.ShouldBe(2);
        list.Items.ShouldAllBe(d => d.Recipient == "a***@example.com" && d.Kind == "ticket-confirmation" && d.Attempts == 5 && d.LastError == "render-failed");

        using (var retried = await admin.PostAsync($"/api/dead-letters/{retryId}/retry", null, Ct)) { retried.StatusCode.ShouldBe(HttpStatusCode.NoContent); }
        (await ScalarStringAsync(database, $"SELECT status FROM email_outbox WHERE id = '{retryId}'")).ShouldBe("Pending");
        (await database.ScalarAsync<int>($"SELECT attempts FROM email_outbox WHERE id = '{retryId}'")).ShouldBe(0);
        using (var twice = await admin.PostAsync($"/api/dead-letters/{retryId}/retry", null, Ct)) { twice.StatusCode.ShouldBe(HttpStatusCode.Conflict); }
        using (var discarded = await admin.DeleteAsync($"/api/dead-letters/{discardId}", Ct)) { discarded.StatusCode.ShouldBe(HttpStatusCode.NoContent); }
        using (var missing = await admin.DeleteAsync($"/api/dead-letters/{Guid.NewGuid()}", Ct)) { missing.StatusCode.ShouldBe(HttpStatusCode.NotFound); }
        (await admin.GetFromJsonAsync<PagedResponse<DeadLetterDto>>("/api/dead-letters", Ct))!.Items.ShouldBeEmpty();

        var audit = (await admin.GetFromJsonAsync<PagedResponse<AdminEventDto>>("/api/admin-events?subjectType=EmailOutbox", Ct))!;
        audit.Items.Select(e => e.Type).ShouldBe(["DeadLetterRetried", "DeadLetterDiscarded"], ignoreOrder: true);
    }
```

If the real route or DTO names differ from Tasks 4 to 6, fix the test to match the contract. Do not change the contract. If a test fails on product behaviour, fix the product code and say so in the commit body.

- [ ] **Step 2: Run the tests**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "TicketLifecycleEndToEndTests|SensitiveDataLeakTests|LogRedactionTests"`
Expected: PASS.

- [ ] **Step 3: Close the docs**

**`docs/development/TICKET-OPERATIONS.md`**

(a) Replace the whole `### Sentry` subsection (heading, the paragraph and the `**06c remaining:**` sentence) with:

```markdown
### Sentry

The Api scrubs `X-Ticket-Token`, `X-Api-Key`, `Authorization` and `Cookie` from Sentry events before they leave the process. The Portal
makes no API calls yet, so its scrub moves to PHASE-09 with its first proxied token (D-039).
```

(b) Insert before `## Try it end to end`:

````markdown
## Admin operations (06c)

All of these are Admin-only (D-022) and write an `AdminEvent` whose payload holds ids and counts, never ticket content or personal data.

```bash
# Hard-delete a ticket: its messages, attachment rows and files, events, tokens, tags and outbox rows go. 204, or 404 for an unknown id.
curl -s -X DELETE -H "$ADMIN_AUTH" "$API/api/tickets/$TICKET_ID"

# Erase a requester (GDPR). 204; running it again is safe and also 204.
curl -s -X POST -H "$ADMIN_AUTH" "$API/api/requesters/$REQUESTER_ID/erase"

# Dead letters: list (paged), retry, discard.
curl -s -H "$ADMIN_AUTH" "$API/api/dead-letters?page=1&pageSize=25"
curl -s -X POST -H "$ADMIN_AUTH" "$API/api/dead-letters/$OUTBOX_ID/retry"
curl -s -X DELETE -H "$ADMIN_AUTH" "$API/api/dead-letters/$OUTBOX_ID"
```

- **Delete.** A follow-up of the deleted ticket survives with its parent link cleared. Its own `Created` event still records the old parent id. Files are removed after the commit, best effort; a failure is logged by storage key only.
- **Erase.** The requester row stays as a tombstone, so ticket numbers and event ids survive. Erase replaces the subject of every ticket the requester opened, and the body of every message the requester wrote, with `[erased]`. It clears the tickets' `metadata` and `custom_fields`, deletes the attachments on the requester's messages (rows and files), revokes every access token, and deletes every `email_outbox` row addressed to the requester or belonging to one of their tickets, in any status. Agent replies and internal notes stay. Agents holding an old row version of those tickets get a 409 and must reload.
- **Dead letters.** `Recipient` is masked (`a***@example.com`) and the payload is never returned. Retry makes the row `Pending` with no attempts, and the Worker sends it on its next poll. Discard marks it `Discarded`. Either answers 409 `outbox-not-dead-lettered` for a row that is not a dead letter.

### Outbox retention

The Worker deletes `Sent` and `Discarded` outbox rows older than N days, measured from `created_at`, in batches. Dead letters, `Pending` and `Sending` rows are never deleted.

| Setting | Default | Meaning |
| --- | --- | --- |
| `OutboxRetention__Enabled` (`TECHSTRAP_OUTBOX_RETENTION_ENABLED` in compose) | true | Turns the sweep off |
| `OutboxRetention__Days` (`TECHSTRAP_OUTBOX_RETENTION_DAYS` in compose) | 90 | Age at which a finished row is deleted (1 to 3650) |
| `OutboxRetention__IntervalMinutes` | 60 | Delay between sweeps |
| `OutboxRetention__BatchSize` | 500 | Rows deleted per statement; a full batch runs again at once |

### Logging and personal data

Every Serilog event in the Api and the Worker passes through `PiiRedactionEnricher` before any sink: email addresses become `[email]`, 43-character access tokens `[token]` and `sha256:` hashes `[hash]`, in every property, including nested ones. It cannot rewrite an attached exception or recognise a name, so application code logs ids and exception type names only, and nothing may enable `EnableSensitiveDataLogging` or `Include Error Detail` (`LoggingSafetyTests` fails the build if one does). The Admin and Portal hosts handle no requester data yet; they join in PHASE-07 and PHASE-09.
````

(c) Replace the `## Try it end to end` closing paragraph's last sentence (`` `SensitiveDataLeakTests` checks that the customer view and customer emails carry no internal or agent-private data.``) with:

```markdown
`SensitiveDataLeakTests` checks that the customer view and customer emails carry no internal or agent-private data. Three more
`TicketLifecycleEndToEndTests` cover the admin side: deleting a Closed ticket whose follow-up survives, erasing a requester (nothing
personal left in rows, files, search or the customer link), and listing, retrying and discarding dead letters. `LogRedactionTests` runs the
intake, customer view and lost-link flow and scans every captured log event.
```

**`docs/architecture/PHASE-06-ticket-operations.md`**. Change only the checkbox and the trailing evidence line of each item; leave the validation text Task 1 wrote.

| Item | Edit |
| --- | --- |
| `P06-T10` | `- [ ]` becomes `- [x]`. Replace the line that starts `  - **06a part done:** mark spam` (it ends `**06c remaining:** delete-ticket (plan: …)`) with: `  - **06a part done:** mark spam (MarkTicketSpamRequestHandlerTests, TicketTagAndSpamEndpointTests); **06c done:** delete-ticket (DeleteTicketRequestHandlerTests, DeleteTicketIntegrationTests, TicketLifecycleEndToEndTests; plan: docs/superpowers/plans/2026-10-03-phase-06c-delete-erase-dead-letters.md)` |
| `P06-T11` | `- [ ]` becomes `- [x]`. Append the line: `  - **06c evidence:** EraseRequesterRequestHandlerTests, EraseRequesterIntegrationTests (Nothing_personal_remains_after_erase), TicketLifecycleEndToEndTests (plan: docs/superpowers/plans/2026-10-03-phase-06c-delete-erase-dead-letters.md)` |
| `P06-T16` | `- [ ]` becomes `- [x]`. Append the line: `  - **06c evidence:** ListDeadLettersRequestHandlerTests, RetryDeadLetterRequestHandlerTests, DiscardDeadLetterRequestHandlerTests, the dead-letter integration tests, TicketLifecycleEndToEndTests (plan: docs/superpowers/plans/2026-10-03-phase-06c-delete-erase-dead-letters.md)` |
| `P06-T19` | `- [ ]` becomes `- [x]`. Replace the `**06c remaining:**` clause with `**06c done:** Serilog PII redaction (LogRedactionTests, LoggingSafetyTests). The Portal Sentry scrub is deferred to PHASE-09 (D-039): the Portal proxies no tokens yet.` |
| `P06-T20` | `- [ ]` becomes `- [x]`. Replace the `**06c remaining:**` clause with `**06c done:** the delete, erase and dead-letter parts of the lifecycle (TicketLifecycleEndToEndTests)` |

In **Success Criteria**, tick these (`- [ ]` to `- [x]`) and append the evidence in brackets:
- ``Erase requester leaves no personal data behind (message bodies, names, emails, attachment files) …`` → `(06c: EraseRequesterIntegrationTests, TicketLifecycleEndToEndTests)`
- ``Dead letters can be listed, retried and discarded by an Admin only.`` → `(06c: dead-letter handler and integration tests, AgentAccessCoverageTests)`
- ``Logs contain no requester email, name or token (`LogRedactionTests`). …`` → replace the sentence after `LogRedactionTests` with `(06c: PiiRedactionEnricher on the Api and the Worker; the Portal and Admin hosts follow in PHASE-09 and PHASE-07, D-039).`
- ``` CI is green; `/openapi/v1.json` lists all new routes.``` → tick after Step 4 passes, appending `(06c: OpenApiSurfaceTests lists the five new routes and documents the multipart bodies)`.
- ``All 21 handlers in the boundary table …`` → tick only if `ControllerBoundaryTests` passes and the boundary table already lists the six 06c handlers (`DeleteTicket`, `EraseRequester`, `ListDeadLetters`, `RetryDeadLetter`, `DiscardDeadLetter`, `PurgeEmailOutbox`); if the table lacks a row, add it first.

In **Risks and Open Questions**:
- `Erase versus audit trail: event payloads must not contain PII …` → `- [x]` and append ` Resolved (06c: the AdminEvent payload guard plus the counts-only payloads asserted in EraseRequesterIntegrationTests and DeleteTicketIntegrationTests).`
- Tick the five carry-forwards for the full erase cascade, the hard-delete repository method, the `email_outbox` index with retention, the truncated multipart body, and the sanitised-name dedupe, each with its evidence in brackets (`EraseRequesterIntegrationTests`; `DeleteTicketIntegrationTests`; the Task 2 schema test and `PurgeEmailOutboxIntegrationTests`; `ReadFormBeforeBindingTests`; `AddCustomerReplyRequestHandlerTests`).
- The `[Consumes]` 401-versus-415 carry-forward → tick, evidence `MultipartRouteContractTests`.
- The Portal Sentry scrub carry-forward stays `- [ ]`; change its trailing `(06c or PHASE-09)` to `(PHASE-09, D-039)`.

**`99-IMPLEMENTATION-ROADMAP.md` and `00-DISCOVERY-INDEX.md`:** in the PHASE-06 row replace `06a complete; 06b implemented (pending merge); 06c not started` with `06a and 06b complete; 06c implemented (pending merge)`. In the roadmap row's decisions column append `, D-039` if Task 1 has not already.

- [ ] **Step 4: Run every check**

Run:
- `dotnet build TechStrap.slnx -c Release` (0 warnings);
- `dotnet test --solution TechStrap.CI.slnf -c Release`;
- `pwsh -File scripts/Invoke-ScriptTests.ps1`;
- `pwsh -File scripts/Check-PackageVersions.ps1`;
- `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build`.

All must pass. Then `grep -n "06c remaining" docs/architecture/PHASE-06-ticket-operations.md docs/development/TICKET-OPERATIONS.md` must return nothing.

- [ ] **Step 5: Commit**

```bash
git add tests docs
git diff --cached --stat
git commit -m "test(admin): end-to-end delete, erase and dead letters, plus PHASE-06c docs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```
