# PHASE-04: Agent Auth and Admin Config API

## Objective

Agents can sign in with an OIDC JWT, are gated by a configured group claim, are provisioned on first call, and roles come from IdP groups only (D-029). Admins can manage agents, products (with branding), trusted and public API keys, and tags through the API, with every change audited in `AdminEvent`, and expected failures mapped to ProblemDetails. This phase introduces the handler, Result and transport-mapping conventions that PHASE-05 and PHASE-06 reuse.

## Dependencies

- **Depends on:** [PHASE-03](PHASE-03-domain-and-persistence.md) (repositories, `IUnitOfWork`, schema, seeder).
- **Unblocks:** [PHASE-05](PHASE-05-intake-email-worker.md); PHASE-07 settings screens (via PHASE-06).
- **External prerequisites:** an OIDC provider for integration checks (Authentik from `syntax-circus-authentik` for the owner; tests use a signing-key fake, not a live IdP); issuer authority, audience, group claim type and group names configured in `.env.example`.

## Architecture Decisions

- Authentication: `SyntaxCircus.AspNetCore.Authentication` JWT bearer, authority and audience from configuration. Two authorization policies: `Agent` requires the claim value in `TECHSTRAP_AGENT_GROUP` or `TECHSTRAP_ADMIN_GROUP`; `Admin` requires `TECHSTRAP_ADMIN_GROUP`. Both policies refuse an agent whose stored row is deactivated (D-029). Per D-022, product, API key, agent and tag management and the audit log are Admin-only; product, tag and active-agent lists are Agent-readable (agents need them for filters and assignment). A user with neither claim gets 403 on every agent endpoint, including `GET /api/agents/me`. Policy names and claim settings are constants and bound options (validated on start).
- Role source of truth (D-029): the group claim alone. The stored `Agent.Role` mirrors the claim at each `GET /api/agents/me`. `UpdateAgentRequestHandler` changes only `IsActive`.
- Provisioning on first call: `GET /api/agents/me` upserts the `Agent` from token claims (subject, name, email) and returns the profile (`AgentDto`). No separate registration endpoint.
- Handlers follow _template `APPLICATION_ARCHITECTURE.md`: one named handler and `I...Handler` interface per operation; handlers accept `TechStrap.Contracts` request records directly (D-016: Contracts is dependency-free, so any transport-only detail is mapped by the controller into the request or a small Application-owned model), identity through `ICurrentAgentClaims`, outcomes as `Result`/`Result<T>`. Controllers inject handlers with `[FromServices]` on the action and map Results with `SyntaxCircus.AspNetCore.Common` ProblemDetails helpers, choosing the success response explicitly (`Ok`, `Created`, `NoContent`).
- Contracts (`TechStrap.Contracts`): DTOs end in `Dto`/`Request`/`Response` (`AgentDto`, `ProductDto`, `ProductBrandingDto`, `ProductApiKeyDto`, `CreateProductApiKeyResponse` carrying the plaintext key exactly once, `TagDto`, `AdminEventDto`, `UpdateAgentRequest`, ...). Constants shared with clients (key prefix format, max lengths) live in Contracts only if clients validate them.
- API keys: generated server-side with a recognizable prefix and 256-bit random secret; only prefix and hash stored via `IApiKeyHasher`; plaintext shown once at creation. Kinds: `Trusted` (server-side; later may set external user ref and trusted metadata) and `Public` (client-embedded; create-only; per key+IP rate-limited; metadata flagged untrusted). Kind is immutable after creation; revoke rather than edit. A product may have several keys of each kind.
- Product branding (name, logo reference, accent color, from-name/reply-to) is updated through `UpdateProductRequestHandler`; accent validated for format only (D-031); the derived on-accent and ink colors are returned in `ProductBrandingDto`. Logo upload uses `SyntaxCircus.Storage` in PHASE-05; this phase accepts a logo URL or storage key string only. **Assumption.**
- `AdminEvent` is written in the same transaction as the change (via `IUnitOfWork`). The audit event payload never contains plaintext keys. Reads through `GET /api/admin-events` (paged, filter by entity type and actor), Admin-only. PHASE-06 writes the same event type for erase-requester, delete-ticket and dead-letter retry/discard (D-006, D-022), so the type constants and payload shape defined here must allow those subjects.
- Tags are global with unique slug; deleting a tag in use is rejected with `409 tag-in-use` unless `force=true`, which detaches the tag from every ticket with `TagRemoved` events (D-030).
- Errors: validation to 400 problem details with field errors, not-found to 404, conflict to 409, forbidden to 403. Exact mapping is the standard `SyntaxCircus.AspNetCore.Common` mapping; tests assert it per handler.
- Abstractions introduced in this phase: `ICurrentAgentClaims` (Application-owned identity: subject, name, email, group-derived role) and `IApiKeyHasher`, plus the Api options type `AgentAccessOptions` (group names and claim type).

