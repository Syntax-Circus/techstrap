# PHASE-05 Intake, Email and Worker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Customers can submit a ticket three ways: the portal web form, a trusted product-app key, or a public embedded key. In one transaction the system creates the requester, the ticket, the first message, any attachments, an access token and a queued confirmation email. The Worker then drains the email outbox reliably, claiming each email, sending it, retrying with backoff and dead-lettering it if it keeps failing. A public endpoint exposes product branding.

**Architecture:**
- **One handler, two routes.** One Application handler, `SubmitTicketRequestHandler`, serves both submit routes. The controller supplies an Application-owned `SubmitTicketContext` holding the channel, trust level, product, API key, honeypot flag, files and idempotency key. The handler applies every rule inside one `IUnitOfWork`, after saving attachments through `IAttachmentStore` with a compensating delete.
- **Emails.** The outbox row holds template data (D-033). The Worker's `DrainEmailOutboxHandler` claims a batch, renders it with `IEmailTemplateRenderer` using the product's branding, sends it through an Application-owned `IOutboundEmailSender` (an adapter over SyntaxCircus.Email), and acks or fails each row.
- **Routes and access.** Anonymous public routes carry an explicit `Public` policy. API-key routes use the SyntaxCircus API-key scheme with a database-backed validator (D-034).

**Tech Stack:**
- .NET 10 and ASP.NET Core controllers, with a Worker `BackgroundService`.
- `SyntaxCircus.AspNetCore.Authentication` 0.1.5 (API-key scheme), `SyntaxCircus.Email` 0.1.6 (SMTP), `SyntaxCircus.Storage` 0.2.1 (local disk), and `SyntaxCircus.AspNetCore.Common` 0.1.15 (rate limiting and ProblemDetails).
- `HtmlSanitizer` 9.2.1039.
- EF Core and Npgsql.
- Testing: xUnit v3, Shouldly, NSubstitute, Testcontainers (Postgres and a Mailpit generic container), and a Mailpit container in compose.

**Spec:**
- `docs/architecture/PHASE-05-intake-email-worker.md`, together with the owner decisions of 2026-10-03, recorded as D-032 in Task 1.
- Supporting documents:
  - `docs/architecture/02-ARCHITECTURE.md` sections 3.1, 4, 6.4, 7.2, 11.1 and 11.2;
  - `04-DECISION-LOG.md` entries D-001, D-006, D-010, D-016, D-019, D-020, D-024, D-026 and D-029;
  - `docs/BRAND.md`: the email rules, section 22 (the accent rule) and the Powered-by rule;
  - `_template/docs/patterns/CLIENT_IP_RATE_LIMITING.md`.

### Owner decisions (2026-10-03), recorded as D-032 in Task 1
- **Honeypot.** When the hidden field is filled in, return the normal 201 with a plausible ticket number. Create no ticket and send no email.
- **External user reference from an untrusted source.** If a public key or the web form sends an external user reference, drop it and add the warning `external-user-ref-ignored` to the response.
- **Access token lifetime.** The token still slides 90 days on each use, but it can never outlive 365 days from when it was issued.
- **Attachments.**
  - Limits: 10 MiB per file, 25 MiB per message, and at most 5 files.
  - Allowed types: PNG, JPEG, GIF, WebP, PDF, plain text, `.log`, CSV and ZIP.
  - Each file is checked by its declared type and by its leading bytes.

### Technical decisions this plan makes (recorded in Task 1; confirmed by the owner when they review this plan)
- **D-033: emails render at send time, in the Worker.** This follows 02-ARCHITECTURE sections 6.4 and 7.2 and the D-024 consequences. The PHASE-05 doc's "render at enqueue" assumption cannot hold, for two reasons. First, the outbox payload is capped at 16 000 characters. Second, the Worker is the host that reads `TECHSTRAP_PORTAL_SHOW_POWERED_BY`.
  - The confirmation row's payload holds the template data: the ticket number, the subject, the requester's name and the portal link.
  - The link contains the plaintext access token. It stays at rest in `email_outbox` until a retention sweep removes sent rows. That sweep is carried forward to PHASE-12.
  - Application owns `IOutboundEmailSender`. Infrastructure adapts it to `SyntaxCircus.Email.IEmailSender`, because Application may reference only `SyntaxCircus.Common`, and handlers may depend only on Application interfaces.
- **D-034: intake transport.**
  - Anonymous public controllers carry `[Authorize(Policy = AuthorizationPolicies.Public)]`, a policy that admits everyone. This keeps the PHASE-04 rule that every controller declares a policy. The fallback policy stays "authenticated".
  - The `ApiKey` policy authenticates with the `ApiKey` scheme only. A missing, unknown or revoked key gets a uniform 401.
  - API-key intake accepts a JSON body with no attachments in v1. Attachments over the API are carried forward to PHASE-11, if the SDK needs them.
  - Intake does not take a ticket number for follow-ups, so the PHASE-03 carry-forward ("check that a supplied ticket number belongs to the key's product") moves to the phase that adds follow-up submission (PHASE-09).

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`. `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on.
  - Private fields use `_camelCase`, including `private static readonly`.
  - Constants use PascalCase.
  - Namespaces are file-scoped.
- **Packages.** Versions are managed centrally, and `Directory.Packages.props` must agree with `docs/architecture/03-PACKAGE-MAP.md` (`Check-PackageVersions.ps1`). A csproj never sets `Version` on a package reference.
  - This phase adds references to `HtmlSanitizer` (already pinned at 9.2.1039), `SyntaxCircus.Email` (0.1.6) and `SyntaxCircus.Storage` (0.2.1) in Infrastructure.
  - It pins the Mailpit image tag in the package map's container-images table.
  - If you add a Testcontainers package, add it to both `Directory.Packages.props` and the package map in the same commit.
- **Project references are fixed.**
  - Domain references nothing.
  - Contracts references nothing and holds no attributes (D-016).
  - Application references Domain and Contracts, and only the `SyntaxCircus.Common` package.
  - Infrastructure references Application, Domain and Contracts, and may reference packages.
  - Api and Worker reference Application, Infrastructure and Contracts. **The Worker never references Api.**
- **Handlers.**
  - Each handler is a `public sealed class XxxHandler` with a public `IXxxHandler` interface whose `HandleAsync(..., CancellationToken)` takes the token last.
  - Constructor parameters may only be Application interfaces, `TimeProvider`, `IOptions<>`, `IOptionsSnapshot<>`, `IOptionsMonitor<>`, `ILogger` or `ILogger<>`.
  - No `HttpContext`, EF, Infrastructure, `*Record` or package types.
  - Every public `I*` interface in Application ends each async method with a `CancellationToken`, and exposes no `Microsoft.AspNetCore*`, EF, `Npgsql`, `System.Net.Http` or `TechStrap.Infrastructure` types. For example, use `Stream`, not `IFormFile`.
- **Controllers.**
  - Each controller has a class-level `[Authorize(Policy = ...)]`, no constructor dependencies, and exactly one `[FromServices]` handler interface plus a `CancellationToken` per action.
  - Map results with `result.ToActionResult(this, onSuccess)`.
  - Every new action gets a row in `ControllerActions.ExpectedSuccess`.
- **Results.** Return `new ResultError(code, message, kind, target?)`, where `target` is only for `Validation`. Validation maps to 400, never 422. Convert domain failures with `domainResult.Error!.ToError()`.
- **Contracts naming.** Every public non-static type ends in `Dto`, `Request` or `Response`. Constant holders are `public static class`.
- **Persistence.**
  - Write inside `await using var scope = await unitOfWork.BeginAsync(ct)` with exactly one `CommitAsync`.
  - A commit conflict returns a Conflict result coded `duplicate`, `concurrency-conflict` or `reference-violation`, and clears the change tracker.
  - Never hand-write or hand-edit migrations. Generate them with `dotnet ef migrations add <Name> --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api`.
- **Secrets.**
  - Plaintext API keys and access tokens never appear in logs, audit payloads, responses (other than where this plan places them), or any DB column except `email_outbox.payload` (D-033, with retention carried forward).
  - SMTP exceptions are logged and stored only as a sanitized category, never `ex.ToString()`.
- **Customer-facing copy (BRAND.md).**
  - Product branding leads.
  - TechStrap appears only as the "Powered by TechStrap" line, which links to https://github.com/Syntax-Circus/techstrap in HTML and is the bare URL in text. It is omitted when `TECHSTRAP_PORTAL_SHOW_POWERED_BY=false`.
  - No mascot, no puns, no TechStrap colours.
  - The accent comes from `ProductAccent.TryDerive`, and `--accent-ink` is used for text on white.
  - No agent email in From or Reply-To (D-024).
- **Commits.** Use Conventional Commits. Every commit ends with exactly:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden.**
  - Never run `git add -f`.
  - Never commit `.superpowers/`.
  - Never run `docker compose down -v`.
  - Never commit a real secret. Dev keys come from `DevelopmentApiKeys`.
  - Before each commit, run `git diff --cached --stat` and confirm every listed file is staged.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release` produces 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release` passes. Docker is required.
  - `pwsh -File scripts/Invoke-ScriptTests.ps1` passes.
  - `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api` is clean.

## Review Focus

These are the inputs and conditions most likely to hurt a real customer or operator. Each is pinned by a test in the task that owns it:

1. **A file is renamed to look safe.** Example: `invoice.pdf.exe`, or an `.exe` renamed to `.pdf` whose leading bytes are `MZ`. It is rejected with `400 attachment-type-not-allowed`, and nothing is stored. Pinned in Task 6 by `AttachmentStoreTests.A_renamed_executable_is_rejected_by_its_leading_bytes`.
2. **The same customer submits twice at once.** Two submissions with the same new email address, sent concurrently, create one requester and two tickets, and both succeed. Pinned in Task 10 by `SubmitTicketIntegrationTests.Two_first_submissions_from_one_new_email_share_one_requester`.
3. **The SMTP server is down.** The email is retried with backoff and then dead-lettered. The request that queued it already returned 201, and no SMTP host or credentials appear in `last_error` or the logs. Pinned in Task 16 by `EmailDrainIntegrationTests.A_dead_smtp_server_retries_then_dead_letters_without_leaking_details`.
4. **A customer pastes HTML or script into the body.** It is stored as escaped text that renders literally, and no live markup survives. Pinned in Task 5 by `CustomerTextTests.Pasted_markup_is_shown_as_text_not_rendered`.
5. **An SDK retries the same `Idempotency-Key` while the first request is still committing.** It gets the same ticket number with a working link, and only one ticket exists. The stored idempotent response never holds the link. Pinned in Task 10 by `SubmitTicketIntegrationTests.Two_concurrent_requests_with_one_idempotency_key_create_one_ticket`.

---

## File Structure

- **Docs:**
  - decision log entries D-032, D-033 and D-034;
  - `PHASE-05-intake-email-worker.md`, `02-ARCHITECTURE.md` (sections 3.1, 4, 6.4, 7.2 and 11.2) and `03-PACKAGE-MAP.md` (Mailpit tag);
  - new: `docs/development/INTAKE.md`, which explains the smoke script and Mailpit.
- **Contracts:**
  - `Intake/IntakeDtos.cs`: `SubmitTicketRequest`, `SubmitTicketResponse`, `IntakeWarnings`;
  - `Intake/IntakeLimits.cs`;
  - `Products/PublicProductDto.cs`;
  - `Http/HeaderNames.cs`.
- **Domain:** `Tickets/TicketAccessToken.cs` gets `IssuedAt` and `MaxLifetime`, plus the absolute cap.
- **Application:**
  - `Intake/`: `SubmitTicketContext`, `IntakeChannel`, `PortalLinkOptions`, `IntakeErrors`, `SubmitTicketRequestHandler`, `CustomerText`;
  - `Security/IAccessTokenService.cs`;
  - `Content/IHtmlSanitizer.cs`;
  - `Attachments/IAttachmentStore.cs` (with `IncomingAttachment` and `StoredAttachment`);
  - `Email/`: `IEmailTemplateRenderer`, `EmailTemplates` (kinds and the payload record), `RenderedEmail`, `OutboundEmail` and `IOutboundEmailSender`, `EmailBrandingOptions`, `EmailOutboxWorkerOptions`, `DrainEmailOutboxHandler`;
  - `Persistence/IIntakeIdempotencyStore.cs`;
  - `Products/GetPublicProductRequestHandler.cs`.
- **Infrastructure:**
  - `Security/AccessTokenService.cs`;
  - `Content/HtmlSanitizerAdapter.cs`;
  - `Attachments/AttachmentStore.cs` and `FileSignatures.cs`;
  - `Email/`: `EmailTemplateRenderer`, `SmtpOutboundEmailSender`, `EmailServiceCollectionExtensions`;
  - `Persistence/Repositories/IntakeIdempotencyStore.cs`;
  - `Intake/IntakeServiceCollectionExtensions.cs`;
- **Api:**
  - `Security/ApiKeySetup.cs` (scheme, validator, `ApiKey` and `Public` policies);
  - `Security/ProductApiKeyValidator.cs` and `Security/ApiKeyClaimTypes.cs`;
  - `Options/IntakeRateLimitOptions.cs`;
  - `Controllers/`: `IntakeController`, `PublicIntakeController`, `PublicProductsController`, `PublicSubmitTicketForm`;
  - `Startup/IntakeHosting.cs` (body limits and the 413 mapping) and `Startup/IntakeRateLimiting.cs`;
  - `Startup/ApplicationHandlerRegistration.cs` stays in Api and skips the Worker-only drain handler, which `AddTechStrapEmail` registers for the Worker;
  - `Program.cs`.
- **Worker:**
  - `Outbox/EmailOutboxWorker.cs`;
  - `Program.cs` and `.env.example`.
- **Compose and scripts:**
  - `docker-compose*.yml` (Mailpit locally; SMTP TLS and retry settings; the Powered-by passthrough);
  - `.env.production.example`;
  - `scripts/Send-TestTicket.ps1`, with a Pester test.
- **Tests:** handler tests and service tests in the Infrastructure integration project, plus Api endpoint tests (`ApiKeyAuthTests`, `IntakeEndpointTests`, `PublicIntakeEndpointTests`, `PublicProductEndpointTests`, `IntakeRateLimitTests`, `SensitiveDataLeakTests`) and Worker tests (`EmailOutboxWorkerTests`, `EmailDrainIntegrationTests`).
  - The coverage tests from PHASE-04 are updated: `AgentAccessCoverageTests` scans only routes with the Agent or Admin policy, and `ExpectedSuccess` gets the new rows.

---

### Task 1: Record decisions and align the docs

**Files:**
- Modify: `docs/architecture/04-DECISION-LOG.md`
  - approval-basis bullet;
  - index rows for D-032, D-033 and D-034;
  - their sections at the end of the file.
- Modify: `docs/architecture/PHASE-05-intake-email-worker.md`
- Modify: `docs/architecture/02-ARCHITECTURE.md`
- Modify: `docs/architecture/PHASE-09-portal.md`. Check the file name with `ls docs/architecture`. The change here is the follow-up ticket-number carry-forward.
- Modify: `docs/architecture/PHASE-11-*.md` (API attachments carry-forward) and `docs/architecture/PHASE-12-*.md` (outbox payload retention).

**Interfaces:** produces the decision IDs that later tasks cite.

- [ ] **Step 1: Add the decision log entries**

Add this bullet to the approval-basis list:

```markdown
- **Owner decision (2026-10-03, PHASE-05 planning):** D-032 (intake rules: honeypot, untrusted external ref, link cap, attachments). D-033 and D-034 were proposed in the PHASE-05 plan and approved when the owner approved the plan.
```

Add these rows to the index after D-031:

```markdown
| D-032 | Intake rules: honeypot, untrusted external ref, access-link cap, attachment limits | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-05, PHASE-09, PHASE-11 |
| D-033 | Emails render in the Worker at send time; Application owns the sender abstraction | Approved (owner, PHASE-05 plan review) | 2026-10-03 | PHASE-05, PHASE-06, PHASE-12, 02-ARCHITECTURE |
| D-034 | Intake transport: explicit Public policy, ApiKey scheme policy, JSON-only API intake | Approved (owner, PHASE-05 plan review) | 2026-10-03 | PHASE-05, PHASE-09, PHASE-11 |
```

Append these sections at the end of the file, using the existing format:

```markdown
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
- **The portal link.** The plaintext access token exists only when the email is queued, so the link is captured in the payload. A sent row's payload therefore holds a working link until a retention sweep removes it, carried forward to PHASE-12.
- **Nowhere else.** `email_outbox.payload` is the only column that may hold a plaintext token. The idempotency store (D-020) keeps the response without the link; a replay issues a fresh access token for the original ticket and returns a new link.

### Alternatives Considered
- **Render when queued, and raise the payload cap.** This leaves the link in the row just the same, freezes branding, and contradicts D-024's statement that the Worker reads the Powered-by setting.
- **Let Application reference `SyntaxCircus.Email`.** This breaks the project reference rules.

### Consequences
- The Worker reads `TECHSTRAP_PORTAL_SHOW_POWERED_BY`; the Api does not need to.
- PHASE-06 notification emails follow the same pattern: a payload kind plus a renderer template.
- PHASE-12 adds a retention sweep for sent rows (proposed: 30 days), with a payload scrub.

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

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-05 plan review)
- **Approved on:** 2026-10-03
```

- [ ] **Step 2: Align the PHASE-05 doc**

In `PHASE-05-intake-email-worker.md`:

1. **Architecture Decisions.** Edit these bullets:
   - **First bullet:** add "(D-034: API-key intake is JSON-only in v1; the web form is multipart)".
   - **Trust-rules bullet:** replace "(**Assumption**: dropped, with a warning field in the response)" with "(D-032: dropped, warning `external-user-ref-ignored`)". Replace "stored under an `untrusted` section" with "stored with `MetadataTrusted = false`".
   - **Abuse-controls bullet:**
     - Replace "`AddPerSubjectFixedWindow` with subject `{keyPrefix}:{ip}`" with "a partitioned fixed window keyed `{keyPrefix}:{ip}` (public) or `{keyPrefix}` (trusted)".
     - Replace "(defaults 10 MB per file, 25 MB per message, common document and image allowlist, **Assumption** from spec §10)" with "(D-032: 10 MiB per file, 25 MiB per message, 5 files, PNG/JPEG/GIF/WebP/PDF/text/log/CSV/ZIP checked by declared type and leading bytes)".
   - **Access-token bullet:** add "Absolute cap 365 days from issue (D-032, `TicketAccessToken.MaxLifetime`)."
   - **Outbox bullet:** replace "rendered at enqueue time into the outbox row (subject, text, HTML) so the worker needs no domain lookups. **Assumption.**" with "the outbox row holds template data and the Worker renders at send time with the product's current branding (D-033)".
   - **`DrainEmailOutboxHandler` bullet:**
     - Replace "constructor-injects `IDrainEmailOutboxHandler`" with "resolves `IDrainEmailOutboxHandler` from a new DI scope each iteration (the handler is scoped)".
     - Replace "sends via the email provider abstraction" with "renders with `IEmailTemplateRenderer` and sends through the Application-owned `IOutboundEmailSender` (D-033)".
   - **Abstractions bullet:** replace `IEmailSender (the SyntaxCircus.Email provider interface, consumed by the drain handler)` with "`IOutboundEmailSender` (Application; adapted to `SyntaxCircus.Email.IEmailSender` in Infrastructure, D-033), `IAccessTokenService`, `IAttachmentStore`, `IHtmlSanitizer`, `IEmailTemplateRenderer`". Add the options `EmailBrandingOptions` and `EmailOutboxWorkerOptions`.
2. **Boundary table.**
   - Web-form row: change "400/422" to "400". Change "Honeypot yields the same 201" to "Honeypot yields the same 201 (D-032)".
   - Worker row: change the abstractions to `IEmailOutboxStore`, `IProductRepository`, `IEmailTemplateRenderer`, `IOutboundEmailSender`, `IOptions<EmailOutboxWorkerOptions>`, and the implementation to "EF `EmailOutboxStore`, `EmailTemplateRenderer`, `SmtpOutboundEmailSender` over `SyntaxCircus.Email`".
