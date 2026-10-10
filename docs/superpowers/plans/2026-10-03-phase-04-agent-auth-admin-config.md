# PHASE-04 Agent Auth and Admin Config API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agents sign in with an OIDC JWT gated by IdP groups and are provisioned on their first `GET /api/agents/me` call. Admins manage agents (activate or deactivate), products and branding, trusted and public API keys, and tags through the API. Every change is audited in `AdminEvent`, and every expected failure is mapped to ProblemDetails.

**Architecture:** The Api uses JWT bearer authentication (`SyntaxCircus.AspNetCore.Authentication`) and two group-based policies, `Agent` and `Admin`. Both policies also refuse an agent whose stored row is deactivated. Each endpoint is a thin controller action that injects one named `I…RequestHandler` with `[FromServices]` and maps its `Result` with `ToActionResult`. Handlers live in `TechStrap.Application`. They accept and return `TechStrap.Contracts` DTOs (D-016), read identity through an Application-owned `ICurrentAgentClaims`, and write through the PHASE-03 repositories and `IUnitOfWork`, recording an `AdminEvent` in the same transaction. Roles come only from IdP groups (D-029).

**Tech Stack:** .NET 10, ASP.NET Core controllers, `SyntaxCircus.AspNetCore.Authentication` 0.1.5 (JWT bearer), `SyntaxCircus.AspNetCore.Common` 0.1.15 (`AddResultProblemDetails`, `ToActionResult`), `SyntaxCircus.Common` 0.1.3 (`Result`, `ResultError`, `PagedResult<T>`), EF Core and Npgsql 10 (PHASE-03 repositories), xUnit v3, Shouldly, NSubstitute, Testcontainers, `Microsoft.AspNetCore.Mvc.Testing`, and bUnit for the Admin and Portal error pages.

**Spec:** `docs/architecture/PHASE-04-agent-auth-and-admin-config.md`, read together with the owner decisions taken on 2026-10-03 (recorded in Task 1 as D-029, D-030 and D-031). Supporting docs: `docs/architecture/02-ARCHITECTURE.md` (sections 3.1, 4 and 7.1), `docs/architecture/04-DECISION-LOG.md` (D-001, D-004, D-006, D-016, D-022, D-024, D-026, D-028), `docs/BRAND.md` section 3, and `_template/docs/APPLICATION_ARCHITECTURE.md` (summarized in Global Constraints).

### Owner decisions this plan implements (2026-10-03)

The plan deviates from the PHASE-04 doc wherever these decisions apply. Task 1 updates the doc.

- **D-029, roles come from IdP groups only.**
  - The `Agent` policy requires the agent group *or* the admin group. The `Admin` policy requires the admin group.
  - The stored `Agent.Role` mirrors the claim at each `GET /api/agents/me`: Admin if the caller is in the admin group, otherwise Agent.
  - `TECHSTRAP_BOOTSTRAP_ADMIN` is **removed**. This amends D-004: the first admin is whoever is in the IdP admin group, so no database edit is needed.
  - `UpdateAgentRequest` carries only `IsActive`. The API cannot change roles. `active = false` always blocks access.
  - The PHASE-04 doc's bootstrap and `email_verified` requirements are dropped.
- **D-030, deleting a tag that tickets use.**
  - Without `force`, the delete is rejected with `409 tag-in-use` and the usage count.
  - With `?force=true`, the tag is removed from every ticket that carries it, each ticket recording a `TagRemoved` event, and then deleted.
  - Closed tickets are read-only, but they are detached through a dedicated Domain method that still records the event.
- **D-031, product accent validation is format only (`#RRGGBB`).** The PHASE-02 `ProductAccent` helper derives readable on-accent and ink colors for any accent, so the PHASE-04 doc's "400 low-contrast accent" case is dropped.

### Resolved inconsistencies

These are inconsistencies between the docs and the code. They are not new decisions, so they get no decision-log entries.

- **Policy names.** The policies are named `Agent` and `Admin`, as in 02-ARCHITECTURE section 4 and D-022. The PHASE-04 doc's `AgentPolicy` and `AdminPolicy` names are corrected in Task 1.
- **Validation status.** Validation failures map to **400** (`ValidationProblemDetails`), which is the `SyntaxCircus.AspNetCore.Common` default. The PHASE-04 doc's "400/422" is corrected to 400.
- **Identity abstraction.**
  - Identity comes from the Application-owned `ICurrentAgentClaims`, the "project-specific equivalent" that `_template` APPLICATION_ARCHITECTURE explicitly allows.
  - The package's `ICurrentUserService` has no groups, and Application must not read claims directly.
  - The Api implements `ICurrentAgentClaims` over `IHttpContextAccessor`.
- **Notification preferences.** `GET /api/agents/me/notification-preferences` (`GetMyNotificationPreferencesRequestHandler`) is added. Admin (PHASE-07) cannot show the per-product alert toggles without it. There are 18 handlers in total.

## Global Constraints

- **Build settings.** .NET SDK 10.0.401, `net10.0`. `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on, so any analyzer or style warning fails the build.
  - Private fields use `_camelCase`, including `private static readonly`.
  - Constants use PascalCase.
  - Namespaces are file-scoped.
- **Central package versions.**
  - Every package version lives in `Directory.Packages.props`, and every one must also appear in `docs/architecture/03-PACKAGE-MAP.md` with the same version (`scripts/Check-PackageVersions.ps1`).
  - A csproj never carries `Version` or `VersionOverride`.
  - The packages this phase adds to the Api are already pinned: `SyntaxCircus.AspNetCore.Authentication` 0.1.5 and `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.12.
- **Project references are fixed** (`ReferenceRules`):
  - Domain → nothing.
  - Contracts → nothing; it is a dependency-free leaf with no attributes (D-016).
  - Application → Domain and Contracts, plus only the `SyntaxCircus.Common` package.
  - Api → Application, Infrastructure and Contracts.
- **Handlers** (enforced by `HandlerShapeTests` and `HandlerConstructorDependencyTests`):
  - A handler is a `public sealed class XxxRequestHandler` in `TechStrap.Application` with a public `IXxxRequestHandler` interface.
  - The interface declares `HandleAsync(...)`, whose last parameter is `CancellationToken`.
  - A handler's constructor takes only Application interfaces, `TimeProvider`, or `Microsoft.Extensions.Options` types. It never takes `HttpContext`, `IHttpContextAccessor`, EF types, Infrastructure types or `*Record` types.
  - Nothing else in Application may have a name ending in `Handler`.
- **Controllers and HTTP** (`ControllerHandlerInjectionTests`, plus `ControllerBoundaryTests` from Task 13):
  - Controllers live in `TechStrap.Api/Controllers`. They take no constructor dependencies. Each action takes exactly one handler interface parameter marked `[FromServices]`, plus a `CancellationToken`.
  - An action never takes a repository, a `DbContext` or `IUnitOfWork`.
  - Results are mapped with `result.ToActionResult(this, onSuccess)`, and the success response is chosen explicitly (`Ok`, `CreatedAtAction`, `NoContent`).
- **Results.** Failures are `new ResultError(code, message, ResultErrorKind.X, target?)`. `target` is used only with `Validation`. Domain failures are converted with `DomainResultExtensions.ToError()` or `.ToResult()` (D-028). Error codes are kebab-case constants.
- **Contracts naming.** Every public, non-static type in `TechStrap.Contracts` ends in `Dto`, `Request` or `Response` (`ContractNamingTests`, Task 2). The only exemption is `TechStrap.Contracts.Branding.ProductAccentColors`. Roles and key kinds are strings defined as constants, not enums.
- **Persistence.**
  - `Update(x)` requires that `x` was loaded with a repository `Get…Async` in the same request scope. List results are untracked.
  - Writes run inside `await using var scope = await unitOfWork.BeginAsync(ct)` with exactly one `scope.CommitAsync(ct)`. A commit failure is a `Result` of kind Conflict with code `concurrency-conflict`, `duplicate` or `reference-violation` (`PersistenceErrorCodes`).
- **Audit (D-006, D-022).**
  - Every admin write records an `AdminEvent` in the same unit of work. Its actor is the acting agent's `Agent.Id`.
  - A payload never contains a plaintext key or hash, an email address, or properties named `name`, `email`, `key`, `body`, `subject` or `address`. `AdminEvent.Record` rejects these.
  - Use property names ending in `Id` and the names `productKey`, `keyPrefix`, `slug`, `kind`, `isActive`, `numberPrefix`, `changed` and `detachedTicketCount`.
- **API keys.**
  - Plaintext format: `tsk_` (Trusted) or `tsp_` (Public) followed by 43 base64url characters (32 random bytes, 256 bits).
  - The stored prefix is the first 12 characters of the plaintext.
  - The stored hash is `sha256:` followed by the lowercase hex SHA-256 of the UTF-8 plaintext.
  - Verification uses `CryptographicOperations.FixedTimeEquals`.
  - The plaintext appears only in `CreateProductApiKeyResponse`. It never appears in logs, audit payloads or list responses.
- **Customer-facing identity (D-024).** Agent emails never appear in customer-facing output. This phase's endpoints are all agent-facing, so emails may appear in Admin-only agent lists.
- **Copy (BRAND.md section 3).** Error text uses sentence case and an active voice, has no humor, no exclamation marks and no emoji, and states the plain cause plus the next step.
- **Commits.** Use Conventional Commits. Every commit message ends with exactly these two lines:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden actions.**
  - Never `git add -f`, and never commit `.superpowers/`.
  - Never hand-write or hand-edit EF migrations (this phase needs none).
  - Never commit a real secret.
  - Never run `docker compose down -v`.
- **Verification commands.**
  - `dotnet build TechStrap.slnx -c Release` (0 warnings).
  - `dotnet test --solution TechStrap.CI.slnf` (Docker required for the Testcontainers tests).
  - `pwsh -File scripts/Invoke-ScriptTests.ps1`.
  - `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api` (must stay clean).

## Review Focus

These are the failure modes most likely to bite a real admin or agent. No requirement in the spec forces a test for them, so each is pinned in the task that owns the code:

1. **Groups arrive as a single JSON-array string, or as a comma-separated or space-separated value**, as some IdPs and Authentik property mappings emit them, rather than as repeated `groups` claims. Group matching must still work, ignoring case. Pinned in Task 3 by `ClaimsCurrentAgentClaimsTests.Groups_are_read_from_repeated_claims_json_arrays_and_delimited_lists`.
2. **A token with no `email` claim** (the IdP does not release it) must give `403 agent-email-required` with a message that says what to fix, not a 500 or a 400 about a field the user cannot edit. Pinned in Task 5 by `GetCurrentAgentRequestHandlerTests.A_token_without_an_email_is_refused_with_a_clear_reason`.
3. **A deactivated agent with a still-valid token** gets 403 on *every* agent endpoint (`/api/products`, `/api/tags`, and the rest), not only on `/me`. Pinned in Task 5 by `AgentProvisioningTests.A_deactivated_agent_is_refused_on_every_agent_endpoint`. Task 13 widens the test to every route in `AgentAccessCoverageTests`.
4. **Two concurrent first sign-ins of the same person** produce one agent row, and both calls get 200. Pinned in Task 5 by `AgentProvisioningTests.Two_concurrent_first_calls_create_one_agent`.
5. **Force-deleting a tag that a Closed ticket carries** succeeds. The tag is detached with a `TagRemoved` event, and the Closed ticket stays Closed. A delete without `force` returns 409 with the count. Pinned in Task 11 by `DeleteTagIntegrationTests.Force_delete_detaches_open_and_closed_tickets_with_events`.

---

## File Structure

New and changed files, by responsibility. Tasks list each path again under **Files**.

- **Docs.** The decisions go in `docs/architecture/04-DECISION-LOG.md` (D-029 to D-031, plus an amendment to D-004). Also updated: `PHASE-04-agent-auth-and-admin-config.md`, `02-ARCHITECTURE.md` (sections 3.1, 4 and 7.1), `03-PACKAGE-MAP.md` (row 18), and the new `docs/self-hosting/AGENT-AUTHENTICATION.md`.
- **`src/TechStrap.Contracts`** (DTOs, one file per feature area):
  - `Paging/PagedResponse.cs`
  - `Agents/AgentDtos.cs` (`AgentDto`, `AgentListItemDto`, `UpdateAgentRequest`, `UpdateMyProfileRequest`, `NotificationPreferenceDto`, `UpdateNotificationPreferencesRequest`, `AgentRoles`, `AgentPublicName`)
  - `Products/ProductDtos.cs` (`ProductDto`, `ProductBrandingDto`, `ProductBrandingRequest`, `CreateProductRequest`, `UpdateProductRequest`)
  - `ApiKeys/ApiKeyDtos.cs` (`ProductApiKeyDto`, `CreateProductApiKeyRequest`, `CreateProductApiKeyResponse`, `ApiKeyKinds`)
  - `Tags/TagDtos.cs` (`TagDto`, `CreateTagRequest`, `UpdateTagRequest`)
  - `AdminEvents/AdminEventDtos.cs` (`AdminEventDto`)
- **`src/TechStrap.Application`:**
  - `Agents/ICurrentAgentClaims.cs` (the identity abstraction and the `AgentClaims` record)
  - `Agents/CurrentAgent.cs` (the internal helper that resolves the acting agent)
  - `Agents/AgentErrors.cs`
  - `Agents/*RequestHandler.cs` (6 handlers)
  - `Agents/AgentMapping.cs`
  - `Auditing/AdminAudit.cs` (builds the payload and records the event)
  - `Products/*RequestHandler.cs` (4) and `Products/ProductMapping.cs`
  - `ApiKeys/IApiKeyHasher.cs`, `ApiKeys/ApiKeyFormat.cs`, `ApiKeys/*RequestHandler.cs` (3)
  - `Tags/*RequestHandler.cs` (4) and `Tags/TagErrors.cs`
  - `AdminEvents/ListAdminEventsRequestHandler.cs`
- **`src/TechStrap.Application/Persistence`** (new repository members):
  - `IAgentRepository.CountActiveAdminsLockedAsync`
  - `IAgentRepository.GetByIdsAsync`
  - `ITicketRepository.ListTicketIdsWithTagAsync`
  - the actor filter on `IAdminEventRepository.ListAsync`
- **`src/TechStrap.Domain/Tickets/Ticket.cs`:** `DetachDeletedTag`.
- **`src/TechStrap.Infrastructure`:**
  - `Security/ApiKeyHasher.cs` and `Security/SecurityServiceCollectionExtensions.cs`
  - repository implementations for the new members
  - `Seeding/DevelopmentDataSeeder.cs`, which now generates real dev key hashes
  - `Seeding/DevelopmentApiKeys.cs`
- **`src/TechStrap.Api`:**
  - `Options/AgentAccessOptions.cs`
  - `Security/AuthorizationPolicies.cs`
  - `Security/AgentGroups.cs` (group-claim parsing)
  - `Security/ClaimsCurrentAgentClaims.cs`
  - `Security/AgentAccessRequirement.cs`
  - `Security/AgentAuthenticationSetup.cs`
  - `Startup/ApplicationHandlerRegistration.cs`
  - `Controllers/AgentsController.cs`, `ProductsController.cs`, `TagsController.cs`, `AdminEventsController.cs`
  - delete `Security/UnauthenticatedScheme.cs`
  - `Program.cs` wiring
  - `.env.example`
- **Admin and Portal:** `Components/Pages/Error.razor` (with `.razor.cs` only if it needs code) and the `Program.cs` exception handler.
- **Tests:**
  - `tests/TechStrap.Architecture.Tests`: `ContractNamingTests`, `ControllerBoundaryTests`, and an extended `HandlerConstructorDependencyTests`.
  - `tests/TechStrap.Application.Tests`: handler tests, one class per handler.
  - `tests/TechStrap.Api.Tests`:
    - the JWT helper `TestJwt`
    - `AgentAuthTests`
    - endpoint integration tests
    - `ResultMappingTests`, `CancellationPropagationTests` and `ControllerActionCoverage`
    - `OpenApiSurfaceTests`
    - `ClaimsCurrentAgentClaimsTests`
  - `tests/TechStrap.Infrastructure.IntegrationTests`: `ApiKeyHasherTests` and the new repository-member tests.
  - `tests/TechStrap.Domain.Tests`: `DetachDeletedTag` tests.
  - Admin and Portal: `UnhandledErrorHostTests`.

---

### Task 1: Record the owner decisions and align the docs

**Files:**
- Modify: `docs/architecture/04-DECISION-LOG.md`
  - approval-basis list
  - Index
  - D-004 status
  - new D-029, D-030, D-031 sections at the end
- Modify: `docs/architecture/PHASE-04-agent-auth-and-admin-config.md`
- Modify: `docs/architecture/02-ARCHITECTURE.md` (section 4 auth text, section 3.1 `ICurrentAgentClaims` row, section 7.1 PHASE-04 rows)
- Modify: `docs/architecture/03-PACKAGE-MAP.md` (the `SyntaxCircus.AspNetCore.Authentication` row: drop `TECHSTRAP_BOOTSTRAP_ADMIN`)

**Interfaces:** Produces the decision IDs D-029, D-030 and D-031 that later tasks cite in comments and tests.

- [ ] **Step 1: Add the approval-basis line and the index rows**

In `04-DECISION-LOG.md`, under "Approval basis:", add this bullet after the PHASE-03 planning bullet:

```markdown
- **Owner decision (2026-10-03, PHASE-04 planning):** D-029 (roles from IdP groups only; amends D-004), D-030 (deleting a tag in use), D-031 (product accent validation is format only).
```

Change the D-004 index row's status cell from `Approved (owner Q&A)` to `Approved (owner Q&A); amended by D-029`. After the D-028 row, append:

```markdown
| D-029 | Agent roles come from IdP groups only; no bootstrap admin | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-04, PHASE-07, 02-ARCHITECTURE, D-004 |
| D-030 | Deleting a tag in use: reject unless forced; forced delete detaches with events | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-04, PHASE-06 |
| D-031 | Product accent validation is format only | Approved (owner 2026-10-03) | 2026-10-03 | PHASE-02, PHASE-04, D-025 |
```

- [ ] **Step 2: Amend D-004**

In the D-004 section, change `- **Status:** Approved` to `- **Status:** Approved; amended by D-029 (2026-10-03)`. Then add this paragraph at the end of its Decision:

```markdown
**Amended by D-029 (2026-10-03):** the bootstrap admin is removed. Admin access comes from `TECHSTRAP_ADMIN_GROUP` alone, so the first admin is whoever is in that IdP group; no database edit is needed.
```

- [ ] **Step 3: Append D-029, D-030 and D-031 at the end of the file**

```markdown
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
PHASE-04 asked for a 400 on a "low-contrast" product accent. The PHASE-02 `ProductAccent` helper (D-025) already derives an on-accent color (white or black, at least 4.58:1) and an ink color darkened to 4.5:1 for any accent, so no accent produces unreadable text.

### Decision
- A product accent must be `#RRGGBB`. A malformed value is `400 accent-colour-invalid` from `ProductBranding.Create`.
- Contrast is not validated.
- `ProductBrandingDto` returns the derived `OnAccentColour` and `AccentInkColour`, so the Admin preview shows exactly what customers see.

### Alternatives Considered
- **Reject accents below 3:1 against the white portal page:** stops near-white accents, but refuses brand colors some products really use. The derived ink keeps links and text readable regardless.

### Consequences
- The PHASE-04 "400 low-contrast accent" test is dropped.
- The PHASE-07 branding form shows a live preview built from the derived colors.

### Approval
- **Approved by:** Jon Seeley (owner, 2026-10-03 PHASE-04 planning)
- **Approved on:** 2026-10-03
```

- [ ] **Step 4: Align the PHASE-04 doc**

Edit `docs/architecture/PHASE-04-agent-auth-and-admin-config.md`:

1. **Objective.** Replace "and the first admin is bootstrapped from configuration" with "and roles come from IdP groups only (D-029)".
2. **Architecture Decisions.**
   - **Authentication bullet.** Rename the policies `AgentPolicy`/`AdminPolicy` to `Agent`/`Admin`, and add: "The `Agent` policy accepts the agent group or the admin group. Both policies refuse an agent whose stored row is deactivated (D-029)."
   - **Role-source bullet.** Replace it with: "Role source of truth (D-029): the group claim alone. The stored `Agent.Role` mirrors the claim at each `GET /api/agents/me`. `UpdateAgentRequestHandler` changes only `IsActive`."
   - **Bootstrap-admin bullet.** Delete it.
   - **Product-branding bullet.** Change "accent validated for the contrast rule from PHASE-02" to "accent validated for format only (D-031); the derived on-accent and ink colors are returned in `ProductBrandingDto`".
   - **Tags bullet.** Replace the **Assumption** sentence with "Delete is rejected with `409 tag-in-use` unless `force=true`, which detaches the tag from every ticket with `TagRemoved` events (D-030)."
   - **Errors bullet.** Replace "400/422" with "400".
   - **Abstractions bullet.** Replace it with: "Abstractions introduced in this phase: `ICurrentAgentClaims` (Application-owned identity: subject, name, email, group-derived role) and `IApiKeyHasher`, plus the Api options type `AgentAccessOptions` (group names and claim type)."
3. **Boundary table.**
   - `GetCurrentAgentRequestHandler` row: the handler column reads "(provisions agent on first call, mirrors the group-derived role)", and the abstractions are `IAgentRepository`, `IUnitOfWork`, `ICurrentAgentClaims`, `TimeProvider`.
   - Every other row: replace `ICurrentUserService` with `ICurrentAgentClaims`.
   - `PUT /api/agents/{id}`: change "(role, active)" to "(active only, D-029)".
   - `PUT /api/products/{id}`: change "400 (color/contrast)" to "400 (color format, D-031)".
   - Add a row after `PUT /api/agents/me/notification-preferences`:
     ```markdown
     | `GET /api/agents/me/notification-preferences` | `GetMyNotificationPreferencesRequestHandler` | `IAgentRepository`, `IProductRepository`, `ICurrentAgentClaims` | EF repositories | 200 `NotificationPreferenceDto[]` (every active product, default off) | Mandatory flow |
     ```
   - Below the table, the sentence that mentions `ICurrentUserService` becomes: "`ICurrentAgentClaims` is implied in every row that needs the caller."
4. **Deliverables and tasks.** Change "17 handlers" to "18 handlers" everywhere.
   - **P04-T02 validation:** add "group claims delivered as repeated claims, a JSON array string or a delimited list".
   - **P04-T03:** rename it to "Implement `ICurrentAgentClaims` for ASP.NET (subject, email, name, group-derived role) and `ClaimsCurrentAgentClaimsTests`".
   - **P04-T04 validation:** replace the bootstrap clauses with "first call provisions with the group-derived role, second call updates name, email and role, a token without an email is refused (`agent-email-required`), an inactive agent is forbidden".
   - **P04-T05:** replace "role change" with "deactivate and reactivate", and add "concurrent deactivation of the last two admins leaves one active admin".
   - **P04-T07 validation:** replace "400 low-contrast accent" with "400 malformed accent color".
   - **P04-T14:** replace "(an admin from the bootstrap setting)" with "(dev API keys hashed through `IApiKeyHasher` with documented dev plaintexts)".
5. **Success Criteria.** Delete the `TECHSTRAP_BOOTSTRAP_ADMIN` criterion. Change "All 17 handlers" to "All 18 handlers". Add: "Roles come from IdP groups only; a deactivated agent gets 403 on every agent endpoint (D-029)."
6. **Risks and Open Questions.**
   - Tick these four items, appending " Resolved: D-029.", " Resolved: D-030." and so on:
     - claim-versus-stored-role
     - deleting an in-use tag
     - the Admin-only versus Agent-readable product list (" Resolved: D-022, agents see active products only.")
     - the Authentik claim item (" Resolved: `TECHSTRAP_GROUP_CLAIM_TYPE`.")
   - In the carried-forward `IApiKeyHasher` line, change "re-seed them through the real hasher so they authenticate" to end " (Task 9 of the PHASE-04 plan)".

- [ ] **Step 5: Align 02-ARCHITECTURE and the package map**

In `02-ARCHITECTURE.md`:
- **Section 3.1.** Change the row `| ICurrentUserService, TimeProvider | ASP.NET implementation; framework clock | Identity and time |` to:
  ```markdown
  | `ICurrentAgentClaims`, `TimeProvider` | `ClaimsCurrentAgentClaims` (Api, over `IHttpContextAccessor`); framework clock | Agent identity (subject, name, email, group-derived role, D-029) and time. `SyntaxCircus.Common.ICurrentUserService` is not used because it carries no groups |
  ```
  In the "Options types" row, remove any mention of bootstrap.
- **Section 4.** Wherever `TECHSTRAP_BOOTSTRAP_ADMIN` or the bootstrap admin is described, replace it with "Roles come from IdP groups only (D-029)". After the group variables, add the claim-type setting `TECHSTRAP_GROUP_CLAIM_TYPE` (default `groups`).
- **Section 7.1, PHASE-04 rows.**
  - Add `GetMyNotificationPreferencesRequestHandler`.
  - Change the `UpdateAgentRequestHandler` description to "active only".
  - Change any "role" wording to match the PHASE-04 table.

In `03-PACKAGE-MAP.md`, in the `SyntaxCircus.AspNetCore.Authentication` row, replace `TECHSTRAP_AGENT_GROUP`, `TECHSTRAP_ADMIN_GROUP`, `TECHSTRAP_BOOTSTRAP_ADMIN` with `TECHSTRAP_AGENT_GROUP`, `TECHSTRAP_ADMIN_GROUP`, `TECHSTRAP_GROUP_CLAIM_TYPE`, and replace "Group-claim authorization policies and bootstrap admin are application-owned." with "Group-claim authorization policies are application-owned (D-029)."

- [ ] **Step 6: Check nothing else still documents the bootstrap admin**

Run: `git grep -n -i "bootstrap" -- docs README.md`
Expected: matches only in `04-DECISION-LOG.md` (D-004's history and D-029). If another doc still describes `TECHSTRAP_BOOTSTRAP_ADMIN` as current behavior, reword it to D-029. Leave history in the PHASE-03 docs and the plans untouched.

- [ ] **Step 7: Run the doc tests**

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: all pass. `RepositoryDocs.Tests.ps1` and `Check-PackageVersions.Tests.ps1` read these docs.

- [ ] **Step 8: Commit**

```bash
git add docs/architecture/04-DECISION-LOG.md docs/architecture/PHASE-04-agent-auth-and-admin-config.md docs/architecture/02-ARCHITECTURE.md docs/architecture/03-PACKAGE-MAP.md
git commit -m "docs: record PHASE-04 owner decisions D-029 to D-031" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 2: Contracts DTOs and the naming rule

**Files:**
- Create: `src/TechStrap.Contracts/Paging/PagedResponse.cs`
- Create: `src/TechStrap.Contracts/Agents/AgentDtos.cs`
- Create: `src/TechStrap.Contracts/Products/ProductDtos.cs`
- Create: `src/TechStrap.Contracts/ApiKeys/ApiKeyDtos.cs`
- Create: `src/TechStrap.Contracts/Tags/TagDtos.cs`
- Create: `src/TechStrap.Contracts/AdminEvents/AdminEventDtos.cs`
- Create: `tests/TechStrap.Architecture.Tests/ContractNamingRules.cs`
- Create: `tests/TechStrap.Architecture.Tests/ContractNamingTests.cs`
- Create: `tests/TechStrap.Architecture.Tests/ContractNamingFixtures.cs`
- Modify: `tests/TechStrap.Architecture.Tests/TechStrap.Architecture.Tests.csproj` (add a ProjectReference to `../../src/TechStrap.Contracts/TechStrap.Contracts.csproj` only if the Contracts assembly is not already reachable; Application references it, so `typeof(TechStrap.Contracts.Paging.PagedResponse<>)` should already resolve. Check before editing)
- Create: `tests/TechStrap.Application.Tests/Agents/AgentPublicNameParityTests.cs`

**Interfaces:**
- Produces every Contracts type below, with exact names and members. Later tasks use them verbatim.
- Produces `ContractNamingRules.FindViolations(IEnumerable<Type>) : IReadOnlyList<string>`.

- [ ] **Step 1: Write the failing naming-rule tests**

`tests/TechStrap.Architecture.Tests/ContractNamingFixtures.cs`:

```csharp
namespace TechStrap.Architecture.Tests.ContractNamingFixtureTypes;

public sealed record GoodWidgetDto(Guid Id);

public sealed record GoodCreateWidgetRequest(string Name);

public sealed record GoodCreateWidgetResponse(Guid Id);

public sealed record GoodPageResponse<T>(IReadOnlyList<T> Items);

public static class GoodWidgetConstants
{
    public const string Kind = "widget";
}

public sealed record BadWidget(Guid Id);

public enum BadWidgetKind
{
    One,
}

public sealed class BadWidgetModel;
```

`tests/TechStrap.Architecture.Tests/ContractNamingRules.cs`:

```csharp
namespace TechStrap.Architecture.Tests;

/// <summary>
/// Every public, non-static type in TechStrap.Contracts ends in Dto, Request or Response, so API shapes are recognizable
/// wherever they are used. Static classes hold shared constants and helpers. The single named exemption is
/// ProductAccentColors, a value tuple returned by the PHASE-02 ProductAccent helper (D-025), which is not an API shape.
/// </summary>
public static class ContractNamingRules
{
    private static readonly string[] Suffixes = ["Dto", "Request", "Response"];

    private static readonly HashSet<string> Exempt = new(StringComparer.Ordinal)
    {
        "TechStrap.Contracts.Branding.ProductAccentColors",
    };

    public static IReadOnlyList<string> FindViolations(IEnumerable<Type> types) =>
        types
            .Where(type => type.IsPublic && !(type.IsAbstract && type.IsSealed))
            .Where(type => type.FullName is not null && !Exempt.Contains(type.FullName))
            .Where(type => !Suffixes.Any(suffix => BaseName(type).EndsWith(suffix, StringComparison.Ordinal)))
            .Select(type => $"{type.FullName} must end in Dto, Request or Response")
            .ToList();

    private static string BaseName(Type type)
    {
        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }
}
```

