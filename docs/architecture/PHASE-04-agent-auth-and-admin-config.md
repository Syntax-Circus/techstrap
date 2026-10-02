# PHASE-04: Agent Auth and Admin Config API

## Objective

Agents can sign in with an OIDC JWT, are gated by a configured group claim, are provisioned on first call, and the first admin is bootstrapped from configuration. Admins can manage agents, products (with branding), trusted and public API keys, and tags through the API, with every change audited in `AdminEvent`, and expected failures mapped to ProblemDetails. This phase introduces the handler, Result and transport-mapping conventions that PHASE-05 and PHASE-06 reuse.

## Dependencies

- **Depends on:** [PHASE-03](PHASE-03-domain-and-persistence.md) (repositories, `IUnitOfWork`, schema, seeder).
- **Unblocks:** [PHASE-05](PHASE-05-intake-email-worker.md); PHASE-07 settings screens (via PHASE-06).
- **External prerequisites:** an OIDC provider for integration checks (Authentik from `syntax-circus-authentik` for the owner; tests use a signing-key fake, not a live IdP); issuer authority, audience, group claim type and group names configured in `.env.example`.

## Architecture Decisions

- Authentication: `SyntaxCircus.AspNetCore.Authentication` JWT bearer, authority and audience from configuration. Two authorization policies: `AgentPolicy` requires the claim value in `TECHSTRAP_AGENT_GROUP`; `AdminPolicy` requires `TECHSTRAP_ADMIN_GROUP`. Per D-022, product, API key, agent and tag management and the audit log are Admin-only; product, tag and active-agent lists are Agent-readable (agents need them for filters and assignment). A user with neither claim gets 403 on every agent endpoint, including `GET /api/agents/me`. Policy names and claim settings are constants and bound options (validated on start).
- Role source of truth: the group claim gates access; the stored `Agent.role` is derived from the claim at each sign-in (Admin group implies `Admin`) and can be lowered or deactivated via `UpdateAgentRequestHandler`. **Assumption**: a claim-granted Admin cannot be demoted below the claim by the API (the claim wins), while `active=false` always blocks access. Record in the decision log.
- Bootstrap admin: `TECHSTRAP_BOOTSTRAP_ADMIN` (email or subject). On `GetCurrentAgentRequestHandler`, when no active admin exists yet and the caller's subject or verified email matches, the agent is created as `Admin`. After one admin exists the setting has no effect. The comparison is case-insensitive for email and exact for subject, and requires `email_verified` when matching by email (**Assumption**).
- Provisioning on first call: `GET /api/agents/me` upserts the `Agent` from token claims (subject, name, email) and returns the profile (`AgentDto`). No separate registration endpoint.
- Handlers follow _template `APPLICATION_ARCHITECTURE.md`: one named handler and `I...Handler` interface per operation; handlers accept `TechStrap.Contracts` request records directly (D-016: Contracts is dependency-free, so any transport-only detail is mapped by the controller into the request or a small Application-owned model), identity through `ICurrentUserService`, outcomes as `Result`/`Result<T>`. Controllers inject handlers with `[FromServices]` on the action and map Results with `SyntaxCircus.AspNetCore.Common` ProblemDetails helpers, choosing the success response explicitly (`Ok`, `Created`, `NoContent`).
- Contracts (`TechStrap.Contracts`): DTOs end in `Dto`/`Request`/`Response` (`AgentDto`, `ProductDto`, `ProductBrandingDto`, `ProductApiKeyDto`, `CreateProductApiKeyResponse` carrying the plaintext key exactly once, `TagDto`, `AdminEventDto`, `UpdateAgentRequest`, ...). Constants shared with clients (key prefix format, max lengths) live in Contracts only if clients validate them.
- API keys: generated server-side with a recognisable prefix and 256-bit random secret; only prefix and hash stored via `IApiKeyHasher`; plaintext shown once at creation. Kinds: `Trusted` (server-side; later may set external user ref and trusted metadata) and `Public` (client-embedded; create-only; per key+IP rate-limited; metadata flagged untrusted). Kind is immutable after creation; revoke rather than edit. A product may have several keys of each kind.
- Product branding (name, logo reference, accent colour, from-name/reply-to) is updated through `UpdateProductRequestHandler`; accent validated for the contrast rule from PHASE-02. Logo upload uses `SyntaxCircus.Storage` in PHASE-05; this phase accepts a logo URL or storage key string only. **Assumption.**
- `AdminEvent` is written in the same transaction as the change (via `IUnitOfWork`). The audit event payload never contains plaintext keys. Reads through `GET /api/admin-events` (paged, filter by entity type and actor), Admin-only. PHASE-06 writes the same event type for erase-requester, delete-ticket and dead-letter retry/discard (D-006, D-022), so the type constants and payload shape defined here must allow those subjects.
- Tags are global with unique slug; deleting a tag in use detaches it from tickets (emitting `TagRemoved` ticket events in PHASE-06 semantics) or is rejected; **Assumption**: delete is rejected with a conflict if in use unless `force=true`, decision recorded in the log.
- Errors: validation to 400/422 problem details with field errors, not-found to 404, conflict to 409, forbidden to 403. Exact mapping is the standard `SyntaxCircus.AspNetCore.Common` mapping; tests assert it per handler.
- Abstractions introduced in this phase (listed in `02-ARCHITECTURE.md` section 3.1): `IAdminEventRepository` for audit reads and writes, and the options type `AgentAccessOptions` for bootstrap and group configuration. Everything else uses the catalog abstractions.