3. **P05-T01:** replace "checked by `MagicValueTests` for these values" with "checked by a parity test (`IntakeLimits.MaxFileBytes == DomainLimits.AttachmentMaxBytes`)".
4. **P05-T17:** replace "over the `intake_idempotency_keys` table (migration generated with `dotnet ef`, ...)" with "over the existing `intake_idempotency_keys` table (created in PHASE-03; no new migration)".
5. **P05-T18:** replace the sentence beginning "If rendering stays at enqueue time" with "Rendering happens in the Worker (D-033), so only the Worker reads the setting."
6. **Risks.** Tick these four, each with its resolution:
   - honeypot: "Resolved: D-032."
   - drop-versus-reject: "Resolved: D-032."
   - render-at-enqueue: "Resolved: D-033, render at send."
   - the `IEmailSender` shape: "Resolved: `EmailMessage` has `MessageId` and no custom headers; D-033 adapter."

   Mark these two carried-forward items as moved:
   - the follow-up ticket-number check: "Moved to PHASE-09 (D-034)."
   - the access-token absolute cap: "Delivered in this phase (D-032)."

- [ ] **Step 3: Align 02-ARCHITECTURE**

In `02-ARCHITECTURE.md`:
- **Section 3.1.** Replace the `IEmailSender` row with:
  ```markdown
  | `IOutboundEmailSender` | `SmtpOutboundEmailSender` over `SyntaxCircus.Email.IEmailSender` (SMTP; in-memory sender in tests) | Sends a rendered message; used only by `DrainEmailOutboxHandler` (D-033) |
  ```
  Add `EmailBrandingOptions` and `EmailOutboxWorkerOptions` to the options row.
- **Section 4.** In the anonymous-public row, after "`[AllowAnonymous]` on explicit endpoints only", add "(controllers declare the `Public` policy, D-034)". In both product-app rows, add "(`ApiKey` policy; JSON body in v1, D-034)".
- **Section 7.2.** In the drain handler row, replace `IEmailSender` with `IOutboundEmailSender`, drop `TimeProvider`, and add `IOptions<EmailOutboxWorkerOptions>`. The renderer, not the handler, reads `EmailBrandingOptions`.
- **Section 11.2.** For the `intake-public-key` and `intake-trusted-key` rows, replace any "AddPerSubjectFixedWindow" wording with "partitioned fixed window keyed by key prefix (and client IP for public keys)".

- [ ] **Step 4: Carry forward to later phases**

Add a bullet to the "Risks and Open Questions" section of each of these phase docs:
- **PHASE-09:** "Carried forward from PHASE-05 (D-034): follow-up submission must check the ticket belongs to the caller's product and requester before attaching (the PHASE-03 carry-forward). The lost-link flow issues a fresh token when a link reaches its 365-day cap (D-032)."
- **PHASE-11:** "Carried forward from PHASE-05 (D-034): API-key intake is JSON-only; add multipart attachments if the SDK or the MAUI helper needs screenshots."
- **PHASE-12:** "Carried forward from PHASE-05 (D-033): add a retention sweep for sent `email_outbox` rows (proposed 30 days) that also removes payloads, because a confirmation payload holds a working portal link."

- [ ] **Step 5: Run the doc tests and commit**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS.

```bash
git add docs/architecture
git commit -m "docs: record PHASE-05 intake decisions D-032 to D-034" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 2: Contracts for intake and public product

**Files:**
- Create: `src/TechStrap.Contracts/Http/HeaderNames.cs`
- Create: `src/TechStrap.Contracts/Intake/IntakeLimits.cs`
- Create: `src/TechStrap.Contracts/Intake/IntakeDtos.cs`
- Create: `src/TechStrap.Contracts/Products/PublicProductDto.cs`
- Create: `tests/TechStrap.Application.Tests/Intake/IntakeLimitsParityTests.cs`

**Interfaces (produced, used verbatim later):**

```csharp
namespace TechStrap.Contracts.Http;
public static class HeaderNames
{
    public const string ApiKey = "X-Api-Key";
    public const string TicketToken = "X-Ticket-Token";
    public const string IdempotencyKey = "Idempotency-Key";
}

namespace TechStrap.Contracts.Intake;
public static class IntakeLimits
{
    public const long MaxFileBytes = 10L * 1024 * 1024;
    public const long MaxMessageBytes = 25L * 1024 * 1024;
    public const int MaxFiles = 5;
    public const int MaxMetadataKeys = 50;
    public const int MaxMetadataKeyLength = 64;
    public const int MaxMetadataValueLength = 1_000;
    public const int MaxIdempotencyKeyLength = 200;
    public static readonly IReadOnlyList<string> AllowedExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf", ".txt", ".log", ".csv", ".zip"];
}
public static class IntakeWarnings { public const string ExternalUserRefIgnored = "external-user-ref-ignored"; }
public sealed record SubmitTicketRequest(string? Email, string? Name, string? Subject, string? Body, string? ExternalUserRef, IReadOnlyDictionary<string, string>? Metadata);
public sealed record SubmitTicketResponse(string TicketNumber, string? ViewUrl, IReadOnlyList<string> Warnings);

namespace TechStrap.Contracts.Products;
public sealed record PublicProductDto(string Key, string DisplayName, string? LogoPath, string AccentColour, string OnAccentColour, string AccentInkColour);
```

- [ ] **Step 1: Write the failing parity test**

`tests/TechStrap.Application.Tests/Intake/IntakeLimitsParityTests.cs`:

```csharp
using TechStrap.Contracts.Http;
using TechStrap.Contracts.Intake;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Tests.Intake;

public sealed class IntakeLimitsParityTests
{
    [Fact]
    public void The_per_file_limit_matches_the_domain_attachment_limit() =>
        IntakeLimits.MaxFileBytes.ShouldBe(DomainLimits.AttachmentMaxBytes);

    [Fact]
    public void The_message_limit_holds_the_maximum_number_of_maximum_files_or_fewer() =>
        IntakeLimits.MaxMessageBytes.ShouldBeLessThanOrEqualTo(IntakeLimits.MaxFiles * IntakeLimits.MaxFileBytes);

    [Fact]
    public void The_api_key_header_matches_the_authentication_package_default() =>
        HeaderNames.ApiKey.ShouldBe("X-Api-Key");

    [Fact]
    public void Allowed_extensions_are_lower_case_and_dotted() =>
        IntakeLimits.AllowedExtensions.ShouldAllBe(extension => extension.StartsWith('.') && extension == extension.ToLowerInvariant());
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: a build failure, because the Contracts types are missing.

- [ ] **Step 3: Write the Contracts files**

Create each file with the content given under **Interfaces** above. Give each type an XML summary:
- `HeaderNames`: "Header names shared by the API, Portal and SDK (02-ARCHITECTURE section 4)."
- `IntakeLimits`: "Intake limits (D-032). The API enforces them; the Portal and SDK may check them before uploading."
- `SubmitTicketRequest`: "A new ticket from a customer. Email, subject and body are required. `ExternalUserRef` and `Metadata` are trusted only from trusted API keys (D-001, D-032). Attachments arrive separately as multipart files on the web form."
- `SubmitTicketResponse`: "`ViewUrl` is set for API-key callers only; web-form submitters get the link by email. `Warnings` holds codes from `IntakeWarnings`."
- `PublicProductDto`: "Public branding for the portal. It never carries emails, keys or internal ids."

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release` and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: PASS. `ContractNamingTests` accepts the static classes and the `Dto`/`Request`/`Response` records.

- [ ] **Step 5: Commit**

```bash
git add src/TechStrap.Contracts tests/TechStrap.Application.Tests/Intake
git commit -m "feat(contracts): intake request and response, public product, limits and header names" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 3: Absolute cap on customer access tokens (D-032)

**Files:**
- Modify: `src/TechStrap.Domain/Tickets/TicketAccessToken.cs`
- Modify: `tests/TechStrap.Domain.Tests/Tickets/TicketAccessTokenTests.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Records/TicketEventRecord.cs`. `TicketAccessTokenRecord` gets an `IssuedAt` property.
- Modify: the `TicketAccessTokenRecord` configuration. Find it with `git grep -n "TicketAccessTokenRecord>" src/TechStrap.Infrastructure/Persistence/Configurations`. Add `builder.Property(t => t.IssuedAt).HasDefaultValueSql("now()");`.
- Modify: `src/TechStrap.Infrastructure/Persistence/Mapping/TicketMappings.cs` (lines 107-125).
- Create (generated): the migration `AddAccessTokenIssuedAt`.
- Modify: any test that calls `TicketAccessToken.Restore`. Find them with `git grep -n "TicketAccessToken.Restore"`.

**Interfaces:**
- Produces:
  - `TicketAccessToken.MaxLifetime` (365 days) and `TicketAccessToken.IssuedAt`.
  - `Restore(Guid id, Guid ticketId, Guid requesterId, string tokenHash, DateTimeOffset issuedAt, DateTimeOffset expiresAt, DateTimeOffset? revokedAt, DateTimeOffset? lastUsedAt)`.
  - `RecordUse` slides the expiry to the earlier of `now + Lifetime` and `IssuedAt + MaxLifetime`.

- [ ] **Step 1: Write the failing Domain tests**

Append to `TicketAccessTokenTests`:

```csharp
    [Fact]
    public void A_new_token_records_when_it_was_issued()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));

        var token = TicketAccessToken.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), "sha256:abc", clock).Value;

        token.IssuedAt.ShouldBe(clock.GetUtcNow());
        token.ExpiresAt.ShouldBe(clock.GetUtcNow() + TicketAccessToken.Lifetime);
    }

    [Fact]
    public void Use_slides_the_expiry_but_never_past_the_absolute_cap()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var token = TicketAccessToken.Issue(Guid.CreateVersion7(), Guid.CreateVersion7(), "sha256:abc", clock).Value;

        for (var day = 0; day < 400; day += 30)
        {
            clock.Advance(TimeSpan.FromDays(30));
            if (token.IsValid(clock))
            {
                token.RecordUse(clock).IsSuccess.ShouldBeTrue();
            }
        }

        token.ExpiresAt.ShouldBeLessThanOrEqualTo(token.IssuedAt + TicketAccessToken.MaxLifetime);
        clock.SetUtcNow(token.IssuedAt + TicketAccessToken.MaxLifetime);
        token.IsValid(clock).ShouldBeFalse();
    }

    [Fact]
    public void The_cap_is_one_year() => TicketAccessToken.MaxLifetime.ShouldBe(TimeSpan.FromDays(365));
```

Use the namespace, `using` lines and clock style already used in the file.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Domain.Tests -c Release`
Expected: a build failure: `IssuedAt` and `MaxLifetime` are missing.

- [ ] **Step 3: Implement the Domain change**

In `TicketAccessToken.cs`:
- Add `public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(365);` with the summary "Absolute lifetime from issue; sliding never passes it (D-032)."
- Add `public DateTimeOffset IssuedAt { get; }` and set it in the private constructor. The new constructor parameter goes after `tokenHash`.
- `Issue`: set `var now = DomainTime.Now(clock);`, then pass `now` as `issuedAt` and `now + Lifetime` as the expiry.
- `Restore`: give it the new signature shown under **Interfaces**, with `issuedAt` after `tokenHash`.
- `RecordUse`: compute `var slid = now + Lifetime; var cap = IssuedAt + MaxLifetime; ExpiresAt = slid < cap ? slid : cap;`.
- Update the class summary to mention the cap.

In `TicketAccessTokenRecord`, add `public DateTimeOffset IssuedAt { get; set; }`. In the record configuration, add `builder.Property(t => t.IssuedAt).HasDefaultValueSql("now()");`, so existing rows get the migration time as their issue time. In `TicketMappings`, pass `record.IssuedAt` to `Restore`, set `IssuedAt = token.IssuedAt` in `ToRecord`, and leave `CopyTo` unchanged, because `IssuedAt` never changes.

Fix every other `TicketAccessToken.Restore` caller you find with `git grep`.

- [ ] **Step 4: Generate the migration**

Run: `dotnet ef migrations add AddAccessTokenIssuedAt --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api`
Expected: a new migration whose `Up` method adds `issued_at` as `timestamp with time zone`, not nullable, with `defaultValueSql: "now()"`. Do not edit the generated files. If the output differs, fix the model configuration, remove the migration (`dotnet ef migrations remove ...`) and generate it again.

- [ ] **Step 5: Run the tests**

Run:
- `dotnet test --project tests/TechStrap.Domain.Tests -c Release`
- `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release`
- `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api`

Expected: PASS, and the model is clean.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(tickets): cap customer access tokens at one year from issue" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 4: Access token service

**Files:**
- Create: `src/TechStrap.Application/Security/IAccessTokenService.cs`
- Create: `src/TechStrap.Infrastructure/Security/AccessTokenService.cs`
- Modify: `src/TechStrap.Infrastructure/Security/SecurityServiceCollectionExtensions.cs`, which registers the service.
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/AccessTokenServiceTests.cs`

**Interfaces:**

```csharp
namespace TechStrap.Application.Security;

/// <summary>Customer ticket links (D-032). The plaintext token is returned once and only its hash is stored.</summary>
public interface IAccessTokenService
{
    /// <summary>A new 256-bit token for the ticket and requester, ready to stage with ITicketRepository.AddAccessToken.</summary>
    DomainResult<IssuedAccessToken> Issue(Guid ticketId, Guid requesterId);

    /// <summary>Hash used for storage and lookup: "sha256:" + lower-case hex of the UTF-8 token.</summary>
    string Hash(string token);
}

public sealed record IssuedAccessToken(string PlaintextToken, TicketAccessToken Token);
```

The Application interface may use the Domain types `DomainResult` and `TicketAccessToken`.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Infrastructure.IntegrationTests/AccessTokenServiceTests.cs`. These are plain unit tests; they live here because `AccessTokenService` is internal.

```csharp
using Microsoft.Extensions.Time.Testing;
using TechStrap.Infrastructure.Security;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class AccessTokenServiceTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));

    private AccessTokenService Service() => new(_clock);

    [Fact]
    public void Ten_thousand_tokens_never_collide()
    {
        var service = Service();

        Enumerable.Range(0, 10_000).Select(_ => service.Issue(Guid.CreateVersion7(), Guid.CreateVersion7()).Value.PlaintextToken)
            .Distinct().Count().ShouldBe(10_000);
    }

    [Fact]
    public void A_token_is_256_bits_of_base64url_and_only_its_hash_is_on_the_entity()
    {
        var issued = Service().Issue(Guid.CreateVersion7(), Guid.CreateVersion7()).Value;

        issued.PlaintextToken.ShouldMatch("^[A-Za-z0-9_-]{43}$");
        issued.Token.TokenHash.ShouldBe(Service().Hash(issued.PlaintextToken));
        issued.Token.TokenHash.ShouldStartWith("sha256:");
        issued.Token.TokenHash.ShouldNotContain(issued.PlaintextToken);
    }

    [Fact]
    public void The_hash_is_the_documented_sha256() =>
        Service().Hash("abc").ShouldBe("sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("abc"u8)));

    [Fact]
    public void A_new_token_expires_after_the_sliding_lifetime() =>
        Service().Issue(Guid.CreateVersion7(), Guid.CreateVersion7()).Value.Token.ExpiresAt.ShouldBe(_clock.GetUtcNow() + Domain.Tickets.TicketAccessToken.Lifetime);
}
```

The spec says "verify accepts only the right token". Lookup is by hash, so the check is exact-hash equality in the repository. The `Hash` determinism test and the PHASE-03 repository tests cover that; no separate `Verify` is needed.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter AccessTokenServiceTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

`src/TechStrap.Application/Security/IAccessTokenService.cs`: copy the code under **Interfaces**, with `using TechStrap.Domain;` and `using TechStrap.Domain.Tickets;`.

`src/TechStrap.Infrastructure/Security/AccessTokenService.cs`:

```csharp
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using TechStrap.Application.Security;
using TechStrap.Domain;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.Security;

internal sealed class AccessTokenService(TimeProvider clock) : IAccessTokenService
{
    private const int TokenBytes = 32;
    private const string HashScheme = "sha256:";

    public DomainResult<IssuedAccessToken> Issue(Guid ticketId, Guid requesterId)
    {
        var plaintext = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var token = TicketAccessToken.Issue(ticketId, requesterId, Hash(plaintext), clock);
        return token.IsFailure ? token.Error! : DomainResult<IssuedAccessToken>.Ok(new IssuedAccessToken(plaintext, token.Value));
    }

    public string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return HashScheme + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
```

Register it in `AddTechStrapSecurity`: `services.TryAddScoped<IAccessTokenService, AccessTokenService>();`. `TimeProvider` is already registered.

- [ ] **Step 4: Run the tests, build and commit**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter AccessTokenServiceTests`, then `dotnet build TechStrap.slnx -c Release`.
Expected: PASS, with 0 warnings.

```bash
git add src tests
git commit -m "feat(security): customer access token service with hash-only storage" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 5: Customer text and the HTML sanitizer

**Files:**
- Create: `src/TechStrap.Application/Content/IHtmlSanitizer.cs`
- Create: `src/TechStrap.Application/Intake/CustomerText.cs`
- Create: `src/TechStrap.Infrastructure/Content/HtmlSanitizerAdapter.cs`
- Create: `src/TechStrap.Infrastructure/Content/ContentServiceCollectionExtensions.cs`
- Modify: `src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj` (add `<PackageReference Include="HtmlSanitizer" />`; the version is already pinned)
- Create: `tests/TechStrap.Application.Tests/Intake/CustomerTextTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/HtmlSanitizerTests.cs`

**Interfaces:**

```csharp
namespace TechStrap.Application.Content;
/// <summary>Removes unsafe markup from HTML before it is stored or shown (D-014). Reused by the knowledge base (PHASE-08).</summary>
public interface IHtmlSanitizer { string Sanitize(string html); }

namespace TechStrap.Application.Intake;
/// <summary>Customer bodies are plain text (no Markdown, no HTML). This turns them into safe HTML paragraphs before sanitizing.</summary>
public static class CustomerText { public static string ToHtml(string text); }

// Infrastructure
public static IServiceCollection AddTechStrapContent(this IServiceCollection services); // singleton IHtmlSanitizer
```

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Application.Tests/Intake/CustomerTextTests.cs`:

```csharp
using TechStrap.Application.Intake;

namespace TechStrap.Application.Tests.Intake;

public sealed class CustomerTextTests
{
    [Fact]
    public void Pasted_markup_is_shown_as_text_not_rendered() =>
        CustomerText.ToHtml("<script>alert(1)</script> & <b>bold</b>")
            .ShouldBe("<p>&lt;script&gt;alert(1)&lt;/script&gt; &amp; &lt;b&gt;bold&lt;/b&gt;</p>");

    [Fact]
    public void Blank_lines_separate_paragraphs_and_single_newlines_become_breaks() =>
        CustomerText.ToHtml("Hi,\r\nit broke.\n\nThanks").ShouldBe("<p>Hi,<br>it broke.</p><p>Thanks</p>");

    [Fact]
    public void Surrounding_whitespace_and_extra_blank_lines_are_ignored() =>
        CustomerText.ToHtml("\n\n  one  \n\n\n\ntwo\n").ShouldBe("<p>one</p><p>two</p>");
}
```

`tests/TechStrap.Infrastructure.IntegrationTests/HtmlSanitizerTests.cs`. This needs at least 20 attack vectors (P05-T03):

```csharp
using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class HtmlSanitizerTests
{
    private readonly HtmlSanitizerAdapter _sanitizer = new();

    public static TheoryData<string> Attacks() =>
    [
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<svg onload=alert(1)>",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<a href=\"JaVaScRiPt:alert(1)\">x</a>",
        "<a href=\"&#106;avascript:alert(1)\">x</a>",
        "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>",
        "<img src=\"data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+\">",
        "<iframe src=\"https://evil.example\"></iframe>",
        "<object data=\"x\"></object>",
        "<embed src=\"x\">",
        "<body onload=alert(1)>",
        "<div style=\"background:url(javascript:alert(1))\">x</div>",
        "<style>*{background:url('javascript:alert(1)')}</style>",
        "<form action=\"javascript:alert(1)\"><button>x</button></form>",
        "<input autofocus onfocus=alert(1)>",
        "<details open ontoggle=alert(1)>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<a href=\"vbscript:msgbox(1)\">x</a>",
        "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
        "<base href=\"javascript:alert(1)//\">",
        "<link rel=\"stylesheet\" href=\"javascript:alert(1)\">",
    ];

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Attack_vectors_lose_every_script_and_handler(string html)
    {
        var clean = _sanitizer.Sanitize(html).ToLowerInvariant();

        clean.ShouldNotContain("<script");
        clean.ShouldNotContain("javascript:");
        clean.ShouldNotContain("vbscript:");
        clean.ShouldNotContain("data:");
        clean.ShouldNotContain("onerror");
        clean.ShouldNotContain("onload");
        clean.ShouldNotContain("onfocus");
        clean.ShouldNotContain("ontoggle");
        clean.ShouldNotContain("<iframe");
        clean.ShouldNotContain("<object");
        clean.ShouldNotContain("<embed");
        clean.ShouldNotContain("<meta");
        clean.ShouldNotContain("<base");
        clean.ShouldNotContain("<form");
    }

    [Fact]
    public void Safe_formatting_is_kept_and_links_get_safe_rel() =>
        _sanitizer.Sanitize("<p>Hi <strong>there</strong> <a href=\"https://example.com\">docs</a></p>")
            .ShouldSatisfyAllConditions(
                clean => clean.ShouldContain("<strong>there</strong>"),
                clean => clean.ShouldContain("href=\"https://example.com\""),
                clean => clean.ShouldContain("rel=\"noopener noreferrer nofollow\""));

    [Fact]
    public void Customer_text_survives_sanitizing_unchanged()
    {
        var html = TechStrap.Application.Intake.CustomerText.ToHtml("Line one\nLine <two>");

        _sanitizer.Sanitize(html).ShouldBe(html);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter CustomerTextTests` and `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter HtmlSanitizerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

`src/TechStrap.Application/Intake/CustomerText.cs`:

```csharp
using System.Net;
using System.Text.RegularExpressions;

namespace TechStrap.Application.Intake;

/// <summary>Customer bodies are plain text (no Markdown, no HTML). This turns them into safe HTML paragraphs before sanitizing.</summary>
public static partial class CustomerText
{
    [GeneratedRegex(@"\n\s*\n", RegexOptions.CultureInvariant)]
    private static partial Regex ParagraphBreak();