`tests/TechStrap.Architecture.Tests/ContractNamingTests.cs`:

```csharp
using TechStrap.Architecture.Tests.ContractNamingFixtureTypes;
using TechStrap.Contracts.Paging;

namespace TechStrap.Architecture.Tests;

public sealed class ContractNamingTests
{
    [Fact]
    public void Every_public_contract_type_ends_in_Dto_Request_or_Response()
    {
        var types = typeof(PagedResponse<>).Assembly.GetTypes();

        ContractNamingRules.FindViolations(types).ShouldBeEmpty();
    }

    [Fact]
    public void The_contract_scan_is_not_vacuous()
    {
        typeof(PagedResponse<>).Assembly.GetTypes()
            .Count(type => type.IsPublic && type.Name.EndsWith("Dto", StringComparison.Ordinal))
            .ShouldBeGreaterThanOrEqualTo(8);
    }

    [Fact]
    public void Well_named_types_and_static_constant_classes_pass()
    {
        ContractNamingRules.FindViolations(
            [typeof(GoodWidgetDto), typeof(GoodCreateWidgetRequest), typeof(GoodCreateWidgetResponse), typeof(GoodPageResponse<>), typeof(GoodWidgetConstants)])
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(BadWidget))]
    [InlineData(typeof(BadWidgetKind))]
    [InlineData(typeof(BadWidgetModel))]
    public void A_type_without_a_contract_suffix_is_flagged(Type type) =>
        ContractNamingRules.FindViolations([type]).ShouldHaveSingleItem().ShouldContain(type.Name);
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: a build failure, CS0234/CS0246 (`TechStrap.Contracts.Paging` does not exist).

- [ ] **Step 3: Write the Contracts types**

`src/TechStrap.Contracts/Paging/PagedResponse.cs`:

```csharp
namespace TechStrap.Contracts.Paging;

/// <summary>One page of results. <paramref name="Page"/> is 1-based; <paramref name="TotalCount"/> counts every matching item.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
```

`src/TechStrap.Contracts/Agents/AgentDtos.cs`:

```csharp
namespace TechStrap.Contracts.Agents;

/// <summary>Role names as they appear in agent DTOs (D-029: derived from IdP groups).</summary>
public static class AgentRoles
{
    public const string Agent = "Agent";
    public const string Admin = "Admin";
}

/// <summary>
/// The customer-facing agent name format (D-024): "{first name or override} from {product} Support". Admin uses it for the
/// profile preview; it matches TechStrap.Domain.Agents.AgentPublicIdentity.Resolve (parity test in Application.Tests).
/// </summary>
public static class AgentPublicName
{
    public const string Format = "{0} from {1} Support";

    public const string FallbackFormat = "{0} Support";
}

/// <summary>The signed-in agent's own profile.</summary>
public sealed record AgentDto(
    Guid Id,
    string? Name,
    string Email,
    string Role,
    bool IsActive,
    string? PublicDisplayName,
    DateTimeOffset? LastSeenAt);

/// <summary>
/// One row of the agent list. Agents see active agents with Id, Name and DisplayLabel only (for assignment); Admins also
/// get Email, Role, IsActive and LastSeenAt (D-022).
/// </summary>
public sealed record AgentListItemDto(
    Guid Id,
    string? Name,
    string DisplayLabel,
    string? Email,
    string? Role,
    bool? IsActive,
    DateTimeOffset? LastSeenAt);

/// <summary>Admin-only. The API cannot change a role (D-029); it only activates or deactivates.</summary>
public sealed record UpdateAgentRequest(bool IsActive);

/// <summary>Sets or clears (null or blank) the customer-facing display name override (D-024).</summary>
public sealed record UpdateMyProfileRequest(string? PublicDisplayName);

/// <summary>Per-product new-ticket alert opt-in for the signed-in agent.</summary>
public sealed record NotificationPreferenceDto(Guid ProductId, string ProductName, bool NotifyNewTicket);

public sealed record NotificationPreferenceUpdateDto(Guid ProductId, bool NotifyNewTicket);

public sealed record UpdateNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceUpdateDto> Preferences);
```

`src/TechStrap.Contracts/Products/ProductDtos.cs`:

```csharp
namespace TechStrap.Contracts.Products;

/// <summary>Branding as stored, plus the colors derived from the accent (D-025, D-031) for previews.</summary>
public sealed record ProductBrandingDto(
    string DisplayName,
    string? LogoPath,
    string AccentColour,
    string OnAccentColour,
    string AccentInkColour,
    string? FromAddress,
    string? ReplyTo);

/// <summary>Branding input. A null accent means the default accent; the accent must be #RRGGBB (D-031).</summary>
public sealed record ProductBrandingRequest(
    string? DisplayName,
    string? LogoPath,
    string? AccentColour,
    string? FromAddress,
    string? ReplyTo);

/// <summary><paramref name="Version"/> is the concurrency token; send it back unchanged in <see cref="UpdateProductRequest"/>.</summary>
public sealed record ProductDto(
    Guid Id,
    string Key,
    string Name,
    string NumberPrefix,
    bool IsActive,
    ProductBrandingDto Branding,
    uint Version);

/// <summary>Key and number prefix are permanent once created. A null branding derives the default from the name.</summary>
public sealed record CreateProductRequest(string? Key, string? Name, string? NumberPrefix, ProductBrandingRequest? Branding);

/// <summary><paramref name="Version"/> must equal the version last read; otherwise the update is a 409 conflict.</summary>
public sealed record UpdateProductRequest(string? Name, ProductBrandingRequest Branding, bool IsActive, uint Version);
```

`src/TechStrap.Contracts/ApiKeys/ApiKeyDtos.cs`:

```csharp
namespace TechStrap.Contracts.ApiKeys;

/// <summary>Key kinds (D-001). Trusted keys are server-side; public keys are embedded in client apps.</summary>
public static class ApiKeyKinds
{
    public const string Trusted = "Trusted";
    public const string Public = "Public";
}

/// <summary>A product API key without its secret: the prefix identifies it.</summary>
public sealed record ProductApiKeyDto(
    Guid Id,
    Guid ProductId,
    string Kind,
    string KeyPrefix,
    string? Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt);

public sealed record CreateProductApiKeyRequest(string? Kind, string? Label);

/// <summary>The only place the plaintext key ever appears. It cannot be shown again.</summary>
public sealed record CreateProductApiKeyResponse(ProductApiKeyDto Key, string PlaintextKey);
```

`src/TechStrap.Contracts/Tags/TagDtos.cs`:

```csharp
namespace TechStrap.Contracts.Tags;

public sealed record TagDto(Guid Id, string Slug, string Name, string Colour);

/// <summary>The slug is permanent once created; lower-case words separated by hyphens.</summary>
public sealed record CreateTagRequest(string? Slug, string? Name, string? Colour);

public sealed record UpdateTagRequest(string? Name, string? Colour);
```

`src/TechStrap.Contracts/AdminEvents/AdminEventDtos.cs`:

```csharp
namespace TechStrap.Contracts.AdminEvents;

/// <summary>
/// One admin audit entry. <paramref name="Payload"/> is the raw JSON object recorded with the change; it never holds secrets
/// or personal data (D-006). <paramref name="ActorLabel"/> is the acting agent's name, or email when the name is unknown,
/// or null when the agent no longer exists.
/// </summary>
public sealed record AdminEventDto(
    Guid Id,
    string Type,
    Guid ActorId,
    string? ActorLabel,
    string SubjectType,
    Guid SubjectId,
    string Payload,
    DateTimeOffset OccurredAt);
```

- [ ] **Step 4: Write the public-name parity test**

`tests/TechStrap.Application.Tests/Agents/AgentPublicNameParityTests.cs`:

```csharp
using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

/// <summary>Admin previews the customer-facing name with the Contracts format; customers see AgentPublicIdentity (D-024).</summary>
public sealed class AgentPublicNameParityTests
{
    private static Agent NewAgent(string? name, string? publicDisplayName)
    {
        var agent = Agent.Create("sub-1", name, "sam@example.com", AgentRole.Agent, new FakeTimeProvider()).Value;
        agent.SetPublicDisplayName(publicDisplayName).IsSuccess.ShouldBeTrue();
        return agent;
    }

    [Theory]
    [InlineData("Sam Whitfield", null, "Sam")]
    [InlineData("Riley Chen", "Ry", "Ry")]
    public void The_contract_format_produces_the_domain_name(string name, string? publicDisplayName, string given)
    {
        var agent = NewAgent(name, publicDisplayName);

        string.Format(CultureInfo.InvariantCulture, AgentPublicName.Format, given, "Orbitly")
            .ShouldBe(AgentPublicIdentity.Resolve(agent, "Orbitly"));
    }

    [Fact]
    public void The_fallback_format_produces_the_domain_name_when_no_name_is_known()
    {
        var agent = NewAgent(null, null);

        string.Format(CultureInfo.InvariantCulture, AgentPublicName.FallbackFormat, "Orbitly")
            .ShouldBe(AgentPublicIdentity.Resolve(agent, "Orbitly"));
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release` and `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: PASS. If the parity test fails because `AgentPublicIdentity.Resolve` uses different words, fix the **Contracts constants** to match Domain, not the other way round. Domain is the customer-facing source of truth (D-024).

- [ ] **Step 6: Run the full build and commit**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: 0 warnings.

```bash
git add src/TechStrap.Contracts tests/TechStrap.Architecture.Tests/ContractNaming*.cs tests/TechStrap.Application.Tests/Agents/AgentPublicNameParityTests.cs
git commit -m "feat(contracts): agent, product, api key, tag and admin event DTOs with a naming rule" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 3: JWT authentication, group policies and agent identity

**Files:**
- Modify: `src/TechStrap.Api/TechStrap.Api.csproj` (add `<PackageReference Include="SyntaxCircus.AspNetCore.Authentication" />` and `<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />`; both versions are already pinned)
- Create: `src/TechStrap.Application/Agents/ICurrentAgentClaims.cs`
- Create: `src/TechStrap.Api/Options/AgentAccessOptions.cs`
- Create: `src/TechStrap.Api/Security/AuthorizationPolicies.cs`
- Create: `src/TechStrap.Api/Security/AgentGroups.cs`
- Create: `src/TechStrap.Api/Security/ClaimsCurrentAgentClaims.cs`
- Create: `src/TechStrap.Api/Security/AgentAccessRequirement.cs`
- Create: `src/TechStrap.Api/Security/AgentAuthenticationSetup.cs`
- Create: `src/TechStrap.Api/Startup/ApplicationHandlerRegistration.cs`
- Delete: `src/TechStrap.Api/Security/UnauthenticatedScheme.cs`
- Modify: `src/TechStrap.Api/Program.cs`
- Modify: `src/TechStrap.Api/.env.example`, `.env.production.example`, `docker-compose.production.yml`, `docker-compose.uat.yml` (remove `TECHSTRAP_BOOTSTRAP_ADMIN`; add `TECHSTRAP_GROUP_CLAIM_TYPE`)
- Modify: `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs` (Api key list)
- Modify: `tests/TechStrap.Api.Tests/HostFactory.cs` (`ApiFactory` adds test JWT settings and validation)
- Create: `tests/TechStrap.Api.Tests/Auth/TestJwt.cs`
- Create: `tests/TechStrap.Api.Tests/Auth/AuthProbeController.cs`
- Create: `tests/TechStrap.Api.Tests/Auth/AgentAuthTests.cs`
- Create: `tests/TechStrap.Api.Tests/Auth/ClaimsCurrentAgentClaimsTests.cs`
- Create: `tests/TechStrap.Api.Tests/Auth/AgentAccessOptionsTests.cs`

**Interfaces:**
- Produces (Application, namespace `TechStrap.Application.Agents`):
  ```csharp
  public interface ICurrentAgentClaims { AgentClaims? Current { get; } }
  public sealed record AgentClaims(string Subject, string? Name, string? Email, AgentRole Role);
  ```
- Produces (Api):
  - `AuthorizationPolicies.Agent` = `"Agent"` and `AuthorizationPolicies.Admin` = `"Admin"`.
  - `AgentAccessOptions` with properties `AgentGroup`, `AdminGroup`, `GroupClaimType`.
  - `ClaimsCurrentAgentClaims.FromPrincipal(ClaimsPrincipal, AgentAccessOptions) : AgentClaims?`.
  - `AgentAccessRequirement` and `AgentAccessAuthorizationHandler`. Task 5 extends the handler.
  - `IServiceCollection.AddAgentAuthentication(IConfiguration, IHostEnvironment)`.
  - `IServiceCollection.AddApplicationHandlers()`.
- Produces (Api.Tests):
  - `TestJwt.Token(...)`, `TestJwt.Settings`, `TestJwt.Configure(IServiceCollection)`, `TestJwt.Bearer(HttpClient, string token)`.
  - `AuthProbeController`, with routes `/__test/agent` and `/__test/admin`.
  - `ApiFactory`, which now always carries the test JWT setup.

- [ ] **Step 1: Write the failing identity tests**

`tests/TechStrap.Api.Tests/Auth/ClaimsCurrentAgentClaimsTests.cs`:

```csharp
using System.Security.Claims;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Domain.Agents;

namespace TechStrap.Api.Tests.Auth;

public sealed class ClaimsCurrentAgentClaimsTests
{
    private static readonly AgentAccessOptions Options = new();

    private static ClaimsPrincipal Principal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "test"));

    [Fact]
    public void An_agent_group_member_gets_the_Agent_role_with_identity_claims()
    {
        var claims = ClaimsCurrentAgentClaims.FromPrincipal(
            Principal(("sub", "abc"), ("email", "sam@example.com"), ("name", "Sam Whitfield"), ("groups", "techstrap-agents")), Options);

        claims.ShouldBe(new AgentClaims("abc", "Sam Whitfield", "sam@example.com", AgentRole.Agent));
    }

    [Fact]
    public void An_admin_group_member_gets_the_Admin_role_even_without_the_agent_group()
    {
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("groups", "techstrap-admins")), Options)!
            .Role.ShouldBe(AgentRole.Admin);
    }

    [Theory]
    [InlineData("[\"other\",\"TechStrap-Agents\"]")]
    [InlineData("other, techstrap-agents")]
    [InlineData("other techstrap-agents")]
    public void Groups_are_read_from_repeated_claims_json_arrays_and_delimited_lists(string value)
    {
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("groups", "unrelated"), ("groups", value)), Options)!
            .Role.ShouldBe(AgentRole.Agent);
    }

    [Fact]
    public void A_configured_claim_type_is_used_instead_of_groups()
    {
        var options = new AgentAccessOptions { GroupClaimType = "roles" };

        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("roles", "techstrap-admins")), options)!
            .Role.ShouldBe(AgentRole.Admin);
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("groups", "techstrap-admins")), options).ShouldBeNull();
    }

    [Fact]
    public void Preferred_username_is_the_name_when_no_name_claim_is_present()
    {
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("preferred_username", "sam"), ("groups", "techstrap-agents")), Options)!
            .Name.ShouldBe("sam");
    }

    [Fact]
    public void No_group_no_subject_or_an_anonymous_principal_gives_no_agent()
    {
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("sub", "abc"), ("groups", "someone-else")), Options).ShouldBeNull();
        ClaimsCurrentAgentClaims.FromPrincipal(Principal(("groups", "techstrap-agents")), Options).ShouldBeNull();
        ClaimsCurrentAgentClaims.FromPrincipal(new ClaimsPrincipal(new ClaimsIdentity()), Options).ShouldBeNull();
    }
}
```

`tests/TechStrap.Api.Tests/Auth/AgentAccessOptionsTests.cs`:

```csharp
using Microsoft.Extensions.Options;
using TechStrap.Api.Options;

namespace TechStrap.Api.Tests.Auth;

public sealed class AgentAccessOptionsTests
{
    [Fact]
    public void Defaults_match_the_documented_env_example_values()
    {
        var options = new AgentAccessOptions();

        options.AgentGroup.ShouldBe("techstrap-agents");
        options.AdminGroup.ShouldBe("techstrap-admins");
        options.GroupClaimType.ShouldBe("groups");
    }

    [Theory]
    [InlineData("TECHSTRAP_AGENT_GROUP", "")]
    [InlineData("TECHSTRAP_ADMIN_GROUP", " ")]
    [InlineData("TECHSTRAP_GROUP_CLAIM_TYPE", "")]
    [InlineData("TECHSTRAP_ADMIN_GROUP", "techstrap-agents")]
    public async Task A_blank_or_duplicate_group_setting_stops_the_api_from_starting(string key, string value)
    {
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { [key] = value });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Outside_development_a_missing_jwt_authority_stops_the_api_from_starting()
    {
        await using var factory = new ApiFactory(
            environment: "Production",
            settings: new Dictionary<string, string?> { ["Authentication:JwtBearer:Authority"] = "" });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient());
    }
}
```

- [ ] **Step 2: Write the JWT test helper, the probe controller and the auth tests**

`tests/TechStrap.Api.Tests/Auth/TestJwt.cs`:

```csharp
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace TechStrap.Api.Tests.Auth;

/// <summary>
/// A locally signed test issuer. Tests never contact a real IdP: the JWT bearer handler gets a static OIDC configuration
/// holding this HS256 key (the pattern from cmsify's OidcAuthenticationApiTests).
/// </summary>
public static class TestJwt
{
    public const string Issuer = "https://issuer.techstrap.test/";
    public const string Audience = "techstrap-api";
    public const string AgentGroup = "techstrap-agents";
    public const string AdminGroup = "techstrap-admins";

    private static readonly SymmetricSecurityKey SigningKey = new(Encoding.UTF8.GetBytes("techstrap-test-signing-key-not-a-secret-0123456789"));