## Application Boundaries

Follow _template `APPLICATION_ARCHITECTURE.md` (not copied into this repo). All handlers return `Result`/`Result<T>`; controllers pass `HttpContext.RequestAborted` as the cancellation token. `ICurrentAgentClaims` is implied in every row that needs the caller.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| `GET /api/agents/me` | `GetCurrentAgentRequestHandler` (provisions agent on first call, mirrors the group-derived role) | `IAgentRepository`, `IUnitOfWork`, `ICurrentAgentClaims`, `TimeProvider` | EF `AgentRepository`, `UnitOfWork`, Api `ClaimsCurrentAgentClaims` | 200 `AgentDto`; 403 if inactive | Mandatory flow |
| `GET /api/agents` | `ListAgentsRequestHandler` | `IAgentRepository`, `ICurrentAgentClaims` | EF `AgentRepository` | 200 paged `AgentDto`; Agent policy sees active agents only (id, name) for assignment, Admin sees all with role and active flag (D-022) | Mandatory flow |
| `PUT /api/agents/{id}` | `UpdateAgentRequestHandler` (active only, D-029) | `IAgentRepository`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentAgentClaims`, `TimeProvider` | EF repositories, `UnitOfWork` | 200 `AgentDto`; 404; 409 (last active admin cannot be removed) | Mandatory flow |
| `PUT /api/agents/me/notification-preferences` | `UpdateNotificationPreferencesRequestHandler` | `IAgentRepository`, `IUnitOfWork`, `ICurrentAgentClaims` | EF `AgentRepository`, `UnitOfWork` | 204; 400 for unknown product | Mandatory flow |
| `GET /api/agents/me/notification-preferences` | `GetMyNotificationPreferencesRequestHandler` | `IAgentRepository`, `IProductRepository`, `ICurrentAgentClaims` | EF repositories | 200 `NotificationPreferenceDto[]` (every active product, default off) | Mandatory flow |
| `PUT /api/agents/me/profile` | `UpdateMyProfileRequestHandler` (sets or clears `public_display_name`, D-024) | `IAgentRepository`, `IUnitOfWork`, `ICurrentAgentClaims` | EF `AgentRepository`, `UnitOfWork` | 204; 400 invalid name | Mandatory flow |
| `GET /api/products` | `ListProductsRequestHandler` | `IProductRepository` | EF `ProductRepository` | 200 `ProductDto[]` | Mandatory flow |
| `GET /api/products/{id}` | `GetProductRequestHandler` | `IProductRepository` | EF `ProductRepository` | 200; 404 | Mandatory flow |
| `POST /api/products` | `CreateProductRequestHandler` | `IProductRepository`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentAgentClaims`, `TimeProvider` | EF repositories, `UnitOfWork` | 201 `ProductDto`; 409 duplicate key; 400 | Mandatory flow |
| `PUT /api/products/{id}` | `UpdateProductRequestHandler` (incl. branding) | `IProductRepository`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentAgentClaims`, `TimeProvider` | EF repositories, `UnitOfWork` | 200; 404; 409 concurrency; 400 (color format, D-031) | Mandatory flow |
| `GET /api/products/{id}/api-keys` | `ListProductApiKeysRequestHandler` | `IProductRepository` | EF `ProductRepository` | 200 `ProductApiKeyDto[]` (no secrets) | Mandatory flow |
| `POST /api/products/{id}/api-keys` | `CreateProductApiKeyRequestHandler` | `IProductRepository`, `IApiKeyHasher`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentAgentClaims`, `TimeProvider` | EF repositories, `ApiKeyHasher`, `UnitOfWork` | 201 `CreateProductApiKeyResponse` (plaintext once); 404; 400 | Mandatory flow |
| `DELETE /api/products/{id}/api-keys/{keyId}` | `RevokeProductApiKeyRequestHandler` | `IProductRepository`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentAgentClaims`, `TimeProvider` | EF repositories, `UnitOfWork` | 204; 404 | Mandatory flow |
| `GET /api/tags` | `ListTagsRequestHandler` | `ITagRepository` | EF `TagRepository` | 200 `TagDto[]` | Mandatory flow |
| `POST /api/tags` | `CreateTagRequestHandler` | `ITagRepository`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentAgentClaims`, `TimeProvider` | EF repositories, `UnitOfWork` | 201; 409 duplicate slug; 400 | Mandatory flow |
| `PUT /api/tags/{id}` | `UpdateTagRequestHandler` | same as create | same as create | 200; 404; 409 | Mandatory flow |
| `DELETE /api/tags/{id}` | `DeleteTagRequestHandler` | same as create | same as create | 204; 404; 409 if in use | Mandatory flow |
| `GET /api/admin-events` | `ListAdminEventsRequestHandler` | `IAdminEventRepository` | EF `AdminEventRepository` | 200 paged `AdminEventDto`; Admin policy | Mandatory flow |
| `/health/live`, `/health/ready`, `/openapi/v1.json` | Exempt: framework operational endpoints (PHASE-01) | n/a | n/a | n/a | Exempt |