    public static string ToHtml(string text)
    {
        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        var paragraphs = ParagraphBreak().Split(normalised)
            .Select(paragraph => paragraph.Trim())
            .Where(paragraph => paragraph.Length > 0)
            .Select(paragraph => "<p>" + string.Join("<br>", paragraph.Split('\n').Select(line => WebUtility.HtmlEncode(line.Trim()))) + "</p>");
        return string.Concat(paragraphs);
    }
}
```

`src/TechStrap.Application/Content/IHtmlSanitizer.cs`: copy it from **Interfaces**.

`src/TechStrap.Infrastructure/Content/HtmlSanitizerAdapter.cs`:
- Use the Ganss.Xss `HtmlSanitizer` API, built once and configured in the constructor.
- `AllowedTags` is limited to `p, br, strong, b, em, i, u, a, ul, ol, li, blockquote, code, pre, h1, h2, h3, h4`.
- `AllowedSchemes` is `http`, `https` and `mailto`.
- No `data:` URIs, no `style` attribute, no CSS properties, and no classes.
- Add a `PostProcessNode` hook that sets `rel="noopener noreferrer nofollow"` on every `<a>`.
- Make the class `internal sealed class HtmlSanitizerAdapter : IHtmlSanitizer` with a public parameterless constructor.

**Verify the package API after restoring it.** The fact sheet could not confirm the exact member names of 9.2.1039. Run `dotnet build` after adding the reference, then use the names the compiler accepts. If `PostProcessNode` has a different shape, use the equivalent hook the package provides, such as `PostProcessDom`, and list the deviation in your report.

`ContentServiceCollectionExtensions.AddTechStrapContent()` calls `services.TryAddSingleton<IHtmlSanitizer, HtmlSanitizerAdapter>()`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run both filtered test commands from Step 2.
Expected: PASS. If any attack vector fails, tighten the configuration, not the test.

- [ ] **Step 5: Build and commit**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: 0 warnings. If the package-versions Pester check complains, check `03-PACKAGE-MAP.md`; HtmlSanitizer is already listed there.

```bash
git add src tests
git commit -m "feat(content): plain-text customer bodies and an HTML sanitizer adapter" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 6: Attachment store

**Files:**
- Create: `src/TechStrap.Application/Attachments/IAttachmentStore.cs`
- Create: `src/TechStrap.Infrastructure/Attachments/FileSignatures.cs`
- Create: `src/TechStrap.Infrastructure/Attachments/AttachmentStore.cs`
- Create: `src/TechStrap.Infrastructure/Attachments/AttachmentServiceCollectionExtensions.cs`
- Modify: `src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj` (add `<PackageReference Include="SyntaxCircus.Storage" />`)
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/AttachmentStoreTests.cs`

**Interfaces:**

```csharp
namespace TechStrap.Application.Attachments;

/// <summary>A file the customer uploaded, as a readable stream. <see cref="Length"/> is the declared length.</summary>
public sealed record IncomingAttachment(string FileName, string? DeclaredContentType, long Length, Stream Content);

/// <summary>A stored file: a random storage key, a safe display name and the canonical content type of the matched kind.</summary>
public sealed record StoredAttachment(string StorageKey, string FileName, string ContentType, long Size);

/// <summary>
/// Ticket attachments over SyntaxCircus.Storage (D-032). Validates size, type (declared and leading bytes) and stores under a
/// random key; never uses the customer's file name as a path.
/// </summary>
public interface IAttachmentStore
{
    /// <summary>Validation errors: attachment-too-large, attachment-empty, attachment-type-not-allowed (target "attachments").</summary>
    Task<Result<StoredAttachment>> SaveAsync(Guid ticketId, IncomingAttachment file, CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

// Infrastructure
public static IServiceCollection AddTechStrapAttachments(this IServiceCollection services, IConfiguration configuration);
// = AddStorageProvider(configuration) + ValidateOnStart that Storage:Local:RootPath is set when the provider is Local + scoped IAttachmentStore
```

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Infrastructure.IntegrationTests/AttachmentStoreTests.cs`. Use a temp directory as the storage root and delete it in `Dispose`. Build the store with `new AttachmentStore(new LocalFileStorageProvider(Options.Create(new LocalStorageOptions { RootPath = _root })))`. Check that constructor shape against the package.

```csharp
using System.Text;
using Microsoft.Extensions.Options;
using SyntaxCircus.Storage;
using TechStrap.Application.Attachments;
using TechStrap.Infrastructure.Attachments;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class AttachmentStoreTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n");
    private static readonly byte[] Exe = [0x4D, 0x5A, 0x90, 0x00, 0x03];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "techstrap-attachments-" + Guid.NewGuid().ToString("N"));
    private readonly AttachmentStore _store;

    public AttachmentStoreTests() =>
        _store = new AttachmentStore(new LocalFileStorageProvider(Options.Create(new LocalStorageOptions { RootPath = _root })));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static IncomingAttachment Upload(string name, string? type, byte[] bytes) => new(name, type, bytes.Length, new MemoryStream(bytes));

    [Fact]
    public async Task A_png_is_stored_under_a_random_key_with_its_canonical_type_and_reads_back()
    {
        var ticketId = Guid.CreateVersion7();

        var stored = (await _store.SaveAsync(ticketId, Upload("screen shot.png", "image/png", Png), TestContext.Current.CancellationToken)).Value;

        stored.ContentType.ShouldBe("image/png");
        stored.FileName.ShouldBe("screen shot.png");
        stored.StorageKey.ShouldStartWith($"attachments/{ticketId:N}/");
        stored.StorageKey.ShouldNotContain("screen");
        System.IO.File.ReadAllBytes(Path.Combine(_root, stored.StorageKey)).ShouldBe(Png);
    }

    [Fact]
    public async Task A_renamed_executable_is_rejected_by_its_leading_bytes()
    {
        var result = await _store.SaveAsync(Guid.CreateVersion7(), Upload("invoice.pdf", "application/pdf", Exe), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-type-not-allowed");
        Directory.Exists(_root).ShouldBeFalse();
    }

    [Theory]
    [InlineData("tool.exe", "application/octet-stream")]
    [InlineData("invoice.pdf.exe", "application/pdf")]
    [InlineData("page.html", "text/html")]
    public async Task Disallowed_extensions_are_rejected(string name, string type) =>
        (await _store.SaveAsync(Guid.CreateVersion7(), Upload(name, type, Pdf), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-type-not-allowed");

    [Fact]
    public async Task A_declared_type_that_does_not_match_the_content_is_rejected() =>
        (await _store.SaveAsync(Guid.CreateVersion7(), Upload("photo.png", "image/png", Pdf), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-type-not-allowed");

    [Fact]
    public async Task An_oversize_file_is_rejected_even_if_its_declared_length_lies()
    {
        var big = new byte[TechStrap.Contracts.Intake.IntakeLimits.MaxFileBytes + 1];
        Png.CopyTo(big, 0);

        var result = await _store.SaveAsync(Guid.CreateVersion7(), new IncomingAttachment("big.png", "image/png", 10, new MemoryStream(big)), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-too-large");
        (Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : []).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_path_traversal_file_name_never_escapes_the_root()
    {
        var stored = (await _store.SaveAsync(Guid.CreateVersion7(), Upload("../../etc/passwd.txt", "text/plain", "hello"u8.ToArray()), TestContext.Current.CancellationToken)).Value;

        stored.FileName.ShouldBe("passwd.txt");
        Path.GetFullPath(Path.Combine(_root, stored.StorageKey)).ShouldStartWith(Path.GetFullPath(_root));
    }

    [Fact]
    public async Task Text_with_binary_content_is_rejected() =>
        (await _store.SaveAsync(Guid.CreateVersion7(), Upload("app.log", "text/plain", [0x41, 0x00, 0x42]), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe("attachment-type-not-allowed");

    [Fact]
    public async Task Delete_removes_the_file_and_is_harmless_twice()
    {
        var stored = (await _store.SaveAsync(Guid.CreateVersion7(), Upload("a.pdf", "application/pdf", Pdf), TestContext.Current.CancellationToken)).Value;

        await _store.DeleteAsync(stored.StorageKey, TestContext.Current.CancellationToken);
        await _store.DeleteAsync(stored.StorageKey, TestContext.Current.CancellationToken);

        System.IO.File.Exists(Path.Combine(_root, stored.StorageKey)).ShouldBeFalse();
    }
}
```

Qualify file-system calls as `System.IO.File` and `System.IO.Directory`, so they cannot clash with the `Upload` helper or with any `File` member in the test base.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter AttachmentStoreTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

`FileSignatures.cs` (internal static class). Map each allowed extension to a canonical content type and a content check:

| Extensions | Content type | Content check |
| --- | --- | --- |
| `.png` | `image/png` | starts with `89 50 4E 47 0D 0A 1A 0A` |
| `.jpg`, `.jpeg` | `image/jpeg` | starts with `FF D8 FF` |
| `.gif` | `image/gif` | starts with ASCII `GIF87a` or `GIF89a` |
| `.webp` | `image/webp` | bytes 0-3 are `RIFF` and bytes 8-11 are `WEBP` |
| `.pdf` | `application/pdf` | starts with `%PDF-` |
| `.zip` | `application/zip` | starts with `PK 03 04` or `PK 05 06` |
| `.txt`, `.log` | `text/plain` | no `0x00` byte, and valid UTF-8, in the first 8 KiB |
| `.csv` | `text/csv` | same text check |

Expose `static bool TryMatch(string extension, ReadOnlySpan<byte> head, out string contentType)`.

A declared content type is accepted when it is null or blank, `application/octet-stream`, or equal (ignoring case and any `;` parameters) to the canonical type. For text kinds it may also be any `text/*`. Anything else is rejected.

`AttachmentStore` (`internal sealed`, primary constructor taking `IStorageProvider storage`). `SaveAsync` does the following, in order:
1. Build a safe display name: `Path.GetFileName` with both `/` and `\` normalised, control and format characters stripped, trimmed, cut to `DomainLimits.FileNameMaxLength`, and `"attachment"` if empty or `.`/`..`.
2. Take the extension in lower case, and reject it if it is not in `IntakeLimits.AllowedExtensions`.
3. If the declared `Length` is more than `IntakeLimits.MaxFileBytes`, return `attachment-too-large`.
4. Read the stream into a pooled buffer, capped at `MaxFileBytes + 1` bytes:
   - Read the first 8 KiB as the head.
   - Read the rest into a `MemoryStream`, or a temp-file stream for large files. Simplest: a `MemoryStream` capped at 10 MiB.
   - If the total exceeds `MaxFileBytes`, return `attachment-too-large`. If it is 0, return `attachment-empty`.
5. Call `FileSignatures.TryMatch` and the declared-type rule. On failure, return `attachment-type-not-allowed`.
6. Build the key `$"attachments/{ticketId:N}/{Guid.CreateVersion7():N}"`. Store the content with `IStorageProvider.StoreAsync(new StoreObjectRequest(key, content, contentType), ct)`.
7. Return `StoredAttachment(key, safeName, contentType, size)`.

Every validation error is `new ResultError(code, message, ResultErrorKind.Validation, "attachments")`, with messages in plain customer-facing language, for example "This file type isn't accepted. Attach images, PDFs, text, CSV or ZIP files." Nothing is written before validation passes.

`DeleteAsync` calls `storage.DeleteAsync(key, ct)`.

`AttachmentServiceCollectionExtensions.AddTechStrapAttachments(services, configuration)`:
- Call `services.AddStorageProvider(configuration)`.
- Add `services.AddOptions<LocalStorageOptions>().Bind(configuration.GetSection(LocalStorageOptions.SectionName)).Validate(o => !string.Equals(configuration["Storage:Provider"] ?? "Local", "Local", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(o.RootPath), "Storage:Local:RootPath is required for the Local storage provider.").ValidateOnStart();`
- Register `services.TryAddScoped<IAttachmentStore, AttachmentStore>();`.

- [ ] **Step 4: Run the tests, build and commit**

Run the filtered tests, then `dotnet build TechStrap.slnx -c Release`.
Expected: PASS, with 0 warnings.

```bash
git add src tests
git commit -m "feat(attachments): validated attachment store with content sniffing over SyntaxCircus.Storage" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 7: Email templates (confirmation) with product branding and Powered-by

**Files:**
- Create: `src/TechStrap.Application/Email/EmailTemplates.cs`
- Create: `src/TechStrap.Application/Email/RenderedEmail.cs`
- Create: `src/TechStrap.Application/Email/IEmailTemplateRenderer.cs`
- Create: `src/TechStrap.Application/Email/EmailBrandingOptions.cs`
- Create: `src/TechStrap.Infrastructure/Email/EmailTemplateRenderer.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/EmailTemplateRendererTests.cs`

**Interfaces:**

```csharp
namespace TechStrap.Application.Email;

/// <summary>Outbox kinds and their payloads (D-033): the payload is template data, never a rendered body.</summary>
public static class EmailTemplates
{
    public const string TicketConfirmation = "ticket-confirmation";
}

/// <summary>Payload of a ticket-confirmation outbox row. <see cref="AgentPublicName"/> is the already-resolved public name (D-024) or null.</summary>
public sealed record TicketConfirmationEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink, string? AgentPublicName);

/// <summary>The branding a template needs, taken from the product at send time.</summary>
public sealed record EmailBranding(string DisplayName, string? LogoPath, string AccentColour, string? FromAddress, string? ReplyTo);

public sealed record RenderedEmail(string Subject, string Text, string Html, string? From, string? ReplyTo);

public interface IEmailTemplateRenderer
{
    RenderedEmail RenderTicketConfirmation(TicketConfirmationEmail model, EmailBranding branding);
}

/// <summary>Installation-wide email branding (D-024): <c>TECHSTRAP_PORTAL_SHOW_POWERED_BY</c>, default true.</summary>
public sealed class EmailBrandingOptions
{
    public const string ShowPoweredByKey = "TECHSTRAP_PORTAL_SHOW_POWERED_BY";
    public const string PoweredByUrl = "https://github.com/Syntax-Circus/techstrap";
    public bool ShowPoweredBy { get; set; } = true;
}
```

`EmailTemplateRenderer` (Infrastructure, `internal sealed`) has the constructor `EmailTemplateRenderer(IOptions<EmailBrandingOptions> options)`.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Infrastructure.IntegrationTests/EmailTemplateRendererTests.cs`:

```csharp
using Microsoft.Extensions.Options;
using TechStrap.Application.Email;
using TechStrap.Infrastructure.Email;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class EmailTemplateRendererTests
{
    private static readonly EmailBranding Orbitly = new("Orbitly", "https://cdn.orbitly.example/logo.png", "#7C3AED", "support@orbitly.example", "help@orbitly.example");
    private static readonly EmailBranding Amber = new("Paperplane", null, "#F59E0B", null, null);
    private static readonly TicketConfirmationEmail Model = new("ORB-38", "Can't sign in", "Ann", "https://help.example/t/abc", null);

    private static EmailTemplateRenderer Renderer(bool showPoweredBy = true) =>
        new(Options.Create(new EmailBrandingOptions { ShowPoweredBy = showPoweredBy }));

    [Fact]
    public void The_confirmation_uses_the_product_name_number_and_link()
    {
        var email = Renderer().RenderTicketConfirmation(Model, Orbitly);

        email.Subject.ShouldBe("[ORB-38] We've got your request: Can't sign in");
        email.Text.ShouldContain("ORB-38");
        email.Text.ShouldContain("https://help.example/t/abc");
        email.Html.ShouldContain("Orbitly");
        email.Html.ShouldContain("href=\"https://help.example/t/abc\"");
        email.From.ShouldBe("Orbitly <support@orbitly.example>");
        email.ReplyTo.ShouldBe("help@orbitly.example");
    }

    [Fact]
    public void A_product_without_addresses_leaves_from_and_reply_to_to_the_installation_default()
    {
        var email = Renderer().RenderTicketConfirmation(Model, Amber);

        email.From.ShouldBeNull();
        email.ReplyTo.ShouldBeNull();
    }

    [Fact]
    public void The_accent_rule_from_phase_02_is_applied()
    {
        var html = Renderer().RenderTicketConfirmation(Model, Amber).Html;

        html.ShouldContain("#F59E0B");
        html.ShouldContain("#000000");
        html.ShouldContain("#9D6507");
    }

    [Fact]
    public void Customer_content_is_escaped_in_html_and_plain_in_text()
    {
        var email = Renderer().RenderTicketConfirmation(Model with { Subject = "<script>alert(1)</script>", RequesterName = "<b>Ann</b>" }, Orbitly);

        email.Html.ShouldNotContain("<script>");
        email.Html.ShouldContain("&lt;script&gt;");
        email.Html.ShouldNotContain("<b>Ann</b>");
        email.Text.ShouldNotContain("&lt;");
    }

    [Fact]
    public void Powered_by_links_to_github_in_html_and_is_a_bare_url_in_text()
    {
        var email = Renderer().RenderTicketConfirmation(Model, Orbitly);

        email.Html.ShouldContain($"href=\"{EmailBrandingOptions.PoweredByUrl}\"");
        email.Html.ShouldContain("Powered by TechStrap");
        email.Text.ShouldContain($"Powered by TechStrap: {EmailBrandingOptions.PoweredByUrl}");
    }

    [Fact]
    public void Powered_by_is_omitted_when_the_setting_is_false()
    {
        var email = Renderer(showPoweredBy: false).RenderTicketConfirmation(Model, Orbitly);

        email.Html.ShouldNotContain("TechStrap");
        email.Text.ShouldNotContain("TechStrap");
    }

    [Fact]
    public void The_agent_public_name_is_shown_when_present_and_the_model_cannot_carry_an_email()
    {
        Renderer().RenderTicketConfirmation(Model with { AgentPublicName = "Sam from Orbitly Support" }, Orbitly)
            .Text.ShouldContain("Sam from Orbitly Support");
        typeof(TicketConfirmationEmail).GetProperties().Select(property => property.Name)
            .ShouldNotContain(name => name.Contains("Email", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Two_products_render_their_own_branding()
    {
        var orbitly = Renderer().RenderTicketConfirmation(Model, Orbitly).Html;
        var paperplane = Renderer().RenderTicketConfirmation(Model, Amber).Html;

        orbitly.ShouldContain("#7C3AED");
        orbitly.ShouldContain("https://cdn.orbitly.example/logo.png");
        paperplane.ShouldContain("Paperplane");
        paperplane.ShouldNotContain("Orbitly");
        paperplane.ShouldNotContain("<img");
    }
}
```

Check the BRAND.md section 22 vector for `#F59E0B`. Assert the exact on-accent and ink values that `ProductAccent.TryDerive` returns; BRAND.md lists `#000000` and `#9D6507`.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter EmailTemplateRendererTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

Create the Application types from **Interfaces**.

`EmailTemplateRenderer`:
- **Subject:** `$"[{model.TicketNumber}] We've got your request: {model.Subject}"`.
- **Text:**
  - A greeting: "Hi {RequesterName}," or "Hi," when there is no name.
  - "We've got your request and created ticket {number}."
  - "Follow it here: {link}".
  - If `AgentPublicName` is set: "{AgentPublicName} is looking after it."
  - The sign-off "The {DisplayName} team".
  - If `ShowPoweredBy`, a blank line and then `Powered by TechStrap: {PoweredByUrl}`.
  - Use plain characters only, with no HTML entities.
- **HTML:**
  - A single-column table layout with inline styles only (no `<style>` block).
  - The product logo `<img>` only when `LogoPath` is set and starts with `https://`.
  - A header bar filled with `colors.Accent` and text in `colors.OnAccent`.
  - The ticket number in bold.
  - A button link styled with the accent background and on-accent text.
  - Any other link text in `colors.AccentInk`.
  - Get the colours with `ProductAccent.TryDerive(branding.AccentColour, out var colors)`, falling back to `ProductBranding.DefaultAccentColour`.
  - HTML-encode every model value with `WebUtility.HtmlEncode`.
  - The footer has `Powered by <a href="{PoweredByUrl}">TechStrap</a>` only when `ShowPoweredBy`.
  - No mascot, no TechStrap colours.
- **From:** `$"{DisplayName} <{FromAddress}>"` when `FromAddress` is set, otherwise null. **ReplyTo:** `branding.ReplyTo`.

The "Powered by TechStrap" text is a single constant in the renderer, and the URL is `EmailBrandingOptions.PoweredByUrl`.

- [ ] **Step 4: Run the tests, build and commit**

Run the filtered tests, then `dotnet build TechStrap.slnx -c Release`.
Expected: PASS, with 0 warnings.

```bash
git add src tests
git commit -m "feat(email): branded ticket confirmation template with the Powered-by rule" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 8: Intake idempotency store (D-020)

**Files:**
- Create: `src/TechStrap.Application/Persistence/IIntakeIdempotencyStore.cs`
- Create: `src/TechStrap.Infrastructure/Persistence/Repositories/IntakeIdempotencyStore.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs` (register the store as scoped)
- Modify: `tests/TechStrap.Architecture.Tests/AbstractionShapeTests.cs` (add the new interface to the expected list)
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/IntakeIdempotencyStoreTests.cs`

**Interfaces:**

```csharp
namespace TechStrap.Application.Persistence;

/// <summary>A stored intake response for one API key and Idempotency-Key (D-020). <see cref="ResponseJson"/> is the serialised SubmitTicketResponse.</summary>
public sealed record IntakeIdempotencyEntry(Guid Id, Guid ApiKeyId, Guid TicketId, string ResponseJson, DateTimeOffset CreatedAt);

/// <summary>
/// Idempotency-Key lookups for API-key intake (D-020), over the existing intake_idempotency_keys table. Keys are hashed before
/// storage. Writes are staged in the caller's unit of work; a concurrent duplicate surfaces as a "duplicate" commit conflict.
/// </summary>
public interface IIntakeIdempotencyStore
{
    /// <summary>24 hours (D-020).</summary>
    static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    Task<IntakeIdempotencyEntry?> FindAsync(Guid apiKeyId, string idempotencyKey, CancellationToken cancellationToken);

    void Add(Guid apiKeyId, string idempotencyKey, Guid ticketId, string responseJson, DateTimeOffset createdAt);

    /// <summary>Stages removal of an expired entry so its key can be reused.</summary>
    void Remove(IntakeIdempotencyEntry entry);

    /// <summary>Deletes up to <paramref name="limit"/> entries created before <paramref name="olderThan"/>; returns the count.</summary>
    Task<int> PruneAsync(DateTimeOffset olderThan, int limit, CancellationToken cancellationToken);
}
```

A static field in an interface may trip the analyzer or the architecture rules. If it does, move `Retention` to a `public static class IntakeIdempotency { public static readonly TimeSpan Retention = TimeSpan.FromHours(24); }` in the same file, and use that name in Task 9.

- [ ] **Step 1: Write the failing integration tests**

`tests/TechStrap.Infrastructure.IntegrationTests/IntakeIdempotencyStoreTests.cs` uses `PostgresIntegrationTestBase`, `PersistenceTestHost` and `RecordSeed`/`TicketScenario`, following `KnowledgeAndOutboxSchemaTests.An_idempotency_key_is_unique_per_api_key` for how it seeds an API key and a ticket. Write these tests:
- `A_stored_entry_is_found_by_the_same_api_key_and_key`
  - Add an entry, commit, then find it.
  - Assert that `ResponseJson`, `TicketId` and `CreatedAt` round-trip.
  - Assert that the stored `key_hash` column is `"sha256:" + hex(SHA256(key))` and never the raw key. Check with a SQL scalar query.
- `The_same_key_under_another_api_key_is_not_found`.
- `A_concurrent_duplicate_is_a_duplicate_commit_conflict`. Add the same `(apiKeyId, key)` in two scopes; the second commit returns Conflict `duplicate`.
- `Remove_then_add_reuses_an_expired_key_in_one_commit`. Use one unit-of-work scope: Find, then Remove, then Add a new entry for a different ticket, then commit. The commit succeeds and Find returns the new entry.
- `Prune_deletes_only_old_entries_and_respects_the_limit`. Create three old entries and one new one, then call `PruneAsync(cutoff, limit: 2)`. Expect the result 2 and two entries remaining (one old, one new).

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter IntakeIdempotencyStoreTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

`IntakeIdempotencyStore(TechStrapDbContext context)`, `internal sealed`:
- **Hashing.** Hash with the same SHA-256 scheme as `ApiKeyFormat` (`"sha256:" + Convert.ToHexStringLower(SHA256.HashData(UTF8))`) in a private static `HashKey(string)` helper.
- **`FindAsync`.** `AsNoTracking`, filtered on `ApiKeyId` and `KeyHash`; map to the entry.
- **`Add`.** Stage `new IntakeIdempotencyKeyRecord { Id = Guid.CreateVersion7(createdAt), ApiKeyId, KeyHash, TicketId, Response = responseJson, CreatedAt = createdAt }`.
- **`Remove`.** Attach and delete the record by `Id`. Use `context.Set<...>().Remove(new IntakeIdempotencyKeyRecord { Id = entry.Id, ... })`, setting the required fields, or load it first if tracking conflicts.
- **`PruneAsync`.** Select up to `limit` ids ordered by `CreatedAt` where `CreatedAt < olderThan`, then `ExecuteDeleteAsync` on those ids. The append-only bypass rule covers only the ticket-event and admin-event tables, so this is allowed. Return the count.

Register the store in `AddTechStrapPersistence` with `services.AddScoped<IIntakeIdempotencyStore, IntakeIdempotencyStore>();`.

Add `IIntakeIdempotencyStore` to the names list in `AbstractionShapeTests` (the "12 persistence abstractions" assertion). If that test asserts an exact count, update the count.

- [ ] **Step 4: Run the tests, build and commit**

Run the filtered tests, `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`, and `dotnet build TechStrap.slnx -c Release`.
Expected: PASS. `has-pending-model-changes` stays clean, because this task changes no schema.

```bash
git add src tests
git commit -m "feat(intake): idempotency key store over the existing intake_idempotency_keys table" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 9: `SubmitTicketRequestHandler`

**Files:**
- Create: `src/TechStrap.Application/Intake/IntakeChannel.cs`
- Create: `src/TechStrap.Application/Intake/SubmitTicketContext.cs`
- Create: `src/TechStrap.Application/Intake/PortalLinkOptions.cs`
- Create: `src/TechStrap.Application/Intake/IntakeErrors.cs`
- Create: `src/TechStrap.Application/Intake/SubmitTicketRequestHandler.cs`
- Create: `tests/TechStrap.Application.Tests/Intake/SubmitTicketRequestHandlerTests.cs`

**Interfaces:**
- Consumes:
  - Tasks 2 to 8: `SubmitTicketRequest`, `SubmitTicketResponse`, `IntakeLimits`, `IntakeWarnings`, `IAccessTokenService`, `IHtmlSanitizer`, `CustomerText`, `IAttachmentStore`, `IncomingAttachment`, `StoredAttachment`, `TicketConfirmationEmail`, `EmailTemplates`, `IIntakeIdempotencyStore`.
  - PHASE-03 code: `IProductRepository.GetByKeyAsync`, `GetByIdAsync`, `GetApiKeyAsync` and `UpdateApiKey`; `IRequesterRepository`; `ITicketRepository.Add` and `AddAccessToken`; `ITicketNumberAllocator.AllocateAsync`; `IEmailOutbox.Enqueue`; `EmailOutboxItem.Enqueue(kind, to, payloadJson, productId, ticketId, clock)`; `Ticket.Create(number, productId, requesterId, subject, TicketChannel, metadataJson, metadataTrusted, clock)`; `ticket.AddCustomerReply(requesterId, body, clock)`; `message.AddAttachment(fileName, contentType, size, storageKey, clock)`; `Requester.Create` and `UpdateProfile`; `ProductApiKey.RecordUse`.
- Produces:

```csharp
namespace TechStrap.Application.Intake;
public enum IntakeChannel { Web, Api }

/// <summary>
/// Everything the transport knows about a submission that is not in the request body (D-016, D-034). Built by the controllers:
/// the web form sets ProductKey, the honeypot flag and attachments; the API sets ProductId, ApiKeyId, Trusted and IdempotencyKey
/// from the authenticated key principal and headers.
/// </summary>
public sealed record SubmitTicketContext(
    IntakeChannel Channel,
    string? ProductKey,
    Guid? ProductId,
    Guid? ApiKeyId,
    bool Trusted,
    bool HoneypotTripped,
    IReadOnlyList<IncomingAttachment> Attachments,
    string? IdempotencyKey);

/// <summary>The customer portal base URL (TECHSTRAP_PORTAL_PUBLIC_URL) used to build ticket links.</summary>
public sealed class PortalLinkOptions
{
    public const string PublicUrlKey = "TECHSTRAP_PORTAL_PUBLIC_URL";
    public string PublicUrl { get; set; } = string.Empty;
    public string TicketLink(string token) => $"{PublicUrl.TrimEnd('/')}/t/{token}";
}

public interface ISubmitTicketRequestHandler
{
    Task<Result<SubmitTicketResponse>> HandleAsync(SubmitTicketRequest request, SubmitTicketContext context, CancellationToken cancellationToken);
}
```

`IntakeErrors` is `internal static`, and each member returns a `ResultError`:
- `ProductNotFound()`: NotFound, `product-not-found`.
- `ApiKeyRequired()`: Unauthenticated, `api-key-required`.
- `TooManyFiles()`: Validation, `attachments-too-many`, target `attachments`.
- `MessageTooLarge()`: Validation, `attachments-too-large`, target `attachments`.
- `MetadataInvalid(string message)`: Validation, `metadata-invalid`, target `metadata`.
- `IdempotencyKeyInvalid()`: Validation, `idempotency-key-invalid`, target `Idempotency-Key`.

**Flow** (implement it exactly; the tests pin each step):
1. **Shape checks, before anything else.** Return the first error found:
   - More than `IntakeLimits.MaxFiles` files fails with `TooManyFiles`.
   - A declared total over `IntakeLimits.MaxMessageBytes` fails with `MessageTooLarge`.
   - Metadata fails with `MetadataInvalid` when it has more than `MaxMetadataKeys` keys, a blank key, a key longer than `MaxMetadataKeyLength`, or a value longer than `MaxMetadataValueLength`.
   - A non-null `IdempotencyKey` that is blank or longer than `MaxIdempotencyKeyLength` fails with `IdempotencyKeyInvalid`.
   - On the Api channel, a null `ProductId` or `ApiKeyId` fails with `ApiKeyRequired`.
2. **Resolve the product.** The Web channel uses `GetByKeyAsync(ProductKey ?? "")`; the Api channel uses `GetByIdAsync(ProductId)`. A missing or inactive product returns `ProductNotFound`.
3. **Honeypot.** If `HoneypotTripped`, return `Success(new SubmitTicketResponse($"{product.NumberPrefix}-{Random.Shared.Next(1000, 100000)}", null, []))`. Write nothing.
4. **Warnings.** If the submission is not `Trusted` and `ExternalUserRef` is not blank, add `IntakeWarnings.ExternalUserRefIgnored` and ignore the reference.
5. **Body.** Compute `CustomerText.ToHtml(request.Body ?? "")` and pass the result through `sanitizer.Sanitize`. An empty result stays empty, so the Domain reports `body-required`.
6. **Metadata JSON.** `null` when the metadata is null or empty. Otherwise serialise it with `System.Text.Json` (Web defaults).
7. **Attempt loop.** Allow at most 2 attempts; a second attempt happens only when the commit fails with `PersistenceErrorCodes.Duplicate`. Keep a `List<string> stored` of storage keys saved in the current attempt. Each attempt:
   1. `await using var scope = await unitOfWork.BeginAsync(ct)`.
   2. **Idempotency (Api channel with a key).** Call `entry = await idempotency.FindAsync(apiKeyId, key, ct)`.
      - If the entry is newer than `now - Retention`, this is a replay. The stored response never holds a link (step 10), so issue a fresh one for the original ticket:
        - `ticket = await tickets.GetByIdAsync(entry.TicketId, ct)`; if it is null (deleted), treat the entry as expired (next bullet).
        - `issued = tokens.Issue(ticket.Id, ticket.RequesterId)`, then `tickets.AddAccessToken(issued.Value.Token)`.
        - Commit. On success, return the deserialised stored response `with { ViewUrl = portal.Value.TicketLink(issued.Value.PlaintextToken) }`. On failure, return the failure.
        - No ticket, requester, outbox row or key-use update is created on a replay.
      - If an entry exists but is older, call `idempotency.Remove(entry)`.
   3. **Requester.** Call `requesters.GetByEmailAsync(email)`.
      - If none exists, call `Requester.Create(email, name, Trusted ? externalRef : null, clock)`; on failure return the converted error. Then `requesters.Add`.
      - If one exists:
        - `newName = existing.Name ?? trimmed(name)`;
        - `newRef = Trusted ? (blank(externalRef) ? existing.ExternalUserRef : externalRef) : existing.ExternalUserRef`;
        - if either value differs, call `UpdateProfile(newName, newRef)` and then `requesters.Update`.
   4. **Ticket.**
      1. Call `number = await allocator.AllocateAsync(product.Id, ct)`, and return the failure if there is one.
      2. Call `ticket = Ticket.Create(number, product.Id, requester.Id, request.Subject, channel, metadataJson, Trusted, clock)`, and return the converted failure if there is one.
      3. Call `message = ticket.AddCustomerReply(requester.Id, bodyHtml, clock)`, and return the converted failure if there is one.
   5. **Attachments.** For each one:
      - Call `saved = await attachments.SaveAsync(ticket.Id, file, ct)`. On failure, delete everything already stored and return the error.
      - Add `saved.StorageKey` to `stored`.
      - Call `message.Value.AddAttachment(saved.FileName, saved.ContentType, saved.Size, saved.StorageKey, clock)`. On failure, delete the stored files and return the converted error.

      When the files are read from a non-seekable stream, a second attempt cannot re-read them. On a duplicate retry with attachments present, return the conflict instead of retrying. Web-form requester races are rare, and the customer can resubmit.
   6. Call `tickets.Add(ticket)`.
   7. **Token.** Call `issued = tokens.Issue(ticket.Id, requester.Id)` and then `tickets.AddAccessToken(issued.Value.Token)`. Build `link = portal.Value.TicketLink(issued.Value.PlaintextToken)`.
   8. **Outbox.** Build the payload JSON with `TicketConfirmationEmail(number, ticket.Subject, requester.Name, link, null)`, then call `EmailOutboxItem.Enqueue(EmailTemplates.TicketConfirmation, requester.Email, payload, product.Id, ticket.Id, clock)`.
      - If it succeeds, call `outbox.Enqueue(item.Value)`.
      - If it fails, log a warning with `ILogger<SubmitTicketRequestHandler>` (the error code only) and carry on. A failed email never fails the submission.
   9. **Response.** `new SubmitTicketResponse(number.ToString(), channel == Api ? link : null, warnings)`.
   10. **Api channel bookkeeping.**
       - Call `key = await products.GetApiKeyAsync(ApiKeyId, ct)`. If the key is not null, call `key.RecordUse(clock)`; if that succeeds, call `products.UpdateApiKey(key)`.
       - If there is an idempotency key, call `idempotency.Add(apiKeyId, key, ticket.Id, json(response with { ViewUrl = null }), now)`. The link holds a plaintext access token, which must never be stored outside `email_outbox.payload` (D-033).
   11. **Commit.**
       - On success: when an idempotency key was used, call `await idempotency.PruneAsync(now - Retention, 100, ct)`. Then return the response.
       - On failure: delete every key in `stored` (DeleteAsync, ignoring exceptions) and clear `stored`.
         - If the error is `Duplicate`, this is attempt 1, and there are no attachments, retry.
         - Otherwise return the failure.
   12. Wrap each attempt in `try { ... } catch { delete stored; throw; }`, so an exception also removes any files it saved.

The handler's constructor takes these dependencies:
`IProductRepository products`, `IRequesterRepository requesters`, `ITicketRepository tickets`, `ITicketNumberAllocator allocator`, `IAccessTokenService tokens`, `IAttachmentStore attachments`, `IHtmlSanitizer sanitizer`, `IEmailOutbox outbox`, `IIntakeIdempotencyStore idempotency`, `IUnitOfWork unitOfWork`, `TimeProvider clock`, `IOptions<PortalLinkOptions> portal`, `ILogger<SubmitTicketRequestHandler> logger`.

Application can see `IOptions` and `ILogger` through `SyntaxCircus.Common`'s framework reference. If the `using` lines fail to compile, check `obj/project.assets.json` and say so in your report.

- [ ] **Step 1: Write the failing handler tests**

`tests/TechStrap.Application.Tests/Intake/SubmitTicketRequestHandlerTests.cs`:
- Use NSubstitute for every dependency, `UnitOfWorkSubstitute.Create(...)` (PHASE-04), and `FakeTimeProvider(2026-10-03T09:00Z)`.
- Use a real `Product` (`Product.Create("orbitly","Orbitly","ORB", null, clock)`), returned by both `GetByKeyAsync("orbitly")` and `GetByIdAsync`.
- `allocator.AllocateAsync` returns `Result<TicketNumber>.Success(TicketNumber.Create("ORB", 42).Value)`.
- `tokens.Issue` returns a real `TicketAccessToken.Issue(...)` with plaintext `"tok"`.
- `sanitizer.Sanitize` returns its input.
- `attachments.SaveAsync` returns `StoredAttachment("attachments/x/y", file.FileName, "image/png", file.Length)`.
- Portal options use `PublicUrl = "https://help.test/"`.
- Build the handler in a `Handler()` method.

Write one test for each case below. Each name states the behaviour it asserts.

```csharp
    [Fact] public async Task A_web_submission_creates_requester_ticket_message_token_and_queues_the_confirmation()
    // channel Web, ProductKey "orbitly"; asserts:
    //   requesters.Add one, tickets.Add one ticket with Channel Web and MetadataTrusted false,
    //   tickets.AddAccessToken once, outbox.Enqueue one item with Kind "ticket-confirmation", ToAddress "ann@example.com",
    //   and payload containing "\"portalLink\":\"https://help.test/t/tok\"";
    //   response.TicketNumber "ORB-42", ViewUrl null, Warnings empty.

    [Fact] public async Task A_trusted_api_submission_keeps_the_external_ref_and_trusted_metadata_and_returns_the_view_url()
    // channel Api, Trusted true, ExternalUserRef "u-1", Metadata { plan = "pro" }; asserts:
    //   new requester.ExternalUserRef "u-1", ticket.MetadataTrusted true, ticket.MetadataJson contains "pro",
    //   ViewUrl "https://help.test/t/tok", no warnings.

    [Theory]
    [InlineData(IntakeChannel.Web)]
    [InlineData(IntakeChannel.Api)]
    public async Task An_untrusted_submission_drops_the_external_ref_with_a_warning_and_marks_metadata_untrusted(IntakeChannel channel)
    // Trusted false; asserts the requester has no ref, MetadataTrusted false, Warnings == ["external-user-ref-ignored"].

    [Fact] public async Task An_existing_requester_is_reused_case_insensitively_and_only_blank_names_are_filled()
    // GetByEmailAsync("ANN@example.com") returns an existing requester (Name null); the request name "Ann";
    // asserts no Add, Update called once, requester.Name "Ann"; a second test row with an existing Name "Annie" keeps "Annie".

    [Fact] public async Task An_untrusted_submitter_never_overwrites_a_known_external_ref()

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task An_unknown_or_inactive_product_is_not_found(bool exists)

    [Fact] public async Task A_honeypot_hit_returns_a_normal_looking_success_and_writes_nothing()
    // asserts the number starts with "ORB-", unitOfWork.BeginAsync never called, outbox/tickets/attachments untouched.

    [Fact] public async Task More_than_five_files_or_more_than_25_mib_in_total_is_a_field_error_before_anything_is_stored()

    [Fact] public async Task A_rejected_attachment_removes_files_already_stored_and_saves_nothing()
    // the second SaveAsync returns a Validation failure; asserts DeleteAsync was called for the first key and no commit happened.

    [Fact] public async Task A_failed_commit_deletes_the_stored_attachments()
    // UnitOfWorkSubstitute.Create(Conflict("concurrency-conflict")); asserts DeleteAsync was called for each stored key and the result is Conflict.

    [Fact] public async Task The_body_is_plain_text_turned_into_sanitised_html()
    // body "<b>hi</b>"; asserts sanitizer.Sanitize received "<p>&lt;b&gt;hi&lt;/b&gt;</p>".

    [Fact] public async Task A_repeated_idempotency_key_returns_the_original_ticket_with_a_fresh_link_and_creates_nothing_else()
    // idempotency.FindAsync returns an entry 1 h old with ResponseJson of ("ORB-7", null, []) and tickets.GetByIdAsync returns a ticket;
    // asserts TicketNumber "ORB-7", ViewUrl "https://help.test/t/tok", AddAccessToken once, one commit,
    // allocator, tickets.Add, requesters.Add and outbox.Enqueue never called.

    [Fact] public async Task The_stored_idempotent_response_never_holds_the_link()
    // a first submission with an idempotency key; asserts idempotency.Add received a responseJson that does not contain "/t/"
    // and whose viewUrl is null.

    [Fact] public async Task An_expired_idempotency_key_is_replaced()
    // the entry is 25 h old; asserts Remove(entry) and Add(...) were called and a new ticket was created.

    [Theory] [MemberData(nameof(BadIdempotencyKeys))] // "", " ", new string('k', 201)
    public async Task A_blank_or_over_long_idempotency_key_is_a_field_error(string key)

    [Fact] public async Task An_api_submission_without_a_key_principal_is_unauthenticated()

    [Fact] public async Task A_queueing_failure_never_fails_the_submission()
    // see the note below the list for how to force the Domain enqueue failure.

    [Fact] public async Task A_duplicate_conflict_on_the_first_attempt_is_retried_once()
    // UnitOfWorkSubstitute.Create(Conflict("duplicate"), Success); asserts two BeginAsync calls and a success.

    [Fact] public async Task The_api_key_use_is_recorded()

    [Fact] public async Task Cancellation_reaches_the_repositories_store_and_allocator()
    // pass a token from a CancellationTokenSource; assert the allocator, attachments.SaveAsync and requesters.GetByEmailAsync received that token.
```

Write the complete body of each test, building the request and context inline in the test. Use `Arg.Is<>` to assert on staged entities.

`A_queueing_failure_never_fails_the_submission`: set the portal URL to `"https://help.test/" + new string('a', 16_000)`. The confirmation payload then exceeds `DomainLimits.OutboxPayloadMaxLength`, so `EmailOutboxItem.Enqueue` fails. Assert that the submission succeeds and that `outbox.Enqueue` is never received.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter SubmitTicketRequestHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement the handler**

Write the types listed under Interfaces and the handler, following the Flow above. Keep the code readable:
- put the shape checks in a private static `Validate(...)` method that returns `ResultError?`;
- put the requester upsert in a private method;
- put the attempt body in a private method that returns `(Result<SubmitTicketResponse> Result, bool RetryOnDuplicate)`.

Every exit from an attempt must delete the files that attempt stored, except a successful commit.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter SubmitTicketRequestHandlerTests`
Expected: PASS.

- [ ] **Step 5: Run the architecture tests, build and commit**

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release` and `dotnet build TechStrap.slnx -c Release`.
Expected: PASS. Every constructor dependency is approved, and the build has 0 warnings.

```bash
git add src tests
git commit -m "feat(intake): submit ticket handler for web and API-key channels" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 10: Submit-ticket integration tests (real Postgres and real storage)

**Files:**
- Create: `src/TechStrap.Infrastructure/Intake/IntakeServiceCollectionExtensions.cs`
  - Provides `AddTechStrapIntake(this IServiceCollection, IConfiguration)`.
  - It binds `PortalLinkOptions` from the flat key `TECHSTRAP_PORTAL_PUBLIC_URL` and validates it on start: the value must be an absolute `http` or `https` URL.
  - It calls `AddTechStrapContent()` and `AddTechStrapAttachments(configuration)`.
  - It registers `IAccessTokenService` through `AddTechStrapSecurity()`.
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/SubmitTicketIntegrationTests.cs`
- Modify: `tests/TechStrap.Infrastructure.IntegrationTests/PersistenceTestHost.cs`, only if it needs a hook to add services and configuration. Add an optional `Action<IServiceCollection>? configure` constructor parameter.

**Interfaces:**
- Consumes `SubmitTicketRequestHandler` (Task 9) and every service from Tasks 4 to 8.
- Produces `AddTechStrapIntake(IServiceCollection, IConfiguration)`, which the Api uses in Task 11.

- [ ] **Step 1: Write the failing tests**

`SubmitTicketIntegrationTests` is a `PostgresIntegrationTestBase`.

Build a host with `PersistenceTestHost(Database, configure: services => ...)` that:
- registers `AddTechStrapIntake` with an in-memory configuration holding `TECHSTRAP_PORTAL_PUBLIC_URL=https://help.test` and `Storage:Local:RootPath=<temp dir>`;
- adds `services.AddLogging()`;
- registers the handler with `services.AddScoped<ISubmitTicketRequestHandler, SubmitTicketRequestHandler>()`.

Seed one active product (`orbitly`, prefix `ORB`) and one trusted API key through the repositories inside `host.CommitAsync`. Resolve the handler from a fresh scope for each submission.

Tests:
- `One_submission_writes_requester_ticket_message_event_token_and_outbox_together`
  - Submit through the web channel with one PNG attachment.
  - Assert, with SQL scalars:
    - 1 requester, 1 ticket with `number = 'ORB-1'`, 1 message, 1 attachment;
    - 1 `ticket_events` row of type `Created` (check how the type is stored), and 1 `MessageAdded`;
    - 1 `ticket_access_tokens` row whose `token_hash` starts with `sha256:`;
    - 1 `email_outbox` row with kind `ticket-confirmation` whose payload contains `https://help.test/t/`;
    - the attachment file exists under the temp root.
- `A_failure_after_creation_leaves_no_rows_and_no_orphan_file`
  - Replace `IEmailOutbox` in the container with a test implementation whose `Enqueue` throws `InvalidOperationException`. Register it after `AddTechStrapPersistence`, so the last registration wins.
  - Submit with an attachment. The handler throws.
  - Assert 0 tickets, 0 requesters, 0 outbox rows, and no files under the temp root.
- `Twenty_concurrent_submissions_get_distinct_sequential_numbers`
  - Run 20 parallel submissions, each in its own scope, from different emails.
  - Assert the set of numbers equals `ORB-1` to `ORB-20`.
- `Two_first_submissions_from_one_new_email_share_one_requester`
  - Run 2 parallel submissions from `New@Example.com` and `new@example.com`, with no attachments.
  - Assert both succeed, 1 requester, 2 tickets.
- `Two_concurrent_requests_with_one_idempotency_key_create_one_ticket`
  - Run 2 parallel API-channel submissions with the same key and the same API key id.
  - Assert both succeed with the same ticket number, both carry a working `ViewUrl`, and there is 1 ticket.
  - Assert `intake_idempotency_keys.response` does not contain `/t/`.
- `The_same_idempotency_key_on_another_api_key_creates_a_second_ticket`
- `An_expired_idempotency_key_creates_a_new_ticket`
  - The host's `FakeTimeProvider` advances 25 hours between the two calls.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter SubmitTicketIntegrationTests`
Expected: a build failure, because `AddTechStrapIntake` is missing. Then add the extension and run again. The tests may now fail for real reasons; fix the handler or the services, not the tests.

- [ ] **Step 3: Implement `AddTechStrapIntake` and fix what the tests expose**

```csharp
public static IServiceCollection AddTechStrapIntake(this IServiceCollection services, IConfiguration configuration)
{
    services.AddOptions<PortalLinkOptions>()
        .Configure(options => options.PublicUrl = configuration[PortalLinkOptions.PublicUrlKey]?.Trim() ?? string.Empty)
        .Validate(
            options => Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
            $"{PortalLinkOptions.PublicUrlKey} must be an absolute http or https URL.")
        .ValidateOnStart();
    services.AddTechStrapSecurity();
    services.AddTechStrapContent();
    services.AddTechStrapAttachments(configuration);
    return services;
}
```

The concurrency tests may expose real issues: a requester race that needs the duplicate retry, or an idempotency race. Fix them in the handler, inside the Flow from Task 9. Write down every fix in your report.

- [ ] **Step 4: Run all affected tests, build and commit**

Run:
- `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release`
- `dotnet test --project tests/TechStrap.Application.Tests -c Release`
- `dotnet build TechStrap.slnx -c Release`

Expected: PASS.

Run the concurrency tests 5 times with `--filter Concurrent` to check they are stable.

```bash
git add src tests
git commit -m "test(intake): transactional intake, rollback and concurrency against Postgres" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 11: API-key authentication and the JSON intake endpoint

**Files:**
- Create: `src/TechStrap.Api/Security/ApiKeyClaimTypes.cs`
- Create: `src/TechStrap.Api/Security/ProductApiKeyValidator.cs`
- Create: `src/TechStrap.Api/Security/ApiKeyPrincipal.cs`
- Create: `src/TechStrap.Api/Security/ApiKeySetup.cs`
- Modify: `src/TechStrap.Api/Security/AuthorizationPolicies.cs` (add `ApiKey` and `Public`)
- Create: `src/TechStrap.Api/Controllers/IntakeController.cs`
- Modify: `src/TechStrap.Api/Startup/ApplicationHandlerRegistration.cs` (exclude the Worker-only drain handler; see Step 3)
- Modify: `src/TechStrap.Api/Program.cs`
- Modify: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs` (one `ExpectedSuccess` row)
- Modify: `tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs` (scan only Agent and Admin routes)
- Create: `tests/TechStrap.Api.Tests/Auth/RoutePolicyCoverageTests.cs`
- Create: `tests/TechStrap.Api.Tests/Intake/IntakeTestData.cs`
- Create: `tests/TechStrap.Api.Tests/Intake/ApiKeyAuthTests.cs`
- Create: `tests/TechStrap.Api.Tests/Intake/IntakeEndpointTests.cs`

**Interfaces:**
- Consumes:
  - `ISubmitTicketRequestHandler`, `SubmitTicketContext` and `IntakeChannel` (Task 9);
  - `AddTechStrapIntake` (Task 10);
  - `HeaderNames` (Task 2);
  - `IApiKeyHasher.Hash`, `IProductRepository.GetApiKeyByHashAsync` and `GetByIdAsync` (PHASE-03 and PHASE-04).
- Produces:

```csharp
namespace TechStrap.Api.Security;

public static class ApiKeyClaimTypes
{
    public const string KeyId = "techstrap:api_key_id";
    public const string ProductId = "techstrap:product_id";
    public const string Kind = "techstrap:api_key_kind"; // ApiKeyKinds.Trusted or ApiKeyKinds.Public
}

public static partial class AuthorizationPolicies
{
    public const string ApiKey = "ApiKey";  // ApiKey scheme only; requires the product claim
    public const string Public = "Public";  // admits everyone; marks a route as deliberately anonymous (D-034)
}

/// <summary>Reads the key principal built by <see cref="ProductApiKeyValidator"/>.</summary>
internal static class ApiKeyPrincipal
{
    public static (Guid? KeyId, Guid? ProductId, bool Trusted) Read(ClaimsPrincipal user);
}

public static class ApiKeySetup
{
    public const string SchemeName = "ApiKey";
    public static IServiceCollection AddProductApiKeyAuthentication(this IServiceCollection services, IConfiguration configuration);
}
```

`AuthorizationPolicies` is an existing `public static class`. Add the two constants to it; do not make it partial. The block above only shows the new members.

- [ ] **Step 1: Write the test data helper and the failing tests**

`tests/TechStrap.Api.Tests/Intake/IntakeTestData.cs` seeds through the real repositories and `IApiKeyHasher`, inside one unit of work, in a scope from `factory.Services`:
- `orbitly` (prefix `ORB`, active), `paperplane` (prefix `PPL`, active), `dormant` (prefix `DRM`, deactivated with `product.Deactivate(clock)`; check the method name in `Product.cs`);
- for `orbitly`: one trusted key, one public key, and one revoked trusted key (`key.Revoke(clock)`);
- for `paperplane`: one trusted key;
- for `dormant`: one trusted key.

It returns a record holding the products and every plaintext key, generated with `hasher.Generate(kind)`. Plaintext keys exist only in test memory.

```csharp
internal sealed record IntakeSeed(
    Product Orbitly, Product Paperplane, Product Dormant,
    string OrbitlyTrusted, string OrbitlyPublic, string OrbitlyRevoked, string PaperplaneTrusted, string DormantTrusted);

internal static class IntakeTestData
{
    public static async Task<IntakeSeed> SeedAsync(IServiceProvider services, CancellationToken cancellationToken);
}
```

The endpoint tests use `ApiFactory` with a migrated database (`ApiTestDatabase.CreateAsync(postgres)`, as in `Products/ApiKeyEndpointTests`) and these settings:
- `TECHSTRAP_PORTAL_PUBLIC_URL=https://help.test`;
- `Storage:Local:RootPath` set to a fresh temp directory, deleted in `DisposeAsync`.

`ApiKeyAuthTests`:

```csharp
    [Fact] public async Task A_request_without_a_key_is_401_with_no_body()
    [Fact] public async Task An_unknown_key_is_401_with_no_body()
    [Fact] public async Task A_revoked_key_is_401()
    [Fact] public async Task A_key_whose_product_is_deactivated_is_401()
    [Fact] public async Task An_agent_bearer_token_is_not_accepted_on_intake()          // TestJwt admin token, no key: 401
    [Fact] public async Task An_api_key_is_not_accepted_on_agent_routes()               // GET /api/products with a valid key: 401
    [Fact] public async Task A_key_creates_tickets_only_for_its_own_product()           // paperplane key: number starts "PPL-", product id = paperplane
    [Fact] public async Task All_401_responses_are_identical()                           // missing, unknown, revoked: same status, same empty body, same headers apart from Date
```

`IntakeEndpointTests` (every request is `POST /api/intake/tickets` with a JSON body):

```csharp
    [Fact] public async Task A_trusted_key_submission_returns_201_with_the_number_and_view_url_and_no_store()
    // body { email, name, subject, body, externalUserRef = "u-1", metadata = { plan = "pro" } };
    // asserts 201, TicketNumber "ORB-1", ViewUrl starting "https://help.test/t/", Warnings empty,
    // Cache-Control "no-store"; the DB has metadata_trusted = true and requester external_user_ref = "u-1".

    [Fact] public async Task A_public_key_submission_drops_the_external_ref_with_a_warning()
    // asserts Warnings == ["external-user-ref-ignored"], metadata_trusted = false, external_user_ref is null.

    [Fact] public async Task An_invalid_email_is_a_400_with_the_email_target()
    // asserts ValidationProblemDetails errors has key "email" and errorCodes contains "email-invalid".

    [Fact] public async Task A_repeated_idempotency_key_returns_the_first_response_and_one_ticket()
    // two sequential posts with Idempotency-Key "abc": 201 both, the same TicketNumber and Warnings, both ViewUrls start
    // "https://help.test/t/" (the replay gets a fresh link), 1 ticket in the DB.

    [Fact] public async Task An_oversized_json_body_is_413_problem_details()
    // a body of IntakeRequestLimits.JsonBodyBytes + 1 bytes; asserts 413, application/problem+json, type "request-too-large".
```

`RoutePolicyCoverageTests`:

```csharp
    [Fact] public void Every_api_route_declares_exactly_one_known_policy()
    // every RouteEndpoint under "api/" has IAuthorizeData metadata whose Policy is one of Agent, Admin, ApiKey, Public;
    // none uses [AllowAnonymous] (IAllowAnonymous metadata is absent).

    [Fact] public void Every_public_and_api_key_route_is_rate_limited()
    // every endpoint whose policy is Public or ApiKey carries EnableRateLimitingAttribute metadata.
```

`Every_public_and_api_key_route_is_rate_limited` fails until Task 13. Mark it `[Fact(Skip = "Rate limits arrive in Task 13")]` in this task, and Task 13 removes the `Skip`.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "FullyQualifiedName~Intake|FullyQualifiedName~RoutePolicyCoverage"`
Expected: a build failure.

- [ ] **Step 3: Implement**

`ProductApiKeyValidator` (Api, `internal sealed`, implements `SyntaxCircus.AspNetCore.Authentication.IApiKeyValidator`). Its constructor takes `IApiKeyHasher` and `IProductRepository`.

```csharp
public async Task<ApiKeyValidationResult> ValidateAsync(string apiKey, CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return ApiKeyValidationResult.Invalid;
    }

    var key = await _products.GetApiKeyByHashAsync(_hasher.Hash(apiKey.Trim()), cancellationToken);
    if (key is null || key.IsRevoked)
    {
        return ApiKeyValidationResult.Invalid;
    }

    var product = await _products.GetByIdAsync(key.ProductId, cancellationToken);
    if (product is null || !product.IsActive)
    {
        return ApiKeyValidationResult.Invalid;
    }

    return ApiKeyValidationResult.Valid(
    [
        new Claim(ClaimTypes.NameIdentifier, key.Id.ToString("D")),
        new Claim(ApiKeyClaimTypes.KeyId, key.Id.ToString("D")),
        new Claim(ApiKeyClaimTypes.ProductId, key.ProductId.ToString("D")),
        new Claim(ApiKeyClaimTypes.Kind, key.Kind == ApiKeyKind.Trusted ? ApiKeyKinds.Trusted : ApiKeyKinds.Public),
    ]);
}
```

`ApiKeySetup.AddProductApiKeyAuthentication`:

```csharp
public static IServiceCollection AddProductApiKeyAuthentication(this IServiceCollection services, IConfiguration configuration)
{
    // Keyed by scheme: the package's handler resolves the keyed validator from the request scope, so a scoped,
    // repository-backed validator works. The package's unkeyed constant validator stays registered but unused.
    services.AddKeyedScoped<IApiKeyValidator, ProductApiKeyValidator>(SchemeName);
    services.AddSyntaxCircusApiKey(configuration, schemeName: SchemeName);
    services.PostConfigure<ApiKeyAuthenticationOptions>(SchemeName, options => options.HeaderName = HeaderNames.ApiKey);

    services.AddAuthorizationBuilder()
        .AddPolicy(AuthorizationPolicies.ApiKey, policy => policy
            .AddAuthenticationSchemes(SchemeName)
            .RequireAuthenticatedUser()
            .RequireClaim(ApiKeyClaimTypes.ProductId))
        .AddPolicy(AuthorizationPolicies.Public, policy => policy.RequireAssertion(_ => true));
    return services;
}
```

`ApiKeyPrincipal.Read` parses the three claims with `Guid.TryParse`. `Trusted` is true only when the kind claim equals `ApiKeyKinds.Trusted`.

`IntakeController`:

```csharp
[ApiController]
[Route("api/intake/tickets")]
[Authorize(Policy = AuthorizationPolicies.ApiKey)]
public sealed class IntakeController : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(IntakeRequestLimits.JsonBodyBytes)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitTicketRequest request,
        [FromHeader(Name = HeaderNames.IdempotencyKey)] string? idempotencyKey,
        [FromServices] ISubmitTicketRequestHandler handler,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store"; // the response carries the customer's ticket link
        var (keyId, productId, trusted) = ApiKeyPrincipal.Read(User);
        var context = new SubmitTicketContext(IntakeChannel.Api, null, productId, keyId, trusted, false, [], idempotencyKey);
        return (await handler.HandleAsync(request, context, cancellationToken))
            .ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));
    }
}
```

`IntakeRequestLimits` goes in `src/TechStrap.Api/Startup/IntakeHosting.cs`, which Task 12 extends:

```csharp
public static class IntakeRequestLimits
{
    public const long JsonBodyBytes = 256 * 1024; // text fields and metadata only (D-034: no API attachments in v1)
}
```

**413 mapping.** A body over the limit throws `BadHttpRequestException` (status 413) while MVC reads it, and the generic exception handler would turn that into a 500. In `IntakeHosting.cs`, add a middleware that maps it, plus `UseRequestTooLargeProblemDetails(this IApplicationBuilder)`. In `Program.cs`, put the call directly after `app.UseProblemDetailsExceptionHandling()`, so it runs inside the generic handler:

```csharp
internal sealed class RequestTooLargeMiddleware(RequestDelegate next)
{
    public const string ErrorCode = "request-too-large";

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (IsTooLarge(ex) && !context.Response.HasStarted)
        {
            context.Response.Clear();
            await Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                type: ErrorCode,
                title: "Request too large",
                detail: "The request body is larger than this endpoint accepts.").ExecuteAsync(context);
        }
    }