    public static IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Authentication:JwtBearer:Authority"] = Issuer,
        ["Authentication:JwtBearer:Audiences:0"] = Audience,
    };

    public static void Configure(IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { SigningKey } });
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                IssuerSigningKey = SigningKey,
                ClockSkew = TimeSpan.Zero,
            };
        });

    public static string Token(
        string subject,
        IEnumerable<string> groups,
        string? email = "agent@example.com",
        string? name = "Test Agent",
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var claims = new List<Claim> { new("sub", subject) };
        if (email is not null)
        {
            claims.Add(new Claim("email", email));
        }

        if (name is not null)
        {
            claims.Add(new Claim("name", name));
        }

        claims.AddRange(groups.Select(group => new Claim("groups", group)));
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.AddMinutes(-5),
            IssuedAt = now.AddMinutes(-5),
            Expires = expires ?? now.AddMinutes(30),
            SigningCredentials = new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }

    public static HttpClient Bearer(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
```

`tests/TechStrap.Api.Tests/Auth/AuthProbeController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TechStrap.Api.Security;

namespace TechStrap.Api.Tests.Auth;

/// <summary>Test-only endpoints that carry the real policies, added to the host as an application part by AgentAuthTests.</summary>
[ApiController]
[Route("__test")]
public sealed class AuthProbeController : ControllerBase
{
    [HttpGet("agent")]
    [Authorize(Policy = AuthorizationPolicies.Agent)]
    public IActionResult AgentOnly() => Ok();

    [HttpGet("admin")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public IActionResult AdminOnly() => Ok();
}
```

`ControllerHandlerInjectionTests` scans only the Api assembly, so this test controller is not subject to the handler rules.

Modify `tests/TechStrap.Api.Tests/HostFactory.cs`. Replace the `ApiFactory` class with:

```csharp
/// <summary>The Api host with the locally signed test issuer (TestJwt) always configured, so any test can send a bearer token.</summary>
public sealed class ApiFactory(
    string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null,
    Action<IServiceCollection>? configureServices = null)
    : HostFactory<TechStrap.Api.Program>(
        environment,
        Auth.TestJwt.Settings.Concat(settings ?? new Dictionary<string, string?>())
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase),
        services =>
        {
            Auth.TestJwt.Configure(services);
            configureServices?.Invoke(services);
        });
```

`tests/TechStrap.Api.Tests/Auth/AgentAuthTests.cs`:

```csharp
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests.Auth;

/// <summary>JWT validation and the group policies (D-004, D-029), against a migrated test database.</summary>
public sealed class AgentAuthTests(TestPostgres postgres)
{
    private async Task<ApiFactory> FactoryAsync()
    {
        var database = await postgres.CreateDatabaseAsync();
        return new ApiFactory(
            settings: new Dictionary<string, string?>
            {
                ["ConnectionStrings:TechStrap"] = database,
                [ApiStartupTasks.MigrateOnStartupKey] = "true",
            },
            configureServices: services => services.AddControllers().AddApplicationPart(typeof(AuthProbeController).Assembly));
    }

    private static async Task<HttpStatusCode> GetAsync(ApiFactory factory, string path, string? token)
    {
        using var client = factory.CreateClient();
        if (token is not null)
        {
            client.Bearer(token);
        }

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    [Fact]
    public async Task A_request_without_a_token_is_401()
    {
        await using var factory = await FactoryAsync();

        (await GetAsync(factory, "/__test/agent", null)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_with_a_bad_signature_wrong_audience_wrong_issuer_or_expiry_is_401()
    {
        await using var factory = await FactoryAsync();
        var otherKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-different-key-that-is-long-enough-0123456789"));

        (await GetAsync(factory, "/__test/agent", TestJwt.Token("s1", [TestJwt.AgentGroup], signingKey: otherKey))).ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(factory, "/__test/agent", TestJwt.Token("s1", [TestJwt.AgentGroup], audience: "someone-else"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(factory, "/__test/agent", TestJwt.Token("s1", [TestJwt.AgentGroup], issuer: "https://evil.test/"))).ShouldBe(HttpStatusCode.Unauthorized);
        (await GetAsync(factory, "/__test/agent", TestJwt.Token("s1", [TestJwt.AgentGroup], expires: DateTime.UtcNow.AddMinutes(-1)))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_valid_token_without_an_agent_or_admin_group_is_403()
    {
        await using var factory = await FactoryAsync();

        (await GetAsync(factory, "/__test/agent", TestJwt.Token("s1", ["someone-else"]))).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_agent_group_reaches_agent_routes_but_not_admin_routes()
    {
        await using var factory = await FactoryAsync();
        var token = TestJwt.Token("s1", [TestJwt.AgentGroup]);

        (await GetAsync(factory, "/__test/agent", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync(factory, "/__test/admin", token)).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_admin_group_alone_reaches_agent_and_admin_routes()
    {
        await using var factory = await FactoryAsync();
        var token = TestJwt.Token("s1", [TestJwt.AdminGroup]);

        (await GetAsync(factory, "/__test/agent", token)).ShouldBe(HttpStatusCode.OK);
        (await GetAsync(factory, "/__test/admin", token)).ShouldBe(HttpStatusCode.OK);
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`
Expected: a build failure (CS0234/CS0246: `TechStrap.Api.Options.AgentAccessOptions`, `TechStrap.Api.Security.ClaimsCurrentAgentClaims`, `AuthorizationPolicies` and `TechStrap.Application.Agents` do not exist).

- [ ] **Step 4: Add the Application identity abstraction**

`src/TechStrap.Application/Agents/ICurrentAgentClaims.cs`:

```csharp
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

/// <summary>
/// The signed-in agent as the identity provider describes them. This is the project-specific identity abstraction (instead
/// of SyntaxCircus.Common.ICurrentUserService, which carries no groups). Application code never reads claims directly.
/// </summary>
public interface ICurrentAgentClaims
{
    /// <summary>Null when the caller is not authenticated, has no subject, or is in neither the agent nor the admin group.</summary>
    AgentClaims? Current { get; }
}

/// <summary>Identity claims plus the role derived from IdP groups alone (D-029): Admin for the admin group, otherwise Agent.</summary>
public sealed record AgentClaims(string Subject, string? Name, string? Email, AgentRole Role);
```

- [ ] **Step 5: Add the Api options, policies and identity implementation**

`src/TechStrap.Api/Options/AgentAccessOptions.cs`:

```csharp
namespace TechStrap.Api.Options;

/// <summary>
/// Which IdP groups grant agent and admin access, and which claim carries them (D-004, D-029). The settings are flat
/// environment names (no section), so they are bound by key in AgentAuthenticationSetup.
/// </summary>
public sealed class AgentAccessOptions
{
    public const string AgentGroupKey = "TECHSTRAP_AGENT_GROUP";
    public const string AdminGroupKey = "TECHSTRAP_ADMIN_GROUP";
    public const string GroupClaimTypeKey = "TECHSTRAP_GROUP_CLAIM_TYPE";

    public string AgentGroup { get; set; } = "techstrap-agents";

    public string AdminGroup { get; set; } = "techstrap-admins";

    public string GroupClaimType { get; set; } = "groups";
}
```

`src/TechStrap.Api/Security/AuthorizationPolicies.cs`:

```csharp
namespace TechStrap.Api.Security;

/// <summary>Policy names (02-ARCHITECTURE section 4, D-022, D-029).</summary>
public static class AuthorizationPolicies
{
    /// <summary>Agent group or admin group, and not deactivated.</summary>
    public const string Agent = "Agent";

    /// <summary>Admin group, and not deactivated.</summary>
    public const string Admin = "Admin";
}
```

`src/TechStrap.Api/Security/AgentGroups.cs`:

```csharp
using System.Security.Claims;
using System.Text.Json;

namespace TechStrap.Api.Security;

/// <summary>
/// Reads group membership from a principal. IdPs deliver groups as repeated claims, as one JSON array string, or as a
/// comma- or space-separated list; all three are accepted, and names compare without case.
/// </summary>
public static class AgentGroups
{
    private static readonly char[] Separators = [',', ' ', '\t', '\n', '\r'];

    public static bool Has(ClaimsPrincipal principal, string claimType, string group) =>
        principal.FindAll(claimType).SelectMany(claim => Split(claim.Value)).Any(value => string.Equals(value, group, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> Split(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<string[]>(text)?.Select(item => item.Trim()) ?? [];
            }
            catch (JsonException)
            {
                // Not JSON after all: fall through to the delimited form.
            }
        }

        return text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
```

`src/TechStrap.Api/Security/ClaimsCurrentAgentClaims.cs`:

```csharp
using System.Security.Claims;
using Microsoft.Extensions.Options;
using TechStrap.Api.Options;
using TechStrap.Application.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Api.Security;

/// <summary>
/// ICurrentAgentClaims over the request principal. Claim names are the raw OIDC names (sub, email, name,
/// preferred_username) because AgentAuthenticationSetup turns off inbound claim mapping.
/// </summary>
public sealed class ClaimsCurrentAgentClaims(IHttpContextAccessor httpContextAccessor, IOptions<AgentAccessOptions> options) : ICurrentAgentClaims
{
    public const string SubjectClaim = "sub";
    public const string EmailClaim = "email";
    public const string NameClaim = "name";
    public const string PreferredUsernameClaim = "preferred_username";

    public AgentClaims? Current =>
        httpContextAccessor.HttpContext?.User is { } user ? FromPrincipal(user, options.Value) : null;

    public static AgentClaims? FromPrincipal(ClaimsPrincipal principal, AgentAccessOptions options)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var subject = principal.FindFirst(SubjectClaim)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        AgentRole? role = AgentGroups.Has(principal, options.GroupClaimType, options.AdminGroup) ? AgentRole.Admin
            : AgentGroups.Has(principal, options.GroupClaimType, options.AgentGroup) ? AgentRole.Agent
            : null;

        return role is null
            ? null
            : new AgentClaims(
                subject,
                principal.FindFirst(NameClaim)?.Value ?? principal.FindFirst(PreferredUsernameClaim)?.Value,
                principal.FindFirst(EmailClaim)?.Value,
                role.Value);
    }
}
```

`src/TechStrap.Api/Security/AgentAccessRequirement.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TechStrap.Api.Options;
using TechStrap.Domain.Agents;

namespace TechStrap.Api.Security;

/// <summary>The caller must be in the agent or admin group; <see cref="AdminOnly"/> requires the admin group (D-029).</summary>
public sealed class AgentAccessRequirement(bool adminOnly) : IAuthorizationRequirement
{
    public bool AdminOnly { get; } = adminOnly;
}

/// <summary>Host-level policy check. Task 5 adds the refusal of deactivated agents.</summary>
public sealed class AgentAccessAuthorizationHandler(IOptions<AgentAccessOptions> options) : AuthorizationHandler<AgentAccessRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AgentAccessRequirement requirement)
    {
        var claims = ClaimsCurrentAgentClaims.FromPrincipal(context.User, options.Value);
        if (claims is not null && (!requirement.AdminOnly || claims.Role == AgentRole.Admin))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```

`src/TechStrap.Api/Security/AgentAuthenticationSetup.cs`:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SyntaxCircus.AspNetCore.Authentication;
using TechStrap.Api.Options;
using TechStrap.Application.Agents;

namespace TechStrap.Api.Security;

/// <summary>Agent sign-in: OIDC JWT bearer plus the Agent and Admin group policies (D-004, D-029).</summary>
public static class AgentAuthenticationSetup
{
    public const string JwtSection = "Authentication:JwtBearer";

    public static IServiceCollection AddAgentAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSyntaxCircusJwtBearer(configuration);

        // Keep raw OIDC claim names (sub, email, name, groups) so ClaimsCurrentAgentClaims and TECHSTRAP_GROUP_CLAIM_TYPE
        // mean exactly what the IdP sends.
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.MapInboundClaims = false);

        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSection))
            .Validate(
                settings => environment.IsDevelopment() || (!string.IsNullOrWhiteSpace(settings.Authority) && settings.Audiences.Any(audience => !string.IsNullOrWhiteSpace(audience))),
                "Authentication:JwtBearer:Authority and Audiences:0 are required outside Development.")
            .ValidateOnStart();

        services.AddOptions<AgentAccessOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                options.AgentGroup = config[AgentAccessOptions.AgentGroupKey] ?? options.AgentGroup;
                options.AdminGroup = config[AgentAccessOptions.AdminGroupKey] ?? options.AdminGroup;
                options.GroupClaimType = config[AgentAccessOptions.GroupClaimTypeKey] ?? options.GroupClaimType;
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.AgentGroup), $"{AgentAccessOptions.AgentGroupKey} must not be blank.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.AdminGroup), $"{AgentAccessOptions.AdminGroupKey} must not be blank.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.GroupClaimType), $"{AgentAccessOptions.GroupClaimTypeKey} must not be blank.")
            .Validate(
                options => !string.Equals(options.AgentGroup.Trim(), options.AdminGroup.Trim(), StringComparison.OrdinalIgnoreCase),
                $"{AgentAccessOptions.AgentGroupKey} and {AgentAccessOptions.AdminGroupKey} must be different groups.")
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentAgentClaims, ClaimsCurrentAgentClaims>();
        services.AddScoped<IAuthorizationHandler, AgentAccessAuthorizationHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Agent, policy => policy.RequireAuthenticatedUser().AddRequirements(new AgentAccessRequirement(adminOnly: false)))
            .AddPolicy(AuthorizationPolicies.Admin, policy => policy.RequireAuthenticatedUser().AddRequirements(new AgentAccessRequirement(adminOnly: true)))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }

    /// <summary>The subset of the package's JWT settings that start-up validation checks.</summary>
    public sealed class JwtSettings
    {
        public string Authority { get; set; } = string.Empty;

        public List<string> Audiences { get; set; } = [];
    }
}
```

`src/TechStrap.Api/Startup/ApplicationHandlerRegistration.cs`:

```csharp
using TechStrap.Application;

namespace TechStrap.Api.Startup;

/// <summary>Registers every Application handler (class XxxHandler with interface IXxxHandler) as scoped.</summary>
public static class ApplicationHandlerRegistration
{
    public static IServiceCollection AddApplicationHandlers(this IServiceCollection services)
    {
        foreach (var (contract, implementation) in Handlers())
        {
            services.AddScoped(contract, implementation);
        }

        return services;
    }

    public static IEnumerable<(Type Contract, Type Implementation)> Handlers() =>
        typeof(ApplicationAssemblyMarker).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(type => (Contract: type.GetInterface("I" + type.Name), Implementation: type))
            .Where(pair => pair.Contract is not null)
            .Select(pair => (pair.Contract!, pair.Implementation));
}
```

- [ ] **Step 6: Wire Program.cs and remove the placeholder scheme**

In `src/TechStrap.Api/Program.cs`:

- Remove the `using TechStrap.Api.Security;` usage of `UnauthenticatedScheme`. Keep the namespace import, because it is now needed for `AgentAuthenticationSetup`.
- Replace the block from `builder.Services.AddAuthentication(UnauthenticatedScheme.Name)` through `.SetFallbackPolicy(...)` with:

```csharp
builder.Services.AddAgentAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddResultProblemDetails();
builder.Services.AddApplicationHandlers();
```

Add `using TechStrap.Api.Startup;` if it is not already present (it is, for `ApiStartupTasks`). Delete `src/TechStrap.Api/Security/UnauthenticatedScheme.cs`.

Run: `git grep -n "UnauthenticatedScheme"`
Expected: no matches in `src/` or `tests/`. Update any doc comment that still mentions the placeholder.

- [ ] **Step 7: Update environment files and the completeness test**

In `src/TechStrap.Api/.env.example`, replace the line `TECHSTRAP_BOOTSTRAP_ADMIN=` with:

```
# Claim that carries IdP group names (D-029). Authentik and most IdPs use "groups".
TECHSTRAP_GROUP_CLAIM_TYPE=groups
```

In `.env.production.example`, delete the `TECHSTRAP_BOOTSTRAP_ADMIN=` line and add `TECHSTRAP_GROUP_CLAIM_TYPE=groups` next to `TECHSTRAP_ADMIN_GROUP`.

In `docker-compose.production.yml` and `docker-compose.uat.yml`, replace `TECHSTRAP_BOOTSTRAP_ADMIN: ${TECHSTRAP_BOOTSTRAP_ADMIN:-}` with `TECHSTRAP_GROUP_CLAIM_TYPE: ${TECHSTRAP_GROUP_CLAIM_TYPE:-groups}`, keeping the indentation exactly.

In `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`, replace `"TECHSTRAP_BOOTSTRAP_ADMIN",` with `"TECHSTRAP_GROUP_CLAIM_TYPE",` in the Api list.

Run: `git grep -n "BOOTSTRAP" -- . ':!docs/architecture/04-DECISION-LOG.md' ':!docs/superpowers'`
Expected: no matches.

- [ ] **Step 8: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`
Expected:
- the new tests pass;
- the existing tests still pass, including `PublicApiHardeningTests.An_unknown_route_is_denied_with_401_not_a_server_error` and `The_fallback_authorization_policy_denies_anonymous_callers`;
- tests that start the Api in Production now get the test Authority from `ApiFactory`.

If a test constructs `HostFactory<TechStrap.Api.Program>` directly in a non-Development environment, switch it to `ApiFactory`.

The host may surface the start-up validation failure wrapped in an `AggregateException` or `InvalidOperationException`. If it does, change the two `Should.Throw<OptionsValidationException>` assertions to catch `Exception` and then assert on the innermost exception:

```csharp
var thrown = Should.Throw<Exception>(() => factory.CreateClient());
thrown.GetBaseException().ShouldBeOfType<OptionsValidationException>();
```

Keep the assertion strict: the innermost exception must be `OptionsValidationException`.

- [ ] **Step 9: Run the architecture tests and the full build**

Run: `dotnet build TechStrap.slnx -c Release` and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: 0 warnings, and the tests pass. `AbstractionShapeTests` now also covers `ICurrentAgentClaims`; it has no async members and no ASP.NET types.

- [ ] **Step 10: Commit**

```bash
git add -A src/TechStrap.Api src/TechStrap.Application/Agents tests/TechStrap.Api.Tests .env.production.example docker-compose.production.yml docker-compose.uat.yml
git commit -m "feat(api): OIDC JWT sign-in with group-based Agent and Admin policies" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

Before committing, run `git status` and make sure nothing under `.superpowers` is staged.

---

### Task 4: Shared Application helpers (acting agent, errors, audit)

**Files:**
- Modify: `src/TechStrap.Application/TechStrap.Application.csproj` (add `<ItemGroup><InternalsVisibleTo Include="TechStrap.Application.Tests" /></ItemGroup>`)
- Create: `src/TechStrap.Application/Agents/AgentErrors.cs`
- Create: `src/TechStrap.Application/Agents/CurrentAgent.cs`
- Create: `src/TechStrap.Application/Auditing/AdminAudit.cs`
- Create: `tests/TechStrap.Application.Tests/Agents/CurrentAgentTests.cs`
- Create: `tests/TechStrap.Application.Tests/Auditing/AdminAuditTests.cs`
- Create: `tests/TechStrap.Application.Tests/Support/UnitOfWorkSubstitute.cs` (shared by the handler tests in later tasks)

**Interfaces:**
- Consumes: `ICurrentAgentClaims` and `AgentClaims` (Task 3); `IAgentRepository.GetBySubjectAsync`, `IAdminEventRepository.Add` and `IUnitOfWork` (PHASE-03); `AdminEvent.Record` (PHASE-03).
- Produces:
  - `internal static class AgentErrors`, with these members, each returning a `ResultError`:
    - `AccessRequired()`
    - `EmailRequired()`
    - `IdentityInvalid(string detail)`
    - `NotProvisioned()`
    - `Inactive()`
    - `NotFound()`
    - `LastActiveAdmin()`

    The code constants are `AgentErrors.Codes.*`.
  - `internal static class CurrentAgent { static Task<Result<Agent>> RequireActiveAsync(ICurrentAgentClaims, IAgentRepository, CancellationToken) }`.
  - `internal static class AdminAudit { static void Record(IAdminEventRepository, AdminEventType, Agent actor, AdminSubjectType, Guid subjectId, object payload, TimeProvider) }`.
  - Test support: `UnitOfWorkSubstitute.Create(params Result[] commits) : IUnitOfWork`.

- [ ] **Step 1: Write the failing tests**

`tests/TechStrap.Application.Tests/Support/UnitOfWorkSubstitute.cs`:

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Tests.Support;

/// <summary>An IUnitOfWork whose scopes commit with the given results in order (success when the list runs out).</summary>
public static class UnitOfWorkSubstitute
{
    public static IUnitOfWork Create(params Result[] commits)
    {
        var queue = new Queue<Result>(commits);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.BeginAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var scope = Substitute.For<IUnitOfWorkScope>();
            scope.CommitAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : Result.Success()));
            return Task.FromResult(scope);
        });
        return unitOfWork;
    }

    public static Result Conflict(string code) =>
        Result.Failure(new ResultError(code, "The change conflicts with the stored data.", ResultErrorKind.Conflict));
}
```

`tests/TechStrap.Application.Tests/Agents/CurrentAgentTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

public sealed class CurrentAgentTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();

    private static Agent NewAgent(bool active = true)
    {
        var agent = Agent.Create("sub-1", "Sam", "sam@example.com", AgentRole.Admin, new FakeTimeProvider()).Value;
        agent.SetActive(active);
        return agent;
    }

    [Fact]
    public async Task An_active_provisioned_agent_is_returned()
    {
        var agent = NewAgent();
        _claims.Current.Returns(new AgentClaims("sub-1", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(agent);

        var result = await CurrentAgent.RequireActiveAsync(_claims, _agents, TestContext.Current.CancellationToken);

        result.Value.ShouldBeSameAs(agent);
    }

    [Fact]
    public async Task No_agent_claims_is_forbidden()
    {
        var result = await CurrentAgent.RequireActiveAsync(_claims, _agents, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Forbidden),
            error => error.Code.ShouldBe(AgentErrors.Codes.AccessRequired));
    }

    [Fact]
    public async Task An_agent_without_a_row_is_told_to_open_techstrap_first()
    {
        _claims.Current.Returns(new AgentClaims("sub-1", "Sam", "sam@example.com", AgentRole.Agent));

        var result = await CurrentAgent.RequireActiveAsync(_claims, _agents, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(AgentErrors.Codes.NotProvisioned);
    }

    [Fact]
    public async Task A_deactivated_agent_is_forbidden()
    {
        _claims.Current.Returns(new AgentClaims("sub-1", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(NewAgent(active: false));

        var result = await CurrentAgent.RequireActiveAsync(_claims, _agents, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Forbidden),
            error => error.Code.ShouldBe(AgentErrors.Codes.Inactive));
    }
}
```

`tests/TechStrap.Application.Tests/Auditing/AdminAuditTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Auditing;

public sealed class AdminAuditTests
{
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _actor = Agent.Create("sub-1", "Sam", "sam@example.com", AgentRole.Admin, new FakeTimeProvider()).Value;

    [Fact]
    public void The_event_is_staged_with_a_camel_case_payload_and_the_acting_agent()
    {
        var subjectId = Guid.CreateVersion7();

        AdminAudit.Record(_events, AdminEventType.TagCreated, _actor, AdminSubjectType.Tag, subjectId, new { Slug = "bug" }, _clock);

        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.TagCreated && e.ActorId == _actor.Id && e.SubjectId == subjectId && e.PayloadJson == "{\"slug\":\"bug\"}"));
    }

    [Fact]
    public void A_payload_with_personal_data_is_a_programming_error() =>
        Should.Throw<InvalidOperationException>(() =>
            AdminAudit.Record(_events, AdminEventType.TagCreated, _actor, AdminSubjectType.Tag, Guid.CreateVersion7(), new { Email = "x@example.com" }, _clock));
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: a build failure, because `AgentErrors`, `CurrentAgent` and `AdminAudit` do not exist yet.

- [ ] **Step 3: Implement**

`src/TechStrap.Application/Agents/AgentErrors.cs`:

```csharp
using SyntaxCircus.Common;

namespace TechStrap.Application.Agents;

/// <summary>Agent access outcomes. The messages are shown to agents: each gives the plain cause and the next step (BRAND.md section 3).</summary>
internal static class AgentErrors
{
    public static class Codes
    {
        public const string AccessRequired = "agent-access-required";
        public const string EmailRequired = "agent-email-required";
        public const string IdentityInvalid = "agent-identity-invalid";
        public const string NotProvisioned = "agent-not-provisioned";
        public const string Inactive = "agent-inactive";
        public const string NotFound = "agent-not-found";
        public const string LastActiveAdmin = "last-active-admin";
    }

    public static ResultError AccessRequired() =>
        new(Codes.AccessRequired, "Your account is not in the TechStrap agent or admin group. Ask your identity provider administrator to add you.", ResultErrorKind.Forbidden);

    public static ResultError EmailRequired() =>
        new(Codes.EmailRequired, "Your sign-in did not include an email address. Ask your identity provider administrator to release the email claim to TechStrap.", ResultErrorKind.Forbidden);

    public static ResultError IdentityInvalid(string detail) =>
        new(Codes.IdentityInvalid, $"Your sign-in details could not be used: {detail} Ask your identity provider administrator to check your profile.", ResultErrorKind.Forbidden);

    public static ResultError NotProvisioned() =>
        new(Codes.NotProvisioned, "Open TechStrap once to finish signing in, then try again.", ResultErrorKind.Forbidden);

    public static ResultError Inactive() =>
        new(Codes.Inactive, "Your TechStrap access is turned off. Ask an admin to reactivate it.", ResultErrorKind.Forbidden);

    public static ResultError NotFound() =>
        new(Codes.NotFound, "That agent does not exist.", ResultErrorKind.NotFound);

    public static ResultError LastActiveAdmin() =>
        new(Codes.LastActiveAdmin, "TechStrap needs at least one active admin. Make sure another admin is active before turning this one off.", ResultErrorKind.Conflict);
}
```

`src/TechStrap.Application/Agents/CurrentAgent.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

/// <summary>
/// Resolves the acting agent for handlers that act as them (audit actor, own profile). The Agent/Admin policies already refuse
/// deactivated agents at the host; this repeats the check because Application must not rely on the transport.
/// </summary>
internal static class CurrentAgent
{
    public static async Task<Result<Agent>> RequireActiveAsync(ICurrentAgentClaims currentAgent, IAgentRepository agents, CancellationToken cancellationToken)
    {
        if (currentAgent.Current is not { } claims)
        {
            return Result<Agent>.Failure(AgentErrors.AccessRequired());
        }

        var agent = await agents.GetBySubjectAsync(claims.Subject, cancellationToken);
        if (agent is null)
        {
            return Result<Agent>.Failure(AgentErrors.NotProvisioned());
        }

        return agent.IsActive ? Result<Agent>.Success(agent) : Result<Agent>.Failure(AgentErrors.Inactive());
    }
}
```

`src/TechStrap.Application/Auditing/AdminAudit.cs`:

```csharp
using System.Text.Json;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Auditing;

/// <summary>
/// Stages an AdminEvent in the caller's unit of work (D-006, D-022). Payloads are small anonymous objects serialized as camelCase JSON.
/// AdminEvent.Record rejects secrets and personal data; a rejected payload is a bug in the calling handler, so it throws.
/// </summary>
internal static class AdminAudit
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Record(
        IAdminEventRepository events,
        AdminEventType type,
        Agent actor,
        AdminSubjectType subjectType,
        Guid subjectId,
        object payload,
        TimeProvider clock)
    {
        var recorded = AdminEvent.Record(type, actor.Id, subjectType, subjectId, JsonSerializer.Serialize(payload, Json), clock);
        if (recorded.IsFailure)
        {
            throw new InvalidOperationException($"The {type} audit payload was rejected ({recorded.Error!.Code}); fix the handler's payload.");
        }

        events.Add(recorded.Value);
    }
}
```

Add `<ItemGroup><InternalsVisibleTo Include="TechStrap.Application.Tests" /></ItemGroup>` to `TechStrap.Application.csproj`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: PASS. Check that `DomainResult<T>.Error` is the right property name for the failure (`src/TechStrap.Domain/DomainResult.cs`) and adjust the name if it differs.

- [ ] **Step 5: Run the full build and the architecture tests, then commit**

Run: `dotnet build TechStrap.slnx -c Release` and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: 0 warnings and PASS. None of the new types ends in `Handler`, and they are all internal.

```bash
git add src/TechStrap.Application tests/TechStrap.Application.Tests
git commit -m "feat(application): acting-agent resolution, agent access errors and admin audit helper" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 5: `GET /api/agents/me`: provisioning, role mirroring and deactivated agents

**Files:**
- Create: `src/TechStrap.Application/Agents/AgentMapping.cs`
- Create: `src/TechStrap.Application/Agents/GetCurrentAgentRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/AgentsController.cs`
- Modify: `src/TechStrap.Api/Security/AgentAccessRequirement.cs` (refuse deactivated agents; add failure reasons)
- Create: `src/TechStrap.Api/Security/ProblemDetailsAuthorizationResultHandler.cs`
- Modify: `src/TechStrap.Api/Security/AgentAuthenticationSetup.cs` (register the result handler)
- Create: `tests/TechStrap.Application.Tests/Agents/GetCurrentAgentRequestHandlerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Controllers/ControllerTestContext.cs` (shared by later controller tests)
- Create: `tests/TechStrap.Api.Tests/Controllers/AgentsControllerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Auth/AgentProvisioningTests.cs`
- Create: `tests/TechStrap.Api.Tests/Auth/ApiTestDatabase.cs` (shared database helper: a migrated database plus SQL helpers)

**Interfaces:**
- Consumes: Task 3 (`ICurrentAgentClaims`, `AgentAccessRequirement`, `TestJwt`, `ApiFactory`, `AuthProbeController`) and Task 4 (`AgentErrors`, `UnitOfWorkSubstitute`).
- Produces:
  - `IGetCurrentAgentRequestHandler { Task<Result<AgentDto>> HandleAsync(CancellationToken cancellationToken); }`
  - `AgentMapping.ToDto(Agent) : AgentDto` and `AgentMapping.ToListItem(Agent, bool includeAdminFields) : AgentListItemDto` (internal)
  - `AgentsController` (route `api/agents`, `[Authorize(Policy = AuthorizationPolicies.Agent)]`) with `GetMe`
  - `ControllerTestContext.For<TController>() : TController` (a controller with `AddResultProblemDetails` services)
  - `ApiTestDatabase.CreateAsync(TestPostgres) : Task<ApiTestDatabase>`, which exposes `ConnectionString`, `Settings` and `ExecuteAsync(string sql)`/`ScalarAsync<T>(string sql)`

- [ ] **Step 1: Write the failing handler tests**

`tests/TechStrap.Application.Tests/Agents/GetCurrentAgentRequestHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

public sealed class GetCurrentAgentRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));

    private GetCurrentAgentRequestHandler Handler(IUnitOfWork? unitOfWork = null) =>
        new(_claims, _agents, unitOfWork ?? UnitOfWorkSubstitute.Create(), _clock);

    private void SignedIn(AgentRole role, string? email = "sam@example.com", string? name = "Sam Whitfield") =>
        _claims.Current.Returns(new AgentClaims("sub-1", name, email, role));

    [Fact]
    public async Task The_first_call_provisions_the_agent_with_the_group_role()
    {
        SignedIn(AgentRole.Admin);

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Email.ShouldBe("sam@example.com"),
            dto => dto.Name.ShouldBe("Sam Whitfield"),
            dto => dto.Role.ShouldBe(AgentRoles.Admin),
            dto => dto.IsActive.ShouldBeTrue(),
            dto => dto.LastSeenAt.ShouldBe(_clock.GetUtcNow()));
        _agents.Received(1).Add(Arg.Is<Agent>(agent => agent.OidcSubject == "sub-1" && agent.Role == AgentRole.Admin));
    }

    [Fact]
    public async Task A_later_call_refreshes_name_email_and_mirrors_the_current_group_role()
    {
        var existing = Agent.Create("sub-1", "Old Name", "old@example.com", AgentRole.Admin, _clock).Value;
        _agents.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(existing);
        SignedIn(AgentRole.Agent, email: "new@example.com", name: "New Name");

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Name.ShouldBe("New Name"),
            dto => dto.Email.ShouldBe("new@example.com"),
            dto => dto.Role.ShouldBe(AgentRoles.Agent));
        _agents.Received(1).Update(existing);
        _agents.DidNotReceive().Add(Arg.Any<Agent>());
    }

    [Fact]
    public async Task A_token_without_an_email_is_refused_with_a_clear_reason()
    {
        SignedIn(AgentRole.Agent, email: null);

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Forbidden),
            error => error.Code.ShouldBe("agent-email-required"),
            error => error.Message.ShouldContain("email claim"));
    }

    [Fact]
    public async Task An_over_long_identity_provider_name_is_shortened_instead_of_blocking_sign_in()
    {
        SignedIn(AgentRole.Agent, name: new string('n', 150));

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Value.Name!.Length.ShouldBe(100);
    }

    [Fact]
    public async Task An_unusable_email_is_forbidden_not_a_validation_error_the_agent_cannot_fix()
    {
        SignedIn(AgentRole.Agent, email: "not-an-email");

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Forbidden),
            error => error.Code.ShouldBe("agent-identity-invalid"));
    }

    [Fact]
    public async Task A_deactivated_agent_is_forbidden_and_nothing_is_saved()
    {
        var existing = Agent.Create("sub-1", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        existing.SetActive(false);
        _agents.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(existing);
        SignedIn(AgentRole.Agent);

        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        _agents.DidNotReceive().Update(Arg.Any<Agent>());
    }

    [Fact]
    public async Task A_concurrent_first_call_that_loses_the_insert_race_returns_the_winner_row()
    {
        var winner = Agent.Create("sub-1", "Sam", "sam@example.com", AgentRole.Agent, _clock).Value;
        _agents.GetBySubjectAsync("sub-1", Arg.Any<CancellationToken>()).Returns(null, winner);
        SignedIn(AgentRole.Agent);
        var unitOfWork = UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.Duplicate));

        var result = await Handler(unitOfWork).HandleAsync(TestContext.Current.CancellationToken);

        result.Value.Id.ShouldBe(winner.Id);
    }

    [Fact]
    public async Task Without_agent_claims_the_call_is_forbidden()
    {
        var result = await Handler().HandleAsync(TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-access-required");
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: a build failure, because `GetCurrentAgentRequestHandler` does not exist.

- [ ] **Step 3: Implement the mapping and the handler**

`src/TechStrap.Application/Agents/AgentMapping.cs`:

```csharp
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

internal static class AgentMapping
{
    public static string RoleName(AgentRole role) => role == AgentRole.Admin ? AgentRoles.Admin : AgentRoles.Agent;

    public static AgentDto ToDto(Agent agent) =>
        new(agent.Id, agent.Name, agent.Email, RoleName(agent.Role), agent.IsActive, agent.PublicDisplayName, agent.LastSeenAt);

    /// <summary>Agents get only what assignment needs; Admins also get email, role, status and last seen (D-022).</summary>
    public static AgentListItemDto ToListItem(Agent agent, bool includeAdminFields) =>
        includeAdminFields
            ? new(agent.Id, agent.Name, agent.Name ?? agent.Email, agent.Email, RoleName(agent.Role), agent.IsActive, agent.LastSeenAt)
            : new(agent.Id, agent.Name, agent.Name ?? agent.Email, null, null, null, null);
}
```

`src/TechStrap.Application/Agents/GetCurrentAgentRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Agents;
using TechStrap.Domain;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Agents;

public interface IGetCurrentAgentRequestHandler
{
    Task<Result<AgentDto>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/agents/me (D-004, D-029). The first call provisions the agent; every call refreshes name and email from the token,
/// mirrors the group-derived role and records the sign-in time. Two first calls racing each other create one row: the loser's
/// insert fails as a duplicate and it retries once, finding the winner's row.
/// </summary>
public sealed class GetCurrentAgentRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IGetCurrentAgentRequestHandler
{
    private const int MaxAttempts = 2;

    public async Task<Result<AgentDto>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentAgent.Current is not { } claims)
        {
            return Result<AgentDto>.Failure(AgentErrors.AccessRequired());
        }

        if (string.IsNullOrWhiteSpace(claims.Email))
        {
            return Result<AgentDto>.Failure(AgentErrors.EmailRequired());
        }

        var name = Shorten(claims.Name);
        for (var attempt = 1; ; attempt++)
        {
            await using var scope = await unitOfWork.BeginAsync(cancellationToken);
            var agent = await agents.GetBySubjectAsync(claims.Subject, cancellationToken);
            if (agent is null)
            {
                var created = Agent.Create(claims.Subject, name, claims.Email, claims.Role, clock);
                if (created.IsFailure)
                {
                    return Result<AgentDto>.Failure(AgentErrors.IdentityInvalid(created.Error!.Message));
                }

                agent = created.Value;
                agent.RecordSeen(clock);
                agents.Add(agent);
            }
            else
            {
                if (!agent.IsActive)
                {
                    return Result<AgentDto>.Failure(AgentErrors.Inactive());
                }

                var refreshed = agent.UpdateIdentity(name, claims.Email);
                if (refreshed.IsFailure)
                {
                    return Result<AgentDto>.Failure(AgentErrors.IdentityInvalid(refreshed.Error!.Message));
                }

                agent.ChangeRole(claims.Role);
                agent.RecordSeen(clock);
                agents.Update(agent);
            }

            var committed = await scope.CommitAsync(cancellationToken);
            if (committed.IsSuccess)
            {
                return Result<AgentDto>.Success(AgentMapping.ToDto(agent));
            }

            if (attempt < MaxAttempts && committed.Errors[0].Code == PersistenceErrorCodes.Duplicate)
            {
                continue;
            }

            return Result<AgentDto>.Failure(committed.Errors[0]);
        }
    }

    private static string? Shorten(string? name)
    {
        var text = name?.Trim();
        return text is { Length: > DomainLimits.NameMaxLength } ? text[..DomainLimits.NameMaxLength] : text;
    }
}
```

Remove any `using` that the build reports as unnecessary, for example `TechStrap.Domain` or `TechStrap.Application.Results` if unused. Style warnings fail the build.

- [ ] **Step 4: Run the handler tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: PASS.

- [ ] **Step 5: Write the failing controller and integration tests**

`tests/TechStrap.Api.Tests/Controllers/ControllerTestContext.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.AspNetCore.Common;

namespace TechStrap.Api.Tests.Controllers;

/// <summary>Creates a controller with just enough request services for ToActionResult's ProblemDetails mapping.</summary>
public static class ControllerTestContext
{
    public static TController For<TController>()
        where TController : ControllerBase, new()
    {
        var services = new ServiceCollection().AddLogging().AddResultProblemDetails().BuildServiceProvider();
        return new TController
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } },
        };
    }
}
```

`tests/TechStrap.Api.Tests/Controllers/AgentsControllerTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Api.Controllers;
using TechStrap.Application.Agents;
using TechStrap.Contracts.Agents;

namespace TechStrap.Api.Tests.Controllers;

public sealed class AgentsControllerTests
{
    private static readonly AgentDto Me = new(Guid.CreateVersion7(), "Sam", "sam@example.com", AgentRoles.Agent, true, null, null);

    [Fact]
    public async Task GetMe_DelegatesAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<IGetCurrentAgentRequestHandler>();
        handler.HandleAsync(cancellation.Token).Returns(Result<AgentDto>.Success(Me));

        var result = await ControllerTestContext.For<AgentsController>().GetMe(handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Me);
        await handler.Received(1).HandleAsync(cancellation.Token);
    }
}
```

`tests/TechStrap.Api.Tests/Auth/ApiTestDatabase.cs`:

```csharp
using Npgsql;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests.Auth;

/// <summary>A fresh database that the Api migrates on start, plus small SQL helpers for arranging and asserting rows.</summary>
public sealed class ApiTestDatabase
{
    private ApiTestDatabase(string connectionString) => ConnectionString = connectionString;

    public string ConnectionString { get; }

    public IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["ConnectionStrings:TechStrap"] = ConnectionString,
        [ApiStartupTasks.MigrateOnStartupKey] = "true",
    };

    public static async Task<ApiTestDatabase> CreateAsync(TestPostgres postgres) => new(await postgres.CreateDatabaseAsync());

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }
}
```

Npgsql reaches Api.Tests transitively, through Api → Infrastructure. If the build cannot resolve `NpgsqlConnection`, use the namespace that `TestPostgres.cs` already uses.

`tests/TechStrap.Api.Tests/Auth/AgentProvisioningTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Agents;

namespace TechStrap.Api.Tests.Auth;

public sealed class AgentProvisioningTests(TestPostgres postgres)
{
    private static ApiFactory Factory(ApiTestDatabase database) =>
        new(settings: database.Settings, configureServices: services => services.AddControllers().AddApplicationPart(typeof(AuthProbeController).Assembly));

    [Fact]
    public async Task The_first_call_creates_the_agent_and_the_next_call_reuses_it()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = Factory(database);
        using var client = factory.CreateClient().Bearer(TestJwt.Token("sub-1", [TestJwt.AdminGroup], email: "sam@example.com", name: "Sam Whitfield"));

        var first = await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken);
        var second = await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken);

        first!.Role.ShouldBe(AgentRoles.Admin);
        second!.Id.ShouldBe(first.Id);
        (await database.ScalarAsync<long>("SELECT count(*) FROM agents")).ShouldBe(1);
    }

    [Fact]
    public async Task Two_concurrent_first_calls_create_one_agent()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = Factory(database);
        var token = TestJwt.Token("sub-race", [TestJwt.AgentGroup], email: "race@example.com");

        async Task<HttpStatusCode> CallAsync()
        {
            using var client = factory.CreateClient().Bearer(token);
            using var response = await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken);
            return response.StatusCode;
        }

        var statuses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CallAsync()));

        statuses.ShouldAllBe(status => status == HttpStatusCode.OK);
        (await database.ScalarAsync<long>("SELECT count(*) FROM agents WHERE oidc_subject = 'sub-race'")).ShouldBe(1);
    }

    [Fact]
    public async Task A_deactivated_agent_is_refused_on_every_agent_endpoint()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = Factory(database);
        using var client = factory.CreateClient().Bearer(TestJwt.Token("sub-off", [TestJwt.AdminGroup], email: "off@example.com"));
        (await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await database.ExecuteAsync("UPDATE agents SET is_active = false WHERE oidc_subject = 'sub-off'");

        foreach (var path in new[] { "/api/agents/me", "/__test/agent", "/__test/admin" })
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("agent-inactive", customMessage: path);
        }
    }
}
```

Task 13 widens this test to every route in the Api.

- [ ] **Step 6: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release`
Expected: a build failure, because `TechStrap.Api.Controllers.AgentsController` does not exist yet.

- [ ] **Step 7: Implement the controller, the deactivated-agent check and the problem details for refusals**

`src/TechStrap.Api/Controllers/AgentsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Agents;

namespace TechStrap.Api.Controllers;

/// <summary>Agent profile, list and access management (PHASE-04). Every action delegates to one named handler.</summary>
[ApiController]
[Route("api/agents")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class AgentsController : ControllerBase
{
    /// <summary>The signed-in agent; provisions them on first call.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetMe([FromServices] IGetCurrentAgentRequestHandler getCurrentAgent, CancellationToken cancellationToken) =>
        (await getCurrentAgent.HandleAsync(cancellationToken)).ToActionResult(this, Ok);
}
```

Replace `src/TechStrap.Api/Security/AgentAccessRequirement.cs` with:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TechStrap.Api.Options;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;

namespace TechStrap.Api.Security;

/// <summary>The caller must be in the agent or admin group; <see cref="AdminOnly"/> requires the admin group (D-029).</summary>
public sealed class AgentAccessRequirement(bool adminOnly) : IAuthorizationRequirement
{
    public bool AdminOnly { get; } = adminOnly;
}

/// <summary>
/// Host-level policy check: group membership from the token, and a stored agent row that is not deactivated. A deactivated agent
/// is refused on every agent endpoint even though their token is still valid (D-029). Agents without a row yet pass; only
/// GET /api/agents/me provisions, and handlers that need the row ask the agent to open TechStrap first.
/// </summary>
public sealed class AgentAccessAuthorizationHandler(IOptions<AgentAccessOptions> options, IAgentRepository agents) : AuthorizationHandler<AgentAccessRequirement>
{
    public const string AccessRequired = "agent-access-required";
    public const string AdminRequired = "admin-access-required";
    public const string Inactive = "agent-inactive";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AgentAccessRequirement requirement)
    {
        var claims = ClaimsCurrentAgentClaims.FromPrincipal(context.User, options.Value);
        if (claims is null)
        {
            context.Fail(new AuthorizationFailureReason(this, AccessRequired));
            return;
        }

        if (requirement.AdminOnly && claims.Role != AgentRole.Admin)
        {
            context.Fail(new AuthorizationFailureReason(this, AdminRequired));
            return;
        }

        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;
        if (await agents.GetBySubjectAsync(claims.Subject, cancellationToken) is { IsActive: false })
        {
            context.Fail(new AuthorizationFailureReason(this, Inactive));
            return;
        }

        context.Succeed(requirement);
    }
}
```

`src/TechStrap.Api/Security/ProblemDetailsAuthorizationResultHandler.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace TechStrap.Api.Security;

/// <summary>
/// Turns a refusal from AgentAccessAuthorizationHandler into a 403 problem with a stable type code, so the Admin app can tell
/// "not in the group" from "deactivated". Other outcomes keep the framework behavior.
/// </summary>
public sealed class ProblemDetailsAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly Dictionary<string, string> Details = new(StringComparer.Ordinal)
    {
        [AgentAccessAuthorizationHandler.AccessRequired] = "Your account is not in the TechStrap agent or admin group. Ask your identity provider administrator to add you.",
        [AgentAccessAuthorizationHandler.AdminRequired] = "This needs the TechStrap admin group. Ask an admin to make the change, or to add you to the group.",
        [AgentAccessAuthorizationHandler.Inactive] = "Your TechStrap access is turned off. Ask an admin to reactivate it.",
    };

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        var reason = authorizeResult.AuthorizationFailure?.FailureReasons
            .FirstOrDefault(failure => failure.Handler is AgentAccessAuthorizationHandler);
        if (authorizeResult.Forbidden && reason is not null && Details.TryGetValue(reason.Message, out var detail))
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: detail, type: reason.Message)
                .ExecuteAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
```

In `AgentAuthenticationSetup.AddAgentAuthentication`, after the `IAuthorizationHandler` registration, add:

```csharp
services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationResultHandler>();
```

Add `using Microsoft.AspNetCore.Authorization;` there if it is not already present.

- [ ] **Step 8: Run all affected tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release` and `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: PASS, including the Task 3 `AgentAuthTests`. They run against a migrated database, so the new repository lookup works.

- [ ] **Step 9: Run the full build and the architecture tests, then commit**

Run: `dotnet build TechStrap.slnx -c Release` and `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: 0 warnings and PASS. `HandlerShapeTests` and `ControllerHandlerInjectionTests` are now non-vacuous.

```bash
git add src tests
git commit -m "feat(agents): provision the signed-in agent and refuse deactivated agents everywhere" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 6: List agents, activate and deactivate (last-admin guard)

**Files:**
- Modify: `src/TechStrap.Application/Persistence/IAgentRepository.cs` (add `CountActiveAdminsLockedAsync`)
- Modify: `src/TechStrap.Infrastructure/Persistence/Repositories/AgentRepository.cs`
- Create: `src/TechStrap.Application/Agents/ListAgentsRequestHandler.cs`
- Create: `src/TechStrap.Application/Agents/UpdateAgentRequestHandler.cs`
- Modify: `src/TechStrap.Api/Controllers/AgentsController.cs` (add `List` and `Update`)
- Create: `tests/TechStrap.Application.Tests/Agents/ListAgentsRequestHandlerTests.cs`
- Create: `tests/TechStrap.Application.Tests/Agents/UpdateAgentRequestHandlerTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/AgentAdminLockTests.cs`
- Modify: `tests/TechStrap.Api.Tests/Controllers/AgentsControllerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Agents/AgentManagementEndpointTests.cs`

**Interfaces:**
- Consumes: `CurrentAgent`, `AgentErrors`, `AdminAudit`, `AgentMapping` (Tasks 4–5); `UnitOfWorkSubstitute`, `ControllerTestContext`, `ApiTestDatabase`, `TestJwt` (Tasks 3–5).
- Produces:
  - `IAgentRepository.CountActiveAdminsLockedAsync(CancellationToken) : Task<int>`. It must run inside a unit of work, and it locks the active admin rows until that unit of work ends.
  - `IListAgentsRequestHandler { Task<Result<PagedResponse<AgentListItemDto>>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken); }`
  - `IUpdateAgentRequestHandler { Task<Result<AgentDto>> HandleAsync(Guid agentId, UpdateAgentRequest request, CancellationToken cancellationToken); }`
  - Routes:
    - `GET /api/agents?page=&pageSize=` (Agent)
    - `PUT /api/agents/{id:guid}` (Admin)

- [ ] **Step 1: Write the failing repository test**

Add this file to `tests/TechStrap.Infrastructure.IntegrationTests/AgentAdminLockTests.cs`. Follow the style of the existing `AgentRepositoryTests`: `PostgresIntegrationTestBase`, `PersistenceTestHost` and `FakeTimeProvider`. Read that file first and copy its constructor and helper shape.

```csharp
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class AgentAdminLockTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    [Fact]
    public async Task Counting_locked_admins_needs_a_unit_of_work()
    {
        await using var host = new PersistenceTestHost(Database);
        await using var scope = host.CreateScope();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IAgentRepository>().CountActiveAdminsLockedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Only_active_admins_are_counted()
    {
        await using var host = new PersistenceTestHost(Database);
        (await host.CommitAsync(provider =>
        {
            var agents = provider.GetRequiredService<IAgentRepository>();
            agents.Add(Agent.Create("a1", "A1", "a1@example.com", AgentRole.Admin, host.Clock).Value);
            var inactive = Agent.Create("a2", "A2", "a2@example.com", AgentRole.Admin, host.Clock).Value;
            inactive.SetActive(false);
            agents.Add(inactive);
            agents.Add(Agent.Create("a3", "A3", "a3@example.com", AgentRole.Agent, host.Clock).Value);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var count = 0;
        (await host.CommitAsync(async provider =>
            count = await provider.GetRequiredService<IAgentRepository>().CountActiveAdminsLockedAsync(TestContext.Current.CancellationToken))).IsSuccess.ShouldBeTrue();

        count.ShouldBe(1);
    }
}
```

`PersistenceTestHost` may expose its clock under a different name (for example `Clock`). Use the existing member and adjust the call.

- [ ] **Step 2: Write the failing handler tests**

`tests/TechStrap.Application.Tests/Agents/ListAgentsRequestHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

public sealed class ListAgentsRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly Agent _sam = Agent.Create("s", "Sam", "sam@example.com", AgentRole.Admin, new FakeTimeProvider()).Value;

    private ListAgentsRequestHandler Handler() => new(_claims, _agents);

    [Fact]
    public async Task An_agent_sees_active_agents_with_assignment_fields_only()
    {
        _claims.Current.Returns(new AgentClaims("s", "Sam", "sam@example.com", AgentRole.Agent));
        _agents.ListAsync(true, 2, 10, Arg.Any<CancellationToken>()).Returns(new PagedResult<Agent>([_sam], 2, 10, 11));

        var page = (await Handler().HandleAsync(2, 10, TestContext.Current.CancellationToken)).Value;

        page.ShouldSatisfyAllConditions(
            p => p.Page.ShouldBe(2),
            p => p.TotalCount.ShouldBe(11),
            p => p.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
                item => item.DisplayLabel.ShouldBe("Sam"),
                item => item.Email.ShouldBeNull(),
                item => item.Role.ShouldBeNull(),
                item => item.IsActive.ShouldBeNull()));
    }

    [Fact]
    public async Task An_admin_sees_every_agent_with_email_role_and_status()
    {
        _claims.Current.Returns(new AgentClaims("s", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.ListAsync(false, 1, 25, Arg.Any<CancellationToken>()).Returns(new PagedResult<Agent>([_sam], 1, 25, 1));

        var item = (await Handler().HandleAsync(1, 25, TestContext.Current.CancellationToken)).Value.Items.ShouldHaveSingleItem();

        item.Email.ShouldBe("sam@example.com");
        item.Role.ShouldBe("Admin");
        item.IsActive.ShouldBe(true);
    }
}
```

`tests/TechStrap.Application.Tests/Agents/UpdateAgentRequestHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

public sealed class UpdateAgentRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _actor;

    public UpdateAgentRequestHandlerTests()
    {
        _actor = Agent.Create("actor", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("actor", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("actor", Arg.Any<CancellationToken>()).Returns(_actor);
    }

    private UpdateAgentRequestHandler Handler() => new(_claims, _agents, _events, UnitOfWorkSubstitute.Create(), _clock);

    private Agent Target(AgentRole role, bool active = true)
    {
        var agent = Agent.Create("target", "Riley", "riley@example.com", role, _clock).Value;
        agent.SetActive(active);
        _agents.GetByIdAsync(agent.Id, Arg.Any<CancellationToken>()).Returns(agent);
        return agent;
    }

    [Fact]
    public async Task Deactivating_an_agent_saves_and_audits_it()
    {
        var target = Target(AgentRole.Agent);
        _agents.CountActiveAdminsLockedAsync(Arg.Any<CancellationToken>()).Returns(1);

        var result = await Handler().HandleAsync(target.Id, new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        result.Value.IsActive.ShouldBeFalse();
        _agents.Received(1).Update(target);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.AgentUpdated && e.SubjectId == target.Id && e.ActorId == _actor.Id && e.PayloadJson == "{\"isActive\":false}"));
    }

    [Fact]
    public async Task Reactivating_an_agent_saves_and_audits_it()
    {
        var target = Target(AgentRole.Agent, active: false);

        var result = await Handler().HandleAsync(target.Id, new UpdateAgentRequest(true), TestContext.Current.CancellationToken);

        result.Value.IsActive.ShouldBeTrue();
        _events.Received(1).Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_deactivated()
    {
        var target = Target(AgentRole.Admin);
        _agents.CountActiveAdminsLockedAsync(Arg.Any<CancellationToken>()).Returns(1);

        var result = await Handler().HandleAsync(target.Id, new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("last-active-admin"));
        _agents.DidNotReceive().Update(Arg.Any<Agent>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task An_admin_can_be_deactivated_while_another_admin_stays_active()
    {
        var target = Target(AgentRole.Admin);
        _agents.CountActiveAdminsLockedAsync(Arg.Any<CancellationToken>()).Returns(2);

        (await Handler().HandleAsync(target.Id, new UpdateAgentRequest(false), TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Setting_the_current_state_again_changes_nothing_and_is_not_audited()
    {
        var target = Target(AgentRole.Agent);

        var result = await Handler().HandleAsync(target.Id, new UpdateAgentRequest(true), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _agents.DidNotReceive().Update(Arg.Any<Agent>());
        _events.DidNotReceive().Add(Arg.Any<AdminEvent>());
    }

    [Fact]
    public async Task An_unknown_agent_is_not_found()
    {
        var result = await Handler().HandleAsync(Guid.CreateVersion7(), new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }

    [Fact]
    public async Task An_actor_without_an_agent_row_is_asked_to_open_techstrap_first()
    {
        _agents.GetBySubjectAsync("actor", Arg.Any<CancellationToken>()).Returns((Agent?)null);

        var result = await Handler().HandleAsync(Guid.CreateVersion7(), new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-not-provisioned");
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release` and `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release`
Expected: the build fails, because the handlers and `CountActiveAdminsLockedAsync` are missing.

- [ ] **Step 4: Add the locked count**

In `IAgentRepository`, after `CountActiveAdminsAsync`, add:

```csharp
    /// <summary>
    /// Counts active admins and locks their rows until the current unit of work ends, so two concurrent deactivations cannot both
    /// see "another admin is still active" (D-029). Must run inside an IUnitOfWork scope.
    /// </summary>
    Task<int> CountActiveAdminsLockedAsync(CancellationToken cancellationToken);
```

In `AgentRepository`:

```csharp
    public Task<int> CountActiveAdminsLockedAsync(CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Counting locked admins must run inside an IUnitOfWork scope.");
        }

        // FOR UPDATE cannot sit next to an aggregate; EF wraps the raw query as a subquery: SELECT count(*) FROM (... FOR UPDATE).
        return context.Set<AgentRecord>()
            .FromSqlRaw("SELECT * FROM agents WHERE role = 'Admin' AND is_active FOR UPDATE")
            .CountAsync(cancellationToken);
    }
```

Check the stored values with `SELECT DISTINCT role FROM agents` against a seeded database, or read `AgentRecordConfiguration`, and confirm that `role` is stored as the text `'Admin'` (`HasEnumAsString`). If it is stored differently, adjust the literal.

- [ ] **Step 5: Implement the handlers**

`src/TechStrap.Application/Agents/ListAgentsRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

public interface IListAgentsRequestHandler
{
    Task<Result<PagedResponse<AgentListItemDto>>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>GET /api/agents. Agents get active agents for assignment; Admins get everyone with role and status (D-022).</summary>
public sealed class ListAgentsRequestHandler(ICurrentAgentClaims currentAgent, IAgentRepository agents) : IListAgentsRequestHandler
{
    public async Task<Result<PagedResponse<AgentListItemDto>>> HandleAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        if (currentAgent.Current is not { } claims)
        {
            return Result<PagedResponse<AgentListItemDto>>.Failure(AgentErrors.AccessRequired());
        }

        var isAdmin = claims.Role == AgentRole.Admin;
        var found = await agents.ListAsync(activeOnly: !isAdmin, page, pageSize, cancellationToken);
        return Result<PagedResponse<AgentListItemDto>>.Success(new PagedResponse<AgentListItemDto>(
            [.. found.Items.Select(agent => AgentMapping.ToListItem(agent, includeAdminFields: isAdmin))],
            found.Page,
            found.PageSize,
            found.TotalCount));
    }
}
```

`src/TechStrap.Application/Agents/UpdateAgentRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

public interface IUpdateAgentRequestHandler
{
    Task<Result<AgentDto>> HandleAsync(Guid agentId, UpdateAgentRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// PUT /api/agents/{id} (Admin, D-022). Activates or deactivates an agent; roles come from IdP groups and cannot change here (D-029).
/// The last active admin cannot be deactivated: the active admin rows are locked first, so concurrent deactivations queue.
/// </summary>
public sealed class UpdateAgentRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IUpdateAgentRequestHandler
{
    public async Task<Result<AgentDto>> HandleAsync(Guid agentId, UpdateAgentRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<AgentDto>.Failure(actor.Errors[0]);
        }

        var activeAdmins = request.IsActive ? 0 : await agents.CountActiveAdminsLockedAsync(cancellationToken);
        var agent = await agents.GetByIdAsync(agentId, cancellationToken);
        if (agent is null)
        {
            return Result<AgentDto>.Failure(AgentErrors.NotFound());
        }

        if (agent.IsActive == request.IsActive)
        {
            return Result<AgentDto>.Success(AgentMapping.ToDto(agent));
        }

        if (!request.IsActive && agent.Role == AgentRole.Admin && activeAdmins <= 1)
        {
            return Result<AgentDto>.Failure(AgentErrors.LastActiveAdmin());
        }

        agent.SetActive(request.IsActive);
        agents.Update(agent);
        AdminAudit.Record(adminEvents, AdminEventType.AgentUpdated, actor.Value, AdminSubjectType.Agent, agent.Id, new { isActive = request.IsActive }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        return committed.IsSuccess ? Result<AgentDto>.Success(AgentMapping.ToDto(agent)) : Result<AgentDto>.Failure(committed.Errors[0]);
    }
}
```

- [ ] **Step 6: Add the controller actions**

Add these two actions to `AgentsController`. Add the `using`s for `TechStrap.Application.Persistence` (for `Paging`) and `TechStrap.Contracts.Agents`.

```csharp
    /// <summary>Agents: active agents for assignment. Admins: everyone, with role and status.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromServices] IListAgentsRequestHandler listAgents,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await listAgents.HandleAsync(page, pageSize, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Activates or deactivates an agent (Admin). Roles come from IdP groups (D-029).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateAgentRequest request,
        [FromServices] IUpdateAgentRequestHandler updateAgent,
        CancellationToken cancellationToken) =>
        (await updateAgent.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);
```

Optional parameters must come after the `CancellationToken`. If the build complains about the parameter order (CA1068: the `CancellationToken` must be last), make `page` and `pageSize` required `[FromQuery]` parameters with no defaults, put the `CancellationToken` last, and default them in the handler instead: a value of 0 or less becomes 1 or `Paging.DefaultPageSize`, through `Paging.NormalizePage` and `Paging.NormalizePageSize`. Whichever way you choose, use the same pattern for every paged action in this phase.

- [ ] **Step 7: Write the controller test and the endpoint tests (race included)**

Append to `AgentsControllerTests`:

```csharp
    [Fact]
    public async Task Update_DelegatesTheIdAndBodyAndPassesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var id = Guid.CreateVersion7();
        var request = new UpdateAgentRequest(false);
        var handler = Substitute.For<IUpdateAgentRequestHandler>();
        handler.HandleAsync(id, request, cancellation.Token).Returns(Result<AgentDto>.Success(Me));

        var result = await ControllerTestContext.For<AgentsController>().Update(id, request, handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>();
        await handler.Received(1).HandleAsync(id, request, cancellation.Token);
    }
```

`tests/TechStrap.Api.Tests/Agents/AgentManagementEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Paging;

namespace TechStrap.Api.Tests.Agents;

public sealed class AgentManagementEndpointTests(TestPostgres postgres)
{
    private static async Task<AgentDto> SignInAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task An_agent_cannot_change_another_agent()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        var me = await SignInAsync(agent);

        using var response = await agent.PutAsJsonAsync($"/api/agents/{me.Id}", new UpdateAgentRequest(false), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_sees_only_active_agents_without_admin_fields()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        using var other = factory.CreateClient().Bearer(TestJwt.Token("other", [TestJwt.AgentGroup], email: "other@example.com"));
        await SignInAsync(admin);
        await SignInAsync(agent);
        var otherMe = await SignInAsync(other);
        (await admin.PutAsJsonAsync($"/api/agents/{otherMe.Id}", new UpdateAgentRequest(false), TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var page = (await agent.GetFromJsonAsync<PagedResponse<AgentListItemDto>>("/api/agents", TestContext.Current.CancellationToken))!;

        page.Items.Select(item => item.Id).ShouldNotContain(otherMe.Id);
        page.Items.ShouldAllBe(item => item.Email == null && item.Role == null);
    }

    [Fact]
    public async Task Two_admins_deactivating_each_other_at_once_leave_one_active_admin()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var first = factory.CreateClient().Bearer(TestJwt.Token("admin-1", [TestJwt.AdminGroup], email: "one@example.com"));
        using var second = factory.CreateClient().Bearer(TestJwt.Token("admin-2", [TestJwt.AdminGroup], email: "two@example.com"));
        var one = await SignInAsync(first);
        var two = await SignInAsync(second);

        var responses = await Task.WhenAll(
            first.PutAsJsonAsync($"/api/agents/{two.Id}", new UpdateAgentRequest(false), TestContext.Current.CancellationToken),
            second.PutAsJsonAsync($"/api/agents/{one.Id}", new UpdateAgentRequest(false), TestContext.Current.CancellationToken));

        responses.Select(response => response.StatusCode).OrderBy(status => status)
            .ShouldBe([HttpStatusCode.OK, HttpStatusCode.Forbidden], ignoreOrder: true, customMessage: "one wins; the loser is now deactivated and refused, or gets 409");
        (await database.ScalarAsync<long>("SELECT count(*) FROM agents WHERE role = 'Admin' AND is_active")).ShouldBe(1);
    }
}
```

There are two valid outcomes for the losing request in the race test. If the loser's authorization check runs after the winner commits, the policy refuses the now-deactivated loser with **403**. If both requests pass authorization first, the lock makes the loser see one admin left, and it gets **409**. The invariant is that exactly one admin stays active and exactly one request succeeds. Assert exactly that:

```csharp
        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Single(response => response.StatusCode != HttpStatusCode.OK).StatusCode
            .ShouldBeOneOf(HttpStatusCode.Conflict, HttpStatusCode.Forbidden);
```

This replaces the `OrderBy(...).ShouldBe(...)` assertion above. Keep the final count assertion. Dispose the responses: wrap them in `using` or dispose them in a loop.

- [ ] **Step 8: Run all tests and confirm they pass**

Run:
- `dotnet test --project tests/TechStrap.Application.Tests -c Release`
- `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release`
- `dotnet test --project tests/TechStrap.Api.Tests -c Release`

Expected: PASS. Run the race test 5 times (`--filter Two_admins_deactivating` in a loop) and confirm it never fails.

- [ ] **Step 9: Build and commit**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: 0 warnings.

```bash
git add src tests
git commit -m "feat(agents): list agents and activate or deactivate them with a last-admin lock" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 7: Own notification preferences and public display name

**Files:**
- Create: `src/TechStrap.Application/Agents/GetMyNotificationPreferencesRequestHandler.cs`
- Create: `src/TechStrap.Application/Agents/UpdateNotificationPreferencesRequestHandler.cs`
- Create: `src/TechStrap.Application/Agents/UpdateMyProfileRequestHandler.cs`
- Modify: `src/TechStrap.Api/Controllers/AgentsController.cs`
- Create: `tests/TechStrap.Application.Tests/Agents/GetMyNotificationPreferencesRequestHandlerTests.cs`
- Create: `tests/TechStrap.Application.Tests/Agents/UpdateNotificationPreferencesRequestHandlerTests.cs`
- Create: `tests/TechStrap.Application.Tests/Agents/UpdateMyProfileRequestHandlerTests.cs`
- Modify: `tests/TechStrap.Api.Tests/Controllers/AgentsControllerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Agents/AgentSelfServiceEndpointTests.cs`

**Interfaces:**
- Consumes: `CurrentAgent`, `AgentErrors`, `UnitOfWorkSubstitute`; `IProductRepository.ListAsync(bool activeOnly, CancellationToken)` and `GetByIdAsync`; `IAgentRepository.ListNotificationPreferencesAsync` and `SetNotificationPreferenceAsync`; `Agent.SetPublicDisplayName`.
- Produces:
  - `IGetMyNotificationPreferencesRequestHandler { Task<Result<IReadOnlyList<NotificationPreferenceDto>>> HandleAsync(CancellationToken cancellationToken); }`
  - `IUpdateNotificationPreferencesRequestHandler { Task<Result> HandleAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken); }`
  - `IUpdateMyProfileRequestHandler { Task<Result> HandleAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken); }`
  - Routes, all Agent:
    - `GET /api/agents/me/notification-preferences`
    - `PUT /api/agents/me/notification-preferences`, which returns 204
    - `PUT /api/agents/me/profile`, which returns 204

- [ ] **Step 1: Write the failing handler tests**

`tests/TechStrap.Application.Tests/Agents/UpdateMyProfileRequestHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Tests.Agents;

public sealed class UpdateMyProfileRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly Agent _me = Agent.Create("me", "Riley Chen", "riley@example.com", AgentRole.Agent, new FakeTimeProvider()).Value;

    public UpdateMyProfileRequestHandlerTests()
    {
        _claims.Current.Returns(new AgentClaims("me", "Riley Chen", "riley@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("me", Arg.Any<CancellationToken>()).Returns(_me);
    }

    private UpdateMyProfileRequestHandler Handler() => new(_claims, _agents, UnitOfWorkSubstitute.Create());

    [Theory]
    [InlineData("Ry", "Ry")]
    [InlineData("  Ry  ", "Ry")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public async Task Setting_changing_and_clearing_updates_only_the_callers_record(string? value, string? stored)
    {
        _me.SetPublicDisplayName("Before");

        var result = await Handler().HandleAsync(new UpdateMyProfileRequest(value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        _me.PublicDisplayName.ShouldBe(stored);
        _agents.Received(1).Update(_me);
    }

    [Theory]
    [InlineData("ry@example.com", "public-display-name-invalid")]
    [InlineData("This display name is far too long to fit on a ticket reply line at all", "public-display-name-too-long")]
    public async Task Invalid_names_are_field_errors(string value, string code)
    {
        var result = await Handler().HandleAsync(new UpdateMyProfileRequest(value), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe(code),
            error => error.Target.ShouldBe("public-display-name"));
        _agents.DidNotReceive().Update(Arg.Any<Agent>());
    }

    [Fact]
    public async Task A_deactivated_agent_cannot_change_their_profile()
    {
        _me.SetActive(false);

        (await Handler().HandleAsync(new UpdateMyProfileRequest("Ry"), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
    }
}
```

`tests/TechStrap.Application.Tests/Agents/UpdateNotificationPreferencesRequestHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Agents;

public sealed class UpdateNotificationPreferencesRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly Agent _me = Agent.Create("me", "Riley", "riley@example.com", AgentRole.Agent, new FakeTimeProvider()).Value;
    private readonly Product _orbitly = Product.Create("orbitly", "Orbitly", "ORB", null, new FakeTimeProvider()).Value;

    public UpdateNotificationPreferencesRequestHandlerTests()
    {
        _claims.Current.Returns(new AgentClaims("me", "Riley", "riley@example.com", AgentRole.Agent));
        _agents.GetBySubjectAsync("me", Arg.Any<CancellationToken>()).Returns(_me);
        _products.GetByIdAsync(_orbitly.Id, Arg.Any<CancellationToken>()).Returns(_orbitly);
    }

    private UpdateNotificationPreferencesRequestHandler Handler() => new(_claims, _agents, _products, UnitOfWorkSubstitute.Create());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Enabling_and_disabling_stage_the_preference_for_the_caller(bool notify)
    {
        var result = await Handler().HandleAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(_orbitly.Id, notify)]), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _agents.Received(1).SetNotificationPreferenceAsync(new AgentNotificationPreference(_me.Id, _orbitly.Id, notify), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unknown_product_is_a_field_error_and_nothing_is_saved()
    {
        var result = await Handler().HandleAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(_orbitly.Id, true), new NotificationPreferenceUpdateDto(Guid.CreateVersion7(), true)]),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("notification-product-unknown"),
            error => error.Target.ShouldBe("preferences[1].productId"));
        await _agents.DidNotReceive().SetNotificationPreferenceAsync(Arg.Any<AgentNotificationPreference>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_same_product_twice_is_a_field_error()
    {
        var result = await Handler().HandleAsync(
            new UpdateNotificationPreferencesRequest([new NotificationPreferenceUpdateDto(_orbitly.Id, true), new NotificationPreferenceUpdateDto(_orbitly.Id, false)]),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("notification-product-repeated");
    }

    [Fact]
    public async Task A_missing_list_is_a_field_error()
    {
        (await Handler().HandleAsync(new UpdateNotificationPreferencesRequest(null!), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Target.ShouldBe("preferences");
    }
}
```

`tests/TechStrap.Application.Tests/Agents/GetMyNotificationPreferencesRequestHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Agents;

public sealed class GetMyNotificationPreferencesRequestHandlerTests
{
    [Fact]
    public async Task Every_active_product_is_listed_and_unset_products_default_to_off()
    {
        var claims = Substitute.For<ICurrentAgentClaims>();
        var agents = Substitute.For<IAgentRepository>();
        var products = Substitute.For<IProductRepository>();
        var me = Agent.Create("me", "Riley", "riley@example.com", AgentRole.Agent, new FakeTimeProvider()).Value;
        var orbitly = Product.Create("orbitly", "Orbitly", "ORB", null, new FakeTimeProvider()).Value;
        var paperplane = Product.Create("paperplane", "Paperplane", "PPL", null, new FakeTimeProvider()).Value;
        claims.Current.Returns(new AgentClaims("me", "Riley", "riley@example.com", AgentRole.Agent));
        agents.GetBySubjectAsync("me", Arg.Any<CancellationToken>()).Returns(me);
        products.ListAsync(true, Arg.Any<CancellationToken>()).Returns([orbitly, paperplane]);
        agents.ListNotificationPreferencesAsync(me.Id, Arg.Any<CancellationToken>()).Returns([new AgentNotificationPreference(me.Id, orbitly.Id, true)]);

        var preferences = (await new GetMyNotificationPreferencesRequestHandler(claims, agents, products).HandleAsync(TestContext.Current.CancellationToken)).Value;

        preferences.Select(p => (p.ProductName, p.NotifyNewTicket)).ShouldBe([("Orbitly", true), ("Paperplane", false)]);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: the build fails because the handlers do not exist.

- [ ] **Step 3: Implement the three handlers**

`src/TechStrap.Application/Agents/UpdateMyProfileRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Agents;

namespace TechStrap.Application.Agents;

public interface IUpdateMyProfileRequestHandler
{
    Task<Result> HandleAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken);
}

/// <summary>PUT /api/agents/me/profile: the caller sets or clears their customer-facing display name (D-024).</summary>
public sealed class UpdateMyProfileRequestHandler(ICurrentAgentClaims currentAgent, IAgentRepository agents, IUnitOfWork unitOfWork) : IUpdateMyProfileRequestHandler
{
    public async Task<Result> HandleAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var me = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (me.IsFailure)
        {
            return Result.Failure(me.Errors[0]);
        }

        var changed = me.Value.SetPublicDisplayName(request.PublicDisplayName);
        if (changed.IsFailure)
        {
            return changed.ToResult();
        }

        agents.Update(me.Value);
        return await scope.CommitAsync(cancellationToken);
    }
}
```

`src/TechStrap.Application/Agents/UpdateNotificationPreferencesRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Agents;
using TechStrap.Domain.Agents;

namespace TechStrap.Application.Agents;

public interface IUpdateNotificationPreferencesRequestHandler
{
    Task<Result> HandleAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken);
}

/// <summary>PUT /api/agents/me/notification-preferences: per-product new-ticket alert opt-in for the caller. Repeating it is harmless.</summary>
public sealed class UpdateNotificationPreferencesRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IUnitOfWork unitOfWork) : IUpdateNotificationPreferencesRequestHandler
{
    public async Task<Result> HandleAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken)
    {
        if (request.Preferences is null)
        {
            return Result.Failure(new ResultError("notification-preferences-required", "Send the list of product preferences.", ResultErrorKind.Validation, "preferences"));
        }

        var seen = new HashSet<Guid>();
        for (var index = 0; index < request.Preferences.Count; index++)
        {
            var productId = request.Preferences[index].ProductId;
            if (!seen.Add(productId))
            {
                return Result.Failure(new ResultError("notification-product-repeated", "Each product can appear only once.", ResultErrorKind.Validation, $"preferences[{index}].productId"));
            }

            if (await products.GetByIdAsync(productId, cancellationToken) is null)
            {
                return Result.Failure(new ResultError("notification-product-unknown", "That product does not exist. Reload the list and try again.", ResultErrorKind.Validation, $"preferences[{index}].productId"));
            }
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var me = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (me.IsFailure)
        {
            return Result.Failure(me.Errors[0]);
        }

        foreach (var preference in request.Preferences)
        {
            await agents.SetNotificationPreferenceAsync(new AgentNotificationPreference(me.Value.Id, preference.ProductId, preference.NotifyNewTicket), cancellationToken);
        }

        return await scope.CommitAsync(cancellationToken);
    }
}
```

`src/TechStrap.Application/Agents/GetMyNotificationPreferencesRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Agents;

namespace TechStrap.Application.Agents;

public interface IGetMyNotificationPreferencesRequestHandler
{
    Task<Result<IReadOnlyList<NotificationPreferenceDto>>> HandleAsync(CancellationToken cancellationToken);
}

/// <summary>GET /api/agents/me/notification-preferences: every active product with the caller's choice (off when never set).</summary>
public sealed class GetMyNotificationPreferencesRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products) : IGetMyNotificationPreferencesRequestHandler
{
    public async Task<Result<IReadOnlyList<NotificationPreferenceDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        var me = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (me.IsFailure)
        {
            return Result<IReadOnlyList<NotificationPreferenceDto>>.Failure(me.Errors[0]);
        }

        var chosen = (await agents.ListNotificationPreferencesAsync(me.Value.Id, cancellationToken))
            .ToDictionary(preference => preference.ProductId, preference => preference.NotifyNewTicket);
        var active = await products.ListAsync(activeOnly: true, cancellationToken);
        IReadOnlyList<NotificationPreferenceDto> preferences =
            [.. active.Select(product => new NotificationPreferenceDto(product.Id, product.Name, chosen.GetValueOrDefault(product.Id)))];
        return Result<IReadOnlyList<NotificationPreferenceDto>>.Success(preferences);
    }
}
```

- [ ] **Step 4: Add the controller actions and their delegation tests**

Add to `AgentsController`:

```csharp
    [HttpGet("me/notification-preferences")]
    public async Task<IActionResult> GetMyNotificationPreferences(
        [FromServices] IGetMyNotificationPreferencesRequestHandler getPreferences,
        CancellationToken cancellationToken) =>
        (await getPreferences.HandleAsync(cancellationToken)).ToActionResult(this, Ok);

    [HttpPut("me/notification-preferences")]
    public async Task<IActionResult> UpdateMyNotificationPreferences(
        UpdateNotificationPreferencesRequest request,
        [FromServices] IUpdateNotificationPreferencesRequestHandler updatePreferences,
        CancellationToken cancellationToken) =>
        (await updatePreferences.HandleAsync(request, cancellationToken)).ToActionResult(this, NoContent);

    /// <summary>Sets or clears the caller's customer-facing display name (D-024).</summary>
    [HttpPut("me/profile")]
    public async Task<IActionResult> UpdateMyProfile(
        UpdateMyProfileRequest request,
        [FromServices] IUpdateMyProfileRequestHandler updateProfile,
        CancellationToken cancellationToken) =>
        (await updateProfile.HandleAsync(request, cancellationToken)).ToActionResult(this, NoContent);
```

In `AgentsControllerTests`, add three delegation tests in the same style as `Update_DelegatesTheIdAndBodyAndPassesCancellation`:
- `UpdateMyProfile_Delegates_returns_204`, asserting `NoContentResult`
- `UpdateMyNotificationPreferences_Delegates_returns_204`
- `GetMyNotificationPreferences_Delegates_returns_200`

- [ ] **Step 5: Write the endpoint integration test**

`tests/TechStrap.Api.Tests/Agents/AgentSelfServiceEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Agents;

namespace TechStrap.Api.Tests.Agents;

public sealed class AgentSelfServiceEndpointTests(TestPostgres postgres)
{
    [Fact]
    public async Task A_display_name_persists_and_is_returned_by_me()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var client = factory.CreateClient().Bearer(TestJwt.Token("me", [TestJwt.AgentGroup], email: "riley@example.com", name: "Riley Chen"));
        (await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        using var saved = await client.PutAsJsonAsync("/api/agents/me/profile", new UpdateMyProfileRequest("Ry"), TestContext.Current.CancellationToken);
        var me = await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken);

        saved.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        me!.PublicDisplayName.ShouldBe("Ry");
    }

    [Fact]
    public async Task An_invalid_display_name_is_a_400_with_a_field_error()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var client = factory.CreateClient().Bearer(TestJwt.Token("me", [TestJwt.AgentGroup], email: "riley@example.com"));
        (await client.GetAsync("/api/agents/me", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        using var response = await client.PutAsJsonAsync("/api/agents/me/profile", new UpdateMyProfileRequest("ry@example.com"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("public-display-name");
    }
}
```

Persisting notification preferences through the endpoint is tested in Task 8, once products can be created through the API.

- [ ] **Step 6: Run the tests and confirm they pass, then build and commit**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`, `dotnet test --project tests/TechStrap.Api.Tests -c Release`, and `dotnet build TechStrap.slnx -c Release`
Expected: PASS, with 0 warnings.

```bash
git add src tests
git commit -m "feat(agents): own notification preferences and customer-facing display name" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 8: Products and branding

**Files:**
- Create: `src/TechStrap.Application/Products/ProductMapping.cs`
- Create: `src/TechStrap.Application/Products/ProductErrors.cs`
- Create: `src/TechStrap.Application/Products/ListProductsRequestHandler.cs`
- Create: `src/TechStrap.Application/Products/GetProductRequestHandler.cs`
- Create: `src/TechStrap.Application/Products/CreateProductRequestHandler.cs`
- Create: `src/TechStrap.Application/Products/UpdateProductRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/ProductsController.cs`
- Create: `tests/TechStrap.Application.Tests/Products/ListProductsRequestHandlerTests.cs`
- Create: `tests/TechStrap.Application.Tests/Products/GetProductRequestHandlerTests.cs`
- Create: `tests/TechStrap.Application.Tests/Products/CreateProductRequestHandlerTests.cs`
- Create: `tests/TechStrap.Application.Tests/Products/UpdateProductRequestHandlerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Controllers/ProductsControllerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Products/ProductEndpointTests.cs`

**Interfaces:**
- Consumes:
  - Domain: `Product.Create(key, name, numberPrefix, branding?, clock)`, `Product.UpdateDetails(name, branding)`, `Product.SetActive(bool)`, `Product.Version`.
  - `ProductBranding.Create(displayName, logoPath, accentColour, fromAddress, replyTo)`.
  - `ProductAccent.TryDerive(string?, out ProductAccentColors)` from Contracts (D-025).
  - The `IProductRepository` members, plus `CurrentAgent`, `AdminAudit`, `UnitOfWorkSubstitute`, `ControllerTestContext`, `ApiTestDatabase` and `TestJwt`.
- Produces:
  - `IListProductsRequestHandler { Task<Result<IReadOnlyList<ProductDto>>> HandleAsync(CancellationToken cancellationToken); }`
  - `IGetProductRequestHandler { Task<Result<ProductDto>> HandleAsync(Guid productId, CancellationToken cancellationToken); }`
  - `ICreateProductRequestHandler { Task<Result<ProductDto>> HandleAsync(CreateProductRequest request, CancellationToken cancellationToken); }`
  - `IUpdateProductRequestHandler { Task<Result<ProductDto>> HandleAsync(Guid productId, UpdateProductRequest request, CancellationToken cancellationToken); }`
  - `internal static class ProductMapping { ProductDto ToDto(Product); }`
  - `internal static class ProductErrors { NotFound(), KeyTaken(), Stale() }`
  - Routes:
    - `GET /api/products` (Agent)
    - `GET /api/products/{id:guid}` (Agent)
    - `POST /api/products` (Admin, 201 + Location)
    - `PUT /api/products/{id:guid}` (Admin)

- [ ] **Step 1: Write the failing handler tests**

`tests/TechStrap.Application.Tests/Products/CreateProductRequestHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Products;

public sealed class CreateProductRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Agent _admin;

    public CreateProductRequestHandlerTests()
    {
        _admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(_admin);
        _products.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => _added?.Id == call.Arg<Guid>() ? _added : null);
        _products.When(p => p.Add(Arg.Any<Product>())).Do(call => _added = call.Arg<Product>());
    }

    private Product? _added;

    private CreateProductRequestHandler Handler(params Result[] commits) =>
        new(_claims, _agents, _products, _events, UnitOfWorkSubstitute.Create(commits), _clock);

    [Fact]
    public async Task A_product_is_created_with_branding_and_audited_without_personal_data()
    {
        var result = await Handler().HandleAsync(
            new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, "#7c3aed", "support@orbitly.example", null)),
            TestContext.Current.CancellationToken);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Key.ShouldBe("orbitly"),
            dto => dto.Branding.AccentColour.ShouldBe("#7C3AED"),
            dto => dto.Branding.OnAccentColour.ShouldBe("#FFFFFF"),
            dto => dto.Branding.AccentInkColour.ShouldNotBeNullOrWhiteSpace());
        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.ProductCreated && e.ActorId == _admin.Id && e.PayloadJson == "{\"productKey\":\"orbitly\",\"numberPrefix\":\"ORB\"}"));
    }

    [Fact]
    public async Task Without_branding_the_default_branding_comes_from_the_name()
    {
        var result = await Handler().HandleAsync(new CreateProductRequest("orbitly", "Orbitly", "ORB", null), TestContext.Current.CancellationToken);

        result.Value.Branding.DisplayName.ShouldBe("Orbitly");
        result.Value.Branding.AccentColour.ShouldBe(ProductBranding.DefaultAccentColour);
    }

    [Fact]
    public async Task A_malformed_accent_is_a_field_error()
    {
        var result = await Handler().HandleAsync(
            new CreateProductRequest("orbitly", "Orbitly", "ORB", new ProductBrandingRequest("Orbitly", null, "purple", null, null)),
            TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("accent-colour"));
        _products.DidNotReceive().Add(Arg.Any<Product>());
    }

    [Fact]
    public async Task A_taken_key_or_prefix_is_a_clear_conflict()
    {
        var result = await Handler(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.Duplicate))
            .HandleAsync(new CreateProductRequest("orbitly", "Orbitly", "ORB", null), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("product-key-taken"));
    }
}
```

`tests/TechStrap.Application.Tests/Products/UpdateProductRequestHandlerTests.cs`: use the same setup pattern, with a stored product returned from `GetByIdAsync`, built with `Product.Restore(..., version: 7)`. Cover these cases, one test each:
- `An_update_changes_name_branding_and_status_and_audits_what_changed`
  - The request uses `Version: 7`.
  - Assert the returned DTO.
  - Assert `_products.Received(1).Update(product)`.
  - Assert the event payload is exactly `{"changed":["name","branding","isActive"]}` when all three differ.
- `A_stale_version_is_a_conflict_and_nothing_is_saved`
  - The request uses `Version: 6`.
  - Expect Conflict code `concurrency-conflict`, with no `Update` and no `Add`.
- `A_malformed_accent_is_a_field_error`
- `An_unknown_product_is_not_found`
- `A_conflict_at_commit_is_returned`
  - Commit returns `UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ConcurrencyConflict)`.
  - Expect code `concurrency-conflict`.
- `An_update_that_changes_nothing_is_not_audited`
  - Expect success, and `_events.DidNotReceive().Add(...)`.

`ListProductsRequestHandlerTests` covers two cases. An Agent role calls `ListAsync(true, …)` and an Admin calls `ListAsync(false, …)`; assert on the received calls. The mapped DTOs carry the version.

`GetProductRequestHandlerTests` covers three cases:
- found returns the DTO
- unknown returns NotFound
- an inactive product requested by an Agent returns NotFound, while an Admin gets it

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: the build fails because the product handlers do not exist.

- [ ] **Step 3: Implement the mapping, errors and handlers**

`src/TechStrap.Application/Products/ProductMapping.cs`:

```csharp
using TechStrap.Contracts.Branding;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Products;

internal static class ProductMapping
{
    public static ProductDto ToDto(Product product)
    {
        var branding = product.Branding;

        // The stored accent is always a valid #RRGGBB (ProductBranding.Create); the derived colors are what customers see (D-025, D-031).
        var colours = ProductAccent.TryDerive(branding.AccentColour, out var derived)
            ? derived
            : new ProductAccentColors(branding.AccentColour, "#FFFFFF", branding.AccentColour);
        return new ProductDto(
            product.Id,
            product.Key,
            product.Name,
            product.NumberPrefix,
            product.IsActive,
            new ProductBrandingDto(branding.DisplayName, branding.LogoPath, colours.Accent, colours.OnAccent, colours.AccentInk, branding.FromAddress, branding.ReplyTo),
            product.Version);
    }
}
```

`src/TechStrap.Application/Products/ProductErrors.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Products;

internal static class ProductErrors
{
    public static ResultError NotFound() => new("product-not-found", "That product does not exist.", ResultErrorKind.NotFound);

    public static ResultError KeyTaken() =>
        new("product-key-taken", "Another product already uses this key or ticket number prefix. Choose different ones.", ResultErrorKind.Conflict);

    public static ResultError Stale() =>
        new(PersistenceErrorCodes.ConcurrencyConflict, "This product changed since you opened it. Reload it and apply your change again.", ResultErrorKind.Conflict);
}
```

`src/TechStrap.Application/Products/CreateProductRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Products;

public interface ICreateProductRequestHandler
{
    Task<Result<ProductDto>> HandleAsync(CreateProductRequest request, CancellationToken cancellationToken);
}

/// <summary>POST /api/products (Admin, D-022). Key and number prefix are permanent; the accent is checked for format only (D-031).</summary>
public sealed class CreateProductRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICreateProductRequestHandler
{
    public async Task<Result<ProductDto>> HandleAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        ProductBranding? branding = null;
        if (request.Branding is { } input)
        {
            var built = ProductBranding.Create(input.DisplayName, input.LogoPath, input.AccentColour, input.FromAddress, input.ReplyTo);
            if (built.IsFailure)
            {
                return Result<ProductDto>.Failure(built.Error!.ToError());
            }

            branding = built.Value;
        }

        var created = Product.Create(request.Key, request.Name, request.NumberPrefix, branding, clock);
        if (created.IsFailure)
        {
            return Result<ProductDto>.Failure(created.Error!.ToError());
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<ProductDto>.Failure(actor.Errors[0]);
        }

        var product = created.Value;
        products.Add(product);
        AdminAudit.Record(adminEvents, AdminEventType.ProductCreated, actor.Value, AdminSubjectType.Product, product.Id,
            new { productKey = product.Key, numberPrefix = product.NumberPrefix }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<ProductDto>.Failure(committed.Errors[0].Code == PersistenceErrorCodes.Duplicate ? ProductErrors.KeyTaken() : committed.Errors[0]);
        }

        // Re-read so the returned Version is the stored concurrency token.
        var saved = await products.GetByIdAsync(product.Id, cancellationToken) ?? product;
        return Result<ProductDto>.Success(ProductMapping.ToDto(saved));
    }
}
```

When a domain failure becomes a different result type, use `Result<TOther>.Failure(domainResult.Error!.ToError())`. `.ToResult()` keeps the domain value's own type, so it does not fit here.

`src/TechStrap.Application/Products/UpdateProductRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Products;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Products;

public interface IUpdateProductRequestHandler
{
    Task<Result<ProductDto>> HandleAsync(Guid productId, UpdateProductRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// PUT /api/products/{id} (Admin). The caller sends the Version they read; a different stored version is a 409 so a second
/// admin's edit is never silently overwritten (D-026). The repository checks it again at commit.
/// </summary>
public sealed class UpdateProductRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IUpdateProductRequestHandler
{
    public async Task<Result<ProductDto>> HandleAsync(Guid productId, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var input = request.Branding;
        var branding = ProductBranding.Create(input?.DisplayName, input?.LogoPath, input?.AccentColour, input?.FromAddress, input?.ReplyTo);
        if (branding.IsFailure)
        {
            return Result<ProductDto>.Failure(branding.Error!.ToError());
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<ProductDto>.Failure(actor.Errors[0]);
        }

        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null)
        {
            return Result<ProductDto>.Failure(ProductErrors.NotFound());
        }

        if (product.Version != request.Version)
        {
            return Result<ProductDto>.Failure(ProductErrors.Stale());
        }

        var changed = new List<string>();
        if (!string.Equals(product.Name, request.Name?.Trim(), StringComparison.Ordinal))
        {
            changed.Add("name");
        }

        if (product.Branding != branding.Value)
        {
            changed.Add("branding");
        }

        if (product.IsActive != request.IsActive)
        {
            changed.Add("isActive");
        }

        var updated = product.UpdateDetails(request.Name, branding.Value);
        if (updated.IsFailure)
        {
            return Result<ProductDto>.Failure(updated.Error!.ToError());
        }

        if (changed.Count == 0)
        {
            return Result<ProductDto>.Success(ProductMapping.ToDto(product));
        }

        product.SetActive(request.IsActive);
        products.Update(product);
        AdminAudit.Record(adminEvents, AdminEventType.ProductUpdated, actor.Value, AdminSubjectType.Product, product.Id, new { changed }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<ProductDto>.Failure(committed.Errors[0]);
        }

        var saved = await products.GetByIdAsync(product.Id, cancellationToken) ?? product;
        return Result<ProductDto>.Success(ProductMapping.ToDto(saved));
    }
}
```

`ProductBranding` is a record, so `!=` compares values. That is enough to decide whether "branding" changed.

`ListProductsRequestHandler` follows the shape of `ListAgentsRequestHandler`:
- It returns `AgentErrors.AccessRequired()` when there are no claims.
- It sets `activeOnly = claims.Role != AgentRole.Admin`.
- It returns `IReadOnlyList<ProductDto>` from `products.ListAsync(activeOnly, ct)`, mapped with `ProductMapping.ToDto`.

`GetProductRequestHandler`:
- It returns `AgentErrors.AccessRequired()` when there are no claims.
- It loads the product. A missing product, or an inactive one when the caller is not an Admin, returns `ProductErrors.NotFound()`.

Give each handler a summary that names its route and policy, for example `/// <summary>GET /api/products (Agent): agents get active products; admins get all (D-022).</summary>`.

- [ ] **Step 4: Add the controller and its delegation tests**

`src/TechStrap.Api/Controllers/ProductsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Products;
using TechStrap.Contracts.Products;

namespace TechStrap.Api.Controllers;

/// <summary>Products and branding. Agents read active products; admins manage them (D-022).</summary>
[ApiController]
[Route("api/products")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class ProductsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromServices] IListProductsRequestHandler listProducts, CancellationToken cancellationToken) =>
        (await listProducts.HandleAsync(cancellationToken)).ToActionResult(this, Ok);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromServices] IGetProductRequestHandler getProduct, CancellationToken cancellationToken) =>
        (await getProduct.HandleAsync(id, cancellationToken)).ToActionResult(this, Ok);

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Create(CreateProductRequest request, [FromServices] ICreateProductRequestHandler createProduct, CancellationToken cancellationToken) =>
        (await createProduct.HandleAsync(request, cancellationToken)).ToActionResult(this, product => CreatedAtAction(nameof(Get), new { id = product.Id }, product));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Update(Guid id, UpdateProductRequest request, [FromServices] IUpdateProductRequestHandler updateProduct, CancellationToken cancellationToken) =>
        (await updateProduct.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);
}
```

`ProductsControllerTests` holds one delegation test per action, each asserting the received cancellation token and the result type:
- `List` and `Get` return `OkObjectResult`.
- `Create` returns `CreatedAtActionResult` with `ActionName == "Get"`.
- `Update` returns `OkObjectResult`.

- [ ] **Step 5: Write the endpoint integration tests**

`tests/TechStrap.Api.Tests/Products/ProductEndpointTests.cs`. Use two signed-in clients, an admin and an agent, with the pattern from `AgentManagementEndpointTests`, and call `/api/agents/me` first. Cover these cases, one test each:
- `Create_returns_201_with_a_location_and_the_product_can_be_read`
  - Assert `response.Headers.Location` ends with `/api/products/{id}`.
  - GET it back.
  - Assert one row in `admin_events` with type `ProductCreated`: `SELECT count(*) FROM admin_events WHERE type = 'ProductCreated'`. Check how the event type is stored first.
- `A_duplicate_key_is_409`
- `A_malformed_accent_is_400_with_an_accent_field_error`
  - Assert the body contains `accent-colour`.
- `A_stale_version_is_409_and_the_returned_version_allows_the_next_update`
  - Create the product, update it with the returned `Version` (200), and update it again with the **original** version (409).
  - Then update with the version from the 200 response; that update succeeds.
- `An_agent_sees_active_products_but_cannot_create_one`
  - Create two products as admin and deactivate one.
  - The agent's GET list contains only the active one.
  - The agent's POST is 403.
- `Notification_preferences_persist_through_the_api`
  - As an agent, PUT `[{productId, true}]`, which is 204.
  - GET returns the product with `NotifyNewTicket = true` and the other active product with `false`.
  - A PUT with an unknown product is 400 with target `preferences[0].productId`.

- [ ] **Step 6: Run the tests and confirm they pass, then build and commit**

Run:
- `dotnet test --project tests/TechStrap.Application.Tests -c Release`
- `dotnet test --project tests/TechStrap.Api.Tests -c Release`
- `dotnet build TechStrap.slnx -c Release`

Expected: PASS, with 0 warnings.

```bash
git add src tests
git commit -m "feat(products): list, read, create and update products with branding and version checks" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 9: API key hashing and real dev keys

**Files:**
- Create: `src/TechStrap.Application/ApiKeys/IApiKeyHasher.cs`
- Create: `src/TechStrap.Application/ApiKeys/ApiKeyFormat.cs`
- Create: `src/TechStrap.Infrastructure/Security/ApiKeyHasher.cs`
- Create: `src/TechStrap.Infrastructure/Security/SecurityServiceCollectionExtensions.cs`
- Create: `src/TechStrap.Infrastructure/Seeding/DevelopmentApiKeys.cs`
- Modify: `src/TechStrap.Infrastructure/Seeding/DevelopmentDataSeeder.cs` (lines 105-107 and its constructor: take `IApiKeyHasher`)
- Modify: `src/TechStrap.Infrastructure/Seeding/SeedingServiceCollectionExtensions.cs` (call `AddTechStrapSecurity()`)
- Modify: `src/TechStrap.Api/Program.cs` (call `builder.Services.AddTechStrapSecurity();` after `AddTechStrapPersistence()`)
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/ApiKeyHasherTests.cs`
- Modify: `tests/TechStrap.Infrastructure.IntegrationTests/DevSeederTests.cs` (expect real hashes and new prefixes)
- Create: `docs/development/DEV-DATA.md`

**Interfaces:**
- Produces:
  ```csharp
  namespace TechStrap.Application.ApiKeys;
  public interface IApiKeyHasher
  {
      GeneratedApiKey Generate(ApiKeyKind kind);
      string Hash(string plaintextKey);
      bool Verify(string plaintextKey, string keyHash);
  }
  public sealed record GeneratedApiKey(string PlaintextKey, string KeyPrefix, string KeyHash);
  public static class ApiKeyFormat
  {
      public const string TrustedPrefix = "tsk_";
      public const string PublicPrefix = "tsp_";
      public const int SecretBytes = 32;
      public const int SecretLength = 43;
      public const int StoredPrefixLength = 12;
      public const string HashScheme = "sha256:";
      public static string KindPrefix(ApiKeyKind kind);
  }
  ```
- `IServiceCollection.AddTechStrapSecurity()` (Infrastructure) registers a singleton `IApiKeyHasher`.
- `DevelopmentApiKeys.OrbitlyTrusted`, `DevelopmentApiKeys.OrbitlyPublic`, `DevelopmentApiKeys.PaperplaneTrusted` (public consts). PHASE-05 intake tests use them.

- [ ] **Step 1: Write the failing hasher tests**

`tests/TechStrap.Infrastructure.IntegrationTests/ApiKeyHasherTests.cs`. This is a plain unit test; it lives here because `ApiKeyHasher` is `internal` and this project has `InternalsVisibleTo`.

```csharp
using TechStrap.Application.ApiKeys;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Security;

namespace TechStrap.Infrastructure.IntegrationTests;

public sealed class ApiKeyHasherTests
{
    private readonly ApiKeyHasher _hasher = new();

    [Theory]
    [InlineData(ApiKeyKind.Trusted, "tsk_")]
    [InlineData(ApiKeyKind.Public, "tsp_")]
    public void A_generated_key_has_the_kind_prefix_a_256_bit_secret_and_a_matching_hash(ApiKeyKind kind, string prefix)
    {
        var key = _hasher.Generate(kind);

        key.PlaintextKey.ShouldStartWith(prefix);
        key.PlaintextKey.Length.ShouldBe(prefix.Length + ApiKeyFormat.SecretLength);
        key.PlaintextKey[prefix.Length..].ShouldMatch("^[A-Za-z0-9_-]{43}$");
        key.KeyPrefix.ShouldBe(key.PlaintextKey[..ApiKeyFormat.StoredPrefixLength]);
        key.KeyHash.ShouldBe(_hasher.Hash(key.PlaintextKey));
        key.KeyHash.ShouldStartWith("sha256:");
        key.KeyHash.ShouldNotContain(key.PlaintextKey[prefix.Length..]);
    }

    [Fact]
    public void A_thousand_generated_keys_are_all_different()
    {
        var keys = Enumerable.Range(0, 1000).Select(_ => _hasher.Generate(ApiKeyKind.Trusted)).ToList();

        keys.Select(k => k.PlaintextKey).Distinct().Count().ShouldBe(1000);
        keys.Select(k => k.KeyHash).Distinct().Count().ShouldBe(1000);
    }

    [Fact]
    public void Verify_accepts_only_the_exact_key()
    {
        var key = _hasher.Generate(ApiKeyKind.Public);

        _hasher.Verify(key.PlaintextKey, key.KeyHash).ShouldBeTrue();
        _hasher.Verify(key.PlaintextKey + "x", key.KeyHash).ShouldBeFalse();
        _hasher.Verify(key.PlaintextKey[..^1], key.KeyHash).ShouldBeFalse();
        _hasher.Verify(string.Empty, key.KeyHash).ShouldBeFalse();
        _hasher.Verify(key.PlaintextKey, "sha256:00").ShouldBeFalse();
    }

    [Fact]
    public void The_hash_is_the_documented_sha256_of_the_key() =>
        _hasher.Hash("tsk_abc").ShouldBe("sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("tsk_abc"u8)));

    [Fact]
    public void Verify_compares_in_constant_time()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "TechStrap.Infrastructure", "Security", "ApiKeyHasher.cs"));

        source.ShouldContain("CryptographicOperations.FixedTimeEquals");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TechStrap.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter ApiKeyHasherTests`
Expected: a build failure, because `ApiKeyHasher` and `ApiKeyFormat` are missing.

- [ ] **Step 3: Implement the abstraction, the format and the hasher**

`src/TechStrap.Application/ApiKeys/IApiKeyHasher.cs`:

```csharp
using TechStrap.Domain.Products;

namespace TechStrap.Application.ApiKeys;

/// <summary>Generates product API keys and hashes and verifies them (D-001). Only the prefix and the hash are ever stored.</summary>
public interface IApiKeyHasher
{
    GeneratedApiKey Generate(ApiKeyKind kind);

    string Hash(string plaintextKey);

    /// <summary>Constant-time comparison of the key's hash with the stored hash.</summary>
    bool Verify(string plaintextKey, string keyHash);
}

/// <summary>A new key. <see cref="PlaintextKey"/> is shown to the admin once and never stored.</summary>
public sealed record GeneratedApiKey(string PlaintextKey, string KeyPrefix, string KeyHash);
```

`src/TechStrap.Application/ApiKeys/ApiKeyFormat.cs`:

```csharp
using TechStrap.Domain.Products;

namespace TechStrap.Application.ApiKeys;

/// <summary>
/// Key format: kind prefix ("tsk_" trusted, "tsp_" public) plus 43 base64url characters (32 random bytes). The first 12 characters
/// are stored to identify a key; the hash is "sha256:" and the lower-case hex SHA-256 of the whole key. A plain hash is enough
/// because the secret has 256 bits of entropy, and lookups need a deterministic hash.
/// </summary>
public static class ApiKeyFormat
{
    public const string TrustedPrefix = "tsk_";
    public const string PublicPrefix = "tsp_";
    public const int SecretBytes = 32;
    public const int SecretLength = 43;
    public const int StoredPrefixLength = 12;
    public const string HashScheme = "sha256:";

    public static string KindPrefix(ApiKeyKind kind) => kind == ApiKeyKind.Trusted ? TrustedPrefix : PublicPrefix;
}
```

`src/TechStrap.Infrastructure/Security/ApiKeyHasher.cs`:

```csharp
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using TechStrap.Application.ApiKeys;
using TechStrap.Domain.Products;

namespace TechStrap.Infrastructure.Security;

internal sealed class ApiKeyHasher : IApiKeyHasher
{
    public GeneratedApiKey Generate(ApiKeyKind kind)
    {
        var plaintext = ApiKeyFormat.KindPrefix(kind) + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ApiKeyFormat.SecretBytes));
        return new GeneratedApiKey(plaintext, plaintext[..ApiKeyFormat.StoredPrefixLength], Hash(plaintext));
    }

    public string Hash(string plaintextKey) =>
        ApiKeyFormat.HashScheme + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey)));

    public bool Verify(string plaintextKey, string keyHash) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(plaintextKey)), Encoding.UTF8.GetBytes(keyHash));
}
```

`src/TechStrap.Infrastructure/Security/SecurityServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TechStrap.Application.ApiKeys;

namespace TechStrap.Infrastructure.Security;

public static class SecurityServiceCollectionExtensions
{
    public static IServiceCollection AddTechStrapSecurity(this IServiceCollection services)
    {
        services.TryAddSingleton<IApiKeyHasher, ApiKeyHasher>();
        return services;
    }
}
```

- [ ] **Step 4: Run the hasher tests and confirm they pass**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter ApiKeyHasherTests`
Expected: PASS.

- [ ] **Step 5: Seed real dev keys (closes the PHASE-03 carry-forward)**

`src/TechStrap.Infrastructure/Seeding/DevelopmentApiKeys.cs`:

```csharp
namespace TechStrap.Infrastructure.Seeding;

/// <summary>
/// Fixed, obviously fake API keys for the development seed data, so local intake calls work. Development only; documented in
/// docs/development/DEV-DATA.md. They are never valid anywhere else because only development seeding stores their hashes.
/// </summary>
public static class DevelopmentApiKeys
{
    public const string OrbitlyTrusted = "tsk_devOrbitlyServerKeyNotASecret00000000000000";
    public const string OrbitlyPublic = "tsp_devOrbitlyAppKeyNotASecret00000000000000000";
    public const string PaperplaneTrusted = "tsk_devPaperplaneServerKeyNotASecret00000000000";
}
```

Each value is the prefix plus exactly 43 characters. Verify the lengths with a test step: add `[Theory]` rows to `DevSeederTests` asserting `value.Length == 47`. If a value is off, adjust its `0` padding.

In `DevelopmentDataSeeder`, add `IApiKeyHasher apiKeyHasher` to the primary constructor, then replace lines 105-107 with:

```csharp
        products.AddApiKey(Must(DevKey(orbitly.Id, ApiKeyKind.Trusted, DevelopmentApiKeys.OrbitlyTrusted, "Orbitly server (dev)")));
        products.AddApiKey(Must(DevKey(orbitly.Id, ApiKeyKind.Public, DevelopmentApiKeys.OrbitlyPublic, "Orbitly app (dev)")));
        products.AddApiKey(Must(DevKey(paperplane.Id, ApiKeyKind.Trusted, DevelopmentApiKeys.PaperplaneTrusted, "Paperplane server (dev)")));
```

and add the private helper:

```csharp
    private DomainResult<ProductApiKey> DevKey(Guid productId, ApiKeyKind kind, string plaintext, string label) =>
        ProductApiKey.Create(productId, kind, plaintext[..ApiKeyFormat.StoredPrefixLength], apiKeyHasher.Hash(plaintext), label, clock);
```

`clock` is whatever the seeder's `TimeProvider` field or parameter is called. Never log the plaintext.

In `SeedingServiceCollectionExtensions.AddTechStrapDevelopmentSeeding`, call `services.AddTechStrapSecurity();` before registering the seeder. In `Program.cs`, after `builder.Services.AddTechStrapPersistence();`, add `builder.Services.AddTechStrapSecurity();` with `using TechStrap.Infrastructure.Security;`.

Update `DevSeederTests`:
- Wherever it asserts the old prefixes or hashes, assert instead that each seeded key's `key_hash` equals `new ApiKeyHasher().Hash(DevelopmentApiKeys.X)`.
- Assert that the `key_prefix` values are `tsk_devOrbit`, `tsp_devOrbit` and `tsk_devPaper`.
- Assert that no row stores any part of the secret beyond the 12-character prefix: `SELECT count(*) FROM product_api_keys WHERE key_hash LIKE '%NotASecret%'` must be 0.

`docs/development/DEV-DATA.md`:

```markdown
# Development seed data

With `ASPNETCORE_ENVIRONMENT=Development` and `TECHSTRAP_SEED_DEV_DATA=true`, the Api seeds two products, two agents, three
requesters, tags, tickets in every status and knowledge base articles. Seeding runs once per database (markers: product
`orbitly`, shared article `welcome`).

## Dev API keys

These keys are fake and work only against a database the development seeder filled. Never use them anywhere else.

| Product | Kind | Key |
| --- | --- | --- |
| Orbitly | Trusted | `tsk_devOrbitlyServerKeyNotASecret00000000000000` |
| Orbitly | Public | `tsp_devOrbitlyAppKeyNotASecret00000000000000000` |
| Paperplane | Trusted | `tsk_devPaperplaneServerKeyNotASecret00000000000` |

A database seeded before PHASE-04 holds placeholder hashes that match no key. Start from a fresh development database to get
working keys (remove the dev database volume yourself; the seeder never deletes data).

## Dev agents

Seeded agents are `dev|sam` (Admin) and `dev|riley` (Agent, public display name "Ry"). A token from your own identity provider
signs you in as a new agent on your first `GET /api/agents/me`; roles come from your IdP groups (D-029).
```

Copy the exact key values from `DevelopmentApiKeys` into the table. The doc and the code must match.

- [ ] **Step 6: Run the seeder and Api tests, then build and commit**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release`, `dotnet test --project tests/TechStrap.Api.Tests -c Release` (`ApiSeedOnStartupTests` must still count 2 key kinds), and `dotnet build TechStrap.slnx -c Release`.
Expected: PASS, with 0 warnings.

```bash
git add src tests docs/development/DEV-DATA.md
git commit -m "feat(security): API key generation and constant-time verification; real dev key hashes" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 10: Product API key endpoints

**Files:**
- Create: `src/TechStrap.Application/ApiKeys/ApiKeyMapping.cs`
- Create: `src/TechStrap.Application/ApiKeys/ListProductApiKeysRequestHandler.cs`
- Create: `src/TechStrap.Application/ApiKeys/CreateProductApiKeyRequestHandler.cs`
- Create: `src/TechStrap.Application/ApiKeys/RevokeProductApiKeyRequestHandler.cs`
- Modify: `src/TechStrap.Api/Controllers/ProductsController.cs` (add the three key actions)
- Create: `tests/TechStrap.Application.Tests/ApiKeys/ListProductApiKeysRequestHandlerTests.cs`
- Create: `tests/TechStrap.Application.Tests/ApiKeys/CreateProductApiKeyRequestHandlerTests.cs`
- Create: `tests/TechStrap.Application.Tests/ApiKeys/RevokeProductApiKeyRequestHandlerTests.cs`
- Modify: `tests/TechStrap.Api.Tests/Controllers/ProductsControllerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Products/ApiKeyEndpointTests.cs`

**Interfaces:**
- Consumes:
  - `IApiKeyHasher` and `GeneratedApiKey` (Task 9)
  - `ProductApiKey.Create(productId, kind, keyPrefix, keyHash, label, clock)`, `Revoke(clock)` and `IsRevoked`
  - `IProductRepository.GetApiKeyAsync`, `ListApiKeysAsync`, `AddApiKey` and `UpdateApiKey`
  - `CurrentAgent`, `AdminAudit`, `ProductErrors` and `UnitOfWorkSubstitute`
- Produces:
  - `IListProductApiKeysRequestHandler { Task<Result<IReadOnlyList<ProductApiKeyDto>>> HandleAsync(Guid productId, CancellationToken cancellationToken); }`
  - `ICreateProductApiKeyRequestHandler { Task<Result<CreateProductApiKeyResponse>> HandleAsync(Guid productId, CreateProductApiKeyRequest request, CancellationToken cancellationToken); }`
  - `IRevokeProductApiKeyRequestHandler { Task<Result> HandleAsync(Guid productId, Guid keyId, CancellationToken cancellationToken); }`
  - Routes, all Admin:
    - `GET /api/products/{id:guid}/api-keys`
    - `POST /api/products/{id:guid}/api-keys`, which returns 201 with the key
    - `DELETE /api/products/{id:guid}/api-keys/{keyId:guid}`, which returns 204

- [ ] **Step 1: Write the failing handler tests**

`tests/TechStrap.Application.Tests/ApiKeys/CreateProductApiKeyRequestHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.ApiKeys;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.ApiKeys;

public sealed class CreateProductApiKeyRequestHandlerTests
{
    private const string Plaintext = "tsk_abcdefghijABCDEFGHIJabcdefghijABCDEFGHIJabc";

    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly IApiKeyHasher _hasher = Substitute.For<IApiKeyHasher>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Product _product = Product.Create("orbitly", "Orbitly", "ORB", null, new FakeTimeProvider()).Value;

    public CreateProductApiKeyRequestHandlerTests()
    {
        var admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(admin);
        _products.GetByIdAsync(_product.Id, Arg.Any<CancellationToken>()).Returns(_product);
        _hasher.Generate(Arg.Any<ApiKeyKind>()).Returns(new GeneratedApiKey(Plaintext, Plaintext[..12], "sha256:feed"));
    }

    private CreateProductApiKeyRequestHandler Handler() => new(_claims, _agents, _products, _events, _hasher, UnitOfWorkSubstitute.Create(), _clock);

    [Theory]
    [InlineData("Trusted", ApiKeyKind.Trusted)]
    [InlineData("public", ApiKeyKind.Public)]
    public async Task The_plaintext_appears_only_in_the_create_response(string kind, ApiKeyKind expected)
    {
        var result = await Handler().HandleAsync(_product.Id, new CreateProductApiKeyRequest(kind, "Server"), TestContext.Current.CancellationToken);

        result.Value.PlaintextKey.ShouldBe(Plaintext);
        result.Value.Key.KeyPrefix.ShouldBe(Plaintext[..12]);
        _hasher.Received(1).Generate(expected);
        _products.Received(1).AddApiKey(Arg.Is<ProductApiKey>(k => k.KeyHash == "sha256:feed" && k.Kind == expected));
    }

    [Fact]
    public async Task The_audit_event_holds_the_prefix_but_no_secret_or_hash()
    {
        await Handler().HandleAsync(_product.Id, new CreateProductApiKeyRequest("Trusted", null), TestContext.Current.CancellationToken);

        _events.Received(1).Add(Arg.Is<AdminEvent>(e =>
            e.Type == AdminEventType.ApiKeyCreated
            && e.PayloadJson.Contains(Plaintext[..12])
            && !e.PayloadJson.Contains(Plaintext)
            && !e.PayloadJson.Contains("sha256")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Secret")]
    public async Task An_unknown_kind_is_a_field_error(string? kind)
    {
        var result = await Handler().HandleAsync(_product.Id, new CreateProductApiKeyRequest(kind, null), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("kind"));
        _hasher.DidNotReceive().Generate(Arg.Any<ApiKeyKind>());
    }

    [Fact]
    public async Task An_unknown_product_is_not_found()
    {
        (await Handler().HandleAsync(Guid.CreateVersion7(), new CreateProductApiKeyRequest("Public", null), TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }
}
```

`RevokeProductApiKeyRequestHandlerTests` has these cases:
- Revoking an active key calls `Revoke`, then `UpdateApiKey`. It audits `ApiKeyRevoked` with a payload holding `productId` and `keyPrefix`, and no hash.
- Revoking an already revoked key succeeds, but saves nothing and adds no audit.
- A key that belongs to another product is NotFound (`api-key-not-found`).
- An unknown key is NotFound.

`ListProductApiKeysRequestHandlerTests` has these cases:
- The keys map to DTOs with `RevokedAt` set for revoked keys. Serialize the DTO list with `System.Text.Json` and assert that the JSON contains neither `sha256` nor `KeyHash`.
- An unknown product is NotFound.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: the build fails because the handlers do not exist.

- [ ] **Step 3: Implement the handlers**

`src/TechStrap.Application/ApiKeys/ApiKeyMapping.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Domain.Products;

namespace TechStrap.Application.ApiKeys;

internal static class ApiKeyMapping
{
    public static ProductApiKeyDto ToDto(ProductApiKey key) =>
        new(key.Id, key.ProductId, key.Kind == ApiKeyKind.Trusted ? ApiKeyKinds.Trusted : ApiKeyKinds.Public, key.KeyPrefix, key.Label, key.CreatedAt, key.RevokedAt, key.LastUsedAt);

    public static bool TryParseKind(string? value, out ApiKeyKind kind)
    {
        kind = default;
        if (string.Equals(value?.Trim(), ApiKeyKinds.Trusted, StringComparison.OrdinalIgnoreCase))
        {
            kind = ApiKeyKind.Trusted;
            return true;
        }

        if (string.Equals(value?.Trim(), ApiKeyKinds.Public, StringComparison.OrdinalIgnoreCase))
        {
            kind = ApiKeyKind.Public;
            return true;
        }

        return false;
    }

    public static ResultError KindInvalid() =>
        new("api-key-kind-invalid", "Choose Trusted (server-side) or Public (inside an app).", ResultErrorKind.Validation, "kind");

    public static ResultError NotFound() => new("api-key-not-found", "That API key does not exist for this product.", ResultErrorKind.NotFound);
}
```

`src/TechStrap.Application/ApiKeys/CreateProductApiKeyRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Products;
using TechStrap.Application.Results;
using TechStrap.Contracts.ApiKeys;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Products;

namespace TechStrap.Application.ApiKeys;

public interface ICreateProductApiKeyRequestHandler
{
    Task<Result<CreateProductApiKeyResponse>> HandleAsync(Guid productId, CreateProductApiKeyRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/products/{id}/api-keys (Admin, D-001, D-022). The plaintext key is returned once, in this response only; the
/// database keeps the prefix and hash, and the audit records the prefix.
/// </summary>
public sealed class CreateProductApiKeyRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IAdminEventRepository adminEvents,
    IApiKeyHasher apiKeyHasher,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICreateProductApiKeyRequestHandler
{
    public async Task<Result<CreateProductApiKeyResponse>> HandleAsync(Guid productId, CreateProductApiKeyRequest request, CancellationToken cancellationToken)
    {
        if (!ApiKeyMapping.TryParseKind(request.Kind, out var kind))
        {
            return Result<CreateProductApiKeyResponse>.Failure(ApiKeyMapping.KindInvalid());
        }

        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<CreateProductApiKeyResponse>.Failure(actor.Errors[0]);
        }

        if (await products.GetByIdAsync(productId, cancellationToken) is null)
        {
            return Result<CreateProductApiKeyResponse>.Failure(ProductErrors.NotFound());
        }

        var generated = apiKeyHasher.Generate(kind);
        var created = ProductApiKey.Create(productId, kind, generated.KeyPrefix, generated.KeyHash, request.Label, clock);
        if (created.IsFailure)
        {
            return Result<CreateProductApiKeyResponse>.Failure(created.Error!.ToError());
        }

        products.AddApiKey(created.Value);
        AdminAudit.Record(adminEvents, AdminEventType.ApiKeyCreated, actor.Value, AdminSubjectType.ApiKey, created.Value.Id,
            new { productId, kind = kind.ToString(), keyPrefix = generated.KeyPrefix }, clock);

        var committed = await scope.CommitAsync(cancellationToken);
        return committed.IsSuccess
            ? Result<CreateProductApiKeyResponse>.Success(new CreateProductApiKeyResponse(ApiKeyMapping.ToDto(created.Value), generated.PlaintextKey))
            : Result<CreateProductApiKeyResponse>.Failure(committed.Errors[0]);
    }
}
```

`RevokeProductApiKeyRequestHandler`:
- Dependencies: `ICurrentAgentClaims`, `IAgentRepository`, `IProductRepository`, `IAdminEventRepository`, `IUnitOfWork`, `TimeProvider`.
- Inside a unit of work it resolves the actor, then `var key = await products.GetApiKeyAsync(keyId, ct)`.
- If `key is null || key.ProductId != productId` it returns `ApiKeyMapping.NotFound()`.
- If `key.IsRevoked` it returns `Result.Success()` without committing.
- Otherwise it calls `key.Revoke(clock)` and `products.UpdateApiKey(key)`, records `ApiKeyRevoked` with `new { productId, keyPrefix = key.KeyPrefix }`, and returns `await scope.CommitAsync(ct)`.
- Its summary reads "DELETE ... (Admin): revokes a key; revoking twice is harmless. Kind and secret never change: revoke and create a new key instead."

`ListProductApiKeysRequestHandler`:
- Dependencies: `IProductRepository` only.
- It returns `ProductErrors.NotFound()` when the product is missing.
- Otherwise it returns `[.. (await products.ListApiKeysAsync(productId, ct)).Select(ApiKeyMapping.ToDto)]`, in repository order (newest first, revoked keys included).

- [ ] **Step 4: Add the controller actions and their delegation tests**

Add to `ProductsController`, with `using TechStrap.Application.ApiKeys;` and `using TechStrap.Contracts.ApiKeys;`:

```csharp
    [HttpGet("{id:guid}/api-keys")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> ListApiKeys(Guid id, [FromServices] IListProductApiKeysRequestHandler listApiKeys, CancellationToken cancellationToken) =>
        (await listApiKeys.HandleAsync(id, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Creates a key. The response carries the plaintext key once; it cannot be shown again.</summary>
    [HttpPost("{id:guid}/api-keys")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> CreateApiKey(Guid id, CreateProductApiKeyRequest request, [FromServices] ICreateProductApiKeyRequestHandler createApiKey, CancellationToken cancellationToken) =>
        (await createApiKey.HandleAsync(id, request, cancellationToken)).ToActionResult(this, created => StatusCode(StatusCodes.Status201Created, created));

    [HttpDelete("{id:guid}/api-keys/{keyId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> RevokeApiKey(Guid id, Guid keyId, [FromServices] IRevokeProductApiKeyRequestHandler revokeApiKey, CancellationToken cancellationToken) =>
        (await revokeApiKey.HandleAsync(id, keyId, cancellationToken)).ToActionResult(this, NoContent);
```

There is no single-key GET route, so the create action returns a plain 201 without a Location header. Add delegation tests to `ProductsControllerTests` for each action:
- `ListApiKeys` returns `OkObjectResult`.
- `CreateApiKey` returns an `ObjectResult` with `StatusCode == 201`.
- `RevokeApiKey` returns `NoContentResult`.

Each test also asserts that the cancellation token is passed through.

- [ ] **Step 5: Write the endpoint integration tests**

`tests/TechStrap.Api.Tests/Products/ApiKeyEndpointTests.cs` uses an admin client and creates a product first. Cover these cases:
- `Only_the_prefix_and_hash_are_stored_and_the_plaintext_is_shown_once`
  - POST a Trusted key and assert 201, and keep `PlaintextKey`.
  - Assert `SELECT key_hash FROM product_api_keys WHERE id = '{id}'` equals `"sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)))`.
  - Assert `SELECT count(*) FROM product_api_keys WHERE key_hash LIKE '%' || '{secretPart}' || '%' OR key_prefix = '{plaintext}'` is 0. `secretPart` is the plaintext after its prefix.
  - Assert the raw JSON of GET `/api-keys` contains the key prefix and contains neither the plaintext nor `sha256`.
  - Assert `SELECT count(*) FROM admin_events WHERE payload::text LIKE '%{secretPart}%'` is 0. Check the payload column's name and type in `AdminEventRecordConfiguration` first.
- `Both_kinds_can_be_created_and_revoked_per_product`: create a Trusted key and a Public key, DELETE one (204), and confirm the list shows `RevokedAt` set on that key only.
- `Revoking_a_key_of_another_product_is_404`.
- `An_agent_cannot_list_or_create_keys`: both requests return 403.

- [ ] **Step 6: Run the tests, build and commit**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`, `dotnet test --project tests/TechStrap.Api.Tests -c Release` and `dotnet build TechStrap.slnx -c Release`.
Expected: PASS with 0 warnings.

```bash
git add src tests
git commit -m "feat(api-keys): create, list and revoke product API keys; plaintext shown once" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 11: Tags, including forced delete (D-030)

**Files:**
- Modify: `src/TechStrap.Domain/Tickets/Ticket.cs` (add `DetachDeletedTag`)
- Modify: `tests/TechStrap.Domain.Tests/Tickets/TicketMutationTests.cs` (add tests for it)
- Modify: `src/TechStrap.Application/Persistence/ITicketRepository.cs` (add `ListTicketIdsWithTagAsync`)
- Modify: `src/TechStrap.Infrastructure/Persistence/Repositories/TicketRepository.cs`
- Create: `src/TechStrap.Application/Tags/TagMapping.cs` (holds `TagErrors` too)
- Create: `src/TechStrap.Application/Tags/ListTagsRequestHandler.cs`, `CreateTagRequestHandler.cs`, `UpdateTagRequestHandler.cs`, `DeleteTagRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/TagsController.cs`
- Create: `tests/TechStrap.Application.Tests/Tags/*RequestHandlerTests.cs` (four classes)
- Create: `tests/TechStrap.Api.Tests/Controllers/TagsControllerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Tags/TagEndpointTests.cs`
- Create: `tests/TechStrap.Infrastructure.IntegrationTests/DeleteTagIntegrationTests.cs`

**Interfaces:**
- Consumes:
  - `Tag.Create(slug, name, colour, clock)`, `Tag.Update(name, colour)`.
  - `ITagRepository.GetByIdAsync`, `ListAsync`, `Add`, `Update`, `Remove`.
  - `ITicketRepository.GetByIdAsync` and `Update`.
  - `Actor.ForAgent(Guid)`.
  - `CurrentAgent`, `AdminAudit`, `UnitOfWorkSubstitute`.
- Produces:
  - `Ticket.DetachDeletedTag(Guid tagId, Actor actor, TimeProvider clock) : DomainResult`.
  - `ITicketRepository.ListTicketIdsWithTagAsync(Guid tagId, CancellationToken) : Task<IReadOnlyList<Guid>>`.
  - Handlers:
    - `IListTagsRequestHandler { Task<Result<IReadOnlyList<TagDto>>> HandleAsync(CancellationToken cancellationToken); }`
    - `ICreateTagRequestHandler { Task<Result<TagDto>> HandleAsync(CreateTagRequest request, CancellationToken cancellationToken); }`
    - `IUpdateTagRequestHandler { Task<Result<TagDto>> HandleAsync(Guid tagId, UpdateTagRequest request, CancellationToken cancellationToken); }`
    - `IDeleteTagRequestHandler { Task<Result> HandleAsync(Guid tagId, bool force, CancellationToken cancellationToken); }`
  - Routes:
    - `GET /api/tags` (Agent).
    - `POST /api/tags` (Admin, returns 201 with the tag).
    - `PUT /api/tags/{id:guid}` (Admin).
    - `DELETE /api/tags/{id:guid}?force=` (Admin, returns 204).

- [ ] **Step 1: Write the failing Domain tests**

Append these tests to `TicketMutationTests`. Use the existing `TicketFactory` helpers to make an open ticket and a Closed ticket; read `TicketFactory.cs` for the exact helper names.

```csharp
    [Fact]
    public void Detaching_a_deleted_tag_records_TagRemoved_with_the_reason_and_leaves_last_activity_alone()
    {
        var ticket = TicketFactory.Open(Clock);
        var tagId = Guid.CreateVersion7();
        ticket.AddTag(tagId, Actor.ForAgent(AgentId), Clock).IsSuccess.ShouldBeTrue();
        ticket.AcceptChanges();
        var lastActivity = ticket.LastActivityAt;

        ticket.DetachDeletedTag(tagId, Actor.ForAgent(AgentId), Clock).IsSuccess.ShouldBeTrue();

        ticket.TagIds.ShouldNotContain(tagId);
        ticket.LastActivityAt.ShouldBe(lastActivity);
        ticket.PendingEvents.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            e => e.Type.ShouldBe(TicketEventType.TagRemoved),
            e => e.PayloadJson.ShouldContain("\"reason\":\"tag-deleted\""));
    }

    [Fact]
    public void A_closed_ticket_can_still_lose_a_deleted_tag_and_stays_closed()
    {
        var ticket = TicketFactory.ClosedWithTag(Clock, out var tagId);

        ticket.DetachDeletedTag(tagId, Actor.ForAgent(AgentId), Clock).IsSuccess.ShouldBeTrue();

        ticket.Status.ShouldBe(TicketStatus.Closed);
        ticket.TagIds.ShouldNotContain(tagId);
    }

    [Fact]
    public void Detaching_a_tag_the_ticket_does_not_carry_records_nothing()
    {
        var ticket = TicketFactory.Open(Clock);
        ticket.AcceptChanges();

        ticket.DetachDeletedTag(Guid.CreateVersion7(), Actor.ForAgent(AgentId), Clock).IsSuccess.ShouldBeTrue();

        ticket.PendingEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Detaching_needs_a_valid_actor() =>
        TicketFactory.Open(Clock).DetachDeletedTag(Guid.CreateVersion7(), default, Clock).Error!.Code.ShouldBe("actor-invalid");
```

If `TicketFactory` has no Closed-with-tag helper, add one. It creates a ticket, adds the tag, moves the ticket to Solved, and then Closes it, following the transitions that `TicketStatusRules` allows. It returns the ticket after `AcceptChanges()`. Name the clock and agent-id fields to match the existing test class.

- [ ] **Step 2: Run the tests and confirm they fail, then implement**

Run: `dotnet test --project tests/TechStrap.Domain.Tests -c Release`
Expected: the build fails with CS1061, because `DetachDeletedTag` does not exist.

Add the method to `Ticket.cs`, next to `RemoveTag`:

```csharp
    /// <summary>
    /// Removes a tag that is being deleted (D-030). Unlike RemoveTag it is allowed on a Closed ticket, because the tag itself is going
    /// away; it records TagRemoved with reason "tag-deleted" and leaves the status and last activity alone (housekeeping, not activity).
    /// </summary>
    public DomainResult DetachDeletedTag(Guid tagId, Actor actor, TimeProvider clock)
    {
        if (ValidateActor(actor) is { } invalid)
        {
            return invalid;
        }

        if (_tagIds.Remove(tagId))
        {
            Raise(TicketEventType.TagRemoved, actor, Payload(("tagId", tagId), ("reason", "tag-deleted")), clock);
        }

        return DomainResult.Ok();
    }
```

`Payload` takes `(string, object)` tuples, as `RemoveTag` uses it. If the signature differs, for example if it is typed `object?`, adapt the call.

Run the Domain tests again. Expected: PASS.

- [ ] **Step 3: Add the repository member and its integration test**

In `ITicketRepository`, add:

```csharp
    /// <summary>Ids of every ticket carrying the tag, any status. Used to detach a tag before deleting it (D-030).</summary>
    Task<IReadOnlyList<Guid>> ListTicketIdsWithTagAsync(Guid tagId, CancellationToken cancellationToken);
```

Implement it in `TicketRepository`:

```csharp
    public async Task<IReadOnlyList<Guid>> ListTicketIdsWithTagAsync(Guid tagId, CancellationToken cancellationToken) =>
        await context.Set<TicketTagRecord>().AsNoTracking()
            .Where(link => link.TagId == tagId)
            .Select(link => link.TicketId)
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);
```

Check the property names on `TicketTagRecord` (`src/TechStrap.Infrastructure/Persistence/Records/TicketRecord.cs`, around line 56).

- [ ] **Step 4: Write the failing handler tests**

`tests/TechStrap.Application.Tests/Tags/DeleteTagRequestHandlerTests.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tags;
using TechStrap.Application.Tests.Support;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tests.Tags;

public sealed class DeleteTagRequestHandlerTests
{
    private readonly ICurrentAgentClaims _claims = Substitute.For<ICurrentAgentClaims>();
    private readonly IAgentRepository _agents = Substitute.For<IAgentRepository>();
    private readonly ITagRepository _tags = Substitute.For<ITagRepository>();
    private readonly ITicketRepository _tickets = Substitute.For<ITicketRepository>();
    private readonly IAdminEventRepository _events = Substitute.For<IAdminEventRepository>();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));
    private readonly Tag _tag;

    public DeleteTagRequestHandlerTests()
    {
        var admin = Agent.Create("admin", "Sam", "sam@example.com", AgentRole.Admin, _clock).Value;
        _claims.Current.Returns(new AgentClaims("admin", "Sam", "sam@example.com", AgentRole.Admin));
        _agents.GetBySubjectAsync("admin", Arg.Any<CancellationToken>()).Returns(admin);
        _tag = Tag.Create("bug", "Bug", "#DC2626", _clock).Value;
        _tags.GetByIdAsync(_tag.Id, Arg.Any<CancellationToken>()).Returns(_tag);
    }

    private DeleteTagRequestHandler Handler() => new(_claims, _agents, _tags, _tickets, _events, UnitOfWorkSubstitute.Create(), _clock);

    [Fact]
    public async Task An_unused_tag_is_deleted_and_audited()
    {
        _tickets.ListTicketIdsWithTagAsync(_tag.Id, Arg.Any<CancellationToken>()).Returns([]);

        (await Handler().HandleAsync(_tag.Id, force: false, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();

        _tags.Received(1).Remove(_tag);
        _events.Received(1).Add(Arg.Is<AdminEvent>(e => e.Type == AdminEventType.TagDeleted && e.PayloadJson == "{\"slug\":\"bug\",\"detachedTicketCount\":0}"));
    }

    [Fact]
    public async Task A_tag_in_use_is_a_conflict_that_states_how_many_tickets_carry_it()
    {
        _tickets.ListTicketIdsWithTagAsync(_tag.Id, Arg.Any<CancellationToken>()).Returns([Guid.CreateVersion7(), Guid.CreateVersion7()]);

        var result = await Handler().HandleAsync(_tag.Id, force: false, TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("tag-in-use"),
            error => error.Message.ShouldContain("2 tickets"));
        _tags.DidNotReceive().Remove(Arg.Any<Tag>());
    }

    [Fact]
    public async Task An_unknown_tag_is_not_found()
    {
        (await Handler().HandleAsync(Guid.CreateVersion7(), force: true, TestContext.Current.CancellationToken))
            .Errors.ShouldHaveSingleItem().Kind.ShouldBe(ResultErrorKind.NotFound);
    }
}
```

Add a fourth test, `Force_detaches_the_tag_from_every_ticket_then_deletes_it`, to this class. It uses real `Ticket` objects and does the following:
1. Build two tickets with `Ticket.Create(...)`, using the factory code that `tests/TechStrap.Domain.Tests/Tickets/TicketFactory.cs` uses. Copy the minimal creation call; Application.Tests cannot reference Domain.Tests.
2. Add `_tag.Id` to both tickets, then call `AcceptChanges()`.
3. Arrange `_tickets.ListTicketIdsWithTagAsync(...)` to return both ids, and `GetByIdAsync` to return each ticket.
4. Call `HandleAsync(_tag.Id, force: true, ...)`.
5. Assert that both tickets no longer carry the tag and each has one pending `TagRemoved` event.
6. Assert `_tickets.Received(1).Update(ticketA)` and `Update(ticketB)`, plus `_tags.Received(1).Remove(_tag)`.
7. Assert the audit payload is exactly `{"slug":"bug","detachedTicketCount":2}`.

`CreateTagRequestHandlerTests` covers:
- A tag is created and returned, with the color stored in upper case, and `TagCreated` is audited with payload `{"slug":"bug"}`.
- A bad slug (`"Not A Slug"`) returns a Validation error with target `slug`.
- A bad color (`"red"`) returns a Validation error with target `colour`.
- A commit that fails with `Duplicate` returns a Conflict with code `tag-slug-taken`.

`UpdateTagRequestHandlerTests` covers:
- Name and color change; `TagUpdated` is audited with payload `{"slug":"bug","changed":["name","colour"]}`.
- An unchanged update succeeds and adds no audit event.
- An unknown tag returns NotFound.
- A bad color returns a Validation error with target `colour`.

`ListTagsRequestHandlerTests` covers: the tags are mapped to DTOs in repository order.

- [ ] **Step 5: Run the tests and confirm they fail, then implement the handlers**

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: the build fails because the Tags handlers do not exist.

`src/TechStrap.Application/Tags/TagMapping.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Tags;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tags;

internal static class TagMapping
{
    public static TagDto ToDto(Tag tag) => new(tag.Id, tag.Slug, tag.Name, tag.Colour);
}

internal static class TagErrors
{
    public static ResultError NotFound() => new("tag-not-found", "That tag does not exist.", ResultErrorKind.NotFound);

    public static ResultError SlugTaken() => new("tag-slug-taken", "Another tag already uses this slug. Choose a different one.", ResultErrorKind.Conflict);

    public static ResultError InUse(int ticketCount) =>
        new("tag-in-use", $"This tag is on {ticketCount} tickets. Delete it with force to remove it from them first.", ResultErrorKind.Conflict);
}
```

`src/TechStrap.Application/Tags/DeleteTagRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Auditing;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Domain.Admin;
using TechStrap.Domain.Tickets;

namespace TechStrap.Application.Tags;

public interface IDeleteTagRequestHandler
{
    Task<Result> HandleAsync(Guid tagId, bool force, CancellationToken cancellationToken);
}

/// <summary>
/// DELETE /api/tags/{id} (Admin, D-030). A tag in use is refused with the ticket count unless <c>force</c> is set; then every ticket
/// carrying it records TagRemoved (reason tag-deleted) and the tag is deleted, all in one transaction.
/// </summary>
public sealed class DeleteTagRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    ITagRepository tags,
    ITicketRepository tickets,
    IAdminEventRepository adminEvents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IDeleteTagRequestHandler
{
    public async Task<Result> HandleAsync(Guid tagId, bool force, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result.Failure(actor.Errors[0]);
        }

        var tag = await tags.GetByIdAsync(tagId, cancellationToken);
        if (tag is null)
        {
            return Result.Failure(TagErrors.NotFound());
        }

        var carriers = await tickets.ListTicketIdsWithTagAsync(tagId, cancellationToken);
        if (carriers.Count > 0 && !force)
        {
            return Result.Failure(TagErrors.InUse(carriers.Count));
        }

        foreach (var ticketId in carriers)
        {
            var ticket = await tickets.GetByIdAsync(ticketId, cancellationToken)
                ?? throw new InvalidOperationException($"Ticket {ticketId} carries tag {tagId} but could not be loaded.");
            var detached = ticket.DetachDeletedTag(tagId, Actor.ForAgent(actor.Value.Id), clock);
            if (detached.IsFailure)
            {
                return detached.ToResult();
            }

            tickets.Update(ticket);
        }

        tags.Remove(tag);
        AdminAudit.Record(adminEvents, AdminEventType.TagDeleted, actor.Value, AdminSubjectType.Tag, tag.Id,
            new { slug = tag.Slug, detachedTicketCount = carriers.Count }, clock);
        return await scope.CommitAsync(cancellationToken);
    }
}
```

The other three handlers follow the same shape:

- **`ListTagsRequestHandler(ITagRepository tags)`** returns `[.. (await tags.ListAsync(ct)).Select(TagMapping.ToDto)]`.
- **`CreateTagRequestHandler`** takes `ICurrentAgentClaims`, `IAgentRepository`, `ITagRepository`, `IAdminEventRepository`, `IUnitOfWork` and `TimeProvider`.
  1. Call `Tag.Create(request.Slug, request.Name, request.Colour, clock)`. On failure, return `Result<TagDto>.Failure(created.Error!.ToError())`.
  2. Open the unit of work and resolve the actor.
  3. Call `tags.Add(tag)`, then audit `TagCreated` with `new { slug = tag.Slug }`.
  4. Commit. A `Duplicate` conflict becomes `TagErrors.SlugTaken()`.
  5. Return `TagMapping.ToDto(tag)`.
- **`UpdateTagRequestHandler`** takes the same dependencies as create.
  1. Open the unit of work, resolve the actor, then load the tag. A missing tag returns `TagErrors.NotFound()`.
  2. Work out `changed`: `"name"` if the name differs from `request.Name?.Trim()`, and `"colour"` if the color differs from `request.Colour?.Trim().ToUpperInvariant()`.
  3. Call `tag.Update(request.Name, request.Colour)`. On failure, return the converted Domain error.
  4. If nothing changed, return the DTO without committing.
  5. Otherwise call `tags.Update(tag)`, audit `TagUpdated` with `new { slug = tag.Slug, changed }`, commit, and return the DTO.

Give each handler a summary naming its route and its policy.

Run: `dotnet test --project tests/TechStrap.Application.Tests -c Release`
Expected: PASS.

- [ ] **Step 6: Add the controller and its delegation tests**

`src/TechStrap.Api/Controllers/TagsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Tags;
using TechStrap.Contracts.Tags;

namespace TechStrap.Api.Controllers;

/// <summary>Global tags. Agents read them for filters; admins manage them (D-022, D-030).</summary>
[ApiController]
[Route("api/tags")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class TagsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromServices] IListTagsRequestHandler listTags, CancellationToken cancellationToken) =>
        (await listTags.HandleAsync(cancellationToken)).ToActionResult(this, Ok);

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Create(CreateTagRequest request, [FromServices] ICreateTagRequestHandler createTag, CancellationToken cancellationToken) =>
        (await createTag.HandleAsync(request, cancellationToken)).ToActionResult(this, tag => StatusCode(StatusCodes.Status201Created, tag));

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Update(Guid id, UpdateTagRequest request, [FromServices] IUpdateTagRequestHandler updateTag, CancellationToken cancellationToken) =>
        (await updateTag.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Deletes a tag. A tag in use is a 409 unless <paramref name="force"/> is true (D-030).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Delete(Guid id, [FromServices] IDeleteTagRequestHandler deleteTag, CancellationToken cancellationToken, [FromQuery] bool force = false) =>
        (await deleteTag.HandleAsync(id, force, cancellationToken)).ToActionResult(this, NoContent);
}
```

Order the optional `force` parameter the same way Task 6 settled `page`/`pageSize`. If CA1068 required the `CancellationToken` to come last, make `force` a non-optional `[FromQuery] bool force`; it binds to `false` when the query string omits it.

In `TagsControllerTests`, add one delegation test per action. Each test asserts the result type and that the cancellation token is passed through. The `Delete` test also passes `force: true` and asserts the handler received `true`.

- [ ] **Step 7: Write the integration tests**

`tests/TechStrap.Infrastructure.IntegrationTests/DeleteTagIntegrationTests.cs` runs the real `DeleteTagRequestHandler` against Postgres, wired with the real repositories from `PersistenceTestHost` and a stub `ICurrentAgentClaims`. The handler type is public and lives in Application, which Infrastructure references.

1. Read `TicketScenario.cs` and `RecordSeed.cs` to see how existing tests create a product, a requester, an agent and tickets. Through those helpers, create an admin agent, a tag, one open ticket and one Closed ticket, both carrying the tag.
2. Build the handler with `new DeleteTagRequestHandler(stubClaims, agents, tags, tickets, adminEvents, unitOfWork, host.Clock)`, resolving each repository and the unit of work from one `host.CreateScope()`.

Write these tests:
- `Force_delete_detaches_open_and_closed_tickets_with_events`:
  - Call `HandleAsync(tagId, force: true)` and assert it succeeds.
  - `SELECT count(*) FROM ticket_tags WHERE tag_id = …` is 0.
  - `SELECT count(*) FROM tags WHERE id = …` is 0.
  - `SELECT count(*) FROM ticket_events WHERE type = 'TagRemoved'` is 2.
  - The Closed ticket's status is still `Closed`.
  - `admin_events` has one `TagDeleted` row.
- `Without_force_a_tag_in_use_is_refused_and_nothing_changes`:
  - The result is a Conflict with code `tag-in-use`.
  - Both `ticket_tags` rows remain, and the tag still exists.

Check the stored column and type names against the configurations before you write the SQL.

`tests/TechStrap.Api.Tests/Tags/TagEndpointTests.cs` uses admin and agent clients, as in the earlier endpoint tests. Write these tests:
- `An_admin_creates_updates_lists_and_deletes_an_unused_tag`: create returns 201 and the list contains the tag, update returns 200, delete returns 204, and the list no longer contains it.
- `A_duplicate_slug_is_409`.
- `A_bad_colour_is_400_with_a_colour_field_error`.
- `An_agent_can_list_tags_but_cannot_create_update_or_delete`: the list is 200; create, update and delete are each 403.

- [ ] **Step 8: Run all tests, build and commit**

Run each suite:

```bash
dotnet test --project tests/TechStrap.Domain.Tests -c Release
dotnet test --project tests/TechStrap.Application.Tests -c Release
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release
dotnet build TechStrap.slnx -c Release
```

Expected: every suite passes, the build has 0 warnings, and `has-pending-model-changes` is clean (no schema change).

```bash
git add src tests
git commit -m "feat(tags): tag management with forced delete that detaches tickets with events" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 12: Admin audit log endpoint

**Files:**
- Create: `src/TechStrap.Application/Persistence/AdminEventFilter.cs`
- Modify: `src/TechStrap.Application/Persistence/IAdminEventRepository.cs` (`ListAsync` takes a filter)
- Modify: `src/TechStrap.Infrastructure/Persistence/Repositories/AdminEventRepository.cs`
- Modify: `tests/TechStrap.Infrastructure.IntegrationTests/AdminEventRepositoryTests.cs` (move existing calls to the filter; add actor and as-of tests)
- Modify: `src/TechStrap.Application/Persistence/IAgentRepository.cs` (add `GetByIdsAsync`)
- Modify: `src/TechStrap.Infrastructure/Persistence/Repositories/AgentRepository.cs`
- Create: `src/TechStrap.Application/AdminEvents/ListAdminEventsRequestHandler.cs`
- Create: `src/TechStrap.Api/Controllers/AdminEventsController.cs`
- Create: `tests/TechStrap.Application.Tests/AdminEvents/ListAdminEventsRequestHandlerTests.cs`
- Create: `tests/TechStrap.Api.Tests/Controllers/AdminEventsControllerTests.cs`
- Create: `tests/TechStrap.Api.Tests/AdminEvents/AdminEventEndpointTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record AdminEventFilter(AdminSubjectType? SubjectType, Guid? ActorId, DateTimeOffset? AsOf);
  // IAdminEventRepository:
  Task<PagedResult<AdminEvent>> ListAsync(AdminEventFilter filter, int page, int pageSize, CancellationToken cancellationToken);
  // IAgentRepository:
  Task<IReadOnlyList<Agent>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
  public interface IListAdminEventsRequestHandler
  {
      Task<Result<PagedResponse<AdminEventDto>>> HandleAsync(string? subjectType, Guid? actorId, DateTimeOffset? asOf, int page, int pageSize, CancellationToken cancellationToken);
  }
  ```
- Route: `GET /api/admin-events?subjectType=&actorId=&asOf=&page=&pageSize=` (Admin).

`asOf` keeps paging stable while new events arrive. The Admin UI sends the time of its first page load with every later page, so events added after that time never shift the pages. With `asOf`, the repository returns only events whose `OccurredAt <= asOf`.

- [ ] **Step 1: Write the failing repository tests**

In `AdminEventRepositoryTests`, change every `ListAsync(subjectType, page, size, ct)` call to `ListAsync(new AdminEventFilter(subjectType, null, null), page, size, ct)`. Then add these tests in the file's existing style:
- `Events_can_be_filtered_by_actor`: two actors with two events each. Filtering by the first actor returns exactly that actor's two events, newest first.
- `An_as_of_time_hides_events_recorded_after_it`:
  1. Record two events at T.
  2. Advance the clock by one minute.
  3. Record one more event.
  4. Assert that `AsOf = T` returns 2 events and `TotalCount == 2`.
- `Events_with_the_same_time_page_in_a_stable_order`: record three events at the same clock time. The first page of size 2 and the second page of size 2 together hold all three ids, with none repeated. The existing ordering is `OccurredAt` descending, then `Id` descending.

In `AgentRepositoryTests`, add `GetByIdsAsync_returns_the_known_agents_and_ignores_unknown_ids`.

- [ ] **Step 2: Run the tests and confirm they fail, then implement the repository changes**

Run: `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release`
Expected: the build fails, because `AdminEventFilter` and `GetByIdsAsync` do not exist yet.

`src/TechStrap.Application/Persistence/AdminEventFilter.cs`:

```csharp
using TechStrap.Domain.Admin;

namespace TechStrap.Application.Persistence;

/// <summary>Admin audit filters. <see cref="AsOf"/> hides events recorded later, so paging stays stable while new events arrive.</summary>
public sealed record AdminEventFilter(AdminSubjectType? SubjectType, Guid? ActorId, DateTimeOffset? AsOf);
```

In `IAdminEventRepository`, replace `ListAsync` with:

```csharp
    /// <summary>Newest first (then id); filtered by subject type, actor and as-of time when given. Normalizes paging through Paging.</summary>
    Task<PagedResult<AdminEvent>> ListAsync(AdminEventFilter filter, int page, int pageSize, CancellationToken cancellationToken);
```

In `AdminEventRepository.ListAsync`, apply `Where`s for each non-null filter value, keeping the existing normalization and ordering:
- `if (filter.SubjectType is { } subjectType) query = query.Where(e => e.SubjectType == subjectType);`
- `ActorId`: `e.ActorId == actorId`
- `AsOf`: `e.OccurredAt <= asOf`

Add `GetByIdsAsync` to the interface (summary: "The agents with these ids; unknown ids are skipped.") and to `AgentRepository`:

```csharp
    public async Task<IReadOnlyList<Agent>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var records = await context.Set<AgentRecord>().AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken);
        return [.. records.Select(a => a.ToDomain())];
    }
```

Find every other caller of `IAdminEventRepository.ListAsync` with `git grep -n "ListAsync(" -- src tests | grep -i adminevent` and update it. Then run the Infrastructure tests again; expected: PASS.

- [ ] **Step 3: Write the failing handler tests, then implement the handler**

`tests/TechStrap.Application.Tests/AdminEvents/ListAdminEventsRequestHandlerTests.cs` covers:
- `Events_map_to_dtos_with_the_actor_name_as_label`: the actor's name, or their email when the name is null, becomes `ActorLabel`. Type and SubjectType are returned as their enum names.
- `An_event_whose_actor_no_longer_exists_has_no_label`.
- `A_known_subject_type_filter_is_passed_to_the_repository`:
  - The filter value is case-insensitive, so `"tag"` becomes `AdminSubjectType.Tag`.
  - Assert the received `AdminEventFilter`, including `ActorId` and `AsOf`.
- `An_unknown_subject_type_is_a_field_error`: target `subjectType`, code `admin-event-subject-type-invalid`. The repository is not called.

`src/TechStrap.Application/AdminEvents/ListAdminEventsRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.AdminEvents;
using TechStrap.Contracts.Paging;
using TechStrap.Domain.Admin;

namespace TechStrap.Application.AdminEvents;

public interface IListAdminEventsRequestHandler
{
    Task<Result<PagedResponse<AdminEventDto>>> HandleAsync(string? subjectType, Guid? actorId, DateTimeOffset? asOf, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>GET /api/admin-events (Admin, D-006, D-022): newest first, filterable by subject type and actor, stable with asOf.</summary>
public sealed class ListAdminEventsRequestHandler(IAdminEventRepository adminEvents, IAgentRepository agents) : IListAdminEventsRequestHandler
{
    public async Task<Result<PagedResponse<AdminEventDto>>> HandleAsync(
        string? subjectType,
        Guid? actorId,
        DateTimeOffset? asOf,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        AdminSubjectType? subject = null;
        if (!string.IsNullOrWhiteSpace(subjectType))
        {
            if (!Enum.TryParse<AdminSubjectType>(subjectType.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Result<PagedResponse<AdminEventDto>>.Failure(new ResultError(
                    "admin-event-subject-type-invalid",
                    $"Use one of: {string.Join(", ", Enum.GetNames<AdminSubjectType>())}.",
                    ResultErrorKind.Validation,
                    "subjectType"));
            }

            subject = parsed;
        }

        var found = await adminEvents.ListAsync(new AdminEventFilter(subject, actorId, asOf), page, pageSize, cancellationToken);
        var actorIds = found.Items.Select(e => e.ActorId).Distinct().ToList();
        var labels = (await agents.GetByIdsAsync(actorIds, cancellationToken)).ToDictionary(a => a.Id, a => a.Name ?? a.Email);

        return Result<PagedResponse<AdminEventDto>>.Success(new PagedResponse<AdminEventDto>(
            [.. found.Items.Select(e => new AdminEventDto(
                e.Id, e.Type.ToString(), e.ActorId, labels.GetValueOrDefault(e.ActorId), e.SubjectType.ToString(), e.SubjectId, e.PayloadJson, e.OccurredAt))],
            found.Page,
            found.PageSize,
            found.TotalCount));
    }
}
```

`Enum.TryParse` also accepts numeric strings, which is why the code checks `Enum.IsDefined`. Add a test row with the value `"99"` to show that a number outside the enum is rejected.

Run the Application tests; expected: PASS.

- [ ] **Step 4: Add the controller, its test and the endpoint test**

`src/TechStrap.Api/Controllers/AdminEventsController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.AdminEvents;
using TechStrap.Application.Persistence;

namespace TechStrap.Api.Controllers;

/// <summary>The admin audit log (Admin only, D-022).</summary>
[ApiController]
[Route("api/admin-events")]
[Authorize(Policy = AuthorizationPolicies.Admin)]
public sealed class AdminEventsController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromServices] IListAdminEventsRequestHandler listAdminEvents,
        CancellationToken cancellationToken,
        [FromQuery] string? subjectType = null,
        [FromQuery] Guid? actorId = null,
        [FromQuery] DateTimeOffset? asOf = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await listAdminEvents.HandleAsync(subjectType, actorId, asOf, page, pageSize, cancellationToken)).ToActionResult(this, Ok);
}
```

Order the parameters the same way as the Task 6 paged actions.

`AdminEventsControllerTests.List_DelegatesFiltersAndPassesCancellation` checks that every filter value and the token reach the handler.

`AdminEventEndpointTests` covers:
- `An_admin_change_appears_in_the_audit_log_with_the_actor_label`: an admin creates a tag through the API, then `GET /api/admin-events?subjectType=Tag` has one `TagCreated` item whose `ActorLabel` is the admin's name.
- `An_agent_cannot_read_the_audit_log`: the request returns 403.

- [ ] **Step 5: Run the tests, build and commit**

Run the Application, Infrastructure and Api test projects, then `dotnet build TechStrap.slnx -c Release`.
Expected: all tests pass, with 0 warnings.

```bash
git add src tests
git commit -m "feat(audit): admin event log endpoint with actor and as-of filters" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 13: Boundary, mapping, cancellation and access-coverage tests

**Files:**
- Create: `tests/TechStrap.Architecture.Tests/ControllerBoundaryRules.cs`
- Create: `tests/TechStrap.Architecture.Tests/ControllerBoundaryFixtures.cs`
- Create: `tests/TechStrap.Architecture.Tests/ControllerBoundaryTests.cs`
- Modify: `tests/TechStrap.Architecture.Tests/HandlerRules.cs` (add `FindUnapprovedDependencies`)
- Modify: `tests/TechStrap.Architecture.Tests/HandlerFixtures.cs` (add an unapproved-dependency fixture)
- Modify: `tests/TechStrap.Architecture.Tests/HandlerConstructorDependencyTests.cs`
- Create: `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs` (reflection helpers, `HandlerProxy`, result factory)
- Create: `tests/TechStrap.Api.Tests/Controllers/ResultMappingTests.cs`
- Create: `tests/TechStrap.Api.Tests/Controllers/CancellationPropagationTests.cs`
- Create: `tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs`

**Interfaces:**
- Consumes: every controller from Tasks 5 to 12, `ControllerTestContext`, `ApiTestDatabase`, `TestJwt`.
- Produces:
  - `ControllerBoundaryRules.FindViolations(IEnumerable<Type> controllers, Assembly handlerAssembly) : IReadOnlyList<string>`
  - `HandlerRules.FindUnapprovedDependencies(IEnumerable<Type> handlers, Assembly applicationAssembly) : IReadOnlyList<string>`
  - `ControllerActions.All()`, `ControllerActions.Key(method)`, `HandlerProxy`, `ControllerActions.ExpectedSuccess` (the coverage table)

- [ ] **Step 1: Write the controller boundary rule, its fixtures and its tests**

`tests/TechStrap.Architecture.Tests/ControllerBoundaryRules.cs`:

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TechStrap.Architecture.Tests;

/// <summary>
/// Api controllers are thin adapters (_template APPLICATION_ARCHITECTURE): every controller carries an authorization policy and has no
/// constructor dependencies. Every action takes exactly one [FromServices] handler interface from Application and a CancellationToken,
/// and never a repository, unit of work, DbContext, Infrastructure type or persistence record.
/// </summary>
public static class ControllerBoundaryRules
{
    public static IReadOnlyList<string> FindViolations(IEnumerable<Type> controllers, Assembly handlerAssembly)
    {
        var violations = new List<string>();
        foreach (var controller in controllers.Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract))
        {
            if (controller.GetCustomAttribute<AuthorizeAttribute>() is not { Policy: { Length: > 0 } })
            {
                violations.Add($"{controller.Name} must declare [Authorize(Policy = ...)]");
            }

            if (controller.GetConstructors().Any(constructor => constructor.GetParameters().Length > 0))
            {
                violations.Add($"{controller.Name} must not take constructor dependencies");
            }

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                         .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null))
            {
                var name = $"{controller.Name}.{action.Name}";
                var parameters = action.GetParameters();
                var handlers = parameters.Count(parameter =>
                    parameter.ParameterType.IsInterface
                    && parameter.ParameterType.Name.EndsWith("Handler", StringComparison.Ordinal)
                    && parameter.ParameterType.Assembly == handlerAssembly
                    && parameter.GetCustomAttribute<FromServicesAttribute>() is not null);
                if (handlers != 1)
                {
                    violations.Add($"{name} must take exactly one [FromServices] handler interface (found {handlers})");
                }

                if (parameters.All(parameter => parameter.ParameterType != typeof(CancellationToken)))
                {
                    violations.Add($"{name} must take a CancellationToken");
                }

                foreach (var parameter in parameters.Where(parameter => IsForbidden(parameter.ParameterType)))
                {
                    violations.Add($"{name} must not take {parameter.ParameterType.Name}");
                }
            }
        }

        return violations;
    }

    private static bool IsForbidden(Type type) =>
        type.Name is "IUnitOfWork" or "IUnitOfWorkScope"
        || (type.IsInterface && type.Name.StartsWith('I') && type.Name.EndsWith("Repository", StringComparison.Ordinal))
        || type.Name.EndsWith("Record", StringComparison.Ordinal)
        || (type.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ?? false)
        || (type.Namespace?.StartsWith("TechStrap.Infrastructure", StringComparison.Ordinal) ?? false);
}
```

`tests/TechStrap.Architecture.Tests/ControllerBoundaryFixtures.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TechStrap.Architecture.Tests.ControllerBoundaryFixtureTypes;

public interface IFixtureRequestHandler
{
    Task<int> HandleAsync(CancellationToken cancellationToken);
}

public interface IOtherFixtureRequestHandler
{
    Task<int> HandleAsync(CancellationToken cancellationToken);
}

public interface IFixtureRepository;

[Authorize(Policy = "Agent")]
public sealed class GoodBoundaryController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler, CancellationToken cancellationToken) => Task.FromResult<IActionResult>(Ok());
}

public sealed class NoPolicyController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler, CancellationToken cancellationToken) => Task.FromResult<IActionResult>(Ok());
}

[Authorize(Policy = "Agent")]
public sealed class RepositoryParameterController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler, [FromServices] IFixtureRepository repository, CancellationToken cancellationToken) =>
        Task.FromResult<IActionResult>(Ok());
}

[Authorize(Policy = "Agent")]
public sealed class TwoHandlersController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler first, [FromServices] IOtherFixtureRequestHandler second, CancellationToken cancellationToken) =>
        Task.FromResult<IActionResult>(Ok());
}

[Authorize(Policy = "Agent")]
public sealed class NoCancellationController : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler handler) => Task.FromResult<IActionResult>(Ok());
}

[Authorize(Policy = "Agent")]
public sealed class ConstructorDependencyController(IFixtureRequestHandler handler) : ControllerBase
{
    public Task<IActionResult> Get([FromServices] IFixtureRequestHandler other, CancellationToken cancellationToken) => Task.FromResult<IActionResult>(Ok(handler));
}
```

`tests/TechStrap.Architecture.Tests/ControllerBoundaryTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using TechStrap.Application;
using TechStrap.Architecture.Tests.ControllerBoundaryFixtureTypes;

namespace TechStrap.Architecture.Tests;

public sealed class ControllerBoundaryTests
{
    private static readonly System.Reflection.Assembly FixtureAssembly = typeof(ControllerBoundaryTests).Assembly;

    [Fact]
    public void Every_api_controller_is_a_thin_policy_guarded_adapter()
    {
        var controllers = typeof(TechStrap.Api.Program).Assembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type)).ToList();

        controllers.Count.ShouldBeGreaterThanOrEqualTo(4);
        ControllerBoundaryRules.FindViolations(controllers, typeof(ApplicationAssemblyMarker).Assembly).ShouldBeEmpty();
    }

    [Fact]
    public void A_good_controller_passes() =>
        ControllerBoundaryRules.FindViolations([typeof(GoodBoundaryController)], FixtureAssembly).ShouldBeEmpty();

    [Theory]
    [InlineData(typeof(NoPolicyController), "Authorize")]
    [InlineData(typeof(RepositoryParameterController), "IFixtureRepository")]
    [InlineData(typeof(TwoHandlersController), "exactly one")]
    [InlineData(typeof(NoCancellationController), "CancellationToken")]
    [InlineData(typeof(ConstructorDependencyController), "constructor")]
    public void A_bad_controller_is_flagged(Type controller, string expected) =>
        ControllerBoundaryRules.FindViolations([controller], FixtureAssembly).ShouldContain(violation => violation.Contains(expected));
}
```

- [ ] **Step 2: Add the approved-abstraction rule for handler constructors**

Add this method to `HandlerRules`. Add `using Microsoft.Extensions.Logging;` and `using Microsoft.Extensions.Options;`; both assemblies reach Architecture.Tests through the Api reference.

```csharp
    /// <summary>
    /// Handler constructors take only approved abstractions (02-ARCHITECTURE section 3.1): Application interfaces, TimeProvider,
    /// options and loggers. Anything else (HttpClient, concrete classes, framework services) is a violation.
    /// </summary>
    public static IReadOnlyList<string> FindUnapprovedDependencies(IEnumerable<Type> handlers, Assembly applicationAssembly) =>
        handlers
            .SelectMany(handler => handler.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => !IsApproved(parameter.ParameterType, applicationAssembly))
                .Select(parameter => $"{handler.Name} takes unapproved dependency {parameter.ParameterType.Name}"))
            .ToList();

    private static bool IsApproved(Type type, Assembly applicationAssembly)
    {
        if (type == typeof(TimeProvider) || type == typeof(ILogger) || (type.IsInterface && type.Assembly == applicationAssembly))
        {
            return true;
        }

        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(IOptions<>) || definition == typeof(IOptionsSnapshot<>) || definition == typeof(IOptionsMonitor<>) || definition == typeof(ILogger<>);
    }
```

In `HandlerFixtures.cs`, add a handler that takes an `HttpClient`. Model it on the existing `GoodSampleHandler` (sealed class plus public `I…` interface):

```csharp
public interface IHttpClientSampleHandler
{
    Task HandleAsync(CancellationToken cancellationToken);
}

public sealed class HttpClientSampleHandler(HttpClient client) : IHttpClientSampleHandler
{
    public Task HandleAsync(CancellationToken cancellationToken) => client.GetAsync("/", cancellationToken);
}
```

In `HandlerConstructorDependencyTests`, add three tests:

```csharp
    [Fact]
    public void Application_handlers_take_only_approved_abstractions()
    {
        var handlers = typeof(ApplicationAssemblyMarker).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Handler", StringComparison.Ordinal))
            .ToList();

        handlers.Count.ShouldBeGreaterThanOrEqualTo(18);
        HandlerRules.FindUnapprovedDependencies(handlers, typeof(ApplicationAssemblyMarker).Assembly).ShouldBeEmpty();
    }

    [Fact]
    public void A_handler_taking_an_http_client_is_flagged() =>
        HandlerRules.FindUnapprovedDependencies([typeof(HttpClientSampleHandler)], typeof(HandlerConstructorDependencyTests).Assembly)
            .ShouldHaveSingleItem().ShouldContain("HttpClient");

    [Fact]
    public void A_handler_taking_an_application_interface_and_a_clock_passes() =>
        HandlerRules.FindUnapprovedDependencies([typeof(GoodSampleHandler)], typeof(GoodSampleHandler).Assembly).ShouldBeEmpty();
```

If `GoodSampleHandler`'s constructor parameters are not interfaces from the fixture assembly or a `TimeProvider`, use a fixture that fits: add one with a fixture interface and a `TimeProvider`.

- [ ] **Step 3: Run the architecture tests**

Run: `dotnet test --project tests/TechStrap.Architecture.Tests -c Release`
Expected: PASS. If the real-assembly test reports a violation in a Task 5–12 controller or handler, fix that code; do not weaken the rule.

- [ ] **Step 4: Write the reflection helpers for controller actions**

`tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`:

```csharp
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.Common;

namespace TechStrap.Api.Tests.Controllers;

/// <summary>Finds every Api controller action and invokes it with a recording handler stand-in.</summary>
public static class ControllerActions
{
    /// <summary>The success status each action must return. Adding an action without a row here fails ResultMappingTests.</summary>
    public static readonly IReadOnlyDictionary<string, int> ExpectedSuccess = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["AgentsController.GetMe"] = 200,
        ["AgentsController.List"] = 200,
        ["AgentsController.Update"] = 200,
        ["AgentsController.GetMyNotificationPreferences"] = 200,
        ["AgentsController.UpdateMyNotificationPreferences"] = 204,
        ["AgentsController.UpdateMyProfile"] = 204,
        ["ProductsController.List"] = 200,
        ["ProductsController.Get"] = 200,
        ["ProductsController.Create"] = 201,
        ["ProductsController.Update"] = 200,
        ["ProductsController.ListApiKeys"] = 200,
        ["ProductsController.CreateApiKey"] = 201,
        ["ProductsController.RevokeApiKey"] = 204,
        ["TagsController.List"] = 200,
        ["TagsController.Create"] = 201,
        ["TagsController.Update"] = 200,
        ["TagsController.Delete"] = 204,
        ["AdminEventsController.List"] = 200,
    };

    public static IEnumerable<MethodInfo> All() =>
        typeof(TechStrap.Api.Program).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName && method.GetCustomAttribute<NonActionAttribute>() is null);

    public static string Key(MethodInfo action) => $"{action.DeclaringType!.Name}.{action.Name}";

    public static Type HandlerType(MethodInfo action) =>
        action.GetParameters().Single(parameter => parameter.GetCustomAttribute<FromServicesAttribute>() is not null).ParameterType;

    /// <summary>Invokes the action with sample arguments, the given handler and token; returns the action result or the thrown exception.</summary>
    public static async Task<IActionResult> InvokeAsync(MethodInfo action, object handler, CancellationToken cancellationToken)
    {
        var controller = (ControllerBase)typeof(ControllerTestContext).GetMethod(nameof(ControllerTestContext.For))!
            .MakeGenericMethod(action.DeclaringType!).Invoke(null, null)!;
        var arguments = action.GetParameters().Select(parameter => Argument(parameter, handler, cancellationToken)).ToArray();
        return await (Task<IActionResult>)action.Invoke(controller, arguments)!;
    }

    /// <summary>A completed Task of the handler's result type: success with a sample value, or failure with the error.</summary>
    public static object ResultTask(MethodInfo handleAsync, ResultError? error)
    {
        var resultType = handleAsync.ReturnType.GetGenericArguments()[0];
        object result;
        if (resultType == typeof(Result))
        {
            result = error is null ? Result.Success() : Result.Failure(error);
        }
        else
        {
            result = error is null
                ? resultType.GetMethod(nameof(Result<int>.Success))!.Invoke(null, [Sample(resultType.GetGenericArguments()[0])])!
                : resultType.GetMethod(nameof(Result<int>.Failure))!.Invoke(null, [error, Array.Empty<ResultError>()])!;
        }

        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType).Invoke(null, [result])!;
    }

    private static object? Argument(ParameterInfo parameter, object handler, CancellationToken cancellationToken)
    {
        var type = parameter.ParameterType;
        if (parameter.GetCustomAttribute<FromServicesAttribute>() is not null)
        {
            return handler;
        }

        if (type == typeof(CancellationToken))
        {
            return cancellationToken;
        }

        if (type == typeof(Guid))
        {
            return Guid.CreateVersion7();
        }

        if (type == typeof(int))
        {
            return 1;
        }

        if (type == typeof(bool))
        {
            return true;
        }

        return Nullable.GetUnderlyingType(type) is not null || type == typeof(string) ? null : Sample(type);
    }

    private static object Sample(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        }

        return RuntimeHelpers.GetUninitializedObject(type);
    }
}

/// <summary>A stand-in for any handler interface: records each call's arguments and returns what <see cref="Respond"/> builds.</summary>
public class HandlerProxy : DispatchProxy
{
    public Func<MethodInfo, object?> Respond { get; set; } = _ => null;

    public List<object?[]> Calls { get; } = [];

    public static (object Handler, HandlerProxy Proxy) For(Type handlerInterface)
    {
        var created = DispatchProxy.Create(handlerInterface, typeof(HandlerProxy));
        return (created, (HandlerProxy)created);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Calls.Add(args ?? []);
        return Respond(targetMethod!);
    }
}
```

`Result<T>.Failure` is declared as `(ResultError first, params ResultError[] additional)`. If reflection picks a different overload, select it explicitly with `GetMethod("Failure", [typeof(ResultError), typeof(ResultError[])])`.

- [ ] **Step 5: Write the mapping and cancellation tests**

`tests/TechStrap.Api.Tests/Controllers/ResultMappingTests.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.Common;

namespace TechStrap.Api.Tests.Controllers;

/// <summary>Every controller action maps each expected failure to the standard status and its success to the chosen response.</summary>
public sealed class ResultMappingTests
{
    public static TheoryData<string> Actions() => [.. ControllerActions.All().Select(ControllerActions.Key)];

    private static async Task<IActionResult> InvokeWithAsync(string key, ResultError? error)
    {
        var action = ControllerActions.All().Single(method => ControllerActions.Key(method) == key);
        var (handler, proxy) = HandlerProxy.For(ControllerActions.HandlerType(action));
        proxy.Respond = method => ControllerActions.ResultTask(method, error);
        return await ControllerActions.InvokeAsync(action, handler, CancellationToken.None);
    }

    [Fact]
    public void Every_action_has_an_expected_success_status() =>
        Actions().Select(row => (string)row[0]).Order().ShouldBe(ControllerActions.ExpectedSuccess.Keys.Order());

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Success_returns_the_chosen_status(string key)
    {
        var result = await InvokeWithAsync(key, null);

        var status = result switch
        {
            IStatusCodeActionResult { StatusCode: { } code } => code,
            _ => -1,
        };
        status.ShouldBe(ControllerActions.ExpectedSuccess[key], key);
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Failures_map_to_problem_details_statuses(string key)
    {
        (ResultErrorKind Kind, int Status)[] cases =
        [
            (ResultErrorKind.Validation, 400),
            (ResultErrorKind.Unauthenticated, 401),
            (ResultErrorKind.Forbidden, 403),
            (ResultErrorKind.NotFound, 404),
            (ResultErrorKind.Conflict, 409),
        ];

        foreach (var (kind, status) in cases)
        {
            var error = new ResultError("sample-code", "Sample message.", kind, kind == ResultErrorKind.Validation ? "name" : null);

            var result = (ObjectResult)await InvokeWithAsync(key, error);

            result.StatusCode.ShouldBe(status, $"{key} {kind}");
            if (kind == ResultErrorKind.Validation)
            {
                result.Value.ShouldBeOfType<ValidationProblemDetails>().Errors.ShouldContainKey("name");
            }
        }
    }
}
```

`tests/TechStrap.Api.Tests/Controllers/CancellationPropagationTests.cs`:

```csharp
namespace TechStrap.Api.Tests.Controllers;

/// <summary>Every controller action passes the request's cancellation token to its handler.</summary>
public sealed class CancellationPropagationTests
{
    [Theory]
    [MemberData(nameof(ResultMappingTests.Actions), MemberType = typeof(ResultMappingTests))]
    public async Task Each_action_passes_the_request_cancellation_token_to_its_handler(string key)
    {
        using var cancellation = new CancellationTokenSource();
        var action = ControllerActions.All().Single(method => ControllerActions.Key(method) == key);
        var (handler, proxy) = HandlerProxy.For(ControllerActions.HandlerType(action));
        proxy.Respond = method => ControllerActions.ResultTask(method, null);

        await ControllerActions.InvokeAsync(action, handler, cancellation.Token);

        proxy.Calls.ShouldHaveSingleItem()[^1].ShouldBe(cancellation.Token);
    }
}
```

- [ ] **Step 6: Write the coverage test for agent access on every route**

`tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Contracts.Agents;

namespace TechStrap.Api.Tests.Auth;

/// <summary>Every controller route refuses anonymous callers, callers outside the groups, and deactivated agents (D-029).</summary>
public sealed class AgentAccessCoverageTests(TestPostgres postgres)
{
    private static IReadOnlyList<(string Method, string Path)> Routes(ApiFactory factory) =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("api/", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => (method, "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment =>
                    segment.IsSimple && segment.Parts[0] is Microsoft.AspNetCore.Routing.Patterns.RoutePatternLiteralPart literal
                        ? literal.Content
                        : Guid.CreateVersion7().ToString())))))];

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path) =>
        await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = method is "GET" or "DELETE" ? null : new StringContent("{}", Encoding.UTF8, "application/json") },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_deactivated_agent_is_refused_on_every_agent_endpoint()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var client = factory.CreateClient().Bearer(TestJwt.Token("off", [TestJwt.AdminGroup], email: "off@example.com"));
        (await client.GetFromJsonAsync<AgentDto>("/api/agents/me", TestContext.Current.CancellationToken)).ShouldNotBeNull();
        await database.ExecuteAsync("UPDATE agents SET is_active = false WHERE oidc_subject = 'off'");
        var routes = Routes(factory);

        routes.Count.ShouldBeGreaterThanOrEqualTo(18);
        foreach (var (method, path) in routes)
        {
            using var response = await SendAsync(client, method, path);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
            (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("agent-inactive", customMessage: $"{method} {path}");
        }
    }

    [Fact]
    public async Task Anonymous_callers_get_401_and_callers_outside_the_groups_get_403_everywhere()
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        await using var factory = new ApiFactory(settings: database.Settings);
        using var anonymous = factory.CreateClient();
        using var outsider = factory.CreateClient().Bearer(TestJwt.Token("outsider", ["someone-else"]));

        foreach (var (method, path) in Routes(factory))
        {
            using var anonymousResponse = await SendAsync(anonymous, method, path);
            using var outsiderResponse = await SendAsync(outsider, method, path);
            anonymousResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {path}");
            outsiderResponse.StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {path}");
        }
    }
}
```

If a route parameter has a non-Guid constraint, generate a matching value. All routes in this phase use `:guid`.

- [ ] **Step 7: Run every test project, build and commit**

Run: `dotnet test --solution TechStrap.CI.slnf -c Release`, then `dotnet build TechStrap.slnx -c Release`.
Expected: PASS with 0 warnings.

```bash
git add tests
git commit -m "test(api): controller boundary, result mapping, cancellation and access coverage for every action" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 14: OpenAPI surface, self-host auth note and closing docs

