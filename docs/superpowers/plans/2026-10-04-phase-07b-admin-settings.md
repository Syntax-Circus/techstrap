# PHASE-07b Admin Settings and Admin Pages Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give admins the settings pages in the Admin app:
- **Products:** create and edit products and their branding.
- **API keys:** issue and revoke keys.
- **Agents:** activate and deactivate agents.
- **Tags:** manage tags, with ticket counts.
- **Audit log:** read the audit log.
- **Failed emails:** retry or discard them.

Every agent also gets My settings: notification opt-ins, a public display name with a live preview, a keyboard-shortcut toggle and a theme choice.

**Architecture:**
- **Pages.** They reuse the 07a client and shell patterns. Typed clients sit over `ApiConnection`. Writes always go through the write client, are never retried, are never cancelled when a page closes, and show uncertain-write copy when the outcome is unclear.
- **Admin-only pages.** They are wrapped in an `AdminOnly` guard that trusts the API's answer from `AgentSession`. It never denies while the session is loading.
- **Browser preferences.** The shortcut toggle and theme are stored in the browser through a small ES module.
- **Backend additions.** There are four, all small:
  - an Admin-only tag summary endpoint with ticket counts;
  - server-side validation of logo URLs;
  - Contracts constants for admin event types and subject types, plus the colour pattern, each with a parity test;
  - an Admin agent-list endpoint test.

**Tech Stack:** .NET 10, Blazor Server, ASP.NET Core controllers, EF Core with Npgsql, SyntaxCircus.Common Result, xUnit v3, Shouldly, NSubstitute, bUnit 2.11.3, and node:test for the JS modules.

**Spec:**
- `docs/architecture/PHASE-07-admin-app.md`, tasks T14–T18 and T23.
- `docs/architecture/UX-BRIEF-admin.md`.
- Decisions D-022, D-024, D-029, D-036 and D-040.
- The owner decisions of 2026-10-04, recorded as D-041 in Task 1.

### Owner decisions (2026-10-04), recorded as D-041 in Task 1
- **Roles are read-only in Admin.** The agents page shows a role badge with "Roles come from your identity provider's groups." (D-029). Admins can only activate or deactivate agents.
- **Product logo is a URL field** with validation and a preview. There is no upload.
- **Tag ticket counts.** The Admin tag list shows how many tickets use each tag, and the delete confirmation shows the same count.
- **One PR.**

### Technical decisions this plan makes (D-041; the owner confirms at plan review)
- **Tag summary endpoint.** `TagSummaryDto` comes from an Admin-only `GET api/tags/summary` backed by a grouped count. `TagDto` and `GET api/tags` are unchanged.
- **Logo URL rule, enforced by the server (Domain).**
  - Accepted: blank; absolute `https` with a host and no userinfo; `http` only for `localhost` or `127.0.0.1`.
  - The `http` loopback exception applies in every environment, because Domain cannot see the hosting environment. A loopback URL cannot be fetched by a real customer's browser or mail client, so it is harmless.
  - Anything else fails with `logo-path-invalid`. A URL over `BrandingRules.LogoUrlMaxLength` (500) fails with `logo-path-too-long`.
  - `BrandingRules.IsAcceptableLogoUrl` mirrors the rule for the Admin editor.
  - Two existing tests that used a relative logo now use an `https` URL.
- **Contracts constants with parity tests.** Add `AdminEventTypes`, `AdminSubjectTypes` and `BrandingRules` (`ColourPattern`, `LogoUrlMaxLength`, `IsAcceptableLogoUrl`). The tag count is a correlated `Count` subquery; no migration.
- **Admin client support types.**
  - `ApiFields` holds the kebab-case 400 targets.
  - `AdminEventFilter(SubjectType, ActorId, AsOf)` carries the audit filter.
  - `ApiErrorCodes` also gains `OutboxNotDeadLettered` and `AdminEventSubjectTypeInvalid`.
  - `FailedEmailCounter` (scoped) holds the nav badge count. It offers `Count`, `Changed`, `RefreshAsync` and `Set`.
- **`AdminOnly` guard.**
  - It shows a Checking state only while `AgentSession` is NotLoaded.
  - When the session is Ready and the user is not an admin, it shows the page-level NoAccess and never builds the content.
  - For NoAccess, SessionExpired and Unavailable it renders nothing, because `AgentGate` owns those states.
  - **Pages are thin shells:** `<AdminOnly><XxxContent/></AdminOnly>`. All loading happens inside the content component, so no admin API call runs before the guard decides.
  - `AgentSession.ReloadAsync` keeps the session Ready, with the current agent, while a reload runs. It also stays Ready after a transient failure, but a 401 or 403 still drops it. This stops the page flickering.
  - NavMenu shows admin links only for admins.
- **Browser preferences.** The shortcut toggle and theme live in browser storage through `wwwroot/js/preferences.js` and a `PreferencesService`. The theme choices are `auto`, `light` and `dark`. `auto` removes `data-bs-theme`.
- **Product editor.**
  - It always sends `IsActive` and `Version`.
  - A conflict shows a banner with Reload and keeps the form values.
  - The branding preview uses Contracts `ProductAccent`. Low contrast is shown as information, never blocked.
- **New-key dialog.**
  - It cannot close until "I have stored this key" is ticked.
  - The plaintext key is cleared from component state on close.
  - The key never appears in logs, URLs or later markup.
- **Destructive actions.**
  - Deleting a tag that is in use needs the tag name typed.
  - Revoking a key, deactivating an agent and discarding a failed email each need a medium-tier confirm.
  - Activating an agent and retrying a failed email need no confirm.
- **Notification toggles** each save the full set.
- **Display name.** It saves on blur or Enter, then the session reloads.
- **Failed emails.**
  - The ticket link uses `/tickets/{ticketId}`.
  - The nav badge count comes from `TotalCount`. After every successful list read, the page calls `FailedEmailCounter.Set(total)`, so the badge follows retries and discards without a separate count call.
  - There is no discard reason, because the API has none.
- **Page support added in Tasks 5–9.**
  - `AdminPageTest`: a bUnit base for page tests. It is admin by default; `AsAgent()` switches to a plain agent.
  - `EmailKinds` (`Features/Settings`): the shared email-kind labels used by the audit and failed-email pages.
  - `wwwroot/js/clipboard.js`: the copy button for the new-key dialog, with node and Pester tests.
- **My settings is open to every agent.** It has no `AdminOnly` wrapper.

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`. Private fields use `_camelCase`, constants use PascalCase, and namespaces are file-scoped.
- **Packages.** Versions are managed centrally. This plan adds no package. `PackageReference` entries carry no version.
- **Project references.**
  - Admin references only Contracts and Hosting.
  - Domain references nothing. Contracts references nothing and has no attributes or enums.
  - The other rules are unchanged.
- **Handlers and controllers follow the existing patterns:**
  - one `[FromServices]` handler interface per action;
  - a `ControllerActions.ExpectedSuccess` row for each action;
  - an explicit policy, with Admin-only routes using the `Admin` policy.
- **Admin code.**
  - Components never inject `HttpClient`.
  - View models that are component parameters are `public`.
  - Copy strings live in `*Copy` constants.
  - Paired `.razor`/`.razor.cs` files are used beyond the inline ceiling.
  - JS lives only in ES modules under `wwwroot/js`.
  - CSS lives in `Styles/_*.scss` partials with the `ts-` prefix.
- **Writes.**
  - Use `ApiConnection` write methods. Never retry.
  - Pass `CancellationToken.None`, and never cancel a write on dispose.
  - Guard state after every await with `_disposed`.
  - Classify outcomes with `WriteOutcomes.Classify` / `ApiErrorCodes.IsUncertainWrite`.
  - A write that may have happened shows uncertain copy with Reload, never a bare "Try again".
- **Encoding.**
  - Write non-ASCII as `\u` escapes in C#.
  - Never write `\u` with GNU sed. Use a Python or .NET writer and grep afterwards.
  - `SourceEncodingTests` and `SourceEscapeTests` must pass.
- **Secrets and PII.**
  - Never log a token, cookie, API-key plaintext, email or requester name.
  - The API-key plaintext appears only inside the new-key dialog while it is open.
- **Tests.**
  - Write the failing test first.
  - Tests must not be vacuous.
  - Every signed-in host test calls `factory.Api.AssertEveryCallBore(principal)`.
- **Persistence.** No migration is expected. If one is needed, generate it only with `dotnet ef`.
- **Commits.** Use Conventional Commits, each ending with exactly:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden.**
  - `git add -f`;
  - committing `.superpowers/`;
  - `docker compose down -v`;
  - killing processes you did not start;
  - committing without running `git diff --cached --stat` first.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release` gives 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release` passes.
  - `pwsh -File scripts/Invoke-ScriptTests.ps1` passes.
  - The EF pending-model check is clean.

## Review Focus

1. **A plain agent reaches an admin page or action, or the guard misbehaves while loading.** Agents must be stopped whether they arrive by URL, nav link or keyboard. While the session is still loading, the guard must neither flicker nor deny. Pinned in Task 4 (`AdminOnlyTests` and the reload no-flicker test) and Task 9 (host tests).
2. **The API-key plaintext leaks.** It must never appear in logs, a URL, or markup after the dialog closes, and the dialog must not close before "stored" is ticked. Pinned in Task 6 (`NewApiKeyDialogTests`) and Task 9 (`AdminLeakTests`).
3. **A product edit silently loses data.** Three ways this can happen: an omitted `IsActive` deactivates the product, a stale `Version` overwrites a newer edit, or a failed save drops the form values. Pinned in Task 5 (`ProductEditorTests`).
4. **An unsafe logo URL is accepted.** `javascript:`, `data:` and relative URLs must be rejected by both the API and the editor. Pinned in Task 2 (Domain and API tests) and Task 5 (editor tests).
5. **A destructive admin action fires without its confirmation, or fires twice.** This covers deleting an in-use tag, discarding a dead letter, deactivating an agent and revoking a key. Pinned in the action tests of Tasks 6–9.

---

### Task 1: Record the decisions (D-041) and align the docs

**Files:**
- Modify: `docs/architecture/04-DECISION-LOG.md`. Add the approval-basis bullet, the index row D-041, its section, one-line amendment notes under D-029 and D-030, and the correction of the "3 retries" text of D-040.
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` (row 07)
- Modify: `docs/architecture/00-DISCOVERY-INDEX.md` (row 07 and the two "D-001 to D-040" sentences)
- Modify: `docs/architecture/PHASE-07-admin-app.md` (07b deviations, the stale client and resilience lines, T14, T16 and T23 wording, component table rows, the tag summary endpoint, two carry-forwards)
- Modify: `docs/architecture/UX-BRIEF-admin.md` (roles, logo upload versus URL, the last-admin rule, discard reason, audit filters, nav)
- Modify: `docs/architecture/02-ARCHITECTURE.md` (one handler row)

**Interfaces:** none (docs only). Task 2 builds what D-041 says about the API; Tasks 3 to 9 build what it says about the Admin.

- [ ] **Step 1: Add D-041**

Follow the D-037 to D-040 format (Status, Date, Owner, Related artifacts, Context, Decision, Alternatives Considered, Consequences, Approval).

1. Add this bullet to the approval-basis list, after the PHASE-07 planning bullet (the D-040 one):

```markdown
- **Owner decision (2026-10-04, PHASE-07b planning):** D-041, the owner decisions on read-only roles in the Admin, the logo URL field, the tag ticket count and the single PR. Its technical decisions were proposed in the PHASE-07b plan and approved when the owner approved the plan.
```

2. Add this index row after the D-040 row:

```
| D-041 | PHASE-07b: roles are read-only in the Admin; the product logo is a validated URL; the tag list shows ticket counts; Admin guard, browser preferences and client decisions | Approved (owner 2026-10-04; technical decisions at PHASE-07b plan review) | 2026-10-04 | PHASE-07, PHASE-08, PHASE-12 |
```

3. Append this section after D-040, after a `---` line (D-040 is the last section; the file ends with its "Approved on" line):

```markdown
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
```

4. Under D-029 "Consequences" add the line: `- Amended in part by D-041: the Admin shows the role as a read-only badge with the note "Roles come from your identity provider's groups."; the agents screen only activates and deactivates.`
5. Under D-030 "Consequences" add the line: `- Amended in part by D-041: the 409 message still names the count; the Admin tag list and delete confirmation take it from GET /api/tags/summary.`
6. In D-040 "Decision", bullet "**HTTP clients.**": replace `` `techstrap-api-read` is resilient (3 retries); `` with `` `techstrap-api-read` retries a failed read twice (transport errors, timeouts, 408, 502, 503 and 504; no circuit breaker, because every circuit shares the client); ``. Then add this line under D-040 "Consequences": `- Corrected by D-041: the read client retries twice (ApiClientRegistration.ReadRetryCount), not three times, and has no circuit breaker.`

- [ ] **Step 2: Align the other docs**

Use a small Python script (or the Edit tool) for the multi-line replacements; do not use GNU sed.

1. **`99-IMPLEMENTATION-ROADMAP.md`**, row 07: the decisions cell `D-017, D-022, D-040` becomes `D-017, D-022, D-040, D-041`, and the status `07a implemented (pending merge); 07b and 07c not started` becomes `07a complete (PR #9 merged); 07b in progress; 07c not started`.
2. **`00-DISCOVERY-INDEX.md`**
   - Row 07 status `07a implemented (pending merge); 07b and 07c not started` becomes `07a complete (PR #9 merged); 07b in progress; 07c not started`.
   - `Material decisions D-001 to D-040` becomes `Material decisions D-001 to D-041`, and `All decisions D-001 to D-040 are approved.` becomes `All decisions D-001 to D-041 are approved.`
3. **`02-ARCHITECTURE.md`**: add this row after the `GET /api/tags` row of the handler table:

```
| `GET /api/tags/summary` (Admin) | `ListTagSummariesRequestHandler` | `ITagRepository` | EF repos (one grouped count) | 200 `TagSummaryDto[]` | H, C | D-041 |
```

4. **`PHASE-07-admin-app.md`**
   - After the paragraph that starts `07a deviations from the text below`, add:

```markdown
07b deviations from the text below, all recorded in D-041: roles are read-only (the agents page activates and deactivates only); the product logo is a validated URL field, not an upload; the Admin tag list shows ticket counts from the new `GET /api/tags/summary`; every admin page sits inside an `AdminOnly` guard; the shortcut toggle and the theme are stored in the browser (`preferences.js`); validation errors are 400, not 422. PHASE-07b lands in one PR.
```

   - In "Architecture Decisions", the bullet "**No new server entry points.**": append ` PHASE-07b adds one read endpoint to the API, GET /api/tags/summary (D-041); the Admin host itself still adds none.`
   - Replace the bullet starting `- **Typed clients**:` (line 37) with:

```markdown
- **Typed clients**: one client per API area (`IAgentsClient`, `IProductsClient`, `ITagsClient`, `ITicketsClient`, `IDeadLettersClient`, `IAdminEventsClient`) in `TechStrap.Admin/Clients/`, over a small Admin `ApiConnection` (D-040). `AddTechStrapApiClients` registers two named clients that run `ApiAuthHandler` (the bearer token) and forward the client IP; the connection obtains each from `IBlazorCircuitHttpClientFactory` (circuit-safe), so components never see an HttpClient. Clients return `Result<T>`/DTOs; ProblemDetails (RFC 7807), including the `errorCodes` of a 400, are mapped to `Result` failures so components never catch HTTP exceptions. `ApiClientBase` is not used: it drops `errorCodes` and has no `Result` mapping.
```

   - Replace the bullet starting `- **Resilience budget**:` (line 38) with:

```markdown
- **Resilience budget**: the read client retries a failed GET twice (`ApiClientRegistration.ReadRetryCount`: exponential backoff with jitter, for transport errors, timeouts, 408 and 502/503/504) and has no circuit breaker, because every circuit shares it; every mutating call uses the write client, which has **no automatic retry** and no breaker (non-idempotent). See D-040.
```

   - In the package table (line 124), replace the `SyntaxCircus.Http.Resilience` row with:

```markdown
| `SyntaxCircus.Http.Resilience` | Brings the resilience pipeline; the read client uses its retry strategy only (no `ApiClientBase`, no circuit breaker, D-040) | Admin -> API typed clients | Unit tests with a stub handler: retry on GET 503, none on POST, ProblemDetails -> `Result` failure |
```

   - In the handler table: in the `AgentsClient : ApiClientBase` row replace `` `AgentsClient : ApiClientBase` (HTTP) `` with `` `AgentsClient` over `ApiConnection` (HTTP) ``; in the `PUT /api/agents/me/profile` row replace `422 shown as a field error` with `400 shown as a field error (target public-display-name)`; in the Tags row replace the handlers cell `` `ListTagsRequestHandler`, `CreateTagRequestHandler`, `UpdateTagRequestHandler`, `DeleteTagRequestHandler` (P04) `` with `` `ListTagsRequestHandler`, `ListTagSummariesRequestHandler` (07b, D-041), `CreateTagRequestHandler`, `UpdateTagRequestHandler`, `DeleteTagRequestHandler` (P04) ``, and the last cell `Consumed; no new server entry point` with `Consumed; one new read endpoint, GET /api/tags/summary (D-041)`.
   - In the component table: `ProductEditorPage`: replace `accent colour validated against `#RRGGBB` constant` with `accent colour validated with `BrandingRules.ColourPattern`, logo URL with `BrandingRules.IsAcceptableLogoUrl` (D-041)`. `AgentsPage` row: replace `Role/active edit inline; Admin-only` with `Read-only role badge with the IdP note; activate and deactivate inline (deactivate confirms); Admin-only (D-041)` and `AgentDto` with `AgentListItemDto`. `TagsPage` row: replace `Inline edit; delete confirm` with `Inline edit; delete confirm (the tag name typed when the tag is in use, with its ticket count)` and `TagDto` with `TagSummaryDto`. `NotificationPreferencesPage` row: replace `NotificationPreferencesDto` with `NotificationPreferenceDto`.
   - T05 text (line 161): replace `over `ApiClientBase` with ProblemDetails -> `Result` mapping and per-client resilience config` with `over `ApiConnection` with ProblemDetails -> `Result` mapping, a retrying read client and a never-retrying write client (D-040)`.
   - T14 validation: replace the sentence with: `bUnit: an invalid accent is rejected client-side with `BrandingRules.ColourPattern` (the server pattern, pinned by a parity test); a logo URL that is not https (or http for localhost) is rejected client-side and by the API; server 400 errors map to fields by their kebab-case target; the page sits inside `AdminOnly`, so a plain agent sees the no-access page.`
   - T16 validation: replace the sentence with: `bUnit: the agents page shows the role as a read-only badge with the note "Roles come from your identity provider's groups." and has no role control; activate calls `SetActiveAsync(id, true)` without a confirm, deactivate calls `SetActiveAsync(id, false)` after a confirm, and a 409 `last-active-admin` shows inline; a plain agent sees the no-access page; the preferences save posts the full toggle set.`
   - T23 validation: replace `from a 422` with `from a 400 (target public-display-name)`.
   - Carry-forwards: change the two lines `- [ ] Carried forward from the PHASE-04 final review: Return the `tag-in-use` count ...` and `- [ ] Carried forward from the PHASE-04 final review: Add an endpoint test for the admin agent-list fields ...` so that each starts `- [ ] [07b] Carried forward` (Task 9 ticks them; the first is closed by the tag summary endpoint, the second by the new `AgentManagementEndpointTests` case).
5. **`UX-BRIEF-admin.md`** (exact fragments; each is replaced inside its line, the file wraps at about 80 columns so the third and sixth are two-line fragments):
   - `grant/revoke agent access and roles,` becomes `activate or deactivate agents (roles come from the IdP groups, D-029 and D-041),`
   - `branding (display name, logo, accent colour, email from-address, reply-to),` becomes `branding (display name, logo URL, accent colour, email from-address, reply-to),`
   - `duplicate key or invalid logo upload` becomes `duplicate key or invalid logo URL`
   - `create/edit product and branding, upload logo, pick accent` becomes `create/edit product and branding, set the logo URL (https, with a preview), pick accent`
   - `change role (Agent/Admin), deactivate/reactivate.` becomes `deactivate/reactivate; the role is a read-only badge with the note "Roles come from your identity provider's groups." (D-029, D-041).`
   - `an admin cannot demote or deactivate the last` newline `    admin (UI prevents it, API enforces it).` becomes `the API refuses to deactivate the last active` newline `    admin (409 last-active-admin) and the screen shows its message inline (D-041).`
   - `**Discard** (confirmed; reason optional).` becomes `**Discard** (confirmed; the API takes no reason).`
   - `filter by actor, entity type, date; read-only.` becomes `filter by actor and subject type; read-only (the API has no date or event-type filter, D-041).`
   - `(Dead letters with badge, Audit log)` becomes `(Failed emails with a count badge, Audit; in 07b the five admin links sit in one group, D-041)`

- [ ] **Step 3: Check and commit**

Run `pwsh -File scripts/Invoke-ScriptTests.ps1` (expect PASS; the docs tests read the README links only). Then:

```bash
grep -n "D-041" docs/architecture/*.md
grep -n "3 retries\|retry (3\|GETs retry\|upload logo\|invalid logo upload\|change role (Agent/Admin)\|from a 422\|D-001 to D-040\|ApiClientBase" docs/architecture/04-DECISION-LOG.md docs/architecture/PHASE-07-admin-app.md docs/architecture/UX-BRIEF-admin.md docs/architecture/00-DISCOVERY-INDEX.md
```

The first grep shows the new text in the decision log (bullet, index row, section, three notes), the roadmap, the index, PHASE-07, UX-BRIEF-admin and 02-ARCHITECTURE. The second returns only `ApiClientBase` hits, which are historical or new and stay: the D-040 context sentence, its Alternatives line and its Consequences line, the D-041 context, the PHASE-07 "07a deviations" paragraph and the new typed clients bullet. No line may still say `3 retries`, `retry (3`, `GETs retry`, `upload logo`, `invalid logo upload`, `change role (Agent/Admin)`, `from a 422` or `D-001 to D-040`.

```bash
git add docs
git diff --cached --stat
git commit -m "docs: record PHASE-07b decisions (D-041) and align the specs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 2: Backend and Contracts: the logo URL rule, the Contracts constants, the tag summary, the agent-list test

**Review Focus pin:**
- **(4)** An unsafe logo URL (`javascript:`, `data:`, relative) must be refused by the API: the logo becomes an image source in customer emails (and in the portal, PHASE-09). Pinned by `ProductBrandingLogoTests` (Domain), `ProductEndpointTests.An_unsafe_logo_url_is_400_with_a_logo_path_error_on_create_and_on_update_and_nothing_is_stored` (API, create and update), and `BrandingRulesParityTests` (the Admin's twin of the rule gives the same answers).

**Files:**
- Create: `src/TechStrap.Contracts/AdminEvents/AdminEventNames.cs`, `src/TechStrap.Contracts/Branding/BrandingRules.cs`
- Modify: `src/TechStrap.Contracts/Tags/TagDtos.cs`
- Modify: `src/TechStrap.Domain/Rules/Guard.cs`, `src/TechStrap.Domain/Products/Product.cs`
- Modify: `src/TechStrap.Application/Persistence/ITagRepository.cs`, `src/TechStrap.Application/Tags/TagMapping.cs`
- Create: `src/TechStrap.Application/Tags/ListTagSummariesRequestHandler.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Repositories/RequesterAndTagRepositories.cs`
- Modify: `src/TechStrap.Api/Controllers/TagsController.cs`
- Create: `tests/TechStrap.Domain.Tests/Products/ProductBrandingLogoTests.cs`
- Modify: `tests/TechStrap.Domain.Tests/Products/ProductTests.cs`, `tests/TechStrap.Infrastructure.IntegrationTests/ProductRepositoryTests.cs` (the relative logo they used)
- Create: `tests/TechStrap.Application.Tests/AdminEvents/AdminNamesParityTests.cs`, `tests/TechStrap.Application.Tests/Products/BrandingRulesParityTests.cs`, `tests/TechStrap.Application.Tests/Tags/ListTagSummariesRequestHandlerTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/TagSummaryIntegrationTests.cs`
- Modify: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`, `tests/TechStrap.Api.Tests/Controllers/TagsControllerTests.cs`, `tests/TechStrap.Api.Tests/Tags/TagEndpointTests.cs`, `tests/TechStrap.Api.Tests/Products/ProductEndpointTests.cs`, `tests/TechStrap.Api.Tests/Agents/AgentManagementEndpointTests.cs`, `tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs`

**Interfaces:**
- Consumes: `Guard`, `DomainErrors.Validation` and `ProductBranding.Create` (Domain); `ITagRepository`, `TagRecord` and `TicketTagRecord` (the existing `ticket_tags` join table); `ApplicationHandlerRegistration` (it registers every `XxxHandler` with its `IXxxHandler`, so the new handler needs no registration line); the Api test support `ApiFactory`, `ApiTestDatabase`, `TestJwt`, `ControllerTestContext`; the Infrastructure test support `PersistenceTestHost`, `TicketScenario`.
- Produces (names are fixed; Tasks 3 to 9 use them):

```csharp
namespace TechStrap.Contracts.Tags;
public sealed record TagSummaryDto(Guid Id, string Slug, string Name, string Colour, int TicketCount);

namespace TechStrap.Contracts.AdminEvents;
public static class AdminEventTypes      // 12 consts, each value equal to its name: ProductCreated, ProductUpdated, ApiKeyCreated, ApiKeyRevoked, AgentUpdated, TagCreated, TagUpdated, TagDeleted, RequesterErased, TicketDeleted, DeadLetterRetried, DeadLetterDiscarded
public static class AdminSubjectTypes    // 7 consts: Product, ApiKey, Agent, Tag, Requester, Ticket, EmailOutbox

namespace TechStrap.Contracts.Branding;
public static class BrandingRules
{
    public const string ColourPattern = "^#[0-9A-Fa-f]{6}$";   // apply it to the trimmed value; the API stores the colour upper-case
    public const int LogoUrlMaxLength = 500;
    public static bool IsAcceptableLogoUrl(string? value);      // blank = true; https with a host and no user info = true; http only for localhost and 127.0.0.1; else false
}

namespace TechStrap.Application.Persistence;
public interface ITagRepository { /* existing members */ Task<IReadOnlyList<TagUsage>> ListWithTicketCountsAsync(CancellationToken cancellationToken); }
public sealed record TagUsage(Tag Tag, int TicketCount);

namespace TechStrap.Application.Tags;
public interface IListTagSummariesRequestHandler { Task<Result<IReadOnlyList<TagSummaryDto>>> HandleAsync(CancellationToken cancellationToken); }
public sealed class ListTagSummariesRequestHandler(ITagRepository tags) : IListTagSummariesRequestHandler;

// TagsController: [HttpGet("summary")] [Authorize(Policy = Admin)] ListSummaries([FromServices] IListTagSummariesRequestHandler, CancellationToken)  =>  GET api/tags/summary -> 200 TagSummaryDto[] ordered by name; a plain agent gets 403 admin-access-required
```

  Wire facts for the Admin tasks: a rejected logo is a 400 `validation-failed` whose `errors` and `errorCodes` are keyed `logo-path`, code `logo-path-invalid` (or `logo-path-too-long` above 500 characters); `GET api/tags` and `TagDto` are unchanged.

**Rules:**
1. **No migration.** The count is a read over the existing `ticket_tags` table, so the model does not change. Prove it with `dotnet ef migrations has-pending-model-changes` (Step 8). Never hand-write a migration; if the check ever reports changes, generate one with `dotnet ef`.
2. **The count query stays read-only and in `TagRepository`.** `AppendOnlyBypassRules` flags an Infrastructure file that mixes `ExecuteUpdate`/`ExecuteDelete` with an event table. `RequesterAndTagRepositories.cs` mentions no event table, so a plain `Count` there is safe. Do not move the query into a file that already uses `ExecuteDelete` (`EmailOutboxStore`, `RequesterErasure`, `IntakeIdempotencyStore`).
3. **`TagDto` and `GET api/tags` do not change.** The count counts every ticket that carries the tag, whatever its status and including spam, which is the set a forced delete detaches (`DeleteTagRequestHandler` uses `ListTicketIdsWithTagAsync`).
4. **The logo rule.** Trim; blank is "no logo" and is stored as null; longer than 500 characters keeps the existing `logo-path-too-long`; no whitespace or control character inside; it must parse as an absolute URI with a host and no user info; the scheme must be `https`, or `http` with the host exactly `localhost` or `127.0.0.1` (any port). The code is `logo-path-invalid`, the target `logo-path`. The error text names the field, like the other guards. **The header's "http for localhost in Development only" cannot be built as written:** the Domain does not know the environment, so the loopback `http` form is allowed everywhere (D-041). It cannot hurt: a customer's browser cannot load a loopback address, and the email renderer only renders `https` logos (`EmailTemplateRenderer.Layout`).
5. **Two copies of the rules, one parity test.** Domain cannot reference Contracts, so `Guard.OptionalImageUrl` and `Guard.Colour` (Domain) and `BrandingRules` (Contracts) are separate code. `BrandingRulesParityTests` runs the same samples through both and fails when they disagree. A change to one rule must change both and the sample list.
6. **A logo stored before this task still loads.** `ProductBranding.Restore` does not validate, so an old row with a relative logo is read and shown; saving such a product needs a valid URL or a blank one (D-041).
7. **Two existing tests used `"/logo.svg"`** (`ProductTests.Updating_details_changes_name_and_branding_but_never_the_key_or_prefix` and `ProductRepositoryTests.A_saved_product_is_found_by_id_and_by_key_with_all_fields`). They now use an https URL; nothing else changes in them.
8. **`ControllerActions.ExpectedSuccess` gets one row** (`TagsController.ListSummaries` = 200), or `ResultMappingTests` and `CancellationPropagationTests` fail for the unknown action. The admin-only floor in `AgentAccessCoverageTests` goes from 15 to 16 routes.
9. **Api and Infrastructure integration tests need Docker** (Testcontainers Postgres), like the existing ones.
10. **Encoding.** The test sources contain `\u0001`, `\uFF03` and `\u00FC` as C# escapes, not literal characters. Some editing tools decode a \uXXXX sequence in the text you send, so after writing a test file run `grep -nP "[^\x00-\x7F]"` on it: it must find nothing. Never write the escapes with GNU sed. If the grep finds a literal character, replace it with its escape using a Python or .NET writer (the character \uFF03 is a full-width number sign, \u00FC is u with a diaeresis).

- [ ] **Step 1: Write the failing tests for the logo rule and the Contracts constants**

1. `tests/TechStrap.Domain.Tests/Products/ProductBrandingLogoTests.cs`:

```csharp
using TechStrap.Domain.Products;

namespace TechStrap.Domain.Tests.Products;

/// <summary>Review Focus 4: the logo becomes an image source in customer emails and on the portal, so only a safe absolute URL may be stored.</summary>
public sealed class ProductBrandingLogoTests
{
    [Theory]
    [InlineData("https://cdn.orbitly.example/logo.png", "https://cdn.orbitly.example/logo.png")]
    [InlineData("  https://cdn.orbitly.example/a/b.svg?v=2  ", "https://cdn.orbitly.example/a/b.svg?v=2")]
    [InlineData("HTTPS://CDN.ORBITLY.EXAMPLE/Logo.PNG", "HTTPS://CDN.ORBITLY.EXAMPLE/Logo.PNG")]
    [InlineData("http://localhost/logo.png", "http://localhost/logo.png")]
    [InlineData("http://localhost:5080/logo.png", "http://localhost:5080/logo.png")]
    [InlineData("http://127.0.0.1:8080/logo.png", "http://127.0.0.1:8080/logo.png")]
    public void A_safe_absolute_logo_url_is_kept_trimmed(string input, string expected)
    {
        ProductBranding.Create("Orbitly", input, null, null, null).Value.LogoPath.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_logo_is_allowed_and_stored_as_null(string? input)
    {
        ProductBranding.Create("Orbitly", input, null, null, null).Value.LogoPath.ShouldBeNull();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://cdn.orbitly.example/logo.png")]
    [InlineData("/logo.svg")]
    [InlineData("logo.svg")]
    [InlineData("../logo.svg")]
    [InlineData("//cdn.orbitly.example/logo.png")]
    [InlineData("http://cdn.orbitly.example/logo.png")]
    [InlineData("http://localhost.evil.example/logo.png")]
    [InlineData("http://192.168.1.10/logo.png")]
    [InlineData("https://")]
    [InlineData("https:///logo.png")]
    [InlineData("https://user:secret@cdn.orbitly.example/logo.png")]
    [InlineData("https://cdn.orbitly.example/lo go.png")]
    [InlineData("https://cdn.orbitly.example/lo\ngo.png")]
    [InlineData("https://cdn.orbitly.example/\u0001logo.png")]
    public void An_unsafe_or_relative_logo_is_rejected_with_a_logo_path_error(string input)
    {
        var error = ProductBranding.Create("Orbitly", input, null, null, null).Error!;

        error.Kind.ShouldBe(DomainErrorKind.Validation);
        error.Code.ShouldBe("logo-path-invalid");
        error.Target.ShouldBe("logo-path");
    }

    [Fact]
    public void A_logo_longer_than_the_limit_is_too_long_not_invalid()
    {
        var url = "https://cdn.orbitly.example/" + new string('a', 500);

        ProductBranding.Create("Orbitly", url, null, null, null).Error!.Code.ShouldBe("logo-path-too-long");
    }

    [Fact]
    public void A_logo_of_exactly_the_limit_is_accepted()
    {
        var url = "https://cdn.orbitly.example/" + new string('a', 500 - "https://cdn.orbitly.example/".Length);

        url.Length.ShouldBe(500);
        ProductBranding.Create("Orbitly", url, null, null, null).Value.LogoPath.ShouldBe(url);
    }

    [Fact]
    public void Restore_does_not_revalidate_a_stored_logo_so_old_rows_still_load()
    {
        ProductBranding.Restore("Orbitly", "/logo.svg", "#1F6FEB", null, null).LogoPath.ShouldBe("/logo.svg");
    }
}
```

2. In `ProductTests.cs` and `ProductRepositoryTests.cs` replace `"/logo.svg"` by `"https://cdn.orbitly.example/logo.svg"` (one occurrence each).

3. `tests/TechStrap.Application.Tests/AdminEvents/AdminNamesParityTests.cs`:

```csharp
using TechStrap.Contracts.AdminEvents;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.Tests.AdminEvents;

/// <summary>The audit page branches on these constants; if the Domain gains a type the constants must follow, or the page would show a generic line for a type it was told it knows.</summary>
public sealed class AdminNamesParityTests
{
    [Fact]
    public void Event_type_names_match_the_domain_enum()
    {
        Names(typeof(AdminEventTypes)).ShouldBe(Enum.GetNames<AdminEventType>(), ignoreOrder: true);
        Names(typeof(AdminEventTypes)).Length.ShouldBe(12);
    }

    [Fact]
    public void Subject_type_names_match_the_domain_enum()
    {
        Names(typeof(AdminSubjectTypes)).ShouldBe(Enum.GetNames<AdminSubjectType>(), ignoreOrder: true);
        Names(typeof(AdminSubjectTypes)).Length.ShouldBe(7);
    }

    [Fact]
    public void Every_constant_value_equals_its_own_name()
    {
        foreach (var holder in new[] { typeof(AdminEventTypes), typeof(AdminSubjectTypes) })
        {
            foreach (var field in holder.GetFields().Where(f => f.IsLiteral))
            {
                field.GetRawConstantValue().ShouldBe(field.Name);
            }
        }
    }

    // Distinct values of every public const string, the same helper shape as TicketNamesParityTests.
    private static string[] Names(Type holder) =>
        [.. holder.GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!).Distinct()];
}
```

4. `tests/TechStrap.Application.Tests/Products/BrandingRulesParityTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Contracts.Branding;
using TechStrap.Domain.Products;
using TechStrap.Domain.Rules;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Products;

/// <summary>
/// The Admin editor checks colour and logo with Contracts <see cref="BrandingRules"/> before it submits; the API checks again with the Domain guard. Domain
/// cannot reference Contracts, so these tests pin the two to the same answers (Review Focus 4: an unsafe logo URL must be refused by both).
/// </summary>
public sealed class BrandingRulesParityTests
{
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));

    public static TheoryData<string> Colours() =>
    [
        "#000000", "#FFFFFF", "#1f6feb", "#1F6FEB", "#aAbBcC", "#12345", "#1234567", "1F6FEB", "#GGGGGG", "red", "", "#", "# 12345", "#12 456",
        "#1F6FEB ", " #1F6FEB", "#1F6FEB\n", "rgb(1,2,3)", "#1F6FEBFF", "\uFF03123456",
    ];

    public static TheoryData<string> Logos() =>
    [
        "", "   ", "https://cdn.orbitly.example/logo.png", "HTTPS://CDN.ORBITLY.EXAMPLE/Logo.png", "  https://cdn.orbitly.example/logo.png  ",
        "https://cdn.orbitly.example:8443/a/b.svg?v=2#x", "http://cdn.orbitly.example/logo.png", "http://localhost/logo.png", "http://LOCALHOST:5080/logo.png",
        "http://127.0.0.1/logo.png", "http://127.0.0.1:8080/logo.png", "http://localhost.evil.example/logo.png", "http://[::1]/logo.png", "http://192.168.0.1/logo.png",
        "javascript:alert(1)", "JAVASCRIPT:alert(1)", "data:image/png;base64,AAAA", "vbscript:x", "file:///etc/passwd", "ftp://cdn.orbitly.example/logo.png",
        "/logo.svg", "logo.svg", "../logo.svg", "//cdn.orbitly.example/logo.png", "https://", "https:///logo.png", "https:logo.png",
        "https://user:secret@cdn.orbitly.example/logo.png", "https://cdn.orbitly.example/lo go.png", "https://cdn.orbitly.example/lo\ngo.png",
        "https://cdn.orbitly.example/\u0001.png", "https://m\u00FCnchen.example/logo.png",
    ];

    [Theory]
    [MemberData(nameof(Colours))]
    public void The_colour_pattern_and_the_domain_guard_accept_and_reject_the_same_values(string sample)
    {
        // Both sides work on the trimmed value: the Domain trims before it matches, and the Admin trims before it validates.
        var byPattern = Regex.IsMatch(sample.Trim(), BrandingRules.ColourPattern);

        ProductBranding.Create("Orbitly", null, sample, null, null).IsSuccess.ShouldBe(byPattern, $"product accent '{sample}'");
        Tag.Create("bug", "Bug", sample, Clock).IsSuccess.ShouldBe(byPattern, $"tag colour '{sample}'");
    }

    [Fact]
    public void The_colour_pattern_is_the_documented_rrggbb_shape()
    {
        BrandingRules.ColourPattern.ShouldBe("^#[0-9A-Fa-f]{6}$");
    }

    [Theory]
    [MemberData(nameof(Logos))]
    public void The_logo_rule_and_the_domain_guard_accept_and_reject_the_same_values(string sample)
    {
        ProductBranding.Create("Orbitly", sample, null, null, null).IsSuccess.ShouldBe(BrandingRules.IsAcceptableLogoUrl(sample), $"logo '{sample}'");
    }

    [Fact]
    public void The_logo_rule_and_the_domain_guard_agree_at_the_length_limit()
    {
        var prefix = "https://cdn.orbitly.example/";
        var atLimit = prefix + new string('a', BrandingRules.LogoUrlMaxLength - prefix.Length);
        var over = atLimit + "a";

        BrandingRules.LogoUrlMaxLength.ShouldBe(DomainLimits.UrlMaxLength);
        BrandingRules.IsAcceptableLogoUrl(atLimit).ShouldBeTrue();
        BrandingRules.IsAcceptableLogoUrl(over).ShouldBeFalse();
        ProductBranding.Create("Orbitly", atLimit, null, null, null).IsSuccess.ShouldBeTrue();
        ProductBranding.Create("Orbitly", over, null, null, null).IsSuccess.ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://cdn.orbitly.example/logo.png", true)]
    [InlineData("http://localhost:5080/logo.png", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=", false)]
    [InlineData("/logo.svg", false)]
    [InlineData("http://cdn.orbitly.example/logo.png", false)]
    public void The_logo_rule_answers_the_documented_cases(string? value, bool expected)
    {
        BrandingRules.IsAcceptableLogoUrl(value).ShouldBe(expected);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test --project tests/TechStrap.Domain.Tests -c Release --filter "ProductBrandingLogoTests|ProductTests"
dotnet test --project tests/TechStrap.Application.Tests -c Release --filter "AdminNamesParityTests|BrandingRulesParityTests"
```

Expected: in Domain.Tests the unsafe-logo cases of `An_unsafe_or_relative_logo_is_rejected_with_a_logo_path_error` fail with `System.NullReferenceException` (the branding was accepted, so `.Error` is null), and `A_logo_longer_than_the_limit_is_too_long_not_invalid` passes already. Application.Tests does not build: `CS0234 ... 'BrandingRules' does not exist in the namespace 'TechStrap.Contracts.Branding'` and `CS0234 ... 'AdminEventTypes' ... 'TechStrap.Contracts.AdminEvents'`.

- [ ] **Step 3: Add the Contracts constants and the Domain guard**

1. `src/TechStrap.Contracts/AdminEvents/AdminEventNames.cs`:

```csharp
namespace TechStrap.Contracts.AdminEvents;

/// <summary>Wire names for the admin event type enum (<c>AdminEventDto.Type</c>). Contracts carries no enums (naming rule).</summary>
public static class AdminEventTypes
{
    public const string ProductCreated = "ProductCreated";
    public const string ProductUpdated = "ProductUpdated";
    public const string ApiKeyCreated = "ApiKeyCreated";
    public const string ApiKeyRevoked = "ApiKeyRevoked";
    public const string AgentUpdated = "AgentUpdated";
    public const string TagCreated = "TagCreated";
    public const string TagUpdated = "TagUpdated";
    public const string TagDeleted = "TagDeleted";
    public const string RequesterErased = "RequesterErased";
    public const string TicketDeleted = "TicketDeleted";
    public const string DeadLetterRetried = "DeadLetterRetried";
    public const string DeadLetterDiscarded = "DeadLetterDiscarded";
}

/// <summary>Wire names for the admin subject type enum (<c>AdminEventDto.SubjectType</c> and the <c>subjectType</c> filter). Contracts carries no enums (naming rule).</summary>
public static class AdminSubjectTypes
{
    public const string Product = "Product";
    public const string ApiKey = "ApiKey";
    public const string Agent = "Agent";
    public const string Tag = "Tag";
    public const string Requester = "Requester";
    public const string Ticket = "Ticket";
    public const string EmailOutbox = "EmailOutbox";
}
```

2. `src/TechStrap.Contracts/Branding/BrandingRules.cs`:

```csharp
namespace TechStrap.Contracts.Branding;

/// <summary>
/// The branding field rules the Admin editor applies before it submits, so an agent sees the error at the field. The API stays the authority: the Domain
/// repeats each rule (<c>Guard.Colour</c>, <c>Guard.OptionalLogoUrl</c>) and a parity test in Application.Tests keeps the two in step, because Domain cannot reference Contracts.
/// </summary>
public static class BrandingRules
{
    /// <summary>A hex colour, <c>#RRGGBB</c>, either case. The API stores it upper-case.</summary>
    public const string ColourPattern = "^#[0-9A-Fa-f]{6}$";

    /// <summary>The longest logo URL, the same limit as the Domain (<c>DomainLimits.UrlMaxLength</c>).</summary>
    public const int LogoUrlMaxLength = 500;

    /// <summary>
    /// True for a blank value (no logo) and for an absolute <c>https</c> URL with a host and no user info. <c>http</c> is accepted only for the
    /// loopback hosts <c>localhost</c> and <c>127.0.0.1</c>, so a developer can serve a logo from a local machine. Everything else, including
    /// <c>javascript:</c>, <c>data:</c>, <c>file:</c>, relative paths and protocol-relative URLs, is refused, because the logo becomes an image source in
    /// customer emails and on the portal.
    /// </summary>
    public static bool IsAcceptableLogoUrl(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        if (text.Length > LogoUrlMaxLength || text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            return false;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Host.Length == 0)
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.Host is "localhost" or "127.0.0.1");
    }
}
```

3. `src/TechStrap.Contracts/Tags/TagDtos.cs`: add after `TagDto`:

```csharp
/// <summary>A tag with the number of tickets that carry it (any status, spam included). Admin only; the tag picker and the queue filters keep using <see cref="TagDto"/>.</summary>
public sealed record TagSummaryDto(Guid Id, string Slug, string Name, string Colour, int TicketCount);
```

4. `src/TechStrap.Domain/Rules/Guard.cs`: add these two members before `Colour` (they sit with the other text guards):

```csharp
    /// <summary>
    /// Blank becomes null. A value must be an absolute https URL with a host and no user info, or an http URL for the loopback hosts localhost and 127.0.0.1
    /// (a developer's machine); anything else is "{target}-invalid". The logo is rendered as an image source in emails and on the portal, so javascript:, data:,
    /// file: and relative paths never get in. The Contracts twin is <c>BrandingRules.IsAcceptableLogoUrl</c>; a parity test keeps them equal.
    /// </summary>
    public static DomainResult<string?> OptionalImageUrl(string? value, int maxLength, string target)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return DomainResult<string?>.Ok(null);
        }

        if (text.Length > maxLength)
        {
            return DomainErrors.Validation($"{target}-too-long", $"{target} must be at most {maxLength} characters.", target);
        }

        return IsSafeImageUrl(text)
            ? DomainResult<string?>.Ok(text)
            : DomainErrors.Validation($"{target}-invalid", $"{target} must be an https URL (or http for localhost).", target);
    }

    private static bool IsSafeImageUrl(string text)
    {
        if (text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Host.Length == 0)
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.Host is "localhost" or "127.0.0.1");
    }
```

  In `src/TechStrap.Domain/Products/Product.cs` change the logo line of `ProductBranding.Create` from `Guard.OptionalText(logoPath, DomainLimits.UrlMaxLength, "logo-path")` to `Guard.OptionalImageUrl(logoPath, DomainLimits.UrlMaxLength, "logo-path")`.

- [ ] **Step 4: Run the tests**

```bash
dotnet test --project tests/TechStrap.Domain.Tests -c Release
dotnet test --project tests/TechStrap.Application.Tests -c Release --filter "AdminNamesParityTests|BrandingRulesParityTests"
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: PASS (the Architecture tests prove the new public Contracts types follow the naming rule: `TagSummaryDto` ends in `Dto`, and the two constant holders and `BrandingRules` are static).

- [ ] **Step 5: Write the failing tests for the tag summary and the agent list**

1. `tests/TechStrap.Application.Tests/Tags/ListTagSummariesRequestHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tags;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tags;

public sealed class ListTagSummariesRequestHandlerTests
{
    [Fact]
    public async Task Summaries_carry_the_ticket_count_and_keep_the_repository_order()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));
        var billing = Tag.Create("billing", "Billing", "#2563EB", clock).Value;
        var bug = Tag.Create("bug", "Bug", "#DC2626", clock).Value;
        var unused = Tag.Create("unused", "Unused", "#16A34A", clock).Value;
        var tags = Substitute.For<ITagRepository>();
        tags.ListWithTicketCountsAsync(Arg.Any<CancellationToken>()).Returns([new TagUsage(billing, 3), new TagUsage(bug, 12), new TagUsage(unused, 0)]);

        var result = await new ListTagSummariesRequestHandler(tags).HandleAsync(TestContext.Current.CancellationToken);

        result.Value.Select(dto => (dto.Id, dto.Slug, dto.Name, dto.Colour, dto.TicketCount)).ShouldBe(
            [(billing.Id, "billing", "Billing", "#2563EB", 3), (bug.Id, "bug", "Bug", "#DC2626", 12), (unused.Id, "unused", "Unused", "#16A34A", 0)]);
    }

    [Fact]
    public async Task No_tags_is_an_empty_success()
    {
        var tags = Substitute.For<ITagRepository>();
        tags.ListWithTicketCountsAsync(Arg.Any<CancellationToken>()).Returns([]);

        var result = await new ListTagSummariesRequestHandler(tags).HandleAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }
}
```

2. `tests/TechStrap.Infrastructure.IntegrationTests/TagSummaryIntegrationTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Tickets;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The grouped count behind GET /api/tags/summary runs against real Postgres: one row per tag, a tag nobody carries reports 0, and the order is by name (D-041).</summary>
public sealed class TagSummaryIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_tag_reports_how_many_tickets_carry_it_and_an_unused_tag_reports_zero()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var bug = Tag.Create("bug", "Bug", "#DC2626", host.Clock).Value;
        var billing = Tag.Create("billing", "Billing", "#2563EB", host.Clock).Value;
        var unused = Tag.Create("unused", "Aardvark", "#16A34A", host.Clock).Value;
        (await host.CommitAsync(sp =>
        {
            var tags = sp.GetRequiredService<ITagRepository>();
            tags.Add(bug);
            tags.Add(billing);
            tags.Add(unused);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        await scenario.CreateTicketAsync("Open one", change: ticket => ticket.AddTag(bug.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue());
        await scenario.CreateTicketAsync("Closed one", change: ticket =>
        {
            ticket.AddTag(bug.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.AddTag(billing.Id, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Solved, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
            ticket.ChangeStatus(TicketStatus.Closed, scenario.AgentActor, host.Clock).IsSuccess.ShouldBeTrue();
        });

        var usage = await host.ReadAsync(sp => sp.GetRequiredService<ITagRepository>().ListWithTicketCountsAsync(Ct));

        // Ordered by name: Aardvark (unused), Billing, Bug.
        usage.Select(row => (row.Tag.Slug, row.TicketCount)).ShouldBe([("unused", 0), ("billing", 1), ("bug", 2)]);
        usage.Select(row => row.Tag.Id).ShouldBe([unused.Id, billing.Id, bug.Id]);
    }

    [Fact]
    public async Task With_no_tags_the_summary_is_empty()
    {
        await using var host = new PersistenceTestHost(Database);

        var usage = await host.ReadAsync(sp => sp.GetRequiredService<ITagRepository>().ListWithTicketCountsAsync(Ct));

        usage.ShouldBeEmpty();
    }
}
```

3. `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`: add `["TagsController.ListSummaries"] = 200,` after the `TagsController.List` row. In `TagsControllerTests.cs` add:

```csharp
    [Fact]
    public async Task ListSummaries_DelegatesAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<TagSummaryDto> summaries = [new TagSummaryDto(Tag.Id, "bug", "Bug", "#DC2626", 7)];
        var handler = Substitute.For<IListTagSummariesRequestHandler>();
        handler.HandleAsync(cancellation.Token).Returns(Result<IReadOnlyList<TagSummaryDto>>.Success(summaries));

        var result = await ControllerTestContext.For<TagsController>().ListSummaries(handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(summaries);
        await handler.Received(1).HandleAsync(cancellation.Token);
    }
```

4. `tests/TechStrap.Api.Tests/Tags/TagEndpointTests.cs`: give the helper an optional requester email, so a test can attach several tickets (each ticket needs its own requester, or the second commit is a duplicate conflict). Change its signature and its requester line:

```csharp
private static async Task AttachTagToNewTicketAsync(ApiFactory factory, Guid agentId, Guid productId, Guid tagId, string requesterEmail = "ann@example.com")
// ...
var requester = Requester.Create(requesterEmail, "Ann", null, clock).Value;
```

   and add the test:

```csharp
    [Fact]
    public async Task The_summary_lists_every_tag_with_its_ticket_count_and_only_an_admin_may_read_it()
    {
        var (factory, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var (productId, agentId) = await ProductAndAdminAsync(admin);
        var bug = await CreateAsync(admin, "bug");
        var billing = await CreateAsync(admin, "billing");
        await CreateAsync(admin, "unused");
        await AttachTagToNewTicketAsync(factory, agentId, productId, bug.Id, "ann@example.com");
        await AttachTagToNewTicketAsync(factory, agentId, productId, bug.Id, "bea@example.com");
        await AttachTagToNewTicketAsync(factory, agentId, productId, billing.Id, "cy@example.com");

        var summary = (await admin.GetFromJsonAsync<List<TagSummaryDto>>("/api/tags/summary", TestContext.Current.CancellationToken))!;
        using var asAgent = await agent.GetAsync("/api/tags/summary", TestContext.Current.CancellationToken);
        var plain = (await agent.GetFromJsonAsync<List<TagDto>>("/api/tags", TestContext.Current.CancellationToken))!;

        // CreateAsync names a tag after its slug, so name order is billing, bug, unused.
        summary.Select(tag => (tag.Slug, tag.TicketCount)).ShouldBe([("billing", 1), ("bug", 2), ("unused", 0)]);
        summary.Single(tag => tag.Slug == "bug").ShouldSatisfyAllConditions(
            tag => tag.Id.ShouldBe(bug.Id),
            tag => tag.Name.ShouldBe("bug"),
            tag => tag.Colour.ShouldBe("#DC2626"));
        asAgent.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        plain.Select(tag => tag.Slug).ShouldBe(["billing", "bug", "unused"]);
    }
```

5. `tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs`: change the line `adminOnly.Count.ShouldBeGreaterThanOrEqualTo(15); // 15 admin-only routes today; ...` to `adminOnly.Count.ShouldBeGreaterThanOrEqualTo(16); // 16 admin-only routes today (15 + the 07b tag summary); the floor catches IsAdminOnly silently matching fewer`, and add the test (it sits next to `The_five_06c_routes_are_admin_only`):

```csharp
    [Fact]
    public async Task The_07b_tag_summary_route_is_admin_only()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        var adminOnly = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/", StringComparison.Ordinal) == true)
            .Where(endpoint => IsAdminOnly(endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.MethodInfo))
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
            .ToHashSet();

        // GET api/tags stays Agent (the tag picker and the queue filters); only the summary with ticket counts is Admin.
        adminOnly.ShouldContain("GET api/tags/summary");
        adminOnly.ShouldNotContain("GET api/tags");
    }
```

6. `tests/TechStrap.Api.Tests/Products/ProductEndpointTests.cs`: add (it uses the class's existing `StartAsync` and `CreateAsync` helpers):

```csharp
    // Review Focus 4: the logo is rendered as an image source in emails and on the portal, so the API itself refuses an unsafe URL (the Admin editor only mirrors the rule).
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("/logo.svg")]
    [InlineData("//evil.example/logo.png")]
    [InlineData("file:///etc/passwd")]
    [InlineData("http://cdn.orbitly.example/logo.png")]
    [InlineData("https://user:secret@cdn.orbitly.example/logo.png")]
    public async Task An_unsafe_logo_url_is_400_with_a_logo_path_error_on_create_and_on_update_and_nothing_is_stored(string logo)
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var existing = await CreateAsync(admin, "orbitly", "ORB");
        var branding = new ProductBrandingRequest("Orbitly", logo, "#7c3aed", null, null);

        using var create = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest("unsafe", "Unsafe", "UNS", branding), TestContext.Current.CancellationToken);
        using var update = await admin.PutAsJsonAsync(
            $"/api/products/{existing.Id}", new UpdateProductRequest("Orbitly", branding, true, existing.Version), TestContext.Current.CancellationToken);

        foreach (var response in new[] { create, update })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            body.ShouldContain("\"logo-path\"");
            body.ShouldContain("logo-path-invalid");
        }

        var products = (await admin.GetFromJsonAsync<List<ProductDto>>("/api/products", TestContext.Current.CancellationToken))!;
        products.Select(product => product.Key).ShouldBe(["orbitly"]);
        products.Single().Branding.LogoPath.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://cdn.orbitly.example/logo.png", "https://cdn.orbitly.example/logo.png")]
    [InlineData("  https://cdn.orbitly.example/logo.png  ", "https://cdn.orbitly.example/logo.png")]
    [InlineData("http://localhost:5080/logo.png", "http://localhost:5080/logo.png")]
    [InlineData("http://127.0.0.1:5080/logo.png", "http://127.0.0.1:5080/logo.png")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public async Task A_safe_logo_url_or_a_blank_one_is_accepted_on_update_and_read_back(string? logo, string? expected)
    {
        var (factory, _, admin, agent) = await StartAsync();
        await using var _ = factory;
        using var __ = admin;
        using var ___ = agent;
        var existing = await CreateAsync(admin, "orbitly", "ORB");

        using var update = await admin.PutAsJsonAsync(
            $"/api/products/{existing.Id}",
            new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", logo, "#7c3aed", null, null), true, existing.Version),
            TestContext.Current.CancellationToken);

        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var read = (await agent.GetFromJsonAsync<ProductDto>($"/api/products/{existing.Id}", TestContext.Current.CancellationToken))!;
        read.Branding.LogoPath.ShouldBe(expected);
    }
```

7. `tests/TechStrap.Api.Tests/Agents/AgentManagementEndpointTests.cs`: add the admin list case. It pins existing behaviour, so it passes as soon as it compiles; Step 7 proves it is not vacuous.

```csharp
    [Fact]
    public async Task An_admin_sees_every_agent_with_email_role_active_flag_and_last_seen()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        using var other = factory.CreateClient().Bearer(TestJwt.Token("other", [TestJwt.AgentGroup], email: "other@example.com"));
        var adminMe = await SignInAsync(admin);
        var agentMe = await SignInAsync(agent);
        var otherMe = await SignInAsync(other);
        using var deactivate = await admin.PutAsJsonAsync($"/api/agents/{otherMe.Id}", new UpdateAgentRequest(false), TestContext.Current.CancellationToken);
        deactivate.EnsureSuccessStatusCode();

        var page = (await admin.GetFromJsonAsync<PagedResponse<AgentListItemDto>>("/api/agents", TestContext.Current.CancellationToken))!;

        page.TotalCount.ShouldBe(3);
        var rows = page.Items.ToDictionary(item => item.Id);
        rows.Keys.ShouldBe([adminMe.Id, agentMe.Id, otherMe.Id], ignoreOrder: true);
        rows[adminMe.Id].ShouldSatisfyAllConditions(
            row => row.Email.ShouldBe("admin@example.com"),
            row => row.Role.ShouldBe(AgentRoles.Admin),
            row => row.IsActive.ShouldBe(true),
            row => row.LastSeenAt.ShouldNotBeNull());
        rows[agentMe.Id].ShouldSatisfyAllConditions(
            row => row.Email.ShouldBe("agent@example.com"),
            row => row.Role.ShouldBe(AgentRoles.Agent),
            row => row.IsActive.ShouldBe(true),
            row => row.LastSeenAt.ShouldNotBeNull());
        rows[otherMe.Id].ShouldSatisfyAllConditions(
            row => row.Email.ShouldBe("other@example.com"),
            row => row.Role.ShouldBe(AgentRoles.Agent),
            row => row.IsActive.ShouldBe(false),
            row => row.LastSeenAt.ShouldNotBeNull());
    }
```

- [ ] **Step 6: Run the tests and confirm they fail**

```bash
dotnet test --project tests/TechStrap.Application.Tests -c Release --filter ListTagSummariesRequestHandlerTests
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter TagSummaryIntegrationTests
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter "TagsControllerTests|TagEndpointTests|AgentAccessCoverageTests|ProductEndpointTests|AgentManagementEndpointTests"
```

Expected: none of the three projects builds: `CS0246 ... 'TagSummaryDto'`, `CS0246 ... 'IListTagSummariesRequestHandler'`, `CS1061 ... 'ITagRepository' does not contain a definition for 'ListWithTicketCountsAsync'`, `CS0246 ... 'TagUsage'` and `CS1061 ... 'TagsController' does not contain a definition for 'ListSummaries'`. (`ProductEndpointTests` fails once it builds: before Step 3 the unsafe URLs answered 201 and 200.)

- [ ] **Step 7: Implement the summary**

1. `src/TechStrap.Application/Persistence/ITagRepository.cs`: add the method after `ListAsync` and the record after the interface:

```csharp
    /// <summary>Every tag with the number of tickets that carry it (any status, spam included), ordered by name. One grouped query; read only.</summary>
    Task<IReadOnlyList<TagUsage>> ListWithTicketCountsAsync(CancellationToken cancellationToken);
```

```csharp
/// <summary>A tag and how many tickets carry it.</summary>
public sealed record TagUsage(Tag Tag, int TicketCount);
```

2. `src/TechStrap.Infrastructure/Persistence/Repositories/RequesterAndTagRepositories.cs`: add to `TagRepository`, after `ListAsync`:

```csharp
    public async Task<IReadOnlyList<TagUsage>> ListWithTicketCountsAsync(CancellationToken cancellationToken)
    {
        // One query: a correlated count over the join table, so a tag with no tickets reports 0 and no ticket rows are loaded.
        var rows = await context.Set<TagRecord>().AsNoTracking()
            .OrderBy(t => t.Name).ThenBy(t => t.Id)
            .Select(t => new { Tag = t, TicketCount = context.Set<TicketTagRecord>().Count(link => link.TagId == t.Id) })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new TagUsage(row.Tag.ToDomain(), row.TicketCount))];
    }
```

   (The method is one `Select` with a correlated `Count` over `TicketTagRecord`: the count is a subquery per tag, so a tag nobody carries reports 0 and no ticket row is loaded. `TagSummaryIntegrationTests` proves the numbers against Postgres.)

3. `src/TechStrap.Application/Tags/TagMapping.cs`: add `using TechStrap.Application.Persistence;` and, in `TagMapping`:

```csharp
    public static TagSummaryDto ToSummaryDto(TagUsage usage) => new(usage.Tag.Id, usage.Tag.Slug, usage.Tag.Name, usage.Tag.Colour, usage.TicketCount);
```

4. `src/TechStrap.Application/Tags/ListTagSummariesRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Tags;

namespace TechStrap.Application.Tags;

public interface IListTagSummariesRequestHandler
{
    Task<Result<IReadOnlyList<TagSummaryDto>>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>GET /api/tags/summary (Admin): every tag with its ticket count, ordered by name, for the Admin tag list and the delete confirmation (D-041).</summary>
public sealed class ListTagSummariesRequestHandler(ITagRepository tags) : IListTagSummariesRequestHandler
{
    public async Task<Result<IReadOnlyList<TagSummaryDto>>> HandleAsync(CancellationToken cancellationToken) =>
        Result<IReadOnlyList<TagSummaryDto>>.Success([.. (await tags.ListWithTicketCountsAsync(cancellationToken)).Select(TagMapping.ToSummaryDto)]);
}
```

5. `src/TechStrap.Api/Controllers/TagsController.cs`: add after `List`:

```csharp
    /// <summary>Every tag with the number of tickets that carry it (Admin). The tag picker keeps using <see cref="List"/>.</summary>
    [HttpGet("summary")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> ListSummaries([FromServices] IListTagSummariesRequestHandler listSummaries, CancellationToken cancellationToken) =>
        (await listSummaries.HandleAsync(cancellationToken)).ToActionResult(this, Ok);
```

- [ ] **Step 8: Run the tests, prove the agent-list test is not vacuous, check the model**

```bash
dotnet test --project tests/TechStrap.Application.Tests -c Release
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter "TagSummaryIntegrationTests|ProductRepositoryTests|DeleteTagIntegrationTests"
dotnet test --project tests/TechStrap.Api.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release
```

Expected: PASS, and `No changes have been made to the model since the last migration.` (a failure of the last command would mean a migration is needed; generate it with `dotnet ef`, never by hand).

Non-vacuity check for the agent-list test (do not commit it): in `src/TechStrap.Application/Agents/ListAgentsRequestHandler.cs` change `includeAdminFields: isAdmin` to `includeAdminFields: false`, run `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter An_admin_sees_every_agent_with_email_role_active_flag_and_last_seen`, expect it to FAIL (`Shouldly.ShouldAssertException : rows[adminMe.Id]`), then restore the line with `git checkout src/TechStrap.Application/Agents/ListAgentsRequestHandler.cs`.

- [ ] **Step 9: Guards and commit**

```bash
dotnet build TechStrap.slnx -c Release
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "SourceEncodingTests|SourceEscapeTests"
grep -rnP "[^\x00-\x7F]" tests/TechStrap.Domain.Tests/Products/ProductBrandingLogoTests.cs tests/TechStrap.Application.Tests/Products/BrandingRulesParityTests.cs src/TechStrap.Contracts/Branding src/TechStrap.Contracts/AdminEvents
```

Expected: 0 warnings; PASS; the grep prints nothing. Then:

```bash
git add src tests
git diff --cached --stat
git commit -m "feat(api): validate the product logo URL; add the tag summary endpoint, the admin event constants and the branding rules" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 3: The management clients, the error codes and `StubApiHandler.ValidationProblem`

**Files:**
- Modify: `src/TechStrap.Admin/Clients/ReferenceDataClients.cs`, `src/TechStrap.Admin/Clients/IAgentsClient.cs`, `src/TechStrap.Admin/Clients/ApiErrorCodes.cs`, `src/TechStrap.Admin/Clients/ApiClientRegistration.cs`
- Create: `src/TechStrap.Admin/Clients/AdminEventsClient.cs`, `src/TechStrap.Admin/Clients/DeadLettersClient.cs`, `src/TechStrap.Admin/Clients/ApiFields.cs`
- Modify: `tests/Shared/AdminHost/StubApiHandler.cs`
- Create: `tests/TechStrap.Admin.Tests/Clients/ProductsClientTests.cs`, `AgentsAndTagsClientTests.cs`, `AdminEventsAndDeadLettersClientTests.cs`, `StubApiHandlerValidationTests.cs`, `ManagementErrorCodesTests.cs`

**Interfaces:**
- Consumes: 07a `ApiConnection` (`GetAsync<T>(uri, ct)` through the retrying read client; `SendAsync<T>(method, uri, body, ct)` and `SendAsync(method, uri, body, ct)` through the write client), `ApiUri.Build`, `ProblemMapping`, `ApiHarness`, `StubApiHandler`, `AdminTestPrincipal`; Contracts DTOs of `Products`, `ApiKeys`, `Agents`, `Tags`, `AdminEvents`, `DeadLetters`, `Paging`; from Task 2 `TagSummaryDto`, `AdminEventTypes`, `AdminSubjectTypes`.
- Produces (every name is fixed; Tasks 5 to 9 call them):

```csharp
namespace TechStrap.Admin.Clients;

public interface IProductsClient
{
    Task<Result<IReadOnlyList<ProductDto>>> ListAsync(CancellationToken cancellationToken);                                                       // existing
    Task<Result<ProductDto>> GetAsync(Guid id, CancellationToken cancellationToken);                                                              // GET api/products/{id}
    Task<Result<ProductDto>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);                                     // POST api/products
    Task<Result<ProductDto>> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken);                             // PUT api/products/{id}; the request carries IsActive and Version
    Task<Result<IReadOnlyList<ProductApiKeyDto>>> ListApiKeysAsync(Guid productId, CancellationToken cancellationToken);                          // GET api/products/{id}/api-keys
    Task<Result<CreateProductApiKeyResponse>> CreateApiKeyAsync(Guid productId, CreateProductApiKeyRequest request, CancellationToken cancellationToken); // POST, 201
    Task<Result> RevokeApiKeyAsync(Guid productId, Guid keyId, CancellationToken cancellationToken);                                              // DELETE, 204
}

public interface IAgentsClient
{
    Task<Result<AgentDto>> GetMeAsync(CancellationToken cancellationToken);                                                                       // existing
    Task<Result<IReadOnlyList<AgentListItemDto>>> ListAllAsync(CancellationToken cancellationToken);                                              // existing
    Task<Result<PagedResponse<AgentListItemDto>>> ListPageAsync(int page, int pageSize, CancellationToken cancellationToken);                      // GET api/agents?page=&pageSize=
    Task<Result<AgentDto>> SetActiveAsync(Guid agentId, bool isActive, CancellationToken cancellationToken);                                       // PUT api/agents/{id} with {isActive}
    Task<Result> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken);                                        // PUT api/agents/me/profile, 204
    Task<Result<IReadOnlyList<NotificationPreferenceDto>>> GetNotificationPreferencesAsync(CancellationToken cancellationToken);                   // GET api/agents/me/notification-preferences
    Task<Result> UpdateNotificationPreferencesAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken);            // PUT, 204
}

public interface ITagsClient
{
    Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken);                                                           // existing
    Task<Result<IReadOnlyList<TagSummaryDto>>> ListSummaryAsync(CancellationToken cancellationToken);                                              // GET api/tags/summary (Admin)
    Task<Result<TagDto>> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken);                                              // POST api/tags
    Task<Result<TagDto>> UpdateAsync(Guid id, UpdateTagRequest request, CancellationToken cancellationToken);                                     // PUT api/tags/{id}
    Task<Result> DeleteAsync(Guid id, bool force, CancellationToken cancellationToken);                                                            // DELETE api/tags/{id}[?force=true], 204
}

public sealed record AdminEventFilter(string? SubjectType = null, Guid? ActorId = null, DateTimeOffset? AsOf = null);
public interface IAdminEventsClient
{
    Task<Result<PagedResponse<AdminEventDto>>> ListAsync(AdminEventFilter filter, int page, int pageSize, CancellationToken cancellationToken);   // GET api/admin-events
}

public interface IDeadLettersClient
{
    Task<Result<PagedResponse<DeadLetterDto>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);                            // GET api/dead-letters
    Task<Result> RetryAsync(Guid id, CancellationToken cancellationToken);                                                                         // POST api/dead-letters/{id}/retry, 204
    Task<Result> DiscardAsync(Guid id, CancellationToken cancellationToken);                                                                       // DELETE api/dead-letters/{id}, 204
    Task<Result<int>> CountAsync(CancellationToken cancellationToken);                                                                             // ListAsync(1, 1).TotalCount; Admin only
}

public static class ApiErrorCodes   // new consts, each value is its wire code:
    // TagSlugTaken "tag-slug-taken", TagInUse "tag-in-use", LastActiveAdmin "last-active-admin", ProductKeyTaken "product-key-taken",
    // ApiKeyKindInvalid "api-key-kind-invalid", ApiKeyNotFound "api-key-not-found", ApiKeyRevoked "api-key-revoked", OutboxNotFound "outbox-not-found",
    // OutboxNotDeadLettered "outbox-not-dead-lettered", AdminEventSubjectTypeInvalid "admin-event-subject-type-invalid",
    // PublicDisplayNameTooLong "public-display-name-too-long", PublicDisplayNameInvalid "public-display-name-invalid", LogoPathInvalid "logo-path-invalid"

public static class ApiFields   // the kebab-case targets of a 400: Key "key", Name "name", NumberPrefix "number-prefix", DisplayName "display-name", LogoPath "logo-path",
    // AccentColour "accent-colour", FromAddress "from-address", ReplyTo "reply-to", Kind "kind", Label "label", Slug "slug", Colour "colour", PublicDisplayName "public-display-name"

// tests/Shared/AdminHost/StubApiHandler.cs
public static HttpResponseMessage ValidationProblem(string target, string code, string message);                                    // one field error, a 400 with errors and errorCodes
public static HttpResponseMessage ValidationProblem(IReadOnlyList<(string Target, string Code, string Message)> errors);            // several; an empty target is a form-level error
public StubApiHandler OnValidationProblem(HttpMethod method, string path, string target, string code, string message);               // route helper
```

**Rules:**
1. **Reads retry, writes never.** The list and get methods use `connection.GetAsync` (read client, two retries). Every create, update, delete, retry, discard, activate and profile or preferences save uses `connection.SendAsync` (write client). A write is never retried and a page never cancels it: its caller passes `CancellationToken.None` (07a rule; the interface still takes a token so the reads can be cancelled).
2. **`UpdateProductRequest.IsActive` is not nullable.** The client sends the request as built, so the editor always passes the current value; an absent flag would deactivate the product (Review Focus 3). `UpdateAsync` is a pass-through and the tests pin that `isActive` and `version` are in the body.
3. **Query strings go through `ApiUri.Build`,** which skips null and empty values. `DeleteAsync(id, force: false)` sends no query; `force: true` sends `?force=true` (`ApiUri` formats a `bool` as `True`, so the client passes the string). `asOf` is sent as an ISO 8601 round-trip string (`ToString("O")`), because the default formatting of a `DateTimeOffset` loses sub-second ticks and is ambiguous.
4. **The kebab-case targets are the API's.** A 400 arrives as `ResultError.Target` exactly as the API sent it (`logo-path`, `accent-colour`, `public-display-name`), not camelCase. Forms compare targets with the `ApiFields` constants. `subjectType` (the audit filter) is the one camelCase target, because it is a query parameter name.
5. **The new codes are refusals, not uncertain writes.** `WriteOutcomes.Classify` answers `Other` for them and `ApiErrorCodes.IsUncertainWrite` is false; pages branch on the code (for example `LastActiveAdmin`, `TagSlugTaken`, `TagInUse`, `OutboxNotDeadLettered`) and fall back to the API message. `ConcurrencyConflict` stays the product conflict code.
6. **`IDeadLettersClient.CountAsync` is Admin only.** It is a page of one. A failed count is a failure result, never a zero; the rail keeps the last number (Task 4).
7. **`StubApiHandler` is shared** by `TechStrap.Admin.Tests` and `TechStrap.Api.Tests` (`tests/Shared`), so the new members must compile in both. `ValidationProblem` produces what the API produces: `type` `validation-failed`, `errors` and `errorCodes` keyed by the field, so a form test never passes against a shape the real API does not send.
8. **Registration.** `IAdminEventsClient` and `IDeadLettersClient` are scoped in `AddTechStrapApiClients`, beside the other typed clients. The implementations are `internal sealed` over `ApiConnection`.

- [ ] **Step 1: Write the failing tests**

1. `tests/TechStrap.Admin.Tests/Clients/StubApiHandlerValidationTests.cs`:

```csharp
using System.Net;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Tags;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>
/// <see cref="StubApiHandler.ValidationProblem(string, string, string)"/> must produce what the API produces, or every form test that uses it would pass against a
/// shape the real API never sends. Read through the real ProblemMapping, it yields one error per field and code with the kebab-case target.
/// </summary>
public sealed class StubApiHandlerValidationTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_validation_problem_reaches_the_client_as_a_field_error_with_the_code_and_message()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnValidationProblem(HttpMethod.Post, "/api/tags", ApiFields.Colour, "colour-invalid", "colour must be a #RRGGBB colour.");

        var result = await api.Get<ITagsClient>().CreateAsync(new CreateTagRequest("bug", "Bug", "red"), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError("colour-invalid", "colour must be a #RRGGBB colour.", ResultErrorKind.Validation, "colour"));
    }

    [Fact]
    public async Task Several_errors_keep_their_fields_and_an_empty_target_is_a_form_level_error()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.On(HttpMethod.Post, "/api/tags", _ => StubApiHandler.ValidationProblem(
        [
            ("slug", "slug-invalid", "slug must be lower-case."),
            ("name", "name-required", "name is required."),
            ("name", "name-too-long", "name is too long."),
            ("", "form-invalid", "Fix the form."),
        ]));

        var result = await api.Get<ITagsClient>().CreateAsync(new CreateTagRequest(null, null, null), Ct);

        result.Errors.Select(e => (e.Target, e.Code)).ShouldBe(
            [("slug", "slug-invalid"), ("name", "name-required"), ("name", "name-too-long"), (null, "form-invalid")], ignoreOrder: true);
        result.Errors.ShouldAllBe(e => e.Kind == ResultErrorKind.Validation);
    }

    [Fact]
    public async Task The_answer_is_a_400_problem_json_with_errors_and_errorCodes_keyed_by_the_kebab_field()
    {
        using var response = StubApiHandler.ValidationProblem("logo-path", "logo-path-invalid", "logo-path must be an https URL.");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("\"type\":\"validation-failed\"");
        body.ShouldContain("\"errors\":{\"logo-path\":[\"logo-path must be an https URL.\"]}");
        body.ShouldContain("\"errorCodes\":{\"logo-path\":[\"logo-path-invalid\"]}");
    }
}
```

2. `tests/TechStrap.Admin.Tests/Clients/ProductsClientTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.Products;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class ProductsClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid KeyId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static ProductDto Product(uint version = 3, bool isActive = true) => new(
        ProductId, "orbitly", "Orbitly", "ORB", isActive, new ProductBrandingDto("Orbitly", null, "#2563EB", "#FFFFFF", "#1E40AF", null, null), version);

    private static ProductApiKeyDto Key(string kind = ApiKeyKinds.Trusted) =>
        new(KeyId, ProductId, kind, "tsk_ab12", "Server", DateTimeOffset.UtcNow, null, null);

    private static JsonElement Body(ApiHarness api) => JsonDocument.Parse(api.Stub.Requests.Last().Body!).RootElement;

    [Fact]
    public async Task Get_reads_the_product_and_a_404_keeps_the_product_not_found_code()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, $"/api/products/{ProductId}", Product());

        (await api.Get<IProductsClient>().GetAsync(ProductId, Ct)).Value.Version.ShouldBe(3u);

        api.Stub.OnProblem(HttpMethod.Get, $"/api/products/{ProductId}", HttpStatusCode.NotFound, ApiErrorCodes.ProductNotFound, "No such product.");
        (await api.Get<IProductsClient>().GetAsync(ProductId, Ct)).Errors.ShouldHaveSingleItem()
            .ShouldBe(new ResultError(ApiErrorCodes.ProductNotFound, "No such product.", ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task Create_posts_the_request_and_returns_the_product()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Post, "/api/products", Product(), HttpStatusCode.Created);

        var result = await api.Get<IProductsClient>().CreateAsync(new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, "#2563EB", null, null)), Ct);

        result.Value.NumberPrefix.ShouldBe("ORB");
        var body = Body(api);
        body.GetProperty("key").GetString().ShouldBe("orbitly");
        body.GetProperty("numberPrefix").GetString().ShouldBe("ORB");
        body.GetProperty("branding").GetProperty("accentColour").GetString().ShouldBe("#2563EB");
    }

    // Review Focus 3: IsActive is not nullable in UpdateProductRequest, so it must always travel; a missing flag would deactivate the product. The version travels too.
    [Fact]
    public async Task Update_sends_the_version_and_the_active_flag_in_the_body()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Put, $"/api/products/{ProductId}", Product(version: 4));

        var result = await api.Get<IProductsClient>().UpdateAsync(
            ProductId, new UpdateProductRequest("Orbitly Cloud", new ProductBrandingRequest("Orbitly", "https://cdn.orbitly.example/logo.png", "#2563EB", "help@orbitly.example", null), IsActive: true, Version: 3), Ct);

        result.Value.Version.ShouldBe(4u);
        var body = Body(api);
        body.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        body.GetProperty("version").GetUInt32().ShouldBe(3u);
        body.GetProperty("name").GetString().ShouldBe("Orbitly Cloud");
        body.GetProperty("branding").GetProperty("logoPath").GetString().ShouldBe("https://cdn.orbitly.example/logo.png");
        api.Stub.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Put);
    }

    [Fact]
    public async Task Update_keeps_a_deactivation_as_false_and_maps_a_stale_version_to_a_conflict_result()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnProblem(HttpMethod.Put, $"/api/products/{ProductId}", HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict, "This product changed since you opened it.");

        var result = await api.Get<IProductsClient>().UpdateAsync(
            ProductId, new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", null, null, null, null), IsActive: false, Version: 2), Ct);

        Body(api).GetProperty("isActive").GetBoolean().ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.ConcurrencyConflict, "This product changed since you opened it.", ResultErrorKind.Conflict));
    }

    [Fact]
    public async Task A_400_on_update_maps_each_kebab_case_field_to_its_own_error()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.On(HttpMethod.Put, $"/api/products/{ProductId}", _ => StubApiHandler.ValidationProblem(
        [
            (ApiFields.LogoPath, ApiErrorCodes.LogoPathInvalid, "logo-path must be an https URL (or http for localhost)."),
            (ApiFields.AccentColour, "accent-colour-invalid", "accent-colour must be a #RRGGBB colour."),
        ]));

        var result = await api.Get<IProductsClient>().UpdateAsync(
            ProductId, new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", "javascript:alert(1)", "purple", null, null), true, 3), Ct);

        result.Errors.Select(e => (e.Target, e.Code)).ShouldBe(
            [(ApiFields.LogoPath, ApiErrorCodes.LogoPathInvalid), (ApiFields.AccentColour, "accent-colour-invalid")], ignoreOrder: true);
    }

    [Fact]
    public async Task A_duplicate_key_is_a_conflict_with_the_product_key_taken_code()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnProblem(HttpMethod.Post, "/api/products", HttpStatusCode.Conflict, ApiErrorCodes.ProductKeyTaken, "That key or number prefix is already used.");

        var result = await api.Get<IProductsClient>().CreateAsync(new CreateProductRequest("orbitly", "Orbitly", "ORB", null), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ProductKeyTaken);
    }

    [Fact]
    public async Task Api_keys_are_listed_created_and_revoked_on_the_product_routes()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub
            .OnJson(HttpMethod.Get, $"/api/products/{ProductId}/api-keys", new[] { Key(), Key(ApiKeyKinds.Public) })
            .OnJson(HttpMethod.Post, $"/api/products/{ProductId}/api-keys", new CreateProductApiKeyResponse(Key(ApiKeyKinds.Public), "tsk_the-plaintext-secret"), HttpStatusCode.Created)
            .OnStatus(HttpMethod.Delete, $"/api/products/{ProductId}/api-keys/{KeyId}", HttpStatusCode.NoContent);
        var client = api.Get<IProductsClient>();

        (await client.ListApiKeysAsync(ProductId, Ct)).Value.Select(k => k.Kind).ShouldBe([ApiKeyKinds.Trusted, ApiKeyKinds.Public]);
        var created = await client.CreateApiKeyAsync(ProductId, new CreateProductApiKeyRequest(ApiKeyKinds.Public, "Website"), Ct);
        (await client.RevokeApiKeyAsync(ProductId, KeyId, Ct)).IsSuccess.ShouldBeTrue();

        created.Value.PlaintextKey.ShouldBe("tsk_the-plaintext-secret");
        created.Value.Key.Kind.ShouldBe(ApiKeyKinds.Public);
        api.Stub.Requests.Select(r => (r.Method.Method, r.Path)).ShouldBe(
        [
            ("GET", $"/api/products/{ProductId}/api-keys"),
            ("POST", $"/api/products/{ProductId}/api-keys"),
            ("DELETE", $"/api/products/{ProductId}/api-keys/{KeyId}"),
        ]);
        var post = api.Stub.Requests[1];
        JsonDocument.Parse(post.Body!).RootElement.GetProperty("kind").GetString().ShouldBe("Public");
        JsonDocument.Parse(post.Body!).RootElement.GetProperty("label").GetString().ShouldBe("Website");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task Creating_a_key_is_never_retried_and_an_unknown_outcome_is_an_uncertain_write(HttpStatusCode status)
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnStatus(HttpMethod.Post, $"/api/products/{ProductId}/api-keys", status);

        var result = await api.Get<IProductsClient>().CreateApiKeyAsync(ProductId, new CreateProductApiKeyRequest("Trusted", null), Ct);

        ApiErrorCodes.IsUncertainWrite(result.Errors[0].Code).ShouldBeTrue();
        api.Stub.Count(HttpMethod.Post, $"/api/products/{ProductId}/api-keys").ShouldBe(1);
    }

    [Fact]
    public async Task A_bad_kind_and_a_missing_key_keep_their_codes()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnValidationProblem(HttpMethod.Post, $"/api/products/{ProductId}/api-keys", ApiFields.Kind, ApiErrorCodes.ApiKeyKindInvalid, "Choose Trusted or Public.");
        api.Stub.OnProblem(HttpMethod.Delete, $"/api/products/{ProductId}/api-keys/{KeyId}", HttpStatusCode.NotFound, ApiErrorCodes.ApiKeyNotFound, "That API key does not exist for this product.");
        var client = api.Get<IProductsClient>();

        (await client.CreateApiKeyAsync(ProductId, new CreateProductApiKeyRequest("Other", null), Ct)).Errors.ShouldHaveSingleItem()
            .ShouldBe(new ResultError(ApiErrorCodes.ApiKeyKindInvalid, "Choose Trusted or Public.", ResultErrorKind.Validation, "kind"));
        (await client.RevokeApiKeyAsync(ProductId, KeyId, Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.ApiKeyNotFound);
    }

    [Fact]
    public async Task Every_product_call_carries_the_bearer_token()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, $"/api/products/{ProductId}", Product()).OnJson(HttpMethod.Put, $"/api/products/{ProductId}", Product());
        var client = api.Get<IProductsClient>();

        await client.GetAsync(ProductId, Ct);
        await client.UpdateAsync(ProductId, new UpdateProductRequest("Orbitly", new ProductBrandingRequest("Orbitly", null, null, null, null), true, 3), Ct);

        api.Stub.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }
}
```

3. `tests/TechStrap.Admin.Tests/Clients/AgentsAndTagsClientTests.cs`:

```csharp
using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tags;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class AgentsAndTagsClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid AgentId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid TagId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static JsonElement Body(ApiHarness api) => JsonDocument.Parse(api.Stub.Requests.Last().Body!).RootElement;

    [Fact]
    public async Task ListPage_sends_the_page_and_the_page_size_and_returns_the_admin_fields()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        var row = new AgentListItemDto(AgentId, "Sam", "Sam (Orbitly)", "sam@example.com", AgentRoles.Agent, true, DateTimeOffset.UtcNow);
        api.Stub.OnJson(HttpMethod.Get, "/api/agents", new PagedResponse<AgentListItemDto>([row], 3, 25, 61));

        var result = await api.Get<IAgentsClient>().ListPageAsync(3, 25, Ct);

        result.Value.TotalCount.ShouldBe(61);
        result.Value.Items.Single().ShouldSatisfyAllConditions(
            r => r.Email.ShouldBe("sam@example.com"),
            r => r.Role.ShouldBe(AgentRoles.Agent),
            r => r.IsActive.ShouldBe(true));
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe("?page=3&pageSize=25");
    }

    [Fact]
    public async Task SetActive_puts_only_the_flag_and_returns_the_agent()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Put, $"/api/agents/{AgentId}", new AgentDto(AgentId, "Sam", "sam@example.com", AgentRoles.Agent, false, null, null));

        var result = await api.Get<IAgentsClient>().SetActiveAsync(AgentId, false, Ct);

        result.Value.IsActive.ShouldBeFalse();
        var body = Body(api);
        body.EnumerateObject().Select(p => p.Name).ShouldBe(["isActive"]);
        body.GetProperty("isActive").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Deactivating_the_last_active_admin_is_a_conflict_with_its_own_code_and_message()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnProblem(HttpMethod.Put, $"/api/agents/{AgentId}", HttpStatusCode.Conflict, ApiErrorCodes.LastActiveAdmin, "TechStrap needs at least one active admin.");

        var result = await api.Get<IAgentsClient>().SetActiveAsync(AgentId, false, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.LastActiveAdmin, "TechStrap needs at least one active admin.", ResultErrorKind.Conflict));
    }

    [Fact]
    public async Task UpdateMyProfile_puts_the_name_and_a_204_is_a_success()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnStatus(HttpMethod.Put, "/api/agents/me/profile", HttpStatusCode.NoContent);

        var result = await api.Get<IAgentsClient>().UpdateMyProfileAsync(new UpdateMyProfileRequest("Samantha"), Ct);

        result.IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("publicDisplayName").GetString().ShouldBe("Samantha");
    }

    [Theory]
    [InlineData("public-display-name-too-long")]
    [InlineData("public-display-name-invalid")]
    public async Task UpdateMyProfile_maps_a_rejected_name_to_the_public_display_name_field(string code)
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnValidationProblem(HttpMethod.Put, "/api/agents/me/profile", ApiFields.PublicDisplayName, code, "A public display name is plain text without '@'.");

        var result = await api.Get<IAgentsClient>().UpdateMyProfileAsync(new UpdateMyProfileRequest("sam@x"), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(code, "A public display name is plain text without '@'.", ResultErrorKind.Validation, "public-display-name"));
    }

    [Fact]
    public async Task Notification_preferences_are_read_as_a_list_and_written_as_the_full_set()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub
            .OnJson(HttpMethod.Get, "/api/agents/me/notification-preferences", new[] { new NotificationPreferenceDto(ProductId, "Orbitly", true), new NotificationPreferenceDto(Guid.NewGuid(), "Acme", false) })
            .OnStatus(HttpMethod.Put, "/api/agents/me/notification-preferences", HttpStatusCode.NoContent);
        var client = api.Get<IAgentsClient>();

        var read = await client.GetNotificationPreferencesAsync(Ct);
        var write = await client.UpdateNotificationPreferencesAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(ProductId, false), new NotificationPreferenceUpdateDto(Guid.Parse("dddddddd-0000-0000-0000-000000000001"), true)]), Ct);

        read.Value.Select(p => (p.ProductName, p.NotifyNewTicket)).ShouldBe([("Orbitly", true), ("Acme", false)]);
        write.IsSuccess.ShouldBeTrue();
        var preferences = Body(api).GetProperty("preferences").EnumerateArray().ToList();
        preferences.Count.ShouldBe(2);
        preferences[0].GetProperty("productId").GetGuid().ShouldBe(ProductId);
        preferences[0].GetProperty("notifyNewTicket").GetBoolean().ShouldBeFalse();
        preferences[1].GetProperty("notifyNewTicket").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_summary_is_read_from_the_summary_route_and_keeps_the_ticket_count()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/tags/summary", new[] { new TagSummaryDto(TagId, "bug", "Bug", "#DC2626", 12) });

        var result = await api.Get<ITagsClient>().ListSummaryAsync(Ct);

        result.Value.Single().ShouldBe(new TagSummaryDto(TagId, "bug", "Bug", "#DC2626", 12));
        api.Stub.Requests.ShouldHaveSingleItem().Path.ShouldBe("/api/tags/summary");
    }

    [Fact]
    public async Task Create_and_update_send_the_request_and_return_the_tag()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        var tag = new TagDto(TagId, "bug", "Bug", "#DC2626");
        api.Stub.OnJson(HttpMethod.Post, "/api/tags", tag, HttpStatusCode.Created).OnJson(HttpMethod.Put, $"/api/tags/{TagId}", tag);
        var client = api.Get<ITagsClient>();

        (await client.CreateAsync(new CreateTagRequest("bug", "Bug", "#dc2626"), Ct)).Value.ShouldBe(tag);
        var created = JsonDocument.Parse(api.Stub.Requests[0].Body!).RootElement;
        created.GetProperty("slug").GetString().ShouldBe("bug");
        created.GetProperty("colour").GetString().ShouldBe("#dc2626");

        (await client.UpdateAsync(TagId, new UpdateTagRequest("Defect", "#2563EB"), Ct)).IsSuccess.ShouldBeTrue();
        api.Stub.Requests[1].Method.ShouldBe(HttpMethod.Put);
        JsonDocument.Parse(api.Stub.Requests[1].Body!).RootElement.GetProperty("name").GetString().ShouldBe("Defect");
    }

    [Fact]
    public async Task A_duplicate_slug_is_a_conflict_with_the_tag_slug_taken_code()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnProblem(HttpMethod.Post, "/api/tags", HttpStatusCode.Conflict, ApiErrorCodes.TagSlugTaken, "Another tag already uses this slug.");

        var result = await api.Get<ITagsClient>().CreateAsync(new CreateTagRequest("bug", "Bug", "#DC2626"), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.TagSlugTaken, "Another tag already uses this slug.", ResultErrorKind.Conflict));
    }

    [Fact]
    public async Task Delete_without_force_sends_no_query_and_with_force_sends_force_true()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnStatus(HttpMethod.Delete, $"/api/tags/{TagId}", HttpStatusCode.NoContent);
        var client = api.Get<ITagsClient>();

        (await client.DeleteAsync(TagId, force: false, Ct)).IsSuccess.ShouldBeTrue();
        (await client.DeleteAsync(TagId, force: true, Ct)).IsSuccess.ShouldBeTrue();

        api.Stub.Requests.Select(r => r.Query).ShouldBe(["", "?force=true"]);
        api.Stub.Requests.ShouldAllBe(r => r.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task A_tag_in_use_is_a_conflict_whose_message_keeps_the_count()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnProblem(HttpMethod.Delete, $"/api/tags/{TagId}", HttpStatusCode.Conflict, ApiErrorCodes.TagInUse, "This tag is on 3 tickets. Delete it with force to remove it from them first.");

        var result = await api.Get<ITagsClient>().DeleteAsync(TagId, force: false, Ct);

        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe(ApiErrorCodes.TagInUse);
        error.Kind.ShouldBe(ResultErrorKind.Conflict);
        error.Message.ShouldContain("3 tickets");
    }
}
```

4. `tests/TechStrap.Admin.Tests/Clients/AdminEventsAndDeadLettersClientTests.cs`:

```csharp
using System.Net;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

public sealed class AdminEventsAndDeadLettersClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid ActorId = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid LetterId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private static AdminEventDto Event(string type = AdminEventTypes.TagCreated, string subject = AdminSubjectTypes.Tag) =>
        new(Guid.NewGuid(), type, ActorId, "Ada", subject, Guid.NewGuid(), "{\"slug\":\"bug\"}", new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));

    private static DeadLetterDto Letter() =>
        new(LetterId, "agent-reply", "a***@example.com", Guid.NewGuid(), Guid.NewGuid(), 5, "smtp-transient", new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Events_send_the_filter_and_the_paging_as_the_query_string()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/admin-events", new PagedResponse<AdminEventDto>([Event()], 2, 25, 51));
        var asOf = new DateTimeOffset(2026, 10, 4, 12, 30, 15, TimeSpan.Zero).AddTicks(1234567);

        var result = await api.Get<IAdminEventsClient>().ListAsync(new AdminEventFilter(AdminSubjectTypes.Tag, ActorId, asOf), 2, 25, Ct);

        result.Value.TotalCount.ShouldBe(51);
        result.Value.Items.Single().Type.ShouldBe("TagCreated");
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe(
            $"?subjectType=Tag&actorId={ActorId}&asOf=2026-10-04T12%3A30%3A15.1234567%2B00%3A00&page=2&pageSize=25");
    }

    [Fact]
    public async Task An_empty_filter_sends_only_the_paging()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/admin-events", new PagedResponse<AdminEventDto>([], 1, 25, 0));

        await api.Get<IAdminEventsClient>().ListAsync(new AdminEventFilter(), 1, 25, Ct);

        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe("?page=1&pageSize=25");
    }

    [Fact]
    public async Task An_unknown_subject_type_is_a_validation_error_on_the_subject_type_field()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnValidationProblem(HttpMethod.Get, "/api/admin-events", "subjectType", ApiErrorCodes.AdminEventSubjectTypeInvalid, "Use one of: Product, ApiKey.");

        var result = await api.Get<IAdminEventsClient>().ListAsync(new AdminEventFilter("Nonsense"), 1, 25, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.AdminEventSubjectTypeInvalid, "Use one of: Product, ApiKey.", ResultErrorKind.Validation, "subjectType"));
    }

    [Fact]
    public async Task Dead_letters_are_listed_with_their_paging()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/dead-letters", new PagedResponse<DeadLetterDto>([Letter()], 1, 25, 1));

        var result = await api.Get<IDeadLettersClient>().ListAsync(1, 25, Ct);

        result.Value.Items.Single().ShouldSatisfyAllConditions(
            l => l.Recipient.ShouldBe("a***@example.com"),
            l => l.LastError.ShouldBe("smtp-transient"),
            l => l.Attempts.ShouldBe(5));
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe("?page=1&pageSize=25");
    }

    [Fact]
    public async Task Count_is_the_total_of_a_page_of_one()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/dead-letters", new PagedResponse<DeadLetterDto>([Letter()], 1, 1, 7));

        var result = await api.Get<IDeadLettersClient>().CountAsync(Ct);

        result.Value.ShouldBe(7);
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe("?page=1&pageSize=1");
    }

    [Fact]
    public async Task A_failed_count_is_a_failure_with_the_api_code_not_a_zero()
    {
        await using var api = await ApiHarness.CreateAsync();
        api.Stub.OnProblem(HttpMethod.Get, "/api/dead-letters", HttpStatusCode.Forbidden, ApiErrorCodes.AdminAccessRequired, "Only admins may do this.");

        var result = await api.Get<IDeadLettersClient>().CountAsync(Ct);

        result.IsFailure.ShouldBeTrue();
        result.Errors[0].ShouldBe(new ResultError(ApiErrorCodes.AdminAccessRequired, "Only admins may do this.", ResultErrorKind.Forbidden));
    }

    [Fact]
    public async Task Retry_posts_and_discard_deletes_the_letter_and_neither_is_ever_retried()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnStatus(HttpMethod.Post, $"/api/dead-letters/{LetterId}/retry", HttpStatusCode.ServiceUnavailable);
        api.Stub.OnStatus(HttpMethod.Delete, $"/api/dead-letters/{LetterId}", HttpStatusCode.ServiceUnavailable);
        var client = api.Get<IDeadLettersClient>();

        var retry = await client.RetryAsync(LetterId, Ct);
        var discard = await client.DiscardAsync(LetterId, Ct);

        ApiErrorCodes.IsUncertainWrite(retry.Errors[0].Code).ShouldBeTrue();
        ApiErrorCodes.IsUncertainWrite(discard.Errors[0].Code).ShouldBeTrue();
        api.Stub.Count(HttpMethod.Post, $"/api/dead-letters/{LetterId}/retry").ShouldBe(1);
        api.Stub.Count(HttpMethod.Delete, $"/api/dead-letters/{LetterId}").ShouldBe(1);
    }

    [Fact]
    public async Task Retry_and_discard_succeed_on_204_and_keep_the_not_found_and_not_dead_lettered_codes()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnStatus(HttpMethod.Post, $"/api/dead-letters/{LetterId}/retry", HttpStatusCode.NoContent);
        api.Stub.OnProblem(HttpMethod.Delete, $"/api/dead-letters/{LetterId}", HttpStatusCode.Conflict, ApiErrorCodes.OutboxNotDeadLettered, "Only a dead-lettered email can be discarded.");
        var client = api.Get<IDeadLettersClient>();

        (await client.RetryAsync(LetterId, Ct)).IsSuccess.ShouldBeTrue();
        (await client.DiscardAsync(LetterId, Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.OutboxNotDeadLettered);

        api.Stub.OnProblem(HttpMethod.Post, $"/api/dead-letters/{LetterId}/retry", HttpStatusCode.NotFound, ApiErrorCodes.OutboxNotFound, "No such email.");
        (await client.RetryAsync(LetterId, Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe(ApiErrorCodes.OutboxNotFound);
    }

    [Fact]
    public async Task Both_new_clients_are_registered_and_send_the_admins_token()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, "/api/dead-letters", new PagedResponse<DeadLetterDto>([], 1, 1, 0));
        api.Stub.OnJson(HttpMethod.Get, "/api/admin-events", new PagedResponse<AdminEventDto>([], 1, 25, 0));

        await api.Get<IDeadLettersClient>().CountAsync(Ct);
        await api.Get<IAdminEventsClient>().ListAsync(new AdminEventFilter(), 1, 25, Ct);

        api.Stub.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }
}
```

5. `tests/TechStrap.Admin.Tests/Clients/ManagementErrorCodesTests.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>The codes the 07b pages branch on are the API's wire strings. A typo here would make a page treat a known conflict as a generic failure.</summary>
public sealed class ManagementErrorCodesTests
{
    [Theory]
    [InlineData(ApiErrorCodes.TagSlugTaken, "tag-slug-taken")]
    [InlineData(ApiErrorCodes.TagInUse, "tag-in-use")]
    [InlineData(ApiErrorCodes.LastActiveAdmin, "last-active-admin")]
    [InlineData(ApiErrorCodes.ProductKeyTaken, "product-key-taken")]
    [InlineData(ApiErrorCodes.ApiKeyKindInvalid, "api-key-kind-invalid")]
    [InlineData(ApiErrorCodes.ApiKeyNotFound, "api-key-not-found")]
    [InlineData(ApiErrorCodes.ApiKeyRevoked, "api-key-revoked")]
    [InlineData(ApiErrorCodes.OutboxNotFound, "outbox-not-found")]
    [InlineData(ApiErrorCodes.OutboxNotDeadLettered, "outbox-not-dead-lettered")]
    [InlineData(ApiErrorCodes.AdminEventSubjectTypeInvalid, "admin-event-subject-type-invalid")]
    [InlineData(ApiErrorCodes.PublicDisplayNameTooLong, "public-display-name-too-long")]
    [InlineData(ApiErrorCodes.PublicDisplayNameInvalid, "public-display-name-invalid")]
    [InlineData(ApiErrorCodes.LogoPathInvalid, "logo-path-invalid")]
    public void The_management_codes_are_the_api_wire_codes(string constant, string wire) => constant.ShouldBe(wire);

    [Theory]
    [InlineData(ApiErrorCodes.TagSlugTaken)]
    [InlineData(ApiErrorCodes.TagInUse)]
    [InlineData(ApiErrorCodes.LastActiveAdmin)]
    [InlineData(ApiErrorCodes.ProductKeyTaken)]
    [InlineData(ApiErrorCodes.OutboxNotFound)]
    [InlineData(ApiErrorCodes.OutboxNotDeadLettered)]
    public void A_refusal_is_never_an_uncertain_write_and_is_classified_as_other(string code)
    {
        ApiErrorCodes.IsUncertainWrite(code).ShouldBeFalse();
        WriteOutcomes.Classify(new ResultError(code, "m", ResultErrorKind.Conflict)).ShouldBe(WriteOutcome.Other);
    }

    [Fact]
    public void The_field_targets_are_the_kebab_case_names_the_api_sends()
    {
        string[] targets =
        [
            ApiFields.Key, ApiFields.Name, ApiFields.NumberPrefix, ApiFields.DisplayName, ApiFields.LogoPath, ApiFields.AccentColour, ApiFields.FromAddress, ApiFields.ReplyTo,
            ApiFields.Kind, ApiFields.Label, ApiFields.Slug, ApiFields.Colour, ApiFields.PublicDisplayName,
        ];

        targets.ShouldBe(
            ["key", "name", "number-prefix", "display-name", "logo-path", "accent-colour", "from-address", "reply-to", "kind", "label", "slug", "colour", "public-display-name"]);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "StubApiHandlerValidationTests|ProductsClientTests|AgentsAndTagsClientTests|AdminEventsAndDeadLettersClientTests|ManagementErrorCodesTests"
```

Expected: the project does not build: `CS0117 ... 'ApiErrorCodes' does not contain a definition for 'TagSlugTaken'` (and the other new codes), `CS0103 ... 'ApiFields' does not exist`, `CS0234 ... 'IAdminEventsClient'`/`'IDeadLettersClient'`/`'AdminEventFilter'`, `CS1061 ... 'IProductsClient' does not contain a definition for 'GetAsync'` (and the other new members), and `CS1061 ... 'StubApiHandler' does not contain a definition for 'OnValidationProblem'`.

- [ ] **Step 3: Add the error codes, the field names and the stub helper**

1. `src/TechStrap.Admin/Clients/ApiErrorCodes.cs`: add after `AdminAccessRequired`, in the "Produced by the API" group:

```csharp
    public const string TagSlugTaken = "tag-slug-taken";
    public const string TagInUse = "tag-in-use";
    public const string LastActiveAdmin = "last-active-admin";
    public const string ProductKeyTaken = "product-key-taken";
    public const string ApiKeyKindInvalid = "api-key-kind-invalid";
    public const string ApiKeyNotFound = "api-key-not-found";
    public const string ApiKeyRevoked = "api-key-revoked";
    public const string OutboxNotFound = "outbox-not-found";
    public const string OutboxNotDeadLettered = "outbox-not-dead-lettered";
    public const string AdminEventSubjectTypeInvalid = "admin-event-subject-type-invalid";
    public const string PublicDisplayNameTooLong = "public-display-name-too-long";
    public const string PublicDisplayNameInvalid = "public-display-name-invalid";
    public const string LogoPathInvalid = "logo-path-invalid";
```

2. `src/TechStrap.Admin/Clients/ApiFields.cs`:

```csharp
namespace TechStrap.Admin.Clients;

/// <summary>
/// The field names a 400 answer targets (<see cref="SyntaxCircus.Common.ResultError.Target"/>), exactly as the API sends them: kebab-case, not the camelCase of the JSON
/// request. A form maps an error to its input by comparing the target with one of these.
/// </summary>
public static class ApiFields
{
    // Products and branding.
    public const string Key = "key";
    public const string Name = "name";
    public const string NumberPrefix = "number-prefix";
    public const string DisplayName = "display-name";
    public const string LogoPath = "logo-path";
    public const string AccentColour = "accent-colour";
    public const string FromAddress = "from-address";
    public const string ReplyTo = "reply-to";

    // API keys.
    public const string Kind = "kind";
    public const string Label = "label";

    // Tags.
    public const string Slug = "slug";
    public const string Colour = "colour";

    // Agents.
    public const string PublicDisplayName = "public-display-name";
}
```

3. `tests/Shared/AdminHost/StubApiHandler.cs`: add this route helper before `WithTestAgents` (after `OnProblem`), and the two static members before `SendAsync`:

```csharp
    /// <summary>A 400 validation answer for one field: see <see cref="ValidationProblem(string, string, string)"/>.</summary>
    public StubApiHandler OnValidationProblem(HttpMethod method, string path, string target, string code, string message) =>
        On(method, path, _ => ValidationProblem(target, code, message));
```

```csharp
    /// <summary>
    /// A 400 in the shape the API produces for a validation failure: <c>type</c> "validation-failed", the message per field in <c>errors</c> and the specific code per field in
    /// <c>errorCodes</c> (the Admin's <c>ProblemMapping</c> reads the codes from there). <paramref name="target"/> is the field exactly as the API sends it, kebab-case
    /// (for example <c>logo-path</c>); an empty target is a form-level error.
    /// </summary>
    public static HttpResponseMessage ValidationProblem(string target, string code, string message) => ValidationProblem([(target, code, message)]);

    /// <summary>The same for several errors; errors on one field keep their order.</summary>
    public static HttpResponseMessage ValidationProblem(IReadOnlyList<(string Target, string Code, string Message)> errors)
    {
        var messages = errors.GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
        var codes = errors.GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.Select(e => e.Code).ToArray());
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new { type = "validation-failed", title = "One or more validation errors occurred.", status = 400, detail = errors[0].Message, errors = messages, errorCodes = codes },
                    Json),
                Encoding.UTF8,
                "application/problem+json"),
        };
    }
```

- [ ] **Step 4: Add the clients**

1. `src/TechStrap.Admin/Clients/IAgentsClient.cs` (the interface file; replace it):

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;

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

    /// <summary><c>GET /api/agents?page=&amp;pageSize=</c>: one page for the Agents screen. The API clamps both numbers (the page size to 100), so a 400 is not expected.</summary>
    Task<Result<PagedResponse<AgentListItemDto>>> ListPageAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// <c>PUT /api/agents/{id}</c> (Admin): activates or deactivates an agent and returns the agent. Never retried. A 409 <c>last-active-admin</c> means the
    /// change would leave no active admin; a 404 <c>agent-not-found</c> means the agent is gone. Roles cannot be changed here (D-029, D-041).
    /// </summary>
    Task<Result<AgentDto>> SetActiveAsync(Guid agentId, bool isActive, CancellationToken cancellationToken);

    /// <summary><c>PUT /api/agents/me/profile</c> (204): sets, or with a blank name clears, the customer-facing display name. 400 fields: public-display-name (too long, or contains @).</summary>
    Task<Result> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken);

    /// <summary><c>GET /api/agents/me/notification-preferences</c>: one row per active product, in the API's order.</summary>
    Task<Result<IReadOnlyList<NotificationPreferenceDto>>> GetNotificationPreferencesAsync(CancellationToken cancellationToken);

    /// <summary><c>PUT /api/agents/me/notification-preferences</c> (204): the full set of per-product choices. Idempotent.</summary>
    Task<Result> UpdateNotificationPreferencesAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken);
}
```

2. `src/TechStrap.Admin/Clients/ReferenceDataClients.cs` (replace the file; `AgentsClient`, `ProductsClient` and `TagsClient` keep their existing members):

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Clients;

/// <summary>
/// Products and their API keys. The reads work for any agent; every write and every key call is Admin only (the API enforces it with 403 admin-access-required).
/// Writes are never retried.
/// </summary>
public interface IProductsClient
{
    /// <summary>Active products for an Agent, all products for an Admin.</summary>
    Task<Result<IReadOnlyList<ProductDto>>> ListAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/products/{id}</c>. 404 is product-not-found. The answer carries the <c>Version</c> the next update must send.</summary>
    Task<Result<ProductDto>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/products</c> (Admin, 201). 409 product-key-taken (the key or the number prefix is used); 400 fields (kebab-case): key, name, number-prefix,
    /// display-name, logo-path, accent-colour, from-address, reply-to.
    /// </summary>
    Task<Result<ProductDto>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// <c>PUT /api/products/{id}</c> (Admin): sends <see cref="UpdateProductRequest.Version"/> and <see cref="UpdateProductRequest.IsActive"/>, which is not nullable, so a
    /// caller always passes the current value (an absent flag would deactivate the product). 409 concurrency-conflict when the version is stale. Returns the re-read product.
    /// </summary>
    Task<Result<ProductDto>> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken);

    /// <summary><c>GET /api/products/{id}/api-keys</c> (Admin): newest first, revoked keys included, never a secret.</summary>
    Task<Result<IReadOnlyList<ProductApiKeyDto>>> ListApiKeysAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/products/{id}/api-keys</c> (Admin, 201). The answer holds <see cref="CreateProductApiKeyResponse.PlaintextKey"/> once. This is a write: it is never retried, and a lost
    /// answer leaves a key whose secret nobody saw (the page says to revoke it and create another). 400 api-key-kind-invalid (field kind) or label-too-long (field label).
    /// </summary>
    Task<Result<CreateProductApiKeyResponse>> CreateApiKeyAsync(Guid productId, CreateProductApiKeyRequest request, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/products/{id}/api-keys/{keyId}</c> (Admin, 204). Idempotent: revoking a revoked key succeeds. 404 api-key-not-found.</summary>
    Task<Result> RevokeApiKeyAsync(Guid productId, Guid keyId, CancellationToken cancellationToken);
}

/// <summary>Tags. <see cref="ListAsync"/> serves every agent (the tag picker and the queue filter); the summary and the writes are Admin only.</summary>
public interface ITagsClient
{
    Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken);

    /// <summary><c>GET /api/tags/summary</c> (Admin): every tag with the number of tickets that carry it, ordered by name (D-041).</summary>
    Task<Result<IReadOnlyList<TagSummaryDto>>> ListSummaryAsync(CancellationToken cancellationToken);

    /// <summary><c>POST /api/tags</c> (Admin, 201). 409 tag-slug-taken; 400 fields: slug, name, colour.</summary>
    Task<Result<TagDto>> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken);

    /// <summary><c>PUT /api/tags/{id}</c> (Admin). The slug is permanent. 404 tag-not-found; 400 fields: name, colour.</summary>
    Task<Result<TagDto>> UpdateAsync(Guid id, UpdateTagRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// <c>DELETE /api/tags/{id}</c> (Admin, 204). With <paramref name="force"/> false a tag that is on tickets is a 409 tag-in-use whose message names the count; with true the tag is
    /// removed from every ticket first, and each of them records a TagRemoved event.
    /// </summary>
    Task<Result> DeleteAsync(Guid id, bool force, CancellationToken cancellationToken);
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

    public Task<Result<PagedResponse<AgentListItemDto>>> ListPageAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        connection.GetAsync<PagedResponse<AgentListItemDto>>(ApiUri.Build("api/agents", ("page", page), ("pageSize", pageSize)), cancellationToken);

    public Task<Result<AgentDto>> SetActiveAsync(Guid agentId, bool isActive, CancellationToken cancellationToken) =>
        connection.SendAsync<AgentDto>(HttpMethod.Put, $"api/agents/{agentId}", new UpdateAgentRequest(isActive), cancellationToken);

    public Task<Result> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Put, "api/agents/me/profile", request, cancellationToken);

    public async Task<Result<IReadOnlyList<NotificationPreferenceDto>>> GetNotificationPreferencesAsync(CancellationToken cancellationToken) =>
        ProductsClient.Narrow(await connection.GetAsync<List<NotificationPreferenceDto>>("api/agents/me/notification-preferences", cancellationToken));

    public Task<Result> UpdateNotificationPreferencesAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Put, "api/agents/me/notification-preferences", request, cancellationToken);
}

internal sealed class ProductsClient(ApiConnection connection) : IProductsClient
{
    public async Task<Result<IReadOnlyList<ProductDto>>> ListAsync(CancellationToken cancellationToken) =>
        Narrow(await connection.GetAsync<List<ProductDto>>("api/products", cancellationToken));

    public Task<Result<ProductDto>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        connection.GetAsync<ProductDto>($"api/products/{id}", cancellationToken);

    public Task<Result<ProductDto>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<ProductDto>(HttpMethod.Post, "api/products", request, cancellationToken);

    public Task<Result<ProductDto>> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<ProductDto>(HttpMethod.Put, $"api/products/{id}", request, cancellationToken);

    public async Task<Result<IReadOnlyList<ProductApiKeyDto>>> ListApiKeysAsync(Guid productId, CancellationToken cancellationToken) =>
        Narrow(await connection.GetAsync<List<ProductApiKeyDto>>($"api/products/{productId}/api-keys", cancellationToken));

    public Task<Result<CreateProductApiKeyResponse>> CreateApiKeyAsync(Guid productId, CreateProductApiKeyRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<CreateProductApiKeyResponse>(HttpMethod.Post, $"api/products/{productId}/api-keys", request, cancellationToken);

    public Task<Result> RevokeApiKeyAsync(Guid productId, Guid keyId, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Delete, $"api/products/{productId}/api-keys/{keyId}", null, cancellationToken);

    internal static Result<IReadOnlyList<T>> Narrow<T>(Result<List<T>> result) =>
        result.IsSuccess ? Result<IReadOnlyList<T>>.Success(result.Value) : Result<IReadOnlyList<T>>.Failure(result.Errors[0], [.. result.Errors.Skip(1)]);
}

internal sealed class TagsClient(ApiConnection connection) : ITagsClient
{
    public async Task<Result<IReadOnlyList<TagDto>>> ListAsync(CancellationToken cancellationToken) =>
        ProductsClient.Narrow(await connection.GetAsync<List<TagDto>>("api/tags", cancellationToken));

    public async Task<Result<IReadOnlyList<TagSummaryDto>>> ListSummaryAsync(CancellationToken cancellationToken) =>
        ProductsClient.Narrow(await connection.GetAsync<List<TagSummaryDto>>("api/tags/summary", cancellationToken));

    public Task<Result<TagDto>> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TagDto>(HttpMethod.Post, "api/tags", request, cancellationToken);

    public Task<Result<TagDto>> UpdateAsync(Guid id, UpdateTagRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<TagDto>(HttpMethod.Put, $"api/tags/{id}", request, cancellationToken);

    public Task<Result> DeleteAsync(Guid id, bool force, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Delete, ApiUri.Build($"api/tags/{id}", ("force", force ? "true" : null)), null, cancellationToken);
}
```

3. `src/TechStrap.Admin/Clients/AdminEventsClient.cs`:

```csharp
using System.Globalization;
using SyntaxCircus.Common;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Clients;

/// <summary>
/// What the audit page filters by. <see cref="SubjectType"/> is one of <c>AdminSubjectTypes</c>; <see cref="AsOf"/> is the first page's timestamp, passed
/// back on every later page so new events do not shift the pages under the reader. A null member is not sent.
/// </summary>
public sealed record AdminEventFilter(string? SubjectType = null, Guid? ActorId = null, DateTimeOffset? AsOf = null);

/// <summary>The admin audit log (Admin only, newest first).</summary>
public interface IAdminEventsClient
{
    /// <summary>
    /// <c>GET /api/admin-events</c>. 400 admin-event-subject-type-invalid (field subjectType) for an unknown subject type. The API has no event-type filter and no date range.
    /// </summary>
    Task<Result<PagedResponse<AdminEventDto>>> ListAsync(AdminEventFilter filter, int page, int pageSize, CancellationToken cancellationToken);
}

internal sealed class AdminEventsClient(ApiConnection connection) : IAdminEventsClient
{
    public Task<Result<PagedResponse<AdminEventDto>>> ListAsync(AdminEventFilter filter, int page, int pageSize, CancellationToken cancellationToken) =>
        connection.GetAsync<PagedResponse<AdminEventDto>>(
            ApiUri.Build(
                "api/admin-events",
                ("subjectType", filter.SubjectType),
                ("actorId", filter.ActorId),
                ("asOf", filter.AsOf?.ToString("O", CultureInfo.InvariantCulture)),
                ("page", page),
                ("pageSize", pageSize)),
            cancellationToken);
}
```

4. `src/TechStrap.Admin/Clients/DeadLettersClient.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Clients;

/// <summary>Emails that ran out of attempts (Admin only: every call is a 403 for a plain agent, so only an admin session may make one).</summary>
public interface IDeadLettersClient
{
    /// <summary><c>GET /api/dead-letters</c>: the recipient is masked and the payload is never returned.</summary>
    Task<Result<PagedResponse<DeadLetterDto>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/dead-letters/{id}/retry</c> (204): puts the email back in the queue. 404 outbox-not-found; 409 outbox-not-dead-lettered (someone else already
    /// retried or discarded it). Never retried by the client.
    /// </summary>
    Task<Result> RetryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/dead-letters/{id}</c> (204): the email is dropped and will never be sent. Same 404 and 409 as retry. Never retried by the client.</summary>
    Task<Result> DiscardAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>How many emails are dead-lettered: the <c>TotalCount</c> of a page of one, for the navigation badge.</summary>
    Task<Result<int>> CountAsync(CancellationToken cancellationToken);
}

internal sealed class DeadLettersClient(ApiConnection connection) : IDeadLettersClient
{
    public Task<Result<PagedResponse<DeadLetterDto>>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        connection.GetAsync<PagedResponse<DeadLetterDto>>(ApiUri.Build("api/dead-letters", ("page", page), ("pageSize", pageSize)), cancellationToken);

    public Task<Result> RetryAsync(Guid id, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Post, $"api/dead-letters/{id}/retry", null, cancellationToken);

    public Task<Result> DiscardAsync(Guid id, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Delete, $"api/dead-letters/{id}", null, cancellationToken);

    public async Task<Result<int>> CountAsync(CancellationToken cancellationToken)
    {
        var page = await ListAsync(1, 1, cancellationToken);
        return page.IsSuccess ? Result<int>.Success(page.Value.TotalCount) : Result<int>.Failure(page.Errors[0], [.. page.Errors.Skip(1)]);
    }
}
```

5. `src/TechStrap.Admin/Clients/ApiClientRegistration.cs`: add after the `IRequestersClient` line of `AddTechStrapApiClients`:

```csharp
        services.AddScoped<IAdminEventsClient, AdminEventsClient>();
        services.AddScoped<IDeadLettersClient, DeadLettersClient>();
```

- [ ] **Step 5: Run the tests**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "StubApiHandlerValidationTests|ProductsClientTests|AgentsAndTagsClientTests|AdminEventsAndDeadLettersClientTests|ManagementErrorCodesTests|ReferenceDataClientTests|ApiConnectionTests"
dotnet build tests/TechStrap.Api.Tests -c Release
```

Expected: PASS, and the Api.Tests build proves the shared `StubApiHandler` still compiles there.

- [ ] **Step 6: Commit**

```bash
git add src tests
git diff --cached --stat
git commit -m "feat(admin): typed clients for products, api keys, agents, tags, audit and dead letters" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 4: The Admin shell: `AdminOnly`, a reload that keeps the session, the admin links, browser preferences

**Review Focus pins:**
- **(1)** A plain agent must not reach an admin page by URL, nav or keyboard, and the guard must not flicker or deny while the session loads or reloads. Pinned by `AdminOnlyTests` (the content is never built for a plain agent; no "Checking" frame and no restart during a reload; a demotion or a deactivation withdraws the content), `AgentSessionTests` (the reload keeps Ready), `NavMenuTests` (no admin link and no badge call for a plain agent; no admin shortcut navigates) and, at host level, Task 9.

**Files:**
- Modify: `src/TechStrap.Admin/Auth/AgentSession.cs`
- Create: `src/TechStrap.Admin/Components/Ui/AdminOnly.razor`, `AdminOnly.razor.cs`
- Modify: `src/TechStrap.Admin/Components/Ui/ShellCopy.cs`, `src/TechStrap.Admin/Components/Layout/NavMenu.razor`, `NavMenu.razor.cs`, `MainLayout.razor.cs`, `src/TechStrap.Admin/Styles/_shell.scss`
- Create: `src/TechStrap.Admin/Features/Shell/PreferencesService.cs`, `FailedEmailCounter.cs`
- Modify: `src/TechStrap.Admin/Features/Shell/ShellServiceCollectionExtensions.cs`, `ShortcutService.cs` (one comment)
- Create: `src/TechStrap.Admin/wwwroot/js/preferences.js`
- Modify: `tests/TechStrap.Admin.Tests/Auth/AgentSessionTests.cs`, `Support/AdminComponentTest.cs`, `Components/ShellTestServices.cs`, `Components/ShellComponentTests.cs`, `ScriptHostTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/AdminOnlyTests.cs`, `NavMenuTests.cs`, `PreferencesServiceTests.cs`, `FailedEmailCounterTests.cs`, `tests/TechStrap.Admin.Tests/RailStyleTests.cs`, `tests/TechStrap.Admin.Tests/js/preferences.test.mjs`
- Modify: `scripts/tests/AdminScripts.Tests.ps1`

**Interfaces:**
- Consumes: 07a `AgentSession` (`State`, `Agent`, `IsAdmin`, `Changed`, `EnsureLoadedAsync`), `AgentGate`, `NoAccessPage` (`ThisPageOnly`), `NoAccessCopy.PageTitle`, `GateCopy.Checking`, `NavMenu`, `MainLayout`, `ShortcutService.SingleKeyEnabled`, `ShellServiceCollectionExtensions.AddShell`, `ShellCopy`, the bUnit base class `AdminComponentTest` and `ShellTestServices.AddAgentShell`; from Task 3 `IDeadLettersClient.CountAsync`.
- Produces (names are fixed; Tasks 5 to 9 use them):

```csharp
namespace TechStrap.Admin.Auth;
public sealed class AgentSession
{
    // unchanged members, plus a changed behaviour:
    public Task ReloadAsync(CancellationToken cancellationToken);   // a Ready session stays Ready, with the current Agent, until the answer arrives; Changed is raised once then
}

namespace TechStrap.Admin.Components.Ui;
public sealed partial class AdminOnly : ComponentBase, IDisposable
{
    [Parameter] public RenderFragment? ChildContent { get; set; }
    // NotLoaded: <p class="ts-gate" role="status">Checking your access...</p>; Ready and admin: ChildContent;
    // Ready and not admin: <NoAccessPage ThisPageOnly="true" /> and ChildContent is never built; NoAccess/SessionExpired/Unavailable: nothing (AgentGate owns those screens)
}

namespace TechStrap.Admin.Components.Ui;   // ShellCopy additions
public const string MySettingsLink = "My settings";        // href /account/notifications
public const string AdminLinksLabel = "Admin";             // aria-label of the admin group
public const string ProductsLink = "Products";             // /settings/products
public const string AgentsLink = "Agents";                 // /settings/agents
public const string TagsLink = "Tags";                     // /settings/tags
public const string AuditLink = "Audit";                   // /settings/audit
public const string FailedEmailsLink = "Failed emails";    // /ops/dead-letters
public static string FailedEmailsCountLabel(int count);    // "{count} waiting"

namespace TechStrap.Admin.Features.Shell;
public enum ThemeChoice { Auto, Light, Dark }
public sealed record StoredPreferences(bool SingleKeyShortcuts, string Theme);   // what preferences.js load() returns
public sealed class PreferencesService(IJSRuntime js, ShortcutService shortcuts) : IAsyncDisposable   // scoped, registered by AddShell
{
    public const string ModulePath = "./js/preferences.js";
    public bool IsLoaded { get; }
    public ThemeChoice Theme { get; }
    public bool SingleKeyShortcuts { get; }                       // = ShortcutService.SingleKeyEnabled
    public event Action? Changed;
    public Task LoadAsync();                                      // once; never throws for a script or storage failure
    public Task SetSingleKeyShortcutsAsync(bool enabled);         // sets ShortcutService.SingleKeyEnabled, then stores
    public Task SetThemeAsync(ThemeChoice theme);                 // applies and stores
}
public sealed class FailedEmailCounter(IDeadLettersClient deadLetters)   // scoped, registered by AddShell; admin sessions only
{
    public int? Count { get; }                                    // null until a refresh succeeded; a failed refresh keeps the last number
    public event Action? Changed;
    public Task RefreshAsync(CancellationToken cancellationToken);   // IDeadLettersClient.CountAsync
    public void Set(int count);                                   // the dead letters page already holds the total; negative becomes 0
}
```

```javascript
// src/TechStrap.Admin/wwwroot/js/preferences.js (ES module; no top-level DOM access, so node can import it)
export const SINGLE_KEY = 'singleKeyShortcuts'; export const THEME = 'theme';
export const THEMES = ['auto', 'light', 'dark']; export const DEFAULTS = { singleKeyShortcuts: true, theme: 'auto' };
export function normaliseTheme(value)                  // anything but light/dark/auto -> 'auto'
export function readPreferences(storage)               // { singleKeyShortcuts, theme }; never throws; null/blocked storage -> DEFAULTS
export function writePreference(storage, key, value)   // true when stored; false for an unknown key/value or a refusing storage; never throws
export function applyTheme(doc, theme)                 // 'light'/'dark' set data-bs-theme on documentElement, 'auto' removes it; returns the applied theme; never throws
export function load()                                 // browser: readPreferences(localStorage) + applyTheme(document); returns the preferences
export function save(key, value)                       // browser: writePreference; applies the theme for key 'theme' even when it could not be stored; returns the boolean
```

  Test support: `AdminComponentTest.Preferences` (the `BunitJSModuleInterop` of `./js/preferences.js`, set up with `load` = defaults and `save` = true); `ShellTestServices.AddAgentShell` also registers a substitute `IDeadLettersClient` whose `CountAsync` answers 0 (read it back with `Services.GetRequiredService<IDeadLettersClient>()`).

**Rules:**
1. **The role is the API's answer.** `AdminOnly` and `NavMenu` read `AgentSession.IsAdmin`. Nothing in the Admin parses a group claim (D-040). Hiding a link or an action is a convenience: every admin call is still a 403 for a plain agent.
2. **`AdminOnly` does not build its content for a plain agent.** A route page that loads data in its own `OnInitializedAsync` would run before the guard decides, so Tasks 5 to 9 make each admin route component a thin shell, `<AdminOnly><XxxContent /></AdminOnly>`, with the loading inside the content component. The Task 9 host tests pin it with a plain agent that opens each admin URL and makes no admin API call.
3. **A reload keeps a Ready session** (the My settings save reloads it for the new display name). While it is in flight `State` stays Ready and `Agent` stays; `Changed` is raised once when the answer is applied; a concurrent `EnsureLoadedAsync` returns at once and starts no second request. The answer still wins: 403 ends in NoAccess, 401 in SessionExpired, a lower role removes the admin pages. Only a failure that is not a refusal (the API unreachable, a timeout, a cancellation) keeps the old Ready session, because one lost request must not lock out an agent who was working a moment ago. A session that is not Ready starts again from NotLoaded, so the gate shows "Checking" while the Retry button's request is in flight.
4. **The failed-email badge call is admin only.** `NavMenu` asks `FailedEmailCounter.RefreshAsync` in `OnAfterRenderAsync` (never while prerendering), once, and only when `Session.IsAdmin`. A failure shows no badge and no error. The badge is `aria-hidden` and a visually hidden "N waiting" follows it, so the link reads "Failed emails 3 waiting".
5. **`preferences.js` never throws** (storage can be missing, full or blocked). Storage keys are `techstrap.admin.singleKeyShortcuts` (`true`/`false`) and `techstrap.admin.theme` (`auto`/`light`/`dark`). The shortcut switch stays on for anything but the exact stored string `false`. `.mjs` files are not covered by `SourceEncodingTests`, so keep the test file ASCII.
6. **`PreferencesService` loads on the first interactive render** (`MainLayout.OnAfterRenderAsync`, before the key listener starts) and so applies the theme after the first interactive paint: a dark-mode agent can see one light frame. An inline script would avoid it but conflicts with the 07c CSP, so this is recorded in D-041 and left to 07c.
7. **Every component test that renders `MainLayout` needs the module** in bUnit's strict JS mode. `AdminComponentTest` sets it up once; tests that need a stored value or a failure set it up again (`Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(...)`).
8. **Components are public; the view data they take is public.** `StoredPreferences` and `ThemeChoice` are public for that reason (the My settings page, Task 7, binds them).
9. **Test hygiene.** A test that holds a `TaskCompletionSource` gate must time out instead of hanging when the code under test is wrong: `While_a_reload_is_in_flight_another_caller_does_not_start_a_second_request` awaits with `WaitAsync(TimeSpan.FromSeconds(5), Ct)`.

- [ ] **Step 1: Write the failing tests for the session reload and the guard**

1. `tests/TechStrap.Admin.Tests/Auth/AgentSessionTests.cs`: add these tests to the class (the helpers `Session()`, `Agent(...)` and `Refused(...)` already exist; the file already imports `NSubstitute.ExceptionExtensions`):

```csharp
    // The My settings save reloads the session (the display name is part of /api/agents/me). The page must not unmount while that is in flight.
    [Fact]
    public async Task A_reload_keeps_the_session_ready_with_the_current_agent_until_the_answer_arrives()
    {
        var admin = Agent(AgentRoles.Admin);
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(admin));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        var changes = 0;
        session.Changed += () => changes++;
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        var reload = session.ReloadAsync(Ct);

        reload.IsCompleted.ShouldBeFalse();
        session.State.ShouldBe(AgentSessionState.Ready);
        session.Agent.ShouldBe(admin);
        session.IsAdmin.ShouldBeTrue();
        changes.ShouldBe(0);

        gate.SetResult(Result<AgentDto>.Success(admin with { PublicDisplayName = "Samantha" }));
        await reload;

        session.State.ShouldBe(AgentSessionState.Ready);
        session.Agent!.PublicDisplayName.ShouldBe("Samantha");
        changes.ShouldBe(1);
    }

    [Fact]
    public async Task While_a_reload_is_in_flight_another_caller_does_not_start_a_second_request()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Agent()));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        _agents.ClearReceivedCalls();
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        var reload = session.ReloadAsync(Ct);
        await session.EnsureLoadedAsync(Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);
        gate.SetResult(Result<AgentDto>.Success(Agent()));
        await reload;

        await _agents.Received(1).GetMeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_reload_that_finds_the_agent_demoted_removes_the_admin_role_at_once()
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Agent(AgentRoles.Admin)), Result<AgentDto>.Success(Agent(AgentRoles.Agent)));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        session.IsAdmin.ShouldBeTrue();

        await session.ReloadAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        session.IsAdmin.ShouldBeFalse();
    }

    [Theory]
    [InlineData(ApiErrorCodes.AgentInactive, ResultErrorKind.Forbidden, AgentSessionState.NoAccess)]
    [InlineData(ApiErrorCodes.Unauthenticated, ResultErrorKind.Unauthenticated, AgentSessionState.SessionExpired)]
    public async Task A_reload_that_the_api_refuses_wins_over_the_ready_session(string code, ResultErrorKind kind, AgentSessionState expected)
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Agent(AgentRoles.Admin)), Refused(code, kind));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);

        await session.ReloadAsync(Ct);

        session.State.ShouldBe(expected);
        session.Agent.ShouldBeNull();
        session.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public async Task A_reload_that_cannot_reach_the_api_keeps_the_ready_session_and_does_not_lock_the_agent_out()
    {
        var admin = Agent(AgentRoles.Admin);
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(admin), Refused(ApiErrorCodes.ApiUnavailable, ResultErrorKind.Failure));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        var changes = 0;
        session.Changed += () => changes++;

        await session.ReloadAsync(Ct);

        session.State.ShouldBe(AgentSessionState.Ready);
        session.Agent.ShouldBe(admin);
        session.ErrorCode.ShouldBeNull();
        changes.ShouldBe(0);
    }

    [Fact]
    public async Task A_cancelled_reload_leaves_the_ready_session_alone()
    {
        var admin = Agent(AgentRoles.Admin);
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(admin));
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Throws(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => session.ReloadAsync(Ct));

        session.State.ShouldBe(AgentSessionState.Ready);
        session.Agent.ShouldBe(admin);
    }

    [Fact]
    public async Task A_reload_from_a_failed_state_starts_again_from_not_loaded_so_the_gate_shows_checking()
    {
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(Refused(ApiErrorCodes.ApiUnavailable, ResultErrorKind.Failure)), _ => gate.Task);
        var session = Session();
        await session.EnsureLoadedAsync(Ct);
        session.State.ShouldBe(AgentSessionState.Unavailable);

        var reload = session.ReloadAsync(Ct);

        session.State.ShouldBe(AgentSessionState.NotLoaded);
        gate.SetResult(Result<AgentDto>.Success(Agent()));
        await reload;
        session.State.ShouldBe(AgentSessionState.Ready);
    }
```

2. `tests/TechStrap.Admin.Tests/Components/AdminOnlyTests.cs`:

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Pages;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 1: a plain agent never reaches an admin page by URL, and the guard neither denies nor flickers while the session loads or reloads. The role is the API's
/// answer in <see cref="AgentSession"/>; the content is not even built for a non-admin, so a component inside it never starts and never calls the API.
/// </summary>
public sealed class AdminOnlyTests : AdminComponentTest
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private int _contentStarted;

    public AdminOnlyTests() => this.AddAgentShell();

    /// <summary>Stands for the component a page puts inside the guard: it counts how many times it was started and renders a marker.</summary>
    private sealed class Probe : ComponentBase
    {
        [Parameter]
        public Action? Started { get; set; }

        protected override void OnInitialized() => Started?.Invoke();

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"secret\">admin content</p>");
    }

    private IRenderedComponent<AdminOnly> RenderGuard()
    {
        RenderFragment content = builder =>
        {
            builder.OpenComponent<Probe>(0);
            builder.AddAttribute(1, nameof(Probe.Started), (Action)(() => _contentStarted++));
            builder.CloseComponent();
        };
        return Render<AdminOnly>(p => p.Add(g => g.ChildContent, content));
    }

    private AgentSession Session => Services.GetRequiredService<AgentSession>();

    private IAgentsClient Agents => Services.GetRequiredService<IAgentsClient>();

    private static AgentDto Me(string role, string? publicName = null) =>
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Sam", "sam@orbitly.test", role, true, publicName, null);

    [Fact]
    public void Before_the_session_has_loaded_the_guard_shows_checking_and_neither_content_nor_a_refusal()
    {
        var cut = RenderGuard();

        cut.Find("p.ts-gate[role=status]").TextContent.ShouldBe("Checking your access...");
        cut.FindAll("#secret").ShouldBeEmpty();
        cut.FindAll(".ts-no-access").ShouldBeEmpty();
        _contentStarted.ShouldBe(0);
    }

    [Fact]
    public async Task A_plain_agent_gets_the_page_level_refusal_and_the_content_is_never_started()
    {
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderGuard();

        var refusal = cut.Find("section.ts-no-access");
        refusal.QuerySelector("h1")!.TextContent.ShouldBe(NoAccessCopy.PageTitle);
        refusal.QuerySelector("a")!.GetAttribute("href").ShouldBe("/");
        cut.FindAll("#secret").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("admin content");
        _contentStarted.ShouldBe(0);
    }

    [Fact]
    public async Task An_admin_sees_the_content_started_once()
    {
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Me(AgentRoles.Admin)));
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderGuard();

        cut.Find("#secret").TextContent.ShouldBe("admin content");
        cut.FindAll("section.ts-no-access").ShouldBeEmpty();
        _contentStarted.ShouldBe(1);
    }

    [Fact]
    public void The_guard_follows_the_session_when_it_loads_after_the_first_render()
    {
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Me(AgentRoles.Admin)));
        var cut = RenderGuard();
        cut.FindAll("#secret").ShouldBeEmpty();

        cut.InvokeAsync(() => Session.EnsureLoadedAsync(Ct));

        cut.WaitForAssertion(() => cut.Find("#secret").TextContent.ShouldBe("admin content"));
        _contentStarted.ShouldBe(1);
    }

    // The My settings save reloads the session. The guard must stay on the admin content while the answer is on its way, with no "Checking" frame and no restart.
    [Fact]
    public async Task A_session_reload_does_not_flicker_the_guard_or_restart_the_content()
    {
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Me(AgentRoles.Admin)));
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderGuard();
        var frames = new List<string>();
        Session.Changed += () => frames.Add(cut.Markup);
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        var reload = cut.InvokeAsync(() => Session.ReloadAsync(Ct));

        reload.IsCompleted.ShouldBeFalse();
        cut.Find("#secret").TextContent.ShouldBe("admin content");
        cut.FindAll(".ts-gate").ShouldBeEmpty();
        cut.FindAll("section.ts-no-access").ShouldBeEmpty();

        gate.SetResult(Result<AgentDto>.Success(Me(AgentRoles.Admin, "Samantha")));
        await reload;

        cut.Find("#secret").TextContent.ShouldBe("admin content");
        cut.FindAll(".ts-gate").ShouldBeEmpty();
        frames.ShouldAllBe(markup => markup.Contains("admin content") && !markup.Contains("ts-gate") && !markup.Contains("ts-no-access"));
        _contentStarted.ShouldBe(1);
    }

    [Fact]
    public async Task A_reload_that_demotes_the_agent_replaces_the_content_with_the_refusal()
    {
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Me(AgentRoles.Admin)), Result<AgentDto>.Success(Me(AgentRoles.Agent)));
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderGuard();
        cut.Find("#secret").ShouldNotBeNull();

        await cut.InvokeAsync(() => Session.ReloadAsync(Ct));

        cut.FindAll("#secret").ShouldBeEmpty();
        cut.Find("section.ts-no-access h1").TextContent.ShouldBe(NoAccessCopy.PageTitle);
    }

    [Fact]
    public async Task A_reload_that_deactivates_the_agent_removes_the_content_too()
    {
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(Me(AgentRoles.Admin)));
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderGuard();
        Agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Failure(new ResultError(ApiErrorCodes.AgentInactive, "Deactivated.", ResultErrorKind.Forbidden)));

        await cut.InvokeAsync(() => Session.ReloadAsync(Ct));

        // AgentGate shows the no-access screen for this state; the guard only withdraws the content and adds nothing of its own.
        cut.FindAll("#secret").ShouldBeEmpty();
        cut.FindAll(".ts-gate").ShouldBeEmpty();
        cut.FindAll("section.ts-no-access").ShouldBeEmpty();
        Session.State.ShouldBe(AgentSessionState.NoAccess);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter AgentSessionTests
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter AdminOnlyTests
```

Expected: the first run builds and fails four tests: `A_reload_keeps_the_session_ready_with_the_current_agent_until_the_answer_arrives` (State is NotLoaded during the reload), `A_cancelled_reload_leaves_the_ready_session_alone`, `A_reload_that_cannot_reach_the_api_keeps_the_ready_session_and_does_not_lock_the_agent_out`, and `While_a_reload_is_in_flight_another_caller_does_not_start_a_second_request` (timeout after 5 seconds). The second run does not build: `CS0246 ... 'AdminOnly' could not be found`.

- [ ] **Step 3: Keep a Ready session during a reload and add `AdminOnly`**

1. `src/TechStrap.Admin/Auth/AgentSession.cs` (replace the file; the changes are `ReloadAsync`, `Apply`, and the new `keepReadyOnTransientFailure` argument of `LoadAsync`):

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
        while (true)
        {
            if (State is AgentSessionState.Ready or AgentSessionState.NoAccess)
            {
                return;
            }

            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref _loading, source.Task, null) is not { } running)
            {
                await LoadAsync(source, keepReadyOnTransientFailure: false, cancellationToken);
                return;
            }

            // Someone else is already asking; share their answer. If their load ended without one (they were cancelled), ask again ourselves.
            await running;
            if (State != AgentSessionState.NotLoaded)
            {
                return;
            }
        }
    }

    private async Task LoadAsync(TaskCompletionSource source, bool keepReadyOnTransientFailure, CancellationToken cancellationToken)
    {
        try
        {
            Apply(await agents.GetMeAsync(cancellationToken), keepReadyOnTransientFailure);
        }
        finally
        {
            // A failed or cancelled load must not pin the shared task: the next call asks again unless the state is final.
            _loading = null;
            source.SetResult();
        }
    }

    /// <summary>
    /// Asks the API again (the Retry button, or after the agent changed their own profile). A session that is Ready stays Ready, with the current
    /// <see cref="Agent"/>, until the answer arrives: dropping to NotLoaded would make <c>AgentGate</c> and <c>AdminOnly</c> replace the page with "Checking" and lose
    /// what the agent was typing (the My settings save reloads the session). <see cref="Changed"/> is raised once, when the answer has been applied. The answer still
    /// wins: a 403 ends in NoAccess, a 401 in SessionExpired, a lower role removes the admin pages; only a transient failure (the API unreachable, a timeout)
    /// keeps the Ready session, because an agent who was working a moment ago is not locked out by one lost request. A session that is not Ready starts again from
    /// NotLoaded, so the gate shows "Checking" while the Retry button's request is in flight.
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var keepReady = State == AgentSessionState.Ready;
        if (!keepReady)
        {
            State = AgentSessionState.NotLoaded;
        }

        while (true)
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref _loading, source.Task, null) is not { } running)
            {
                await LoadAsync(source, keepReady, cancellationToken);
                return;
            }

            // Another load is in flight; its answer may be older than the change that prompted this reload, so wait for it and then ask again.
            await running;
        }
    }

    private void Apply(Result<AgentDto> result, bool keepReadyOnTransientFailure)
    {
        if (keepReadyOnTransientFailure && result.IsFailure && result.Errors[0].Kind is not (ResultErrorKind.Forbidden or ResultErrorKind.Unauthenticated))
        {
            return;
        }

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

2. `src/TechStrap.Admin/Components/Ui/AdminOnly.razor`:

```razor
@if (Session.State == AgentSessionState.Ready)
{
    if (Session.IsAdmin)
    {
        @ChildContent
    }
    else
    {
        <NoAccessPage ThisPageOnly="true" />
    }
}
else if (Session.State == AgentSessionState.NotLoaded)
{
    <p class="ts-gate" role="status">@GateCopy.Checking</p>
}
```

3. `src/TechStrap.Admin/Components/Ui/AdminOnly.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Auth;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// Shows its content only to an admin (D-041, Review Focus 1). The role is the API's answer in <see cref="AgentSession"/> (D-040), never a copy of the group claim.
/// While the session has not loaded yet it shows "Checking" (a session that reloads keeps Ready, so this is not a flicker); in a failed state (no access, expired, unavailable) it shows nothing, because <c>AgentGate</c> owns those screens; for a Ready agent who is not an admin it shows the
/// page-level no-access page and does not build the content at all, so a component inside it never starts, and never makes an admin call. The API still answers
/// 403 admin-access-required to every admin call.
/// <para>
/// Put the data loading in the component INSIDE the guard. A page component that loads in its own <c>OnInitializedAsync</c> runs before this guard decides, so a
/// route page is a thin shell: <c>&lt;AdminOnly&gt;&lt;ProductsList /&gt;&lt;/AdminOnly&gt;</c>.
/// </para>
/// </summary>
public sealed partial class AdminOnly : ComponentBase, IDisposable
{
    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    protected override void OnInitialized() => Session.Changed += OnSessionChanged;

    private void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

    public void Dispose() => Session.Changed -= OnSessionChanged;
}
```

- [ ] **Step 4: Run the tests**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "AgentSessionTests|AdminOnlyTests|AgentGateTests"
```

Expected: PASS. Mutation check (do not commit it): change `if (!keepReady)` in `ReloadAsync` to always set `State = AgentSessionState.NotLoaded`, rerun the same command, expect failures in `AgentSessionTests` (and a 5-second timeout in the in-flight test), then restore the file.

- [ ] **Step 5: Write the failing tests for the browser preferences**

1. `tests/TechStrap.Admin.Tests/js/preferences.test.mjs`:

```javascript
// Runs with `node --test` (no browser, no jsdom): the storage and the page are passed in as plain objects.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { DEFAULTS, THEMES, applyTheme, normaliseTheme, readPreferences, writePreference } from '../../../src/TechStrap.Admin/wwwroot/js/preferences.js';

const memoryStorage = (initial = {}) => {
    const items = new Map(Object.entries(initial));
    return {
        items,
        getItem: (key) => (items.has(key) ? items.get(key) : null),
        setItem: (key, value) => items.set(key, String(value)),
    };
};
const brokenStorage = () => ({
    getItem: () => { throw new Error('SecurityError: storage is blocked'); },
    setItem: () => { throw new Error('QuotaExceededError'); },
});
const page = () => {
    const attributes = new Map();
    return {
        attributes,
        documentElement: {
            setAttribute: (name, value) => attributes.set(name, value),
            removeAttribute: (name) => attributes.delete(name),
        },
    };
};

describe('readPreferences', () => {
    it('returns the defaults for an empty storage', () => {
        assert.deepEqual(readPreferences(memoryStorage()), { singleKeyShortcuts: true, theme: 'auto' });
        assert.deepEqual(DEFAULTS, { singleKeyShortcuts: true, theme: 'auto' });
    });

    it('returns the defaults when there is no storage at all', () => {
        assert.deepEqual(readPreferences(null), { singleKeyShortcuts: true, theme: 'auto' });
        assert.deepEqual(readPreferences(undefined), { singleKeyShortcuts: true, theme: 'auto' });
    });

    it('never throws when the storage is blocked and falls back to the defaults', () => {
        assert.deepEqual(readPreferences(brokenStorage()), { singleKeyShortcuts: true, theme: 'auto' });
    });

    it('reads stored values', () => {
        const storage = memoryStorage({ 'techstrap.admin.singleKeyShortcuts': 'false', 'techstrap.admin.theme': 'dark' });
        assert.deepEqual(readPreferences(storage), { singleKeyShortcuts: false, theme: 'dark' });
    });

    it('treats an unrecognised stored value as the default', () => {
        const storage = memoryStorage({ 'techstrap.admin.singleKeyShortcuts': 'maybe', 'techstrap.admin.theme': 'sepia' });
        assert.deepEqual(readPreferences(storage), { singleKeyShortcuts: true, theme: 'auto' });
    });

    it('keeps the shortcuts on for anything but the exact string false', () => {
        for (const value of ['true', '', '0', 'FALSE', 'no']) {
            assert.equal(readPreferences(memoryStorage({ 'techstrap.admin.singleKeyShortcuts': value })).singleKeyShortcuts, true, value);
        }
    });
});

describe('writePreference', () => {
    it('stores the shortcut toggle as true or false', () => {
        const storage = memoryStorage();
        assert.equal(writePreference(storage, 'singleKeyShortcuts', false), true);
        assert.equal(storage.items.get('techstrap.admin.singleKeyShortcuts'), 'false');
        assert.equal(writePreference(storage, 'singleKeyShortcuts', true), true);
        assert.equal(storage.items.get('techstrap.admin.singleKeyShortcuts'), 'true');
    });

    it('stores each theme and round-trips through readPreferences', () => {
        for (const theme of THEMES) {
            const storage = memoryStorage();
            assert.equal(writePreference(storage, 'theme', theme), true);
            assert.equal(readPreferences(storage).theme, theme);
        }
    });

    it('refuses an unknown key or value and stores nothing', () => {
        const storage = memoryStorage();
        assert.equal(writePreference(storage, 'colour', 'red'), false);
        assert.equal(writePreference(storage, 'theme', 'sepia'), false);
        assert.equal(writePreference(storage, 'theme', undefined), false);
        assert.equal(writePreference(storage, 'singleKeyShortcuts', 'false'), false);
        assert.equal(writePreference(storage, 'singleKeyShortcuts', 0), false);
        assert.equal(writePreference(storage, '__proto__', 'x'), false);
        assert.equal(writePreference(storage, 'toString', 'x'), false);
        assert.equal(storage.items.size, 0);
    });

    it('returns false instead of throwing when the storage is full, blocked or missing', () => {
        assert.equal(writePreference(brokenStorage(), 'theme', 'dark'), false);
        assert.equal(writePreference(null, 'theme', 'dark'), false);
        assert.equal(writePreference(undefined, 'singleKeyShortcuts', true), false);
    });
});

describe('applyTheme', () => {
    it('sets data-bs-theme for light and dark', () => {
        const doc = page();
        assert.equal(applyTheme(doc, 'dark'), 'dark');
        assert.equal(doc.attributes.get('data-bs-theme'), 'dark');
        assert.equal(applyTheme(doc, 'light'), 'light');
        assert.equal(doc.attributes.get('data-bs-theme'), 'light');
    });

    it('removes the attribute for auto so the system preference decides', () => {
        const doc = page();
        applyTheme(doc, 'dark');
        assert.equal(applyTheme(doc, 'auto'), 'auto');
        assert.equal(doc.attributes.has('data-bs-theme'), false);
    });

    it('treats an unknown theme as auto and never writes it to the page', () => {
        const doc = page();
        applyTheme(doc, 'dark');
        assert.equal(applyTheme(doc, 'sepia'), 'auto');
        assert.equal(doc.attributes.has('data-bs-theme'), false);
        assert.equal(normaliseTheme(null), 'auto');
    });

    it('never throws without a page or with a page that refuses', () => {
        assert.equal(applyTheme(null, 'dark'), 'dark');
        assert.equal(applyTheme({}, 'dark'), 'dark');
        const hostile = { documentElement: { setAttribute: () => { throw new Error('nope'); }, removeAttribute: () => { throw new Error('nope'); } } };
        assert.equal(applyTheme(hostile, 'dark'), 'dark');
        assert.equal(applyTheme(hostile, 'auto'), 'auto');
    });
});
```

2. `scripts/tests/AdminScripts.Tests.ps1`: add a second `It` after the first, inside the same `Describe`:

```powershell
    It 'passes the node:test suite for the browser preferences (storage never throws, theme values)' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the preferences.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/preferences.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }
```

3. `tests/TechStrap.Admin.Tests/ScriptHostTests.cs`: add `[InlineData("/js/preferences.js")]` after the `queue.js` line.

4. `tests/TechStrap.Admin.Tests/Support/AdminComponentTest.cs` (replace the file: it now sets up the preferences module, because `MainLayout` loads it on its first render):

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

        // MainLayout loads the stored preferences on its first render; by default nothing is stored (shortcuts on, theme auto).
        Preferences = JSInterop.SetupModule("./js/preferences.js");
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(SingleKeyShortcuts: true, Theme: "auto"));
        Preferences.Setup<bool>("save", _ => true).SetResult(true);
    }

    protected FakeTimeProvider Time { get; }

    /// <summary>The <c>shortcuts.js</c> module double; use <c>VerifyInvoke("register")</c>.</summary>
    protected BunitJSModuleInterop Shortcuts { get; }

    /// <summary>The <c>dialog.js</c> module double; use <c>VerifyInvoke("open")</c> and read the arguments of the invocation.</summary>
    protected BunitJSModuleInterop Dialogs { get; }

    /// <summary>The <c>preferences.js</c> module double: <c>load</c> answers the defaults and <c>save</c> succeeds; read the arguments from <c>Invocations["save"]</c>.</summary>
    protected BunitJSModuleInterop Preferences { get; }

    protected ShortcutService ShortcutService => Services.GetRequiredService<ShortcutService>();

    protected StatusMessageService StatusMessages => Services.GetRequiredService<StatusMessageService>();

    /// <summary>Simulates the page script reporting a key press (what <c>shortcuts.js</c> sends over the JS bridge).</summary>
    protected Task PressAsync(string key, bool ctrl = false, bool typing = false, bool onBody = true, string? scope = null) =>
        Renderer.Dispatcher.InvokeAsync(() => ShortcutService.OnKeyAsync(new KeyPress(key, ctrl, Meta: false, Alt: false, typing, onBody, scope)));
}
```

5. `tests/TechStrap.Admin.Tests/Components/PreferencesServiceTests.cs`:

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;

namespace TechStrap.Admin.Tests.Components;

public sealed class PreferencesServiceTests : AdminComponentTest
{
    private PreferencesService Service => Services.GetRequiredService<PreferencesService>();

    private static object?[] Args(JSRuntimeInvocation call) => [.. call.Arguments];

    [Fact]
    public async Task Loading_reads_the_stored_values_sets_the_shortcut_switch_and_the_theme_choice()
    {
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(SingleKeyShortcuts: false, Theme: "dark"));
        var service = Service;

        await service.LoadAsync();

        service.IsLoaded.ShouldBeTrue();
        service.Theme.ShouldBe(ThemeChoice.Dark);
        service.SingleKeyShortcuts.ShouldBeFalse();
        ShortcutService.SingleKeyEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Loading_happens_once_however_often_it_is_asked()
    {
        var service = Service;

        await Task.WhenAll(service.LoadAsync(), service.LoadAsync());
        await service.LoadAsync();

        Preferences.VerifyInvoke("load", 1);
    }

    [Theory]
    [InlineData("sepia")]
    [InlineData("")]
    [InlineData(null)]
    public async Task An_unrecognised_stored_theme_is_auto(string? theme)
    {
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(true, theme!));
        var service = Service;

        await service.LoadAsync();

        service.Theme.ShouldBe(ThemeChoice.Auto);
    }

    [Fact]
    public async Task A_script_that_fails_leaves_the_defaults_and_never_throws()
    {
        Preferences.Setup<StoredPreferences>("load", _ => true).SetException(new JSException("storage is blocked"));
        var service = Service;

        await service.LoadAsync();

        service.IsLoaded.ShouldBeTrue();
        service.Theme.ShouldBe(ThemeChoice.Auto);
        service.SingleKeyShortcuts.ShouldBeTrue();
        ShortcutService.SingleKeyEnabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Turning_the_shortcuts_off_changes_the_live_switch_stores_the_choice_and_announces_it()
    {
        var service = Service;
        var changes = 0;
        service.Changed += () => changes++;

        await service.SetSingleKeyShortcutsAsync(false);

        ShortcutService.SingleKeyEnabled.ShouldBeFalse();
        service.SingleKeyShortcuts.ShouldBeFalse();
        Args(Preferences.Invocations["save"].Single()).ShouldBe(["singleKeyShortcuts", false]);
        changes.ShouldBe(1);
    }

    [Theory]
    [InlineData(ThemeChoice.Auto, "auto")]
    [InlineData(ThemeChoice.Light, "light")]
    [InlineData(ThemeChoice.Dark, "dark")]
    public async Task Choosing_a_theme_stores_its_lower_case_name(ThemeChoice choice, string stored)
    {
        var service = Service;

        await service.SetThemeAsync(choice);

        service.Theme.ShouldBe(choice);
        Args(Preferences.Invocations["save"].Single()).ShouldBe(["theme", stored]);
    }

    [Fact]
    public async Task A_save_that_fails_keeps_the_choice_for_this_page_and_never_throws()
    {
        Preferences.Setup<bool>("save", _ => true).SetException(new JSException("QuotaExceededError"));
        var service = Service;

        await service.SetThemeAsync(ThemeChoice.Dark);
        await service.SetSingleKeyShortcutsAsync(false);

        service.Theme.ShouldBe(ThemeChoice.Dark);
        ShortcutService.SingleKeyEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_disconnected_circuit_is_not_an_error()
    {
        Preferences.Setup<bool>("save", _ => true).SetException(new JSDisconnectedException("The circuit is gone."));
        var service = Service;

        await service.SetThemeAsync(ThemeChoice.Light);

        service.Theme.ShouldBe(ThemeChoice.Light);
    }

    [Fact]
    public async Task The_layout_loads_the_preferences_once_before_it_starts_the_key_listener()
    {
        this.AddAgentShell();
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(SingleKeyShortcuts: false, Theme: "light"));

        var cut = Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));
        await cut.InvokeAsync(() => Task.CompletedTask);

        Preferences.VerifyInvoke("load", 1);
        Shortcuts.VerifyInvoke("register", 1);
        ShortcutService.SingleKeyEnabled.ShouldBeFalse();
        Service.Theme.ShouldBe(ThemeChoice.Light);
    }

    [Fact]
    public async Task With_the_shortcuts_turned_off_a_single_key_does_nothing_but_the_layout_still_answers_modified_keys()
    {
        this.AddAgentShell();
        Preferences.Setup<StoredPreferences>("load", _ => true).SetResult(new StoredPreferences(SingleKeyShortcuts: false, Theme: "auto"));
        Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));
        var pressed = new List<ShortcutAction>();
        ShortcutService.Pressed += action =>
        {
            pressed.Add(action);
            return Task.CompletedTask;
        };

        await PressAsync("j");
        await PressAsync("?");

        pressed.ShouldBeEmpty();
    }
}
```

- [ ] **Step 6: Run the tests and confirm they fail**

```bash
node --test tests/TechStrap.Admin.Tests/js/preferences.test.mjs
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "PreferencesServiceTests|ScriptHostTests"
```

Expected: node fails with `ERR_MODULE_NOT_FOUND` (no `preferences.js`); the dotnet project does not build: `CS0246 ... 'StoredPreferences' could not be found` and `'PreferencesService'`.

- [ ] **Step 7: Add the module, the service and the layout wiring**

1. `src/TechStrap.Admin/wwwroot/js/preferences.js`:

```javascript
// The agent's browser preferences (UX-BRIEF-admin, My settings): the single-key keyboard shortcuts and the colour theme. They live in the browser, per
// browser, not in the API. The storage and the page are always handed in, so the functions below are pure and tests/TechStrap.Admin.Tests/js/preferences.test.mjs
// can run them under node:test without a DOM. Nothing here may throw: storage can be missing, full or blocked (private windows, site data off), and a bad
// stored value must fall back to the default instead of breaking the page.
//
// PreferencesService (.NET) imports this module once per circuit and calls load() and save().

export const SINGLE_KEY = 'singleKeyShortcuts';
export const THEME = 'theme';

/** The theme values. 'auto' follows the operating system and is the default. */
export const THEMES = ['auto', 'light', 'dark'];

export const DEFAULTS = Object.freeze({ singleKeyShortcuts: true, theme: 'auto' });

// The storage keys carry the app name because the browser shares one localStorage per origin.
const STORAGE_KEYS = Object.freeze({
    [SINGLE_KEY]: 'techstrap.admin.singleKeyShortcuts',
    [THEME]: 'techstrap.admin.theme',
});

function readItem(storage, name) {
    try {
        return storage ? storage.getItem(STORAGE_KEYS[name]) : null;
    } catch {
        return null;
    }
}

/** A stored or requested theme, or 'auto' for anything that is not one of the three values. */
export function normaliseTheme(value) {
    return THEMES.includes(value) ? value : DEFAULTS.theme;
}

/** Reads both preferences. Never throws; a missing, unreadable or unrecognised value is the default. */
export function readPreferences(storage) {
    const singleKey = readItem(storage, SINGLE_KEY);
    const theme = readItem(storage, THEME);
    return {
        singleKeyShortcuts: singleKey === 'false' ? false : DEFAULTS.singleKeyShortcuts,
        theme: normaliseTheme(theme),
    };
}

/**
 * Stores one preference. key is 'singleKeyShortcuts' (value true or false) or 'theme' (value 'auto', 'light' or 'dark'). Returns true when the value was
 * written, false when the key or value is not recognised or the storage refused it (full, blocked, missing). Never throws.
 */
export function writePreference(storage, key, value) {
    if (!storage || !Object.hasOwn(STORAGE_KEYS, key)) {
        return false;
    }

    let text;
    if (key === SINGLE_KEY) {
        if (value !== true && value !== false) {
            return false;
        }

        text = String(value);
    } else {
        if (!THEMES.includes(value)) {
            return false;
        }

        text = value;
    }

    try {
        storage.setItem(STORAGE_KEYS[key], text);
        return true;
    } catch {
        return false;
    }
}

/**
 * Applies the theme to the page. The Admin styles read the data-bs-theme attribute of the root element: 'light' and 'dark' set it, 'auto' removes it so
 * the system preference decides (_color-mode.scss). Returns the theme applied. Never throws.
 */
export function applyTheme(doc, theme) {
    const applied = normaliseTheme(theme);
    try {
        const root = doc && doc.documentElement;
        if (root) {
            if (applied === 'auto') {
                root.removeAttribute('data-bs-theme');
            } else {
                root.setAttribute('data-bs-theme', applied);
            }
        }
    } catch {
        // A page that cannot be themed still works.
    }

    return applied;
}

function browserStorage() {
    try {
        return typeof window === 'undefined' ? null : window.localStorage ?? null;
    } catch {
        return null;
    }
}

function browserDocument() {
    return typeof document === 'undefined' ? null : document;
}

/** Called once by PreferencesService on the first interactive render: reads the stored values, applies the theme, returns the values. */
export function load() {
    const preferences = readPreferences(browserStorage());
    applyTheme(browserDocument(), preferences.theme);
    return preferences;
}

/** Called when the agent changes a choice. The theme is applied even when it could not be stored, so the choice still holds for this page. */
export function save(key, value) {
    const stored = writePreference(browserStorage(), key, value);
    if (key === THEME) {
        applyTheme(browserDocument(), value);
    }

    return stored;
}
```

2. `src/TechStrap.Admin/Features/Shell/PreferencesService.cs`:

```csharp
using Microsoft.JSInterop;

namespace TechStrap.Admin.Features.Shell;

/// <summary>The colour theme choice. <see cref="Auto"/> follows the operating system and is the default (BRAND.md: Light, Dark, Auto).</summary>
public enum ThemeChoice
{
    Auto,
    Light,
    Dark,
}

/// <summary>What <c>preferences.js</c> <c>load()</c> returns. The theme is the lower-case value the script stores: auto, light or dark.</summary>
public sealed record StoredPreferences(bool SingleKeyShortcuts, string Theme);

/// <summary>
/// The agent's browser preferences (UX-BRIEF-admin, My settings): the single-key keyboard shortcuts and the theme, kept in the browser by <c>wwwroot/js/preferences.js</c>.
/// <see cref="LoadAsync"/> runs once on the first interactive render (<c>MainLayout</c>): it reads the stored values, applies the theme to the page and sets
/// <see cref="ShortcutService.SingleKeyEnabled"/>. The setters change the live value first, then store it, so a blocked or full storage never undoes the choice for this
/// page. No call here throws for a script or storage failure: a page that cannot remember a preference still works. Scoped: one per circuit.
/// </summary>
public sealed class PreferencesService(IJSRuntime js, ShortcutService shortcuts) : IAsyncDisposable
{
    public const string ModulePath = "./js/preferences.js";

    private const string SingleKeyKey = "singleKeyShortcuts";
    private const string ThemeKey = "theme";

    private IJSObjectReference? _module;
    private Task? _loading;
    private bool _disposed;

    /// <summary>True once <see cref="LoadAsync"/> has finished (with the stored values or, when the script was unavailable, the defaults).</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>The current theme choice.</summary>
    public ThemeChoice Theme { get; private set; } = ThemeChoice.Auto;

    /// <summary>The single-key shortcuts switch. It lives on <see cref="ShortcutService.SingleKeyEnabled"/>, which the key listener reads on every key press.</summary>
    public bool SingleKeyShortcuts => shortcuts.SingleKeyEnabled;

    /// <summary>Raised after a value changed or the stored values were loaded, so My settings can show the current choice.</summary>
    public event Action? Changed;

    /// <summary>Loads once; concurrent and repeated callers share the first call. Never throws for a script or storage failure.</summary>
    public Task LoadAsync() => _loading ??= LoadCoreAsync();

    private async Task LoadCoreAsync()
    {
        try
        {
            var module = await ImportAsync();
            var stored = await module.InvokeAsync<StoredPreferences>("load");
            shortcuts.SingleKeyEnabled = stored.SingleKeyShortcuts;
            Theme = ParseTheme(stored.Theme);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // No script, no circuit or a prerender: keep the defaults. The agent loses nothing but a remembered choice.
        }

        IsLoaded = true;
        Changed?.Invoke();
    }

    /// <summary>Turns the single-key shortcuts on or off (WCAG 2.1.4) and remembers the choice.</summary>
    public async Task SetSingleKeyShortcutsAsync(bool enabled)
    {
        shortcuts.SingleKeyEnabled = enabled;
        await SaveAsync(SingleKeyKey, enabled);
        Changed?.Invoke();
    }

    /// <summary>Applies a theme to the page now and remembers the choice.</summary>
    public async Task SetThemeAsync(ThemeChoice theme)
    {
        Theme = theme;
        await SaveAsync(ThemeKey, theme.ToString().ToLowerInvariant());
        Changed?.Invoke();
    }

    private async Task SaveAsync(string key, object value)
    {
        try
        {
            var module = await ImportAsync();
            await module.InvokeAsync<bool>("save", key, value);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            // Not stored: the choice holds for this page and the next visit starts from the defaults.
        }
    }

    private async Task<IJSObjectReference> ImportAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    private static ThemeChoice ParseTheme(string? value) =>
        Enum.TryParse<ThemeChoice>(value, ignoreCase: true, out var theme) && Enum.IsDefined(theme) ? theme : ThemeChoice.Auto;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone.
        }
    }
}
```

3. `src/TechStrap.Admin/Features/Shell/ShellServiceCollectionExtensions.cs`: register the service after `ShortcutService` in `AddShell`:

```csharp
        services.AddScoped<PreferencesService>();
```

   In `ShortcutService.cs` replace the comment of `SingleKeyEnabled` (`/// <summary>The My settings toggle arrives in PHASE-07b; until then the layer is always on.</summary>`) with:

```csharp
    /// <summary>On by default. <see cref="PreferencesService"/> sets it from the stored My settings choice on the first interactive render and when the agent flips the toggle.</summary>
```

4. `src/TechStrap.Admin/Components/Layout/MainLayout.razor.cs`: add the injection after the `Navigation` property:

```csharp
    [Inject]
    private PreferencesService Preferences { get; set; } = default!;
```

   and replace the first-render block of `OnAfterRenderAsync`:

```csharp
        if (firstRender)
        {
            // The stored preferences first: they set the theme and whether single-key shortcuts act. Neither call throws for a script or storage failure.
            await Preferences.LoadAsync();
            await Shortcuts.StartAsync();
        }
```

- [ ] **Step 8: Run the tests**

```bash
node --test tests/TechStrap.Admin.Tests/js/preferences.test.mjs
pwsh -NoProfile -Command "Import-Module Pester -RequiredVersion 6.2.0; Invoke-Pester -Path scripts/tests/AdminScripts.Tests.ps1 -Output Minimal"
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "PreferencesServiceTests|ScriptHostTests|MainLayoutTests|ShellComponentTests"
```

Expected: node 14 tests pass; Pester 2 tests pass; the dotnet run passes.

- [ ] **Step 9: Write the failing tests for the rail**

1. `tests/TechStrap.Admin.Tests/Components/ShellTestServices.cs` (replace the file: `AddAgentShell` also registers a substitute dead letters client, because an admin rail now asks it for the badge):

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

        // The rail asks this once for an admin session (the badge). A plain agent must never reach it; tests read it back with GetRequiredService.
        var deadLetters = Substitute.For<IDeadLettersClient>();
        deadLetters.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(0));
        context.Services.AddSingleton(deadLetters);
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

2. `tests/TechStrap.Admin.Tests/Components/ShellComponentTests.cs`: replace the test `The_rail_marks_an_admin_and_has_no_settings_links_yet` (its last two assertions are now false) with:

```csharp
    [Fact]
    public async Task The_rail_marks_an_admin()
    {
        this.AddAgentShell(AgentRoles.Admin);
        await Services.GetRequiredService<AgentSession>().EnsureLoadedAsync(Xunit.TestContext.Current.CancellationToken);

        var cut = Render<NavMenu>(p => p.SignedIn());

        cut.Find(".ts-rail-role").TextContent.ShouldBe("Admin");
    }
```

3. `tests/TechStrap.Admin.Tests/Components/NavMenuTests.cs`:

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Layout;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>Review Focus 1, by nav: a plain agent never sees an admin link and never makes the admin-only badge call; an admin sees the five admin links and the failed-email count.</summary>
public sealed class NavMenuTests : AdminComponentTest
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private IAgentsClient Setup(string role)
    {
        var agents = this.AddAgentShell(role);
        return agents;
    }

    private IDeadLettersClient DeadLetters => Services.GetRequiredService<IDeadLettersClient>();

    private AgentSession Session => Services.GetRequiredService<AgentSession>();

    private IRenderedComponent<NavMenu> RenderRail() => Render<NavMenu>(p => p.SignedIn());

    [Fact]
    public async Task A_plain_agent_sees_the_queue_and_my_settings_and_no_admin_link()
    {
        Setup(AgentRoles.Agent);
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderRail();

        cut.FindAll("a.ts-rail-link").Select(a => (a.GetAttribute("href"), a.TextContent.Trim())).ShouldBe(
            [("/queue", "Queue"), ("/account/notifications", "My settings")]);
        cut.FindAll("a[href^='/settings']").ShouldBeEmpty();
        cut.FindAll("a[href^='/ops']").ShouldBeEmpty();
        cut.FindAll(".ts-rail-group").ShouldBeEmpty();
        cut.FindAll(".ts-rail-role").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_plain_agent_never_makes_the_admin_only_badge_call()
    {
        Setup(AgentRoles.Agent);
        await Session.EnsureLoadedAsync(Ct);

        RenderRail();
        await Task.Yield();

        await DeadLetters.DidNotReceive().CountAsync(Arg.Any<CancellationToken>());
        Services.GetRequiredService<FailedEmailCounter>().Count.ShouldBeNull();
    }

    [Fact]
    public async Task An_admin_sees_the_five_admin_links_in_order_and_my_settings()
    {
        Setup(AgentRoles.Admin);
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderRail();

        cut.FindAll("a.ts-rail-link").Select(a => (a.GetAttribute("href"), a.TextContent.Trim())).ShouldBe(
        [
            ("/queue", "Queue"),
            ("/settings/products", "Products"),
            ("/settings/agents", "Agents"),
            ("/settings/tags", "Tags"),
            ("/settings/audit", "Audit"),
            ("/ops/dead-letters", "Failed emails"),
            ("/account/notifications", "My settings"),
        ]);
        cut.Find(".ts-rail-group").GetAttribute("aria-label").ShouldBe("Admin");
        cut.Find(".ts-rail-role").TextContent.ShouldBe("Admin");
    }

    [Fact]
    public async Task An_admin_session_asks_for_the_failed_email_count_once_and_shows_it_as_a_badge()
    {
        Setup(AgentRoles.Admin);
        DeadLetters.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(3));
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderRail();

        cut.WaitForAssertion(() => cut.Find("a[href='/ops/dead-letters'] .ts-rail-badge").TextContent.ShouldBe("3"));
        cut.Find("a[href='/ops/dead-letters'] .ts-rail-badge").GetAttribute("aria-hidden").ShouldBe("true");
        cut.Find("a[href='/ops/dead-letters'] .visually-hidden").TextContent.ShouldBe("3 waiting");
        cut.Render();
        await DeadLetters.Received(1).CountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_failed_emails_means_no_badge()
    {
        Setup(AgentRoles.Admin);
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderRail();

        cut.WaitForAssertion(() => DeadLetters.Received(1).CountAsync(Arg.Any<CancellationToken>()));
        cut.FindAll(".ts-rail-badge").ShouldBeEmpty();
        cut.Find("a[href='/ops/dead-letters']").TextContent.Trim().ShouldBe("Failed emails");
    }

    [Fact]
    public async Task A_failed_count_call_shows_no_badge_and_no_error()
    {
        Setup(AgentRoles.Admin);
        DeadLetters.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Failure(new ResultError(ApiErrorCodes.ApiUnavailable, "TechStrap could not reach the API.", ResultErrorKind.Failure)));
        await Session.EnsureLoadedAsync(Ct);

        var cut = RenderRail();

        cut.WaitForAssertion(() => DeadLetters.Received(1).CountAsync(Arg.Any<CancellationToken>()));
        cut.FindAll(".ts-rail-badge").ShouldBeEmpty();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("could not reach");
    }

    [Fact]
    public async Task The_badge_follows_the_counter_when_a_page_retries_or_discards_a_letter()
    {
        Setup(AgentRoles.Admin);
        DeadLetters.CountAsync(Arg.Any<CancellationToken>()).Returns(Result<int>.Success(2));
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderRail();
        cut.WaitForAssertion(() => cut.Find(".ts-rail-badge").TextContent.ShouldBe("2"));

        await cut.InvokeAsync(() => Services.GetRequiredService<FailedEmailCounter>().Set(1));
        cut.Find(".ts-rail-badge").TextContent.ShouldBe("1");

        await cut.InvokeAsync(() => Services.GetRequiredService<FailedEmailCounter>().Set(0));
        cut.FindAll(".ts-rail-badge").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_reload_that_demotes_the_admin_removes_the_admin_links_from_the_rail()
    {
        var agents = Setup(AgentRoles.Admin);
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderRail();
        cut.FindAll("a[href^='/settings']").Count.ShouldBe(4);
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Agent, true, null, null)));

        await cut.InvokeAsync(() => Session.ReloadAsync(Ct));

        cut.FindAll("a[href^='/settings']").ShouldBeEmpty();
        cut.FindAll("a[href^='/ops']").ShouldBeEmpty();
        cut.Find("a[href='/account/notifications']").ShouldNotBeNull();
    }

    [Fact]
    public async Task The_rail_stays_complete_while_the_session_reloads()
    {
        var agents = Setup(AgentRoles.Admin);
        await Session.EnsureLoadedAsync(Ct);
        var cut = RenderRail();
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        var reload = cut.InvokeAsync(() => Session.ReloadAsync(Ct));

        cut.FindAll("a.ts-rail-link").Count.ShouldBe(7);
        gate.SetResult(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Admin, true, "Samantha", null)));
        await reload;
        cut.FindAll("a.ts-rail-link").Count.ShouldBe(7);
    }

    [Fact]
    public void Until_the_session_is_ready_the_rail_has_the_brand_alone_and_makes_no_call()
    {
        Setup(AgentRoles.Admin);

        var cut = RenderRail();

        cut.FindAll("a.ts-rail-link").ShouldBeEmpty();
        DeadLetters.DidNotReceive().CountAsync(Arg.Any<CancellationToken>());
    }

    // Review Focus 1, by keyboard: no key of the keyboard layer opens an admin page, for a plain agent or anyone else.
    [Theory]
    [InlineData("j")]
    [InlineData("k")]
    [InlineData("Enter")]
    [InlineData("/")]
    [InlineData("r")]
    [InlineData("n")]
    [InlineData("e")]
    [InlineData("u")]
    [InlineData("?")]
    [InlineData("Escape")]
    public async Task No_keyboard_shortcut_navigates_to_an_admin_page(string key)
    {
        this.AddAgentShell(AgentRoles.Agent);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/tickets/ACME-142");
        Render<MainLayout>(p => p.SignedIn().Add(l => l.Body, (RenderFragment)(b => { })));

        await PressAsync(key);

        navigation.Uri.ShouldNotContain("/settings");
        navigation.Uri.ShouldNotContain("/ops");
    }
}
```

4. `tests/TechStrap.Admin.Tests/Components/FailedEmailCounterTests.cs`:

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Tests.Components;

public sealed class FailedEmailCounterTests
{
    private static readonly CancellationToken Ct = Xunit.TestContext.Current.CancellationToken;

    private readonly IDeadLettersClient _deadLetters = Substitute.For<IDeadLettersClient>();

    [Fact]
    public void The_count_is_unknown_until_it_is_read()
    {
        new FailedEmailCounter(_deadLetters).Count.ShouldBeNull();
    }

    [Fact]
    public async Task A_refresh_reads_the_total_and_announces_it()
    {
        _deadLetters.CountAsync(Ct).Returns(Result<int>.Success(4));
        var counter = new FailedEmailCounter(_deadLetters);
        var changes = 0;
        counter.Changed += () => changes++;

        await counter.RefreshAsync(Ct);

        counter.Count.ShouldBe(4);
        changes.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_refresh_keeps_the_last_known_number()
    {
        _deadLetters.CountAsync(Ct).Returns(Result<int>.Success(4), Result<int>.Failure(new ResultError(ApiErrorCodes.ApiUnavailable, "Down.", ResultErrorKind.Failure)));
        var counter = new FailedEmailCounter(_deadLetters);

        await counter.RefreshAsync(Ct);
        await counter.RefreshAsync(Ct);

        counter.Count.ShouldBe(4);
    }

    [Fact]
    public async Task A_failed_first_refresh_leaves_the_count_unknown()
    {
        _deadLetters.CountAsync(Ct).Returns(Result<int>.Failure(new ResultError(ApiErrorCodes.AdminAccessRequired, "No.", ResultErrorKind.Forbidden)));
        var counter = new FailedEmailCounter(_deadLetters);

        await counter.RefreshAsync(Ct);

        counter.Count.ShouldBeNull();
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(0, 0)]
    [InlineData(-3, 0)]
    public void A_page_that_knows_the_total_sets_it_without_a_call(int total, int expected)
    {
        var counter = new FailedEmailCounter(_deadLetters);

        counter.Set(total);

        counter.Count.ShouldBe(expected);
        _deadLetters.ReceivedCalls().ShouldBeEmpty();
    }
}
```

5. `tests/TechStrap.Admin.Tests/RailStyleTests.cs`:

```csharp
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The rail's admin group and the failed-email badge use brand tokens only, so both themes get a legible badge without a new colour.</summary>
public sealed class RailStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void The_failed_email_badge_uses_the_selection_and_ink_tokens()
    {
        var badge = Css.Declarations(".ts-rail-badge");

        badge["color"].ShouldBe("var(--ink)");
        badge["background"].ShouldBe("var(--sel)");
        badge["border"].ShouldBe("1px solid var(--rule-strong)");
    }

    [Fact]
    public void The_admin_group_is_set_apart_by_a_dashed_rule()
    {
        Css.Declarations(".ts-rail-group")["border-top"].ShouldBe("1px dashed var(--rule-strong)");
    }
}
```

- [ ] **Step 10: Run the tests and confirm they fail**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "NavMenuTests|FailedEmailCounterTests|RailStyleTests|ShellComponentTests"
```

Expected: the project does not build: `CS0246 ... 'FailedEmailCounter' could not be found`.

- [ ] **Step 11: Add the rail**

1. `src/TechStrap.Admin/Components/Ui/ShellCopy.cs`: add after `QueueLink`:

```csharp
    public const string MySettingsLink = "My settings";
    public const string AdminLinksLabel = "Admin";
    public const string ProductsLink = "Products";
    public const string AgentsLink = "Agents";
    public const string TagsLink = "Tags";
    public const string AuditLink = "Audit";
    public const string FailedEmailsLink = "Failed emails";
```

   and after `StatusBarLabel`:

```csharp
    /// <summary>Read out after the "Failed emails" link when the badge shows a number.</summary>
    public static string FailedEmailsCountLabel(int count) => $"{count} waiting";
```

2. `src/TechStrap.Admin/Features/Shell/FailedEmailCounter.cs`:

```csharp
using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Features.Shell;

/// <summary>
/// The number of dead-lettered emails, for the badge on the "Failed emails" link. Only an admin session may call <see cref="RefreshAsync"/>: the call is a 403 for a
/// plain agent. <c>NavMenu</c> refreshes it once when an admin session is ready; the dead letters page calls <see cref="Set"/> or <see cref="RefreshAsync"/> after it
/// retries or discards a row, so the badge follows. <see cref="Count"/> is null until a refresh has succeeded; a failed refresh keeps the last known number (a badge is not
/// worth an error message). Scoped: one per circuit.
/// </summary>
public sealed class FailedEmailCounter(IDeadLettersClient deadLetters)
{
    /// <summary>The last known number of dead-lettered emails, or null when it has not been read.</summary>
    public int? Count { get; private set; }

    /// <summary>Raised after <see cref="Count"/> changed or a refresh finished.</summary>
    public event Action? Changed;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var result = await deadLetters.CountAsync(cancellationToken);
        if (result.IsSuccess)
        {
            Count = result.Value;
        }

        Changed?.Invoke();
    }

    /// <summary>For a page that already holds the total (the dead letters list does), so the badge needs no second call.</summary>
    public void Set(int count)
    {
        Count = Math.Max(0, count);
        Changed?.Invoke();
    }
}
```

   Register it in `ShellServiceCollectionExtensions.AddShell`, after `PreferencesService`, and widen the summary of `AddShell` to `Registers the per-circuit shell services. Scoped, so each circuit has its own message slot, key listener, browser preferences and failed-email badge.`:

```csharp
        services.AddScoped<FailedEmailCounter>();
```

3. `src/TechStrap.Admin/Components/Layout/NavMenu.razor`:

```razor
<nav class="ts-rail" aria-label="@ShellCopy.NavigationLabel">
    <a class="ts-brand" href="/" aria-label="TechStrap Admin home">
        <img src="brand/mark.svg" alt="" width="36" height="36" />
        <span>TechStrap<small>Call log / admin</small></span>
    </a>
    @if (Session.State == AgentSessionState.Ready)
    {
        <NavLink class="ts-rail-link" href="/queue" Match="NavLinkMatch.Prefix">@ShellCopy.QueueLink</NavLink>
        @if (Session.IsAdmin)
        {
            <div class="ts-rail-group" role="group" aria-label="@ShellCopy.AdminLinksLabel">
                <NavLink class="ts-rail-link" href="/settings/products" Match="NavLinkMatch.Prefix">@ShellCopy.ProductsLink</NavLink>
                <NavLink class="ts-rail-link" href="/settings/agents" Match="NavLinkMatch.Prefix">@ShellCopy.AgentsLink</NavLink>
                <NavLink class="ts-rail-link" href="/settings/tags" Match="NavLinkMatch.Prefix">@ShellCopy.TagsLink</NavLink>
                <NavLink class="ts-rail-link" href="/settings/audit" Match="NavLinkMatch.Prefix">@ShellCopy.AuditLink</NavLink>
                <NavLink class="ts-rail-link" href="/ops/dead-letters" Match="NavLinkMatch.Prefix">
                    @ShellCopy.FailedEmailsLink
                    @if (Failed.Count is > 0 and var failed)
                    {
                        <span class="ts-rail-badge" aria-hidden="true">@failed</span>
                        <span class="visually-hidden">@ShellCopy.FailedEmailsCountLabel(failed)</span>
                    }
                </NavLink>
            </div>
        }
        <div class="ts-rail-user">
            <span class="ts-rail-name">@DisplayName</span>
            @if (Session.IsAdmin)
            {
                <span class="ts-rail-role">Admin</span>
            }
            <NavLink class="ts-rail-link" href="/account/notifications" Match="NavLinkMatch.Prefix">@ShellCopy.MySettingsLink</NavLink>
            <SignOutForm />
        </div>
    }
</nav>
```

4. `src/TechStrap.Admin/Components/Layout/NavMenu.razor.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Components.Layout;

/// <summary>
/// The left rail. It shows the agent's links only once <see cref="AgentSession"/> is Ready, so the anonymous pages that share <c>MainLayout</c> (not found, error),
/// and a user the API refused, see the brand alone. It re-renders when the session changes (the gate loads it after the layout first renders). Every agent sees
/// Queue and My settings; the admin links (Products, Agents, Tags, Audit, Failed emails with a count badge) show only for <see cref="AgentSession.IsAdmin"/>, the API's
/// answer (D-040, D-041). Hiding a link is not access control: each admin page is wrapped in <c>AdminOnly</c> and the API answers 403 to every admin call. The badge call
/// is made only for an admin, after the first render (never while prerendering), and only once. Sign out is the shared <c>SignOutForm</c>.
/// </summary>
public sealed partial class NavMenu : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _badgeRequested;
    private bool _disposed;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private FailedEmailCounter Failed { get; set; } = default!;

    private string DisplayName => Session.Agent is { } agent ? (string.IsNullOrWhiteSpace(agent.Name) ? agent.Email : agent.Name) : string.Empty;

    protected override void OnInitialized()
    {
        Session.Changed += OnChanged;
        Failed.Changed += OnChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // A plain agent never asks: the dead letters call is a 403 for them. Ready is only true after the API answered, so there is no admin guess here.
        if (_badgeRequested || !Session.IsAdmin)
        {
            return;
        }

        _badgeRequested = true;
        try
        {
            await Failed.RefreshAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The circuit or the layout went away while the read was in flight.
        }
    }

    private void OnChanged()
    {
        if (!_disposed)
        {
            _ = InvokeAsync(StateHasChanged);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Session.Changed -= OnChanged;
        Failed.Changed -= OnChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

5. `src/TechStrap.Admin/Styles/_shell.scss`: add before `.ts-main`:

```scss
.ts-rail-group {
  display: flex;
  flex-direction: column;
  gap: 2px;
  margin-top: 8px;
  padding-top: 8px;
  border-top: 1px dashed var(--rule-strong);
}

// The dead-letter count on the "Failed emails" link; the number is aria-hidden and a visually-hidden "N waiting" follows it.
.ts-rail-badge {
  min-width: 1.25rem;
  padding: 0 6px;
  font: 600 .6875rem/1.25rem var(--ts-font-mono);
  text-align: center;
  color: var(--ink);
  background: var(--sel);
  border: 1px solid var(--rule-strong);
}
```

- [ ] **Step 12: Run everything**

```bash
dotnet build TechStrap.slnx -c Release
dotnet test --project tests/TechStrap.Admin.Tests -c Release
pwsh -File scripts/Invoke-ScriptTests.ps1
```

Expected: 0 warnings; all Admin tests pass (the `SourceEncodingTests` and `SourceEscapeTests` guards included, and `AdminHostSmokeTests` still pass because the rail only calls the API after the first interactive render); the script tests pass.

- [ ] **Step 13: Commit**

```bash
git add src tests scripts
git diff --cached --stat
git commit -m "feat(admin): AdminOnly guard, a session reload that keeps Ready, admin rail links and browser preferences" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 5: Products list and editor, with the branding preview (P07-T14)

**Review Focus pins (3 and 4):** an edit never silently loses data (an omitted `IsActive` deactivates the product, a stale `Version` overwrites, a failed save drops the form), and an unsafe logo address (`javascript:`, `data:`, relative, plain `http`) never reaches the API or an image source. Pinned by `ProductEditorTests` (the whole class), with the shared rule itself pinned in Task 2.

**Files:**
- Create: `src/TechStrap.Admin/Features/Settings/Products/ProductFields.cs`, `ProductsCopy.cs`, `ProductEditorViewModel.cs`, `ProductRowViewModel.cs`
- Create: `src/TechStrap.Admin/Features/Settings/Products/ProductsContent.razor` and `.razor.cs`, `ProductsPage.razor`
- Create: `src/TechStrap.Admin/Features/Settings/Products/ProductEditorContent.razor` and `.razor.cs`, `ProductEditorPage.razor`
- Create: `src/TechStrap.Admin/Components/Ui/AccentPreview.razor` and `.razor.cs`
- Create: `src/TechStrap.Admin/Styles/_settings.scss`; Modify: `src/TechStrap.Admin/Styles/app.scss`
- Create: `tests/TechStrap.Admin.Tests/Support/AdminPageTest.cs`, `Support/TestData.Products.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/AccentPreviewTests.cs`, `ProductsPageTests.cs`, `ProductEditorTests.cs`, `ProductEditorRealApiTests.cs`
- Create: `tests/TechStrap.Admin.Tests/SettingsStyleTests.cs`

**Interfaces:**
- Consumes (Tasks 1 to 4, names as fixed by the controller):
  - `IProductsClient.GetAsync(Guid, ct)`, `CreateAsync(CreateProductRequest, ct)`, `UpdateAsync(Guid, UpdateProductRequest, ct)`, all `Result<ProductDto>`; `ApiErrorCodes.ConcurrencyConflict`, `ProductNotFound`, `ProductKeyTaken`, `LogoPathInvalid`, `IsUncertainWrite`; `ApiFields` (the kebab-case 400 targets: `Key`, `Name`, `NumberPrefix`, `DisplayName`, `LogoPath`, `AccentColour`, `FromAddress`, `ReplyTo`).
  - `BrandingRules.ColourPattern`, `BrandingRules.IsAcceptableLogoUrl(string?)` (Contracts, Task 2) and `ProductAccent.TryDerive`, `ContrastRatio`, `MinimumTextContrast` (existing Contracts).
  - `AdminOnly` (`TechStrap.Admin.Components.Ui`, `RenderFragment? ChildContent`; Task 4): a Ready admin gets the content, a Ready plain agent gets `NoAccessPage ThisPageOnly` and the content is never built.
  - `StatusMessageService`, `LoadingState`, `ErrorState`, `EmptyState`; 07a test support `AdminComponentTest`, `AgentSessions.SignedIn(bool)`, `TestData` (partial), `ApiHarness`, and from `tests/Shared/AdminHost` `StubApiHandler.ValidationProblem(target, code, message)` (static, returns the 400 `HttpResponseMessage`; Task 3).
- Produces:

```csharp
// TechStrap.Admin.Features.Settings.Products
public sealed partial class ProductsPage           // routable shell: /settings/products          = <AdminOnly><ProductsContent/></AdminOnly>
public sealed partial class ProductEditorPage      // routable shell: /settings/products/new and /settings/products/{Id}   (string? Id)
public sealed partial class ProductsContent : IDisposable        // loads and renders the list; only ever built for an admin
public sealed partial class ProductEditorContent : IDisposable   // the form, the save, the conflict and the preview
public static class ProductFields                  // limits, and the field order of the form (the names of a 400 are ApiFields)
public static class ProductsCopy
// TechStrap.Admin.Components.Ui
public partial class AccentPreview                 // Accent, DisplayName, LogoUrl; sets --ts-accent, --ts-on-accent, --ts-accent-ink
public static class AccentPreviewCopy
// tests: public abstract class AdminPageTest : AdminComponentTest   // AsAgent(), Session, virtual CreateSession(bool admin)
```

**Rules:**
1. **Pages are thin shells.** `ProductsPage` and `ProductEditorPage` hold only the route, `[Authorize]` and `<AdminOnly>`. Every data load is in the `*Content` component inside the guard, so a plain agent's page makes no call and the content is never built. Each page has a test (`A_plain_agent_gets_the_no_access_page_and_the_api_is_not_asked`) that renders it for a plain agent and asserts the substitute client received no call.
2. **A product edit never loses data.** The update is built from the form: `IsActive` is the form's current value (the flag is a non-nullable bool, so an omitted one would deactivate the product) and `Version` is the one the product was loaded with. The model is replaced only by a successful save (with the answer, which carries the new `Version`) or by the Reload button; a validation error, a conflict, an uncertain answer or any other failure leaves every typed value where it is.
3. **Conflict.** `concurrency-conflict` raises a banner ("This product changed since you opened it." + "Your edits are still on screen. Reload shows the saved version and drops them.") with a Reload button. Reload re-reads the product and replaces the form; the next save carries the reloaded `Version`.
4. **Unknown outcome.** A timeout, an unreachable or unreadable answer (`ApiErrorCodes.IsUncertainWrite`) says "The save may have gone through. Reload to see the saved version before you save again." with Reload. It never says nothing was changed and never offers a bare retry. Writes use `CancellationToken.None` and are never retried; a finish after the page is gone changes nothing (`_disposed`).
5. **Field errors.** A 400 names its field in kebab-case (`ResultError.Target`, the `ApiFields` constants); the message appears under that input with `role="alert"` and `aria-invalid`. A target the form has no input for, and any untargeted error, is shown once above the form in the API's words. `product-key-taken` (a key or a ticket number prefix) is one sentence above the form. Client checks use the server's own rules and run on blur and on submit; the colour with `BrandingRules.ColourPattern`.
6. **The logo is a URL (D-041), checked with `BrandingRules.IsAcceptableLogoUrl`.** An address that fails is refused at the field before anything is sent, and the preview never gets an `img` for it; only an address that passes is passed on to `AccentPreview`. Plain `http` is accepted for `localhost` and `127.0.0.1` only (the shared rule has no environment switch), and an address with user info or a space is refused.
7. **Branding preview.** `AccentPreview` derives the three accent properties with `ProductAccent.TryDerive` (the portal's `AccentScope` rule, which Admin cannot reference); a blank or malformed colour sets nothing. A low-contrast colour (below `MinimumTextContrast` on white) only shows an information note whose ratio is rounded down; it never blocks a save.
8. **Create.** The key and the ticket number prefix are asked for only when creating; afterwards they are read-only text (they are permanent). Success shows "Created {name}" in the status bar and goes to `/settings/products/{id}/keys` (Task 6). There is no `IsActive` switch on create.
9. **Copy** lives in `ProductsCopy` and `AccentPreviewCopy`. Non-ASCII text is written as `\u` escapes in C# (`SourceEscapeTests` and `SourceEncodingTests` stay green); write these files with Python or an editor, never GNU sed, and grep afterwards.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/Support/AdminPageTest.cs`

```csharp
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Options;

namespace TechStrap.Admin.Tests.Support;

/// <summary>
/// The setup the settings pages need on top of <see cref="AdminComponentTest"/>: a signed-in session (an Admin unless the test calls <see cref="AsAgent"/> before it renders),
/// the services <c>NoAccessPage</c> needs when <c>AdminOnly</c> refuses a plain agent.
/// The session is built on first use, so a test says what it needs before the first render (services cannot be added once something has been resolved).
/// </summary>
public abstract class AdminPageTest : AdminComponentTest
{
    private bool _admin = true;

    protected AdminPageTest()
    {
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new AgentGroupOptions()));
        Services.AddSingleton<AntiforgeryStateProvider, NoTokenAntiforgeryStateProvider>();
        Services.AddSingleton(_ => CreateSession(_admin));
    }

    protected AgentSession Session => Services.GetRequiredService<AgentSession>();

    /// <summary>Builds the session on first use. A test that needs to see the calls the session makes (a reload) overrides this to build it over its own client.</summary>
    protected virtual AgentSession CreateSession(bool admin) => AgentSessions.SignedIn(admin);

    /// <summary>The page is opened by a plain Agent: <c>AdminOnly</c> refuses it and nothing may call the API.</summary>
    protected void AsAgent() => _admin = false;

    private sealed class NoTokenAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() => null;
    }
}
```

`tests/TechStrap.Admin.Tests/Support/TestData.Products.cs`

```csharp
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    /// <summary>A product with every branding field settable, for the editor tests (<see cref="Product"/> has fixed branding and version 1).</summary>
    public static ProductDto ProductDetail(
        string name = "Orbitly",
        Guid? id = null,
        bool active = true,
        uint version = 7,
        string? logo = null,
        string accent = "#1D4ED8",
        string? from = null,
        string? replyTo = null,
        string key = "orbitly",
        string prefix = "ORB") => new(
            id ?? OrbitlyId, key, name, prefix, active,
            new ProductBrandingDto(name, logo, accent, "#FFFFFF", accent, from, replyTo), version);
}
```

`tests/TechStrap.Admin.Tests/Components/AccentPreviewTests.cs`

```csharp
using Bunit;
using TechStrap.Admin.Components.Ui;
using TechStrap.Contracts.Branding;

namespace TechStrap.Admin.Tests.Components;

public sealed class AccentPreviewTests : BunitContext
{
    [Fact]
    public void It_sets_the_three_accent_properties_the_portal_sets_from_the_one_shared_rule()
    {
        ProductAccent.TryDerive("#1D4ED8", out var expected).ShouldBeTrue();

        var cut = Render<AccentPreview>(p => p.Add(c => c.Accent, "#1D4ED8").Add(c => c.DisplayName, "Orbitly"));

        cut.Find(".ts-accent-preview").GetAttribute("style")
            .ShouldBe($"--ts-accent:{expected.Accent};--ts-on-accent:{expected.OnAccent};--ts-accent-ink:{expected.AccentInk}");
        cut.Find(".ts-accent-preview-name").TextContent.ShouldBe("Orbitly");
        cut.FindAll("[role=note]").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1D4ED8FF")]
    public void A_blank_or_malformed_colour_sets_nothing_so_the_stylesheet_fallbacks_apply_and_no_note_is_shown(string? accent)
    {
        var cut = Render<AccentPreview>(p => p.Add(c => c.Accent, accent));

        cut.Find(".ts-accent-preview").HasAttribute("style").ShouldBeFalse();
        cut.FindAll("[role=note]").ShouldBeEmpty();
    }

    [Fact]
    public void A_low_contrast_colour_gets_an_information_note_and_is_never_refused()
    {
        var cut = Render<AccentPreview>(p => p.Add(c => c.Accent, "#FFEB3B"));

        var note = cut.Find("p[role=note]");
        note.TextContent.ShouldContain("This colour has low contrast on white");
        note.TextContent.ShouldContain("TechStrap darkens it wherever it is used for text");
        cut.Find(".ts-accent-preview").HasAttribute("style").ShouldBeTrue();
    }

    [Fact]
    public void The_ratio_in_the_note_is_rounded_down_so_it_never_claims_the_target_was_met()
    {
        ProductAccent.ContrastRatio("#777777", "#FFFFFF").ShouldBeLessThan(ProductAccent.MinimumTextContrast);

        var cut = Render<AccentPreview>(p => p.Add(c => c.Accent, "#777777"));

        cut.Find("p[role=note]").TextContent.ShouldContain("(4.4:1;");
    }

    [Fact]
    public void The_logo_is_drawn_only_when_an_address_is_given_with_no_alt_text_and_no_referrer()
    {
        var without = Render<AccentPreview>(p => p.Add(c => c.Accent, "#1D4ED8"));
        without.FindAll("img").ShouldBeEmpty();

        var with = Render<AccentPreview>(p => p.Add(c => c.LogoUrl, "https://cdn.example.com/logo.png"));
        var logo = with.Find("img.ts-accent-preview-logo");
        logo.GetAttribute("src").ShouldBe("https://cdn.example.com/logo.png");
        logo.GetAttribute("alt").ShouldBe(string.Empty);
        logo.GetAttribute("referrerpolicy").ShouldBe("no-referrer");
    }

    [Fact]
    public void The_display_name_is_encoded_never_rendered_as_markup()
    {
        var cut = Render<AccentPreview>(p => p.Add(c => c.DisplayName, "<b onclick=\"x()\">Orbitly</b>"));

        cut.Markup.ShouldContain("&lt;b onclick=");
        cut.FindAll(".ts-accent-preview-name b").ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/ProductsPageTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

public sealed class ProductsPageTests : AdminPageTest
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();

    public ProductsPageTests()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<ProductDto>>(
        [
            TestData.ProductDetail("Orbitly", active: true, key: "orbitly", prefix: "ORB"),
            TestData.ProductDetail("Acme", id: Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a2"), active: false, key: "acme", prefix: "ACM"),
        ]));
        Services.AddSingleton(_products);
    }

    [Fact]
    public void An_admin_sees_every_product_by_name_with_its_key_prefix_and_status_in_words()
    {
        var cut = Render<ProductsPage>();

        var rows = cut.FindAll("tbody tr");
        rows.Select(r => r.QuerySelector("td a")!.TextContent).ShouldBe(["Acme", "Orbitly"]);
        rows[0].QuerySelector(".ts-pill")!.TextContent.ShouldBe("Inactive");
        rows[1].QuerySelector(".ts-pill")!.TextContent.ShouldBe("Active");
        rows[1].QuerySelectorAll("code").Select(c => c.TextContent).ShouldBe(["orbitly", "ORB"]);
    }

    [Fact]
    public void Each_row_links_to_its_editor_and_its_keys_and_the_header_links_to_a_new_product()
    {
        var cut = Render<ProductsPage>();

        var orbitly = cut.Find("tr[data-product=orbitly]");
        orbitly.QuerySelectorAll("td.ts-settings-actions a").Select(a => a.GetAttribute("href")).ShouldBe(
            [$"/settings/products/{TestData.OrbitlyId}", $"/settings/products/{TestData.OrbitlyId}/keys"]);
        cut.Find(".ts-settings-head a.btn").GetAttribute("href").ShouldBe("/settings/products/new");
    }

    [Fact]
    public void While_loading_it_shows_a_skeleton_and_then_the_rows()
    {
        var gate = new TaskCompletionSource<Result<IReadOnlyList<ProductDto>>>();
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        var cut = Render<ProductsPage>();

        cut.Find("[role=status]").TextContent.ShouldContain("Loading products");
        cut.FindAll("table").ShouldBeEmpty();
        gate.SetResult(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.ProductDetail()]));
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void No_products_is_a_plain_empty_state_with_the_next_step()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([]));

        var cut = Render<ProductsPage>();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No products yet");
        cut.Markup.ShouldContain("Create the first product");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<ProductDto>>("api-error", "The API is unavailable."), TestData.Ok<IReadOnlyList<ProductDto>>([TestData.ProductDetail()]));
        var cut = Render<ProductsPage>();

        cut.Find("[role=alert]").TextContent.ShouldContain("Couldn't load products. The API is unavailable.");
        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_the_api_is_not_asked()
    {
        AsAgent();

        var cut = Render<ProductsPage>();

        cut.Find("section.ts-no-access h1").TextContent.ShouldBe("You don't have access to this page.");
        cut.FindAll("table").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("New product");
        _products.ReceivedCalls().ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/ProductEditorTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 3 and 4 at the editor. A product edit never silently loses data: the update always carries the current IsActive (an omitted flag would deactivate the product), a stale Version is a
/// conflict and never an overwrite, and a failed save keeps every value on screen. An unsafe logo address (javascript:, data:, relative, plain http) never reaches the API and never reaches an img src.
/// </summary>
public sealed class ProductEditorTests : AdminPageTest
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly NavigationManager _navigation;

    public ProductEditorTests()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.ProductDetail(version: 7)));
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(TestData.ProductDetail(call.ArgAt<UpdateProductRequest>(1).Name!, active: call.ArgAt<UpdateProductRequest>(1).IsActive, version: 8)));
        _products.CreateAsync(Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(TestData.ProductDetail(call.Arg<CreateProductRequest>().Name!, id: NewId, key: call.Arg<CreateProductRequest>().Key!)));
        Services.AddSingleton(_products);
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private static readonly Guid NewId = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000b1");

    private IRenderedComponent<ProductEditorPage> RenderEdit() => Render<ProductEditorPage>(p => p.Add(c => c.Id, TestData.OrbitlyId.ToString()));

    private IRenderedComponent<ProductEditorPage> RenderNew() => Render<ProductEditorPage>();

    private static string Value(IRenderedComponent<ProductEditorPage> cut, string id) => cut.Find($"#{id}").GetAttribute("value")!;

    private static void Type(IRenderedComponent<ProductEditorPage> cut, string id, string text) => cut.Find($"#{id}").Input(text);

    private static string? FieldError(IRenderedComponent<ProductEditorPage> cut, string id) => cut.FindAll($"#{id}-error").SingleOrDefault()?.TextContent;

    // The requests the page sent, read from the substitute's own record of its calls (a later Returns on the same call must not hide them).
    private IReadOnlyList<UpdateProductRequest> Updates =>
        [.. _products.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IProductsClient.UpdateAsync)).Select(c => (UpdateProductRequest)c.GetArguments()[1]!)];

    private IReadOnlyList<CreateProductRequest> Creates =>
        [.. _products.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IProductsClient.CreateAsync)).Select(c => (CreateProductRequest)c.GetArguments()[0]!)];

    private static void Save(IRenderedComponent<ProductEditorPage> cut) => cut.Find("form").Submit();

    // ---- loading -------------------------------------------------------------------------------------------------

    [Fact]
    public void An_edit_shows_the_saved_values_with_the_key_and_prefix_read_only()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(
            logo: "https://cdn.example.com/logo.png", from: "support@orbitly.test", replyTo: "help@orbitly.test", accent: "#1D4ED8")));

        var cut = RenderEdit();

        cut.Find("h1").TextContent.ShouldBe("Orbitly");
        Value(cut, "ts-product-name").ShouldBe("Orbitly");
        Value(cut, "ts-product-display").ShouldBe("Orbitly");
        Value(cut, "ts-product-logo").ShouldBe("https://cdn.example.com/logo.png");
        Value(cut, "ts-product-accent").ShouldBe("#1D4ED8");
        Value(cut, "ts-product-from").ShouldBe("support@orbitly.test");
        Value(cut, "ts-product-reply").ShouldBe("help@orbitly.test");
        cut.Find(".ts-readonly").TextContent.ShouldContain("orbitly");
        cut.Find(".ts-readonly").TextContent.ShouldContain("ORB");
        cut.FindAll("#ts-product-key").ShouldBeEmpty();
        cut.FindAll("#ts-product-prefix").ShouldBeEmpty();
        cut.Find("img.ts-accent-preview-logo").GetAttribute("src").ShouldBe("https://cdn.example.com/logo.png");
        cut.Find("button[type=submit]").HasAttribute("disabled").ShouldBeTrue("nothing changed yet");
    }

    [Fact]
    public void A_product_the_api_does_not_know_says_so_with_no_retry_and_no_form()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ProductNotFound, "No such product.", ResultErrorKind.NotFound));

        var cut = RenderEdit();

        cut.Find("[role=alert]").TextContent.ShouldContain("This product no longer exists.");
        cut.FindAll("[role=alert] button").ShouldBeEmpty();
        cut.FindAll("form.ts-form").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Fail<ProductDto>("api-error", "The API is unavailable."), TestData.Ok(TestData.ProductDetail()));

        var cut = RenderEdit();
        cut.Find("[role=alert]").TextContent.ShouldContain("Couldn't load this product. The API is unavailable.");
        cut.Find("[role=alert] button").Click();

        cut.WaitForAssertion(() => Value(cut, "ts-product-name").ShouldBe("Orbitly"));
    }

    [Fact]
    public void An_id_that_is_not_a_guid_is_reported_as_not_found_and_calls_nothing()
    {
        var notFound = 0;
        _navigation.OnNotFound += (_, _) => notFound++;

        Render<ProductEditorPage>(p => p.Add(c => c.Id, "not-a-guid"));

        notFound.ShouldBe(1);
        _products.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_the_api_is_not_asked()
    {
        AsAgent();

        var cut = RenderEdit();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("form.ts-form").ShouldBeEmpty();
        _products.ReceivedCalls().ShouldBeEmpty();
    }

    // ---- Review Focus 3: no silent data loss ---------------------------------------------------------------------

    [Fact]
    public void Saving_a_changed_name_sends_the_current_IsActive_the_loaded_Version_and_every_branding_field_unchanged()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(
            active: true, version: 7, logo: "https://cdn.example.com/logo.png", from: "support@orbitly.test", replyTo: "help@orbitly.test")));
        var cut = RenderEdit();

        Type(cut, "ts-product-name", "Orbitly Cloud");
        Save(cut);

        var sent = Updates.ShouldHaveSingleItem();
        sent.Name.ShouldBe("Orbitly Cloud");
        sent.IsActive.ShouldBeTrue("an omitted flag would deactivate the product");
        sent.Version.ShouldBe(7u);
        sent.Branding.ShouldBe(new ProductBrandingRequest("Orbitly", "https://cdn.example.com/logo.png", "#1D4ED8", "support@orbitly.test", "help@orbitly.test"));
        _products.Received(1).UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
    }

    [Fact]
    public void An_inactive_product_stays_inactive_when_only_its_name_is_edited()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(active: false, version: 7)));
        var cut = RenderEdit();

        Type(cut, "ts-product-name", "Orbitly Legacy");
        Save(cut);

        Updates.ShouldHaveSingleItem().IsActive.ShouldBeFalse();
    }

    [Fact]
    public void The_active_switch_sends_the_flipped_value_and_blank_optional_fields_go_as_null()
    {
        var cut = RenderEdit();

        cut.Find("#ts-product-active").Change(false);
        Type(cut, "ts-product-logo", "   ");
        Save(cut);

        var sent = Updates.ShouldHaveSingleItem();
        sent.IsActive.ShouldBeFalse();
        sent.Branding.LogoPath.ShouldBeNull();
        sent.Branding.FromAddress.ShouldBeNull();
        sent.Branding.ReplyTo.ShouldBeNull();
    }

    [Fact]
    public void After_a_successful_save_the_form_shows_the_saved_product_and_the_next_save_carries_the_new_Version()
    {
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Orbitly Cloud");

        Save(cut);

        StatusMessages.Current.ShouldBe("Saved Orbitly Cloud");
        cut.Find("button[type=submit]").HasAttribute("disabled").ShouldBeTrue("saved, nothing changed since");
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
        Type(cut, "ts-product-name", "Orbitly Cloud 2");
        cut.Find(".ts-dirty").TextContent.ShouldBe("Unsaved changes");
        Save(cut);
        Updates.Select(u => u.Version).ShouldBe([7u, 8u]);
    }

    [Fact]
    public void A_stale_version_raises_the_conflict_banner_keeps_every_typed_value_and_overwrites_nothing()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ConcurrencyConflict, "This product changed since you opened it.", ResultErrorKind.Conflict));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "My new name");
        Type(cut, "ts-product-accent", "#FF0000");

        Save(cut);

        var banner = cut.Find(".ts-conflict[role=alert]");
        banner.TextContent.ShouldContain("This product changed since you opened it.");
        banner.TextContent.ShouldContain("Your edits are still on screen");
        banner.QuerySelector("button")!.TextContent.ShouldBe("Reload");
        Value(cut, "ts-product-name").ShouldBe("My new name");
        Value(cut, "ts-product-accent").ShouldBe("#FF0000");
        StatusMessages.Current.ShouldBeNull();
        Updates.Count.ShouldBe(1);
    }

    [Fact]
    public void Reload_after_a_conflict_shows_the_saved_version_and_the_next_save_uses_its_Version()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ConcurrencyConflict, "Stale.", ResultErrorKind.Conflict), TestData.Ok(TestData.ProductDetail("Theirs 2", version: 10)));
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail(version: 7)), TestData.Ok(TestData.ProductDetail("Theirs", version: 9)));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Mine");
        Save(cut);

        cut.Find(".ts-conflict button").Click();

        cut.FindAll(".ts-conflict").ShouldBeEmpty();
        Value(cut, "ts-product-name").ShouldBe("Theirs");
        Type(cut, "ts-product-name", "Theirs 2");
        Save(cut);
        Updates.Select(u => u.Version).ShouldBe([7u, 9u]);
    }

    [Theory]
    [InlineData("name", "ts-product-name")]
    [InlineData("display-name", "ts-product-display")]
    [InlineData("logo-path", "ts-product-logo")]
    [InlineData("accent-colour", "ts-product-accent")]
    [InlineData("from-address", "ts-product-from")]
    [InlineData("reply-to", "ts-product-reply")]
    public void A_field_error_from_the_api_appears_at_its_field_by_its_kebab_case_target_and_every_value_stays(string target, string inputId)
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Failure(new ResultError("x-invalid", "The server says no.", ResultErrorKind.Validation, target)));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed name");
        Type(cut, "ts-product-display", "Changed display");

        Save(cut);

        FieldError(cut, inputId).ShouldBe("The server says no.");
        cut.Find($"#{inputId}").GetAttribute("aria-invalid").ShouldBe("true");
        Value(cut, "ts-product-name").ShouldBe("Changed name");
        Value(cut, "ts-product-display").ShouldBe("Changed display");
        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public void An_error_the_form_has_no_field_for_is_shown_once_above_the_form_in_the_apis_words()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>("something-else", "Something unusual happened.", ResultErrorKind.Validation));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed");

        Save(cut);

        cut.Find("p.ts-form-error[role=alert]").TextContent.ShouldBe("Something unusual happened.");
        Value(cut, "ts-product-name").ShouldBe("Changed");
    }

    [Fact]
    public void An_uncertain_save_keeps_the_values_and_never_claims_nothing_happened()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed");

        Save(cut);

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("The save may have gone through.");
        alert.TextContent.ShouldNotContain("Nothing was changed");
        Value(cut, "ts-product-name").ShouldBe("Changed");
    }

    [Fact]
    public void A_second_submit_while_the_first_is_running_sends_once_and_the_form_is_inert_meanwhile()
    {
        var gate = new TaskCompletionSource<Result<ProductDto>>();
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed");

        Save(cut);
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("button[type=submit]").HasAttribute("disabled").ShouldBeTrue();
        Save(cut);

        _products.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IProductsClient.UpdateAsync)).ShouldBe(1);
        gate.SetResult(TestData.Ok(TestData.ProductDetail("Changed", version: 8)));
        cut.WaitForAssertion(() => StatusMessages.Current.ShouldBe("Saved Changed"));
    }

    [Fact]
    public void A_save_that_finishes_after_the_page_is_gone_is_not_cancelled_and_touches_nothing()
    {
        var gate = new TaskCompletionSource<Result<ProductDto>>();
        CancellationToken seen = default;
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Do<CancellationToken>(t => seen = t)).Returns(gate.Task);
        var cut = RenderEdit();
        Type(cut, "ts-product-name", "Changed");
        Save(cut);

        cut.FindComponent<ProductEditorContent>().Instance.Dispose();
        gate.SetResult(TestData.Ok(TestData.ProductDetail("Changed", version: 8)));

        seen.CanBeCanceled.ShouldBeFalse("a write on its way is never abandoned");
        StatusMessages.Current.ShouldBeNull("the page was disposed before the answer, so it does not report on it");
    }

    // ---- client-side validation ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("1D4ED8")]
    public void A_malformed_accent_is_refused_on_blur_and_on_submit_and_never_sent(string accent)
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-accent", accent);
        cut.Find("#ts-product-accent").Blur();
        FieldError(cut, "ts-product-accent").ShouldBe("Use a colour like #1D4ED8: a # and six hex digits.");
        Save(cut);

        Updates.ShouldBeEmpty();
        Value(cut, "ts-product-accent").ShouldBe(accent);
    }

    [Theory]
    [InlineData("#1D4ED8")]
    [InlineData("#1d4ed8")]
    [InlineData("")]
    public void A_valid_or_blank_accent_is_sent(string accent)
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-accent", accent);
        Save(cut);

        Updates.ShouldHaveSingleItem().Branding.AccentColour.ShouldBe(accent.Length == 0 ? null : accent);
    }

    [Fact]
    public void A_low_contrast_accent_shows_only_an_information_note_and_still_saves()
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-accent", "#FFEB3B");

        cut.Find("p[role=note]").TextContent.ShouldContain("low contrast");
        Save(cut);
        Updates.ShouldHaveSingleItem().Branding.AccentColour.ShouldBe("#FFEB3B");
    }

    [Fact]
    public void A_missing_name_or_display_name_or_a_bad_email_blocks_the_save_with_a_field_error_each()
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-name", "  ");
        Type(cut, "ts-product-display", "");
        Type(cut, "ts-product-from", "not-an-email");
        Save(cut);

        FieldError(cut, "ts-product-name").ShouldBe("Enter a name.");
        FieldError(cut, "ts-product-display").ShouldBe("Enter the name customers see.");
        FieldError(cut, "ts-product-from").ShouldBe("Enter a valid email address.");
        Updates.ShouldBeEmpty();
    }

    // ---- Review Focus 4: unsafe logo addresses ------------------------------------------------------------------

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("/images/logo.png")]
    [InlineData("//cdn.example.com/logo.png")]
    [InlineData("ftp://example.com/logo.png")]
    [InlineData("http://example.com/logo.png")]
    public void An_unsafe_logo_address_is_refused_never_previewed_and_never_sent(string logo)
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", logo);
        cut.Find("#ts-product-logo").Blur();

        FieldError(cut, "ts-product-logo").ShouldBe("Use a full https:// address for the logo.");
        cut.FindAll("img").ShouldBeEmpty("the preview never loads an address that failed the rule");
        cut.Markup.ShouldNotContain("src=\"javascript");
        cut.Markup.ShouldNotContain("src=\"data:");
        Save(cut);
        Updates.ShouldBeEmpty();
        _products.DidNotReceive().UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void An_https_logo_is_previewed_and_sent_trimmed()
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", "  https://cdn.example.com/logo.png  ");

        cut.Find("img.ts-accent-preview-logo").GetAttribute("src").ShouldBe("https://cdn.example.com/logo.png");
        Save(cut);
        Updates.ShouldHaveSingleItem().Branding.LogoPath.ShouldBe("https://cdn.example.com/logo.png");
    }

    [Fact]
    public void Plain_http_to_localhost_is_accepted_but_plain_http_to_anywhere_else_is_not()
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", "http://localhost:5000/logo.png");
        Save(cut);

        Updates.ShouldHaveSingleItem().Branding.LogoPath.ShouldBe("http://localhost:5000/logo.png");
    }

    [Theory]
    [InlineData("https://user:secret@cdn.example.com/logo.png")]
    [InlineData("https://cdn.example.com/my logo.png")]
    public void An_address_with_user_info_or_spaces_is_refused_by_the_shared_rule(string logo)
    {
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", logo);
        Save(cut);

        FieldError(cut, "ts-product-logo").ShouldNotBeNull();
        Updates.ShouldBeEmpty();
    }

    [Fact]
    public void The_api_refusing_a_logo_address_the_editor_accepted_is_shown_at_the_logo_field()
    {
        _products.UpdateAsync(TestData.OrbitlyId, Arg.Any<UpdateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<ProductDto>.Failure(new ResultError(ApiErrorCodes.LogoPathInvalid, "The logo address must be a full https address.", ResultErrorKind.Validation, ApiFields.LogoPath)));
        var cut = RenderEdit();

        Type(cut, "ts-product-logo", "https://blocked.example/logo.png");
        Save(cut);

        FieldError(cut, "ts-product-logo").ShouldBe("The logo address must be a full https address.");
        Value(cut, "ts-product-logo").ShouldBe("https://blocked.example/logo.png");
    }

    // ---- create --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_new_product_asks_for_key_name_prefix_and_branding_and_has_no_active_switch()
    {
        var cut = RenderNew();

        cut.Find("h1").TextContent.ShouldBe("New product");
        foreach (var id in new[] { "ts-product-key", "ts-product-name", "ts-product-prefix", "ts-product-display", "ts-product-logo", "ts-product-accent", "ts-product-from", "ts-product-reply" })
        {
            cut.FindAll($"#{id}").Count.ShouldBe(1, id);
        }

        cut.FindAll("#ts-product-active").ShouldBeEmpty();
        cut.Find("button[type=submit]").TextContent.ShouldBe("Create product");
        _products.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void A_new_product_is_validated_before_it_is_sent()
    {
        var cut = RenderNew();

        Type(cut, "ts-product-key", "Bad Key");
        Type(cut, "ts-product-prefix", "o");
        Save(cut);

        FieldError(cut, "ts-product-key").ShouldBe("Use lower-case letters, numbers and single hyphens, up to 40 characters.");
        FieldError(cut, "ts-product-name").ShouldBe("Enter a name.");
        FieldError(cut, "ts-product-prefix").ShouldBe("Use 2 to 10 capital letters or numbers, starting with a letter.");
        FieldError(cut, "ts-product-display").ShouldBe("Enter the name customers see.");
        Creates.ShouldBeEmpty();
    }

    [Fact]
    public void Creating_a_product_sends_the_request_and_goes_to_its_keys_with_a_status_message()
    {
        var cut = RenderNew();
        Type(cut, "ts-product-key", "nimbus");
        Type(cut, "ts-product-name", "Nimbus");
        Type(cut, "ts-product-prefix", "NIM");
        Type(cut, "ts-product-display", "Nimbus Cloud");
        Type(cut, "ts-product-accent", "#0F766E");

        Save(cut);

        Creates.ShouldHaveSingleItem().ShouldBe(new CreateProductRequest("nimbus", "Nimbus", "NIM", new ProductBrandingRequest("Nimbus Cloud", null, "#0F766E", null, null)));
        _navigation.Uri.ShouldEndWith($"/settings/products/{NewId}/keys");
        StatusMessages.Current.ShouldBe("Created Nimbus");
        _products.Received(1).CreateAsync(Arg.Any<CreateProductRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
    }

    [Fact]
    public void A_taken_key_or_prefix_is_shown_above_the_form_and_the_values_stay()
    {
        _products.CreateAsync(Arg.Any<CreateProductRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ProductKeyTaken, "Taken.", ResultErrorKind.Conflict));
        var cut = RenderNew();
        Type(cut, "ts-product-key", "nimbus");
        Type(cut, "ts-product-name", "Nimbus");
        Type(cut, "ts-product-prefix", "NIM");
        Type(cut, "ts-product-display", "Nimbus");

        Save(cut);

        cut.Find("p.ts-form-error").TextContent.ShouldBe("Another product already uses this key or ticket number prefix.");
        Value(cut, "ts-product-key").ShouldBe("nimbus");
        _navigation.Uri.ShouldNotContain("/keys");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/ProductEditorRealApiTests.cs`

```csharp
using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Clients;
using TechStrap.Admin.Tests.Support;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The editor over the real <c>ApiConnection</c> and products client, stubbed only at the socket: the 400 the API answers (kebab-case field targets in <c>errorCodes</c>) must reach the right field,
/// and the JSON the editor sends must carry the flag and the version under the names the API reads.
/// </summary>
public sealed class ProductEditorRealApiTests : AdminPageTest
{
    [Fact]
    public async Task A_400_with_a_kebab_case_target_reaches_its_field_and_the_request_body_carries_isActive_and_version()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, $"/api/products/{TestData.OrbitlyId}", TestData.ProductDetail(version: 7));
        api.Stub.On(HttpMethod.Put, $"/api/products/{TestData.OrbitlyId}", _ =>
            StubApiHandler.ValidationProblem("accent-colour", "accent-colour-invalid", "Use a colour like #RRGGBB."));
        Services.AddSingleton(api.Get<IProductsClient>());
        var cut = Render<ProductEditorPage>(p => p.Add(c => c.Id, TestData.OrbitlyId.ToString()));
        cut.Find("#ts-product-name").Input("Orbitly Cloud");
        cut.Find("#ts-product-accent").Input("#FF0000");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find("#ts-product-accent-error").TextContent.ShouldBe("Use a colour like #RRGGBB."));
        cut.Find("#ts-product-name").GetAttribute("value").ShouldBe("Orbitly Cloud");
        var put = api.Stub.Requests.Single(r => r.Method == HttpMethod.Put);
        using var body = JsonDocument.Parse(put.Body!);
        body.RootElement.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        body.RootElement.GetProperty("version").GetUInt32().ShouldBe(7u);
        body.RootElement.GetProperty("name").GetString().ShouldBe("Orbitly Cloud");
        body.RootElement.GetProperty("branding").GetProperty("accentColour").GetString().ShouldBe("#FF0000");
    }

    [Fact]
    public async Task A_409_concurrency_conflict_from_the_api_raises_the_banner_and_keeps_the_form()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, $"/api/products/{TestData.OrbitlyId}", TestData.ProductDetail(version: 7));
        api.Stub.OnProblem(HttpMethod.Put, $"/api/products/{TestData.OrbitlyId}", HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict, "This product changed since you opened it. Reload it and apply your change again.");
        Services.AddSingleton(api.Get<IProductsClient>());
        var cut = Render<ProductEditorPage>(p => p.Add(c => c.Id, TestData.OrbitlyId.ToString()));
        cut.Find("#ts-product-name").Input("Mine");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("This product changed since you opened it."));
        cut.Find("#ts-product-name").GetAttribute("value").ShouldBe("Mine");
    }

    [Fact]
    public async Task A_500_from_the_api_is_an_uncertain_save_not_a_failure()
    {
        await using var api = await ApiHarness.CreateAsync(AdminTestPrincipal.Admin);
        api.Stub.OnJson(HttpMethod.Get, $"/api/products/{TestData.OrbitlyId}", TestData.ProductDetail(version: 7));
        api.Stub.OnProblem(HttpMethod.Put, $"/api/products/{TestData.OrbitlyId}", HttpStatusCode.InternalServerError, "internal-error", "An unexpected error occurred.");
        Services.AddSingleton(api.Get<IProductsClient>());
        var cut = Render<ProductEditorPage>(p => p.Add(c => c.Id, TestData.OrbitlyId.ToString()));
        cut.Find("#ts-product-name").Input("Mine");

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The save may have gone through."));
        api.Stub.Requests.Count(r => r.Method == HttpMethod.Put).ShouldBe(1, "a write is never retried");
    }
}
```

`tests/TechStrap.Admin.Tests/SettingsStyleTests.cs`

```csharp
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>The settings pages (PHASE-07b): outside text wraps inside the page, and the branding preview wears the same three accent properties the portal sets.</summary>
public sealed class SettingsStyleTests
{
    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Theory]
    [InlineData(".ts-settings-table td")]
    [InlineData(".ts-readonly dd")]
    [InlineData(".ts-accent-preview-name")]
    public void Long_unbroken_text_wraps_anywhere(string selector)
    {
        Css.Declarations(selector)["overflow-wrap"].ShouldBe("anywhere");
    }

    [Fact]
    public void The_preview_bar_and_button_use_the_accent_and_its_on_colour_and_the_link_uses_the_ink()
    {
        Css.Declarations(".ts-accent-preview-bar")["background"].ShouldBe("var(--ts-accent)");
        Css.Declarations(".ts-accent-preview-bar")["color"].ShouldBe("var(--ts-on-accent)");
        Css.Declarations(".ts-accent-preview-button")["background"].ShouldBe("var(--ts-accent)");
        Css.Declarations(".ts-accent-preview-button")["color"].ShouldBe("var(--ts-on-accent)");
        Css.Declarations(".ts-accent-preview-link")["color"].ShouldBe("var(--ts-accent-ink)");
    }

    [Fact]
    public void A_logo_can_never_stretch_the_preview()
    {
        var logo = Css.Declarations(".ts-accent-preview-logo");

        logo["max-width"].ShouldBe("160px");
        logo["max-height"].ShouldBe("32px");
    }
}
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "AccentPreviewTests|ProductsPageTests|ProductEditorTests|ProductEditorRealApiTests|SettingsStyleTests"`
Expected: a build failure (`ProductsPage`, `ProductEditorPage`, `ProductsContent`, `ProductEditorContent`, `AccentPreview`, `ProductFields` do not exist).

- [ ] **Step 3: Implement**

1. The field limits, the copy and the view models:

`src/TechStrap.Admin/Features/Settings/Products/ProductFields.cs`

```csharp
using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>The limits the server enforces on a product, and the order of the fields on the form (so the first error is the first one the agent meets). The field names of a 400 are <see cref="ApiFields"/>.</summary>
public static class ProductFields
{
    public const int KeyMaxLength = 40;
    public const int NameMaxLength = 100;
    public const int DisplayNameMaxLength = 100;
    public const int EmailMaxLength = 320;

    public static readonly IReadOnlyList<string> All =
        [ApiFields.Key, ApiFields.Name, ApiFields.NumberPrefix, ApiFields.DisplayName, ApiFields.LogoPath, ApiFields.AccentColour, ApiFields.FromAddress, ApiFields.ReplyTo];
}
```

`src/TechStrap.Admin/Features/Settings/Products/ProductsCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>The copy of the product list and the product editor.</summary>
public static class ProductsCopy
{
    public const string Heading = "Products";
    public const string NewProduct = "New product";
    public const string Loading = "Loading products";
    public const string LoadFailed = "Couldn't load products.";
    public const string NoProducts = "No products yet";
    public const string NoProductsHint = "Create the first product to give it a portal, branding and API keys.";
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Edit = "Edit";
    public const string ApiKeys = "API keys";
    public const string BackToProducts = "Products";

    public const string EditorNewTitle = "New product";
    public const string EditorLoading = "Loading product";
    public const string EditorLoadFailed = "Couldn't load this product.";
    public const string EditorGone = "This product no longer exists.";

    public const string KeyLabel = "Key";
    public const string KeyHelp = "Letters, numbers and hyphens. It can't be changed once the product exists.";
    public const string NameLabel = "Name";
    public const string NumberPrefixLabel = "Ticket number prefix";
    public const string NumberPrefixHelp = "2 to 10 capital letters or numbers, such as ORB. It can't be changed once the product exists.";
    public const string BrandingHeading = "Branding";
    public const string DisplayNameLabel = "Display name";
    public const string DisplayNameHelp = "The name customers see in the portal and in emails.";
    public const string LogoLabel = "Logo address";
    public const string LogoHelp = "A full https:// address of an image. Leave it blank for no logo.";
    public const string AccentLabel = "Accent colour";
    public const string AccentHelp = "A # and six hex digits, such as #1D4ED8. Leave it blank for the default.";
    public const string FromLabel = "Email from address";
    public const string ReplyToLabel = "Email reply-to address";
    public const string ActiveLabel = "Active";
    public const string ActiveHelp = "Inactive products are hidden from the agents' product lists.";
    public const string PreviewHeading = "Preview";

    public const string Save = "Save product";
    public const string Create = "Create product";
    public const string Saving = "Saving\u2026";
    public const string Unsaved = "Unsaved changes";

    public const string KeyRequired = "Enter a key.";
    public const string KeyInvalid = "Use lower-case letters, numbers and single hyphens, up to 40 characters.";
    public const string NameRequired = "Enter a name.";
    public const string NameTooLong = "Use 100 characters or fewer.";
    public const string NumberPrefixInvalid = "Use 2 to 10 capital letters or numbers, starting with a letter.";
    public const string DisplayNameRequired = "Enter the name customers see.";
    public const string LogoInvalid = "Use a full https:// address for the logo.";
    public const string AccentInvalid = "Use a colour like #1D4ED8: a # and six hex digits.";
    public const string EmailInvalid = "Enter a valid email address.";

    public const string ConflictTitle = "This product changed since you opened it.";
    public const string ConflictKept = "Your edits are still on screen. Reload shows the saved version and drops them.";
    public const string Reload = "Reload";
    public const string SaveUncertain = "The save may have gone through. Reload to see the saved version before you save again.";
    public const string ProductKeyTaken = "Another product already uses this key or ticket number prefix.";

    public static string Saved(string name) => $"Saved {name}";

    public static string Created(string name) => $"Created {name}";
}
```

`src/TechStrap.Admin/Features/Settings/Products/ProductEditorViewModel.cs`

```csharp
using System.Text.RegularExpressions;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>
/// The form model of the product editor. It holds exactly what is on screen, validates it with the server's rules (the colour with the same Contracts constant as the API's pattern),
/// and builds the requests. <see cref="IsActive"/> is always carried (the update request has a non-nullable flag, and an omitted one would deactivate the product) and <see cref="Version"/>
/// is the one the product was loaded with.
/// </summary>
internal sealed partial class ProductEditorViewModel
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,9}$")]
    private static partial Regex NumberPrefixPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string NumberPrefix { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string LogoPath { get; set; } = string.Empty;

    public string AccentColour { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string ReplyTo { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public uint Version { get; set; }

    public static ProductEditorViewModel From(ProductDto product) => new()
    {
        Key = product.Key,
        Name = product.Name,
        NumberPrefix = product.NumberPrefix,
        DisplayName = product.Branding.DisplayName,
        LogoPath = product.Branding.LogoPath ?? string.Empty,
        AccentColour = product.Branding.AccentColour,
        FromAddress = product.Branding.FromAddress ?? string.Empty,
        ReplyTo = product.Branding.ReplyTo ?? string.Empty,
        IsActive = product.IsActive,
        Version = product.Version,
    };

    public CreateProductRequest ToCreateRequest() => new(Key.Trim(), Name.Trim(), NumberPrefix.Trim(), ToBranding());

    public UpdateProductRequest ToUpdateRequest() => new(Name.Trim(), ToBranding(), IsActive, Version);

    private ProductBrandingRequest ToBranding() =>
        new(DisplayName.Trim(), Blank(LogoPath), Blank(AccentColour), Blank(FromAddress), Blank(ReplyTo));

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The message for one field, or null when it is fine. Key and ticket number prefix are checked only when the product is being created.</summary>
    public string? Check(string field, bool creating) => field switch
    {
        ApiFields.Key when creating => string.IsNullOrWhiteSpace(Key) ? ProductsCopy.KeyRequired
            : Key.Trim().Length > ProductFields.KeyMaxLength || !KeyPattern().IsMatch(Key.Trim()) ? ProductsCopy.KeyInvalid : null,
        ApiFields.Name => string.IsNullOrWhiteSpace(Name) ? ProductsCopy.NameRequired
            : Name.Trim().Length > ProductFields.NameMaxLength ? ProductsCopy.NameTooLong : null,
        ApiFields.NumberPrefix when creating => NumberPrefixPattern().IsMatch(NumberPrefix.Trim()) ? null : ProductsCopy.NumberPrefixInvalid,
        ApiFields.DisplayName => string.IsNullOrWhiteSpace(DisplayName) ? ProductsCopy.DisplayNameRequired
            : DisplayName.Trim().Length > ProductFields.DisplayNameMaxLength ? ProductsCopy.NameTooLong : null,
        ApiFields.LogoPath => BrandingRules.IsAcceptableLogoUrl(LogoPath) ? null : ProductsCopy.LogoInvalid,
        ApiFields.AccentColour => string.IsNullOrWhiteSpace(AccentColour) || Regex.IsMatch(AccentColour.Trim(), BrandingRules.ColourPattern) ? null : ProductsCopy.AccentInvalid,
        ApiFields.FromAddress => IsEmailOrBlank(FromAddress) ? null : ProductsCopy.EmailInvalid,
        ApiFields.ReplyTo => IsEmailOrBlank(ReplyTo) ? null : ProductsCopy.EmailInvalid,
        _ => null,
    };

    private static bool IsEmailOrBlank(string value) =>
        string.IsNullOrWhiteSpace(value) || (value.Trim().Length <= ProductFields.EmailMaxLength && EmailPattern().IsMatch(value.Trim()));
}
```

`src/TechStrap.Admin/Features/Settings/Products/ProductRowViewModel.cs`

```csharp
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Settings.Products;

internal sealed record ProductRowViewModel(Guid Id, string Name, string Key, string NumberPrefix, bool IsActive, string Accent)
{
    public static ProductRowViewModel From(ProductDto product) =>
        new(product.Id, product.Name, product.Key, product.NumberPrefix, product.IsActive, product.Branding.AccentColour);
}
```

2. The list: the shell and the content.

`src/TechStrap.Admin/Features/Settings/Products/ProductsPage.razor`

```razor
@page "/settings/products"
@attribute [Authorize]

<AdminOnly>
    <ProductsContent />
</AdminOnly>
```

`src/TechStrap.Admin/Features/Settings/Products/ProductsContent.razor`

```razor
<PageTitle>@ProductsCopy.Heading &middot; Settings</PageTitle>
    <div class="ts-settings">
        <div class="ts-settings-head">
            <h1>@ProductsCopy.Heading</h1>
            <a class="btn btn-primary" href="/settings/products/new">@ProductsCopy.NewProduct</a>
        </div>

        @if (_loading)
        {
            <LoadingState Rows="4" Label="@ProductsCopy.Loading" />
        }
        else if (_error is not null)
        {
            <ErrorState Message="@_error" OnRetry="LoadAsync" />
        }
        else if (_rows.Count == 0)
        {
            <EmptyState Heading="@ProductsCopy.NoProducts">
                <p>@ProductsCopy.NoProductsHint</p>
            </EmptyState>
        }
        else
        {
            <div class="table-responsive">
                <table class="table ts-ledger ts-settings-table">
                    <thead>
                        <tr>
                            <th scope="col">Name</th>
                            <th scope="col">Key</th>
                            <th scope="col">Prefix</th>
                            <th scope="col">Status</th>
                            <th scope="col"><span class="visually-hidden">Actions</span></th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var row in _rows)
                        {
                            <tr @key="row.Id" data-product="@row.Key">
                                <td><a href="/settings/products/@row.Id">@row.Name</a></td>
                                <td><code>@row.Key</code></td>
                                <td><code>@row.NumberPrefix</code></td>
                                <td><span class="ts-pill @(row.IsActive ? "ts-pill--on" : "ts-pill--off")">@(row.IsActive ? ProductsCopy.Active : ProductsCopy.Inactive)</span></td>
                                <td class="ts-settings-actions">
                                    <a href="/settings/products/@row.Id">@ProductsCopy.Edit</a>
                                    <a href="/settings/products/@row.Id/keys">@ProductsCopy.ApiKeys</a>
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        }
    </div>
```

`src/TechStrap.Admin/Features/Settings/Products/ProductsContent.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>
/// The product list (Admin only). The page makes no API call unless the session is an Admin: a plain agent gets the no-access page from <c>AdminOnly</c>, and the load below is not even attempted,
/// so no request is made that the API would refuse with a 403. The API returns every product to an Admin, inactive ones included.
/// </summary>
public sealed partial class ProductsContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<ProductRowViewModel> _rows = [];
    private string? _error;
    private bool _loading = true;

    [Inject]
    private IProductsClient Products { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            var result = await Products.ListAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Select(ProductRowViewModel.From).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Key, StringComparer.Ordinal)];
            }
            else
            {
                _error = $"{ProductsCopy.LoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

3. The editor: the shell and the content.

`src/TechStrap.Admin/Features/Settings/Products/ProductEditorPage.razor`

```razor
@page "/settings/products/new"
@page "/settings/products/{Id}"
@attribute [Authorize]

<AdminOnly>
    <ProductEditorContent Id="@Id" />
</AdminOnly>

@code {
    /// <summary>The route segment: a product id, or absent on <c>/settings/products/new</c>.</summary>
    [Parameter]
    public string? Id { get; set; }
}
```

`src/TechStrap.Admin/Features/Settings/Products/ProductEditorContent.razor`

```razor
<PageTitle>@Title &middot; Products</PageTitle>
    <div class="ts-settings ts-product-editor">
        <p class="ts-settings-back"><a href="/settings/products">&larr; @ProductsCopy.BackToProducts</a></p>
        <h1>@Title</h1>

        @if (_loading)
        {
            <LoadingState Rows="6" Label="@ProductsCopy.EditorLoading" />
        }
        else if (_loadError is not null)
        {
            <ErrorState Message="@_loadError" OnRetry="@(_gone ? default(EventCallback) : EventCallback.Factory.Create(this, LoadAsync))" />
        }
        else
        {
            @if (_conflict)
            {
                <div class="ts-conflict" role="alert">
                    <p><strong>@ProductsCopy.ConflictTitle</strong> @ProductsCopy.ConflictKept</p>
                    <button type="button" class="btn btn-outline-secondary" disabled="@_busy" @onclick="ReloadAsync">@ProductsCopy.Reload</button>
                </div>
            }
            @if (_uncertain)
            {
                <div class="ts-conflict" role="alert">
                    <p>@ProductsCopy.SaveUncertain</p>
                    <button type="button" class="btn btn-outline-secondary" disabled="@_busy" @onclick="ReloadAsync">@ProductsCopy.Reload</button>
                </div>
            }
            @if (_formError is not null)
            {
                <p class="ts-form-error" role="alert">@_formError</p>
            }

            <form class="ts-form" novalidate @onsubmit="SaveAsync">
                <fieldset disabled="@_busy">
                    @if (_creating)
                    {
                        @Field("ts-product-key", ProductsCopy.KeyLabel, ApiFields.Key, _model.Key, v => _model.Key = v, ProductsCopy.KeyHelp)
                    }
                    else
                    {
                        <dl class="ts-readonly">
                            <dt>@ProductsCopy.KeyLabel</dt>
                            <dd><code>@_model.Key</code></dd>
                            <dt>@ProductsCopy.NumberPrefixLabel</dt>
                            <dd><code>@_model.NumberPrefix</code></dd>
                        </dl>
                    }
                    @Field("ts-product-name", ProductsCopy.NameLabel, ApiFields.Name, _model.Name, v => _model.Name = v)
                    @if (_creating)
                    {
                        @Field("ts-product-prefix", ProductsCopy.NumberPrefixLabel, ApiFields.NumberPrefix, _model.NumberPrefix, v => _model.NumberPrefix = v, ProductsCopy.NumberPrefixHelp)
                    }

                    <h2>@ProductsCopy.BrandingHeading</h2>
                    @Field("ts-product-display", ProductsCopy.DisplayNameLabel, ApiFields.DisplayName, _model.DisplayName, v => _model.DisplayName = v, ProductsCopy.DisplayNameHelp)
                    @Field("ts-product-logo", ProductsCopy.LogoLabel, ApiFields.LogoPath, _model.LogoPath, v => _model.LogoPath = v, ProductsCopy.LogoHelp, "url")
                    @Field("ts-product-accent", ProductsCopy.AccentLabel, ApiFields.AccentColour, _model.AccentColour, v => _model.AccentColour = v, ProductsCopy.AccentHelp)
                    @Field("ts-product-from", ProductsCopy.FromLabel, ApiFields.FromAddress, _model.FromAddress, v => _model.FromAddress = v, null, "email")
                    @Field("ts-product-reply", ProductsCopy.ReplyToLabel, ApiFields.ReplyTo, _model.ReplyTo, v => _model.ReplyTo = v, null, "email")

                    @if (!_creating)
                    {
                        <div class="form-check ts-product-active">
                            <input id="ts-product-active" class="form-check-input" type="checkbox" checked="@_model.IsActive" @onchange="OnActiveChanged" />
                            <label class="form-check-label" for="ts-product-active">@ProductsCopy.ActiveLabel</label>
                            <p class="ts-field-help">@ProductsCopy.ActiveHelp</p>
                        </div>
                    }

                    <h2>@ProductsCopy.PreviewHeading</h2>
                    <AccentPreview Accent="@_model.AccentColour" DisplayName="@_model.DisplayName" LogoUrl="@PreviewLogo" />
                </fieldset>

                <div class="ts-form-actions">
                    <button type="submit" class="btn btn-primary" disabled="@(_busy || !CanSave)">@(_busy ? ProductsCopy.Saving : _creating ? ProductsCopy.Create : ProductsCopy.Save)</button>
                    @if (_dirty && !_creating)
                    {
                        <span class="ts-dirty" role="status">@ProductsCopy.Unsaved</span>
                    }
                </div>
            </form>
        }
    </div>

@code {
    /// <summary>One labelled text input with its help text and its error; the value is bound on input and checked on blur.</summary>
    private RenderFragment Field(string id, string label, string field, string value, Action<string> set, string? help = null, string type = "text") => @<div class="ts-field">
        <label for="@id" class="form-label">@label</label>
        <input id="@id" type="@type" class="form-control @(_errors.ContainsKey(field) ? "is-invalid" : null)" value="@value" autocomplete="off"
               aria-invalid="@(_errors.ContainsKey(field) ? "true" : null)" aria-describedby="@(_errors.ContainsKey(field) ? $"{id}-error" : help is not null ? $"{id}-help" : null)"
               @oninput="e => OnInput(set, e)" @onblur="() => CheckField(field)" />
        @if (help is not null)
        {
            <p id="@($"{id}-help")" class="ts-field-help">@help</p>
        }
        @if (_errors.TryGetValue(field, out var message))
        {
            <p id="@($"{id}-error")" class="ts-field-error" role="alert">@message</p>
        }
    </div>;
}
```

`src/TechStrap.Admin/Features/Settings/Products/ProductEditorContent.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Branding;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>
/// Creates a product or edits one (Admin only). Nothing on this page loses what the agent typed: a failed, refused or uncertain save leaves the form exactly as it was, and only a successful save
/// or an explicit Reload replaces the model. The update always carries the product's current <c>IsActive</c> (an omitted flag would deactivate it) and the <c>Version</c> it was loaded with, so a
/// stale save is a 409 and never an overwrite. Field errors from the API are mapped by their kebab-case target. Writes use <see cref="CancellationToken.None"/>: a save that is on its way is never
/// abandoned because the agent left the page. The logo address is checked with <see cref="BrandingRules.IsAcceptableLogoUrl"/> before anything is sent, and the preview never loads an address that failed it.
/// </summary>
public sealed partial class ProductEditorContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, string> _errors = [];
    private ProductEditorViewModel _model = new();
    private Guid _id;
    private string? _loadedFor;
    private string? _loadError;
    private string? _formError;
    private bool _creating;
    private bool _loading;
    private bool _busy;
    private bool _dirty;
    private bool _conflict;
    private bool _uncertain;
    private bool _gone;
    private bool _disposed;

    [Inject]
    private IProductsClient Products { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    /// <summary>The route segment: a product id, or absent on <c>/settings/products/new</c>.</summary>
    [Parameter]
    public string? Id { get; set; }

    private string Title => _creating ? ProductsCopy.EditorNewTitle : string.IsNullOrWhiteSpace(_model.Name) ? ProductsCopy.Heading : _model.Name;

    // Save is offered for a new product, and for an edit once something changed.
    private bool CanSave => _creating || _dirty;

    // The preview never loads an address that failed the rule.
    private string? PreviewLogo => BrandingRules.IsAcceptableLogoUrl(_model.LogoPath) && !string.IsNullOrWhiteSpace(_model.LogoPath) ? _model.LogoPath.Trim() : null;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedFor == (Id ?? string.Empty))
        {
            return;
        }

        _loadedFor = Id ?? string.Empty;
        _creating = Id is null;
        if (_creating)
        {
            _model = new ProductEditorViewModel();
            return;
        }

        if (!Guid.TryParse(Id, out _id))
        {
            Navigation.NotFound();
            return;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadError = null;
        _gone = false;
        try
        {
            var result = await Products.GetAsync(_id, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _model = ProductEditorViewModel.From(result.Value);
                _errors.Clear();
                _dirty = false;
            }
            else
            {
                _gone = result.Errors[0].Code == ApiErrorCodes.ProductNotFound;
                _loadError = _gone ? ProductsCopy.EditorGone : $"{ProductsCopy.EditorLoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ReloadAsync()
    {
        _conflict = false;
        _uncertain = false;
        _formError = null;
        await LoadAsync();
    }

    private void OnInput(Action<string> set, ChangeEventArgs e)
    {
        set(e.Value?.ToString() ?? string.Empty);
        _dirty = true;
    }

    private void OnActiveChanged(ChangeEventArgs e)
    {
        _model.IsActive = e.Value is true;
        _dirty = true;
    }

    private void CheckField(string field)
    {
        var message = _model.Check(field, _creating);
        if (message is null)
        {
            _errors.Remove(field);
        }
        else
        {
            _errors[field] = message;
        }
    }

    private bool CheckAll()
    {
        foreach (var field in ProductFields.All)
        {
            CheckField(field);
        }

        return _errors.Count == 0;
    }

    private async Task SaveAsync()
    {
        if (_busy)
        {
            return;
        }

        _formError = null;
        _conflict = false;
        _uncertain = false;
        if (!CheckAll())
        {
            return;
        }

        _busy = true;
        try
        {
            var result = _creating
                ? await Products.CreateAsync(_model.ToCreateRequest(), CancellationToken.None)
                : await Products.UpdateAsync(_id, _model.ToUpdateRequest(), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                ShowFailure(result.Errors);
                return;
            }

            var saved = result.Value;
            if (_creating)
            {
                StatusMessages.Show(ProductsCopy.Created(saved.Name));
                Navigation.NavigateTo($"/settings/products/{saved.Id}/keys");
                return;
            }

            _model = ProductEditorViewModel.From(saved);
            _dirty = false;
            StatusMessages.Show(ProductsCopy.Saved(saved.Name));
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowFailure(IReadOnlyList<ResultError> errors)
    {
        var first = errors[0];
        if (first.Code == ApiErrorCodes.ConcurrencyConflict)
        {
            _conflict = true;
        }
        else if (first.Code == ApiErrorCodes.ProductNotFound)
        {
            _formError = ProductsCopy.EditorGone;
        }
        else if (first.Code == ApiErrorCodes.ProductKeyTaken)
        {
            _formError = ProductsCopy.ProductKeyTaken;
        }
        else if (ApiErrorCodes.IsUncertainWrite(first.Code))
        {
            _uncertain = true;
        }
        else
        {
            MapFieldErrors(errors);
        }
    }

    // A 400 names the field in kebab-case. Anything the form has no field for is shown once, above the form, in the API's words.
    private void MapFieldErrors(IReadOnlyList<ResultError> errors)
    {
        foreach (var error in errors)
        {
            if (error.Target is { } target && ProductFields.All.Contains(target))
            {
                _errors.TryAdd(target, error.Message);
            }
            else
            {
                _formError ??= error.Message;
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

4. The branding preview:

`src/TechStrap.Admin/Components/Ui/AccentPreview.razor`

```razor
<div class="ts-accent-preview" style="@_style">
    <div class="ts-accent-preview-bar">
        @if (!string.IsNullOrEmpty(LogoUrl))
        {
            <img class="ts-accent-preview-logo" src="@LogoUrl" alt="" referrerpolicy="no-referrer" />
        }
        <span class="ts-accent-preview-name">@DisplayName</span>
    </div>
    <p class="ts-accent-preview-sample">
        <span class="ts-accent-preview-button">@AccentPreviewCopy.SampleButton</span>
        <span class="ts-accent-preview-link">@AccentPreviewCopy.SampleLink</span>
    </p>
</div>
@if (_note is not null)
{
    <p class="ts-field-note" role="note">@_note</p>
}
```

`src/TechStrap.Admin/Components/Ui/AccentPreview.razor.cs`

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Components;
using TechStrap.Contracts.Branding;

namespace TechStrap.Admin.Components.Ui;

/// <summary>
/// A small preview of a product's branding: its name and logo on a bar, a button in the accent and a link in the readable accent ink. It sets the same three custom properties as the portal's
/// accent scope (<c>--ts-accent</c>, <c>--ts-on-accent</c>, <c>--ts-accent-ink</c>), derived with the one shared rule (<see cref="ProductAccent"/>). A malformed or blank colour sets nothing and the stylesheet's
/// fallbacks apply. A low-contrast colour only produces an information note: TechStrap darkens it where it is used as text, so it is never refused. The caller passes only a logo address that
/// passed the logo rule; this component renders whatever it is given.
/// </summary>
public partial class AccentPreview
{
    private string? _style;
    private string? _note;

    /// <summary>A <c>#RRGGBB</c> value as typed. Blank or malformed shows the default look.</summary>
    [Parameter]
    public string? Accent { get; set; }

    [Parameter]
    public string? DisplayName { get; set; }

    /// <summary>An already-validated image address, or null for no logo.</summary>
    [Parameter]
    public string? LogoUrl { get; set; }

    protected override void OnParametersSet()
    {
        if (!ProductAccent.TryDerive(Accent?.Trim(), out var colors))
        {
            _style = null;
            _note = null;
            return;
        }

        _style = $"--ts-accent:{colors.Accent};--ts-on-accent:{colors.OnAccent};--ts-accent-ink:{colors.AccentInk}";
        var ratio = ProductAccent.ContrastRatio(colors.Accent, "#FFFFFF");
        _note = ratio < ProductAccent.MinimumTextContrast ? AccentPreviewCopy.LowContrast(Floor(ratio)) : null;
    }

    // Rounded down, so "4.5:1" is never shown for a colour that is below 4.5.
    private static string Floor(double ratio) => (Math.Floor(ratio * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture);
}

public static class AccentPreviewCopy
{
    public const string SampleButton = "Button";
    public const string SampleLink = "Link";

    public static string LowContrast(string ratio) =>
        $"This colour has low contrast on white ({ratio}:1; 4.5:1 is the aim for text). TechStrap darkens it wherever it is used for text, so it stays readable. You can keep it.";
}
```

5. The styles:

`src/TechStrap.Admin/Styles/_settings.scss`

```scss
// Settings pages (PHASE-07b): the page frame, forms, status pills and the branding preview. Working UI like the queue: compact, no mascot, no window frame.
// Text that comes from outside (names, keys, addresses) wraps instead of stretching the table.

.ts-settings {
  max-width: 960px;
  padding: 12px 16px;

  h1 {
    margin: 0 0 8px;
    font: 600 1.125rem var(--ts-font-mono);
  }

  h2 {
    margin: 20px 0 8px;
    font: 600 .875rem var(--ts-font-mono);
    letter-spacing: .04em;
    text-transform: uppercase;
  }
}

.ts-settings-head {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 16px;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;

  h1 {
    margin: 0;
  }
}

.ts-settings-back {
  margin: 0 0 4px;
  font: 500 .75rem var(--ts-font-mono);
}

.ts-settings-table {
  td {
    min-width: 0;
    overflow-wrap: anywhere;
    vertical-align: top;
  }
}

.ts-settings-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 12px;
}

// Status in words first; the border colour only repeats it.
.ts-pill {
  display: inline-block;
  padding: 1px 8px;
  font: 600 .6875rem var(--ts-font-mono);
  letter-spacing: .04em;
  text-transform: uppercase;
  color: var(--ink);
  background: var(--sheet);
  border: 2px solid var(--rule-strong);
}

.ts-pill--on {
  border-color: var(--st-open);
}

.ts-pill--off {
  color: var(--ink-2);
  border-style: dashed;
}

.ts-form {
  max-width: 560px;

  fieldset {
    min-width: 0;
    padding: 0;
    margin: 0;
    border: 0;
  }
}

.ts-field {
  margin-bottom: 12px;
}

.ts-field-help {
  margin: 4px 0 0;
  font-size: .75rem;
  color: var(--ink-2);
}

.ts-field-error {
  margin: 4px 0 0;
  padding-left: 8px;
  font-size: .8125rem;
  border-left: 3px solid var(--st-spam);
}

.ts-field-note {
  max-width: 560px;
  margin: 8px 0 0;
  padding-left: 8px;
  font-size: .8125rem;
  color: var(--ink-2);
  border-left: 3px solid var(--rule-strong);
}

.ts-form-error {
  max-width: 560px;
  margin: 0 0 12px;
  padding: 6px 10px;
  border: 2px solid var(--st-spam);
}

.ts-form-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 16px;
  align-items: center;
  margin-top: 16px;
}

.ts-dirty {
  font: 500 .75rem var(--ts-font-mono);
  color: var(--ink-2);
}

.ts-readonly {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 4px 16px;
  margin: 0 0 12px;

  dt {
    font: 500 .75rem var(--ts-font-mono);
    color: var(--ink-2);
  }

  dd {
    min-width: 0;
    margin: 0;
    overflow-wrap: anywhere;
  }
}

// The branding preview: the same three custom properties the portal sets on its root (docs/BRAND.md), set here on the preview only.
.ts-accent-preview {
  max-width: 360px;
  background: var(--sheet);
  border: 2px solid var(--rule-strong);
}

.ts-accent-preview-bar {
  display: flex;
  gap: 8px;
  align-items: center;
  min-height: 40px;
  padding: 8px 12px;
  color: var(--ts-on-accent);
  background: var(--ts-accent);
}

.ts-accent-preview-logo {
  max-width: 160px;
  max-height: 32px;
  object-fit: contain;
}

.ts-accent-preview-name {
  min-width: 0;
  font: 600 .9375rem var(--ts-font-mono);
  overflow-wrap: anywhere;
}

.ts-accent-preview-sample {
  display: flex;
  gap: 16px;
  align-items: center;
  margin: 0;
  padding: 12px;
}

.ts-accent-preview-button {
  padding: 4px 12px;
  font: 600 .75rem var(--ts-font-mono);
  color: var(--ts-on-accent);
  background: var(--ts-accent);
  border: 2px solid var(--ts-accent);
}

.ts-accent-preview-link {
  color: var(--ts-accent-ink);
  text-decoration: underline;
}
```

`src/TechStrap.Admin/Styles/app.scss` imports it after the composer:

```diff
@@
 @import "composer";
+@import "settings";
 @import "styleguide";
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "AccentPreviewTests|ProductsPageTests|ProductEditorTests|ProductEditorRealApiTests|SettingsStyleTests|SourceEncodingTests|SourceEscapeTests"`
Expected: PASS. These tests were written first and failed to build in Step 2. To see them guard (do not commit these edits), make each change below, rerun `--filter ProductEditorTests`, expect the failures named, and restore the file:
- `ProductEditorViewModel.ToUpdateRequest`: send `true` instead of `IsActive`: `An_inactive_product_stays_inactive_when_only_its_name_is_edited` and `The_active_switch_sends_the_flipped_value_...` fail.
- `ProductEditorViewModel.ToUpdateRequest`: send `0` instead of `Version`: `Saving_a_changed_name_sends_...`, `After_a_successful_save_...` and `Reload_after_a_conflict_...` fail.
- `ProductEditorViewModel.Check`: return `null` for the logo field: every case of `An_unsafe_logo_address_is_refused_never_previewed_and_never_sent` fails.

- [ ] **Step 5: Whole-project check**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then `dotnet test --project tests/TechStrap.Admin.Tests -c Release`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Admin/Features/Settings/Products \
  src/TechStrap.Admin/Components/Ui/AccentPreview.razor \
  src/TechStrap.Admin/Components/Ui/AccentPreview.razor.cs \
  src/TechStrap.Admin/Styles \
  tests/TechStrap.Admin.Tests
git diff --cached --stat
git commit -m "feat(admin): products list and editor with branding preview (P07-T14)" -m "Pages are thin shells over AdminOnly. The editor always sends the current IsActive and the loaded Version, keeps the form on every failure and checks the logo with the shared rule." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 6: API keys panel and the show-once dialog (P07-T15)

**Review Focus pins (2 and 5):** the API key plaintext never leaks (not into a log, a URL, any attribute but the dialog's read-only field, or the markup after the dialog closes) and the dialog cannot be dismissed before "I have stored this key" is ticked; a revoke never fires without its confirmation and never fires twice. Pinned by `NewApiKeyDialogTests` (the whole class), `RevokeApiKeyTests` (the whole class) and, at the host, Task 9's `AdminLeakTests`.

**Files:**
- Create: `src/TechStrap.Admin/Features/Settings/Products/ApiKeysCopy.cs`, `ApiKeyRowViewModel.cs`, `ApiKeyKindBadge.razor`, `NewApiKeyDialog.razor` and `.razor.cs`, `ApiKeysPanel.razor` and `.razor.cs`, `ProductKeysContent.razor` and `.razor.cs`, `ProductKeysPage.razor`
- Create: `src/TechStrap.Admin/wwwroot/js/clipboard.js`, `src/TechStrap.Admin/Styles/_api-keys.scss`; Modify: `src/TechStrap.Admin/Styles/app.scss`
- Create: `tests/TechStrap.Admin.Tests/Support/TestData.ApiKeys.cs`, `Support/RecordingLoggerProvider.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/NewApiKeyDialogTests.cs`, `ApiKeysPanelTests.cs`, `RevokeApiKeyTests.cs`, `ProductKeysPageTests.cs`, `tests/TechStrap.Admin.Tests/js/clipboard.test.mjs`
- Modify: `tests/TechStrap.Admin.Tests/ScriptHostTests.cs`, `scripts/tests/AdminScripts.Tests.ps1`

**Interfaces:**
- Consumes:
  - `IProductsClient.GetAsync`, `ListApiKeysAsync(Guid productId, ct)` (`Result<IReadOnlyList<ProductApiKeyDto>>`), `CreateApiKeyAsync(Guid productId, CreateProductApiKeyRequest, ct)` (`Result<CreateProductApiKeyResponse>`), `RevokeApiKeyAsync(Guid productId, Guid keyId, ct)` (`Result`); `ApiErrorCodes.ApiKeyKindInvalid`, `ApiKeyNotFound`, `ProductNotFound`, `IsUncertainWrite`; `ApiFields.Kind`, `ApiFields.Label`.
  - `ApiKeyKinds` (existing Contracts), `AdminOnly` (Task 4), the 07a `ConfirmDialog` (`Open`, `Title`, `ConfirmLabel`, `CancelLabel`, `Danger`, `Busy`, `ConfirmDisabled`, `Error`, `OnConfirm`, `OnCancel`; Enter never confirms, Esc and Cancel both raise `OnCancel`, the browser close is always prevented so only the owner closes it), `RelativeTime`, `LoadingState`, `ErrorState`, `EmptyState`, `StatusMessageService`, `ShellCopy.Close`.
  - From Task 5: `AdminPageTest`, `ProductsCopy`, `TestData.ProductDetail`.
- Produces:

```csharp
// TechStrap.Admin.Features.Settings.Products
public sealed partial class ProductKeysPage             // shell: /settings/products/{Id:guid}/keys
public sealed partial class ProductKeysContent          // loads the product, hosts the panel
public sealed partial class ApiKeysPanel : IDisposable  // ProductId, ProductName
public sealed partial class NewApiKeyDialog : IAsyncDisposable   // ProductName, OnClosed; void Show(ProductApiKeyDto key, string plaintext)
public static class ApiKeysCopy
// wwwroot/js/clipboard.js: export async function copyText(text, nav = globalThis.navigator) -> bool;  export function selectText(element) -> bool   (neither throws)
```

**Rules:**
1. **The plaintext lives in one field.** `ApiKeysPanel` calls `NewApiKeyDialog.Show(key, plaintext)` and keeps nothing: not in a row (rows are built from `ProductApiKeyDto`, which has the prefix only), not in the status bar, not in a URL, not in a component field of the panel. The dialog holds it in `_plaintext` from `Show` until close and renders it in exactly one place, the read-only field's `value` (so the markup contains it once). The field is not rendered at all once the dialog is closed. Nothing logs it; nothing puts it in `aria-*`, `title` or `href`.
2. **No accidental dismissal.** `NewApiKeyDialog` is a `ConfirmDialog` whose `OnCancel` (Esc and the Close button alike) does nothing but show "Tick "I have stored this key" before you close this window." until the box is ticked, and whose Done button is `ConfirmDisabled` until then. After the tick, Done, Close and Esc all close it, clear `_plaintext`, untick the box and raise `OnClosed` once. Showing a second key starts clean.
3. **Copy.** The Copy button calls `clipboard.js` `copyText` with the key. If the browser refuses (`false`) or the script cannot run (`JSException`), the key field is selected through `selectText` (the element reference, never the text again) and the status line says "Couldn't copy. The key is selected: press Ctrl+C to copy it."; success says "Copied to the clipboard.". The script is imported on first use, so a dialog nobody copies from never loads it.
4. **Create.** The agent must choose the kind (Create is disabled until then; both kinds are explained beside the choice with the UX-BRIEF text word for word), the label is optional and at most 100 characters. A blank label goes as `null`. The write uses `CancellationToken.None`. On success the new row is added from `response.Key`, the status bar says "Created a {kind} key for {product}" and the dialog opens. A field error from the API (`kind`, `label`) appears at its field; `product-not-found` is one sentence above the form. A lost answer says the key may exist but its secret cannot be shown, to revoke it and create another, and offers Reload list; it never opens the dialog and never retries.
5. **Revoke is a medium-tier confirmation, once.** The Revoke button (only on an active key) opens one `ConfirmDialog` that names the key (its label, or its prefix when it has none) and says "Apps using this key will stop working." and "This can't be undone.". Cancel and Esc make no call. Confirm calls `RevokeApiKeyAsync(productId, keyId, CancellationToken.None)` once (`_revokeBusy` plus the dialog's own `Busy`), then marks the row Revoked from the clock and says "Revoked {name}". A failure keeps the dialog open: "Couldn't revoke the key. Nothing was changed. {API message}"; a lost answer says "The revoke may have gone through. Reload the list to check before you try again." and disables Confirm; `api-key-not-found` closes the dialog, says "That key no longer exists." and reloads the list.
6. **The list.** Newest first; label (or "No label"), kind badge (the word, plus `ts-kind--trusted` or `ts-kind--public`), prefix in `code`, created and last-used time ("Never used"), status ("Active" or "Revoked" plus when). A revoked row has no button. No keys is an empty state that explains the two kinds. The page is a thin shell (`ProductKeysPage` = `<AdminOnly><ProductKeysContent/></AdminOnly>`).

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/Support/TestData.ApiKeys.cs`

```csharp
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static ProductApiKeyDto ApiKey(
        string prefix = "tsk_ab12",
        string kind = ApiKeyKinds.Trusted,
        string? label = "Billing server",
        Guid? id = null,
        DateTimeOffset? created = null,
        DateTimeOffset? revoked = null,
        DateTimeOffset? lastUsed = null) => new(id ?? Guid.NewGuid(), OrbitlyId, kind, prefix, label, created ?? Now.AddDays(-3), revoked, lastUsed);
}
```

`tests/TechStrap.Admin.Tests/Support/RecordingLoggerProvider.cs`

```csharp
using Microsoft.Extensions.Logging;

namespace TechStrap.Admin.Tests.Support;

/// <summary>Collects every log line a test run writes (message, exception and structured values), so a test can prove a secret never reached a log.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _lines = [];
    private readonly object _gate = new();

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return [.. _lines];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Recorder(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(string line)
    {
        lock (_gate)
        {
            _lines.Add(line);
        }
    }

    private sealed class Recorder(RecordingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(' ', pairs.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
            owner.Add($"{category} {logLevel} {formatter(state, exception)} {values} {exception}");
        }
    }
}
```

`tests/TechStrap.Admin.Tests/Components/NewApiKeyDialogTests.cs`

```csharp
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 2: the API key plaintext leaks. It is shown once, in the dialog's own field, with a copy button; the dialog ignores Cancel and Esc until "I have stored this key" is ticked; and once it
/// closes the key is gone from the component and from the markup. It is never logged, never in a URL, and appears in the markup exactly once while the dialog is open.
/// </summary>
public sealed class NewApiKeyDialogTests : AdminComponentTest
{
    private const string Secret = "tsk_live_9f8e7d6c5b4a39281706f5e4d3c2b1a0";

    private readonly RecordingLoggerProvider _logs = new();
    private readonly NavigationManager _navigation;
    private BunitJSModuleInterop? _clipboard;
    private int _closed;

    public NewApiKeyDialogTests()
    {
        Services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<NewApiKeyDialog> RenderDialog() =>
        Render<NewApiKeyDialog>(p => p.Add(c => c.ProductName, "Orbitly").Add(c => c.OnClosed, () => _closed++));

    private static async Task ShowAsync(IRenderedComponent<NewApiKeyDialog> cut, string kind = ApiKeyKinds.Trusted, string secret = Secret) =>
        await cut.InvokeAsync(() => cut.Instance.Show(TestData.ApiKey(kind: kind), secret));

    private void SetupClipboard(bool copied)
    {
        _clipboard = JSInterop.SetupModule("./js/clipboard.js");
        _clipboard.Setup<bool>("copyText", _ => true).SetResult(copied);
        _clipboard.SetupVoid("selectText", _ => true).SetVoidResult();
    }

    private static int Occurrences(string markup, string text) => Regex.Matches(markup, Regex.Escape(text)).Count;

    private static AngleSharp.Dom.IElement Done(IRenderedComponent<NewApiKeyDialog> cut) => cut.Find(".ts-dialog-actions button:not(.btn-outline-secondary)");

    private static AngleSharp.Dom.IElement Close(IRenderedComponent<NewApiKeyDialog> cut) => cut.Find(".ts-dialog-actions button.btn-outline-secondary");

    private static void TickStored(IRenderedComponent<NewApiKeyDialog> cut) => cut.Find("#ts-key-stored").Change(true);

    // ---- what it shows --------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_closed_dialog_has_no_key_in_the_markup_and_never_opens_the_script()
    {
        var cut = RenderDialog();

        cut.Markup.ShouldNotContain(Secret);
        cut.FindAll("#ts-key-secret").ShouldBeEmpty();
        Dialogs.VerifyNotInvoke("open");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Showing_a_key_opens_the_dialog_with_the_key_in_a_read_only_field_the_kind_and_the_stored_box_unticked()
    {
        var cut = RenderDialog();

        await ShowAsync(cut, ApiKeyKinds.Public);

        Dialogs.VerifyInvoke("open", 1);
        cut.Find("h2").TextContent.ShouldBe("Your new API key");
        var field = cut.Find("input#ts-key-secret");
        field.GetAttribute("value").ShouldBe(Secret);
        field.HasAttribute("readonly").ShouldBeTrue();
        field.GetAttribute("aria-label").ShouldBe("New API key");
        cut.Find(".ts-kind").TextContent.ShouldBe("Public");
        cut.Find(".ts-key-kind-line").TextContent.ShouldContain("Public: safe to embed in a client app, create-only, rate limited, metadata treated as untrusted");
        cut.Markup.ShouldContain("This is the only time the key for Orbitly is shown.");
        cut.Find("label[for=ts-key-stored]").TextContent.ShouldBe("I have stored this key");
        cut.Find("#ts-key-stored").HasAttribute("checked").ShouldBeFalse();
        Done(cut).HasAttribute("disabled").ShouldBeTrue();
        Occurrences(cut.Markup, Secret).ShouldBe(1, "the key appears once, in the read-only field, and nowhere else");
    }

    [Fact]
    public async Task The_key_is_in_no_url_no_link_and_no_other_attribute()
    {
        var cut = RenderDialog();

        await ShowAsync(cut);

        _navigation.Uri.ShouldNotContain(Secret);
        cut.FindAll("[href]").ShouldAllBe(e => !e.GetAttribute("href")!.Contains(Secret));
        cut.FindAll("*").SelectMany(e => e.Attributes).Where(a => a.Value.Contains(Secret)).Select(a => $"{a.Name}").ShouldBe(["value"]);
    }

    // ---- it cannot be dismissed before "stored" ------------------------------------------------------------------

    [Fact]
    public async Task Esc_and_Cancel_do_nothing_but_say_what_is_missing_until_the_stored_box_is_ticked()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);

        cut.Find("dialog").TriggerEvent("oncancel", EventArgs.Empty);
        Close(cut).Click();
        Done(cut).Click();

        _closed.ShouldBe(0);
        Dialogs.VerifyNotInvoke("close");
        cut.Find("input#ts-key-secret").GetAttribute("value").ShouldBe(Secret);
        cut.Find(".ts-dialog-error[role=alert]").TextContent.ShouldBe("Tick \"I have stored this key\" before you close this window.");
    }

    [Fact]
    public async Task Ticking_the_box_clears_the_hint_and_enables_Done()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);
        Close(cut).Click();
        cut.FindAll(".ts-dialog-error").Count.ShouldBe(1);

        TickStored(cut);

        cut.FindAll(".ts-dialog-error").ShouldBeEmpty();
        Done(cut).HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public async Task Done_after_the_tick_closes_once_clears_the_key_and_leaves_no_trace_in_the_markup()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);
        TickStored(cut);

        Done(cut).Click();

        _closed.ShouldBe(1);
        Dialogs.VerifyInvoke("close", 1);
        cut.Markup.ShouldNotContain(Secret);
        cut.FindAll("#ts-key-secret").ShouldBeEmpty();
        cut.FindAll("#ts-key-stored").ShouldBeEmpty();
        _navigation.Uri.ShouldNotContain(Secret);
    }

    [Fact]
    public async Task Esc_and_Close_work_once_the_box_is_ticked_and_clear_the_key_too()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);
        TickStored(cut);

        cut.Find("dialog").TriggerEvent("oncancel", EventArgs.Empty);

        _closed.ShouldBe(1);
        cut.Markup.ShouldNotContain(Secret);

        await ShowAsync(cut, secret: "tsk_second");
        TickStored(cut);
        Close(cut).Click();
        _closed.ShouldBe(2);
        cut.Markup.ShouldNotContain("tsk_second");
    }

    [Fact]
    public async Task A_second_key_starts_clean_with_the_box_unticked_and_the_first_key_gone()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);
        TickStored(cut);
        Done(cut).Click();

        await ShowAsync(cut, secret: "tsk_second");

        cut.Markup.ShouldNotContain(Secret);
        cut.Find("input#ts-key-secret").GetAttribute("value").ShouldBe("tsk_second");
        cut.Find("#ts-key-stored").HasAttribute("checked").ShouldBeFalse();
        Done(cut).HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task Disposing_the_dialog_drops_the_key()
    {
        var cut = RenderDialog();
        await ShowAsync(cut);

        await cut.Instance.DisposeAsync();

        typeof(NewApiKeyDialog).GetField("_plaintext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(cut.Instance).ShouldBeNull();
    }

    // ---- copy ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Copy_sends_the_key_to_the_clipboard_script_once_and_confirms_without_selecting()
    {
        SetupClipboard(copied: true);
        var cut = RenderDialog();
        await ShowAsync(cut);

        cut.Find(".ts-key-secret button").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-key-copy-status[role=status]").TextContent.ShouldBe("Copied to the clipboard."));
        var clipboard = _clipboard!;
        clipboard.VerifyInvoke("copyText", 1);
        clipboard.Invocations["copyText"].Single().Arguments.ShouldBe([Secret]);
        clipboard.VerifyNotInvoke("selectText");
    }

    [Fact]
    public async Task When_the_browser_refuses_the_copy_the_text_is_selected_and_the_agent_is_told_to_press_Ctrl_C()
    {
        SetupClipboard(copied: false);
        var cut = RenderDialog();
        await ShowAsync(cut);

        cut.Find(".ts-key-secret button").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-key-copy-status").TextContent.ShouldBe("Couldn't copy. The key is selected: press Ctrl+C to copy it."));
        var clipboard = _clipboard!;
        clipboard.VerifyInvoke("selectText", 1);
        // What is selected is the element reference of the key field: the script gets the element itself, never the text a second time.
        var argument = clipboard.Invocations["selectText"].Single().Arguments.ShouldHaveSingleItem();
        argument.ShouldBeOfType<ElementReference>().Id.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_script_that_cannot_run_is_the_same_as_a_refused_copy_and_never_breaks_the_dialog()
    {
        _clipboard = JSInterop.SetupModule("./js/clipboard.js");
        _clipboard.Setup<bool>("copyText", _ => true).SetException(new JSException("clipboard blocked"));
        var cut = RenderDialog();
        await ShowAsync(cut);

        cut.Find(".ts-key-secret button").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-key-copy-status").TextContent.ShouldBe("Couldn't copy. The key is selected: press Ctrl+C to copy it."));
        cut.Find("input#ts-key-secret").GetAttribute("value").ShouldBe(Secret);
    }

    // ---- logs ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_key_is_in_no_log_line_through_a_whole_show_copy_and_close()
    {
        Services.GetRequiredService<ILoggerFactory>().CreateLogger("probe").LogInformation("the sink is wired");
        SetupClipboard(copied: true);
        var cut = RenderDialog();
        await ShowAsync(cut);
        cut.Find(".ts-key-secret button").Click();
        cut.WaitForAssertion(() => cut.Find(".ts-key-copy-status").TextContent.ShouldNotBeEmpty());
        TickStored(cut);
        Done(cut).Click();

        _logs.Lines.ShouldContain(line => line.Contains("the sink is wired"), "the recorder must be wired, or this test proves nothing");
        _logs.Lines.ShouldAllBe(line => !line.Contains(Secret));
    }
}
```

`tests/TechStrap.Admin.Tests/Components/ApiKeysPanelTests.cs`

```csharp
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The key list and the create flow. The plaintext goes straight into the dialog (Review Focus 2) and is in no row, no status message and no other part of the page.</summary>
public sealed class ApiKeysPanelTests : AdminComponentTest
{
    private const string Secret = "tsk_live_0123456789abcdef0123456789abcdef";

    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly Guid _activeId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    public ApiKeysPanelTests()
    {
        _products.ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<ProductApiKeyDto>>(
        [
            TestData.ApiKey("tsk_old1", ApiKeyKinds.Public, "Old widget", created: TestData.Now.AddDays(-30), revoked: TestData.Now.AddDays(-2)),
            TestData.ApiKey("tsk_ab12", ApiKeyKinds.Trusted, "Billing server", _activeId, TestData.Now.AddDays(-3), lastUsed: TestData.Now.AddMinutes(-5)),
            TestData.ApiKey("tsk_cd34", ApiKeyKinds.Public, null, created: TestData.Now.AddDays(-1)),
        ]));
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(new CreateProductApiKeyResponse(TestData.ApiKey("tsk_new9", call.Arg<CreateProductApiKeyRequest>().Kind!, call.Arg<CreateProductApiKeyRequest>().Label), Secret)));
        Services.AddSingleton(_products);
    }

    private IRenderedComponent<ApiKeysPanel> RenderPanel() =>
        Render<ApiKeysPanel>(p => p.Add(c => c.ProductId, TestData.OrbitlyId).Add(c => c.ProductName, "Orbitly"));

    private IReadOnlyList<CreateProductApiKeyRequest> Creates() =>
        [.. _products.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IProductsClient.CreateApiKeyAsync)).Select(c => (CreateProductApiKeyRequest)c.GetArguments()[1]!)];

    private static void Choose(IRenderedComponent<ApiKeysPanel> cut, string kind) => cut.Find("#ts-key-kind").Change(kind);

    private static void Submit(IRenderedComponent<ApiKeysPanel> cut) => cut.Find("form.ts-key-create").Submit();

    // ---- the list -----------------------------------------------------------------------------------------------

    [Fact]
    public void Keys_are_listed_newest_first_with_label_kind_prefix_and_last_used()
    {
        var cut = RenderPanel();

        var rows = cut.FindAll("tbody tr");
        rows.Select(r => r.QuerySelector("code")!.TextContent).ShouldBe(["tsk_cd34", "tsk_ab12", "tsk_old1"]);
        rows[0].Children[0].TextContent.ShouldBe("No label");
        rows[1].Children[0].TextContent.ShouldBe("Billing server");
        rows[1].Children[4].TextContent.ShouldBe("5 min ago");
        rows[0].Children[4].TextContent.Trim().ShouldBe("Never used");
    }

    [Fact]
    public void Trusted_and_public_badges_are_distinct_in_words_and_in_class()
    {
        var cut = RenderPanel();

        var badges = cut.FindAll("tbody .ts-kind").Select(b => (b.TextContent, b.ClassName)).ToList();
        badges.ShouldContain(("Trusted", "ts-kind ts-kind--trusted"));
        badges.ShouldContain(("Public", "ts-kind ts-kind--public"));
    }

    [Fact]
    public void A_revoked_key_renders_as_revoked_with_no_revoke_button_and_an_active_one_has_one()
    {
        var cut = RenderPanel();

        var revoked = cut.Find("tr.ts-key--revoked");
        revoked.QuerySelector("code")!.TextContent.ShouldBe("tsk_old1");
        revoked.QuerySelector(".ts-pill")!.TextContent.ShouldBe("Revoked");
        revoked.QuerySelectorAll("button").ShouldBeEmpty();
        cut.FindAll("tbody tr:not(.ts-key--revoked) button.ts-revoke").Count.ShouldBe(2);
        cut.FindAll("tbody tr:not(.ts-key--revoked) .ts-pill").ShouldAllBe(p => p.TextContent == "Active");
    }

    [Fact]
    public void No_keys_is_an_empty_state_that_explains_the_two_kinds()
    {
        _products.ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductApiKeyDto>>([]));

        var cut = RenderPanel();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No API keys yet");
        var empty = cut.Find(".ts-state--empty");
        empty.TextContent.ShouldContain("Trusted: server-side only, may set external user ref and trusted metadata");
        empty.TextContent.ShouldContain("Public: safe to embed in a client app, create-only, rate limited, metadata treated as untrusted");
        cut.FindAll("table").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _products.ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<IReadOnlyList<ProductApiKeyDto>>("api-error", "The API is unavailable."),
            TestData.Ok<IReadOnlyList<ProductApiKeyDto>>([TestData.ApiKey()]));
        var cut = RenderPanel();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the API keys. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void The_list_never_holds_more_than_the_prefix()
    {
        var cut = RenderPanel();

        cut.Markup.ShouldNotContain("tsk_live");
        cut.FindAll("dialog input#ts-key-secret").ShouldBeEmpty();
    }

    // ---- creating -----------------------------------------------------------------------------------------------

    [Fact]
    public void A_kind_must_be_chosen_and_both_kinds_are_explained_beside_the_choice()
    {
        var cut = RenderPanel();

        cut.Find("form.ts-key-create button[type=submit]").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#ts-key-kind").QuerySelectorAll("option").Select(o => o.TextContent).ShouldBe(["Choose a kind", "Trusted", "Public"]);
        cut.Find("form.ts-key-create .ts-kind-explained").TextContent.ShouldContain("Trusted: server-side only");
        Submit(cut);
        Creates().ShouldBeEmpty();
        cut.Find("#ts-key-kind-error").TextContent.ShouldBe("Choose a kind.");
    }

    [Fact]
    public void Creating_a_key_sends_the_kind_and_label_through_the_write_path_and_opens_the_dialog_with_the_key()
    {
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Trusted);
        cut.Find("#ts-key-label").Input("  CI server  ");

        Submit(cut);

        Creates().ShouldHaveSingleItem().ShouldBe(new CreateProductApiKeyRequest(ApiKeyKinds.Trusted, "CI server"));
        _products.Received(1).CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        cut.Find("dialog input#ts-key-secret").GetAttribute("value").ShouldBe(Secret);
        Dialogs.VerifyInvoke("open", 1);
        cut.FindAll("tbody tr").Count.ShouldBe(4);
        cut.FindAll("tbody code").Select(c => c.TextContent).ShouldContain("tsk_new9");
        StatusMessages.Current.ShouldBe("Created a Trusted key for Orbitly");
        cut.Find("#ts-key-label").GetAttribute("value").ShouldBe(string.Empty);
    }

    [Fact]
    public void The_plaintext_is_in_the_dialog_field_only_and_not_in_the_status_a_row_or_any_other_attribute()
    {
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);

        Submit(cut);

        Regex.Matches(cut.Markup, Regex.Escape(Secret)).Count.ShouldBe(1);
        StatusMessages.Current!.ShouldNotContain(Secret);
        cut.FindAll("tbody").Single().TextContent.ShouldNotContain(Secret);
    }

    [Fact]
    public void A_blank_label_goes_as_null_and_an_over_long_one_is_refused_before_it_is_sent()
    {
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);
        cut.Find("#ts-key-label").Input("   ");
        Submit(cut);
        Creates().ShouldHaveSingleItem().Label.ShouldBeNull();

        cut.Find("#ts-key-label").Input(new string('x', 101));
        Submit(cut);

        cut.Find("#ts-key-label-error").TextContent.ShouldBe("Use 100 characters or fewer.");
        Creates().Count.ShouldBe(1);
    }

    [Fact]
    public void A_double_submit_creates_one_key()
    {
        var gate = new TaskCompletionSource<Result<CreateProductApiKeyResponse>>();
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);

        Submit(cut);
        cut.Find("fieldset").HasAttribute("disabled").ShouldBeTrue();
        Submit(cut);

        Creates().Count.ShouldBe(1);
        gate.SetResult(TestData.Ok(new CreateProductApiKeyResponse(TestData.ApiKey("tsk_new9"), Secret)));
        cut.WaitForAssertion(() => cut.Find("dialog input#ts-key-secret").GetAttribute("value").ShouldBe(Secret));
    }

    [Theory]
    [InlineData(ApiErrorCodes.ApiKeyKindInvalid, "kind", "#ts-key-kind-error")]
    [InlineData("label-too-long", "label", "#ts-key-label-error")]
    public void A_field_error_from_the_api_appears_at_its_field_and_no_dialog_opens(string code, string target, string errorSelector)
    {
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<CreateProductApiKeyResponse>.Failure(new ResultError(code, "The server says no.", ResultErrorKind.Validation, target)));
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);

        Submit(cut);

        cut.Find(errorSelector).TextContent.ShouldBe("The server says no.");
        Dialogs.VerifyNotInvoke("open");
        cut.FindAll("input#ts-key-secret").ShouldBeEmpty();
    }

    [Fact]
    public void A_product_that_is_gone_is_said_so_and_no_dialog_opens()
    {
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<CreateProductApiKeyResponse>(ApiErrorCodes.ProductNotFound, "No such product.", ResultErrorKind.NotFound));
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);

        Submit(cut);

        cut.Find("p.ts-form-error[role=alert]").TextContent.ShouldBe("This product no longer exists.");
        Dialogs.VerifyNotInvoke("open");
    }

    [Fact]
    public void A_lost_answer_says_the_key_may_exist_but_its_secret_cannot_be_shown_and_never_offers_a_bare_retry()
    {
        _products.CreateApiKeyAsync(TestData.OrbitlyId, Arg.Any<CreateProductApiKeyRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<CreateProductApiKeyResponse>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPanel();
        Choose(cut, ApiKeyKinds.Public);

        Submit(cut);

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("The key may have been created");
        alert.TextContent.ShouldContain("revoke it and create another");
        alert.TextContent.ShouldNotContain("Try again");
        Dialogs.VerifyNotInvoke("open");
        Creates().Count.ShouldBe(1, "a write is never retried");

        alert.QuerySelector("button")!.Click();

        _products.Received(2).ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>());
        cut.FindAll(".ts-conflict").ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/RevokeApiKeyTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Tests.Components;

/// <summary>Review Focus 5 for API keys: a revoke never fires without its confirmation and never fires twice. The confirmation names the key and says apps using it will stop working.</summary>
public sealed class RevokeApiKeyTests : AdminComponentTest
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly Guid _billingId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private readonly Guid _widgetId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002");

    public RevokeApiKeyTests()
    {
        _products.ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<ProductApiKeyDto>>(
        [
            TestData.ApiKey("tsk_ab12", ApiKeyKinds.Trusted, "Billing server", _billingId, TestData.Now.AddDays(-3)),
            TestData.ApiKey("tsk_cd34", ApiKeyKinds.Public, null, _widgetId, TestData.Now.AddDays(-2)),
        ]));
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok());
        Services.AddSingleton(_products);
    }

    private IRenderedComponent<ApiKeysPanel> RenderPanel() =>
        Render<ApiKeysPanel>(p => p.Add(c => c.ProductId, TestData.OrbitlyId).Add(c => c.ProductName, "Orbitly"));

    private int Revokes() => _products.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IProductsClient.RevokeApiKeyAsync));

    private static void AskToRevoke(IRenderedComponent<ApiKeysPanel> cut, string prefix) =>
        cut.FindAll("tbody tr").Single(r => r.QuerySelector("code")!.TextContent == prefix).QuerySelector("button.ts-revoke")!.Click();

    private static AngleSharp.Dom.IElement RevokeDialog(IRenderedComponent<ApiKeysPanel> cut) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.StartsWith("Revoke ", StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<ApiKeysPanel> cut) =>
        RevokeDialog(cut).QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    [Fact]
    public void Revoke_asks_first_naming_the_key_and_saying_apps_using_it_will_stop_working_and_calls_nothing()
    {
        var cut = RenderPanel();

        AskToRevoke(cut, "tsk_ab12");

        var dialog = RevokeDialog(cut);
        dialog.QuerySelector("h2")!.TextContent.ShouldBe("Revoke Billing server?");
        dialog.TextContent.ShouldContain("Billing server (tsk_ab12), a Trusted key for Orbitly.");
        dialog.TextContent.ShouldContain("Apps using this key will stop working.");
        dialog.TextContent.ShouldContain("This can't be undone.");
        dialog.QuerySelector("input").ShouldBeNull("a medium confirmation: no typed text");
        Confirm(cut).TextContent.ShouldBe("Revoke key");
        Confirm(cut).ClassName!.ShouldContain("btn-danger");
        Dialogs.VerifyInvoke("open", 1);
        Revokes().ShouldBe(0);
    }

    [Fact]
    public void A_key_without_a_label_is_named_by_its_prefix()
    {
        var cut = RenderPanel();

        AskToRevoke(cut, "tsk_cd34");

        RevokeDialog(cut).QuerySelector("h2")!.TextContent.ShouldBe("Revoke tsk_cd34?");
    }

    [Fact]
    public void Cancel_and_Esc_close_the_confirmation_and_make_no_call()
    {
        var cut = RenderPanel();

        AskToRevoke(cut, "tsk_ab12");
        RevokeDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        AskToRevoke(cut, "tsk_ab12");
        RevokeDialog(cut).TriggerEvent("oncancel", EventArgs.Empty);

        Revokes().ShouldBe(0);
        Dialogs.VerifyInvoke("close", 2);
        cut.FindAll("tr.ts-key--revoked").ShouldBeEmpty();
    }

    [Fact]
    public void Confirming_revokes_that_key_once_with_no_cancellation_marks_the_row_revoked_and_says_so()
    {
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");

        Confirm(cut).Click();

        _products.Received(1).RevokeApiKeyAsync(TestData.OrbitlyId, _billingId, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Revokes().ShouldBe(1);
        var row = cut.Find("tr.ts-key--revoked");
        row.QuerySelector("code")!.TextContent.ShouldBe("tsk_ab12");
        row.QuerySelector(".ts-pill")!.TextContent.ShouldBe("Revoked");
        row.QuerySelectorAll("button").ShouldBeEmpty();
        StatusMessages.Current.ShouldBe("Revoked Billing server");
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_revoked_key_cannot_be_revoked_again_from_the_list()
    {
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");
        Confirm(cut).Click();

        cut.FindAll("tbody tr").Single(r => r.QuerySelector("code")!.TextContent == "tsk_ab12").QuerySelectorAll("button.ts-revoke").ShouldBeEmpty();
        Revokes().ShouldBe(1);
    }

    [Fact]
    public void A_double_click_on_confirm_revokes_once_and_the_dialog_is_inert_while_it_runs()
    {
        var gate = new TaskCompletionSource<Result>();
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");

        Confirm(cut).Click();
        var dialog = RevokeDialog(cut);
        dialog.QuerySelectorAll("button").ShouldAllBe(b => b.HasAttribute("disabled"));
        dialog.TriggerEvent("oncancel", EventArgs.Empty);
        Confirm(cut).Click();

        Revokes().ShouldBe(1);
        Dialogs.VerifyNotInvoke("close");
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => cut.FindAll("tr.ts-key--revoked").Count.ShouldBe(1));
    }

    [Fact]
    public void Choosing_a_different_row_names_that_row_and_revokes_that_one()
    {
        var cut = RenderPanel();

        AskToRevoke(cut, "tsk_ab12");
        RevokeDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        AskToRevoke(cut, "tsk_cd34");
        Confirm(cut).Click();

        _products.Received(1).RevokeApiKeyAsync(TestData.OrbitlyId, _widgetId, Arg.Any<CancellationToken>());
        _products.DidNotReceive().RevokeApiKeyAsync(TestData.OrbitlyId, _billingId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_failed_revoke_keeps_the_dialog_open_says_nothing_changed_and_leaves_the_row_active()
    {
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("boom", "The API refused."));
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");

        Confirm(cut).Click();

        RevokeDialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't revoke the key. Nothing was changed. The API refused.");
        cut.FindAll("tr.ts-key--revoked").ShouldBeEmpty();
        Dialogs.VerifyNotInvoke("close");
        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public void A_lost_answer_never_claims_nothing_changed_blocks_a_second_revoke_and_offers_a_reload()
    {
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");
        Confirm(cut).Click();

        var dialog = RevokeDialog(cut);
        dialog.QuerySelector("[role=alert]")!.TextContent.ShouldBe("The revoke may have gone through. Reload the list to check before you try again.");
        dialog.TextContent.ShouldNotContain("Nothing was changed");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Revokes().ShouldBe(1);

        RevokeDialog(cut).QuerySelector(".ts-dialog-recovery button")!.Click();

        _products.Received(2).ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_key_that_is_already_gone_closes_the_dialog_says_so_and_reloads_the_list()
    {
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail(ApiErrorCodes.ApiKeyNotFound, "No such key.", ResultErrorKind.NotFound));
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");

        Confirm(cut).Click();

        StatusMessages.Current.ShouldBe("That key no longer exists.");
        _products.Received(2).ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_revoke_that_finishes_after_the_panel_is_gone_is_not_cancelled_and_reports_nothing()
    {
        var gate = new TaskCompletionSource<Result>();
        _products.RevokeApiKeyAsync(TestData.OrbitlyId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPanel();
        AskToRevoke(cut, "tsk_ab12");
        Confirm(cut).Click();

        cut.Instance.Dispose();
        gate.SetResult(TestData.Ok());

        _products.Received(1).RevokeApiKeyAsync(TestData.OrbitlyId, _billingId, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        StatusMessages.Current.ShouldBeNull();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/ProductKeysPageTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Products;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

public sealed class ProductKeysPageTests : AdminPageTest
{
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();

    public ProductKeysPageTests()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.ProductDetail()));
        _products.ListApiKeysAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductApiKeyDto>>([TestData.ApiKey()]));
        Services.AddSingleton(_products);
    }

    private IRenderedComponent<ProductKeysPage> RenderPage() => Render<ProductKeysPage>(p => p.Add(c => c.Id, TestData.OrbitlyId));

    [Fact]
    public void An_admin_sees_the_product_name_a_link_back_and_its_keys()
    {
        var cut = RenderPage();

        cut.Find("h1").TextContent.ShouldBe("Orbitly");
        cut.Find(".ts-settings-back a").GetAttribute("href").ShouldBe("/settings/products");
        cut.Find(".ts-settings-head a").GetAttribute("href").ShouldBe($"/settings/products/{TestData.OrbitlyId}");
        cut.FindAll("tbody tr").Count.ShouldBe(1);
        cut.Find("form.ts-key-create").ShouldNotBeNull();
    }

    [Fact]
    public void A_product_the_api_does_not_know_says_so_and_asks_for_no_keys()
    {
        _products.GetAsync(TestData.OrbitlyId, Arg.Any<CancellationToken>()).Returns(TestData.Fail<ProductDto>(ApiErrorCodes.ProductNotFound, "No such product.", ResultErrorKind.NotFound));

        var cut = RenderPage();

        cut.Find("[role=alert]").TextContent.ShouldContain("This product no longer exists.");
        cut.FindAll("form.ts-key-create").ShouldBeEmpty();
        _products.DidNotReceive().ListApiKeysAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_nothing_is_asked_of_the_api()
    {
        AsAgent();

        var cut = RenderPage();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("form.ts-key-create").ShouldBeEmpty();
        cut.Markup.ShouldNotContain("tsk_");
        _products.ReceivedCalls().ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Admin.Tests/js/clipboard.test.mjs`

```javascript
// Runs with `node --test` (no browser): copyText and selectText take the navigator and the element as arguments.
import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { copyText, selectText } from '../../../src/TechStrap.Admin/wwwroot/js/clipboard.js';

describe('copyText', () => {
    it('writes the text to the clipboard and answers true', async () => {
        const written = [];
        const nav = { clipboard: { writeText: async (text) => { written.push(text); } } };

        assert.equal(await copyText('tsk_secret', nav), true);
        assert.deepEqual(written, ['tsk_secret']);
    });

    it('answers false, and does not throw, when the browser refuses', async () => {
        const nav = { clipboard: { writeText: async () => { throw new Error('NotAllowedError'); } } };

        assert.equal(await copyText('tsk_secret', nav), false);
    });

    it('answers false when there is no clipboard (an insecure page) or no navigator', async () => {
        assert.equal(await copyText('x', {}), false);
        assert.equal(await copyText('x', { clipboard: {} }), false);
        assert.equal(await copyText('x', null), false);
    });
});

describe('selectText', () => {
    it('focuses and selects the whole value', () => {
        const calls = [];
        const element = {
            value: 'tsk_secret',
            focus: () => calls.push('focus'),
            select: () => calls.push('select'),
            setSelectionRange: (start, end) => calls.push(`range ${start}-${end}`),
        };

        assert.equal(selectText(element), true);
        assert.deepEqual(calls, ['focus', 'select', 'range 0-10']);
    });

    it('answers false, and does not throw, for a missing or unusable element', () => {
        assert.equal(selectText(null), false);
        assert.equal(selectText({ focus: () => { throw new Error('detached'); } }), false);
    });
});
```


`tests/TechStrap.Admin.Tests/ScriptHostTests.cs` serves the new module:

```diff
@@
     [InlineData("/js/queue.js")]
+    [InlineData("/js/clipboard.js")]
     public async Task Module_scripts_are_served_as_javascript_without_signing_in(string path)
```

`scripts/tests/AdminScripts.Tests.ps1` gets a second `It` for the node tests (Task 4 added the one for `preferences.js`; add this one after the last `It` inside the same `Describe`):

```powershell
    It 'passes the node:test suite for the clipboard helpers' {
        if (-not $script:Node) {
            Set-ItResult -Skipped -Because 'node is not installed, so the clipboard.js tests cannot run'
            return
        }

        $testFile = Join-Path $script:RepoRoot 'tests/TechStrap.Admin.Tests/js/clipboard.test.mjs'

        $output = & node --test $testFile 2>&1 | Out-String

        $LASTEXITCODE | Should -Be 0 -Because $output
    }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "NewApiKeyDialogTests|ApiKeysPanelTests|RevokeApiKeyTests|ProductKeysPageTests|ScriptHostTests"` and `node --test tests/TechStrap.Admin.Tests/js/clipboard.test.mjs`
Expected: the dotnet project does not build (`NewApiKeyDialog`, `ApiKeysPanel`, `ProductKeysPage` and `ApiKeysCopy` do not exist); node fails with `ERR_MODULE_NOT_FOUND` (no `clipboard.js`).

- [ ] **Step 3: Implement**

1. The script:

`src/TechStrap.Admin/wwwroot/js/clipboard.js`

```javascript
// Copies the one-time API key to the clipboard (the key is never put anywhere else) and, when the browser refuses, selects the text so the agent can press Ctrl+C.
// Both functions take what they touch as arguments and never throw: a refused clipboard is an ordinary answer ("false"), not an error.

export async function copyText(text, nav = globalThis.navigator) {
    try {
        if (!nav || !nav.clipboard || typeof nav.clipboard.writeText !== 'function') {
            return false;
        }

        await nav.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}

export function selectText(element) {
    try {
        if (!element) {
            return false;
        }

        element.focus();
        element.select();
        if (typeof element.setSelectionRange === 'function') {
            element.setSelectionRange(0, (element.value ?? '').length);
        }

        return true;
    } catch {
        return false;
    }
}
```

2. The copy and the row:

`src/TechStrap.Admin/Features/Settings/Products/ApiKeysCopy.cs`

```csharp
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>The copy of the API keys panel, the new-key dialog and the revoke confirmation. The two kind explanations come from UX-BRIEF-admin word for word.</summary>
public static class ApiKeysCopy
{
    public const string Heading = "API keys";
    public const string Loading = "Loading API keys";
    public const string LoadFailed = "Couldn't load the API keys.";
    public const string NoKeys = "No API keys yet";
    public const string NoKeysHint = "An app needs a key to create tickets for this product. Choose the kind that fits where the key will live:";
    public const string ProductGone = "This product no longer exists.";

    public const string TrustedLine = "Trusted: server-side only, may set external user ref and trusted metadata";
    public const string PublicLine = "Public: safe to embed in a client app, create-only, rate limited, metadata treated as untrusted";

    public const string KindLabel = "Kind";
    public const string KindPlaceholder = "Choose a kind";
    public const string KindRequired = "Choose a kind.";
    public const string LabelLabel = "Label (optional)";
    public const string LabelHelp = "Helps you tell keys apart, such as the app that uses it. Up to 100 characters.";
    public const string LabelTooLong = "Use 100 characters or fewer.";
    public const string Create = "Create key";
    public const string Creating = "Creating\u2026";

    public const string ColumnLabel = "Label";
    public const string ColumnKind = "Kind";
    public const string ColumnKey = "Key";
    public const string ColumnCreated = "Created";
    public const string ColumnLastUsed = "Last used";
    public const string ColumnStatus = "Status";
    public const string NoLabel = "No label";
    public const string NeverUsed = "Never used";
    public const string Active = "Active";
    public const string Revoked = "Revoked";
    public const string Revoke = "Revoke";

    public const string NewKeyTitle = "Your new API key";
    public const string SecretLabel = "New API key";
    public const string Copy = "Copy";
    public const string Copied = "Copied to the clipboard.";
    public const string CopyFailed = "Couldn't copy. The key is selected: press Ctrl+C to copy it.";
    public const string Stored = "I have stored this key";
    public const string StoreFirst = "Tick \"I have stored this key\" before you close this window.";
    public const string Done = "Done";

    public const string RevokeBody = "Apps using this key will stop working.";
    public const string RevokeIrreversible = "This can't be undone.";
    public const string RevokeConfirm = "Revoke key";
    public const string RevokeUncertain = "The revoke may have gone through. Reload the list to check before you try again.";
    public const string CreateUncertain = "The key may have been created, but if the answer was lost its secret can't be shown. Reload the list. If you see a key you never copied, revoke it and create another.";
    public const string ReloadList = "Reload list";
    public const string KeyGone = "That key no longer exists.";

    public static string Kind(string kind) => kind == ApiKeyKinds.Trusted ? TrustedLine : PublicLine;

    public static string ShownOnce(string product) =>
        $"This is the only time the key for {product} is shown. Copy it and keep it somewhere safe: TechStrap can't show it again.";

    public static string RevokeTitle(string name) => $"Revoke {name}?";

    public static string RevokeSubject(string name, string prefix, string kind, string product) => $"{name} ({prefix}), a {kind} key for {product}.";

    public static string RevokeFailed(string reason) => $"Couldn't revoke the key. Nothing was changed. {reason}";

    public static string RevokedMessage(string name) => $"Revoked {name}";

    public static string CreatedMessage(string kind, string product) => $"Created a {kind} key for {product}";
}
```

`src/TechStrap.Admin/Features/Settings/Products/ApiKeyRowViewModel.cs`

```csharp
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>One row of the key list: the API sends only the label and the prefix, never the key, and this never holds more.</summary>
internal sealed record ApiKeyRowViewModel(Guid Id, string Kind, string KeyPrefix, string? Label, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt, DateTimeOffset? LastUsedAt)
{
    public bool IsRevoked => RevokedAt is not null;

    /// <summary>What the key is called in a sentence: its label, or its prefix when it has none.</summary>
    public string Name => string.IsNullOrWhiteSpace(Label) ? KeyPrefix : Label;

    public static ApiKeyRowViewModel From(ProductApiKeyDto key) =>
        new(key.Id, key.Kind, key.KeyPrefix, key.Label, key.CreatedAt, key.RevokedAt, key.LastUsedAt);
}
```

`src/TechStrap.Admin/Features/Settings/Products/ApiKeyKindBadge.razor`

```razor
<span class="@("ts-kind ts-kind--" + Kind.ToLowerInvariant())">@Kind</span>

@code {
    /// <summary>Trusted or Public. Always shown as the word; the style only repeats it.</summary>
    [Parameter, EditorRequired]
    public string Kind { get; set; } = string.Empty;
}
```

3. The dialog:

`src/TechStrap.Admin/Features/Settings/Products/NewApiKeyDialog.razor`

```razor
@implements IAsyncDisposable

<ConfirmDialog Open="_open" Title="@ApiKeysCopy.NewKeyTitle" ConfirmLabel="@ApiKeysCopy.Done" CancelLabel="@ShellCopy.Close"
               ConfirmDisabled="@(!_stored)" Error="@_hint" OnConfirm="CloseAsync" OnCancel="TryCloseAsync">
    @if (_plaintext is not null)
    {
        <p>@ApiKeysCopy.ShownOnce(ProductName)</p>
        <p class="ts-key-kind-line"><ApiKeyKindBadge Kind="@(_kind ?? string.Empty)" /> @ApiKeysCopy.Kind(_kind ?? string.Empty)</p>
        <div class="ts-key-secret">
            <input @ref="_secret" id="ts-key-secret" class="form-control ts-key-secret-input" type="text" readonly value="@_plaintext"
                   aria-label="@ApiKeysCopy.SecretLabel" autocomplete="off" spellcheck="false" />
            <button type="button" class="btn btn-outline-secondary" @onclick="CopyAsync">@ApiKeysCopy.Copy</button>
        </div>
        <p class="ts-key-copy-status" role="status">@_copyStatus</p>
        <div class="form-check">
            <input id="ts-key-stored" class="form-check-input" type="checkbox" checked="@_stored" @onchange="OnStoredChanged" />
            <label class="form-check-label" for="ts-key-stored">@ApiKeysCopy.Stored</label>
        </div>
    }
</ConfirmDialog>
```

`src/TechStrap.Admin/Features/Settings/Products/NewApiKeyDialog.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>
/// Shows a new API key once. The plaintext lives only in this component's field, from <see cref="Show"/> until the dialog closes; it is never logged, never put in a URL or an
/// attribute other than the read-only field's value, and the field is not rendered at all once the dialog is closed. Cancel and Esc do nothing but say what is missing until the agent ticks
/// "I have stored this key": the dialog cannot be dismissed by accident, and a dismissal after the tick clears the key. Copying goes to the clipboard through <c>clipboard.js</c>; when the
/// browser refuses (or the script cannot run) the text is selected so the agent can press Ctrl+C.
/// </summary>
public sealed partial class NewApiKeyDialog : IAsyncDisposable
{
    private const string ModulePath = "./js/clipboard.js";

    private ElementReference _secret;
    private IJSObjectReference? _module;
    private string? _plaintext;
    private string? _kind;
    private string? _hint;
    private string? _copyStatus;
    private bool _open;
    private bool _stored;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    /// <summary>The product's name, for the sentence that says the key is shown only once.</summary>
    [Parameter]
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Raised once, after the dialog has closed and the key has been cleared.</summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>Opens the dialog with a freshly created key. The caller does not keep the plaintext.</summary>
    public void Show(ProductApiKeyDto key, string plaintext)
    {
        _plaintext = plaintext;
        _kind = key.Kind;
        _stored = false;
        _hint = null;
        _copyStatus = null;
        _open = true;
        StateHasChanged();
    }

    private void OnStoredChanged(ChangeEventArgs e)
    {
        _stored = e.Value is true;
        if (_stored)
        {
            _hint = null;
        }
    }

    private async Task TryCloseAsync()
    {
        if (!_stored)
        {
            _hint = ApiKeysCopy.StoreFirst;
            return;
        }

        await CloseAsync();
    }

    private async Task CloseAsync()
    {
        if (!_stored)
        {
            return;
        }

        Clear();
        await OnClosed.InvokeAsync();
    }

    private void Clear()
    {
        _plaintext = null;
        _kind = null;
        _hint = null;
        _copyStatus = null;
        _stored = false;
        _open = false;
    }

    private async Task CopyAsync()
    {
        if (_plaintext is null)
        {
            return;
        }

        var copied = false;
        try
        {
            _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            copied = await _module.InvokeAsync<bool>("copyText", _plaintext);
            if (!copied)
            {
                await _module.InvokeVoidAsync("selectText", _secret);
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException)
        {
            // The script could not run: the key is still on screen in a read-only field the agent can select by hand.
        }

        _copyStatus = copied ? ApiKeysCopy.Copied : ApiKeysCopy.CopyFailed;
    }

    public async ValueTask DisposeAsync()
    {
        _plaintext = null;
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone.
        }
    }
}
```

4. The panel:

`src/TechStrap.Admin/Features/Settings/Products/ApiKeysPanel.razor`

```razor
@using TechStrap.Contracts.ApiKeys
<section class="ts-keys" aria-labelledby="ts-keys-heading">
    <h2 id="ts-keys-heading">@ApiKeysCopy.Heading</h2>

    <form class="ts-key-create ts-form" novalidate @onsubmit="CreateAsync">
        <fieldset disabled="@_busy">
            <div class="ts-field">
                <label for="ts-key-kind" class="form-label">@ApiKeysCopy.KindLabel</label>
                <select id="ts-key-kind" class="form-select @(_kindError is null ? null : "is-invalid")" @onchange="OnKindChanged">
                    <option value="" selected="@(_kind.Length == 0)">@ApiKeysCopy.KindPlaceholder</option>
                    <option value="@ApiKeyKinds.Trusted" selected="@(_kind == ApiKeyKinds.Trusted)">@ApiKeyKinds.Trusted</option>
                    <option value="@ApiKeyKinds.Public" selected="@(_kind == ApiKeyKinds.Public)">@ApiKeyKinds.Public</option>
                </select>
                <ul class="ts-kind-explained">
                    <li>@ApiKeysCopy.TrustedLine</li>
                    <li>@ApiKeysCopy.PublicLine</li>
                </ul>
                @if (_kindError is not null)
                {
                    <p id="ts-key-kind-error" class="ts-field-error" role="alert">@_kindError</p>
                }
            </div>
            <div class="ts-field">
                <label for="ts-key-label" class="form-label">@ApiKeysCopy.LabelLabel</label>
                <input id="ts-key-label" type="text" class="form-control @(_labelError is null ? null : "is-invalid")" value="@_label" autocomplete="off" @oninput="OnLabelInput" />
                <p class="ts-field-help">@ApiKeysCopy.LabelHelp</p>
                @if (_labelError is not null)
                {
                    <p id="ts-key-label-error" class="ts-field-error" role="alert">@_labelError</p>
                }
            </div>
        </fieldset>
        <div class="ts-form-actions">
            <button type="submit" class="btn btn-primary" disabled="@(_busy || _kind.Length == 0)">@(_busy ? ApiKeysCopy.Creating : ApiKeysCopy.Create)</button>
        </div>
    </form>

    @if (_formError is not null)
    {
        <p class="ts-form-error" role="alert">@_formError</p>
    }
    @if (_createUncertain)
    {
        <div class="ts-conflict" role="alert">
            <p>@ApiKeysCopy.CreateUncertain</p>
            <button type="button" class="btn btn-outline-secondary" @onclick="ReloadAsync">@ApiKeysCopy.ReloadList</button>
        </div>
    }

    @if (_loading)
    {
        <LoadingState Rows="3" Label="@ApiKeysCopy.Loading" />
    }
    else if (_loadError is not null)
    {
        <ErrorState Message="@_loadError" OnRetry="ReloadAsync" />
    }
    else if (_rows.Count == 0)
    {
        <EmptyState Heading="@ApiKeysCopy.NoKeys">
            <p>@ApiKeysCopy.NoKeysHint</p>
            <ul class="ts-kind-explained">
                <li>@ApiKeysCopy.TrustedLine</li>
                <li>@ApiKeysCopy.PublicLine</li>
            </ul>
        </EmptyState>
    }
    else
    {
        <div class="table-responsive">
            <table class="table ts-ledger ts-settings-table ts-key-table">
                <thead>
                    <tr>
                        <th scope="col">@ApiKeysCopy.ColumnLabel</th>
                        <th scope="col">@ApiKeysCopy.ColumnKind</th>
                        <th scope="col">@ApiKeysCopy.ColumnKey</th>
                        <th scope="col">@ApiKeysCopy.ColumnCreated</th>
                        <th scope="col">@ApiKeysCopy.ColumnLastUsed</th>
                        <th scope="col">@ApiKeysCopy.ColumnStatus</th>
                        <th scope="col"><span class="visually-hidden">Actions</span></th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var row in _rows)
                    {
                        <tr @key="row.Id" class="@(row.IsRevoked ? "ts-key--revoked" : null)" data-key-id="@row.Id">
                            <td>@(string.IsNullOrWhiteSpace(row.Label) ? ApiKeysCopy.NoLabel : row.Label)</td>
                            <td><ApiKeyKindBadge Kind="@row.Kind" /></td>
                            <td><code>@row.KeyPrefix</code></td>
                            <td><RelativeTime When="row.CreatedAt" /></td>
                            <td>
                                @if (row.LastUsedAt is { } used)
                                {
                                    <RelativeTime When="used" />
                                }
                                else
                                {
                                    @ApiKeysCopy.NeverUsed
                                }
                            </td>
                            <td>
                                @if (row.RevokedAt is { } revoked)
                                {
                                    <span class="ts-pill ts-pill--off">@ApiKeysCopy.Revoked</span>
                                    <RelativeTime When="revoked" />
                                }
                                else
                                {
                                    <span class="ts-pill ts-pill--on">@ApiKeysCopy.Active</span>
                                }
                            </td>
                            <td class="ts-settings-actions">
                                @if (!row.IsRevoked)
                                {
                                    <button type="button" class="btn btn-outline-secondary ts-revoke" @onclick="() => AskRevoke(row)">@ApiKeysCopy.Revoke</button>
                                }
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    }

    <ConfirmDialog Open="@(_revoking is not null)" Title="@(_revoking is null ? string.Empty : ApiKeysCopy.RevokeTitle(_revoking.Name))" ConfirmLabel="@ApiKeysCopy.RevokeConfirm" Danger="true"
                   Busy="_revokeBusy" ConfirmDisabled="_revokeUncertain" Error="@_revokeError" OnConfirm="ConfirmRevokeAsync" OnCancel="CancelRevoke">
        @if (_revoking is not null)
        {
            <p>@ApiKeysCopy.RevokeSubject(_revoking.Name, _revoking.KeyPrefix, _revoking.Kind, ProductName)</p>
            <p>@ApiKeysCopy.RevokeBody @ApiKeysCopy.RevokeIrreversible</p>
            @if (_revokeUncertain)
            {
                <div class="ts-dialog-recovery">
                    <button type="button" class="btn btn-link" @onclick="ReloadAfterUncertainAsync">@ApiKeysCopy.ReloadList</button>
                </div>
            }
        }
    </ConfirmDialog>

    <NewApiKeyDialog @ref="_dialog" ProductName="@ProductName" />
</section>
```

`src/TechStrap.Admin/Features/Settings/Products/ApiKeysPanel.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.ApiKeys;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>
/// The keys of one product: the list, the create form and the revoke confirmation. Creating a key hands its plaintext straight to <see cref="NewApiKeyDialog"/> and keeps nothing; the list shows the label,
/// the kind as a badge (the word, plus a border style) and the prefix only. Revoke is a medium-tier confirmation that says apps using the key will stop working. Writes use
/// <see cref="CancellationToken.None"/> and are never retried: an unknown outcome says so and offers a reload, never a bare retry, and a lost create answer says its secret cannot be shown.
/// </summary>
public sealed partial class ApiKeysPanel : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private NewApiKeyDialog? _dialog;
    private IReadOnlyList<ApiKeyRowViewModel> _rows = [];
    private ApiKeyRowViewModel? _revoking;
    private string _kind = string.Empty;
    private string _label = string.Empty;
    private string? _loadError;
    private string? _formError;
    private string? _kindError;
    private string? _labelError;
    private string? _revokeError;
    private bool _loading = true;
    private bool _busy;
    private bool _createUncertain;
    private bool _revokeBusy;
    private bool _revokeUncertain;
    private bool _disposed;
    private Guid _loadedFor;

    [Inject]
    private IProductsClient Products { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Parameter, EditorRequired]
    public Guid ProductId { get; set; }

    [Parameter, EditorRequired]
    public string ProductName { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedFor != ProductId)
        {
            _loadedFor = ProductId;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _loadError = null;
        try
        {
            var result = await Products.ListApiKeysAsync(ProductId, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Select(ApiKeyRowViewModel.From).OrderByDescending(r => r.CreatedAt)];
            }
            else
            {
                _loadError = $"{ApiKeysCopy.LoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ReloadAsync()
    {
        _createUncertain = false;
        await LoadAsync();
    }

    private void OnKindChanged(ChangeEventArgs e)
    {
        _kind = e.Value?.ToString() ?? string.Empty;
        _kindError = null;
    }

    private void OnLabelInput(ChangeEventArgs e)
    {
        _label = e.Value?.ToString() ?? string.Empty;
        _labelError = null;
    }

    private async Task CreateAsync()
    {
        if (_busy)
        {
            return;
        }

        _formError = null;
        _kindError = null;
        _labelError = null;
        _createUncertain = false;
        if (_kind.Length == 0)
        {
            _kindError = ApiKeysCopy.KindRequired;
            return;
        }

        if (_label.Trim().Length > 100)
        {
            _labelError = ApiKeysCopy.LabelTooLong;
            return;
        }

        _busy = true;
        try
        {
            var label = string.IsNullOrWhiteSpace(_label) ? null : _label.Trim();
            var result = await Products.CreateApiKeyAsync(ProductId, new CreateProductApiKeyRequest(_kind, label), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                ShowCreateFailure(result.Errors);
                return;
            }

            // The plaintext goes straight into the dialog and nowhere else: not into the rows, the status bar, a field of this component or a URL.
            var created = result.Value;
            _rows = [ApiKeyRowViewModel.From(created.Key), .. _rows];
            _label = string.Empty;
            StatusMessages.Show(ApiKeysCopy.CreatedMessage(created.Key.Kind, ProductName));
            _dialog!.Show(created.Key, created.PlaintextKey);
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowCreateFailure(IReadOnlyList<ResultError> errors)
    {
        var first = errors[0];
        if (ApiErrorCodes.IsUncertainWrite(first.Code))
        {
            _createUncertain = true;
        }
        else if (first.Code == ApiErrorCodes.ProductNotFound)
        {
            _formError = ApiKeysCopy.ProductGone;
        }
        else if (first.Code == ApiErrorCodes.ApiKeyKindInvalid || first.Target == ApiFields.Kind)
        {
            _kindError = first.Message;
        }
        else if (first.Target == ApiFields.Label)
        {
            _labelError = first.Message;
        }
        else
        {
            _formError = first.Message;
        }
    }

    private void AskRevoke(ApiKeyRowViewModel row)
    {
        _revokeError = null;
        _revokeUncertain = false;
        _revoking = row;
    }

    private void CancelRevoke()
    {
        if (!_revokeBusy)
        {
            _revoking = null;
            _revokeError = null;
            _revokeUncertain = false;
        }
    }

    private async Task ConfirmRevokeAsync()
    {
        if (_revokeBusy || _revoking is not { } key)
        {
            return;
        }

        _revokeBusy = true;
        _revokeError = null;
        _revokeUncertain = false;
        StateHasChanged();
        try
        {
            var result = await Products.RevokeApiKeyAsync(ProductId, key.Id, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. _rows.Select(r => r.Id == key.Id ? r with { RevokedAt = Time.GetUtcNow() } : r)];
                _revoking = null;
                StatusMessages.Show(ApiKeysCopy.RevokedMessage(key.Name));
                return;
            }

            await ShowRevokeFailureAsync(result.Errors[0]);
        }
        finally
        {
            _revokeBusy = false;
        }
    }

    private async Task ShowRevokeFailureAsync(ResultError error)
    {
        if (error.Code == ApiErrorCodes.ApiKeyNotFound)
        {
            _revoking = null;
            StatusMessages.Show(ApiKeysCopy.KeyGone);
            await LoadAsync();
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            // The revoke may have been applied before the answer was lost: never a bare "try again".
            _revokeError = ApiKeysCopy.RevokeUncertain;
            _revokeUncertain = true;
        }
        else
        {
            _revokeError = ApiKeysCopy.RevokeFailed(error.Message);
        }
    }

    private async Task ReloadAfterUncertainAsync()
    {
        _revoking = null;
        _revokeError = null;
        _revokeUncertain = false;
        await LoadAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

5. The page: the shell and the content.

`src/TechStrap.Admin/Features/Settings/Products/ProductKeysPage.razor`

```razor
@page "/settings/products/{Id:guid}/keys"
@attribute [Authorize]

<AdminOnly>
    <ProductKeysContent Id="Id" />
</AdminOnly>

@code {
    [Parameter]
    public Guid Id { get; set; }
}
```

`src/TechStrap.Admin/Features/Settings/Products/ProductKeysContent.razor`

```razor
<PageTitle>@ApiKeysCopy.Heading &middot; Products</PageTitle>
    <div class="ts-settings">
        <p class="ts-settings-back"><a href="/settings/products">&larr; @ProductsCopy.BackToProducts</a></p>
        @if (_loading)
        {
            <LoadingState Rows="3" Label="@ProductsCopy.EditorLoading" />
        }
        else if (_error is not null)
        {
            <ErrorState Message="@_error" OnRetry="@(_gone ? default(EventCallback) : EventCallback.Factory.Create(this, LoadAsync))" />
        }
        else
        {
            <div class="ts-settings-head">
                <h1>@_product!.Name</h1>
                <a href="/settings/products/@_product.Id">@ProductsCopy.Edit</a>
            </div>
            <ApiKeysPanel ProductId="_product.Id" ProductName="@_product.Branding.DisplayName" />
        }
    </div>
```

`src/TechStrap.Admin/Features/Settings/Products/ProductKeysContent.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Settings.Products;

/// <summary>The API keys of one product (Admin only). Like every settings page it makes no call unless the session is an Admin.</summary>
public sealed partial class ProductKeysContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private ProductDto? _product;
    private string? _error;
    private bool _loading = true;
    private bool _gone;
    private Guid _loadedFor;

    [Inject]
    private IProductsClient Products { get; set; } = default!;

    [Parameter]
    public Guid Id { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedFor != Id)
        {
            _loadedFor = Id;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        _gone = false;
        try
        {
            var result = await Products.GetAsync(Id, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _product = result.Value;
            }
            else
            {
                _gone = result.Errors[0].Code == ApiErrorCodes.ProductNotFound;
                _error = _gone ? ProductsCopy.EditorGone : $"{ProductsCopy.EditorLoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

6. The styles:

`src/TechStrap.Admin/Styles/_api-keys.scss`

```scss
// API keys (PHASE-07b): the kind badge, the one-time secret and the revoked row. The kind is always the word; the border style (solid for Trusted, dashed for Public) only repeats it.

.ts-kind {
  display: inline-block;
  padding: 1px 8px;
  font: 600 .6875rem var(--ts-font-mono);
  letter-spacing: .04em;
  text-transform: uppercase;
  color: var(--ink);
  background: var(--sheet);
  border: 2px solid var(--ink);
}

.ts-kind--public {
  border-style: dashed;
}

.ts-kind-explained {
  margin: 8px 0 0;
  padding-left: 18px;
  font-size: .8125rem;
  color: var(--ink-2);
}

.ts-key-kind-line {
  overflow-wrap: anywhere;
}

.ts-key-secret {
  display: flex;
  gap: 8px;
  align-items: stretch;
}

.ts-key-secret-input {
  min-width: 0;
  font-family: var(--ts-font-mono);
  font-size: .8125rem;
}

.ts-key-copy-status {
  min-height: 1.25rem;
  margin: 4px 0 8px;
  font-size: .8125rem;
}

// A revoked key stays in the list so the history is visible, but it reads as finished: struck through and muted, with the word "Revoked" in its status.
.ts-key--revoked {
  color: var(--ink-2);

  td:not(:last-child) {
    text-decoration: line-through;
    text-decoration-thickness: 1px;
  }

  .ts-pill,
  .ts-pill + time {
    text-decoration: none;
  }
}
```

```diff
@@
 @import "settings";
+@import "api-keys";
 @import "styleguide";
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "NewApiKeyDialogTests|ApiKeysPanelTests|RevokeApiKeyTests|ProductKeysPageTests|ScriptHostTests|SourceEncodingTests|SourceEscapeTests"`, `node --test tests/TechStrap.Admin.Tests/js/clipboard.test.mjs` and `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS. To see the pins guard (do not commit), make each change, rerun `--filter "NewApiKeyDialogTests|RevokeApiKeyTests"`, expect the failures named, and restore:
- `NewApiKeyDialog.TryCloseAsync`: call `CloseAsync()` without the `_stored` check (and drop the guard at the top of `CloseAsync`): `Esc_and_Cancel_do_nothing_but_say_what_is_missing_until_the_stored_box_is_ticked` and `Ticking_the_box_clears_the_hint_and_enables_Done` fail.
- `NewApiKeyDialog.Clear`: delete the `_plaintext = null;` line: `Done_after_the_tick_closes_once_clears_the_key_and_leaves_no_trace_in_the_markup` and `Esc_and_Close_work_once_the_box_is_ticked_and_clear_the_key_too` fail.
- `ApiKeysPanel.AskRevoke`: add `_ = ConfirmRevokeAsync();` after `_revoking = row;`: eight of the `RevokeApiKeyTests` fail (the dialog never asks).

- [ ] **Step 5: Whole-project check**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), `dotnet test --project tests/TechStrap.Admin.Tests -c Release`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Admin/Features/Settings/Products \
  src/TechStrap.Admin/wwwroot/js/clipboard.js \
  src/TechStrap.Admin/Styles \
  tests/TechStrap.Admin.Tests \
  scripts/tests/AdminScripts.Tests.ps1
git diff --cached --stat
git commit -m "feat(admin): API keys panel with a show-once key dialog (P07-T15)" -m "The new key is held in one field of the dialog and cleared on close; the dialog ignores Cancel and Esc until the key is stored. Revoke asks first and fires once." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 7: Agents page and My settings (P07-T16, P07-T23)

**Review Focus pin (5):** deactivating an agent fires only after its confirmation and only once; activating (no confirmation, by design) fires once per click. Pinned by `AgentsPageTests`. The session-reload behaviour behind the display-name save is pinned in Task 4 and re-checked here by `PublicDisplayNameFieldTests`.

**Files:**
- Create: `src/TechStrap.Admin/Features/Settings/Agents/AgentsCopy.cs`, `AgentRowViewModel.cs`, `AgentsContent.razor` and `.razor.cs`, `AgentsPage.razor`
- Create: `src/TechStrap.Admin/Features/Account/MySettingsCopy.cs`, `PublicNamePreview.cs`, `MyProfileViewModel.cs`, `PublicDisplayNameField.razor` and `.razor.cs`, `NotificationPreferencesPage.razor` and `.razor.cs`
- Create: `src/TechStrap.Admin/Styles/_agents.scss`, `_account.scss`; Modify: `src/TechStrap.Admin/Styles/app.scss`
- Create: `tests/TechStrap.Admin.Tests/Support/TestData.Agents.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/AgentsPageTests.cs`, `NotificationPreferencesPageTests.cs`, `PublicDisplayNameFieldTests.cs`, `PublicNamePreviewTests.cs`

**Interfaces:**
- Consumes:
  - `IAgentsClient.ListPageAsync(int page, int pageSize, ct)` (`Result<PagedResponse<AgentListItemDto>>`), `SetActiveAsync(Guid, bool, ct)` (`Result<AgentDto>`), `UpdateMyProfileAsync(UpdateMyProfileRequest, ct)` (`Result`), `GetNotificationPreferencesAsync(ct)`, `UpdateNotificationPreferencesAsync(UpdateNotificationPreferencesRequest, ct)`; `IProductsClient.ListAsync` (the first active product's display name); `ApiErrorCodes.LastActiveAdmin`, `AgentNotFound`, `IsUncertainWrite`; `ApiFields.PublicDisplayName`.
  - `AgentSession` (`Agent`, `IsAdmin`, `State`, `ReloadAsync`, which stays Ready while it runs; Task 4), `AdminOnly`, `PreferencesService` (`IsLoaded`, `Theme` as `ThemeChoice`, `SingleKeyShortcuts`, `Changed`, `LoadAsync()`, `SetSingleKeyShortcutsAsync(bool)`, `SetThemeAsync(ThemeChoice)`; the setters apply in memory first and never throw), `ThemeChoice`, `AgentRoles`, `AgentPublicName.Format` and `FallbackFormat` (existing Contracts), `ConfirmDialog`, `PagerControl`, `RelativeTime`, `StatusMessageService`.
  - Test support: `AdminComponentTest.Preferences` (the strict `./js/preferences.js` double: `load` answers the defaults, `save(key, value)` succeeds; read the calls from `Invocations["save"]`), `AdminPageTest` (Task 5).
- Produces:

```csharp
// TechStrap.Admin.Features.Settings.Agents
public sealed partial class AgentsPage                   // shell: /settings/agents
public sealed partial class AgentsContent : IDisposable  // [SupplyParameterFromQuery("page")] PageNumber; 25 to a page
public static class AgentsCopy                           // PageSize = 25
// TechStrap.Admin.Features.Account   (every agent; no AdminOnly)
public sealed partial class NotificationPreferencesPage : IDisposable     // /account/notifications ("My settings")
public sealed partial class PublicDisplayNameField : IDisposable          // ProductDisplayName; saves itself on blur or Enter
public static class PublicNamePreview                    // string Build(string? typed, string? profileName, string? productDisplayName)
public static class MySettingsCopy
```

**Rules:**
1. **Roles are read-only (D-041).** Each agent's role is a badge (`ts-role--admin` solid, `ts-role--agent` dashed, always the word) and the page says "Roles come from your identity provider's groups." There is no select and no input on the page. The admin can only activate or deactivate.
2. **Activate has no confirmation and fires once.** The click calls `SetActiveAsync(id, true, CancellationToken.None)`; every action button is disabled while one runs. The row is replaced from the answer ("Activated {name}"). A failure says "Couldn't activate {name}. Nothing was changed. {message}"; a lost answer says the change may have gone through and offers Reload list.
3. **Deactivate asks first and fires once.** A medium-tier `ConfirmDialog` ("Deactivate {name}?" / "They will lose access to TechStrap straight away. You can activate them again later.") with Cancel and Esc making no call. A 409 `last-active-admin` keeps the dialog open and shows the API's own sentence inline; it changes nothing. Another failure: "Couldn't deactivate the agent. Nothing was changed. {message}"; a lost answer disables Confirm, says the change may have gone through and offers Reload list; `agent-not-found` closes the dialog, says "That agent no longer exists." and reloads.
4. **Deactivating yourself** warns in the dialog ("This is your own account. ...") and, when it succeeds, calls `AgentSession.ReloadAsync` so the shell shows what the API now says (agent-inactive, the no-access page).
5. **Paging.** 25 to a page; the page number is in the query string (`?page=2`, absent for page 1); the pager navigates, the content reloads from the new parameter.
6. **My settings is for every agent.** No `AdminOnly`: a plain agent opens it and the page asks for the preferences and the product list only. **Alerts:** one switch per active product; every change sends the full set (`UpdateNotificationPreferencesRequest` with every product, the one change applied), then the status message "Saved your alert settings". Switches are inert while a save runs. A failure puts the switch back (a new `@key` redraws it from what is saved, because Blazor would otherwise leave the clicked state on screen), says nothing was changed, and the next save does not carry the failed change. A lost answer says the outcome is unknown, disables the switches and offers Reload.
7. **Keyboard and theme** go through `PreferencesService` only (`SetSingleKeyShortcutsAsync`, `SetThemeAsync(ThemeChoice)`); the page subscribes to `Changed` to show the current choice and loads the service on its first render (it loads once and never throws). They are kept in the browser, not in the account.
8. **Public display name (D-024, T23).** Optional, plain text, at most 60 characters, no `@`, checked with the API's own two rules before anything is sent. A live line shows "Customers see: {name} from {product} Support" (`AgentPublicName.Format`); with no usable name it is `FallbackFormat` ("Orbitly Support"); the name is what was typed, else the first word of the profile name unless that word contains `@`; with no active product the product is the placeholder "[product]". The helper line is "Customers never see your email address." It saves on blur or Enter, only when the text changed since the last save (so Enter then blur is one write), with `CancellationToken.None`, never retried; after a save it asks the session again, and because the session keeps its agent while it reloads the field and the page keep their state. A server error at `public-display-name` appears at the field.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/Support/TestData.Agents.cs`

```csharp
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static readonly Guid RaeAgentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>A row of the Admin agent list: every field is filled in, as the API does for an Admin.</summary>
    public static AgentListItemDto AgentRow(string name, Guid id, string role = AgentRoles.Agent, bool active = true, DateTimeOffset? lastSeen = null, string? email = null) =>
        new(id, name, name, email ?? $"{name.Split(' ')[0].ToLowerInvariant()}@example.com", role, active, lastSeen);

    /// <summary>The agent's own record, as <c>GET /api/agents/me</c> answers it.</summary>
    public static AgentDto Me(bool admin, string? publicName = null, bool active = true) => new(
        admin ? AdaAgentId : SamAgentId, admin ? "Ada Admin" : "Sam Ortiz", admin ? "ada@example.com" : "sam@example.com",
        admin ? AgentRoles.Admin : AgentRoles.Agent, active, publicName, null);

    public static NotificationPreferenceDto Pref(string product, bool on = false, Guid? id = null) => new(id ?? Guid.NewGuid(), product, on);
}
```

`tests/TechStrap.Admin.Tests/Components/AgentsPageTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Agents;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The agents page. Roles are read-only (D-041): a badge and the identity-provider note, never an input. Review Focus 5: deactivating asks first and fires once; activating has no confirmation by
/// design and fires once. A 409 last-active-admin is shown inside the dialog and changes nothing.
/// </summary>
public sealed class AgentsPageTests : AdminPageTest
{
    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();
    private readonly NavigationManager _navigation;

    public AgentsPageTests()
    {
        _agents.ListPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(Page(
            TestData.AgentRow("Ada Admin", TestData.AdaAgentId, AgentRoles.Admin, lastSeen: TestData.Now.AddMinutes(-1)),
            TestData.AgentRow("Sam Ortiz", TestData.SamAgentId, lastSeen: TestData.Now.AddHours(-2)),
            TestData.AgentRow("Rae Quinn", TestData.RaeAgentId, active: false))));
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(call =>
            TestData.Ok(new AgentDto(call.Arg<Guid>(), "x", "x@example.com", AgentRoles.Agent, call.ArgAt<bool>(1), null, null)));
        Services.AddSingleton(_agents);
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    protected override AgentSession CreateSession(bool admin)
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.Me(admin)));
        var session = new AgentSession(_agents);
        session.EnsureLoadedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return session;
    }

    private static PagedResponse<AgentListItemDto> Page(params AgentListItemDto[] items) => new(items, 1, AgentsCopy.PageSize, items.Length);

    private IRenderedComponent<AgentsPage> RenderPage(string query = "")
    {
        _navigation.NavigateTo($"/settings/agents{query}");
        return Render<AgentsPage>();
    }

    private IEnumerable<(Guid Id, bool Active)> Writes() =>
        _agents.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IAgentsClient.SetActiveAsync)).Select(c => ((Guid)c.GetArguments()[0]!, (bool)c.GetArguments()[1]!));

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<AgentsPage> cut, Guid id) => cut.Find($"tr[data-agent='{id}']");

    private static AngleSharp.Dom.IElement DeactivateDialog(IRenderedComponent<AgentsPage> cut) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.StartsWith("Deactivate ", StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<AgentsPage> cut) =>
        DeactivateDialog(cut).QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    // ---- the list, read-only roles -------------------------------------------------------------------------------

    [Fact]
    public void Each_agent_is_listed_with_email_role_status_and_last_seen_and_the_admin_is_marked_as_you()
    {
        var cut = RenderPage();

        var ada = Row(cut, TestData.AdaAgentId);
        ada.Children[0].TextContent.ShouldContain("Ada Admin");
        ada.Children[0].TextContent.ShouldContain("(you)");
        ada.Children[1].TextContent.ShouldBe("ada@example.com");
        ada.Children[2].TextContent.ShouldBe("Admin");
        ada.Children[3].TextContent.ShouldBe("Active");
        ada.Children[4].TextContent.ShouldBe("1 min ago");
        var rae = Row(cut, TestData.RaeAgentId);
        rae.Children[3].TextContent.ShouldBe("Inactive");
        rae.Children[4].TextContent.ShouldBe("Never");
        Row(cut, TestData.SamAgentId).Children[0].TextContent.ShouldNotContain("(you)");
    }

    [Fact]
    public void Roles_are_a_read_only_badge_with_the_identity_provider_note_and_never_an_input()
    {
        var cut = RenderPage();

        cut.Find("p.ts-roles-note").TextContent.ShouldBe("Roles come from your identity provider's groups.");
        cut.FindAll("td .ts-role").Select(r => (r.TextContent, r.ClassName)).ShouldBe(
            [("Admin", "ts-role ts-role--admin"), ("Agent", "ts-role ts-role--agent"), ("Agent", "ts-role ts-role--agent")]);
        cut.FindAll("select").ShouldBeEmpty();
        cut.FindAll("input").ShouldBeEmpty();
    }

    [Fact]
    public void The_first_page_asks_for_25()
    {
        RenderPage();

        _agents.Received(1).ListPageAsync(1, 25, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_page_in_the_query_string_asks_for_that_page()
    {
        RenderPage("?page=3");

        _agents.Received(1).ListPageAsync(3, 25, Arg.Any<CancellationToken>());
        _agents.DidNotReceive().ListPageAsync(1, Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Paging_goes_through_the_query_string()
    {
        _agents.ListPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<AgentListItemDto>([TestData.AgentRow("Ada Admin", TestData.AdaAgentId, AgentRoles.Admin)], 1, 25, 60)));
        var cut = RenderPage();

        cut.FindAll(".ts-pager button").Single(b => b.TextContent == "Next").Click();

        _navigation.Uri.ShouldEndWith("/settings/agents?page=2");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _agents.ListPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<PagedResponse<AgentListItemDto>>("api-error", "The API is unavailable."),
            TestData.Ok(Page(TestData.AgentRow("Ada Admin", TestData.AdaAgentId, AgentRoles.Admin))));
        var cut = RenderPage();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the agents. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_the_api_is_not_asked_for_the_list()
    {
        AsAgent();

        var cut = RenderPage();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("table").ShouldBeEmpty();
        _agents.DidNotReceive().ListPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ---- activate: no confirmation, once -------------------------------------------------------------------------

    [Fact]
    public void Activating_needs_no_confirmation_sends_one_write_with_no_cancellation_and_shows_the_agent_active()
    {
        var cut = RenderPage();

        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        Writes().ShouldBe([(TestData.RaeAgentId, true)]);
        _agents.Received(1).SetActiveAsync(TestData.RaeAgentId, true, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Row(cut, TestData.RaeAgentId).Children[3].TextContent.ShouldBe("Active");
        Row(cut, TestData.RaeAgentId).QuerySelectorAll("button.ts-activate").ShouldBeEmpty();
        StatusMessages.Current.ShouldBe("Activated Rae Quinn");
        Dialogs.VerifyNotInvoke("open");
    }

    [Fact]
    public void Two_clicks_on_activate_while_the_first_runs_send_one_write()
    {
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();

        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();
        cut.FindAll("button.ts-activate, button.ts-deactivate").ShouldAllBe(b => b.HasAttribute("disabled"));
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        Writes().Count().ShouldBe(1);
        gate.SetResult(TestData.Ok(new AgentDto(TestData.RaeAgentId, "Rae Quinn", "rae@example.com", AgentRoles.Agent, true, null, null)));
        cut.WaitForAssertion(() => Row(cut, TestData.RaeAgentId).Children[3].TextContent.ShouldBe("Active"));
    }

    [Fact]
    public void A_failed_activation_says_nothing_changed_and_leaves_the_row_inactive()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>("boom", "The API refused."));
        var cut = RenderPage();

        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("Couldn't activate Rae Quinn. Nothing was changed. The API refused.");
        Row(cut, TestData.RaeAgentId).Children[3].TextContent.ShouldBe("Inactive");
        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public void A_lost_answer_on_activate_never_claims_nothing_changed_and_offers_a_reload()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, TestData.RaeAgentId).QuerySelector("button.ts-activate")!.Click();

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("The change to Rae Quinn may have gone through.");
        alert.TextContent.ShouldNotContain("Nothing was changed");

        alert.QuerySelector("button")!.Click();

        _agents.Received(2).ListPageAsync(1, 25, Arg.Any<CancellationToken>());
        Writes().Count().ShouldBe(1);
    }

    // ---- deactivate: asks first, once ----------------------------------------------------------------------------

    [Fact]
    public void Deactivating_asks_first_names_the_agent_and_the_consequence_and_calls_nothing_yet()
    {
        var cut = RenderPage();

        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();

        var dialog = DeactivateDialog(cut);
        dialog.QuerySelector("h2")!.TextContent.ShouldBe("Deactivate Sam Ortiz?");
        dialog.TextContent.ShouldContain("They will lose access to TechStrap straight away.");
        dialog.QuerySelector("input").ShouldBeNull("a medium confirmation: no typed text");
        dialog.TextContent.ShouldNotContain("This is your own account");
        Confirm(cut).TextContent.ShouldBe("Deactivate agent");
        Dialogs.VerifyInvoke("open", 1);
        Writes().ShouldBeEmpty();
    }

    [Fact]
    public void Cancel_and_Esc_close_the_confirmation_and_make_no_call()
    {
        var cut = RenderPage();

        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();
        DeactivateDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();
        DeactivateDialog(cut).TriggerEvent("oncancel", EventArgs.Empty);

        Writes().ShouldBeEmpty();
        Dialogs.VerifyInvoke("close", 2);
        Row(cut, TestData.SamAgentId).Children[3].TextContent.ShouldBe("Active");
    }

    [Fact]
    public void Confirming_deactivates_that_agent_once_and_the_row_shows_inactive_with_its_activate_button()
    {
        var cut = RenderPage();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();

        Confirm(cut).Click();

        Writes().ShouldBe([(TestData.SamAgentId, false)]);
        _agents.Received(1).SetActiveAsync(TestData.SamAgentId, false, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Row(cut, TestData.SamAgentId).Children[3].TextContent.ShouldBe("Inactive");
        Row(cut, TestData.SamAgentId).QuerySelectorAll("button.ts-activate").Count.ShouldBe(1);
        StatusMessages.Current.ShouldBe("Deactivated Sam Ortiz");
        Dialogs.VerifyInvoke("close", 1);
        Session.State.ShouldBe(AgentSessionState.Ready, "someone else's deactivation does not touch my session");
    }

    [Fact]
    public void A_double_click_on_confirm_deactivates_once_and_the_dialog_is_inert_while_it_runs()
    {
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.SetActiveAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();

        Confirm(cut).Click();
        DeactivateDialog(cut).QuerySelectorAll("button").ShouldAllBe(b => b.HasAttribute("disabled"));
        DeactivateDialog(cut).TriggerEvent("oncancel", EventArgs.Empty);
        Confirm(cut).Click();

        Writes().Count().ShouldBe(1);
        Dialogs.VerifyNotInvoke("close");
        gate.SetResult(TestData.Ok(new AgentDto(TestData.SamAgentId, "Sam Ortiz", "sam@example.com", AgentRoles.Agent, false, null, null)));
        cut.WaitForAssertion(() => Row(cut, TestData.SamAgentId).Children[3].TextContent.ShouldBe("Inactive"));
    }

    [Fact]
    public void The_last_active_admin_conflict_is_shown_inside_the_dialog_in_the_apis_words_and_changes_nothing()
    {
        const string Message = "TechStrap needs at least one active admin. Make sure another admin is active before turning this one off.";
        _agents.SetActiveAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.LastActiveAdmin, Message, ResultErrorKind.Conflict));
        var cut = RenderPage();
        Row(cut, TestData.AdaAgentId).QuerySelector("button.ts-deactivate")!.Click();

        Confirm(cut).Click();

        DeactivateDialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe(Message);
        Row(cut, TestData.AdaAgentId).Children[3].TextContent.ShouldBe("Active");
        Dialogs.VerifyNotInvoke("close");
        StatusMessages.Current.ShouldBeNull();
        Session.State.ShouldBe(AgentSessionState.Ready);
    }

    [Fact]
    public void Another_failure_says_nothing_was_changed_and_keeps_the_dialog_open()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>("boom", "The API refused."));
        var cut = RenderPage();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();

        Confirm(cut).Click();

        DeactivateDialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't deactivate the agent. Nothing was changed. The API refused.");
        Dialogs.VerifyNotInvoke("close");
    }

    [Fact]
    public void A_lost_answer_never_claims_nothing_changed_blocks_a_second_write_and_offers_a_reload()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();
        Confirm(cut).Click();

        var dialog = DeactivateDialog(cut);
        dialog.QuerySelector("[role=alert]")!.TextContent.ShouldBe("The change may have gone through. Reload the list to check before you try again.");
        dialog.TextContent.ShouldNotContain("Nothing was changed");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Writes().Count().ShouldBe(1);

        DeactivateDialog(cut).QuerySelector(".ts-dialog-recovery button")!.Click();

        _agents.Received(2).ListPageAsync(1, 25, Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void An_agent_who_is_already_gone_closes_the_dialog_says_so_and_reloads_the_list()
    {
        _agents.SetActiveAsync(Arg.Any<Guid>(), false, Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.AgentNotFound, "No such agent.", ResultErrorKind.NotFound));
        var cut = RenderPage();
        Row(cut, TestData.SamAgentId).QuerySelector("button.ts-deactivate")!.Click();

        Confirm(cut).Click();

        StatusMessages.Current.ShouldBe("That agent no longer exists.");
        _agents.Received(2).ListPageAsync(1, 25, Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    // ---- deactivating yourself -----------------------------------------------------------------------------------

    [Fact]
    public void Deactivating_yourself_warns_in_the_dialog_and_after_it_succeeds_the_session_asks_the_api_again()
    {
        var cut = RenderPage();
        Row(cut, TestData.AdaAgentId).QuerySelector("button.ts-deactivate")!.Click();
        DeactivateDialog(cut).TextContent.ShouldContain("This is your own account.");
        _agents.ClearReceivedCalls();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<AgentDto>(ApiErrorCodes.AgentInactive, "Your agent account is deactivated.", ResultErrorKind.Forbidden));

        Confirm(cut).Click();

        Writes().ShouldBe([(TestData.AdaAgentId, false)]);
        _agents.Received(1).GetMeAsync(Arg.Any<CancellationToken>());
        Session.State.ShouldBe(AgentSessionState.NoAccess);
    }

    // ---- no stale session flicker --------------------------------------------------------------------------------

    [Fact]
    public void The_page_never_makes_a_call_a_plain_agent_would_be_refused()
    {
        AsAgent();
        var cut = RenderPage();

        cut.Markup.ShouldNotContain("Deactivate");
        _agents.ReceivedCalls().Select(c => c.GetMethodInfo().Name).Distinct().ShouldBe([nameof(IAgentsClient.GetMeAsync)]);
    }
}
```

`tests/TechStrap.Admin.Tests/Components/NotificationPreferencesPageTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Account;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>My settings, opened by a plain agent: alert toggles that always send the full set, the keyboard switch and the theme (kept in the browser), and the public display name.</summary>
public sealed class NotificationPreferencesPageTests : AdminPageTest
{
    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly Guid _orbitly = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private readonly Guid _nimbus = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private readonly Guid _acme = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    public NotificationPreferencesPageTests()
    {
        AsAgent();
        _agents.GetNotificationPreferencesAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<NotificationPreferenceDto>>(
        [
            new NotificationPreferenceDto(_orbitly, "Orbitly", false),
            new NotificationPreferenceDto(_nimbus, "Nimbus", true),
            new NotificationPreferenceDto(_acme, "Acme", false),
        ]));
        _agents.UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok());
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>(
        [
            TestData.ProductDetail("Orbitly", active: false),
            TestData.ProductDetail("Nimbus", id: _nimbus, key: "nimbus", prefix: "NIM") with { Branding = TestData.ProductDetail("Nimbus Cloud").Branding },
        ]));
        Services.AddSingleton(_agents);
        Services.AddSingleton(_products);
    }

    protected override AgentSession CreateSession(bool admin)
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.Me(admin)));
        var session = new AgentSession(_agents);
        session.EnsureLoadedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return session;
    }

    private IReadOnlyList<UpdateNotificationPreferencesRequest> Saves() =>
        [.. _agents.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IAgentsClient.UpdateNotificationPreferencesAsync)).Select(c => (UpdateNotificationPreferencesRequest)c.GetArguments()[0]!)];

    private static AngleSharp.Dom.IElement Toggle(IRenderedComponent<NotificationPreferencesPage> cut, string product) =>
        cut.FindAll(".ts-toggle-list li").Single(li => li.TextContent.Contains($"New tickets in {product}")).QuerySelector("input")!;

    private static bool IsOn(AngleSharp.Dom.IElement toggle) => toggle.HasAttribute("checked");

    // ---- a plain agent can use it --------------------------------------------------------------------------------

    [Fact]
    public void A_plain_agent_gets_the_page_not_the_no_access_page()
    {
        var cut = Render<NotificationPreferencesPage>();

        cut.Find("h1").TextContent.ShouldBe("My settings");
        cut.FindAll("section.ts-no-access").ShouldBeEmpty();
        Session.IsAdmin.ShouldBeFalse();
    }

    // ---- alerts: the full set ------------------------------------------------------------------------------------

    [Fact]
    public void Each_active_product_has_a_toggle_showing_what_is_saved()
    {
        var cut = Render<NotificationPreferencesPage>();

        IsOn(Toggle(cut, "Orbitly")).ShouldBeFalse();
        IsOn(Toggle(cut, "Nimbus")).ShouldBeTrue();
        IsOn(Toggle(cut, "Acme")).ShouldBeFalse();
        cut.Find("#ts-alerts-heading").TextContent.ShouldBe("Email alerts");
    }

    [Fact]
    public void A_toggle_sends_the_full_set_of_products_with_that_one_changed_then_a_status_message()
    {
        var cut = Render<NotificationPreferencesPage>();

        Toggle(cut, "Orbitly").Change(true);

        var sent = Saves().ShouldHaveSingleItem();
        sent.Preferences.ShouldBe(
        [
            new NotificationPreferenceUpdateDto(_orbitly, true),
            new NotificationPreferenceUpdateDto(_nimbus, true),
            new NotificationPreferenceUpdateDto(_acme, false),
        ]);
        _agents.Received(1).UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        StatusMessages.Current.ShouldBe("Saved your alert settings");
    }

    [Fact]
    public void A_second_toggle_carries_the_first_change_so_the_set_is_always_complete()
    {
        var cut = Render<NotificationPreferencesPage>();

        Toggle(cut, "Orbitly").Change(true);
        Toggle(cut, "Nimbus").Change(false);

        Saves()[1].Preferences.ShouldBe(
        [
            new NotificationPreferenceUpdateDto(_orbitly, true),
            new NotificationPreferenceUpdateDto(_nimbus, false),
            new NotificationPreferenceUpdateDto(_acme, false),
        ]);
    }

    [Fact]
    public void While_a_save_runs_every_toggle_is_inert_so_two_saves_never_overlap()
    {
        var gate = new TaskCompletionSource<Result>();
        _agents.UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = Render<NotificationPreferencesPage>();

        Toggle(cut, "Orbitly").Change(true);
        cut.FindAll(".ts-toggle-list input").ShouldAllBe(i => i.HasAttribute("disabled"));
        Toggle(cut, "Acme").Change(true);

        Saves().Count.ShouldBe(1);
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => cut.FindAll(".ts-toggle-list input").ShouldAllBe(i => !i.HasAttribute("disabled")));
        IsOn(Toggle(cut, "Orbitly")).ShouldBeTrue();
    }

    [Fact]
    public void A_failed_save_puts_the_toggle_back_says_nothing_changed_and_the_next_save_does_not_carry_the_failed_change()
    {
        _agents.UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail("boom", "The API refused."), TestData.Ok());
        var cut = Render<NotificationPreferencesPage>();

        Toggle(cut, "Orbitly").Change(true);

        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("Couldn't save your alert settings. Nothing was changed. The API refused.");
        IsOn(Toggle(cut, "Orbitly")).ShouldBeFalse();
        StatusMessages.Current.ShouldBeNull();

        Toggle(cut, "Acme").Change(true);

        Saves()[1].Preferences.ShouldBe(
        [
            new NotificationPreferenceUpdateDto(_orbitly, false),
            new NotificationPreferenceUpdateDto(_nimbus, true),
            new NotificationPreferenceUpdateDto(_acme, true),
        ]);
        cut.FindAll(".ts-conflict").ShouldBeEmpty();
    }

    [Fact]
    public void A_lost_answer_never_claims_nothing_changed_blocks_more_toggles_and_a_reload_shows_what_is_saved()
    {
        _agents.UpdateNotificationPreferencesAsync(Arg.Any<UpdateNotificationPreferencesRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = Render<NotificationPreferencesPage>();
        Toggle(cut, "Orbitly").Change(true);

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("Couldn't confirm that your alert settings were saved. Reload to see what is saved.");
        alert.TextContent.ShouldNotContain("Nothing was changed");
        cut.FindAll(".ts-toggle-list input").ShouldAllBe(i => i.HasAttribute("disabled"));

        alert.QuerySelector("button")!.Click();

        _agents.Received(2).GetNotificationPreferencesAsync(Arg.Any<CancellationToken>());
        cut.FindAll(".ts-conflict").ShouldBeEmpty();
        cut.FindAll(".ts-toggle-list input").ShouldAllBe(i => !i.HasAttribute("disabled"));
        Saves().Count.ShouldBe(1, "a write is never retried");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _agents.GetNotificationPreferencesAsync(Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<IReadOnlyList<NotificationPreferenceDto>>("api-error", "The API is unavailable."),
            TestData.Ok<IReadOnlyList<NotificationPreferenceDto>>([new NotificationPreferenceDto(_orbitly, "Orbitly", false)]));
        var cut = Render<NotificationPreferencesPage>();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load your alert settings. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll(".ts-toggle-list li").Count.ShouldBe(1));
    }

    [Fact]
    public void No_active_products_is_a_plain_empty_state()
    {
        _agents.GetNotificationPreferencesAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<NotificationPreferenceDto>>([]));

        var cut = Render<NotificationPreferencesPage>();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No active products yet");
    }

    // ---- keyboard and theme: this browser ------------------------------------------------------------------------

    [Fact]
    public void The_keyboard_switch_turns_the_single_key_layer_off_and_remembers_it_in_the_browser()
    {
        var cut = Render<NotificationPreferencesPage>();
        cut.WaitForAssertion(() => cut.Find("#ts-keyboard").HasAttribute("checked").ShouldBeTrue());
        ShortcutService.SingleKeyEnabled.ShouldBeTrue();

        cut.Find("#ts-keyboard").Change(false);

        ShortcutService.SingleKeyEnabled.ShouldBeFalse();
        Preferences.Invocations["save"].Single().Arguments.ShouldBe(["singleKeyShortcuts", false]);
        cut.Find("label[for=ts-keyboard]").TextContent.ShouldBe("Keyboard shortcuts");
    }

    [Fact]
    public void The_theme_offers_auto_light_and_dark_with_auto_chosen_and_choosing_one_saves_it_in_the_browser()
    {
        var cut = Render<NotificationPreferencesPage>();

        cut.FindAll("input[name=ts-theme]").Select(i => i.GetAttribute("value")).ShouldBe(["auto", "light", "dark"]);
        cut.WaitForAssertion(() => cut.Find("#ts-theme-auto").HasAttribute("checked").ShouldBeTrue());

        cut.Find("#ts-theme-dark").Change("dark");

        Preferences.Invocations["save"].Single().Arguments.ShouldBe(["theme", "dark"]);
    }

    // ---- the public display name's preview uses the first active product ----------------------------------------

    [Fact]
    public void The_preview_uses_the_first_active_product_and_the_agents_first_name()
    {
        var cut = Render<NotificationPreferencesPage>();

        cut.Find("#ts-public-name-preview").TextContent.ShouldBe("Customers see: Sam from Nimbus Cloud Support");
    }

    [Fact]
    public void When_no_product_is_active_the_preview_shows_a_placeholder_for_it()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.ProductDetail("Orbitly", active: false)]));

        var cut = Render<NotificationPreferencesPage>();

        cut.Find("#ts-public-name-preview").TextContent.ShouldBe("Customers see: Sam from [product] Support");
    }

    [Fact]
    public void A_product_list_that_cannot_be_read_only_means_the_placeholder_preview()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<ProductDto>>("api-error", "Down."));

        var cut = Render<NotificationPreferencesPage>();

        cut.Find("#ts-public-name-preview").TextContent.ShouldBe("Customers see: Sam from [product] Support");
        cut.Find(".ts-toggle-list").ShouldNotBeNull();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/PublicDisplayNameFieldTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Account;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>PHASE-07 T23: the optional public display name with its live preview, saved on blur or Enter, with the two rules checked before anything is sent.</summary>
public sealed class PublicDisplayNameFieldTests : AdminPageTest
{
    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();
    private string? _savedName;

    public PublicDisplayNameFieldTests()
    {
        AsAgent();
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok());
        Services.AddSingleton(_agents);
    }

    protected override AgentSession CreateSession(bool admin)
    {
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.Me(admin, _savedName)));
        var session = new AgentSession(_agents);
        session.EnsureLoadedAsync(CancellationToken.None).GetAwaiter().GetResult();
        return session;
    }

    private IRenderedComponent<PublicDisplayNameField> RenderField(string? product = "Orbitly") =>
        Render<PublicDisplayNameField>(p => p.Add(c => c.ProductDisplayName, product));

    private IReadOnlyList<UpdateMyProfileRequest> Saves() =>
        [.. _agents.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IAgentsClient.UpdateMyProfileAsync)).Select(c => (UpdateMyProfileRequest)c.GetArguments()[0]!)];

    private static string Preview(IRenderedComponent<PublicDisplayNameField> cut) => cut.Find("#ts-public-name-preview").TextContent;

    // ---- the preview ---------------------------------------------------------------------------------------------

    [Fact]
    public void The_preview_starts_as_the_first_name_from_the_first_product_and_the_helper_says_the_email_is_never_shown()
    {
        var cut = RenderField();

        Preview(cut).ShouldBe("Customers see: Sam from Orbitly Support");
        cut.Find("#ts-public-name-help").TextContent.ShouldBe("Customers never see your email address.");
        cut.Find("label[for=ts-public-name]").TextContent.ShouldBe("Public display name (optional)");
        cut.Find("#ts-public-name").GetAttribute("value").ShouldBe(string.Empty);
    }

    [Fact]
    public void Typing_changes_the_preview_live_and_clearing_returns_to_the_default()
    {
        var cut = RenderField();

        cut.Find("#ts-public-name").Input("Samantha");
        Preview(cut).ShouldBe("Customers see: Samantha from Orbitly Support");

        cut.Find("#ts-public-name").Input("");
        Preview(cut).ShouldBe("Customers see: Sam from Orbitly Support");
        Saves().ShouldBeEmpty("typing alone never saves");
    }

    [Fact]
    public void A_saved_name_fills_the_field_and_the_preview()
    {
        _savedName = "Sammy";

        var cut = RenderField();

        cut.Find("#ts-public-name").GetAttribute("value").ShouldBe("Sammy");
        Preview(cut).ShouldBe("Customers see: Sammy from Orbitly Support");
    }

    // ---- saving --------------------------------------------------------------------------------------------------

    [Fact]
    public void Blur_saves_the_trimmed_name_once_with_no_cancellation_shows_the_confirmation_and_asks_the_session_again()
    {
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("  Samantha  ");

        cut.Find("#ts-public-name").Blur();

        Saves().ShouldHaveSingleItem().PublicDisplayName.ShouldBe("Samantha");
        _agents.Received(1).UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        cut.Find("p[role=status]").TextContent.ShouldBe("Saved.");
        cut.Find("#ts-public-name").GetAttribute("value").ShouldBe("Samantha");
        _agents.Received(2).GetMeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Enter_saves_and_the_blur_that_follows_does_not_save_a_second_time()
    {
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");

        cut.Find("#ts-public-name").KeyDown(Key.Enter);
        cut.Find("#ts-public-name").Blur();

        Saves().Count.ShouldBe(1);
    }

    [Fact]
    public void Blur_without_a_change_saves_nothing_and_the_field_is_optional()
    {
        var cut = RenderField();

        cut.Find("#ts-public-name").Blur();
        cut.Find("#ts-public-name").Input("   ");
        cut.Find("#ts-public-name").Blur();

        Saves().ShouldBeEmpty();
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void Clearing_a_saved_name_sends_null()
    {
        _savedName = "Sammy";
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("");

        cut.Find("#ts-public-name").Blur();

        Saves().ShouldHaveSingleItem().PublicDisplayName.ShouldBeNull();
    }

    [Fact]
    public void The_session_keeps_the_agent_and_stays_ready_while_it_reloads_so_the_field_does_not_flicker()
    {
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");
        var gate = new TaskCompletionSource<Result<AgentDto>>();
        _agents.GetMeAsync(Arg.Any<CancellationToken>()).Returns(gate.Task);

        cut.Find("#ts-public-name").Blur();

        Session.State.ShouldBe(AgentSessionState.Ready);
        Session.Agent.ShouldNotBeNull();
        cut.Find("#ts-public-name").GetAttribute("value").ShouldBe("Samantha");
        gate.SetResult(TestData.Ok(TestData.Me(admin: false, "Samantha")));
        cut.WaitForAssertion(() => Session.Agent!.PublicDisplayName.ShouldBe("Samantha"));
        Session.State.ShouldBe(AgentSessionState.Ready);
    }

    // ---- the two rules are checked before anything is sent -------------------------------------------------------

    [Fact]
    public void A_name_over_60_characters_or_with_an_at_sign_shows_a_field_error_and_sends_nothing()
    {
        var cut = RenderField();

        cut.Find("#ts-public-name").Input(new string('x', 61));
        cut.Find("#ts-public-name").Blur();
        cut.Find("#ts-public-name-error").TextContent.ShouldBe("Use 60 characters or fewer.");

        cut.Find("#ts-public-name").Input("sam@example.com");
        cut.Find("#ts-public-name").KeyDown(Key.Enter);
        cut.Find("#ts-public-name-error").TextContent.ShouldBe("Use a name without @, so it can't be mistaken for an email address.");

        Saves().ShouldBeEmpty();
        cut.Find("#ts-public-name").GetAttribute("aria-invalid").ShouldBe("true");
    }

    [Fact]
    public void Exactly_60_characters_is_accepted()
    {
        var cut = RenderField();
        cut.Find("#ts-public-name").Input(new string('x', 60));

        cut.Find("#ts-public-name").Blur();

        Saves().ShouldHaveSingleItem().PublicDisplayName!.Length.ShouldBe(60);
    }

    // ---- what the API says ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ApiErrorCodes.PublicDisplayNameTooLong)]
    [InlineData(ApiErrorCodes.PublicDisplayNameInvalid)]
    public void A_field_error_from_the_api_appears_at_the_field_and_the_text_stays(string code)
    {
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(new ResultError(code, "The server says no.", ResultErrorKind.Validation, "public-display-name")));
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");

        cut.Find("#ts-public-name").Blur();

        cut.Find("#ts-public-name-error").TextContent.ShouldBe("The server says no.");
        cut.Find("#ts-public-name").GetAttribute("value").ShouldBe("Samantha");
        cut.FindAll("p[role=status]").ShouldBeEmpty();
        _agents.Received(1).GetMeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Another_failure_says_nothing_changed_and_a_lost_answer_never_does()
    {
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail("boom", "The API refused."), TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");

        cut.Find("#ts-public-name").Blur();
        cut.Find("#ts-public-name-error").TextContent.ShouldBe("Couldn't save your public display name. Nothing was changed. The API refused.");

        cut.Find("#ts-public-name").Blur();
        var lost = cut.Find("#ts-public-name-error").TextContent;
        lost.ShouldBe("The save may have gone through. Reload the page to see what is saved before you try again.");
        lost.ShouldNotContain("Nothing was changed");
        Saves().Count.ShouldBe(2);
    }

    [Fact]
    public void A_save_that_finishes_after_the_field_is_gone_touches_nothing_and_is_not_cancelled()
    {
        var gate = new TaskCompletionSource<Result>();
        _agents.UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderField();
        cut.Find("#ts-public-name").Input("Samantha");
        cut.Find("#ts-public-name").Blur();

        cut.Instance.Dispose();
        gate.SetResult(TestData.Ok());

        _agents.Received(1).UpdateMyProfileAsync(Arg.Any<UpdateMyProfileRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        _agents.Received(1).GetMeAsync(Arg.Any<CancellationToken>());
    }
}
```

`tests/TechStrap.Admin.Tests/Components/PublicNamePreviewTests.cs`

```csharp
using TechStrap.Admin.Features.Account;

namespace TechStrap.Admin.Tests.Components;

public sealed class PublicNamePreviewTests
{
    [Theory]
    [InlineData(null, "Sam Ortiz", "Orbitly", "Sam from Orbitly Support")]
    [InlineData("", "Sam Ortiz", "Orbitly", "Sam from Orbitly Support")]
    [InlineData("   ", "Sam Ortiz", "Orbitly", "Sam from Orbitly Support")]
    [InlineData("Samantha", "Sam Ortiz", "Orbitly", "Samantha from Orbitly Support")]
    [InlineData("  Samantha  ", "Sam Ortiz", " Orbitly ", "Samantha from Orbitly Support")]
    [InlineData(null, "  Sam   Ortiz ", "Orbitly", "Sam from Orbitly Support")]
    public void A_typed_name_or_the_first_word_of_the_profile_name_goes_before_the_product(string? typed, string? profile, string product, string expected)
    {
        PublicNamePreview.Build(typed, profile, product).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, "sam@example.com")]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", "   ")]
    public void With_no_usable_name_it_is_the_products_plain_support_name_and_an_email_is_never_shown(string? typed, string? profile)
    {
        var line = PublicNamePreview.Build(typed, profile, "Orbitly");

        line.ShouldBe("Orbitly Support");
        line.ShouldNotContain("@");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_an_active_product_the_product_is_a_placeholder(string? product)
    {
        PublicNamePreview.Build(null, "Sam Ortiz", product).ShouldBe("Sam from [product] Support");
        PublicNamePreview.Build(null, null, product).ShouldBe("[product] Support");
    }

    [Fact]
    public void FirstWord_skips_a_profile_name_that_is_an_email_address()
    {
        PublicNamePreview.FirstWord("sam@example.com Ortiz").ShouldBeNull();
        PublicNamePreview.FirstWord("Sam Ortiz").ShouldBe("Sam");
        PublicNamePreview.FirstWord(null).ShouldBeNull();
    }
}
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "AgentsPageTests|NotificationPreferencesPageTests|PublicDisplayNameFieldTests|PublicNamePreviewTests"`
Expected: a build failure (`AgentsPage`, `AgentsContent`, `AgentsCopy`, `NotificationPreferencesPage`, `PublicDisplayNameField`, `PublicNamePreview` do not exist).

- [ ] **Step 3: Implement**

1. The agents page:

`src/TechStrap.Admin/Features/Settings/Agents/AgentsCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Settings.Agents;

/// <summary>The copy of the agents page. Roles are read-only here (D-041): they come from the identity provider's groups, and an admin can only activate or deactivate an agent.</summary>
public static class AgentsCopy
{
    public const int PageSize = 25;

    public const string Heading = "Agents";
    public const string RolesNote = "Roles come from your identity provider's groups.";
    public const string Loading = "Loading agents";
    public const string LoadFailed = "Couldn't load the agents.";
    public const string NoAgents = "No agents yet";

    public const string ColumnName = "Name";
    public const string ColumnEmail = "Email";
    public const string ColumnRole = "Role";
    public const string ColumnStatus = "Status";
    public const string ColumnLastSeen = "Last seen";
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string NeverSeen = "Never";
    public const string Unknown = "\u2014";
    public const string You = "(you)";
    public const string Activate = "Activate";
    public const string Deactivate = "Deactivate";
    public const string DeactivateConfirm = "Deactivate agent";
    public const string ReloadList = "Reload list";

    public const string DeactivateBody = "They will lose access to TechStrap straight away. You can activate them again later.";
    public const string DeactivateSelfWarning = "This is your own account. You will lose access as soon as you confirm, and another admin will have to activate you again.";
    public const string DeactivateUncertain = "The change may have gone through. Reload the list to check before you try again.";
    public const string AgentGone = "That agent no longer exists.";

    public static string DeactivateTitle(string name) => $"Deactivate {name}?";

    public static string DeactivateFailed(string reason) => $"Couldn't deactivate the agent. Nothing was changed. {reason}";

    public static string ActivateFailed(string name, string reason) => $"Couldn't activate {name}. Nothing was changed. {reason}";

    public static string ActivateUncertain(string name) => $"The change to {name} may have gone through. Reload the list to check before you try again.";

    public static string Activated(string name) => $"Activated {name}";

    public static string Deactivated(string name) => $"Deactivated {name}";
}
```

`src/TechStrap.Admin/Features/Settings/Agents/AgentRowViewModel.cs`

```csharp
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Features.Settings.Agents;

/// <summary>One row of the agents page. An Admin gets every field; a field the API left out (null) is shown as a dash and offers no action.</summary>
internal sealed record AgentRowViewModel(Guid Id, string DisplayName, string? Email, string? Role, bool? IsActive, DateTimeOffset? LastSeenAt)
{
    public static AgentRowViewModel From(AgentListItemDto agent) =>
        new(agent.Id, string.IsNullOrWhiteSpace(agent.Name) ? agent.DisplayLabel : agent.Name, agent.Email, agent.Role, agent.IsActive, agent.LastSeenAt);

    /// <summary>The row after the API answered a change: its fresh role, active flag and last-seen time, with the name the list already showed.</summary>
    public AgentRowViewModel With(AgentDto agent) => this with { Email = agent.Email, Role = agent.Role, IsActive = agent.IsActive, LastSeenAt = agent.LastSeenAt };
}
```

`src/TechStrap.Admin/Features/Settings/Agents/AgentsPage.razor`

```razor
@page "/settings/agents"
@attribute [Authorize]

<AdminOnly>
    <AgentsContent />
</AdminOnly>
```

`src/TechStrap.Admin/Features/Settings/Agents/AgentsContent.razor`

```razor
<PageTitle>@AgentsCopy.Heading &middot; Settings</PageTitle>
    <div class="ts-settings ts-agents">
        <h1>@AgentsCopy.Heading</h1>
        <p class="ts-field-note ts-roles-note">@AgentsCopy.RolesNote</p>

        @if (_rowError is not null)
        {
            <div class="ts-conflict" role="alert">
                <p>@_rowError</p>
                @if (_rowUncertain)
                {
                    <button type="button" class="btn btn-outline-secondary" @onclick="ReloadAsync">@AgentsCopy.ReloadList</button>
                }
            </div>
        }

        @if (_loading && _rows.Count == 0)
        {
            <LoadingState Rows="5" Label="@AgentsCopy.Loading" />
        }
        else if (_error is not null)
        {
            <ErrorState Message="@_error" OnRetry="ReloadAsync" />
        }
        else if (_rows.Count == 0)
        {
            <EmptyState Heading="@AgentsCopy.NoAgents" />
        }
        else
        {
            <div class="table-responsive">
                <table class="table ts-ledger ts-settings-table" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
                            <th scope="col">@AgentsCopy.ColumnName</th>
                            <th scope="col">@AgentsCopy.ColumnEmail</th>
                            <th scope="col">@AgentsCopy.ColumnRole</th>
                            <th scope="col">@AgentsCopy.ColumnStatus</th>
                            <th scope="col">@AgentsCopy.ColumnLastSeen</th>
                            <th scope="col"><span class="visually-hidden">Actions</span></th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var row in _rows)
                        {
                            <tr @key="row.Id" data-agent="@row.Id">
                                <td>@row.DisplayName @if (IsSelf(row)) { <span class="ts-you">@AgentsCopy.You</span> }</td>
                                <td>@(row.Email ?? AgentsCopy.Unknown)</td>
                                <td>
                                    @if (row.Role is { } role)
                                    {
                                        <span class="@("ts-role ts-role--" + role.ToLowerInvariant())">@role</span>
                                    }
                                    else
                                    {
                                        @AgentsCopy.Unknown
                                    }
                                </td>
                                <td>
                                    @if (row.IsActive is { } active)
                                    {
                                        <span class="ts-pill @(active ? "ts-pill--on" : "ts-pill--off")">@(active ? AgentsCopy.Active : AgentsCopy.Inactive)</span>
                                    }
                                    else
                                    {
                                        @AgentsCopy.Unknown
                                    }
                                </td>
                                <td>
                                    @if (row.LastSeenAt is { } seen)
                                    {
                                        <RelativeTime When="seen" />
                                    }
                                    else
                                    {
                                        @AgentsCopy.NeverSeen
                                    }
                                </td>
                                <td class="ts-settings-actions">
                                    @if (row.IsActive == true)
                                    {
                                        <button type="button" class="btn btn-outline-secondary ts-deactivate" disabled="@_busy" @onclick="() => AskDeactivate(row)">@AgentsCopy.Deactivate</button>
                                    }
                                    else if (row.IsActive == false)
                                    {
                                        <button type="button" class="btn btn-outline-secondary ts-activate" disabled="@_busy" @onclick="() => ActivateAsync(row)">@AgentsCopy.Activate</button>
                                    }
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
            <PagerControl Page="_page" PageSize="AgentsCopy.PageSize" TotalCount="_total" OnPageChanged="OnPageChanged" />
        }

        <ConfirmDialog Open="@(_deactivating is not null)" Title="@(_deactivating is null ? string.Empty : AgentsCopy.DeactivateTitle(_deactivating.DisplayName))"
                       ConfirmLabel="@AgentsCopy.DeactivateConfirm" Danger="true" Busy="_busy" ConfirmDisabled="_uncertain" Error="@_dialogError"
                       OnConfirm="ConfirmDeactivateAsync" OnCancel="CancelDeactivate">
            @if (_deactivating is not null)
            {
                <p>@AgentsCopy.DeactivateBody</p>
                @if (IsSelf(_deactivating))
                {
                    <p>@AgentsCopy.DeactivateSelfWarning</p>
                }
                @if (_uncertain)
                {
                    <div class="ts-dialog-recovery">
                        <button type="button" class="btn btn-link" @onclick="ReloadAfterUncertainAsync">@AgentsCopy.ReloadList</button>
                    </div>
                }
            }
        </ConfirmDialog>
    </div>
```

`src/TechStrap.Admin/Features/Settings/Agents/AgentsContent.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Features.Settings.Agents;

/// <summary>
/// The agents (Admin only): who has access, with the role as a read-only badge (roles come from the identity provider's groups, D-041) and one action, activate or deactivate. Activating needs no
/// confirmation; deactivating is a medium-tier confirmation that names the agent. A 409 <c>last-active-admin</c> is shown inside the dialog, in the API's words, and changes nothing. Writes use
/// <see cref="CancellationToken.None"/>, never retry, and say so when the outcome is unknown. When an admin deactivates their own account the session is asked again, so the shell shows what the API now says.
/// </summary>
public sealed partial class AgentsContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<AgentRowViewModel> _rows = [];
    private AgentRowViewModel? _deactivating;
    private string? _error;
    private string? _rowError;
    private string? _dialogError;
    private int _page = 1;
    private int _total;
    private int _loadedPage;
    private bool _loading = true;
    private bool _busy;
    private bool _uncertain;
    private bool _rowUncertain;
    private bool _disposed;

    [Inject]
    private IAgentsClient Agents { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "page")]
    public string? PageNumber { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        _page = int.TryParse(PageNumber, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 1;
        if (_loadedPage != _page)
        {
            _loadedPage = _page;
            await LoadAsync();
        }
    }

    private bool IsSelf(AgentRowViewModel row) => Session.Agent?.Id == row.Id;

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            var result = await Agents.ListPageAsync(_page, AgentsCopy.PageSize, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Items.Select(AgentRowViewModel.From)];
                _total = result.Value.TotalCount;
            }
            else
            {
                _error = $"{AgentsCopy.LoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ReloadAsync()
    {
        _rowError = null;
        _rowUncertain = false;
        await LoadAsync();
    }

    private void OnPageChanged(int page) => Navigation.NavigateTo(page <= 1 ? "/settings/agents" : $"/settings/agents?page={page}");

    // ---- activate: no confirmation -------------------------------------------------------------------------------

    private async Task ActivateAsync(AgentRowViewModel row)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _rowError = null;
        _rowUncertain = false;
        try
        {
            var result = await Agents.SetActiveAsync(row.Id, true, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                Replace(row, result.Value);
                StatusMessages.Show(AgentsCopy.Activated(row.DisplayName));
            }
            else if (ApiErrorCodes.IsUncertainWrite(result.Errors[0].Code))
            {
                _rowError = AgentsCopy.ActivateUncertain(row.DisplayName);
                _rowUncertain = true;
            }
            else
            {
                _rowError = AgentsCopy.ActivateFailed(row.DisplayName, result.Errors[0].Message);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- deactivate: a confirmation naming the agent -------------------------------------------------------------

    private void AskDeactivate(AgentRowViewModel row)
    {
        _dialogError = null;
        _uncertain = false;
        _rowError = null;
        _deactivating = row;
    }

    private void CancelDeactivate()
    {
        if (!_busy)
        {
            _deactivating = null;
            _dialogError = null;
            _uncertain = false;
        }
    }

    private async Task ConfirmDeactivateAsync()
    {
        if (_busy || _deactivating is not { } row)
        {
            return;
        }

        _busy = true;
        _dialogError = null;
        _uncertain = false;
        StateHasChanged();
        try
        {
            var result = await Agents.SetActiveAsync(row.Id, false, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                Replace(row, result.Value);
                _deactivating = null;
                StatusMessages.Show(AgentsCopy.Deactivated(row.DisplayName));
                if (IsSelf(row))
                {
                    // The API now refuses this account: ask again, so the shell shows the no-access page instead of a working app.
                    await Session.ReloadAsync(CancellationToken.None);
                }

                return;
            }

            await ShowDeactivateFailureAsync(result.Errors[0]);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ShowDeactivateFailureAsync(ResultError error)
    {
        if (error.Code == ApiErrorCodes.AgentNotFound)
        {
            _deactivating = null;
            StatusMessages.Show(AgentsCopy.AgentGone);
            await LoadAsync();
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _dialogError = AgentsCopy.DeactivateUncertain;
            _uncertain = true;
        }
        else if (error.Code == ApiErrorCodes.LastActiveAdmin)
        {
            // The API's own sentence says what to do (make another admin active first); nothing was changed.
            _dialogError = error.Message;
        }
        else
        {
            _dialogError = AgentsCopy.DeactivateFailed(error.Message);
        }
    }

    private async Task ReloadAfterUncertainAsync()
    {
        _deactivating = null;
        _dialogError = null;
        _uncertain = false;
        await LoadAsync();
    }

    private void Replace(AgentRowViewModel row, TechStrap.Contracts.Agents.AgentDto agent) =>
        _rows = [.. _rows.Select(r => r.Id == row.Id ? r.With(agent) : r)];

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

2. My settings:

`src/TechStrap.Admin/Features/Account/MySettingsCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Account;

/// <summary>The copy of My settings: alert, keyboard, theme and public display name. The example and the helper line of the display name come from UX-BRIEF-admin word for word.</summary>
public static class MySettingsCopy
{
    public const int PublicNameMaxLength = 60;

    public const string Heading = "My settings";

    public const string AlertsHeading = "Email alerts";
    public const string AlertsHelp = "Get an email when a new ticket arrives for a product.";
    public const string NoProducts = "No active products yet";
    public const string AlertsLoading = "Loading your alert settings";
    public const string AlertsLoadFailed = "Couldn't load your alert settings.";
    public const string AlertsSaved = "Saved your alert settings";
    public const string AlertsUncertain = "Couldn't confirm that your alert settings were saved. Reload to see what is saved.";
    public const string Reload = "Reload";

    public const string KeyboardHeading = "Keyboard shortcuts";
    public const string KeyboardLabel = "Keyboard shortcuts";
    public const string KeyboardHelp = "Single-key shortcuts such as j, k, r and u. This is remembered in this browser.";
    public const string ThemeHeading = "Theme";
    public const string ThemeHelp = "Remembered in this browser.";
    public const string ThemeAuto = "Auto (follows your device)";
    public const string ThemeLight = "Light";
    public const string ThemeDark = "Dark";

    public const string NameHeading = "Public display name";
    public const string NameLabel = "Public display name (optional)";
    public const string NameHelp = "Customers never see your email address.";
    public const string NameSaved = "Saved.";
    public const string NameTooLong = "Use 60 characters or fewer.";
    public const string NameInvalid = "Use a name without @, so it can't be mistaken for an email address.";
    public const string NameUncertain = "The save may have gone through. Reload the page to see what is saved before you try again.";

    // The placeholder stands in for a product when none is active yet.
    public const string GenericProduct = "[product]";

    public static string AlertLabel(string product) => $"New tickets in {product}";

    public static string AlertsFailed(string reason) => $"Couldn't save your alert settings. Nothing was changed. {reason}";

    public static string NameFailed(string reason) => $"Couldn't save your public display name. Nothing was changed. {reason}";

    public static string Preview(string line) => $"Customers see: {line}";
}
```

`src/TechStrap.Admin/Features/Account/PublicNamePreview.cs`

```csharp
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Features.Account;

/// <summary>
/// The line under the public display name: what a customer will read as the agent's name. It uses the Contracts format constants, so the preview and the emails cannot drift apart.
/// The name is what was typed, or the first word of the agent's profile name (never one that is an email address), or, with neither, the product's plain support name.
/// Without an active product the product is shown as a placeholder.
/// </summary>
public static class PublicNamePreview
{
    public static string Build(string? typed, string? profileName, string? productDisplayName)
    {
        var product = string.IsNullOrWhiteSpace(productDisplayName) ? MySettingsCopy.GenericProduct : productDisplayName.Trim();
        var given = !string.IsNullOrWhiteSpace(typed) ? typed.Trim() : FirstWord(profileName);
        return given is null
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture, AgentPublicName.FallbackFormat, product)
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, AgentPublicName.Format, given, product);
    }

    /// <summary>The first word of the profile name; null when there is none or it contains an @ (an identity provider may send an email address as the name).</summary>
    public static string? FirstWord(string? profileName)
    {
        var word = profileName?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return word is not null && word.Contains('@') ? null : word;
    }
}
```

`src/TechStrap.Admin/Features/Account/MyProfileViewModel.cs`

```csharp
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Features.Account;

/// <summary>The public display name as typed. The same two rules as the API (60 characters at most, no @), checked before anything is sent; blank clears the name.</summary>
internal sealed class MyProfileViewModel
{
    public string PublicDisplayName { get; set; } = string.Empty;

    /// <summary>What would be saved: the trimmed text, or null to clear.</summary>
    public string? Normalized => string.IsNullOrWhiteSpace(PublicDisplayName) ? null : PublicDisplayName.Trim();

    public string? Check() => Normalized switch
    {
        null => null,
        { Length: > MySettingsCopy.PublicNameMaxLength } => MySettingsCopy.NameTooLong,
        var name when name.Contains('@') => MySettingsCopy.NameInvalid,
        _ => null,
    };

    public UpdateMyProfileRequest ToRequest() => new(Normalized);
}
```

`src/TechStrap.Admin/Features/Account/PublicDisplayNameField.razor`

```razor
<div class="ts-field ts-public-name">
    <label for="ts-public-name" class="form-label">@MySettingsCopy.NameLabel</label>
    <input id="ts-public-name" type="text" class="form-control @(_error is null ? null : "is-invalid")" value="@_model.PublicDisplayName" autocomplete="off" spellcheck="false"
           disabled="@_saving" aria-invalid="@(_error is null ? null : "true")" aria-describedby="ts-public-name-preview ts-public-name-help"
           @oninput="OnInput" @onblur="CommitAsync" @onkeydown="OnKeyDownAsync" />
    <p id="ts-public-name-preview" class="ts-public-name-preview">@MySettingsCopy.Preview(PreviewLine)</p>
    <p id="ts-public-name-help" class="ts-field-help">@MySettingsCopy.NameHelp</p>
    @if (_error is not null)
    {
        <p id="ts-public-name-error" class="ts-field-error" role="alert">@_error</p>
    }
    @if (_saved)
    {
        <p class="ts-field-saved" role="status">@MySettingsCopy.NameSaved</p>
    }
</div>
```

`src/TechStrap.Admin/Features/Account/PublicDisplayNameField.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Features.Account;

/// <summary>
/// The optional name customers see in place of the agent's own (D-024). It shows a live preview, checks the two rules before it sends anything, and saves on blur or Enter, only when the text
/// changed since the last save (so Enter followed by blur saves once). A save is never cancelled and never retried; after it the session is asked again, and because the session keeps its
/// agent while it reloads (it never drops to "not loaded"), the page does not flicker or lose what is on it.
/// </summary>
public sealed partial class PublicDisplayNameField : IDisposable
{
    private readonly MyProfileViewModel _model = new();
    private string _committed = string.Empty;
    private string? _error;
    private bool _saving;
    private bool _saved;
    private bool _disposed;

    [Inject]
    private IAgentsClient Agents { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    /// <summary>The display name of the first active product, for the preview; null when there is none yet.</summary>
    [Parameter]
    public string? ProductDisplayName { get; set; }

    private string PreviewLine => PublicNamePreview.Build(_model.PublicDisplayName, Session.Agent?.Name, ProductDisplayName);

    protected override void OnInitialized()
    {
        _model.PublicDisplayName = Session.Agent?.PublicDisplayName ?? string.Empty;
        _committed = _model.Normalized ?? string.Empty;
    }

    private void OnInput(ChangeEventArgs e)
    {
        _model.PublicDisplayName = e.Value?.ToString() ?? string.Empty;
        _saved = false;
        _error = null;
    }

    private Task OnKeyDownAsync(KeyboardEventArgs e) => e.Key == "Enter" ? CommitAsync() : Task.CompletedTask;

    private async Task CommitAsync()
    {
        if (_saving)
        {
            return;
        }

        _error = _model.Check();
        if (_error is not null || (_model.Normalized ?? string.Empty) == _committed)
        {
            return;
        }

        _saving = true;
        _saved = false;
        try
        {
            var request = _model.ToRequest();
            var result = await Agents.UpdateMyProfileAsync(request, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                var error = result.Errors[0];
                _error = ApiErrorCodes.IsUncertainWrite(error.Code)
                    ? MySettingsCopy.NameUncertain
                    : error.Target == ApiFields.PublicDisplayName ? error.Message : MySettingsCopy.NameFailed(error.Message);
                return;
            }

            _committed = request.PublicDisplayName ?? string.Empty;
            _model.PublicDisplayName = _committed;
            _saved = true;

            // The agent's own record carries the name, so ask again; the session keeps the current agent until the answer arrives.
            await Session.ReloadAsync(CancellationToken.None);
        }
        finally
        {
            _saving = false;
        }
    }

    public void Dispose() => _disposed = true;
}
```

`src/TechStrap.Admin/Features/Account/NotificationPreferencesPage.razor`

```razor
@page "/account/notifications"
@attribute [Authorize]

<PageTitle>@MySettingsCopy.Heading</PageTitle>
<div class="ts-settings ts-account">
    <h1>@MySettingsCopy.Heading</h1>

    <section aria-labelledby="ts-alerts-heading">
        <h2 id="ts-alerts-heading">@MySettingsCopy.AlertsHeading</h2>
        <p class="ts-field-help">@MySettingsCopy.AlertsHelp</p>
        @if (_loadingPrefs)
        {
            <LoadingState Rows="2" Label="@MySettingsCopy.AlertsLoading" />
        }
        else if (_prefsError is not null)
        {
            <ErrorState Message="@_prefsError" OnRetry="LoadPreferencesAsync" />
        }
        else if (_prefs.Count == 0)
        {
            <EmptyState Heading="@MySettingsCopy.NoProducts" />
        }
        else
        {
            @if (_saveError is not null)
            {
                <div class="ts-conflict" role="alert">
                    <p>@_saveError</p>
                    @if (_saveUncertain)
                    {
                        <button type="button" class="btn btn-outline-secondary" @onclick="LoadPreferencesAsync">@MySettingsCopy.Reload</button>
                    }
                </div>
            }
            <ul class="ts-toggle-list">
                @foreach (var pref in _prefs)
                {
                    <li @key="(pref.ProductId, _version)" class="form-check">
                        <input id="@($"ts-alert-{pref.ProductId}")" class="form-check-input" type="checkbox" checked="@pref.NotifyNewTicket" disabled="@(_saving || _saveUncertain)"
                               @onchange="e => ToggleAsync(pref, e.Value is true)" />
                        <label class="form-check-label" for="@($"ts-alert-{pref.ProductId}")">@MySettingsCopy.AlertLabel(pref.ProductName)</label>
                    </li>
                }
            </ul>
        }
    </section>

    <section aria-labelledby="ts-keyboard-heading">
        <h2 id="ts-keyboard-heading">@MySettingsCopy.KeyboardHeading</h2>
        <div class="form-check">
            <input id="ts-keyboard" class="form-check-input" type="checkbox" checked="@Preferences.SingleKeyShortcuts" @onchange="OnKeyboardChangedAsync" />
            <label class="form-check-label" for="ts-keyboard">@MySettingsCopy.KeyboardLabel</label>
        </div>
        <p class="ts-field-help">@MySettingsCopy.KeyboardHelp</p>
    </section>

    <section aria-labelledby="ts-theme-heading">
        <h2 id="ts-theme-heading">@MySettingsCopy.ThemeHeading</h2>
        <fieldset class="ts-theme-choice">
            <legend class="visually-hidden">@MySettingsCopy.ThemeHeading</legend>
            @foreach (var (value, label) in Themes)
            {
                <div class="form-check">
                    <input id="@($"ts-theme-{ThemeId(value)}")" class="form-check-input" type="radio" name="ts-theme" value="@ThemeId(value)" checked="@(Preferences.Theme == value)"
                           @onchange="() => OnThemeChangedAsync(value)" />
                    <label class="form-check-label" for="@($"ts-theme-{ThemeId(value)}")">@label</label>
                </div>
            }
        </fieldset>
        <p class="ts-field-help">@MySettingsCopy.ThemeHelp</p>
    </section>

    <section aria-labelledby="ts-name-heading">
        <h2 id="ts-name-heading">@MySettingsCopy.NameHeading</h2>
        <PublicDisplayNameField ProductDisplayName="@_productDisplayName" />
    </section>
</div>

@code {
    private static string ThemeId(ThemeChoice theme) => theme.ToString().ToLowerInvariant();
}
```

`src/TechStrap.Admin/Features/Account/NotificationPreferencesPage.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Features.Account;

/// <summary>
/// My settings (any agent): which products email the agent about new tickets, the single-key shortcut switch and the theme (both kept in this browser through <see cref="PreferencesService"/>), and the
/// public display name. Every alert toggle sends the full set of products, so the API never has to guess about the ones left out, then shows a status message. A failed save puts the toggle back
/// and says nothing changed; a lost answer says the outcome is unknown and offers a reload. Toggles are inert while a save runs, so two saves never overlap. Writes use <see cref="CancellationToken.None"/>.
/// </summary>
public sealed partial class NotificationPreferencesPage : IDisposable
{
    private static readonly (ThemeChoice Value, string Label)[] ThemeChoices =
    [
        (ThemeChoice.Auto, MySettingsCopy.ThemeAuto),
        (ThemeChoice.Light, MySettingsCopy.ThemeLight),
        (ThemeChoice.Dark, MySettingsCopy.ThemeDark),
    ];

    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<NotificationPreferenceDto> _prefs = [];
    private string? _prefsError;
    private string? _saveError;
    private string? _productDisplayName;
    private int _version;
    private bool _loadingPrefs = true;
    private bool _saving;
    private bool _saveUncertain;
    private bool _disposed;

    [Inject]
    private IAgentsClient Agents { get; set; } = default!;

    [Inject]
    private IProductsClient Products { get; set; } = default!;

    [Inject]
    private PreferencesService Preferences { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    private static (ThemeChoice Value, string Label)[] Themes => ThemeChoices;

    protected override async Task OnInitializedAsync()
    {
        Preferences.Changed += OnPreferencesChanged;
        await Task.WhenAll(LoadPreferencesAsync(), LoadProductAsync());
    }

    private void OnPreferencesChanged() => _ = InvokeAsync(StateHasChanged);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // The browser's stored choices exist only on the client, so they are read after the first interactive render (the service loads once and never throws).
            await Preferences.LoadAsync();
        }
    }

    private async Task LoadPreferencesAsync()
    {
        _loadingPrefs = true;
        _prefsError = null;
        _saveError = null;
        _saveUncertain = false;
        try
        {
            var result = await Agents.GetNotificationPreferencesAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _prefs = result.Value;
                _version++;
            }
            else
            {
                _prefsError = $"{MySettingsCopy.AlertsLoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            _loadingPrefs = false;
        }
    }

    // The preview uses the first active product (by name, so the choice is stable). A failed lookup only means the generic preview.
    private async Task LoadProductAsync()
    {
        var result = await Products.ListAsync(_lifetime.Token);
        if (_lifetime.IsCancellationRequested || result.IsFailure)
        {
            return;
        }

        _productDisplayName = result.Value.Where(p => p.IsActive).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => p.Branding.DisplayName).FirstOrDefault();
    }

    private async Task ToggleAsync(NotificationPreferenceDto changed, bool value)
    {
        if (_saving || _saveUncertain)
        {
            return;
        }

        _saving = true;
        _saveError = null;
        try
        {
            // The full set, with the one change: nothing is left for the API to guess.
            var next = _prefs.Select(p => p.ProductId == changed.ProductId ? p with { NotifyNewTicket = value } : p).ToList();
            var request = new UpdateNotificationPreferencesRequest([.. next.Select(p => new NotificationPreferenceUpdateDto(p.ProductId, p.NotifyNewTicket))]);
            var result = await Agents.UpdateNotificationPreferencesAsync(request, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _prefs = next;
                StatusMessages.Show(MySettingsCopy.AlertsSaved);
                return;
            }

            ShowSaveFailure(result.Errors[0]);
        }
        finally
        {
            _saving = false;
        }
    }

    private void ShowSaveFailure(ResultError error)
    {
        // A new key makes Blazor draw the checkbox again from what is saved, so a toggle the agent clicked does not stay ticked on screen when nothing was changed.
        _version++;
        if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _saveError = MySettingsCopy.AlertsUncertain;
            _saveUncertain = true;
        }
        else
        {
            _saveError = MySettingsCopy.AlertsFailed(error.Message);
        }
    }

    private Task OnKeyboardChangedAsync(ChangeEventArgs e) => Preferences.SetSingleKeyShortcutsAsync(e.Value is true);

    private Task OnThemeChangedAsync(ThemeChoice theme) => Preferences.SetThemeAsync(theme);

    public void Dispose()
    {
        Preferences.Changed -= OnPreferencesChanged;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

3. The styles:

`src/TechStrap.Admin/Styles/_agents.scss`

```scss
// Agents page (PHASE-07b): the read-only role badge. The role is the word; the border style only repeats it (solid for Admin, dashed for Agent).

.ts-role {
  display: inline-block;
  padding: 1px 8px;
  font: 600 .6875rem var(--ts-font-mono);
  letter-spacing: .04em;
  text-transform: uppercase;
  color: var(--ink);
  background: var(--sheet);
  border: 2px solid var(--ink);
}

.ts-role--agent {
  border-style: dashed;
}

.ts-you {
  color: var(--ink-2);
}
```

`src/TechStrap.Admin/Styles/_account.scss`

```scss
// My settings (PHASE-07b): the toggle lists and the public display name preview.

.ts-account {
  section {
    margin-bottom: 8px;
  }
}

.ts-toggle-list {
  padding: 0;
  margin: 8px 0;
  list-style: none;

  li {
    margin-bottom: 6px;
  }
}

.ts-theme-choice {
  min-width: 0;
  padding: 0;
  margin: 8px 0;
  border: 0;
}

.ts-public-name {
  max-width: 560px;
}

.ts-public-name-preview {
  margin: 6px 0 0;
  font: 500 .8125rem var(--ts-font-mono);
  overflow-wrap: anywhere;
}

.ts-field-saved {
  margin: 4px 0 0;
  padding-left: 8px;
  font-size: .8125rem;
  border-left: 3px solid var(--st-open);
}
```

```diff
@@
 @import "api-keys";
+@import "agents";
+@import "account";
 @import "styleguide";
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "AgentsPageTests|NotificationPreferencesPageTests|PublicDisplayNameFieldTests|PublicNamePreviewTests|SourceEncodingTests|SourceEscapeTests"`
Expected: PASS. To see the pins guard (do not commit), make each change, rerun the same filter, expect the failures named, and restore:
- `AgentsContent.AskDeactivate`: add `_ = ConfirmDeactivateAsync();` after `_deactivating = row;`: six `AgentsPageTests` fail (the dialog never asks, a cancel still writes).
- `NotificationPreferencesPage.ToggleAsync`: build the request from only the changed product: `A_toggle_sends_the_full_set_...`, `A_second_toggle_carries_...` and `A_failed_save_puts_the_toggle_back_...` fail.
- `PublicDisplayNameField.CommitAsync`: remove `|| (_model.Normalized ?? string.Empty) == _committed`: `Enter_saves_and_the_blur_that_follows_does_not_save_a_second_time` and `Blur_without_a_change_saves_nothing_...` fail.

- [ ] **Step 5: Whole-project check**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), `dotnet test --project tests/TechStrap.Admin.Tests -c Release`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Admin/Features/Settings/Agents \
  src/TechStrap.Admin/Features/Account \
  src/TechStrap.Admin/Styles \
  tests/TechStrap.Admin.Tests
git diff --cached --stat
git commit -m "feat(admin): agents page and My settings with the public display name (P07-T16, P07-T23)" -m "Roles are read-only badges; activate and deactivate are the only agent actions, and deactivate confirms. My settings saves each alert toggle with the full set and the display name on blur or Enter." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 8: Tags page and audit page with `AdminEventSummaryFactory` (P07-T17)

**Review Focus pin (5):** deleting a tag in use without its typed name, or forcing the delete of an unused tag, or firing a delete twice. Pinned by `DeleteTagTests` (the whole class).

**Files:**
- Create: `src/TechStrap.Admin/Features/Settings/EmailKinds.cs`
- Create: `src/TechStrap.Admin/Features/Settings/Tags/TagsCopy.cs`, `TagRowViewModel.cs`, `TagForm.cs`, `TagsContent.razor` and `.razor.cs`, `TagsPage.razor`
- Create: `src/TechStrap.Admin/Features/Settings/Audit/AdminEventSummaryFactory.cs`, `AuditCopy.cs`, `AuditFilters.cs`, `AdminEventRowViewModel.cs`, `AdminEventsContent.razor` and `.razor.cs`, `AdminEventsPage.razor`
- Create: `src/TechStrap.Admin/Styles/_tags.scss`, `_audit.scss`; Modify: `src/TechStrap.Admin/Styles/app.scss`
- Create: `tests/TechStrap.Admin.Tests/Support/TestData.Tags.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/TagsPageTests.cs`, `DeleteTagTests.cs`, `AdminEventSummaryFactoryTests.cs`, `AdminEventsPageTests.cs`

**Interfaces:**
- Consumes:
  - `ITagsClient.ListSummaryAsync(ct)` (`Result<IReadOnlyList<TagSummaryDto>>`), `CreateAsync(CreateTagRequest, ct)`, `UpdateAsync(Guid, UpdateTagRequest, ct)` (`Result<TagDto>`), `DeleteAsync(Guid id, bool force, ct)` (`Result`); `IAdminEventsClient.ListAsync(AdminEventFilter, int page, int pageSize, ct)` with `AdminEventFilter(string? SubjectType = null, Guid? ActorId = null, DateTimeOffset? AsOf = null)`; `IAgentsClient.ListAllAsync` (the actor filter); `ApiErrorCodes.TagSlugTaken`, `TagInUse`, `TagNotFound`, `IsUncertainWrite`; `ApiFields.Name`, `Slug`, `Colour`.
  - `TagSummaryDto`, `AdminEventTypes` (12 constants), `AdminSubjectTypes` (7), `BrandingRules.ColourPattern` (Task 2); `AdminOnly`, `ConfirmDialog` (`RequiredText` for the typed name), `TagChip`, `PagerControl`, `RelativeTime`, `TimeProvider`.
  - Test support from Task 5: `AdminPageTest`.
- Produces:

```csharp
// TechStrap.Admin.Features.Settings.Tags
public sealed partial class TagsPage / TagsContent : IDisposable       // /settings/tags
public static class TagsCopy                                           // DefaultColour "#4B5563"
// TechStrap.Admin.Features.Settings.Audit
public sealed partial class AdminEventsPage / AdminEventsContent : IDisposable   // /settings/audit?subject=&actor=&page=
public static class AdminEventSummaryFactory     // string Summarize(string type, string payload); string SubjectLabel(string subjectType)
public static class AuditFilters                 // Subjects, CanonicalSubject, Uri(subject, actor, page)
public static class AuditCopy                    // PageSize = 25
// TechStrap.Admin.Features.Settings
public static class EmailKinds                   // the seven outbox kinds and Label(kind); also used by the failed-emails page (Task 9)
```

**Rules:**
1. **Counts come from the summary.** The list reads `GET api/tags/summary` through `ITagsClient.ListSummaryAsync` and shows each tag's ticket count; the agent tag picker and the queue filters keep using `ITagsClient.ListAsync` (`TagDto`) untouched.
2. **Delete is two flows.** *Unused* (`TicketCount == 0`): a medium confirmation naming the tag ("Delete the tag {name}?", "No tickets use this tag. This can't be undone."), no typed text, `force=false`. *In use*: an irreversible one: danger style, the count as "{n} tickets" ("1 ticket"), the body "This tag is on {n} tickets. Deleting it removes it from all of them, and each ticket records the change. This can't be undone.", the tag's name typed (`ConfirmDialog.RequiredText`, so Confirm stays disabled until it matches, ignoring case), and then `force=true`. `force` is never true for a tag the list shows as unused. Cancel and Esc make no call; a double click sends once (`_deleteBusy` and the dialog's `Busy`).
3. **A stale count is caught by the API.** A 409 `tag-in-use` for a tag the list showed as unused reads the list again and keeps the dialog open with "This tag was just added to tickets. The count above is updated: type the name to delete it anyway." The dialog is built from the row looked up fresh by id, so after the reload it shows the new count and asks for the name.
4. **Other outcomes.** `tag-not-found` closes the dialog, says "That tag no longer exists." and reloads; another failure: "Couldn't delete the tag. Nothing was changed. {message}"; a lost answer disables Confirm, says the delete may have gone through and offers Reload list (never "nothing was changed"). Writes use `CancellationToken.None`; a finish after the page is gone changes nothing.
5. **Create and edit.** The slug follows the name ("Billing issue" gives "billing-issue") until the agent edits it. The colour is checked with `BrandingRules.ColourPattern`; name at most 50, slug at most 40 with the server's pattern. A 409 `tag-slug-taken` is a field error on the slug ("Another tag already uses this slug."); a 400 maps by its kebab-case target (`ApiFields`). Edit is in the row (name and colour; the slug is permanent); the count the list showed stays.
6. **The audit log is read-only and never renders a payload.** `AdminEventSummaryFactory` reads ids, slugs, prefixes, kinds and counts only, shortens every value to 60 characters, replaces control characters, and never returns the payload; the sentence is plain text that Razor encodes. All 12 `AdminEventTypes` have their own sentence; an unknown type is shown as words ("Something new"), and no payload shape (not JSON, an array, wrong kinds, huge numbers, hostile text) throws or echoes. `AdminEventSummaryFactoryTests` fails when a new constant has no sample.
7. **Filters and paging.** Subject type and actor, both in the query string; a subject that is not one of the seven (case-insensitive) or an actor that is not a Guid is dropped, never sent. Changing a filter navigates with the other filter kept and the page dropped. The first page of a filter fixes `asOf` from the clock; later pages of the same filter send the same `asOf`; page 1 again, a changed filter, Refresh, or a page opened directly takes a new one. A failed agent list only means the actor filter has no names.
8. **Thin shells.** `TagsPage` and `AdminEventsPage` are `<AdminOnly><...Content/></AdminOnly>`; all loading, including the agent list, is in the content.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/Support/TestData.Tags.cs`

```csharp
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static TagSummaryDto TagSummary(string name = "bug", int count = 0, string? slug = null, string colour = "#DC2626", Guid? id = null) =>
        new(id ?? Guid.NewGuid(), slug ?? name.ToLowerInvariant().Replace(' ', '-'), name, colour, count);

    public static AdminEventDto AdminEvent(
        string type = AdminEventTypes.TagCreated,
        string payload = "{\"slug\":\"bug\"}",
        string subjectType = AdminSubjectTypes.Tag,
        string? actor = "Ada Admin",
        DateTimeOffset? at = null,
        Guid? id = null) => new(id ?? Guid.NewGuid(), type, AdaAgentId, actor, subjectType, Guid.NewGuid(), payload, at ?? Now.AddMinutes(-5));
}
```

`tests/TechStrap.Admin.Tests/Components/TagsPageTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Tags;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The tag list with its ticket counts, create, and inline rename and recolour. Delete has its own class (Review Focus 5).</summary>
public sealed class TagsPageTests : AdminPageTest
{
    private readonly ITagsClient _tags = Substitute.For<ITagsClient>();

    public TagsPageTests()
    {
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<TagSummaryDto>>(
        [
            TestData.TagSummary("urgent", 12, id: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000010")),
            TestData.TagSummary("bug", 1, id: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000011")),
            TestData.TagSummary("Billing issue", 0, "billing-issue", "#1D4ED8", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000012")),
        ]));
        _tags.CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            TestData.Ok(new TagDto(Guid.NewGuid(), call.Arg<CreateTagRequest>().Slug!, call.Arg<CreateTagRequest>().Name!, call.Arg<CreateTagRequest>().Colour!.ToUpperInvariant())));
        _tags.UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            TestData.Ok(new TagDto(call.Arg<Guid>(), "x", call.Arg<UpdateTagRequest>().Name!, call.Arg<UpdateTagRequest>().Colour!.ToUpperInvariant())));
        Services.AddSingleton(_tags);
    }

    private IReadOnlyList<CreateTagRequest> Creates() =>
        [.. _tags.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITagsClient.CreateAsync)).Select(c => (CreateTagRequest)c.GetArguments()[0]!)];

    private IReadOnlyList<UpdateTagRequest> Updates() =>
        [.. _tags.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITagsClient.UpdateAsync)).Select(c => (UpdateTagRequest)c.GetArguments()[1]!)];

    private static string Value(IRenderedComponent<TagsPage> cut, string id) => cut.Find($"#{id}").GetAttribute("value")!;

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<TagsPage> cut, string slug) => cut.Find($"tr[data-tag='{slug}']");

    // ---- the list ------------------------------------------------------------------------------------------------

    [Fact]
    public void Tags_are_listed_by_name_with_a_chip_the_slug_and_the_ticket_count()
    {
        var cut = RenderPage();

        cut.FindAll("tbody tr").Select(r => r.Children[0].TextContent.Trim()).ShouldBe(["Billing issue", "bug", "urgent"]);
        Row(cut, "urgent").QuerySelector(".ts-tag-count")!.TextContent.ShouldBe("12");
        Row(cut, "bug").QuerySelector(".ts-tag-count")!.TextContent.ShouldBe("1");
        Row(cut, "billing-issue").QuerySelector(".ts-tag-count")!.TextContent.ShouldBe("0");
        Row(cut, "urgent").QuerySelector("code")!.TextContent.ShouldBe("urgent");
        _tags.Received(1).ListSummaryAsync(Arg.Any<CancellationToken>());
        _tags.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<TagsPage> RenderPage() => Render<TagsPage>();

    [Fact]
    public void No_tags_is_a_plain_empty_state_and_the_create_form_is_still_there()
    {
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagSummaryDto>>([]));

        var cut = RenderPage();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No tags yet");
        cut.Find("form.ts-tag-create").ShouldNotBeNull();
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<IReadOnlyList<TagSummaryDto>>("api-error", "The API is unavailable."),
            TestData.Ok<IReadOnlyList<TagSummaryDto>>([TestData.TagSummary("bug")]));
        var cut = RenderPage();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the tags. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_the_api_is_not_asked()
    {
        AsAgent();

        var cut = RenderPage();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("form.ts-tag-create").ShouldBeEmpty();
        _tags.ReceivedCalls().ShouldBeEmpty();
    }

    // ---- create --------------------------------------------------------------------------------------------------

    [Fact]
    public void The_slug_follows_the_name_until_the_agent_edits_it()
    {
        var cut = RenderPage();

        cut.Find("#ts-tag-name").Input("Billing issue");
        Value(cut, "ts-tag-slug").ShouldBe("billing-issue");

        cut.Find("#ts-tag-slug").Input("billing");
        cut.Find("#ts-tag-name").Input("Billing problem");
        Value(cut, "ts-tag-slug").ShouldBe("billing");
    }

    [Fact]
    public void Creating_sends_the_trimmed_values_adds_the_tag_with_no_tickets_and_says_so()
    {
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("  Refund  ");
        cut.Find("#ts-tag-colour").Input("#0f766e");

        cut.Find("form.ts-tag-create").Submit();

        Creates().ShouldHaveSingleItem().ShouldBe(new CreateTagRequest("refund", "Refund", "#0f766e"));
        _tags.Received(1).CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Row(cut, "refund").QuerySelector(".ts-tag-count")!.TextContent.ShouldBe("0");
        StatusMessages.Current.ShouldBe("Created the tag Refund");
        Value(cut, "ts-tag-name").ShouldBe(string.Empty);
        Value(cut, "ts-tag-slug").ShouldBe(string.Empty);
        Value(cut, "ts-tag-colour").ShouldBe("#4B5563");
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void A_colour_that_is_not_hash_and_six_hex_digits_is_refused_with_the_shared_constant_and_never_sent(string colour)
    {
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("Refund");
        cut.Find("#ts-tag-colour").Input(colour);

        cut.Find("form.ts-tag-create").Submit();

        cut.Find("#ts-tag-colour-error").TextContent.ShouldBe("Use a colour like #1D4ED8: a # and six hex digits.");
        Creates().ShouldBeEmpty();
    }

    [Fact]
    public void A_missing_name_or_a_bad_slug_blocks_the_create_with_a_field_error_each()
    {
        var cut = RenderPage();
        cut.Find("#ts-tag-slug").Input("Bad Slug");

        cut.Find("form.ts-tag-create").Submit();

        cut.Find("#ts-tag-name-error").TextContent.ShouldBe("Enter a name.");
        cut.Find("#ts-tag-slug-error").TextContent.ShouldBe("Use lower-case letters, numbers and single hyphens, up to 40 characters.");
        Creates().ShouldBeEmpty();
    }

    [Fact]
    public void A_duplicate_slug_409_shows_a_field_error_on_the_slug_and_keeps_every_value()
    {
        _tags.CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<TagDto>(ApiErrorCodes.TagSlugTaken, "A tag with this slug already exists.", ResultErrorKind.Conflict));
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("Bug");

        cut.Find("form.ts-tag-create").Submit();

        cut.Find("#ts-tag-slug-error").TextContent.ShouldBe("Another tag already uses this slug.");
        Value(cut, "ts-tag-name").ShouldBe("Bug");
        Value(cut, "ts-tag-slug").ShouldBe("bug");
        StatusMessages.Current.ShouldBeNull();
        cut.FindAll("tbody tr").Count.ShouldBe(3);
    }

    [Theory]
    [InlineData("name", "#ts-tag-name-error")]
    [InlineData("slug", "#ts-tag-slug-error")]
    [InlineData("colour", "#ts-tag-colour-error")]
    public void A_400_names_its_field_in_kebab_case_and_it_appears_there(string target, string selector)
    {
        _tags.CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<TagDto>.Failure(new ResultError("x-invalid", "The server says no.", ResultErrorKind.Validation, target)));
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("Refund");

        cut.Find("form.ts-tag-create").Submit();

        cut.Find(selector).TextContent.ShouldBe("The server says no.");
    }

    [Fact]
    public void A_double_submit_creates_one_tag()
    {
        var gate = new TaskCompletionSource<Result<TagDto>>();
        _tags.CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("Refund");

        cut.Find("form.ts-tag-create").Submit();
        cut.Find("form.ts-tag-create").Submit();

        Creates().Count.ShouldBe(1);
        gate.SetResult(TestData.Ok(new TagDto(Guid.NewGuid(), "refund", "Refund", "#4B5563")));
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(4));
    }

    [Fact]
    public void A_lost_create_answer_never_claims_nothing_changed_and_offers_a_reload()
    {
        _tags.CreateAsync(Arg.Any<CreateTagRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<TagDto>(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        cut.Find("#ts-tag-name").Input("Refund");

        cut.Find("form.ts-tag-create").Submit();

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("The change may have gone through.");
        alert.TextContent.ShouldNotContain("Nothing was changed");
        alert.QuerySelector("button")!.Click();
        _tags.Received(2).ListSummaryAsync(Arg.Any<CancellationToken>());
        Creates().Count.ShouldBe(1);
    }

    // ---- edit ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Edit_changes_the_name_and_colour_in_the_row_with_the_slug_read_only_and_keeps_the_count()
    {
        var cut = RenderPage();
        Row(cut, "urgent").QuerySelector("button.ts-edit")!.Click();

        var editing = cut.Find("tr.ts-tag-editing");
        editing.QuerySelector("code")!.TextContent.ShouldBe("urgent");
        Value(cut, "ts-edit-name").ShouldBe("urgent");
        cut.Find("#ts-edit-name").Input("Urgent now");
        cut.Find("#ts-edit-colour").Input("#1d4ed8");
        cut.Find("button.ts-save").Click();

        Updates().ShouldHaveSingleItem().ShouldBe(new UpdateTagRequest("Urgent now", "#1d4ed8"));
        cut.FindAll("tr.ts-tag-editing").ShouldBeEmpty();
        Row(cut, "urgent").QuerySelector(".ts-tag-count")!.TextContent.ShouldBe("12");
        StatusMessages.Current.ShouldBe("Saved the tag Urgent now");
    }

    [Fact]
    public void Cancel_leaves_the_row_as_it_was_and_sends_nothing()
    {
        var cut = RenderPage();
        Row(cut, "urgent").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-edit-name").Input("Changed");

        cut.Find("button.ts-cancel").Click();

        cut.FindAll("tr.ts-tag-editing").ShouldBeEmpty();
        Updates().ShouldBeEmpty();
        Row(cut, "urgent").TextContent.ShouldContain("urgent");
    }

    [Fact]
    public void An_invalid_edit_is_refused_before_it_is_sent_and_a_server_error_stays_in_the_row()
    {
        var cut = RenderPage();
        Row(cut, "urgent").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-edit-colour").Input("nope");
        cut.Find("button.ts-save").Click();
        cut.Find("#ts-edit-colour-error").TextContent.ShouldBe("Use a colour like #1D4ED8: a # and six hex digits.");
        Updates().ShouldBeEmpty();

        _tags.UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<TagDto>.Failure(new ResultError("name-too-long", "The server says no.", ResultErrorKind.Validation, "name")));
        cut.Find("#ts-edit-colour").Input("#1D4ED8");
        cut.Find("button.ts-save").Click();

        cut.Find("#ts-edit-name-error").TextContent.ShouldBe("The server says no.");
        cut.FindAll("tr.ts-tag-editing").Count.ShouldBe(1);
    }

    [Fact]
    public void Editing_a_tag_that_is_gone_says_so_and_reloads_the_list()
    {
        _tags.UpdateAsync(Arg.Any<Guid>(), Arg.Any<UpdateTagRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<TagDto>(ApiErrorCodes.TagNotFound, "No such tag.", ResultErrorKind.NotFound));
        var cut = RenderPage();
        Row(cut, "urgent").QuerySelector("button.ts-edit")!.Click();

        cut.Find("button.ts-save").Click();

        StatusMessages.Current.ShouldBe("That tag no longer exists.");
        _tags.Received(2).ListSummaryAsync(Arg.Any<CancellationToken>());
        cut.FindAll("tr.ts-tag-editing").ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/DeleteTagTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Tags;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Review Focus 5 for tags: a tag in use is never deleted without its name typed, and only then with force; an unused tag asks first and is never forced; nothing fires twice. The dialog shows the
/// ticket count from the list, and a tag that was tagged after the list was read is caught by the API's 409 and shown again with its new count.
/// </summary>
public sealed class DeleteTagTests : AdminPageTest
{
    private readonly ITagsClient _tags = Substitute.For<ITagsClient>();
    private readonly Guid _urgentId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000010");
    private readonly Guid _unusedId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000012");

    public DeleteTagTests()
    {
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<TagSummaryDto>>(
        [
            TestData.TagSummary("urgent", 12, id: _urgentId),
            TestData.TagSummary("Old idea", 0, "old-idea", id: _unusedId),
        ]));
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok());
        Services.AddSingleton(_tags);
    }

    private IRenderedComponent<TagsPage> RenderPage() => Render<TagsPage>();

    private IReadOnlyList<(Guid Id, bool Force)> Deletes() =>
        [.. _tags.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITagsClient.DeleteAsync)).Select(c => ((Guid)c.GetArguments()[0]!, (bool)c.GetArguments()[1]!))];

    private static void AskToDelete(IRenderedComponent<TagsPage> cut, string slug) => cut.Find($"tr[data-tag='{slug}'] button.ts-delete").Click();

    private static AngleSharp.Dom.IElement Dialog(IRenderedComponent<TagsPage> cut) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.StartsWith("Delete the tag ", StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<TagsPage> cut) => Dialog(cut).QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    private static void Type(IRenderedComponent<TagsPage> cut, string text) => Dialog(cut).QuerySelector("input")!.Input(text);

    // ---- a tag in use: typed name, count, force ------------------------------------------------------------------

    [Fact]
    public void A_tag_in_use_shows_its_ticket_count_asks_for_the_name_typed_and_calls_nothing_yet()
    {
        var cut = RenderPage();

        AskToDelete(cut, "urgent");

        var dialog = Dialog(cut);
        dialog.QuerySelector("h2")!.TextContent.ShouldBe("Delete the tag urgent?");
        dialog.QuerySelector(".ts-delete-count")!.TextContent.ShouldBe("12 tickets");
        dialog.TextContent.ShouldContain("This tag is on 12 tickets. Deleting it removes it from all of them, and each ticket records the change. This can't be undone.");
        dialog.QuerySelector("label")!.TextContent.ShouldBe("Type urgent to confirm");
        dialog.ClassName!.ShouldContain("ts-dialog--danger");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Dialogs.VerifyInvoke("open", 1);
        Deletes().ShouldBeEmpty();
    }

    [Fact]
    public void One_ticket_is_said_in_the_singular()
    {
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagSummaryDto>>([TestData.TagSummary("urgent", 1, id: _urgentId)]));
        var cut = RenderPage();

        AskToDelete(cut, "urgent");

        Dialog(cut).QuerySelector(".ts-delete-count")!.TextContent.ShouldBe("1 ticket");
    }

    [Fact]
    public void Without_the_name_typed_confirm_does_nothing_and_a_near_miss_is_not_enough()
    {
        var cut = RenderPage();
        AskToDelete(cut, "urgent");

        Confirm(cut).Click();
        Type(cut, "urgen");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Type(cut, "urgent now");
        Confirm(cut).Click();

        Deletes().ShouldBeEmpty();
        Dialogs.VerifyNotInvoke("close");
    }

    [Fact]
    public void Typing_the_name_in_any_case_deletes_once_with_force_removes_the_row_and_says_how_many_tickets_lost_it()
    {
        var cut = RenderPage();
        AskToDelete(cut, "urgent");
        Type(cut, "URGENT");

        Confirm(cut).Click();

        Deletes().ShouldBe([(_urgentId, true)]);
        _tags.Received(1).DeleteAsync(_urgentId, true, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        cut.FindAll("tr[data-tag='urgent']").ShouldBeEmpty();
        StatusMessages.Current.ShouldBe("Deleted the tag urgent and removed it from 12 tickets");
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_double_click_on_confirm_deletes_once_and_the_dialog_is_inert_while_it_runs()
    {
        var gate = new TaskCompletionSource<Result>();
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        AskToDelete(cut, "urgent");
        Type(cut, "urgent");

        Confirm(cut).Click();
        Dialog(cut).QuerySelectorAll("button, input").ShouldAllBe(e => e.HasAttribute("disabled"));
        Dialog(cut).TriggerEvent("oncancel", EventArgs.Empty);
        Confirm(cut).Click();

        Deletes().Count.ShouldBe(1);
        Dialogs.VerifyNotInvoke("close");
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => cut.FindAll("tr[data-tag='urgent']").ShouldBeEmpty());
    }

    [Fact]
    public void Cancel_and_Esc_close_the_dialog_and_make_no_call_and_reopening_starts_with_an_empty_box()
    {
        var cut = RenderPage();
        AskToDelete(cut, "urgent");
        Type(cut, "urgent");

        Dialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        AskToDelete(cut, "urgent");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue("the typed name from the first time is gone");
        Dialog(cut).TriggerEvent("oncancel", EventArgs.Empty);

        Deletes().ShouldBeEmpty();
        Dialogs.VerifyInvoke("close", 2);
        cut.FindAll("tr[data-tag='urgent']").Count.ShouldBe(1);
    }

    // ---- an unused tag: a medium confirmation, never forced ------------------------------------------------------

    [Fact]
    public void An_unused_tag_asks_first_with_no_typed_name_and_deletes_once_without_force()
    {
        var cut = RenderPage();

        AskToDelete(cut, "old-idea");

        var dialog = Dialog(cut);
        dialog.QuerySelector("h2")!.TextContent.ShouldBe("Delete the tag Old idea?");
        dialog.TextContent.ShouldContain("No tickets use this tag. This can't be undone.");
        dialog.QuerySelector("input").ShouldBeNull();
        dialog.ClassName!.ShouldNotContain("ts-dialog--danger");
        Deletes().ShouldBeEmpty();

        Confirm(cut).Click();

        Deletes().ShouldBe([(_unusedId, false)]);
        StatusMessages.Current.ShouldBe("Deleted the tag Old idea");
        cut.FindAll("tr[data-tag='old-idea']").ShouldBeEmpty();
    }

    [Fact]
    public void An_unused_tag_is_never_deleted_by_cancel_or_escape()
    {
        var cut = RenderPage();
        AskToDelete(cut, "old-idea");

        Dialog(cut).TriggerEvent("oncancel", EventArgs.Empty);
        AskToDelete(cut, "old-idea");
        Dialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();

        Deletes().ShouldBeEmpty();
    }

    // ---- when the list was stale ---------------------------------------------------------------------------------

    [Fact]
    public void A_tag_that_gained_tickets_since_the_list_was_read_is_refused_by_the_api_shown_again_with_the_new_count_and_needs_the_name()
    {
        _tags.DeleteAsync(_unusedId, false, Arg.Any<CancellationToken>())
            .Returns(TestData.Fail(ApiErrorCodes.TagInUse, "This tag is on 3 ticket(s). Delete it with force to remove it from them first.", ResultErrorKind.Conflict));
        _tags.ListSummaryAsync(Arg.Any<CancellationToken>()).Returns(
            TestData.Ok<IReadOnlyList<TagSummaryDto>>([TestData.TagSummary("urgent", 12, id: _urgentId), TestData.TagSummary("Old idea", 0, "old-idea", id: _unusedId)]),
            TestData.Ok<IReadOnlyList<TagSummaryDto>>([TestData.TagSummary("urgent", 12, id: _urgentId), TestData.TagSummary("Old idea", 3, "old-idea", id: _unusedId)]));
        var cut = RenderPage();
        AskToDelete(cut, "old-idea");

        Confirm(cut).Click();

        Deletes().ShouldBe([(_unusedId, false)]);
        var dialog = Dialog(cut);
        dialog.QuerySelector("[role=alert]")!.TextContent.ShouldBe("This tag was just added to tickets. The count above is updated: type the name to delete it anyway.");
        dialog.QuerySelector(".ts-delete-count")!.TextContent.ShouldBe("3 tickets");
        dialog.QuerySelector("label")!.TextContent.ShouldBe("Type Old idea to confirm");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Deletes().Count.ShouldBe(1);

        Type(cut, "old idea");
        Confirm(cut).Click();

        Deletes().ShouldBe([(_unusedId, false), (_unusedId, true)]);
    }

    [Fact]
    public void A_tag_that_is_already_gone_closes_the_dialog_says_so_and_reloads_the_list()
    {
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.TagNotFound, "No such tag.", ResultErrorKind.NotFound));
        var cut = RenderPage();
        AskToDelete(cut, "old-idea");

        Confirm(cut).Click();

        StatusMessages.Current.ShouldBe("That tag no longer exists.");
        _tags.Received(2).ListSummaryAsync(Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void Another_failure_keeps_the_dialog_open_and_says_nothing_was_changed()
    {
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("boom", "The API refused."));
        var cut = RenderPage();
        AskToDelete(cut, "old-idea");

        Confirm(cut).Click();

        Dialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't delete the tag. Nothing was changed. The API refused.");
        cut.FindAll("tr[data-tag='old-idea']").Count.ShouldBe(1);
        Dialogs.VerifyNotInvoke("close");
    }

    [Fact]
    public void A_lost_answer_never_claims_nothing_changed_blocks_a_second_delete_and_offers_a_reload()
    {
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        AskToDelete(cut, "urgent");
        Type(cut, "urgent");
        Confirm(cut).Click();

        var dialog = Dialog(cut);
        dialog.QuerySelector("[role=alert]")!.TextContent.ShouldBe("The delete may have gone through. Reload the list to check before you try again.");
        dialog.TextContent.ShouldNotContain("Nothing was changed");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Deletes().Count.ShouldBe(1);

        Dialog(cut).QuerySelector(".ts-dialog-recovery button")!.Click();

        _tags.Received(2).ListSummaryAsync(Arg.Any<CancellationToken>());
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_delete_that_finishes_after_the_page_is_gone_is_not_cancelled_and_reports_nothing()
    {
        var gate = new TaskCompletionSource<Result>();
        _tags.DeleteAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        AskToDelete(cut, "old-idea");
        Confirm(cut).Click();

        cut.FindComponent<TagsContent>().Instance.Dispose();
        gate.SetResult(TestData.Ok());

        _tags.Received(1).DeleteAsync(_unusedId, false, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        StatusMessages.Current.ShouldBeNull();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/AdminEventSummaryFactoryTests.cs`

```csharp
using System.Reflection;
using TechStrap.Admin.Features.Settings.Audit;
using TechStrap.Contracts.AdminEvents;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// PHASE-07 T17: the factory theory covers every admin event type constant. It reads ids, slugs, prefixes, kinds and counts only, never returns a payload, and never throws for an unknown type, a payload that
/// is not JSON, or a field of the wrong kind.
/// </summary>
public sealed class AdminEventSummaryFactoryTests
{
    private static readonly Dictionary<string, (string Payload, string Expected)> Samples = new()
    {
        [AdminEventTypes.ProductCreated] = ("{\"productKey\":\"orbitly\",\"numberPrefix\":\"ORB\"}", "Created product orbitly (ticket prefix ORB)"),
        [AdminEventTypes.ProductUpdated] = ("{\"changed\":[\"name\",\"branding\",\"isActive\"]}", "Updated a product: name, branding, active status"),
        [AdminEventTypes.ApiKeyCreated] = ("{\"productId\":\"11111111-1111-1111-1111-111111111111\",\"kind\":\"Trusted\",\"keyPrefix\":\"tsk_ab12\"}", "Created a Trusted API key (tsk_ab12)"),
        [AdminEventTypes.ApiKeyRevoked] = ("{\"productId\":\"11111111-1111-1111-1111-111111111111\",\"keyPrefix\":\"tsk_ab12\"}", "Revoked API key tsk_ab12"),
        [AdminEventTypes.AgentUpdated] = ("{\"isActive\":false}", "Deactivated an agent"),
        [AdminEventTypes.TagCreated] = ("{\"slug\":\"bug\"}", "Created tag bug"),
        [AdminEventTypes.TagUpdated] = ("{\"slug\":\"bug\",\"changed\":[\"name\",\"colour\"]}", "Updated tag bug: name, colour"),
        [AdminEventTypes.TagDeleted] = ("{\"slug\":\"bug\",\"detachedTicketCount\":12}", "Deleted tag bug, removed from 12 tickets"),
        [AdminEventTypes.RequesterErased] = ("{\"tickets\":3,\"messages\":12,\"attachments\":1,\"links\":2,\"outboxRows\":4}", "Erased a requester: 3 tickets, 12 messages, 1 attachment, 2 access links, 4 queued emails"),
        [AdminEventTypes.TicketDeleted] = ("{\"number\":\"ORB-42\",\"messageCount\":1,\"attachmentCount\":0}", "Deleted ticket ORB-42 (1 message, 0 attachments)"),
        [AdminEventTypes.DeadLetterRetried] = ("{\"kind\":\"agent-reply\",\"attempts\":5}", "Retried a failed agent reply email after 5 attempts"),
        [AdminEventTypes.DeadLetterDiscarded] = ("{\"kind\":\"new-ticket-alert\",\"attempts\":1}", "Discarded a failed new ticket alert email after 1 attempt"),
    };

    private static IEnumerable<string> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!);

    public static TheoryData<string> EveryEventType()
    {
        var data = new TheoryData<string>();
        foreach (var type in Constants(typeof(AdminEventTypes)))
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void There_is_a_sample_for_every_event_type_constant_so_a_new_type_fails_here_until_it_has_a_sentence()
    {
        Constants(typeof(AdminEventTypes)).OrderBy(t => t).ShouldBe(Samples.Keys.OrderBy(t => t));
        Constants(typeof(AdminEventTypes)).Count().ShouldBe(12);
    }

    [Theory]
    [MemberData(nameof(EveryEventType))]
    public void Every_event_type_has_its_own_sentence_from_its_payload(string type)
    {
        var (payload, expected) = Samples[type];

        AdminEventSummaryFactory.Summarize(type, payload).ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(EveryEventType))]
    public void Every_event_type_has_a_plain_sentence_with_an_empty_payload_too(string type)
    {
        var summary = AdminEventSummaryFactory.Summarize(type, "{}");

        summary.ShouldNotBeNullOrWhiteSpace();
        summary.ShouldNotContain("{");
    }

    [Theory]
    [MemberData(nameof(EveryEventType))]
    public void No_payload_shape_makes_any_event_type_throw_or_echo_the_payload(string type)
    {
        string[] payloads =
        [
            string.Empty, "   ", "not json", "[]", "null", "42", "\"text\"", "{", "{\"slug\":42}", "{\"slug\":{\"a\":1}}", "{\"slug\":null,\"kind\":[]}",
            "{\"detachedTicketCount\":\"x\"}", "{\"detachedTicketCount\":99999999999}", "{\"detachedTicketCount\":-3}", "{\"detachedTicketCount\":1.5}",
            "{\"changed\":\"name\"}", "{\"changed\":[1,null,{},[]]}", "{\"isActive\":\"yes\"}", "{\"attempts\":true}",
            "{\"hostile\":\"<script>alert(1)</script>\"}",
        ];

        foreach (var payload in payloads)
        {
            var summary = AdminEventSummaryFactory.Summarize(type, payload);

            summary.ShouldNotBeNullOrWhiteSpace(payload);
            summary.ShouldNotContain("<script>", Shouldly.Case.Insensitive, payload);
            summary.ShouldNotContain("hostile", Shouldly.Case.Insensitive, payload);
            summary.ShouldNotContain("{", Shouldly.Case.Sensitive, payload);
        }
    }

    [Fact]
    public void An_agent_update_says_activated_deactivated_or_just_changed()
    {
        AdminEventSummaryFactory.Summarize(AdminEventTypes.AgentUpdated, "{\"isActive\":true}").ShouldBe("Activated an agent");
        AdminEventSummaryFactory.Summarize(AdminEventTypes.AgentUpdated, "{\"isActive\":false}").ShouldBe("Deactivated an agent");
        AdminEventSummaryFactory.Summarize(AdminEventTypes.AgentUpdated, "{}").ShouldBe("Changed an agent");
    }

    [Fact]
    public void A_value_from_the_payload_is_shortened_and_has_no_control_characters()
    {
        var summary = AdminEventSummaryFactory.Summarize(AdminEventTypes.TagCreated, "{\"slug\":\"" + new string('a', 300) + "\"}");

        summary.Length.ShouldBeLessThan(80);
        summary.ShouldEndWith("\u2026");

        AdminEventSummaryFactory.Summarize(AdminEventTypes.TagCreated, "{\"slug\":\"a\\nb\\u0007c\"}").ShouldBe("Created tag a b c");
    }

    [Fact]
    public void Fields_the_factory_does_not_know_are_never_shown()
    {
        var summary = AdminEventSummaryFactory.Summarize(AdminEventTypes.TagCreated, "{\"slug\":\"bug\",\"secret\":\"hunter2\",\"email\":\"ada@example.com\"}");

        summary.ShouldBe("Created tag bug");
    }

    [Fact]
    public void An_unknown_event_type_is_shown_as_words_and_never_throws()
    {
        AdminEventSummaryFactory.Summarize("SomethingNew", "{\"slug\":\"x\"}").ShouldBe("Something new");
        AdminEventSummaryFactory.Summarize("WebhookSecretRotated", "not json").ShouldBe("Webhook secret rotated");
        AdminEventSummaryFactory.Summarize(string.Empty, string.Empty).ShouldBe("Admin event");
    }

    [Fact]
    public void Every_subject_type_has_its_own_label_and_an_unknown_one_is_shown_as_it_came()
    {
        var labels = Constants(typeof(AdminSubjectTypes)).Select(AdminEventSummaryFactory.SubjectLabel).ToList();

        labels.Count.ShouldBe(7);
        labels.Distinct().Count().ShouldBe(7);
        labels.ShouldAllBe(l => !string.IsNullOrWhiteSpace(l));
        AdminEventSummaryFactory.SubjectLabel(AdminSubjectTypes.EmailOutbox).ShouldBe("Email");
        AdminEventSummaryFactory.SubjectLabel("Webhook").ShouldBe("Webhook");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/AdminEventsPageTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Settings.Audit;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Tests.Components;

public sealed class AdminEventsPageTests : AdminPageTest
{
    private readonly IAdminEventsClient _events = Substitute.For<IAdminEventsClient>();
    private readonly IAgentsClient _agents = Substitute.For<IAgentsClient>();
    private readonly NavigationManager _navigation;

    public AdminEventsPageTests()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(new PagedResponse<AdminEventDto>(
        [
            TestData.AdminEvent(AdminEventTypes.TagDeleted, "{\"slug\":\"bug\",\"detachedTicketCount\":2}", at: TestData.Now.AddMinutes(-5)),
            TestData.AdminEvent(AdminEventTypes.ApiKeyRevoked, "{\"productId\":\"11111111-1111-1111-1111-111111111111\",\"keyPrefix\":\"tsk_ab12\"}", AdminSubjectTypes.ApiKey, actor: null, at: TestData.Now.AddHours(-2)),
        ], 1, 25, 2)));
        _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>(
            [TestData.AgentRow("Ada Admin", TestData.AdaAgentId, AgentRoles.Admin), TestData.AgentRow("Sam Ortiz", TestData.SamAgentId)]));
        Services.AddSingleton(_events);
        Services.AddSingleton(_agents);
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<AdminEventsPage> RenderPage(string query = "")
    {
        _navigation.NavigateTo($"/settings/audit{query}");
        return Render<AdminEventsPage>();
    }

    private IReadOnlyList<(AdminEventFilter Filter, int Page, int PageSize)> Requests() =>
        [.. _events.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IAdminEventsClient.ListAsync))
            .Select(c => ((AdminEventFilter)c.GetArguments()[0]!, (int)c.GetArguments()[1]!, (int)c.GetArguments()[2]!))];

    // ---- what it shows -------------------------------------------------------------------------------------------

    [Fact]
    public void Events_are_listed_in_the_order_the_api_sent_with_when_who_what_and_one_sentence()
    {
        var cut = RenderPage();

        var rows = cut.FindAll("tbody tr");
        rows.Count.ShouldBe(2);
        rows[0].Children[0].TextContent.ShouldBe("5 min ago");
        rows[0].Children[1].TextContent.ShouldBe("Ada Admin");
        rows[0].Children[2].TextContent.ShouldBe("Tag");
        rows[0].Children[3].TextContent.ShouldBe("Deleted tag bug, removed from 2 tickets");
        rows[1].Children[1].TextContent.ShouldBe("Unknown agent");
        rows[1].Children[2].TextContent.ShouldBe("API key");
        rows[1].Children[3].TextContent.ShouldBe("Revoked API key tsk_ab12");
        cut.Find("h1").TextContent.ShouldBe("Audit log");
    }

    [Fact]
    public void A_hostile_payload_is_encoded_and_the_raw_json_is_never_rendered()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new PagedResponse<AdminEventDto>(
        [
            TestData.AdminEvent(AdminEventTypes.TagCreated, "{\"slug\":\"<img src=x onerror=alert(1)>\",\"secret\":\"hunter2\"}"),
            TestData.AdminEvent("SomethingNew", "{\"slug\":\"x\",\"token\":\"abc123\"}"),
        ], 1, 25, 2)));

        var cut = RenderPage();

        cut.FindAll("img").ShouldBeEmpty();
        cut.Markup.ShouldContain("&lt;img src=x onerror=alert(1)&gt;");
        cut.Markup.ShouldNotContain("\"slug\"");
        cut.Markup.ShouldNotContain("hunter2");
        cut.Markup.ShouldNotContain("abc123");
        cut.FindAll("tbody tr")[1].Children[3].TextContent.ShouldBe("Something new");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<PagedResponse<AdminEventDto>>("api-error", "The API is unavailable."),
            TestData.Ok(new PagedResponse<AdminEventDto>([TestData.AdminEvent()], 1, 25, 1)));
        var cut = RenderPage();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the audit log. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void No_events_at_all_is_a_plain_empty_state_and_no_matches_is_a_different_one_with_a_way_back()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<AdminEventDto>([], 1, 25, 0)));

        var plain = RenderPage();
        plain.Find(".ts-state-heading").TextContent.ShouldBe("No audit events yet");
        plain.FindAll("table").ShouldBeEmpty();
    }

    [Fact]
    public void A_filter_with_no_matches_says_so_and_offers_to_clear_the_filters()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<AdminEventDto>([], 1, 25, 0)));

        var cut = RenderPage("?subject=Ticket");

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No events match these filters");
        cut.Find(".ts-state--empty a").GetAttribute("href").ShouldBe("/settings/audit");
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_neither_the_log_nor_the_agent_list_is_asked_for()
    {
        AsAgent();

        var cut = RenderPage();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("table").ShouldBeEmpty();
        _events.ReceivedCalls().ShouldBeEmpty();
        _agents.DidNotReceive().ListAllAsync(Arg.Any<CancellationToken>());
    }

    // ---- filters -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_filters_offer_every_subject_type_and_every_agent()
    {
        var cut = RenderPage();

        cut.Find("#ts-audit-subject").QuerySelectorAll("option").Select(o => o.TextContent).ShouldBe(
            ["Everything", "Product", "API key", "Agent", "Tag", "Requester", "Ticket", "Email"]);
        cut.Find("#ts-audit-actor").QuerySelectorAll("option").Select(o => o.TextContent).ShouldBe(["Anyone", "Ada Admin", "Sam Ortiz"]);
    }

    [Fact]
    public void Filters_in_the_query_string_are_sent_in_canonical_form_and_shown_as_selected()
    {
        var cut = RenderPage($"?subject=tag&actor={TestData.SamAgentId}");

        var request = Requests().ShouldHaveSingleItem();
        request.Filter.SubjectType.ShouldBe("Tag");
        request.Filter.ActorId.ShouldBe(TestData.SamAgentId);
        cut.Find("#ts-audit-subject option[selected]").TextContent.ShouldBe("Tag");
        cut.Find("#ts-audit-actor option[selected]").TextContent.ShouldBe("Sam Ortiz");
    }

    [Fact]
    public void An_unknown_subject_or_a_bad_actor_in_the_address_is_dropped_and_never_sent()
    {
        RenderPage("?subject=Everything%27%3BDROP&actor=not-a-guid");

        var request = Requests().ShouldHaveSingleItem();
        request.Filter.SubjectType.ShouldBeNull();
        request.Filter.ActorId.ShouldBeNull();
    }

    [Fact]
    public void Choosing_a_subject_navigates_with_the_filter_in_the_query_string_keeping_the_actor_and_dropping_the_page()
    {
        var cut = RenderPage($"?actor={TestData.AdaAgentId}&page=3");

        cut.Find("#ts-audit-subject").Change("Ticket");

        _navigation.Uri.ShouldEndWith($"/settings/audit?subject=Ticket&actor={TestData.AdaAgentId}");
    }

    [Fact]
    public void Choosing_everything_removes_the_filter_from_the_address()
    {
        var cut = RenderPage("?subject=Tag");

        cut.Find("#ts-audit-subject").Change(string.Empty);

        _navigation.Uri.ShouldEndWith("/settings/audit");
    }

    [Fact]
    public void Choosing_an_actor_navigates_with_the_actor_in_the_query_string()
    {
        var cut = RenderPage("?subject=Tag");

        cut.Find("#ts-audit-actor").Change(TestData.SamAgentId.ToString());

        _navigation.Uri.ShouldEndWith($"/settings/audit?subject=Tag&actor={TestData.SamAgentId}");
    }

    [Fact]
    public void The_filter_still_works_when_the_agent_list_cannot_be_read()
    {
        _agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<AgentListItemDto>>("api-error", "Down."));

        var cut = RenderPage();

        cut.FindAll("tbody tr").Count.ShouldBe(2);
        cut.Find("#ts-audit-actor").QuerySelectorAll("option").Select(o => o.TextContent).ShouldBe(["Anyone"]);
    }

    // ---- paging and asOf -----------------------------------------------------------------------------------------

    [Fact]
    public void The_first_page_asks_for_25_as_of_now()
    {
        RenderPage();

        var request = Requests().ShouldHaveSingleItem();
        request.Page.ShouldBe(1);
        request.PageSize.ShouldBe(25);
        request.Filter.AsOf.ShouldBe(TestData.Now);
    }

    [Fact]
    public void Paging_goes_through_the_query_string()
    {
        _events.ListAsync(Arg.Any<AdminEventFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<AdminEventDto>([TestData.AdminEvent()], 1, 25, 60)));
        var cut = RenderPage("?subject=Tag");

        cut.FindAll(".ts-pager button").Single(b => b.TextContent == "Next").Click();

        _navigation.Uri.ShouldEndWith("/settings/audit?subject=Tag&page=2");
    }

    [Fact]
    public void A_later_page_carries_the_asOf_of_the_first_page_even_when_time_has_moved_on()
    {
        RenderPage();
        Time.Advance(TimeSpan.FromMinutes(10));

        _navigation.NavigateTo("/settings/audit?page=2");

        var requests = Requests();
        requests.Count.ShouldBe(2);
        requests[1].Page.ShouldBe(2);
        requests[1].Filter.AsOf.ShouldBe(requests[0].Filter.AsOf);
        requests[1].Filter.AsOf.ShouldBe(TestData.Now);
    }

    [Fact]
    public void Going_back_to_the_first_page_or_changing_the_filter_or_refreshing_takes_a_new_asOf()
    {
        var cut = RenderPage();
        Time.Advance(TimeSpan.FromMinutes(10));
        _navigation.NavigateTo("/settings/audit?page=2");
        Time.Advance(TimeSpan.FromMinutes(10));

        _navigation.NavigateTo("/settings/audit");
        Time.Advance(TimeSpan.FromMinutes(10));
        _navigation.NavigateTo("/settings/audit?subject=Tag");
        Time.Advance(TimeSpan.FromMinutes(10));
        cut.Find("button.btn-outline-secondary").Click();

        Requests().Select(r => r.Filter.AsOf).ShouldBe(
        [
            TestData.Now,
            TestData.Now,
            TestData.Now.AddMinutes(20),
            TestData.Now.AddMinutes(30),
            TestData.Now.AddMinutes(40),
        ]);
    }

    [Fact]
    public void A_page_opened_directly_takes_now_as_its_asOf()
    {
        Time.Advance(TimeSpan.FromHours(1));

        RenderPage("?page=3");

        Requests().ShouldHaveSingleItem().Filter.AsOf.ShouldBe(TestData.Now.AddHours(1));
    }
}
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "TagsPageTests|DeleteTagTests|AdminEventSummaryFactoryTests|AdminEventsPageTests"`
Expected: a build failure (`TagsPage`, `TagsContent`, `AdminEventsPage`, `AdminEventsContent`, `AdminEventSummaryFactory`, `AuditCopy`, `EmailKinds` do not exist).

- [ ] **Step 3: Implement**

1. The shared email kinds:

`src/TechStrap.Admin/Features/Settings/EmailKinds.cs`

```csharp
namespace TechStrap.Admin.Features.Settings;

/// <summary>
/// The kinds of email the API queues (the <c>kind</c> of an outbox row), with a plain label for each. The audit log and the failed-emails page both name an email by its kind, so the words live in one place.
/// An unknown kind is shown as it came, never dropped.
/// </summary>
public static class EmailKinds
{
    public const string TicketConfirmation = "ticket-confirmation";
    public const string AgentReply = "agent-reply";
    public const string TicketSolved = "ticket-solved";
    public const string TicketAssigned = "ticket-assigned";
    public const string NewTicketAlert = "new-ticket-alert";
    public const string CustomerReplyAlert = "customer-reply-alert";
    public const string AccessLinks = "access-links";

    public static string Label(string? kind) => kind switch
    {
        TicketConfirmation => "Ticket confirmation",
        AgentReply => "Agent reply",
        TicketSolved => "Ticket solved",
        TicketAssigned => "Ticket assigned",
        NewTicketAlert => "New ticket alert",
        CustomerReplyAlert => "Customer reply alert",
        AccessLinks => "Access links",
        null or "" => "Email",
        _ => kind,
    };
}
```

2. The tags page:

`src/TechStrap.Admin/Features/Settings/Tags/TagsCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Settings.Tags;

/// <summary>The copy of the tags page: create, rename and recolour, and the two delete confirmations (an unused tag, and a tag in use with its count and a typed name).</summary>
public static class TagsCopy
{
    public const string DefaultColour = "#4B5563";

    public const string Heading = "Tags";
    public const string Loading = "Loading tags";
    public const string LoadFailed = "Couldn't load the tags.";
    public const string NoTags = "No tags yet";
    public const string NoTagsHint = "Create a tag, then agents can add it to tickets.";

    public const string NewHeading = "New tag";
    public const string NameLabel = "Name";
    public const string SlugLabel = "Slug";
    public const string SlugHelp = "Letters, numbers and hyphens. It can't be changed once the tag exists.";
    public const string ColourLabel = "Colour";
    public const string ColourHelp = "A # and six hex digits, such as #1D4ED8.";
    public const string Create = "Create tag";
    public const string Creating = "Creating\u2026";

    public const string ColumnTag = "Tag";
    public const string ColumnSlug = "Slug";
    public const string ColumnTickets = "Tickets";
    public const string Edit = "Edit";
    public const string Save = "Save";
    public const string Cancel = "Cancel";
    public const string Delete = "Delete";
    public const string Reload = "Reload list";

    public const string NameRequired = "Enter a name.";
    public const string NameTooLong = "Use 50 characters or fewer.";
    public const string SlugRequired = "Enter a slug.";
    public const string SlugInvalid = "Use lower-case letters, numbers and single hyphens, up to 40 characters.";
    public const string SlugTaken = "Another tag already uses this slug.";
    public const string ColourInvalid = "Use a colour like #1D4ED8: a # and six hex digits.";

    public const string DeleteConfirm = "Delete tag";
    public const string DeleteUnusedBody = "No tickets use this tag. This can't be undone.";
    public const string DeleteUncertain = "The delete may have gone through. Reload the list to check before you try again.";
    public const string NowInUse = "This tag was just added to tickets. The count above is updated: type the name to delete it anyway.";
    public const string TagGone = "That tag no longer exists.";
    public const string SaveUncertain = "The change may have gone through. Reload the list to check before you try again.";

    public static string Tickets(int count) => count == 1 ? "1 ticket" : $"{count} tickets";

    public static string DeleteTitle(string name) => $"Delete the tag {name}?";

    public static string DeleteInUseBody(int count) =>
        $"This tag is on {Tickets(count)}. Deleting it removes it from all of them, and each ticket records the change. This can't be undone.";

    public static string DeleteFailed(string reason) => $"Couldn't delete the tag. Nothing was changed. {reason}";

    public static string Created(string name) => $"Created the tag {name}";

    public static string Saved(string name) => $"Saved the tag {name}";

    public static string Deleted(string name) => $"Deleted the tag {name}";

    public static string DeletedFromTickets(string name, int count) => $"Deleted the tag {name} and removed it from {Tickets(count)}";
}
```

`src/TechStrap.Admin/Features/Settings/Tags/TagRowViewModel.cs`

```csharp
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Features.Settings.Tags;

internal sealed record TagRowViewModel(Guid Id, string Slug, string Name, string Colour, int TicketCount)
{
    public static TagRowViewModel From(TagSummaryDto tag) => new(tag.Id, tag.Slug, tag.Name, tag.Colour, tag.TicketCount);

    /// <summary>The row after a rename or recolour: the API answers with the tag only, so the count the list already showed stays.</summary>
    public TagRowViewModel With(TagDto tag) => this with { Name = tag.Name, Colour = tag.Colour };
}
```

`src/TechStrap.Admin/Features/Settings/Tags/TagForm.cs`

```csharp
using System.Text.RegularExpressions;
using TechStrap.Contracts.Branding;

namespace TechStrap.Admin.Features.Settings.Tags;

/// <summary>The checks the tag form makes before it sends, with the server's own rules (the colour with the same Contracts constant). The field names of a 400 are <see cref="Clients.ApiFields"/>.</summary>
internal static partial class TagForm
{
    private const int NameMaxLength = 50;
    private const int SlugMaxLength = 40;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlugCharacters();

    public static string? CheckName(string value) =>
        string.IsNullOrWhiteSpace(value) ? TagsCopy.NameRequired : value.Trim().Length > NameMaxLength ? TagsCopy.NameTooLong : null;

    public static string? CheckSlug(string value) =>
        string.IsNullOrWhiteSpace(value) ? TagsCopy.SlugRequired : value.Trim().Length > SlugMaxLength || !SlugPattern().IsMatch(value.Trim()) ? TagsCopy.SlugInvalid : null;

    public static string? CheckColour(string value) => Regex.IsMatch(value.Trim(), BrandingRules.ColourPattern) ? null : TagsCopy.ColourInvalid;

    /// <summary>A slug suggested from a name ("Billing issue" gives "billing-issue"). The agent can change it until the tag is created.</summary>
    public static string SlugFrom(string name)
    {
        var slug = NotSlugCharacters().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug.Length > SlugMaxLength ? slug[..SlugMaxLength].TrimEnd('-') : slug;
    }
}
```

`src/TechStrap.Admin/Features/Settings/Tags/TagsPage.razor`

```razor
@page "/settings/tags"
@attribute [Authorize]

<AdminOnly>
    <TagsContent />
</AdminOnly>
```

`src/TechStrap.Admin/Features/Settings/Tags/TagsContent.razor`

```razor
<PageTitle>@TagsCopy.Heading &middot; Settings</PageTitle>
    <div class="ts-settings ts-tags">
        <h1>@TagsCopy.Heading</h1>

        <form class="ts-form ts-tag-create" novalidate @onsubmit="CreateAsync">
            <h2>@TagsCopy.NewHeading</h2>
            <fieldset disabled="@_creating">
                <div class="ts-field">
                    <label for="ts-tag-name" class="form-label">@TagsCopy.NameLabel</label>
                    <input id="ts-tag-name" type="text" class="form-control @(Err(_createErrors, ApiFields.Name) is null ? null : "is-invalid")" value="@_newName" autocomplete="off" @oninput="OnNewNameInput" @onblur="() => CheckCreate(ApiFields.Name)" />
                    @ErrorLine("ts-tag-name-error", Err(_createErrors, ApiFields.Name))
                </div>
                <div class="ts-field">
                    <label for="ts-tag-slug" class="form-label">@TagsCopy.SlugLabel</label>
                    <input id="ts-tag-slug" type="text" class="form-control @(Err(_createErrors, ApiFields.Slug) is null ? null : "is-invalid")" value="@_newSlug" autocomplete="off" spellcheck="false" @oninput="OnNewSlugInput" @onblur="() => CheckCreate(ApiFields.Slug)" />
                    <p class="ts-field-help">@TagsCopy.SlugHelp</p>
                    @ErrorLine("ts-tag-slug-error", Err(_createErrors, ApiFields.Slug))
                </div>
                <div class="ts-field">
                    <label for="ts-tag-colour" class="form-label">@TagsCopy.ColourLabel</label>
                    <div class="ts-colour-row">
                        <input id="ts-tag-colour" type="text" class="form-control @(Err(_createErrors, ApiFields.Colour) is null ? null : "is-invalid")" value="@_newColour" autocomplete="off" spellcheck="false" @oninput="OnNewColourInput" @onblur="() => CheckCreate(ApiFields.Colour)" />
                        <span class="ts-colour-preview"><TagChip Name="@(string.IsNullOrWhiteSpace(_newName) ? TagsCopy.NewHeading : _newName.Trim())" Colour="@_newColour" /></span>
                    </div>
                    <p class="ts-field-help">@TagsCopy.ColourHelp</p>
                    @ErrorLine("ts-tag-colour-error", Err(_createErrors, ApiFields.Colour))
                </div>
            </fieldset>
            @if (_createFormError is not null)
            {
                <p class="ts-form-error" role="alert">@_createFormError</p>
            }
            <div class="ts-form-actions">
                <button type="submit" class="btn btn-primary" disabled="@_creating">@(_creating ? TagsCopy.Creating : TagsCopy.Create)</button>
            </div>
        </form>

        @if (_rowError is not null)
        {
            <div class="ts-conflict" role="alert">
                <p>@_rowError</p>
                @if (_rowUncertain)
                {
                    <button type="button" class="btn btn-outline-secondary" @onclick="ReloadAsync">@TagsCopy.Reload</button>
                }
            </div>
        }

        @if (_loading && _rows.Count == 0)
        {
            <LoadingState Rows="4" Label="@TagsCopy.Loading" />
        }
        else if (_error is not null)
        {
            <ErrorState Message="@_error" OnRetry="ReloadAsync" />
        }
        else if (_rows.Count == 0)
        {
            <EmptyState Heading="@TagsCopy.NoTags"><p>@TagsCopy.NoTagsHint</p></EmptyState>
        }
        else
        {
            <div class="table-responsive">
                <table class="table ts-ledger ts-settings-table">
                    <thead>
                        <tr>
                            <th scope="col">@TagsCopy.ColumnTag</th>
                            <th scope="col">@TagsCopy.ColumnSlug</th>
                            <th scope="col">@TagsCopy.ColumnTickets</th>
                            <th scope="col"><span class="visually-hidden">Actions</span></th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var row in _rows)
                        {
                            @if (_editing == row.Id)
                            {
                                <tr @key="row.Id" class="ts-tag-editing" data-tag="@row.Slug">
                                    <td>
                                        <label for="ts-edit-name" class="visually-hidden">@TagsCopy.NameLabel</label>
                                        <input id="ts-edit-name" type="text" class="form-control @(Err(_editErrors, ApiFields.Name) is null ? null : "is-invalid")" value="@_editName" autocomplete="off" @oninput="e => _editName = e.Value?.ToString() ?? string.Empty" />
                                        @ErrorLine("ts-edit-name-error", Err(_editErrors, ApiFields.Name))
                                        <label for="ts-edit-colour" class="visually-hidden">@TagsCopy.ColourLabel</label>
                                        <input id="ts-edit-colour" type="text" class="form-control @(Err(_editErrors, ApiFields.Colour) is null ? null : "is-invalid")" value="@_editColour" autocomplete="off" spellcheck="false" @oninput="e => _editColour = e.Value?.ToString() ?? string.Empty" />
                                        @ErrorLine("ts-edit-colour-error", Err(_editErrors, ApiFields.Colour))
                                    </td>
                                    <td><code>@row.Slug</code></td>
                                    <td>@row.TicketCount</td>
                                    <td class="ts-settings-actions">
                                        <button type="button" class="btn btn-primary ts-save" disabled="@_busy" @onclick="SaveEditAsync">@TagsCopy.Save</button>
                                        <button type="button" class="btn btn-outline-secondary ts-cancel" disabled="@_busy" @onclick="CancelEdit">@TagsCopy.Cancel</button>
                                    </td>
                                </tr>
                            }
                            else
                            {
                                <tr @key="row.Id" data-tag="@row.Slug">
                                    <td><TagChip Name="@row.Name" Colour="@row.Colour" /></td>
                                    <td><code>@row.Slug</code></td>
                                    <td class="ts-tag-count">@row.TicketCount</td>
                                    <td class="ts-settings-actions">
                                        <button type="button" class="btn btn-outline-secondary ts-edit" disabled="@_busy" @onclick="() => BeginEdit(row)">@TagsCopy.Edit</button>
                                        <button type="button" class="btn btn-outline-secondary ts-delete" disabled="@_busy" @onclick="() => AskDelete(row)">@TagsCopy.Delete</button>
                                    </td>
                                </tr>
                            }
                        }
                    </tbody>
                </table>
            </div>
        }

        <ConfirmDialog Open="@(Deleting is not null)" Title="@(Deleting is null ? string.Empty : TagsCopy.DeleteTitle(Deleting.Name))" ConfirmLabel="@TagsCopy.DeleteConfirm"
                       Danger="@(Deleting is { TicketCount: > 0 })" RequiredText="@(Deleting is { TicketCount: > 0 } ? Deleting.Name : null)"
                       Busy="_deleteBusy" ConfirmDisabled="_deleteUncertain" Error="@_deleteError" OnConfirm="ConfirmDeleteAsync" OnCancel="CancelDelete">
            @if (Deleting is { } tag)
            {
                @if (tag.TicketCount > 0)
                {
                    <p class="ts-delete-count"><strong>@TagsCopy.Tickets(tag.TicketCount)</strong></p>
                    <p>@TagsCopy.DeleteInUseBody(tag.TicketCount)</p>
                }
                else
                {
                    <p>@TagsCopy.DeleteUnusedBody</p>
                }
                @if (_deleteUncertain)
                {
                    <div class="ts-dialog-recovery">
                        <button type="button" class="btn btn-link" @onclick="ReloadAfterUncertainAsync">@TagsCopy.Reload</button>
                    </div>
                }
            }
        </ConfirmDialog>
    </div>

@code {
    private static string? Err(Dictionary<string, string> errors, string field) => errors.GetValueOrDefault(field);

    private RenderFragment ErrorLine(string id, string? message) => @<text>
        @if (message is not null)
        {
            <p id="@id" class="ts-field-error" role="alert">@message</p>
        }
    </text>;
}
```

`src/TechStrap.Admin/Features/Settings/Tags/TagsContent.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Tags;

namespace TechStrap.Admin.Features.Settings.Tags;

/// <summary>
/// The tag list with its ticket counts (Admin only), create, rename and recolour, and delete. Deleting an unused tag is a medium-tier confirmation and sends no force flag. Deleting a tag that is in use is
/// irreversible: the dialog shows the count ("12 tickets"), asks for the tag's name to be typed, and only then sends <c>force=true</c>. A 409 <c>tag-in-use</c> (someone tagged a ticket after the list was read) refreshes
/// the counts and keeps the dialog open, so the next confirmation is the typed one. Writes use <see cref="CancellationToken.None"/> and never retry; an unknown outcome says so and offers a reload.
/// </summary>
public sealed partial class TagsContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, string> _createErrors = [];
    private readonly Dictionary<string, string> _editErrors = [];
    private IReadOnlyList<TagRowViewModel> _rows = [];
    private Guid _deletingId;
    private Guid _editing;
    private string _newName = string.Empty;
    private string _newSlug = string.Empty;
    private string _newColour = TagsCopy.DefaultColour;
    private string _editName = string.Empty;
    private string _editColour = string.Empty;
    private string? _error;
    private string? _rowError;
    private string? _createFormError;
    private string? _deleteError;
    private bool _slugEdited;
    private bool _loading = true;
    private bool _creating;
    private bool _busy;
    private bool _rowUncertain;
    private bool _deleteBusy;
    private bool _deleteUncertain;
    private bool _disposed;

    [Inject]
    private ITagsClient Tags { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    // The row being deleted, looked up fresh each time so a refreshed count (and so a refreshed dialog) is always the one confirmed.
    private TagRowViewModel? Deleting => _deletingId == Guid.Empty ? null : _rows.FirstOrDefault(r => r.Id == _deletingId);

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            var result = await Tags.ListSummaryAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = Sorted(result.Value.Select(TagRowViewModel.From));
            }
            else
            {
                _error = $"{TagsCopy.LoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private static IReadOnlyList<TagRowViewModel> Sorted(IEnumerable<TagRowViewModel> rows) =>
        [.. rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Slug, StringComparer.Ordinal)];

    private async Task ReloadAsync()
    {
        _rowError = null;
        _rowUncertain = false;
        await LoadAsync();
    }

    // ---- create --------------------------------------------------------------------------------------------------

    private void OnNewNameInput(ChangeEventArgs e)
    {
        _newName = e.Value?.ToString() ?? string.Empty;
        if (!_slugEdited)
        {
            _newSlug = TagForm.SlugFrom(_newName);
        }
    }

    private void OnNewSlugInput(ChangeEventArgs e)
    {
        _newSlug = e.Value?.ToString() ?? string.Empty;
        _slugEdited = true;
    }

    private void OnNewColourInput(ChangeEventArgs e) => _newColour = e.Value?.ToString() ?? string.Empty;

    private void CheckCreate(string field)
    {
        var message = field switch
        {
            ApiFields.Name => TagForm.CheckName(_newName),
            ApiFields.Slug => TagForm.CheckSlug(_newSlug),
            _ => TagForm.CheckColour(_newColour),
        };
        Set(_createErrors, field, message);
    }

    private static void Set(Dictionary<string, string> errors, string field, string? message)
    {
        if (message is null)
        {
            errors.Remove(field);
        }
        else
        {
            errors[field] = message;
        }
    }

    private async Task CreateAsync()
    {
        if (_creating)
        {
            return;
        }

        _createFormError = null;
        foreach (var field in new[] { ApiFields.Name, ApiFields.Slug, ApiFields.Colour })
        {
            CheckCreate(field);
        }

        if (_createErrors.Count > 0)
        {
            return;
        }

        _creating = true;
        try
        {
            var result = await Tags.CreateAsync(new CreateTagRequest(_newSlug.Trim(), _newName.Trim(), _newColour.Trim()), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                ShowFailure(result.Errors, _createErrors, create: true);
                return;
            }

            var tag = result.Value;
            _rows = Sorted([.. _rows, new TagRowViewModel(tag.Id, tag.Slug, tag.Name, tag.Colour, 0)]);
            _newName = string.Empty;
            _newSlug = string.Empty;
            _newColour = TagsCopy.DefaultColour;
            _slugEdited = false;
            _createErrors.Clear();
            StatusMessages.Show(TagsCopy.Created(tag.Name));
        }
        finally
        {
            _creating = false;
        }
    }

    // A 409 tag-slug-taken is a field error on the slug; a 400 names its field; an unknown outcome or anything else is said once, above the form or the list.
    private void ShowFailure(IReadOnlyList<ResultError> errors, Dictionary<string, string> fieldErrors, bool create)
    {
        var first = errors[0];
        if (first.Code == ApiErrorCodes.TagSlugTaken)
        {
            fieldErrors[ApiFields.Slug] = TagsCopy.SlugTaken;
            return;
        }

        if (ApiErrorCodes.IsUncertainWrite(first.Code))
        {
            _rowError = TagsCopy.SaveUncertain;
            _rowUncertain = true;
            return;
        }

        foreach (var error in errors)
        {
            if (error.Target is ApiFields.Name or ApiFields.Slug or ApiFields.Colour)
            {
                fieldErrors.TryAdd(error.Target, error.Message);
            }
            else if (create)
            {
                _createFormError ??= error.Message;
            }
            else
            {
                _rowError ??= error.Message;
            }
        }
    }

    // ---- edit ----------------------------------------------------------------------------------------------------

    private void BeginEdit(TagRowViewModel row)
    {
        _rowError = null;
        _rowUncertain = false;
        _editErrors.Clear();
        _editing = row.Id;
        _editName = row.Name;
        _editColour = row.Colour;
    }

    private void CancelEdit()
    {
        _editing = Guid.Empty;
        _editErrors.Clear();
    }

    private async Task SaveEditAsync()
    {
        if (_busy || _rows.FirstOrDefault(r => r.Id == _editing) is not { } row)
        {
            return;
        }

        Set(_editErrors, ApiFields.Name, TagForm.CheckName(_editName));
        Set(_editErrors, ApiFields.Colour, TagForm.CheckColour(_editColour));
        if (_editErrors.Count > 0)
        {
            return;
        }

        _busy = true;
        _rowError = null;
        try
        {
            var result = await Tags.UpdateAsync(row.Id, new UpdateTagRequest(_editName.Trim(), _editColour.Trim()), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = Sorted(_rows.Select(r => r.Id == row.Id ? r.With(result.Value) : r));
                _editing = Guid.Empty;
                StatusMessages.Show(TagsCopy.Saved(result.Value.Name));
            }
            else if (result.Errors[0].Code == ApiErrorCodes.TagNotFound)
            {
                _editing = Guid.Empty;
                StatusMessages.Show(TagsCopy.TagGone);
                await LoadAsync();
            }
            else
            {
                ShowFailure(result.Errors, _editErrors, create: false);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- delete --------------------------------------------------------------------------------------------------

    private void AskDelete(TagRowViewModel row)
    {
        _deleteError = null;
        _deleteUncertain = false;
        _rowError = null;
        _deletingId = row.Id;
    }

    private void CancelDelete()
    {
        if (!_deleteBusy)
        {
            _deletingId = Guid.Empty;
            _deleteError = null;
            _deleteUncertain = false;
        }
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_deleteBusy || Deleting is not { } tag)
        {
            return;
        }

        // force is sent only for a tag the list shows as in use, and the dialog (typed name) has already been satisfied for it: an unused tag is never deleted with force.
        var force = tag.TicketCount > 0;
        _deleteBusy = true;
        _deleteError = null;
        _deleteUncertain = false;
        StateHasChanged();
        try
        {
            var result = await Tags.DeleteAsync(tag.Id, force, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. _rows.Where(r => r.Id != tag.Id)];
                _deletingId = Guid.Empty;
                StatusMessages.Show(force ? TagsCopy.DeletedFromTickets(tag.Name, tag.TicketCount) : TagsCopy.Deleted(tag.Name));
                return;
            }

            await ShowDeleteFailureAsync(result.Errors[0]);
        }
        finally
        {
            _deleteBusy = false;
        }
    }

    private async Task ShowDeleteFailureAsync(ResultError error)
    {
        if (error.Code == ApiErrorCodes.TagInUse)
        {
            // Tickets were tagged after the list was read: show the new count, and the next confirmation is the typed one.
            await LoadAsync();
            _deleteError = TagsCopy.NowInUse;
        }
        else if (error.Code == ApiErrorCodes.TagNotFound)
        {
            _deletingId = Guid.Empty;
            StatusMessages.Show(TagsCopy.TagGone);
            await LoadAsync();
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _deleteError = TagsCopy.DeleteUncertain;
            _deleteUncertain = true;
        }
        else
        {
            _deleteError = TagsCopy.DeleteFailed(error.Message);
        }
    }

    private async Task ReloadAfterUncertainAsync()
    {
        _deletingId = Guid.Empty;
        _deleteError = null;
        _deleteUncertain = false;
        await LoadAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

3. The audit page:

`src/TechStrap.Admin/Features/Settings/Audit/AdminEventSummaryFactory.cs`

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using TechStrap.Admin.Features.Settings;
using TechStrap.Contracts.AdminEvents;

namespace TechStrap.Admin.Features.Settings.Audit;

/// <summary>
/// Turns an admin event into one plain sentence for the audit log. It reads only the ids, slugs, prefixes, kinds and counts the API puts in a payload (a payload never carries names, emails or secrets),
/// shortens every value it prints, and never returns the payload itself: no raw JSON is ever rendered, and what it returns is encoded by Razor like any other text. An unknown event type, a payload that is
/// not JSON, or a field of the wrong kind never throws: the sentence just says less.
/// </summary>
public static class AdminEventSummaryFactory
{
    private const int MaxValueLength = 60;

    public static string Summarize(string type, string payload)
    {
        using var document = TryParse(payload);
        var root = document?.RootElement is { ValueKind: JsonValueKind.Object } obj ? obj : (JsonElement?)null;

        return type switch
        {
            AdminEventTypes.ProductCreated => Join("Created product", Text(root, "productKey"), Wrap("ticket prefix", Text(root, "numberPrefix")), "Created a product"),
            AdminEventTypes.ProductUpdated => Changed("Updated a product", root, ProductChange),
            AdminEventTypes.ApiKeyCreated => ApiKeyCreated(root),
            AdminEventTypes.ApiKeyRevoked => Join("Revoked API key", Text(root, "keyPrefix"), null, "Revoked an API key"),
            AdminEventTypes.AgentUpdated => Flag(root, "isActive") switch
            {
                true => "Activated an agent",
                false => "Deactivated an agent",
                _ => "Changed an agent",
            },
            AdminEventTypes.TagCreated => Join("Created tag", Text(root, "slug"), null, "Created a tag"),
            AdminEventTypes.TagUpdated => Changed(Join("Updated tag", Text(root, "slug"), null, "Updated a tag"), root, TagChange),
            AdminEventTypes.TagDeleted => TagDeleted(root),
            AdminEventTypes.RequesterErased => RequesterErased(root),
            AdminEventTypes.TicketDeleted => TicketDeleted(root),
            AdminEventTypes.DeadLetterRetried => DeadLetter(root, "Retried a failed", "after"),
            AdminEventTypes.DeadLetterDiscarded => DeadLetter(root, "Discarded a failed", "after"),
            _ => Humanize(type),
        };
    }

    /// <summary>The words for what an event is about. A subject type this app does not know is shown as it came, shortened.</summary>
    public static string SubjectLabel(string subjectType) => subjectType switch
    {
        AdminSubjectTypes.Product => "Product",
        AdminSubjectTypes.ApiKey => "API key",
        AdminSubjectTypes.Agent => "Agent",
        AdminSubjectTypes.Tag => "Tag",
        AdminSubjectTypes.Requester => "Requester",
        AdminSubjectTypes.Ticket => "Ticket",
        AdminSubjectTypes.EmailOutbox => "Email",
        _ => Clip(subjectType),
    };

    // ---- the sentences that need more than a name ----------------------------------------------------------------

    private static string ApiKeyCreated(JsonElement? root)
    {
        var kind = Text(root, "kind");
        var prefix = Text(root, "keyPrefix");
        var head = kind is null ? "Created an API key" : $"Created a {kind} API key";
        return prefix is null ? head : $"{head} ({prefix})";
    }

    private static string TagDeleted(JsonElement? root)
    {
        var head = Join("Deleted tag", Text(root, "slug"), null, "Deleted a tag");
        return Count(root, "detachedTicketCount") is { } count ? $"{head}, removed from {Plural(count, "ticket", "tickets")}" : head;
    }

    private static string RequesterErased(JsonElement? root)
    {
        var parts = new List<string>();
        Add(parts, Count(root, "tickets"), "ticket", "tickets");
        Add(parts, Count(root, "messages"), "message", "messages");
        Add(parts, Count(root, "attachments"), "attachment", "attachments");
        Add(parts, Count(root, "links"), "access link", "access links");
        Add(parts, Count(root, "outboxRows"), "queued email", "queued emails");
        return parts.Count == 0 ? "Erased a requester" : $"Erased a requester: {string.Join(", ", parts)}";
    }

    private static string TicketDeleted(JsonElement? root)
    {
        var head = Join("Deleted ticket", Text(root, "number"), null, "Deleted a ticket");
        var parts = new List<string>();
        Add(parts, Count(root, "messageCount"), "message", "messages");
        Add(parts, Count(root, "attachmentCount"), "attachment", "attachments");
        return parts.Count == 0 ? head : $"{head} ({string.Join(", ", parts)})";
    }

    private static string DeadLetter(JsonElement? root, string verb, string attemptsWord)
    {
        var kind = Text(root, "kind");
        var head = kind is null ? $"{verb} email" : $"{verb} {EmailKinds.Label(kind).ToLowerInvariant()} email";
        return Count(root, "attempts") is { } attempts ? $"{head} {attemptsWord} {Plural(attempts, "attempt", "attempts")}" : head;
    }

    private static string Changed(string head, JsonElement? root, Func<string, string> label)
    {
        var changed = List(root, "changed");
        return changed.Count == 0 ? head : $"{head}: {string.Join(", ", changed.Select(label))}";
    }

    private static string ProductChange(string field) => field switch
    {
        "name" => "name",
        "branding" => "branding",
        "isActive" => "active status",
        _ => field,
    };

    private static string TagChange(string field) => field switch
    {
        "name" => "name",
        "colour" => "colour",
        _ => field,
    };

    // ---- reading a payload that may be anything ------------------------------------------------------------------

    private static JsonDocument? TryParse(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement? root, string name) =>
        root is { } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? Clip(text) : null;

    private static int? Count(JsonElement? root, string name) =>
        root is { } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 ? number : null;

    private static bool? Flag(JsonElement? root, string name) =>
        root is { } element && element.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) ? value.GetBoolean() : null;

    private static List<string> List(JsonElement? root, string name)
    {
        var result = new List<string>();
        if (root is { } element && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            result.AddRange(value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => Clip(item.GetString() ?? string.Empty)).Where(text => text.Length > 0).Take(8));
        }

        return result;
    }

    // ---- wording --------------------------------------------------------------------------------------------------

    // "Created product orbitly (ticket prefix ORB)": the head, then the first value, then an optional second; with no values, the fallback sentence.
    private static string Join(string head, string? first, string? second, string fallback)
    {
        if (first is null && second is null)
        {
            return fallback;
        }

        return string.Join(' ', new[] { head, first, second }.Where(part => !string.IsNullOrEmpty(part)));
    }

    private static string? Wrap(string? label, string? value) =>
        value is null ? null : label is null ? $"({value})" : $"({label} {value})";

    private static void Add(List<string> parts, int? count, string singular, string plural)
    {
        if (count is { } value)
        {
            parts.Add(Plural(value, singular, plural));
        }
    }

    private static string Plural(int count, string singular, string plural) => $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural)}";

    /// <summary>"DeadLetterRetried" gives "Dead letter retried": what an event type this app does not know is shown as.</summary>
    private static string Humanize(string type)
    {
        var text = Clip(type);
        var builder = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (i > 0 && char.IsUpper(text[i]) && !char.IsUpper(text[i - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(i == 0 || !char.IsUpper(text[i]) ? text[i] : char.ToLowerInvariant(text[i]));
        }

        return builder.Length == 0 ? "Admin event" : builder.ToString();
    }

    private static string Clip(string value)
    {
        var clean = new string([.. value.Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        return clean.Length <= MaxValueLength ? clean : string.Concat(clean.AsSpan(0, MaxValueLength - 1), "\u2026");
    }
}
```

`src/TechStrap.Admin/Features/Settings/Audit/AuditCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Settings.Audit;

/// <summary>The copy of the audit log: who changed configuration and who ran privacy and operations actions. Read-only.</summary>
public static class AuditCopy
{
    public const int PageSize = 25;
    public const string SubjectKey = "subject";
    public const string ActorKey = "actor";
    public const string PageKey = "page";

    public const string Heading = "Audit log";
    public const string Intro = "Changes to products, API keys, agents and tags, and who ran an erase, a delete or a failed-email action. Newest first.";
    public const string Loading = "Loading the audit log";
    public const string LoadFailed = "Couldn't load the audit log.";
    public const string NoEvents = "No audit events yet";
    public const string NoMatches = "No events match these filters";
    public const string ClearFilters = "Clear filters";
    public const string Refresh = "Refresh";

    public const string SubjectFilter = "What changed";
    public const string ActorFilter = "Who";
    public const string AllSubjects = "Everything";
    public const string AllActors = "Anyone";
    public const string ColumnWhen = "When";
    public const string ColumnWho = "Who";
    public const string ColumnWhat = "What changed";
    public const string ColumnSummary = "What happened";
    public const string UnknownActor = "Unknown agent";
}
```

`src/TechStrap.Admin/Features/Settings/Audit/AuditFilters.cs`

```csharp
using TechStrap.Contracts.AdminEvents;

namespace TechStrap.Admin.Features.Settings.Audit;

/// <summary>What the audit log can be filtered by (the API supports only the subject type and the actor, plus the as-of time that keeps paging stable), and the link for a filter set.</summary>
public static class AuditFilters
{
    public static IReadOnlyList<string> Subjects { get; } =
    [
        AdminSubjectTypes.Product,
        AdminSubjectTypes.ApiKey,
        AdminSubjectTypes.Agent,
        AdminSubjectTypes.Tag,
        AdminSubjectTypes.Requester,
        AdminSubjectTypes.Ticket,
        AdminSubjectTypes.EmailOutbox,
    ];

    /// <summary>The canonical spelling of a subject type from the query string (case-insensitive), or null when it is blank or unknown: an unknown value is dropped, never sent.</summary>
    public static string? CanonicalSubject(string? value) => Subjects.FirstOrDefault(s => s.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string Uri(string? subject, Guid? actor, int page)
    {
        var query = new List<string>();
        if (subject is not null)
        {
            query.Add($"{AuditCopy.SubjectKey}={System.Uri.EscapeDataString(subject)}");
        }

        if (actor is { } id)
        {
            query.Add($"{AuditCopy.ActorKey}={id}");
        }

        if (page > 1)
        {
            query.Add($"{AuditCopy.PageKey}={page}");
        }

        return query.Count == 0 ? "/settings/audit" : $"/settings/audit?{string.Join('&', query)}";
    }
}
```

`src/TechStrap.Admin/Features/Settings/Audit/AdminEventRowViewModel.cs`

```csharp
using TechStrap.Contracts.AdminEvents;

namespace TechStrap.Admin.Features.Settings.Audit;

internal sealed record AdminEventRowViewModel(Guid Id, DateTimeOffset At, string Actor, string Subject, string Summary)
{
    public static AdminEventRowViewModel From(AdminEventDto admin) => new(
        admin.Id,
        admin.OccurredAt,
        string.IsNullOrWhiteSpace(admin.ActorLabel) ? AuditCopy.UnknownActor : admin.ActorLabel,
        AdminEventSummaryFactory.SubjectLabel(admin.SubjectType),
        AdminEventSummaryFactory.Summarize(admin.Type, admin.Payload));
}
```

`src/TechStrap.Admin/Features/Settings/Audit/AdminEventsPage.razor`

```razor
@page "/settings/audit"
@attribute [Authorize]

<AdminOnly>
    <AdminEventsContent />
</AdminOnly>
```

`src/TechStrap.Admin/Features/Settings/Audit/AdminEventsContent.razor`

```razor
<PageTitle>@AuditCopy.Heading &middot; Settings</PageTitle>
    <div class="ts-settings ts-audit">
        <h1>@AuditCopy.Heading</h1>
        <p class="ts-field-help">@AuditCopy.Intro</p>

        <div class="ts-filterbar">
            <div class="ts-field">
                <label for="ts-audit-subject" class="form-label">@AuditCopy.SubjectFilter</label>
                <select id="ts-audit-subject" class="form-select" @onchange="OnSubjectChanged">
                    <option value="" selected="@(_subject is null)">@AuditCopy.AllSubjects</option>
                    @foreach (var subject in Subjects)
                    {
                        <option value="@subject" selected="@(_subject == subject)">@AdminEventSummaryFactory.SubjectLabel(subject)</option>
                    }
                </select>
            </div>
            <div class="ts-field">
                <label for="ts-audit-actor" class="form-label">@AuditCopy.ActorFilter</label>
                <select id="ts-audit-actor" class="form-select" @onchange="OnActorChanged">
                    <option value="" selected="@(_actor is null)">@AuditCopy.AllActors</option>
                    @foreach (var agent in _agents)
                    {
                        <option value="@agent.Id" selected="@(_actor == agent.Id)">@(string.IsNullOrWhiteSpace(agent.Name) ? agent.DisplayLabel : agent.Name)</option>
                    }
                </select>
            </div>
            <button type="button" class="btn btn-outline-secondary" @onclick="RefreshAsync">@AuditCopy.Refresh</button>
        </div>
        <p class="visually-hidden" role="status">@_announcement</p>

        @if (_loading && _rows.Count == 0)
        {
            <LoadingState Rows="6" Label="@AuditCopy.Loading" />
        }
        else if (_error is not null)
        {
            <ErrorState Message="@_error" OnRetry="RefreshAsync" />
        }
        else if (_rows.Count == 0)
        {
            @if (_subject is not null || _actor is not null)
            {
                <EmptyState Heading="@AuditCopy.NoMatches">
                    <a class="btn btn-outline-secondary" href="/settings/audit">@AuditCopy.ClearFilters</a>
                </EmptyState>
            }
            else
            {
                <EmptyState Heading="@AuditCopy.NoEvents" />
            }
        }
        else
        {
            <div class="table-responsive">
                <table class="table ts-ledger ts-settings-table" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
                            <th scope="col">@AuditCopy.ColumnWhen</th>
                            <th scope="col">@AuditCopy.ColumnWho</th>
                            <th scope="col">@AuditCopy.ColumnWhat</th>
                            <th scope="col">@AuditCopy.ColumnSummary</th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var row in _rows)
                        {
                            <tr @key="row.Id">
                                <td><RelativeTime When="row.At" /></td>
                                <td>@row.Actor</td>
                                <td>@row.Subject</td>
                                <td class="ts-audit-summary">@row.Summary</td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
            <PagerControl Page="_page" PageSize="AuditCopy.PageSize" TotalCount="_total" OnPageChanged="OnPageChanged" />
        }
    </div>

@code {
    private static IReadOnlyList<string> Subjects => AuditFilters.Subjects;
}
```

`src/TechStrap.Admin/Features/Settings/Audit/AdminEventsContent.razor.cs`

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Agents;

namespace TechStrap.Admin.Features.Settings.Audit;

/// <summary>
/// The audit log (Admin only, read-only): admin events newest first, 25 to a page, filtered by what changed (subject type) and who did it (an agent), both kept in the query string so a view can be linked.
/// The first page fixes an <c>asOf</c> time and every later page of the same filter carries it, so events recorded meanwhile do not shift the pages. Unknown filter values in the address are dropped, never sent.
/// Each event is shown as one sentence from <see cref="AdminEventSummaryFactory"/>; a payload is never rendered.
/// </summary>
public sealed partial class AdminEventsContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<AdminEventRowViewModel> _rows = [];
    private IReadOnlyList<AgentListItemDto> _agents = [];
    private (string? Subject, Guid? Actor, int Page)? _loadedFor;
    private (string? Subject, Guid? Actor)? _filterOfAsOf;
    private DateTimeOffset? _asOf;
    private string? _subject;
    private Guid? _actor;
    private string? _error;
    private string _announcement = string.Empty;
    private int _page = 1;
    private int _total;
    private bool _loading = true;
    private bool _agentsLoaded;

    [Inject]
    private IAdminEventsClient Events { get; set; } = default!;

    [Inject]
    private IAgentsClient AgentsClient { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [SupplyParameterFromQuery(Name = AuditCopy.SubjectKey)]
    public string? Subject { get; set; }

    [SupplyParameterFromQuery(Name = AuditCopy.ActorKey)]
    public string? Actor { get; set; }

    [SupplyParameterFromQuery(Name = AuditCopy.PageKey)]
    public string? PageNumber { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        _subject = AuditFilters.CanonicalSubject(Subject);
        _actor = Guid.TryParse(Actor, out var actor) ? actor : null;
        _page = int.TryParse(PageNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 1;
        if (!_agentsLoaded)
        {
            _agentsLoaded = true;
            await LoadAgentsAsync();
        }

        var key = (_subject, _actor, _page);
        if (_loadedFor != key)
        {
            _loadedFor = key;
            await LoadAsync();
        }
    }

    // The filter's actor list. If it cannot be read the filter simply has no names to offer; the log itself still works.
    private async Task LoadAgentsAsync()
    {
        var result = await AgentsClient.ListAllAsync(_lifetime.Token);
        if (!_lifetime.IsCancellationRequested && result.IsSuccess)
        {
            _agents = [.. result.Value.OrderBy(a => a.Name ?? a.DisplayLabel, StringComparer.OrdinalIgnoreCase)];
        }
    }

    private async Task LoadAsync()
    {
        // The first page of a filter fixes "as of". Later pages of the same filter reuse it, so a new event cannot push rows onto the next page while the admin reads.
        var filter = (_subject, _actor);
        if (_page == 1 || _asOf is null || _filterOfAsOf != filter)
        {
            _asOf = Time.GetUtcNow();
            _filterOfAsOf = filter;
        }

        _loading = true;
        _error = null;
        try
        {
            var result = await Events.ListAsync(new AdminEventFilter(_subject, _actor, _asOf), _page, AuditCopy.PageSize, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Items.Select(AdminEventRowViewModel.From)];
                _total = result.Value.TotalCount;
                _announcement = $"{_total} events";
            }
            else
            {
                _error = $"{AuditCopy.LoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    // Refresh starts again from now: a new "as of" for the page on screen.
    private async Task RefreshAsync()
    {
        _asOf = null;
        await LoadAsync();
    }

    private void OnSubjectChanged(ChangeEventArgs e) =>
        Navigation.NavigateTo(AuditFilters.Uri(AuditFilters.CanonicalSubject(e.Value?.ToString()), _actor, 1));

    private void OnActorChanged(ChangeEventArgs e) =>
        Navigation.NavigateTo(AuditFilters.Uri(_subject, Guid.TryParse(e.Value?.ToString(), out var actor) ? actor : null, 1));

    private void OnPageChanged(int page) => Navigation.NavigateTo(AuditFilters.Uri(_subject, _actor, page));

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

4. The styles:

`src/TechStrap.Admin/Styles/_tags.scss`

```scss
// Tags page (PHASE-07b): the colour row and the delete count.

.ts-colour-row {
  display: flex;
  gap: 12px;
  align-items: center;

  .form-control {
    max-width: 12rem;
    font-family: var(--ts-font-mono);
  }
}

.ts-colour-preview {
  min-width: 0;
  overflow-wrap: anywhere;
}

.ts-tag-count {
  font-variant-numeric: tabular-nums;
}

.ts-delete-count {
  margin: 0 0 8px;
  font: 600 .9375rem var(--ts-font-mono);
}
```

`src/TechStrap.Admin/Styles/_audit.scss`

```scss
// Audit log (PHASE-07b): the sentence column wraps (it may carry a long slug or key prefix), and nothing in it is markup.

.ts-audit-summary {
  min-width: 0;
  overflow-wrap: anywhere;
}
```

```diff
@@
 @import "account";
+@import "tags";
+@import "audit";
 @import "styleguide";
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "TagsPageTests|DeleteTagTests|AdminEventSummaryFactoryTests|AdminEventsPageTests|SourceEncodingTests|SourceEscapeTests"`
Expected: PASS. To see the pins guard (do not commit), make each change, rerun `--filter "DeleteTagTests|AdminEventsPageTests"`, expect the failures named, and restore:
- `TagsContent.ConfirmDeleteAsync`: `var force = true;`: `An_unused_tag_asks_first_...` and `A_tag_that_gained_tickets_...` fail (and the in-use cases still pass, which is why the unused ones matter).
- `TagsContent.razor`: pass `RequiredText="@((string?)null)"` to the dialog: `Typing_the_name_in_any_case_deletes_once_with_force_...`, `Without_the_name_typed_confirm_does_nothing_...` and four more fail.
- `AdminEventsContent.LoadAsync`: replace the `asOf` condition with `if (true)`: `A_later_page_carries_the_asOf_of_the_first_page_...` fails.

- [ ] **Step 5: Whole-project check**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), `dotnet test --project tests/TechStrap.Admin.Tests -c Release`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Admin/Features/Settings/EmailKinds.cs \
  src/TechStrap.Admin/Features/Settings/Tags \
  src/TechStrap.Admin/Features/Settings/Audit \
  src/TechStrap.Admin/Styles \
  tests/TechStrap.Admin.Tests
git diff --cached --stat
git commit -m "feat(admin): tags with ticket counts and the audit log (P07-T17)" -m "A tag in use is deleted only after its name is typed, with force. The audit log shows one sentence per event from ids and counts and never renders a payload." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 9: Failed emails page, host and leak tests, and the closing docs (P07-T18)

**Review Focus pins:** (5) a discard never fires without its confirmation and nothing fires twice (`DeadLettersPageTests`); (1) a plain agent never reaches an admin page or action by address or navigation, and the guard neither denies an admin nor shows the page while the session loads (`AdminSettingsHostTests`); (2) the API key plaintext never leaks (`AdminLeakTests`, extended here).

**Files:**
- Create: `src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLettersCopy.cs`, `DeadLetterRowViewModel.cs`, `DeadLettersContent.razor` and `.razor.cs`, `DeadLettersPage.razor`
- Create: `src/TechStrap.Admin/Styles/_ops.scss`; Modify: `src/TechStrap.Admin/Styles/app.scss`
- Create: `tests/TechStrap.Admin.Tests/Support/TestData.DeadLetters.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/DeadLettersPageTests.cs`
- Create: `tests/TechStrap.Admin.Tests/AdminSettingsHostTests.cs`
- Modify: `tests/TechStrap.Api.Tests/AdminLeakTests.cs`
- Modify: `docs/development/ADMIN-APP.md`, `docs/architecture/PHASE-07-admin-app.md`, `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, `docs/architecture/00-DISCOVERY-INDEX.md`

**Interfaces:**
- Consumes:
  - `IDeadLettersClient.ListAsync(int page, int pageSize, ct)` (`Result<PagedResponse<DeadLetterDto>>`), `RetryAsync(Guid, ct)`, `DiscardAsync(Guid, ct)` (`Result`); `ApiErrorCodes.OutboxNotFound`, `OutboxNotDeadLettered`, `IsUncertainWrite`.
  - `FailedEmailCounter` (`Set(int)`, `RefreshAsync(ct)`, `Count`; scoped, registered by `AddShell`; Task 4) and the rail (`NavMenu`) with the admin links `/settings/products`, `/settings/agents`, `/settings/tags`, `/settings/audit`, `/ops/dead-letters` and `ShellCopy.MySettingsLink` (`/account/notifications`); `AdminOnly`, `EmailKinds` (Task 8), `ConfirmDialog`, `PagerControl`, `RelativeTime`.
  - Host test support: `AdminFactory` in both test projects, `AdminTestPrincipal.Agent/Admin/Outsider` (`AccessToken`, internal `ToClaimsPrincipal(scheme)`), `AdminTestAuth.Scheme`, `client.SignedInAs`, `StubApiHandler.OnJson/On/OnProblem`, `Requests`, `AssertEveryCallBore`, the Api.Tests factory's `LogSink.Events`.
  - Everything in Tasks 5 to 8 (the page routes and their copy).
- Produces: `DeadLettersPage` (shell for `/ops/dead-letters`), `DeadLettersContent`, `DeadLettersCopy`; no other production code. The docs below.

**Rules:**
1. **Failed emails.** Each row: the recipient exactly as the API masked it (the page never has the full address), the kind (`EmailKinds.Label`), a link to `/tickets/{ticketId}` when there is one (the ticket page's route takes the id as well as the number), the tries, the last error as a plain category (`smtp-transient` and the other four have their own words; anything else is shortened to 80 characters and shown as text, never as markup), and the created time. Empty is the positive plain state "No failed emails".
2. **Retry needs no confirmation and fires once**; every action button is disabled while one runs. **Discard is a medium-tier confirmation** that names the kind and the masked recipient and says "TechStrap will stop trying to send it, and it will not be sent. This can't be undone."; Cancel and Esc make no call; Confirm fires once.
3. **After a retry or a discard** the list is read again and `FailedEmailCounter.Set(total)` follows from that read (the page sets it on every successful load), so the rail badge shows the new total without a second call; a retry that fails again comes back with its new error. `outbox-not-found` and `outbox-not-dead-lettered` ("already retried or discarded") say so and refresh. A failure says "Couldn't retry the email. Nothing was changed. {message}" (or discard); a lost answer says the retry or discard may have gone through, never "nothing was changed", disables a second discard and offers Reload list. Writes use `CancellationToken.None` and are never retried; a finish after the page is gone changes nothing.
4. **Pin 1 at the host.** `AdminSettingsHostTests` opens every admin address as a plain agent and asserts the stub saw only `GET /api/agents/me` (zero admin API calls), the page-level no-access page, no admin data in the HTML and no admin links in the rail; as an admin it asserts each page shows its data, the first call is `/api/agents/me` and every call bore the token; anonymous and refused users never reach a page. My settings is open to a plain agent and asks only for what an agent may read.
5. **Pin 2 at the host.** The leak test creates a key through the host's real client pipeline as the admin (the scope gets the sign-in a circuit would have), proves the secret really travelled, then opens the keys, editor and list pages and asserts the secret is in no page, no request path or query, no request body the Admin sent, and no Serilog event; the secret has a shape the log redactor does not mask, so the test proves the Admin never logs it, not that the redactor hid it. The second test opens all nine settings pages as an admin and asserts the access token is in no page and no log line.
6. **Docs.** `ADMIN-APP.md` describes what an admin can do, the role table, the layout, the tests and the known gaps; `PHASE-07-admin-app.md` ticks T14, T15, T16, T17, T18 and T23 with their evidence and the two carry-forwards this phase closes; the roadmap and the index say "07a merged; 07b implemented (pending merge); 07c not started". A script applies them and asserts every replaced text exists exactly once.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Admin.Tests/Support/TestData.DeadLetters.cs`

```csharp
using TechStrap.Admin.Features.Settings;
using TechStrap.Contracts.DeadLetters;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static DeadLetterDto DeadLetter(
        string kind = EmailKinds.AgentReply,
        string recipient = "a***@example.com",
        Guid? ticketId = null,
        int attempts = 5,
        string? lastError = "smtp-transient",
        Guid? id = null,
        DateTimeOffset? created = null) => new(id ?? Guid.NewGuid(), kind, recipient, ticketId, OrbitlyId, attempts, lastError, created ?? Now.AddHours(-3));
}
```

`tests/TechStrap.Admin.Tests/Components/DeadLettersPageTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Ops.DeadLetters;
using TechStrap.Admin.Features.Settings;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The failed emails. Review Focus 5: Discard asks first and fires once; Retry needs no confirmation and fires once. Every outcome that is not a plain success refreshes or explains, an unknown outcome never claims
/// nothing happened, and the navigation badge is refreshed after every write.
/// </summary>
public sealed class DeadLettersPageTests : AdminPageTest
{
    private readonly IDeadLettersClient _letters = Substitute.For<IDeadLettersClient>();
    private readonly NavigationManager _navigation;
    private readonly Guid _first = Guid.Parse("dddddddd-1111-0000-0000-000000000001");
    private readonly Guid _second = Guid.Parse("dddddddd-1111-0000-0000-000000000002");

    public DeadLettersPageTests()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(Page(
            TestData.DeadLetter(EmailKinds.AgentReply, "a***@example.com", TestData.TicketId, 5, "smtp-transient", _first),
            TestData.DeadLetter(EmailKinds.NewTicketAlert, "s***@orbitly.test", null, 3, "<b>550 mailbox unavailable</b> " + new string('x', 200), _second))));
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok());
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok());
        Services.AddSingleton(_letters);
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private static PagedResponse<DeadLetterDto> Page(params DeadLetterDto[] items) => new(items, 1, DeadLettersCopy.PageSize, items.Length);

    private FailedEmailCounter Counter => Services.GetRequiredService<FailedEmailCounter>();

    private IRenderedComponent<DeadLettersPage> RenderPage(string query = "")
    {
        _navigation.NavigateTo($"/ops/dead-letters{query}");
        return Render<DeadLettersPage>();
    }

    private int Calls(string method) => _letters.ReceivedCalls().Count(c => c.GetMethodInfo().Name == method);

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<DeadLettersPage> cut, Guid id) => cut.Find($"tr[data-letter='{id}']");

    private static AngleSharp.Dom.IElement DiscardDialog(IRenderedComponent<DeadLettersPage> cut) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent == "Discard this failed email?");

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<DeadLettersPage> cut) => DiscardDialog(cut).QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    // ---- the rows ------------------------------------------------------------------------------------------------

    [Fact]
    public void Each_row_shows_the_masked_recipient_kind_ticket_link_tries_last_error_category_and_created_time()
    {
        var cut = RenderPage();

        var row = Row(cut, _first);
        row.Children[0].TextContent.ShouldBe("a***@example.com");
        row.Children[1].TextContent.ShouldBe("Agent reply");
        row.Children[2].QuerySelector("a")!.GetAttribute("href").ShouldBe($"/tickets/{TestData.TicketId}");
        row.Children[3].TextContent.ShouldBe("5");
        row.Children[4].TextContent.ShouldBe("Temporary SMTP problem");
        row.Children[5].TextContent.ShouldBe("3 h ago");
        Row(cut, _second).Children[2].QuerySelectorAll("a").ShouldBeEmpty();
        cut.Find("h1").TextContent.ShouldBe("Failed emails");
    }

    [Fact]
    public void Free_text_in_the_last_error_is_cut_short_and_encoded_never_markup()
    {
        var cut = RenderPage();

        var error = Row(cut, _second).Children[4];
        error.TextContent.Length.ShouldBeLessThanOrEqualTo(80);
        error.TextContent.ShouldEndWith("\u2026");
        error.QuerySelectorAll("b").ShouldBeEmpty();
        cut.Markup.ShouldContain("&lt;b&gt;550 mailbox unavailable");
    }

    [Theory]
    [InlineData("smtp-transient", "Temporary SMTP problem")]
    [InlineData("smtp-permanent", "SMTP refused the email")]
    [InlineData("smtp-authentication", "SMTP sign-in failed")]
    [InlineData("smtp-timeout", "SMTP timed out")]
    [InlineData("smtp-unknown", "Unknown SMTP error")]
    [InlineData(null, "No error recorded")]
    [InlineData("", "No error recorded")]
    [InlineData("Lease expired", "Lease expired")]
    public void The_last_error_category_has_plain_words(string? lastError, string expected)
    {
        DeadLettersCopy.ErrorLabel(lastError).ShouldBe(expected);
    }

    [Fact]
    public void No_failed_emails_is_a_positive_plain_empty_state()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(Page()));

        var cut = RenderPage();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No failed emails");
        cut.FindAll("table").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again_and_refreshes_the_badge()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<PagedResponse<DeadLetterDto>>("api-error", "The API is unavailable."),
            TestData.Ok(Page(TestData.DeadLetter(id: _first))));
        var cut = RenderPage();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the failed emails. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void The_first_page_asks_for_25_and_a_page_in_the_query_string_asks_for_that_page()
    {
        RenderPage("?page=2");

        _letters.Received(1).ListAsync(2, 25, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Paging_goes_through_the_query_string()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new PagedResponse<DeadLetterDto>([TestData.DeadLetter(id: _first)], 1, 25, 60)));
        var cut = RenderPage();

        cut.FindAll(".ts-pager button").Single(b => b.TextContent == "Next").Click();

        _navigation.Uri.ShouldEndWith("/ops/dead-letters?page=2");
    }

    [Fact]
    public void A_plain_agent_gets_the_no_access_page_and_the_api_is_not_asked()
    {
        AsAgent();

        var cut = RenderPage();

        cut.Find("section.ts-no-access").ShouldNotBeNull();
        cut.FindAll("table").ShouldBeEmpty();
        _letters.ReceivedCalls().ShouldBeEmpty();
    }

    // ---- retry: no confirmation, once ----------------------------------------------------------------------------

    [Fact]
    public void Retry_needs_no_confirmation_calls_retry_once_for_that_row_reads_the_list_again_and_the_badge_follows()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(Page(TestData.DeadLetter(id: _first), TestData.DeadLetter(id: _second))),
            TestData.Ok(Page(TestData.DeadLetter(id: _second))));
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        _letters.Received(1).RetryAsync(_first, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Calls(nameof(IDeadLettersClient.RetryAsync)).ShouldBe(1);
        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2);
        cut.FindAll("tr[data-letter]").Count.ShouldBe(1);
        Counter.Count.ShouldBe(1, "the badge follows the total of the list that was read again");
        StatusMessages.Current.ShouldBe("Queued a retry for the agent reply email");
        Dialogs.VerifyNotInvoke("open");
    }

    [Fact]
    public void A_retry_that_fails_again_comes_back_in_the_list_with_its_new_error()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(Page(TestData.DeadLetter(id: _first, lastError: "smtp-transient"))),
            TestData.Ok(Page(TestData.DeadLetter(id: _first, attempts: 6, lastError: "smtp-permanent"))));
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        Row(cut, _first).Children[3].TextContent.ShouldBe("6");
        Row(cut, _first).Children[4].TextContent.ShouldBe("SMTP refused the email");
    }

    [Fact]
    public void Two_clicks_on_retry_while_the_first_runs_send_one_request()
    {
        var gate = new TaskCompletionSource<Result>();
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();
        cut.FindAll("button.ts-retry, button.ts-discard").ShouldAllBe(b => b.HasAttribute("disabled"));
        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        Calls(nameof(IDeadLettersClient.RetryAsync)).ShouldBe(1);
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2));
    }

    [Fact]
    public void A_failed_retry_says_nothing_changed_and_keeps_the_row_and_the_badge_as_they_were()
    {
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("boom", "The API refused."));
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        cut.Find(".ts-conflict[role=alert]").TextContent.ShouldContain("Couldn't retry the email. Nothing was changed. The API refused.");
        cut.FindAll("tr[data-letter]").Count.ShouldBe(2);
        Counter.Count.ShouldBe(2, "a failed write changed nothing, so the badge still shows the list that was read first");
        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public void A_lost_retry_answer_never_claims_nothing_changed_and_offers_a_reload_that_refreshes_the_badge()
    {
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        var alert = cut.Find(".ts-conflict[role=alert]");
        alert.TextContent.ShouldContain("The retry may have been queued.");
        alert.TextContent.ShouldNotContain("Nothing was changed");

        alert.QuerySelector("button")!.Click();

        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2);
        Counter.Count.ShouldNotBeNull();
        Calls(nameof(IDeadLettersClient.RetryAsync)).ShouldBe(1);
    }

    [Theory]
    [InlineData(ApiErrorCodes.OutboxNotFound, "That email isn't in the list any more.")]
    [InlineData(ApiErrorCodes.OutboxNotDeadLettered, "That email was already retried or discarded.")]
    public void An_email_that_is_already_handled_says_so_and_reads_the_list_again_and_the_badge_follows(string code, string message)
    {
        _letters.RetryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(code, "No such row.", ResultErrorKind.NotFound));
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-retry")!.Click();

        StatusMessages.Current.ShouldBe(message);
        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2);
        Counter.Count.ShouldNotBeNull();
    }

    // ---- discard: asks first, once -------------------------------------------------------------------------------

    [Fact]
    public void Discard_asks_first_naming_the_email_and_the_consequence_and_calls_nothing_yet()
    {
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        var dialog = DiscardDialog(cut);
        dialog.TextContent.ShouldContain("Agent reply to a***@example.com.");
        dialog.TextContent.ShouldContain("TechStrap will stop trying to send it, and it will not be sent. This can't be undone.");
        dialog.QuerySelector("input").ShouldBeNull("a medium confirmation: no typed text");
        Confirm(cut).TextContent.ShouldBe("Discard email");
        Confirm(cut).ClassName!.ShouldContain("btn-danger");
        Dialogs.VerifyInvoke("open", 1);
        Calls(nameof(IDeadLettersClient.DiscardAsync)).ShouldBe(0);
    }

    [Fact]
    public void Cancel_and_Esc_close_the_confirmation_and_make_no_call()
    {
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();
        DiscardDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();
        DiscardDialog(cut).TriggerEvent("oncancel", EventArgs.Empty);

        Calls(nameof(IDeadLettersClient.DiscardAsync)).ShouldBe(0);
        Dialogs.VerifyInvoke("close", 2);
        cut.FindAll("tr[data-letter]").Count.ShouldBe(2);
    }

    [Fact]
    public void Confirming_discards_that_email_once_reads_the_list_again_and_the_badge_follows_and_says_so()
    {
        _letters.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(Page(TestData.DeadLetter(id: _first), TestData.DeadLetter(id: _second))),
            TestData.Ok(Page(TestData.DeadLetter(id: _second))));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        Confirm(cut).Click();

        _letters.Received(1).DiscardAsync(_first, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        Calls(nameof(IDeadLettersClient.DiscardAsync)).ShouldBe(1);
        cut.FindAll("tr[data-letter]").Count.ShouldBe(1);
        Counter.Count.ShouldNotBeNull();
        StatusMessages.Current.ShouldBe("Discarded the agent reply email");
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_double_click_on_confirm_discards_once_and_the_dialog_is_inert_while_it_runs()
    {
        var gate = new TaskCompletionSource<Result>();
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        Confirm(cut).Click();
        DiscardDialog(cut).QuerySelectorAll("button").ShouldAllBe(b => b.HasAttribute("disabled"));
        DiscardDialog(cut).TriggerEvent("oncancel", EventArgs.Empty);
        Confirm(cut).Click();

        Calls(nameof(IDeadLettersClient.DiscardAsync)).ShouldBe(1);
        Dialogs.VerifyNotInvoke("close");
        gate.SetResult(TestData.Ok());
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("close", 1));
    }

    [Fact]
    public void Choosing_a_different_row_names_that_row_and_discards_that_one()
    {
        var cut = RenderPage();

        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();
        DiscardDialog(cut).QuerySelector("button.btn-outline-secondary")!.Click();
        Row(cut, _second).QuerySelector("button.ts-discard")!.Click();
        DiscardDialog(cut).TextContent.ShouldContain("New ticket alert to s***@orbitly.test.");
        Confirm(cut).Click();

        _letters.Received(1).DiscardAsync(_second, Arg.Any<CancellationToken>());
        _letters.DidNotReceive().DiscardAsync(_first, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_failed_discard_keeps_the_dialog_open_and_says_nothing_was_changed()
    {
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail("boom", "The API refused."));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        Confirm(cut).Click();

        DiscardDialog(cut).QuerySelector("[role=alert]")!.TextContent.ShouldBe("Couldn't discard the email. Nothing was changed. The API refused.");
        Dialogs.VerifyNotInvoke("close");
        cut.FindAll("tr[data-letter]").Count.ShouldBe(2);
    }

    [Fact]
    public void A_lost_discard_answer_never_claims_nothing_changed_blocks_a_second_discard_and_offers_a_reload()
    {
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "TechStrap took too long to answer. Try again."));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();
        Confirm(cut).Click();

        var dialog = DiscardDialog(cut);
        dialog.QuerySelector("[role=alert]")!.TextContent.ShouldBe("The discard may have gone through. Reload the list to check before you try again.");
        dialog.TextContent.ShouldNotContain("Nothing was changed");
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        Confirm(cut).Click();
        Calls(nameof(IDeadLettersClient.DiscardAsync)).ShouldBe(1);

        DiscardDialog(cut).QuerySelector(".ts-dialog-recovery button")!.Click();

        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2);
        Counter.Count.ShouldNotBeNull();
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_discard_of_an_email_that_is_already_gone_closes_the_dialog_says_so_and_refreshes()
    {
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.OutboxNotFound, "No such row.", ResultErrorKind.NotFound));
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();

        Confirm(cut).Click();

        StatusMessages.Current.ShouldBe("That email isn't in the list any more.");
        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(2);
        Dialogs.VerifyInvoke("close", 1);
    }

    [Fact]
    public void A_discard_that_finishes_after_the_page_is_gone_is_not_cancelled_and_reports_nothing()
    {
        var gate = new TaskCompletionSource<Result>();
        _letters.DiscardAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(gate.Task);
        var cut = RenderPage();
        Row(cut, _first).QuerySelector("button.ts-discard")!.Click();
        Confirm(cut).Click();

        cut.FindComponent<DeadLettersContent>().Instance.Dispose();
        gate.SetResult(TestData.Ok());

        _letters.Received(1).DiscardAsync(_first, Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        StatusMessages.Current.ShouldBeNull();
        Calls(nameof(IDeadLettersClient.ListAsync)).ShouldBe(1);
    }
}
```

`tests/TechStrap.Admin.Tests/AdminSettingsHostTests.cs`

```csharp
using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The whole Admin host with the fake sign-in and the stub API, for the settings and operations pages (PHASE-07b). Review Focus 1: a plain agent never reaches an admin page or action, whether by address or by
/// the navigation, and the guard neither denies an admin nor shows the page to an agent while the session is loading. Pages prerender, so the HTML already holds the data the stub served.
/// </summary>
public sealed class AdminSettingsHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid KeyId = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly string[] AdminLinks =
        ["/settings/products", "/settings/agents", "/settings/tags", "/settings/audit", "/ops/dead-letters"];

    // Every admin route with a sentence its page shows to an admin (taken from the stub data below).
    private static readonly (string Path, string Expected)[] Pages =
    [
        ("/settings/products", "Orbitly"),
        ("/settings/products/new", "New product"),
        ($"/settings/products/{OrbitlyId}", "Orbitly Cloud"),
        ($"/settings/products/{OrbitlyId}/keys", "tsk_ab12"),
        ("/settings/agents", "Roles come from your identity provider"),
        ("/settings/tags", "urgent"),
        ("/settings/audit", "Deleted tag bug, removed from 2 tickets"),
        ("/ops/dead-letters", "a***@example.com"),
    ];

    public static TheoryData<string, string> AdminPages
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (path, expected) in Pages)
            {
                data.Add(path, expected);
            }

            return data;
        }
    }

    public static TheoryData<string> AdminPaths
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var (path, _) in Pages)
            {
                data.Add(path);
            }

            return data;
        }
    }

    private static HttpClient NoRedirectClient(AdminFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>The factory with a happy-path API behind it: one product with a key, two agents, two tags, one audit event and one failed email.</summary>
    private static AdminFactory FactoryWithSettingsData()
    {
        var factory = new AdminFactory();
        var product = new ProductDto(OrbitlyId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly Cloud", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 7);
        var key = new ProductApiKeyDto(KeyId, OrbitlyId, ApiKeyKinds.Trusted, "tsk_ab12", "Billing server", Now.AddDays(-2), null, null);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, $"/api/products/{OrbitlyId}", product)
            .OnJson(HttpMethod.Get, $"/api/products/{OrbitlyId}/api-keys", (IReadOnlyList<ProductApiKeyDto>)[key])
            .OnJson(HttpMethod.Get, "/api/agents", new PagedResponse<AgentListItemDto>(
                [new AgentListItemDto(Guid.NewGuid(), "Sam Ortiz", "Sam Ortiz", "sam@example.com", AgentRoles.Agent, true, Now.AddHours(-1)),
                 new AgentListItemDto(Guid.NewGuid(), "Ada Admin", "Ada Admin", "ada@example.com", AgentRoles.Admin, true, Now)], 1, 25, 2))
            .OnJson(HttpMethod.Get, "/api/agents/me/notification-preferences", (IReadOnlyList<NotificationPreferenceDto>)[new NotificationPreferenceDto(OrbitlyId, "Orbitly", true)])
            .OnJson(HttpMethod.Get, "/api/tags/summary", (IReadOnlyList<TagSummaryDto>)[new TagSummaryDto(Guid.NewGuid(), "urgent", "urgent", "#DC2626", 12)])
            .OnJson(HttpMethod.Get, "/api/admin-events", new PagedResponse<AdminEventDto>(
                [new AdminEventDto(Guid.NewGuid(), AdminEventTypes.TagDeleted, Guid.NewGuid(), "Ada Admin", AdminSubjectTypes.Tag, Guid.NewGuid(), "{\"slug\":\"bug\",\"detachedTicketCount\":2}", Now.AddMinutes(-5))], 1, 25, 1))
            .OnJson(HttpMethod.Get, "/api/dead-letters", new PagedResponse<DeadLetterDto>(
                [new DeadLetterDto(Guid.NewGuid(), "agent-reply", "a***@example.com", null, OrbitlyId, 5, "smtp-transient", Now.AddHours(-3))], 1, 25, 1));
        return factory;
    }

    // ---- anonymous and refused users ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AdminPaths))]
    public async Task An_anonymous_visitor_is_sent_to_the_sign_in_landing_and_the_api_is_never_asked(string path)
    {
        await using var factory = FactoryWithSettingsData();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/signin");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AdminPaths))]
    public async Task A_user_the_api_refuses_gets_the_whole_app_no_access_page_and_only_the_me_call_is_made(string path)
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        var html = await client.GetStringAsync(path, Ct);

        (await new HtmlParser().ParseDocumentAsync(html, Ct)).QuerySelector("section.ts-no-access h1")!.TextContent.ShouldBe("You don't have access to TechStrap.");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Outsider);
    }

    // ---- Review Focus 1: a plain agent -----------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AdminPaths))]
    public async Task A_plain_agent_who_opens_an_admin_page_by_address_gets_the_page_level_no_access_and_no_admin_call_is_made(string path)
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync(path, Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await new HtmlParser().ParseDocumentAsync(html, Ct)).QuerySelector("section.ts-no-access h1")!.TextContent.ShouldBe("You don't have access to this page.");
        html.ShouldNotContain("Orbitly Cloud");
        html.ShouldNotContain("tsk_ab12");
        html.ShouldNotContain("urgent");
        html.ShouldNotContain("a***@example.com");
        html.ShouldNotContain("Deleted tag bug");
        html.ShouldNotContain("New product");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me", "a plain agent's page load must not call an endpoint the API would refuse");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task A_plain_agents_navigation_has_no_admin_links_but_has_my_settings()
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/", Ct);

        foreach (var link in AdminLinks)
        {
            html.ShouldNotContain($"href=\"{link}\"");
        }

        html.ShouldContain("href=\"/account/notifications\"");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task A_plain_agent_can_open_my_settings_and_it_asks_only_for_what_an_agent_may_read()
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync("/account/notifications", Ct);

        html.ShouldContain("My settings");
        html.ShouldContain("New tickets in Orbitly");
        html.ShouldContain("Customers see: ");
        html.ShouldNotContain("You don't have access");
        factory.Api.Requests.Select(r => r.Path).Distinct().Order().ShouldBe(["/api/agents/me", "/api/agents/me/notification-preferences", "/api/products"]);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    // ---- an admin ---------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AdminPages))]
    public async Task An_admin_sees_each_admin_page_with_its_data_and_the_api_is_asked_who_they_are_first(string path, string expected)
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var html = await client.GetStringAsync(path, Ct);

        html.ShouldContain(expected);
        html.ShouldNotContain("You don't have access");
        factory.Api.Requests[0].Path.ShouldBe("/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }

    [Fact]
    public async Task An_admins_navigation_has_every_admin_link_and_my_settings()
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var html = await client.GetStringAsync("/settings/tags", Ct);

        foreach (var link in AdminLinks)
        {
            html.ShouldContain($"href=\"{link}\"");
        }

        html.ShouldContain("href=\"/account/notifications\"");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }

    [Fact]
    public async Task The_admin_pages_never_render_the_raw_audit_payload_or_a_full_recipient_address()
    {
        await using var factory = FactoryWithSettingsData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var audit = await client.GetStringAsync("/settings/audit", Ct);
        var letters = await client.GetStringAsync("/ops/dead-letters", Ct);

        audit.ShouldNotContain("detachedTicketCount");
        audit.ShouldNotContain("&quot;slug&quot;");
        letters.ShouldContain("a***@example.com");
        letters.ShouldNotContain("ada@example.com");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }
}
```


`tests/TechStrap.Api.Tests/AdminLeakTests.cs`: add these `using` lines at the top of the file (the others are already there):

```csharp
using System.Net;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Contracts.DeadLetters;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
```

and add these two tests inside the class, after the existing test (the Api.Tests `AdminFactory` is the one with `LogSink`; `Everything` and `Ct` are already defined there):

```csharp
    // A secret shaped so the PII log redactor does not mask it: the test proves the Admin never logs it, not that the redactor hid it.
    private const string KeySecret = "tsk_live_leakcheck_0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task The_api_key_secret_appears_in_no_log_event_page_url_or_header_when_a_key_is_created_through_the_host_pipeline()
    {
        var productId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var key = new ProductApiKeyDto(Guid.Parse("eeeeeeee-0000-0000-0000-000000000001"), productId, ApiKeyKinds.Trusted, "tsk_leakchk", "CI", DateTimeOffset.UtcNow, null, null);
        var product = new ProductDto(productId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 3);
        await using var factory = new AdminFactory();
        factory.Api
            .OnJson(HttpMethod.Post, $"/api/products/{productId}/api-keys", new CreateProductApiKeyResponse(key, KeySecret), HttpStatusCode.Created)
            .OnJson(HttpMethod.Get, $"/api/products/{productId}/api-keys", (IReadOnlyList<ProductApiKeyDto>)[key])
            .OnJson(HttpMethod.Get, $"/api/products/{productId}", product)
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .On(HttpMethod.Delete, $"/api/products/{productId}/api-keys/{key.Id}", _ => new HttpResponseMessage(HttpStatusCode.NoContent));

        // The same call the keys page makes, through the host's own client pipeline (named clients, auth handler, token cache), as the admin: a circuit has no HTTP request, so the scope gets the sign-in the circuit would have.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var authentication = (IHostEnvironmentAuthenticationStateProvider)scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
            authentication.SetAuthenticationState(Task.FromResult(new AuthenticationState(AdminTestPrincipal.Admin.ToClaimsPrincipal(AdminTestAuth.Scheme))));
            var products = scope.ServiceProvider.GetRequiredService<IProductsClient>();

            var created = await products.CreateApiKeyAsync(productId, new CreateProductApiKeyRequest(ApiKeyKinds.Trusted, "CI"), CancellationToken.None);
            created.IsSuccess.ShouldBeTrue(string.Join("; ", created.IsFailure ? created.Errors.Select(e => e.Message) : []));
            created.Value.PlaintextKey.ShouldBe(KeySecret, "the secret really travelled through the pipeline, or this test proves nothing");
            (await products.RevokeApiKeyAsync(productId, key.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);
        var pages = new List<string>
        {
            await client.GetStringAsync($"/settings/products/{productId}/keys", Ct),
            await client.GetStringAsync($"/settings/products/{productId}", Ct),
            await client.GetStringAsync("/settings/products", Ct),
        };

        pages.ShouldAllBe(html => !html.Contains(KeySecret));
        pages[0].ShouldContain("tsk_leakchk");
        factory.Api.Requests.ShouldContain(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/api-keys", StringComparison.Ordinal));
        factory.Api.Requests.ShouldAllBe(r => !r.Path.Contains(KeySecret) && !r.Query.Contains(KeySecret), "the key is never in a URL");
        factory.Api.Requests.ShouldAllBe(r => r.Body == null || !r.Body.Contains(KeySecret), "nothing the Admin sends carries the key");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
        factory.LogSink.Events.ShouldNotBeEmpty();
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(KeySecret) && !text.Contains("Bearer ") && !text.Contains(AdminTestPrincipal.Admin.AccessToken));
    }

    [Fact]
    public async Task The_access_token_appears_in_no_log_event_or_page_of_any_admin_page_for_an_admin()
    {
        var productId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        await using var factory = new AdminFactory();
        var product = new ProductDto(productId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 3);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, $"/api/products/{productId}", product)
            .OnJson(HttpMethod.Get, $"/api/products/{productId}/api-keys", (IReadOnlyList<ProductApiKeyDto>)[])
            .OnJson(HttpMethod.Get, "/api/agents", new PagedResponse<AgentListItemDto>([], 1, 25, 0))
            .OnJson(HttpMethod.Get, "/api/agents/me/notification-preferences", (IReadOnlyList<NotificationPreferenceDto>)[])
            .OnJson(HttpMethod.Get, "/api/tags/summary", (IReadOnlyList<TagSummaryDto>)[])
            .OnJson(HttpMethod.Get, "/api/admin-events", new PagedResponse<AdminEventDto>([], 1, 25, 0))
            .OnJson(HttpMethod.Get, "/api/dead-letters", new PagedResponse<DeadLetterDto>([], 1, 25, 0));
        var token = AdminTestPrincipal.Admin.AccessToken;
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var pages = new List<string>();
        foreach (var path in new[]
                 {
                     "/settings/products", "/settings/products/new", $"/settings/products/{productId}", $"/settings/products/{productId}/keys", "/settings/agents", "/settings/tags",
                     "/settings/audit", "/ops/dead-letters", "/account/notifications",
                 })
        {
            pages.Add(await client.GetStringAsync(path, Ct));
        }

        factory.Api.Requests.ShouldContain(r => r.Authorization == "Bearer " + token, "the token must actually have been used, or this test proves nothing");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
        pages.ShouldAllBe(html => !html.Contains(token));
        factory.LogSink.Events.ShouldNotBeEmpty();
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text => !text.Contains(token) && !text.Contains("Bearer "));
    }
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "DeadLettersPageTests|AdminSettingsHostTests"`
Expected: a build failure (`DeadLettersPage`, `DeadLettersContent`, `DeadLettersCopy` do not exist).
Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter AdminLeakTests`
Expected: PASS already. The new leak tests exercise code from Tasks 3 to 8, so they are written green; see Step 4 for making them fail once on purpose.

- [ ] **Step 3: Implement the page**

`src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLettersCopy.cs`

```csharp
using TechStrap.Admin.Features.Settings;

namespace TechStrap.Admin.Features.Ops.DeadLetters;

/// <summary>
/// The copy of the failed-emails page. A "dead letter" is an email that used up its retries. The last error is shown as a plain category, never as the raw text, and a long or unknown text is cut short.
/// </summary>
public static class DeadLettersCopy
{
    public const int PageSize = 25;
    public const int MaxErrorLength = 80;

    public const string Heading = "Failed emails";
    public const string Intro = "Emails TechStrap gave up sending after several tries. Retry puts one back in the queue; Discard stops trying for good.";
    public const string Loading = "Loading failed emails";
    public const string LoadFailed = "Couldn't load the failed emails.";
    public const string Empty = "No failed emails";

    public const string ColumnRecipient = "Recipient";
    public const string ColumnEmail = "Email";
    public const string ColumnTicket = "Ticket";
    public const string ColumnAttempts = "Tries";
    public const string ColumnError = "Last error";
    public const string ColumnCreated = "Created";
    public const string OpenTicket = "Open ticket";
    public const string NoTicket = "\u2014";
    public const string Retry = "Retry";
    public const string Discard = "Discard";
    public const string ReloadList = "Reload list";

    public const string DiscardTitle = "Discard this failed email?";
    public const string DiscardConfirm = "Discard email";
    public const string DiscardBody = "TechStrap will stop trying to send it, and it will not be sent. This can't be undone.";
    public const string DiscardUncertain = "The discard may have gone through. Reload the list to check before you try again.";
    public const string AlreadyHandled = "That email was already retried or discarded.";
    public const string Gone = "That email isn't in the list any more.";

    public const string ErrorNone = "No error recorded";
    public const string ErrorTransient = "Temporary SMTP problem";
    public const string ErrorPermanent = "SMTP refused the email";
    public const string ErrorAuthentication = "SMTP sign-in failed";
    public const string ErrorTimeout = "SMTP timed out";
    public const string ErrorUnknown = "Unknown SMTP error";

    /// <summary>The words for the API's last-error category (<c>smtp-transient</c> and its siblings). Anything else is free text from the API: it is shortened and shown as text, never as markup.</summary>
    public static string ErrorLabel(string? lastError) => lastError switch
    {
        null or "" => ErrorNone,
        "smtp-transient" => ErrorTransient,
        "smtp-permanent" => ErrorPermanent,
        "smtp-authentication" => ErrorAuthentication,
        "smtp-timeout" => ErrorTimeout,
        "smtp-unknown" => ErrorUnknown,
        _ => Clip(lastError),
    };

    public static string Clip(string text)
    {
        var clean = new string([.. text.Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        return clean.Length <= MaxErrorLength ? clean : string.Concat(clean.AsSpan(0, MaxErrorLength - 1), "\u2026");
    }

    public static string KindLabel(string kind) => EmailKinds.Label(kind);

    public static string Retried(string kind) => $"Queued a retry for the {EmailKinds.Label(kind).ToLowerInvariant()} email";

    public static string Discarded(string kind) => $"Discarded the {EmailKinds.Label(kind).ToLowerInvariant()} email";

    public static string DiscardSubject(string kind, string recipient) => $"{EmailKinds.Label(kind)} to {recipient}.";

    public static string RetryFailed(string reason) => $"Couldn't retry the email. Nothing was changed. {reason}";

    public static string DiscardFailed(string reason) => $"Couldn't discard the email. Nothing was changed. {reason}";

    public const string RetryUncertain = "The retry may have been queued. Reload the list to check before you try again.";
}
```

`src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLetterRowViewModel.cs`

```csharp
using TechStrap.Contracts.DeadLetters;

namespace TechStrap.Admin.Features.Ops.DeadLetters;

/// <summary>One failed email. The recipient arrives already masked by the API ("a***@example.com"); the page never has the full address.</summary>
internal sealed record DeadLetterRowViewModel(Guid Id, string Kind, string Recipient, Guid? TicketId, int Attempts, string LastError, DateTimeOffset CreatedAt)
{
    public static DeadLetterRowViewModel From(DeadLetterDto letter) =>
        new(letter.Id, letter.Kind, letter.Recipient, letter.TicketId, letter.Attempts, DeadLettersCopy.ErrorLabel(letter.LastError), letter.CreatedAt);
}
```

`src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLettersPage.razor`

```razor
@page "/ops/dead-letters"
@attribute [Authorize]

<AdminOnly>
    <DeadLettersContent />
</AdminOnly>
```

`src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLettersContent.razor`

```razor
<PageTitle>@DeadLettersCopy.Heading &middot; Operations</PageTitle>
    <div class="ts-settings ts-dead-letters">
        <h1>@DeadLettersCopy.Heading</h1>
        <p class="ts-field-help">@DeadLettersCopy.Intro</p>

        @if (_rowError is not null)
        {
            <div class="ts-conflict" role="alert">
                <p>@_rowError</p>
                @if (_rowUncertain)
                {
                    <button type="button" class="btn btn-outline-secondary" @onclick="ReloadAsync">@DeadLettersCopy.ReloadList</button>
                }
            </div>
        }

        @if (_loading && _rows.Count == 0)
        {
            <LoadingState Rows="4" Label="@DeadLettersCopy.Loading" />
        }
        else if (_error is not null)
        {
            <ErrorState Message="@_error" OnRetry="ReloadAsync" />
        }
        else if (_rows.Count == 0)
        {
            <EmptyState Heading="@DeadLettersCopy.Empty" />
        }
        else
        {
            <div class="table-responsive">
                <table class="table ts-ledger ts-settings-table" aria-busy="@(_loading ? "true" : null)">
                    <thead>
                        <tr>
                            <th scope="col">@DeadLettersCopy.ColumnRecipient</th>
                            <th scope="col">@DeadLettersCopy.ColumnEmail</th>
                            <th scope="col">@DeadLettersCopy.ColumnTicket</th>
                            <th scope="col">@DeadLettersCopy.ColumnAttempts</th>
                            <th scope="col">@DeadLettersCopy.ColumnError</th>
                            <th scope="col">@DeadLettersCopy.ColumnCreated</th>
                            <th scope="col"><span class="visually-hidden">Actions</span></th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var row in _rows)
                        {
                            <tr @key="row.Id" data-letter="@row.Id">
                                <td><code>@row.Recipient</code></td>
                                <td>@DeadLettersCopy.KindLabel(row.Kind)</td>
                                <td>
                                    @if (row.TicketId is { } ticket)
                                    {
                                        <a href="/tickets/@ticket">@DeadLettersCopy.OpenTicket</a>
                                    }
                                    else
                                    {
                                        @DeadLettersCopy.NoTicket
                                    }
                                </td>
                                <td>@row.Attempts</td>
                                <td class="ts-last-error">@row.LastError</td>
                                <td><RelativeTime When="row.CreatedAt" /></td>
                                <td class="ts-settings-actions">
                                    <button type="button" class="btn btn-outline-secondary ts-retry" disabled="@_busy" @onclick="() => RetryAsync(row)">@DeadLettersCopy.Retry</button>
                                    <button type="button" class="btn btn-outline-secondary ts-discard" disabled="@_busy" @onclick="() => AskDiscard(row)">@DeadLettersCopy.Discard</button>
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
            <PagerControl Page="_page" PageSize="DeadLettersCopy.PageSize" TotalCount="_total" OnPageChanged="OnPageChanged" />
        }

        <ConfirmDialog Open="@(_discarding is not null)" Title="@DeadLettersCopy.DiscardTitle" ConfirmLabel="@DeadLettersCopy.DiscardConfirm" Danger="true"
                       Busy="_busy" ConfirmDisabled="_uncertain" Error="@_dialogError" OnConfirm="ConfirmDiscardAsync" OnCancel="CancelDiscard">
            @if (_discarding is not null)
            {
                <p>@DeadLettersCopy.DiscardSubject(_discarding.Kind, _discarding.Recipient)</p>
                <p>@DeadLettersCopy.DiscardBody</p>
                @if (_uncertain)
                {
                    <div class="ts-dialog-recovery">
                        <button type="button" class="btn btn-link" @onclick="ReloadAfterUncertainAsync">@DeadLettersCopy.ReloadList</button>
                    </div>
                }
            }
        </ConfirmDialog>
    </div>
```

`src/TechStrap.Admin/Features/Ops/DeadLetters/DeadLettersContent.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;

namespace TechStrap.Admin.Features.Ops.DeadLetters;

/// <summary>
/// The failed emails (Admin only): each row shows the masked recipient, the kind of email, a link to its ticket, the tries, the last error as a plain category and when it was created. Retry needs no confirmation
/// (it only puts the email back in the queue); Discard is a medium-tier confirmation. After either the list is read again and the navigation badge follows the total, so a retry that fails again shows up with its
/// new error. Writes use <see cref="CancellationToken.None"/> and never retry; an unknown outcome says so and offers a reload. One action runs at a time.
/// </summary>
public sealed partial class DeadLettersContent : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<DeadLetterRowViewModel> _rows = [];
    private DeadLetterRowViewModel? _discarding;
    private string? _error;
    private string? _rowError;
    private string? _dialogError;
    private int _page = 1;
    private int _total;
    private int _loadedPage;
    private bool _loading = true;
    private bool _busy;
    private bool _uncertain;
    private bool _rowUncertain;
    private bool _disposed;

    [Inject]
    private IDeadLettersClient DeadLetters { get; set; } = default!;

    [Inject]
    private FailedEmailCounter Failed { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "page")]
    public string? PageNumber { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        _page = int.TryParse(PageNumber, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 1;
        if (_loadedPage != _page)
        {
            _loadedPage = _page;
            await LoadAsync();
        }
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        try
        {
            var result = await DeadLetters.ListAsync(_page, DeadLettersCopy.PageSize, _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. result.Value.Items.Select(DeadLetterRowViewModel.From)];
                _total = result.Value.TotalCount;

                // The list already holds the total, so the badge in the navigation follows it without a second call.
                Failed.Set(_total);
            }
            else
            {
                _error = $"{DeadLettersCopy.LoadFailed} {result.Errors[0].Message}";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task ReloadAsync()
    {
        _rowError = null;
        _rowUncertain = false;
        await LoadAsync();
    }

    private void OnPageChanged(int page) => Navigation.NavigateTo(page <= 1 ? "/ops/dead-letters" : $"/ops/dead-letters?page={page}");

    // ---- retry: no confirmation, once ----------------------------------------------------------------------------

    private async Task RetryAsync(DeadLetterRowViewModel row)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _rowError = null;
        _rowUncertain = false;
        try
        {
            var result = await DeadLetters.RetryAsync(row.Id, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                StatusMessages.Show(DeadLettersCopy.Retried(row.Kind));
                await RefreshAfterWriteAsync();
            }
            else
            {
                await ShowRowFailureAsync(result.Errors[0], DeadLettersCopy.RetryFailed, DeadLettersCopy.RetryUncertain);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- discard: a confirmation ---------------------------------------------------------------------------------

    private void AskDiscard(DeadLetterRowViewModel row)
    {
        _dialogError = null;
        _uncertain = false;
        _rowError = null;
        _discarding = row;
    }

    private void CancelDiscard()
    {
        if (!_busy)
        {
            _discarding = null;
            _dialogError = null;
            _uncertain = false;
        }
    }

    private async Task ConfirmDiscardAsync()
    {
        if (_busy || _discarding is not { } row)
        {
            return;
        }

        _busy = true;
        _dialogError = null;
        _uncertain = false;
        StateHasChanged();
        try
        {
            var result = await DeadLetters.DiscardAsync(row.Id, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _discarding = null;
                StatusMessages.Show(DeadLettersCopy.Discarded(row.Kind));
                await RefreshAfterWriteAsync();
                return;
            }

            await ShowDiscardFailureAsync(result.Errors[0]);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task ShowDiscardFailureAsync(ResultError error)
    {
        if (error.Code is ApiErrorCodes.OutboxNotFound or ApiErrorCodes.OutboxNotDeadLettered)
        {
            _discarding = null;
            StatusMessages.Show(error.Code == ApiErrorCodes.OutboxNotFound ? DeadLettersCopy.Gone : DeadLettersCopy.AlreadyHandled);
            await RefreshAfterWriteAsync();
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _dialogError = DeadLettersCopy.DiscardUncertain;
            _uncertain = true;
        }
        else
        {
            _dialogError = DeadLettersCopy.DiscardFailed(error.Message);
        }
    }

    private async Task ShowRowFailureAsync(ResultError error, Func<string, string> failed, string uncertain)
    {
        if (error.Code is ApiErrorCodes.OutboxNotFound or ApiErrorCodes.OutboxNotDeadLettered)
        {
            StatusMessages.Show(error.Code == ApiErrorCodes.OutboxNotFound ? DeadLettersCopy.Gone : DeadLettersCopy.AlreadyHandled);
            await RefreshAfterWriteAsync();
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _rowError = uncertain;
            _rowUncertain = true;
        }
        else
        {
            _rowError = failed(error.Message);
        }
    }

    private async Task RefreshAfterWriteAsync()
    {
        await LoadAsync();
    }

    private async Task ReloadAfterUncertainAsync()
    {
        _discarding = null;
        _dialogError = null;
        _uncertain = false;
        await ReloadAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

`src/TechStrap.Admin/Styles/_ops.scss`

```scss
// Failed emails (PHASE-07b): the last error is short text from the API and wraps like any other outside text.

.ts-last-error {
  min-width: 0;
  overflow-wrap: anywhere;
}
```

```diff
@@
 @import "audit";
+@import "ops";
 @import "styleguide";
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter "DeadLettersPageTests|AdminSettingsHostTests|SourceEncodingTests|SourceEscapeTests"` and `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter AdminLeakTests`
Expected: PASS. To see them guard (do not commit), make each change, rerun, expect the failure named, and restore:
- `DeadLettersContent.AskDiscard`: add `_ = ConfirmDiscardAsync();` after `_discarding = row;`: most of the discard tests fail (the dialog never asks).
- `TagsContent.OnInitializedAsync` (Task 8): load for everyone (remove nothing from the shell, make the content load, then take `<AdminOnly>` out of `TagsPage.razor`): `A_plain_agent_who_opens_an_admin_page_by_address_...` fails for `/settings/tags` with "a plain agent's page load must not call an endpoint the API would refuse".
- In the Admin client for `CreateApiKeyAsync` (Task 3) add `logger.LogInformation("created {Key}", response.PlaintextKey)` (inject an `ILogger`): the leak test fails on the log scan. Restore.

- [ ] **Step 5: Write the closing docs**

The docs are applied by this script. Save it as `docs07b.py` outside the repository (it is not committed; the commit lists only the four docs), run it once from the repository root, and it reads and writes each file with its own line endings.

```python
"""PHASE-07b closing docs (Task 9). Run from the repository root: python docs07b.py (no sed).

Every replacement asserts that the text it replaces is there exactly once, so a drifted document fails loudly instead of being silently skipped.
"""
import io
import re
import sys

ROOT = sys.argv[1] if len(sys.argv) > 1 else '.'


LF = chr(10)
CRLF = chr(13) + chr(10)
_CRLF = {}


def read(path):
    # The files may have CRLF line endings: work on LF and write the original endings back.
    with io.open(f'{ROOT}/{path}', encoding='utf-8', newline='') as handle:
        text = handle.read()
    _CRLF[path] = CRLF in text
    return text.replace(CRLF, LF)


def write(path, text):
    if _CRLF.get(path):
        text = text.replace(LF, CRLF)
    with io.open(f'{ROOT}/{path}', 'w', encoding='utf-8', newline='') as handle:
        handle.write(text)


def replace_once(text, old, new, label):
    count = text.count(old)
    assert count == 1, f'{label}: expected exactly one match, found {count}'
    return text.replace(old, new)


# ---------------------------------------------------------------------------------------------------------------------
# docs/development/ADMIN-APP.md
# ---------------------------------------------------------------------------------------------------------------------
path = 'docs/development/ADMIN-APP.md'
doc = read(path)

doc = replace_once(
    doc,
    '''PHASE-07 is delivered in three pull requests. **07a (this page)**: sign-in, the shell, the queue, ticket detail, the reply composer, the sidebar, spam, delete and
erase. **07b**: settings (products, API keys, agents, tags, My settings, public display name), dead letters and the audit log. **07c**: polish, the command
palette, compose and architecture rules, CSP.''',
    '''PHASE-07 is delivered in three pull requests. **07a**: sign-in, the shell, the queue, ticket detail, the reply composer, the sidebar, spam, delete and
erase. **07b**: settings (products and their branding, API keys, agents, tags, My settings with the public display name), failed emails and the audit log.
**07c**: polish, the command palette, compose and architecture rules, CSP. This page describes the app as it is once 07b is merged; the 07b parts say so.''',
    'intro',
)

doc = replace_once(
    doc,
    '''3. Delete and erase (and, in 07b, settings, dead letters and the audit log) show **only to an Admin** (`AgentDto.Role`). The API enforces the same rule, so hiding is a courtesy.''',
    '''3. Delete and erase, and the 07b pages (products, agents, tags, the audit log, failed emails), are **for an Admin only** (`AgentDto.Role`). A plain agent who types the address of one gets the
   page-level no-access page ("You don't have access to this page.") and the page makes no API call; the navigation shows them no admin links. While the session is still loading the page shows
   "Checking your access...", never the page and never a refusal. The API enforces the same rule (403 `admin-access-required`), so hiding is a courtesy. My settings is for every agent.''',
    'sign-in item 3',
)

doc = replace_once(
    doc,
    '''| Delete a ticket, erase a requester | no (not rendered) | yes, with a typed confirmation |
''',
    '''| Delete a ticket, erase a requester | no (not rendered) | yes, with a typed confirmation |
| My settings (email alerts, keyboard shortcuts, theme, public display name) | yes | yes |
| Products, branding, API keys | no (page-level no-access) | yes |
| Agents: see the list, activate, deactivate | no | yes |
| Tags: create, rename, recolour, delete | no | yes |
| Audit log, failed emails (retry, discard) | no | yes |

**Roles are read-only here (D-041).** The agents page shows each role as a badge, with the note "Roles come from your identity provider's groups." An admin can only activate or deactivate an
agent. To make someone an admin, or an agent, change their group in the identity provider; the API reads it at their next sign-in.
''',
    'roles table',
)

doc = replace_once(
    doc,
    '''  Clients/        ApiConnection and the typed clients (IAgentsClient, IProductsClient, ITagsClient, ITicketsClient, IRequestersClient), the /attachments/{id} pass-through''',
    '''  Clients/        ApiConnection and the typed clients (IAgentsClient, IProductsClient, ITagsClient, ITicketsClient, IRequestersClient, IAdminEventsClient, IDeadLettersClient), the /attachments/{id} pass-through''',
    'tree clients',
)

doc = replace_once(
    doc,
    '''    Ui/           reusable primitives: LoadingState, ErrorState, EmptyState, ConfirmDialog, PagerControl, TagChip, RelativeTime, StatusStamp, PriorityMark, ...''',
    '''    Ui/           reusable primitives: LoadingState, ErrorState, EmptyState, ConfirmDialog, PagerControl, TagChip, RelativeTime, StatusStamp, PriorityMark, AdminOnly (the admin page guard), AccentPreview, ...''',
    'tree ui',
)

doc = replace_once(
    doc,
    '''    Tickets/      TicketDetailPage, presenter, timeline factory, ReplyComposer, TicketSidebar, TagPicker, TicketActions
''',
    '''    Tickets/      TicketDetailPage, presenter, timeline factory, ReplyComposer, TicketSidebar, TagPicker, TicketActions
    Settings/     Admin only. Products/ (ProductsPage, ProductEditorPage, ProductKeysPage, ApiKeysPanel, NewApiKeyDialog, LogoUrlRule), Agents/ (AgentsPage), Tags/ (TagsPage),
                  Audit/ (AdminEventsPage, AdminEventSummaryFactory), EmailKinds
    Ops/          Admin only. DeadLetters/ (DeadLettersPage)
    Account/      My settings for every agent: NotificationPreferencesPage, PublicDisplayNameField, PublicNamePreview
''',
    'tree features',
)

doc = replace_once(
    doc,
    '''  wwwroot/js/     ES modules only: dialog.js, shortcuts.js, queue.js (no inline script anywhere)''',
    '''  wwwroot/js/     ES modules only: dialog.js, shortcuts.js, queue.js, preferences.js (browser preferences and the theme), clipboard.js (copy the new API key) (no inline script anywhere)''',
    'tree js',
)

doc = replace_once(
    doc,
    '''Writes (reply, note, sidebar changes, spam, delete, erase) deliberately pass `CancellationToken.None`''',
    '''Writes (reply, note, sidebar changes, spam, delete, erase, and every settings write) deliberately pass `CancellationToken.None`''',
    'rules writes',
)

doc = replace_once(
    doc,
    '''`?` lists them in the app. A My settings switch to turn them off arrives in 07b.''',
    '''`?` lists them in the app. My settings has a Keyboard shortcuts switch that turns them off; it is remembered in this browser.''',
    'keyboard',
)

doc = replace_once(
    doc,
    '''## Known limits
''',
    '''## What an admin can do here (07b)

Everything in this section needs the Admin role. The API refuses the same calls to anyone else.

- **Products** (`/settings/products`, `/settings/products/new`, `/settings/products/{id}`): a list of every product, active or not, and an editor for the name and the branding (display name, logo, accent colour,
  email from address and reply-to). The key and the ticket number prefix are permanent: they are asked for when the product is created and shown read-only afterwards. The logo is an **address, not an upload**:
  only a full `https://` address is accepted (`http://localhost` too, in Development), a blank value means no logo, and the API refuses the same addresses, so `javascript:`, `data:`, relative paths and other
  schemes never reach an email or the portal. The accent colour is checked with the same pattern as the API (`#RRGGBB`); a low-contrast colour only shows a note, because TechStrap darkens it wherever it is used
  for text. A live preview shows the name, the logo and the accent. Saving always sends the product's current Active setting and the version the editor was opened on: if someone else saved first you see "This
  product changed since you opened it", your edits stay on screen, and Reload shows the saved version. A failed or lost save never clears the form.
- **API keys** (`/settings/products/{id}/keys`): the keys of a product by label, kind (a Trusted or a Public badge, always the word), prefix, created and last-used time, and status. Creating one asks for the kind
  and an optional label, then shows the key **once**, in a dialog with a Copy button. The dialog cannot be closed (Cancel and Esc do nothing) until you tick "I have stored this key"; closing it clears the key from
  the page, and nothing can show it again. If the answer to a create is lost, the page says the key may exist but its secret cannot be shown: revoke it and create another. Revoking asks first ("Apps using this key
  will stop working.") and a revoked key stays in the list, marked Revoked.
- **Agents** (`/settings/agents`): everyone who has signed in, with role badge, active or not, and last seen, 25 to a page. Activate needs no confirmation; Deactivate asks first. Deactivating the only active admin
  is refused by the API, and the page shows its message inside the dialog. Deactivating yourself warns you, and then shows the no-access page.
- **Tags** (`/settings/tags`): every tag with a **ticket count**, create (the slug follows the name until you edit it), rename and recolour in the row, and delete. Deleting an unused tag asks first. Deleting a tag
  that is in use shows its count ("12 tickets"), asks you to type the tag's name, and only then removes it from every ticket and deletes it.
- **Audit log** (`/settings/audit`): who changed what, newest first, 25 to a page. Filter by what changed (product, API key, agent, tag, requester, ticket, email) and by who; both stay in the address so a view can
  be linked. Every event is one sentence built from the ids, slugs, prefixes and counts in its payload. The raw payload is never shown. The first page fixes a point in time, so events recorded while you read do
  not push rows onto the next page.
- **Failed emails** (`/ops/dead-letters`): emails that used up their retries, with the recipient masked, the kind, a link to the ticket, the tries and the last error as a plain category. Retry puts one back in the
  queue with no confirmation; Discard asks first. After either, the list and the count beside "Failed emails" in the navigation are read again. An empty list says "No failed emails".

Every agent, not only an admin, has **My settings** (`/account/notifications`):

- **Email alerts**: a switch per active product for "a new ticket arrived". Each change sends every product's setting, so nothing is left for the API to guess.
- **Keyboard shortcuts** and **theme** (Auto, Light or Dark): kept in this browser only, so they follow the browser, not the account.
- **Public display name**: optional, plain text, up to 60 characters, no `@`. A live line shows what customers will see ("Customers see: Sam from Orbitly Support"); clearing the field returns to the first name from
  your profile. It saves when you leave the field or press Enter, and customers never see your email address.

## Known limits
''',
    'admin section',
)

doc = replace_once(
    doc,
    '''bUnit notes that cost time once: JS interop runs in strict mode, so set up `./js/dialog.js` and `./js/shortcuts.js` (the `AdminComponentTest` base class does); the element reference of an element is blanked after the next''',
    '''bUnit notes that cost time once: JS interop runs in strict mode, so set up `./js/dialog.js`, `./js/shortcuts.js` and `./js/preferences.js` (the `AdminComponentTest` base class does; a test that copies an API key sets up
`./js/clipboard.js` itself); the settings pages derive from `AdminPageTest`, which gives them a session (an Admin unless the test calls `AsAgent()` before it renders), what `NoAccessPage` needs and a host environment;
the element reference of an element is blanked after the next''',
    'tests notes',
)

doc = replace_once(
    doc,
    '''| Keyboard shortcuts do nothing | Focus is in a field, a dialog is open, or `shortcuts.js` was blocked. Check the browser console. |
''',
    '''| Keyboard shortcuts do nothing | Focus is in a field, a dialog is open, My settings has Keyboard shortcuts switched off, or `shortcuts.js` was blocked. Check the browser console. |
| A settings page says "You don't have access to this page." | The signed-in agent is not an Admin. Roles come from the identity provider's groups, and the API decides. |
| The new API key dialog will not close | Tick "I have stored this key" first. The key is shown once and cannot be shown again; if you lost it, revoke the key and create another. |
| Copy does nothing in the new API key dialog | The browser refused the clipboard (a page that is not https, or blocked). The key is selected: press Ctrl+C. |
| A product's logo is refused | Only a full https:// address is accepted (and http://localhost in Development). There is no upload. |
| The theme does not change | The choice is kept in this browser: private windows and cleared site data forget it, and Auto follows the device. |
''',
    'troubleshooting',
)

doc = doc.rstrip('\n') + '''

## Known gaps in 07b

Recorded in D-041 and tracked for later phases:

- Roles cannot be changed here (use the identity provider's groups), and there is no invite: an agent appears by signing in.
- The logo is an address, not an upload. The preview loads it from the address you typed, so a slow host shows a slow preview.
- The editors do not warn about unsaved changes when you leave the page; a conflict banner keeps your edits and offers Reload, but "Apply my change again" is not built (as in 07a).
- Ticket counts on the tags page, and the count in the delete dialog, are read when the list loads. A tag that gains tickets meanwhile is caught by the API (409 `tag-in-use`) and shown again with its new count.
- The audit log filters by what changed and by who only: the API has no event-type or date filter. Events show ids, slugs, prefixes and counts and never names, because a payload carries none.
- My settings has the new-ticket alerts only: the UX brief's assignment-alert switch has no API field yet.
- Discard on a failed email takes no reason (the API has none). The failed-emails count in the navigation is read when the shell loads and after you act on the page; it is not live (PHASE-10).
- Times are shown in UTC, as in 07a.
- The OpenAPI bearer scheme, the CSP and the responsive and accessibility pass remain 07c.
'''
write(path, doc)

# ---------------------------------------------------------------------------------------------------------------------
# docs/architecture/PHASE-07-admin-app.md : tick T14, T15, T16, T17, T18, T23 and the two carry-forwards
# ---------------------------------------------------------------------------------------------------------------------
path = 'docs/architecture/PHASE-07-admin-app.md'
text = read(path)
lines = text.split('\n')

EVIDENCE = {
    'P07-T14': '`ProductEditorTests`, `ProductEditorRealApiTests`, `AccentPreviewTests`, `ProductsPageTests`, `AdminSettingsHostTests`',
    'P07-T15': '`NewApiKeyDialogTests`, `ApiKeysPanelTests`, `RevokeApiKeyTests`, `ProductKeysPageTests`, `AdminLeakTests`',
    'P07-T16': '`AgentsPageTests`, `NotificationPreferencesPageTests`, `AdminOnlyTests`, `AdminSettingsHostTests`',
    'P07-T17': '`TagsPageTests`, `DeleteTagTests`, `AdminEventSummaryFactoryTests`, `AdminEventsPageTests`',
    'P07-T18': '`DeadLettersPageTests`, `AdminSettingsHostTests`',
    'P07-T23': '`PublicDisplayNameFieldTests`, `PublicNamePreviewTests`, `NotificationPreferencesPageTests`',
}

out = []
i = 0
while i < len(lines):
    line = lines[i]
    match = re.match(r'^- \[ \] \*\*(P07-T\d+)\*\* \[07b\]', line)
    if match and match.group(1) in EVIDENCE:
        task = match.group(1)
        out.append(line.replace('- [ ]', '- [x]', 1))
        i += 1
        while i < len(lines) and lines[i].startswith('  '):
            out.append(lines[i])
            i += 1
        out.append(f'  - **07b evidence:** {EVIDENCE[task]}')
        EVIDENCE.pop(task)
        continue
    if line.startswith('- [ ] Carried forward from the PHASE-04 final review:') and ('`tag-in-use` count' in line or 'admin agent-list fields' in line):
        line = line.replace('- [ ]', '- [x]', 1).rstrip() + ' (done in PHASE-07b, D-041)'
    out.append(line)
    i += 1

assert not EVIDENCE, f'tasks not found as open [07b] tasks: {sorted(EVIDENCE)}'
ticked = '\n'.join(out)
assert ticked.count('(done in PHASE-07b, D-041)') == 2, 'both carry-forwards must be ticked'
write(path, ticked)

# ---------------------------------------------------------------------------------------------------------------------
# roadmap and index
# ---------------------------------------------------------------------------------------------------------------------
for path in ('docs/architecture/99-IMPLEMENTATION-ROADMAP.md', 'docs/architecture/00-DISCOVERY-INDEX.md'):
    text = read(path)
    text = replace_once(text, '07a implemented (pending merge); 07b and 07c not started', '07a merged; 07b implemented (pending merge); 07c not started', path)
    write(path, text)

print('docs updated')
```

Run: `python docs07b.py .` from the repository root.
Expected: `docs updated`. Then:
- `git diff --stat docs/` shows the four files changed.
- `grep -c "07b evidence" docs/architecture/PHASE-07-admin-app.md` prints `6`.
- `grep -n "(done in PHASE-07b" docs/architecture/PHASE-07-admin-app.md` prints the two carry-forward lines.
- `grep -n "07a merged; 07b implemented" docs/architecture/99-IMPLEMENTATION-ROADMAP.md docs/architecture/00-DISCOVERY-INDEX.md` prints one line each.

The edit the script makes to `docs/development/ADMIN-APP.md`, in full, as a diff (the new "What an admin can do here (07b)" section, the role rows and note, the tree lines, the keyboard sentence, the troubleshooting rows, the test notes and the "Known gaps in 07b" section):

```diff
--- a/docs/development/ADMIN-APP.md
+++ b/docs/development/ADMIN-APP.md
@@ -6,5 +6,5 @@
 
-PHASE-07 is delivered in three pull requests. **07a (this page)**: sign-in, the shell, the queue, ticket detail, the reply composer, the sidebar, spam, delete and
-erase. **07b**: settings (products, API keys, agents, tags, My settings, public display name), dead letters and the audit log. **07c**: polish, the command
-palette, compose and architecture rules, CSP.
+PHASE-07 is delivered in three pull requests. **07a**: sign-in, the shell, the queue, ticket detail, the reply composer, the sidebar, spam, delete and
+erase. **07b**: settings (products and their branding, API keys, agents, tags, My settings with the public display name), failed emails and the audit log.
+**07c**: polish, the command palette, compose and architecture rules, CSP. This page describes the app as it is once 07b is merged; the 07b parts say so.
 
@@ -61,3 +61,5 @@
    - the API could not be reached: an alert with Retry, and still no page content.
-3. Delete and erase (and, in 07b, settings, dead letters and the audit log) show **only to an Admin** (`AgentDto.Role`). The API enforces the same rule, so hiding is a courtesy.
+3. Delete and erase, and the 07b pages (products, agents, tags, the audit log, failed emails), are **for an Admin only** (`AgentDto.Role`). A plain agent who types the address of one gets the
+   page-level no-access page ("You don't have access to this page.") and the page makes no API call; the navigation shows them no admin links. While the session is still loading the page shows
+   "Checking your access...", never the page and never a refusal. The API enforces the same rule (403 `admin-access-required`), so hiding is a courtesy. My settings is for every agent.
 4. Sign out is a POST to `/signout` with an antiforgery token; it ends the cookie session and the provider session.
@@ -75,2 +77,10 @@
 | Delete a ticket, erase a requester | no (not rendered) | yes, with a typed confirmation |
+| My settings (email alerts, keyboard shortcuts, theme, public display name) | yes | yes |
+| Products, branding, API keys | no (page-level no-access) | yes |
+| Agents: see the list, activate, deactivate | no | yes |
+| Tags: create, rename, recolour, delete | no | yes |
+| Audit log, failed emails (retry, discard) | no | yes |
+
+**Roles are read-only here (D-041).** The agents page shows each role as a badge, with the note "Roles come from your identity provider's groups." An admin can only activate or deactivate an
+agent. To make someone an admin, or an agent, change their group in the identity provider; the API reads it at their next sign-in.
 
@@ -106,5 +116,5 @@
   Auth/           cookie + OIDC wiring, /signin and /signout endpoints, AgentSession (the API's answer to /me)
-  Clients/        ApiConnection and the typed clients (IAgentsClient, IProductsClient, ITagsClient, ITicketsClient, IRequestersClient), the /attachments/{id} pass-through
+  Clients/        ApiConnection and the typed clients (IAgentsClient, IProductsClient, ITagsClient, ITicketsClient, IRequestersClient, IAdminEventsClient, IDeadLettersClient), the /attachments/{id} pass-through
   Components/
-    Ui/           reusable primitives: LoadingState, ErrorState, EmptyState, ConfirmDialog, PagerControl, TagChip, RelativeTime, StatusStamp, PriorityMark, ...
+    Ui/           reusable primitives: LoadingState, ErrorState, EmptyState, ConfirmDialog, PagerControl, TagChip, RelativeTime, StatusStamp, PriorityMark, AdminOnly (the admin page guard), AccentPreview, ...
     Layout/       MainLayout, NavMenu, StatusBar, AgentGate, ShortcutHelpDialog
@@ -115,3 +125,7 @@
     Tickets/      TicketDetailPage, presenter, timeline factory, ReplyComposer, TicketSidebar, TagPicker, TicketActions
-  wwwroot/js/     ES modules only: dialog.js, shortcuts.js, queue.js (no inline script anywhere)
+    Settings/     Admin only. Products/ (ProductsPage, ProductEditorPage, ProductKeysPage, ApiKeysPanel, NewApiKeyDialog, LogoUrlRule), Agents/ (AgentsPage), Tags/ (TagsPage),
+                  Audit/ (AdminEventsPage, AdminEventSummaryFactory), EmailKinds
+    Ops/          Admin only. DeadLetters/ (DeadLettersPage)
+    Account/      My settings for every agent: NotificationPreferencesPage, PublicDisplayNameField, PublicNamePreview
+  wwwroot/js/     ES modules only: dialog.js, shortcuts.js, queue.js, preferences.js (browser preferences and the theme), clipboard.js (copy the new API key) (no inline script anywhere)
   Styles/         SCSS partials over the brand tokens (docs/BRAND.md)
@@ -123,3 +137,3 @@
 - **Components never inject `HttpClient`.** They inject the `I*Client` interfaces, which return `Result<T>`; the clients map the API's ProblemDetails (the error `type` is the code).
-- Every read takes the component's `CancellationToken`. Writes (reply, note, sidebar changes, spam, delete, erase) deliberately pass `CancellationToken.None`: the server may commit a write after the agent has left the screen, so cancelling the call would only hide the outcome.
+- Every read takes the component's `CancellationToken`. Writes (reply, note, sidebar changes, spam, delete, erase, and every settings write) deliberately pass `CancellationToken.None`: the server may commit a write after the agent has left the screen, so cancelling the call would only hide the outcome.
 - Razor components are always public classes, so a type used as a component parameter is public (a view model cannot be `internal`).
@@ -140,3 +154,3 @@
 
-Single-key shortcuts are off while you type in a text field, select or editable area and while a dialog is open. `?` lists them in the app. A My settings switch to turn them off arrives in 07b.
+Single-key shortcuts are off while you type in a text field, select or editable area and while a dialog is open. `?` lists them in the app. My settings has a Keyboard shortcuts switch that turns them off; it is remembered in this browser.
 The command palette (Ctrl+K) arrives in 07c.
@@ -166,2 +180,33 @@
 
+## What an admin can do here (07b)
+
+Everything in this section needs the Admin role. The API refuses the same calls to anyone else.
+
+- **Products** (`/settings/products`, `/settings/products/new`, `/settings/products/{id}`): a list of every product, active or not, and an editor for the name and the branding (display name, logo, accent colour,
+  email from address and reply-to). The key and the ticket number prefix are permanent: they are asked for when the product is created and shown read-only afterwards. The logo is an **address, not an upload**:
+  only a full `https://` address is accepted (`http://localhost` too, in Development), a blank value means no logo, and the API refuses the same addresses, so `javascript:`, `data:`, relative paths and other
+  schemes never reach an email or the portal. The accent colour is checked with the same pattern as the API (`#RRGGBB`); a low-contrast colour only shows a note, because TechStrap darkens it wherever it is used
+  for text. A live preview shows the name, the logo and the accent. Saving always sends the product's current Active setting and the version the editor was opened on: if someone else saved first you see "This
+  product changed since you opened it", your edits stay on screen, and Reload shows the saved version. A failed or lost save never clears the form.
+- **API keys** (`/settings/products/{id}/keys`): the keys of a product by label, kind (a Trusted or a Public badge, always the word), prefix, created and last-used time, and status. Creating one asks for the kind
+  and an optional label, then shows the key **once**, in a dialog with a Copy button. The dialog cannot be closed (Cancel and Esc do nothing) until you tick "I have stored this key"; closing it clears the key from
+  the page, and nothing can show it again. If the answer to a create is lost, the page says the key may exist but its secret cannot be shown: revoke it and create another. Revoking asks first ("Apps using this key
+  will stop working.") and a revoked key stays in the list, marked Revoked.
+- **Agents** (`/settings/agents`): everyone who has signed in, with role badge, active or not, and last seen, 25 to a page. Activate needs no confirmation; Deactivate asks first. Deactivating the only active admin
+  is refused by the API, and the page shows its message inside the dialog. Deactivating yourself warns you, and then shows the no-access page.
+- **Tags** (`/settings/tags`): every tag with a **ticket count**, create (the slug follows the name until you edit it), rename and recolour in the row, and delete. Deleting an unused tag asks first. Deleting a tag
+  that is in use shows its count ("12 tickets"), asks you to type the tag's name, and only then removes it from every ticket and deletes it.
+- **Audit log** (`/settings/audit`): who changed what, newest first, 25 to a page. Filter by what changed (product, API key, agent, tag, requester, ticket, email) and by who; both stay in the address so a view can
+  be linked. Every event is one sentence built from the ids, slugs, prefixes and counts in its payload. The raw payload is never shown. The first page fixes a point in time, so events recorded while you read do
+  not push rows onto the next page.
+- **Failed emails** (`/ops/dead-letters`): emails that used up their retries, with the recipient masked, the kind, a link to the ticket, the tries and the last error as a plain category. Retry puts one back in the
+  queue with no confirmation; Discard asks first. After either, the list and the count beside "Failed emails" in the navigation are read again. An empty list says "No failed emails".
+
+Every agent, not only an admin, has **My settings** (`/account/notifications`):
+
+- **Email alerts**: a switch per active product for "a new ticket arrived". Each change sends every product's setting, so nothing is left for the API to guess.
+- **Keyboard shortcuts** and **theme** (Auto, Light or Dark): kept in this browser only, so they follow the browser, not the account.
+- **Public display name**: optional, plain text, up to 60 characters, no `@`. A live line shows what customers will see ("Customers see: Sam from Orbitly Support"); clearing the field returns to the first name from
+  your profile. It saves when you leave the field or press Enter, and customers never see your email address.
+
 ## Known limits
@@ -184,3 +229,8 @@
 | Attachments open as a page instead of downloading | Report it: the pass-through must force a download. |
-| Keyboard shortcuts do nothing | Focus is in a field, a dialog is open, or `shortcuts.js` was blocked. Check the browser console. |
+| Keyboard shortcuts do nothing | Focus is in a field, a dialog is open, My settings has Keyboard shortcuts switched off, or `shortcuts.js` was blocked. Check the browser console. |
+| A settings page says "You don't have access to this page." | The signed-in agent is not an Admin. Roles come from the identity provider's groups, and the API decides. |
+| The new API key dialog will not close | Tick "I have stored this key" first. The key is shown once and cannot be shown again; if you lost it, revoke the key and create another. |
+| Copy does nothing in the new API key dialog | The browser refused the clipboard (a page that is not https, or blocked). The key is selected: press Ctrl+C. |
+| A product's logo is refused | Only a full https:// address is accepted (and http://localhost in Development). There is no upload. |
+| The theme does not change | The choice is kept in this browser: private windows and cleared site data forget it, and Auto follows the device. |
 
@@ -193,3 +243,5 @@
 
-bUnit notes that cost time once: JS interop runs in strict mode, so set up `./js/dialog.js` and `./js/shortcuts.js` (the `AdminComponentTest` base class does); the element reference of an element is blanked after the next
+bUnit notes that cost time once: JS interop runs in strict mode, so set up `./js/dialog.js`, `./js/shortcuts.js` and `./js/preferences.js` (the `AdminComponentTest` base class does; a test that copies an API key sets up
+`./js/clipboard.js` itself); the settings pages derive from `AdminPageTest`, which gives them a session (an Admin unless the test calls `AsAgent()` before it renders), what `NoAccessPage` needs and a host environment;
+the element reference of an element is blanked after the next
 render, so read it before the action; services cannot be added after the first render; `InputFile` has no `MaxAllowedSize` and bUnit does not enforce stream limits, so the composer checks files itself.
@@ -201 +253,15 @@
 no knowledge-base article picker (PHASE-08); no presence or live updates (PHASE-10); no command palette (07c); the manual sign-in check against a real Authentik is outstanding.
+
+## Known gaps in 07b
+
+Recorded in D-041 and tracked for later phases:
+
+- Roles cannot be changed here (use the identity provider's groups), and there is no invite: an agent appears by signing in.
+- The logo is an address, not an upload. The preview loads it from the address you typed, so a slow host shows a slow preview.
+- The editors do not warn about unsaved changes when you leave the page; a conflict banner keeps your edits and offers Reload, but "Apply my change again" is not built (as in 07a).
+- Ticket counts on the tags page, and the count in the delete dialog, are read when the list loads. A tag that gains tickets meanwhile is caught by the API (409 `tag-in-use`) and shown again with its new count.
+- The audit log filters by what changed and by who only: the API has no event-type or date filter. Events show ids, slugs, prefixes and counts and never names, because a payload carries none.
+- My settings has the new-ticket alerts only: the UX brief's assignment-alert switch has no API field yet.
+- Discard on a failed email takes no reason (the API has none). The failed-emails count in the navigation is read when the shell loads and after you act on the page; it is not live (PHASE-10).
+- Times are shown in UTC, as in 07a.
+- The OpenAPI bearer scheme, the CSP and the responsive and accessibility pass remain 07c.
```

The PHASE-07 edits are, for each of T14, T15, T16, T17, T18 and T23: the line `- [ ] **P07-Txx** [07b] ...` becomes `- [x] ...` and a last sub-bullet `  - **07b evidence:** ...` is added (the test classes are listed in the `EVIDENCE` table of the script; whatever Task 1 did to the wording of T16 and T23 does not matter, the script keys on the task id), and the two open carry-forwards (the `tag-in-use` count and the agent-list endpoint test) are ticked with `(done in PHASE-07b, D-041)`. The roadmap row and the index row change their status cell from `07a implemented (pending merge); 07b and 07c not started` to `07a merged; 07b implemented (pending merge); 07c not started`.

- [ ] **Step 6: Final verification**

Run, in order:
- `dotnet build TechStrap.slnx -c Release` (0 warnings)
- `dotnet test --solution TechStrap.CI.slnf -c Release`
- `pwsh -File scripts/Invoke-ScriptTests.ps1`
- the EF pending-model check (no migration is expected in PHASE-07b)
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Admin/Features/Ops \
  src/TechStrap.Admin/Styles \
  tests/TechStrap.Admin.Tests \
  tests/TechStrap.Api.Tests/AdminLeakTests.cs \
  docs/development/ADMIN-APP.md \
  docs/architecture/PHASE-07-admin-app.md \
  docs/architecture/99-IMPLEMENTATION-ROADMAP.md \
  docs/architecture/00-DISCOVERY-INDEX.md
git diff --cached --stat
git commit -m "feat(admin): failed emails page, host and leak tests, closing docs (P07-T18)" -m "Retry needs no confirmation and discard asks first; the rail badge follows the list total. Host tests prove a plain agent makes no admin call, and the leak tests prove the API key secret and the token reach no page, URL or log." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