    // MVC wraps form-read failures (BadHttpRequestException is an IOException) in ValueProviderException; walk the chain.
    private static bool IsTooLarge(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
            {
                return true;
            }
        }

        return false;
    }
}
```

If the 413 test shows that the test server does not enforce `RequestSizeLimit`, do not weaken the test. Check `IHttpMaxRequestBodySizeFeature` on the test server, write the finding in your report, and stop with status `BLOCKED`.

**Handler registration.** `ApplicationHandlerRegistration.Handlers()` scans every Application handler. Task 15 adds `DrainEmailOutboxHandler`, which needs email services the Api does not register. Development hosts validate the container on build, so the Api would fail to start. Add one exclusion now, so Task 15 can register the drain handler without touching the Api:

```csharp
// Worker-only handlers: registered by the Worker host, never by the Api (their dependencies live there).
private static readonly HashSet<string> _workerOnly = ["IDrainEmailOutboxHandler"];
```

Filter the scan by contract interface name, because the type does not exist yet. Add a test, `ApplicationHandlerRegistrationTests.Worker_only_handlers_are_not_registered_in_the_api`. It asserts that no contract named in `_workerOnly` appears in `Handlers()`, and that `Handlers()` has at least 18 entries. Expose the set as `internal static IReadOnlyCollection<string> WorkerOnly`; Api.Tests already has `InternalsVisibleTo`, so check it does.

**Program.cs** (Api), after `AddAgentAuthentication(...)`:

```csharp
builder.Services.AddProductApiKeyAuthentication(builder.Configuration);
builder.Services.AddTechStrapIntake(builder.Configuration);
```

Both registrations come after `AddAgentAuthentication`, and the default scheme stays Bearer, because `AddSyntaxCircusApiKey` does not set a default.

**Coverage tests.**
- In `AgentAccessCoverageTests.Routes`, keep only endpoints whose `IAuthorizeData` policies include `AuthorizationPolicies.Agent` or `AuthorizationPolicies.Admin`.
- Compare the count with `ControllerActions.All()` filtered the same way. Add `ControllerActions.AgentOrAdminActions()`, which reads the controller's and the action's `[Authorize]` attributes.
- In `ControllerActions.ExpectedSuccess`, add `["IntakeController.Submit"] = 201`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release` and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`.
Expected: PASS. The Rate-limit coverage fact is skipped.

`ResultMappingTests` and `CancellationPropagationTests` cover `IntakeController.Submit` automatically. `ControllerActions.InvokeAsync` passes `null` for the `string?` header and an uninitialised `SubmitTicketRequest`. `User` is an empty principal, so the context has null ids, and the proxy handler never looks at it.

- [ ] **Step 5: Build and commit**

Run: `dotnet build TechStrap.slnx -c Release`. Expected: 0 warnings. Check `git diff --cached --stat`.

```bash
git add src tests
git commit -m "feat(api): API-key scheme with product-scoped validator and JSON intake endpoint" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 12: Public web-form intake endpoint