**Files:**
- Create: `tests/TechStrap.Api.Tests/OpenApiSurfaceTests.cs`
- Create: `docs/self-hosting/AGENT-AUTHENTICATION.md`
- Modify: `README.md`, or the docs index that `RepositoryDocs.Tests.ps1` checks: link the two new docs (`docs/self-hosting/AGENT-AUTHENTICATION.md`, and `docs/development/DEV-DATA.md` from Task 9)
- Modify: `docs/architecture/02-ARCHITECTURE.md` (section 3.1: `IApiKeyHasher` is implemented by `ApiKeyHasher` (Infrastructure); `ICurrentAgentClaims` row as written in Task 1)
- Modify: `docs/architecture/PHASE-04-agent-auth-and-admin-config.md` (tick the delivered Deliverables, Tasks, Success Criteria and Boundary Validation boxes; mark N/A Razor rows as such)
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` (set PHASE-04 status to Complete; add D-029, D-030 and D-031 to Key decisions)

**Interfaces:** consumes every route from Tasks 5–12.

- [ ] **Step 1: Write the OpenAPI surface test**

`tests/TechStrap.Api.Tests/OpenApiSurfaceTests.cs`:

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Api.Tests;

/// <summary>The OpenAPI document lists exactly the controller routes, so typed clients (PHASE-07, PHASE-11) can be generated from it.</summary>
public sealed partial class OpenApiSurfaceTests
{
    [GeneratedRegex(@":[^}]+")]
    private static partial Regex RouteConstraint();

    [Fact]
    public async Task The_openapi_document_lists_every_controller_route()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));

        var documented = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(operation => $"{operation.Name.ToUpperInvariant()} {path.Name}"))
            .ToHashSet(StringComparer.Ordinal);
        var routed = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(method => $"{method} /{RouteConstraint().Replace(endpoint.RoutePattern.RawText!, string.Empty)}"))
            .ToHashSet(StringComparer.Ordinal);

        routed.Count.ShouldBeGreaterThanOrEqualTo(18);
        documented.ShouldBe(routed, ignoreOrder: true);
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter OpenApiSurfaceTests`
Expected: PASS. ASP.NET's document generator emits every controller action by default. If a path differs only in formatting (for example a trailing slash), normalize both sides the same way. Do not drop routes.