Reads (`List...`, `Get...`) are handlers too even when trivial: they own authorization-adjacent shaping and keep controllers free of repository calls. Do not introduce pass-through services.

Handlers must not depend on HTTP objects, EF types, concrete infrastructure, or transport response types. Link an approved decision for every exception.

## Razor Component Boundaries

Follow _template `RAZOR_COMPONENT_ARCHITECTURE.md`. ViewModels are Razor-only and feature-local; API contracts use DTO names.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| N/A — no Razor in this phase | n/a | n/a | n/a | DTOs defined here in `TechStrap.Contracts` are consumed by Admin in PHASE-07 through typed clients; Admin maps them to its own ViewModels |

## Syntax Circus Packages

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.AspNetCore.Authentication` | JWT bearer and policy helpers | Agent authentication and group-claim policies | `AgentAuthTests`: valid token with group is 200, no group is 403, bad signature or audience is 401 |
| `SyntaxCircus.AspNetCore.Common` | ProblemDetails mapping for `Result`, correlation id | Result-to-HTTP mapping used by every controller | `ResultMappingTests` for validation, not found, conflict, forbidden |
| `SyntaxCircus.Common` | `Result`/`Result<T>` | Handler outcomes | Handler unit tests assert Result kinds; identity comes from the Application-owned `ICurrentAgentClaims`, not `ICurrentUserService` (no groups) |

Keys are hashed with .NET primitives behind `IApiKeyHasher`; no extra package. Versions are in `03-PACKAGE-MAP.md`.

Record the exact package version in the linked package map. In the foundation phase, lock every selected version in `Directory.Packages.props`.

## Deliverables

- [x] Contracts DTOs and requests for agents, products, keys, tags, admin events
- [x] 18 handlers with interfaces and application request models (`TechStrap.Application`)
- [x] Controllers `AgentsController`, `ProductsController`, `TagsController`, `AdminEventsController` using `[FromServices]`
- [x] JWT auth, group-claim policies, group-claim options (validated on start) in the API
- [x] `IApiKeyHasher` implementation and `IAdminEventRepository` implementation
- [x] OpenAPI document includes the new endpoints
- [x] Handler unit tests, controller tests, integration tests, `.env.example` updated
- [x] Self-host auth note (Authentik example) drafted in `docs/self-hosting/AGENT-AUTHENTICATION.md`; the release-guide polish stays with [PHASE-12](PHASE-12-release-hardening.md)

## Actionable Tasks

Each handler task writes `{Handler}Tests` (substitutes for repositories, fake `TimeProvider`) first.

- [x] **P04-T01** Add Contracts DTOs and request types for agents, products (with `ProductBrandingDto`), API keys, tags and admin events with naming and serialization tests (`ContractNamingTests` asserts every public type under `Contracts` ends in `Dto`, `Request` or `Response`)
  - **Depends on:** none (inside this phase)
  - **Validation:** `ContractNamingTests` pass in `TechStrap.Architecture.Tests`
- [x] **P04-T02** Add `AgentAccessOptions` (agent group, admin group, claim type) with validation, JWT bearer wiring and the two policies in the Api; write `AgentAuthTests` using a locally signed test JWT
  - **Depends on:** none
  - **Validation:** `AgentAuthTests` cover missing token (401), token without group (403), agent group (200), admin-only route with agent token (403), misconfigured options fail startup, group claims delivered as repeated claims, a JSON array string or a delimited list
- [x] **P04-T03** Implement `ICurrentAgentClaims` for ASP.NET (subject, email, name, group-derived role) and `ClaimsCurrentAgentClaimsTests`
  - **Depends on:** P04-T02
  - **Validation:** tests with a built `ClaimsPrincipal` return expected subject, email, name and group-derived role; Application has no reference to `HttpContext` (architecture test green)
- [x] **P04-T04** Implement `GetCurrentAgentRequestHandler` (`GetCurrentAgentRequestHandlerTests`: first call provisions with the group-derived role, second call updates name, email and role, a token without an email is refused (`agent-email-required`), an inactive agent is forbidden) and `AgentsController.GetMe`
  - **Depends on:** P04-T01, P04-T03
  - **Validation:** handler tests pass; `AgentsControllerTests.GetMe_DelegatesAndPassesCancellation` and a Testcontainers integration test provisions the row once under two concurrent first calls
- [x] **P04-T05** Implement `ListAgentsRequestHandler` (Agent: active agents only; Admin: all) and `UpdateAgentRequestHandler` with `AdminEvent`s and the "last active admin" protection; `UpdateAgent` action with Admin policy
  - **Depends on:** P04-T04
  - **Validation:** `UpdateAgentRequestHandlerTests` cover deactivate and reactivate, last-admin conflict, concurrent deactivation of the last two admins leaves one active admin, audit event written; controller test shows 403 for an Agent-only caller on `PUT /api/agents/{id}` and a filtered active-only list for an Agent on `GET /api/agents`
- [x] **P04-T06** Implement `UpdateNotificationPreferencesRequestHandler` (per-product new-ticket alert opt-in) and its endpoint
  - **Depends on:** P04-T04
  - **Validation:** `UpdateNotificationPreferencesRequestHandlerTests` cover enabling, disabling, unknown product (400), idempotent repeat; integration test persists preferences
- [x] **P04-T07** Implement `ListProductsRequestHandler`, `GetProductRequestHandler`, `CreateProductRequestHandler`, `UpdateProductRequestHandler` (branding, accent format validation, concurrency) and `ProductsController`
  - **Depends on:** P04-T01, P04-T02
  - **Validation:** one `*RequestHandlerTests` class per handler; `ProductsControllerTests` assert 201 with location, 409 duplicate key, 409 stale concurrency token, 400 malformed accent color; every write produces an `AdminEvent`
- [x] **P04-T08** Implement `IApiKeyHasher` (generate prefix plus 256-bit secret, hash, constant-time verify) with `ApiKeyHasherTests`
  - **Depends on:** none
  - **Validation:** tests assert uniqueness of 1000 generated keys, constant-time compare helper used, hash never equals plaintext, verify accepts only the correct key
- [x] **P04-T09** Implement `ListProductApiKeysRequestHandler`, `CreateProductApiKeyRequestHandler`, `RevokeProductApiKeyRequestHandler` (kinds `Trusted` and `Public`)
  - **Depends on:** P04-T07, P04-T08
  - **Validation:** handler tests assert plaintext only in the create response, listing never exposes hash or secret, revoked key flagged, audit event excludes secret; integration test confirms only hash and prefix are stored
- [x] **P04-T10** Implement `ListTagsRequestHandler`, `CreateTagRequestHandler`, `UpdateTagRequestHandler`, `DeleteTagRequestHandler` and `TagsController`
  - **Depends on:** P04-T01, P04-T02
  - **Validation:** handler tests cover duplicate slug (409), delete in use (409), color format; audit events written
- [x] **P04-T11** Implement `IAdminEventRepository` (infrastructure) and `ListAdminEventsRequestHandler` with paging and filters, `AdminEventsController`
  - **Depends on:** P04-T05
  - **Validation:** `AdminEventRepositoryTests` and `ListAdminEventsRequestHandlerTests` pass; endpoint is Admin-only; newest first and stable paging under inserts
- [x] **P04-T12** Add `ResultMappingTests` for all controllers (validation 400 with field errors, not found 404, conflict 409, forbidden 403) and `CancellationPropagationTests` proving each controller passes `RequestAborted` to its handler
  - **Depends on:** P04-T05, P04-T07, P04-T09, P04-T10, P04-T11
  - **Validation:** both test classes pass and enumerate every controller action through reflection so a new action without a test fails
- [x] **P04-T13** Extend the architecture tests: every Api controller action (except exempt endpoints) has `[FromServices]` handler parameters and no repository, `DbContext` or `IUnitOfWork` parameter; handler constructors take only approved abstractions
  - **Depends on:** P04-T12
  - **Validation:** `ControllerBoundaryTests` and `HandlerConstructorDependencyTests` pass and fail on a deliberately bad fixture
- [x] **P04-T14** (delivered; the `docker compose up` JWT check was replaced by integration tests against a locally signed issuer, `TestJwt`) Update `.env.example` files, OpenAPI document and the dev seeder (dev API keys hashed through `IApiKeyHasher` with documented dev plaintexts); add a self-host auth note
  - **Depends on:** P04-T11
  - **Validation:** `EnvExampleCompletenessTests` pass; `/openapi/v1.json` lists all new routes (`OpenApiSurfaceTests` compares route list to controllers); `docker compose up` with a test JWT gets 200 on `/api/agents/me`
- [x] **P04-T15** (D-024) Add `UpdateMyProfileRequest` (`PublicDisplayName`, nullable) and `AgentDto.PublicDisplayName` to Contracts, a shared public-name format constant (`{0} from {1} Support`) for the Admin preview, and implement `UpdateMyProfileRequestHandler` with `AgentsController.UpdateMyProfile` (`PUT /api/agents/me/profile`, Agent policy). A new handler is needed because `UpdateNotificationPreferencesRequestHandler` is a per-product alert opt-in
  - **Depends on:** P04-T04, P03-T17
  - **Validation:** `UpdateMyProfileRequestHandlerTests` cover set, change, clear (null and blank), over-long and `@` names (400 with field error), deactivated agent forbidden, and that only the caller's own record changes; controller test shows delegation with the cancellation token; integration test persists the value and `GET /api/agents/me` returns it; `ContractNamingTests` and `OpenApiSurfaceTests` pass; a parity test shows the Contracts format constant and `AgentPublicIdentity.Resolve` produce the same string
- [x] **P04-T16** (PHASE-02 carry-over) Add a production `UseExceptionHandler` with a plain, no-humour error page in Admin and Portal (cause plus next step, BRAND.md section 3); the branded 404 is for 404 only
  - **Depends on:** P04-T02
  - **Validation:** a host test forcing an unhandled exception in Admin and Portal returns 500 with the plain error page, no `ts-window` and no 404 copy

## Success Criteria

- [x] All 18 handlers listed in the boundary table exist with interfaces, and every controller action delegates to exactly one of them.
- [x] A token without the configured group claim receives 403 on every agent endpoint; a token with it receives 200 on `GET /api/agents/me` and the agent row is created once.
- [x] Roles come from IdP groups only; a deactivated agent gets 403 on every agent endpoint (D-029).
- [x] Product, key, tag and agent changes each write an `AdminEvent` in the same transaction; no event or response after creation contains a plaintext API key.
- [x] `Trusted` and `Public` keys can be created and revoked per product; only hashes are stored.
- [x] An agent can set, change and clear their own public display name through `UpdateMyProfileRequestHandler`; invalid names are rejected with field errors (D-024).
- [x] Expected failures produce the specified ProblemDetails status codes (`ResultMappingTests`).
- [ ] `dotnet test` is green including `ControllerBoundaryTests`, `HandlerConstructorDependencyTests` and `ContractNamingTests`; CI is green. (Tests pass locally; CI runs on the pull request.)

## Boundary Validation

- [x] Application use-case entry points delegate to the named handlers listed above.
- [x] Framework-owned operational or static exemptions execute no application workflow.
- [x] Handler constructor dependencies contain only approved abstractions.
- [x] Persistence and integration entities do not cross infrastructure boundaries.
- [x] Cancellation reaches asynchronous handler dependencies.
- [x] Expected outcomes and transport mapping have focused tests.
- [x] Infrastructure implementations have integration coverage where applicable.
- [x] Inline Razor components contain only simple parameters and, at most, one
      trivial synchronous `EventCallback`-forwarding callback. (N/A.)
- [x] Every component beyond the inline ceiling uses paired `.razor` and
      `.razor.cs` files, with all C# in code-behind. (N/A.)
- [x] Each Razor ViewModel is feature-local and presentation-only; the recorded
      direct-model decision does not expose an API ViewModel. (N/A.)
- [x] A factory or presentation service is used only for non-trivial mapping,
      asynchronous assembly, or multiple dependencies. (N/A.)
- [x] API request and response contracts use DTO names and contracts, never
      Razor ViewModels.
- [x] Repeated or business-meaningful literals are named constants at the
      right scope, not bare magic values (policy names, claim types, key prefix, page sizes).
- [x] Duplicated-looking logic across flows was evaluated for genuine
      divergence before extracting (or intentionally not extracting) a shared
      abstraction (the four tag handlers and four product handlers look alike but have distinct rules; extract only the audit-event helper if the duplication is identical).

## Risks and Open Questions

- [x] Claim-versus-stored-role precedence (claim wins) is an **Assumption**; confirm with the owner and log it. Resolved: D-029.
- [x] Authentik group claim name and `email_verified` availability differ between IdPs; make the claim type configurable and document it. Resolved: `TECHSTRAP_GROUP_CLAIM_TYPE`.
- [x] Deleting an in-use tag: reject versus detach (**Assumption**: reject unless forced). Resolved: D-030.
- [ ] Follow-up: the OpenAPI document has no bearer security scheme, so generated clients (PHASE-07, PHASE-11) do not know the endpoints need a token. Adding one is out of scope for this phase.
- [ ] Logo handling for products (URL versus uploaded file) is deferred to PHASE-05/PHASE-07.
- [x] `IAdminEventRepository` is now listed in `02-ARCHITECTURE.md` section 3.1 (resolved).
- [x] Admin-only versus Agent-readable product list: Agents need `ListProductsRequestHandler` for filters; this phase makes it Agent policy. **Assumption.** Resolved: D-022, agents see active products only.
- [x] Carried forward from the PHASE-03 final review (delivered in Task 6, last-admin lock): The last-admin race: two concurrent demotions or deactivations can leave no active Admin; guard it in the agent-management handlers (for example a locking read or a serializable check) and test it.
- [x] Carried forward from the PHASE-03 final review (delivered in Task 9, dev keys hashed): `IApiKeyHasher` is declared and implemented here (it is not declared in PHASE-03), and the development seed keys (`tsk_dev1`, `tsp_dev1`, `tsk_dev2`) carry placeholder hashes, so re-seed them through the real hasher so they authenticate (Task 9 of the PHASE-04 plan).

## Handoff

Before PHASE-05 starts: JWT auth and policies work end to end against a test issuer, an admin can create a product with both key kinds through the API, handlers/controllers conform to the architecture tests, and `docs/architecture/02-ARCHITECTURE.md` lists the final abstraction set. Next: [PHASE-05-intake-email-worker.md](PHASE-05-intake-email-worker.md).