**Files:**
- Create: `src/TechStrap.Api/Controllers/PublicSubmitTicketForm.cs`
- Create: `src/TechStrap.Api/Controllers/PublicIntakeController.cs`
- Modify: `src/TechStrap.Api/Startup/IntakeHosting.cs` (`FormBodyBytes`)
- Modify: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs` (one row)
- Create: `tests/TechStrap.Api.Tests/Intake/PublicIntakeEndpointTests.cs`

**Interfaces:**
- Consumes: `ISubmitTicketRequestHandler`, `SubmitTicketContext`, `IncomingAttachment` (Tasks 6 and 9); `AuthorizationPolicies.Public`, `IntakeRequestLimits`, `RequestTooLargeMiddleware` (Task 11); `IntakeLimits` (Task 2).
- Produces:

```csharp
namespace TechStrap.Api.Controllers;

/// <summary>Multipart form posted by the portal contact form. Website is the honeypot: real people never see or fill it.</summary>
public sealed class PublicSubmitTicketForm
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public string? Website { get; set; }
    public List<IFormFile>? Attachments { get; set; }
}
```

Add `FormBodyBytes` to `IntakeRequestLimits`:

```csharp
    // 25 MiB of files plus room for the text fields and multipart framing; the handler enforces the exact file limits.
    public const long FormBodyBytes = IntakeLimits.MaxMessageBytes + (1024 * 1024);