- [ ] **Step 3: Write the self-host auth note**

`docs/self-hosting/AGENT-AUTHENTICATION.md`:

````markdown
# Agent authentication

Agents sign in with your OpenID Connect provider. The TechStrap API validates the access token (JWT) and lets a person in only when
the token carries one of two groups (D-004, D-029):

| Setting | Default | Meaning |
| --- | --- | --- |
| `Authentication__JwtBearer__Authority` | (required) | Your issuer URL, for example `https://auth.example.com/application/o/techstrap/` |
| `Authentication__JwtBearer__Audiences__0` | (required) | The audience in TechStrap's access tokens (usually the client id) |
| `TECHSTRAP_AGENT_GROUP` | `techstrap-agents` | Members can work tickets |
| `TECHSTRAP_ADMIN_GROUP` | `techstrap-admins` | Members can also manage products, API keys, tags and agents |
| `TECHSTRAP_GROUP_CLAIM_TYPE` | `groups` | The token claim that lists group names |

Roles come from these groups only. To make someone an admin, add them to the admin group in your identity provider; to remove
access, remove them from both groups or turn them off in TechStrap's agent list. The first admin is simply the first person in the
admin group to sign in.

The token must also carry `sub` and `email`; TechStrap refuses sign-in without an email (`agent-email-required`).