## Application Boundaries

Follow _template `APPLICATION_ARCHITECTURE.md` (not copied into this repo). All handlers return `Result`/`Result<T>`; controllers pass `HttpContext.RequestAborted` as the cancellation token. `ICurrentUserService` is implied in every row that needs the caller.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| `GET /api/agents/me` | `GetCurrentAgentRequestHandler` (provisions agent on first call, applies bootstrap admin) | `IAgentRepository`, `IUnitOfWork`, `ICurrentUserService`, `TimeProvider`, `IOptions<AgentAccessOptions>` | EF `AgentRepository`, `UnitOfWork`, ASP.NET `CurrentUserService` | 200 `AgentDto`; 403 if inactive | Mandatory flow |
| `GET /api/agents` | `ListAgentsRequestHandler` | `IAgentRepository`, `ICurrentUserService` | EF `AgentRepository` | 200 paged `AgentDto`; AgentPolicy sees active agents only (id, name) for assignment, Admin sees all with role and active flag (D-022) | Mandatory flow |
| `PUT /api/agents/{id}` | `UpdateAgentRequestHandler` (role, active) | `IAgentRepository`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentUserService`, `TimeProvider` | EF repositories, `UnitOfWork` | 200 `AgentDto`; 404; 409 (last active admin cannot be removed) | Mandatory flow |
| `PUT /api/agents/me/notification-preferences` | `UpdateNotificationPreferencesRequestHandler` | `IAgentRepository`, `IUnitOfWork`, `ICurrentUserService` | EF `AgentRepository`, `UnitOfWork` | 204; 400 for unknown product | Mandatory flow |
| `GET /api/products` | `ListProductsRequestHandler` | `IProductRepository` | EF `ProductRepository` | 200 `ProductDto[]` | Mandatory flow |
| `GET /api/products/{id}` | `GetProductRequestHandler` | `IProductRepository` | EF `ProductRepository` | 200; 404 | Mandatory flow |
| `POST /api/products` | `CreateProductRequestHandler` | `IProductRepository`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentUserService`, `TimeProvider` | EF repositories, `UnitOfWork` | 201 `ProductDto`; 409 duplicate key; 400 | Mandatory flow |
| `PUT /api/products/{id}` | `UpdateProductRequestHandler` (incl. branding) | `IProductRepository`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentUserService`, `TimeProvider` | EF repositories, `UnitOfWork` | 200; 404; 409 concurrency; 400 (colour/contrast) | Mandatory flow |
| `GET /api/products/{id}/api-keys` | `ListProductApiKeysRequestHandler` | `IProductRepository` | EF `ProductRepository` | 200 `ProductApiKeyDto[]` (no secrets) | Mandatory flow |
| `POST /api/products/{id}/api-keys` | `CreateProductApiKeyRequestHandler` | `IProductRepository`, `IApiKeyHasher`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentUserService`, `TimeProvider` | EF repositories, `ApiKeyHasher`, `UnitOfWork` | 201 `CreateProductApiKeyResponse` (plaintext once); 404; 400 | Mandatory flow |
| `DELETE /api/products/{id}/api-keys/{keyId}` | `RevokeProductApiKeyRequestHandler` | `IProductRepository`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentUserService`, `TimeProvider` | EF repositories, `UnitOfWork` | 204; 404 | Mandatory flow |
| `GET /api/tags` | `ListTagsRequestHandler` | `ITagRepository` | EF `TagRepository` | 200 `TagDto[]` | Mandatory flow |
| `POST /api/tags` | `CreateTagRequestHandler` | `ITagRepository`, `IAdminEventRepository`, `IUnitOfWork`, `ICurrentUserService`, `TimeProvider` | EF repositories, `UnitOfWork` | 201; 409 duplicate slug; 400 | Mandatory flow |
| `PUT /api/tags/{id}` | `UpdateTagRequestHandler` | same as create | same as create | 200; 404; 409 | Mandatory flow |
| `DELETE /api/tags/{id}` | `DeleteTagRequestHandler` | same as create | same as create | 204; 404; 409 if in use | Mandatory flow |
| `GET /api/admin-events` | `ListAdminEventsRequestHandler` | `IAdminEventRepository` | EF `AdminEventRepository` | 200 paged `AdminEventDto`; AdminPolicy | Mandatory flow |
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
| `SyntaxCircus.Common` | `Result`/`Result<T>`, `ICurrentUserService` | Handler outcomes and identity | Handler unit tests assert Result kinds; `CurrentUserService` test with claims principal |

Keys are hashed with .NET primitives behind `IApiKeyHasher`; no extra package. Versions are in `03-PACKAGE-MAP.md`.

Record the exact package version in the linked package map. In the foundation phase, lock every selected version in `Directory.Packages.props`.

## Deliverables

- [ ] Contracts DTOs and requests for agents, products, keys, tags, admin events
- [ ] 16 handlers with interfaces and application request models (`TechStrap.Application`)
- [ ] Controllers `AgentsController`, `ProductsController`, `TagsController`, `AdminEventsController` using `[FromServices]`
- [ ] JWT auth, group-claim policies, bootstrap-admin options (validated on start) in the API
- [ ] `IApiKeyHasher` implementation and `IAdminEventRepository` implementation
- [ ] OpenAPI document includes the new endpoints
- [ ] Handler unit tests, controller tests, integration tests, `.env.example` updated
- [ ] Self-host auth note (Authentik example) drafted for the release guide in PHASE-12

## Actionable Tasks

Each handler task writes `{Handler}Tests` (substitutes for repositories, fake `TimeProvider`) first.

- [ ] **P04-T01** Add Contracts DTOs and request types for agents, products (with `ProductBrandingDto`), API keys, tags and admin events with naming and serialisation tests (`ContractNamingTests` asserts every public type under `Contracts` ends in `Dto`, `Request` or `Response`)
  - **Depends on:** none (inside this phase)
  - **Validation:** `ContractNamingTests` pass in `TechStrap.Architecture.Tests`
- [ ] **P04-T02** Add `AgentAccessOptions` (agent group, admin group, claim type, bootstrap admin) with validation, JWT bearer wiring and the two policies in the Api; write `AgentAuthTests` using a locally signed test JWT
  - **Depends on:** none
  - **Validation:** `AgentAuthTests` cover missing token (401), token without group (403), agent group (200), admin-only route with agent token (403), misconfigured options fail startup
- [ ] **P04-T03** Implement `ICurrentUserService` for ASP.NET (subject, email, name, groups) and `CurrentUserServiceTests`
  - **Depends on:** P04-T02
  - **Validation:** tests with a built `ClaimsPrincipal` return expected subject, email and verified flag; Application has no reference to `HttpContext` (architecture test green)
- [ ] **P04-T04** Implement `GetCurrentAgentRequestHandler` (`GetCurrentAgentRequestHandlerTests`: first call provisions, second call updates name/email, bootstrap match yields Admin only when no admin exists, unverified email does not bootstrap, inactive agent is forbidden) and `AgentsController.GetMe`
  - **Depends on:** P04-T01, P04-T03
  - **Validation:** handler tests pass; `AgentsControllerTests.GetMe_DelegatesAndPassesCancellation` and a Testcontainers integration test provisions the row once under two concurrent first calls
- [ ] **P04-T05** Implement `ListAgentsRequestHandler` (Agent: active agents only; Admin: all) and `UpdateAgentRequestHandler` with `AdminEvent`s and the "last active admin" protection; `UpdateAgent` action with AdminPolicy
  - **Depends on:** P04-T04
  - **Validation:** `UpdateAgentRequestHandlerTests` cover role change, deactivate, last-admin conflict, audit event written; controller test shows 403 for an Agent-only caller on `PUT /api/agents/{id}` and a filtered active-only list for an Agent on `GET /api/agents`
- [ ] **P04-T06** Implement `UpdateNotificationPreferencesRequestHandler` (per-product new-ticket alert opt-in) and its endpoint
  - **Depends on:** P04-T04
  - **Validation:** `UpdateNotificationPreferencesRequestHandlerTests` cover enabling, disabling, unknown product (400), idempotent repeat; integration test persists preferences
- [ ] **P04-T07** Implement `ListProductsRequestHandler`, `GetProductRequestHandler`, `CreateProductRequestHandler`, `UpdateProductRequestHandler` (branding, accent contrast validation, concurrency) and `ProductsController`
  - **Depends on:** P04-T01, P04-T02
  - **Validation:** one `*RequestHandlerTests` class per handler; `ProductsControllerTests` assert 201 with location, 409 duplicate key, 409 stale concurrency token, 400 low-contrast accent; every write produces an `AdminEvent`
- [ ] **P04-T08** Implement `IApiKeyHasher` (generate prefix plus 256-bit secret, hash, constant-time verify) with `ApiKeyHasherTests`
  - **Depends on:** none
  - **Validation:** tests assert uniqueness of 1000 generated keys, constant-time compare helper used, hash never equals plaintext, verify accepts only the correct key
- [ ] **P04-T09** Implement `ListProductApiKeysRequestHandler`, `CreateProductApiKeyRequestHandler`, `RevokeProductApiKeyRequestHandler` (kinds `Trusted` and `Public`)
  - **Depends on:** P04-T07, P04-T08
  - **Validation:** handler tests assert plaintext only in the create response, listing never exposes hash or secret, revoked key flagged, audit event excludes secret; integration test confirms only hash and prefix are stored
- [ ] **P04-T10** Implement `ListTagsRequestHandler`, `CreateTagRequestHandler`, `UpdateTagRequestHandler`, `DeleteTagRequestHandler` and `TagsController`
  - **Depends on:** P04-T01, P04-T02
  - **Validation:** handler tests cover duplicate slug (409), delete in use (409), colour format; audit events written
- [ ] **P04-T11** Implement `IAdminEventRepository` (infrastructure) and `ListAdminEventsRequestHandler` with paging and filters, `AdminEventsController`
  - **Depends on:** P04-T05
  - **Validation:** `AdminEventRepositoryTests` and `ListAdminEventsRequestHandlerTests` pass; endpoint is Admin-only; newest first and stable paging under inserts
- [ ] **P04-T12** Add `ResultMappingTests` for all controllers (validation 400/422 with field errors, not found 404, conflict 409, forbidden 403) and `CancellationPropagationTests` proving each controller passes `RequestAborted` to its handler
  - **Depends on:** P04-T05, P04-T07, P04-T09, P04-T10, P04-T11
  - **Validation:** both test classes pass and enumerate every controller action through reflection so a new action without a test fails
- [ ] **P04-T13** Extend the architecture tests: every Api controller action (except exempt endpoints) has `[FromServices]` handler parameters and no repository, `DbContext` or `IUnitOfWork` parameter; handler constructors take only approved abstractions
  - **Depends on:** P04-T12
  - **Validation:** `ControllerBoundaryTests` and `HandlerConstructorDependencyTests` pass and fail on a deliberately bad fixture
- [ ] **P04-T14** Update `.env.example` files, OpenAPI document and the dev seeder (an admin from the bootstrap setting); add a self-host auth note
  - **Depends on:** P04-T11
  - **Validation:** `EnvExampleCompletenessTests` pass; `/openapi/v1.json` lists all new routes (`OpenApiSurfaceTests` compares route list to controllers); `docker compose up` with a test JWT gets 200 on `/api/agents/me`

## Success Criteria

- [ ] All 16 handlers listed in the boundary table exist with interfaces, and every controller action delegates to exactly one of them.
- [ ] A token without the configured group claim receives 403 on every agent endpoint; a token with it receives 200 on `GET /api/agents/me` and the agent row is created once.
- [ ] Setting `TECHSTRAP_BOOTSTRAP_ADMIN` produces exactly one Admin on first sign-in and never promotes later callers.
- [ ] Product, key, tag and agent changes each write an `AdminEvent` in the same transaction; no event or response after creation contains a plaintext API key.
- [ ] `Trusted` and `Public` keys can be created and revoked per product; only hashes are stored.
- [ ] Expected failures produce the specified ProblemDetails status codes (`ResultMappingTests`).
- [ ] `dotnet test` is green including `ControllerBoundaryTests`, `HandlerConstructorDependencyTests` and `ContractNamingTests`; CI is green.

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
      right scope, not bare magic values (policy names, claim types, key prefix, page sizes).
- [ ] Duplicated-looking logic across flows was evaluated for genuine
      divergence before extracting (or intentionally not extracting) a shared
      abstraction (the four tag handlers and four product handlers look alike but have distinct rules; extract only the audit-event helper if the duplication is identical).

## Risks and Open Questions

- [ ] Claim-versus-stored-role precedence (claim wins) is an **Assumption**; confirm with the owner and log it.
- [ ] Authentik group claim name and `email_verified` availability differ between IdPs; make the claim type configurable and document it.
- [ ] Deleting an in-use tag: reject versus detach (**Assumption**: reject unless forced).
- [ ] Logo handling for products (URL versus uploaded file) is deferred to PHASE-05/PHASE-07.
- [ ] `IAdminEventRepository` is now listed in `02-ARCHITECTURE.md` section 3.1 (resolved).
- [ ] Admin-only versus Agent-readable product list: Agents need `ListProductsRequestHandler` for filters; this phase makes it AgentPolicy. **Assumption.**

## Handoff

Before PHASE-05 starts: JWT auth and policies work end to end against a test issuer, an admin can create a product with both key kinds through the API, handlers/controllers conform to the architecture tests, and `docs/architecture/02-ARCHITECTURE.md` lists the final abstraction set. Next: [PHASE-05-intake-email-worker.md](PHASE-05-intake-email-worker.md).