```

- [ ] **Step 1: Write the failing tests**

`PublicIntakeEndpointTests` uses `ApiFactory`, a migrated database, `IntakeTestData.SeedAsync`, and the temp storage root from Task 11. Every request is `POST /api/public/products/{key}/tickets` as `multipart/form-data`, built with `MultipartFormDataContent`, and carries no authentication.

```csharp
    [Fact] public async Task An_anonymous_form_submission_returns_201_without_a_view_url()
    // fields email/name/subject/body; asserts 201, TicketNumber "ORB-1", ViewUrl null, Cache-Control "no-store",
    // DB: one ticket with channel "Web" and metadata_trusted false, one outbox row of kind "ticket-confirmation".

    [Fact] public async Task A_png_attachment_is_stored_and_recorded()
    // a real 1x1 PNG byte array; asserts one attachments row with content_type "image/png" and the file under the storage root.

    [Fact] public async Task A_filled_honeypot_returns_a_plausible_201_and_writes_nothing()
    // Website = "https://spam.example"; asserts 201, TicketNumber matches ^ORB-\d+$, 0 tickets, 0 requesters, 0 outbox rows.

    [Fact] public async Task An_executable_renamed_to_pdf_is_400_attachment_type_not_allowed_and_nothing_is_stored()
    // bytes starting "MZ" named "invoice.pdf" with content type application/pdf; asserts 400, errorCodes contains
    // "attachment-type-not-allowed", 0 tickets, the storage root is empty.

    [Fact] public async Task Six_files_is_400_attachments_too_many()

    [Theory] [InlineData("nope")] [InlineData("dormant")]
    public async Task An_unknown_or_deactivated_product_is_404(string key)

    [Fact] public async Task A_form_body_over_the_limit_is_413()
    // one file of IntakeRequestLimits.FormBodyBytes + 1 bytes; asserts 413 and type "request-too-large".

    [Fact] public async Task A_form_post_ignores_any_api_key_or_bearer_token()
    // sending a valid X-Api-Key and an agent bearer still gives Channel Web and metadata_trusted false.
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter PublicIntakeEndpointTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

```csharp
[ApiController]
[Route("api/public/products/{productKey}/tickets")]
[Authorize(Policy = AuthorizationPolicies.Public)]
public sealed class PublicIntakeController : ControllerBase
{
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(IntakeRequestLimits.FormBodyBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = IntakeRequestLimits.FormBodyBytes * 2)] // Kestrel's limit must trip first, as a 413
    public async Task<IActionResult> Submit(
        string productKey,
        [FromForm] PublicSubmitTicketForm form,
        [FromServices] ISubmitTicketRequestHandler handler,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var files = form.Attachments ?? [];
        var streams = new List<Stream>(files.Count);
        try
        {
            var attachments = new List<IncomingAttachment>(files.Count);
            foreach (var file in files)
            {
                var stream = file.OpenReadStream();
                streams.Add(stream);
                attachments.Add(new IncomingAttachment(file.FileName, file.ContentType, file.Length, stream));
            }

            var request = new SubmitTicketRequest(form.Email, form.Name, form.Subject, form.Body, null, null);
            var context = new SubmitTicketContext(
                IntakeChannel.Web, productKey, null, null, false, !string.IsNullOrEmpty(form.Website), attachments, null);
            return (await handler.HandleAsync(request, context, cancellationToken))
                .ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }
}
```

The handler's shape checks (Task 9) count files and total bytes before it opens a unit of work, so six files never reach storage.

Add `["PublicIntakeController.Submit"] = 201` to `ControllerActions.ExpectedSuccess`. `ControllerActions.InvokeAsync` samples `PublicSubmitTicketForm` with `GetUninitializedObject`. All its properties are then null, which the action already tolerates.

Check that the controller passes `ControllerBoundaryRules`: one `[FromServices]` handler, a `CancellationToken`, and no constructor.

- [ ] **Step 4: Run all Api and architecture tests**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release` and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`.
Expected: PASS. `OpenApiSurfaceTests` lists the new route automatically.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(api): anonymous portal form intake with honeypot and attachment limits" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 13: Intake rate limits

**Files:**
- Create: `src/TechStrap.Api/Options/IntakeRateLimitOptions.cs`
- Create: `src/TechStrap.Api/Startup/IntakeRateLimiting.cs`
- Modify: `src/TechStrap.Api/Program.cs`
- Modify: `src/TechStrap.Api/Controllers/IntakeController.cs` and `PublicIntakeController.cs` (`[EnableRateLimiting]`)
- Modify: `src/TechStrap.Api/.env.example`
- Modify: `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`
- Modify: `tests/TechStrap.Api.Tests/Auth/RoutePolicyCoverageTests.cs` (remove the `Skip`)
- Create: `tests/TechStrap.Api.Tests/Intake/IntakeRateLimitTests.cs`
- Create: `tests/TechStrap.Api.Tests/Intake/IntakeRateLimitOptionsTests.cs`

**Interfaces:**

```csharp
namespace TechStrap.Api.Options;

/// <summary>Intake limits (02-ARCHITECTURE 11.2, D-001). Validated at boot.</summary>
public sealed class IntakeRateLimitOptions
{
    public const string SectionName = "RateLimiting:Intake";
    public const string WebFormPolicyName = "public-submit";
    public const string KeyPolicyName = "intake-key";

    public int WebFormPermitLimit { get; set; } = 5;          // per client IP
    public int WebFormWindowSeconds { get; set; } = 600;
    public int PublicKeyPermitLimit { get; set; } = 10;       // per key prefix + client IP
    public int PublicKeyWindowSeconds { get; set; } = 60;
    public int TrustedKeyPermitLimit { get; set; } = 120;     // per key prefix
    public int TrustedKeyWindowSeconds { get; set; } = 60;
}
```

```csharp
namespace TechStrap.Api.Startup;

public static class IntakeRateLimiting
{
    /// <summary>Adds the two intake policies. Call inside AddRateLimiter, next to the existing "public" policy.</summary>
    public static RateLimiterOptions AddIntakePolicies(this RateLimiterOptions options, IntakeRateLimitOptions limits);

    /// <summary>The partition for a request on the key policy. Internal for tests.</summary>
    internal static (string Key, bool Trusted) KeyPartition(HttpContext context);
}
```

**Partition rules.** The rate limiter runs before authentication, and the API-key scheme is never the default, so the partition must be read from the raw header:
- The partition reads the first value of `HeaderNames.ApiKey`, trimmed, and takes its first `ApiKeyFormat.StoredPrefixLength` (12) characters.
- If the value starts with `ApiKeyFormat.TrustedPrefix`, the partition is `trusted:{prefix12}`, limited by `TrustedKey*`.
- Otherwise the partition is `key:{prefix12 or "none"}:{client IP}`, limited by `PublicKey*`. This covers public keys and junk.
- The client IP is `context.Connection.RemoteIpAddress?.ToString() ?? "unknown"`, which is already resolved by forwarded headers (D-019).

A caller that invents `tsk_` prefixes gets its own partitions, but each request still costs one indexed hash lookup and ends in a 401. Record this in `docs/development/INTAKE.md` in Task 17. Do not add a second tier.

The web-form policy uses `AddPerIpFixedWindow(WebFormPolicyName, WebFormPermitLimit, TimeSpan.FromSeconds(WebFormWindowSeconds))`.

The key policy is built with the framework API:

```csharp
options.AddPolicy(IntakeRateLimitOptions.KeyPolicyName, context =>
{
    var (key, trusted) = KeyPartition(context);
    var (limit, window) = trusted
        ? (limits.TrustedKeyPermitLimit, limits.TrustedKeyWindowSeconds)
        : (limits.PublicKeyPermitLimit, limits.PublicKeyWindowSeconds);
    return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = limit,
        Window = TimeSpan.FromSeconds(window),
        QueueLimit = 0,
    });
});
```

- [ ] **Step 1: Write the failing tests**

`IntakeRateLimitTests` follows the `PublicApiHardeningTests` pattern: trusted peer `192.0.2.5`, an `X-Forwarded-For` client IP, and limits lowered through settings.
- Set `RateLimiting:Intake:WebFormPermitLimit=2` and `PublicKeyPermitLimit=2`.
- Set `TrustedKeyPermitLimit=3`.

```csharp
    [Fact] public async Task The_third_form_post_from_one_ip_is_429_problem_details_with_retry_after()
    [Fact] public async Task Form_posts_from_another_ip_have_their_own_allowance()
    [Fact] public async Task A_public_key_is_limited_per_key_and_ip()
    // the third post with key K from IP A is 429; a post with key K from IP B is not 429.
    [Fact] public async Task A_trusted_key_is_limited_per_key_regardless_of_ip()
    // the fourth post across IPs A and B is 429.
    [Fact] public async Task Junk_keys_are_limited_per_ip()
    // three posts with unknown "tsp_..." keys from one IP: two are 401 and the third is 429.
```

The tests may post invalid bodies: the limiter counts requests before validation, so a 400 still uses a permit. Use a minimal valid body anyway, so a failure shows clearly. Assert only the status codes, plus the 429 problem `type` of `rate-limited`.

`IntakeRateLimitOptionsTests`, modelled on `PublicRateLimitOptionsTests`, has one theory row per property, each with the value `0`:

```csharp
    [Theory]
    [InlineData("WebFormPermitLimit")] [InlineData("WebFormWindowSeconds")]
    [InlineData("PublicKeyPermitLimit")] [InlineData("PublicKeyWindowSeconds")]
    [InlineData("TrustedKeyPermitLimit")] [InlineData("TrustedKeyWindowSeconds")]
    public void A_non_positive_limit_fails_the_boot(string property)
    // starting ApiFactory with RateLimiting:Intake:{property}=0 throws OptionsValidationException mentioning the key.
```

Also add one row per `IntakeRateLimitOptions` property to `EnvExampleCompletenessTests.Hosts()`, the same way `PublicRateLimitOptions` is listed, and remove the `Skip` from `Every_public_and_api_key_route_is_rate_limited`.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "FullyQualifiedName~RateLimit|FullyQualifiedName~EnvExample|FullyQualifiedName~RoutePolicyCoverage"`
Expected: FAIL. The build breaks first; after that, a route is not rate limited and keys are missing from `.env.example`.

- [ ] **Step 3: Implement**

In `Program.cs`:
- Bind `IntakeRateLimitOptions` with `.Validate(...)` for every property (each `>= 1`, with the message `"RateLimiting:Intake:{Property} must be >= 1."`) and `.ValidateOnStart()`.
- In the existing `AddRateLimiter` delegate, read `builder.Configuration.GetSection(IntakeRateLimitOptions.SectionName).Get<IntakeRateLimitOptions>() ?? new()` and call `options.AddIntakePolicies(intake)`. This is the same lazy pattern as the public policy.

On the controllers:
- `[EnableRateLimiting(IntakeRateLimitOptions.KeyPolicyName)]` on `IntakeController`.
- `[EnableRateLimiting(IntakeRateLimitOptions.WebFormPolicyName)]` on `PublicIntakeController`.

Add these lines to `src/TechStrap.Api/.env.example` under the existing rate-limit keys, with one comment line:

```
# Intake limits (02-ARCHITECTURE 11.2): web form per IP; public keys per key and IP; trusted keys per key.
RATELIMITING__INTAKE__WEBFORMPERMITLIMIT=5
RATELIMITING__INTAKE__WEBFORMWINDOWSECONDS=600
RATELIMITING__INTAKE__PUBLICKEYPERMITLIMIT=10
RATELIMITING__INTAKE__PUBLICKEYWINDOWSECONDS=60
RATELIMITING__INTAKE__TRUSTEDKEYPERMITLIMIT=120
RATELIMITING__INTAKE__TRUSTEDKEYWINDOWSECONDS=60
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`.
Expected: PASS.

The intake endpoint tests from Tasks 11 and 12 must still pass, because each makes only a few requests per IP. If one of them trips the limit, raise the limits in that test class's settings. Do not remove the limiter.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(api): per-IP, per-key and per-key-and-IP intake rate limits" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 14: Public product endpoint

**Files:**
- Create: `src/TechStrap.Application/Products/GetPublicProductRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/PublicProductsController.cs`
- Modify: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs` (one row)
- Create: `tests/TechStrap.Application.Tests/Products/GetPublicProductRequestHandlerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Intake/PublicProductEndpointTests.cs`

**Interfaces:**
- Consumes: `PublicProductDto(Key, DisplayName, LogoPath, AccentColour, OnAccentColour, AccentInkColour)` (Task 2); `ProductAccent.TryDerive`; `IProductRepository.GetByKeyAsync`.
- Produces:

```csharp
namespace TechStrap.Application.Products;

public interface IGetPublicProductRequestHandler
{
    Task<Result<PublicProductDto>> HandleAsync(string? productKey, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write the failing tests**

`GetPublicProductRequestHandlerTests` (substitute repository):

```csharp
    [Fact] public async Task An_active_product_returns_its_public_branding_with_derived_accent_colours()
    // Product "orbitly" with accent "#7C3AED"; asserts the DTO fields and that OnAccent/AccentInk equal ProductAccent.TryDerive's.

    [Theory] [InlineData(null)] [InlineData("")] [InlineData("missing")]
    public async Task A_blank_or_unknown_key_is_not_found(string? key)

    [Fact] public async Task A_deactivated_product_is_not_found()

    [Fact] public async Task The_dto_never_exposes_from_or_reply_to_addresses()
    // reflection: PublicProductDto has no property whose name contains "From" or "Reply".
```

`PublicProductEndpointTests` (ApiFactory and a migrated database, seeded with `IntakeTestData`):

```csharp
    [Fact] public async Task An_anonymous_get_returns_200_with_public_caching()
    // GET /api/public/products/orbitly: 200, Cache-Control "public, max-age=300", body Key "orbitly".
    [Theory] [InlineData("nope")] [InlineData("dormant")]
    public async Task An_unknown_or_deactivated_product_is_404_no_store(string key)
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter GetPublicProductRequestHandlerTests`
Expected: a build failure.

- [ ] **Step 3: Implement**

```csharp
public sealed class GetPublicProductRequestHandler(IProductRepository products) : IGetPublicProductRequestHandler
{
    public async Task<Result<PublicProductDto>> HandleAsync(string? productKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productKey))
        {
            return Result<PublicProductDto>.Failure(ProductErrors.NotFound());
        }

        var product = await products.GetByKeyAsync(productKey.Trim(), cancellationToken);
        if (product is null || !product.IsActive)
        {
            return Result<PublicProductDto>.Failure(ProductErrors.NotFound());
        }

        var branding = product.Branding;
        ProductAccent.TryDerive(branding.AccentColour, out var colors);
        return Result<PublicProductDto>.Success(new PublicProductDto(
            product.Key, branding.DisplayName, branding.LogoPath, colors.Accent, colors.OnAccent, colors.AccentInk));
    }
}
```

`ProductErrors.NotFound()` already exists in Application from PHASE-04; check the exact name with a grep. If `ProductErrors` is `internal`, it is still visible here, because both are in the same assembly.

Controller:

```csharp
[ApiController]
[Route("api/public/products")]
[Authorize(Policy = AuthorizationPolicies.Public)]
[EnableRateLimiting(PublicRateLimitOptions.PolicyName)]
public sealed class PublicProductsController : ControllerBase
{
    [HttpGet("{productKey}")]
    public async Task<IActionResult> Get(string productKey, [FromServices] IGetPublicProductRequestHandler handler, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return (await handler.HandleAsync(productKey, cancellationToken)).ToActionResult(this, product =>
        {
            Response.Headers.CacheControl = "public, max-age=300";
            return Ok(product);
        });
    }
}
```

Add `["PublicProductsController.Get"] = 200` to `ExpectedSuccess`.

- [ ] **Step 4: Run all affected tests**

Run:
- `dotnet test --project tests/TechStrap.Application.Tests -c Release`
- `dotnet test --project tests/TechStrap.Api.Tests -c Release`
- `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`

Expected: PASS.

- [ ] **Step 5: Build and commit**

```bash
git add src tests
git commit -m "feat(api): public product branding endpoint with cache headers" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 15: Outbound email sender and `DrainEmailOutboxHandler`