## Authentik example

1. Create an OAuth2/OpenID provider for TechStrap. Use the scopes `openid`, `email` and `profile`.
2. Add a scope mapping that puts the user's group names in a `groups` claim. Authentik's default "authentik default OAuth Mapping:
   OpenID 'profile'" already includes `groups`.
3. Create the groups `techstrap-agents` and `techstrap-admins` and add people to them.
4. Set `Authentication__JwtBearer__Authority` to the provider's issuer and `Authentication__JwtBearer__Audiences__0` to its client id.

Other providers work the same way: release `sub`, `email` and a groups claim, and set `TECHSTRAP_GROUP_CLAIM_TYPE` if the claim
is not called `groups` (for example `roles`).
````

If the Authentik default mapping name differs from what you can verify, keep the instruction ("include the user's groups in a `groups` claim") and drop the quoted mapping name. This note is a draft for the PHASE-12 release guide. Only add a note that you have verified.

- [ ] **Step 4: Close the docs**

- **Links.** Link both new docs where `RepositoryDocs.Tests.ps1` expects documentation links. Read that test first; if it has no such rule, add both docs to the README's documentation list.
- **`02-ARCHITECTURE.md`.** In section 3.1, change the `IAccessTokenService, IApiKeyHasher` row to `IApiKeyHasher` → `ApiKeyHasher` (Infrastructure, PHASE-04), and note that `IAccessTokenService` follows in PHASE-05.
- **`PHASE-04-agent-auth-and-admin-config.md`.** Tick each delivered checkbox. The Razor rows are N/A. Leave PHASE-12's release-guide polish unticked: the note is drafted, which matches the deliverable wording "drafted".
- **`99-IMPLEMENTATION-ROADMAP.md`.** Set PHASE-04 to "Complete", and add "D-029 (roles from IdP groups only), D-030 (tag delete), D-031 (accent format only)" to its key decisions, matching how PHASE-03 was recorded.

