# PHASE-07a Admin Shell, Sign-in and Ticket Handling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the Admin placeholder into a working agent app. Agents sign in with OIDC, see the ticket queue with every view and filter, and open a ticket to read its timeline. From the ticket they can reply or add internal notes with files, and change status, assignee, priority, product and tags. They can mark a ticket spam or not spam, and admins can delete tickets or erase requesters. The Admin talks to the API only through typed clients over Contracts.

**Architecture:**
- **Sign-in.** Admin is a Blazor Server app with global interactive rendering. Sign-in uses a cookie plus OIDC on non-interactive endpoints.
- **Token forwarding.** Access tokens reach the API through `SyntaxCircus.Blazor.Auth`. Every API call goes through a small Admin `ApiConnection`. It picks a retrying read client or a never-retrying write client, both from `IBlazorCircuitHttpClientFactory`. It maps ProblemDetails, including `errorCodes`, into `SyntaxCircus.Common` `Result`.
- **Access.** UI access comes from the API itself: an `AgentSession` calls `GET /api/agents/me` once, which also provisions the agent. NoAccess wins on any 403.
- **Attachments.** They stream through an authenticated Admin pass-through endpoint (D-017).
- **Shared hosting project.** The PII log redactor and the Sentry header scrubber move to a new `TechStrap.Hosting` project, so Admin can use them without referencing Infrastructure.

**Tech Stack:** .NET 10, Blazor Server (interactive server render mode), ASP.NET Core cookie + OpenIdConnect, SyntaxCircus.Blazor.Auth, SyntaxCircus.Http.Resilience, SyntaxCircus.Common, Serilog, Sentry. Tests use xUnit v3, Shouldly, NSubstitute and bUnit 2.11.3.

**Spec:** `docs/architecture/PHASE-07-admin-app.md` (T01–T13, T22), `docs/architecture/UX-BRIEF-admin.md`, `docs/architecture/02-ARCHITECTURE.md` (Admin sections), D-004, D-016, D-017, D-022, D-024, D-029, D-036, D-039, and the owner decisions of 2026-10-04, recorded as D-040 in Task 1.

### Owner decisions (2026-10-04), recorded as D-040 in Task 1
- **Three PRs.**
  - 07a (this plan): sign-in, shell, typed clients, queue, and ticket detail with every agent action.
  - 07b: the settings and admin pages.
  - 07c: the brand, responsive and accessibility pass, plus the compose and Dockerfile checks, the Admin architecture rules, the CSP, shared host wiring, and the OpenAPI bearer scheme.
- **Authentik is not set up yet.** OIDC is wired from config. Tests use a fake authentication scheme and a stub API. The docs get an Authentik setup note.
- **New `TechStrap.Hosting` project.** It holds `PiiRedactionEnricher` and `SensitiveHeaderSentryProcessor`. Api, Worker, Admin and Portal may reference it. Infrastructure drops its Serilog package.
- **No Playwright in PHASE-07.** Browser smoke tests move to PHASE-12.

### Technical decisions this plan makes (recorded as D-040 in Task 1; the owner confirms at plan review)
- **OIDC config.** It keeps the existing `Auth__*` keys (`AddBlazorTokenForwarding(config, "Auth")`).
- **Group keys and options.** These are the API's flat `TECHSTRAP_AGENT_GROUP`, `TECHSTRAP_ADMIN_GROUP` and `TECHSTRAP_GROUP_CLAIM_TYPE`. Options are validated on start and read lazily. Compose passes the new keys to the admin service.
- **UI gating.** It uses the API's answer from `GET /api/agents/me`, not a copy of the group-claim parsing. Admin-only actions (delete, erase) appear only for the Admin role, and the API still enforces it.
- **Sign-in endpoints and routing.**
  - Cookie scheme `"Cookies"` with OpenIdConnect (code + PKCE, `SaveTokens`, `offline_access`, `MapInboundClaims=false`).
  - Non-interactive `/signin`, `/signin/start` and `POST /signout`.
  - The fallback policy requires an authenticated user. `/error`, `/not-found` and `/signin*` stay anonymous, and `/_styleguide` stays anonymous in Development only.
  - Routes and HeadOutlet render `InteractiveServer`.
- **HTTP clients.** There are two named clients, `techstrap-api-read` (resilient, 3 retries) and `techstrap-api-write` (no retry). Admin uses its own `ApiConnection` instead of `ApiClientBase`, because `ApiClientBase` drops `errorCodes` and has no `Result` mapping.
- **Folders.** The existing `Components/{Ui,Pages,Layout}` stay. Shared primitives go in `Components/Ui`, and the new folders are `Auth/`, `Clients/`, `Features/Queue/` and `Features/Tickets/`. `StatusStamp` and `PriorityMark` serve as the spec's StatusBadge and PriorityBadge.
- **Sidebar and conflicts.** The sidebar is optimistic-free: it shows a pending state, then re-renders from the returned `TicketStateDto`. A conflict raises a non-blocking banner with Reload, and drafts are kept.
- **Queue defaults.** The queue opens on the Unassigned view. Page size is 25, search debounces at 300 ms, and all state lives in the query string.
- **Keyboard.** One ES-module key listener (`wwwroot/js/shortcuts.js`) drives the shortcuts. The command palette (Ctrl+K) is deferred to 07c.
- **Dialogs.**
  - Native `<dialog>`; Enter never confirms.
  - Spam asks for confirmation. Delete needs the ticket number typed, and erase needs the requester's email typed.
  - Not spam has no dialog.
- **Forced by Blazor or the API, found while prototyping:**
  - Component view models are `public`. Razor components are always public, so an internal parameter type does not compile.
  - The composer enforces file size, type and count itself, because `InputFile` has no size limit parameter.
  - Not spam from the Spam view first reads the ticket, because the queue row has no RowVersion.
  - A Closed ticket shows a read-only facts panel instead of the sidebar.
  - Times show in UTC; local-zone display moves to 07c.
  - The conflict banner offers Reload, but not the UX's "Apply my change again".
  - `MessageThread` is merged into `TicketTimeline` and `MessageBubble`.
  - Queue tabs are real links, so each view can be bookmarked.
- **Known API gaps, recorded:**
  - the erase dialog shows no ticket or attachment counts;
  - the follow-up children list is omitted;
  - the parent number of a follow-up is fetched by id.

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on. Private fields are `_camelCase`, constants are PascalCase, and namespaces are file-scoped.
- **Packages.** Versions are managed centrally. This plan adds no new package. It may reference packages already in `Directory.Packages.props`: SyntaxCircus.Blazor.Auth, SyntaxCircus.Http.Resilience, SyntaxCircus.Common, Microsoft.AspNetCore.Authentication.OpenIdConnect, Microsoft.Extensions.Http, and bunit. `PackageReference`s carry no version.
- **Project references.** Admin and Portal reference only Contracts and Hosting, never Application, Infrastructure or Domain. Hosting references no TechStrap project. Api and Worker may also reference Hosting. The other rules are unchanged.
- **Admin code.**
  - Components never inject `HttpClient`. They inject only `I*Client` interfaces and session or services.
  - ViewModels are `internal` and feature-local, and DTOs are never renamed.
  - Paired `.razor` and `.razor.cs` files are required beyond the inline ceiling (simple parameters, plus at most one trivial callback).
  - Factories or presenters are used only for non-trivial mapping: `TicketDetailPresenter` and `TimelineEntryFactory`.
  - Named constants cover the view names, page size, debounce delay and event types.
  - JavaScript lives only in ES modules under `wwwroot/js`, with no inline script.
  - CSS lives in new `Styles/_*.scss` partials imported by `app.scss`, with `ts-` class names.
- **Copy.** Use the exact strings from UX-BRIEF-admin. Reused strings live in `UiCopy`-style constants.
- **Secrets and PII.**
  - Never log a token, cookie, email address or requester name.
  - Tokens never appear in URLs or rendered markup.
  - The attachment pass-through never buffers and always sends `Content-Disposition: attachment` and `nosniff`.
- **Commits.** Use Conventional Commits, each ending with exactly:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden:**
  - `git add -f`;
  - committing `.superpowers/`;
  - `docker compose down -v`;
  - killing processes you did not start;
  - hand-editing EF migrations (none are expected);
  - committing without first running `git diff --cached --stat`.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release` produces 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release` passes.
  - `pwsh -File scripts/Invoke-ScriptTests.ps1` passes.
  - The EF pending-model check is clean.

## Review Focus

1. **A signed-in user without agent access, or a deactivated agent, reaches ticket data or actions.** NoAccess must win, and no ticket call may happen before `/api/agents/me` succeeds. Pinned in Task 4 by `AgentSessionTests` and in Task 13 by the authenticated host tests.
2. **A token leaks.** That covers an access or refresh token in a log, a URL, rendered markup or the attachment pass-through response, and a circuit request sent without a token. Pinned in Tasks 5 and 6 by the client tests, which show requests go through the named clients with the auth handler, and in Task 13 by a log scan for the test token.
3. **A stale write silently overwrites, or a failed reply loses the agent's draft or files.** Pinned in Task 10 by `ReplyComposerTests` (draft and files survive a conflict) and in Task 11 by the sidebar conflict tests.
4. **A plain agent sees or triggers delete or erase, a destructive action fires without its typed confirmation, or Enter confirms a dialog.** Pinned in Task 12 by `DestructiveActionTests`.
5. **A mutating request is retried, producing a duplicate reply or note.** Pinned in Task 5 by the `ApiConnectionTests` that check a POST is never retried on 503.

---

### Task 1: Record the decisions (D-040) and align the docs

**Files:**
- Modify: `docs/architecture/04-DECISION-LOG.md`. Add the approval-basis bullet, the index row D-040, its section, and one-line amendment notes under D-004, D-017 and D-039.
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` (rows 06 and 07, the Contracts-only convention)
- Modify: `docs/architecture/00-DISCOVERY-INDEX.md` (rows 03, 04, 06, 07 and the two "D-001 to D-022" sentences)
- Modify: `docs/architecture/UX-BRIEF-admin.md` (the stale "Still expected from PHASE-02" bullet)
- Modify: `docs/architecture/PHASE-07-admin-app.md` (delivery split, task tags, deviations, Playwright note)
- Modify: `docs/architecture/02-ARCHITECTURE.md` (reference table, project tree)
- Modify: `docs/architecture/PHASE-09-public-portal.md` and `docs/development/TICKET-OPERATIONS.md` (one sentence each)

**Interfaces:** none (docs only).

- [ ] **Step 1: Add D-040**

Follow the D-037 to D-039 format (Status, Date, Owner, Related artifacts, Context, Decision, Alternatives Considered, Consequences, Approval).

1. Add this bullet to the approval-basis list, after the PHASE-06c bullet (the D-039 one):

```markdown
- **Owner decision (2026-10-04, PHASE-07 planning):** D-040, the owner decisions on the PHASE-07 split, the identity provider and the shared hosting project. Its technical decisions were proposed in the PHASE-07a plan and approved when the owner approved the plan.
```

2. Add this index row after the D-039 row:

```
| D-040 | PHASE-07 lands as 07a/07b/07c; Authentik set up later; TechStrap.Hosting project; Admin sign-in, API clients and ticket-handling decisions | Approved (owner 2026-10-04; technical decisions at PHASE-07a plan review) | 2026-10-04 | PHASE-07, PHASE-08, PHASE-09, PHASE-12 |
```

3. Append this section after D-039, after a `---` line (D-039 is the last section; the file ends with its "Approved on" line):

```markdown
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
- **HTTP clients.** There are two named clients with the auth handler and the forwarded client IP. `techstrap-api-read` is resilient (3 retries); `techstrap-api-write` has no retry and no circuit breaker, so a transient failure can never duplicate a reply or a note. The typed clients sit on a small Admin `ApiConnection`, not on `ApiClientBase`. It reads problem details, including `errorCodes`, into `SyntaxCircus.Common` `Result`: 400 is Validation, 401 Unauthenticated, 403 Forbidden, 404 NotFound, 409 Conflict, anything else Failure. Cancellation by the caller propagates and is never mapped.
- **Attachments (D-017).** `GET /attachments/{id}` streams `GET api/attachments/{id}` through the read client without buffering, forces `Content-Disposition: attachment`, `nosniff` and `Cache-Control: private, no-store`, and answers 404 for any upstream 404. There is no `IAttachmentsClient`: a buffering typed client would defeat the streaming.
- **Sidebar.** It is optimistic-free (the PHASE-07 table wins over the UX brief): the control shows a pending state, then re-renders from the returned `TicketStateDto`. A failure shows an inline error and the previous value. Every write sends the current `RowVersion`. A 409 `concurrency-conflict` raises a non-blocking banner with Reload and keeps drafts.
- **Queue.** The default view is Unassigned (UX), not `TicketViews.Default` (All). The page size is 25, the search debounce 300 ms, and all filter state lives in the query string.
- **Keyboard.** One ES module drives the shortcuts. A status bar with a `role="status"` slot, the shortcut help dialog and the Not spam key `u` are in 07a. The command palette (Ctrl+K) moves to 07c.
- **Dialogs.** Native `<dialog>`; Enter never confirms. Spam asks for confirmation naming the ticket, delete needs the ticket number typed, erase the requester's email. Not spam has no dialog.
- **Reply composer.** Separate drafts for a public reply and an internal note, kept per ticket for the life of the circuit. Selected files are kept until a send succeeds. `LinkedArticleIds` stays empty until PHASE-08.
- **Spec deviations.** `Components/{Ui,Pages,Layout}` stay; the new folders are `Auth/`, `Clients/`, `Options/`, `Features/Queue/` and `Features/Tickets/` (the spec said `Shared/` and `Features/TicketDetail`). `StatusStamp` and `PriorityMark` are the spec's `StatusBadge` and `PriorityBadge`, with a mapper for the API's status strings. The spec's `TicketView.Spam` is `TicketViews.Spam`. The queue mapping stays in the page code-behind; the PHASE-07 table wins over the `TicketQueueViewModelFactory` of 02-ARCHITECTURE section 8.1. `IDeadLettersClient` and `IAdminEventsClient` arrive in 07b with their pages.
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

### Approval
- **Approved by:** Jon Seeley (owner, PHASE-07 planning)
- **Approved on:** 2026-10-04
```

4. Under D-004 "Consequences" add the line: `- Amended in part by D-040: the Admin does not parse the group claim; its no-access page follows the API's answer to GET /api/agents/me.`
5. Under D-017 "Consequences" add the line: `- Amended in part by D-040: the Admin adapter streams through the named read client (not a typed, buffering client) and there is no IAttachmentsClient.`
6. Under D-039 "Consequences" add the line: `- Amended in part by D-040: PiiRedactionEnricher and the Sentry header scrubber moved to TechStrap.Hosting, and the Admin host is wired in PHASE-07a.`

- [ ] **Step 2: Align the other docs**

1. **`99-IMPLEMENTATION-ROADMAP.md`**
   - The last bullet of the cross-cutting conventions, "Handlers accept `TechStrap.Contracts` request records directly (D-016); Admin and Portal reference only Contracts.", becomes "...; Admin and Portal reference only Contracts and Hosting (D-040)."
   - Row 06, last cell: replace `06a and 06b complete; 06c implemented (pending merge)` with `Complete (PRs #6, #7 and #8 merged)`.
   - Row 07: decisions cell `D-017, D-022` becomes `D-017, D-022, D-040`; status `Not started` becomes `07a in progress; 07b and 07c not started (D-040)`.
2. **`00-DISCOVERY-INDEX.md`**
   - Row 03 status `Not started` becomes `Complete`; row 04 the same.
   - Row 06 status `06a and 06b complete; 06c implemented (pending merge)` becomes `Complete`.
   - Row 07 status `Not started` becomes `07a in progress; 07b and 07c not started`.
   - The 04-DECISION-LOG row of the file table: `Material decisions D-001 to D-022` becomes `Material decisions D-001 to D-040`. In "Open decisions": `All decisions D-001 to D-022 are approved.` becomes `All decisions D-001 to D-040 are approved.`
3. **`UX-BRIEF-admin.md`** (around line 746): replace the bullet

   > Still expected from PHASE-02 (tracked there, not by this brief): the final `docs/BRAND.md`, the token sheet for the SCSS overrides, and mockups for states the v2 file does not cover (conflict banner, API-key secret reveal, KB editor, dead letters, tablet width).

   with

   > Delivered by PHASE-02: the final `docs/BRAND.md` and the token sheet for the SCSS overrides. Still open (tracked in PHASE-07, D-040): mockups for states the v2 file does not cover (conflict banner, API-key secret reveal, KB editor, dead letters, tablet width). PHASE-07a builds the conflict banner from the text of this brief; the 07c review covers the rest.
4. **`PHASE-07-admin-app.md`**
   - Insert this section before "## Architecture Decisions":

```markdown
## Delivery split (D-040)

PHASE-07 lands in three PRs. Each task id below carries its PR in brackets.

| PR | Tasks | Content |
| :-- | :---- | :------ |
| 07a | P07-T01 to T13, T22 | Options, sign-in, shell and primitives, typed clients (agents, products, tags, tickets, requesters), queue with the Spam view, ticket detail, reply composer, sidebar, spam/delete/erase, the attachment pass-through |
| 07b | P07-T14 to T18, T23 | Products, API keys, agents and my settings, tags and the audit log, dead letters |
| 07c | P07-T19, T20 | Brand, responsive and accessibility pass, compose and Dockerfile verification, Admin architecture rules, CSP, shared host wiring, OpenAPI bearer scheme, the command palette |
| PHASE-12 | P07-T21 | Playwright smoke tests (dropped from PHASE-07) |

07a deviations from the text below, all recorded in D-040: the typed clients sit on a small `ApiConnection` (not `ApiClientBase`); there is no `IAttachmentsClient`; the folders are `Auth/`, `Clients/`, `Options/`, `Features/Queue/`, `Features/Tickets/` with primitives in `Components/Ui`; `StatusStamp` and `PriorityMark` are the badges; the sidebar is optimistic-free; the Admin references Contracts and Hosting; the no-access page follows the API's answer to `GET /api/agents/me`.
```

   - In "Architecture Decisions", the bullet starting "**Typed clients**" gets this sentence appended: ` 07a builds them on an Admin ApiConnection with two named clients (read: retried; write: never retried), see D-040.`
   - Tag each task: after the bold id insert ` [07a]` for T01 to T13 and T22, ` [07b]` for T14 to T18 and T23, ` [07c]` for T19 and T20. For T21 replace the whole line text after the id with `Moved to PHASE-12 (D-040): optional Playwright smoke tests for sign-in-free paths.`
   - In "No new server entry points", change "it references `TechStrap.Contracts` only" to "it references `TechStrap.Contracts` and `TechStrap.Hosting` only".
5. **`02-ARCHITECTURE.md`**
   - Section 2 reference table: add the row `| Hosting | none (packages only: Serilog and Observability) |` after the Contracts row, and change the Api, Worker, Admin and Portal rows to `Application, Infrastructure (composition root only), Contracts, Hosting`, `Application, Infrastructure (composition root only), Contracts, Hosting`, `Contracts, Hosting` and `Contracts, Hosting`.
   - After the table add: `TechStrap.Hosting` is a leaf with the log redactor and the Sentry header scrubber that every request-serving host shares (D-039, D-040). It references no TechStrap project. Admin and Portal still never reference Application, Infrastructure or Domain.`
   - The "Forbidden" paragraph keeps its text.
   - In the project tree add, after the `TechStrap.Contracts` line: `    TechStrap.Hosting         log redaction (PiiRedactionEnricher) and Sentry header scrubbing shared by Api, Worker, Admin and Portal`.
   - In the Admin rule near the top ("Admin and Portal never reference Application, Infrastructure or Domain..."), no change.
6. **`PHASE-09-public-portal.md`**: append to the carry-forward line from PHASE-06c: ` Both now live in TechStrap.Hosting (D-040): reference it, call options.AddSensitiveHeaderScrubbing() in the UseSentry callback and logger.Enrich.With<PiiRedactionEnricher>() in AddStandardSerilog.`
7. **`docs/development/TICKET-OPERATIONS.md`** (the redaction paragraph, line 214): replace "The Admin and Portal hosts handle no requester data yet; they join in PHASE-07 and PHASE-09." with "The Admin host has joined since PHASE-07a, through the shared `TechStrap.Hosting` project (D-040); the Portal joins in PHASE-09."

- [ ] **Step 3: Check and commit**

Run `pwsh -File scripts/Invoke-ScriptTests.ps1` (expect PASS; the docs tests read the README links only). Then:

```bash
grep -n "D-040" docs/architecture/*.md docs/development/*.md
grep -n "Admin and Portal reference only Contracts\.\|Still expected from PHASE-02\|D-001 to D-022" docs/architecture/*.md
```

The first grep shows the new text in the decision log, roadmap, PHASE-07, 02-ARCHITECTURE, PHASE-09 and TICKET-OPERATIONS; the second returns no hits.

```bash
git add docs
git diff --cached --stat
git commit -m "docs: record PHASE-07 decisions (D-040) and align specs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 2: The TechStrap.Hosting project

**Files:**
- Create: `src/TechStrap.Hosting/TechStrap.Hosting.csproj`
- Move (`git mv`): `src/TechStrap.Infrastructure/Logging/PiiRedactionEnricher.cs` to `src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs` (namespace `TechStrap.Hosting.Logging`)
- Move (`git mv`): `src/TechStrap.Api/Startup/SensitiveHeaderSentryProcessor.cs` to `src/TechStrap.Hosting/Sentry/SensitiveHeaderSentryProcessor.cs` (namespace `TechStrap.Hosting.Sentry`)
- Create: `src/TechStrap.Hosting/Sentry/SentryOptionsExtensions.cs`
- Modify: `src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj` (drop the Serilog package and, if nothing else needs it, the `TechStrap.Api.Tests` InternalsVisibleTo)
- Modify: `src/TechStrap.Api/TechStrap.Api.csproj`, `src/TechStrap.Worker/TechStrap.Worker.csproj`, `src/TechStrap.Admin/TechStrap.Admin.csproj` (project reference)
- Modify: `src/TechStrap.Api/Program.cs`, `src/TechStrap.Worker/Program.cs`, `src/TechStrap.Admin/Program.cs`
- Modify: `TechStrap.slnx`, `TechStrap.CI.slnf`
- Modify: `tests/TechStrap.Architecture.Tests/ReferenceRules.cs` and `ProjectReferenceDirectionTests.cs`
- Modify: `tests/TechStrap.Api.Tests/Redaction/LogRedactionTests.cs` and `SensitiveHeaderSentryProcessorTests.cs` (usings)
- Create: `tests/TechStrap.Api.Tests/SentryOptionsExtensionsTests.cs` and `tests/TechStrap.Api.Tests/Redaction/AdminHostRedactionTests.cs`
- Modify: `scripts/tests/Dockerfiles.Tests.ps1`
- Modify: `docs/architecture/PHASE-07-admin-app.md` (tick one carry-forward line)

**Interfaces:**
- Consumes: the two existing classes, unchanged apart from their namespace; `SyntaxCircus.AspNetCore.Serilog` (`Serilog.ILogEventEnricher` types come from it), `SyntaxCircus.Observability` (brings `Sentry`).
- Produces:

```csharp
namespace TechStrap.Hosting.Logging;
public sealed partial class PiiRedactionEnricher : ILogEventEnricher { /* unchanged: EmailMarker, TokenMarker, HashMarker, FailedMarker, public ctor, internal ctor and RedactText */ }

namespace TechStrap.Hosting.Sentry;
public sealed class SensitiveHeaderSentryProcessor : ISentryEventProcessor, ISentryTransactionProcessor { /* unchanged */ }

public static class SentryOptionsExtensions
{
    /// <summary>Registers SensitiveHeaderSentryProcessor for events and transactions.</summary>
    public static void AddSensitiveHeaderScrubbing(this SentryOptions options);
}
```

**Rules:**
1. **Behaviour does not change.** The two classes move with `git mv` so the history follows, and only the namespace line changes. Their tests keep passing unmodified except for a `using`.
2. **`TechStrap.Hosting` is a leaf.** It references no TechStrap project and only the two `SyntaxCircus` packages, versionless (`Check-PackageVersions.ps1` forbids inline versions). No `FrameworkReference` is needed (verified: the project builds with the two packages alone).
3. **Wiring.** Api and Admin call the redactor in `AddStandardSerilog` and `AddSensitiveHeaderScrubbing()` in the `UseSentry` callback. The Worker serves no request that carries credentials, so it keeps the redactor only, as now. The Portal is not wired here (PHASE-09).
4. **The namespace `TechStrap.Hosting.Sentry` hides nothing.** The files start with `using Sentry;` outside the namespace, which resolves to the package. Do not write `Sentry.` qualified names inside `TechStrap.Hosting.*` namespaces.

- [ ] **Step 1: Write the failing tests**

1. `tests/TechStrap.Architecture.Tests/ReferenceRules.cs`: add the constant and the entries (the constant after `Contracts`, the entries as shown). The existing hosts may reference Hosting; Infrastructure may not.

```csharp
public const string Hosting = "TechStrap.Hosting";
// ...
[Contracts] = [],
[Hosting] = [],
[Application] = [Domain, Contracts],
[Infrastructure] = [Application, Domain, Contracts],
[Api] = [Application, Infrastructure, Contracts, Hosting],
[Worker] = [Application, Infrastructure, Contracts, Hosting],
[Admin] = [Contracts, Hosting],
[Portal] = [Contracts, Hosting],
```

2. `tests/TechStrap.Architecture.Tests/ProjectReferenceDirectionTests.cs`: rename `Solution_contains_the_ten_source_projects` to `Solution_contains_the_eleven_source_projects`, replace `Admin_and_Portal_reference_only_Contracts`, and add the rule tests:

```csharp
[Fact]
public void Admin_and_Portal_reference_only_Contracts_and_Hosting()
{
    var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

    // Admin signs agents in and logs, so it uses the shared Hosting helpers; Portal joins in PHASE-09. Neither may ever see Application, Infrastructure or Domain.
    graph[ReferenceRules.Admin].ProjectReferences.Order().ShouldBe([ReferenceRules.Contracts, ReferenceRules.Hosting]);
    graph[ReferenceRules.Portal].ProjectReferences.ShouldContain(ReferenceRules.Contracts);
    graph[ReferenceRules.Portal].ProjectReferences.Except([ReferenceRules.Contracts, ReferenceRules.Hosting]).ShouldBeEmpty();
}

[Fact]
public void Hosting_references_no_TechStrap_project_and_only_the_logging_and_telemetry_packages()
{
    var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

    graph[ReferenceRules.Hosting].ProjectReferences.ShouldBeEmpty();
    graph[ReferenceRules.Hosting].PackageReferences.Order().ShouldBe(["SyntaxCircus.AspNetCore.Serilog", "SyntaxCircus.Observability"]);
}

[Fact]
public void Infrastructure_no_longer_references_a_logging_package()
{
    var graph = ProjectGraph.LoadSourceProjects(ProjectGraph.FindRepositoryRoot());

    graph[ReferenceRules.Infrastructure].PackageReferences.ShouldNotContain("SyntaxCircus.AspNetCore.Serilog");
}

[Fact]
public void Rules_flag_Hosting_referencing_a_project_and_Infrastructure_referencing_Hosting()
{
    var graph = new Dictionary<string, ProjectNode>
    {
        [ReferenceRules.Hosting] = Node(ReferenceRules.Hosting, projects: [ReferenceRules.Contracts]),
        [ReferenceRules.Infrastructure] = Node(ReferenceRules.Infrastructure, projects: [ReferenceRules.Hosting]),
    };

    var violations = ReferenceRules.Evaluate(graph);

    violations.ShouldContain($"{ReferenceRules.Hosting} must not reference {ReferenceRules.Contracts}.");
    violations.ShouldContain($"{ReferenceRules.Infrastructure} must not reference {ReferenceRules.Hosting}.");
}
```

3. `tests/TechStrap.Api.Tests/Redaction/LogRedactionTests.cs`: change `using TechStrap.Infrastructure.Logging;` to `using TechStrap.Hosting.Logging;`. `tests/TechStrap.Api.Tests/SensitiveHeaderSentryProcessorTests.cs`: change `using TechStrap.Api.Startup;` to `using TechStrap.Hosting.Sentry;`.

4. New `tests/TechStrap.Api.Tests/SentryOptionsExtensionsTests.cs`:

```csharp
using Sentry;
using TechStrap.Hosting.Sentry;

namespace TechStrap.Api.Tests;

public sealed class SentryOptionsExtensionsTests
{
    [Fact]
    public void Scrubbing_registers_the_header_processor_for_events_and_for_transactions()
    {
        var options = new SentryOptions();

        options.AddSensitiveHeaderScrubbing();

        options.GetAllEventProcessors().OfType<SensitiveHeaderSentryProcessor>().ShouldHaveSingleItem();
        options.GetAllTransactionProcessors().OfType<SensitiveHeaderSentryProcessor>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Scrubbing_needs_options()
    {
        Should.Throw<ArgumentNullException>(() => SentryOptionsExtensions.AddSensitiveHeaderScrubbing(null!));
    }
}
```

5. New `tests/TechStrap.Api.Tests/Redaction/AdminHostRedactionTests.cs` (it uses the existing Api.Tests `AdminFactory`, which Task 3 later extends):

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TechStrap.Api.Tests.Redaction;

/// <summary>The Admin handles requester data from PHASE-07 on (ticket subjects, names, emails), so its logs are redacted like the Api's and the Worker's (D-039, D-040).</summary>
public sealed class AdminHostRedactionTests
{
    private const string Token = "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE";   // exactly 43 base64url characters
    private static readonly string Hash = "sha256:" + new string('a', 64);

    [Fact]
    public async Task The_admin_host_redacts_what_application_code_logs()
    {
        await using var factory = new AdminFactory();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RedactionProbe");

        logger.LogWarning("Probe {Email} {Token} {Hash}", "ada@example.com", Token, Hash);

        var probe = factory.LogSink.Events.Single(e => e.MessageTemplate.Text.StartsWith("Probe ", StringComparison.Ordinal));
        probe.RenderMessage().ShouldBe("Probe \"[email]\" \"[token]\" \"[hash]\"");
    }
}
```

6. `scripts/tests/Dockerfiles.Tests.ps1`: the clean-publish test copies only `src/$Project` and `src/TechStrap.Contracts`, so it breaks as soon as Admin references Hosting. Replace the hard-coded list with the project's reference closure. In the top-level `BeforeAll`, after `Get-DockerfileText`, add:

```powershell
    # Every TechStrap project the host needs to build: itself plus its ProjectReference closure (Admin: Contracts and Hosting).
    function Get-ProjectReferenceClosure {
        param([string]$Project)
        $seen = [System.Collections.Generic.HashSet[string]]::new()
        $queue = [System.Collections.Generic.Queue[string]]::new()
        $queue.Enqueue($Project)
        while ($queue.Count -gt 0) {
            $name = $queue.Dequeue()
            if (-not $seen.Add($name)) { continue }
            $text = Get-Content -LiteralPath (Join-Path $script:RepoRoot "src/$name/$name.csproj") -Raw
            foreach ($match in [regex]::Matches($text, '<ProjectReference Include="\.\./(?<name>[^/]+)/')) {
                $queue.Enqueue($match.Groups['name'].Value)
            }
        }
        return @($seen | Sort-Object)
    }
```

   Add a fast, network-free test before the `Describe 'clean publish serves the self-hosted fonts'` block:

```powershell
Describe 'clean publish copy list' {
    It '<Project> copies itself and every project it references, and no other TechStrap project' -ForEach @(
        @{ Project = 'TechStrap.Admin'; Expected = @('TechStrap.Admin', 'TechStrap.Contracts', 'TechStrap.Hosting') }
        @{ Project = 'TechStrap.Portal'; Expected = @('TechStrap.Contracts', 'TechStrap.Portal') }
    ) {
        Get-ProjectReferenceClosure -Project $Project | Should -Be $Expected
    }
}
```

   And in the network test replace `foreach ($dir in '.config', 'eng', 'assets/brand/scss', "src/$Project", 'src/TechStrap.Contracts') {` with

```powershell
            $projectDirs = Get-ProjectReferenceClosure -Project $Project | ForEach-Object { "src/$_" }
            foreach ($dir in @('.config', 'eng', 'assets/brand/scss') + $projectDirs) {
```

   and the inner `foreach ($dir in "src/$Project", 'src/TechStrap.Contracts') {` with `foreach ($dir in $projectDirs) {`.

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test --project tests/TechStrap.Architecture.Tests -c Release --filter ProjectReferenceDirectionTests
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "SentryOptionsExtensionsTests|AdminHostRedactionTests"
pwsh -NoProfile -Command "Import-Module Pester -RequiredVersion 6.2.0; Invoke-Pester -Path scripts/tests/Dockerfiles.Tests.ps1 -ExcludeTagFilter Network -Output Minimal"
```

Expected: the architecture tests fail with `TechStrap.Hosting: project is missing from src/.`; Api.Tests does not build (`TechStrap.Hosting` does not exist); the Pester test for `TechStrap.Admin` fails (the closure lacks `TechStrap.Hosting`).

- [ ] **Step 3: Create the project and move the files**

1. `src/TechStrap.Hosting/TechStrap.Hosting.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="SyntaxCircus.AspNetCore.Serilog" />
    <PackageReference Include="SyntaxCircus.Observability" />
  </ItemGroup>

  <ItemGroup>
    <!-- LogRedactionTests construct PiiRedactionEnricher through its internal failure seam (a throwing text redactor) to prove redaction fails closed. -->
    <InternalsVisibleTo Include="TechStrap.Api.Tests" />
  </ItemGroup>

</Project>
```

2. Move and re-namespace:

```bash
mkdir -p src/TechStrap.Hosting/Logging src/TechStrap.Hosting/Sentry
git mv src/TechStrap.Infrastructure/Logging/PiiRedactionEnricher.cs src/TechStrap.Hosting/Logging/PiiRedactionEnricher.cs
git mv src/TechStrap.Api/Startup/SensitiveHeaderSentryProcessor.cs src/TechStrap.Hosting/Sentry/SensitiveHeaderSentryProcessor.cs
```

   In the first file change `namespace TechStrap.Infrastructure.Logging;` to `namespace TechStrap.Hosting.Logging;`. In the second change `namespace TechStrap.Api.Startup;` to `namespace TechStrap.Hosting.Sentry;`. Change nothing else in either file.

3. `src/TechStrap.Hosting/Sentry/SentryOptionsExtensions.cs`:

```csharp
using Sentry;

namespace TechStrap.Hosting.Sentry;

public static class SentryOptionsExtensions
{
    /// <summary>
    /// Registers <see cref="SensitiveHeaderSentryProcessor"/> for events and transactions, so no host forgets one of the two. Call it from the
    /// <c>UseSentry</c> callback of every host that serves requests (Api, Admin, and Portal from PHASE-09).
    /// </summary>
    public static void AddSensitiveHeaderScrubbing(this SentryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var processor = new SensitiveHeaderSentryProcessor();
        options.AddEventProcessor(processor);
        options.AddTransactionProcessor(processor);
    }
}
```

4. `src/TechStrap.Infrastructure/TechStrap.Infrastructure.csproj`: delete the line `<PackageReference Include="SyntaxCircus.AspNetCore.Serilog" />` and the `TechStrap.Api.Tests` InternalsVisibleTo entry with its comment (the comment says it exists only for `LogRedactionTests`). **Check:** build Api.Tests (Step 5). If it reports `CS0122` for some other Infrastructure internal, put the entry back with a comment naming that use, and keep the Serilog removal.

5. Project references: add `<ProjectReference Include="../TechStrap.Hosting/TechStrap.Hosting.csproj" />` to the `Api`, `Worker` and `Admin` csproj files (after the Contracts reference).

6. `src/TechStrap.Api/Program.cs`: replace `using TechStrap.Infrastructure.Logging;` by `using TechStrap.Hosting.Logging;` and add `using TechStrap.Hosting.Sentry;` (keep the usings alphabetical). Replace the three processor lines in the `UseSentry` callback:

```csharp
        var headerProcessor = new SensitiveHeaderSentryProcessor();
        options.AddEventProcessor(headerProcessor);
        options.AddTransactionProcessor(headerProcessor);
```

   with `        options.AddSensitiveHeaderScrubbing();`.

7. `src/TechStrap.Worker/Program.cs`: replace `using TechStrap.Infrastructure.Logging;` by `using TechStrap.Hosting.Logging;`.

8. `src/TechStrap.Admin/Program.cs`: add `using TechStrap.Hosting.Logging;` and `using TechStrap.Hosting.Sentry;`, replace

```csharp
builder.AddStandardSerilog(configureEnrichment: telemetry.ConfigureSerilog);
```

   with

```csharp
builder.AddStandardSerilog(configureEnrichment: logger =>
{
    telemetry.ConfigureSerilog(logger);
    logger.Enrich.With<PiiRedactionEnricher>();
});
```

   and add `        options.AddSensitiveHeaderScrubbing();` before `options.AutoSessionTracking = false;` in the `UseSentry` callback.

9. `TechStrap.slnx`: add `<Project Path="src/TechStrap.Hosting/TechStrap.Hosting.csproj" />` after the Domain line in the `/src/` folder. `TechStrap.CI.slnf`: add `"src/TechStrap.Hosting/TechStrap.Hosting.csproj",` after the Domain entry.

- [ ] **Step 4: Tick the carry-forward**

In `docs/architecture/PHASE-07-admin-app.md` change `- [ ] Carried forward from PHASE-06c (D-039): wire `PiiRedactionEnricher` into the Admin host's `AddStandardSerilog` call once the Admin handles requester data.` to `- [x] ... (done in PHASE-07a through TechStrap.Hosting, D-040; the Sentry header scrub is wired too).`

- [ ] **Step 5: Run the tests**

```bash
dotnet build TechStrap.slnx -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "SentryOptionsExtensionsTests|AdminHostRedactionTests|SensitiveHeaderSentryProcessorTests|LogRedactionTests"
pwsh -NoProfile -Command "Import-Module Pester -RequiredVersion 6.2.0; Invoke-Pester -Path scripts/tests/Dockerfiles.Tests.ps1 -Output Minimal"
pwsh -File scripts/Check-PackageVersions.ps1
```

Expected: PASS and a build with 0 warnings. The Pester run includes the network-tagged clean publish (it needs network for libman, like the Docker build); add `-ExcludeTagFilter Network` when offline. `LogRedactionTests` need Docker for their Postgres container; if Docker is not running, run the other filters and leave that class to CI.

- [ ] **Step 6: Commit**

```bash
git add src tests scripts TechStrap.slnx TechStrap.CI.slnf docs
git diff --cached --stat
git commit -m "refactor(hosting): move the log redactor and Sentry scrubber to TechStrap.Hosting; wire the Admin" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 3: Admin options and configuration (P07-T01)

**Files:**
- Modify: `src/TechStrap.Admin/TechStrap.Admin.csproj` (package, InternalsVisibleTo)
- Create: `src/TechStrap.Admin/Options/AgentGroupOptions.cs`, `AdminOptionsValidators.cs`, `AdminOptionsRegistration.cs`
- Modify: `src/TechStrap.Admin/Program.cs`
- Modify: `src/TechStrap.Admin/.env.example`
- Modify: `docker-compose.yml`, `docker-compose.uat.yml`, `docker-compose.production.yml` (the `admin` service)
- Modify: `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs` (the Admin row)
- Modify: `scripts/tests/ComposeFiles.Tests.ps1`
- Create: `tests/Shared/AdminHost/AdminTestSettings.cs` and `tests/Shared/AdminHost/StubApiHandler.cs` (shared by both Admin test hosts)
- Modify: `tests/TechStrap.Admin.Tests/AdminFactory.cs`, `tests/TechStrap.Admin.Tests/TechStrap.Admin.Tests.csproj`
- Modify: `tests/TechStrap.Api.Tests/HostFactory.cs` (the `AdminFactory` class), `tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj`
- Create: `tests/TechStrap.Admin.Tests/Options/AdminOptionsTests.cs`, `tests/TechStrap.Admin.Tests/AdminStartupSettingsTests.cs`

**Interfaces:**
- Consumes: `SyntaxCircus.Blazor.Auth` `AuthOptions` (section `Auth`: `Authority`, `ClientId`, `ClientSecret`, `Scopes`; env `AUTH__AUTHORITY`, `AUTH__CLIENTID`, `AUTH__CLIENTSECRET`) and `ApiOptions` (section `Api`: `BaseUrl`, `TimeoutSeconds`; env `API__BASEURL`, `API__TIMEOUTSECONDS`), both bound by `AddBlazorTokenForwarding(configuration, "Auth")` (verified in the decompiled package: it binds `AuthOptions` from the given section and `ApiOptions` from `Api`, and registers `IBlazorCircuitHttpClientFactory`, `ApiAuthHandler`, `IServerTokenCache` and the token services).
- Produces:

```csharp
namespace TechStrap.Admin.Options;

public sealed class AgentGroupOptions
{
    public const string AgentGroupKey = "TECHSTRAP_AGENT_GROUP";
    public const string AdminGroupKey = "TECHSTRAP_ADMIN_GROUP";
    public const string GroupClaimTypeKey = "TECHSTRAP_GROUP_CLAIM_TYPE";
    public const string DefaultAgentGroup = "techstrap-agents";
    public const string DefaultAdminGroup = "techstrap-admins";
    public const string DefaultGroupClaimType = "groups";
    public string AgentGroup { get; set; }       // default DefaultAgentGroup
    public string AdminGroup { get; set; }       // default DefaultAdminGroup
    public string GroupClaimType { get; set; }   // default DefaultGroupClaimType
}

public static class AdminOptionsRegistration
{
    public const string AuthSection = "Auth";
    public static IServiceCollection AddAdminOptions(this IServiceCollection services, IConfiguration configuration);
}

internal sealed class AdminAuthOptionsValidator(IHostEnvironment environment) : IValidateOptions<AuthOptions>;
internal sealed class AdminApiOptionsValidator : IValidateOptions<ApiOptions>;   // const MaxTimeoutSeconds = 300
internal sealed class AgentGroupOptionsValidator : IValidateOptions<AgentGroupOptions>;
```

  Test helpers (namespace `TechStrap.Tests.Shared.AdminHost`, compiled into both `TechStrap.Admin.Tests` and `TechStrap.Api.Tests` by a linked `Compile Include`; all members public so the public Api.Tests `AdminFactory` can expose them):

```csharp
public static class AdminTestSettings
{
    public const string Authority = "https://idp.test/application/o/techstrap-admin/";
    public const string ApiBaseUrl = "http://api.test/";
    public static IReadOnlyDictionary<string, string?> Required { get; }   // Auth:Authority, Auth:ClientId, Auth:ClientSecret, Api:BaseUrl
    public static Dictionary<string, string?> With(IReadOnlyDictionary<string, string?>? overrides = null);   // a null value BLANKS the key
}

public sealed record StubApiRequest(HttpMethod Method, string Path, string Query, string? Authorization, string? ContentType, string? Body);

public sealed class StubApiHandler : HttpMessageHandler
{
    public IReadOnlyList<StubApiRequest> Requests { get; }
    public int Count(HttpMethod method, string path);
    public StubApiHandler On(HttpMethod method, string path, Func<StubApiRequest, HttpResponseMessage> respond);
    public StubApiHandler OnJson<T>(HttpMethod method, string path, T body, HttpStatusCode status = HttpStatusCode.OK);
    public StubApiHandler OnStatus(HttpMethod method, string path, HttpStatusCode status);
    public StubApiHandler OnProblem(HttpMethod method, string path, HttpStatusCode status, string type, string detail);
    public static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body);
    public static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail);
}

// tests/TechStrap.Admin.Tests (internal sealed)
internal sealed class AdminFactory(string environment = "Development", IReadOnlyDictionary<string, string?>? settings = null, Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TechStrap.Admin.Program>
{
    public StubApiHandler Api { get; }
}

// tests/TechStrap.Api.Tests (public sealed): same constructor shape, same Api property
public sealed class AdminFactory : HostFactory<TechStrap.Admin.Program> { public StubApiHandler Api { get; } }
```

**Rules:**
1. **Nothing is read eagerly in `Program`.** The options are resolved lazily and `ValidateOnStart` checks them when the host starts. The Api tests' `HostFactory` applies settings through `ConfigureAppConfiguration`, which runs after `Program` has executed, so an eager read would see nothing. (`AddTrustedProxyForwardedHeaders` is the one eager read that exists, and it is why the factories set a `TRUSTEDPROXY__` environment variable.)
2. **Required keys:** `Auth:Authority` (absolute http or https URL; http only in Development), `Auth:ClientId`, `Auth:ClientSecret`, `Api:BaseUrl` (absolute URL), `Api:TimeoutSeconds` (1 to 300, default 30). `Auth:Scopes` must keep `openid` and `offline_access`. Every failure message names the environment variable.
3. **Group keys.** They are the API's flat names and defaults, trimmed, absent means default, present but blank is an error, and the agent and admin groups must differ (case-insensitive), the same rules as `AgentAuthenticationSetup`. The Admin never parses group claims (D-040): the names exist so the no-access page can say which group to ask for, and so one env file configures both hosts.
4. **Compose.** Local compose gets placeholder OIDC values (so the container starts without an identity provider; sign-in then fails), uat and production pass the real ones from the env file. Local `.env.local` values for `AUTH__*` would clash with the compose `Auth__*` values: the docs (Task 13) say to put `OIDC_*` in the root `.env` instead.

- [ ] **Step 1: Write the failing tests**

1. `tests/Shared/AdminHost/AdminTestSettings.cs`:

```csharp
namespace TechStrap.Tests.Shared.AdminHost;

/// <summary>The settings an Admin host needs to start (the options are validated on start). Test factories merge their own overrides on top.</summary>
public static class AdminTestSettings
{
    public const string Authority = "https://idp.test/application/o/techstrap-admin/";
    public const string ApiBaseUrl = "http://api.test/";

    public static IReadOnlyDictionary<string, string?> Required { get; } = new Dictionary<string, string?>
    {
        ["Auth:Authority"] = Authority,
        ["Auth:ClientId"] = "techstrap-admin-test",
        ["Auth:ClientSecret"] = "test-client-secret",
        ["Api:BaseUrl"] = ApiBaseUrl,
    };

    /// <summary>
    /// The required settings with <paramref name="overrides"/> applied. A null value blanks the key (rather than dropping it, so a variable set
    /// in the developer's shell cannot fill it in), which lets a test prove a missing setting stops the start.
    /// </summary>
    public static Dictionary<string, string?> With(IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>(Required, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
        {
            settings[key] = value ?? string.Empty;
        }

        return settings;
    }
}
```

2. `tests/Shared/AdminHost/StubApiHandler.cs`. It answers by method and path, records every request, and answers an unconfigured request with 404 `stub-not-configured` so a test fails loudly. Task 4 adds `WithTestAgents()` to it.

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace TechStrap.Tests.Shared.AdminHost;

/// <summary>One request the Admin sent to the stub API.</summary>
public sealed record StubApiRequest(HttpMethod Method, string Path, string Query, string? Authorization, string? ContentType, string? Body);

/// <summary>
/// Stands in for the TechStrap API behind the Admin's named HTTP clients (it replaces their primary handler, so the real handler pipeline above it
/// still runs: the auth handler adds the bearer token, the resilience handler retries). It records every request and answers by method and path;
/// an unconfigured request answers 404 with the problem code "stub-not-configured" so a test fails loudly.
/// </summary>
public sealed class StubApiHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly List<StubApiRequest> _requests = [];
    private readonly List<(HttpMethod Method, string Path, Func<StubApiRequest, HttpResponseMessage> Respond)> _routes = [];
    private readonly object _gate = new();

    /// <summary>Every request received so far, in order.</summary>
    public IReadOnlyList<StubApiRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>How many requests with this method and path (any query) were received.</summary>
    public int Count(HttpMethod method, string path) => Requests.Count(r => r.Method == method && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Answers requests for <paramref name="path"/> (path only, no query) with whatever <paramref name="respond"/> returns. A later route for the same method and path replaces an earlier one.</summary>
    public StubApiHandler On(HttpMethod method, string path, Func<StubApiRequest, HttpResponseMessage> respond)
    {
        lock (_gate)
        {
            _routes.RemoveAll(r => r.Method == method && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
            _routes.Add((method, path, respond));
        }

        return this;
    }

    public StubApiHandler OnJson<T>(HttpMethod method, string path, T body, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, path, _ => JsonResponse(status, body));

    public StubApiHandler OnStatus(HttpMethod method, string path, HttpStatusCode status) => On(method, path, _ => new HttpResponseMessage(status));

    /// <summary>An RFC 7807 answer in the shape the API produces: <c>type</c> is the error code, <c>detail</c> the message.</summary>
    public StubApiHandler OnProblem(HttpMethod method, string path, HttpStatusCode status, string type, string detail) =>
        On(method, path, _ => Problem(status, type, detail));

    public static HttpResponseMessage JsonResponse<T>(HttpStatusCode status, T body) => new(status) { Content = JsonContent.Create(body, options: Json) };

    public static HttpResponseMessage Problem(HttpStatusCode status, string type, string detail) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { type, title = status.ToString(), status = (int)status, detail }, Json),
            Encoding.UTF8,
            "application/problem+json"),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var seen = new StubApiRequest(
            request.Method,
            request.RequestUri!.AbsolutePath,
            request.RequestUri.Query,
            request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentType?.ToString(),
            body);

        Func<StubApiRequest, HttpResponseMessage>? respond;
        lock (_gate)
        {
            _requests.Add(seen);
            respond = _routes.FirstOrDefault(r => r.Method == seen.Method && string.Equals(r.Path, seen.Path, StringComparison.OrdinalIgnoreCase)).Respond;
        }

        return respond is null ? Problem(HttpStatusCode.NotFound, "stub-not-configured", $"{seen.Method} {seen.Path} is not configured in the stub API.") : respond(seen);
    }
}
```

3. Link the helpers into both test projects. In `tests/TechStrap.Admin.Tests/TechStrap.Admin.Tests.csproj` add to the `Compile` item group:

```xml
    <Compile Include="../Shared/AdminHost/*.cs" LinkBase="Shared/AdminHost" />
```

   In `tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj` add an item group before the package references:

```xml
  <ItemGroup>
    <!-- The Admin host test helpers (settings, stub API, Test sign-in) are shared with TechStrap.Admin.Tests. -->
    <Compile Include="../Shared/AdminHost/*.cs" LinkBase="Shared/AdminHost" />
  </ItemGroup>
```

4. `tests/TechStrap.Admin.Tests/AdminFactory.cs` (replace the file):

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// Starts the Admin host in-process in the given environment. A developer's gitignored .env.local must never leak into tests, and Production needs a trusted network to start.
/// It supplies the settings the options validation requires (<see cref="AdminTestSettings"/>, with <c>settings</c> applied on top) and a stub API (<see cref="Api"/>).
/// </summary>
internal sealed class AdminFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<TechStrap.Admin.Program>
{
    static AdminFactory()
    {
        Environment.SetEnvironmentVariable("DotEnv__Enabled", "false");

        // Production refuses to start without trusted proxies, and that option is bound before the factory can override it.
        Environment.SetEnvironmentVariable("TRUSTEDPROXY__TRUSTEDNETWORKS__0", "192.0.2.0/24");
    }

    /// <summary>The stub behind the Admin's API clients. The tasks that add the clients and the sign-in wire it in.</summary>
    public StubApiHandler Api { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(AdminTestSettings.With(settings)));
        builder.ConfigureServices(services => configureServices?.Invoke(services));
    }
}
```

5. `tests/TechStrap.Api.Tests/HostFactory.cs`: replace the `AdminFactory` class at the bottom (`public sealed class AdminFactory(string environment = "Development") : HostFactory<TechStrap.Admin.Program>(environment);`) with the version below, and add `using TechStrap.Tests.Shared.AdminHost;`. It is not a primary-constructor class because the stub must be created before the base constructor call.

```csharp
/// <summary>
/// The Admin host with the settings its options validation requires (<see cref="AdminTestSettings"/>, with <c>settings</c> applied on top) and a stub API behind its
/// named HTTP clients (<see cref="Api"/>).
/// </summary>
public sealed class AdminFactory : HostFactory<TechStrap.Admin.Program>
{
    public AdminFactory(
        string environment = "Development",
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
        : this(environment, settings, configureServices, new StubApiHandler())
    {
    }

    private AdminFactory(
        string environment,
        IReadOnlyDictionary<string, string?>? settings,
        Action<IServiceCollection>? configureServices,
        StubApiHandler api)
        : base(environment, AdminTestSettings.With(settings), services => configureServices?.Invoke(services)) => Api = api;

    /// <summary>The stub behind the Admin's API clients. The tasks that add the clients and the sign-in wire it in.</summary>
    public StubApiHandler Api { get; }
}
```

6. `tests/TechStrap.Admin.Tests/Options/AdminOptionsTests.cs` (the validators are internal; the Admin csproj gets the InternalsVisibleTo in Step 3):

```csharp
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Options;

namespace TechStrap.Admin.Tests.Options;

public sealed class AdminOptionsTests
{
    private static AuthOptions ValidAuth() => new()
    {
        Authority = "https://auth.example.com/application/o/techstrap-admin/",
        ClientId = "techstrap-admin",
        ClientSecret = "secret",
    };

    private static AdminAuthOptionsValidator Auth(string environment = "Production") => new(new FakeEnvironment(environment));

    [Fact]
    public void A_complete_oidc_configuration_is_valid() => Auth().Validate(null, ValidAuth()).Succeeded.ShouldBeTrue();

    [Theory]
    [InlineData("", "AUTH__AUTHORITY")]
    [InlineData("not a url", "AUTH__AUTHORITY")]
    [InlineData("/relative/path", "AUTH__AUTHORITY")]
    [InlineData("ftp://auth.example.com/", "AUTH__AUTHORITY")]
    public void A_missing_or_malformed_authority_names_its_variable(string authority, string variable)
    {
        var options = ValidAuth();
        options.Authority = authority;

        var result = Auth().Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage!.ShouldContain(variable);
    }

    [Fact]
    public void An_http_authority_is_allowed_in_development_only()
    {
        var options = ValidAuth();
        options.Authority = "http://localhost:9000/application/o/techstrap-admin/";

        Auth("Development").Validate(null, options).Succeeded.ShouldBeTrue();
        Auth("Production").Validate(null, options).FailureMessage!.ShouldContain("https");
    }

    [Theory]
    [InlineData(nameof(AuthOptions.ClientId), "AUTH__CLIENTID")]
    [InlineData(nameof(AuthOptions.ClientSecret), "AUTH__CLIENTSECRET")]
    public void A_blank_client_setting_names_its_variable(string property, string variable)
    {
        var options = ValidAuth();
        typeof(AuthOptions).GetProperty(property)!.SetValue(options, "  ");

        Auth().Validate(null, options).FailureMessage!.ShouldContain(variable);
    }

    [Fact]
    public void The_scopes_must_keep_openid_and_offline_access_because_the_api_token_is_refreshed_with_the_refresh_token()
    {
        var options = ValidAuth();
        options.Scopes = ["openid", "profile", "email"];

        Auth().Validate(null, options).FailureMessage!.ShouldContain("offline_access");
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("api", false)]
    [InlineData("http://api/", true)]
    [InlineData("https://api.example.com", true)]
    public void The_api_base_url_must_be_absolute(string baseUrl, bool valid)
    {
        var result = new AdminApiOptionsValidator().Validate(null, new ApiOptions { BaseUrl = baseUrl });

        result.Succeeded.ShouldBe(valid);
        if (!valid)
        {
            result.FailureMessage!.ShouldContain("API__BASEURL");
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(30, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void The_api_timeout_is_bounded(int seconds, bool valid) =>
        new AdminApiOptionsValidator().Validate(null, new ApiOptions { BaseUrl = "http://api/", TimeoutSeconds = seconds }).Succeeded.ShouldBe(valid);

    [Fact]
    public void The_group_keys_default_to_the_api_defaults_and_must_differ_and_not_be_blank()
    {
        var validator = new AgentGroupOptionsValidator();

        validator.Validate(null, new AgentGroupOptions()).Succeeded.ShouldBeTrue();
        validator.Validate(null, new AgentGroupOptions { AdminGroup = "TECHSTRAP-AGENTS" }).FailureMessage!.ShouldContain("different groups");
        validator.Validate(null, new AgentGroupOptions { AgentGroup = " " }).FailureMessage!.ShouldContain("TECHSTRAP_AGENT_GROUP");
        validator.Validate(null, new AgentGroupOptions { AdminGroup = "" }).FailureMessage!.ShouldContain("TECHSTRAP_ADMIN_GROUP");
        validator.Validate(null, new AgentGroupOptions { GroupClaimType = "" }).FailureMessage!.ShouldContain("TECHSTRAP_GROUP_CLAIM_TYPE");
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "TechStrap.Admin";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
```

7. `tests/TechStrap.Admin.Tests/AdminStartupSettingsTests.cs`:

```csharp
using Microsoft.Extensions.Options;

namespace TechStrap.Admin.Tests;

/// <summary>The Admin refuses to start with a clear message when a required setting is missing, instead of failing on the first sign-in (P07-T01).</summary>
public sealed class AdminStartupSettingsTests
{
    [Theory]
    [InlineData("Auth:Authority", "AUTH__AUTHORITY")]
    [InlineData("Auth:ClientId", "AUTH__CLIENTID")]
    [InlineData("Auth:ClientSecret", "AUTH__CLIENTSECRET")]
    [InlineData("Api:BaseUrl", "API__BASEURL")]
    public async Task A_missing_required_setting_stops_the_start_and_names_the_variable(string key, string variable)
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { [key] = null });

        var error = Should.Throw<OptionsValidationException>(() => factory.CreateClient());

        error.Message.ShouldContain(variable);
    }

    [Fact]
    public async Task Two_identical_group_names_stop_the_start()
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?>
        {
            ["TECHSTRAP_AGENT_GROUP"] = "staff",
            ["TECHSTRAP_ADMIN_GROUP"] = "Staff",
        });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("different groups");
    }

    [Fact]
    public async Task With_the_required_settings_the_host_starts()
    {
        await using var factory = new AdminFactory();

        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }
}
```

8. `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`: add `using TechStrap.Admin.Options;` and extend the `TechStrap.Admin` row of `Hosts()` (the OIDC and API keys stay as they are; `TECHSTRAP_ADMIN_GROUP` becomes a constant):

```csharp
                "API__BASEURL",
                "API__TIMEOUTSECONDS",
                "AUTH__AUTHORITY",
                "AUTH__CLIENTID",
                "AUTH__CLIENTSECRET",
                AgentGroupOptions.AgentGroupKey,
                AgentGroupOptions.AdminGroupKey,
                AgentGroupOptions.GroupClaimTypeKey,
                "DATAPROTECTION__KEYRINGPATH",
```

9. `scripts/tests/ComposeFiles.Tests.ps1`: add these two tests in the `docker-compose files` block, before the test named `'<file> resolves with the example env file, ...'`:

```powershell
    It '<file> passes the Admin its OIDC client, the API address and the three group keys' -ForEach @(
        @{ file = 'docker-compose.yml'; withEnv = $false }
        @{ file = 'docker-compose.uat.yml'; withEnv = $true }
        @{ file = 'docker-compose.production.yml'; withEnv = $true }
    ) {
        $envFile = ''
        if ($withEnv) {
            $envFile = Join-Path $TestDrive 'env-admin'
            New-ProductionEnvFile -Path $envFile
        }
        $result = Get-ComposeConfig -File $file -EnvFile $envFile
        $result.ExitCode | Should -Be 0
        $names = $result.Config.services.admin.environment.PSObject.Properties.Name
        foreach ($key in 'Auth__Authority', 'Auth__ClientId', 'Auth__ClientSecret', 'Api__BaseUrl', 'TECHSTRAP_AGENT_GROUP', 'TECHSTRAP_ADMIN_GROUP', 'TECHSTRAP_GROUP_CLAIM_TYPE') {
            $names | Should -Contain $key
        }
        $result.Config.services.admin.environment.TECHSTRAP_AGENT_GROUP | Should -Be 'techstrap-agents'
        $result.Config.services.admin.environment.TECHSTRAP_GROUP_CLAIM_TYPE | Should -Be 'groups'
    }

    It 'local compose gives the Admin placeholder OIDC values so the container starts without an identity provider' {
        $admin = (Get-ComposeConfig -File 'docker-compose.yml').Config.services.admin.environment
        $admin.Auth__Authority | Should -Match '^https://'
        $admin.Auth__ClientId | Should -Not -BeNullOrEmpty
        $admin.Auth__ClientSecret | Should -Not -BeNullOrEmpty
    }
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "AdminOptionsTests|AdminStartupSettingsTests"
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter EnvExampleCompletenessTests
pwsh -NoProfile -Command "Import-Module Pester -RequiredVersion 6.2.0; Invoke-Pester -Path scripts/tests/ComposeFiles.Tests.ps1 -Output Minimal"
```

Expected: both .NET projects fail to build (`AgentGroupOptions`, `AdminAuthOptionsValidator` do not exist). The Pester tests fail: the admin service has no `Auth__Authority` in local compose and no `TECHSTRAP_AGENT_GROUP` anywhere. (The Pester tests are skipped when Docker Compose is not installed.)

- [ ] **Step 3: Implement**

1. `src/TechStrap.Admin/TechStrap.Admin.csproj`: add `<PackageReference Include="SyntaxCircus.Blazor.Auth" />` (alphabetical, before `SyntaxCircus.Blazor.Components`) and an item group:

```xml
  <ItemGroup>
    <!-- Internal options validators, clients and view models are unit-tested directly. -->
    <InternalsVisibleTo Include="TechStrap.Admin.Tests" />
  </ItemGroup>
```

2. `src/TechStrap.Admin/Options/AgentGroupOptions.cs`:

```csharp
namespace TechStrap.Admin.Options;

/// <summary>
/// The agent and admin group names and the group claim type, under the same flat keys and defaults the API reads (AgentAccessOptions), so one
/// env file configures both hosts. Admin never parses group claims: access comes from <c>GET /api/agents/me</c> (D-040). The names exist
/// so the no-access page can say which group to ask for, and so a mismatch between the two hosts fails at start rather than in production.
/// </summary>
public sealed class AgentGroupOptions
{
    public const string AgentGroupKey = "TECHSTRAP_AGENT_GROUP";
    public const string AdminGroupKey = "TECHSTRAP_ADMIN_GROUP";
    public const string GroupClaimTypeKey = "TECHSTRAP_GROUP_CLAIM_TYPE";

    public const string DefaultAgentGroup = "techstrap-agents";
    public const string DefaultAdminGroup = "techstrap-admins";
    public const string DefaultGroupClaimType = "groups";

    public string AgentGroup { get; set; } = DefaultAgentGroup;

    public string AdminGroup { get; set; } = DefaultAdminGroup;

    public string GroupClaimType { get; set; } = DefaultGroupClaimType;
}
```

3. `src/TechStrap.Admin/Options/AdminOptionsValidators.cs`:

```csharp
using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Auth;

namespace TechStrap.Admin.Options;

/// <summary>Fails the start when the OIDC settings are missing or malformed. Messages name the environment variable so the operator knows what to set.</summary>
internal sealed class AdminAuthOptionsValidator(IHostEnvironment environment) : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) || (authority.Scheme != Uri.UriSchemeHttps && authority.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Auth:Authority (AUTH__AUTHORITY) must be the absolute URL of the OIDC provider, for example https://auth.example.com/application/o/techstrap-admin/.");
        }
        else if (authority.Scheme == Uri.UriSchemeHttp && !environment.IsDevelopment())
        {
            failures.Add("Auth:Authority (AUTH__AUTHORITY) must use https outside Development.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("Auth:ClientId (AUTH__CLIENTID) is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add("Auth:ClientSecret (AUTH__CLIENTSECRET) is required: the Admin OIDC client is confidential.");
        }

        if (!options.Scopes.Contains("openid", StringComparer.Ordinal) || !options.Scopes.Contains("offline_access", StringComparer.Ordinal))
        {
            failures.Add("Auth:Scopes must include openid and offline_access: the API token is refreshed with the refresh token.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>The API base address and timeout (the Blazor.Auth ApiOptions, section "Api", env API__BASEURL).</summary>
internal sealed class AdminApiOptionsValidator : IValidateOptions<ApiOptions>
{
    public const int MaxTimeoutSeconds = 300;

    public ValidateOptionsResult Validate(string? name, ApiOptions options)
    {
        var failures = new List<string>();

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl) || (baseUrl.Scheme != Uri.UriSchemeHttps && baseUrl.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("Api:BaseUrl (API__BASEURL) must be the absolute URL of the TechStrap API, for example http://api/.");
        }

        if (options.TimeoutSeconds is < 1 or > MaxTimeoutSeconds)
        {
            failures.Add($"Api:TimeoutSeconds (API__TIMEOUTSECONDS) must be between 1 and {MaxTimeoutSeconds}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>The same rules the API applies to its group keys (AgentAuthenticationSetup): none blank, and the two groups differ.</summary>
internal sealed class AgentGroupOptionsValidator : IValidateOptions<AgentGroupOptions>
{
    public ValidateOptionsResult Validate(string? name, AgentGroupOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AgentGroup))
        {
            failures.Add($"{AgentGroupOptions.AgentGroupKey} must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(options.AdminGroup))
        {
            failures.Add($"{AgentGroupOptions.AdminGroupKey} must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(options.GroupClaimType))
        {
            failures.Add($"{AgentGroupOptions.GroupClaimTypeKey} must not be blank.");
        }

        if (string.Equals(options.AgentGroup, options.AdminGroup, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{AgentGroupOptions.AgentGroupKey} and {AgentGroupOptions.AdminGroupKey} must name different groups.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
```

4. `src/TechStrap.Admin/Options/AdminOptionsRegistration.cs`:

```csharp
using Microsoft.Extensions.Options;
using SyntaxCircus.Blazor.Auth;

namespace TechStrap.Admin.Options;

public static class AdminOptionsRegistration
{
    /// <summary>The configuration section the OIDC settings live in. The existing env keys are AUTH__AUTHORITY, AUTH__CLIENTID and AUTH__CLIENTSECRET.</summary>
    public const string AuthSection = "Auth";

    /// <summary>
    /// Registers and validates the Admin options. Nothing is read here: every option is resolved lazily and <c>ValidateOnStart</c> checks it when the
    /// host starts, so a test factory can supply the settings through in-memory configuration. <c>AddBlazorTokenForwarding</c> binds
    /// <see cref="AuthOptions"/> (section <see cref="AuthSection"/>) and <see cref="ApiOptions"/> (section "Api"); this only adds the rules.
    /// </summary>
    public static IServiceCollection AddAdminOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<AuthOptions>, AdminAuthOptionsValidator>();
        services.AddSingleton<IValidateOptions<ApiOptions>, AdminApiOptionsValidator>();
        services.AddSingleton<IValidateOptions<AgentGroupOptions>, AgentGroupOptionsValidator>();

        services.AddOptions<AuthOptions>().ValidateOnStart();
        services.AddOptions<ApiOptions>().ValidateOnStart();
        services.AddOptions<AgentGroupOptions>()
            .Configure(options =>
            {
                options.AgentGroup = Read(configuration, AgentGroupOptions.AgentGroupKey, AgentGroupOptions.DefaultAgentGroup);
                options.AdminGroup = Read(configuration, AgentGroupOptions.AdminGroupKey, AgentGroupOptions.DefaultAdminGroup);
                options.GroupClaimType = Read(configuration, AgentGroupOptions.GroupClaimTypeKey, AgentGroupOptions.DefaultGroupClaimType);
            })
            .ValidateOnStart();
        return services;
    }

    // A key that is present but blank stays blank (and fails validation), exactly like the API; only an absent key takes the default.
    private static string Read(IConfiguration configuration, string key, string fallback) => configuration[key]?.Trim() ?? fallback;
}
```

5. `src/TechStrap.Admin/Program.cs`: add `using SyntaxCircus.Blazor.Auth;` and `using TechStrap.Admin.Options;`, and before `builder.Services.AddRazorComponents()` add:

```csharp
// Required settings are validated when the host starts (not read here), so a missing Auth or Api key stops the start with a clear message.
builder.Services.AddAdminOptions(builder.Configuration);
builder.Services.AddBlazorTokenForwarding(builder.Configuration, AdminOptionsRegistration.AuthSection);
```

6. `src/TechStrap.Admin/.env.example`: replace the `# --- API ---` and `# --- Agent sign-in ... ---` blocks (down to and including `TECHSTRAP_ADMIN_GROUP=techstrap-admins`) with:

```
# --- API ---
# Required. The Admin calls the API with the signed-in agent's token. A request that takes longer than the timeout fails.
API__BASEURL=http://localhost:8080/
# API__TIMEOUTSECONDS=30

# --- Agent sign-in (OpenID Connect code flow with PKCE, any provider; docs/development/ADMIN-APP.md) ---
# Required: the Admin refuses to start without all three. A confidential client with redirect URI {admin url}/signin-oidc,
# post-logout URI {admin url}/signout-callback-oidc and the scopes openid profile email offline_access.
AUTH__AUTHORITY=
AUTH__CLIENTID=
AUTH__CLIENTSECRET=

# --- Agent and admin groups (the same keys and defaults as the API; the API decides who has access) ---
TECHSTRAP_AGENT_GROUP=techstrap-agents
TECHSTRAP_ADMIN_GROUP=techstrap-admins
TECHSTRAP_GROUP_CLAIM_TYPE=groups
```

7. `docker-compose.yml`, the `admin` service `environment`: after `DataProtection__KeyRingPath: /app/dataprotection-keys` add

```yaml
      # The Admin will not start without an OIDC client. These placeholders let the container start without an identity provider;
      # signing in then fails until OIDC_AUTHORITY, OIDC_ADMIN_CLIENT_ID and OIDC_ADMIN_CLIENT_SECRET are set in the root .env (docs/development/ADMIN-APP.md).
      Auth__Authority: ${OIDC_AUTHORITY:-https://authentik.invalid/application/o/techstrap-admin/}
      Auth__ClientId: ${OIDC_ADMIN_CLIENT_ID:-techstrap-admin}
      Auth__ClientSecret: ${OIDC_ADMIN_CLIENT_SECRET:-not-configured}
      TECHSTRAP_AGENT_GROUP: ${TECHSTRAP_AGENT_GROUP:-techstrap-agents}
      TECHSTRAP_ADMIN_GROUP: ${TECHSTRAP_ADMIN_GROUP:-techstrap-admins}
      TECHSTRAP_GROUP_CLAIM_TYPE: ${TECHSTRAP_GROUP_CLAIM_TYPE:-groups}
```

   `docker-compose.uat.yml` and `docker-compose.production.yml`, the `admin` service: the `Auth__*` lines and `TECHSTRAP_ADMIN_GROUP` already exist. Insert before `TECHSTRAP_ADMIN_GROUP` and after it:

```yaml
      TECHSTRAP_AGENT_GROUP: ${TECHSTRAP_AGENT_GROUP:-techstrap-agents}
      TECHSTRAP_ADMIN_GROUP: ${TECHSTRAP_ADMIN_GROUP:-techstrap-admins}
      TECHSTRAP_GROUP_CLAIM_TYPE: ${TECHSTRAP_GROUP_CLAIM_TYPE:-groups}
```

   (replace the one existing `TECHSTRAP_ADMIN_GROUP` line by these three). `.env.production.example` already lists `OIDC_AUTHORITY`, `OIDC_ADMIN_CLIENT_ID`, `OIDC_ADMIN_CLIENT_SECRET` and the three group keys.

- [ ] **Step 4: Run the tests**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "EnvExampleCompletenessTests|AdminHostRedactionTests|Admin_reports_live_only|ShellHostTests"
pwsh -NoProfile -Command "Import-Module Pester -RequiredVersion 6.2.0; Invoke-Pester -Path scripts/tests/ComposeFiles.Tests.ps1 -Output Minimal"
```

Expected: PASS. If `ShellHostTests.Portal_serves_the_placeholder_page_icons_and_compiled_css` fails on a clean checkout, that is the pre-existing dependence on restored fonts and icons, not this task.

- [ ] **Step 5: Commit**

```bash
git add src tests scripts docker-compose.yml docker-compose.uat.yml docker-compose.production.yml
git diff --cached --stat
git commit -m "feat(admin): validated OIDC, API and group options with env, compose and test-host settings" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 4: Sign-in and the agent session (P07-T02)

**Review Focus pin (1):** a signed-in user without agent access, or a deactivated agent, must not reach ticket data or actions. NoAccess wins, and no ticket call is made before `GET /api/agents/me` succeeds. Pinned here by `AgentSessionTests` and `AgentGateTests`, and at the host by `AgentAccessHostTests` (Task 5) and Task 13.

**Files:**
- Modify: `src/TechStrap.Admin/TechStrap.Admin.csproj` (OpenIdConnect package)
- Create: `src/TechStrap.Admin/Auth/AdminAuthentication.cs`, `LocalReturnUrl.cs`, `AgentSession.cs`
- Create: `src/TechStrap.Admin/Clients/ApiErrorCodes.cs`, `src/TechStrap.Admin/Clients/IAgentsClient.cs` (the interface only; Task 5 implements it)
- Create: `src/TechStrap.Admin/Components/Pages/SignInLanding.razor`, `SignInCopy.cs`, `NoAccessPage.razor`, `NoAccessCopy.cs`
- Create: `src/TechStrap.Admin/Components/Layout/RedirectToSignIn.razor`, `SignOutForm.razor`, `AgentGate.razor`, `AgentGate.razor.cs`, `GateCopy.cs`
- Modify: `src/TechStrap.Admin/Components/Routes.razor`, `App.razor`, `_Imports.razor`, `Layout/MainLayout.razor`, `Pages/Home.razor`, `Pages/Error.razor`, `Pages/NotFound.razor`, `Pages/StyleGuide.razor`
- Modify: `src/TechStrap.Admin/Program.cs`
- Create: `tests/Shared/AdminHost/AdminTestAuth.cs`; modify `tests/Shared/AdminHost/StubApiHandler.cs`
- Modify: `tests/TechStrap.Admin.Tests/AdminFactory.cs`, `tests/TechStrap.Api.Tests/HostFactory.cs`, and the existing host tests listed in Step 1
- Create: `tests/TechStrap.Admin.Tests/Auth/LocalReturnUrlTests.cs`, `AgentSessionTests.cs`, `AgentGateTests.cs`, `tests/TechStrap.Admin.Tests/AdminSignInTests.cs`, `tests/TechStrap.Admin.Tests/Components/ShellTestServices.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/MainLayoutTests.cs`
- Modify: `docs/architecture/PHASE-07-admin-app.md` (tick the `/error` carry-forward)

**Interfaces:**
- Consumes: Task 3 (`AuthOptions`, `ApiOptions`, `AddBlazorTokenForwarding`); `SyntaxCircus.Common` `Result`, `ResultError`, `ResultErrorKind`; `TechStrap.Contracts.Agents.AgentDto`, `AgentRoles`.
- Produces:

```csharp
namespace TechStrap.Admin.Auth;

public static class AdminAuthentication
{
    public const string CookieScheme = "Cookies";        // literal: Blazor.Auth reads tokens from the scheme with this name
    public const string OidcScheme = "oidc";
    public const string SignInPath = "/signin";
    public const string SignInStartPath = "/signin/start";
    public const string SignOutPath = "/signout";
    public const string OidcCallbackPath = "/signin-oidc";
    public const string OidcSignedOutCallbackPath = "/signout-callback-oidc";
    public const string ReturnUrlParameter = "returnUrl";
    public const string FailedParameter = "failed";
    public static readonly TimeSpan SessionLifetime;      // 8 hours, sliding
    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services);
    public static IEndpointRouteBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder endpoints);   // GET /signin, GET /signin/start, POST /signout
}

public static class LocalReturnUrl
{
    public const string Home = "/";
    public static string Sanitize(string? returnUrl);     // a local path, or "/"
}

public enum AgentSessionState { NotLoaded, Ready, NoAccess, SessionExpired, Unavailable }

public sealed class AgentSession(IAgentsClient agents)    // scoped: one per circuit
{
    public AgentSessionState State { get; }
    public AgentDto? Agent { get; }                       // set when Ready
    public string? ErrorCode { get; }                     // the API's code when NoAccess, SessionExpired or Unavailable
    public string? ErrorMessage { get; }
    public bool IsAdmin { get; }                          // Ready and Agent.Role == AgentRoles.Admin
    public event Action? Changed;
    public Task EnsureLoadedAsync(CancellationToken cancellationToken);   // single-flight; Ready and NoAccess are final per scope
    public Task ReloadAsync(CancellationToken cancellationToken);
}

namespace TechStrap.Admin.Clients;

public static class ApiErrorCodes { /* string constants: Unauthenticated, Forbidden, NotFound, Conflict, ValidationFailed, ApiUnavailable, ApiTimeout, UnexpectedResponse, ApiError, ConcurrencyConflict, TicketClosed, InvalidStatusTransition, TicketNotFound, RowVersionRequired, AgentAccessRequired, AgentInactive, AgentEmailRequired, AgentIdentityInvalid, AgentNotProvisioned, AdminAccessRequired */ }

public interface IAgentsClient
{
    Task<Result<AgentDto>> GetMeAsync(CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<AgentListItemDto>>> ListAllAsync(CancellationToken cancellationToken);
}

namespace TechStrap.Admin.Components.Layout;
public sealed partial class AgentGate : ComponentBase, IDisposable { [Parameter] public RenderFragment? ChildContent { get; set; } }   // renders ChildContent only when AgentSession is Ready, or for an anonymous visitor

namespace TechStrap.Admin.Components.Pages;
// NoAccessPage: [Parameter] string? Code, [Parameter] string? Message. NoAccessCopy.Reason(code, apiMessage, agentGroup). SignInCopy: constants. SignInLanding: [Parameter] ReturnUrl, Failed.
```

  Test helpers for Tasks 7 to 13:

```csharp
// tests/Shared/AdminHost/AdminTestAuth.cs (public; compiled into both test projects)
public sealed record AdminTestPrincipal(string Name, string Subject, string DisplayName, string Email, string[] Groups)
{
    public static AdminTestPrincipal Agent { get; }      // "agent":   sub agent-sub-1,   Sam Agent,   sam@orbitly.test, group techstrap-agents
    public static AdminTestPrincipal Admin { get; }      // "admin":   sub admin-sub-1,   Ada Admin,   ada@orbitly.test, group techstrap-admins
    public static AdminTestPrincipal Outsider { get; }   // "outsider": sub outsider-sub-1, Olly Outsider, olly@orbitly.test, no group (the stub API answers 403 agent-access-required)
    public static IReadOnlyList<AdminTestPrincipal> All { get; }
    public string AccessToken { get; }                   // "test-access-token-" + Name: what the API-bound request carries as "Bearer ..."
}

public static class AdminTestAuth
{
    public const string Scheme = "Test";
    public const string PrincipalHeader = "X-Test-Principal";          // value: agent | admin | outsider; no header = anonymous
    public const string AccessTokenPrefix = "test-access-token-";
    public static IServiceCollection AddAdminTestAuthentication(this IServiceCollection services);   // Test scheme as DefaultAuthenticate, fixed OIDC metadata, token seeding
    public static HttpClient SignedInAs(this HttpClient client, AdminTestPrincipal principal);        // sets the header, returns the client
}

// StubApiHandler gains:
public StubApiHandler WithTestAgents();   // GET /api/agents/me: 200 AgentDto for agent and admin, 403 agent-access-required for outsider, 401 without a token

// tests/TechStrap.Admin.Tests/Components/ShellTestServices.cs (internal; bUnit)
internal static class ShellTestServices
{
    public static IAgentsClient AddAgentShell(this BunitContext context, string role = AgentRoles.Agent);   // authorization, a substitute IAgentsClient answering Ready, AgentSession, AgentGroupOptions
    public static ComponentParameterCollectionBuilder<TComponent> SignedIn<TComponent>(this ComponentParameterCollectionBuilder<TComponent> parameters, bool signedIn = true) where TComponent : IComponent;   // cascading authentication state
}
```

**Rules:**
1. **Order of the middleware.** `UseForwardedHeaders`, `UseCorrelationId`, exception handler, status code pages, then `UseAuthentication`, `UseAuthorization`, `UseBlazorTokenCache`, `UseAntiforgery`, then the endpoints (`UseBlazorTokenCache` must be after `UseAuthorization` and before `UseAntiforgery`, as the package documents).
2. **The fallback policy requires a signed-in user.** Health checks and static assets are mapped inside `app.MapGroup(string.Empty).AllowAnonymous()`. `/error`, `/not-found` and `/_styleguide` are anonymous through `[AllowAnonymous]` on the pages (the style guide still answers 404 outside Development through `DevelopmentOnly`). Because the fallback policy also applies to an address that matches nothing, an anonymous request for an unknown path is redirected to `/signin`: only a signed-in agent sees the branded 404.
3. **`/signin` is not a routable component.** It is a minimal-API endpoint returning `RazorComponentResult<SignInLanding>`, and `SignInLanding` is a whole HTML document (it runs outside `App.razor`, with no router and no circuit). The sign-in form is a plain `GET` form, so the browser submits it natively and the interactive router never intercepts it. `Assets["css/app.css"]` resolves there (verified by `The_landing_page_is_anonymous_has_one_sign_in_form_and_opens_no_circuit`).
4. **`/signin/start` and `returnUrl`.** Only a local path survives `LocalReturnUrl.Sanitize`; everything else is `/`. The sign-in routes themselves are never a return target.
5. **`POST /signout`** takes the form (`IFormCollection`), which makes the minimal API validate the antiforgery token (a missing token is 400). It signs out of both the cookie and the provider, and the sign-out form is a plain `POST` form with `<AntiforgeryToken />`.
6. **The OIDC options** read the validated `AuthOptions` lazily. `RequireHttpsMetadata` is off in Development only. A failed remote sign-in redirects to `/signin?failed=1` and never shows the provider's text.
7. **`AgentSession`** treats `agent-inactive` as final even if the API answers 200 with `IsActive == false`. A `Forbidden` error is NoAccess, `Unauthenticated` is SessionExpired, anything else (transport failure, unexpected answer) is Unavailable and is asked again on the next call. A cancelled load never pins the session.
8. **Gate wiring is Task 5.** `AgentGate` exists and is tested here, but `MainLayout` wraps `@Body` in it only in Task 5, once `IAgentsClient` has an implementation (a signed-in host test would otherwise fail to resolve the client).

- [ ] **Step 1: Write the failing tests**

1. `tests/Shared/AdminHost/AdminTestAuth.cs`:

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SyntaxCircus.Blazor.Auth;

namespace TechStrap.Tests.Shared.AdminHost;

/// <summary>
/// A signed-in test agent. Admin host tests never touch OpenID Connect: a request carries the header <see cref="AdminTestAuth.PrincipalHeader"/>
/// with one of the names below and the "Test" scheme turns it into a principal with the claim names the real sign-in produces
/// (sub, name, email, groups). No header means an anonymous request.
/// </summary>
public sealed record AdminTestPrincipal(string Name, string Subject, string DisplayName, string Email, string[] Groups)
{
    /// <summary>A member of the agent group.</summary>
    public static AdminTestPrincipal Agent { get; } = new("agent", "agent-sub-1", "Sam Agent", "sam@orbitly.test", ["techstrap-agents"]);

    /// <summary>A member of the admin group.</summary>
    public static AdminTestPrincipal Admin { get; } = new("admin", "admin-sub-1", "Ada Admin", "ada@orbitly.test", ["techstrap-admins"]);

    /// <summary>Signed in with the provider but in neither group: the API answers 403 agent-access-required.</summary>
    public static AdminTestPrincipal Outsider { get; } = new("outsider", "outsider-sub-1", "Olly Outsider", "olly@orbitly.test", []);

    public static IReadOnlyList<AdminTestPrincipal> All { get; } = [Agent, Admin, Outsider];

    /// <summary>The bearer token the Admin sends to the API for this principal. It is unique per principal so the stub API can tell them apart.</summary>
    public string AccessToken => AdminTestAuth.AccessTokenPrefix + Name;

    public static AdminTestPrincipal? Find(string? name) => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    internal ClaimsPrincipal ToClaimsPrincipal(string scheme)
    {
        List<Claim> claims = [new("sub", Subject), new("name", DisplayName), new("email", Email), .. Groups.Select(g => new Claim("groups", g))];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme, "name", "roles"));
    }
}

public static class AdminTestAuth
{
    public const string Scheme = "Test";
    public const string PrincipalHeader = "X-Test-Principal";

    /// <summary>The tokens look like secrets on purpose: the host tests scan the log for them (Review Focus 2).</summary>
    public const string AccessTokenPrefix = "test-access-token-";

    /// <summary>
    /// Adds the "Test" scheme as the default authenticate scheme. The real cookie scheme stays the default challenge scheme, so an anonymous
    /// request is still redirected to /signin. Each principal's access token is put in the server token cache, which is where
    /// SyntaxCircus.Blazor.Auth looks when the cookie holds no tokens, so the API-bound request carries "Bearer {AccessToken}".
    /// </summary>
    public static IServiceCollection AddAdminTestAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, AdminTestAuthHandler>(Scheme, _ => { });
        services.PostConfigure<AuthenticationOptions>(options => options.DefaultAuthenticateScheme = Scheme);
        // The provider metadata is fixed, so a challenge builds its redirect without any network call.
        services.Configure<OpenIdConnectOptions>("oidc", options => options.Configuration = new OpenIdConnectConfiguration
        {
            Issuer = "https://idp.test/application/o/techstrap-admin/",
            AuthorizationEndpoint = "https://idp.test/application/o/authorize/",
            TokenEndpoint = "https://idp.test/application/o/token/",
            EndSessionEndpoint = "https://idp.test/application/o/techstrap-admin/end-session/",
        });
        services.AddHostedService<AdminTestTokenSeeder>();
        return services;
    }

    /// <summary>The header a test client sends to sign in as <paramref name="principal"/>.</summary>
    public static HttpClient SignedInAs(this HttpClient client, AdminTestPrincipal principal)
    {
        client.DefaultRequestHeaders.Remove(PrincipalHeader);
        client.DefaultRequestHeaders.Add(PrincipalHeader, principal.Name);
        return client;
    }
}

internal sealed class AdminTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AdminTestAuth.PrincipalHeader, out var name))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var principal = AdminTestPrincipal.Find(name.ToString());
        return Task.FromResult(principal is null
            ? AuthenticateResult.Fail("Unknown test principal.")
            : AuthenticateResult.Success(new AuthenticationTicket(principal.ToClaimsPrincipal(Scheme.Name), Scheme.Name)));
    }
}

/// <summary>Puts each test principal's access token in the server token cache under the key SyntaxCircus.Blazor.Auth uses ("user:{sub}").</summary>
internal sealed class AdminTestTokenSeeder(IServerTokenCache cache, IUserTokenCacheKeyProvider keys) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var principal in AdminTestPrincipal.All)
        {
            var key = keys.GetCacheKey(principal.Subject)!;
            await cache.SetAsync(key, new ServerTokenCacheEntry(principal.AccessToken, null, null, DateTimeOffset.UtcNow.AddDays(1)), cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

2. `tests/Shared/AdminHost/StubApiHandler.cs`: add `using TechStrap.Contracts.Agents;` and this method before `JsonResponse`:

```csharp
    /// <summary>
    /// The default answer to <c>GET /api/agents/me</c>: the token decides who is calling (see <see cref="AdminTestPrincipal.AccessToken"/>).
    /// The agent and the admin are 200 with their role; the outsider is 403 agent-access-required; no token is 401.
    /// </summary>
    public StubApiHandler WithTestAgents() => On(HttpMethod.Get, "/api/agents/me", request =>
    {
        var principal = AdminTestPrincipal.All.FirstOrDefault(p => request.Authorization == "Bearer " + p.AccessToken);
        if (principal is null)
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        if (principal == AdminTestPrincipal.Outsider)
        {
            return Problem(HttpStatusCode.Forbidden, "agent-access-required", "Your account is not in an agent group.");
        }

        var role = principal == AdminTestPrincipal.Admin ? AgentRoles.Admin : AgentRoles.Agent;
        return JsonResponse(HttpStatusCode.OK, new AgentDto(Guid.NewGuid(), principal.DisplayName, principal.Email, role, true, null, null));
    });
```

3. The factories sign in with the Test scheme and answer `/api/agents/me` by default. In `tests/TechStrap.Admin.Tests/AdminFactory.cs`: `public StubApiHandler Api { get; } = new StubApiHandler().WithTestAgents();`, add `using Microsoft.AspNetCore.TestHost;`, and replace the `ConfigureServices` line with

```csharp
        builder.ConfigureTestServices(services =>
        {
            services.AddAdminTestAuthentication();
            configureServices?.Invoke(services);
        });
```

   (and extend the class comment: "registers the `Test` authentication scheme: a request signs in with `AdminTestAuth.SignedInAs`, no header is anonymous"). In `tests/TechStrap.Api.Tests/HostFactory.cs` change the private constructor's base call to

```csharp
        : base(
            environment,
            AdminTestSettings.With(settings),
            services =>
            {
                services.AddAdminTestAuthentication();
                configureServices?.Invoke(services);
            }) => Api = api;
```

   and the public constructor to pass `new StubApiHandler().WithTestAgents()`.

4. The signed-out world changes the existing host tests. The fallback policy sends an anonymous request for `/` (and for any unknown address) to `/signin`, so every test that asserts shell markup signs in. In `LayoutHostTests`, `NotFoundHostTests`, `ErrorStatusHostTests`, `UnhandledErrorHostTests` and the **first** test of `ReconnectAndErrorTests` (the dialog test; the others stay anonymous) replace `using var client = factory.CreateClient();` by

```csharp
using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);
```

   and add `using TechStrap.Tests.Shared.AdminHost;`. In `tests/TechStrap.Api.Tests/ShellHostTests.cs` do the same for `Admin_serves_the_placeholder_page_icons_and_compiled_css`. `HostHealthSmokeTests.Admin_reports_live_only` stays anonymous (the health checks are anonymous).

5. `tests/TechStrap.Admin.Tests/Auth/LocalReturnUrlTests.cs`:

```csharp
using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Tests.Auth;

public sealed class LocalReturnUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/queue/mine?page=2", "/queue/mine?page=2")]
    [InlineData("/tickets/ORB-42", "/tickets/ORB-42")]
    public void A_local_path_is_kept(string input, string expected) => LocalReturnUrl.Sanitize(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\evil.example")]
    [InlineData("\\evil.example")]
    [InlineData("tickets/ORB-42")]
    [InlineData("/ok\r\nSet-Cookie: x=1")]
    [InlineData("/signin?returnUrl=/")]
    [InlineData("/SignOut")]
    public void Anything_else_becomes_the_home_page(string? input) => LocalReturnUrl.Sanitize(input).ShouldBe("/");
}
```

6. `tests/TechStrap.Admin.Tests/AdminSignInTests.cs`:

```csharp
using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

public sealed class AdminSignInTests
{
    private static HttpClient NoRedirectClient(AdminFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task An_anonymous_request_for_a_page_is_sent_to_the_landing_page_with_a_local_return_url()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.PathAndQuery.ShouldBe("/signin?returnUrl=%2F");
    }

    [Fact]
    public async Task The_landing_page_is_anonymous_has_one_sign_in_form_and_opens_no_circuit()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync("/signin?returnUrl=%2Ftickets%2FORB-1", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var page = await new HtmlParser().ParseDocumentAsync(html, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        page.QuerySelector("article.ts-window")!.TextContent.ShouldContain("Sign in");
        var form = page.QuerySelectorAll("form[action='/signin/start'][method=get]").ShouldHaveSingleItem();
        form.QuerySelector("input[name=returnUrl]")!.GetAttribute("value").ShouldBe("/tickets/ORB-1");
        page.QuerySelector("link[rel=stylesheet]")!.GetAttribute("href")!.ShouldContain("css/app.css");
        html.ShouldNotContain("blazor.web.js");
    }

    [Fact]
    public async Task Static_assets_health_and_the_error_pages_are_anonymous()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory);

        foreach (var path in new[] { "/health/live", "/health/ready", "/brand/mark.svg", "/favicon.ico", "/not-found", "/error", "/_styleguide" })
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task Starting_sign_in_redirects_to_the_provider_with_the_code_flow_pkce_and_offline_access()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync("/signin/start?returnUrl=%2Ftickets%2FORB-1", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        location.GetLeftPart(UriPartial.Path).ShouldBe("https://idp.test/application/o/authorize/");
        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        query["response_type"].ShouldBe("code");
        query["client_id"].ShouldBe("techstrap-admin-test");
        query["code_challenge_method"].ShouldBe("S256");
        query["redirect_uri"].ShouldEndWith("/signin-oidc");
        query["scope"]!.Split(' ').ShouldBe(["openid", "profile", "email", "offline_access"], ignoreOrder: true);
    }

    [Fact]
    public async Task Signing_out_needs_an_antiforgery_token()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory).SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.PostAsync("/signout", new FormUrlEncodedContent([]), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_anonymous_caller_cannot_open_a_circuit()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory);

        using var response = await client.PostAsync("/_blazor/negotiate?negotiateVersion=1", null, TestContext.Current.CancellationToken);

        ((int)response.StatusCode).ShouldBeOneOf(302, 401);
    }

    [Fact]
    public async Task Signing_out_with_the_form_token_redirects_to_the_provider_end_session_endpoint()
    {
        await using var factory = new AdminFactory();
        using var client = NoRedirectClient(factory).SignedInAs(AdminTestPrincipal.Agent);

        var shell = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var page = await new HtmlParser().ParseDocumentAsync(await shell.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken);
        var form = page.QuerySelector("form[action='/signout'][method=post]").ShouldNotBeNull();
        var token = form.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
        using var post = new HttpRequestMessage(HttpMethod.Post, "/signout") { Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]) };
        foreach (var cookie in shell.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]))
        {
            post.Headers.Add("Cookie", cookie);
        }

        using var response = await client.SendAsync(post, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.GetLeftPart(UriPartial.Path).ShouldBe("https://idp.test/application/o/techstrap-admin/end-session/");
    }
}
```

7. `tests/TechStrap.Admin.Tests/Auth/AgentSessionTests.cs`:

```csharp
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Auth;

/// <summary>Review Focus 1: the API's answer to <c>GET /api/agents/me</c> decides who may work, and nothing else is trusted.</summary>
public sealed class AgentSessionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();

    private AgentSession Session() => new(_agents);

    private static AgentDto Agent(string role = AgentRoles.Agent, bool active = true) => new(Guid.NewGuid(), "Sam", "sam@orbitly.test", role, active, null, null);

    private static Result<AgentDto> Refused(string code, ResultErrorKind kind = ResultErrorKind.Forbidden) =>
        Result<AgentDto>.Failure(new ResultError(code, "The API says no.", kind));

    [Fact]
    public async Task An_active_agent_is_ready_and_an_agent_is_not_an_admin()
    {
        _agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Success(Agent()));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        session.Agent!.Email.ShouldBe("sam@orbitly.test");
        session.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public async Task Only_the_api_role_makes_an_admin()
    {
        _agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Success(Agent(AgentRoles.Admin)));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);

        session.IsAdmin.ShouldBeTrue();
    }

    [Theory]
    [InlineData(ApiErrorCodes.AgentAccessRequired)]
    [InlineData(ApiErrorCodes.AgentInactive)]
    [InlineData(ApiErrorCodes.AgentEmailRequired)]
    [InlineData(ApiErrorCodes.AgentIdentityInvalid)]
    public async Task Every_403_is_no_access_with_the_reason_and_never_an_agent(string code)
    {
        _agents.GetMeAsync(Ct).Returns(Refused(code));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);

        session.State.ShouldBe(AgentSessionState.NoAccess);
        session.ErrorCode.ShouldBe(code);
        session.ErrorMessage.ShouldBe("The API says no.");
        session.Agent.ShouldBeNull();
        session.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public async Task A_deactivated_agent_the_api_still_returns_is_refused()
    {
        _agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Success(Agent(AgentRoles.Admin, active: false)));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);

        session.State.ShouldBe(AgentSessionState.NoAccess);
        session.ErrorCode.ShouldBe(ApiErrorCodes.AgentInactive);
        session.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public async Task A_401_is_an_expired_session_and_a_transport_failure_is_unavailable_and_both_are_asked_again()
    {
        _agents.GetMeAsync(Ct).Returns(
            Refused(ApiErrorCodes.Unauthenticated, ResultErrorKind.Unauthenticated),
            Refused(ApiErrorCodes.ApiUnavailable, ResultErrorKind.Failure),
            Result<AgentDto>.Success(Agent()));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);
        session.State.ShouldBe(AgentSessionState.SessionExpired);
        await session.EnsureLoadedAsync(Ct);
        session.State.ShouldBe(AgentSessionState.Unavailable);
        await session.EnsureLoadedAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        await _agents.Received(3).GetMeAsync(Ct);
    }

    [Fact]
    public async Task Ready_and_no_access_are_final_so_the_api_is_asked_once_per_scope()
    {
        _agents.GetMeAsync(Ct).Returns(Result<AgentDto>.Success(Agent()));
        var session = Session();

        await session.EnsureLoadedAsync(Ct);
        await session.EnsureLoadedAsync(Ct);

        await _agents.Received(1).GetMeAsync(Ct);

        _agents.ClearReceivedCalls();
        _agents.GetMeAsync(Ct).Returns(Refused(ApiErrorCodes.AgentAccessRequired));
        var refused = Session();
        await refused.EnsureLoadedAsync(Ct);
        await refused.EnsureLoadedAsync(Ct);
        await _agents.Received(1).GetMeAsync(Ct);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_request()
    {
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.GetMeAsync(Ct).Returns(gate.Task);
        var session = Session();

        var first = session.EnsureLoadedAsync(Ct);
        var second = session.EnsureLoadedAsync(Ct);
        gate.SetResult(Result<AgentDto>.Success(Agent()));
        await Task.WhenAll(first, second);

        await _agents.Received(1).GetMeAsync(Ct);
        session.State.ShouldBe(AgentSessionState.Ready);
    }

    [Fact]
    public async Task A_cancelled_load_does_not_pin_the_session()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Throws(new OperationCanceledException());
        var session = Session();

        await Should.ThrowAsync<OperationCanceledException>(() => session.EnsureLoadedAsync(Ct));

        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Agent()));
        await session.EnsureLoadedAsync(Ct);
        session.State.ShouldBe(AgentSessionState.Ready);
    }

    [Fact]
    public async Task Reload_asks_again_and_a_change_is_announced()
    {
        _agents.GetMeAsync(Ct).Returns(Refused(ApiErrorCodes.AgentInactive), Result<AgentDto>.Success(Agent()));
        var session = Session();
        var changes = 0;
        session.Changed += () => changes++;

        await session.EnsureLoadedAsync(Ct);
        await session.ReloadAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        changes.ShouldBe(2);
    }
}
```

8. `tests/TechStrap.Admin.Tests/Components/ShellTestServices.cs`:

```csharp
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Options;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>What the shell (MainLayout, AgentGate, NoAccessPage, sign-out form) needs in a bUnit test: authorization services, the session and its client, the group options.</summary>
internal static class ShellTestServices
{
    /// <summary>Registers the shell services with a substitute <see cref="IAgentsClient"/> that accepts the agent, and returns it so a test can change the answer.</summary>
    public static IAgentsClient AddAgentShell(this BunitContext context, string role = AgentRoles.Agent)
    {
        var agents = Substitute.For<IAgentsClient>();
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", role, true, null, null)));
        context.AddAuthorization().SetAuthorized("Sam Agent");
        context.Services.AddSingleton(agents);
        context.Services.AddSingleton<AgentSession>();
        context.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new AgentGroupOptions()));
        context.Services.AddSingleton<AntiforgeryStateProvider, NoTokenAntiforgeryStateProvider>();
        return agents;
    }

    /// <summary>The cascading authentication state a router would provide, for a signed-in agent or an anonymous visitor.</summary>
    public static ComponentParameterCollectionBuilder<TComponent> SignedIn<TComponent>(this ComponentParameterCollectionBuilder<TComponent> parameters, bool signedIn = true)
        where TComponent : Microsoft.AspNetCore.Components.IComponent =>
        parameters.AddCascadingValue(Task.FromResult(new AuthenticationState(
            signedIn ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "s1"), new Claim("name", "Sam Agent")], "Test", "name", "roles")) : new ClaimsPrincipal(new ClaimsIdentity()))));

    private sealed class NoTokenAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => null;
    }
}
```

9. `tests/TechStrap.Admin.Tests/Auth/AgentGateTests.cs`:

```csharp
using Bunit;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Tests.Components;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Auth;

/// <summary>Review Focus 1, component half: no page content (and so no ticket call) exists until the API has accepted the agent.</summary>
public sealed class AgentGateTests : BunitContext
{
    private readonly IAgentsClient _agents;

    public AgentGateTests() => _agents = this.AddAgentShell();

    private IRenderedComponent<AgentGate> RenderGate(bool signedIn = true) => Render<AgentGate>(p => p
        .SignedIn(signedIn)
        .AddChildContent("<p id='content'>ticket data</p>"));

    private static Result<AgentDto> Refused(string code, ResultErrorKind kind = ResultErrorKind.Forbidden) =>
        Result<AgentDto>.Failure(new ResultError(code, "The API says no.", kind));

    [Fact]
    public void An_accepted_agent_sees_the_content()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null)));

        var cut = RenderGate();

        cut.WaitForAssertion(() => cut.Find("#content").TextContent.ShouldBe("ticket data"));
    }

    [Fact]
    public void The_content_does_not_exist_while_the_api_has_not_answered()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<Result<AgentDto>>().Task);

        var cut = RenderGate();

        cut.FindAll("#content").ShouldBeEmpty();
        cut.Find("[role=status]").TextContent.ShouldBe(GateCopy.Checking);
    }

    [Theory]
    [InlineData(ApiErrorCodes.AgentAccessRequired, "not in the techstrap-agents group")]
    [InlineData(ApiErrorCodes.AgentInactive, "deactivated")]
    [InlineData(ApiErrorCodes.AgentEmailRequired, "email address")]
    [InlineData(ApiErrorCodes.AgentIdentityInvalid, "could not be matched")]
    public void A_refused_agent_sees_the_reason_and_never_the_content(string code, string expectedText)
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Refused(code));

        var cut = RenderGate();

        cut.FindAll("#content").ShouldBeEmpty();
        cut.Find("section.ts-no-access h1").TextContent.ShouldBe("You don't have access to TechStrap.");
        cut.Find("section.ts-no-access p").TextContent.ShouldContain(expectedText);
        cut.Find("section.ts-no-access form[action='/signout']").ShouldNotBeNull();
    }

    [Fact]
    public void An_expired_session_offers_sign_in_and_hides_the_content()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Refused(ApiErrorCodes.Unauthenticated, ResultErrorKind.Unauthenticated));

        var cut = RenderGate();

        cut.FindAll("#content").ShouldBeEmpty();
        cut.Find("form[action='/signin/start'] button").TextContent.ShouldBe(GateCopy.SignInAgain);
    }

    [Fact]
    public void When_the_api_is_unavailable_the_content_stays_hidden_and_retry_asks_again()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(
            Refused(ApiErrorCodes.ApiUnavailable, ResultErrorKind.Failure),
            Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null)));
        var cut = RenderGate();
        cut.FindAll("#content").ShouldBeEmpty();

        cut.Find("section.ts-gate button").Click();

        cut.WaitForAssertion(() => cut.Find("#content").TextContent.ShouldBe("ticket data"));
    }

    [Fact]
    public void An_anonymous_visitor_sees_the_content_and_the_api_is_not_called()
    {
        var cut = RenderGate(signedIn: false);

        cut.Find("#content").TextContent.ShouldBe("ticket data");
        _agents.DidNotReceiveWithAnyArgs().GetMeAsync(Xunit.TestContext.Current.CancellationToken);
    }
}
```

10. `tests/TechStrap.Admin.Tests/Components/MainLayoutTests.cs`: the layout now contains the sign-out form (an `AuthorizeView`), so its three tests need the shell services and a cascading authentication state. Add `using TechStrap.Admin.Tests.Components;` is not needed (same namespace). Add this constructor as the first member and change every `Render<MainLayout>(p => p.Add(...))` to start with `p.SignedIn()`:

```csharp
    public MainLayoutTests() => this.AddAgentShell();
    // ...
    var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "LocalReturnUrlTests|AdminSignInTests|AgentSessionTests|AgentGateTests|MainLayoutTests"
```

Expected: a build failure (`AdminAuthentication`, `AgentSession`, `AgentGate`, `IAgentsClient`, `LocalReturnUrl` do not exist).

- [ ] **Step 3: Implement the plumbing**

1. `src/TechStrap.Admin/TechStrap.Admin.csproj`: add `<PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" />` (alphabetical, after `GitVersion.MsBuild`). The version is already central (10.0.12).

2. `src/TechStrap.Admin/Auth/AdminAuthentication.cs`:

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Components.Pages;

namespace TechStrap.Admin.Auth;

/// <summary>
/// Cookie plus OpenID Connect sign-in (D-040). The cookie scheme is literally "Cookies": SyntaxCircus.Blazor.Auth reads and refreshes the saved
/// tokens from the scheme with that name. The sign-in, sign-in start and sign-out routes are minimal-API endpoints, not components, so they never need a circuit.
/// </summary>
public static class AdminAuthentication
{
    public const string CookieScheme = "Cookies";
    public const string OidcScheme = "oidc";

    public const string SignInPath = "/signin";
    public const string SignInStartPath = "/signin/start";
    public const string SignOutPath = "/signout";
    public const string OidcCallbackPath = "/signin-oidc";
    public const string OidcSignedOutCallbackPath = "/signout-callback-oidc";
    public const string ReturnUrlParameter = "returnUrl";
    public const string FailedParameter = "failed";

    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieScheme;
                options.DefaultChallengeScheme = CookieScheme;
            })
            .AddCookie(CookieScheme, options =>
            {
                options.Cookie.Name = "techstrap.admin";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = SessionLifetime;
                options.SlidingExpiration = true;
                // An unauthenticated request is sent to the signed-out landing page, never straight to the provider.
                options.LoginPath = SignInPath;
                options.ReturnUrlParameter = ReturnUrlParameter;
            })
            .AddOpenIdConnect(OidcScheme, _ => { });

        // Read lazily from the validated options, so test factories can supply the values through in-memory configuration.
        services.AddOptions<OpenIdConnectOptions>(OidcScheme)
            .Configure<IOptions<AuthOptions>, IHostEnvironment>((options, auth, environment) =>
            {
                var settings = auth.Value;
                options.Authority = settings.Authority;
                options.ClientId = settings.ClientId;
                options.ClientSecret = settings.ClientSecret;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.SignInScheme = CookieScheme;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                // Raw claim names (sub, email, name, groups), the same as the API sees.
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";
                options.CallbackPath = OidcCallbackPath;
                options.SignedOutCallbackPath = OidcSignedOutCallbackPath;
                options.Scope.Clear();
                foreach (var scope in settings.Scopes)
                {
                    options.Scope.Add(scope);
                }

                // Never show the provider's failure text (it can carry request details): send the agent back to the landing page.
                options.Events.OnRemoteFailure = context =>
                {
                    context.Response.Redirect($"{SignInPath}?{FailedParameter}=1");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    public static IEndpointRouteBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(SignInPath, (string? returnUrl, string? failed) =>
                new RazorComponentResult<SignInLanding>(new { ReturnUrl = LocalReturnUrl.Sanitize(returnUrl), Failed = !string.IsNullOrEmpty(failed) }))
            .AllowAnonymous();

        endpoints.MapGet(SignInStartPath, (string? returnUrl) =>
                TypedResults.Challenge(new AuthenticationProperties { RedirectUri = LocalReturnUrl.Sanitize(returnUrl) }, [OidcScheme]))
            .AllowAnonymous();

        // POST only. Binding the form makes minimal APIs reject a request without a valid antiforgery token (400); signs out of the cookie and of the provider.
        endpoints.MapPost(SignOutPath, (IFormCollection form) =>
                TypedResults.SignOut(new AuthenticationProperties { RedirectUri = SignInPath }, [CookieScheme, OidcScheme]));
        return endpoints;
    }
}
```

3. `src/TechStrap.Admin/Auth/LocalReturnUrl.cs`:

```csharp
namespace TechStrap.Admin.Auth;

/// <summary>
/// The only URLs the sign-in flow will send an agent back to: local paths of this app. Anything else (another host, a protocol-relative
/// "//host", a backslash trick, a control character, or the sign-in routes themselves) becomes the home page, so the returnUrl parameter cannot
/// be used as an open redirect.
/// </summary>
public static class LocalReturnUrl
{
    public const string Home = "/";

    public static string Sanitize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)
            || returnUrl[0] != '/'
            || (returnUrl.Length > 1 && returnUrl[1] is '/' or '\\')
            || returnUrl.Any(char.IsControl)
            || returnUrl.Contains('\\'))
        {
            return Home;
        }

        if (returnUrl.StartsWith(AdminAuthentication.SignInPath, StringComparison.OrdinalIgnoreCase)
            || returnUrl.StartsWith(AdminAuthentication.SignOutPath, StringComparison.OrdinalIgnoreCase))
        {
            return Home;
        }

        return returnUrl;
    }
}
```

4. `src/TechStrap.Admin/Clients/ApiErrorCodes.cs` and `src/TechStrap.Admin/Clients/IAgentsClient.cs` (Task 5 implements the interface):

```csharp
namespace TechStrap.Admin.Clients;

/// <summary>
/// The error codes components branch on. Codes that come from the API are its problem <c>type</c> (or, for a validation failure, the entries of
/// <c>errorCodes</c>); the others are produced by the API connection when the API gave no code.
/// </summary>
public static class ApiErrorCodes
{
    // Produced by the Admin.
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not-found";
    public const string Conflict = "conflict";
    public const string ValidationFailed = "validation-failed";
    public const string ApiUnavailable = "api-unavailable";
    public const string ApiTimeout = "api-timeout";
    public const string UnexpectedResponse = "api-unexpected-response";
    public const string ApiError = "api-error";

    // Produced by the API.
    public const string ConcurrencyConflict = "concurrency-conflict";
    public const string TicketClosed = "ticket-closed";
    public const string InvalidStatusTransition = "invalid-status-transition";
    public const string TicketNotFound = "ticket-not-found";
    public const string RowVersionRequired = "row-version-required";
    public const string AgentAccessRequired = "agent-access-required";
    public const string AgentInactive = "agent-inactive";
    public const string AgentEmailRequired = "agent-email-required";
    public const string AgentIdentityInvalid = "agent-identity-invalid";
    public const string AgentNotProvisioned = "agent-not-provisioned";
    public const string AdminAccessRequired = "admin-access-required";
}
```

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Clients;

/// <summary>The signed-in agent and the agent list (assignee picker). Components never see HTTP: every failure is a <see cref="Result"/> error.</summary>
public interface IAgentsClient
{
    /// <summary>
    /// <c>GET /api/agents/me</c>. The first call after sign-in also creates the agent row, so no ticket call works before it. 403 types:
    /// agent-access-required, agent-inactive, agent-email-required, agent-identity-invalid.
    /// </summary>
    Task<Result<AgentDto>> GetMeAsync(CancellationToken cancellationToken);

    /// <summary>Every agent the caller may see (active agents for an Agent, everyone for an Admin), fetched page by page at the API maximum of 100.</summary>
    Task<Result<IReadOnlyList<AgentListItemDto>>> ListAllAsync(CancellationToken cancellationToken);
}
```

5. `src/TechStrap.Admin/Auth/AgentSession.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Auth;

public enum AgentSessionState
{
    /// <summary><c>GET /api/agents/me</c> has not answered yet.</summary>
    NotLoaded,

    /// <summary>The API knows the agent and they may work: <see cref="AgentSession.Agent"/> is set.</summary>
    Ready,

    /// <summary>The API refused the agent (no agent group, deactivated, no email, unusable identity): show the no-access page and make no other call.</summary>
    NoAccess,

    /// <summary>The API rejected the token (401). The agent signs in again.</summary>
    SessionExpired,

    /// <summary>The API could not be reached or answered something unexpected. Retryable.</summary>
    Unavailable,
}

/// <summary>
/// Who the signed-in user is, according to the API (D-040). The Admin does not parse group claims: the first call of a circuit is <c>GET /api/agents/me</c>,
/// which also creates the agent row that every ticket call needs, and its answer decides everything. Components that show ticket data sit inside
/// <c>AgentGate</c>, which renders them only when <see cref="State"/> is <see cref="AgentSessionState.Ready"/>, so NoAccess always wins and no ticket call
/// precedes a successful <c>/me</c>. Scoped: one per circuit (and one per prerender request).
/// </summary>
public sealed class AgentSession(IAgentsClient agents)
{
    private Task? _loading;

    public AgentSessionState State { get; private set; }

    /// <summary>The signed-in agent when <see cref="State"/> is Ready.</summary>
    public AgentDto? Agent { get; private set; }

    /// <summary>The API's error code when the state is NoAccess, SessionExpired or Unavailable (for example agent-inactive).</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>The API's message for that error, written for the agent to read.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Admin-only actions (delete, erase) show only for an Admin. The API still enforces it.</summary>
    public bool IsAdmin => State == AgentSessionState.Ready && Agent?.Role == AgentRoles.Admin;

    /// <summary>Raised after every state change so the gate and the navigation can re-render.</summary>
    public event Action? Changed;

    /// <summary>
    /// Loads the session once. Concurrent callers share one request. Ready and NoAccess are final for the life of the scope (the stored role is refreshed
    /// by the API on every <c>/me</c>, so a deliberate <see cref="ReloadAsync"/> picks up a change); SessionExpired and Unavailable are tried again on the next call.
    /// </summary>
    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (State is AgentSessionState.Ready or AgentSessionState.NoAccess)
        {
            return;
        }

        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref _loading, source.Task, null) is { } running)
        {
            // Someone else is already asking; share their answer (their cancellation is theirs alone: the state tells the outcome).
            await running;
            return;
        }

        try
        {
            Apply(await agents.GetMeAsync(cancellationToken));
        }
        finally
        {
            // A failed or cancelled load must not pin the shared task: the next call asks again unless the state is final.
            _loading = null;
            source.SetResult();
        }
    }

    /// <summary>Asks the API again (the Retry button, or after an admin changed the agent's access).</summary>
    public Task ReloadAsync(CancellationToken cancellationToken)
    {
        State = AgentSessionState.NotLoaded;
        return EnsureLoadedAsync(cancellationToken);
    }
    private void Apply(Result<AgentDto> result)
    {
        if (result.IsSuccess && result.Value.IsActive)
        {
            Agent = result.Value;
            State = AgentSessionState.Ready;
            ErrorCode = ErrorMessage = null;
        }
        else
        {
            Agent = null;
            var error = result.IsSuccess
                ? new ResultError(ApiErrorCodes.AgentInactive, "Your agent account is deactivated.", ResultErrorKind.Forbidden)
                : result.Errors[0];
            ErrorCode = error.Code;
            ErrorMessage = error.Message;
            State = error.Kind switch
            {
                ResultErrorKind.Forbidden => AgentSessionState.NoAccess,
                ResultErrorKind.Unauthenticated => AgentSessionState.SessionExpired,
                _ => AgentSessionState.Unavailable,
            };
        }

        Changed?.Invoke();
    }
}
```

6. `src/TechStrap.Admin/Program.cs`: add `using TechStrap.Admin.Auth;`. After `AddBlazorTokenForwarding` add

```csharp
builder.Services.AddAdminAuthentication();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AgentSession>();
```

   and replace the block from `app.UseAntiforgery();` through the `app.MapRazorComponentsWithStaticAssets<App>()...` call with

```csharp
// Order matters: the token cache middleware needs the authenticated user and must run before antiforgery (SyntaxCircus.Blazor.Auth).
app.UseAuthentication();
app.UseAuthorization();
app.UseBlazorTokenCache();
app.UseAntiforgery();

// Health checks and static assets stay anonymous; the fallback policy requires a signed-in agent for everything else.
var anonymous = app.MapGroup(string.Empty).AllowAnonymous();
anonymous.MapStandardHealthChecks();
anonymous.MapStaticAssets();
app.MapAdminAuthEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
```

   (`MapRazorComponentsWithStaticAssets` is `MapStaticAssets()` plus `MapRazorComponents<T>()`, and discards the static assets builder, so the static assets could not be made anonymous. Call the two directly.)

- [ ] **Step 4: Implement the components**

1. `src/TechStrap.Admin/Components/Pages/SignInLanding.razor` and `SignInCopy.cs`:

```razor
@* The signed-out landing page, rendered by the GET /signin endpoint (not routable: no @page). It is a whole document because it runs outside App.razor,
   with no router and no circuit, so an anonymous visitor never opens a connection. *@
<!DOCTYPE html>
<html lang="en">

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <base href="/" />
    <title>Sign in - TechStrap Admin</title>
    <link rel="icon" href="favicon.ico" sizes="any" />
    <link rel="icon" type="image/png" sizes="32x32" href="favicon-32.png" />
    <link rel="apple-touch-icon" href="apple-touch-icon.png" />
    <link rel="stylesheet" href="@Assets["css/app.css"]" />
</head>

<body>
    <main class="ts-window-page">
        <BrandWindow Title="@SignInCopy.WindowTitle" Heading="@SignInCopy.Heading">
            <p>@SignInCopy.Body</p>
            @if (Failed)
            {
                <p class="ts-signin-failed" role="alert">@SignInCopy.FailedBody</p>
            }
            @* A plain GET form: the browser submits it natively, so no router or circuit is involved. *@
            <form method="get" action="@AdminAuthentication.SignInStartPath" class="ts-window-action">
                <input type="hidden" name="@AdminAuthentication.ReturnUrlParameter" value="@ReturnUrl" />
                <button type="submit" class="ts-window-button">@SignInCopy.Button</button>
            </form>
        </BrandWindow>
    </main>
</body>

</html>

@code {
    /// <summary>A local path, already sanitised by the endpoint (see LocalReturnUrl).</summary>
    [Parameter]
    public string ReturnUrl { get; set; } = LocalReturnUrl.Home;

    /// <summary>True when the provider sign-in failed and the agent was sent back here.</summary>
    [Parameter]
    public bool Failed { get; set; }
}
```

```csharp
namespace TechStrap.Admin.Components.Pages;

/// <summary>Copy of the signed-out landing page (a brand moment: humour is allowed, docs/BRAND.md section 3, but the failure line stays plain).</summary>
public static class SignInCopy
{
    public const string WindowTitle = "signin.exe";
    public const string Heading = "Sign in to the call log.";
    public const string Body = "TechStrap Admin is for support agents. Sign in with your work account to see the queue.";
    public const string Button = "Sign in";
    public const string FailedBody = "Sign-in did not finish. Try again, and tell an admin if it keeps happening.";
}
```

2. `src/TechStrap.Admin/Components/Pages/NoAccessPage.razor` and `NoAccessCopy.cs`:

```razor
@* Not routable: AgentGate shows it in place of the page content. Plain, no mascot (UX-BRIEF-admin). *@
<section class="ts-no-access" role="alert">
    <h1>@NoAccessCopy.Title</h1>
    <p>@NoAccessCopy.Reason(Code, Message, Groups.Value.AgentGroup)</p>
    <form method="post" action="@AdminAuthentication.SignOutPath">
        <AntiforgeryToken />
        <button type="submit">@SignOutForm.SignOutLabel</button>
    </form>
</section>

@code {
    [Inject]
    private Microsoft.Extensions.Options.IOptions<TechStrap.Admin.Options.AgentGroupOptions> Groups { get; set; } = default!;

    /// <summary>The API's error code (agent-access-required, agent-inactive, agent-email-required, agent-identity-invalid, ...).</summary>
    [Parameter]
    public string? Code { get; set; }

    /// <summary>The API's own message, shown only for a code this page has no copy for.</summary>
    [Parameter]
    public string? Message { get; set; }
}
```

```csharp
using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Components.Pages;

/// <summary>What to tell a signed-in user the API refused, and what to do next. Plain copy: cause plus next step.</summary>
public static class NoAccessCopy
{
    public const string Title = "You don't have access to TechStrap.";

    public static string Reason(string? code, string? apiMessage, string agentGroup) => code switch
    {
        ApiErrorCodes.AgentAccessRequired => $"You are signed in, but your account is not in the {agentGroup} group. Ask an administrator to add you, then sign in again.",
        ApiErrorCodes.AgentInactive => "Your agent account has been deactivated. Ask an administrator to reactivate it.",
        ApiErrorCodes.AgentEmailRequired => "Your sign-in did not include an email address. Ask an administrator to check the email scope of the sign-in provider.",
        ApiErrorCodes.AgentIdentityInvalid => "Your sign-in could not be matched to an agent profile. Sign out and in again; if it keeps happening, tell an administrator.",
        _ => string.IsNullOrWhiteSpace(apiMessage) ? "The API refused your account. Ask an administrator for help." : apiMessage,
    };
}
```

3. `src/TechStrap.Admin/Components/Layout/RedirectToSignIn.razor`, `SignOutForm.razor`, `AgentGate.razor`, `AgentGate.razor.cs`, `GateCopy.cs`:

```razor
@inject NavigationManager Navigation

@code {
    // A full load (forceLoad) because /signin is an endpoint, not a route of this router. During prerendering the framework turns the
    // navigation into a 302; in a circuit it is a browser navigation. Only a path and query is ever sent: LocalReturnUrl.Sanitize checks it again.
    protected override void OnInitialized()
    {
        var returnUrl = "/" + Navigation.ToBaseRelativePath(Navigation.Uri);
        Navigation.NavigateTo(
            $"{AdminAuthentication.SignInPath}?{AdminAuthentication.ReturnUrlParameter}={Uri.EscapeDataString(returnUrl)}",
            forceLoad: true);
    }
}
```

```razor
@* A plain POST form, not an interactive handler: the sign-out endpoint redirects the browser to the provider. The antiforgery token is checked by the endpoint. *@
<AuthorizeView>
    <Authorized>
        <form method="post" action="@AdminAuthentication.SignOutPath" class="ts-signout">
            <AntiforgeryToken />
            <span class="ts-signout-name">@context.User.Identity?.Name</span>
            <button type="submit" class="ts-rail-link ts-signout-button">@SignOutLabel</button>
        </form>
    </Authorized>
</AuthorizeView>

@code {
    public const string SignOutLabel = "Sign out";
}
```

```razor
@if (_authenticated != true)
{
    @* Anonymous pages (error, not found) and the moment before the authentication state is known. *@
    @ChildContent
}
else
{
    switch (Session.State)
    {
        case AgentSessionState.Ready:
            @ChildContent
            break;
        case AgentSessionState.NoAccess:
            <NoAccessPage Code="@Session.ErrorCode" Message="@Session.ErrorMessage" />
            break;
        case AgentSessionState.SessionExpired:
            <section class="ts-gate" role="alert">
                <h1>@GateCopy.SessionExpiredTitle</h1>
                <p>@GateCopy.SessionExpiredBody</p>
                <form method="get" action="@AdminAuthentication.SignInStartPath">
                    <button type="submit">@GateCopy.SignInAgain</button>
                </form>
            </section>
            break;
        case AgentSessionState.Unavailable:
            <section class="ts-gate" role="alert">
                <h1>@GateCopy.UnavailableTitle</h1>
                <p>@Session.ErrorMessage</p>
                <button type="button" @onclick="RetryAsync">@UiCopy.RetryLabel</button>
            </section>
            break;
        default:
            <p class="ts-gate" role="status">@GateCopy.Checking</p>
            break;
    }
}
```

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// Shows its content only to an agent the API accepts (Review Focus 1). For a signed-in user it asks <see cref="AgentSession"/> (<c>GET /api/agents/me</c>) before
/// rendering anything inside it, so no ticket page can start a call, and the no-access page replaces the content on any 403. For an anonymous visitor
/// (the error and not-found pages) it renders the content directly.
/// </summary>
public sealed partial class AgentGate : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool? _authenticated;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Session.Changed += OnSessionChanged;
        var state = AuthenticationState is null ? null : await AuthenticationState;
        _authenticated = state?.User.Identity?.IsAuthenticated == true;
        if (_authenticated == true)
        {
            await Session.EnsureLoadedAsync(_lifetime.Token);
        }
    }

    private Task RetryAsync() => Session.ReloadAsync(_lifetime.Token);

    private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Session.Changed -= OnSessionChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

```csharp
namespace TechStrap.Admin.Components.Layout;

/// <summary>Plain copy for the access states (BRAND.md section 3: no humour on anything that blocks work).</summary>
public static class GateCopy
{
    public const string Checking = "Checking your access...";
    public const string SessionExpiredTitle = "Your session has expired.";
    public const string SessionExpiredBody = "Sign in again to carry on. Nothing you were typing was sent.";
    public const string SignInAgain = "Sign in again";
    public const string UnavailableTitle = "TechStrap could not check your access.";
}
```

4. `src/TechStrap.Admin/Components/Routes.razor` (replace the file):

```razor
<Router AppAssembly="typeof(Program).Assembly" NotFoundPage="typeof(Pages.NotFound)">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                <RedirectToSignIn />
            </NotAuthorized>
        </AuthorizeRouteView>
    </Found>
</Router>
```

5. `src/TechStrap.Admin/Components/App.razor`: add `@using static Microsoft.AspNetCore.Components.Web.RenderMode` plus a blank line as the first lines, and change `<HeadOutlet />` to `<HeadOutlet @rendermode="InteractiveServer" />` and `<Routes />` to `<Routes @rendermode="InteractiveServer" />` (`AppReconnectModal` stays outside, in the static document).

6. `src/TechStrap.Admin/Components/_Imports.razor`: append

```razor
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.Forms
@using TechStrap.Admin.Auth
@using TechStrap.Admin.Components.Pages
```

7. Page attributes: `Pages/Home.razor` gets `@attribute [Authorize]` directly after `@page "/"`. `Pages/Error.razor` and `Pages/NotFound.razor` get `@attribute [AllowAnonymous]` after `@layout MainLayout`. `Pages/StyleGuide.razor` gets `@attribute [AllowAnonymous]` after `@page "/_styleguide"`.

8. `src/TechStrap.Admin/Components/Layout/MainLayout.razor`: add `<SignOutForm />` after the Home link, inside the `<nav>`:

```razor
        <a class="ts-rail-link" href="/">Home</a>
        <SignOutForm />
```

9. Tick `- [ ] Carried forward from the PHASE-04 final review: Mark the Admin `/error` page `[AllowAnonymous]` when Admin auth lands.` in `docs/architecture/PHASE-07-admin-app.md` (change `[ ]` to `[x]` and append ` (done in PHASE-07a)`).

- [ ] **Step 5: Run the tests**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "ShellHostTests|Admin_reports_live_only|AdminHostRedactionTests"
```

Expected: PASS (`The_landing_page_is_anonymous_has_one_sign_in_form_and_opens_no_circuit` and `Signing_out_needs_an_antiforgery_token` prove rules 3 and 5). If `RazorComponentResult` renders the landing page without the stylesheet link, the `Assets[...]` lookup is the culprit: check that the component is rendered by the endpoint and not constructed by hand.

- [ ] **Step 6: Check the app by hand (sign-in is the one thing the tests fake)**

With a dummy provider the app starts and the landing page works; the challenge then fails at the metadata fetch, which proves the redirect is attempted. In PowerShell:

```powershell
$env:DotEnv__Enabled = 'false'
$env:Auth__Authority = 'https://authentik.invalid/application/o/techstrap-admin/'
$env:Auth__ClientId = 'techstrap-admin'; $env:Auth__ClientSecret = 'dummy'; $env:Api__BaseUrl = 'http://localhost:8080/'
dotnet run --project src/TechStrap.Admin
```

Open `http://localhost:5000/` (use the URL the log prints). Expected: a redirect to `/signin?returnUrl=%2F`, the retro window with a **Sign in** button, **no** "Connection lost" dialog, and no request to `/_blazor` in the network tab. Open `/not-found` (anonymous): the branded 404 appears; note whether the reconnect dialog flashes (an anonymous circuit is refused by the fallback policy). If it does, mark `Error.razor` and `NotFound.razor` with `@attribute [ExcludeFromInteractiveRouting]` and give `App.razor` a per-page render mode (`<Routes @rendermode="PageRenderMode" />` with `private IComponentRenderMode? PageRenderMode => HttpContext.AcceptsInteractiveRouting() ? InteractiveServer : null;`, the pattern of the Blazor Web App template) and record it in the PR description. Stop the app.

- [ ] **Step 7: Commit**

```bash
git add src tests docs
git diff --cached --stat
git commit -m "feat(admin): OIDC cookie sign-in, signed-out landing page and the API-backed agent session" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 5: The API connection and the reference-data clients (P07-T05)

**Review Focus pins:**
- **(5)** A mutating request must never be retried (a duplicate reply or note). Pinned by `ApiConnectionTests.A_post_is_never_retried_on_503` (POST, PUT and DELETE) and by `Both_named_clients_run_the_auth_handler_and_the_forwarded_ip_handler_and_only_the_read_client_retries`.
- **(2)** No request leaves a circuit without its token, and a token never reaches a response. Pinned here by `ApiConnectionTests.Reads_and_writes_both_carry_the_bearer_token_of_the_signed_in_agent` (the token comes from the real handler pipeline, through `IBlazorCircuitHttpClientFactory`), and in Task 6 for the attachment pass-through.

**Files:**
- Modify: `src/TechStrap.Admin/TechStrap.Admin.csproj` (two packages)
- Create: `src/TechStrap.Admin/Clients/ApiClientNames.cs`, `ProblemMapping.cs`, `ApiConnection.cs`, `ApiClientRegistration.cs`, `ReferenceDataClients.cs`
- Modify: `src/TechStrap.Admin/Program.cs`, `src/TechStrap.Admin/Components/Layout/MainLayout.razor`
- Create: `tests/Shared/AdminHost/AdminTestApi.cs`
- Modify: `tests/TechStrap.Admin.Tests/AdminFactory.cs`, `tests/TechStrap.Api.Tests/HostFactory.cs`
- Create: `tests/TechStrap.Admin.Tests/Clients/ApiHarness.cs`, `ApiConnectionTests.cs`, `ReferenceDataClientTests.cs`
- Create: `tests/TechStrap.Admin.Tests/AgentAccessHostTests.cs`

**Interfaces:**
- Consumes: Task 3 (`ApiOptions`, `AddBlazorTokenForwarding`, `StubApiHandler`), Task 4 (`ApiErrorCodes`, `IAgentsClient`, `AgentSession`, `AdminTestAuth`); `SyntaxCircus.Blazor.Auth` (`ApiAuthHandler`, `IBlazorCircuitHttpClientFactory`: `HttpClient CreateClient(string name)`, scoped, disposes the clients it created with the scope); `SyntaxCircus.Http.Resilience` `AddResilientHttpClient(IServiceCollection, string name, Action<HttpClient>? configureClient = null, int retryCount = 2, ...)` (retries every method on transport errors, timeouts and 408/429/5xx; exponential backoff from 2 seconds with jitter; has no method filter); `SyntaxCircus.AspNetCore.Common` `AddForwardedClientIp(this IHttpClientBuilder)` (**verified: it exists** in `SyntaxCircus.AspNetCore.Common` 0.1.15, in `ForwardedClientIpHttpClientBuilderExtensions`; it adds a handler that sets `X-Forwarded-For` from `HttpContext.Connection.RemoteIpAddress`, and does nothing when there is no HttpContext, as in a circuit); `SyntaxCircus.Common` `Result`.
- Produces:

```csharp
namespace TechStrap.Admin.Clients;

public static class ApiClientNames
{
    public const string Read = "techstrap-api-read";
    public const string Write = "techstrap-api-write";
}

public static class ApiClientRegistration
{
    public const int ReadRetryCount = 3;
    // Both clients: BaseAddress and Timeout from the validated ApiOptions (resolved when the client is first created),
    // .AddHttpMessageHandler<ApiAuthHandler>(), .AddForwardedClientIp(), a SocketsHttpHandler with PooledConnectionLifetime = 5 minutes.
    // Read = AddResilientHttpClient(Read, retryCount: 3); Write = AddHttpClient(Write) (no retry, no circuit breaker).
    // Registers scoped: ApiConnection, IAgentsClient, IProductsClient, ITagsClient (and, from Task 6, ITicketsClient and IRequestersClient).
    public static IServiceCollection AddTechStrapApiClients(this IServiceCollection services);
}

internal sealed class ApiConnection(IBlazorCircuitHttpClientFactory httpClients)   // scoped
{
    public Task<Result<T>> GetAsync<T>(string uri, CancellationToken cancellationToken);                                  // read client
    public Task<Result<T>> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken); // write client, JSON body, JSON answer
    public Task<Result> SendAsync(HttpMethod method, string uri, object? body, CancellationToken cancellationToken);       // write client, no answer body (204)
    public Task<Result<T>> SendContentAsync<T>(HttpMethod method, string uri, HttpContent content, CancellationToken cancellationToken);   // write client, prepared (multipart) content
}

internal static class ProblemMapping
{
    public static IReadOnlyList<ResultError> Map(HttpStatusCode status, string? body);
    public static ResultErrorKind KindOf(HttpStatusCode status);
}

internal static class ApiUri
{
    public static string Build(string path, params (string Key, object? Value)[] query);   // skips null and empty values, escapes, invariant formatting
}

public interface IProductsClient { Task<Result<IReadOnlyList<ProductDto>>> ListAsync(CancellationToken cancellationToken); }
public interface ITagsClient { Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken); }
// IAgentsClient is the Task 4 interface; AgentsClient implements GetMeAsync and ListAllAsync (pages of 100 until TotalCount is read, at most 100 pages).
```

  Mapping contract (what every component can rely on, from `ProblemMapping`): status 400 is `Validation`, 401 `Unauthenticated`, 403 `Forbidden`, 404 `NotFound`, 409 `Conflict`, anything else `Failure`. The code is the problem `type` (the API sends its error code there). A 400 carries one error per entry of `errorCodes`, with `Target` set to the field (null for the empty key) and the message taken from `errors[field][i]`, falling back to `detail`; without `errorCodes` it is one error with code `validation-failed`. The message is `detail`, then `title`, then a plain default. A response with no usable body gets `unauthenticated`, `forbidden`, `not-found`, `conflict`, `api-unavailable` (502, 503, 504) or `api-error`. A transport failure or open circuit is `api-unavailable`, a timeout the caller did not ask for is `api-timeout`, and a 2xx body that is not the expected JSON is `api-unexpected-response`. `OperationCanceledException` caused by the caller's token propagates.

  Test helpers:

```csharp
// tests/Shared/AdminHost/AdminTestApi.cs (public)
public static class AdminTestApi
{
    public static IServiceCollection AddStubApi(this IServiceCollection services, StubApiHandler stub);   // stub = primary handler of both named clients
}

// tests/TechStrap.Admin.Tests/Clients/ApiHarness.cs (internal): the clients wired as in Program, circuit-like scope (no HttpContext), token seeded in the server token cache
internal sealed class ApiHarness : IAsyncDisposable
{
    public static Task<ApiHarness> CreateAsync(AdminTestPrincipal? principal = null, bool seedToken = true);   // default principal: Agent
    public StubApiHandler Stub { get; }
    public IServiceProvider Services { get; }
    public T Get<T>() where T : notnull;     // from the scope: ApiConnection, IAgentsClient, ITicketsClient, ...
}
```

**Rules:**
1. **Two named clients, never a typed client from DI.** A circuit has no `HttpContext`, so the token reaches the handler only through a client made by `IBlazorCircuitHttpClientFactory.CreateClient(name)`, which needs a **named** client. Components inject the `I*Client` interfaces only; `ApiConnection` is the only class that touches an `HttpClient`, and it creates each of its two clients once per scope (the factory keeps every client it makes until the scope ends).
2. **Reads retry, writes never.** The read client is `AddResilientHttpClient(..., retryCount: 3)`. The write client is a plain `AddHttpClient`: no retry, no circuit breaker. The retrying pipeline's breaker therefore never sees write failures.
3. **Why not `ApiClientBase`:** it throws `ProblemDetailsException`, which keeps only `type`, `title`, `detail` and `errors`, so the specific validation codes (`row-version-required`, `status-after-invalid`, ...) in `errorCodes` are lost, and it has no `Result` mapping.
4. **The base address and timeout are read lazily** (`ConfigureHttpClient((services, client) => ...)` over the validated `ApiOptions`), never in `Program`.
5. **A `GET` retried three times can take about 14 seconds** (2, 4 and 8 seconds of backoff) before the failure reaches the agent; the tests therefore exercise one retry only, and use the write client for failure shapes.

- [ ] **Step 1: Write the failing tests**

1. `tests/Shared/AdminHost/AdminTestApi.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Clients;

namespace TechStrap.Tests.Shared.AdminHost;

public static class AdminTestApi
{
    /// <summary>
    /// Makes <paramref name="stub"/> the primary handler of both named API clients. The handlers above it (bearer token, forwarded IP, and the read client's
    /// retries) are the real ones, so a test sees what the API would see.
    /// </summary>
    public static IServiceCollection AddStubApi(this IServiceCollection services, StubApiHandler stub)
    {
        services.AddHttpClient(ApiClientNames.Read).ConfigurePrimaryHttpMessageHandler(() => stub);
        services.AddHttpClient(ApiClientNames.Write).ConfigurePrimaryHttpMessageHandler(() => stub);
        return services;
    }
}
```

2. Hook the stub into both factories. In `tests/TechStrap.Admin.Tests/AdminFactory.cs` add `services.AddStubApi(Api);` after `services.AddAdminTestAuthentication();` inside `ConfigureTestServices`, and fix the `Api` summary to "The stub behind the Admin's API clients. By default it answers GET /api/agents/me for the three test principals.". In `tests/TechStrap.Api.Tests/HostFactory.cs` add `services.AddStubApi(api);` after `services.AddAdminTestAuthentication();` in the private constructor's lambda, and fix the `Api` summary the same way.

3. `tests/TechStrap.Admin.Tests/Clients/ApiHarness.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Blazor.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>
/// The Admin's API clients wired exactly as in Program (<c>AddBlazorTokenForwarding</c> + <c>AddTechStrapApiClients</c>, so the real auth, forwarded-IP and
/// resilience handlers run) with a <see cref="StubApiHandler"/> as the primary handler and a circuit-like scope: no HttpContext, a fixed authentication state, the
/// principal's access token in the server token cache. Inject nothing but the interfaces under test.
/// </summary>
internal sealed class ApiHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    private ApiHarness(ServiceProvider provider, StubApiHandler stub)
    {
        _provider = provider;
        _scope = provider.CreateAsyncScope();
        Stub = stub;
    }

    public StubApiHandler Stub { get; }

    public IServiceProvider Services => _scope.ServiceProvider;

    public static async Task<ApiHarness> CreateAsync(AdminTestPrincipal? principal = null, bool seedToken = true)
    {
        principal ??= AdminTestPrincipal.Agent;
        var stub = new StubApiHandler();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(AdminTestSettings.With()).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddBlazorTokenForwarding(configuration, "Auth");
        services.AddScoped<AuthenticationStateProvider>(_ => new FixedAuthenticationStateProvider(principal.ToClaimsPrincipal("Test")));
        services.AddTechStrapApiClients();
        services.AddStubApi(stub);

        var provider = services.BuildServiceProvider();
        if (seedToken)
        {
            await provider.GetRequiredService<IServerTokenCache>().SetAsync(
                provider.GetRequiredService<IUserTokenCacheKeyProvider>().GetCacheKey(principal.Subject)!,
                new ServerTokenCacheEntry(principal.AccessToken, null, null, DateTimeOffset.UtcNow.AddDays(1)));
        }

        return new ApiHarness(provider, stub);
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }
}
```

4. `tests/TechStrap.Admin.Tests/Clients/ApiConnectionTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Blazor.Auth;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Tickets;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class ApiConnectionTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static TicketStateDto State() => new(Guid.NewGuid(), "ORB-1", "Open", "Normal", Guid.NewGuid(), null, false, [], DateTimeOffset.UtcNow, 7);

    [Fact]
    public async Task A_get_is_retried_on_503_and_then_succeeds()
    {
        await using var api = await ApiHarness.CreateAsync();
        var calls = 0;
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : StubApiHandler.JsonResponse(HttpStatusCode.OK, State()));

        var result = await api.Get<ApiConnection>().GetAsync<TicketStateDto>("api/thing", Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Count(HttpMethod.Get, "/api/thing").ShouldBe(2);
    }

    // Review Focus 5: a duplicate reply or note must never come from a retry.
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_post_is_never_retried_on_503(string verb)
    {
        await using var api = await ApiHarness.CreateAsync();
        var method = new HttpMethod(verb);
        api.Stub.OnStatus(method, "/api/thing", HttpStatusCode.ServiceUnavailable);

        var result = await api.Get<ApiConnection>().SendAsync<TicketStateDto>(method, "api/thing", new { note = "x" }, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(method, "/api/thing").ShouldBe(1);
    }

    [Fact]
    public async Task A_transport_failure_on_a_write_is_one_attempt_and_a_result_not_an_exception()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.On(HttpMethod.Post, "/api/thing", _ => throw new HttpRequestException("connection refused"));

        var result = await api.Get<ApiConnection>().SendAsync(HttpMethod.Post, "api/thing", new { }, Ct);

        result.Errors[0].Code.ShouldBe(ApiErrorCodes.ApiUnavailable);
        api.Stub.Count(HttpMethod.Post, "/api/thing").ShouldBe(1);
    }

    [Fact]
    public async Task A_timeout_the_caller_did_not_ask_for_is_a_failure()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.On(HttpMethod.Post, "/api/thing", _ => throw new TaskCanceledException("timed out", new TimeoutException()));

        var result = await api.Get<ApiConnection>().SendAsync(HttpMethod.Post, "api/thing", new { }, Ct);

        result.Errors[0].Code.ShouldBe(ApiErrorCodes.ApiTimeout);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_propagates_and_is_never_a_result()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", State());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => api.Get<ApiConnection>().GetAsync<TicketStateDto>("api/thing", cancelled.Token));
        await Should.ThrowAsync<OperationCanceledException>(() => api.Get<ApiConnection>().SendAsync(HttpMethod.Put, "api/thing", new { }, cancelled.Token));
    }

    [Fact]
    public async Task A_validation_failure_yields_the_codes_from_errorCodes_with_their_fields()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.On(HttpMethod.Put, "/api/thing", _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"type":"validation-failed","title":"One or more validation errors occurred.","status":400,"detail":"A row version is required.","errors":{"rowVersion":["A row version is required."],"":["Pick a status."]},"errorCodes":{"rowVersion":["row-version-required"],"":["status-invalid"]}}""",
                System.Text.Encoding.UTF8,
                "application/problem+json"),
        });

        var result = await api.Get<ApiConnection>().SendAsync<TicketStateDto>(HttpMethod.Put, "api/thing", new { }, Ct);

        result.Errors.Count.ShouldBe(2);
        result.Errors.ShouldAllBe(e => e.Kind == ResultErrorKind.Validation);
        var rowVersion = result.Errors.Single(e => e.Code == ApiErrorCodes.RowVersionRequired);
        rowVersion.Target.ShouldBe("rowVersion");
        rowVersion.Message.ShouldBe("A row version is required.");
        var general = result.Errors.Single(e => e.Code == "status-invalid");
        general.Target.ShouldBeNull();
        general.Message.ShouldBe("Pick a status.");
    }

    [Theory]
    [InlineData(409, "concurrency-conflict", ResultErrorKind.Conflict)]
    [InlineData(409, "ticket-closed", ResultErrorKind.Conflict)]
    [InlineData(409, "invalid-status-transition", ResultErrorKind.Conflict)]
    [InlineData(404, "ticket-not-found", ResultErrorKind.NotFound)]
    [InlineData(403, "agent-inactive", ResultErrorKind.Forbidden)]
    [InlineData(401, "unauthenticated", ResultErrorKind.Unauthenticated)]
    [InlineData(500, "unexpected", ResultErrorKind.Failure)]
    public async Task A_problem_keeps_the_api_code_and_maps_the_status_to_a_kind(int status, string type, ResultErrorKind kind)
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnProblem(HttpMethod.Delete, "/api/thing", (HttpStatusCode)status, type, "Something specific the agent should read.");

        var result = await api.Get<ApiConnection>().SendAsync(HttpMethod.Delete, "api/thing", null, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(type, "Something specific the agent should read.", kind));
    }

    [Theory]
    [InlineData(401, ApiErrorCodes.Unauthenticated, ResultErrorKind.Unauthenticated)]
    [InlineData(403, ApiErrorCodes.Forbidden, ResultErrorKind.Forbidden)]
    [InlineData(404, ApiErrorCodes.NotFound, ResultErrorKind.NotFound)]
    public async Task An_answer_without_a_body_still_gets_a_code_and_a_message(int status, string code, ResultErrorKind kind)
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnStatus(HttpMethod.Post, "/api/thing", (HttpStatusCode)status);

        var result = await api.Get<ApiConnection>().SendAsync(HttpMethod.Post, "api/thing", new { }, Ct);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(code);
        error.Kind.ShouldBe(kind);
        error.Message.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_success_with_a_body_that_is_not_json_is_an_unexpected_response()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.On(HttpMethod.Get, "/api/thing", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>proxy error</html>") });

        var result = await api.Get<ApiConnection>().GetAsync<TicketStateDto>("api/thing", Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.UnexpectedResponse);
    }

    // Review Focus 2: no request leaves a circuit without its token, and the token is added by the handler pipeline, not by the caller.
    [Fact]
    public async Task Reads_and_writes_both_carry_the_bearer_token_of_the_signed_in_agent()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", State()).OnJson(HttpMethod.Post, "/api/thing", State());
        var connection = api.Get<ApiConnection>();

        (await connection.GetAsync<TicketStateDto>("api/thing", Ct)).IsSuccess.ShouldBeTrue();
        (await connection.SendAsync<TicketStateDto>(HttpMethod.Post, "api/thing", new { }, Ct)).IsSuccess.ShouldBeTrue();

        api.Stub.Requests.Select(r => r.Authorization).ShouldBe(["Bearer " + AdminTestPrincipal.Admin.AccessToken, "Bearer " + AdminTestPrincipal.Admin.AccessToken]);
    }

    [Fact]
    public async Task Both_named_clients_run_the_auth_handler_and_the_forwarded_ip_handler_and_only_the_read_client_retries()
    {
        await using var api = await ApiHarness.CreateAsync();
        var factory = api.Get<IHttpMessageHandlerFactory>();

        string[] Chain(string name)
        {
            var names = new List<string>();
            for (var handler = factory.CreateHandler(name) as DelegatingHandler; handler is not null; handler = handler.InnerHandler as DelegatingHandler)
            {
                names.Add(handler.GetType().Name);
            }

            return [.. names];
        }

        var read = Chain(ApiClientNames.Read);
        var write = Chain(ApiClientNames.Write);

        foreach (var chain in new[] { read, write })
        {
            chain.ShouldContain(nameof(ApiAuthHandler));
            chain.ShouldContain("ForwardedClientIpHandler");
        }

        read.ShouldContain(name => name.Contains("Resilience", StringComparison.OrdinalIgnoreCase));
        write.ShouldNotContain(name => name.Contains("Resilience", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_clients_use_the_api_base_address_and_timeout_from_the_options()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnJson(HttpMethod.Get, "/api/thing", State());

        await api.Get<ApiConnection>().GetAsync<TicketStateDto>("api/thing", Ct);

        api.Get<IHttpClientFactory>().CreateClient(ApiClientNames.Read).BaseAddress.ShouldBe(new Uri("http://api.test/"));
        api.Get<IHttpClientFactory>().CreateClient(ApiClientNames.Write).Timeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void A_query_string_skips_empty_values_and_escapes_the_rest()
    {
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        ApiUri.Build("api/tickets", ("view", "Unassigned"), ("search", "a b&c"), ("status", null), ("tag", ""), ("tagId", id), ("page", 2))
            .ShouldBe("api/tickets?view=Unassigned&search=a%20b%26c&tagId=11111111-2222-3333-4444-555555555555&page=2");
        ApiUri.Build("api/tags").ShouldBe("api/tags");
    }
}
```

5. `tests/TechStrap.Admin.Tests/Clients/ReferenceDataClientTests.cs`:

```csharp
using System.Net;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class ReferenceDataClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static AgentListItemDto Agent(int n) => new(Guid.NewGuid(), $"Agent {n}", $"Agent {n} (Orbitly)", null, null, null, null);

    [Fact]
    public async Task Me_returns_the_agent_dto_from_the_me_route()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.WithTestAgents();

        var result = await api.Get<IAgentsClient>().GetMeAsync(Ct);

        result.Value.Role.ShouldBe(AgentRoles.Admin);
        result.Value.Email.ShouldBe(AdminTestPrincipal.Admin.Email);
        api.Stub.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/agents/me");
    }

    [Theory]
    [InlineData("agent-access-required")]
    [InlineData("agent-inactive")]
    [InlineData("agent-email-required")]
    [InlineData("agent-identity-invalid")]
    public async Task Me_maps_each_403_to_a_forbidden_result_carrying_the_api_code(string code)
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnProblem(HttpMethod.Get, "/api/agents/me", HttpStatusCode.Forbidden, code, "No access.");

        var result = await api.Get<IAgentsClient>().GetMeAsync(Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(code, "No access.", ResultErrorKind.Forbidden));
    }

    [Fact]
    public async Task ListAll_fetches_pages_of_100_until_every_agent_is_read()
    {
        await using var api = await ApiHarness.CreateAsync();
        var everyone = Enumerable.Range(1, 230).Select(Agent).ToList();
        api.Stub.On(HttpMethod.Get, "/api/agents", request =>
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.Query);
            var page = int.Parse(query["page"]!);
            query["pageSize"].ShouldBe("100");
            return StubApiHandler.JsonResponse(HttpStatusCode.OK, new PagedResponse<AgentListItemDto>([.. everyone.Skip((page - 1) * 100).Take(100)], page, 100, everyone.Count));
        });

        var result = await api.Get<IAgentsClient>().ListAllAsync(Ct);

        result.Value.Count.ShouldBe(230);
        api.Stub.Requests.Select(r => r.Query).ShouldBe(["?page=1&pageSize=100", "?page=2&pageSize=100", "?page=3&pageSize=100"]);
    }

    [Fact]
    public async Task ListAll_stops_on_an_empty_page_and_returns_the_first_failure()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnJson(HttpMethod.Get, "/api/agents", new PagedResponse<AgentListItemDto>([], 1, 100, 500));
        (await api.Get<IAgentsClient>().ListAllAsync(Ct)).Value.ShouldBeEmpty();

        api.Stub.OnProblem(HttpMethod.Get, "/api/agents", HttpStatusCode.Forbidden, "agent-inactive", "Deactivated.");
        (await api.Get<IAgentsClient>().ListAllAsync(Ct)).Errors[0].Code.ShouldBe("agent-inactive");
    }

    [Fact]
    public async Task Products_and_tags_are_read_as_lists()
    {
        await using var api = await ApiHarness.CreateAsync();
        var product = new ProductDto(Guid.NewGuid(), "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#2563EB", "#FFFFFF", "#1E40AF", null, null), 3);
        api.Stub.OnJson(HttpMethod.Get, "/api/products", new[] { product });
        api.Stub.OnJson(HttpMethod.Get, "/api/tags", new[] { new TagDto(Guid.NewGuid(), "bug", "Bug", "#DC2626") });

        (await api.Get<IProductsClient>().ListAsync(Ct)).Value.Single().NumberPrefix.ShouldBe("ORB");
        (await api.Get<ITagsClient>().ListAsync(Ct)).Value.Single().Slug.ShouldBe("bug");
    }
}
```

6. `tests/TechStrap.Admin.Tests/AgentAccessHostTests.cs` (Review Focus 1 at the host; it needs the gate wired in Step 4):

```csharp
using System.Net;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>Review Focus 1 at the host: the API's answer to /api/agents/me decides what the signed-in user is shown.</summary>
public sealed class AgentAccessHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_sees_the_page_and_the_api_was_asked_who_they_are_first()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("shell-placeholder");
        html.ShouldNotContain("You don't have access");
        factory.Api.Requests.First().Path.ShouldBe("/api/agents/me");
        factory.Api.Requests.First().Authorization.ShouldBe("Bearer " + AdminTestPrincipal.Agent.AccessToken);
    }

    [Fact]
    public async Task A_user_the_api_refuses_sees_no_access_and_never_the_page()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        using var response = await client.GetAsync("/", Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await new AngleSharp.Html.Parser.HtmlParser().ParseDocumentAsync(html, Ct)).QuerySelector("section.ts-no-access h1")!.TextContent.ShouldBe("You don't have access to TechStrap.");
        html.ShouldContain("not in the techstrap-agents group");
        html.ShouldNotContain("shell-placeholder");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
    }

    [Fact]
    public async Task A_deactivated_agent_is_refused_the_same_way()
    {
        await using var factory = new AdminFactory();
        factory.Api.OnProblem(HttpMethod.Get, "/api/agents/me", HttpStatusCode.Forbidden, "agent-inactive", "Deactivated.");
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("has been deactivated");
        html.ShouldNotContain("shell-placeholder");
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "ApiConnectionTests|ReferenceDataClientTests|AgentAccessHostTests"`
Expected: a build failure (`ApiConnection`, `ApiClientNames`, `AddTechStrapApiClients`, `IProductsClient` do not exist).

- [ ] **Step 3: Implement**

1. `src/TechStrap.Admin/TechStrap.Admin.csproj`: add `<PackageReference Include="SyntaxCircus.Common" />` and `<PackageReference Include="SyntaxCircus.Http.Resilience" />` in alphabetical position. The versions are central (0.1.3 and 0.2.2). `Polly.CircuitBreaker` (used to recognise an open circuit) arrives transitively through `SyntaxCircus.Http.Resilience`.

2. `src/TechStrap.Admin/Clients/ApiClientNames.cs`:

```csharp
namespace TechStrap.Admin.Clients;

/// <summary>The two named HTTP clients every API call goes through (D-040). Both run the auth handler; only the read client retries.</summary>
public static class ApiClientNames
{
    /// <summary>Idempotent GETs: retried up to <see cref="ApiClientRegistration.ReadRetryCount"/> times on transport errors, timeouts and 408/429/5xx.</summary>
    public const string Read = "techstrap-api-read";

    /// <summary>Every POST, PUT and DELETE: no retry and no circuit breaker, so a transient failure can never duplicate a reply or a note.</summary>
    public const string Write = "techstrap-api-write";
}
```

3. `src/TechStrap.Admin/Clients/ProblemMapping.cs`:

```csharp
using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;

namespace TechStrap.Admin.Clients;

/// <summary>
/// Turns a non-success API response into <see cref="Result"/> errors. The API answers with RFC 7807 problem details whose <c>type</c> is the error code;
/// a validation failure (400) carries <c>type</c> "validation-failed" and the specific codes in the <c>errorCodes</c> extension, keyed by field.
/// </summary>
internal static class ProblemMapping
{
    public static IReadOnlyList<ResultError> Map(HttpStatusCode status, string? body)
    {
        var problem = Problem.TryParse(body);
        var kind = KindOf(status);
        var message = problem.Detail ?? problem.Title ?? DefaultMessage(status);

        if (kind == ResultErrorKind.Validation)
        {
            var errors = ValidationErrors(problem, message);
            if (errors.Count > 0)
            {
                return errors;
            }
        }

        var code = !string.IsNullOrWhiteSpace(problem.Type) ? problem.Type : DefaultCode(status);
        return [new ResultError(code, message, kind)];
    }

    public static ResultErrorKind KindOf(HttpStatusCode status) => (int)status switch
    {
        400 => ResultErrorKind.Validation,
        401 => ResultErrorKind.Unauthenticated,
        403 => ResultErrorKind.Forbidden,
        404 => ResultErrorKind.NotFound,
        409 => ResultErrorKind.Conflict,
        _ => ResultErrorKind.Failure,
    };

    private static List<ResultError> ValidationErrors(Problem problem, string fallbackMessage)
    {
        var errors = new List<ResultError>();
        foreach (var (field, codes) in problem.ErrorCodes)
        {
            for (var i = 0; i < codes.Length; i++)
            {
                var messages = problem.Errors.GetValueOrDefault(field);
                var message = messages is not null && i < messages.Length ? messages[i] : fallbackMessage;
                errors.Add(new ResultError(codes[i], message, ResultErrorKind.Validation, field.Length == 0 ? null : field));
            }
        }

        return errors;
    }

    private static string DefaultCode(HttpStatusCode status) => (int)status switch
    {
        400 => ApiErrorCodes.ValidationFailed,
        401 => ApiErrorCodes.Unauthenticated,
        403 => ApiErrorCodes.Forbidden,
        404 => ApiErrorCodes.NotFound,
        409 => ApiErrorCodes.Conflict,
        502 or 503 or 504 => ApiErrorCodes.ApiUnavailable,
        _ => ApiErrorCodes.ApiError,
    };

    private static string DefaultMessage(HttpStatusCode status) => (int)status switch
    {
        401 => "Your session has expired. Sign in again.",
        403 => "You do not have access to this.",
        404 => "That item no longer exists.",
        502 or 503 or 504 => "TechStrap could not reach the API. Try again in a moment.",
        _ => "The request failed. Try again, and tell an admin if it keeps happening.",
    };

    private sealed record Problem(
        string? Type,
        string? Title,
        string? Detail,
        IReadOnlyDictionary<string, string[]> Errors,
        IReadOnlyDictionary<string, string[]> ErrorCodes)
    {
        private static readonly Problem Empty = new(null, null, null, new Dictionary<string, string[]>(), new Dictionary<string, string[]>());

        public static Problem TryParse(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return Empty;
                }

                return new Problem(
                    Text(root, "type"),
                    Text(root, "title"),
                    Text(root, "detail"),
                    Dictionary(root, "errors"),
                    Dictionary(root, "errorCodes"));
            }
            catch (JsonException)
            {
                return Empty;
            }
        }

        private static string? Text(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString() : null;

        private static Dictionary<string, string[]> Dictionary(JsonElement root, string name)
        {
            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in value.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Array))
                {
                    result[property.Name] = [.. property.Value.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)];
                }
            }

            return result;
        }
    }
}
```

4. `src/TechStrap.Admin/Clients/ApiConnection.cs` (with `ApiUri`):

```csharp
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Polly.CircuitBreaker;
using SyntaxCircus.Blazor.Auth;
using SyntaxCircus.Common;

namespace TechStrap.Admin.Clients;

/// <summary>
/// The only place the Admin talks HTTP to the API (D-040). It builds requests, sends them through the right named client (reads: retried; writes: never
/// retried) obtained from <see cref="IBlazorCircuitHttpClientFactory"/> so the circuit's token reaches the auth handler, and reads the answer into a
/// <see cref="Result"/>. It does not use ApiClientBase because that drops the <c>errorCodes</c> the API sends and has no Result mapping. Cancellation by the
/// caller propagates as <see cref="OperationCanceledException"/>; it is never turned into a Result. One instance per scope (circuit).
/// </summary>
internal sealed class ApiConnection(IBlazorCircuitHttpClientFactory httpClients)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // One HttpClient per name for the life of the scope: the factory keeps every client it creates until the scope ends.
    private HttpClient? _read;
    private HttpClient? _write;

    private HttpClient ReadClient => _read ??= httpClients.CreateClient(ApiClientNames.Read);

    private HttpClient WriteClient => _write ??= httpClients.CreateClient(ApiClientNames.Write);

    /// <summary>GET through the retrying read client.</summary>
    public async Task<Result<T>> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        return await SendAsync<T>(ReadClient, request, cancellationToken);
    }

    /// <summary>A POST, PUT or DELETE with an optional JSON body, through the write client; the answer carries a JSON body.</summary>
    public async Task<Result<T>> SendAsync<T>(HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Json) };
        return await SendAsync<T>(WriteClient, request, cancellationToken);
    }

    /// <summary>A POST, PUT or DELETE with an optional JSON body, through the write client; success has no body (204).</summary>
    public async Task<Result> SendAsync(HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: Json) };
        return await SendAsync(WriteClient, request, cancellationToken);
    }

    /// <summary>A write with a prepared body (the multipart reply). The content is used once: the caller builds a new one for every attempt.</summary>
    public async Task<Result<T>> SendContentAsync<T>(HttpMethod method, string uri, HttpContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        return await SendAsync<T>(WriteClient, request, cancellationToken);
    }

    private static async Task<Result<T>> SendAsync<T>(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                return Result<T>.Failure(errors[0], [.. errors.Skip(1)]);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
            return value is null ? Result<T>.Failure(Unexpected()) : Result<T>.Success(value);
        }
        catch (JsonException)
        {
            return Result<T>.Failure(Unexpected());
        }
        catch (Exception ex) when (Transport(ex, cancellationToken) is { } error)
        {
            return Result<T>.Failure(error);
        }
    }

    private static async Task<Result> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return Result.Success();
            }

            var errors = ProblemMapping.Map(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            return Result.Failure(errors[0], [.. errors.Skip(1)]);
        }
        catch (Exception ex) when (Transport(ex, cancellationToken) is { } error)
        {
            return Result.Failure(error);
        }
    }

    /// <summary>
    /// A transport failure (unreachable API, timeout, open circuit) as a Result error. Returns null for anything else, including a cancellation requested by the caller,
    /// which must keep propagating.
    /// </summary>
    internal static ResultError? Transport(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        OperationCanceledException or TimeoutException => new ResultError(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again.", ResultErrorKind.Failure),
        HttpRequestException or BrokenCircuitException => new ResultError(ApiErrorCodes.ApiUnavailable, "TechStrap could not reach the API. Try again in a moment.", ResultErrorKind.Failure),
        _ => null,
    };

    private static ResultError Unexpected() =>
        new(ApiErrorCodes.UnexpectedResponse, "The API answered in a form TechStrap did not expect. Tell an admin if it keeps happening.", ResultErrorKind.Failure);
}

/// <summary>Builds a request URI with a query string, skipping null and empty values. Values are formatted invariantly and escaped.</summary>
internal static class ApiUri
{
    public static string Build(string path, params (string Key, object? Value)[] query)
    {
        var parts = query
            .Where(q => q.Value is not null && Format(q.Value).Length > 0)
            .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(Format(q.Value!))}")
            .ToList();
        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }

    private static string Format(object value) => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty;
}
```

5. `src/TechStrap.Admin/Clients/ReferenceDataClients.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Clients;

/// <summary>Active products for an Agent, all products for an Admin.</summary>
public interface IProductsClient
{
    Task<Result<IReadOnlyList<ProductDto>>> ListAsync(CancellationToken cancellationToken);
}

public interface ITagsClient
{
    Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken);
}

internal sealed class AgentsClient(ApiConnection connection) : IAgentsClient
{
    public const int PageSize = 100;

    // Stops a misbehaving API (a TotalCount that never converges) from looping for ever: 100 pages of 100 is 10 000 agents.
    private const int MaxPages = 100;

    public Task<Result<AgentDto>> GetMeAsync(CancellationToken cancellationToken) => connection.GetAsync<AgentDto>("api/agents/me", cancellationToken);

    public async Task<Result<IReadOnlyList<AgentListItemDto>>> ListAllAsync(CancellationToken cancellationToken)
    {
        var all = new List<AgentListItemDto>();
        for (var page = 1; page <= MaxPages; page++)
        {
            var result = await connection.GetAsync<PagedResponse<AgentListItemDto>>(ApiUri.Build("api/agents", ("page", page), ("pageSize", PageSize)), cancellationToken);
            if (result.IsFailure)
            {
                return Result<IReadOnlyList<AgentListItemDto>>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
            }

            all.AddRange(result.Value.Items);
            if (result.Value.Items.Count == 0 || all.Count >= result.Value.TotalCount)
            {
                break;
            }
        }

        return Result<IReadOnlyList<AgentListItemDto>>.Success(all);
    }
}

internal sealed class ProductsClient(ApiConnection connection) : IProductsClient
{
    public async Task<Result<IReadOnlyList<ProductDto>>> ListAsync(CancellationToken cancellationToken) =>
        Narrow(await connection.GetAsync<List<ProductDto>>("api/products", cancellationToken));

    internal static Result<IReadOnlyList<T>> Narrow<T>(Result<List<T>> result) =>
        result.IsSuccess ? Result<IReadOnlyList<T>>.Success(result.Value) : Result<IReadOnlyList<T>>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
}

internal sealed class TagsClient(ApiConnection connection) : ITagsClient
{
    public async Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken) =>
        ProductsClient.Narrow(await connection.GetAsync<List<TagDto>>("api/tags", cancellationToken));
}
```

6. `src/TechStrap.Admin/Clients/ApiClientRegistration.cs`. Task 6 adds the last two registrations; leave them out now:

```csharp
using Microsoft.Extensions.Options;
using SyntaxCircus.AspNetCore.Common;
using SyntaxCircus.Blazor.Auth;
using SyntaxCircus.Http.Resilience;

namespace TechStrap.Admin.Clients;

public static class ApiClientRegistration
{
    public const int ReadRetryCount = 3;

    // A circuit can live for hours: refresh pooled connections so a DNS change of the API is picked up.
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Registers the two named API clients and the scoped typed clients (D-040). Both named clients run the auth handler (bearer token) and forward the
    /// caller's IP; only <see cref="ApiClientNames.Read"/> retries. Components inject the <c>I*Client</c> interfaces, never an HttpClient.
    /// The base address and timeout come from the validated <c>Api</c> options when a client is first created.
    /// </summary>
    public static IServiceCollection AddTechStrapApiClients(this IServiceCollection services)
    {
        services.AddResilientHttpClient(ApiClientNames.Read, retryCount: ReadRetryCount)
            .ConfigureHttpClient(ConfigureClient)
            .AddHttpMessageHandler<ApiAuthHandler>()
            .AddForwardedClientIp()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime });

        services.AddHttpClient(ApiClientNames.Write)
            .ConfigureHttpClient(ConfigureClient)
            .AddHttpMessageHandler<ApiAuthHandler>()
            .AddForwardedClientIp()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = ConnectionLifetime });

        services.AddScoped<ApiConnection>();
        services.AddScoped<IAgentsClient, AgentsClient>();
        services.AddScoped<IProductsClient, ProductsClient>();
        services.AddScoped<ITagsClient, TagsClient>();
        return services;
    }

    private static void ConfigureClient(IServiceProvider services, HttpClient client)
    {
        var api = services.GetRequiredService<IOptions<ApiOptions>>().Value;
        client.BaseAddress = new Uri(api.BaseUrl.EndsWith('/') ? api.BaseUrl : api.BaseUrl + "/");
        client.Timeout = TimeSpan.FromSeconds(api.TimeoutSeconds);
    }
}
```

7. `src/TechStrap.Admin/Program.cs`: add `using TechStrap.Admin.Clients;` and, after `builder.Services.AddCascadingAuthenticationState();`, `builder.Services.AddTechStrapApiClients();`.

- [ ] **Step 4: Wire the gate into the layout**

`IAgentsClient` now has an implementation, so the layout can ask the API who is signed in before it renders any page. In `src/TechStrap.Admin/Components/Layout/MainLayout.razor` change `@Body` inside the error boundary to `<AgentGate>@Body</AgentGate>`:

```razor
        <GlobalErrorBoundary BoundaryName="admin UI"
                             CssClass="ts-error"
                             Title="@UiCopy.ErrorTitle"
                             Description="@UiCopy.ErrorDescription"
                             HomeLabel="@UiCopy.ErrorHomeLabel">
            <AgentGate>@Body</AgentGate>
        </GlobalErrorBoundary>
```

An anonymous visitor (the error and not-found pages) renders straight through, a signed-in user waits for `GET /api/agents/me`.

- [ ] **Step 5: Run the tests**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "ShellHostTests|Admin_reports_live_only|AdminHostRedactionTests|EnvExampleCompletenessTests"
```

Expected: PASS. The retry test takes about 2 seconds (Polly's first backoff). The signed-in host tests now cause one `GET /api/agents/me` against the stub, which answers it by default.

- [ ] **Step 6: Commit**

```bash
git add src tests
git diff --cached --stat
git commit -m "feat(admin): API connection with read and write clients, problem-details Result mapping and reference-data clients" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 6: The tickets client, the requesters client and the attachment pass-through (P07-T06, P07-T10 host part)

**Files:**
- Create: `src/TechStrap.Admin/Clients/TicketsClient.cs` (the `ITicketsClient` and `IRequestersClient` interfaces, `ReplyAttachment`, and their implementations)
- Create: `src/TechStrap.Admin/Clients/AttachmentPassThrough.cs`
- Modify: `src/TechStrap.Admin/Clients/ApiClientRegistration.cs`, `src/TechStrap.Admin/Program.cs`
- Create: `tests/TechStrap.Admin.Tests/Clients/TicketsClientTests.cs`, `tests/TechStrap.Admin.Tests/AttachmentPassThroughTests.cs`

**Review Focus pin (2):** the attachment pass-through must not leak the token into its response, and must not buffer. Pinned by `AttachmentPassThroughTests.The_file_is_streamed_for_the_signed_in_agent_as_a_forced_download` (the API request carries the agent's bearer token; the response carries none) and `The_body_is_streamed_not_buffered`.

**Interfaces:**
- Consumes: Task 5 (`ApiConnection`, `ApiUri`, `ApiClientNames`, `ApiHarness`, `AdminFactory.Api`); Contracts `ListTicketsRequest`, `AddAgentReplyRequest`, `AddInternalNoteRequest`, `ChangeTicketStatusRequest`, `AssignTicketRequest`, `ChangeTicketPriorityRequest`, `MoveTicketProductRequest`, `AddTicketTagRequest`, `MarkTicketSpamRequest`, `TicketSummaryDto`, `TicketDetailDto`, `TicketStateDto`, `AgentMessageResponse`, `TicketViewCountsResponse`, `PagedResponse<T>`.
- Produces:

```csharp
namespace TechStrap.Admin.Clients;

/// <summary>A file to send with a reply. OpenRead is called when the request is built and the stream is disposed after the send, so the same record can be sent again after a failure.</summary>
public sealed record ReplyAttachment(string FileName, string ContentType, Func<Stream> OpenRead);

public interface ITicketsClient
{
    Task<Result<PagedResponse<TicketSummaryDto>>> ListAsync(ListTicketsRequest request, CancellationToken cancellationToken);   // GET api/tickets?view&productId&status&priority&assigneeId&tagId&requesterId&search&page&pageSize
    Task<Result<TicketViewCountsResponse>> GetCountsAsync(CancellationToken cancellationToken);                                  // GET api/tickets/counts
    Task<Result<TicketDetailDto>> GetAsync(string reference, CancellationToken cancellationToken);                              // GET api/tickets/{id or number}; 404 = ticket-not-found
    Task<Result<AgentMessageResponse>> ReplyAsync(Guid ticketId, AddAgentReplyRequest request, IReadOnlyList<ReplyAttachment> attachments, CancellationToken cancellationToken);   // POST .../replies, multipart/form-data
    Task<Result<AgentMessageResponse>> AddNoteAsync(Guid ticketId, AddInternalNoteRequest request, CancellationToken cancellationToken);             // POST .../notes, JSON
    Task<Result<TicketStateDto>> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken);          // PUT .../status
    Task<Result<TicketStateDto>> AssignAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken);                       // PUT .../assignee (AssigneeId null = unassign)
    Task<Result<TicketStateDto>> ChangePriorityAsync(Guid ticketId, ChangeTicketPriorityRequest request, CancellationToken cancellationToken);      // PUT .../priority
    Task<Result<TicketStateDto>> MoveProductAsync(Guid ticketId, MoveTicketProductRequest request, CancellationToken cancellationToken);            // PUT .../product
    Task<Result<TicketStateDto>> AddTagAsync(Guid ticketId, AddTicketTagRequest request, CancellationToken cancellationToken);                       // POST .../tags (idempotent)
    Task<Result<TicketStateDto>> RemoveTagAsync(Guid ticketId, Guid tagId, uint rowVersion, CancellationToken cancellationToken);                    // DELETE .../tags/{tagId}?rowVersion=N
    Task<Result<TicketStateDto>> SetSpamAsync(Guid ticketId, MarkTicketSpamRequest request, CancellationToken cancellationToken);                    // PUT .../spam (IsSpam false = Not spam)
    Task<Result> DeleteAsync(Guid ticketId, CancellationToken cancellationToken);                                                                    // DELETE api/tickets/{id} (Admin), 204
}

public interface IRequestersClient
{
    Task<Result> EraseAsync(Guid requesterId, CancellationToken cancellationToken);   // POST api/requesters/{id}/erase (Admin), 204, idempotent
}

public static class AttachmentPassThrough
{
    public const string Route = "/attachments/{id:guid}";
    public static IEndpointRouteBuilder MapAttachmentPassThrough(this IEndpointRouteBuilder endpoints);
}
```

  Every write sends the `RowVersion` the caller passes in its request record (a stale one is a Conflict result with code `concurrency-conflict`; a missing one is a validation error `row-version-required`). Tickets other than the reply and the note answer 200 with the new `TicketStateDto`; the reply and the note answer 201 with `AgentMessageResponse`.

**Rules:**
1. **Component contract.** Components call these interfaces with the `RowVersion` they hold, and replace it from the returned `TicketStateDto.RowVersion`. They never build a URL.
2. **The reply is multipart; the note is JSON.** The form fields are the ones `AgentReplyForm` binds: `Body`, `LinkedArticleIds` (one field per id), `StatusAfter`, `RowVersion` and the files as `Attachments`. Empty `StatusAfter` is not sent.
3. **The pass-through is request-scoped.** It takes `IHttpClientFactory` (a request has an `HttpContext`, so the auth handler resolves the token from the cookie, not from a circuit), uses `ApiClientNames.Read` and `HttpCompletionOption.ResponseHeadersRead`, and copies the body with `CopyToAsync`: it never buffers. It sets `Content-Disposition: attachment` (keeping only the file name of the upstream header), `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store` and `Content-Security-Policy: sandbox`, whatever the API sent. An upstream 404 is a 404, 401 and 403 pass on, any other failure is 502. No upstream detail is ever echoed. Only a Guid matches the route.
4. **Architecture (D-017).** The pass-through is an exempt adapter: no workflow, no persistence. Task 13's docs record it; PHASE-07c adds an architecture test that components never inject `HttpClient`.

- [ ] **Step 1: Write the failing tests**

1. `tests/TechStrap.Admin.Tests/Clients/TicketsClientTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class TicketsClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid TicketId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static TicketStateDto State(uint rowVersion = 8) => new(TicketId, "ORB-1", "Pending", "High", Guid.NewGuid(), null, false, [], DateTimeOffset.UtcNow, rowVersion);

    private static async Task<(ApiHarness Api, ITicketsClient Client)> StartAsync()
    {
        var api = await ApiHarness.CreateAsync();
        return (api, api.Get<ITicketsClient>());
    }

    private static JsonElement Body(ApiHarness api) => JsonDocument.Parse(api.Stub.Requests.Last().Body!).RootElement;

    [Fact]
    public async Task List_sends_every_filter_and_the_paging_as_the_query_string()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, "/api/tickets", new PagedResponse<TicketSummaryDto>([], 2, 25, 0));
        var productId = Guid.NewGuid();

        var result = await client.ListAsync(new ListTicketsRequest(TicketViews.Unassigned, productId, null, TicketPriorities.High, null, null, null, "login loop", 2, 25), Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe($"?view=Unassigned&productId={productId}&priority=High&search=login%20loop&page=2&pageSize=25");
    }

    [Fact]
    public async Task Get_accepts_an_id_or_a_number_and_a_404_keeps_the_ticket_not_found_code()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnProblem(HttpMethod.Get, "/api/tickets/ORB-404", HttpStatusCode.NotFound, ApiErrorCodes.TicketNotFound, "No such ticket.");

        var result = await client.GetAsync("ORB-404", Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.TicketNotFound, "No such ticket.", ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task Counts_are_read_from_the_counts_route()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, "/api/tickets/counts", new TicketViewCountsResponse(1, 2, 3, 4, 5, 6));

        (await client.GetCountsAsync(Ct)).Value.Spam.ShouldBe(6);
    }

    [Fact]
    public async Task Each_state_change_uses_its_verb_route_and_sends_the_row_version_in_the_body()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        var assignee = Guid.NewGuid();
        var product = Guid.NewGuid();
        var tag = Guid.NewGuid();
        foreach (var (method, suffix) in new[]
        {
            (HttpMethod.Put, "status"), (HttpMethod.Put, "assignee"), (HttpMethod.Put, "priority"),
            (HttpMethod.Put, "product"), (HttpMethod.Post, "tags"), (HttpMethod.Put, "spam"),
        })
        {
            api.Stub.OnJson(method, $"/api/tickets/{TicketId}/{suffix}", State());
        }

        (await client.ChangeStatusAsync(TicketId, new ChangeTicketStatusRequest(TicketStatuses.Solved, 7), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("rowVersion").GetUInt32().ShouldBe(7u);
        Body(api).GetProperty("status").GetString().ShouldBe("Solved");

        (await client.AssignAsync(TicketId, new AssignTicketRequest(assignee, 8), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("assigneeId").GetGuid().ShouldBe(assignee);

        (await client.AssignAsync(TicketId, new AssignTicketRequest(null, 9), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("assigneeId").ValueKind.ShouldBe(JsonValueKind.Null);

        (await client.ChangePriorityAsync(TicketId, new ChangeTicketPriorityRequest(TicketPriorities.Urgent, 10), Ct)).IsSuccess.ShouldBeTrue();
        (await client.MoveProductAsync(TicketId, new MoveTicketProductRequest(product, 11), Ct)).IsSuccess.ShouldBeTrue();
        (await client.AddTagAsync(TicketId, new AddTicketTagRequest(tag, 12), Ct)).IsSuccess.ShouldBeTrue();
        (await client.SetSpamAsync(TicketId, new MarkTicketSpamRequest(false, 13), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("isSpam").GetBoolean().ShouldBeFalse();

        api.Stub.Requests.Select(r => $"{r.Method} {r.Path}").ShouldBe(
        [
            $"PUT /api/tickets/{TicketId}/status", $"PUT /api/tickets/{TicketId}/assignee", $"PUT /api/tickets/{TicketId}/assignee",
            $"PUT /api/tickets/{TicketId}/priority", $"PUT /api/tickets/{TicketId}/product", $"POST /api/tickets/{TicketId}/tags", $"PUT /api/tickets/{TicketId}/spam",
        ]);
    }

    [Fact]
    public async Task Removing_a_tag_sends_the_row_version_in_the_query_and_no_body()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        var tag = Guid.NewGuid();
        api.Stub.OnJson(HttpMethod.Delete, $"/api/tickets/{TicketId}/tags/{tag}", State());

        var result = await client.RemoveTagAsync(TicketId, tag, 21, Ct);

        result.Value.RowVersion.ShouldBe(8u);
        var request = api.Stub.Requests.ShouldHaveSingleItem();
        request.Query.ShouldBe("?rowVersion=21");
        request.Body.ShouldBeNull();
    }

    [Fact]
    public async Task A_stale_row_version_is_a_conflict_with_the_concurrency_code_and_the_call_is_not_retried()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnProblem(HttpMethod.Put, $"/api/tickets/{TicketId}/status", HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict, "The ticket changed.");

        var result = await client.ChangeStatusAsync(TicketId, new ChangeTicketStatusRequest(TicketStatuses.Open, 1), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ConcurrencyConflict, "The ticket changed.", ResultErrorKind.Conflict));
        api.Stub.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_note_is_json_and_a_reply_is_multipart_with_the_fields_and_files_the_api_binds()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        var response = new AgentMessageResponse(
            new MessageDto(Guid.NewGuid(), "Agent", Guid.NewGuid(), "Sam", "Public", "<p>hi</p>", DateTimeOffset.UtcNow, [], []), State());
        api.Stub.OnJson(HttpMethod.Post, $"/api/tickets/{TicketId}/notes", response, HttpStatusCode.Created);
        api.Stub.OnJson(HttpMethod.Post, $"/api/tickets/{TicketId}/replies", response, HttpStatusCode.Created);
        var article = Guid.NewGuid();
        var opened = 0;

        (await client.AddNoteAsync(TicketId, new AddInternalNoteRequest("heads up", 5), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("body").GetString().ShouldBe("heads up");

        var reply = await client.ReplyAsync(
            TicketId,
            new AddAgentReplyRequest("Thanks!", [article], TicketStatuses.Solved, 6),
            [new ReplyAttachment("log.txt", "text/plain", () => { opened++; return new MemoryStream("file-bytes"u8.ToArray()); })],
            Ct);

        reply.Value.Ticket.RowVersion.ShouldBe(8u);
        var sent = api.Stub.Requests.Last();
        sent.ContentType.ShouldStartWith("multipart/form-data");
        sent.Body.ShouldNotBeNull();
        sent.Body.ShouldContain("name=Body");
        sent.Body.ShouldContain("Thanks!");
        sent.Body.ShouldContain("name=LinkedArticleIds");
        sent.Body.ShouldContain(article.ToString());
        sent.Body.ShouldContain("name=StatusAfter");
        sent.Body.ShouldContain("Solved");
        sent.Body.ShouldContain("name=RowVersion");
        sent.Body.ShouldContain("name=Attachments; filename=log.txt");
        sent.Body.ShouldContain("file-bytes");
        opened.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_reply_can_be_sent_again_because_the_files_are_opened_for_each_attempt()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnProblem(HttpMethod.Post, $"/api/tickets/{TicketId}/replies", HttpStatusCode.ServiceUnavailable, "unavailable", "Down.");
        var opened = 0;
        ReplyAttachment[] files = [new("a.png", "image/png", () => { opened++; return new MemoryStream([1, 2, 3]); })];

        (await client.ReplyAsync(TicketId, new AddAgentReplyRequest("x", null, null, 1), files, Ct)).IsFailure.ShouldBeTrue();
        (await client.ReplyAsync(TicketId, new AddAgentReplyRequest("x", null, null, 1), files, Ct)).IsFailure.ShouldBeTrue();

        opened.ShouldBe(2);
        api.Stub.Count(HttpMethod.Post, $"/api/tickets/{TicketId}/replies").ShouldBe(2);
    }

    [Fact]
    public async Task Delete_and_erase_are_bodyless_and_a_403_keeps_the_admin_code()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        var requester = Guid.NewGuid();
        api.Stub.OnStatus(HttpMethod.Delete, $"/api/tickets/{TicketId}", HttpStatusCode.NoContent);
        api.Stub.OnProblem(HttpMethod.Post, $"/api/requesters/{requester}/erase", HttpStatusCode.Forbidden, ApiErrorCodes.AdminAccessRequired, "Admins only.");

        (await client.DeleteAsync(TicketId, Ct)).IsSuccess.ShouldBeTrue();
        var erase = await api.Get<IRequestersClient>().EraseAsync(requester, Ct);

        erase.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.AdminAccessRequired);
        api.Stub.Requests.ShouldAllBe(r => r.Body == null);
    }
}
```

2. `tests/TechStrap.Admin.Tests/AttachmentPassThroughTests.cs` (the 404 case reads the branded not-found page, which is why it asserts the API detail is absent rather than an empty body):

```csharp
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

public sealed class AttachmentPassThroughTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static HttpResponseMessage File(byte[] bytes, string contentType, string disposition)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        response.Content.Headers.ContentDisposition = ContentDispositionHeaderValue.Parse(disposition);
        return response;
    }

    [Fact]
    public async Task The_file_is_streamed_for_the_signed_in_agent_as_a_forced_download()
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        var bytes = "<svg onload=alert(1)></svg>"u8.ToArray();
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{id}", _ => File(bytes, "image/svg+xml", "inline; filename=\"logo.svg\""));
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync($"/attachments/{id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(bytes);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/svg+xml");
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition.FileName.ShouldBe("logo.svg");
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.CacheControl.Private.ShouldBeTrue();
        // The agent's bearer token went to the API, and nowhere in the answer.
        factory.Api.Requests.Last(r => r.Path.StartsWith("/api/attachments", StringComparison.Ordinal)).Authorization.ShouldBe("Bearer " + AdminTestPrincipal.Agent.AccessToken);
        response.ToString().ShouldNotContain(AdminTestPrincipal.Agent.AccessToken);
    }

    [Fact]
    public async Task The_body_is_streamed_not_buffered()
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        var release = new TaskCompletionSource();
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{id}", _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new GatedStream([1, 2, 3, 4], release.Task)) };
            response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
            return response;
        });
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        // If the Admin read the whole file before answering, the stream would never be released and this would time out.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync($"/attachments/{id}", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        var first = new byte[2];
        await body.ReadExactlyAsync(first, timeout.Token);
        release.SetResult();
        var rest = new MemoryStream();
        await body.CopyToAsync(rest, timeout.Token);

        first.ShouldBe([1, 2]);
        rest.ToArray().ShouldBe([3, 4]);
    }

    [Theory]
    [InlineData(404, 404)]
    [InlineData(403, 403)]
    [InlineData(401, 401)]
    [InlineData(400, 502)]
    public async Task An_upstream_failure_is_passed_on_without_the_api_detail(int upstream, int expected)
    {
        await using var factory = new AdminFactory();
        var id = Guid.NewGuid();
        factory.Api.OnProblem(HttpMethod.Get, $"/api/attachments/{id}", (HttpStatusCode)upstream, "attachment-not-found", "Secret detail from the API.");
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync($"/attachments/{id}", Ct);

        ((int)response.StatusCode).ShouldBe(expected);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("Secret detail from the API.");
    }

    [Fact]
    public async Task An_anonymous_request_is_sent_to_sign_in_and_never_reaches_the_api()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync($"/attachments/{Guid.NewGuid()}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        factory.Api.Requests.ShouldNotContain(r => r.Path.StartsWith("/api/attachments", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_a_guid_is_accepted_as_the_id()
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync("/attachments/..%2Fagents%2Fme", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        factory.Api.Requests.ShouldNotContain(r => r.Path.StartsWith("/api/attachments", StringComparison.Ordinal));
    }

    /// <summary>Serves the first chunk at once and the rest only after <paramref name="gate"/> completes.</summary>
    private sealed class GatedStream(byte[] data, Task gate) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= data.Length)
            {
                return 0;
            }

            if (_position >= 2)
            {
                await gate.WaitAsync(cancellationToken);
            }

            var count = Math.Min(Math.Min(buffer.Length, data.Length - _position), _position < 2 ? 2 : int.MaxValue);
            data.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "TicketsClientTests|AttachmentPassThroughTests"`
Expected: a build failure (`ITicketsClient`, `IRequestersClient`, `ReplyAttachment` do not exist).

- [ ] **Step 3: Implement**

1. `src/TechStrap.Admin/Clients/TicketsClient.cs`:

```csharp
using System.Net.Http.Headers;
using SyntaxCircus.Common;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Clients;

/// <summary>
/// A file to send with a reply. The stream is opened when the request is built and closed when it has been sent, so the same
/// <see cref="ReplyAttachment"/> can be sent again after a failure (the composer keeps the browser files until a send succeeds).
/// </summary>
public sealed record ReplyAttachment(string FileName, string ContentType, Func<Stream> OpenRead);

/// <summary>
/// Every ticket operation of the API (D-036). Every write sends the <c>RowVersion</c> in its request (or query) and returns the new <see cref="TicketStateDto"/>;
/// a stale version is a Conflict result with code <see cref="ApiErrorCodes.ConcurrencyConflict"/>. Mutations are never retried.
/// </summary>
public interface ITicketsClient
{
    /// <summary><c>GET /api/tickets</c>: the request is sent as the query string; Page and PageSize are always sent.</summary>
    Task<Result<PagedResponse<TicketSummaryDto>>> ListAsync(ListTicketsRequest request, CancellationToken cancellationToken);

    /// <summary><c>GET /api/tickets/counts</c>.</summary>
    Task<Result<TicketViewCountsResponse>> GetCountsAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/tickets/{reference}</c>: <paramref name="reference"/> is the ticket id or its number (for example ORB-42). 404 is code ticket-not-found.</summary>
    Task<Result<TicketDetailDto>> GetAsync(string reference, CancellationToken cancellationToken);

    /// <summary><c>POST /api/tickets/{id}/replies</c> as multipart/form-data (a public reply, emailed to the customer).</summary>
    Task<Result<AgentMessageResponse>> ReplyAsync(Guid ticketId, AddAgentReplyRequest request, IReadOnlyList<ReplyAttachment> attachments, CancellationToken cancellationToken);

    /// <summary><c>POST /api/tickets/{id}/notes</c> (an internal note).</summary>
    Task<Result<AgentMessageResponse>> AddNoteAsync(Guid ticketId, AddInternalNoteRequest request, CancellationToken cancellationToken);

    Task<Result<TicketStateDto>> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken);

    Task<Result<TicketStateDto>> AssignAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken);

    Task<Result<TicketStateDto>> ChangePriorityAsync(Guid ticketId, ChangeTicketPriorityRequest request, CancellationToken cancellationToken);

    Task<Result<TicketStateDto>> MoveProductAsync(Guid ticketId, MoveTicketProductRequest request, CancellationToken cancellationToken);

    /// <summary>Idempotent: adding a tag the ticket already has succeeds.</summary>
    Task<Result<TicketStateDto>> AddTagAsync(Guid ticketId, AddTicketTagRequest request, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/tickets/{id}/tags/{tagId}?rowVersion=N</c>: the row version travels in the query string and is required.</summary>
    Task<Result<TicketStateDto>> RemoveTagAsync(Guid ticketId, Guid tagId, uint rowVersion, CancellationToken cancellationToken);

    /// <summary><c>PUT /api/tickets/{id}/spam</c>: IsSpam true marks, false restores (Not spam).</summary>
    Task<Result<TicketStateDto>> SetSpamAsync(Guid ticketId, MarkTicketSpamRequest request, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/tickets/{id}</c> (Admin): hard delete, no row version. 204.</summary>
    Task<Result> DeleteAsync(Guid ticketId, CancellationToken cancellationToken);
}

/// <summary>Erasing a requester (Admin).</summary>
public interface IRequestersClient
{
    /// <summary><c>POST /api/requesters/{id}/erase</c>: <paramref name="requesterId"/> is <c>TicketDetailDto.Requester.Id</c>. Idempotent, 204.</summary>
    Task<Result> EraseAsync(Guid requesterId, CancellationToken cancellationToken);
}

internal sealed class TicketsClient(ApiConnection connection) : ITicketsClient
{
    private static string Ticket(Guid id) => $"api/tickets/{id}";

    public Task<Result<PagedResponse<TicketSummaryDto>>> ListAsync(ListTicketsRequest request, CancellationToken cancellationToken) =>
        connection.GetAsync<PagedResponse<TicketSummaryDto>>(
            ApiUri.Build(
                "api/tickets",
                ("view", request.View),
                ("productId", request.ProductId),
                ("status", request.Status),
                ("priority", request.Priority),
                ("assigneeId", request.AssigneeId),
                ("tagId", request.TagId),
                ("requesterId", request.RequesterId),
                ("search", request.Search),
                ("page", request.Page),
                ("pageSize", request.PageSize)),
            cancellationToken);

    public Task<Result<TicketViewCountsResponse>> GetCountsAsync(CancellationToken cancellationToken) =>
        connection.GetAsync<TicketViewCountsResponse>("api/tickets/counts", cancellationToken);

    public Task<Result<TicketDetailDto>> GetAsync(string reference, CancellationToken cancellationToken) =>
        connection.GetAsync<TicketDetailDto>($"api/tickets/{Uri.EscapeDataString(reference)}", cancellationToken);

    public async Task<Result<AgentMessageResponse>> ReplyAsync(Guid ticketId, AddAgentReplyRequest request, IReadOnlyList<ReplyAttachment> attachments, CancellationToken cancellationToken)
    {
        var streams = new List<Stream>();
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(request.Body ?? string.Empty), "Body");
            foreach (var articleId in request.LinkedArticleIds ?? [])
            {
                form.Add(new StringContent(articleId.ToString()), "LinkedArticleIds");
            }

            if (!string.IsNullOrEmpty(request.StatusAfter))
            {
                form.Add(new StringContent(request.StatusAfter), "StatusAfter");
            }

            if (request.RowVersion is { } rowVersion)
            {
                form.Add(new StringContent(rowVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), "RowVersion");
            }

            foreach (var attachment in attachments)
            {
                var stream = attachment.OpenRead();
                streams.Add(stream);
                var file = new StreamContent(stream);
                file.Headers.ContentType = MediaTypeHeaderValue.TryParse(attachment.ContentType, out var type) ? type : new MediaTypeHeaderValue("application/octet-stream");
                form.Add(file, "Attachments", attachment.FileName);
            }

            return await connection.SendContentAsync<AgentMessageResponse>(HttpMethod.Post, $"{Ticket(ticketId)}/replies", form, cancellationToken);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    public Task<Result<AgentMessageResponse>> AddNoteAsync(Guid ticketId, AddInternalNoteRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<AgentMessageResponse>(HttpMethod.Post, $"{Ticket(ticketId)}/notes", request, cancellationToken);

    public Task<Result<TicketStateDto>> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Put, $"{Ticket(ticketId)}/status", request, cancellationToken);

    public Task<Result<TicketStateDto>> AssignAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Put, $"{Ticket(ticketId)}/assignee", request, cancellationToken);

    public Task<Result<TicketStateDto>> ChangePriorityAsync(Guid ticketId, ChangeTicketPriorityRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Put, $"{Ticket(ticketId)}/priority", request, cancellationToken);

    public Task<Result<TicketStateDto>> MoveProductAsync(Guid ticketId, MoveTicketProductRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Put, $"{Ticket(ticketId)}/product", request, cancellationToken);

    public Task<Result<TicketStateDto>> AddTagAsync(Guid ticketId, AddTicketTagRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Post, $"{Ticket(ticketId)}/tags", request, cancellationToken);

    public Task<Result<TicketStateDto>> RemoveTagAsync(Guid ticketId, Guid tagId, uint rowVersion, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Delete, ApiUri.Build($"{Ticket(ticketId)}/tags/{tagId}", ("rowVersion", rowVersion)), null, cancellationToken);

    public Task<Result<TicketStateDto>> SetSpamAsync(Guid ticketId, MarkTicketSpamRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TicketStateDto>(HttpMethod.Put, $"{Ticket(ticketId)}/spam", request, cancellationToken);

    public Task<Result> DeleteAsync(Guid ticketId, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Delete, Ticket(ticketId), null, cancellationToken);
}

internal sealed class RequestersClient(ApiConnection connection) : IRequestersClient
{
    public Task<Result> EraseAsync(Guid requesterId, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Post, $"api/requesters/{requesterId}/erase", null, cancellationToken);
}
```

2. `src/TechStrap.Admin/Clients/AttachmentPassThrough.cs`:

```csharp
using System.Net;
using Microsoft.Net.Http.Headers;

namespace TechStrap.Admin.Clients;

/// <summary>
/// <c>GET /attachments/{id}</c>: the exempt pass-through adapter of D-017. A browser link cannot carry the agent's bearer token, so the Admin streams
/// <c>GET api/attachments/{id}</c> for the signed-in agent. It runs no workflow and touches no data. It never buffers the file, and it forces a download
/// (<c>Content-Disposition: attachment</c>), <c>nosniff</c> and no caching, whatever the API sent. An upstream 404 is a 404; the API alone decides who may read a file.
/// This is a request-scoped call (HttpContext present), so the auth handler resolves the token from the cookie, not from a circuit.
/// </summary>
public static class AttachmentPassThrough
{
    public const string Route = "/attachments/{id:guid}";

    public static IEndpointRouteBuilder MapAttachmentPassThrough(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, StreamAsync);
        return endpoints;
    }

    private static async Task StreamAsync(Guid id, HttpContext http, IHttpClientFactory clients, CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient(ApiClientNames.Read);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/attachments/{id}");
        HttpResponseMessage upstream;
        try
        {
            upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or Polly.CircuitBreaker.BrokenCircuitException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            http.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }

        using (upstream)
        {
            if (!upstream.IsSuccessStatusCode)
            {
                http.Response.StatusCode = upstream.StatusCode switch
                {
                    HttpStatusCode.NotFound => StatusCodes.Status404NotFound,
                    HttpStatusCode.Unauthorized => StatusCodes.Status401Unauthorized,
                    HttpStatusCode.Forbidden => StatusCodes.Status403Forbidden,
                    _ => StatusCodes.Status502BadGateway,
                };
                return;
            }

            var response = http.Response;
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            if (upstream.Content.Headers.ContentLength is { } length)
            {
                response.ContentLength = length;
            }

            response.Headers[HeaderNames.ContentDisposition] = AttachmentDisposition(upstream.Content.Headers.ContentDisposition);
            response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
            response.Headers[HeaderNames.CacheControl] = "private, no-store";
            response.Headers[HeaderNames.ContentSecurityPolicy] = "sandbox";

            await using var body = await upstream.Content.ReadAsStreamAsync(cancellationToken);
            await body.CopyToAsync(response.Body, cancellationToken);
        }
    }

    /// <summary>The upstream header with its file name, but always of type "attachment" (never "inline").</summary>
    internal static string AttachmentDisposition(System.Net.Http.Headers.ContentDispositionHeaderValue? upstream)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        if (upstream?.FileNameStar is { Length: > 0 } fileNameStar)
        {
            disposition.SetHttpFileName(fileNameStar);
        }
        else if (upstream?.FileName is { Length: > 0 } fileName)
        {
            disposition.SetHttpFileName(fileName.Trim('"'));
        }

        return disposition.ToString();
    }
}
```

3. `src/TechStrap.Admin/Clients/ApiClientRegistration.cs`: add the two registrations after `ITagsClient`:

```csharp
        services.AddScoped<ITicketsClient, TicketsClient>();
        services.AddScoped<IRequestersClient, RequestersClient>();
```

4. `src/TechStrap.Admin/Program.cs`: add `app.MapAttachmentPassThrough();` directly after `app.MapAdminAuthEndpoints();`. It sits under the fallback policy, so an anonymous request is sent to `/signin` and never reaches the API (tested).

- [ ] **Step 4: Run the tests**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "ShellHostTests|Admin_reports_live_only|AdminHostRedactionTests"
dotnet build TechStrap.slnx -c Release
```

Expected: PASS and 0 warnings. `ApiClientRegistration` is `public` and the clients are `internal`; the interfaces are public because Razor components in later tasks inject them.

- [ ] **Step 5: Commit**

```bash
git add src tests
git diff --cached --stat
git commit -m "feat(admin): tickets and requesters clients and the streaming attachment pass-through" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 7: Shell and reusable primitives

**Files:**
- Create: `src/TechStrap.Admin/Components/Ui/ShellCopy.cs`, `LoadingState.razor`, `ErrorState.razor`, `EmptyState.razor`, `TagChip.razor` and `.razor.cs`, `PagerControl.razor` and `.razor.cs`, `ConfirmDialog.razor` and `.razor.cs`, `TicketDisplay.cs`, `RelativeTime.razor` and `.razor.cs`
- Create: `src/TechStrap.Admin/Components/Layout/NavMenu.razor` and `.razor.cs`, `StatusBar.razor` and `.razor.cs`, `ShortcutHelpDialog.razor`, `MainLayout.razor.cs`
- Modify: `src/TechStrap.Admin/Components/Layout/MainLayout.razor` (replace the file), `src/TechStrap.Admin/Components/_Imports.razor`, `src/TechStrap.Admin/Program.cs`, `src/TechStrap.Admin/TechStrap.Admin.csproj`, `src/TechStrap.Admin/Styles/app.scss`
- Create: `src/TechStrap.Admin/Features/Shell/StatusMessageService.cs`, `ShortcutService.cs`, `ShortcutCatalog.cs`, `ShellServiceCollectionExtensions.cs`
- Create: `src/TechStrap.Admin/wwwroot/js/dialog.js`, `src/TechStrap.Admin/wwwroot/js/shortcuts.js`
- Create: `src/TechStrap.Admin/Styles/_states.scss`, `_dialog.scss`, `_statusbar.scss`
- Create: `tests/TechStrap.Admin.Tests/Support/AgentSessions.cs`, `Support/AdminComponentTest.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/StateComponentTests.cs`, `TagChipAndPagerTests.cs`, `ConfirmDialogTests.cs`, `TicketDisplayTests.cs`, `ShortcutServiceTests.cs`, `ShellComponentTests.cs`, and `tests/TechStrap.Admin.Tests/ScriptHostTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/MainLayoutTests.cs`, `tests/TechStrap.Admin.Tests/TechStrap.Admin.Tests.csproj`

**Interfaces:**
- Consumes (Tasks 3 to 5, names as the controller fixed them):
  - `AgentSession` (`TechStrap.Admin.Auth`, scoped): `State` (`AgentSessionState`: `NotLoaded`, `Ready`, `NoAccess`, `SessionExpired`, `Unavailable`), `AgentDto? Agent`, `bool IsAdmin`, `event Action? Changed`, `EnsureLoadedAsync(CancellationToken)`.
  - `AgentGate` (wraps `@Body` in `MainLayout` since Task 5), `SignOutForm` (renders the POST form to `/signout`), `NoAccessPage` (already created in Task 4; this task does not touch it).
  - Test helpers `ShellTestServices.AddAgentShell(this BunitContext, role)` (returns the substitute `IAgentsClient`) and `SignedIn<T>(builder)` in `tests/TechStrap.Admin.Tests/Components/ShellTestServices.cs`.
  - Existing primitives: `StatusStamp`, `PriorityMark`, `TintedEntry`, `Kbd`, `BrandWindow`, `UiCopy`, the `GlobalErrorBoundary` wrapper in `MainLayout`.
  - `TechStrap.Contracts`: `TicketStatuses`, `TicketPriorities`, `ProductAccent` (`TryDerive`).
- Produces (all public unless stated; tests reach the few `internal` members through `InternalsVisibleTo`):

```csharp
// TechStrap.Admin.Components.Ui
public sealed partial class ConfirmDialog : IAsyncDisposable   // Open, Title, ChildContent, ConfirmLabel, CancelLabel, Danger, RequiredText, Busy, Error, Informational, OnConfirm, OnCancel
public partial class PagerControl                              // Page (1-based), PageSize, TotalCount, OnPageChanged EventCallback<int>
public partial class TagChip                                   // Name, Colour (#RRGGBB)
public partial class RelativeTime                              // When
internal static class TicketDisplay { Stamp(status, isSpam); Priority(priority); Relative(when, now); Absolute(when); Initials(name); FileSize(bytes) }

// TechStrap.Admin.Features.Shell
public sealed class StatusMessageService { string? Current; event Action? Changed; void Show(string); void Clear(); }
public enum ShortcutAction { MoveDown, MoveUp, OpenSelected, FocusSearch, Reply, Note, FocusAssignee, Send, Escape, Help }
public sealed record KeyPress(string Key, bool Ctrl, bool Meta, bool Alt, bool Typing, bool OnBody, string? Scope);
public sealed class ShortcutService(IJSRuntime js) : IAsyncDisposable
{
    const string ComposerScope = "composer"; event Func<ShortcutAction, Task>? Pressed; bool SingleKeyEnabled;
    static ShortcutAction? Map(KeyPress press, bool singleKeyEnabled); Task StartAsync(); [JSInvokable] Task OnKeyAsync(KeyPress press);
}
public static IServiceCollection AddShell(this IServiceCollection services);   // StatusMessageService + ShortcutService (scoped), TimeProvider.System if none
```

**Rules:**
1. **Verified bUnit 2.11.3 facts the tests rely on** (checked in a scratch project against the package in `C:\nuget`, not recalled from memory):
   - `BunitContext`, `Render<T>(p => p.Add(...))`, `p.AddCascadingValue(...)`, `AddAuthorization()` then `SetAuthorized`, `SetRoles`, `SetClaims`.
   - JS is strict by default. `JSInterop.SetupModule("./js/x.js")` returns a `BunitJSModuleInterop`; `SetupVoid("name", _ => true).SetVoidResult()` plans a call; `VerifyInvoke("name", n)`, `VerifyNotInvoke("name")` and `Invocations["name"]` assert and read arguments. `JSInterop.VerifyFocusAsyncInvoke()` records `ElementReference.FocusAsync`.
   - **bUnit blanks `blazor:elementReference` on an element after the next render**, so `ShouldBeElementReferenceTo(cut.Find(...))` only works when no render happened after the focus call. When one did, read the element's `blazor:elementReference` attribute before the action and compare it with `((ElementReference)args[0]).Id`.
   - **Services cannot be added once anything was resolved** (`NavigationManager` counts). A test that must choose its role late registers a factory in the constructor that reads a field.
   - `cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(bytes, name, null, contentType))` works. bUnit does **not** enforce `OpenReadStream(maxAllowedSize)`, and `InputFile` has **no `MaxAllowedSize` parameter** (the attribute silently becomes a DOM attribute): the 07a brief's "InputFile with MaxAllowedSize" is not real, so Task 10 checks size, type and count itself and passes the limit to `OpenReadStream`.
   - `SupplyParameterFromQuery` binds after `NavigationManager.NavigateTo(...)`; `Services.AddSingleton<TimeProvider>(new FakeTimeProvider())` plus `Advance` drives a `TimeProvider.CreateTimer` debounce; `BunitNavigationManager.History[...].Options.ReplaceHistoryEntry` records `replace: true`.
   - xUnit analyzer `xUnit1051` is an error: pass `Arg.Any<CancellationToken>()` or `Xunit.TestContext.Current.CancellationToken`. Write `Xunit.TestContext` in files that also import `Bunit` (there is a `Bunit.TestContext`). `DisposeComponentsAsync()` is the v2 name.
2. **Razor components are always generated `public`.** A `[Parameter]` of an `internal` type is CS0053, and a code-behind that declares `internal partial class` conflicts with the generated `public` half (CS0262). Verified by the compiler. So the feature view models in Tasks 8 to 11 are `public sealed record`s in their feature namespace (the brief said `internal`; this is recorded in D-040), and only `TicketDisplay` and the `ResultConversions` helper are `internal`.
3. **Native `<dialog>` and focus.** `ConfirmDialog` never confirms on Enter: there is no `<form>`, both buttons are `type="button"`, and the first focus goes to the typed-confirmation input or, with none, to the heading (`tabindex="-1"`), never to a button. So pressing Enter on a fresh dialog does nothing. Esc raises `oncancel`, which is routed to `OnCancel` (the native close is prevented, the owner closes through `Open`). While `Busy`, every button, the input and Esc are inert.
4. **Keyboard layer.** One document `keydown` listener in `shortcuts.js` reports only the keys the layer maps and nothing while the user types (except Ctrl or Cmd plus Enter, which sends from the composer), so typing never costs a circuit round trip. The script ignores every key while a `dialog[open]` exists. `ShortcutService.Map` repeats the typing check in .NET, which is where the tests pin it. Enter and the arrows act on the page only when nothing interactive has focus (`OnBody`). `SingleKeyEnabled` is always on in 07a (the My settings toggle is 07b). The command palette (Ctrl+K) is deferred to 07c.
5. **Chips never trust a colour.** `TagChip` derives its foreground through `ProductAccent.TryDerive` (the one shared rule, BRAND.md section 22) and renders the plain chip for a malformed value. The inline `style` attribute is the only inline style in the Admin and is isolated in `TagChip`, so the 07c CSP work has one place to change.
6. **Time.** `RelativeTime` shows "5 min ago" with the machine time in `datetime` and the absolute time in the tooltip, in **UTC**. Converting to the agent's zone needs the browser's zone and a JS call; that is a recorded gap for 07c.
7. **`TicketDisplay` throws for an unknown status or priority.** Both sets are closed in the Contracts constants; a new value must fail loudly in the error boundary, not render as a guess.
8. **Layout.** `MainLayout` keeps `AgentGate` around `@Body` and keeps the `GlobalErrorBoundary`. The rail is now `NavMenu` (brand, the Queue link and the signed-in block with `SignOutForm`, shown only when `AgentSession.State` is `Ready`, and re-rendered when the session changes). The status bar and the shortcut help dialog sit under the page. Admin-only links arrive in 07b.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/TechStrap.Admin.Tests.csproj`: add `<PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />` next to the existing `bunit` reference if it is not there yet (the version is central, 10.10.0). In `src/TechStrap.Admin/TechStrap.Admin.csproj`, make sure this item exists (Task 5 may have added it):

```xml
  <ItemGroup>
    <!-- The component tests use a few internal helpers (TicketDisplay, view-model mappers). -->
    <InternalsVisibleTo Include="TechStrap.Admin.Tests" />
  </ItemGroup>
```

`tests/TechStrap.Admin.Tests/Support/AgentSessions.cs`

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Support;

/// <summary>
/// Builds the <see cref="AgentSession"/> a component test needs, over a substitute <see cref="IAgentsClient"/>. This is the only test code that
/// knows how a session is constructed and loaded, so a change to <see cref="AgentSession"/> touches one file.
/// </summary>
internal static class AgentSessions
{
    public static readonly Guid SamId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>A session whose <c>GET /api/agents/me</c> succeeded for Sam, an Agent, or Ada, an Admin.</summary>
    public static async Task<AgentSession> SignedInAsync(bool admin = false)
    {
        var agents = Substitute.For<IAgentsClient>();
        var me = new AgentDto(
            admin ? Guid.Parse("22222222-2222-2222-2222-222222222222") : SamId,
            admin ? "Ada Admin" : "Sam Ortiz",
            admin ? "ada@example.com" : "sam@example.com",
            admin ? AgentRoles.Admin : AgentRoles.Agent,
            IsActive: true,
            PublicDisplayName: null,
            LastSeenAt: null);
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(me));
        var session = new AgentSession(agents);
        await session.EnsureLoadedAsync(CancellationToken.None);
        return session;
    }

    /// <summary>
    /// The same as <see cref="SignedInAsync"/> for a constructor, where there is nothing to await. The substitute client answers synchronously, so the
    /// load has completed by the time this returns.
    /// </summary>
    public static AgentSession SignedIn(bool admin = false) => SignedInAsync(admin).GetAwaiter().GetResult();
}
```

`AgentSessions` is the one place that knows how a component test builds a signed-in `AgentSession` (a substitute `IAgentsClient` answering `GetMeAsync`). The NavMenu and layout tests use the shared `AddAgentShell` instead, because they render `SignOutForm`.

`tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Tests.Support;

/// <summary>
/// The common setup of the Admin component tests: the shell services (message slot, shortcut service), a fake clock, and the two script modules
/// the shell imports, set up in bUnit's strict JS mode so an unexpected JS call fails the test instead of passing silently.
/// </summary>
public abstract class AdminComponentTest : BunitContext
{
    protected AdminComponentTest()
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        Services.AddShell();
        Services.AddSingleton<TimeProvider>(Time);

        Shortcuts = JSInterop.SetupModule("./js/shortcuts.js");
        Shortcuts.SetupVoid("register", _ => true).SetVoidResult();
        Shortcuts.SetupVoid("unregister", _ => true).SetVoidResult();

        Dialogs = JSInterop.SetupModule("./js/dialog.js");
        Dialogs.SetupVoid("open", _ => true).SetVoidResult();
        Dialogs.SetupVoid("close", _ => true).SetVoidResult();
    }

    protected FakeTimeProvider Time { get; }

    /// <summary>The <c>shortcuts.js</c> module double; use <c>VerifyInvoke("register")</c>.</summary>
    protected BunitJSModuleInterop Shortcuts { get; }

    /// <summary>The <c>dialog.js</c> module double; use <c>VerifyInvoke("open")</c> and read the arguments of the invocation.</summary>
    protected BunitJSModuleInterop Dialogs { get; }

    protected ShortcutService ShortcutService => Services.GetRequiredService<ShortcutService>();

    protected StatusMessageService StatusMessages => Services.GetRequiredService<StatusMessageService>();

    /// <summary>Simulates the page script reporting a key press (what <c>shortcuts.js</c> sends over the JS bridge).</summary>
    protected Task PressAsync(string key, bool ctrl = false, bool typing = false, bool onBody = true, string? scope = null) =>
        Renderer.Dispatcher.InvokeAsync(() => ShortcutService.OnKeyAsync(new KeyPress(key, ctrl, Meta: false, Alt: false, typing, onBody, scope)));
}
```

`tests/TechStrap.Admin.Tests/Components/StateComponentTests.cs`

```csharp
using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class StateComponentTests : AdminComponentTest
{
    [Fact]
    public void Loading_draws_the_requested_skeleton_rows_and_announces_one_label()
    {
        var cut = Render<LoadingState>(p => p.Add(c => c.Rows, 3));

        cut.FindAll(".ts-skeleton").Count.ShouldBe(3);
        cut.FindAll(".ts-skeleton").ShouldAllBe(row => row.GetAttribute("aria-hidden") == "true");
        cut.Find("[role=status] .visually-hidden").TextContent.ShouldBe("Loading");
    }

    [Fact]
    public void An_error_is_an_alert_with_a_retry_that_fires_once_per_click()
    {
        var retries = 0;
        var cut = Render<ErrorState>(p => p
            .Add(c => c.Message, "Couldn't load tickets.")
            .Add(c => c.OnRetry, () => retries++));

        cut.Find("[role=alert] p").TextContent.ShouldBe("Couldn't load tickets.");
        cut.Find("button").TextContent.ShouldBe("Retry");

        cut.Find("button").Click();

        retries.ShouldBe(1);
    }

    [Fact]
    public void An_error_without_a_retry_callback_draws_no_button()
    {
        var cut = Render<ErrorState>(p => p.Add(c => c.Message, "Not allowed."));

        cut.FindAll("button").ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_state_is_plain_text_with_optional_content_and_no_brand_window()
    {
        var cut = Render<EmptyState>(p => p
            .Add(c => c.Heading, "No spam")
            .AddChildContent("<a href=\"/queue\">Back to the queue</a>"));

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No spam");
        cut.Find("a").TextContent.ShouldBe("Back to the queue");
        cut.FindAll(".ts-window").ShouldBeEmpty();
        cut.FindAll("img").ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/TagChipAndPagerTests.cs`

```csharp
using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class TagChipAndPagerTests : AdminComponentTest
{
    [Theory]
    [InlineData("#FFFF00", "#000000")]
    [InlineData("#1D4ED8", "#FFFFFF")]
    [InlineData("#dc2626", "#FFFFFF")]
    public void A_tag_chip_derives_a_legible_foreground_from_its_colour(string colour, string expectedForeground)
    {
        var cut = Render<TagChip>(p => p.Add(c => c.Name, "bug").Add(c => c.Colour, colour));

        var chip = cut.Find("span.ts-tag");
        chip.TextContent.ShouldBe("bug");
        chip.GetAttribute("style")!.ShouldContain($"color:{expectedForeground}");
        chip.GetAttribute("style")!.ShouldContain($"background-color:{colour.ToUpperInvariant()}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("url(javascript:alert(1))")]
    public void A_malformed_colour_renders_the_plain_chip_and_never_reaches_a_style_attribute(string? colour)
    {
        var cut = Render<TagChip>(p => p.Add(c => c.Name, "bug").Add(c => c.Colour, colour));

        cut.Find("span.ts-tag").HasAttribute("style").ShouldBeFalse();
        cut.Markup.ShouldNotContain("javascript");
    }

    [Fact]
    public void The_pager_summarises_the_range_and_reports_the_next_page_once()
    {
        var requested = new List<int>();
        var cut = Render<PagerControl>(p => p
            .Add(c => c.Page, 2)
            .Add(c => c.PageSize, 25)
            .Add(c => c.TotalCount, 163)
            .Add(c => c.OnPageChanged, page => requested.Add(page)));

        cut.Find(".ts-pager-summary").TextContent.ShouldBe("26–50 of 163");
        cut.Find(".ts-pager-page").TextContent.ShouldBe("Page 2 of 7");

        cut.FindAll("button")[1].Click();

        requested.ShouldBe([3]);
    }

    [Fact]
    public void Previous_is_disabled_on_the_first_page_and_next_on_the_last()
    {
        var first = Render<PagerControl>(p => p.Add(c => c.Page, 1).Add(c => c.PageSize, 25).Add(c => c.TotalCount, 60));
        var last = Render<PagerControl>(p => p.Add(c => c.Page, 3).Add(c => c.PageSize, 25).Add(c => c.TotalCount, 60));

        first.FindAll("button")[0].HasAttribute("disabled").ShouldBeTrue();
        first.FindAll("button")[1].HasAttribute("disabled").ShouldBeFalse();
        last.FindAll("button")[1].HasAttribute("disabled").ShouldBeTrue();
        last.Find(".ts-pager-summary").TextContent.ShouldBe("51–60 of 60");
    }

    [Fact]
    public void A_single_page_shows_the_summary_without_buttons_and_an_empty_list_shows_nothing()
    {
        var single = Render<PagerControl>(p => p.Add(c => c.Page, 1).Add(c => c.PageSize, 25).Add(c => c.TotalCount, 4));
        var none = Render<PagerControl>(p => p.Add(c => c.Page, 1).Add(c => c.PageSize, 25).Add(c => c.TotalCount, 0));

        single.Find(".ts-pager-summary").TextContent.ShouldBe("1–4 of 4");
        single.FindAll("button").ShouldBeEmpty();
        none.Markup.Trim().ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/ConfirmDialogTests.cs`

```csharp
using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class ConfirmDialogTests : AdminComponentTest
{
    private int _confirmed;
    private int _cancelled;

    private IRenderedComponent<ConfirmDialog> RenderDialog(Action<ComponentParameterCollectionBuilder<ConfirmDialog>>? more = null) =>
        Render<ConfirmDialog>(p =>
        {
            p.Add(c => c.Open, true)
                .Add(c => c.Title, "Delete ACME-142?")
                .Add(c => c.ConfirmLabel, "Delete ticket")
                .Add(c => c.OnConfirm, () => _confirmed++)
                .Add(c => c.OnCancel, () => _cancelled++)
                .AddChildContent("<p>This cannot be undone.</p>");
            more?.Invoke(p);
        });

    private static void Type(IRenderedComponent<ConfirmDialog> cut, string text) => cut.Find("input").Input(text);

    [Fact]
    public void Opening_shows_the_modal_once_and_focuses_the_heading_when_nothing_must_be_typed()
    {
        var cut = RenderDialog();

        Dialogs.VerifyInvoke("open", 1);
        var open = Dialogs.Invocations["open"].Single();
        open.Arguments[1].ShouldBeElementReferenceTo(cut.Find("h2"));
        cut.Find("h2").GetAttribute("tabindex").ShouldBe("-1");
    }

    [Fact]
    public void A_typed_confirmation_takes_the_initial_focus()
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142"));

        Dialogs.Invocations["open"].Single().Arguments[1].ShouldBeElementReferenceTo(cut.Find("input"));
        cut.Find("label").TextContent.ShouldBe("Type ACME-142 to confirm");
    }

    [Fact]
    public void A_closed_dialog_never_calls_the_script()
    {
        Render<ConfirmDialog>(p => p.Add(c => c.Open, false).Add(c => c.Title, "x"));

        Dialogs.VerifyNotInvoke("open");
    }

    [Fact]
    public void Closing_asks_the_browser_to_close_the_dialog()
    {
        var cut = RenderDialog();

        cut.Render(p => p.Add(c => c.Open, false));

        Dialogs.VerifyInvoke("close", 1);
    }

    [Theory]
    [InlineData("ACME-142", true)]
    [InlineData("  acme-142 ", true)]
    [InlineData("ACME-14", false)]
    [InlineData("", false)]
    public void Confirm_is_enabled_only_when_the_typed_text_matches(string typed, bool enabled)
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142"));

        Type(cut, typed);

        cut.Find("button.btn-primary").HasAttribute("disabled").ShouldBe(!enabled);
    }

    [Fact]
    public void Confirm_fires_once_and_a_disabled_confirm_fires_nothing()
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142"));

        cut.Find("button.btn-primary").Click();
        _confirmed.ShouldBe(0);

        Type(cut, "ACME-142");
        cut.Find("button.btn-primary").Click();

        _confirmed.ShouldBe(1);
    }

    [Fact]
    public void Cancel_fires_once_and_never_confirms()
    {
        var cut = RenderDialog();

        cut.Find("button.btn-outline-secondary").Click();

        _cancelled.ShouldBe(1);
        _confirmed.ShouldBe(0);
    }

    [Fact]
    public void Enter_cannot_confirm_because_there_is_no_form_and_the_buttons_are_not_submit_buttons()
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142"));
        Type(cut, "ACME-142");

        cut.FindAll("form").ShouldBeEmpty();
        cut.FindAll("button").ShouldAllBe(button => button.GetAttribute("type") == "button");
        cut.Find("input").HasAttribute("onkeydown").ShouldBeFalse();
        _confirmed.ShouldBe(0);
    }

    [Fact]
    public void Escape_the_native_cancel_event_cancels_the_dialog()
    {
        var cut = RenderDialog();

        cut.Find("dialog").TriggerEvent("oncancel", EventArgs.Empty);

        _cancelled.ShouldBe(1);
        _confirmed.ShouldBe(0);
    }

    [Fact]
    public void While_busy_everything_is_inert_including_escape()
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142").Add(c => c.Busy, true));

        cut.FindAll("button").ShouldAllBe(button => button.HasAttribute("disabled"));
        cut.Find("input").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("dialog").TriggerEvent("oncancel", EventArgs.Empty);

        _cancelled.ShouldBe(0);
        _confirmed.ShouldBe(0);
    }

    [Fact]
    public void A_failure_is_shown_as_an_alert_and_the_dialog_stays_open()
    {
        var cut = RenderDialog(p => p.Add(c => c.Error, "Couldn't delete the ticket. Nothing was changed."));

        cut.Find("[role=alert]").TextContent.ShouldBe("Couldn't delete the ticket. Nothing was changed.");
        Dialogs.VerifyNotInvoke("close");
    }

    [Fact]
    public void An_informational_dialog_has_only_a_close_button()
    {
        var cut = RenderDialog(p => p.Add(c => c.Informational, true).Add(c => c.CancelLabel, "Close"));

        cut.FindAll("button").Count.ShouldBe(1);
        cut.Find("button").TextContent.ShouldBe("Close");
    }

    [Fact]
    public void Reopening_clears_what_was_typed_before()
    {
        var cut = RenderDialog(p => p.Add(c => c.RequiredText, "ACME-142"));
        Type(cut, "ACME-142");
        cut.Render(p => p.Add(c => c.Open, false));

        cut.Render(p => p.Add(c => c.Open, true));

        cut.Find("button.btn-primary").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void A_dangerous_dialog_uses_the_danger_style_on_the_confirm_button()
    {
        var cut = RenderDialog(p => p.Add(c => c.Danger, true));

        cut.Find("dialog").ClassList.ShouldContain("ts-dialog--danger");
        cut.Find("button.btn-danger").TextContent.ShouldBe("Delete ticket");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/TicketDisplayTests.cs`

```csharp
using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class TicketDisplayTests : AdminComponentTest
{
    public static TheoryData<string, StampStatus> Statuses() => new()
    {
        { TicketStatuses.New, StampStatus.New },
        { TicketStatuses.Open, StampStatus.Open },
        { TicketStatuses.Pending, StampStatus.Pending },
        { TicketStatuses.Solved, StampStatus.Solved },
        { TicketStatuses.Closed, StampStatus.Closed },
    };

    [Theory]
    [MemberData(nameof(Statuses))]
    public void Every_status_constant_maps_to_its_stamp(string status, StampStatus expected) =>
        TicketDisplay.Stamp(status, isSpam: false).ShouldBe(expected);

    [Theory]
    [MemberData(nameof(Statuses))]
    public void The_spam_flag_wins_over_any_status(string status, StampStatus _) =>
        TicketDisplay.Stamp(status, isSpam: true).ShouldBe(StampStatus.Spam);

    [Fact]
    public void An_unknown_status_fails_loudly_instead_of_guessing() =>
        Should.Throw<ArgumentOutOfRangeException>(() => TicketDisplay.Stamp("Archived", isSpam: false));

    [Theory]
    [InlineData(TicketPriorities.Urgent, PriorityLevel.Urgent)]
    [InlineData(TicketPriorities.High, PriorityLevel.High)]
    [InlineData(TicketPriorities.Normal, PriorityLevel.Normal)]
    [InlineData(TicketPriorities.Low, PriorityLevel.Low)]
    public void Every_priority_constant_maps_to_its_level(string priority, PriorityLevel expected) =>
        TicketDisplay.Priority(priority).ShouldBe(expected);

    [Fact]
    public void An_unknown_priority_fails_loudly() =>
        Should.Throw<ArgumentOutOfRangeException>(() => TicketDisplay.Priority("Critical"));

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(59, "just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(59 * 60, "59 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(2 * 86400, "2 d ago")]
    [InlineData(8 * 86400, "2026-09-26")]
    public void Relative_time_reads_like_a_person_would_say_it(int secondsAgo, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        TicketDisplay.Relative(now.AddSeconds(-secondsAgo), now).ShouldBe(expected);
    }

    [Fact]
    public void A_time_in_the_future_reads_as_just_now()
    {
        var now = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

        TicketDisplay.Relative(now.AddMinutes(3), now).ShouldBe("just now");
    }

    [Theory]
    [InlineData("Sam Ortiz", "SO")]
    [InlineData("sam", "S")]
    [InlineData("  ada   lovelace byron ", "AL")]
    [InlineData("", "?")]
    [InlineData(null, "?")]
    public void Initials_are_at_most_two_uppercase_letters(string? name, string expected) =>
        TicketDisplay.Initials(name).ShouldBe(expected);

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(10L * 1024 * 1024, "10 MB")]
    public void File_sizes_use_one_decimal_and_the_invariant_culture(long bytes, string expected) =>
        TicketDisplay.FileSize(bytes).ShouldBe(expected);

    [Fact]
    public void The_relative_time_component_keeps_the_machine_time_and_the_utc_tooltip()
    {
        var when = Time.GetUtcNow().AddMinutes(-5);

        var cut = Render<RelativeTime>(p => p.Add(c => c.When, when));

        var element = cut.Find("time");
        element.TextContent.ShouldBe("5 min ago");
        element.GetAttribute("datetime").ShouldBe("2026-10-04T11:55:00.0000000Z");
        element.GetAttribute("title").ShouldBe("2026-10-04 11:55 UTC");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/ShortcutServiceTests.cs`

```csharp
using Bunit;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class ShortcutServiceTests : AdminComponentTest
{
    private static KeyPress Key(string key, bool ctrl = false, bool typing = false, bool onBody = true, string? scope = null, bool alt = false) =>
        new(key, ctrl, Meta: false, alt, typing, onBody, scope);

    [Theory]
    [InlineData("j", ShortcutAction.MoveDown)]
    [InlineData("J", ShortcutAction.MoveDown)]
    [InlineData("k", ShortcutAction.MoveUp)]
    [InlineData("ArrowDown", ShortcutAction.MoveDown)]
    [InlineData("ArrowUp", ShortcutAction.MoveUp)]
    [InlineData("Enter", ShortcutAction.OpenSelected)]
    [InlineData("/", ShortcutAction.FocusSearch)]
    [InlineData("r", ShortcutAction.Reply)]
    [InlineData("n", ShortcutAction.Note)]
    [InlineData("e", ShortcutAction.FocusAssignee)]
    [InlineData("?", ShortcutAction.Help)]
    [InlineData("Escape", ShortcutAction.Escape)]
    public void A_single_key_maps_to_its_action_when_the_user_is_not_typing(string key, ShortcutAction expected) =>
        ShortcutService.Map(Key(key), singleKeyEnabled: true).ShouldBe(expected);

    [Theory]
    [InlineData("j")]
    [InlineData("/")]
    [InlineData("r")]
    [InlineData("Enter")]
    [InlineData("Escape")]
    public void While_typing_no_single_key_is_a_shortcut(string key) =>
        ShortcutService.Map(Key(key, typing: true), singleKeyEnabled: true).ShouldBeNull();

    [Theory]
    [InlineData("j")]
    [InlineData("/")]
    [InlineData("ArrowDown")]
    public void With_single_key_shortcuts_off_only_escape_and_ctrl_enter_still_work(string key)
    {
        ShortcutService.Map(Key(key), singleKeyEnabled: false).ShouldBeNull();
        ShortcutService.Map(Key("Escape"), singleKeyEnabled: false).ShouldBe(ShortcutAction.Escape);
        ShortcutService.Map(Key("Enter", ctrl: true, typing: true, scope: ShortcutService.ComposerScope), singleKeyEnabled: false).ShouldBe(ShortcutAction.Send);
    }

    [Fact]
    public void Enter_and_the_arrows_are_left_alone_when_something_interactive_has_focus()
    {
        ShortcutService.Map(Key("Enter", onBody: false), singleKeyEnabled: true).ShouldBeNull();
        ShortcutService.Map(Key("ArrowDown", onBody: false), singleKeyEnabled: true).ShouldBeNull();
        ShortcutService.Map(Key("j", onBody: false), singleKeyEnabled: true).ShouldBe(ShortcutAction.MoveDown);
    }

    [Fact]
    public void Ctrl_enter_sends_only_from_inside_the_composer()
    {
        ShortcutService.Map(Key("Enter", ctrl: true, typing: true, scope: ShortcutService.ComposerScope), true).ShouldBe(ShortcutAction.Send);
        ShortcutService.Map(Key("Enter", ctrl: true, typing: true, scope: null), true).ShouldBeNull();
        ShortcutService.Map(Key("Enter", ctrl: true, typing: true, scope: "queue"), true).ShouldBeNull();
        ShortcutService.Map(Key("j", ctrl: true), true).ShouldBeNull();
    }

    [Fact]
    public void An_alt_chord_is_never_a_shortcut() =>
        ShortcutService.Map(Key("j", alt: true), singleKeyEnabled: true).ShouldBeNull();

    [Fact]
    public async Task Every_subscriber_hears_a_recognised_shortcut_in_order()
    {
        var heard = new List<string>();
        ShortcutService.Pressed += action => { heard.Add($"first:{action}"); return Task.CompletedTask; };
        ShortcutService.Pressed += action => { heard.Add($"second:{action}"); return Task.CompletedTask; };

        await PressAsync("j");

        heard.ShouldBe(["first:MoveDown", "second:MoveDown"]);
    }

    [Fact]
    public async Task Nothing_is_raised_while_typing_or_when_the_layer_is_off()
    {
        var heard = new List<ShortcutAction>();
        ShortcutService.Pressed += action => { heard.Add(action); return Task.CompletedTask; };

        await PressAsync("j", typing: true);
        ShortcutService.SingleKeyEnabled = false;
        await PressAsync("j");

        heard.ShouldBeEmpty();
    }

    [Fact]
    public async Task Starting_imports_the_module_and_registers_the_listener_once()
    {
        await ShortcutService.StartAsync();
        await ShortcutService.StartAsync();

        Shortcuts.VerifyInvoke("register", 1);
    }

    [Fact]
    public void The_help_list_never_gives_two_shortcuts_the_same_key()
    {
        var keys = ShortcutCatalog.All.SelectMany(entry => entry.Keys.Split(" / ")).ToList();

        keys.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(keys.Count);
    }

    [Fact]
    public void The_help_list_covers_every_key_the_service_maps_except_the_arrow_aliases()
    {
        var listed = ShortcutCatalog.All.SelectMany(entry => entry.Keys.Split(" / ")).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in new[] { "j", "k", "Enter", "/", "r", "n", "e", "?", "Esc", "Ctrl+Enter" })
        {
            listed.ShouldContain(key);
        }
    }
}
```

`tests/TechStrap.Admin.Tests/Components/ShellComponentTests.cs`

```csharp
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Components.Layout;
using TechStrap.Contracts.Agents;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class ShellComponentTests : AdminComponentTest
{
    [Fact]
    public void The_status_bar_lists_the_hints_and_has_one_polite_message_slot()
    {
        var cut = Render<StatusBar>();

        cut.FindAll(".ts-statusbar-hints li").Select(li => li.TextContent.Trim()).ShouldBe(
            ["j k move", "Enter open", "r reply", "n note", "e assign", "/ search", "? help"]);
        cut.Find("p.ts-statusbar-message").GetAttribute("role").ShouldBe("status");
        cut.Find("p.ts-statusbar-message").TextContent.ShouldBeEmpty();
    }

    [Fact]
    public void A_message_appears_in_the_status_slot_and_the_next_one_replaces_it()
    {
        var cut = Render<StatusBar>();

        cut.InvokeAsync(() => StatusMessages.Show("Reply sent on ACME-142"));
        cut.Find("p.ts-statusbar-message").TextContent.ShouldBe("Reply sent on ACME-142");

        cut.InvokeAsync(() => StatusMessages.Show("Restored ACME-142 from spam"));
        cut.Find("p.ts-statusbar-message").TextContent.ShouldBe("Restored ACME-142 from spam");
    }

    [Fact]
    public async Task The_rail_for_an_agent_shows_the_queue_the_name_and_a_post_form_to_sign_out()
    {
        this.AddAgentShell();
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken);

        var cut = Render<NavMenu>(p => p.SignedIn());

        cut.Find("a.ts-rail-link[href='/queue']").TextContent.ShouldBe("Queue");
        cut.Find(".ts-rail-name").TextContent.ShouldBe("Sam");
        cut.FindAll(".ts-rail-role").ShouldBeEmpty();
        cut.Find("form[action='/signout']").GetAttribute("method").ShouldBe("post");
    }

    [Fact]
    public async Task The_rail_marks_an_admin_and_has_no_settings_links_yet()
    {
        this.AddAgentShell(AgentRoles.Admin);
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken);

        var cut = Render<NavMenu>(p => p.SignedIn());

        cut.Find(".ts-rail-role").TextContent.ShouldBe("Admin");
        cut.FindAll("a[href^='/settings']").ShouldBeEmpty();
        cut.FindAll("a[href^='/ops']").ShouldBeEmpty();
    }

    [Fact]
    public void The_rail_shows_the_brand_alone_until_the_session_is_ready_and_then_follows_it()
    {
        this.AddAgentShell();

        var cut = Render<NavMenu>(p => p.SignedIn());

        cut.Find("a.ts-brand").GetAttribute("aria-label").ShouldBe("TechStrap Admin home");
        cut.FindAll("a.ts-rail-link").ShouldBeEmpty();
        cut.FindAll("form").ShouldBeEmpty();

        cut.InvokeAsync(() => Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken));

        cut.WaitForAssertion(() => cut.Find("a.ts-rail-link[href='/queue']").ShouldNotBeNull());
    }

    [Fact]
    public void The_layout_registers_the_key_listener_once_and_unsubscribes_on_dispose()
    {
        this.AddAgentShell();

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<p id=\"page\">page</p>"))));

        Shortcuts.VerifyInvoke("register", 1);
        cut.Find("main#main #page").TextContent.ShouldBe("page");
        cut.Find("a.ts-skip-link").GetAttribute("href").ShouldBe("#main");
    }

    [Fact]
    public async Task The_question_mark_opens_the_shortcut_help_and_close_hides_it()
    {
        this.AddAgentShell();
        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));

        await PressAsync("?");
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 1));

        cut.Find("dialog.ts-dialog h2").TextContent.ShouldBe("Keyboard shortcuts");
        cut.FindAll(".ts-shortcut-table tbody tr").Count.ShouldBe(9);

        cut.Find("dialog button").Click();

        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("close", 1));
    }

    [Fact]
    public async Task The_slash_key_on_another_screen_opens_the_queue_and_on_the_queue_does_nothing()
    {
        this.AddAgentShell();
        var navigation = Services.GetRequiredService<NavigationManager>();
        Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));

        navigation.NavigateTo("/tickets/ACME-142");
        await PressAsync("/");
        navigation.Uri.ShouldEndWith("/queue");

        var historyBefore = ((BunitNavigationManager)navigation).History.Count;
        await PressAsync("/");

        ((BunitNavigationManager)navigation).History.Count.ShouldBe(historyBefore);
    }
}
```

`tests/TechStrap.Admin.Tests/Components/MainLayoutTests.cs` (replace the file; the Task 4 version added `AddAgentShell` and `SignedIn`, this one also waits for the gate):

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class MainLayoutTests : AdminComponentTest
{
    public MainLayoutTests() => this.AddAgentShell();

    [Fact]
    public void Header_shows_the_SVG_head_mark_and_no_mascot_copy()
    {
        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        var mark = cut.Find(".ts-brand img");
        mark.GetAttribute("src").ShouldBe("brand/mark.svg");
        mark.GetAttribute("alt").ShouldBe(string.Empty);
        cut.Find(".ts-brand span").TextContent.ShouldContain("TechStrap");
    }

    [Fact]
    public void Page_content_renders_inside_main_under_an_error_boundary()
    {
        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, b => b.AddMarkupContent(0, "<p id=\"page\">page</p>")));

        cut.WaitForAssertion(() => cut.Find("main.ts-main #page").TextContent.ShouldBe("page"));
        cut.Find("nav[aria-label='Admin navigation']").ShouldNotBeNull();
    }

    [Fact]
    public void A_page_that_throws_shows_the_plain_error_view_instead_of_crashing_the_shell()
    {
        RenderFragment throwing = builder =>
        {
            builder.OpenComponent<Throwing>(0);
            builder.CloseComponent();
        };

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, throwing));

        var error = cut.Find("section.ts-error");
        error.QuerySelector("h1")!.TextContent.ShouldBe("Couldn't load this screen.");
        error.TextContent.ShouldNotContain("InvalidOperationException");
        cut.Find(".ts-brand").ShouldNotBeNull();
    }

    private sealed class Throwing : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) =>
            throw new InvalidOperationException("boom");
    }
}
```

`tests/TechStrap.Admin.Tests/ScriptHostTests.cs` (the module scripts must be reachable without signing in; the browser imports them as ES modules):

```csharp
using System.Net;

namespace TechStrap.Admin.Tests;

public sealed class ScriptHostTests
{
    [Theory]
    [InlineData("/js/dialog.js")]
    [InlineData("/js/shortcuts.js")]
    public async Task Module_scripts_are_served_as_javascript_without_signing_in(string path)
    {
        await using var factory = new AdminFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBeOneOf("text/javascript", "application/javascript");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("export ");
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "StateComponentTests|TagChipAndPagerTests|ConfirmDialogTests|TicketDisplayTests|ShortcutServiceTests|ShellComponentTests|MainLayoutTests|ScriptHostTests"`
Expected: a build failure (`LoadingState`, `ConfirmDialog`, `TicketDisplay`, `ShortcutService`, `StatusBar`, `NavMenu`, `AddShell` do not exist).

- [ ] **Step 3: Implement**

1. Wiring. In `src/TechStrap.Admin/Program.cs` add `using TechStrap.Admin.Features.Shell;` and, next to the other `builder.Services` registrations, `builder.Services.AddShell();`. In `src/TechStrap.Admin/Components/_Imports.razor` make sure these lines exist (merge with what Tasks 4 and 5 added, keep the order alphabetical):

```razor
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.JSInterop
@using SyntaxCircus.Blazor.Components.Feedback
@using TechStrap.Admin
@using TechStrap.Admin.Auth
@using TechStrap.Admin.Clients
@using TechStrap.Admin.Components
@using TechStrap.Admin.Components.Layout
@using TechStrap.Admin.Components.Ui
@using TechStrap.Admin.Features.Shell
```

Razor imports do not reach code-behind files, so every `.razor.cs` below carries its own `using`s.

2. The reusable states and primitives:

`src/TechStrap.Admin/Components/Ui/ShellCopy.cs`

```csharp
namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Copy shared by the shell and the reusable states. Plain, sentence case, no humour (docs/BRAND.md section 3); brand moments live in
/// <see cref="BrandMomentCopy"/> and blocking failures in <see cref="UiCopy"/>.
/// </summary>
public static class ShellCopy
{
    public const string Loading = "Loading";
    public const string Cancel = "Cancel";
    public const string Close = "Close";
    public const string Previous = "Previous";
    public const string Next = "Next";
    public const string PaginationLabel = "Pagination";
    public const string QueueLink = "Queue";
    public const string NavigationLabel = "Admin navigation";
    public const string ShortcutHelpTitle = "Keyboard shortcuts";
    public const string ShortcutHelpOpen = "Shortcuts";
    public const string StatusBarLabel = "Keyboard hints and messages";
}
```

`src/TechStrap.Admin/Components/Ui/LoadingState.razor`

```razor
<div class="ts-loading" role="status" aria-live="polite">
    <span class="visually-hidden">@Label</span>
    @for (var i = 0; i < Rows; i++)
    {
        <div class="ts-skeleton" aria-hidden="true"></div>
    }
</div>

@code {
    /// <summary>How many skeleton rows to draw (the queue uses a screenful; a panel uses one or two).</summary>
    [Parameter]
    public int Rows { get; set; } = 5;

    /// <summary>The text a screen reader announces; nothing visible says it.</summary>
    [Parameter]
    public string Label { get; set; } = ShellCopy.Loading;
}
```

`src/TechStrap.Admin/Components/Ui/ErrorState.razor`

```razor
<div class="ts-state ts-state--error" role="alert">
    <p>@Message</p>
    @if (OnRetry.HasDelegate)
    {
        <button type="button" class="btn btn-outline-secondary" @onclick="OnRetry">@RetryLabel</button>
    }
</div>

@code {
    [Parameter, EditorRequired]
    public string Message { get; set; } = string.Empty;

    [Parameter]
    public string RetryLabel { get; set; } = UiCopy.RetryLabel;

    /// <summary>When set, a retry button is drawn and this fires once per click.</summary>
    [Parameter]
    public EventCallback OnRetry { get; set; }
}
```

`src/TechStrap.Admin/Components/Ui/EmptyState.razor`

```razor
<div class="ts-state ts-state--empty">
    <p class="ts-state-heading">@Heading</p>
    @ChildContent
</div>

@code {
    [Parameter, EditorRequired]
    public string Heading { get; set; } = string.Empty;

    /// <summary>Optional next action (a link or a button), already styled by the caller.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }
}
```

`src/TechStrap.Admin/Components/Ui/TagChip.razor`

```razor
<span class="ts-tag" style="@Style">@Name</span>
```

`src/TechStrap.Admin/Components/Ui/TagChip.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Branding;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// A tag as a text chip. Tag colours are arbitrary admin input, so the foreground is always derived from the background through the one
/// shared rule (<see cref="ProductAccent"/>, BRAND.md section 22), and a malformed colour renders the plain chip. The word is the meaning; the colour only decorates.
/// </summary>
public partial class TagChip
{
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>A <c>#RRGGBB</c> colour, as stored on the tag.</summary>
    [Parameter]
    public string? Colour { get; set; }

    private string? Style => ProductAccent.TryDerive(Colour, out var colours)
        ? $"background-color:{colours.Accent};border-color:{colours.Accent};color:{colours.OnAccent}"
        : null;
}
```

`src/TechStrap.Admin/Components/Ui/PagerControl.razor`

```razor
@if (TotalCount > 0)
{
    <nav class="ts-pager" aria-label="@ShellCopy.PaginationLabel">
        <span class="ts-pager-summary">@First&ndash;@Last of @TotalCount</span>
        @if (PageCount > 1)
        {
            <button type="button" class="btn btn-outline-secondary" disabled="@(Page <= 1)" @onclick="PreviousAsync">@ShellCopy.Previous</button>
            <span class="ts-pager-page">Page @Page of @PageCount</span>
            <button type="button" class="btn btn-outline-secondary" disabled="@(Page >= PageCount)" @onclick="NextAsync">@ShellCopy.Next</button>
        }
    </nav>
}
```

`src/TechStrap.Admin/Components/Ui/PagerControl.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>Previous/next paging with a "26-50 of 163" summary. It only reports the requested page; the owner of the list decides what to do with it.</summary>
public partial class PagerControl
{
    /// <summary>The current page, 1-based.</summary>
    [Parameter, EditorRequired]
    public int Page { get; set; } = 1;

    [Parameter, EditorRequired]
    public int PageSize { get; set; } = 1;

    [Parameter, EditorRequired]
    public int TotalCount { get; set; }

    [Parameter]
    public EventCallback<int> OnPageChanged { get; set; }

    private int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize)));

    private int First => ((Page - 1) * PageSize) + 1;

    private int Last => Math.Min(TotalCount, Page * PageSize);

    private Task PreviousAsync() => Page > 1 ? OnPageChanged.InvokeAsync(Page - 1) : Task.CompletedTask;

    private Task NextAsync() => Page < PageCount ? OnPageChanged.InvokeAsync(Page + 1) : Task.CompletedTask;
}
```

`src/TechStrap.Admin/Components/Ui/ConfirmDialog.razor`

```razor
<dialog @ref="_dialog" class="@CssClass" aria-labelledby="@TitleId" aria-describedby="@BodyId"
        @oncancel="CancelAsync" @oncancel:preventDefault="true">
    <h2 id="@TitleId" tabindex="-1" @ref="_title">@Title</h2>
    <div id="@BodyId" class="ts-dialog-body">@ChildContent</div>
    @if (RequiredText is not null)
    {
        <label for="@InputId" class="ts-dialog-label">@ConfirmPrompt</label>
        <input id="@InputId" @ref="_input" type="text" class="form-control" autocomplete="off" spellcheck="false"
               disabled="@Busy" value="@_typed" @oninput="OnTyped" />
    }
    @if (Error is not null)
    {
        <p class="ts-dialog-error" role="alert">@Error</p>
    }
    <div class="ts-dialog-actions">
        <button type="button" class="btn btn-outline-secondary" disabled="@Busy" @onclick="CancelAsync">@CancelLabel</button>
        @if (!Informational)
        {
            <button type="button" class="@ConfirmCss" disabled="@(!CanConfirm)" @onclick="ConfirmAsync">@ConfirmLabel</button>
        }
    </div>
</dialog>
```

`src/TechStrap.Admin/Components/Ui/ConfirmDialog.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// A modal confirmation built on the native <c>dialog</c> element (the browser supplies the focus trap and the inert background).
/// Enter never confirms: there is no form, both buttons are <c>type="button"</c>, and initial focus goes to the typed-confirmation input
/// or, without one, to the heading (never to a button). Esc cancels, except while a request is running. The owner controls <see cref="Open"/>.
/// </summary>
public partial class ConfirmDialog : IAsyncDisposable
{
    private const string ModulePath = "./js/dialog.js";
    private static int _nextId;

    private readonly int _id = Interlocked.Increment(ref _nextId);
    private ElementReference _dialog;
    private ElementReference _title;
    private ElementReference _input;
    private IJSObjectReference? _module;
    private bool _shown;
    private bool _openSeen;
    private string _typed = string.Empty;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    [Parameter]
    public bool Open { get; set; }

    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter]
    public string ConfirmLabel { get; set; } = "Confirm";

    [Parameter]
    public string CancelLabel { get; set; } = ShellCopy.Cancel;

    /// <summary>An information dialog (the shortcut list): no confirm button, and the one remaining button, labelled by <see cref="CancelLabel"/>, closes it.</summary>
    [Parameter]
    public bool Informational { get; set; }

    /// <summary>Styles the confirm button as destructive (a danger style plus the word in the label, never colour alone).</summary>
    [Parameter]
    public bool Danger { get; set; }

    /// <summary>When set, Confirm stays disabled until the user types exactly this text (compared ignoring case and surrounding spaces).</summary>
    [Parameter]
    public string? RequiredText { get; set; }

    /// <summary>A request is running: both buttons, the input and Esc are inert.</summary>
    [Parameter]
    public bool Busy { get; set; }

    /// <summary>The failure from the last confirm. The dialog stays open and nothing else changes.</summary>
    [Parameter]
    public string? Error { get; set; }

    [Parameter]
    public EventCallback OnConfirm { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private string TitleId => $"ts-dialog-title-{_id}";

    private string BodyId => $"ts-dialog-body-{_id}";

    private string InputId => $"ts-dialog-input-{_id}";

    private string CssClass => Danger ? "ts-dialog ts-dialog--danger" : "ts-dialog";

    private string ConfirmCss => Danger ? "btn btn-danger" : "btn btn-primary";

    private string ConfirmPrompt => $"Type {RequiredText} to confirm";

    private bool CanConfirm => !Busy && (RequiredText is null || string.Equals(_typed.Trim(), RequiredText, StringComparison.OrdinalIgnoreCase));

    protected override void OnParametersSet()
    {
        if (Open && !_openSeen)
        {
            _typed = string.Empty;
        }

        _openSeen = Open;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Open == _shown)
        {
            return;
        }

        _shown = Open;
        _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        if (Open)
        {
            await _module.InvokeVoidAsync("open", _dialog, RequiredText is null ? _title : _input);
        }
        else
        {
            await _module.InvokeVoidAsync("close", _dialog);
        }
    }

    private void OnTyped(ChangeEventArgs e) => _typed = e.Value as string ?? string.Empty;

    private Task ConfirmAsync() => CanConfirm ? OnConfirm.InvokeAsync() : Task.CompletedTask;

    private Task CancelAsync() => Busy ? Task.CompletedTask : OnCancel.InvokeAsync();

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            if (_shown)
            {
                await _module.InvokeVoidAsync("close", _dialog);
            }

            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone; the browser has dropped the dialog with the page.
        }
    }
}
```

`src/TechStrap.Admin/Components/Ui/TicketDisplay.cs`

```csharp
using System.Globalization;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Maps the API's string vocabulary (Contracts carries no enums) onto the presentation enums and display text. An unknown status or
/// priority throws: both sets are closed, so a new value is a contract change that must fail loudly in the error boundary, not render as a guess.
/// </summary>
internal static class TicketDisplay
{
    private const int MinutesPerHour = 60;
    private const int HoursPerDay = 24;
    private const int DaysPerWeek = 7;
    private const double BytesPerKilobyte = 1024;

    /// <summary>The stamp for a ticket. The spam flag wins over the status (the status is unchanged underneath, D-024).</summary>
    public static StampStatus Stamp(string status, bool isSpam) => isSpam
        ? StampStatus.Spam
        : status switch
        {
            TicketStatuses.New => StampStatus.New,
            TicketStatuses.Open => StampStatus.Open,
            TicketStatuses.Pending => StampStatus.Pending,
            TicketStatuses.Solved => StampStatus.Solved,
            TicketStatuses.Closed => StampStatus.Closed,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown ticket status."),
        };

    public static PriorityLevel Priority(string priority) => priority switch
    {
        TicketPriorities.Urgent => PriorityLevel.Urgent,
        TicketPriorities.High => PriorityLevel.High,
        TicketPriorities.Normal => PriorityLevel.Normal,
        TicketPriorities.Low => PriorityLevel.Low,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown ticket priority."),
    };

    /// <summary>"just now", "5 min ago", "3 h ago", "2 d ago", then the UTC date. The caller supplies <paramref name="now"/> (a <see cref="TimeProvider"/>), so tests control it.</summary>
    public static string Relative(DateTimeOffset when, DateTimeOffset now)
    {
        var delta = now - when;
        if (delta < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (delta.TotalMinutes < MinutesPerHour)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)delta.TotalMinutes} min ago");
        }

        if (delta.TotalHours < HoursPerDay)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)delta.TotalHours} h ago");
        }

        return delta.TotalDays < DaysPerWeek
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)delta.TotalDays} d ago")
            : when.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>The absolute time for a tooltip. Always UTC and labelled so: converting to the agent's zone needs the browser's zone (recorded gap, PHASE-07c).</summary>
    public static string Absolute(DateTimeOffset when) => when.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Up to two initials for an avatar: "Sam Ortiz" is "SO", "sam" is "S", nothing is "?".</summary>
    public static string Initials(string? name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0
            ? "?"
            : string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / BytesPerKilobyte:0.#} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (BytesPerKilobyte * BytesPerKilobyte):0.#} MB"),
    };
}
```

`src/TechStrap.Admin/Components/Ui/RelativeTime.razor`

```razor
<time datetime="@When.UtcDateTime.ToString("o")" title="@TicketDisplay.Absolute(When)">@TicketDisplay.Relative(When, Time.GetUtcNow())</time>
```

`src/TechStrap.Admin/Components/Ui/RelativeTime.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Components.Ui;

/// <summary>"5 min ago" as visible text, with the machine-readable <c>datetime</c> and the absolute UTC time in the tooltip.</summary>
public partial class RelativeTime
{
    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Parameter, EditorRequired]
    public DateTimeOffset When { get; set; }
}
```

3. The keyboard layer and the message slot:

`src/TechStrap.Admin/Features/Shell/StatusMessageService.cs`

```csharp
namespace TechStrap.Admin.Features.Shell;

/// <summary>
/// The one-line confirmations in the status bar ("Reply sent on ACME-142"). Scoped, so there is one per circuit. A message stays until the next
/// one replaces it. It supplements, and never replaces, an inline error for a failure that needs action.
/// </summary>
public sealed class StatusMessageService
{
    public string? Current { get; private set; }

    public event Action? Changed;

    public void Show(string message)
    {
        Current = message;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Current = null;
        Changed?.Invoke();
    }
}
```

`src/TechStrap.Admin/Features/Shell/ShortcutService.cs`

```csharp
using Microsoft.JSInterop;

namespace TechStrap.Admin.Features.Shell;

public enum ShortcutAction
{
    MoveDown,
    MoveUp,
    OpenSelected,
    FocusSearch,
    Reply,
    Note,
    FocusAssignee,
    Send,
    Escape,
    Help,
}

/// <summary>What <c>wwwroot/js/shortcuts.js</c> reports for one key press. The script decides <see cref="Typing"/> and <see cref="OnBody"/> from the focused element.</summary>
/// <param name="Key">The <c>KeyboardEvent.key</c> value.</param>
/// <param name="Typing">Focus is in a text field, select or editable area.</param>
/// <param name="OnBody">Nothing interactive has focus (so Enter and the arrows are free to act on the page).</param>
/// <param name="Scope">The nearest <c>data-shortcut-scope</c> of the focused element, if any.</param>
public sealed record KeyPress(string Key, bool Ctrl, bool Meta, bool Alt, bool Typing, bool OnBody, string? Scope);

/// <summary>
/// The keyboard layer (UX-BRIEF-admin, Density and keyboard shortcuts). The script reports key presses; this class decides whether one is a
/// shortcut and tells whoever subscribed. Pages and components subscribe to <see cref="Pressed"/> while they are on screen and ignore what is not theirs.
/// Single-key shortcuts are off while the user types, and when <see cref="SingleKeyEnabled"/> is false (WCAG 2.1.4). The command palette is deferred to PHASE-07c.
/// </summary>
public sealed class ShortcutService(IJSRuntime js) : IAsyncDisposable
{
    /// <summary>The <c>data-shortcut-scope</c> value of the reply composer; Ctrl+Enter sends only from inside it.</summary>
    public const string ComposerScope = "composer";

    private const string ModulePath = "./js/shortcuts.js";

    private IJSObjectReference? _module;
    private DotNetObjectReference<ShortcutService>? _self;

    /// <summary>Raised once per recognised shortcut; every handler is awaited in subscription order.</summary>
    public event Func<ShortcutAction, Task>? Pressed;

    /// <summary>The My settings toggle arrives in PHASE-07b; until then the layer is always on.</summary>
    public bool SingleKeyEnabled { get; set; } = true;

    /// <summary>The pure decision: which action, if any, a key press means.</summary>
    public static ShortcutAction? Map(KeyPress press, bool singleKeyEnabled)
    {
        if (press.Ctrl || press.Meta)
        {
            return press.Key == "Enter" && press.Scope == ComposerScope ? ShortcutAction.Send : null;
        }

        if (press.Alt)
        {
            return null;
        }

        if (press.Key == "Escape")
        {
            // While typing, the script only blurs the field (the text is kept); this is the "back" Escape.
            return press.Typing ? null : ShortcutAction.Escape;
        }

        if (press.Typing || !singleKeyEnabled)
        {
            return null;
        }

        var key = press.Key.Length == 1 ? press.Key.ToLowerInvariant() : press.Key;
        return key switch
        {
            "j" => ShortcutAction.MoveDown,
            "k" => ShortcutAction.MoveUp,
            "ArrowDown" when press.OnBody => ShortcutAction.MoveDown,
            "ArrowUp" when press.OnBody => ShortcutAction.MoveUp,
            "Enter" when press.OnBody => ShortcutAction.OpenSelected,
            "/" => ShortcutAction.FocusSearch,
            "r" => ShortcutAction.Reply,
            "n" => ShortcutAction.Note,
            "e" => ShortcutAction.FocusAssignee,
            "?" => ShortcutAction.Help,
            _ => null,
        };
    }

    /// <summary>Imports the module and registers the document listener. Call it once per circuit, after the first render.</summary>
    public async Task StartAsync()
    {
        if (_module is not null)
        {
            return;
        }

        _module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        _self = DotNetObjectReference.Create(this);
        await _module.InvokeVoidAsync("register", _self);
    }

    [JSInvokable]
    public async Task OnKeyAsync(KeyPress press)
    {
        var action = Map(press, SingleKeyEnabled);
        if (action is null || Pressed is null)
        {
            return;
        }

        foreach (var handler in Pressed.GetInvocationList().Cast<Func<ShortcutAction, Task>>())
        {
            await handler(action.Value);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("unregister");
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, and so is the page that held the listener.
        }

        _self?.Dispose();
    }
}
```

`src/TechStrap.Admin/Features/Shell/ShortcutCatalog.cs`

```csharp
namespace TechStrap.Admin.Features.Shell;

/// <summary>One row of the shortcut help dialog.</summary>
public sealed record ShortcutEntry(string Keys, string Context, string Description);

/// <summary>Every shortcut the help dialog lists. Two entries never share a key (pinned by a test), so the list is also the clash check.</summary>
public static class ShortcutCatalog
{
    public static IReadOnlyList<ShortcutEntry> All { get; } =
    [
        new("j / k", "Queue", "Move the selection down or up (the arrow keys work too)"),
        new("Enter", "Queue", "Open the selected ticket"),
        new("/", "Anywhere", "Focus search (opens the queue from other screens)"),
        new("r", "Ticket", "Public reply: open the tab and focus the box"),
        new("n", "Ticket", "Internal note: open the tab and focus the box"),
        new("e", "Ticket", "Focus the assignee control"),
        new("Ctrl+Enter", "Reply box", "Send in the current mode"),
        new("Esc", "Anywhere", "Leave a field (your text is kept), close a dialog, or go back to the queue"),
        new("?", "Anywhere", "Show this list"),
    ];

    /// <summary>The one-line hints in the status bar, in display order.</summary>
    public static IReadOnlyList<(string Keys, string Label)> Hints { get; } =
    [
        ("j k", "move"),
        ("Enter", "open"),
        ("r", "reply"),
        ("n", "note"),
        ("e", "assign"),
        ("/", "search"),
        ("?", "help"),
    ];
}
```

`src/TechStrap.Admin/Features/Shell/ShellServiceCollectionExtensions.cs`

```csharp
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TechStrap.Admin.Features.Shell;

public static class ShellServiceCollectionExtensions
{
    /// <summary>Registers the per-circuit shell services. Scoped, so each circuit has its own message slot and its own key listener.</summary>
    public static IServiceCollection AddShell(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<StatusMessageService>();
        services.AddScoped<ShortcutService>();
        return services;
    }
}
```

4. The shell. `MainLayout.razor` replaces the old file; the old inline `<nav>` (brand, Home link, `SignOutForm`) is now `NavMenu`:

`src/TechStrap.Admin/Components/Layout/NavMenu.razor`

```razor
<nav class="ts-rail" aria-label="@ShellCopy.NavigationLabel">
    <a class="ts-brand" href="/" aria-label="TechStrap Admin home">
        <img src="brand/mark.svg" alt="" width="36" height="36" />
        <span>TechStrap<small>Call log / admin</small></span>
    </a>
    @if (Session.State == AgentSessionState.Ready)
    {
        <NavLink class="ts-rail-link" href="/queue" Match="NavLinkMatch.Prefix">@ShellCopy.QueueLink</NavLink>
        <div class="ts-rail-user">
            <span class="ts-rail-name">@DisplayName</span>
            @if (Session.IsAdmin)
            {
                <span class="ts-rail-role">Admin</span>
            }
            <SignOutForm />
        </div>
    }
</nav>
```

`src/TechStrap.Admin/Components/Layout/NavMenu.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Components.Ui;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// The left rail. It shows the agent's links only once <see cref="AgentSession"/> is Ready, so the anonymous pages that share <c>MainLayout</c> (not found, error),
/// and a user the API refused, see the brand alone. It re-renders when the session changes (the gate loads it after the layout first renders).
/// The Admin-only links (settings, dead letters) arrive in PHASE-07b. Sign out is the shared <c>SignOutForm</c>.
/// </summary>
public sealed partial class NavMenu : IDisposable
{
    [Inject]
    private AgentSession Session { get; set; } = default!;

    private string DisplayName => Session.Agent is { } agent ? (string.IsNullOrWhiteSpace(agent.Name) ? agent.Email : agent.Name) : string.Empty;

    protected override void OnInitialized() => Session.Changed += OnSessionChanged;

    private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Session.Changed -= OnSessionChanged;
}
```

`src/TechStrap.Admin/Components/Layout/StatusBar.razor`

```razor
<footer class="ts-statusbar" aria-label="@ShellCopy.StatusBarLabel">
    <ul class="ts-statusbar-hints">
        @foreach (var (keys, label) in ShortcutCatalog.Hints)
        {
            <li><Kbd>@keys</Kbd> @label</li>
        }
    </ul>
    <p class="ts-statusbar-message" role="status">@Messages.Current</p>
</footer>
```

`src/TechStrap.Admin/Components/Layout/StatusBar.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Layout;

/// <summary>The footer: the current shortcut hints and one polite message slot (<c>role="status"</c>) for short confirmations.</summary>
public partial class StatusBar : IDisposable
{
    [Inject]
    private StatusMessageService Messages { get; set; } = default!;

    protected override void OnInitialized() => Messages.Changed += OnChanged;

    private void OnChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Messages.Changed -= OnChanged;
}
```

`src/TechStrap.Admin/Components/Layout/ShortcutHelpDialog.razor`

```razor
<ConfirmDialog Open="Open" Title="@ShellCopy.ShortcutHelpTitle" Informational="true" CancelLabel="@ShellCopy.Close" OnCancel="OnClose">
    <table class="ts-shortcut-table">
        <thead>
            <tr><th scope="col">Keys</th><th scope="col">Where</th><th scope="col">What it does</th></tr>
        </thead>
        <tbody>
            @foreach (var entry in ShortcutCatalog.All)
            {
                <tr>
                    <td><Kbd>@entry.Keys</Kbd></td>
                    <td>@entry.Context</td>
                    <td>@entry.Description</td>
                </tr>
            }
        </tbody>
    </table>
    <p>Single-key shortcuts are off while you type in a field.</p>
</ConfirmDialog>

@code {
    [Parameter]
    public bool Open { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }
}
```

`src/TechStrap.Admin/Components/Layout/MainLayout.razor`

```razor
@inherits LayoutComponentBase

<a class="ts-skip-link" href="#main">Skip to main content</a>
<div class="ts-shell">
    <NavMenu />
    <div class="ts-content">
        <main class="ts-main" id="main">
            <GlobalErrorBoundary BoundaryName="admin UI"
                                 CssClass="ts-error"
                                 Title="@UiCopy.ErrorTitle"
                                 Description="@UiCopy.ErrorDescription"
                                 HomeLabel="@UiCopy.ErrorHomeLabel">
                <AgentGate>@Body</AgentGate>
            </GlobalErrorBoundary>
        </main>
        <StatusBar />
    </div>
</div>
<ShortcutHelpDialog Open="_helpOpen" OnClose="CloseHelp" />
```

`src/TechStrap.Admin/Components/Layout/MainLayout.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// The frame around every page: rail, content, status bar, and the keyboard layer's two global jobs: start the key listener once per
/// circuit, and answer the shortcuts no page owns (help, and "/" from a screen that has no search box).
/// </summary>
public partial class MainLayout : IDisposable
{
    private const string QueuePath = "queue";

    private bool _helpOpen;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await Shortcuts.StartAsync();
        }
    }

    private Task OnShortcutAsync(ShortcutAction action)
    {
        switch (action)
        {
            case ShortcutAction.Help:
                _helpOpen = true;
                return InvokeAsync(StateHasChanged);
            case ShortcutAction.FocusSearch when !IsOnQueue():
                Navigation.NavigateTo("/" + QueuePath);
                break;
        }

        return Task.CompletedTask;
    }

    private bool IsOnQueue()
    {
        var path = Navigation.ToBaseRelativePath(Navigation.Uri);
        var end = path.IndexOfAny(['?', '#']);
        path = end < 0 ? path : path[..end];
        return path.Length == 0 || path.StartsWith(QueuePath, StringComparison.OrdinalIgnoreCase);
    }

    private void CloseHelp() => _helpOpen = false;

    public void Dispose() => Shortcuts.Pressed -= OnShortcutAsync;
}
```

5. The scripts (ES modules; the Admin has no inline script anywhere). The queue module arrives in Task 8:

`src/TechStrap.Admin/wwwroot/js/dialog.js`

```javascript
// Opens and closes a native <dialog> for ConfirmDialog. showModal() gives the focus trap and the inert background;
// the caller names the element that takes the first focus (the typed-confirmation input, or the heading), never a button.

export function open(dialog, focusTarget) {
    if (!dialog.open) {
        dialog.showModal();
    }

    (focusTarget || dialog).focus();
}

export function close(dialog) {
    if (dialog.open) {
        dialog.close();
    }
}
```

`src/TechStrap.Admin/wwwroot/js/shortcuts.js`

```javascript
// The keyboard layer's one document listener (UX-BRIEF-admin, Density and keyboard shortcuts). It only reports key presses; ShortcutService decides
// what they mean. To keep the circuit quiet it reports nothing but the keys the layer can use, and nothing while the user types (except Ctrl/Cmd+Enter,
// which sends from the composer). Escape while typing blurs the field and keeps the text.

const TEXT_TARGET = 'input, textarea, select, [contenteditable=""], [contenteditable="true"]';
const NON_TEXT_INPUTS = new Set(['button', 'checkbox', 'radio', 'submit', 'reset', 'file', 'range', 'color', 'image']);

// The keys ShortcutService maps; any other key is never sent. (u is Not spam.)
const RELEVANT = new Set(['j', 'k', 'ArrowDown', 'ArrowUp', 'Enter', '/', 'r', 'n', 'e', 'u', '?', 'Escape']);

let reference = null;
let listener = null;

function isTyping(element) {
    if (!element || !element.closest) {
        return false;
    }

    const target = element.closest(TEXT_TARGET);
    return !!target && !(target.tagName === 'INPUT' && NON_TEXT_INPUTS.has(target.type));
}

export function register(dotNetReference) {
    unregister();
    reference = dotNetReference;
    listener = (event) => {
        if (event.defaultPrevented || event.isComposing) {
            return;
        }

        // A modal dialog owns the keyboard: Esc and Enter belong to it.
        if (document.querySelector('dialog[open]')) {
            return;
        }

        const active = document.activeElement;
        const typing = isTyping(active);
        const chord = event.ctrlKey || event.metaKey;

        if (event.key === 'Escape' && typing) {
            active.blur();
            return;
        }

        const key = event.key.length === 1 ? event.key.toLowerCase() : event.key;
        const sendFromComposer = chord && event.key === 'Enter';
        if (!sendFromComposer && (typing || chord || event.altKey || !RELEVANT.has(key))) {
            return;
        }

        const scopeElement = active && active.closest ? active.closest('[data-shortcut-scope]') : null;
        const scope = scopeElement ? scopeElement.dataset.shortcutScope : null;
        const onBody = !active || active === document.body || active === document.documentElement;

        // Stop the browser's own use of the key (Firefox quick-find on "/", page scroll on the arrows in the queue, a form submit on Ctrl+Enter).
        if (sendFromComposer && scope === 'composer') {
            event.preventDefault();
        } else if (key === '/' || key === '?') {
            event.preventDefault();
        } else if ((key === 'ArrowDown' || key === 'ArrowUp') && onBody && document.querySelector('[data-shortcut-scope="queue"]')) {
            event.preventDefault();
        }

        reference.invokeMethodAsync('OnKeyAsync', {
            key: event.key,
            ctrl: event.ctrlKey,
            meta: event.metaKey,
            alt: event.altKey,
            typing,
            onBody,
            scope,
        });
    };
    document.addEventListener('keydown', listener);
}

export function unregister() {
    if (listener) {
        document.removeEventListener('keydown', listener);
    }

    listener = null;
    reference = null;
}
```

6. The styles. Plain CSS over the brand custom properties only (no new colour, no Bootstrap mixin), so they compile on their own. Add the three imports to `src/TechStrap.Admin/Styles/app.scss` after `@import "feedback";`:

```scss
@import "states";
@import "dialog";
@import "statusbar";
```

`src/TechStrap.Admin/Styles/_states.scss`

```scss
// Reusable page states (PHASE-07a): loading skeleton, error alert and plain empty state. Plain by rule: no mascot, no window frame.
// The skeleton is a static block, not an animation, so reduced-motion needs nothing special.

.ts-loading {
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 12px 16px;
}

.ts-skeleton {
  height: 28px;
  background: var(--head);
  border: 1px solid var(--rule);
}

.ts-state {
  max-width: 560px;
  margin: 16px;
  padding: 12px 16px;
  background: var(--sheet);
  border: 1px solid var(--rule-strong);

  p {
    margin: 0 0 8px;
  }
}

.ts-state--error {
  border: 2px solid var(--st-spam);
  box-shadow: 3px 3px 0 var(--shadow);
}

.ts-state-heading {
  font: 600 .9375rem var(--ts-font-mono);
}

// A signed-in user the API refused (NoAccessPage): a plain page, deliberately without the window frame.
.ts-noaccess {
  max-width: 560px;
  margin: 48px 16px;

  h1 {
    margin: 0 0 12px;
    font: 600 1.25rem/1.3 var(--ts-font-mono);
  }
}

.ts-noaccess-identity {
  color: var(--ink-2);
}

.ts-gone {
  max-width: 560px;
  margin: 48px 16px;

  h1 {
    margin: 0 0 12px;
    font: 600 1.25rem/1.3 var(--ts-font-mono);
  }
}

// Chips: a text label always carries the meaning, the colour only decorates.
.ts-tag,
.ts-product {
  display: inline-block;
  padding: 0 6px;
  font: 500 .6875rem/1.6 var(--ts-font-mono);
  letter-spacing: .03em;
  color: var(--ink);
  background: var(--head);
  border: 1px solid var(--rule-strong);
}

.ts-tag + .ts-tag {
  margin-left: 4px;
}

.ts-pager {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
  padding: 8px 16px;
  font: 400 .75rem var(--ts-font-mono);
}

.ts-pager-summary {
  margin-right: auto;
}
```

`src/TechStrap.Admin/Styles/_dialog.scss`

```scss
// Native <dialog> confirmations and the shortcut help table (PHASE-07a). The dialog is plain working UI: no mascot, no window frame.

.ts-dialog {
  width: min(34rem, calc(100vw - 32px));
  padding: 16px;
  color: var(--ink);
  background: var(--sheet);
  border: 2px solid var(--ink);
  box-shadow: 4px 4px 0 var(--shadow);

  &::backdrop {
    background: var(--scrim);
  }

  // The heading takes focus when nothing must be typed; it is not interactive, so it shows no ring.
  h2 {
    margin: 0 0 8px;
    font: 600 1.0625rem/1.3 var(--ts-font-mono);

    &:focus {
      outline: none;
    }
  }
}

// A destructive dialog gets a heavier edge and the danger colour on its heading, plus the word in its buttons.
.ts-dialog--danger {
  border-color: var(--st-spam);

  h2 {
    color: var(--st-spam);
  }
}

.ts-dialog-label {
  display: block;
  margin: 12px 0 4px;
  font: 500 .75rem var(--ts-font-mono);
}

.ts-dialog-error {
  margin: 12px 0 0;
  padding: 6px 10px;
  border: 2px solid var(--st-spam);
}

.ts-dialog-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  justify-content: flex-end;
  margin-top: 16px;
}

.ts-shortcut-table {
  width: 100%;
  font-size: .8125rem;

  th {
    font: 600 .6875rem var(--ts-font-mono);
    text-transform: uppercase;
    text-align: left;
  }

  td,
  th {
    padding: 3px 8px 3px 0;
    vertical-align: top;
  }
}
```

`src/TechStrap.Admin/Styles/_statusbar.scss`

```scss
// The shell additions of PHASE-07a: the skip link, the status bar under the page, and the signed-in block at the foot of the rail.

.ts-skip-link {
  position: absolute;
  left: -9999px;

  &:focus {
    left: 8px;
    top: 8px;
    z-index: 10;
    padding: 6px 10px;
    background: var(--sheet);
    border: 2px solid var(--ink);
  }
}

.ts-statusbar {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 16px;
  align-items: center;
  padding: 4px 16px;
  font: 400 .6875rem var(--ts-font-mono);
  color: var(--ink-2);
  background: var(--head);
  border-top: 1px solid var(--rule-strong);
}

.ts-statusbar-hints {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 12px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.ts-statusbar-message {
  margin: 0 0 0 auto;
  color: var(--ink);
}

// The shell now wraps the page and the status bar so the bar stays at the foot of the screen.
.ts-content {
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 100vh;

  .ts-main {
    flex: 1;
  }
}

.ts-rail-user {
  display: flex;
  flex-direction: column;
  gap: 2px;
  margin-top: auto;
  padding: 8px 14px;
  border-top: 1px dashed var(--rule-strong);
  font-size: .75rem;

  form {
    margin: 0;
  }

  button.ts-rail-link {
    width: 100%;
    padding-left: 0;
    background: none;
    border-top: 0;
    border-right: 0;
    border-bottom: 0;
    text-align: left;
  }
}

.ts-rail-role {
  font: 600 .625rem var(--ts-font-mono);
  letter-spacing: .05em;
  text-transform: uppercase;
  color: var(--ink-2);
}
```

- [ ] **Step 4: Run the tests**

Run the filter from Step 2, then the whole project, which includes the existing error-boundary and reconnect tests the spec asks to verify (`MainLayoutTests`, `ReconnectAndErrorTests`) and the style tests (`StyleBuildTests`, `TintStyleTests`, `TokenContrastTests`):

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "ShellHostTests|Admin_reports_live_only"
```

Expected: PASS. If a style test fails because the compiled CSS no longer has a rule it reads, you changed an existing partial: the three new partials only add classes.

- [ ] **Step 5: Check the keyboard layer in a browser (UNVERIFIED by tests: the script is not run by bUnit)**

Run the Admin against the stub or a dev API, sign in, and check in the browser:
- `?` opens the shortcut dialog with focus on its heading, Esc closes it and focus returns to the page.
- With the cursor in a text box, `j`, `/` and `r` type the letter and nothing else happens; Esc leaves the box and keeps the text.
- With a dialog open, no shortcut fires.
- each module passes a syntax check (a slip fails the whole layer silently): `node --input-type=module --check < src/TechStrap.Admin/wwwroot/js/shortcuts.js`, and the same for `dialog.js`.

- [ ] **Step 6: Commit**

```bash
git diff --cached --stat   # after git add, before committing
git add src tests
git commit -m "feat(admin): shell primitives, status bar and the keyboard layer" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---
### Task 8: The queue

**Files:**
- Delete: `src/TechStrap.Admin/Components/Pages/Home.razor` (the placeholder; the queue takes `/`)
- Create: `src/TechStrap.Admin/Features/_Imports.razor`
- Create: `src/TechStrap.Admin/Features/Queue/QueueModel.cs`, `TicketQueuePage.razor` and `.razor.cs`, `QueueViewTabs.razor`, `QueueFilterBar.razor` and `.razor.cs`, `TicketRow.razor` and `.razor.cs`
- Create: `src/TechStrap.Admin/wwwroot/js/queue.js`, `src/TechStrap.Admin/Styles/_queue.scss`; modify `src/TechStrap.Admin/Styles/app.scss`
- Create: `tests/TechStrap.Admin.Tests/Support/TestData.Queue.cs`, `tests/TechStrap.Admin.Tests/Components/TicketQueuePageTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/ScriptHostTests.cs`, and any host test that still asserts the placeholder page (Step 4)

**Interfaces:**
- Consumes: `ITicketsClient.ListAsync(ListTicketsRequest, ct)` returning `Result<PagedResponse<TicketSummaryDto>>` and `GetCountsAsync(ct)` returning `Result<TicketViewCountsResponse>`; `IProductsClient.ListAsync(ct)`, `ITagsClient.ListAsync(ct)`; `ShortcutService`, `LoadingState`, `ErrorState`, `EmptyState`, `PagerControl`, `TagChip`, `RelativeTime`, `TicketDisplay`, `BrandWindow`, `StatusStamp`, `PriorityMark` (Task 7 and earlier); Contracts `TicketViews`, `TicketStatuses`, `TicketPriorities`, `ListTicketsRequest`, `TicketSummaryDto`, `TicketViewCountsResponse`, `PagedResponse<T>`.
- Produces:

```csharp
// TechStrap.Admin.Features.Queue
public static class QueueDefaults { const int PageSize = 25; static readonly TimeSpan SearchDebounce = 300 ms; const string View = TicketViews.Unassigned; }
public static class QueueQueryKeys { Product, Status, Priority, Tag, Search, Page }          // "product", "status", "priority", "tag", "search", "page"
public sealed record QueueFilter(string View, Guid? ProductId, string? Status, string? Priority, Guid? TagId, string? Search, int Page)   // HasFilters, ToRequest(), Cleared()
public static class QueueViews { All; Parse(segment); Slug(view); CountOf(counts, view) }
public static class QueueLinks { static string Uri(QueueFilter filter) }                     // /queue/mine?status=Pending&page=2
public sealed record TicketRowViewModel(...)                                                   // From(TicketSummaryDto)
public sealed partial class TicketQueuePage                                                    // @page "/" and "/queue/{View?}", [Authorize]
```

**Rules:**
1. **Routes.** Exactly `@page "/"` and `@page "/queue/{View?}"`. A third `@page "/queue"` would make `/queue` ambiguous with the optional segment (the router throws). The view segment is case-insensitive; an unknown one calls `NavigationManager.NotFound()` and loads nothing.
2. **The default view is Unassigned** (UX), not the API's `TicketViews.Default` (All). No "remember the last view" in 07a. Only the Spam tab ever requests `TicketViews.Spam`; the five other tabs never do (D-024). Pinned by a theory over all six views.
3. **All filter state is in the URL.** Tabs are real links (a view can be opened in a new tab or bookmarked); a tab link keeps the filters and drops the page. A filter or search change navigates with `replace: true` (no history spam); a page change pushes. The page reloads when the parsed `QueueFilter` differs from the one it loaded (record equality), so an unrelated parameter set never reloads.
4. **Search is debounced by `QueueDefaults.SearchDebounce` through `TimeProvider.CreateTimer`**, so a test advances a `FakeTimeProvider`. Enter commits at once. A new search goes back to page 1.
5. **Failure handling.** A failed list shows `ErrorState` with the API's message and Retry. A failed **refresh** keeps the rows already on screen and shows the alert above them (UX). A failed counts call only hides the numbers; a failed products or tags call only leaves that filter with no options.
6. **Empty states.** Truly empty Unassigned, Mine or Open with no filters and no search: the "All caught up" brand window (title `queue.exe — 0 items`, the em dash the existing brand tests already use), with "View open tickets" (on Open itself, "View all tickets"). Filtered-empty: "No tickets match" and a Clear filters button, never the window. Pending, All and Spam: plain text ("No pending tickets", "No tickets yet", "No spam").
7. **Keyboard.** `j`/`k`/arrows move a selection that never reorders rows (`aria-current="true"`, scrolled into view by `queue.js`); Enter opens it; `/` focuses the search box. The handlers are removed on dispose. `u` arrives in Task 12.
8. **Every client call gets a cancellable token** from a page-lifetime source (lookups) or a per-load source linked to it (list and counts); a newer load cancels the older one, and a cancelled load never touches state.
9. **No bulk actions, no live updates** (queue is manual-refresh until PHASE-10: the Refresh button). The product filter stays a plain select; the "searchable combobox above N products" decision needs a realistic product list and is left to the rendered review of 07c.
10. **Authorization.** The page carries `@attribute [Authorize]` (as the old `Home.razor` did) on top of the fallback policy; `AgentGate` holds the page back until `GET /api/agents/me` has succeeded, so no queue call precedes it (Review Focus 1).

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/Support/TestData.Queue.cs`

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Support;

/// <summary>Small builders for DTOs and <see cref="Result"/> values, shared by the component tests (one file per feature, all one partial class).</summary>
internal static partial class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    public static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid BugTagId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    public static Result<T> Ok<T>(T value) => Result<T>.Success(value);

    public static Result Ok() => Result.Success();

    public static Result<T> Fail<T>(string code, string message = "Something went wrong.", ResultErrorKind kind = ResultErrorKind.Failure) =>
        Result<T>.Failure(new ResultError(code, message, kind));

    public static Result Fail(string code, string message = "Something went wrong.", ResultErrorKind kind = ResultErrorKind.Failure) =>
        Result.Failure(new ResultError(code, message, kind));

    public static ProductDto Product(string name = "Orbitly", Guid? id = null) => new(
        id ?? OrbitlyId, name.ToLowerInvariant(), name, name[..3].ToUpperInvariant(), IsActive: true,
        new ProductBrandingDto(name, null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), Version: 1);

    public static TagDto Tag(string name = "bug", Guid? id = null, string colour = "#DC2626") => new(id ?? BugTagId, name, name, colour);

    public static TicketSummaryDto Summary(
        string number = "ORB-1",
        string subject = "Cannot log in",
        string status = TicketStatuses.Open,
        string priority = TicketPriorities.Normal,
        bool isSpam = false,
        string? assignee = null,
        string? requesterName = "Ada Lovelace",
        IReadOnlyList<TicketTagDto>? tags = null) => new(
            Guid.NewGuid(), number, subject, status, priority, OrbitlyId, "Orbitly",
            Guid.NewGuid(), "ada@example.com", requesterName,
            assignee is null ? null : Guid.NewGuid(), assignee,
            isSpam, tags ?? [], Now.AddDays(-1), Now.AddMinutes(-5));

    public static PagedResponse<TicketSummaryDto> Page(IReadOnlyList<TicketSummaryDto> items, int page = 1, int total = -1) =>
        new(items, page, 25, total < 0 ? items.Count : total);

    public static TicketViewCountsResponse Counts(int unassigned = 3, int mine = 2, int open = 5, int pending = 1, int all = 11, int spam = 4) =>
        new(unassigned, mine, open, pending, all, spam);
}
```

`tests/TechStrap.Admin.Tests/Components/TicketQueuePageTests.cs`

```csharp
using Bunit;
using SyntaxCircus.Common;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Queue;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class TicketQueuePageTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly ITagsClient _tags = Substitute.For<ITagsClient>();
    private readonly BunitJSModuleInterop _queueModule;
    private readonly NavigationManager _navigation;

    public TicketQueuePageTests()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => TestData.Ok(TestData.Page([TestData.Summary("ORB-1"), TestData.Summary("ORB-2", "Billing question")])));
        _tickets.GetCountsAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Counts()));
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        _tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(_products);
        Services.AddSingleton(_tags);
        _queueModule = JSInterop.SetupModule("./js/queue.js");
        _queueModule.SetupVoid("scrollSelectedIntoView", _ => true).SetVoidResult();
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<TicketQueuePage> RenderQueue(string? view = null, string query = "")
    {
        _navigation.NavigateTo($"/queue/{view}{query}");
        return Render<TicketQueuePage>(p => p.Add(c => c.View, view));
    }

    private IEnumerable<ListTicketsRequest> Requests() =>
        _tickets.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(ITicketsClient.ListAsync))
            .Select(call => (ListTicketsRequest)call.GetArguments()[0]!);

    [Fact]
    public void The_root_route_shows_the_Unassigned_view_first_page_of_25()
    {
        _navigation.NavigateTo("/");
        Render<TicketQueuePage>();

        var request = Requests().Single();
        request.View.ShouldBe(TicketViews.Unassigned);
        request.Page.ShouldBe(1);
        request.PageSize.ShouldBe(25);
        request.Search.ShouldBeNull();
    }

    [Theory]
    [InlineData("unassigned", TicketViews.Unassigned)]
    [InlineData("mine", TicketViews.Mine)]
    [InlineData("open", TicketViews.Open)]
    [InlineData("pending", TicketViews.Pending)]
    [InlineData("all", TicketViews.All)]
    [InlineData("spam", TicketViews.Spam)]
    public void Each_view_asks_the_API_for_exactly_that_view_and_only_the_Spam_tab_asks_for_spam(string slug, string expected)
    {
        RenderQueue(slug);

        Requests().Select(r => r.View).ShouldBe([expected]);
        if (expected != TicketViews.Spam)
        {
            Requests().ShouldNotContain(r => r.View == TicketViews.Spam);
        }
    }

    [Fact]
    public void The_view_in_the_route_is_case_insensitive()
    {
        RenderQueue("MINE");

        Requests().Single().View.ShouldBe(TicketViews.Mine);
    }

    [Fact]
    public void An_unknown_view_is_reported_as_not_found_and_calls_nothing()
    {
        var notFound = 0;
        _navigation.OnNotFound += (_, _) => notFound++;

        RenderQueue("everything");

        notFound.ShouldBe(1);
        Requests().ShouldBeEmpty();
    }

    [Fact]
    public void The_six_tabs_link_to_their_views_and_show_counts_with_Spam_muted()
    {
        var cut = RenderQueue("open");

        var tabs = cut.FindAll("nav.ts-tabs a");
        tabs.Select(t => t.GetAttribute("href")).ShouldBe(
            ["/queue/unassigned", "/queue/mine", "/queue/open", "/queue/pending", "/queue/all", "/queue/spam"]);
        tabs.Select(t => t.QuerySelector(".ts-count")!.TextContent).ShouldBe(["3", "2", "5", "1", "11", "4"]);
        cut.FindAll(".ts-count--muted").Count.ShouldBe(1);
        cut.Find(".ts-tab--spam .ts-count").ClassList.ShouldContain("ts-count--muted");
        cut.Find("a[aria-current=page]").TextContent.ShouldContain("Open");
    }

    [Fact]
    public void A_failed_counts_call_leaves_the_tabs_without_numbers_and_the_list_working()
    {
        _tickets.GetCountsAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketViewCountsResponse>("boom"));

        var cut = RenderQueue("open");

        cut.FindAll(".ts-count").ShouldBeEmpty();
        cut.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Fact]
    public void Tab_links_keep_the_filters_but_not_the_page()
    {
        var cut = RenderQueue("open", "?status=Pending&page=3");

        cut.FindAll("nav.ts-tabs a")[1].GetAttribute("href").ShouldBe("/queue/mine?status=Pending");
    }

    [Fact]
    public void A_row_shows_number_subject_tags_product_requester_stamp_priority_assignee_and_age()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page(
        [
            TestData.Summary("ORB-42", "Cannot log in", TicketStatuses.Pending, TicketPriorities.Urgent, assignee: "Sam Ortiz",
                tags: [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]),
        ])));

        var cut = RenderQueue("open");

        var row = cut.Find("tbody tr");
        row.QuerySelector("a")!.GetAttribute("href").ShouldBe("/tickets/ORB-42");
        row.QuerySelector(".ts-subject")!.TextContent.ShouldBe("Cannot log in");
        row.QuerySelector(".ts-tag")!.TextContent.ShouldBe("bug");
        row.QuerySelector(".ts-product")!.TextContent.ShouldBe("Orbitly");
        row.QuerySelector(".ts-col-requester")!.TextContent.ShouldBe("Ada Lovelace");
        row.QuerySelector(".ts-stamp")!.TextContent.ShouldBe("Pending");
        row.QuerySelector(".ts-stamp")!.ClassList.ShouldContain("ts-stamp--queue");
        row.QuerySelector(".ts-priority")!.TextContent.ShouldBe("Urgent");
        row.QuerySelector(".ts-avatar")!.TextContent.ShouldContain("SO");
        row.QuerySelector("time")!.TextContent.ShouldBe("5 min ago");
    }

    [Fact]
    public void A_row_without_an_assignee_or_requester_name_falls_back_and_a_spam_row_wears_the_spam_stamp()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page(
            [TestData.Summary("ORB-7", requesterName: null, isSpam: true)])));

        var cut = RenderQueue("spam");

        var row = cut.Find("tbody tr");
        row.QuerySelector(".ts-col-requester")!.TextContent.ShouldBe("ada@example.com");
        row.QuerySelector(".ts-unassigned .visually-hidden")!.TextContent.ShouldBe("Unassigned");
        row.QuerySelector(".ts-stamp")!.TextContent.ShouldBe("Spam?");
    }

    [Fact]
    public void The_table_has_column_headers_and_the_result_count_is_announced()
    {
        var cut = RenderQueue("open");

        cut.FindAll("thead th[scope=col]").Count.ShouldBe(9);
        cut.Find("p.visually-hidden[role=status]").TextContent.ShouldBe("2 of 2 tickets");
        cut.Find("h1").TextContent.ShouldBe("Queue");
    }

    [Fact]
    public void While_the_list_is_loading_the_page_shows_skeleton_rows_then_the_rows()
    {
        var gate = new TaskCompletionSource<Result<PagedResponse<TicketSummaryDto>>>();
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);

        var cut = RenderQueue("open");

        cut.FindAll(".ts-skeleton").Count.ShouldBe(8);
        cut.FindAll("tbody tr").ShouldBeEmpty();

        gate.SetResult(TestData.Ok(TestData.Page([TestData.Summary()])));

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
        cut.FindAll(".ts-skeleton").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_load_shows_an_alert_with_the_API_message_and_retry_reloads()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<PagedResponse<TicketSummaryDto>>("status-invalid", "Status must be one of New, Open."),
            TestData.Ok(TestData.Page([TestData.Summary()])));

        var cut = RenderQueue("open");

        cut.Find("[role=alert] p").TextContent.ShouldBe("Couldn't load tickets. Status must be one of New, Open.");

        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_refresh_keeps_the_rows_that_were_already_on_screen()
    {
        var cut = RenderQueue("open");
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<PagedResponse<TicketSummaryDto>>("boom", "The API is unavailable."));

        cut.FindAll("button").Single(b => b.TextContent == "Refresh").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("The API is unavailable."));
        cut.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("unassigned")]
    [InlineData("mine")]
    [InlineData("open")]
    public void A_truly_empty_Unassigned_Mine_or_Open_view_shows_the_All_caught_up_brand_moment(string slug)
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page([])));

        var cut = RenderQueue(slug);

        cut.Find(".ts-window-title").TextContent.ShouldBe("queue.exe — 0 items");
        cut.Find(".ts-window h2").TextContent.ShouldBe("All caught up");
        cut.Find(".ts-window p").TextContent.ShouldBe("Zero tickets, fully supported.");
        var link = cut.Find(".ts-window-link");
        link.TextContent.ShouldBe(slug == "open" ? "View all tickets" : "View open tickets");
        link.GetAttribute("href").ShouldBe(slug == "open" ? "/queue/all" : "/queue/open");
    }

    [Theory]
    [InlineData("pending", "No pending tickets")]
    [InlineData("all", "No tickets yet")]
    [InlineData("spam", "No spam")]
    public void The_other_empty_views_are_plain_text_without_the_brand_window(string slug, string heading)
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page([])));

        var cut = RenderQueue(slug);

        cut.Find(".ts-state-heading").TextContent.ShouldBe(heading);
        cut.FindAll(".ts-window").ShouldBeEmpty();
    }

    [Fact]
    public void A_filtered_empty_result_says_no_tickets_match_and_clearing_filters_goes_back_to_the_bare_view()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page([])));
        var cut = RenderQueue("open", "?search=zzz&status=Pending");

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No tickets match");
        cut.FindAll(".ts-window").ShouldBeEmpty();

        cut.FindAll(".ts-state button").Single().Click();

        _navigation.Uri.ShouldEndWith("/queue/open");
    }

    [Fact]
    public void The_filters_in_the_query_string_become_the_request()
    {
        var productId = Guid.NewGuid();
        var tagId = Guid.NewGuid();

        RenderQueue("pending", $"?product={productId}&status=Pending&priority=High&tag={tagId}&search=refund%20policy&page=3");

        var request = Requests().Single();
        request.View.ShouldBe(TicketViews.Pending);
        request.ProductId.ShouldBe(productId);
        request.Status.ShouldBe("Pending");
        request.Priority.ShouldBe("High");
        request.TagId.ShouldBe(tagId);
        request.Search.ShouldBe("refund policy");
        request.Page.ShouldBe(3);
        request.PageSize.ShouldBe(25);
    }

    [Fact]
    public void Typing_in_search_issues_one_call_after_the_debounce_and_replaces_the_history_entry()
    {
        var cut = RenderQueue("open");
        _tickets.ClearReceivedCalls();

        cut.Find("input[type=search]").Input("r");
        cut.Find("input[type=search]").Input("re");
        cut.Find("input[type=search]").Input("refund");
        Time.Advance(QueueDefaults.SearchDebounce - TimeSpan.FromMilliseconds(1));
        Requests().ShouldBeEmpty();

        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => Requests().Select(r => r.Search).ShouldBe(["refund"]));
        _navigation.Uri.ShouldEndWith("/queue/open?search=refund");
        ((BunitNavigationManager)_navigation).History.Last().Options.ReplaceHistoryEntry.ShouldBeTrue();
    }

    [Fact]
    public void Pressing_Enter_in_search_does_not_wait_for_the_debounce()
    {
        var cut = RenderQueue("open");
        _tickets.ClearReceivedCalls();

        cut.Find("input[type=search]").Input("refund");
        cut.Find("form[role=search]").Submit();

        cut.WaitForAssertion(() => Requests().Select(r => r.Search).ShouldBe(["refund"]));
    }

    [Fact]
    public void A_new_search_goes_back_to_page_one_and_clearing_the_text_removes_the_filter()
    {
        var cut = RenderQueue("open", "?search=old&page=4");
        _tickets.ClearReceivedCalls();

        cut.Find("input[type=search]").Input("");
        cut.Find("form[role=search]").Submit();

        cut.WaitForAssertion(() => Requests().Select(r => (r.Search, r.Page)).ShouldBe([((string?)null, 1)]));
    }

    [Fact]
    public void Choosing_a_product_status_priority_or_tag_requests_page_one_with_that_filter()
    {
        var cut = RenderQueue("all", "?page=2");
        _tickets.ClearReceivedCalls();

        cut.Find("#queue-product").Change(TestData.OrbitlyId.ToString());
        cut.WaitForAssertion(() => Requests().Last().ProductId.ShouldBe(TestData.OrbitlyId));
        Requests().Last().Page.ShouldBe(1);

        cut.Find("#queue-status").Change("Solved");
        cut.WaitForAssertion(() => Requests().Last().Status.ShouldBe("Solved"));

        cut.Find("#queue-priority").Change("Urgent");
        cut.WaitForAssertion(() => Requests().Last().Priority.ShouldBe("Urgent"));

        cut.Find("#queue-tag").Change(TestData.BugTagId.ToString());
        cut.WaitForAssertion(() => Requests().Last().TagId.ShouldBe(TestData.BugTagId));

        var last = Requests().Last();
        (last.ProductId, last.Status, last.Priority, last.Page).ShouldBe((TestData.OrbitlyId, "Solved", "Urgent", 1));
    }

    [Fact]
    public void Changing_the_page_keeps_every_filter()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.Page([TestData.Summary()], page: 1, total: 60)));
        var cut = RenderQueue("open", "?status=Pending&search=refund");
        _tickets.ClearReceivedCalls();

        cut.FindAll(".ts-pager button").Single(b => b.TextContent == "Next").Click();

        cut.WaitForAssertion(() => Requests().Select(r => (r.Status, r.Search, r.Page)).ShouldBe([("Pending", "refund", 2)]));
        _navigation.Uri.ShouldEndWith("/queue/open?status=Pending&search=refund&page=2");
    }

    [Fact]
    public async Task J_and_k_move_the_selection_without_reordering_and_enter_opens_the_selected_ticket()
    {
        var cut = RenderQueue("open");
        cut.FindAll("tr[aria-current]").ShouldBeEmpty();

        await PressAsync("j");
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-1");
        await PressAsync("ArrowDown");
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-2");
        await PressAsync("j");
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-2");
        await PressAsync("k");
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-1");
        cut.FindAll("tbody tr").Select(r => r.GetAttribute("data-ticket")).ShouldBe(["ORB-1", "ORB-2"]);

        await PressAsync("Enter");

        _navigation.Uri.ShouldEndWith("/tickets/ORB-1");
        _queueModule.Invocations["scrollSelectedIntoView"].Count.ShouldBe(4);
    }

    [Fact]
    public async Task Enter_with_nothing_selected_and_keys_pressed_while_typing_do_nothing()
    {
        var cut = RenderQueue("open");
        var before = _navigation.Uri;

        await PressAsync("Enter");
        await PressAsync("j", typing: true);

        _navigation.Uri.ShouldBe(before);
        cut.FindAll("tr[aria-current]").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_slash_key_focuses_the_search_box()
    {
        var cut = RenderQueue("open");

        await PressAsync("/");

        JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(cut.Find("input[type=search]"));
    }

    [Fact]
    public async Task A_disposed_page_no_longer_answers_shortcuts()
    {
        var cut = RenderQueue("open");
        await cut.Instance.DisposeAsync();

        await PressAsync("j");

        cut.FindAll("tr[aria-current]").ShouldBeEmpty();
    }

    [Fact]
    public void Every_client_call_receives_the_page_s_cancellation_token()
    {
        RenderQueue("open");

        var tokens = _tickets.ReceivedCalls().Concat(_products.ReceivedCalls()).Concat(_tags.ReceivedCalls())
            .Select(call => call.GetArguments().Last()).OfType<CancellationToken>().ToList();
        tokens.Count.ShouldBe(4);
        tokens.ShouldAllBe(token => token.CanBeCanceled);
    }
}
```

`tests/TechStrap.Admin.Tests/ScriptHostTests.cs`: serve the third module too:

```diff
     [InlineData("/js/dialog.js")]
     [InlineData("/js/shortcuts.js")]
+    [InlineData("/js/queue.js")]
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "TicketQueuePageTests|ScriptHostTests"`
Expected: a build failure (`TicketQueuePage`, `QueueFilter`, `QueueDefaults` do not exist).

- [ ] **Step 3: Implement**

1. Delete the placeholder: `git rm src/TechStrap.Admin/Components/Pages/Home.razor`. The `.shell-placeholder` rule at the foot of `Styles/app.scss` becomes dead; leave it for the 07c style pass.
2. Razor imports do not cross from `Components/` into `Features/`, so the feature folder gets its own (every later task relies on it):

`src/TechStrap.Admin/Features/_Imports.razor`

```razor
@using Microsoft.AspNetCore.Authorization
@using Microsoft.AspNetCore.Components.Authorization
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.JSInterop
@using TechStrap.Admin.Auth
@using TechStrap.Admin.Clients
@using TechStrap.Admin.Components.Ui
@using TechStrap.Admin.Features.Shell
@using TechStrap.Contracts.Tickets
```

3. The model, the filter and the copy:

`src/TechStrap.Admin/Features/Queue/QueueModel.cs`

```csharp
using Microsoft.AspNetCore.WebUtilities;
using TechStrap.Admin.Components.Ui;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Queue;

/// <summary>The queue's named constants (PHASE-07 boundary validation: page size and debounce are never literals at a call site).</summary>
public static class QueueDefaults
{
    /// <summary>Tickets per page. The API's own default is 25 as well; the queue states it so a server change cannot move it.</summary>
    public const int PageSize = 25;

    /// <summary>How long the search box waits after the last keystroke before it asks the API.</summary>
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>The view shown at <c>/</c> and at <c>/queue</c> (UX-BRIEF-admin). Not the API's <c>TicketViews.Default</c>, which is All.</summary>
    public const string View = TicketViews.Unassigned;
}

/// <summary>The query-string keys of the queue. Every filter lives in the URL so views are linkable and survive a refresh.</summary>
public static class QueueQueryKeys
{
    public const string Product = "product";
    public const string Status = "status";
    public const string Priority = "priority";
    public const string Tag = "tag";
    public const string Search = "search";
    public const string Page = "page";
}

/// <summary>Everything that decides which tickets the queue shows. Record equality is how the page knows a parameter change needs a reload.</summary>
public sealed record QueueFilter(string View, Guid? ProductId, string? Status, string? Priority, Guid? TagId, string? Search, int Page)
{
    public bool HasFilters => ProductId is not null || Status is not null || Priority is not null || TagId is not null || !string.IsNullOrWhiteSpace(Search);

    /// <summary>The API request. Assignee and requester filters are not part of the queue UI (requester history is a later phase).</summary>
    public ListTicketsRequest ToRequest() =>
        new(View, ProductId, Status, Priority, AssigneeId: null, TagId, RequesterId: null, Search, Page, QueueDefaults.PageSize);

    /// <summary>The same view with no filters, no search and the first page.</summary>
    public QueueFilter Cleared() => new(View, null, null, null, null, null, 1);
}

public static class QueueViews
{
    /// <summary>The six views, in rail order. Spam is separate by design: the five before it never include it (D-024).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        TicketViews.Unassigned, TicketViews.Mine, TicketViews.Open, TicketViews.Pending, TicketViews.All, TicketViews.Spam,
    ];

    /// <summary>The canonical view for a route segment (case-insensitive); the default view for none; null for an unknown segment.</summary>
    public static string? Parse(string? segment) =>
        string.IsNullOrEmpty(segment)
            ? QueueDefaults.View
            : All.FirstOrDefault(view => view.Equals(segment, StringComparison.OrdinalIgnoreCase));

    public static string Slug(string view) => view.ToLowerInvariant();

    public static int CountOf(TicketViewCountsResponse counts, string view) => view switch
    {
        TicketViews.Unassigned => counts.Unassigned,
        TicketViews.Mine => counts.Mine,
        TicketViews.Open => counts.Open,
        TicketViews.Pending => counts.Pending,
        TicketViews.All => counts.All,
        TicketViews.Spam => counts.Spam,
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, "Unknown queue view."),
    };
}

public static class QueueLinks
{
    /// <summary>The URL of a filter: <c>/queue/mine?product=...&amp;search=...&amp;page=2</c>. Page 1 and empty values are left out.</summary>
    public static string Uri(QueueFilter filter)
    {
        var query = new Dictionary<string, string?>
        {
            [QueueQueryKeys.Product] = filter.ProductId?.ToString(),
            [QueueQueryKeys.Status] = filter.Status,
            [QueueQueryKeys.Priority] = filter.Priority,
            [QueueQueryKeys.Tag] = filter.TagId?.ToString(),
            [QueueQueryKeys.Search] = string.IsNullOrWhiteSpace(filter.Search) ? null : filter.Search,
            [QueueQueryKeys.Page] = filter.Page > 1 ? filter.Page.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
        };
        return QueryHelpers.AddQueryString($"/queue/{QueueViews.Slug(filter.View)}", query.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary());
    }
}

/// <summary>One queue row, ready to draw. Mapping is simple, so there is no factory (PHASE-07 component boundaries).</summary>
public sealed record TicketRowViewModel(
    Guid Id,
    string Number,
    string Href,
    string Subject,
    IReadOnlyList<TicketTagDto> Tags,
    string ProductName,
    string Requester,
    StampStatus Stamp,
    PriorityLevel Priority,
    string? AssigneeName,
    string AssigneeInitials,
    DateTimeOffset LastActivity)
{
    public static TicketRowViewModel From(TicketSummaryDto ticket) => new(
        ticket.Id,
        ticket.Number,
        $"/tickets/{Uri.EscapeDataString(ticket.Number)}",
        ticket.Subject,
        ticket.Tags,
        ticket.ProductName,
        string.IsNullOrWhiteSpace(ticket.RequesterName) ? ticket.RequesterEmail : ticket.RequesterName,
        TicketDisplay.Stamp(ticket.Status, ticket.IsSpam),
        TicketDisplay.Priority(ticket.Priority),
        ticket.AssigneeName,
        TicketDisplay.Initials(ticket.AssigneeName),
        ticket.LastActivityAt);
}

/// <summary>The queue's copy. Plain text; the one brand moment (All caught up) takes its words from <see cref="CaughtUpCopy"/>.</summary>
public static class QueueCopy
{
    public const string Heading = "Queue";
    public const string LoadFailed = "Couldn't load tickets.";
    public const string Refresh = "Refresh";
    public const string ClearFilters = "Clear filters";
    public const string NoMatchHeading = "No tickets match";
    public const string NoPendingHeading = "No pending tickets";
    public const string NoTicketsHeading = "No tickets yet";
    public const string NoSpamHeading = "No spam";
    public const string SearchLabel = "Search tickets";
    public const string AllProducts = "All products";
    public const string AllStatuses = "Any status";
    public const string AllPriorities = "Any priority";
    public const string AllTags = "Any tag";
}

/// <summary>The "All caught up" brand moment (UX-BRIEF-admin, Brand moments): a truly empty Unassigned, Mine or Open view with no filters.</summary>
public static class CaughtUpCopy
{
    public const string Title = "queue.exe — 0 items";
    public const string Heading = "All caught up";
    public const string Body = "Zero tickets, fully supported.";
    public const string OpenTicketsLink = "View open tickets";
    public const string AllTicketsLink = "View all tickets";
}
```

4. The components:

`src/TechStrap.Admin/Features/Queue/TicketRow.razor`

```razor
<tr class="@(Selected ? "ts-row ts-row--selected" : "ts-row")" aria-current="@(Selected ? "true" : null)" data-ticket="@Row.Number">
    <td class="ts-col-mark" aria-hidden="true"></td>
    <td class="ts-col-number"><a href="@Row.Href">@Row.Number</a></td>
    <td class="ts-col-subject">
        <span class="ts-subject">@Row.Subject</span>
        @foreach (var tag in Row.Tags)
        {
            <TagChip Name="@tag.Name" Colour="@tag.Colour" />
        }
    </td>
    <td class="ts-col-product"><span class="ts-product">@Row.ProductName</span></td>
    <td class="ts-col-requester">@Row.Requester</td>
    <td class="ts-col-status"><StatusStamp Status="Row.Stamp" /></td>
    <td class="ts-col-priority"><PriorityMark Level="Row.Priority" /></td>
    <td class="ts-col-assignee">
        @if (Row.AssigneeName is not null)
        {
            <span class="ts-avatar" title="@Row.AssigneeName"><span aria-hidden="true">@Row.AssigneeInitials</span><span class="visually-hidden">@Row.AssigneeName</span></span>
        }
        else
        {
            <span class="ts-unassigned"><span aria-hidden="true">&mdash;</span><span class="visually-hidden">Unassigned</span></span>
        }
    </td>
    <td class="ts-col-activity"><RelativeTime When="Row.LastActivity" /></td>
</tr>
```

`src/TechStrap.Admin/Features/Queue/TicketRow.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Features.Queue;

/// <summary>
/// One ledger row: marker, number, subject with tag chips, product, requester, status stamp (straight), priority, assignee and last activity.
/// Parameters only.
/// </summary>
public sealed partial class TicketRow
{
    [Parameter, EditorRequired]
    public TicketRowViewModel Row { get; set; } = default!;

    /// <summary>The keyboard selection (<c>j</c>/<c>k</c>); it never reorders rows.</summary>
    [Parameter]
    public bool Selected { get; set; }
}
```

`src/TechStrap.Admin/Features/Queue/QueueViewTabs.razor`

```razor
<nav class="ts-tabs" aria-label="Queue views">
    @foreach (var view in QueueViews.All)
    {
        var isSpam = view == TicketViews.Spam;
        <a class="@(isSpam ? "ts-tab ts-tab--spam" : "ts-tab")" href="@HrefFor(view)" aria-current="@(view == ActiveView ? "page" : null)">
            @view
            @if (Counts is not null)
            {
                <span class="@(isSpam ? "ts-count ts-count--muted" : "ts-count")">@QueueViews.CountOf(Counts, view)</span>
            }
        </a>
    }
</nav>

@code {
    /// <summary>The canonical view name that is on screen.</summary>
    [Parameter, EditorRequired]
    public string ActiveView { get; set; } = QueueDefaults.View;

    /// <summary>The per-view counts; null until they arrive or when the counts call failed (the tabs then show no numbers).</summary>
    [Parameter]
    public TicketViewCountsResponse? Counts { get; set; }

    /// <summary>The link for a tab. Tabs are real links, so a view can be opened in a new tab or bookmarked.</summary>
    [Parameter, EditorRequired]
    public Func<string, string> HrefFor { get; set; } = _ => "#";
}
```

`src/TechStrap.Admin/Features/Queue/QueueFilterBar.razor`

```razor
<form class="ts-filterbar" role="search" @onsubmit="SubmitSearchAsync" @onsubmit:preventDefault>
    <button type="submit" hidden>Search</button>
    <div class="ts-filter ts-filter--search">
        <label for="queue-search">@QueueCopy.SearchLabel</label>
        <input id="queue-search" @ref="_search" type="search" class="form-control" autocomplete="off" spellcheck="false"
               value="@_text" @oninput="OnSearchInput" />
    </div>
    <div class="ts-filter">
        <label for="queue-product">Product</label>
        <select id="queue-product" class="form-select" @onchange="OnProductChangedAsync">
            <option value="" selected="@(Filter.ProductId is null)">@QueueCopy.AllProducts</option>
            @foreach (var product in Products)
            {
                <option value="@product.Id" selected="@(Filter.ProductId == product.Id)">@product.Name</option>
            }
        </select>
    </div>
    <div class="ts-filter">
        <label for="queue-status">Status</label>
        <select id="queue-status" class="form-select" @onchange="OnStatusChangedAsync">
            <option value="" selected="@(Filter.Status is null)">@QueueCopy.AllStatuses</option>
            @foreach (var status in Statuses)
            {
                <option value="@status" selected="@(Filter.Status == status)">@status</option>
            }
        </select>
    </div>
    <div class="ts-filter">
        <label for="queue-priority">Priority</label>
        <select id="queue-priority" class="form-select" @onchange="OnPriorityChangedAsync">
            <option value="" selected="@(Filter.Priority is null)">@QueueCopy.AllPriorities</option>
            @foreach (var priority in Priorities)
            {
                <option value="@priority" selected="@(Filter.Priority == priority)">@priority</option>
            }
        </select>
    </div>
    <div class="ts-filter">
        <label for="queue-tag">Tag</label>
        <select id="queue-tag" class="form-select" @onchange="OnTagChangedAsync">
            <option value="" selected="@(Filter.TagId is null)">@QueueCopy.AllTags</option>
            @foreach (var tag in Tags)
            {
                <option value="@tag.Id" selected="@(Filter.TagId == tag.Id)">@tag.Name</option>
            }
        </select>
    </div>
    <div class="ts-filter ts-filter--actions">
        @if (Filter.HasFilters)
        {
            <button type="button" class="btn btn-outline-secondary" @onclick="ClearAsync">@QueueCopy.ClearFilters</button>
        }
        <button type="button" class="btn btn-outline-secondary" @onclick="OnRefresh">@QueueCopy.Refresh</button>
    </div>
</form>
```

`src/TechStrap.Admin/Features/Queue/QueueFilterBar.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Queue;

/// <summary>
/// Search and the four filters. The search box waits <see cref="QueueDefaults.SearchDebounce"/> after the last keystroke (Enter commits at once);
/// a select commits immediately. Every commit reports a new <see cref="QueueFilter"/> on page 1 and the owner navigates, so the URL stays the single source of truth.
/// </summary>
public sealed partial class QueueFilterBar : IDisposable
{
    private static readonly string[] Statuses =
        [TicketStatuses.New, TicketStatuses.Open, TicketStatuses.Pending, TicketStatuses.Solved, TicketStatuses.Closed];

    private static readonly string[] Priorities =
        [TicketPriorities.Urgent, TicketPriorities.High, TicketPriorities.Normal, TicketPriorities.Low];

    private ElementReference _search;
    private ITimer? _timer;
    private string _text = string.Empty;
    private string? _appliedSearch;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Parameter, EditorRequired]
    public QueueFilter Filter { get; set; } = default!;

    [Parameter]
    public IReadOnlyList<ProductDto> Products { get; set; } = [];

    [Parameter]
    public IReadOnlyList<TagDto> Tags { get; set; } = [];

    [Parameter, EditorRequired]
    public EventCallback<QueueFilter> OnChanged { get; set; }

    [Parameter]
    public EventCallback OnRefresh { get; set; }

    /// <summary>The <c>/</c> shortcut: put the cursor in the search box.</summary>
    public ValueTask FocusSearchAsync() => _search.FocusAsync();

    protected override void OnParametersSet()
    {
        // Follow the URL (clear filters, back button) unless the user is mid-word: a pending timer means the text is newer than the filter.
        if (_timer is null && Filter.Search != _appliedSearch)
        {
            _text = Filter.Search ?? string.Empty;
        }

        _appliedSearch = Filter.Search;
    }

    private void OnSearchInput(ChangeEventArgs e)
    {
        _text = e.Value as string ?? string.Empty;
        _timer?.Dispose();
        _timer = Time.CreateTimer(_ => _ = InvokeAsync(CommitSearchAsync), null, QueueDefaults.SearchDebounce, Timeout.InfiniteTimeSpan);
    }

    private Task SubmitSearchAsync() => CommitSearchAsync();

    private async Task CommitSearchAsync()
    {
        _timer?.Dispose();
        _timer = null;
        var search = string.IsNullOrWhiteSpace(_text) ? null : _text.Trim();
        if (search != Filter.Search)
        {
            await OnChanged.InvokeAsync(Filter with { Search = search, Page = 1 });
        }
    }

    private Task OnProductChangedAsync(ChangeEventArgs e) => Commit(Filter with { ProductId = ParseGuid(e), Page = 1 });

    private Task OnStatusChangedAsync(ChangeEventArgs e) => Commit(Filter with { Status = ParseText(e), Page = 1 });

    private Task OnPriorityChangedAsync(ChangeEventArgs e) => Commit(Filter with { Priority = ParseText(e), Page = 1 });

    private Task OnTagChangedAsync(ChangeEventArgs e) => Commit(Filter with { TagId = ParseGuid(e), Page = 1 });

    private Task ClearAsync() => Commit(Filter.Cleared());

    private Task Commit(QueueFilter filter) => OnChanged.InvokeAsync(filter);

    private static Guid? ParseGuid(ChangeEventArgs e) => Guid.TryParse(e.Value as string, out var id) ? id : null;

    private static string? ParseText(ChangeEventArgs e) => e.Value is string { Length: > 0 } text ? text : null;

    public void Dispose() => _timer?.Dispose();
}
```

`src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor`

```razor
@page "/"
@page "/queue/{View?}"
@attribute [Authorize]

<PageTitle>@_filter.View &middot; Queue</PageTitle>
<div class="ts-queue">
    <h1>@QueueCopy.Heading</h1>
    <QueueViewTabs ActiveView="@_filter.View" Counts="_counts" HrefFor="TabHref" />
    <QueueFilterBar @ref="_filterBar" Filter="_filter" Products="_products" Tags="_tags" OnChanged="OnFilterChangedAsync" OnRefresh="RefreshAsync" />
    <p class="visually-hidden" role="status">@_announcement</p>

    @if (_loading && _page is null)
    {
        <LoadingState Rows="8" Label="Loading tickets" />
    }
    else
    {
        @if (_error is not null)
        {
            <ErrorState Message="@_error" OnRetry="RetryAsync" />
        }

        @if (_rows.Count > 0)
        {
            <div class="table-responsive">
                <table class="table ts-ledger" data-shortcut-scope="queue" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
                            <th scope="col"><span class="visually-hidden">Selection</span></th>
                            <th scope="col">Ticket</th>
                            <th scope="col">Subject</th>
                            <th scope="col">Product</th>
                            <th scope="col">Requester</th>
                            <th scope="col">Status</th>
                            <th scope="col">Priority</th>
                            <th scope="col">Assignee</th>
                            <th scope="col">Last activity</th>
                        </tr>
                    </thead>
                    <tbody>
                        @for (var i = 0; i < _rows.Count; i++)
                        {
                            <TicketRow @key="_rows[i].Id" Row="_rows[i]" Selected="@(i == _selected)" />
                        }
                    </tbody>
                </table>
            </div>
            <PagerControl Page="_filter.Page" PageSize="QueueDefaults.PageSize" TotalCount="_page!.TotalCount" OnPageChanged="OnPageChangedAsync" />
        }
        else if (_page is not null && _error is null)
        {
            @if (_filter.HasFilters)
            {
                <EmptyState Heading="@QueueCopy.NoMatchHeading">
                    <button type="button" class="btn btn-outline-secondary" @onclick="ClearFiltersAsync">@QueueCopy.ClearFilters</button>
                </EmptyState>
            }
            else if (IsCaughtUpView)
            {
                <div class="ts-window-page">
                    <BrandWindow Title="@CaughtUpCopy.Title" Heading="@CaughtUpCopy.Heading">
                        <p>@CaughtUpCopy.Body</p>
                        <div class="ts-window-action">
                            <a class="ts-window-link" href="@CaughtUpLinkHref">@CaughtUpLinkText</a>
                        </div>
                    </BrandWindow>
                </div>
            }
            else
            {
                <EmptyState Heading="@PlainEmptyHeading" />
            }
        }
    }
</div>
```

`src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Tickets;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Features.Queue;

/// <summary>
/// The queue: six views, filters, search and paging, all in the URL. It loads the list and the per-view counts together, keeps the previous list on screen
/// when a refresh fails, and drives the keyboard selection (<c>j</c>/<c>k</c>, Enter, <c>/</c>). The default view is Unassigned.
/// </summary>
public sealed partial class TicketQueuePage : IAsyncDisposable
{
    private const string ModulePath = "./js/queue.js";

    private QueueFilter _filter = new(QueueDefaults.View, null, null, null, null, null, 1);
    private QueueFilter? _loadedFor;
    private QueueFilterBar? _filterBar;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _cts;
    private IJSObjectReference? _module;
    private PagedResponse<TicketSummaryDto>? _page;
    private IReadOnlyList<TicketRowViewModel> _rows = [];
    private TicketViewCountsResponse? _counts;
    private IReadOnlyList<ProductDto> _products = [];
    private IReadOnlyList<TagDto> _tags = [];
    private string? _error;
    private string _announcement = string.Empty;
    private bool _loading;
    private bool _lookupsLoaded;
    private bool _scrollSelected;
    private int _selected = -1;

    [Inject]
    private ITicketsClient Tickets { get; set; } = default!;

    [Inject]
    private IProductsClient ProductsClient { get; set; } = default!;

    [Inject]
    private ITagsClient TagsClient { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    /// <summary>The route segment: <c>unassigned</c>, <c>mine</c>, <c>open</c>, <c>pending</c>, <c>all</c> or <c>spam</c>. Absent means the default view.</summary>
    [Parameter]
    public string? View { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Product)]
    public Guid? Product { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Status)]
    public string? Status { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Priority)]
    public string? Priority { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Tag)]
    public Guid? Tag { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Search)]
    public string? Search { get; set; }

    [SupplyParameterFromQuery(Name = QueueQueryKeys.Page)]
    public int? PageNumber { get; set; }

    private bool IsCaughtUpView => _filter.View is TicketViews.Unassigned or TicketViews.Mine or TicketViews.Open;

    private string CaughtUpLinkHref => _filter.View == TicketViews.Open
        ? QueueLinks.Uri(_filter with { View = TicketViews.All })
        : QueueLinks.Uri(_filter with { View = TicketViews.Open });

    private string CaughtUpLinkText => _filter.View == TicketViews.Open ? CaughtUpCopy.AllTicketsLink : CaughtUpCopy.OpenTicketsLink;

    private string PlainEmptyHeading => _filter.View switch
    {
        TicketViews.Pending => QueueCopy.NoPendingHeading,
        TicketViews.Spam => QueueCopy.NoSpamHeading,
        _ => QueueCopy.NoTicketsHeading,
    };

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    protected override async Task OnParametersSetAsync()
    {
        var view = QueueViews.Parse(View);
        if (view is null)
        {
            Navigation.NotFound();
            return;
        }

        var filter = new QueueFilter(view, Product, Blank(Status), Blank(Priority), Tag, Blank(Search), Math.Max(1, PageNumber ?? 1));
        _filter = filter;
        if (!_lookupsLoaded)
        {
            _lookupsLoaded = true;
            await LoadLookupsAsync();
        }

        if (filter != _loadedFor)
        {
            _loadedFor = filter;
            _page = null;
            _rows = [];
            _selected = -1;
            await LoadAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_scrollSelected)
        {
            return;
        }

        _scrollSelected = false;
        _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        await _module.InvokeVoidAsync("scrollSelectedIntoView");
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task LoadLookupsAsync()
    {
        var products = ProductsClient.ListAsync(_lifetime.Token);
        var tags = TagsClient.ListAsync(_lifetime.Token);
        await Task.WhenAll(products, tags);

        // A failed lookup only means that filter has no options; the queue itself still works.
        _products = products.Result.IsSuccess ? products.Result.Value : [];
        _tags = tags.Result.IsSuccess ? tags.Result.Value : [];
    }

    private async Task LoadAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        var cts = _cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var filter = _filter;
        _loading = true;
        _error = null;

        var list = Tickets.ListAsync(filter.ToRequest(), cts.Token);
        var counts = Tickets.GetCountsAsync(cts.Token);
        try
        {
            await Task.WhenAll(list, counts);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        _loading = false;
        if (counts.Result.IsSuccess)
        {
            _counts = counts.Result.Value;
        }

        if (list.Result.IsSuccess)
        {
            _page = list.Result.Value;
            _rows = _page.Items.Select(TicketRowViewModel.From).ToList();
            _selected = Math.Min(_selected, _rows.Count - 1);
            _announcement = $"{_rows.Count} of {_page.TotalCount} tickets";
        }
        else
        {
            // Keep whatever list is already on screen; the alert explains and offers a retry.
            _error = $"{QueueCopy.LoadFailed} {list.Result.Errors[0].Message}";
        }
    }

    private Task RefreshAsync() => LoadAsync();

    private Task RetryAsync() => LoadAsync();

    private string TabHref(string view) => QueueLinks.Uri(_filter with { View = view, Page = 1 });

    private Task OnFilterChangedAsync(QueueFilter filter)
    {
        Navigation.NavigateTo(QueueLinks.Uri(filter), new NavigationOptions { ReplaceHistoryEntry = true });
        return Task.CompletedTask;
    }

    private Task OnPageChangedAsync(int page)
    {
        Navigation.NavigateTo(QueueLinks.Uri(_filter with { Page = page }));
        return Task.CompletedTask;
    }

    private Task ClearFiltersAsync() => OnFilterChangedAsync(_filter.Cleared());

    private async Task OnShortcutAsync(ShortcutAction action)
    {
        await InvokeAsync(async () =>
        {
            switch (action)
            {
                case ShortcutAction.MoveDown:
                    Select(_selected + 1);
                    break;
                case ShortcutAction.MoveUp:
                    Select(_selected - 1);
                    break;
                case ShortcutAction.OpenSelected when _selected >= 0 && _selected < _rows.Count:
                    Navigation.NavigateTo(_rows[_selected].Href);
                    break;
                case ShortcutAction.FocusSearch when _filterBar is not null:
                    await _filterBar.FocusSearchAsync();
                    break;
            }

            StateHasChanged();
        });
    }

    private void Select(int index)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        _selected = Math.Clamp(index, 0, _rows.Count - 1);
        _scrollSelected = true;
    }

    public async ValueTask DisposeAsync()
    {
        Shortcuts.Pressed -= OnShortcutAsync;
        _lifetime.Cancel();
        _cts?.Dispose();
        _lifetime.Dispose();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is gone; nothing left to release.
            }
        }
    }
}
```

5. The scroll module and the styles (add `@import "queue";` to `app.scss` after `@import "statusbar";`):

`src/TechStrap.Admin/wwwroot/js/queue.js`

```javascript
// Keeps the keyboard-selected queue row on screen (j / k). Rows never reorder; only the scroll position moves.

export function scrollSelectedIntoView() {
    const row = document.querySelector('tr[aria-current="true"]');
    if (row) {
        row.scrollIntoView({ block: 'nearest' });
    }
}
```

`src/TechStrap.Admin/Styles/_queue.scss`

```scss
// The queue (PHASE-07a): view tabs, filter bar and the ruled ledger. Compact density; the selection marker and the status stamp
// carry the state in text and shape, not colour alone.

.ts-queue {
  padding: 12px 16px;

  h1 {
    margin: 0 0 8px;
    font: 600 1.125rem var(--ts-font-mono);
  }
}

.ts-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 2px;
  margin-bottom: 8px;
  border-bottom: 2px solid var(--rule-strong);
}

.ts-tab {
  display: inline-flex;
  gap: 6px;
  align-items: baseline;
  padding: 6px 12px;
  font: 600 .75rem var(--ts-font-mono);
  letter-spacing: .04em;
  text-transform: uppercase;
  color: var(--ink);
  text-decoration: none;
  border-bottom: 3px solid transparent;

  &:hover {
    background: var(--hover);
  }

  &[aria-current="page"] {
    background: var(--sel);
    border-bottom-color: var(--margin);
  }
}

// The Spam count is muted, never an alert (D-024).
.ts-count {
  font-weight: 500;
  color: var(--ink-2);
}

.ts-count--muted {
  color: var(--ink-3);
}

.ts-filterbar {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 12px;
  align-items: end;
  margin-bottom: 8px;
}

.ts-filter {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;

  label {
    font: 500 .6875rem var(--ts-font-mono);
    letter-spacing: .04em;
    text-transform: uppercase;
    color: var(--ink-2);
  }
}

.ts-filter--search {
  flex: 1 1 220px;
}

.ts-filter--actions {
  flex-direction: row;
  gap: 8px;
}

// The ruled ledger: square rows on ruled lines with a margin rule on the left.
.ts-ledger {
  --bs-table-bg: transparent;

  width: 100%;
  margin: 0;
  font-size: .8125rem;

  th {
    font: 600 .6875rem var(--ts-font-mono);
    letter-spacing: .05em;
    text-transform: uppercase;
    color: var(--ink-2);
    white-space: nowrap;
  }

  td {
    padding: 4px 8px;
    vertical-align: middle;
    border-bottom: 1px solid var(--rule);
  }
}

.ts-row {
  &:hover {
    background: var(--hover);
  }
}

.ts-row--selected {
  background: var(--sel);

  .ts-col-mark {
    box-shadow: inset 3px 0 0 var(--margin);
  }

  .ts-col-mark::before {
    content: "\25B6";
    font-size: .625rem;
  }
}

.ts-col-mark {
  width: 22px;
  padding-left: 6px;
  color: var(--ink);
}

.ts-col-number a {
  font: 600 .75rem var(--ts-font-mono);
  white-space: nowrap;
}

.ts-subject {
  margin-right: 6px;
  font-weight: 500;
}

.ts-avatar {
  display: inline-block;
  min-width: 24px;
  padding: 1px 4px;
  font: 600 .6875rem/1.5 var(--ts-font-mono);
  text-align: center;
  background: var(--head);
  border: 1px solid var(--rule-strong);
}

.ts-unassigned {
  color: var(--ink-3);
}

.ts-col-activity {
  white-space: nowrap;
  color: var(--ink-2);
}

// Narrow screens drop Product, Requester and Priority first (UX open question 2 proposes this order).
@media (max-width: 820px) {
  .ts-ledger {
    .ts-col-product,
    .ts-col-requester,
    .ts-col-priority,
    th:nth-child(4),
    th:nth-child(5),
    th:nth-child(7) {
      display: none;
    }
  }
}
```

- [ ] **Step 4: Fix the host tests that read the old placeholder, then run everything**

`/` is now the queue, so any host test that asserted the placeholder breaks. Find them:

```bash
grep -rn "shell-placeholder\|placeholder shell\|<h1>TechStrap Admin" tests/
```

Task 4's `AgentAccessHostTests` asserts `html.ShouldContain("shell-placeholder")` and `ShouldNotContain("shell-placeholder")`: change both to `"class=\"ts-queue\""`. The stub API answers an unconfigured route with 404, so the queue renders its error alert, which is enough for these tests (Task 13 configures the happy path). Anything that asserted the shell chrome (`ts-shell`, the brand link) is unaffected. Then:

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "ShellHostTests|Admin_reports_live_only|AdminHostRedactionTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git diff --cached --stat   # after git add, before committing
git add src tests
git commit -m "feat(admin): ticket queue with six views, filters, search, paging and keyboard selection" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---
### Task 9: Ticket detail, read side

**Files:**
- Create: `src/TechStrap.Admin/Features/Tickets/TicketDetailModel.cs`, `TimelineEntryFactory.cs`, `TicketDetailPresenter.cs`, `TicketCopy.cs`, `TicketsServiceCollectionExtensions.cs`
- Create: `src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor` and `.razor.cs`, `TicketHeader.razor`, `TicketTimeline.razor`, `MessageBubble.razor` and `.razor.cs`, `AttachmentList.razor`, `TicketSidePanel.razor`, `TicketFacts.razor`, `RequesterCard.razor`, `MetadataPanel.razor`, `TicketGone.razor`
- Create: `src/TechStrap.Admin/Styles/_ticket.scss`; modify `src/TechStrap.Admin/Styles/app.scss`, `src/TechStrap.Admin/Program.cs`
- Create: `tests/TechStrap.Admin.Tests/Support/TestData.Detail.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/TimelineEntryFactoryTests.cs`, `TicketDetailPresenterTests.cs`, `TicketDetailPageTests.cs`, and `tests/TechStrap.Admin.Tests/MarkupStringSiteTests.cs`

**Interfaces:**
- Consumes: `ITicketsClient.GetAsync(string reference, ct)` returning `Result<TicketDetailDto>`; `IProductsClient.ListAsync`, `IAgentsClient.ListAllAsync` (all pages), `ITagsClient.ListAsync`; `ErrorState`, `LoadingState`, `RelativeTime`, `StatusStamp`, `PriorityMark`, `TagChip`, `TintedEntry`, `TintLegend`, `TicketDisplay`, `ShortcutService` (Tasks 7 and 8); Contracts `TicketDetailDto`, `TicketStateDto`, `MessageDto`, `TicketEventDto`, `TicketEventTypes`, `MessageAuthorTypes`, `MessageVisibilities`.
- Produces:

```csharp
// TechStrap.Admin.Features.Tickets
public sealed record TicketLookups(IReadOnlyList<ProductDto> Products, IReadOnlyList<AgentListItemDto> Agents, IReadOnlyList<TagDto> Tags);
public sealed record TicketDetailViewModel(...)    // IsClosed, Stamp, Caller, WithState(TicketStateDto)
public sealed record TimelineEntryViewModel(...); public sealed record MessageViewModel(...); public sealed record MetadataViewModel(...)
public static class TimelineEntryFactory { static IReadOnlyList<TimelineEntryViewModel> Build(messages, events, lookups); }
public sealed class TicketDetailPresenter(ITicketsClient, IProductsClient, IAgentsClient, ITagsClient) { Task<Result<TicketDetailViewModel>> LoadAsync(string reference, CancellationToken); }
public sealed partial class TicketDetailPage       // @page "/tickets/{Number}", [Authorize]; internal RefreshAsync() and ApplyState(TicketStateDto)
public static IServiceCollection AddTicketFeatures(this IServiceCollection services);   // scoped TicketDetailPresenter
```

**Rules:**
1. **The presenter exists for assembly, not mapping** (PHASE-07 allows exactly `TicketDetailPresenter`, `TimelineEntryFactory` and, later, `AdminEventSummaryFactory`). It loads the ticket and the three lookups in parallel, then, for a follow-up, makes **one** extra read of the parent to show "Follow-up to ORB-7" (the API returns only `ParentTicketId`). A parent that is gone shows no link and never fails the load. The ticket's own failure wins over a lookup failure, so a missing ticket is reported as missing; any failed lookup fails the load with that error (the sidebar needs the lists).
2. **Events carry ids only**, so product, agent and tag names come from the lookups, with plain fallbacks ("another agent", "a deleted tag") for an id the lookups lack (agents see only active agents and active products). A guid never reaches the screen.
3. **One chronological stream.** `MessageAdded` events are folded into their message by `messageId`; an event whose message is not in the list stays as a generic line. Order is oldest first; at the same instant `Created` comes first, then messages, then changes. All ten `TicketEventTypes` constants have a sentence, an unknown type becomes `Event: {type}`, and a payload that is not valid JSON never throws. The coverage test reflects over the constants, so a new type without a sample fails the build of the tests.
4. **The only HTML the Admin renders from the API is `MessageBubble`'s `MarkupString`** of `MessageDto.BodyHtml` (sanitised on the server). Every other string is encoded (subject, names, filenames, metadata). `MarkupStringSiteTests` fails if a second file uses the type.
5. **Attachments are downloads**, not pages: links to `/attachments/{id}` (Task 6's pass-through) carry `download` and `data-enhance-nav="false"` so Blazor's link interception does not route them. UNVERIFIED in a browser (bUnit cannot): click one and confirm the browser downloads the file and the app does not show Not found.
6. **Metadata** is labelled **Untrusted** (the word, a dashed border, a help sentence) unless `MetadataTrusted`; unreadable JSON says so instead of vanishing.
7. **Closed is read-only.** A Closed ticket shows the UX sentence "Closed tickets are read-only; a customer reply starts a follow-up", no composer and no controls (the read-only `TicketFacts` shows status, assignee, priority, product and tags instead). Closed hides the controls rather than disabling them (the spec's T12 wording), and still says why.
8. **State flow.** The page owns the model and `RowVersion`. A write replaces the status fields from the returned `TicketStateDto` through `WithState`, then refreshes silently: the model on screen stays while the refresh runs, a failed refresh keeps it with an inline alert, and a 404 turns into "This ticket no longer exists" with a link back (first-load 404 says "Ticket not found").
9. **Not in 07a (recorded in D-040):** the follow-up child list (no API field), the requester's ticket count and first-seen date (no API), the local-zone times, the presence hint and live updates (PHASE-10).

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/Support/TestData.Detail.cs`

```csharp
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static readonly Guid SamAgentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdaAgentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid RequesterId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    public static readonly Guid TicketId = Guid.Parse("dddddddd-0000-0000-0000-000000000042");

    public static AgentListItemDto Agent(string name, Guid id) => new(id, name, name, null, null, null, null);

    public static MessageDto Message(
        string authorType = MessageAuthorTypes.Requester,
        string visibility = MessageVisibilities.Public,
        string bodyHtml = "<p>Hello</p>",
        DateTimeOffset? at = null,
        string? authorName = "Ada Lovelace",
        Guid? id = null,
        IReadOnlyList<AttachmentDto>? attachments = null,
        IReadOnlyList<LinkedArticleDto>? articles = null) => new(
            id ?? Guid.NewGuid(), authorType, null, authorName, visibility, bodyHtml, at ?? Now.AddHours(-3), attachments ?? [], articles ?? []);

    public static TicketEventDto Event(string type, string payload = "{}", DateTimeOffset? at = null, string actorType = "Agent", string? actorName = "Sam Ortiz") =>
        new(Guid.NewGuid(), type, actorType, null, actorName, payload, at ?? Now.AddHours(-2));

    public static TicketDetailDto Detail(
        string number = "ORB-42",
        string status = TicketStatuses.Open,
        string priority = TicketPriorities.Normal,
        bool isSpam = false,
        Guid? assigneeId = null,
        string? assigneeName = null,
        IReadOnlyList<TicketTagDto>? tags = null,
        IReadOnlyList<MessageDto>? messages = null,
        IReadOnlyList<TicketEventDto>? events = null,
        Guid? parentId = null,
        string? metadataJson = null,
        bool metadataTrusted = false,
        uint rowVersion = 7,
        string subject = "Cannot log in") => new(
            TicketId, number, subject, status, priority, OrbitlyId, "Orbitly",
            new TicketRequesterDto(RequesterId, "ada@example.com", "Ada Lovelace", null),
            assigneeId, assigneeName, isSpam, tags ?? [], "Email", parentId, metadataJson, metadataTrusted,
            Now.AddDays(-1), null, null, null, Now.AddMinutes(-5), rowVersion,
            messages ?? [Message()], events ?? []);

    public static TicketStateDto State(
        string status = TicketStatuses.Open,
        string priority = TicketPriorities.Normal,
        Guid? assigneeId = null,
        Guid? productId = null,
        bool isSpam = false,
        IReadOnlyList<Guid>? tagIds = null,
        uint rowVersion = 8) => new(TicketId, "ORB-42", status, priority, productId ?? OrbitlyId, assigneeId, isSpam, tagIds ?? [], Now, rowVersion);
}
```

`tests/TechStrap.Admin.Tests/Components/TimelineEntryFactoryTests.cs`

```csharp
using System.Reflection;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class TimelineEntryFactoryTests
{
    private static readonly Guid OtherProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid MessageId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private static readonly TicketLookups Lookups = new(
        [TestData.Product("Orbitly"), TestData.Product("Nimbus", OtherProductId)],
        [TestData.Agent("Sam Ortiz", TestData.SamAgentId), TestData.Agent("Ada Admin", TestData.AdaAgentId)],
        [TestData.Tag("bug")]);

    /// <summary>One plausible payload per event type, as the Domain writes them (Ticket.cs). A new constant without a sample fails the coverage test below.</summary>
    private static readonly Dictionary<string, (string Payload, string Expected)> Samples = new()
    {
        [TicketEventTypes.Created] = ("""{"number":"ORB-42","channel":"Email"}""", "Ticket opened via Email"),
        [TicketEventTypes.MessageAdded] = ($$"""{"messageId":"{{Guid.NewGuid()}}","visibility":"Public"}""", "A message was added"),
        [TicketEventTypes.StatusChanged] = ("""{"from":"Open","to":"Pending"}""", "Status changed from Open to Pending"),
        [TicketEventTypes.Assigned] = ($$"""{"from":null,"to":"{{TestData.SamAgentId}}"}""", "Assigned to Sam Ortiz"),
        [TicketEventTypes.ProductChanged] = ($$"""{"from":"{{TestData.OrbitlyId}}","to":"{{OtherProductId}}"}""", "Product changed from Orbitly to Nimbus"),
        [TicketEventTypes.PriorityChanged] = ("""{"from":"Normal","to":"Urgent"}""", "Priority changed from Normal to Urgent"),
        [TicketEventTypes.TagAdded] = ($$"""{"tagId":"{{TestData.BugTagId}}"}""", "Tag bug added"),
        [TicketEventTypes.TagRemoved] = ($$"""{"tagId":"{{TestData.BugTagId}}"}""", "Tag bug removed"),
        [TicketEventTypes.MarkedSpam] = ("""{"isSpam":true}""", "Marked as spam"),
        [TicketEventTypes.FollowUpCreated] = ("""{"followUpTicketId":"ffffffff-0000-0000-0000-000000000001"}""", "The customer replied after the ticket closed, so a follow-up ticket was opened"),
    };

    private static IReadOnlyList<string> EveryEventTypeConstant() =>
        typeof(TicketEventTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

    public static TheoryData<string> EventTypes()
    {
        var data = new TheoryData<string>();
        foreach (var type in EveryEventTypeConstant())
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void Every_event_type_constant_has_a_sample_here_so_a_new_type_cannot_slip_past_the_timeline()
    {
        EveryEventTypeConstant().ShouldBe(Samples.Keys, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(EventTypes))]
    public void Every_event_type_becomes_one_readable_entry_and_never_throws(string type)
    {
        var (payload, expected) = Samples[type];

        var entries = TimelineEntryFactory.Build([], [TestData.Event(type, payload)], Lookups);

        var entry = entries.ShouldHaveSingleItem();
        entry.Kind.ShouldBe(TimelineEntryKind.Event);
        entry.Text.ShouldBe(expected);
        entry.Actor.ShouldBe("Sam Ortiz");
    }

    [Theory]
    [MemberData(nameof(EventTypes))]
    public void An_unreadable_payload_still_gives_a_line_for_every_type(string type)
    {
        foreach (var payload in new[] { "", "not json", "[1,2]", "null", "{}", """{"from":5,"to":{}}""" })
        {
            var entries = TimelineEntryFactory.Build([], [TestData.Event(type, payload)], Lookups);

            entries.ShouldHaveSingleItem().Text.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void An_event_type_this_build_does_not_know_is_a_generic_line()
    {
        var entries = TimelineEntryFactory.Build([], [TestData.Event("SomethingNew", "{}")], Lookups);

        entries.ShouldHaveSingleItem().Text.ShouldBe("Event: SomethingNew");
    }

    [Fact]
    public void A_MessageAdded_event_is_folded_into_its_message_so_a_reply_appears_once()
    {
        var message = TestData.Message(MessageAuthorTypes.Agent, id: MessageId, authorName: "Sam Ortiz");
        var folded = TestData.Event(TicketEventTypes.MessageAdded, $$"""{"messageId":"{{MessageId}}","visibility":"Public"}""");
        var orphan = TestData.Event(TicketEventTypes.MessageAdded, $$"""{"messageId":"{{Guid.NewGuid()}}","visibility":"Public"}""");

        var entries = TimelineEntryFactory.Build([message], [folded, orphan], Lookups);

        entries.Count(e => e.Kind == TimelineEntryKind.Message).ShouldBe(1);
        entries.Count(e => e.Text == "A message was added").ShouldBe(1);
    }

    [Theory]
    [InlineData(MessageAuthorTypes.Requester, MessageVisibilities.Public, EntryKind.Customer)]
    [InlineData(MessageAuthorTypes.Agent, MessageVisibilities.Public, EntryKind.PublicReply)]
    [InlineData(MessageAuthorTypes.Agent, MessageVisibilities.Internal, EntryKind.InternalNote)]
    [InlineData(MessageAuthorTypes.System, MessageVisibilities.Public, EntryKind.PublicReply)]
    public void A_message_gets_the_tint_of_who_wrote_it_and_who_may_see_it(string author, string visibility, EntryKind expected)
    {
        var entries = TimelineEntryFactory.Build([TestData.Message(author, visibility)], [], Lookups);

        entries.Single().Message!.Kind.ShouldBe(expected);
    }

    [Fact]
    public void Entries_are_oldest_first_with_Created_before_the_first_message_and_changes_after_it()
    {
        var t = TestData.Now.AddHours(-5);
        var created = TestData.Event(TicketEventTypes.Created, """{"channel":"Email"}""", t, "Requester", "Ada Lovelace");
        var first = TestData.Message(at: t);
        var reply = TestData.Message(MessageAuthorTypes.Agent, at: t.AddHours(1), authorName: "Sam Ortiz");
        var status = TestData.Event(TicketEventTypes.StatusChanged, """{"from":"New","to":"Pending"}""", t.AddHours(1));
        var late = TestData.Event(TicketEventTypes.PriorityChanged, """{"from":"Normal","to":"High"}""", t.AddHours(2));

        var entries = TimelineEntryFactory.Build([first, reply], [created, status, late], Lookups);

        entries.Select(e => e.Kind == TimelineEntryKind.Message ? "message:" + e.Actor : e.Text).ShouldBe(
        [
            "Ticket opened via Email",
            "message:Ada Lovelace",
            "message:Sam Ortiz",
            "Status changed from New to Pending",
            "Priority changed from Normal to High",
        ]);
    }

    [Theory]
    [InlineData(null, "ada", "Assigned to Ada Admin")]
    [InlineData("sam", null, "Unassigned (was Sam Ortiz)")]
    [InlineData("sam", "ada", "Reassigned from Sam Ortiz to Ada Admin")]
    [InlineData(null, null, "Assignment changed")]
    public void Assignments_say_who_got_it_and_who_lost_it(string? from, string? to, string expected)
    {
        static string Json(string? who) => who switch { "sam" => $"\"{TestData.SamAgentId}\"", "ada" => $"\"{TestData.AdaAgentId}\"", _ => "null" };

        var entries = TimelineEntryFactory.Build([], [TestData.Event(TicketEventTypes.Assigned, $$"""{"from":{{Json(from)}},"to":{{Json(to)}}}""")], Lookups);

        entries.Single().Text.ShouldBe(expected);
    }

    [Fact]
    public void An_id_the_lookups_do_not_know_gets_a_plain_fallback_not_a_guid()
    {
        var unknown = Guid.NewGuid();

        var entries = TimelineEntryFactory.Build([],
        [
            TestData.Event(TicketEventTypes.Assigned, $$"""{"from":null,"to":"{{unknown}}"}"""),
            TestData.Event(TicketEventTypes.TagAdded, $$"""{"tagId":"{{unknown}}"}"""),
            TestData.Event(TicketEventTypes.ProductChanged, $$"""{"from":"{{unknown}}","to":"{{TestData.OrbitlyId}}"}"""),
        ], Lookups);

        entries.Select(e => e.Text).ShouldBe(
            ["Assigned to another agent", "Tag a deleted tag added", "Product changed from another product to Orbitly"]);
        entries.ShouldNotContain(e => e.Text.Contains(unknown.ToString()));
    }

    [Fact]
    public void A_tag_removed_because_the_tag_was_deleted_says_so_and_a_restore_from_spam_is_not_called_spam()
    {
        var entries = TimelineEntryFactory.Build([],
        [
            TestData.Event(TicketEventTypes.TagRemoved, $$"""{"tagId":"{{TestData.BugTagId}}","reason":"tag-deleted"}"""),
            TestData.Event(TicketEventTypes.MarkedSpam, """{"isSpam":false}"""),
        ], Lookups);

        entries.Select(e => e.Text).ShouldBe(["Tag bug removed because the tag was deleted", "Restored from spam"]);
    }

    [Fact]
    public void A_system_actor_such_as_auto_close_is_named_as_system()
    {
        var entries = TimelineEntryFactory.Build([],
            [TestData.Event(TicketEventTypes.StatusChanged, """{"from":"Solved","to":"Closed"}""", actorType: "System", actorName: null)], Lookups);

        entries.Single().Actor.ShouldBe("System");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/TicketDetailPresenterTests.cs`

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class TicketDetailPresenterTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();
    private readonly ITagsClient _tags = Substitute.For<ITagsClient>();

    public TicketDetailPresenterTests()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail()));
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.Agent("Sam Ortiz", TestData.SamAgentId)]));
        _tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
    }

    private TicketDetailPresenter Presenter() => new(_tickets, _products, _agents, _tags);

    [Fact]
    public async Task A_ticket_detail_fixture_maps_to_the_view_model()
    {
        var tag = new TicketTagDto(TestData.BugTagId, "bug", "#DC2626");
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(
            status: TicketStatuses.Pending, priority: TicketPriorities.High, assigneeId: TestData.SamAgentId, assigneeName: "Sam Ortiz", tags: [tag],
            messages: [TestData.Message(), TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Internal, authorName: "Sam Ortiz")],
            events: [TestData.Event(TicketEventTypes.Created, """{"channel":"Email"}""")], rowVersion: 9)));

        var result = await Presenter().LoadAsync("ORB-42", Ct);

        var model = result.Value;
        (model.Number, model.Subject, model.Status, model.Priority, model.RowVersion).ShouldBe(("ORB-42", "Cannot log in", "Pending", "High", 9u));
        (model.AssigneeId, model.AssigneeName, model.ProductName, model.IsSpam, model.IsClosed).ShouldBe((TestData.SamAgentId, "Sam Ortiz", "Orbitly", false, false));
        model.Tags.ShouldBe([tag]);
        model.Caller.ShouldBe("Ada Lovelace");
        model.Stamp.ShouldBe(StampStatus.Pending);
        model.Timeline.Count.ShouldBe(3);
        model.Timeline.Count(e => e.Message is not null).ShouldBe(2);
        model.Lookups.Agents.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_closed_spam_ticket_reports_closed_and_wears_the_spam_stamp()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(status: TicketStatuses.Closed, isSpam: true)));

        var model = (await Presenter().LoadAsync("ORB-42", Ct)).Value;

        model.IsClosed.ShouldBeTrue();
        model.Stamp.ShouldBe(StampStatus.Spam);
    }

    [Fact]
    public async Task A_missing_ticket_is_reported_as_not_found_even_when_a_lookup_also_failed()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "No such ticket.", ResultErrorKind.NotFound));
        _tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<TagDto>>("boom"));

        var result = await Presenter().LoadAsync("ORB-42", Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Kind.ShouldBe(ResultErrorKind.NotFound);
        result.Errors[0].Code.ShouldBe("ticket-not-found");
    }

    [Theory]
    [InlineData("products")]
    [InlineData("agents")]
    [InlineData("tags")]
    public async Task A_failed_lookup_fails_the_load_with_that_error(string which)
    {
        switch (which)
        {
            case "products":
                _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<ProductDto>>("products-down", "Products are unavailable."));
                break;
            case "agents":
                _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<AgentListItemDto>>("agents-down", "Agents are unavailable."));
                break;
            default:
                _tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<TagDto>>("tags-down", "Tags are unavailable."));
                break;
        }

        var result = await Presenter().LoadAsync("ORB-42", Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].Code.ShouldBe($"{which}-down");
    }

    [Fact]
    public async Task A_follow_up_fetches_its_parent_once_to_show_the_parent_number()
    {
        var parentId = Guid.NewGuid();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(parentId: parentId)));
        _tickets.GetAsync(parentId.ToString(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail("ORB-7")));

        var model = (await Presenter().LoadAsync("ORB-42", Ct)).Value;

        model.ParentTicketId.ShouldBe(parentId);
        model.ParentNumber.ShouldBe("ORB-7");
        await _tickets.Received(1).GetAsync(parentId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_parent_that_is_gone_shows_no_link_and_does_not_fail_the_load()
    {
        var parentId = Guid.NewGuid();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(parentId: parentId)));
        _tickets.GetAsync(parentId.ToString(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));

        var result = await Presenter().LoadAsync("ORB-42", Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ParentNumber.ShouldBeNull();
    }

    [Fact]
    public async Task A_ticket_without_a_parent_makes_no_extra_read()
    {
        await Presenter().LoadAsync("ORB-42", Ct);

        await _tickets.Received(1).GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("""{"appVersion":"2.3.1","device":"Pixel 8"}""", true)]
    public async Task Metadata_is_read_into_labelled_items_and_absent_when_there_is_none(string? json, bool present)
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(metadataJson: json, metadataTrusted: true)));

        var metadata = (await Presenter().LoadAsync("ORB-42", Ct)).Value.Metadata;

        (metadata is not null).ShouldBe(present);
        if (present)
        {
            metadata!.Trusted.ShouldBeTrue();
            metadata.Items.ShouldBe([new MetadataItem("appVersion", "2.3.1"), new MetadataItem("device", "Pixel 8")]);
        }
    }

    [Fact]
    public async Task Metadata_that_is_not_valid_json_is_flagged_unreadable_not_dropped()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail(metadataJson: "{oops")));

        var metadata = (await Presenter().LoadAsync("ORB-42", Ct)).Value.Metadata;

        metadata.ShouldNotBeNull();
        metadata.Readable.ShouldBeFalse();
        metadata.Trusted.ShouldBeFalse();
    }

    [Fact]
    public async Task The_token_reaches_every_client_call()
    {
        using var cts = new CancellationTokenSource();

        await Presenter().LoadAsync("ORB-42", cts.Token);

        await _tickets.Received().GetAsync("ORB-42", cts.Token);
        await _products.Received().ListAsync(cts.Token);
        await _agents.Received().ListAllAsync(cts.Token);
        await _tags.Received().ListAsync(cts.Token);
    }

    [Fact]
    public async Task A_write_s_state_response_replaces_status_assignee_tags_and_row_version_from_the_lookups()
    {
        var model = (await Presenter().LoadAsync("ORB-42", Ct)).Value;

        var after = model.WithState(TestData.State(TicketStatuses.Solved, TicketPriorities.Urgent, TestData.SamAgentId, tagIds: [TestData.BugTagId], rowVersion: 12));

        after.Status.ShouldBe(TicketStatuses.Solved);
        after.Priority.ShouldBe(TicketPriorities.Urgent);
        after.AssigneeName.ShouldBe("Sam Ortiz");
        after.Tags.Select(t => t.Name).ShouldBe(["bug"]);
        after.RowVersion.ShouldBe(12u);
        after.Timeline.ShouldBe(model.Timeline);
    }

    [Fact]
    public async Task An_assignee_missing_from_the_lookups_keeps_the_name_already_shown_and_a_new_unknown_one_gets_a_fallback()
    {
        var detail = TestData.Detail(assigneeId: TestData.AdaAgentId, assigneeName: "Ada Admin");
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(detail));
        var model = (await Presenter().LoadAsync("ORB-42", Ct)).Value;

        model.WithState(TestData.State(assigneeId: TestData.AdaAgentId)).AssigneeName.ShouldBe("Ada Admin");
        model.WithState(TestData.State(assigneeId: Guid.NewGuid())).AssigneeName.ShouldBe("another agent");
        model.WithState(TestData.State(assigneeId: null)).AssigneeName.ShouldBeNull();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/TicketDetailPageTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class TicketDetailPageTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly NavigationManager _navigation;

    public TicketDetailPageTests()
    {
        var products = Substitute.For<IProductsClient>();
        var agents = Substitute.For<IAgentsClient>();
        var tags = Substitute.For<ITagsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.Agent("Sam Ortiz", TestData.SamAgentId)]));
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(agents);
        Services.AddSingleton(tags);
        Services.AddTicketFeatures();
        ShowTicket(TestData.Detail());
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private void ShowTicket(TicketDetailDto detail) =>
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(detail));

    private IRenderedComponent<TicketDetailPage> RenderTicket() => Render<TicketDetailPage>(p => p.Add(c => c.Number, "ORB-42"));

    [Fact]
    public void The_header_shows_a_copyable_number_subject_status_product_and_caller()
    {
        var cut = RenderTicket();

        cut.Find("code.ts-ticket-number").TextContent.ShouldBe("ORB-42");
        cut.Find("h1.ts-ticket-subject").TextContent.ShouldBe("Cannot log in");
        cut.Find(".ts-ticket-badges .ts-stamp").TextContent.ShouldBe("Open");
        cut.Find(".ts-ticket-badges .ts-stamp").ClassList.ShouldContain("ts-stamp--ticket");
        cut.Find(".ts-ticket-badges .ts-product").TextContent.ShouldBe("Orbitly");
        cut.FindAll(".ts-callform-field dd").Select(d => d.TextContent.Trim()).ShouldContain("Ada Lovelace");
    }

    [Fact]
    public void While_loading_the_page_shows_a_skeleton_and_then_the_ticket()
    {
        var gate = new TaskCompletionSource<Result<TicketDetailDto>>();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(gate.Task);

        var cut = RenderTicket();

        cut.FindAll(".ts-skeleton").Count.ShouldBe(6);
        cut.FindAll("article.ts-ticket").ShouldBeEmpty();

        gate.SetResult(TestData.Ok(TestData.Detail()));

        cut.WaitForAssertion(() => cut.Find("article.ts-ticket").ShouldNotBeNull());
        cut.FindAll(".ts-skeleton").ShouldBeEmpty();
    }

    [Fact]
    public void A_404_shows_the_plain_not_found_view_with_a_way_back_and_no_ticket_chrome()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "No such ticket.", ResultErrorKind.NotFound));

        var cut = RenderTicket();

        cut.Find("section.ts-gone h1").TextContent.ShouldBe("Ticket not found");
        cut.Find("section.ts-gone a").GetAttribute("href").ShouldBe("/queue");
        cut.FindAll("article.ts-ticket").ShouldBeEmpty();
        cut.FindAll(".ts-window").ShouldBeEmpty();
    }

    [Fact]
    public void Another_failure_shows_an_alert_with_retry_and_retry_loads_the_ticket()
    {
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<TicketDetailDto>("boom", "The API is unavailable."),
            TestData.Ok(TestData.Detail()));

        var cut = RenderTicket();

        cut.Find("[role=alert] p").TextContent.ShouldBe("Couldn't load this ticket. The API is unavailable.");

        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => cut.Find("article.ts-ticket").ShouldNotBeNull());
    }

    [Fact]
    public void The_timeline_is_one_ordered_list_of_messages_and_changes()
    {
        var t = TestData.Now.AddHours(-5);
        ShowTicket(TestData.Detail(
            messages: [TestData.Message(at: t, bodyHtml: "<p>Help</p>"), TestData.Message(MessageAuthorTypes.Agent, at: t.AddHours(1), authorName: "Sam Ortiz", bodyHtml: "<p>Try this</p>")],
            events: [TestData.Event(TicketEventTypes.Created, """{"channel":"Email"}""", t, "Requester", "Ada Lovelace"), TestData.Event(TicketEventTypes.StatusChanged, """{"from":"New","to":"Pending"}""", t.AddHours(1))]));

        var cut = RenderTicket();

        var items = cut.FindAll("ol.ts-timeline > li");
        items.Select(i => i.ClassList.Contains("ts-timeline-message") ? "message" : i.QuerySelector(".ts-event-text")!.TextContent).ShouldBe(
            ["Ticket opened via Email", "message", "message", "Status changed from New to Pending"]);
        cut.Find("ol.ts-timeline").GetAttribute("aria-label").ShouldBe("Timeline");
        cut.FindAll(".ts-legend").Count.ShouldBe(1);
    }

    [Fact]
    public void Internal_notes_customer_messages_and_public_replies_are_visibly_distinct_with_text_labels()
    {
        ShowTicket(TestData.Detail(messages:
        [
            TestData.Message(MessageAuthorTypes.Requester),
            TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Public, authorName: "Sam Ortiz"),
            TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Internal, authorName: "Sam Ortiz"),
        ]));

        var cut = RenderTicket();

        cut.FindAll("article.ts-entry--customer").Count.ShouldBe(1);
        cut.FindAll("article.ts-entry--public").Count.ShouldBe(1);
        var note = cut.Find("article.ts-entry--note");
        note.TextContent.ShouldContain("INTERNAL NOTE");
        cut.FindAll(".ts-entry--customer .ts-entry-role").Single().TextContent.ShouldBe("customer");
    }

    [Fact]
    public void The_message_body_is_the_servers_sanitised_html_and_everything_else_is_encoded()
    {
        ShowTicket(TestData.Detail(
            subject: "<img src=x onerror=alert(1)>",
            messages: [TestData.Message(bodyHtml: "<p>Hello <strong>there</strong></p>", authorName: "<b>Mallory</b>")]));

        var cut = RenderTicket();

        cut.Find(".ts-message-body strong").TextContent.ShouldBe("there");
        cut.Find("h1.ts-ticket-subject").InnerHtml.ShouldBe("&lt;img src=x onerror=alert(1)&gt;");
        cut.Find(".ts-entry-head strong").TextContent.ShouldBe("<b>Mallory</b>");
        cut.FindAll("img[onerror]").ShouldBeEmpty();
    }

    [Fact]
    public void Attachments_link_to_the_admin_pass_through_as_downloads()
    {
        var id = Guid.NewGuid();
        ShowTicket(TestData.Detail(messages: [TestData.Message(attachments: [new AttachmentDto(id, "screenshot.png", "image/png", 2048)])]));

        var cut = RenderTicket();

        var link = cut.Find("ul.ts-attachments a");
        link.GetAttribute("href").ShouldBe($"/attachments/{id}");
        link.HasAttribute("download").ShouldBeTrue();
        link.GetAttribute("data-enhance-nav").ShouldBe("false");
        link.TextContent.ShouldBe("screenshot.png");
        cut.Find(".ts-attachment-meta").TextContent.ShouldBe("(image/png, 2 KB)");
    }

    [Fact]
    public void Linked_articles_are_listed_by_title_without_inventing_a_link()
    {
        ShowTicket(TestData.Detail(messages: [TestData.Message(MessageAuthorTypes.Agent, articles: [new LinkedArticleDto(Guid.NewGuid(), "Reset your password", "reset-password")])]));

        var cut = RenderTicket();

        cut.Find("p.ts-articles").TextContent.ShouldBe("Linked articles: Reset your password");
        cut.FindAll("p.ts-articles a").ShouldBeEmpty();
    }

    [Fact]
    public void A_closed_ticket_is_read_only_and_says_why()
    {
        ShowTicket(TestData.Detail(status: TicketStatuses.Closed));

        var cut = RenderTicket();

        cut.Find("p.ts-closed-note").TextContent.ShouldBe("Closed tickets are read-only; a customer reply starts a follow-up");
        cut.Find(".ts-ticket-badges .ts-stamp").TextContent.ShouldBe("Closed");
        cut.FindAll("textarea, select, form").ShouldBeEmpty();
    }

    [Fact]
    public void An_open_ticket_has_no_closed_note()
    {
        RenderTicket().FindAll("p.ts-closed-note").ShouldBeEmpty();
    }

    [Fact]
    public void A_follow_up_links_to_its_parent_by_number()
    {
        var parentId = Guid.NewGuid();
        ShowTicket(TestData.Detail(parentId: parentId));
        _tickets.GetAsync(parentId.ToString(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail("ORB-7")));

        var cut = RenderTicket();

        var link = cut.Find("p.ts-followup a");
        link.TextContent.ShouldBe("ORB-7");
        link.GetAttribute("href").ShouldBe("/tickets/ORB-7");
        cut.Find("p.ts-followup").TextContent.ShouldStartWith("Follow-up to");
    }

    [Fact]
    public void Metadata_from_a_public_key_is_labelled_untrusted_and_a_trusted_one_is_not()
    {
        ShowTicket(TestData.Detail(metadataJson: """{"appVersion":"2.3.1"}""", metadataTrusted: false));
        var untrusted = RenderTicket();

        untrusted.Find("section.ts-metadata--untrusted .ts-metadata-trust strong").TextContent.ShouldBe("Untrusted");
        untrusted.Find("section.ts-metadata dt").TextContent.ShouldBe("appVersion");
        untrusted.Find("section.ts-metadata dd").TextContent.ShouldBe("2.3.1");

        ShowTicket(TestData.Detail(metadataJson: """{"appVersion":"2.3.1"}""", metadataTrusted: true));
        var trusted = RenderTicket();

        trusted.FindAll(".ts-metadata--untrusted").ShouldBeEmpty();
        trusted.Find("section.ts-metadata .ts-metadata-trust strong").TextContent.ShouldBe("Trusted");
    }

    [Fact]
    public void A_ticket_without_metadata_shows_no_metadata_section()
    {
        RenderTicket().FindAll("section.ts-metadata").ShouldBeEmpty();
    }

    [Fact]
    public void The_side_panel_shows_the_requester_and_the_ticket_facts_read_only()
    {
        ShowTicket(TestData.Detail(priority: TicketPriorities.Urgent, assigneeId: TestData.SamAgentId, assigneeName: "Sam Ortiz",
            tags: [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]));

        var cut = RenderTicket();

        cut.Find("section.ts-requester .ts-requester-email").TextContent.ShouldBe("ada@example.com");
        var facts = cut.Find("section.ts-facts");
        facts.TextContent.ShouldContain("Sam Ortiz");
        facts.QuerySelector(".ts-priority")!.TextContent.ShouldBe("Urgent");
        facts.QuerySelector(".ts-tag")!.TextContent.ShouldBe("bug");
    }

    [Fact]
    public async Task Escape_goes_back_to_the_queue_but_not_while_typing()
    {
        RenderTicket();

        await PressAsync("Escape", typing: true);
        _navigation.Uri.ShouldNotEndWith("/queue");

        await PressAsync("Escape");

        _navigation.Uri.ShouldEndWith("/queue");
    }

    [Fact]
    public async Task A_refresh_keeps_the_ticket_on_screen_and_a_deleted_ticket_becomes_no_longer_exists()
    {
        var cut = RenderTicket();
        var gate = new TaskCompletionSource<Result<TicketDetailDto>>();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(gate.Task);

        var refresh = cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        cut.Find("article.ts-ticket").ShouldNotBeNull();
        cut.FindAll(".ts-skeleton").ShouldBeEmpty();

        gate.SetResult(TestData.Fail<TicketDetailDto>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));
        await refresh;

        cut.Find("section.ts-gone h1").TextContent.ShouldBe("This ticket no longer exists");
        cut.FindAll("article.ts-ticket").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_ticket_and_shows_an_inline_alert()
    {
        var cut = RenderTicket();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("boom", "The API is unavailable."));

        await cut.InvokeAsync(() => cut.Instance.RefreshAsync());

        cut.Find("article.ts-ticket [role=alert] p").TextContent.ShouldContain("The API is unavailable.");
        cut.Find("code.ts-ticket-number").TextContent.ShouldBe("ORB-42");
    }

    [Fact]
    public void Applying_a_write_response_replaces_the_row_version_and_status_on_screen()
    {
        var cut = RenderTicket();

        cut.InvokeAsync(() => cut.Instance.ApplyState(TestData.State(TicketStatuses.Solved, rowVersion: 12)));

        cut.Find(".ts-ticket-badges .ts-stamp").TextContent.ShouldBe("Solved");
    }

    [Fact]
    public void The_ticket_number_in_the_route_is_what_the_API_is_asked_for_and_the_token_is_passed()
    {
        RenderTicket();

        _tickets.Received(1).GetAsync("ORB-42", Arg.Is<CancellationToken>(t => t.CanBeCanceled));
    }
}
```

`tests/TechStrap.Admin.Tests/MarkupStringSiteTests.cs`:

```csharp
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The one place the Admin turns API HTML into markup is the message bubble; the server sanitises that body (PHASE-07 T10).</summary>
public sealed class MarkupStringSiteTests
{
    [Fact]
    public void MarkupString_is_used_in_exactly_one_file_the_message_bubble()
    {
        var admin = RepositoryRoot.Combine("src", "TechStrap.Admin");
        var separator = Path.DirectorySeparatorChar;

        var users = Directory.EnumerateFiles(admin, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".razor", StringComparison.Ordinal) || file.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{separator}obj{separator}") && !file.Contains($"{separator}bin{separator}"))
            .Where(file => File.ReadAllText(file).Contains("MarkupString", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(admin, file).Replace('\\', '/'))
            .ToList();

        users.ShouldBe(["Features/Tickets/MessageBubble.razor"]);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "TimelineEntryFactoryTests|TicketDetailPresenterTests|TicketDetailPageTests|MarkupStringSiteTests"`
Expected: a build failure (`TimelineEntryFactory`, `TicketDetailPresenter`, `TicketDetailPage`, `AddTicketFeatures` do not exist).

- [ ] **Step 3: Implement**

1. Register the services. In `Program.cs` add `using TechStrap.Admin.Features.Tickets;` and `builder.Services.AddTicketFeatures();` after `AddShell()`. Add `@import "ticket";` to `app.scss` after `@import "queue";`. The partial also holds the classes the Task 10 to 12 components use (composer styles live in their own `_composer.scss`), so it is written once.
2. The model, the factory and the presenter:

`src/TechStrap.Admin/Features/Tickets/TicketCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// The ticket screen's copy, defined once. The strings from UX-BRIEF-admin are used word for word; the rest follows the voice rules
/// (plain cause plus next step, sentence case, no humour, no exclamation marks).
/// </summary>
public static class TicketCopy
{
    public const string Loading = "Loading ticket";
    public const string LoadFailed = "Couldn't load this ticket.";
    public const string NotFoundHeading = "Ticket not found";
    public const string NotFoundBody = "No ticket has that number. Check the number, or search the queue.";
    public const string GoneHeading = "This ticket no longer exists";
    public const string GoneBody = "It was deleted after you opened it.";
    public const string BackToQueue = "Back to the queue";
    public const string ClosedNotice = "Closed tickets are read-only; a customer reply starts a follow-up";
    public const string FollowUpTo = "Follow-up to";
    public const string Untrusted = "Untrusted";
    public const string Trusted = "Trusted";
    public const string UntrustedHelp = "Sent by an app with a public key. Don't rely on it.";
    public const string MetadataUnreadable = "The metadata couldn't be read.";
    public const string RequesterHeading = "Requester";
    public const string MetadataHeading = "Metadata";
    public const string TimelineLabel = "Timeline";
    public const string AttachmentsLabel = "Attachments";
    public const string LinkedArticlesLabel = "Linked articles";
}
```

`src/TechStrap.Admin/Features/Tickets/TicketDetailModel.cs`

```csharp
using TechStrap.Admin.Components.Ui;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// The reference data the ticket screen resolves ids against: the API's events and state responses carry ids only (no product, agent or tag names).
/// Agents see only active agents and active products, so every lookup has a fallback for an id that is no longer in the list.
/// </summary>
public sealed record TicketLookups(IReadOnlyList<ProductDto> Products, IReadOnlyList<AgentListItemDto> Agents, IReadOnlyList<TagDto> Tags)
{
    public const string UnknownProduct = "another product";
    public const string UnknownAgent = "another agent";
    public const string UnknownTag = "a deleted tag";

    public static TicketLookups Empty { get; } = new([], [], []);

    public string ProductName(Guid? id) => Products.FirstOrDefault(p => p.Id == id)?.Name ?? UnknownProduct;

    public string AgentName(Guid? id) => Agents.FirstOrDefault(a => a.Id == id)?.DisplayLabel ?? UnknownAgent;

    public TagDto? Tag(Guid id) => Tags.FirstOrDefault(t => t.Id == id);

    public string TagName(Guid? id) => id is { } tagId && Tag(tagId) is { } tag ? tag.Name : UnknownTag;
}

public enum TimelineEntryKind
{
    Message,
    Event,
}

/// <summary>One line of the single chronological stream (UX-BRIEF-admin): a message or a change, never both.</summary>
public sealed record TimelineEntryViewModel(Guid Id, TimelineEntryKind Kind, DateTimeOffset At, string Actor, string Text, MessageViewModel? Message);

public sealed record MessageViewModel(
    Guid Id,
    EntryKind Kind,
    string Author,
    DateTimeOffset At,
    string BodyHtml,
    IReadOnlyList<AttachmentDto> Attachments,
    IReadOnlyList<LinkedArticleDto> Articles);

public sealed record MetadataItem(string Key, string Value);

/// <param name="Trusted">True only when the ticket came in on a trusted API key (<c>MetadataTrusted</c>). Everything else is labelled untrusted.</param>
/// <param name="Readable">False when the stored JSON could not be read; the panel then says so instead of showing nothing.</param>
public sealed record MetadataViewModel(bool Trusted, bool Readable, IReadOnlyList<MetadataItem> Items);

/// <summary>Everything the ticket screen draws. Writes replace the status fields from the API's returned <see cref="TicketStateDto"/> through <see cref="WithState"/>.</summary>
public sealed record TicketDetailViewModel(
    Guid Id,
    string Number,
    string Subject,
    string Status,
    string Priority,
    bool IsSpam,
    Guid ProductId,
    string ProductName,
    Guid? AssigneeId,
    string? AssigneeName,
    IReadOnlyList<TicketTagDto> Tags,
    TicketRequesterDto Requester,
    string Channel,
    DateTimeOffset CreatedAt,
    Guid? ParentTicketId,
    string? ParentNumber,
    MetadataViewModel? Metadata,
    uint RowVersion,
    IReadOnlyList<TimelineEntryViewModel> Timeline,
    TicketLookups Lookups)
{
    /// <summary>Closed tickets are read-only: no composer, no actions (a customer reply starts a follow-up).</summary>
    public bool IsClosed => Status == TicketStatuses.Closed;

    public StampStatus Stamp => TicketDisplay.Stamp(Status, IsSpam);

    /// <summary>Who wrote in: the requester's name, or their address when they gave none.</summary>
    public string Caller => string.IsNullOrWhiteSpace(Requester.Name) ? Requester.Email : Requester.Name;

    /// <summary>
    /// The model after a write: status, priority, product, assignee, tags, spam flag and RowVersion all come from the response, so the display
    /// can never run ahead of what the server accepted. Names are resolved from the lookups; an id the lookups lack keeps the name already shown.
    /// </summary>
    public TicketDetailViewModel WithState(TicketStateDto state)
    {
        var assigneeName = state.AssigneeId is null
            ? null
            : state.AssigneeId == AssigneeId
                ? AssigneeName
                : Lookups.AgentName(state.AssigneeId);
        var productName = state.ProductId == ProductId ? ProductName : Lookups.ProductName(state.ProductId);
        var tags = state.TagIds
            .Select(id => Tags.FirstOrDefault(t => t.Id == id) ?? (Lookups.Tag(id) is { } tag ? new TicketTagDto(tag.Id, tag.Name, tag.Colour) : null))
            .OfType<TicketTagDto>()
            .ToList();
        return this with
        {
            Status = state.Status,
            Priority = state.Priority,
            IsSpam = state.IsSpam,
            ProductId = state.ProductId,
            ProductName = productName,
            AssigneeId = state.AssigneeId,
            AssigneeName = assigneeName,
            Tags = tags,
            RowVersion = state.RowVersion,
        };
    }
}
```

`src/TechStrap.Admin/Features/Tickets/TimelineEntryFactory.cs`

```csharp
using System.Globalization;
using System.Text.Json;
using TechStrap.Admin.Components.Ui;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// Builds the single chronological stream from the API's messages and events (both oldest first). A <c>MessageAdded</c> event is folded into its message
/// (matched by <c>messageId</c>), so a reply appears once. Payloads carry ids only; names come from <see cref="TicketLookups"/>. An event type this build does not know,
/// or a payload that cannot be read, becomes a generic line: the timeline never throws (PHASE-07 T09).
/// </summary>
public static class TimelineEntryFactory
{
    private const string MessageIdKey = "messageId";
    private const string ChannelKey = "channel";
    private const string FromKey = "from";
    private const string ToKey = "to";
    private const string TagIdKey = "tagId";
    private const string ReasonKey = "reason";
    private const string IsSpamKey = "isSpam";
    private const string TagDeletedReason = "tag-deleted";

    private const int CreatedRank = 0;
    private const int MessageRank = 1;
    private const int EventRank = 2;

    public static IReadOnlyList<TimelineEntryViewModel> Build(IReadOnlyList<MessageDto> messages, IReadOnlyList<TicketEventDto> events, TicketLookups lookups)
    {
        var messageIds = messages.Select(m => m.Id).ToHashSet();
        var ranked = new List<(int Rank, TimelineEntryViewModel Entry)>(messages.Count + events.Count);

        foreach (var message in messages)
        {
            ranked.Add((MessageRank, FromMessage(message)));
        }

        foreach (var ticketEvent in events)
        {
            if (ticketEvent.Type == TicketEventTypes.MessageAdded && ReadGuid(ticketEvent.PayloadJson, MessageIdKey) is { } id && messageIds.Contains(id))
            {
                continue;
            }

            ranked.Add((ticketEvent.Type == TicketEventTypes.Created ? CreatedRank : EventRank, FromEvent(ticketEvent, lookups)));
        }

        // A stable sort: entries at the same instant keep the API's order (Created, then the message, then the changes made with it).
        return ranked.Select((item, index) => (item.Rank, item.Entry, Index: index))
            .OrderBy(item => item.Entry.At).ThenBy(item => item.Rank).ThenBy(item => item.Index)
            .Select(item => item.Entry)
            .ToList();
    }

    private static TimelineEntryViewModel FromMessage(MessageDto message)
    {
        var kind = message.AuthorType == MessageAuthorTypes.Requester
            ? EntryKind.Customer
            : message.Visibility == MessageVisibilities.Internal ? EntryKind.InternalNote : EntryKind.PublicReply;
        var author = message.AuthorName ?? message.AuthorType;
        var view = new MessageViewModel(message.Id, kind, author, message.CreatedAt, message.BodyHtml, message.Attachments, message.LinkedArticles);
        return new TimelineEntryViewModel(message.Id, TimelineEntryKind.Message, message.CreatedAt, author, string.Empty, view);
    }

    private static TimelineEntryViewModel FromEvent(TicketEventDto e, TicketLookups lookups) =>
        new(e.Id, TimelineEntryKind.Event, e.OccurredAt, e.ActorName ?? e.ActorType, Describe(e, lookups), null);

    private static string Describe(TicketEventDto e, TicketLookups lookups)
    {
        using var payload = TryParse(e.PayloadJson);
        var root = payload?.RootElement;
        return e.Type switch
        {
            TicketEventTypes.Created => $"Ticket opened via {Text(root, ChannelKey) ?? "an unknown channel"}",
            TicketEventTypes.MessageAdded => "A message was added",
            TicketEventTypes.StatusChanged => $"Status changed from {Text(root, FromKey) ?? "?"} to {Text(root, ToKey) ?? "?"}",
            TicketEventTypes.Assigned => DescribeAssignment(root, lookups),
            TicketEventTypes.ProductChanged => $"Product changed from {lookups.ProductName(Id(root, FromKey))} to {lookups.ProductName(Id(root, ToKey))}",
            TicketEventTypes.PriorityChanged => $"Priority changed from {Text(root, FromKey) ?? "?"} to {Text(root, ToKey) ?? "?"}",
            TicketEventTypes.TagAdded => $"Tag {lookups.TagName(Id(root, TagIdKey))} added",
            TicketEventTypes.TagRemoved => Text(root, ReasonKey) == TagDeletedReason
                ? $"Tag {lookups.TagName(Id(root, TagIdKey))} removed because the tag was deleted"
                : $"Tag {lookups.TagName(Id(root, TagIdKey))} removed",
            TicketEventTypes.MarkedSpam => Flag(root, IsSpamKey) == false ? "Restored from spam" : "Marked as spam",
            TicketEventTypes.FollowUpCreated => "The customer replied after the ticket closed, so a follow-up ticket was opened",
            _ => $"Event: {e.Type}",
        };
    }

    private static string DescribeAssignment(JsonElement? root, TicketLookups lookups)
    {
        var from = Id(root, FromKey);
        var to = Id(root, ToKey);
        return (from, to) switch
        {
            (null, null) => "Assignment changed",
            (null, { } target) => $"Assigned to {lookups.AgentName(target)}",
            ({ } source, null) => $"Unassigned (was {lookups.AgentName(source)})",
            ({ } source, { } target) => $"Reassigned from {lookups.AgentName(source)} to {lookups.AgentName(target)}",
        };
    }

    private static JsonDocument? TryParse(string json)
    {
        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Guid? ReadGuid(string json, string key)
    {
        using var payload = TryParse(json);
        return payload is null ? null : Id(payload.RootElement, key);
    }

    private static string? Text(JsonElement? root, string key) =>
        root is { } element && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Guid? Id(JsonElement? root, string key) =>
        Guid.TryParse(Text(root, key), CultureInfo.InvariantCulture, out var id) ? id : null;

    private static bool? Flag(JsonElement? root, string key) =>
        root is { } element && element.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
```

`src/TechStrap.Admin/Features/Tickets/TicketDetailPresenter.cs`

```csharp
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// Assembles the ticket screen's model: the ticket, the products, agents and tags its ids resolve against, and (for a follow-up) the parent's number.
/// It exists because the assembly is asynchronous and uses four clients (PHASE-07 allows exactly this presenter, the timeline factory and the audit summary factory).
/// The ticket's own failure wins over a lookup failure, so a missing ticket is reported as missing. Components never call the clients for lookups themselves.
/// </summary>
public sealed class TicketDetailPresenter(ITicketsClient tickets, IProductsClient products, IAgentsClient agents, ITagsClient tags)
{
    public async Task<Result<TicketDetailViewModel>> LoadAsync(string reference, CancellationToken cancellationToken)
    {
        var detailTask = tickets.GetAsync(reference, cancellationToken);
        var productsTask = products.ListAsync(cancellationToken);
        var agentsTask = agents.ListAllAsync(cancellationToken);
        var tagsTask = tags.ListAsync(cancellationToken);
        await Task.WhenAll(detailTask, productsTask, agentsTask, tagsTask);

        var detail = detailTask.Result;
        if (detail.IsFailure)
        {
            return Result<TicketDetailViewModel>.Failure(detail.Errors[0]);
        }

        foreach (var lookup in new Result[] { productsTask.Result.ToResult(), agentsTask.Result.ToResult(), tagsTask.Result.ToResult() })
        {
            if (lookup.IsFailure)
            {
                return Result<TicketDetailViewModel>.Failure(lookup.Errors[0]);
            }
        }

        var lookups = new TicketLookups(productsTask.Result.Value, agentsTask.Result.Value, tagsTask.Result.Value);
        var ticket = detail.Value;
        var parentNumber = await LoadParentNumberAsync(ticket.ParentTicketId, cancellationToken);
        return Result<TicketDetailViewModel>.Success(Map(ticket, lookups, parentNumber));
    }

    // One extra read. A parent that was deleted (or is not visible) simply shows no link.
    private async Task<string?> LoadParentNumberAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            return null;
        }

        var parent = await tickets.GetAsync(parentId.Value.ToString(), cancellationToken);
        return parent.IsSuccess ? parent.Value.Number : null;
    }

    private static TicketDetailViewModel Map(TicketDetailDto ticket, TicketLookups lookups, string? parentNumber) => new(
        ticket.Id,
        ticket.Number,
        ticket.Subject,
        ticket.Status,
        ticket.Priority,
        ticket.IsSpam,
        ticket.ProductId,
        ticket.ProductName,
        ticket.AssigneeId,
        ticket.AssigneeName,
        ticket.Tags,
        ticket.Requester,
        ticket.Channel,
        ticket.CreatedAt,
        ticket.ParentTicketId,
        parentNumber,
        ReadMetadata(ticket.MetadataJson, ticket.MetadataTrusted),
        ticket.RowVersion,
        TimelineEntryFactory.Build(ticket.Messages, ticket.Events, lookups),
        lookups);

    private static MetadataViewModel? ReadMetadata(string? json, bool trusted)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new MetadataViewModel(trusted, Readable: false, []);
            }

            var items = document.RootElement.EnumerateObject()
                .Select(p => new MetadataItem(p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.GetRawText()))
                .ToList();
            return items.Count == 0 ? null : new MetadataViewModel(trusted, Readable: true, items);
        }
        catch (JsonException)
        {
            return new MetadataViewModel(trusted, Readable: false, []);
        }
    }
}

internal static class ResultConversions
{
    /// <summary>Drops the value so differently-typed results can be checked in one loop.</summary>
    public static Result ToResult<T>(this Result<T> result) =>
        result.IsSuccess ? Result.Success() : Result.Failure(result.Errors[0]);
}
```

`src/TechStrap.Admin/Features/Tickets/TicketsServiceCollectionExtensions.cs`

```csharp
namespace TechStrap.Admin.Features.Tickets;

public static class TicketsServiceCollectionExtensions
{
    /// <summary>Registers the ticket screen's services. Scoped: one presenter per circuit, built over that circuit's clients.</summary>
    public static IServiceCollection AddTicketFeatures(this IServiceCollection services)
    {
        services.AddScoped<TicketDetailPresenter>();
        return services;
    }
}
```

3. The components:

`src/TechStrap.Admin/Features/Tickets/TicketHeader.razor`

```razor
<header class="ts-ticket-header">
    <dl class="ts-callform">
        <div class="ts-callform-field">
            <dt><span class="ts-field-no" aria-hidden="true">1</span> Ticket no.</dt>
            <dd><code class="ts-ticket-number">@Ticket.Number</code></dd>
        </div>
        <div class="ts-callform-field">
            <dt><span class="ts-field-no" aria-hidden="true">2</span> Received</dt>
            <dd><RelativeTime When="Ticket.CreatedAt" /></dd>
        </div>
        <div class="ts-callform-field">
            <dt><span class="ts-field-no" aria-hidden="true">3</span> Caller</dt>
            <dd>@Ticket.Caller</dd>
        </div>
        <div class="ts-callform-field">
            <dt><span class="ts-field-no" aria-hidden="true">4</span> Channel</dt>
            <dd>@Ticket.Channel</dd>
        </div>
    </dl>
    <div class="ts-callform-problem">
        <span class="ts-field-no" aria-hidden="true">5</span>
        <h1 class="ts-ticket-subject">@Ticket.Subject</h1>
    </div>
    <div class="ts-ticket-badges">
        <StatusStamp Status="Ticket.Stamp" Variant="StampVariant.Ticket" />
        <span class="ts-product">@Ticket.ProductName</span>
    </div>
    @if (Ticket.ParentNumber is not null)
    {
        <p class="ts-followup">@TicketCopy.FollowUpTo <a href="@($"/tickets/{Uri.EscapeDataString(Ticket.ParentNumber)}")">@Ticket.ParentNumber</a></p>
    }
</header>

@code {
    [Parameter, EditorRequired]
    public TicketDetailViewModel Ticket { get; set; } = default!;
}
```

`src/TechStrap.Admin/Features/Tickets/TicketTimeline.razor`

```razor
<ol class="ts-timeline" aria-label="@TicketCopy.TimelineLabel">
    @foreach (var entry in Entries)
    {
        <li @key="entry.Id" class="@(entry.Kind == TimelineEntryKind.Message ? "ts-timeline-message" : "ts-timeline-event")">
            @if (entry.Message is { } message)
            {
                <MessageBubble Message="message" />
            }
            else
            {
                <span class="ts-event-text">@entry.Text</span>
                <span class="ts-event-by">by @entry.Actor</span>
                <RelativeTime When="entry.At" />
            }
        </li>
    }
</ol>

@code {
    [Parameter, EditorRequired]
    public IReadOnlyList<TimelineEntryViewModel> Entries { get; set; } = [];
}
```

`src/TechStrap.Admin/Features/Tickets/MessageBubble.razor`

```razor
<TintedEntry Kind="Message.Kind" Author="@Message.Author" Time="@TicketDisplay.Relative(Message.At, Time.GetUtcNow())">
    @* The ONE place the Admin renders HTML from the API. MessageDto.BodyHtml is sanitised on the server; nothing else may use MarkupString (MarkupStringSiteTests). *@
    <div class="ts-message-body">@((MarkupString)Message.BodyHtml)</div>
    <AttachmentList Items="Message.Attachments" />
    @if (Message.Articles.Count > 0)
    {
        <p class="ts-articles">@TicketCopy.LinkedArticlesLabel: @string.Join(", ", Message.Articles.Select(a => a.Title))</p>
    }
</TintedEntry>
```

`src/TechStrap.Admin/Features/Tickets/MessageBubble.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>One message in the timeline, in the carbon tint code: customer, public reply or internal note. Its body is the only HTML the Admin renders from the API.</summary>
public sealed partial class MessageBubble
{
    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Parameter, EditorRequired]
    public MessageViewModel Message { get; set; } = default!;
}
```

`src/TechStrap.Admin/Features/Tickets/AttachmentList.razor`

```razor
@if (Items.Count > 0)
{
    <ul class="ts-attachments" aria-label="@TicketCopy.AttachmentsLabel">
        @foreach (var item in Items)
        {
            @* Served by the Admin pass-through (GET /attachments/{id}): always a download, never rendered inline. data-enhance-nav keeps Blazor's router from treating it as a page. *@
            <li>
                <a href="@($"/attachments/{item.Id}")" download data-enhance-nav="false">@item.FileName</a>
                <span class="ts-attachment-meta">(@item.ContentType, @TicketDisplay.FileSize(item.Size))</span>
            </li>
        }
    </ul>
}

@code {
    [Parameter, EditorRequired]
    public IReadOnlyList<AttachmentDto> Items { get; set; } = [];
}
```

`src/TechStrap.Admin/Features/Tickets/TicketSidePanel.razor`

```razor
<aside class="ts-side" aria-label="Ticket details">
    @if (Controls is not null)
    {
        @Controls
    }
    else
    {
        <TicketFacts Ticket="Ticket" />
    }
    <RequesterCard Requester="Ticket.Requester" />
    <MetadataPanel Metadata="Ticket.Metadata" />
</aside>

@code {
    [Parameter, EditorRequired]
    public TicketDetailViewModel Ticket { get; set; } = default!;

    /// <summary>The editable controls (status, assignee, priority, product, tags). Without them the same facts are shown as read-only text (a Closed ticket).</summary>
    [Parameter]
    public RenderFragment? Controls { get; set; }
}
```

`src/TechStrap.Admin/Features/Tickets/TicketFacts.razor`

```razor
<section class="ts-facts" aria-label="Ticket properties">
    <dl>
        <dt>Status</dt>
        <dd><StatusStamp Status="Ticket.Stamp" Variant="StampVariant.Ticket" /></dd>
        <dt>Assignee</dt>
        <dd>@(Ticket.AssigneeName ?? "Unassigned")</dd>
        <dt>Priority</dt>
        <dd><PriorityMark Level="TicketDisplay.Priority(Ticket.Priority)" /></dd>
        <dt>Product</dt>
        <dd>@Ticket.ProductName</dd>
        <dt>Tags</dt>
        <dd>
            @if (Ticket.Tags.Count == 0)
            {
                <span>None</span>
            }
            @foreach (var tag in Ticket.Tags)
            {
                <TagChip Name="@tag.Name" Colour="@tag.Colour" />
            }
        </dd>
    </dl>
</section>

@code {
    [Parameter, EditorRequired]
    public TicketDetailViewModel Ticket { get; set; } = default!;
}
```

`src/TechStrap.Admin/Features/Tickets/RequesterCard.razor`

```razor
<section class="ts-requester" aria-label="@TicketCopy.RequesterHeading">
    <h2>@TicketCopy.RequesterHeading</h2>
    @if (!string.IsNullOrWhiteSpace(Requester.Name))
    {
        <p class="ts-requester-name">@Requester.Name</p>
    }
    <p class="ts-requester-email">@Requester.Email</p>
</section>

@code {
    [Parameter, EditorRequired]
    public TicketRequesterDto Requester { get; set; } = default!;
}
```

`src/TechStrap.Admin/Features/Tickets/MetadataPanel.razor`

```razor
@if (Metadata is not null)
{
    <section class="@(Metadata.Trusted ? "ts-metadata" : "ts-metadata ts-metadata--untrusted")" aria-label="@TicketCopy.MetadataHeading">
        <h2>@TicketCopy.MetadataHeading</h2>
        @* Two cues besides colour: the word and the dashed border. *@
        <p class="ts-metadata-trust">
            <strong>@(Metadata.Trusted ? TicketCopy.Trusted : TicketCopy.Untrusted)</strong>
            @if (!Metadata.Trusted)
            {
                <span> &mdash; @TicketCopy.UntrustedHelp</span>
            }
        </p>
        @if (!Metadata.Readable)
        {
            <p>@TicketCopy.MetadataUnreadable</p>
        }
        else
        {
            <dl>
                @foreach (var item in Metadata.Items)
                {
                    <dt>@item.Key</dt>
                    <dd>@item.Value</dd>
                }
            </dl>
        }
    </section>
}

@code {
    [Parameter]
    public MetadataViewModel? Metadata { get; set; }
}
```

`src/TechStrap.Admin/Features/Tickets/TicketGone.razor`

```razor
<section class="ts-gone">
    <h1>@Heading</h1>
    <p>@Body</p>
    <a class="btn btn-outline-secondary" href="/queue">@TicketCopy.BackToQueue</a>
</section>

@code {
    [Parameter, EditorRequired]
    public string Heading { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string Body { get; set; } = string.Empty;
}
```

`src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor`

```razor
@page "/tickets/{Number}"
@attribute [Authorize]

<PageTitle>@Number</PageTitle>
@if (_loading)
{
    <LoadingState Rows="6" Label="@TicketCopy.Loading" />
}
else if (_gone is not null)
{
    <TicketGone Heading="@_gone.Heading" Body="@_gone.Body" />
}
else if (_model is null)
{
    <ErrorState Message="@_error" OnRetry="LoadAsync" />
}
else
{
    <article class="ts-ticket">
        <TicketHeader Ticket="_model" />
        @if (_error is not null)
        {
            <ErrorState Message="@_error" OnRetry="RefreshAsync" />
        }
        <div class="ts-ticket-layout">
            <section class="ts-conversation" aria-label="Conversation">
                <TicketTimeline Entries="_model.Timeline" />
                <TintLegend />
                @if (_model.IsClosed)
                {
                    <p class="ts-closed-note" role="note">@TicketCopy.ClosedNotice</p>
                }
            </section>
            <TicketSidePanel Ticket="_model" />
        </div>
    </article>
}
```

`src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// One ticket: header, one chronological timeline (messages and changes), and the side panel. The page owns the model and its RowVersion; the children raise callbacks
/// and the page replaces the model from the server's answer (never from what the user typed). A refresh keeps the current model on screen while it runs, and a ticket
/// deleted meanwhile becomes "This ticket no longer exists". Esc, when nothing is being typed, goes back to the queue.
/// </summary>
public sealed partial class TicketDetailPage : IDisposable
{
    private const string QueuePath = "/queue";

    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _load;
    private string? _loadedNumber;
    private GoneMessage? _gone;
    private string _error = string.Empty;
    private bool _loading;
    private TicketDetailViewModel? _model;

    [Inject]
    private TicketDetailPresenter Presenter { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    /// <summary>The ticket number from the route, for example <c>ORB-42</c>.</summary>
    [Parameter]
    public string Number { get; set; } = string.Empty;

    private sealed record GoneMessage(string Heading, string Body);

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    protected override async Task OnParametersSetAsync()
    {
        if (Number == _loadedNumber)
        {
            return;
        }

        _loadedNumber = Number;
        await LoadAsync();
    }

    /// <summary>The full load: a skeleton while it runs. Used for the first load and the Retry after a failed first load.</summary>
    private async Task LoadAsync()
    {
        _loading = true;
        _gone = null;
        _model = null;
        _error = string.Empty;
        await LoadCoreAsync(silent: false);
    }

    /// <summary>The refresh after a write or a conflict: the model on screen stays until the new one arrives.</summary>
    internal async Task RefreshAsync()
    {
        await LoadCoreAsync(silent: true);
        StateHasChanged();
    }

    private async Task LoadCoreAsync(bool silent)
    {
        _load?.Cancel();
        _load?.Dispose();
        var cts = _load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

        Result<TicketDetailViewModel> result;
        try
        {
            result = await Presenter.LoadAsync(Number, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        _loading = false;
        if (result.IsSuccess)
        {
            _model = result.Value;
            _error = string.Empty;
            return;
        }

        var error = result.Errors[0];
        if (error.Kind == ResultErrorKind.NotFound)
        {
            _model = null;
            _gone = silent
                ? new GoneMessage(TicketCopy.GoneHeading, TicketCopy.GoneBody)
                : new GoneMessage(TicketCopy.NotFoundHeading, TicketCopy.NotFoundBody);
            return;
        }

        // A failed refresh keeps the ticket on screen with an inline alert; a failed first load shows the alert alone.
        _error = $"{TicketCopy.LoadFailed} {error.Message}";
    }

    /// <summary>Replaces the status fields and RowVersion from a write's response. Used by the composer, the sidebar and the actions.</summary>
    internal void ApplyState(TicketStateDto state)
    {
        if (_model is not null)
        {
            _model = _model.WithState(state);
            StateHasChanged();
        }
    }

    private Task OnShortcutAsync(ShortcutAction action)
    {
        if (action == ShortcutAction.Escape)
        {
            return InvokeAsync(() => Navigation.NavigateTo(QueuePath));
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Shortcuts.Pressed -= OnShortcutAsync;
        _lifetime.Cancel();
        _load?.Dispose();
        _lifetime.Dispose();
    }
}
```

4. The styles:

`src/TechStrap.Admin/Styles/_ticket.scss`

```scss
// The ticket screen (PHASE-07a): the numbered call-log header, the two regions, the timeline, the side panel and the conflict banner.
// Two regions: the conversation is dominant; the side panel moves above it on narrow screens.

.ts-ticket {
  padding: 12px 16px;
}

.ts-ticket-layout {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 280px;
  gap: 16px;
  align-items: start;
}

// The paper-form header: numbered field boxes (decoration on this header only).
.ts-callform {
  display: flex;
  flex-wrap: wrap;
  gap: 0;
  margin: 0 0 8px;
  border: 1px solid var(--rule-strong);
}

.ts-callform-field {
  flex: 1 1 140px;
  padding: 4px 8px;
  border-right: 1px solid var(--rule-strong);

  dt {
    font: 500 .625rem var(--ts-font-mono);
    letter-spacing: .05em;
    text-transform: uppercase;
    color: var(--ink-2);
  }

  dd {
    margin: 0;
    font-size: .8125rem;
  }
}

.ts-field-no {
  display: inline-block;
  min-width: 14px;
  padding: 0 3px;
  margin-right: 2px;
  color: var(--paper);
  background: var(--ink);
}

.ts-callform-problem {
  display: flex;
  gap: 8px;
  align-items: baseline;
}

.ts-ticket-number {
  font: 600 .875rem var(--ts-font-mono);
  user-select: all;
}

.ts-ticket-subject {
  margin: 0;
  font: 600 1.25rem/1.3 var(--ts-font-mono);
}

.ts-ticket-badges {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin: 8px 0;
}

.ts-followup {
  margin: 0 0 8px;
  font-size: .8125rem;
}

.ts-timeline {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.ts-timeline-event {
  padding: 2px 0 2px 12px;
  font: 400 .75rem var(--ts-font-mono);
  color: var(--ink-2);
  border-left: 2px solid var(--rule-strong);
}

.ts-event-text {
  color: var(--ink);
}

.ts-attachments {
  margin: 8px 0 0;
  padding: 0;
  list-style: none;
  font-size: .8125rem;
}

.ts-attachment-meta,
.ts-articles {
  color: var(--ink-2);
}

.ts-closed-note {
  margin: 12px 0 0;
  padding: 8px 12px;
  background: var(--head);
  border: 1px solid var(--rule-strong);
}

.ts-side {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;

  h2 {
    margin: 0 0 4px;
    font: 600 .6875rem var(--ts-font-mono);
    letter-spacing: .05em;
    text-transform: uppercase;
    color: var(--ink-2);
  }

  dl {
    margin: 0;
  }

  dt {
    font: 500 .6875rem var(--ts-font-mono);
    color: var(--ink-2);
  }

  dd {
    margin: 0 0 6px;
  }
}

.ts-sidebar {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.ts-control {
  display: flex;
  flex-direction: column;
  gap: 2px;

  label,
  .ts-control-label {
    font: 500 .6875rem var(--ts-font-mono);
    letter-spacing: .04em;
    text-transform: uppercase;
    color: var(--ink-2);
  }
}

.ts-saving {
  font: 400 .6875rem var(--ts-font-mono);
  color: var(--ink-2);
}

.ts-control-error {
  margin: 2px 0 0;
  padding: 2px 6px;
  font-size: .75rem;
  border: 2px solid var(--st-spam);
}

.ts-tagpicker-list {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin: 0 0 4px;
  padding: 0;
  list-style: none;
}

// Untrusted metadata: the word "Untrusted" and a dashed edge, never colour alone.
.ts-metadata {
  padding: 6px 8px;
  border: 1px solid var(--rule-strong);
}

.ts-metadata--untrusted {
  border-style: dashed;
}

.ts-metadata-trust {
  margin: 0 0 4px;
  font: 500 .6875rem var(--ts-font-mono);
}

.ts-conflict {
  margin: 8px 0;
  padding: 8px 12px;
  background: var(--sheet);
  border: 2px solid var(--st-pending);

  p {
    margin: 0 0 6px;
  }
}

.ts-conflict--reloaded {
  border-color: var(--rule-strong);
}

.ts-actions {
  position: relative;
  display: inline-block;
  margin-bottom: 8px;
}

.ts-menu {
  position: absolute;
  z-index: 5;
  min-width: 14rem;
  margin: 2px 0 0;
  padding: 4px 0;
  list-style: none;
  background: var(--sheet);
  border: 2px solid var(--ink);
  box-shadow: 3px 3px 0 var(--shadow);

  &[hidden] {
    display: none;
  }
}

.ts-menu-item {
  display: block;
  width: 100%;
  padding: 6px 12px;
  font: 500 .75rem var(--ts-font-mono);
  color: var(--ink);
  text-align: left;
  background: none;
  border: 0;

  &:hover {
    background: var(--hover);
  }
}

.ts-menu-item--danger {
  color: var(--st-spam);
  font-weight: 700;
}

@media (max-width: 1100px) {
  .ts-ticket-layout {
    grid-template-columns: minmax(0, 1fr);
  }

  .ts-side {
    order: -1;
  }
}
```

- [ ] **Step 4: Run the tests**

Run the filter from Step 2, then `dotnet test --project tests/TechStrap.Admin.Tests -c Release`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git diff --cached --stat   # after git add, before committing
git add src tests
git commit -m "feat(admin): ticket detail with one timeline, requester, metadata and read-only closed state" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---
### Task 10: Reply composer

**Review Focus pin (3):** a failed or conflicting reply must lose neither the typed text nor the picked files, and a write must never go out with a stale RowVersion. Pinned here by `ReplyComposerTests.Draft_and_files_survive_a_conflict` (and the retry that follows it), and in Task 11 by the sidebar's conflict test.

**Files:**
- Create: `src/TechStrap.Admin/Features/Tickets/ReplyComposerModel.cs`, `ReplyComposer.razor` and `.razor.cs`
- Create: `src/TechStrap.Admin/Styles/_composer.scss`; modify `src/TechStrap.Admin/Styles/app.scss`
- Modify: `src/TechStrap.Admin/Features/Tickets/TicketsServiceCollectionExtensions.cs`, `TicketDetailPage.razor`, `TicketDetailPage.razor.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/ReplyComposerTests.cs`, `tests/TechStrap.Admin.Tests/ComposerStyleTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/TicketDetailPageTests.cs`, `tests/TechStrap.Admin.Tests/TintStyleTests.cs`

**Interfaces:**
- Consumes: `ITicketsClient.ReplyAsync(Guid ticketId, AddAgentReplyRequest, IReadOnlyList<ReplyAttachment>, ct)` and `AddNoteAsync(Guid, AddInternalNoteRequest, ct)`, both returning `Result<AgentMessageResponse>`; `ReplyAttachment(string FileName, string ContentType, Func<Stream> OpenRead)` (Task 6); `ApiErrorCodes.ConcurrencyConflict`, `TicketClosed`; `ShortcutService`, `StatusMessageService`, `Kbd` (Task 7); `TicketDetailPage.ApplyState` and `RefreshAsync` (Task 9); Contracts `IntakeLimits` (`MaxFileBytes`, `MaxFiles`, `AllowedExtensions`).
- Produces:

```csharp
// TechStrap.Admin.Features.Tickets
public enum ComposerMode { PublicReply, InternalNote }
public sealed class ComposerDraft { ComposerMode Mode; string PublicText; string NoteText; string StatusAfter; List<IBrowserFile> Files; }
public sealed class DraftStore { ComposerDraft Get(Guid ticketId); }                 // scoped: one per circuit
public static class ReplyComposerCopy { ... }                                       // the UX strings, word for word
public sealed partial class ReplyComposer : IDisposable
{   // TicketId, TicketNumber, RequesterEmail, RowVersion (uint), OnSent EventCallback<AgentMessageResponse>, OnConflict, OnGone
}
```

**Rules:**
1. **The segmented control decides the mode.** "Public reply `r`" and "Internal note `n`" with `aria-pressed`; typing never converts, switching never moves text; each mode has its own draft per ticket in `DraftStore`. The default is always Public (the composer never defaults to Internal, so the UX rule "never default to Internal when a customer message awaits a reply" holds trivially); the mode does not change after a send or a conflict reload.
2. **Exact copy** (UX-BRIEF-admin): public shows "To: {email}" and "PUBLIC: this will be emailed to the customer." with "Send reply" (Ctrl+Enter) and "Send and solve", and a status-on-send choice (Pending by default, or leave unchanged). The note shows a `role="status"` bar reading exactly **"INTERNAL: the customer will NOT see this note."**, "Visible to agents only", the placeholder "Note for the team only" and "Add internal note", with no status choice and no file picker (the note request has no attachments). Status bar after a send: "Reply sent on ORB-42" or "Internal note added to ORB-42".
3. **Draft and files survive any failure.** Only an accepted send clears anything, and only the mode that was sent. The picked `IBrowserFile`s stay in the draft; each attempt builds fresh `ReplyAttachment`s whose `OpenRead` calls `OpenReadStream(IntakeLimits.MaxFileBytes)`, because a stream cannot be read twice. A conflict raises `OnConflict` (the banner is Task 11) and keeps everything; the page then passes the reloaded `RowVersion` and the same draft goes out against it.
4. **Sending is single-flight** (`_sending`): the buttons, the box and the file picker are disabled and a second click or shortcut is ignored.
5. **Files are checked in the component**, because bUnit and `InputFile` do not do it for us: at most `IntakeLimits.MaxFiles`, at most `MaxFileBytes` each, only `AllowedExtensions` (the picker's `accept` lists the same). The server still validates; this is a courtesy that keeps the failure local.
6. **Unsent text warns on leaving.** `NavigationLock ConfirmExternalNavigation` is on while either draft has text (reload, close, external link). Internal navigation needs no prompt: the draft is kept for the circuit. A lost circuit loses the draft (UX open question 6); the warning is the 07a answer, a session-storage backup is not built.
7. **Keyboard.** `r` and `n` switch the tab and focus the box; Ctrl or Cmd plus Enter sends in the current mode but only from inside the composer (`data-shortcut-scope="composer"`, set by the script from the focused element). The text box binds with `oninput` (a round trip per keystroke, accepted for a handful of agents); the shortcut path avoids any per-keystroke key handler.
8. **The tint tokens.** The composer paints with the canary and pink tokens, which `TintStyleTests` allows only in a listed file: `_composer.scss` joins `_tinted-entry.scss`. The status-bar message and the avatar deliberately use `--head`, so they need no exception.
9. **Linked KB articles stay empty** (`LinkedArticleIds = []`, the picker is PHASE-08). The request's `RowVersion` is sent for replies and notes although the API treats it as optional there; whether the API 409s a stale reply is UNVERIFIED, and the composer handles a conflict either way.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/Components/ReplyComposerTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class ReplyComposerTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private int _sent;
    private int _conflicts;
    private int _gone;
    private AgentMessageResponse? _lastSent;

    public ReplyComposerTests()
    {
        Services.AddSingleton(_tickets);
        Services.AddScoped<DraftStore>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Accepted(MessageVisibilities.Public));
        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Accepted(MessageVisibilities.Internal));
    }

    private static Result<AgentMessageResponse> Accepted(string visibility) =>
        TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent, visibility), TestData.State(rowVersion: 8)));

    private IRenderedComponent<ReplyComposer> RenderComposer(uint rowVersion = 7, Guid? ticketId = null) =>
        Render<ReplyComposer>(p => p
            .Add(c => c.TicketId, ticketId ?? TestData.TicketId)
            .Add(c => c.TicketNumber, "ORB-42")
            .Add(c => c.RequesterEmail, "ada@example.com")
            .Add(c => c.RowVersion, rowVersion)
            .Add(c => c.OnSent, response => { _sent++; _lastSent = response; })
            .Add(c => c.OnConflict, () => _conflicts++)
            .Add(c => c.OnGone, () => _gone++));

    private static void Pick(IRenderedComponent<ReplyComposer> cut, string name, int bytes = 4, string type = "image/png") =>
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[bytes], name, null, type));

    private IEnumerable<AddAgentReplyRequest> ReplyRequests() =>
        _tickets.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync)).Select(c => (AddAgentReplyRequest)c.GetArguments()[1]!);

    private IEnumerable<AddInternalNoteRequest> NoteRequests() =>
        _tickets.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITicketsClient.AddNoteAsync)).Select(c => (AddInternalNoteRequest)c.GetArguments()[1]!);

    [Fact]
    public void A_public_reply_is_the_default_with_the_audience_and_the_plain_warning()
    {
        var cut = RenderComposer();

        var tabs = cut.FindAll(".ts-composer-modes button");
        tabs[0].GetAttribute("aria-pressed").ShouldBe("true");
        tabs[1].GetAttribute("aria-pressed").ShouldBe("false");
        tabs[0].TextContent.ShouldContain("Public reply");
        tabs[1].TextContent.ShouldContain("Internal note");
        cut.Find("p.ts-composer-audience").TextContent.ShouldBe("To: ada@example.com");
        cut.Find("p.ts-composer-warning").TextContent.ShouldBe("PUBLIC: this will be emailed to the customer.");
        cut.FindAll("p.ts-composer-warning--internal").ShouldBeEmpty();
        cut.Find("section.ts-composer").ClassList.ShouldContain("ts-composer--public");
        cut.Find("section.ts-composer").GetAttribute("data-shortcut-scope").ShouldBe("composer");
        cut.Find("textarea").GetAttribute("placeholder").ShouldBe("Write your reply");
        cut.FindAll(".ts-composer-actions button").Select(b => b.TextContent.Trim()).ShouldBe(["Send reply CtrlEnter", "Send and solve"]);
        cut.Find("select").InnerHtml.ShouldContain("Set to Pending");
    }

    [Fact]
    public void The_internal_note_mode_has_the_exact_warning_a_status_role_and_none_of_the_public_controls()
    {
        var cut = RenderComposer();

        cut.FindAll(".ts-composer-modes button")[1].Click();

        cut.FindAll(".ts-composer-modes button")[1].GetAttribute("aria-pressed").ShouldBe("true");
        cut.FindAll(".ts-composer-modes button")[0].GetAttribute("aria-pressed").ShouldBe("false");
        var warning = cut.Find("p.ts-composer-warning--internal");
        warning.TextContent.ShouldBe("INTERNAL: the customer will NOT see this note.");
        warning.GetAttribute("role").ShouldBe("status");
        cut.Find("p.ts-composer-audience").TextContent.ShouldBe("Visible to agents only");
        cut.Find("textarea").GetAttribute("placeholder").ShouldBe("Note for the team only");
        cut.Find(".ts-composer-actions button").TextContent.ShouldContain("Add internal note");
        cut.Find("section.ts-composer").ClassList.ShouldContain("ts-composer--note");
        cut.FindAll("select, input[type=file], .ts-composer-files").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("Send and solve");
        cut.Markup.ShouldNotContain("PUBLIC:");
    }

    [Fact]
    public void Each_mode_keeps_its_own_text_and_switching_never_moves_or_converts_it()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Public words");

        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
        cut.Find("textarea").Input("Team words");

        cut.FindAll(".ts-composer-modes button")[0].Click();
        cut.Find("textarea").GetAttribute("value").ShouldBe("Public words");

        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").GetAttribute("value").ShouldBe("Team words");
    }

    [Fact]
    public async Task Drafts_survive_the_screen_being_rebuilt_and_are_separate_per_ticket()
    {
        var first = RenderComposer();
        first.Find("textarea").Input("Half-written reply");
        first.FindAll(".ts-composer-modes button")[1].Click();
        first.Find("textarea").Input("Half-written note");
        await DisposeComponentsAsync();

        var again = RenderComposer();
        again.FindAll(".ts-composer-modes button")[1].GetAttribute("aria-pressed").ShouldBe("true");
        again.Find("textarea").GetAttribute("value").ShouldBe("Half-written note");
        again.FindAll(".ts-composer-modes button")[0].Click();
        again.Find("textarea").GetAttribute("value").ShouldBe("Half-written reply");

        var other = RenderComposer(ticketId: Guid.NewGuid());
        other.Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
        other.FindAll(".ts-composer-modes button")[0].GetAttribute("aria-pressed").ShouldBe("true");
    }

    [Fact]
    public void A_public_reply_calls_the_reply_method_with_the_text_the_status_and_the_current_row_version()
    {
        var cut = RenderComposer(rowVersion: 7);
        cut.Find("textarea").Input("Try resetting your password.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        var request = ReplyRequests().Single();
        (request.Body, request.StatusAfter, request.RowVersion).ShouldBe(("Try resetting your password.", "Pending", (uint?)7));
        request.LinkedArticleIds.ShouldBeEmpty();
        NoteRequests().ShouldBeEmpty();
    }

    [Fact]
    public void Send_and_solve_asks_for_Solved_and_the_leave_unchanged_choice_asks_for_no_status()
    {
        var solve = RenderComposer();
        solve.Find("textarea").Input("Fixed in 2.3.2.");
        solve.FindAll(".ts-composer-actions button")[1].Click();
        ReplyRequests().Single().StatusAfter.ShouldBe("Solved");

        _tickets.ClearReceivedCalls();
        var keep = RenderComposer(ticketId: Guid.NewGuid());
        keep.Find("textarea").Input("Looking into it.");
        keep.Find("select").Change(ReplyComposerCopy.LeaveUnchangedValue);
        keep.FindAll(".ts-composer-actions button")[0].Click();
        ReplyRequests().Single().StatusAfter.ShouldBeNull();
    }

    [Fact]
    public void An_internal_note_calls_the_note_method_with_the_current_row_version_and_never_the_reply_method()
    {
        var cut = RenderComposer(rowVersion: 7);
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Customer is on the legacy plan.");

        cut.Find(".ts-composer-actions button").Click();

        var request = NoteRequests().Single();
        (request.Body, request.RowVersion).ShouldBe(("Customer is on the legacy plan.", (uint?)7));
        ReplyRequests().ShouldBeEmpty();
    }

    [Fact]
    public void The_reply_carries_the_picked_files_and_the_page_s_token()
    {
        var cut = RenderComposer();
        Pick(cut, "screenshot.png");
        cut.Find("textarea").Input("See attached.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        var call = _tickets.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync));
        ((IReadOnlyList<ReplyAttachment>)call.GetArguments()[2]!).Select(f => f.FileName).ShouldBe(["screenshot.png"]);
        ((CancellationToken)call.GetArguments()[3]!).CanBeCanceled.ShouldBeTrue();
    }

    [Fact]
    public void Double_submit_is_prevented_and_the_controls_are_disabled_while_a_request_is_in_flight()
    {
        var gate = new TaskCompletionSource<Result<AgentMessageResponse>>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderComposer();
        cut.Find("textarea").Input("Once only.");

        cut.FindAll(".ts-composer-actions button")[0].Click();
        cut.FindAll(".ts-composer-actions button").ShouldAllBe(b => b.HasAttribute("disabled"));
        cut.Find("textarea").HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll(".ts-composer-actions button")[0].Click();

        ReplyRequests().Count().ShouldBe(1);

        gate.SetResult(Accepted(MessageVisibilities.Public));
        cut.WaitForAssertion(() => cut.FindAll(".ts-composer-actions button").ShouldAllBe(b => !b.HasAttribute("disabled")));
        ReplyRequests().Count().ShouldBe(1);
    }

    [Fact]
    public void An_accepted_reply_clears_the_reply_and_its_files_only_and_reports_once()
    {
        var cut = RenderComposer();
        Pick(cut, "a.png");
        cut.Find("textarea").Input("Reply text");
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Note text");
        cut.FindAll(".ts-composer-modes button")[0].Click();

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
        cut.FindAll(".ts-file-list").ShouldBeEmpty();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
        _sent.ShouldBe(1);
        _lastSent!.Ticket.RowVersion.ShouldBe(8u);
        StatusMessages.Current.ShouldBe("Reply sent on ORB-42");
        cut.FindAll(".ts-composer-modes button")[0].GetAttribute("aria-pressed").ShouldBe("true");
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").GetAttribute("value").ShouldBe("Note text");
    }

    [Fact]
    public void An_accepted_note_clears_the_note_says_so_in_the_status_bar_and_stays_in_note_mode()
    {
        var cut = RenderComposer();
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Escalated to billing.");

        cut.Find(".ts-composer-actions button").Click();

        cut.Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
        StatusMessages.Current.ShouldBe("Internal note added to ORB-42");
        cut.FindAll(".ts-composer-modes button")[1].GetAttribute("aria-pressed").ShouldBe("true");
        _sent.ShouldBe(1);
    }

    [Fact]
    public void A_failed_send_keeps_the_text_and_the_files_shows_why_and_a_retry_sends_the_same_files_again()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>("boom", "The API is unavailable."), Accepted(MessageVisibilities.Public));
        var cut = RenderComposer();
        Pick(cut, "a.png");
        cut.Find("textarea").Input("Reply text");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.Find("[role=alert]").TextContent.ShouldBe("Couldn't send the reply. Your text and files are kept. The API is unavailable.");
        cut.Find("textarea").GetAttribute("value").ShouldBe("Reply text");
        cut.Find(".ts-file-name").TextContent.ShouldBe("a.png");
        _sent.ShouldBe(0);

        cut.FindAll(".ts-composer-actions button")[0].Click();

        _sent.ShouldBe(1);
        var files = _tickets.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync))
            .Select(c => ((IReadOnlyList<ReplyAttachment>)c.GetArguments()[2]!).Single()).ToList();
        files.Count.ShouldBe(2);
        files.Select(f => f.FileName).ShouldBe(["a.png", "a.png"]);
        using var second = files[1].OpenRead();
        second.Length.ShouldBe(4, "the retry reopens the same picked file");
    }

    [Fact]
    public void Draft_and_files_survive_a_conflict()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict), Accepted(MessageVisibilities.Public));
        var cut = RenderComposer(rowVersion: 7);
        Pick(cut, "log.txt", type: "text/plain");
        cut.Find("textarea").Input("A long reply that must not be lost.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        _conflicts.ShouldBe(1);
        _sent.ShouldBe(0);
        cut.Find("[role=alert]").TextContent.ShouldBe("This ticket changed since you opened it. Your text and files are kept; reload the ticket, then send again.");
        cut.Find("textarea").GetAttribute("value").ShouldBe("A long reply that must not be lost.");
        cut.Find(".ts-file-name").TextContent.ShouldBe("log.txt");
        cut.FindAll(".ts-composer-modes button")[0].GetAttribute("aria-pressed").ShouldBe("true");

        // The page reloads and hands the composer the new RowVersion; the retry sends the SAME draft against it.
        cut.Render(p => p.Add(c => c.RowVersion, 9u));
        cut.FindAll(".ts-composer-actions button")[0].Click();

        ReplyRequests().Select(r => r.RowVersion).ShouldBe([7u, 9u]);
        ReplyRequests().Select(r => r.Body).Distinct().ShouldBe(["A long reply that must not be lost."]);
        _sent.ShouldBe(1);
        cut.Find("textarea").GetAttribute("value").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void A_note_conflict_also_keeps_the_note_and_raises_the_conflict_once()
    {
        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderComposer();
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Do not lose me.");

        cut.Find(".ts-composer-actions button").Click();

        _conflicts.ShouldBe(1);
        cut.Find("textarea").GetAttribute("value").ShouldBe("Do not lose me.");
        cut.FindAll(".ts-composer-modes button")[1].GetAttribute("aria-pressed").ShouldBe("true");
    }

    [Fact]
    public void A_closed_ticket_shows_the_servers_message_and_a_deleted_ticket_raises_gone_and_both_keep_the_text()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(
                TestData.Fail<AgentMessageResponse>(ApiErrorCodes.TicketClosed, "This ticket is closed.", ResultErrorKind.Conflict),
                TestData.Fail<AgentMessageResponse>(ApiErrorCodes.TicketNotFound, "Gone.", ResultErrorKind.NotFound));
        var cut = RenderComposer();
        cut.Find("textarea").Input("Keep this.");

        cut.FindAll(".ts-composer-actions button")[0].Click();
        cut.Find("[role=alert]").TextContent.ShouldBe("This ticket is closed.");
        _conflicts.ShouldBe(0);

        cut.FindAll(".ts-composer-actions button")[0].Click();
        _gone.ShouldBe(1);
        cut.Find("textarea").GetAttribute("value").ShouldBe("Keep this.");
    }

    [Fact]
    public void An_empty_reply_or_note_is_refused_before_any_call()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("   ");

        cut.FindAll(".ts-composer-actions button")[0].Click();
        cut.Find("[role=alert]").TextContent.ShouldBe("Write a reply before sending.");

        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find(".ts-composer-actions button").Click();
        cut.Find("[role=alert]").TextContent.ShouldBe("Write a note before adding it.");

        _tickets.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void Picked_files_are_listed_with_their_size_and_can_be_removed()
    {
        var cut = RenderComposer();
        Pick(cut, "a.png", 2048);
        Pick(cut, "b.pdf", 10, "application/pdf");

        cut.FindAll(".ts-file-name").Select(n => n.TextContent).ShouldBe(["a.png", "b.pdf"]);
        cut.FindAll(".ts-file-size").First().TextContent.ShouldBe("(2 KB)");

        cut.Find("button[aria-label='Remove a.png']").Click();

        cut.FindAll(".ts-file-name").Select(n => n.TextContent).ShouldBe(["b.pdf"]);
    }

    [Fact]
    public void A_file_that_is_too_large_or_the_wrong_type_or_over_the_count_is_refused_with_a_reason()
    {
        var cut = RenderComposer();

        Pick(cut, "huge.png", checked((int)IntakeLimits.MaxFileBytes + 1));
        cut.Find("[role=alert]").TextContent.ShouldBe("huge.png is larger than 10 MB.");
        cut.FindAll(".ts-file-list").ShouldBeEmpty();

        Pick(cut, "run.exe");
        cut.Find("[role=alert]").TextContent.ShouldBe("run.exe: this file type isn't allowed.");

        for (var i = 0; i < IntakeLimits.MaxFiles; i++)
        {
            Pick(cut, $"f{i}.png");
        }

        cut.FindAll(".ts-file-name").Count.ShouldBe(IntakeLimits.MaxFiles);
        Pick(cut, "one-too-many.png");
        cut.Find("[role=alert]").TextContent.ShouldBe("You can attach up to 5 files.");
        cut.FindAll(".ts-file-name").Count.ShouldBe(IntakeLimits.MaxFiles);
    }

    [Fact]
    public void The_file_picker_offers_only_the_allowed_extensions()
    {
        var cut = RenderComposer();

        cut.Find("input[type=file]").GetAttribute("accept").ShouldBe(".png,.jpg,.jpeg,.gif,.webp,.pdf,.txt,.log,.csv,.zip");
        cut.Find("input[type=file]").HasAttribute("multiple").ShouldBeTrue();
    }

    [Fact]
    public async Task R_and_n_switch_the_tab_and_focus_the_box_and_the_mode_choice_is_kept()
    {
        var cut = RenderComposer();
        // bUnit blanks blazor:elementReference on the next render, so read the box's reference id before the key press and compare ids.
        var boxId = cut.Find("textarea").GetAttribute("blazor:elementReference");

        await PressAsync("n");
        cut.FindAll(".ts-composer-modes button")[1].GetAttribute("aria-pressed").ShouldBe("true");
        ((ElementReference)JSInterop.VerifyFocusAsyncInvoke().Arguments[0]!).Id.ShouldBe(boxId);

        await PressAsync("r");
        cut.FindAll(".ts-composer-modes button")[0].GetAttribute("aria-pressed").ShouldBe("true");
        JSInterop.VerifyFocusAsyncInvoke(2);
    }

    [Fact]
    public async Task Ctrl_enter_inside_the_composer_sends_in_the_current_mode_and_outside_it_does_nothing()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Sent with the keyboard.");

        await PressAsync("Enter", ctrl: true, typing: true, scope: null);
        _tickets.ReceivedCalls().ShouldBeEmpty();

        await PressAsync("Enter", ctrl: true, typing: true, scope: "composer");

        ReplyRequests().Select(r => r.Body).ShouldBe(["Sent with the keyboard."]);
        cut.WaitForAssertion(() => _sent.ShouldBe(1));
    }

    [Fact]
    public async Task Ctrl_enter_in_note_mode_adds_the_note()
    {
        var cut = RenderComposer();
        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("Note by keyboard.");

        await PressAsync("Enter", ctrl: true, typing: true, scope: "composer");

        NoteRequests().Select(r => r.Body).ShouldBe(["Note by keyboard."]);
        ReplyRequests().ShouldBeEmpty();
    }

    [Fact]
    public void Leaving_the_page_asks_for_confirmation_only_while_there_is_unsent_text()
    {
        var cut = RenderComposer();
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeFalse();

        cut.Find("textarea").Input("Unsent");

        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();

        cut.FindAll(".ts-composer-actions button")[0].Click();

        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeFalse();
    }

    [Fact]
    public async Task A_disposed_composer_no_longer_answers_shortcuts()
    {
        var cut = RenderComposer();
        cut.Find("textarea").Input("Text");
        await DisposeComponentsAsync();

        await PressAsync("Enter", ctrl: true, typing: true, scope: "composer");

        _tickets.ReceivedCalls().ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/TicketDetailPageTests.cs`: add these tests at the end of the class (they exercise the composer through the page):

`tests/TechStrap.Admin.Tests/Components/TicketDetailPageTests.cs` changes like this:

```diff
@@ -309,4 +309,74 @@
 
         _tickets.Received(1).GetAsync("ORB-42", Arg.Is<CancellationToken>(t => t.CanBeCanceled));
     }
+
+    [Fact]
+    public void An_open_ticket_gets_the_composer_beneath_the_timeline_and_a_closed_one_does_not()
+    {
+        var open = RenderTicket();
+
+        open.Find("section.ts-conversation ol.ts-timeline").ShouldNotBeNull();
+        open.Find("section.ts-conversation section.ts-composer").ShouldNotBeNull();
+        open.Find("section.ts-composer p.ts-composer-audience").TextContent.ShouldBe("To: ada@example.com");
+
+        ShowTicket(TestData.Detail(status: TicketStatuses.Closed));
+
+        RenderTicket().FindAll("section.ts-composer").ShouldBeEmpty();
+    }
+
+    [Fact]
+    public void A_sent_reply_takes_the_new_row_version_from_the_response_and_reloads_the_timeline()
+    {
+        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
+            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent), TestData.State(TicketStatuses.Pending, rowVersion: 8))));
+        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
+            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Internal), TestData.State(TicketStatuses.Pending, rowVersion: 9))));
+        var cut = RenderTicket();
+        cut.Find("textarea").Input("On it.");
+        ShowTicket(TestData.Detail(status: TicketStatuses.Pending, rowVersion: 8, messages:
+            [TestData.Message(), TestData.Message(MessageAuthorTypes.Agent, authorName: "Sam Ortiz", bodyHtml: "<p>On it.</p>")]));
+
+        cut.FindAll(".ts-composer-actions button")[0].Click();
+
+        cut.WaitForAssertion(() => cut.FindAll("ol.ts-timeline > li.ts-timeline-message").Count.ShouldBe(2));
+        cut.Find(".ts-ticket-badges .ts-stamp").TextContent.ShouldBe("Pending");
+
+        cut.FindAll(".ts-composer-modes button")[1].Click();
+        cut.Find("textarea").Input("Follow-up note.");
+        cut.Find(".ts-composer-actions button").Click();
+
+        _tickets.Received(1).AddNoteAsync(TestData.TicketId, Arg.Is<AddInternalNoteRequest>(r => r.RowVersion == 8u), Arg.Any<CancellationToken>());
+    }
+
+    [Fact]
+    public void A_draft_in_the_other_tab_survives_the_reload_that_follows_a_send()
+    {
+        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
+            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent), TestData.State(rowVersion: 8))));
+        var cut = RenderTicket();
+        cut.FindAll(".ts-composer-modes button")[1].Click();
+        cut.Find("textarea").Input("Half-written note");
+        cut.FindAll(".ts-composer-modes button")[0].Click();
+        cut.Find("textarea").Input("Reply");
+
+        cut.FindAll(".ts-composer-actions button")[0].Click();
+
+        cut.WaitForAssertion(() => _tickets.Received(2).GetAsync("ORB-42", Arg.Any<CancellationToken>()));
+        cut.FindAll(".ts-composer-modes button")[1].Click();
+        cut.Find("textarea").GetAttribute("value").ShouldBe("Half-written note");
+    }
+
+    [Fact]
+    public void A_reply_to_a_ticket_deleted_meanwhile_ends_on_this_ticket_no_longer_exists()
+    {
+        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
+            .Returns(TestData.Fail<AgentMessageResponse>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));
+        var cut = RenderTicket();
+        cut.Find("textarea").Input("Too late.");
+        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));
+
+        cut.FindAll(".ts-composer-actions button")[0].Click();
+
+        cut.WaitForAssertion(() => cut.Find("section.ts-gone h1").TextContent.ShouldBe("This ticket no longer exists"));
+    }
 }
```

`tests/TechStrap.Admin.Tests/TintStyleTests.cs`: let the composer paint with the tints:

```diff
-    // Styles that may paint with the carbon tints. PHASE-07 adds the reply composer, the avatar fill and the status-bar message
-    // here when those components land; the list is deliberately short so a new use is a conscious decision.
-    private static readonly string[] TintFiles = ["_tinted-entry.scss"];
+    // Styles that may paint with the carbon tints: the timeline entries and, since PHASE-07a, the reply composer (UX-BRIEF-admin,
+    // "Composer distinction"). The avatar and the status-bar message use --head and need no entry. The list is deliberately short
+    // so a new use is a conscious decision.
+    private static readonly string[] TintFiles = ["_tinted-entry.scss", "_composer.scss"];
```

`tests/TechStrap.Admin.Tests/ComposerStyleTests.cs` (reads the compiled CSS like `TintStyleTests`; it needs a built Admin, so it runs with the project, and was **not run** in the prototype: check the exact compressed values it compares against the first run):

```csharp
using TechStrap.Contracts.Branding;
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The composer wears the same tint code as the timeline: canary with a solid edge for a public reply, pink with a dashed edge and a notched corner for a note.</summary>
public sealed class ComposerStyleTests
{
    private const string LightScope = ":root,[data-bs-theme=light]";
    private const string DarkScope = "[data-bs-theme=dark]";
    private const double Aa = 4.5;

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void A_public_reply_is_canary_with_a_solid_edge()
    {
        var reply = Css.Declarations(".ts-composer--public");

        reply["background"].ShouldBe("var(--canary)");
        reply["border-color"].ShouldBe("var(--canary-edge)");
    }

    [Fact]
    public void An_internal_note_is_pink_dashed_notched_and_flat_like_its_timeline_entry()
    {
        var note = Css.Declarations(".ts-composer--note");

        note["background"].ShouldBe("var(--pink)");
        note["border"].ShouldBe("2px dashed var(--pink-edge)");
        note["clip-path"].ShouldBe("polygon(0 0, calc(100% - 18px) 0, 100% 18px, 100% 100%, 0 100%)");
        note["box-shadow"].ShouldBe("none");
        Css.Declarations(".ts-composer-warning--internal")["color"].ShouldBe("var(--note-ink)");
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Composer_text_and_the_internal_warning_are_readable_in_both_themes(string theme)
    {
        var tokens = Css.Declarations(theme == "light" ? LightScope : DarkScope);
        var ink = Resolve(tokens, Css.Declarations(".ts-composer")["color"]);

        ProductAccent.ContrastRatio(ink, Resolve(tokens, Css.Declarations(".ts-composer--public")["background"])).ShouldBeGreaterThanOrEqualTo(Aa);
        ProductAccent.ContrastRatio(ink, Resolve(tokens, Css.Declarations(".ts-composer--note")["background"])).ShouldBeGreaterThanOrEqualTo(Aa);
        ProductAccent.ContrastRatio(
                Resolve(tokens, Css.Declarations(".ts-composer-warning--internal")["color"]),
                Resolve(tokens, Css.Declarations(".ts-composer--note")["background"]))
            .ShouldBeGreaterThanOrEqualTo(Aa);
    }

    private static string Resolve(IReadOnlyDictionary<string, string> tokens, string value) =>
        value.StartsWith("var(--", StringComparison.Ordinal) ? tokens[value[4..^1]] : value;
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "ReplyComposerTests|TicketDetailPageTests|ComposerStyleTests|TintStyleTests"`
Expected: a build failure (`ReplyComposer`, `DraftStore`, `ComposerMode` do not exist).

- [ ] **Step 3: Implement**

1. Register the draft store (`TicketsServiceCollectionExtensions.cs`) and add `@import "composer";` to `app.scss` after `@import "ticket";`:

`src/TechStrap.Admin/Features/Tickets/TicketsServiceCollectionExtensions.cs` changes like this:

```diff
@@ -6,6 +6,7 @@
     public static IServiceCollection AddTicketFeatures(this IServiceCollection services)
     {
         services.AddScoped<TicketDetailPresenter>();
+        services.AddScoped<DraftStore>();
         return services;
     }
 }
```

2. The model, the copy and the component:

`src/TechStrap.Admin/Features/Tickets/ReplyComposerModel.cs`

```csharp
using Microsoft.AspNetCore.Components.Forms;

namespace TechStrap.Admin.Features.Tickets;

public enum ComposerMode
{
    PublicReply,
    InternalNote,
}

/// <summary>What an agent has typed for one ticket: a separate text per mode, the chosen status-after, and the files picked for a public reply.</summary>
public sealed class ComposerDraft
{
    public ComposerMode Mode { get; set; } = ComposerMode.PublicReply;

    public string PublicText { get; set; } = string.Empty;

    public string NoteText { get; set; } = string.Empty;

    /// <summary>The status to apply on send: Pending by default, or empty for "leave unchanged". "Send and solve" does not use this.</summary>
    public string StatusAfter { get; set; } = ReplyComposerCopy.PendingValue;

    /// <summary>
    /// The files picked for the public reply. They stay here until the reply is accepted, because a stream cannot be re-read after a failed attempt:
    /// each attempt rebuilds the multipart body from these <see cref="IBrowserFile"/>s.
    /// </summary>
    public List<IBrowserFile> Files { get; } = [];
}

/// <summary>
/// Keeps each ticket's <see cref="ComposerDraft"/> for the life of the circuit, so leaving a ticket, a reload after a conflict, or the screen
/// being rebuilt never loses typed text (UX-BRIEF-admin, Composer). A lost circuit loses it: the leave-warning is the guard for that (UX open question 6).
/// </summary>
public sealed class DraftStore
{
    private readonly Dictionary<Guid, ComposerDraft> _drafts = [];

    public ComposerDraft Get(Guid ticketId)
    {
        if (!_drafts.TryGetValue(ticketId, out var draft))
        {
            draft = new ComposerDraft();
            _drafts[ticketId] = draft;
        }

        return draft;
    }
}

public static class ReplyComposerCopy
{
    public const string PendingValue = "Pending";
    public const string LeaveUnchangedValue = "";

    public const string PublicTab = "Public reply";
    public const string NoteTab = "Internal note";
    public const string PublicWarning = "PUBLIC: this will be emailed to the customer.";
    public const string InternalWarning = "INTERNAL: the customer will NOT see this note.";
    public const string InternalAudience = "Visible to agents only";
    public const string NotePlaceholder = "Note for the team only";
    public const string ReplyPlaceholder = "Write your reply";
    public const string SendReply = "Send reply";
    public const string SendAndSolve = "Send and solve";
    public const string AddNote = "Add internal note";
    public const string SetPending = "Set to Pending";
    public const string LeaveUnchanged = "Leave status unchanged";
    public const string AttachFiles = "Attach files";
    public const string EmptyReply = "Write a reply before sending.";
    public const string EmptyNote = "Write a note before adding it.";
    public const string ReplyFailed = "Couldn't send the reply. Your text and files are kept.";
    public const string NoteFailed = "Couldn't add the note. Your text is kept.";
    public const string Conflict = "This ticket changed since you opened it. Your text and files are kept; reload the ticket, then send again.";
    public const string TooManyFiles = "You can attach up to {0} files.";
    public const string FileTooLarge = "{0} is larger than {1}.";
    public const string FileTypeNotAllowed = "{0}: this file type isn't allowed.";

    public static string ReplySent(string number) => $"Reply sent on {number}";

    public static string NoteAdded(string number) => $"Internal note added to {number}";
}
```

`src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor`

```razor
<NavigationLock ConfirmExternalNavigation="@HasUnsentText" />
<section class="@SectionCss" aria-label="Reply composer" data-shortcut-scope="composer">
    <div class="ts-composer-modes" role="group" aria-label="Reply type">
        <button type="button" class="@ModeCss(ComposerMode.PublicReply)" aria-pressed="@PressedValue(ComposerMode.PublicReply)" @onclick="() => SetMode(ComposerMode.PublicReply)">
            @ReplyComposerCopy.PublicTab <Kbd>r</Kbd>
        </button>
        <button type="button" class="@ModeCss(ComposerMode.InternalNote)" aria-pressed="@PressedValue(ComposerMode.InternalNote)" @onclick="() => SetMode(ComposerMode.InternalNote)">
            @ReplyComposerCopy.NoteTab <Kbd>n</Kbd>
        </button>
    </div>

    @if (IsPublic)
    {
        <p class="ts-composer-audience">To: @RequesterEmail</p>
        <p class="ts-composer-warning">@ReplyComposerCopy.PublicWarning</p>
    }
    else
    {
        <p class="ts-composer-warning ts-composer-warning--internal" role="status">@ReplyComposerCopy.InternalWarning</p>
        <p class="ts-composer-audience">@ReplyComposerCopy.InternalAudience</p>
    }

    <label for="@TextId" class="visually-hidden">@(IsPublic ? ReplyComposerCopy.PublicTab : ReplyComposerCopy.NoteTab)</label>
    <textarea id="@TextId" @ref="_text" class="form-control" rows="6" disabled="@_sending"
              placeholder="@(IsPublic ? ReplyComposerCopy.ReplyPlaceholder : ReplyComposerCopy.NotePlaceholder)"
              @bind="Text" @bind:event="oninput"></textarea>

    @if (IsPublic)
    {
        <div class="ts-composer-files">
            <label for="@FilesId">@ReplyComposerCopy.AttachFiles</label>
            <InputFile id="@FilesId" OnChange="OnFilesPickedAsync" multiple accept="@Accept" disabled="@_sending" />
            @if (_draft.Files.Count > 0)
            {
                <ul class="ts-file-list">
                    @foreach (var file in _draft.Files)
                    {
                        <li>
                            <span class="ts-file-name">@file.Name</span> <span class="ts-file-size">(@TicketDisplay.FileSize(file.Size))</span>
                            <button type="button" class="btn btn-link" aria-label="@($"Remove {file.Name}")" disabled="@_sending" @onclick="() => RemoveFile(file)">Remove</button>
                        </li>
                    }
                </ul>
            }
            @foreach (var problem in _fileProblems)
            {
                <p class="ts-composer-error" role="alert">@problem</p>
            }
        </div>
        <div class="ts-composer-status">
            <label for="@StatusId">After sending</label>
            <select id="@StatusId" class="form-select" disabled="@_sending" @bind="StatusAfter">
                <option value="@ReplyComposerCopy.PendingValue">@ReplyComposerCopy.SetPending</option>
                <option value="@ReplyComposerCopy.LeaveUnchangedValue">@ReplyComposerCopy.LeaveUnchanged</option>
            </select>
        </div>
    }

    @if (_error is not null)
    {
        <p class="ts-composer-error" role="alert">@_error</p>
    }

    <div class="ts-composer-actions">
        @if (IsPublic)
        {
            <button type="button" class="btn btn-primary" disabled="@_sending" @onclick="() => SubmitAsync()">@ReplyComposerCopy.SendReply <Kbd>Ctrl</Kbd><Kbd>Enter</Kbd></button>
            <button type="button" class="btn btn-outline-secondary" disabled="@_sending" @onclick="() => SubmitAsync(TicketStatuses.Solved)">@ReplyComposerCopy.SendAndSolve</button>
        }
        else
        {
            <button type="button" class="btn btn-primary" disabled="@_sending" @onclick="() => SubmitAsync()">@ReplyComposerCopy.AddNote <Kbd>Ctrl</Kbd><Kbd>Enter</Kbd></button>
        }
    </div>
</section>
```

`src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Intake;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// The public reply / internal note composer. The segmented control, not the text, decides the mode; each mode keeps its own draft in <see cref="DraftStore"/>;
/// and the mode never changes by itself (not after a send, not after a conflict reload). Every write sends the ticket's current RowVersion. On ANY failure the text and
/// the picked files stay exactly as they were, so a retry rebuilds the same request; only an accepted send clears them. Sending is disabled while a request is in flight.
/// </summary>
public sealed partial class ReplyComposer : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<string> _fileProblems = [];
    private readonly string _id = Guid.NewGuid().ToString("N")[..8];
    private ComposerDraft _draft = new();
    private Guid _draftTicket;
    private ElementReference _text;
    private string? _error;
    private bool _sending;

    [Inject]
    private ITicketsClient Tickets { get; set; } = default!;

    [Inject]
    private DraftStore Drafts { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    [Parameter, EditorRequired]
    public Guid TicketId { get; set; }

    [Parameter, EditorRequired]
    public string TicketNumber { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public string RequesterEmail { get; set; } = string.Empty;

    /// <summary>The ticket's current RowVersion, replaced by the page after every write and every reload.</summary>
    [Parameter, EditorRequired]
    public uint RowVersion { get; set; }

    /// <summary>Raised once when the API accepted the message; the page applies <see cref="AgentMessageResponse.Ticket"/> and refreshes.</summary>
    [Parameter]
    public EventCallback<AgentMessageResponse> OnSent { get; set; }

    /// <summary>Raised when the API answered 409 <c>concurrency-conflict</c>; the page shows the conflict banner.</summary>
    [Parameter]
    public EventCallback OnConflict { get; set; }

    /// <summary>Raised when the API answered that the ticket no longer exists.</summary>
    [Parameter]
    public EventCallback OnGone { get; set; }

    private bool IsPublic => _draft.Mode == ComposerMode.PublicReply;

    private string TextId => $"ts-composer-text-{_id}";

    private string FilesId => $"ts-composer-files-{_id}";

    private string StatusId => $"ts-composer-status-{_id}";

    private string SectionCss => IsPublic ? "ts-composer ts-composer--public" : "ts-composer ts-composer--note";

    private bool HasUnsentText => !string.IsNullOrWhiteSpace(_draft.PublicText) || !string.IsNullOrWhiteSpace(_draft.NoteText);

    private static string Accept => string.Join(',', IntakeLimits.AllowedExtensions);

    private string Text
    {
        get => IsPublic ? _draft.PublicText : _draft.NoteText;
        set
        {
            if (IsPublic)
            {
                _draft.PublicText = value;
            }
            else
            {
                _draft.NoteText = value;
            }
        }
    }

    private string StatusAfter
    {
        get => _draft.StatusAfter;
        set => _draft.StatusAfter = value;
    }

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    protected override void OnParametersSet()
    {
        if (_draftTicket != TicketId)
        {
            _draftTicket = TicketId;
            _draft = Drafts.Get(TicketId);
            _error = null;
            _fileProblems.Clear();
        }
    }

    private string ModeCss(ComposerMode mode) => mode == _draft.Mode ? "btn btn-primary" : "btn btn-outline-secondary";

    private string PressedValue(ComposerMode mode) => mode == _draft.Mode ? "true" : "false";

    private void SetMode(ComposerMode mode)
    {
        _draft.Mode = mode;
        _error = null;
    }

    private Task OnFilesPickedAsync(InputFileChangeEventArgs e)
    {
        _fileProblems.Clear();
        foreach (var file in e.GetMultipleFiles(Math.Max(1, e.FileCount)))
        {
            if (_draft.Files.Count >= IntakeLimits.MaxFiles)
            {
                _fileProblems.Add(string.Format(ReplyComposerCopy.TooManyFiles, IntakeLimits.MaxFiles));
                break;
            }

            if (!IntakeLimits.AllowedExtensions.Contains(Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase))
            {
                _fileProblems.Add(string.Format(ReplyComposerCopy.FileTypeNotAllowed, file.Name));
            }
            else if (file.Size > IntakeLimits.MaxFileBytes)
            {
                _fileProblems.Add(string.Format(ReplyComposerCopy.FileTooLarge, file.Name, TicketDisplay.FileSize(IntakeLimits.MaxFileBytes)));
            }
            else
            {
                _draft.Files.Add(file);
            }
        }

        return Task.CompletedTask;
    }

    private void RemoveFile(IBrowserFile file) => _draft.Files.Remove(file);

    /// <param name="statusAfterOverride">"Send and solve" passes Solved; otherwise the status choice in the draft applies.</param>
    private async Task SubmitAsync(string? statusAfterOverride = null)
    {
        if (_sending)
        {
            return;
        }

        var mode = _draft.Mode;
        var text = mode == ComposerMode.PublicReply ? _draft.PublicText : _draft.NoteText;
        if (string.IsNullOrWhiteSpace(text))
        {
            _error = mode == ComposerMode.PublicReply ? ReplyComposerCopy.EmptyReply : ReplyComposerCopy.EmptyNote;
            return;
        }

        _sending = true;
        _error = null;
        StateHasChanged();
        try
        {
            var result = mode == ComposerMode.PublicReply
                ? await Tickets.ReplyAsync(TicketId, new AddAgentReplyRequest(text, [], StatusAfterFor(statusAfterOverride), RowVersion), Attachments(), _lifetime.Token)
                : await Tickets.AddNoteAsync(TicketId, new AddInternalNoteRequest(text, RowVersion), _lifetime.Token);
            await HandleResultAsync(mode, result);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The screen was closed mid-request; the draft stays in the store.
        }
        finally
        {
            _sending = false;
        }
    }

    // Each attempt reopens the picked files, so a retry after a failure or a conflict sends the same bytes (a stream cannot be read twice).
    private List<ReplyAttachment> Attachments() =>
        _draft.Files.Select(file => new ReplyAttachment(file.Name, file.ContentType, () => file.OpenReadStream(IntakeLimits.MaxFileBytes))).ToList();

    private string? StatusAfterFor(string? statusAfterOverride)
    {
        if (statusAfterOverride is not null)
        {
            return statusAfterOverride;
        }

        return string.IsNullOrEmpty(_draft.StatusAfter) ? null : _draft.StatusAfter;
    }

    private async Task HandleResultAsync(ComposerMode mode, Result<AgentMessageResponse> result)
    {
        if (result.IsSuccess)
        {
            // Only an accepted send clears anything, and only the mode that was sent.
            if (mode == ComposerMode.PublicReply)
            {
                _draft.PublicText = string.Empty;
                _draft.Files.Clear();
                _fileProblems.Clear();
                StatusMessages.Show(ReplyComposerCopy.ReplySent(TicketNumber));
            }
            else
            {
                _draft.NoteText = string.Empty;
                StatusMessages.Show(ReplyComposerCopy.NoteAdded(TicketNumber));
            }

            await OnSent.InvokeAsync(result.Value);
            return;
        }

        var error = result.Errors[0];
        if (error.Code == ApiErrorCodes.ConcurrencyConflict)
        {
            _error = ReplyComposerCopy.Conflict;
            await OnConflict.InvokeAsync();
        }
        else if (error.Kind == ResultErrorKind.NotFound)
        {
            await OnGone.InvokeAsync();
        }
        else if (error.Code == ApiErrorCodes.TicketClosed)
        {
            _error = error.Message;
        }
        else
        {
            _error = $"{(mode == ComposerMode.PublicReply ? ReplyComposerCopy.ReplyFailed : ReplyComposerCopy.NoteFailed)} {error.Message}";
        }
    }

    private async Task OnShortcutAsync(ShortcutAction action)
    {
        if (_sending)
        {
            return;
        }

        switch (action)
        {
            case ShortcutAction.Reply:
                await FocusAsync(ComposerMode.PublicReply);
                break;
            case ShortcutAction.Note:
                await FocusAsync(ComposerMode.InternalNote);
                break;
            case ShortcutAction.Send:
                await InvokeAsync(async () =>
                {
                    await SubmitAsync();
                    StateHasChanged();
                });
                break;
        }
    }

    private async Task FocusAsync(ComposerMode mode)
    {
        await InvokeAsync(() =>
        {
            SetMode(mode);
            StateHasChanged();
        });
        await _text.FocusAsync();
    }

    public void Dispose()
    {
        Shortcuts.Pressed -= OnShortcutAsync;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

3. Put the composer on the page, beneath the timeline, for any ticket that is not Closed. A sent message applies the response and refreshes; a deleted ticket refreshes into the "no longer exists" view (the conflict wiring is Task 11):

`src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor` changes like this:

```diff
@@ -30,6 +30,11 @@
                 {
                     <p class="ts-closed-note" role="note">@TicketCopy.ClosedNotice</p>
                 }
+                else
+                {
+                    <ReplyComposer TicketId="_model.Id" TicketNumber="@_model.Number" RequesterEmail="@_model.Requester.Email"
+                                   RowVersion="_model.RowVersion" OnSent="OnSentAsync" OnGone="RefreshAsync" />
+                }
             </section>
             <TicketSidePanel Ticket="_model" />
         </div>
```

`src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs` changes like this:

```diff
@@ -121,6 +121,13 @@
         }
     }
 
+    /// <summary>A reply or note was accepted: take the new status and RowVersion from the response at once, then reload so the timeline shows the new entries.</summary>
+    private async Task OnSentAsync(AgentMessageResponse response)
+    {
+        ApplyState(response.Ticket);
+        await RefreshAsync();
+    }
+
     private Task OnShortcutAsync(ShortcutAction action)
     {
         if (action == ShortcutAction.Escape)
```

4. The styles:

`src/TechStrap.Admin/Styles/_composer.scss`

```scss
// The reply composer wears the carbon tint code (BRAND.md section 12, UX-BRIEF-admin "Composer distinction"): canary with a solid edge for a public reply,
// pink with a dashed edge and a notched corner for an internal note. This file is allowed to paint with the tint tokens (TintStyleTests lists it), and
// the same rule applies as in _tinted-entry.scss: a tint never decorates anything else, and colour is never the only cue (the label, the warning text
// and the dashed edge say it too).

.ts-composer {
  position: relative;
  margin-top: 16px;
  padding: 10px 12px 12px;
  color: var(--ink);
  border: 2px solid var(--rule-strong);

  textarea {
    background: var(--sheet);
  }
}

.ts-composer--public {
  background: var(--canary);
  border-color: var(--canary-edge);
  box-shadow: 2px 2px 0 var(--shadow);
}

.ts-composer--note {
  background: var(--pink);
  border: 2px dashed var(--pink-edge);
  clip-path: polygon(0 0, calc(100% - 18px) 0, 100% 18px, 100% 100%, 0 100%);
  box-shadow: none;
}

.ts-composer-modes {
  display: inline-flex;
  gap: 0;
  margin-bottom: 8px;

  .btn + .btn {
    margin-left: -2px;
  }
}

.ts-composer-audience {
  margin: 0 0 4px;
  font: 500 .75rem var(--ts-font-mono);
}

.ts-composer-warning {
  margin: 0 0 6px;
  font: 600 .75rem var(--ts-font-mono);
  letter-spacing: .03em;
}

// The persistent warning bar of the note mode (role="status"): it stays visible while the mode is active.
.ts-composer-warning--internal {
  padding: 4px 8px;
  color: var(--note-ink);
  border: 2px solid var(--note-ink);
}

.ts-composer-files {
  margin-top: 8px;
}

.ts-file-list {
  margin: 4px 0;
  padding: 0;
  list-style: none;
  font-size: .8125rem;
}

.ts-composer-status {
  margin-top: 8px;
  max-width: 16rem;
}

.ts-composer-error {
  margin: 8px 0 0;
  padding: 4px 8px;
  background: var(--sheet);
  border: 2px solid var(--st-spam);
}

.ts-composer-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 10px;
}
```

- [ ] **Step 4: Run the tests**

Run the filter from Step 2, then `dotnet test --project tests/TechStrap.Admin.Tests -c Release`.
Expected: PASS (including `StyleBuildTests`, which compiles the new partial).

- [ ] **Step 5: Commit**

```bash
git diff --cached --stat   # after git add, before committing
git add src tests
git commit -m "feat(admin): reply composer with per-mode drafts, files and conflict-safe retry" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---
### Task 11: Sidebar, tag picker and conflict banner

**Review Focus pin (3), second half:** a write with a stale RowVersion never silently overwrites. Pinned by `TicketSidebarTests.A_stale_row_version_raises_the_conflict_changes_nothing_on_screen_and_shows_no_inline_error`, `Every_write_uses_the_row_version_the_page_holds_now_not_the_one_it_rendered_with`, and `TicketDetailConflictTests.A_conflict_from_the_composer_shows_the_banner_and_reload_refreshes_while_the_draft_and_files_stay`.

**Files:**
- Create: `src/TechStrap.Admin/Features/Tickets/SidebarCopy.cs`, `ConflictBanner.razor`, `ControlFeedback.razor`, `TagPicker.razor` and `.razor.cs`, `TicketSidebar.razor` and `.razor.cs`
- Modify: `src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor`, `TicketDetailPage.razor.cs`
- Modify: `tests/TechStrap.Admin.Tests/Support/TestData.Detail.cs`, `tests/TechStrap.Admin.Tests/Components/TicketDetailPageTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/TicketSidebarTests.cs`, `TicketDetailConflictTests.cs`

**Interfaces:**
- Consumes: `ITicketsClient.ChangeStatusAsync`, `AssignAsync`, `ChangePriorityAsync`, `MoveProductAsync`, `AddTagAsync`, `RemoveTagAsync(Guid ticketId, Guid tagId, uint rowVersion, ct)`, each returning `Result<TicketStateDto>`; `ApiErrorCodes.ConcurrencyConflict`; `AgentSession.Agent` (for "Assign to me"); `TicketDetailViewModel`, `TicketLookups`, `TicketDetailPage.ApplyState` and `RefreshAsync` (Task 9); `ShortcutService` (`e`).
- Produces:

```csharp
// TechStrap.Admin.Features.Tickets
public enum ConflictState { None, Stale, Reloaded }
public enum SidebarField { Status, Assignee, Priority, Product, Tags }
public sealed partial class TicketSidebar : IDisposable    // Ticket (TicketDetailViewModel), OnState EventCallback<TicketStateDto>, OnConflict, OnGone
public sealed partial class TagPicker                      // Selected (TicketTagDto), Available (TagDto), Disabled, OnAdd EventCallback<Guid>, OnRemove EventCallback<Guid>
// ConflictBanner.razor: State, LatestChange, OnReload, OnDismiss
```

**Rules:**
1. **Optimistic-free** (the spec table beats the UX "optimistic" line, brief decision 8): a change shows "Saving..." next to that control and disables every control; on success the page re-renders from the returned `TicketStateDto`; on failure the control shows the API's own sentence under it and the previous value comes back. The select is redrawn by changing its `@key` after every attempt, because Blazor does not reset a `<select>` whose bound value did not change.
2. **Every write sends `Ticket.RowVersion` as the page holds it at that moment** and only one write runs at a time, so two writes can never race on one version. Choosing the value already set makes no call.
3. **Conflict.** A 409 `concurrency-conflict` raises `OnConflict`: no inline text, nothing changes on screen, the page shows the banner "This ticket changed since you opened it. Your reply draft is kept." with Reload. Reload refreshes the ticket, keeps the composer text and files (they live in `DraftStore`), and the banner becomes "Reloaded. Latest change: {newest timeline change} ({actor})." with Dismiss. A failed reload keeps the stale banner; a reload that finds the ticket gone replaces the page with "This ticket no longer exists" and a link to the queue. "Apply my change again" (UX flow step 3) is **not built** in 07a (brief decision 8 asks for Reload only); the agent re-picks the value.
4. **Other refusals** (`ticket-closed`, `invalid-status-transition`, a 400) show `detail` inline. Which status changes are legal is the API's call; the select offers New only when it is the current status, and Open, Pending, Solved, Closed otherwise. UNVERIFIED which transitions an Agent may make: the API message tells the agent.
5. **Assignee.** The select lists the active agents the API returned plus "Unassigned" (null assignee), and an "Assign to me" button shows until the ticket is the signed-in agent's; `e` focuses the select. The product select keeps a current product that is no longer active, so the display never lies. `TagPicker` offers only tags the ticket lacks, resets its select after every pick, and uses `TagDto` directly (the recorded direct-DTO decision).
6. **Closed tickets** get no sidebar (the page passes no `Controls`), so nothing can write to a Closed ticket from here.
7. **Cross-writes share one banner**: the composer (Task 10), the sidebar and the actions (Task 12) all raise the page's `OnConflictAsync`.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/Support/TestData.Detail.cs`: add the lookups and a ready view model:

`tests/TechStrap.Admin.Tests/Support/TestData.Detail.cs` changes like this:

```diff
@@ -1,4 +1,6 @@
+using TechStrap.Admin.Features.Tickets;
 using TechStrap.Contracts.Agents;
+using TechStrap.Contracts.Tags;
 using TechStrap.Contracts.Tickets;
 
 namespace TechStrap.Admin.Tests.Support;
@@ -55,4 +57,26 @@
         bool isSpam = false,
         IReadOnlyList<Guid>? tagIds = null,
         uint rowVersion = 8) => new(TicketId, "ORB-42", status, priority, productId ?? OrbitlyId, assigneeId, isSpam, tagIds ?? [], Now, rowVersion);
+
+    public static readonly Guid BillingTagId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
+    public static readonly Guid NimbusId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
+
+    /// <summary>The lookups the sidebar tests use: two products, two agents, two tags.</summary>
+    public static TicketLookups Lookups() => new(
+        [Product("Orbitly"), Product("Nimbus", NimbusId)],
+        [Agent("Sam Ortiz", SamAgentId), Agent("Ada Admin", AdaAgentId)],
+        [Tag("bug"), Tag("billing", BillingTagId, "#1D4ED8")]);
+
+    /// <summary>A ready view model (what the presenter builds), for component tests that do not go through the page.</summary>
+    public static TicketDetailViewModel Model(
+        string status = TicketStatuses.Open,
+        string priority = TicketPriorities.Normal,
+        Guid? assigneeId = null,
+        string? assigneeName = null,
+        IReadOnlyList<TicketTagDto>? tags = null,
+        uint rowVersion = 7,
+        bool isSpam = false) => new(
+            TicketId, "ORB-42", "Cannot log in", status, priority, isSpam, OrbitlyId, "Orbitly", assigneeId, assigneeName, tags ?? [],
+            new TicketRequesterDto(RequesterId, "ada@example.com", "Ada Lovelace", null), "Email", Now.AddDays(-1), null, null, null,
+            rowVersion, [], Lookups());
 }
```

`tests/TechStrap.Admin.Tests/Components/TicketSidebarTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

public sealed class TicketSidebarTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly List<TicketStateDto> _states = [];
    private int _conflicts;
    private int _gone;

    public TicketSidebarTests()
    {
        Services.AddSingleton(_tickets);
        Services.AddSingleton(AgentSessions.SignedIn());
        _tickets.ChangeStatusAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketStatusRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(TicketStatuses.Solved)));
        _tickets.AssignAsync(Arg.Any<Guid>(), Arg.Any<AssignTicketRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(assigneeId: TestData.AdaAgentId)));
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(priority: TicketPriorities.Urgent)));
        _tickets.MoveProductAsync(Arg.Any<Guid>(), Arg.Any<MoveTicketProductRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(productId: TestData.NimbusId)));
        _tickets.AddTagAsync(Arg.Any<Guid>(), Arg.Any<AddTicketTagRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(tagIds: [TestData.BugTagId])));
        _tickets.RemoveTagAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State()));
    }

    private IRenderedComponent<TicketSidebar> RenderSidebar(TicketDetailViewModel? ticket = null) =>
        Render<TicketSidebar>(p => p
            .Add(c => c.Ticket, ticket ?? TestData.Model())
            .Add(c => c.OnState, state => _states.Add(state))
            .Add(c => c.OnConflict, () => _conflicts++)
            .Add(c => c.OnGone, () => _gone++));

    private static string Selected(IRenderedComponent<TicketSidebar> cut, string selector) =>
        cut.Find(selector).QuerySelectorAll("option").Single(o => o.HasAttribute("selected")).TextContent;

    [Fact]
    public void The_controls_show_the_current_values_and_the_tags()
    {
        var cut = RenderSidebar(TestData.Model(TicketStatuses.Pending, TicketPriorities.High, TestData.SamAgentId, "Sam Ortiz",
            [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]));

        Selected(cut, "select[id^='ts-sidebar-status-']").ShouldBe("Pending");
        Selected(cut, "select[id^='ts-sidebar-assignee-']").ShouldBe("Sam Ortiz");
        Selected(cut, "select[id^='ts-sidebar-priority-']").ShouldBe("High");
        Selected(cut, "select[id^='ts-sidebar-product-']").ShouldBe("Orbitly");
        cut.Find(".ts-tagpicker-list .ts-tag").TextContent.ShouldBe("bug");
        cut.FindAll("label").Select(l => l.TextContent).ShouldBe(["Status", "Assignee", "Priority", "Product", "Add tag…"]);
    }

    [Fact]
    public void Status_calls_the_status_method_with_the_current_row_version_and_reports_the_returned_state()
    {
        var cut = RenderSidebar(TestData.Model(rowVersion: 7));

        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Solved);

        _tickets.Received(1).ChangeStatusAsync(TestData.TicketId, Arg.Is<ChangeTicketStatusRequest>(r => r.Status == "Solved" && r.RowVersion == 7u), Arg.Any<CancellationToken>());
        _states.ShouldHaveSingleItem().Status.ShouldBe("Solved");
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void Assignee_calls_the_assign_method_and_choosing_Unassigned_sends_a_null_assignee()
    {
        var cut = RenderSidebar(TestData.Model(rowVersion: 7));

        cut.Find("select[id^='ts-sidebar-assignee-']").Change(TestData.AdaAgentId.ToString());
        _tickets.Received(1).AssignAsync(TestData.TicketId, Arg.Is<AssignTicketRequest>(r => r.AssigneeId == TestData.AdaAgentId && r.RowVersion == 7u), Arg.Any<CancellationToken>());

        var assigned = RenderSidebar(TestData.Model(assigneeId: TestData.SamAgentId, assigneeName: "Sam Ortiz", rowVersion: 9));
        assigned.Find("select[id^='ts-sidebar-assignee-']").Change(string.Empty);

        _tickets.Received(1).AssignAsync(TestData.TicketId, Arg.Is<AssignTicketRequest>(r => r.AssigneeId == null && r.RowVersion == 9u), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Priority_and_product_call_their_methods_with_the_current_row_version()
    {
        var cut = RenderSidebar(TestData.Model(rowVersion: 7));

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);
        _tickets.Received(1).ChangePriorityAsync(TestData.TicketId, Arg.Is<ChangeTicketPriorityRequest>(r => r.Priority == "Urgent" && r.RowVersion == 7u), Arg.Any<CancellationToken>());

        cut.Find("select[id^='ts-sidebar-product-']").Change(TestData.NimbusId.ToString());
        _tickets.Received(1).MoveProductAsync(TestData.TicketId, Arg.Is<MoveTicketProductRequest>(r => r.ProductId == TestData.NimbusId && r.RowVersion == 7u), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Adding_a_tag_sends_the_tag_and_the_row_version_and_removing_one_sends_the_row_version_for_the_query()
    {
        var cut = RenderSidebar(TestData.Model(rowVersion: 7, tags: [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]));

        cut.Find(".ts-tagpicker select").Change(TestData.BillingTagId.ToString());
        _tickets.Received(1).AddTagAsync(TestData.TicketId, Arg.Is<AddTicketTagRequest>(r => r.TagId == TestData.BillingTagId && r.RowVersion == 7u), Arg.Any<CancellationToken>());

        cut.Find("button[aria-label='Remove tag bug']").Click();
        _tickets.Received(1).RemoveTagAsync(TestData.TicketId, TestData.BugTagId, 7u, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_tag_picker_offers_only_tags_the_ticket_does_not_have_and_puts_the_placeholder_back_after_an_add()
    {
        var cut = RenderSidebar(TestData.Model(tags: [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]));

        cut.FindAll(".ts-tagpicker select option").Select(o => o.TextContent).ShouldBe(["Add tag…", "billing"]);

        cut.Find(".ts-tagpicker select").Change(TestData.BillingTagId.ToString());

        cut.Find(".ts-tagpicker select").QuerySelectorAll("option").Single(o => o.HasAttribute("selected")).TextContent.ShouldBe("Add tag…");
    }

    [Fact]
    public void A_ticket_with_no_tags_says_so_and_a_ticket_with_every_tag_offers_no_add_select()
    {
        RenderSidebar().Find(".ts-tagpicker-none").TextContent.ShouldBe("No tags");

        var all = RenderSidebar(TestData.Model(tags:
            [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626"), new TicketTagDto(TestData.BillingTagId, "billing", "#1D4ED8")]));

        all.FindAll(".ts-tagpicker select").ShouldBeEmpty();
    }

    [Fact]
    public void While_a_change_is_saving_every_control_is_disabled_the_field_says_saving_and_a_second_change_is_ignored()
    {
        var gate = new TaskCompletionSource<Result<TicketStateDto>>();
        _tickets.ChangeStatusAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketStatusRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Solved);

        cut.Find("section.ts-sidebar").GetAttribute("aria-busy").ShouldBe("true");
        cut.FindAll("select, button").ShouldAllBe(e => e.HasAttribute("disabled"));
        cut.Find(".ts-saving").TextContent.ShouldBe("Saving…");
        cut.Find(".ts-saving").GetAttribute("role").ShouldBe("status");

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);
        _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ChangePriorityAsync)).ShouldBe(0);

        gate.SetResult(TestData.Ok(TestData.State(TicketStatuses.Solved)));
        cut.WaitForAssertion(() => cut.FindAll("select").ShouldAllBe(e => !e.HasAttribute("disabled")));
        cut.FindAll(".ts-saving").ShouldBeEmpty();
        _states.Count.ShouldBe(1);
    }

    [Fact]
    public void A_refused_change_shows_the_API_s_message_under_the_control_and_the_previous_value_comes_back()
    {
        _tickets.ChangeStatusAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketStatusRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.InvalidStatusTransition, "A Pending ticket can't go straight to New.", ResultErrorKind.Conflict));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Closed);

        cut.Find(".ts-control-error").TextContent.ShouldBe("A Pending ticket can't go straight to New.");
        cut.Find(".ts-control-error").GetAttribute("role").ShouldBe("alert");
        Selected(cut, "select[id^='ts-sidebar-status-']").ShouldBe("Open");
        _states.ShouldBeEmpty();
        _conflicts.ShouldBe(0);
    }

    [Fact]
    public void A_failure_on_one_control_leaves_the_others_untouched_and_the_error_clears_on_the_next_try()
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<TicketStateDto>("boom", "The API is unavailable."), TestData.Ok(TestData.State(priority: TicketPriorities.High)));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.High);
        cut.FindAll(".ts-control-error").Single().TextContent.ShouldBe("The API is unavailable.");
        Selected(cut, "select[id^='ts-sidebar-priority-']").ShouldBe("Normal");

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.High);

        cut.FindAll(".ts-control-error").ShouldBeEmpty();
        _states.Count.ShouldBe(1);
    }

    [Fact]
    public void A_stale_row_version_raises_the_conflict_changes_nothing_on_screen_and_shows_no_inline_error()
    {
        _tickets.AssignAsync(Arg.Any<Guid>(), Arg.Any<AssignTicketRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-assignee-']").Change(TestData.SamAgentId.ToString());

        _conflicts.ShouldBe(1);
        _states.ShouldBeEmpty();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
        Selected(cut, "select[id^='ts-sidebar-assignee-']").ShouldBe("Unassigned");
        cut.FindAll("select").ShouldAllBe(e => !e.HasAttribute("disabled"));
    }

    [Fact]
    public void A_ticket_deleted_meanwhile_raises_gone()
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.TicketNotFound, "Gone.", ResultErrorKind.NotFound));
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        _gone.ShouldBe(1);
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void Every_write_uses_the_row_version_the_page_holds_now_not_the_one_it_rendered_with()
    {
        var cut = RenderSidebar(TestData.Model(rowVersion: 7));
        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        cut.Render(p => p.Add(c => c.Ticket, TestData.Model(priority: TicketPriorities.Urgent, rowVersion: 12)));
        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Solved);

        _tickets.Received(1).ChangePriorityAsync(Arg.Any<Guid>(), Arg.Is<ChangeTicketPriorityRequest>(r => r.RowVersion == 7u), Arg.Any<CancellationToken>());
        _tickets.Received(1).ChangeStatusAsync(Arg.Any<Guid>(), Arg.Is<ChangeTicketStatusRequest>(r => r.RowVersion == 12u), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Choosing_the_value_that_is_already_set_makes_no_call()
    {
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Open);
        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Normal);
        cut.Find("select[id^='ts-sidebar-assignee-']").Change(string.Empty);
        cut.Find("select[id^='ts-sidebar-product-']").Change(TestData.OrbitlyId.ToString());

        _tickets.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void Assign_to_me_is_offered_until_the_ticket_is_mine_and_assigns_the_signed_in_agent()
    {
        var cut = RenderSidebar();

        cut.Find("button.btn-outline-secondary").TextContent.ShouldBe("Assign to me");
        cut.Find("button.btn-outline-secondary").Click();

        _tickets.Received(1).AssignAsync(TestData.TicketId, Arg.Is<AssignTicketRequest>(r => r.AssigneeId == AgentSessions.SamId && r.RowVersion == 7u), Arg.Any<CancellationToken>());

        var mine = RenderSidebar(TestData.Model(assigneeId: AgentSessions.SamId, assigneeName: "Sam Ortiz"));
        mine.FindAll("button").Where(b => b.TextContent == "Assign to me").ShouldBeEmpty();
    }

    [Fact]
    public void A_status_the_list_does_not_offer_still_shows_as_the_current_choice()
    {
        var cut = RenderSidebar(TestData.Model(TicketStatuses.New));

        cut.Find("select[id^='ts-sidebar-status-']").QuerySelectorAll("option").Select(o => o.TextContent).ShouldBe(["New", "Open", "Pending", "Solved", "Closed"]);
        Selected(cut, "select[id^='ts-sidebar-status-']").ShouldBe("New");
    }

    [Fact]
    public void A_current_product_that_is_no_longer_in_the_active_list_stays_listed_and_selected()
    {
        var model = TestData.Model() with { ProductId = Guid.NewGuid(), ProductName = "Legacy" };

        var cut = RenderSidebar(model);

        Selected(cut, "select[id^='ts-sidebar-product-']").ShouldBe("Legacy");
    }

    [Fact]
    public async Task The_e_key_focuses_the_assignee_control()
    {
        var cut = RenderSidebar();
        var assigneeId = cut.Find("select[id^='ts-sidebar-assignee-']").GetAttribute("blazor:elementReference");

        await PressAsync("e");

        ((ElementReference)JSInterop.VerifyFocusAsyncInvoke().Arguments[0]!).Id.ShouldBe(assigneeId);
    }

    [Fact]
    public void Every_client_call_receives_a_cancellable_token()
    {
        var cut = RenderSidebar();

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        var token = (CancellationToken)_tickets.ReceivedCalls().Single().GetArguments().Last()!;
        token.CanBeCanceled.ShouldBeTrue();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/TicketDetailConflictTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The page with the sidebar and the composer together: state flows, the conflict banner, and what a reload keeps.</summary>
public sealed class TicketDetailConflictTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();

    public TicketDetailConflictTests()
    {
        var products = Substitute.For<IProductsClient>();
        var agents = Substitute.For<IAgentsClient>();
        var tags = Substitute.For<ITagsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.Agent("Sam Ortiz", TestData.SamAgentId)]));
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(agents);
        Services.AddSingleton(tags);
        Services.AddTicketFeatures();
        Services.AddSingleton(AgentSessions.SignedIn());
        ShowTicket(TestData.Detail());
    }

    private void ShowTicket(TicketDetailDto detail) =>
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(detail));

    private IRenderedComponent<TicketDetailPage> RenderTicket() => Render<TicketDetailPage>(p => p.Add(c => c.Number, "ORB-42"));

    private void ReplyReturns(params Result<AgentMessageResponse>[] results) =>
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>()).Returns(results[0], results[1..]);

    private static Result<AgentMessageResponse> Conflict() =>
        TestData.Fail<AgentMessageResponse>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict);

    private void PriorityConflicts() =>
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));

    [Fact]
    public void An_open_ticket_has_the_sidebar_controls_in_the_side_panel_instead_of_the_read_only_facts()
    {
        var cut = RenderTicket();

        cut.Find("aside.ts-side section.ts-sidebar").ShouldNotBeNull();
        cut.FindAll("section.ts-facts").ShouldBeEmpty();
        cut.Find("aside.ts-side section.ts-requester").ShouldNotBeNull();
    }

    [Fact]
    public void A_sidebar_change_applies_the_returned_state_reloads_and_hands_the_composer_the_new_row_version()
    {
        _tickets.ChangeStatusAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketStatusRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.State(TicketStatuses.Solved, rowVersion: 8)));
        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Internal), TestData.State(rowVersion: 9))));
        var cut = RenderTicket();
        ShowTicket(TestData.Detail(status: TicketStatuses.Solved, rowVersion: 8));

        cut.Find("select[id^='ts-sidebar-status-']").Change(TicketStatuses.Solved);

        cut.WaitForAssertion(() => _tickets.Received(2).GetAsync("ORB-42", Arg.Any<CancellationToken>()));
        cut.Find(".ts-ticket-badges .ts-stamp").TextContent.ShouldBe("Solved");

        cut.FindAll(".ts-composer-modes button")[1].Click();
        cut.Find("textarea").Input("After the status change.");
        cut.Find(".ts-composer-actions button").Click();

        _tickets.Received(1).AddNoteAsync(TestData.TicketId, Arg.Is<AddInternalNoteRequest>(r => r.RowVersion == 8u), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_conflict_from_the_composer_shows_the_banner_and_reload_refreshes_while_the_draft_and_files_stay()
    {
        ReplyReturns(Conflict(), TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent), TestData.State(rowVersion: 10))));
        var cut = RenderTicket();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[4], "log.txt", null, "text/plain"));
        cut.Find("textarea").Input("A reply I must not lose.");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        var banner = cut.Find(".ts-conflict");
        banner.GetAttribute("role").ShouldBe("alert");
        banner.TextContent.ShouldContain("This ticket changed since you opened it");
        banner.TextContent.ShouldContain("Your reply draft is kept.");

        ShowTicket(TestData.Detail(rowVersion: 9, events: [TestData.Event(TicketEventTypes.PriorityChanged, """{"from":"Normal","to":"High"}""", actorName: "Ada Admin")]));
        cut.Find(".ts-conflict button").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict--reloaded").TextContent.ShouldContain("Reloaded. Latest change: Priority changed from Normal to High (Ada Admin)."));
        cut.Find("textarea").GetAttribute("value").ShouldBe("A reply I must not lose.");
        cut.Find(".ts-file-name").TextContent.ShouldBe("log.txt");

        cut.FindAll(".ts-composer-actions button")[0].Click();

        _tickets.Received(1).ReplyAsync(
            TestData.TicketId, Arg.Is<AddAgentReplyRequest>(r => r.RowVersion == 9u && r.Body == "A reply I must not lose."),
            Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_conflict_from_the_sidebar_shows_the_same_banner_leaves_the_display_alone_and_dismiss_hides_it()
    {
        PriorityConflicts();
        var cut = RenderTicket();

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        cut.Find(".ts-conflict").TextContent.ShouldContain("This ticket changed since you opened it");
        cut.Find("select[id^='ts-sidebar-priority-']").QuerySelectorAll("option").Single(o => o.HasAttribute("selected")).TextContent.ShouldBe("Normal");

        cut.Find(".ts-conflict button").Click();
        cut.WaitForAssertion(() => cut.FindAll(".ts-conflict--reloaded").Count.ShouldBe(1));
        cut.Find(".ts-conflict--reloaded").TextContent.ShouldNotContain("Latest change");

        cut.Find(".ts-conflict button").Click();

        cut.FindAll(".ts-conflict").ShouldBeEmpty();
    }

    [Fact]
    public void A_reload_that_fails_keeps_the_stale_banner_and_one_that_finds_the_ticket_gone_replaces_the_page()
    {
        PriorityConflicts();
        var cut = RenderTicket();
        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("boom", "The API is unavailable."));
        cut.Find(".ts-conflict button").Click();

        cut.WaitForAssertion(() => cut.Find("article.ts-ticket [role=alert] p").TextContent.ShouldContain("The API is unavailable."));
        cut.FindAll(".ts-conflict--reloaded").ShouldBeEmpty();
        cut.Find(".ts-conflict").TextContent.ShouldContain("This ticket changed since you opened it");

        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));
        cut.Find(".ts-conflict button").Click();

        cut.WaitForAssertion(() => cut.Find("section.ts-gone h1").TextContent.ShouldBe("This ticket no longer exists"));
    }

    [Fact]
    public void A_sidebar_change_on_a_ticket_deleted_meanwhile_ends_on_this_ticket_no_longer_exists_with_a_way_back()
    {
        _tickets.ChangePriorityAsync(Arg.Any<Guid>(), Arg.Any<ChangeTicketPriorityRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.TicketNotFound, "Gone.", ResultErrorKind.NotFound));
        var cut = RenderTicket();
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("ticket-not-found", "Gone.", ResultErrorKind.NotFound));

        cut.Find("select[id^='ts-sidebar-priority-']").Change(TicketPriorities.Urgent);

        cut.WaitForAssertion(() => cut.Find("section.ts-gone h1").TextContent.ShouldBe("This ticket no longer exists"));
        cut.Find("section.ts-gone a").GetAttribute("href").ShouldBe("/queue");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/TicketDetailPageTests.cs`: the sidebar needs the agent session, and the read-only facts now show only for a Closed ticket:

`tests/TechStrap.Admin.Tests/Components/TicketDetailPageTests.cs` changes like this:

```diff
@@ -31,6 +31,7 @@
         Services.AddSingleton(agents);
         Services.AddSingleton(tags);
         Services.AddTicketFeatures();
+        Services.AddSingleton(AgentSessions.SignedIn());
         ShowTicket(TestData.Detail());
         _navigation = Services.GetRequiredService<NavigationManager>();
     }
@@ -234,9 +235,9 @@
     }
 
     [Fact]
-    public void The_side_panel_shows_the_requester_and_the_ticket_facts_read_only()
-    {
-        ShowTicket(TestData.Detail(priority: TicketPriorities.Urgent, assigneeId: TestData.SamAgentId, assigneeName: "Sam Ortiz",
+    public void A_closed_tickets_side_panel_shows_the_requester_and_the_facts_read_only_with_no_controls()
+    {
+        ShowTicket(TestData.Detail(status: TicketStatuses.Closed, priority: TicketPriorities.Urgent, assigneeId: TestData.SamAgentId, assigneeName: "Sam Ortiz",
             tags: [new TicketTagDto(TestData.BugTagId, "bug", "#DC2626")]));
 
         var cut = RenderTicket();
@@ -246,6 +247,7 @@
         facts.TextContent.ShouldContain("Sam Ortiz");
         facts.QuerySelector(".ts-priority")!.TextContent.ShouldBe("Urgent");
         facts.QuerySelector(".ts-tag")!.TextContent.ShouldBe("bug");
+        cut.FindAll("section.ts-sidebar, aside select, aside button").ShouldBeEmpty();
     }
 
     [Fact]
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "TicketSidebarTests|TicketDetailConflictTests|TicketDetailPageTests"`
Expected: a build failure (`TicketSidebar`, `TagPicker`, `ConflictBanner`, `ConflictState` do not exist).

- [ ] **Step 3: Implement**

1. The copy, the banner and the small pieces:

`src/TechStrap.Admin/Features/Tickets/SidebarCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Tickets;

/// <summary>The sidebar's copy and the one conflict banner every write shares. The two banner sentences come from UX-BRIEF-admin word for word.</summary>
public static class SidebarCopy
{
    public const string Saving = "Saving…";
    public const string AssignToMe = "Assign to me";
    public const string Unassigned = "Unassigned";
    public const string AddTagPlaceholder = "Add tag…";
    public const string NoTags = "No tags";

    public const string ConflictTitle = "This ticket changed since you opened it";
    public const string ConflictKept = "Your reply draft is kept.";
    public const string ConflictReload = "Reload";
    public const string ConflictReloaded = "Reloaded.";
    public const string ConflictDismiss = "Dismiss";

    public static string RemoveTag(string name) => $"Remove tag {name}";

    public static string LatestChange(string text, string actor) => $"Latest change: {text} ({actor}).";
}

/// <summary>Where the conflict banner is: hidden, asking for a reload, or confirming the reload.</summary>
public enum ConflictState
{
    None,
    Stale,
    Reloaded,
}

/// <summary>The sidebar's fields, used as keys for its pending, error and re-render state.</summary>
public enum SidebarField
{
    Status,
    Assignee,
    Priority,
    Product,
    Tags,
}
```

`src/TechStrap.Admin/Features/Tickets/ConflictBanner.razor`

```razor
@if (State == ConflictState.Stale)
{
    <div class="ts-conflict" role="alert">
        <p><strong>@SidebarCopy.ConflictTitle</strong> @SidebarCopy.ConflictKept</p>
        <button type="button" class="btn btn-outline-secondary" @onclick="OnReload">@SidebarCopy.ConflictReload</button>
    </div>
}
else if (State == ConflictState.Reloaded)
{
    <div class="ts-conflict ts-conflict--reloaded" role="status">
        <p>@SidebarCopy.ConflictReloaded @LatestChange</p>
        <button type="button" class="btn btn-outline-secondary" @onclick="OnDismiss">@SidebarCopy.ConflictDismiss</button>
    </div>
}

@code {
    [Parameter, EditorRequired]
    public ConflictState State { get; set; }

    /// <summary>After the reload: what changed and by whom, taken from the newest change on the timeline.</summary>
    [Parameter]
    public string? LatestChange { get; set; }

    [Parameter]
    public EventCallback OnReload { get; set; }

    [Parameter]
    public EventCallback OnDismiss { get; set; }
}
```

`src/TechStrap.Admin/Features/Tickets/ControlFeedback.razor`

```razor
@if (Saving)
{
    <span class="ts-saving" role="status">@SidebarCopy.Saving</span>
}
@if (Error is not null)
{
    <p class="ts-control-error" role="alert">@Error</p>
}

@code {
    /// <summary>This control's request is running.</summary>
    [Parameter]
    public bool Saving { get; set; }

    /// <summary>The API's message for the last failed change of this control. The control shows the previous value again.</summary>
    [Parameter]
    public string? Error { get; set; }
}
```

`src/TechStrap.Admin/Features/Tickets/TagPicker.razor`

```razor
<div class="ts-tagpicker">
    @if (Selected.Count == 0)
    {
        <p class="ts-tagpicker-none">@SidebarCopy.NoTags</p>
    }
    else
    {
        <ul class="ts-tagpicker-list">
            @foreach (var tag in Selected)
            {
                <li @key="tag.Id">
                    <TagChip Name="@tag.Name" Colour="@tag.Colour" />
                    <button type="button" class="btn btn-link" aria-label="@SidebarCopy.RemoveTag(tag.Name)" disabled="@Disabled" @onclick="() => OnRemove.InvokeAsync(tag.Id)">&times;</button>
                </li>
            }
        </ul>
    }
    @if (Addable.Count > 0)
    {
        <label for="@AddId" class="visually-hidden">@SidebarCopy.AddTagPlaceholder</label>
        <select id="@AddId" class="form-select" disabled="@Disabled" @key="@($"add-{_rev}")" @onchange="OnPickedAsync">
            <option value="" selected>@SidebarCopy.AddTagPlaceholder</option>
            @foreach (var tag in Addable)
            {
                <option value="@tag.Id">@tag.Name</option>
            }
        </select>
    }
</div>
```

`src/TechStrap.Admin/Features/Tickets/TagPicker.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// The tags on one ticket: chips with a remove button, and a select offering the tags it does not have yet. It reports an add or a remove and nothing else; the sidebar owns
/// the request, the RowVersion and the error. It uses <see cref="TagDto"/> directly (the recorded direct-DTO decision: read-only display, no reshaping).
/// </summary>
public sealed partial class TagPicker
{
    private static int _nextId;

    private readonly int _id = Interlocked.Increment(ref _nextId);
    private int _rev;

    [Parameter, EditorRequired]
    public IReadOnlyList<TicketTagDto> Selected { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<TagDto> Available { get; set; } = [];

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback<Guid> OnAdd { get; set; }

    [Parameter]
    public EventCallback<Guid> OnRemove { get; set; }

    private string AddId => $"ts-tag-add-{_id}";

    private IReadOnlyList<TagDto> Addable => Available.Where(tag => Selected.All(s => s.Id != tag.Id)).ToList();

    private async Task OnPickedAsync(ChangeEventArgs e)
    {
        // Back to the placeholder whatever the outcome, so the select never shows a tag that was not added.
        _rev++;
        if (Guid.TryParse(e.Value as string, out var id))
        {
            await OnAdd.InvokeAsync(id);
        }
    }
}
```

`src/TechStrap.Admin/Features/Tickets/TicketSidebar.razor`

```razor
<section class="ts-sidebar" aria-label="Ticket controls" aria-busy="@(_busy ? "true" : null)">
    <div class="ts-control">
        <label for="@IdOf(SidebarField.Status)">Status</label>
        <select id="@IdOf(SidebarField.Status)" class="form-select" disabled="@_busy" @key="KeyOf(SidebarField.Status)" @onchange="OnStatusChangedAsync">
            @foreach (var status in StatusOptions)
            {
                <option value="@status" selected="@(status == Ticket.Status)">@status</option>
            }
        </select>
        <ControlFeedback Saving="@IsSaving(SidebarField.Status)" Error="@ErrorOf(SidebarField.Status)" />
    </div>

    <div class="ts-control">
        <label for="@IdOf(SidebarField.Assignee)">Assignee</label>
        <select id="@IdOf(SidebarField.Assignee)" @ref="_assignee" class="form-select" disabled="@_busy" @key="KeyOf(SidebarField.Assignee)" @onchange="OnAssigneeChangedAsync">
            <option value="" selected="@(Ticket.AssigneeId is null)">@SidebarCopy.Unassigned</option>
            @foreach (var agent in AgentOptions)
            {
                <option value="@agent.Id" selected="@(agent.Id == Ticket.AssigneeId)">@agent.DisplayLabel</option>
            }
        </select>
        @if (CanAssignToMe)
        {
            <button type="button" class="btn btn-outline-secondary" disabled="@_busy" @onclick="AssignToMeAsync">@SidebarCopy.AssignToMe</button>
        }
        <ControlFeedback Saving="@IsSaving(SidebarField.Assignee)" Error="@ErrorOf(SidebarField.Assignee)" />
    </div>

    <div class="ts-control">
        <label for="@IdOf(SidebarField.Priority)">Priority</label>
        <select id="@IdOf(SidebarField.Priority)" class="form-select" disabled="@_busy" @key="KeyOf(SidebarField.Priority)" @onchange="OnPriorityChangedAsync">
            @foreach (var priority in PriorityOptions)
            {
                <option value="@priority" selected="@(priority == Ticket.Priority)">@priority</option>
            }
        </select>
        <ControlFeedback Saving="@IsSaving(SidebarField.Priority)" Error="@ErrorOf(SidebarField.Priority)" />
    </div>

    <div class="ts-control">
        <label for="@IdOf(SidebarField.Product)">Product</label>
        <select id="@IdOf(SidebarField.Product)" class="form-select" disabled="@_busy" @key="KeyOf(SidebarField.Product)" @onchange="OnProductChangedAsync">
            @foreach (var product in ProductOptions)
            {
                <option value="@product.Id" selected="@(product.Id == Ticket.ProductId)">@product.Name</option>
            }
        </select>
        <ControlFeedback Saving="@IsSaving(SidebarField.Product)" Error="@ErrorOf(SidebarField.Product)" />
    </div>

    <div class="ts-control">
        <span class="ts-control-label" id="@IdOf(SidebarField.Tags)">Tags</span>
        <TagPicker Selected="Ticket.Tags" Available="Ticket.Lookups.Tags" Disabled="_busy" OnAdd="AddTagAsync" OnRemove="RemoveTagAsync" />
        <ControlFeedback Saving="@IsSaving(SidebarField.Tags)" Error="@ErrorOf(SidebarField.Tags)" />
    </div>
</section>
```

`src/TechStrap.Admin/Features/Tickets/TicketSidebar.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// Status, assignee, priority, product and tags. Optimistic-free (PHASE-07 spec): a change shows "Saving..." and disables the controls, then the page re-renders from the
/// <see cref="TicketStateDto"/> the API returned. A failure shows the API's message under the control and puts the previous value back; a 409 conflict raises the banner and changes
/// nothing. Every write sends the RowVersion the page currently holds, never a remembered one, and only one write runs at a time so two writes can never race on one version.
/// Which status changes are legal is the API's decision: a refused transition comes back as a message, not as a hidden option.
/// </summary>
public sealed partial class TicketSidebar : IDisposable
{
    private static int _nextId;

    private static readonly string[] SelectableStatuses =
        [TicketStatuses.Open, TicketStatuses.Pending, TicketStatuses.Solved, TicketStatuses.Closed];

    private static readonly string[] PriorityOptions =
        [TicketPriorities.Low, TicketPriorities.Normal, TicketPriorities.High, TicketPriorities.Urgent];

    private readonly int _id = Interlocked.Increment(ref _nextId);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<SidebarField, string> _errors = [];
    private readonly Dictionary<SidebarField, int> _revisions = [];
    private ElementReference _assignee;
    private SidebarField? _saving;
    private bool _busy;

    [Inject]
    private ITicketsClient Tickets { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    /// <summary>The ticket as the page holds it, including the current RowVersion and the lookups.</summary>
    [Parameter, EditorRequired]
    public TicketDetailViewModel Ticket { get; set; } = default!;

    /// <summary>Raised with the API's returned state after an accepted change; the page replaces its model from it.</summary>
    [Parameter]
    public EventCallback<TicketStateDto> OnState { get; set; }

    /// <summary>Raised on 409 <c>concurrency-conflict</c>; the page shows the conflict banner.</summary>
    [Parameter]
    public EventCallback OnConflict { get; set; }

    /// <summary>Raised when the API answered that the ticket no longer exists.</summary>
    [Parameter]
    public EventCallback OnGone { get; set; }

    private IEnumerable<string> StatusOptions =>
        SelectableStatuses.Contains(Ticket.Status) ? SelectableStatuses : [Ticket.Status, .. SelectableStatuses];

    private IEnumerable<AgentListItemDto> AgentOptions => Ticket.Lookups.Agents;

    // The current product stays selectable even when it is no longer in the active-products list the API returns to agents.
    private IEnumerable<ProductDto> ProductOptions => Ticket.Lookups.Products.Any(p => p.Id == Ticket.ProductId)
        ? Ticket.Lookups.Products
        : [.. Ticket.Lookups.Products, CurrentProductOnly()];

    private bool CanAssignToMe => Session.Agent is { } me && Ticket.AssigneeId != me.Id;

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    private ProductDto CurrentProductOnly() =>
        new(Ticket.ProductId, string.Empty, Ticket.ProductName, string.Empty, IsActive: false, new ProductBrandingDto(Ticket.ProductName, null, string.Empty, string.Empty, string.Empty, null, null), Version: 0);

    private string IdOf(SidebarField field) => $"ts-sidebar-{field.ToString().ToLowerInvariant()}-{_id}";

    private string KeyOf(SidebarField field) => $"{field}-{_revisions.GetValueOrDefault(field)}";

    private bool IsSaving(SidebarField field) => _saving == field;

    private string? ErrorOf(SidebarField field) => _errors.GetValueOrDefault(field);

    private static string? Text(ChangeEventArgs e) => e.Value as string;

    private Task OnStatusChangedAsync(ChangeEventArgs e) =>
        Text(e) is { Length: > 0 } status && status != Ticket.Status
            ? ApplyAsync(SidebarField.Status, ct => Tickets.ChangeStatusAsync(Ticket.Id, new ChangeTicketStatusRequest(status, Ticket.RowVersion), ct))
            : Revert(SidebarField.Status);

    private Task OnAssigneeChangedAsync(ChangeEventArgs e)
    {
        Guid? assignee = Guid.TryParse(Text(e), out var id) ? id : null;
        return assignee == Ticket.AssigneeId
            ? Revert(SidebarField.Assignee)
            : ApplyAsync(SidebarField.Assignee, ct => Tickets.AssignAsync(Ticket.Id, new AssignTicketRequest(assignee, Ticket.RowVersion), ct));
    }

    private Task AssignToMeAsync() =>
        Session.Agent is { } me
            ? ApplyAsync(SidebarField.Assignee, ct => Tickets.AssignAsync(Ticket.Id, new AssignTicketRequest(me.Id, Ticket.RowVersion), ct))
            : Task.CompletedTask;

    private Task OnPriorityChangedAsync(ChangeEventArgs e) =>
        Text(e) is { Length: > 0 } priority && priority != Ticket.Priority
            ? ApplyAsync(SidebarField.Priority, ct => Tickets.ChangePriorityAsync(Ticket.Id, new ChangeTicketPriorityRequest(priority, Ticket.RowVersion), ct))
            : Revert(SidebarField.Priority);

    private Task OnProductChangedAsync(ChangeEventArgs e) =>
        Guid.TryParse(Text(e), out var product) && product != Ticket.ProductId
            ? ApplyAsync(SidebarField.Product, ct => Tickets.MoveProductAsync(Ticket.Id, new MoveTicketProductRequest(product, Ticket.RowVersion), ct))
            : Revert(SidebarField.Product);

    private Task AddTagAsync(Guid tagId) =>
        ApplyAsync(SidebarField.Tags, ct => Tickets.AddTagAsync(Ticket.Id, new AddTicketTagRequest(tagId, Ticket.RowVersion), ct));

    private Task RemoveTagAsync(Guid tagId) =>
        ApplyAsync(SidebarField.Tags, ct => Tickets.RemoveTagAsync(Ticket.Id, tagId, Ticket.RowVersion, ct));

    /// <summary>The user picked what is already there (or nothing usable): put the select back to the model's value.</summary>
    private Task Revert(SidebarField field)
    {
        _revisions[field] = _revisions.GetValueOrDefault(field) + 1;
        return Task.CompletedTask;
    }

    private async Task ApplyAsync(SidebarField field, Func<CancellationToken, Task<Result<TicketStateDto>>> send)
    {
        if (_busy)
        {
            await Revert(field);
            return;
        }

        _busy = true;
        _saving = field;
        _errors.Remove(field);
        StateHasChanged();
        try
        {
            var result = await send(_lifetime.Token);
            if (result.IsSuccess)
            {
                await OnState.InvokeAsync(result.Value);
            }
            else
            {
                await HandleFailureAsync(field, result.Errors[0]);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The screen was closed mid-request.
        }
        finally
        {
            _busy = false;
            _saving = null;
        }

        // Always redraw the selects from the model: after a success the new value, after a failure the old one.
        await Revert(field);
    }

    private async Task HandleFailureAsync(SidebarField field, ResultError error)
    {
        if (error.Code == ApiErrorCodes.ConcurrencyConflict)
        {
            await OnConflict.InvokeAsync();
        }
        else if (error.Kind == ResultErrorKind.NotFound)
        {
            await OnGone.InvokeAsync();
        }
        else
        {
            // Includes ticket-closed and invalid-status-transition: the API's own sentence says why.
            _errors[field] = error.Message;
        }
    }

    private async Task OnShortcutAsync(ShortcutAction action)
    {
        if (action == ShortcutAction.FocusAssignee)
        {
            await _assignee.FocusAsync();
        }
    }

    public void Dispose()
    {
        Shortcuts.Pressed -= OnShortcutAsync;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

2. Wire the sidebar and the banner into the page. The sidebar sits in the side panel through its `Controls` slot (only when the ticket is not Closed), the composer also raises the conflict, and the page gains the conflict state and the reload:

`src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor` changes like this:

```diff
@@ -22,6 +22,7 @@
         {
             <ErrorState Message="@_error" OnRetry="RefreshAsync" />
         }
+        <ConflictBanner State="_conflict" LatestChange="@_latestChange" OnReload="ReloadAfterConflictAsync" OnDismiss="DismissConflict" />
         <div class="ts-ticket-layout">
             <section class="ts-conversation" aria-label="Conversation">
                 <TicketTimeline Entries="_model.Timeline" />
@@ -33,10 +34,21 @@
                 else
                 {
                     <ReplyComposer TicketId="_model.Id" TicketNumber="@_model.Number" RequesterEmail="@_model.Requester.Email"
-                                   RowVersion="_model.RowVersion" OnSent="OnSentAsync" OnGone="RefreshAsync" />
+                                   RowVersion="_model.RowVersion" OnSent="OnSentAsync" OnConflict="OnConflictAsync" OnGone="RefreshAsync" />
                 }
             </section>
-            <TicketSidePanel Ticket="_model" />
+            @if (_model.IsClosed)
+            {
+                <TicketSidePanel Ticket="_model" />
+            }
+            else
+            {
+                <TicketSidePanel Ticket="_model">
+                    <Controls>
+                        <TicketSidebar Ticket="_model" OnState="OnStateChangedAsync" OnConflict="OnConflictAsync" OnGone="RefreshAsync" />
+                    </Controls>
+                </TicketSidePanel>
+            }
         </div>
     </article>
 }
```

`src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor.cs` changes like this:

```diff
@@ -22,6 +22,8 @@
     private string _error = string.Empty;
     private bool _loading;
     private TicketDetailViewModel? _model;
+    private ConflictState _conflict;
+    private string? _latestChange;
 
     [Inject]
     private TicketDetailPresenter Presenter { get; set; } = default!;
@@ -122,11 +124,35 @@
     }
 
     /// <summary>A reply or note was accepted: take the new status and RowVersion from the response at once, then reload so the timeline shows the new entries.</summary>
-    private async Task OnSentAsync(AgentMessageResponse response)
+    private Task OnSentAsync(AgentMessageResponse response) => OnStateChangedAsync(response.Ticket);
+
+    /// <summary>Any accepted write: the response replaces the model's status fields and RowVersion, then a reload brings the new timeline entries.</summary>
+    private async Task OnStateChangedAsync(TicketStateDto state)
     {
-        ApplyState(response.Ticket);
+        ApplyState(state);
         await RefreshAsync();
     }
+
+    /// <summary>A write hit 409 concurrency-conflict: nothing was changed, the user's draft and choice stay, and the banner offers a reload.</summary>
+    private Task OnConflictAsync()
+    {
+        _conflict = ConflictState.Stale;
+        _latestChange = null;
+        return Task.CompletedTask;
+    }
+
+    private async Task ReloadAfterConflictAsync()
+    {
+        await RefreshAsync();
+        if (_model is not null && _error.Length == 0)
+        {
+            var latest = _model.Timeline.LastOrDefault(entry => entry.Kind == TimelineEntryKind.Event);
+            _latestChange = latest is null ? null : SidebarCopy.LatestChange(latest.Text, latest.Actor);
+            _conflict = ConflictState.Reloaded;
+        }
+    }
+
+    private void DismissConflict() => _conflict = ConflictState.None;
 
     private Task OnShortcutAsync(ShortcutAction action)
     {
```

- [ ] **Step 4: Run the tests**

Run the filter from Step 2, then `dotnet test --project tests/TechStrap.Admin.Tests -c Release`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git diff --cached --stat   # after git add, before committing
git add src tests
git commit -m "feat(admin): ticket sidebar, tag picker and the shared conflict banner" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---
### Task 12: Spam, delete and erase actions, and Not spam

**Review Focus pin (4):** a plain Agent never sees or triggers delete or erase; no destructive action fires without its typed confirmation; Enter never confirms a dialog. Pinned by `DestructiveActionTests` (the whole class).

**Files:**
- Create: `src/TechStrap.Admin/Features/Tickets/ActionsCopy.cs`, `TicketActions.razor` and `.razor.cs`
- Modify: `src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor`
- Modify: `src/TechStrap.Admin/Features/Shell/ShortcutService.cs`, `ShortcutCatalog.cs`
- Modify: `src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor`, `TicketQueuePage.razor.cs`, `TicketRow.razor`, `TicketRow.razor.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/DestructiveActionTests.cs`, `NotSpamQueueTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/ShortcutServiceTests.cs`, `ShellComponentTests.cs`, `TicketDetailPageTests.cs`, `TicketDetailConflictTests.cs`

**Interfaces:**
- Consumes: `ITicketsClient.SetSpamAsync(Guid, MarkTicketSpamRequest, ct)` (`Result<TicketStateDto>`), `DeleteAsync(Guid, ct)` (`Result`), `GetAsync(string, ct)`, `GetCountsAsync(ct)`; `IRequestersClient.EraseAsync(Guid requesterId, ct)` (`Result`); `AgentSession.IsAdmin`; `ConfirmDialog` (Task 7); `StatusMessageService`; the page's `OnStateChangedAsync`, `OnConflictAsync`, `RefreshAsync` (Tasks 9 to 11); `TicketQueuePage` (Task 8).
- Produces:

```csharp
// TechStrap.Admin.Features.Tickets
public enum ActionDialog { None, Spam, Delete, Erase }
public static class ActionsCopy { ... }                         // dialog titles and bodies, status-bar messages, failure sentences
public sealed partial class TicketActions : IDisposable        // Ticket, OnState EventCallback<TicketStateDto>, OnConflict, OnGone
// TechStrap.Admin.Features.Shell: ShortcutAction.NotSpam ("u"); ShortcutCatalog gains the u row
```

**Rules:**
1. **Three tiers** (UX-BRIEF-admin). Not spam is reversible: no dialog, status unchanged. Mark as spam is medium: a confirmation naming the ticket and the consequence. Delete and erase are irreversible: a typed confirmation (the ticket number; the requester's email), danger style plus the word ("Delete ticket (permanent)").
2. **Admin-only means not rendered.** For an Agent the delete and erase menu entries and their dialogs are absent from the markup (the test checks the HTML for the words), and the API still answers 403. Spam and Not spam are for any agent. On a Closed ticket the spam entries are not offered (the API refuses spam on Closed with 409 `ticket-closed`), an Agent therefore sees no menu at all, and an Admin sees only delete and erase.
3. **No counts, no child list** (brief decision 11, recorded gap): the erase dialog names the requester's email and says "every ticket from this requester"; there is no "12 tickets, 31 attachments" because the API has no such endpoint. A test fails if the body ever contains a count.
4. **Enter never confirms** (Task 7's structure: no form, `type="button"`, focus on the input or heading) and Esc cancels, except while a request runs. A failure keeps the dialog open, shows "Couldn't ... Nothing was changed. {API message}" and navigates nowhere; only success leaves for the queue (spam, delete, erase) with the status-bar message ("Marked ORB-42 as spam", "Deleted ORB-42", "Erased the requester of ORB-42": the erase message carries no email). A conflict or a missing ticket closes the dialog and goes to the page's banner or "no longer exists" view.
5. **`u` restores** (Not spam) on a flagged, open ticket and, in the Spam view, on the selected row; it follows the typing guard and the shortcut switch (`Map`), and a second `u` while the first runs is ignored. The menu entry and the Spam-view row button are the mouse equivalents.
6. **A queue row has no RowVersion** (`TicketSummaryDto` carries none) and the API requires one for the spam write (400 `row-version-required`), so the Spam view's Not spam reads the ticket first (`GetAsync(number)`) and writes against that version: one read, one write, no dialog. A 409 keeps the row and says "ORB-1 changed meanwhile. Open it to review, then restore it from spam." On success the row is removed locally, the count refreshes, and the status bar says "Restored ORB-1 from spam".
7. **The help dialog lists `u`**, and the catalog test still forbids two shortcuts sharing a key (so `u` cannot collide with `j`, `k`, `r`, `n`, `e` or `/`).

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/Components/DestructiveActionTests.cs`

```csharp
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>Review Focus 4: a plain agent never sees or triggers delete or erase, nothing destructive fires without the typed confirmation, and Enter never confirms a dialog.</summary>
public sealed class DestructiveActionTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly IRequestersClient _requesters = Substitute.For<IRequestersClient>();
    private readonly NavigationManager _navigation;
    private readonly List<TicketStateDto> _states = [];
    private int _conflicts;
    private int _gone;
    private bool _admin;

    public DestructiveActionTests()
    {
        Services.AddSingleton(_tickets);
        Services.AddSingleton(_requesters);
        // Built on first use, after RenderActions has said whether this test signs in an Agent or an Admin (services cannot be added once something has been resolved).
        Services.AddSingleton(_ => AgentSessions.SignedIn(_admin));
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(isSpam: true, rowVersion: 8)));
        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok());
        _requesters.EraseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok());
        _navigation = Services.GetRequiredService<NavigationManager>();
        _navigation.NavigateTo("/tickets/ORB-42");
    }

    private IRenderedComponent<TicketActions> RenderActions(bool admin = false, TicketDetailViewModel? ticket = null)
    {
        _admin = admin;
        return Render<TicketActions>(p => p
            .Add(c => c.Ticket, ticket ?? TestData.Model())
            .Add(c => c.OnState, state => _states.Add(state))
            .Add(c => c.OnConflict, () => _conflicts++)
            .Add(c => c.OnGone, () => _gone++));
    }

    private static IReadOnlyList<string> MenuItems(IRenderedComponent<TicketActions> cut) =>
        cut.FindAll("ul.ts-menu button").Select(b => Regex.Replace(b.TextContent, @"\s+", " ").Trim()).ToList();

    private static void OpenDialog(IRenderedComponent<TicketActions> cut, string menuItem) =>
        cut.FindAll("ul.ts-menu button").Single(b => b.TextContent.Contains(menuItem)).Click();

    private static AngleSharp.Dom.IElement Dialog(IRenderedComponent<TicketActions> cut, string titleContains) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.Contains(titleContains));

    private static void Type(AngleSharp.Dom.IElement dialog, string text) => dialog.QuerySelector("input")!.Input(text);

    private static AngleSharp.Dom.IElement Confirm(AngleSharp.Dom.IElement dialog) =>
        dialog.QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    [Fact]
    public void A_plain_agent_gets_spam_and_nothing_destructive_not_even_in_hidden_markup()
    {
        var cut = RenderActions(admin: false);

        MenuItems(cut).ShouldBe(["Mark as spam"]);
        cut.Markup.ShouldNotContain("Delete");
        cut.Markup.ShouldNotContain("Erase");
        cut.FindAll("dialog").Count.ShouldBe(1);
        cut.FindAll("dialog input").ShouldBeEmpty();
    }

    [Fact]
    public void An_admin_gets_spam_delete_and_erase_with_the_danger_entries_marked_by_a_word_and_a_class()
    {
        var cut = RenderActions(admin: true);

        MenuItems(cut).ShouldBe(["Mark as spam", "Delete ticket (permanent)", "Erase requester (permanent)"]);
        cut.FindAll("button.ts-menu-item--danger").Count.ShouldBe(2);
        cut.FindAll("dialog").Count.ShouldBe(3);
    }

    [Fact]
    public void The_menu_is_closed_until_opened_and_reports_its_state()
    {
        var cut = RenderActions();

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeTrue();
        cut.Find(".ts-actions > button").GetAttribute("aria-expanded").ShouldBe("false");

        cut.Find(".ts-actions > button").Click();

        cut.Find("ul.ts-menu").HasAttribute("hidden").ShouldBeFalse();
        cut.Find(".ts-actions > button").GetAttribute("aria-expanded").ShouldBe("true");
    }

    [Fact]
    public void Marking_spam_asks_first_naming_the_ticket_and_does_nothing_until_confirmed()
    {
        var cut = RenderActions();

        OpenDialog(cut, "Mark as spam");

        var dialog = Dialog(cut, "Mark ORB-42 as spam?");
        dialog.TextContent.ShouldContain("This hides ORB-42 from the normal queue views and flags it as spam. You can restore it from the Spam view.");
        dialog.QuerySelector("input").ShouldBeNull();
        Dialogs.VerifyInvoke("open", 1);
        _tickets.ReceivedCalls().ShouldBeEmpty();

        Confirm(dialog).Click();

        _tickets.Received(1).SetSpamAsync(TestData.TicketId, Arg.Is<MarkTicketSpamRequest>(r => r.IsSpam == true && r.RowVersion == 7u), Arg.Any<CancellationToken>());
        _navigation.Uri.ShouldEndWith("/queue");
        StatusMessages.Current.ShouldBe("Marked ORB-42 as spam");
    }

    [Fact]
    public void Cancelling_or_pressing_escape_in_any_dialog_makes_no_call()
    {
        var cut = RenderActions(admin: true);

        OpenDialog(cut, "Mark as spam");
        Dialog(cut, "Mark ORB-42").QuerySelector("button.btn-outline-secondary")!.Click();
        OpenDialog(cut, "Delete ticket");
        Dialog(cut, "Delete ORB-42").TriggerEvent("oncancel", EventArgs.Empty);
        OpenDialog(cut, "Erase requester");
        Dialog(cut, "Erase this requester").QuerySelector("button.btn-outline-secondary")!.Click();

        _tickets.ReceivedCalls().ShouldBeEmpty();
        _requesters.ReceivedCalls().ShouldBeEmpty();
        _navigation.Uri.ShouldEndWith("/tickets/ORB-42");
        Dialogs.VerifyInvoke("close", 3);
    }

    [Fact]
    public void Deleting_needs_the_ticket_number_typed_and_only_then_calls_delete_once_and_goes_to_the_queue()
    {
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Delete ticket");
        var dialog = Dialog(cut, "Delete ORB-42 permanently?");

        dialog.TextContent.ShouldContain("This permanently removes the ticket with all of its messages and attachments.");
        dialog.QuerySelector("label")!.TextContent.ShouldBe("Type ORB-42 to confirm");
        Confirm(dialog).HasAttribute("disabled").ShouldBeTrue();
        Confirm(dialog).Click();
        _tickets.ReceivedCalls().ShouldBeEmpty();

        Type(dialog, "ORB-4");
        Confirm(Dialog(cut, "Delete ORB-42")).HasAttribute("disabled").ShouldBeTrue();
        Type(Dialog(cut, "Delete ORB-42"), "orb-42");
        Confirm(Dialog(cut, "Delete ORB-42")).Click();

        _tickets.Received(1).DeleteAsync(TestData.TicketId, Arg.Any<CancellationToken>());
        _navigation.Uri.ShouldEndWith("/queue");
        StatusMessages.Current.ShouldBe("Deleted ORB-42");
    }

    [Fact]
    public void Erasing_needs_the_requester_email_typed_names_every_ticket_and_shows_no_counts()
    {
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Erase requester");
        var dialog = Dialog(cut, "Erase this requester?");

        dialog.TextContent.ShouldContain("ada@example.com");
        dialog.TextContent.ShouldContain("every ticket from this requester");
        Regex.IsMatch(dialog.TextContent, @"\d+\s+(tickets?|attachments?|messages?)").ShouldBeFalse("the API has no counts to show");
        dialog.QuerySelector("label")!.TextContent.ShouldBe("Type ada@example.com to confirm");
        Confirm(dialog).HasAttribute("disabled").ShouldBeTrue();

        Type(dialog, "ADA@example.com");
        Confirm(Dialog(cut, "Erase this requester")).Click();

        _requesters.Received(1).EraseAsync(TestData.RequesterId, Arg.Any<CancellationToken>());
        _navigation.Uri.ShouldEndWith("/queue");
        StatusMessages.Current.ShouldBe("Erased the requester of ORB-42");
        StatusMessages.Current!.ShouldNotContain("ada@example.com");
    }

    [Fact]
    public void Enter_cannot_confirm_a_destructive_dialog_it_has_no_form_and_no_submit_button()
    {
        var cut = RenderActions(admin: true);

        foreach (var dialog in cut.FindAll("dialog"))
        {
            dialog.QuerySelectorAll("form").ShouldBeEmpty();
            dialog.QuerySelectorAll("button").ShouldAllBe(b => b.GetAttribute("type") == "button");
        }

        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");
        _tickets.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void A_double_click_on_confirm_deletes_once_and_the_dialog_is_inert_while_it_runs()
    {
        var gate = new TaskCompletionSource<Result>();
        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");

        Confirm(Dialog(cut, "Delete ORB-42")).Click();
        var dialog = Dialog(cut, "Delete ORB-42");
        dialog.QuerySelectorAll("button").ShouldAllBe(b => b.HasAttribute("disabled"));
        dialog.QuerySelector("input")!.HasAttribute("disabled").ShouldBeTrue();
        dialog.TriggerEvent("oncancel", EventArgs.Empty);
        Confirm(dialog).Click();

        _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.DeleteAsync)).ShouldBe(1);
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => _navigation.Uri.ShouldEndWith("/queue"));
    }

    [Fact]
    public void A_failed_delete_keeps_the_dialog_open_shows_why_and_changes_nothing()
    {
        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("boom", "The API is unavailable."));
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");

        Confirm(Dialog(cut, "Delete ORB-42")).Click();

        Dialog(cut, "Delete ORB-42").QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't delete the ticket. Nothing was changed. The API is unavailable.");
        _navigation.Uri.ShouldEndWith("/tickets/ORB-42");
        StatusMessages.Current.ShouldBeNull();
        Dialogs.VerifyNotInvoke("close");
    }

    [Fact]
    public void A_failed_erase_keeps_the_dialog_open_and_a_ticket_deleted_meanwhile_is_handed_to_the_page()
    {
        _requesters.EraseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("requester-not-found", "No such requester.", ResultErrorKind.NotFound));
        var cut = RenderActions(admin: true);
        OpenDialog(cut, "Erase requester");
        Type(Dialog(cut, "Erase this requester"), "ada@example.com");

        Confirm(Dialog(cut, "Erase this requester")).Click();

        Dialog(cut, "Erase this requester").QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't erase the requester. Nothing was changed. No such requester.");
        _gone.ShouldBe(0);

        _tickets.DeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.TicketNotFound, "Gone.", ResultErrorKind.NotFound));
        OpenDialog(cut, "Delete ticket");
        Type(Dialog(cut, "Delete ORB-42"), "ORB-42");
        Confirm(Dialog(cut, "Delete ORB-42")).Click();

        _gone.ShouldBe(1);
    }

    [Fact]
    public void A_spam_conflict_closes_the_dialog_and_raises_the_conflict_without_navigating()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderActions();
        OpenDialog(cut, "Mark as spam");

        Confirm(Dialog(cut, "Mark ORB-42")).Click();

        _conflicts.ShouldBe(1);
        _navigation.Uri.ShouldEndWith("/tickets/ORB-42");
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void Not_spam_is_offered_to_an_agent_on_a_flagged_ticket_needs_no_dialog_and_stays_on_the_ticket()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(isSpam: false, rowVersion: 8)));
        var cut = RenderActions(admin: false, TestData.Model(isSpam: true));

        MenuItems(cut).ShouldBe(["Not spam u"]);
        cut.FindAll("dialog").ShouldBeEmpty();
        cut.Find("ul.ts-menu button").Click();

        _tickets.Received(1).SetSpamAsync(TestData.TicketId, Arg.Is<MarkTicketSpamRequest>(r => r.IsSpam == false && r.RowVersion == 7u), Arg.Any<CancellationToken>());
        _states.ShouldHaveSingleItem().IsSpam.ShouldBeFalse();
        StatusMessages.Current.ShouldBe("Restored ORB-42 from spam");
        _navigation.Uri.ShouldEndWith("/tickets/ORB-42");
        Dialogs.VerifyNotInvoke("open");
    }

    [Fact]
    public void A_failed_not_spam_says_so_in_the_status_bar_and_changes_nothing()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketStateDto>("boom", "The API is unavailable."));
        var cut = RenderActions(admin: false, TestData.Model(isSpam: true));

        cut.Find("ul.ts-menu button").Click();

        StatusMessages.Current.ShouldBe("Couldn't restore the ticket from spam. The API is unavailable.");
        _states.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_u_key_restores_a_flagged_ticket_once_and_is_ignored_in_every_other_case()
    {
        RenderActions(admin: false, TestData.Model(isSpam: true));

        await PressAsync("u", typing: true);
        ShortcutService.SingleKeyEnabled = false;
        await PressAsync("u");
        ShortcutService.SingleKeyEnabled = true;
        _tickets.ReceivedCalls().ShouldBeEmpty();

        await PressAsync("u");

        await _tickets.Received(1).SetSpamAsync(TestData.TicketId, Arg.Is<MarkTicketSpamRequest>(r => r.IsSpam == false), Arg.Any<CancellationToken>());
        _states.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(false, TicketStatuses.Open)]
    [InlineData(true, TicketStatuses.Closed)]
    public async Task The_u_key_does_nothing_on_a_ticket_that_is_not_flagged_or_is_closed(bool isSpam, string status)
    {
        RenderActions(admin: false, TestData.Model(status, isSpam: isSpam));

        await PressAsync("u");

        _tickets.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 2)]
    public void A_closed_ticket_offers_an_agent_nothing_and_an_admin_only_delete_and_erase(bool admin, int expectedEntries)
    {
        var cut = RenderActions(admin, TestData.Model(TicketStatuses.Closed));

        cut.FindAll("ul.ts-menu button").Count.ShouldBe(expectedEntries);
        if (!admin)
        {
            cut.Markup.Trim().ShouldBeEmpty();
        }
        else
        {
            MenuItems(cut).ShouldBe(["Delete ticket (permanent)", "Erase requester (permanent)"]);
        }
    }
}
```

`tests/TechStrap.Admin.Tests/Components/NotSpamQueueTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Queue;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>PHASE-07 T22 on the queue side: the Spam view's Not spam (the u key and the row button).</summary>
public sealed class NotSpamQueueTests : AdminComponentTest
{
    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly NavigationManager _navigation;

    public NotSpamQueueTests()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.Page(
            [TestData.Summary("ORB-1", isSpam: true), TestData.Summary("ORB-2", "Free money", isSpam: true)])));
        _tickets.GetCountsAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Counts(spam: 2)), TestData.Ok(TestData.Counts(spam: 1)));
        _tickets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.Detail("ORB-1", isSpam: true, rowVersion: 11)));
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.State(isSpam: false, rowVersion: 12)));
        var products = Substitute.For<IProductsClient>();
        var tags = Substitute.For<ITagsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([]));
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([]));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(products);
        Services.AddSingleton(tags);
        JSInterop.SetupModule("./js/queue.js").SetupVoid("scrollSelectedIntoView", _ => true).SetVoidResult();
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<TicketQueuePage> RenderQueue(string view)
    {
        _navigation.NavigateTo($"/queue/{view}");
        return Render<TicketQueuePage>(p => p.Add(c => c.View, view));
    }

    private int SpamWrites() =>
        _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.SetSpamAsync));

    [Fact]
    public void Only_the_Spam_view_has_the_Not_spam_button_and_its_column()
    {
        var spam = RenderQueue("spam");

        spam.FindAll("tbody tr").ShouldAllBe(row => row.QuerySelector("td.ts-col-actions button")!.TextContent.Contains("Not spam"));
        spam.FindAll("thead th").Count.ShouldBe(10);

        var open = Render<TicketQueuePage>(p => p.Add(c => c.View, "open"));
        open.FindAll("td.ts-col-actions").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_u_key_restores_the_selected_row_with_one_read_one_write_and_no_dialog()
    {
        var cut = RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        await _tickets.Received(1).GetAsync("ORB-1", Arg.Any<CancellationToken>());
        await _tickets.Received(1).SetSpamAsync(
            Arg.Any<Guid>(), Arg.Is<MarkTicketSpamRequest>(r => r.IsSpam == false && r.RowVersion == 11u), Arg.Any<CancellationToken>());
        cut.FindAll("tbody tr").Select(r => r.GetAttribute("data-ticket")).ShouldBe(["ORB-2"]);
        StatusMessages.Current.ShouldBe("Restored ORB-1 from spam");
        cut.FindAll("dialog").ShouldBeEmpty();
        cut.Find("p.visually-hidden[role=status]").TextContent.ShouldBe("1 of 1 tickets");
        cut.Find(".ts-tab--spam .ts-count").TextContent.ShouldBe("1");
        cut.Find("tr[aria-current=true]").GetAttribute("data-ticket").ShouldBe("ORB-2");
    }

    [Fact]
    public void The_row_button_does_the_same_as_the_key()
    {
        var cut = RenderQueue("spam");

        cut.FindAll("td.ts-col-actions button")[1].Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Select(r => r.GetAttribute("data-ticket")).ShouldBe(["ORB-1"]));
        StatusMessages.Current.ShouldBe("Restored ORB-2 from spam");
        SpamWrites().ShouldBe(1);
    }

    [Fact]
    public async Task The_last_restored_row_leaves_the_plain_No_spam_state()
    {
        _tickets.ListAsync(Arg.Any<ListTicketsRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Page([TestData.Summary("ORB-1", isSpam: true)])));
        var cut = RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No spam");
        cut.FindAll(".ts-window").ShouldBeEmpty();
    }

    [Fact]
    public async Task The_u_key_does_nothing_with_no_selection_while_typing_with_the_layer_off_or_outside_the_Spam_view()
    {
        var spam = RenderQueue("spam");

        await PressAsync("u");
        await PressAsync("j");
        await PressAsync("u", typing: true);
        ShortcutService.SingleKeyEnabled = false;
        await PressAsync("u");

        SpamWrites().ShouldBe(0);
        spam.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_u_key_does_nothing_in_a_normal_view()
    {
        RenderQueue("open");
        await PressAsync("j");

        await PressAsync("u");

        SpamWrites().ShouldBe(0);
        await _tickets.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pressing_u_twice_while_the_first_is_running_writes_once()
    {
        var gate = new TaskCompletionSource<Result<TicketStateDto>>();
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        RenderQueue("spam");
        await PressAsync("j");

        var first = PressAsync("u");
        await PressAsync("u");

        SpamWrites().ShouldBe(1);
        gate.SetResult(TestData.Ok(TestData.State(isSpam: false)));
        await first;
    }

    [Fact]
    public async Task A_conflict_keeps_the_row_and_tells_the_agent_to_open_the_ticket()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict));
        var cut = RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        cut.Find("[role=alert] p").TextContent.ShouldBe("ORB-1 changed meanwhile. Open it to review, then restore it from spam.");
        cut.FindAll("tbody tr").Count.ShouldBe(2);
        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public async Task A_failed_read_or_write_shows_the_API_message_and_keeps_the_row()
    {
        _tickets.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<TicketDetailDto>("boom", "The API is unavailable."));
        var cut = RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        cut.Find("[role=alert] p").TextContent.ShouldBe("Couldn't restore the ticket from spam. The API is unavailable.");
        SpamWrites().ShouldBe(0);
        cut.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_closed_ticket_refused_by_the_API_shows_the_API_s_reason()
    {
        _tickets.SetSpamAsync(Arg.Any<Guid>(), Arg.Any<MarkTicketSpamRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TicketStateDto>(ApiErrorCodes.TicketClosed, "Closed tickets are read-only.", ResultErrorKind.Conflict));
        var cut = RenderQueue("spam");
        await PressAsync("j");

        await PressAsync("u");

        cut.Find("[role=alert] p").TextContent.ShouldBe("Couldn't restore the ticket from spam. Closed tickets are read-only.");
        cut.FindAll("tbody tr").Count.ShouldBe(2);
    }
}
```

`tests/TechStrap.Admin.Tests/Components/ShortcutServiceTests.cs` and `ShellComponentTests.cs`: `u` is a shortcut and the help lists ten rows:

`tests/TechStrap.Admin.Tests/Components/ShortcutServiceTests.cs` changes like this:

```diff
@@ -20,6 +20,8 @@
     [InlineData("r", ShortcutAction.Reply)]
     [InlineData("n", ShortcutAction.Note)]
     [InlineData("e", ShortcutAction.FocusAssignee)]
+    [InlineData("u", ShortcutAction.NotSpam)]
+    [InlineData("U", ShortcutAction.NotSpam)]
     [InlineData("?", ShortcutAction.Help)]
     [InlineData("Escape", ShortcutAction.Escape)]
     public void A_single_key_maps_to_its_action_when_the_user_is_not_typing(string key, ShortcutAction expected) =>
@@ -29,6 +31,7 @@
     [InlineData("j")]
     [InlineData("/")]
     [InlineData("r")]
+    [InlineData("u")]
     [InlineData("Enter")]
     [InlineData("Escape")]
     public void While_typing_no_single_key_is_a_shortcut(string key) =>
@@ -113,7 +116,7 @@
     {
         var listed = ShortcutCatalog.All.SelectMany(entry => entry.Keys.Split(" / ")).ToHashSet(StringComparer.OrdinalIgnoreCase);
 
-        foreach (var key in new[] { "j", "k", "Enter", "/", "r", "n", "e", "?", "Esc", "Ctrl+Enter" })
+        foreach (var key in new[] { "j", "k", "Enter", "/", "r", "n", "e", "u", "?", "Esc", "Ctrl+Enter" })
         {
             listed.ShouldContain(key);
         }
```

`tests/TechStrap.Admin.Tests/Components/ShellComponentTests.cs` changes like this:

```diff
@@ -99,7 +99,8 @@
         cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 1));
 
         cut.Find("dialog.ts-dialog h2").TextContent.ShouldBe("Keyboard shortcuts");
-        cut.FindAll(".ts-shortcut-table tbody tr").Count.ShouldBe(9);
+        cut.FindAll(".ts-shortcut-table tbody tr").Count.ShouldBe(10);
+        cut.FindAll(".ts-shortcut-table kbd").Select(k => k.TextContent).ShouldContain("u");
 
         cut.Find("dialog button").Click();
 
```

`tests/TechStrap.Admin.Tests/Components/TicketDetailPageTests.cs` and `TicketDetailConflictTests.cs`: the page now renders `TicketActions`, which needs the requesters client:

`tests/TechStrap.Admin.Tests/Components/TicketDetailPageTests.cs` changes like this:

```diff
@@ -32,6 +32,7 @@
         Services.AddSingleton(tags);
         Services.AddTicketFeatures();
         Services.AddSingleton(AgentSessions.SignedIn());
+        Services.AddSingleton(Substitute.For<IRequestersClient>());
         ShowTicket(TestData.Detail());
         _navigation = Services.GetRequiredService<NavigationManager>();
     }
```

`tests/TechStrap.Admin.Tests/Components/TicketDetailConflictTests.cs` changes like this:

```diff
@@ -32,6 +32,7 @@
         Services.AddSingleton(tags);
         Services.AddTicketFeatures();
         Services.AddSingleton(AgentSessions.SignedIn());
+        Services.AddSingleton(Substitute.For<IRequestersClient>());
         ShowTicket(TestData.Detail());
     }
 
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "DestructiveActionTests|NotSpamQueueTests|ShortcutServiceTests|ShellComponentTests|TicketQueuePageTests|TicketDetailPageTests|TicketDetailConflictTests"`
Expected: a build failure (`TicketActions`, `ActionsCopy`, `ShortcutAction.NotSpam` do not exist).

- [ ] **Step 3: Implement**

1. The copy and the component:

`src/TechStrap.Admin/Features/Tickets/ActionsCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Tickets;

/// <summary>
/// The copy of the spam, delete and erase flows. The dialogs name the object and the consequence; the irreversible ones also ask for a typed confirmation.
/// There are no counts ("12 tickets, 31 attachments"): the API offers none, so the dialogs say "every ticket from this requester" instead (recorded gap, D-040).
/// </summary>
public static class ActionsCopy
{
    public const string MoreActions = "More actions";
    public const string MarkSpam = "Mark as spam";
    public const string NotSpam = "Not spam";
    public const string DeleteTicket = "Delete ticket (permanent)";
    public const string EraseRequester = "Erase requester (permanent)";

    public const string SpamConfirm = "Mark as spam";
    public const string DeleteConfirm = "Delete ticket";
    public const string EraseConfirm = "Erase requester";

    public static string SpamTitle(string number) => $"Mark {number} as spam?";

    public static string SpamBody(string number) =>
        $"This hides {number} from the normal queue views and flags it as spam. You can restore it from the Spam view.";

    public static string DeleteTitle(string number) => $"Delete {number} permanently?";

    public const string DeleteBody =
        "This permanently removes the ticket with all of its messages and attachments. Follow-up tickets stay, but lose their link to it. This can't be undone.";

    public const string EraseTitle = "Erase this requester?";

    public static string EraseBody(string email) =>
        $"This erases {email} from every ticket from this requester: their name and address, the subjects and the messages they wrote, and their attachments are removed. " +
        "Agent replies and internal notes stay. This can't be undone.";

    public static string SpamFailed(string detail) => $"Couldn't mark the ticket as spam. Nothing was changed. {detail}";

    public static string DeleteFailed(string detail) => $"Couldn't delete the ticket. Nothing was changed. {detail}";

    public static string EraseFailed(string detail) => $"Couldn't erase the requester. Nothing was changed. {detail}";

    public static string NotSpamFailed(string detail) => $"Couldn't restore the ticket from spam. {detail}";

    public static string MarkedSpam(string number) => $"Marked {number} as spam";

    public static string Restored(string number) => $"Restored {number} from spam";

    public static string Deleted(string number) => $"Deleted {number}";

    public static string Erased(string number) => $"Erased the requester of {number}";

    public static string ChangedMeanwhile(string number) => $"{number} changed meanwhile. Open it to review, then restore it from spam.";
}
```

`src/TechStrap.Admin/Features/Tickets/TicketActions.razor`

```razor
@if (HasAnyAction)
{
    <div class="ts-actions">
        <button type="button" class="btn btn-outline-secondary" aria-haspopup="menu" aria-expanded="@(_menuOpen ? "true" : "false")" @onclick="ToggleMenu">@ActionsCopy.MoreActions</button>
        <ul class="ts-menu" hidden="@(!_menuOpen)">
            @if (CanMarkSpam)
            {
                <li><button type="button" class="ts-menu-item" @onclick="() => Open(ActionDialog.Spam)">@ActionsCopy.MarkSpam</button></li>
            }
            @if (CanRestore)
            {
                <li><button type="button" class="ts-menu-item" @onclick="RestoreAsync">@ActionsCopy.NotSpam <Kbd>u</Kbd></button></li>
            }
            @if (Session.IsAdmin)
            {
                <li><button type="button" class="ts-menu-item ts-menu-item--danger" @onclick="() => Open(ActionDialog.Delete)">@ActionsCopy.DeleteTicket</button></li>
                <li><button type="button" class="ts-menu-item ts-menu-item--danger" @onclick="() => Open(ActionDialog.Erase)">@ActionsCopy.EraseRequester</button></li>
            }
        </ul>
    </div>

    @if (CanMarkSpam)
    {
        <ConfirmDialog Open="@(_dialog == ActionDialog.Spam)" Title="@ActionsCopy.SpamTitle(Ticket.Number)" ConfirmLabel="@ActionsCopy.SpamConfirm"
                       Busy="_busy" Error="@_error" OnConfirm="ConfirmSpamAsync" OnCancel="Close">
            <p>@ActionsCopy.SpamBody(Ticket.Number)</p>
        </ConfirmDialog>
    }

    @if (Session.IsAdmin)
    {
        <ConfirmDialog Open="@(_dialog == ActionDialog.Delete)" Title="@ActionsCopy.DeleteTitle(Ticket.Number)" ConfirmLabel="@ActionsCopy.DeleteConfirm" Danger="true"
                       RequiredText="@Ticket.Number" Busy="_busy" Error="@_error" OnConfirm="ConfirmDeleteAsync" OnCancel="Close">
            <p>@ActionsCopy.DeleteBody</p>
        </ConfirmDialog>
        <ConfirmDialog Open="@(_dialog == ActionDialog.Erase)" Title="@ActionsCopy.EraseTitle" ConfirmLabel="@ActionsCopy.EraseConfirm" Danger="true"
                       RequiredText="@Ticket.Requester.Email" Busy="_busy" Error="@_error" OnConfirm="ConfirmEraseAsync" OnCancel="Close">
            <p>@ActionsCopy.EraseBody(Ticket.Requester.Email)</p>
        </ConfirmDialog>
    }
}
```

`src/TechStrap.Admin/Features/Tickets/TicketActions.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Tickets;

public enum ActionDialog
{
    None,
    Spam,
    Delete,
    Erase,
}

/// <summary>
/// The overflow menu of a ticket and the dialogs behind it, in the three tiers of UX-BRIEF-admin. Not spam is reversible and has no dialog. Mark as spam asks for a confirmation that names
/// the ticket. Delete and erase are Admin-only, irreversible, and need the ticket number or the requester's email typed: for an Agent they are not hidden but not rendered at all (no menu
/// entry, no dialog, nothing to trigger), and the API enforces the same rule. A failure leaves the dialog open and everything unchanged; only success navigates.
/// </summary>
public sealed partial class TicketActions : IDisposable
{
    private const string QueuePath = "/queue";

    private readonly CancellationTokenSource _lifetime = new();
    private ActionDialog _dialog;
    private bool _menuOpen;
    private bool _busy;
    private string? _error;

    [Inject]
    private ITicketsClient Tickets { get; set; } = default!;

    [Inject]
    private IRequestersClient Requesters { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private ShortcutService Shortcuts { get; set; } = default!;

    [Parameter, EditorRequired]
    public TicketDetailViewModel Ticket { get; set; } = default!;

    /// <summary>Raised with the API's state after Not spam; the page replaces its model from it.</summary>
    [Parameter]
    public EventCallback<TicketStateDto> OnState { get; set; }

    [Parameter]
    public EventCallback OnConflict { get; set; }

    [Parameter]
    public EventCallback OnGone { get; set; }

    // Spam on a Closed ticket is refused by the API (409 ticket-closed), so the entries are not offered there.
    private bool CanMarkSpam => !Ticket.IsSpam && !Ticket.IsClosed;

    private bool CanRestore => Ticket.IsSpam && !Ticket.IsClosed;

    private bool HasAnyAction => CanMarkSpam || CanRestore || Session.IsAdmin;

    protected override void OnInitialized() => Shortcuts.Pressed += OnShortcutAsync;

    private void ToggleMenu() => _menuOpen = !_menuOpen;

    private void Open(ActionDialog dialog)
    {
        _menuOpen = false;
        _error = null;
        _dialog = dialog;
    }

    private void Close()
    {
        if (!_busy)
        {
            _dialog = ActionDialog.None;
            _error = null;
        }
    }

    private Task ConfirmSpamAsync() =>
        RunAsync(
            ct => Tickets.SetSpamAsync(Ticket.Id, new MarkTicketSpamRequest(true, Ticket.RowVersion), ct),
            _ => LeaveForQueue(ActionsCopy.MarkedSpam(Ticket.Number)),
            ActionsCopy.SpamFailed);

    private Task ConfirmDeleteAsync() =>
        RunAsync(
            async ct => Lift(await Tickets.DeleteAsync(Ticket.Id, ct)),
            _ => LeaveForQueue(ActionsCopy.Deleted(Ticket.Number)),
            ActionsCopy.DeleteFailed);

    private Task ConfirmEraseAsync() =>
        RunAsync(
            async ct => Lift(await Requesters.EraseAsync(Ticket.Requester.Id, ct)),
            _ => LeaveForQueue(ActionsCopy.Erased(Ticket.Number)),
            ActionsCopy.EraseFailed);

    private async Task RestoreAsync()
    {
        _menuOpen = false;
        await RunAsync(
            ct => Tickets.SetSpamAsync(Ticket.Id, new MarkTicketSpamRequest(false, Ticket.RowVersion), ct),
            async state =>
            {
                StatusMessages.Show(ActionsCopy.Restored(Ticket.Number));
                await OnState.InvokeAsync(state);
            },
            ActionsCopy.NotSpamFailed,
            dialogOpen: false);
    }

    /// <summary>Runs one request. Success runs <paramref name="onSuccess"/>; a conflict or a missing ticket is handed to the page; anything else is shown and nothing changes.</summary>
    private async Task RunAsync<T>(Func<CancellationToken, Task<Result<T>>> send, Func<T, Task> onSuccess, Func<string, string> failure, bool dialogOpen = true)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _error = null;
        StateHasChanged();
        try
        {
            var result = await send(_lifetime.Token);
            if (result.IsSuccess)
            {
                _dialog = ActionDialog.None;
                await onSuccess(result.Value);
                return;
            }

            await HandleFailureAsync(result.Errors[0], failure, dialogOpen);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The screen was closed mid-request.
        }
        finally
        {
            _busy = false;
        }
    }

    // Delete and erase answer with a plain Result; this lets them share the one request runner.
    private static Result<bool> Lift(Result result) => result.IsSuccess ? Result<bool>.Success(true) : Result<bool>.Failure(result.Errors[0]);

    private async Task HandleFailureAsync(ResultError error, Func<string, string> failure, bool dialogOpen)
    {
        if (error.Code == ApiErrorCodes.ConcurrencyConflict)
        {
            _dialog = ActionDialog.None;
            await OnConflict.InvokeAsync();
        }
        else if (error.Code == ApiErrorCodes.TicketNotFound)
        {
            _dialog = ActionDialog.None;
            await OnGone.InvokeAsync();
        }
        else if (dialogOpen)
        {
            _error = failure(error.Message);
        }
        else
        {
            StatusMessages.Show(failure(error.Message));
        }
    }

    private Task LeaveForQueue(string message)
    {
        StatusMessages.Show(message);
        Navigation.NavigateTo(QueuePath);
        return Task.CompletedTask;
    }

    private async Task OnShortcutAsync(ShortcutAction action)
    {
        if (action == ShortcutAction.NotSpam && CanRestore && !_busy)
        {
            await InvokeAsync(async () =>
            {
                await RestoreAsync();
                StateHasChanged();
            });
        }
    }

    public void Dispose()
    {
        Shortcuts.Pressed -= OnShortcutAsync;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

2. Render the actions on the page, under the header (the file already carries the composer, the banner and the sidebar):

`src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor` changes like this:

```diff
@@ -22,6 +22,7 @@
         {
             <ErrorState Message="@_error" OnRetry="RefreshAsync" />
         }
+        <TicketActions Ticket="_model" OnState="OnStateChangedAsync" OnConflict="OnConflictAsync" OnGone="RefreshAsync" />
         <ConflictBanner State="_conflict" LatestChange="@_latestChange" OnReload="ReloadAfterConflictAsync" OnDismiss="DismissConflict" />
         <div class="ts-ticket-layout">
             <section class="ts-conversation" aria-label="Conversation">
```

3. The `u` key and its help row:

`src/TechStrap.Admin/Features/Shell/ShortcutService.cs` changes like this:

```diff
@@ -11,6 +11,7 @@
     Reply,
     Note,
     FocusAssignee,
+    NotSpam,
     Send,
     Escape,
     Help,
@@ -80,6 +81,7 @@
             "r" => ShortcutAction.Reply,
             "n" => ShortcutAction.Note,
             "e" => ShortcutAction.FocusAssignee,
+            "u" => ShortcutAction.NotSpam,
             "?" => ShortcutAction.Help,
             _ => null,
         };
```

`src/TechStrap.Admin/Features/Shell/ShortcutCatalog.cs` changes like this:

```diff
@@ -14,6 +14,7 @@
         new("r", "Ticket", "Public reply: open the tab and focus the box"),
         new("n", "Ticket", "Internal note: open the tab and focus the box"),
         new("e", "Ticket", "Focus the assignee control"),
+        new("u", "Spam view, flagged ticket", "Not spam: restore the selected ticket from spam, no dialog"),
         new("Ctrl+Enter", "Reply box", "Send in the current mode"),
         new("Esc", "Anywhere", "Leave a field (your text is kept), close a dialog, or go back to the queue"),
         new("?", "Anywhere", "Show this list"),
```

4. Not spam in the queue (a button column in the Spam view, the key, the read-then-write):

`src/TechStrap.Admin/Features/Queue/TicketRow.razor` changes like this:

```diff
@@ -23,4 +23,10 @@
         }
     </td>
     <td class="ts-col-activity"><RelativeTime When="Row.LastActivity" /></td>
+    @if (ShowNotSpam)
+    {
+        <td class="ts-col-actions">
+            <button type="button" class="btn btn-link" @onclick="() => OnNotSpam.InvokeAsync(Row)">Not spam <Kbd>u</Kbd></button>
+        </td>
+    }
 </tr>
```

`src/TechStrap.Admin/Features/Queue/TicketRow.razor.cs` changes like this:

```diff
@@ -14,4 +14,11 @@
     /// <summary>The keyboard selection (<c>j</c>/<c>k</c>); it never reorders rows.</summary>
     [Parameter]
     public bool Selected { get; set; }
+
+    /// <summary>The Spam view only: a visible Not spam button, the mouse equivalent of the <c>u</c> key.</summary>
+    [Parameter]
+    public bool ShowNotSpam { get; set; }
+
+    [Parameter]
+    public EventCallback<TicketRowViewModel> OnNotSpam { get; set; }
 }
```

`src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor` changes like this:

```diff
@@ -35,12 +35,16 @@
                             <th scope="col">Priority</th>
                             <th scope="col">Assignee</th>
                             <th scope="col">Last activity</th>
+                            @if (IsSpamView)
+                            {
+                                <th scope="col"><span class="visually-hidden">Actions</span></th>
+                            }
                         </tr>
                     </thead>
                     <tbody>
                         @for (var i = 0; i < _rows.Count; i++)
                         {
-                            <TicketRow @key="_rows[i].Id" Row="_rows[i]" Selected="@(i == _selected)" />
+                            <TicketRow @key="_rows[i].Id" Row="_rows[i]" Selected="@(i == _selected)" ShowNotSpam="IsSpamView" OnNotSpam="NotSpamAsync" />
                         }
                     </tbody>
                 </table>
```

`src/TechStrap.Admin/Features/Queue/TicketQueuePage.razor.cs` changes like this:

```diff
@@ -2,6 +2,7 @@
 using Microsoft.JSInterop;
 using TechStrap.Admin.Clients;
 using TechStrap.Admin.Features.Shell;
+using TechStrap.Admin.Features.Tickets;
 using TechStrap.Contracts.Tickets;
 using TechStrap.Contracts.Paging;
 using TechStrap.Contracts.Products;
@@ -33,6 +34,7 @@
     private bool _loading;
     private bool _lookupsLoaded;
     private bool _scrollSelected;
+    private bool _notSpamBusy;
     private int _selected = -1;
 
     [Inject]
@@ -49,6 +51,9 @@
 
     [Inject]
     private ShortcutService Shortcuts { get; set; } = default!;
+
+    [Inject]
+    private StatusMessageService StatusMessages { get; set; } = default!;
 
     [Inject]
     private IJSRuntime Js { get; set; } = default!;
@@ -74,6 +79,8 @@
 
     [SupplyParameterFromQuery(Name = QueueQueryKeys.Page)]
     public int? PageNumber { get; set; }
+
+    private bool IsSpamView => _filter.View == TicketViews.Spam;
 
     private bool IsCaughtUpView => _filter.View is TicketViews.Unassigned or TicketViews.Mine or TicketViews.Open;
 
@@ -227,10 +234,73 @@
                 case ShortcutAction.FocusSearch when _filterBar is not null:
                     await _filterBar.FocusSearchAsync();
                     break;
+                case ShortcutAction.NotSpam when IsSpamView && _selected >= 0 && _selected < _rows.Count:
+                    await NotSpamAsync(_rows[_selected]);
+                    break;
             }
 
             StateHasChanged();
         });
+    }
+
+    /// <summary>
+    /// Not spam from the Spam view (the <c>u</c> key or the row button): no dialog, the status is unchanged. A queue row carries no RowVersion (the summary DTO has none) and the API
+    /// requires one for this write, so the ticket is read first and the write is made against what was read; a 409 means it changed since and the agent should open it.
+    /// </summary>
+    private async Task NotSpamAsync(TicketRowViewModel row)
+    {
+        if (_notSpamBusy)
+        {
+            return;
+        }
+
+        _notSpamBusy = true;
+        try
+        {
+            var ticket = await Tickets.GetAsync(row.Number, _lifetime.Token);
+            if (ticket.IsFailure)
+            {
+                _error = ActionsCopy.NotSpamFailed(ticket.Errors[0].Message);
+                return;
+            }
+
+            var state = await Tickets.SetSpamAsync(row.Id, new MarkTicketSpamRequest(false, ticket.Value.RowVersion), _lifetime.Token);
+            if (state.IsFailure)
+            {
+                var error = state.Errors[0];
+                _error = error.Code == ApiErrorCodes.ConcurrencyConflict ? ActionsCopy.ChangedMeanwhile(row.Number) : ActionsCopy.NotSpamFailed(error.Message);
+                return;
+            }
+
+            _error = null;
+            RemoveRow(row);
+            StatusMessages.Show(ActionsCopy.Restored(row.Number));
+            var counts = await Tickets.GetCountsAsync(_lifetime.Token);
+            if (counts.IsSuccess)
+            {
+                _counts = counts.Value;
+            }
+        }
+        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
+        {
+            // The page was closed mid-request.
+        }
+        finally
+        {
+            _notSpamBusy = false;
+        }
+    }
+
+    private void RemoveRow(TicketRowViewModel row)
+    {
+        _rows = _rows.Where(r => r.Id != row.Id).ToList();
+        if (_page is not null)
+        {
+            _page = _page with { Items = _page.Items.Where(t => t.Id != row.Id).ToList(), TotalCount = Math.Max(0, _page.TotalCount - 1) };
+        }
+
+        _selected = Math.Min(_selected, _rows.Count - 1);
+        _announcement = $"{_rows.Count} of {_page?.TotalCount ?? 0} tickets";
     }
 
     private void Select(int index)
```

- [ ] **Step 4: Run the tests**

Run the filter from Step 2, then the whole Admin test project:

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git diff --cached --stat   # after git add, before committing
git add src tests
git commit -m "feat(admin): spam, not-spam, delete and erase flows with typed confirmations" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---
### Task 13: Host smoke tests, leak checks and closing docs

**Review Focus pins:** (1) no ticket call before `GET /api/agents/me` succeeds, and NoAccess wins (`AdminHostSmokeTests`); (2) no token in a log, a page, the attachment response or a rendered header (`AdminHostSmokeTests` and `AdminLeakTests`).

**Files:**
- Create: `tests/TechStrap.Admin.Tests/AdminHostSmokeTests.cs`
- Create: `tests/TechStrap.Api.Tests/AdminLeakTests.cs`
- Create: `docs/development/ADMIN-APP.md`
- Modify: `docs/architecture/PHASE-07-admin-app.md`, `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, `docs/architecture/00-DISCOVERY-INDEX.md`, `docs/development/TICKET-OPERATIONS.md` (one line)
- Modify: `README.md` (one link, only if it has a docs list)

**Interfaces:**
- Consumes (Tasks 3 to 6, names as fixed by the controller):
  - `AdminFactory` in both test projects (`(environment, settings, configureServices)`), with `StubApiHandler Api`; the Api.Tests one also has `LogSink.Events` (Serilog events collected by the host).
  - `AdminTestPrincipal.Agent`, `.Admin`, `.Outsider` (each with `AccessToken`) and `client.SignedInAs(principal)`; the factories answer `GET /api/agents/me` through `Api.WithTestAgents()` by default (403 `agent-access-required` for the outsider).
  - `StubApiHandler.OnJson<T>(method, path, value)`, `On(method, path, request => response)`, `OnProblem(method, path, status, type, detail)`, `Requests` (`Method`, `Path`, `Query`, `Authorization`), an unconfigured route answers 404.
  - The pass-through `GET /attachments/{id:guid}` (Task 6) and the whole of Tasks 4 to 12.
- Produces: no production code. The docs below.

**Rules:**
1. **Prerendering is on**, so a plain `HttpClient` GET already contains the data the stub served, and every page loads its data twice in a real browser (prerender, then the circuit); the tests only see the first. The assertions are about the HTML and the order of API calls, not about interactivity (bUnit covers that).
2. **The token must actually be used for the leak test to mean anything**: `AdminLeakTests` first asserts the stub saw the bearer token, then scans pages, the download's body and headers, and every collected log event (rendered message, exception text and all properties) for it and for `Bearer `.
3. **Exact copy** is asserted only where the UX brief fixes it ("You don't have access to TechStrap." is Task 4's heading).
4. If a prerendered ticket page shows an error instead of the data (a stub path the Admin client calls that this file does not configure, for example a different list URL), the stub answers 404 and the test fails on its first content assertion. Read `factory.Api.Requests` to see the path, and add the route in `FactoryWithOneTicket`.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/AdminHostSmokeTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The whole Admin host with the fake sign-in and the stub API: what an agent, an admin, a refused user and an anonymous visitor are shown, in what order the API is
/// asked, and what the attachment pass-through returns. Pages are prerendered, so the HTML already contains the data the stub served.
/// </summary>
public sealed class AdminHostSmokeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TicketId = Guid.Parse("dddddddd-0000-0000-0000-000000000042");
    private static readonly Guid RequesterId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid AttachmentId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static HttpClient NoRedirectClient(AdminFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>The factory with a happy-path API behind it: one open ticket, one product, one agent, no tags.</summary>
    private static AdminFactory FactoryWithOneTicket()
    {
        var factory = new AdminFactory();
        var summary = new TicketSummaryDto(
            TicketId, "ORB-42", "Cannot log in", "Open", "Normal", OrbitlyId, "Orbitly", RequesterId, "ada@example.com", "Ada Lovelace",
            null, null, false, [], Now.AddDays(-1), Now.AddMinutes(-5));
        var detail = new TicketDetailDto(
            TicketId, "ORB-42", "Cannot log in", "Open", "Normal", OrbitlyId, "Orbitly", new TicketRequesterDto(RequesterId, "ada@example.com", "Ada Lovelace", null),
            null, null, false, [], "Email", null, null, false, Now.AddDays(-1), null, null, null, Now.AddMinutes(-5), 7,
            [new MessageDto(Guid.NewGuid(), "Requester", null, "Ada Lovelace", "Public", "<p>I cannot log in</p>", Now.AddDays(-1), [], [])],
            [new TicketEventDto(Guid.NewGuid(), "Created", "Requester", null, "Ada Lovelace", "{\"channel\":\"Email\"}", Now.AddDays(-1))]);
        var product = new ProductDto(OrbitlyId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 1);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/tickets", new PagedResponse<TicketSummaryDto>([summary], 1, 25, 1))
            .OnJson(HttpMethod.Get, "/api/tickets/counts", new TicketViewCountsResponse(1, 0, 1, 0, 1, 0))
            .OnJson(HttpMethod.Get, "/api/tickets/ORB-42", detail)
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, "/api/tags", (IReadOnlyList<TagDto>)[])
            .OnJson(HttpMethod.Get, "/api/agents", new PagedResponse<AgentListItemDto>([new AgentListItemDto(Guid.NewGuid(), "Sam Agent", "Sam Agent", null, null, null, null)], 1, 100, 1));
        return factory;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/queue/mine")]
    [InlineData("/tickets/ORB-42")]
    public async Task An_anonymous_visitor_is_sent_to_the_sign_in_landing_and_the_api_is_never_asked(string path)
    {
        await using var factory = FactoryWithOneTicket();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/signin");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_anonymous_download_is_refused_and_the_api_is_never_asked()
    {
        await using var factory = FactoryWithOneTicket();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync($"/attachments/{AttachmentId}", Ct);

        ((int)response.StatusCode).ShouldBeOneOf(302, 401);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_agent_sees_the_queue_and_the_api_was_asked_who_they_are_before_any_ticket_call()
    {
        await using var factory = FactoryWithOneTicket();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/", Ct);

        html.ShouldContain("class=\"ts-queue\"");
        html.ShouldContain("href=\"/tickets/ORB-42\"");
        html.ShouldContain("Cannot log in");
        html.ShouldContain("href=\"/queue/spam\"");
        html.ShouldNotContain("You don't have access");
        var paths = factory.Api.Requests.Select(r => r.Path).ToList();
        paths[0].ShouldBe("/api/agents/me");
        paths.IndexOf("/api/tickets").ShouldBeGreaterThan(0);
        factory.Api.Requests.ShouldAllBe(r => r.Authorization == "Bearer " + AdminTestPrincipal.Agent.AccessToken);
        factory.Api.Requests.First(r => r.Path == "/api/tickets").Query.ShouldContain("view=Unassigned");
    }

    [Fact]
    public async Task An_agent_opens_a_ticket_with_the_composer_and_the_controls_and_no_delete_or_erase()
    {
        await using var factory = FactoryWithOneTicket();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/tickets/ORB-42", Ct);

        var page = await new HtmlParser().ParseDocumentAsync(html, Ct);
        page.QuerySelector("h1.ts-ticket-subject")!.TextContent.ShouldBe("Cannot log in");
        page.QuerySelector(".ts-message-body")!.TextContent.ShouldBe("I cannot log in");
        page.QuerySelector("section.ts-composer").ShouldNotBeNull();
        page.QuerySelector("section.ts-sidebar").ShouldNotBeNull();
        html.ShouldContain("Mark as spam");
        html.ShouldNotContain("Delete ticket");
        html.ShouldNotContain("Erase requester");
        factory.Api.Requests[0].Path.ShouldBe("/api/agents/me");
    }

    [Fact]
    public async Task An_admin_also_gets_delete_and_erase()
    {
        await using var factory = FactoryWithOneTicket();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var html = await client.GetStringAsync("/tickets/ORB-42", Ct);

        html.ShouldContain("Delete ticket (permanent)");
        html.ShouldContain("Erase requester (permanent)");
        html.ShouldContain("Mark as spam");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/queue/spam")]
    [InlineData("/tickets/ORB-42")]
    public async Task A_user_the_api_refuses_gets_the_no_access_page_and_no_ticket_data_is_requested(string path)
    {
        await using var factory = FactoryWithOneTicket();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        using var response = await client.GetAsync(path, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        html.ShouldContain("You don't have access to TechStrap.");
        html.ShouldNotContain("Cannot log in");
        html.ShouldNotContain("class=\"ts-queue\"");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
    }

    [Fact]
    public async Task A_deactivated_agent_is_refused_on_the_ticket_page_and_nothing_else_is_asked()
    {
        await using var factory = FactoryWithOneTicket();
        factory.Api.OnProblem(HttpMethod.Get, "/api/agents/me", HttpStatusCode.Forbidden, "agent-inactive", "Deactivated.");
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var html = await client.GetStringAsync("/tickets/ORB-42", Ct);

        html.ShouldContain("has been deactivated");
        html.ShouldNotContain("Delete ticket");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
    }

    [Fact]
    public async Task The_attachment_pass_through_streams_a_forced_download_and_leaks_no_credentials()
    {
        await using var factory = FactoryWithOneTicket();
        byte[] bytes = [1, 2, 3, 4, 5];
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{AttachmentId}", _ =>
        {
            var upstream = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            upstream.Content.Headers.ContentType = new("image/png");
            upstream.Content.Headers.ContentDisposition = new("inline") { FileName = "shot.png" };
            return upstream;
        });
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync($"/attachments/{AttachmentId}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(bytes);
        response.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        var token = AdminTestPrincipal.Agent.AccessToken;
        response.Headers.Contains("Authorization").ShouldBeFalse();
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        response.Headers.SelectMany(h => h.Value).Concat(response.Content.Headers.SelectMany(h => h.Value)).ShouldAllBe(v => !v.Contains(token));
        factory.Api.Requests.Last().Authorization.ShouldBe("Bearer " + token);
    }

    [Fact]
    public async Task An_attachment_the_api_does_not_know_is_not_found()
    {
        await using var factory = FactoryWithOneTicket();
        factory.Api.OnProblem(HttpMethod.Get, $"/api/attachments/{AttachmentId}", HttpStatusCode.NotFound, "attachment-not-found", "No such attachment.");
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync($"/attachments/{AttachmentId}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
```
`tests/TechStrap.Api.Tests/AdminLeakTests.cs` (the Api.Tests `AdminFactory` is the one with the log sink):

```csharp
using System.Net;
using Serilog.Events;
using TechStrap.Contracts.Agents;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Api.Tests;

/// <summary>Review Focus 2 at the host: whatever the Admin does for a signed-in agent, the access token never reaches a log line, a page, or a download.</summary>
public sealed class AdminLeakTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid AttachmentId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    [Fact]
    public async Task The_access_token_appears_in_no_log_event_page_or_download()
    {
        await using var factory = new AdminFactory();
        factory.Api.OnProblem(HttpMethod.Get, "/api/tickets", HttpStatusCode.ServiceUnavailable, "api-unavailable", "Down.");
        factory.Api.On(HttpMethod.Get, $"/api/attachments/{AttachmentId}", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });
        var token = AdminTestPrincipal.Agent.AccessToken;
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var pages = new List<string>
        {
            await client.GetStringAsync("/", Ct),
            await client.GetStringAsync("/queue/spam", Ct),
            await client.GetStringAsync("/tickets/ORB-42", Ct),
        };
        using var download = await client.GetAsync($"/attachments/{AttachmentId}", Ct);

        factory.Api.Requests.ShouldContain(r => r.Authorization == "Bearer " + token, "the token must actually have been used, or this test proves nothing");
        pages.ShouldAllBe(html => !html.Contains(token));
        (await download.Content.ReadAsStringAsync(Ct)).ShouldNotContain(token);
        download.Headers.SelectMany(h => h.Value).ShouldAllBe(v => !v.Contains(token));
        factory.LogSink.Events.ShouldNotBeEmpty();
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(token) && !text.Contains("Bearer "));
    }
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter AdminHostSmokeTests` and `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter AdminLeakTests`
Expected: PASS. These tests exercise code that already exists (Tasks 4 to 12), so they are written green. To see them guard something, make each fail once on purpose: remove `<AgentGate>` from `MainLayout` (the no-access tests fail), delete the `Delete` entry's `@if (Session.IsAdmin)` guard in `TicketActions` (the agent test fails), and add `logger.LogInformation("token {T}", ...)` of the access token in a client (the leak test fails); then revert. Expected failures are assertion failures, not build errors.

- [ ] **Step 3: Write `docs/development/ADMIN-APP.md`**

The whole file (the Authentik note is its own section; the controller's compose and options keys from Task 3 are the ones in the configuration table):

````markdown
# Admin app (agents)

`TechStrap.Admin` is the Blazor Server app agents work in. It never touches the database: every screen reads and writes through the TechStrap API as the
signed-in agent. This page is for people who run it and people who extend it. What agents can do to a ticket through the API is in
[TICKET-OPERATIONS.md](TICKET-OPERATIONS.md); how the API checks tokens is in [AGENT-AUTHENTICATION.md](../self-hosting/AGENT-AUTHENTICATION.md).

PHASE-07 is delivered in three pull requests. **07a (this page)**: sign-in, the shell, the queue, ticket detail, the reply composer, the sidebar, spam, delete and
erase. **07b**: settings (products, API keys, agents, tags, My settings, public display name), dead letters and the audit log. **07c**: polish, the command
palette, compose and architecture rules, CSP.

## Run it locally

You need the API running (see [TICKET-OPERATIONS.md](TICKET-OPERATIONS.md) and the root README) and an OpenID Connect provider. Without a provider the Admin still
starts if you give it placeholder values, but signing in fails: that is expected until an identity provider exists (see [Authentik setup](#authentik-setup-note)).

```bash
cp src/TechStrap.Admin/.env.example src/TechStrap.Admin/.env.local     # then edit the values below
dotnet run --project src/TechStrap.Admin --urls http://localhost:8081
```

`.env.local` is read in Development only and is git-ignored. With Docker Compose the Admin listens on `http://127.0.0.1:8081`. Compose reads the three provider
values from the root `.env` (not from `src/TechStrap.Admin/.env.local`, whose `AUTH__*` keys would clash with the compose ones):

```bash
OIDC_AUTHORITY=https://auth.example.com/application/o/techstrap-admin/
OIDC_ADMIN_CLIENT_ID=techstrap-admin
OIDC_ADMIN_CLIENT_SECRET=...
```

Without them the container starts on placeholder values and the sign-in page works, but the redirect to the provider fails.

### Configuration

The Admin refuses to start with a message that names the missing variable. Values are validated at start and read lazily.

| Key | Required | Default | Meaning |
| --- | --- | --- | --- |
| `API__BASEURL` | yes | | Absolute URL of the TechStrap API, for example `http://localhost:8080/`. |
| `API__TIMEOUTSECONDS` | no | 30 | Timeout of one API call (1 to 300). |
| `AUTH__AUTHORITY` | yes | | The provider's issuer URL (https outside Development), with its trailing slash. |
| `AUTH__CLIENTID` | yes | | The OIDC client id of the Admin. |
| `AUTH__CLIENTSECRET` | yes | | The client secret. The Admin client is confidential. |
| `AUTH__SCOPES__0`, `AUTH__SCOPES__1`, ... | no | `openid profile email offline_access` | Must keep `openid` and `offline_access` (the API token is refreshed with the refresh token). |
| `TECHSTRAP_AGENT_GROUP` | no | `techstrap-agents` | Same key and default as the API. The API decides who has access; the Admin only shows what the API allows. |
| `TECHSTRAP_ADMIN_GROUP` | no | `techstrap-admins` | Same. |
| `TECHSTRAP_GROUP_CLAIM_TYPE` | no | `groups` | Same. |
| `DATAPROTECTION__KEYRINGPATH` | in containers | | Persistent folder for the cookie and antiforgery keys. |
| `TRUSTEDPROXY__*`, `ALLOWEDHOSTS` | production | | Trust only your reverse proxy, so the sign-in redirect URI is built with the public scheme and host. |
| `SENTRY__*`, `OPENTELEMETRY__*`, `SERILOG__MINIMUMLEVEL__DEFAULT` | no | | Observability; see the `.env.example`. |

## How sign-in and access work

1. An anonymous visitor to any page is redirected to `/signin` (a plain landing page with one button). The button goes to `/signin/start`, which starts the OIDC code
   flow with PKCE. After the provider, the Admin receives the code on `/signin-oidc`, stores the tokens in the session cookie (`SaveTokens`) and returns the agent to the
   page they asked for (a local path only).
2. **The API decides who is an agent.** Once per circuit the Admin calls `GET /api/agents/me` with the agent's access token. That call also creates the agent's row, which
   every ticket call needs. Nothing inside the gate renders, and no ticket call is made, until it has succeeded. The answer is one of:
   - accepted: the shell, the queue and everything else appear;
   - refused (403): the **no-access page** with the reason (not in the agent group, deactivated, no email claim, identity not matched);
   - the token was rejected (401): "Your session has expired" with a sign-in button;
   - the API could not be reached: an alert with Retry, and still no page content.
3. Delete and erase (and, in 07b, settings, dead letters and the audit log) show **only to an Admin** (`AgentDto.Role`). The API enforces the same rule, so hiding is a courtesy.
4. Sign out is a POST to `/signout` with an antiforgery token; it ends the cookie session and the provider session.
5. The Admin forwards the agent's **access token** to the API on every call, through named HTTP clients created per circuit (`IBlazorCircuitHttpClientFactory`). Reads retry on a
   transient failure; **writes are never retried** (a retried reply would send twice). The refresh token stays on the server (an in-memory token cache); it is never rendered
   or logged.

### Roles

| What | Agent | Admin |
| --- | --- | --- |
| Queue, ticket detail, reply, note, status, assignee, priority, product, tags | yes | yes |
| Mark as spam, Not spam | yes | yes |
| Delete a ticket, erase a requester | no (not rendered) | yes, with a typed confirmation |

## Authentik setup note

Authentik is not set up yet, so none of this has been exercised against a live provider. The tests use a fake `Test` authentication scheme and a stub API. When you set
Authentik up, configure the following; any OIDC provider that supports the same features works.

1. **Create an OAuth2/OpenID provider and an application** (for example slug `techstrap-admin`).
   - Client type: **confidential**. Note the client id and the secret: they are `AUTH__CLIENTID` and `AUTH__CLIENTSECRET`.
   - Redirect URIs (strict): `https://<admin host>/signin-oidc`. For local development also `http://localhost:8081/signin-oidc`.
   - Post-logout redirect URIs: `https://<admin host>/signout-callback-oidc` (and `http://localhost:8081/signout-callback-oidc`).
   - The authority is the provider's issuer URL, which ends in a slash: `https://auth.example.com/application/o/techstrap-admin/`.
2. **Scopes.** Select `openid`, `profile`, `email` and **`offline_access`** (Authentik's "offline_access" scope mapping). Without `offline_access` there is no refresh token and an agent's API
   calls start failing when the access token expires (Authentik's default is minutes). Set the refresh token validity to at least a working day.
3. **Groups claim.** Create the groups named by `TECHSTRAP_AGENT_GROUP` and `TECHSTRAP_ADMIN_GROUP` (defaults `techstrap-agents`, `techstrap-admins`) and add people to them. The default
   `profile` scope mapping in Authentik emits a `groups` claim with the group names, which matches `TECHSTRAP_GROUP_CLAIM_TYPE=groups`. An Admin does not also need the agent group.
4. **The token the API sees.** The Admin sends the **access token** to the API, and the API validates it (`Authentication__JwtBearer__Authority`, `__Audiences__0`; see
   [AGENT-AUTHENTICATION.md](../self-hosting/AGENT-AUTHENTICATION.md)). So the access token must carry `sub`, `email`, the groups claim, and an audience equal to the API's configured
   audience. In Authentik the audience of an access token is the client id of the provider that issued it, so the simplest setup is to set the API audience to the Admin's client id.
   Decode a real access token at the first sign-in and check those four claims (this is not verified yet).
5. **Behind a reverse proxy**, set the trusted proxy keys so the Admin builds `https` redirect URIs, and make sure the provider can be reached from the Admin container at the authority URL.

If sign-in loops or the no-access page shows "not in the agent group" for someone who is, check in this order: the groups claim name, whether the claim is in the **access** token (the API
reads that one), and `TECHSTRAP_AGENT_GROUP` on **both** the API and the Admin.

## Where things live

```text
src/TechStrap.Admin/
  Auth/           cookie + OIDC wiring, /signin and /signout endpoints, AgentSession (the API's answer to /me)
  Clients/        ApiConnection and the typed clients (IAgentsClient, IProductsClient, ITagsClient, ITicketsClient, IRequestersClient), the /attachments/{id} pass-through
  Components/
    Ui/           reusable primitives: LoadingState, ErrorState, EmptyState, ConfirmDialog, PagerControl, TagChip, RelativeTime, StatusStamp, PriorityMark, ...
    Layout/       MainLayout, NavMenu, StatusBar, AgentGate, ShortcutHelpDialog
    Pages/        sign-in landing, no-access, not found, error, style guide (Development only)
  Features/
    Shell/        StatusMessageService, ShortcutService, ShortcutCatalog
    Queue/        TicketQueuePage and its filter bar, tabs and row
    Tickets/      TicketDetailPage, presenter, timeline factory, ReplyComposer, TicketSidebar, TagPicker, TicketActions
  wwwroot/js/     ES modules only: dialog.js, shortcuts.js, queue.js (no inline script anywhere)
  Styles/         SCSS partials over the brand tokens (docs/BRAND.md)
```

Rules the code follows (and the reviewers check):

- The Admin references **Contracts and Hosting only**: never Application, Infrastructure or Domain. DTOs are never renamed; view models are feature-local.
- **Components never inject `HttpClient`.** They inject the `I*Client` interfaces, which return `Result<T>`; the clients map the API's ProblemDetails (the error `type` is the code).
- Every client call takes the component's `CancellationToken`.
- Razor components are always public classes, so a type used as a component parameter is public (a view model cannot be `internal`).
- A component beyond a few plain parameters and one forwarder has a `.razor.cs`; a factory or presenter exists only for non-trivial assembly (`TicketDetailPresenter`, `TimelineEntryFactory`).
- The single `MarkupString` is the message body in `MessageBubble` (the API sanitises it). Everything else is encoded.
- Repeated or meaningful literals are named constants (`QueueDefaults.PageSize`, `QueueDefaults.SearchDebounce`, the Contracts constants for views and event types).

### Add a screen that calls the API

1. Add the method to the client interface and implementation in `Clients/`, returning `Result<T>` and taking a `CancellationToken` last. A GET goes through the read client, anything that changes
   data through the write client (never retried).
2. Put the page under `Features/<Area>/` with `@page`, `@attribute [Authorize]`, a paired code-behind, and load in `OnParametersSetAsync` with a cancellable token.
3. Render the four states with `LoadingState`, `ErrorState` (with Retry), `EmptyState` and the content. Keep the previous content when a refresh fails.
4. Send the ticket's `RowVersion` on every write and replace the local state from the returned `TicketStateDto`. Raise the page's conflict callback on `concurrency-conflict`.
5. Test it with bUnit and a substitute client; add a host test only for what bUnit cannot see.

## Keyboard shortcuts

Single-key shortcuts are off while you type in a text field, select or editable area and while a dialog is open. `?` lists them in the app. A My settings switch to turn them off arrives in 07b.
The command palette (Ctrl+K) arrives in 07c.

| Key | Where | What |
| --- | --- | --- |
| `j` / `k` (or the arrow keys) | Queue | Move the selection; it never reorders rows |
| `Enter` | Queue, nothing focused | Open the selected ticket |
| `/` | Anywhere | Focus search (opens the queue from other screens) |
| `r` / `n` | Ticket | Public reply / internal note: switch tab and focus the box |
| `e` | Ticket | Focus the assignee control |
| `u` | Spam view (row selected) or a flagged ticket | Not spam, no dialog |
| `Ctrl+Enter` | Reply box | Send in the current mode |
| `Esc` | Anywhere | Leave a field (text kept), close a dialog, or go back to the queue |
| `?` | Anywhere | Show the list |

## What an agent can do here (07a)

- **Queue**: six views (Unassigned is the default, then Mine, Open, Pending, All and the separate Spam view), counts per view (Spam muted), filters (product, status, priority, tag), search, 25 per page. Every filter is in
  the URL, so views can be bookmarked. The queue refreshes when you press Refresh; live updates arrive with PHASE-10.
- **Ticket**: one timeline of messages and changes (customer white, public reply canary, internal note pink with a dashed edge), the requester, metadata labelled **Untrusted** unless it came from a trusted key,
  attachments as downloads (served through the Admin, never inline), and a link to the parent of a follow-up.
- **Reply or note**: separate drafts per mode, files (up to 5, 10 MB each), Pending by default or "Send and solve". A failed send, or a conflict, never loses the text or the files.
- **Change a ticket**: status, assignee, priority, product, tags. Each change shows "Saving...", then the screen shows what the API accepted. If someone else changed the ticket first you see
  "This ticket changed since you opened it": press Reload, your draft is kept.
- **Spam, delete, erase**: Mark as spam asks first. Delete and erase (Admins) need the ticket number or the requester's email typed, and say so plainly; erase covers every ticket from that requester.

## Troubleshooting

| You see | Cause |
| --- | --- |
| The Admin does not start; the log names `AUTH__AUTHORITY` (or another key) | A required key is missing or malformed. |
| Sign-in bounces back to the sign-in page | The provider rejected the redirect URI, or `AUTH__CLIENTSECRET` is wrong. |
| "You don't have access" | The API refused `GET /api/agents/me`; the page says why. The group claim is checked by the API. |
| "Your session has expired" shortly after signing in | No `offline_access` scope, or a very short refresh token validity. |
| The queue shows "Couldn't load tickets" with the API's message | The API answered an error; the message is the API's own. |
| Attachments open as a page instead of downloading | Report it: the pass-through must force a download. |
| Keyboard shortcuts do nothing | Focus is in a field, a dialog is open, or `shortcuts.js` was blocked. Check the browser console. |

## Tests

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release                              # bUnit components and the host tests (fake sign-in, stub API)
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "AdminHost|ShellHostTests"
```

bUnit notes that cost time once: JS interop runs in strict mode, so set up `./js/dialog.js` and `./js/shortcuts.js` (the `AdminComponentTest` base class does); the element reference of an element is blanked after the next
render, so read it before the action; services cannot be added after the first render; `InputFile` has no `MaxAllowedSize` and bUnit does not enforce stream limits, so the composer checks files itself.

## Known gaps in 07a

Recorded in D-040 and tracked for later phases: no counts in the erase and delete dialogs and no list of a ticket's follow-ups (the API offers neither); the requester card has no ticket count or first-seen date;
times are shown in UTC (the agent's zone needs the browser); "Apply my change again" after a conflict is not built (Reload only); a lost circuit loses an unsent draft (the leave-warning covers a reload);
no knowledge-base article picker (PHASE-08); no presence or live updates (PHASE-10); no command palette (07c); the manual sign-in check against a real Authentik is outstanding.
````

Add one link line to `docs/development/TICKET-OPERATIONS.md` right after its opening paragraph: `The agent app that drives these routes is described in [ADMIN-APP.md](ADMIN-APP.md).` If the root `README.md` has a list of development docs, add `ADMIN-APP.md` to it in the same style.

- [ ] **Step 4: Tick PHASE-07 and record the status**

**`docs/architecture/PHASE-07-admin-app.md`**: tick the items 07a delivers and append the evidence line under each. Match on the `**P07-Txx**` text, not on line numbers (Task 1 may have moved lines).

| Item | Edit |
| --- | --- |
| `P07-T01` | `- [ ]` becomes `- [x]`; append `  - **07a evidence:** the Task 3 options tests, `EnvExampleCompletenessTests`` |
| `P07-T02` | **Stay `- [ ]`**; append `  - **07a evidence (automated part):** `AdminSignInTests`, `AgentSessionTests`, `AgentGateTests`, `AgentAccessHostTests`, `AdminHostSmokeTests`, `AdminLeakTests`. The manual sign-in against a real provider is still open (Authentik is not set up; docs/development/ADMIN-APP.md)` |
| `P07-T03` | `[x]`; evidence `StateComponentTests, TagChipAndPagerTests, ConfirmDialogTests, TicketDisplayTests (the spec's StatusBadge/PriorityBadge are the existing StatusStamp/PriorityMark plus TicketDisplay, D-040)` |
| `P07-T04` | `[x]`; evidence `MainLayoutTests, ShellComponentTests, AgentGateTests, ReconnectAndErrorTests` |
| `P07-T05` | **Stay `- [ ]`**; evidence `07a: agents, products and tags clients (ApiConnectionTests, ReferenceDataClientTests); IAdminEventsClient arrives in 07b` |
| `P07-T06` | **Stay `- [ ]`**; evidence `07a: ITicketsClient, IRequestersClient, the attachment pass-through (TicketsClientTests, AttachmentPassThroughTests); there is no IAttachmentsClient (the pass-through is an endpoint, D-017) and IDeadLettersClient arrives in 07b` |
| `P07-T07` | `[x]`; evidence `TicketQueuePageTests (default Unassigned, only the Spam tab asks for spam, one debounced call, paging keeps filters)` |
| `P07-T08` | `[x]`; evidence `TicketDetailPageTests, TicketDetailPresenterTests` |
| `P07-T09` | `[x]`; evidence `TimelineEntryFactoryTests (every TicketEventTypes constant)` |
| `P07-T10` | `[x]`; evidence `TicketDetailPageTests, MarkupStringSiteTests, AttachmentPassThroughTests, AdminHostSmokeTests` |
| `P07-T11` | `[x]`; evidence `ReplyComposerTests (Draft_and_files_survive_a_conflict)` |
| `P07-T12` | `[x]`; evidence `TicketSidebarTests, TicketDetailConflictTests` |
| `P07-T13` | `[x]`; evidence `DestructiveActionTests` |
| `P07-T22` | `[x]`; evidence `TicketQueuePageTests, NotSpamQueueTests, DestructiveActionTests, ShortcutServiceTests (the palette command is part of the 07c palette)` |

Success Criteria: tick "An agent in the configured group signs in, sees the queue with all five views plus the Spam view, filters and searches, pages results, and opens a ticket." (evidence `TicketQueuePageTests, AdminHostSmokeTests`), "From ticket detail an agent can reply publicly, add an internal note, change status/assignee/priority/product, tag, mark spam, delete, and erase the requester; ..." (evidence `ReplyComposerTests, TicketSidebarTests, DestructiveActionTests`), "The Spam view lists flagged tickets and `u` restores one ..." (evidence `NotSpamQueueTests`) and "A 409 concurrency conflict never loses a typed reply draft." (evidence `ReplyComposerTests, TicketDetailConflictTests`). Leave the others unticked: the no-access page is done but the expired-session prompt is Task 4's sentence and still needs the manual check, and the rest belong to 07b and 07c.

Carry-forwards: the Admin `/error` page `[AllowAnonymous]` line and the `PiiRedactionEnricher` line are ticked by Tasks 4 and 2; check both are `[x]` and fix them if not.

**`99-IMPLEMENTATION-ROADMAP.md` and `00-DISCOVERY-INDEX.md`**: in the PHASE-07 row, replace whatever status Task 1 wrote ("07a in progress" or "Not started") with `07a implemented (pending merge); 07b and 07c not started`, and make sure the decisions column of the roadmap row lists `D-040`.

- [ ] **Step 5: Run every check**

Run:
- `dotnet build TechStrap.slnx -c Release` (0 warnings);
- `dotnet test --solution TechStrap.CI.slnf -c Release`;
- `pwsh -File scripts/Invoke-ScriptTests.ps1`;
- `pwsh -File scripts/Check-PackageVersions.ps1`;
- `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build` (expect no changes: 07a adds no migration).

All must pass. Then a manual smoke in a browser against a dev API with a development token provider, if one is available: sign in, open the queue, press `?`, open a ticket, reply, change the priority, check the status bar message. If no provider exists yet, say so in the PR description (the manual check is the open part of `P07-T02`).

- [ ] **Step 6: Commit**

```bash
git add tests docs README.md
git diff --cached --stat
git commit -m "test(admin): host smoke and leak checks, ADMIN-APP.md and PHASE-07a status" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```