**Files:**
- Create: `src/TechStrap.Application/Email/OutboundEmail.cs` (`OutboundEmail`, `IOutboundEmailSender`, `EmailSendFailures`, `OutboundMessageIds`)
- Create: `src/TechStrap.Application/Email/EmailOutboxWorkerOptions.cs`
- Create: `src/TechStrap.Application/Email/DrainEmailOutboxHandler.cs` (`IDrainEmailOutboxHandler`, `DrainResult`, the handler)
- Create: `src/TechStrap.Infrastructure/Email/SmtpOutboundEmailSender.cs`
- Create: `src/TechStrap.Infrastructure/Email/EmailServiceCollectionExtensions.cs`
- Modify: `src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj` (add `<PackageReference Include="SyntaxCircus.Email" />` if Task 7 did not already add it)
- Create: `tests/TechStrap.Application.Tests/Email/DrainEmailOutboxHandlerTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/SmtpOutboundEmailSenderTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/EmailServiceRegistrationTests.cs`

**Interfaces:**
- Consumes:
  - `IEmailTemplateRenderer`, `TicketConfirmationEmail`, `EmailBranding`, `RenderedEmail`, `EmailBrandingOptions`, `EmailTemplates` (Task 7);
  - `IEmailOutboxStore.ClaimBatchAsync`, `MarkSentAsync` and `MarkFailedAsync`; `IProductRepository.GetByIdAsync` (PHASE-03);
  - `SyntaxCircus.Email`: `IEmailSender`, `EmailMessage`, `SmtpOptions`, `SmtpDeliveryException` and `SmtpFailureKind`, `AddSmtpEmailSender`.
- Produces:

```csharp
namespace TechStrap.Application.Email;

public sealed record OutboundEmail(string To, string Subject, string Text, string Html, string? From, string? ReplyTo, string MessageId);

/// <summary>Sends one rendered email (D-033). Failures are sanitised categories from <see cref="EmailSendFailures"/>, never server text.</summary>
public interface IOutboundEmailSender
{
    Task<Result> SendAsync(OutboundEmail email, CancellationToken cancellationToken);
}

public static class EmailSendFailures
{
    public const string Transient = "smtp-transient";
    public const string Permanent = "smtp-permanent";
    public const string Authentication = "smtp-authentication";
    public const string Timeout = "smtp-timeout";
    public const string Unknown = "smtp-unknown";
}

/// <summary>The outbox id goes in Message-ID so duplicate sends are recognisable (D-010).</summary>
public static class OutboundMessageIds
{
    public const string Domain = "techstrap.local";
    public static string For(Guid outboxId) => $"{outboxId:D}@{Domain}";
}

public sealed class EmailOutboxWorkerOptions
{
    public const string SectionName = "EmailOutbox";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
    public int LeaseSeconds { get; set; } = 120;   // covers sending one whole batch; a crashed worker's rows are reclaimed after it
    public string? WorkerId { get; set; }
}

public sealed record DrainResult(int Claimed, int Sent, int Failed);

public interface IDrainEmailOutboxHandler
{
    Task<Result<DrainResult>> HandleAsync(string workerId, CancellationToken cancellationToken);
}
```

Every claimed row gets the same lease, and the handler sends rows one at a time, so the lease must cover a whole batch. Task 15 validates only `LeaseSeconds >= 30`; the `.env.example` comment in Task 17 tells operators to keep it above one batch's sending time.

```csharp
namespace TechStrap.Infrastructure.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Everything the Worker needs to drain the outbox: the SMTP sender and its adapter, the renderer, the branding and worker options,
    /// and the drain handler. SMTP settings are required only when the worker is enabled.
    /// </summary>
    public static IServiceCollection AddTechStrapEmail(this IServiceCollection services, IConfiguration configuration);
}
```

- [ ] **Step 1: Write the failing handler tests**

`DrainEmailOutboxHandlerTests` uses substitutes for every dependency, a `FakeTimeProvider` and `Options.Create(new EmailOutboxWorkerOptions())`.
- Build outbox items with `EmailOutboxItem.Enqueue(EmailTemplates.TicketConfirmation, "ann@example.com", payload, product.Id, ticketId, clock).Value`. The payload is the camelCase JSON of a `TicketConfirmationEmail`.
- The store's `ClaimBatchAsync` returns the list under test.
- `MarkSentAsync` and `MarkFailedAsync` return `Result.Success()` (check the exact factory name in `SyntaxCircus.Common`).
- The renderer returns `new RenderedEmail("subj", "text", "<p>html</p>", "Orbitly <help@orbitly.test>", "reply@orbitly.test")`.

```csharp
    [Fact] public async Task It_claims_with_the_configured_batch_size_and_lease()
    // asserts ClaimBatchAsync("w1", 20, TimeSpan.FromSeconds(120), token).

    [Fact] public async Task A_confirmation_is_rendered_with_the_products_branding_and_sent_with_the_outbox_id_as_message_id()
    // asserts renderer received the payload model and EmailBranding(DisplayName, LogoPath, AccentColour, FromAddress, ReplyTo) of the product;
    // the sender received OutboundEmail(To "ann@example.com", "subj", "text", "<p>html</p>", From, ReplyTo, OutboundMessageIds.For(item.Id));
    // MarkSentAsync(item.Id, "w1", token); result DrainResult(1, 1, 0).

    [Fact] public async Task A_send_failure_marks_the_row_failed_with_the_sanitised_category()
    // sender returns Failure(EmailSendFailures.Transient); asserts MarkFailedAsync(item.Id, "w1", "smtp-transient", token), DrainResult(1, 0, 1).

    [Fact] public async Task An_unknown_kind_is_failed_without_sending()          // MarkFailedAsync(..., "unknown-kind", ...)
    [Fact] public async Task An_unreadable_payload_is_failed_without_sending()    // payload "{}" lacks fields → "payload-invalid"
    [Fact] public async Task A_missing_or_deleted_product_is_failed_without_sending() // GetByIdAsync returns null → "product-missing"
    [Fact] public async Task A_renderer_exception_fails_that_row_and_the_batch_continues()
    // two items; the renderer throws for the first; asserts "render-failed" for the first and a send for the second.

    [Fact] public async Task Products_are_loaded_once_per_batch()
    // three items for one product; asserts GetByIdAsync received exactly one call.

    [Fact] public async Task A_lost_lease_on_mark_sent_is_logged_and_still_counted_as_sent()
    // MarkSentAsync returns Conflict("outbox-not-claim-owner"); asserts no exception and DrainResult(1, 1, 0).

    [Fact] public async Task An_empty_batch_sends_nothing()                       // DrainResult(0, 0, 0)

    [Fact] public async Task Cancellation_reaches_the_store_and_the_sender()
```

The failure codes `unknown-kind`, `payload-invalid`, `product-missing` and `render-failed` are `const string`s in an `internal static class DrainFailures` next to the handler.

- [ ] **Step 2: Write the failing adapter and registration tests**

`SmtpOutboundEmailSenderTests` uses a substitute `IEmailSender` and a `List<string>` logger. Use the existing collecting logger in the test project if there is one; otherwise add a small `ListLogger<T> : ILogger<T>` in `Support/`.

```csharp
    [Fact] public async Task It_maps_the_email_onto_an_html_message_with_a_text_part_and_the_message_id()
    // captures the EmailMessage: To, Subject, Body == Html, IsBodyHtml true, PlainTextBody == Text, From, ReplyTo, MessageId.

    [Fact] public async Task Any_exception_becomes_a_category_and_its_text_is_never_logged()
    // the sender throws new InvalidOperationException("auth failed for smtp-user@mail.secret-host.test:587");
    // asserts the result's error code is "smtp-unknown" and no log line contains "secret-host" or "smtp-user".

    [Fact] public async Task A_timeout_is_a_timeout_category()                     // throws TimeoutException → "smtp-timeout"

    [Fact] public async Task Caller_cancellation_is_rethrown_not_categorised()
    // cancelled token and the sender throws OperationCanceledException(token) → ThrowsAsync<OperationCanceledException>.
```

`SmtpDeliveryException` has an internal constructor, so the unit tests cannot build one. Its `Kind` mapping is covered against a real dead server in Task 16.

`EmailServiceRegistrationTests`:

```csharp
    [Fact] public void The_drain_handler_and_its_dependencies_resolve_with_scope_and_build_validation()
    // ServiceCollection + AddLogging + AddTechStrapPersistence + AddTechStrapEmail(config with Email:Smtp:Host, DefaultFrom and a connection string);
    // BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }); resolve IDrainEmailOutboxHandler from a scope.

    [Theory] [InlineData("")] [InlineData("maybe")]
    public void A_powered_by_value_that_is_not_true_false_or_blank_fails_validation(string value)
    // "" is valid (default true); "maybe" throws OptionsValidationException when the options are read. Split into two facts if clearer.

    [Fact] public void An_enabled_worker_without_an_smtp_host_or_default_from_fails_validation()
    [Fact] public void A_disabled_worker_needs_no_smtp_settings()
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter DrainEmailOutboxHandlerTests`
and `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter "SmtpOutboundEmailSenderTests|EmailServiceRegistrationTests"`.
Expected: build failures.

- [ ] **Step 4: Implement**

**The handler.** Its constructor takes `IEmailOutboxStore store`, `IProductRepository products`, `IEmailTemplateRenderer renderer`, `IOutboundEmailSender sender`, `IOptions<EmailOutboxWorkerOptions> options` and `ILogger<DrainEmailOutboxHandler> logger`.

```csharp
public async Task<Result<DrainResult>> HandleAsync(string workerId, CancellationToken cancellationToken)
{
    var settings = options.Value;
    var batch = await store.ClaimBatchAsync(workerId, settings.BatchSize, TimeSpan.FromSeconds(settings.LeaseSeconds), cancellationToken);
    var productCache = new Dictionary<Guid, Product?>();
    int sent = 0, failed = 0;
    foreach (var item in batch)
    {
        var outcome = await SendOneAsync(item, productCache, cancellationToken);
        if (outcome is null)
        {
            var marked = await store.MarkSentAsync(item.Id, workerId, cancellationToken);
            if (marked.IsFailure)
            {
                logger.LogWarning("Outbox row {OutboxId} was sent but could not be marked sent: {Code}.", item.Id, marked.Errors[0].Code);
            }

            sent++;
        }
        else
        {
            var marked = await store.MarkFailedAsync(item.Id, workerId, outcome, cancellationToken);
            if (marked.IsFailure)
            {
                logger.LogWarning("Outbox row {OutboxId} failed ({Category}) and could not be marked: {Code}.", item.Id, outcome, marked.Errors[0].Code);
            }

            failed++;
        }
    }

    return Result<DrainResult>.Success(new DrainResult(batch.Count, sent, failed));
}
```

`SendOneAsync` returns `null` on success or a failure category, and never throws except for cancellation:
- An unknown `item.Kind` returns `DrainFailures.UnknownKind`.
- It deserialises the payload with `JsonSerializerDefaults.Web`. A `JsonException`, a null result, or a blank `TicketNumber` or `PortalLink` returns `PayloadInvalid`.
- A null `item.ProductId`, or a product that is not found, returns `ProductMissing`. Fetch the product once per id per batch, using the dictionary.
- It renders inside `try`/`catch (Exception ex) when (ex is not OperationCanceledException)`. On an exception it logs `ex.GetType().Name` only and returns `RenderFailed`.
- It sends `new OutboundEmail(item.ToAddress, rendered.Subject, rendered.Text, rendered.Html, rendered.From, rendered.ReplyTo, OutboundMessageIds.For(item.Id))`. On failure it returns `result.Errors[0].Code`.

Backoff and dead-lettering stay in the Domain (`OutboxRetryPolicy` through `MarkFailedAsync`). Do not compute them here.

**The adapter** (`internal sealed class SmtpOutboundEmailSender(IEmailSender sender, ILogger<SmtpOutboundEmailSender> logger) : IOutboundEmailSender`):

```csharp
public async Task<Result> SendAsync(OutboundEmail email, CancellationToken cancellationToken)
{
    string category;
    try
    {
        await sender.SendAsync(
            new EmailMessage(email.To, email.Subject, email.Html, IsBodyHtml: true, From: email.From, PlainTextBody: email.Text, ReplyTo: email.ReplyTo)
            {
                MessageId = email.MessageId,
            },
            cancellationToken);
        return Result.Success();
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (SmtpDeliveryException ex)
    {
        category = ex.Kind switch
        {
            SmtpFailureKind.Transient => EmailSendFailures.Transient,
            SmtpFailureKind.Permanent => EmailSendFailures.Permanent,
            SmtpFailureKind.Authentication => EmailSendFailures.Authentication,
            SmtpFailureKind.Timeout => EmailSendFailures.Timeout,
            _ => EmailSendFailures.Unknown,
        };
    }
    catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
    {
        category = EmailSendFailures.Timeout;
    }
    catch (Exception)
    {
        category = EmailSendFailures.Unknown;
    }

    // Never log the exception or its message: SMTP exceptions can carry the host and credentials.
    logger.LogWarning("Email {MessageId} was not sent: {Category}.", email.MessageId, category);
    return Result.Failure(new ResultError(category, "The email could not be sent.", ResultErrorKind.Failure));
}
```

Check the `SmtpDeliveryException` and `SmtpFailureKind` names, and the `Result.Success()` and `Result.Failure(...)` factories, against the restored packages. A `catch (Exception)` that discards the exception may trip CA1031. If it does, suppress it at that line with a justification comment, which is consistent with how the repo handles it elsewhere. Grep for `CA1031` first.

**Registration** (`AddTechStrapEmail`):

```csharp
public static IServiceCollection AddTechStrapEmail(this IServiceCollection services, IConfiguration configuration)
{
    services.AddSmtpEmailSender(configuration);
    services.TryAddSingleton<IOutboundEmailSender, SmtpOutboundEmailSender>();
    services.TryAddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

    var poweredBy = configuration[EmailBrandingOptions.ShowPoweredByKey];
    services.AddOptions<EmailBrandingOptions>()
        .Configure(options => options.ShowPoweredBy = !bool.TryParse(poweredBy, out var show) || show)
        .Validate(_ => string.IsNullOrWhiteSpace(poweredBy) || bool.TryParse(poweredBy, out _),
            $"{EmailBrandingOptions.ShowPoweredByKey} must be true or false.")
        .ValidateOnStart();

    services.AddOptions<EmailOutboxWorkerOptions>()
        .Bind(configuration.GetSection(EmailOutboxWorkerOptions.SectionName))
        .Validate(options => options.PollIntervalSeconds >= 1, "EmailOutbox:PollIntervalSeconds must be >= 1.")
        .Validate(options => options.BatchSize is >= 1 and <= Paging.MaxBatchSize, $"EmailOutbox:BatchSize must be between 1 and {Paging.MaxBatchSize}.")
        .Validate(options => options.LeaseSeconds >= 30, "EmailOutbox:LeaseSeconds must be >= 30.")
        .ValidateOnStart();

    services.AddOptions<SmtpOptions>()
        .Validate<IOptions<EmailOutboxWorkerOptions>>(
            (smtp, worker) => !worker.Value.Enabled || (!string.IsNullOrWhiteSpace(smtp.Host) && !string.IsNullOrWhiteSpace(smtp.DefaultFrom)),
            "Email:Smtp:Host and Email:Smtp:DefaultFrom are required while the email outbox worker is enabled.")
        .ValidateOnStart();

    services.TryAddScoped<IDrainEmailOutboxHandler, DrainEmailOutboxHandler>();
    return services;
}
```

`Paging` is `TechStrap.Domain.Rules.Paging`. Check where it lives with a grep. `ShowPoweredBy` reads the configuration value once, at registration. That is fine for the Worker, because environment variables are fixed at start.

- [ ] **Step 5: Run the tests, then the architecture tests and the build**

Run the three filtered test commands, then `dotnet test --project tests/TechStrap.Architecture.Tests -c Release` and `dotnet build TechStrap.slnx -c Release`.
Expected: PASS, with 0 warnings. `HandlerConstructorDependencyTests` accepts the handler: all its dependencies are Application interfaces, `IOptions<>` or `ILogger<>`. `ApplicationHandlerRegistrationTests` (Task 11) proves the Api does not register it.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat(email): outbox drain handler with sanitised SMTP adapter" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 16: Worker loop and SMTP integration tests

**Files:**
- Create: `src/TechStrap.Worker/Outbox/EmailOutboxWorker.cs`
- Modify: `src/TechStrap.Worker/Program.cs`
- Modify: `tests/TechStrap.Api.Tests/HostFactory.cs` (`WorkerFactory` gets `configureServices`, and defaults `EmailOutbox:Enabled=false`)
- Create: `tests/TechStrap.Api.Tests/Worker/EmailOutboxWorkerTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/Support/MailpitContainer.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/EmailDrainIntegrationTests.cs`
- Modify: `docs/architecture/03-PACKAGE-MAP.md` (pin the Mailpit tag) and, only if a direct `Testcontainers` reference is needed, `Directory.Packages.props` and the map in the same commit

**Interfaces:**
- Consumes: `IDrainEmailOutboxHandler`, `EmailOutboxWorkerOptions`, `AddTechStrapEmail` (Task 15).
- Produces:

```csharp
namespace TechStrap.Worker.Outbox;

/// <summary>
/// Drains the email outbox (D-010, D-033). One DI scope per iteration (the outbox store clears its change tracker and runs its own
/// transactions). Keeps going while batches come back non-empty; waits the poll interval when idle or after an unexpected error.
/// </summary>
public sealed class EmailOutboxWorker(
    IServiceScopeFactory scopes,
    IOptions<EmailOutboxWorkerOptions> options,
    TimeProvider clock,
    ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    public string WorkerId { get; }
    protected override Task ExecuteAsync(CancellationToken stoppingToken);
}
```

- [ ] **Step 1: Pin Mailpit**

Find the current stable `axllent/mailpit` release tag; use an exact `vX.Y.Z`, never `latest`. Record it in the container-images table of `03-PACKAGE-MAP.md` ("Mailpit image | Local and test SMTP capture | P05"), replacing "not pinned". Use the same tag in `MailpitContainer` and, in Task 17, in compose.

Check its API before writing the tests: `GET /api/v1/messages` (list, with `total` and `messages[].MessageID`) and `DELETE /api/v1/messages`. Write down in your report the exact fields you rely on.

- [ ] **Step 2: Write the failing Worker loop tests**

`EmailOutboxWorkerTests`:
- Build a `ServiceCollection` with `AddScoped<IDrainEmailOutboxHandler>(_ => { scopesCreated++; return handler; })`.
- Use `FakeTimeProvider` and `NullLogger`, or the collecting logger.
- Start the worker with `StartAsync` and stop it with `StopAsync`.
- Wait for call counts with a bounded polling helper that gives up after 5 s of real time. Advance `FakeTimeProvider` explicitly; never sleep for the poll interval.

```csharp
    [Fact] public async Task It_keeps_draining_without_waiting_while_batches_are_not_empty()
    // handler returns Claimed 3, 2, 0, 0...; asserts three calls with no time advanced, the fourth only after Advance(5 s).

    [Fact] public async Task Each_iteration_gets_a_fresh_scope()
    // after three iterations scopesCreated == 3.

    [Fact] public async Task An_unexpected_exception_is_logged_and_the_loop_resumes_after_the_poll_interval()

    [Fact] public async Task A_failed_result_waits_the_poll_interval()

    [Fact] public async Task A_disabled_worker_never_calls_the_handler()

    [Fact] public async Task Stopping_cancels_the_wait_promptly()
    // StopAsync completes within 1 s of real time while the worker is waiting.

    [Fact] public void A_configured_worker_id_is_used_and_a_blank_one_is_generated_uniquely()
```