- [ ] **Step 5: Verify and commit**

Run:
- `pwsh -File scripts/Invoke-ScriptTests.ps1`
- `dotnet test --solution TechStrap.CI.slnf -c Release`
- `dotnet build TechStrap.slnx -c Release`

Expected: everything passes and the build has 0 warnings.

```bash
git add tests/TechStrap.Api.Tests/OpenApiSurfaceTests.cs docs README.md
git commit -m "docs: agent authentication guide, OpenAPI surface test and PHASE-04 closing docs" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

### Task 15: Plain production error page in Admin and Portal (P04-T16)

**Files:**
- Create: `src/TechStrap.Admin/Components/Pages/Error.razor`
- Modify: `src/TechStrap.Admin/Components/Ui/UiCopy.cs` (add the unhandled-error copy)
- Modify: `src/TechStrap.Admin/Program.cs`
- Create: `src/TechStrap.Portal/Components/Pages/Error.razor`
- Modify: `src/TechStrap.Portal/Program.cs`
- Create: `tests/TechStrap.Admin.Tests/UnhandledErrorHostTests.cs`
- Create: `tests/TechStrap.Portal.Tests/UnhandledErrorHostTests.cs`

**Interfaces:**
- Produces a `/error` page in each host and wires `UseExceptionHandler("/error")` outside Development.
- Consumes `AdminFactory` and `PortalFactory` from the existing test projects, and the existing `ForcedStatusStartupFilter` pattern in `ErrorStatusHostTests`.

- [ ] **Step 1: Write the failing host tests**

`tests/TechStrap.Admin.Tests/UnhandledErrorHostTests.cs`. Read `ErrorStatusHostTests.cs` first and reuse its factory and startup-filter style.

```csharp
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace TechStrap.Admin.Tests;

/// <summary>An unhandled exception in production shows a plain error page: cause and next step, no humor (BRAND.md section 3).</summary>
public sealed class UnhandledErrorHostTests
{
    private sealed class ThrowingStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/__test/throw", branch => branch.Run(_ => throw new InvalidOperationException("boom")));
        };
    }

    [Fact]
    public async Task An_unhandled_exception_returns_500_with_the_plain_error_page()
    {
        await using var factory = new AdminFactory("Production").WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ThrowingStartupFilter>()));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/__test/throw", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        html.ShouldContain("Something went wrong on our side.");
        html.ShouldNotContain("ts-window");
        html.ShouldNotContain("ERROR 404");
        html.ShouldNotContain("fell out of its strap");
        html.ShouldNotContain("boom");
    }
}
```

Write the Portal version the same way, in `tests/TechStrap.Portal.Tests/UnhandledErrorHostTests.cs` with namespace `TechStrap.Portal.Tests`, using `PortalFactory`. It asserts the Portal copy `"Something went wrong."` and that the page contains no `ts-window`, no `boom`, and no `TechStrap` mascot or brand-moment text.

`AdminFactory` and `PortalFactory` may be `internal sealed`, may not accept `WithWebHostBuilder` chaining, and may already apply `TRUSTEDPROXY__TRUSTEDNETWORKS__0` for Production. If a factory does not support chaining, add a constructor parameter `Action<IServiceCollection>? configureServices` and apply it in `ConfigureWebHost`. That follows the Api `HostFactory` pattern.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release --filter UnhandledErrorHostTests` and `dotnet test --project tests/TechStrap.Portal.Tests -c Release --filter UnhandledErrorHostTests`
Expected: FAIL. There is no exception handler yet, so the response is an empty 500 and does not contain the copy.

- [ ] **Step 3: Add the pages and the exception handler**

In `src/TechStrap.Admin/Components/Ui/UiCopy.cs`, add these constants next to the existing error constants:

```csharp
    public const string UnhandledErrorTitle = "Something went wrong on our side.";
    public const string UnhandledErrorDescription = "That request didn't finish. Try again, and tell an admin if it keeps happening.";
```

`src/TechStrap.Admin/Components/Pages/Error.razor`:

```razor
@page "/error"
@using TechStrap.Admin.Components.Ui

<PageTitle>Error</PageTitle>

<section class="error-page" role="alert">
    <h1>@UiCopy.UnhandledErrorTitle</h1>
    <p>@UiCopy.UnhandledErrorDescription</p>
    <p><a href="/">@UiCopy.ErrorHomeLabel</a></p>
</section>
```

The page holds no C#, so no `.razor.cs` file is needed. Use the same `@layout` as `NotFound.razor`, but no `BrandWindow` and no mascot.

`src/TechStrap.Portal/Components/Pages/Error.razor`:

```razor
@page "/error"

<PageTitle>Error</PageTitle>

<section class="shell-placeholder" role="alert">
    <h1>Something went wrong.</h1>
    <p>We couldn't finish that request. Try again in a minute; if it keeps happening, contact support.</p>
    <p><a href="/">Back to the start</a></p>
</section>
```

Use the Portal layout that `NotFound.razor` uses.

In both `Program.cs` files, add these lines **before** `app.UseStatusCodePagesWithReExecute("/not-found", ...)`:

```csharp
if (!app.Environment.IsDevelopment())
{
    // Plain error page for unhandled exceptions (BRAND.md section 3); the branded window is for the Admin 404 only.
    app.UseExceptionHandler("/error", createScopeForErrors: true);
}
```

- [ ] **Step 4: Run the tests and confirm they pass, including the existing error tests**

Run: `dotnet test --project tests/TechStrap.Admin.Tests -c Release` and `dotnet test --project tests/TechStrap.Portal.Tests -c Release`
Expected: PASS. The existing `ErrorStatusHostTests` (forced 500 and 403 with empty bodies) and `NotFoundHostTests` must still pass. If the status-code-page disabling middleware swallows the re-executed `/error` page, move `UseExceptionHandler` so that the exception handler wraps the whole pipeline, and re-run both test classes.

- [ ] **Step 5: Build and commit**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: 0 warnings.

```bash
git add src/TechStrap.Admin src/TechStrap.Portal tests/TechStrap.Admin.Tests tests/TechStrap.Portal.Tests
git commit -m "feat(ui): plain production error page for unhandled exceptions in Admin and Portal" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi"
```

---

## Final verification (after Task 15)

- [ ] `dotnet build TechStrap.slnx -c Release`: 0 warnings, 0 errors.
- [ ] `dotnet test --solution TechStrap.slnx`: all pass (Docker running). Report per-project counts.
- [ ] `pwsh -File scripts/Invoke-ScriptTests.ps1`: all pass.
- [ ] `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api`: clean. This phase adds no migration.
- [ ] `docker compose up -d --build --wait`: all services healthy. Run `docker compose down` afterwards, **never** with `-v`.
- [ ] `git grep -n "BOOTSTRAP" -- . ':!docs/architecture/04-DECISION-LOG.md' ':!docs/superpowers'`: no matches.
- [ ] `git ls-files | grep -E "^\.superpowers/"`: no matches.