In `WorkerFactory`, default the new `EmailOutbox:Enabled` setting to `false`, unless `settings` overrides it, so the existing host smoke tests do not start a loop against an unmigrated database. Then add one more host test:

```csharp
    [Fact] public async Task Worker_boots_with_the_outbox_enabled_and_smtp_configured()
    // WorkerFactory with EmailOutbox:Enabled=true, Email:Smtp:Host=localhost, Email:Smtp:DefaultFrom=support@example.test,
    // a migrated database; /health/ready is 200 and the hosted service list contains EmailOutboxWorker.

    [Fact] public async Task Worker_refuses_to_boot_with_the_outbox_enabled_and_no_smtp_host()
```

- [ ] **Step 3: Write the failing SMTP integration tests**

`MailpitContainer` (Support) wraps a Testcontainers generic container: image `axllent/mailpit:<pinned tag>`, with ports 1025 and 8025 bound to random host ports, waiting until port 8025 responds. It exposes:
- `SmtpHost`, `SmtpPort`;
- `Task<IReadOnlyList<MailpitMessage>> MessagesAsync(CancellationToken)`, reading every page;
- `Task ClearAsync(CancellationToken)`.

`MailpitMessage` holds the fields you recorded in Step 1, at minimum `MessageId`, `Subject` and the `To` addresses. Use a collection fixture, so the suite starts one container.

`EmailDrainIntegrationTests` is a `PostgresIntegrationTestBase` using the Mailpit fixture. Each test builds its host with `PersistenceTestHost(Database, configure: services => services.AddLogging(...).AddTechStrapEmail(config))`. The configuration is:
- `Email:Smtp:Host` and `Port` from the container;
- `TlsMode=None`, `MaxRetryAttempts=1`, `RetryMode=TransientOnly`, `TotalSendTimeout=00:00:10`;
- `DefaultFrom=support@example.test`.

Seed one product with branding From `help@orbitly.test`, and queue rows with `EmailOutboxItem.Enqueue` through `IEmailOutbox` in `host.CommitAsync`.

```csharp
    [Fact] public async Task One_hundred_queued_emails_drained_by_two_workers_arrive_once_each_with_the_outbox_id_as_message_id()
    // run two loops concurrently, each in its own scopes with worker ids "w1" and "w2", calling the handler until it reports Claimed 0;
    // asserts Mailpit has exactly 100 messages, the set of MessageIds equals { OutboundMessageIds.For(id) } for every row,
    // and every row has status Sent and attempts 1.

    [Fact] public async Task The_confirmation_carries_the_product_branding_and_the_portal_link()
    // one row; asserts the Mailpit message's From display name is the product's display name, the subject starts "[ORB-",
    // and the HTML (GET /api/v1/message/{ID}) contains the portal link.

    [Fact] public async Task A_dead_smtp_server_retries_then_dead_letters_without_leaking_details()
    // SMTP host "dead-smtp.invalid", port 2525, Username "smtp-user-canary", Password "smtp-pass-canary";
    // one row; loop five times: drain with "w1", then host.Clock.Advance(OutboxRetryPolicy.MaxDelay).
    // Assert status DeadLettered and attempts == OutboxRetryPolicy.MaxAttempts;
    // last_error is one of the EmailSendFailures values;
    // last_error and every captured log line contain none of "dead-smtp.invalid", "smtp-user-canary", "smtp-pass-canary".

    [Fact] public async Task A_row_claimed_by_a_crashed_worker_is_sent_after_its_lease_expires()
    // claim directly through IEmailOutboxStore.ClaimBatchAsync("crashed", 20, 2 min) and never mark it;
    // drain with "w2": nothing is sent; advance the clock by 3 minutes; drain with "w2": one message in Mailpit, row Sent.
```

Clear Mailpit at the start of each test.

The dead-server test must not wait on real backoff time. It relies on the host's `FakeTimeProvider` driving `next_attempt_at`. If DNS resolution of `.invalid` is slow on CI, use `127.0.0.1` with a port taken from a `TcpListener` that has been bound and stopped, and keep the canaries in the username and password.

- [ ] **Step 4: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "EmailOutboxWorkerTests|HostHealthSmokeTests"` and `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter EmailDrainIntegrationTests`.
Expected: build failures.

- [ ] **Step 5: Implement the Worker**

```csharp
public sealed class EmailOutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly EmailOutboxWorkerOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<EmailOutboxWorker> _logger;

    public EmailOutboxWorker(IServiceScopeFactory scopes, IOptions<EmailOutboxWorkerOptions> options, TimeProvider clock, ILogger<EmailOutboxWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
        WorkerId = string.IsNullOrWhiteSpace(_options.WorkerId)
            ? $"{Truncate(Environment.MachineName, 32)}-{Guid.NewGuid():N}"
            : _options.WorkerId.Trim();
    }

    public string WorkerId { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Email outbox worker is disabled.");
            return;
        }

        var idle = TimeSpan.FromSeconds(_options.PollIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            bool more;
            try
            {
                more = await DrainOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email outbox drain failed; retrying in {Delay}.", idle);
                more = false;
            }

            if (more)
            {
                continue;
            }

            try
            {
                await Task.Delay(idle, _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> DrainOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IDrainEmailOutboxHandler>();
        var result = await handler.HandleAsync(WorkerId, cancellationToken);
        if (result.IsFailure)
        {
            _logger.LogWarning("Email outbox drain returned {Code}.", result.Errors[0].Code);
            return false;
        }

        return result.Value.Claimed > 0;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
```

Check the maximum length of `claimed_by` in the outbox record configuration, and the guard in `EmailOutboxItem.Claim`. Make sure a generated id fits; 32 + 1 + 32 = 65 characters.

Logging the exception is acceptable here. The only exceptions that reach this point are database and infrastructure failures, because the SMTP adapter already turns every send failure into a category.

`Program.cs` (Worker), after `AddTechStrapPersistence()`:

```csharp
builder.Services.AddTechStrapEmail(builder.Configuration);
builder.Services.AddHostedService<EmailOutboxWorker>();
```

Update the Program comment that says background loops arrive in PHASE-05.

`WorkerFactory`:
- Add an optional `Action<IServiceCollection>? configureServices` parameter, mirroring `ApiFactory`.
- Merge `EmailOutbox:Enabled=false` into the settings before the caller's `settings`, so a caller can turn the worker on.

- [ ] **Step 6: Run the tests and confirm they pass**

Run:
- the two filtered commands from Step 4;
- `dotnet test --project tests/TechStrap.Api.Tests -c Release`;
- `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release`.

Expected: PASS.

Run `EmailDrainIntegrationTests` three times to check they are stable.

- [ ] **Step 7: Build and commit**

Run `dotnet build TechStrap.slnx -c Release` (0 warnings), then `pwsh -File scripts/Check-PackageVersions.ps1`, which must pass with the Mailpit row pinned. Check `git diff --cached --stat`.

```bash
git add src tests docs/architecture/03-PACKAGE-MAP.md Directory.Packages.props
git commit -m "feat(worker): email outbox loop with Mailpit-backed delivery tests" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 17: Compose, configuration, smoke script, leak tests and closing docs

**Files:**
- Modify: `docker-compose.yml`, `docker-compose.uat.yml`, `docker-compose.production.yml`
- Modify: `.env.production.example` (and `.env.uat.example` if it exists; check with `ls -a`)
- Modify: `src/TechStrap.Worker/.env.example`
- Modify: `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs` (Worker row)
- Modify: `scripts/tests/ComposeFiles.Tests.ps1`
- Create: `scripts/Send-TestTicket.ps1`
- Create: `scripts/tests/Send-TestTicket.Tests.ps1`
- Create: `tests/TechStrap.Api.Tests/Intake/SensitiveDataLeakTests.cs`
- Create: `docs/development/INTAKE.md`
- Modify: `README.md` (a short "Submit a test ticket" section linking INTAKE.md)
- Modify: `docs/architecture/PHASE-05-intake-email-worker.md` (tick the Definition of Done, link the plan) and the roadmap or phase index that tracks phase status (find it with `grep -rl "PHASE-04" docs/architecture/*.md`)

**Interfaces:**
- Consumes: everything above.
- Produces: the configuration surface operators use. No new code types.

- [ ] **Step 1: Write the failing tests**

`SensitiveDataLeakTests` uses `ApiFactory` and a migrated database seeded by `IntakeTestData`. It reads `factory.LogSink` from `HostFactory`.

```csharp
    [Fact] public async Task A_key_submission_leaves_the_plaintext_key_and_token_out_of_logs_and_every_column_but_the_outbox_payload()
    // POST /api/intake/tickets with the trusted key and an Idempotency-Key, then the same again (a replay).
    // tokens = the path segment after "/t/" of both ViewUrls.
    // 1. No captured log line contains the plaintext API key or any token.
    // 2. For every text, varchar, citext and jsonb column of every table in the public schema (read information_schema.columns),
    //    `select count(*) from {table} where {column}::text like '%' || @needle || '%'` is 0 for the key and each token,
    //    except email_outbox.payload, which contains the first token only.
    // 3. ticket_access_tokens.token_hash values start with "sha256:" and are not the tokens.

    [Fact] public async Task Error_responses_never_echo_the_api_key()
    // a 400 (invalid email) with a valid key: the body does not contain the key.
```

Quote the identifiers from `information_schema` with `"` in the dynamic SQL; the test builds the SQL only from catalogue names, never from user input.

In `scripts/tests/ComposeFiles.Tests.ps1`:
- Add `mailpit` to the expected local service set.
- Add these tests:
  - `'local mailpit publishes its web UI on loopback only and no SMTP port'`;
  - `'local worker sends through mailpit without TLS and with outbox-safe retries'`: `Email__Smtp__Host` is `mailpit`, `Port` is `1025`, `TlsMode` is `None`, `MaxRetryAttempts` is `1`, and `RetryMode` is `TransientOnly`;
  - `'production and uat workers use outbox-safe SMTP settings and pass the Powered-by setting'`: `MaxRetryAttempts` is `1`, `RetryMode` is `TransientOnly`, `TlsMode` resolves from `SMTP_TLS_MODE` with the default `StartTls`, and `TECHSTRAP_PORTAL_SHOW_POWERED_BY` is present.
- Keep the existing UAT/production drift test unchanged. It must pass, because both files receive identical edits.

`scripts/tests/Send-TestTicket.Tests.ps1` (Pester 6, matching the existing script tests):

```powershell
Describe 'Send-TestTicket' {
    It 'posts JSON to the intake endpoint with the key and an idempotency key' {
        Mock Invoke-RestMethod { [pscustomobject]@{ ticketNumber = 'ORB-1'; viewUrl = 'http://localhost:8082/t/x'; warnings = @() } }
        & $script -BaseUrl 'http://localhost:8080' -ApiKey 'tsk_example' -Email 'ann@example.com' -Subject 'Hi' -Body 'Hello' | Out-Null
        Should -Invoke Invoke-RestMethod -Times 1 -ParameterFilter {
            $Uri -eq 'http://localhost:8080/api/intake/tickets' -and $Method -eq 'Post' -and
            $Headers['X-Api-Key'] -eq 'tsk_example' -and $Headers['Idempotency-Key'] -and $ContentType -eq 'application/json'
        }
    }
    It 'prints the request without sending it under -DryRun' {
        Mock Invoke-RestMethod { throw 'must not be called' }
        $output = & $script -BaseUrl 'http://localhost:8080' -ApiKey 'tsk_example' -DryRun | Out-String
        Should -Invoke Invoke-RestMethod -Times 0
        $output | Should -Match ([regex]::Escape('POST http://localhost:8080/api/intake/tickets'))
    }
    It 'never prints the API key' {
        Mock Invoke-RestMethod { [pscustomobject]@{ ticketNumber = 'ORB-1'; viewUrl = $null; warnings = @() } }
        $normal = & $script -ApiKey 'tsk_canaryKeyValue' | Out-String
        $dry = & $script -ApiKey 'tsk_canaryKeyValue' -DryRun | Out-String
        $normal | Should -Not -Match 'tsk_canaryKeyValue'
        $dry | Should -Not -Match 'tsk_canaryKeyValue'
    }
}
```

`$script` is resolved in `BeforeAll` the same way the other script tests resolve their script path.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "SensitiveDataLeakTests|EnvExampleCompletenessTests"` and `pwsh -File scripts/Invoke-ScriptTests.ps1`.
Expected: FAIL. The script is missing, the compose expectations are not yet met, and the Worker `.env.example` keys are missing. `SensitiveDataLeakTests` may already pass; if so, say so in your report. The test is a regression guard.

- [ ] **Step 3: Implement**

**`docker-compose.yml`**:
- Add a `mailpit` service:
  - `image: axllent/mailpit:<pinned tag>`;
  - `ports: ["127.0.0.1:8025:8025"]` (the web UI only; SMTP stays on the compose network);
  - a healthcheck using the image's own readiness command (`/mailpit readyz` in current releases; check this for the pinned tag);
  - `restart: unless-stopped` only if the other local services use it.
- On `worker`, add `depends_on: mailpit: condition: service_healthy` and this environment:

```yaml
      Email__Smtp__Host: mailpit
      Email__Smtp__Port: "1025"
      Email__Smtp__TlsMode: None
      Email__Smtp__MaxRetryAttempts: "1"
      Email__Smtp__RetryMode: TransientOnly
      Email__Smtp__TotalSendTimeout: "00:00:30"
      Email__Smtp__DefaultFrom: support@techstrap.localhost
      TECHSTRAP_PORTAL_SHOW_POWERED_BY: ${TECHSTRAP_PORTAL_SHOW_POWERED_BY:-true}
```

**`docker-compose.uat.yml` and `docker-compose.production.yml`** get identical edits to the worker environment:

```yaml
      Email__Smtp__TlsMode: ${SMTP_TLS_MODE:-StartTls}
      Email__Smtp__MaxRetryAttempts: "1"
      Email__Smtp__RetryMode: TransientOnly
      Email__Smtp__TotalSendTimeout: "00:00:30"
      TECHSTRAP_PORTAL_SHOW_POWERED_BY: ${TECHSTRAP_PORTAL_SHOW_POWERED_BY:-true}
```

**`.env.production.example`**: add `SMTP_TLS_MODE=StartTls`, with a comment listing `None`, `Auto`, `StartTls`, `SslOnConnect` and `StartTlsWhenAvailable`. Also add `TECHSTRAP_PORTAL_SHOW_POWERED_BY=true`, with the D-024 comment.

**`src/TechStrap.Worker/.env.example`**:
- Change `EMAIL__SMTP__MAXRETRYATTEMPTS` to `1`.
- Add `EMAIL__SMTP__TLSMODE=None`, with the comment `Mailpit has no TLS; use StartTls or SslOnConnect for a real server`.
- Add `EMAIL__SMTP__RETRYMODE=TransientOnly` and `EMAIL__SMTP__TOTALSENDTIMEOUT=00:00:30`.
- Add `EMAILOUTBOX__ENABLED=true`, `EMAILOUTBOX__POLLINTERVALSECONDS=5`, `EMAILOUTBOX__BATCHSIZE=20` and `EMAILOUTBOX__LEASESECONDS=120`, with the comment `the lease must cover sending one whole batch`.
- Add `# EMAILOUTBOX__WORKERID=`, with the comment `blank: machine name plus a random suffix`.

Add every new key to the Worker row of `EnvExampleCompletenessTests.Hosts()`. List the `EmailOutboxWorkerOptions` properties the same way `PublicRateLimitOptions` is listed.

**`scripts/Send-TestTicket.ps1`**:

```powershell
<#
.SYNOPSIS
Submits one test ticket to a running TechStrap API through the API-key intake endpoint.
.DESCRIPTION
Defaults target the local compose stack and the development Orbitly trusted key (seeded when TECHSTRAP_SEED_DEV_DATA=true;
not a secret, see docs/development/DEV-DATA.md). The confirmation email lands in Mailpit at http://localhost:8025.
#>
[CmdletBinding()]
param(
    [string] $BaseUrl = 'http://localhost:8080',
    [string] $ApiKey = 'tsk_devOrbitlyServerKeyNotASecret00000000000000',
    [string] $Email = 'test.customer@example.com',
    [string] $Name = 'Test Customer',
    [string] $Subject = 'Test ticket from Send-TestTicket.ps1',
    [string] $Body = 'This is a test ticket.',
    [string] $IdempotencyKey = [guid]::NewGuid().ToString(),
    [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$uri = '{0}/api/intake/tickets' -f $BaseUrl.TrimEnd('/')
$payload = @{ email = $Email; name = $Name; subject = $Subject; body = $Body } | ConvertTo-Json -Compress
$headers = @{ 'X-Api-Key' = $ApiKey; 'Idempotency-Key' = $IdempotencyKey }

if ($DryRun) {
    Write-Output "POST $uri"
    Write-Output "Idempotency-Key: $IdempotencyKey"
    Write-Output $payload
    return
}

$response = Invoke-RestMethod -Uri $uri -Method Post -Headers $headers -ContentType 'application/json' -Body $payload
Write-Output ("Created {0}" -f $response.ticketNumber)
if ($response.viewUrl) { Write-Output ("Customer link: {0}" -f $response.viewUrl) }
foreach ($warning in @($response.warnings)) { if ($warning) { Write-Output ("Warning: {0}" -f $warning) } }
```

The default key must equal `DevelopmentApiKeys.OrbitlyTrusted`. Add a C# test, `DevelopmentApiKeysTests.The_smoke_script_default_key_is_the_seeded_orbitly_trusted_key`, in the test project that already covers `DevelopmentApiKeys`. It reads `scripts/Send-TestTicket.ps1` and asserts that it contains the constant. Find the repository root the way other tests do.

**`docs/development/INTAKE.md`** covers:
- the three submission routes, with their auth, rate limits and a `curl` example each (use the dev keys from DEV-DATA.md);
- the honeypot behaviour (D-032);
- idempotency, including the fresh link on a replay;
- the attachment rules;
- Mailpit at http://localhost:8025;
- `Send-TestTicket.ps1`;
- how the outbox drains, retries (1, 2, 4, 8 minutes, then dead-letter after 5 attempts) and identifies messages (`Message-ID: <outbox-id>@techstrap.local`);
- the known limit that unauthenticated callers can create rate-limit partitions by inventing key prefixes (Task 13).

Keep to the style of the existing `docs/development/*.md` files.

**README**: add a 3 to 5 line section that links `docs/development/INTAKE.md`.

**Closing docs**:
- Tick the PHASE-05 Definition of Done items that this plan delivered.
- Mark each carried-forward item with its target phase (D-033: PHASE-12 retention; D-034: PHASE-09 follow-ups and PHASE-11 API attachments).
- Set the phase status in the roadmap or index file to "Implemented (pending merge)", using the wording PHASE-04 used.

- [ ] **Step 4: Run every check**

Run:
- `dotnet build TechStrap.slnx -c Release` (0 warnings)
- `dotnet test --solution TechStrap.CI.slnf -c Release`
- `pwsh -File scripts/Invoke-ScriptTests.ps1`
- `pwsh -File scripts/Check-PackageVersions.ps1`
- `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api` (expected: no pending changes)
- `docker compose config --quiet` (the local file resolves)

Expected: everything passes.

Then smoke-test the local stack, without `-v` on any `down`:
1. `docker compose up -d --build --wait`, with `TECHSTRAP_SEED_DEV_DATA=true` in the shell.
2. `pwsh -File scripts/Send-TestTicket.ps1`.
3. Within about 10 s, `curl -s http://localhost:8025/api/v1/messages` shows one message, whose subject starts `[ORB-`.

Write the commands and their output in your report. If Docker is unavailable, say so; do not mark the smoke test done.

- [ ] **Step 5: Commit**

Check `git diff --cached --stat`; there must be no `.superpowers/` files and no `.env` files other than the `*.example` files.

```bash
git add docker-compose.yml docker-compose.uat.yml docker-compose.production.yml .env.production.example src tests scripts docs README.md
git commit -m "feat(ops): Mailpit, outbox-safe SMTP settings, smoke script, leak tests and PHASE-05 docs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```
