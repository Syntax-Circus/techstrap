# PHASE-03: Domain and Persistence

## Objective

The complete domain model and its Postgres persistence, proven by tests: entities and invariants in `TechStrap.Domain`, repository and unit-of-work abstractions in `TechStrap.Application`, EF Core configuration, tool-generated migrations, per-product ticket numbering, same-transaction `TicketEvent` writing, concurrency tokens and full-text search indexes in `TechStrap.Infrastructure`. No HTTP surface is added.

## Dependencies

- **Depends on:** [PHASE-01](PHASE-01-foundation.md) (DbContext, `PostgresFixture`, initial migration, architecture tests).
- **Unblocks:** [PHASE-04](PHASE-04-agent-auth-and-admin-config.md). Runs in parallel with [PHASE-02](PHASE-02-brand-and-ux.md).
- **External prerequisites:** Docker for Testcontainers; `dotnet-ef` tool.

## Architecture Decisions

- Domain model follows the superseded spec section 4 as amended by the plan: `Product`, `ProductApiKey` (kind `Trusted` or `Public`, hashed), `Agent`, `Requester`, `Ticket`, `Message`, `Attachment`, `TicketEvent`, `Tag`/`TicketTag`, `TicketAccessToken`, `KbCategory`, `KbArticle`, `TicketArticle`, `AdminEvent`, `EmailOutboxItem`.
- Ticket status enum is `New, Open, Pending, Solved, Closed`; `is_spam` is a flag, not a status; `parent_ticket_id` (nullable self reference) links follow-ups created when a customer replies to a Closed ticket. Closed is read-only: the domain rejects mutations of a Closed ticket except through an explicit follow-up creation path.
- Status transitions and invariants live in Domain methods (not in handlers), returning `DomainResult` (BCL-only, D-028; Application converts it to the `Result` from `SyntaxCircus.Common`). Transition table: `New -> Open | Pending | Solved`, `Open <-> Pending`, `Open|Pending -> Solved`, `Solved -> Open` (customer reply or agent reopen), `Solved -> Closed` (auto-close or agent), nothing leaves `Closed`. **Assumption**: `New` becomes `Open` on first agent action; confirm in PHASE-06.
- Ticket number (`ACME-142`) is `{products.number_prefix}-{sequence}`, allocated by `ITicketNumberAllocator` from a per-product counter row (an `INSERT … ON CONFLICT DO UPDATE … RETURNING` upsert on `product_ticket_sequences` inside the creating transaction) and stored as `tickets.number`. The number is immutable across product moves and keeps its original prefix; uniqueness is global on the stored full number (unique index on `tickets.number`, D-009).
- `TicketEvent` is append-only. The Domain raises events; `IUnitOfWork` persists the entity change and its events in one transaction. Event types: `Created`, `MessageAdded`, `StatusChanged`, `Assigned`, `ProductChanged`, `PriorityChanged`, `TagAdded`, `TagRemoved`, `MarkedSpam`, `FollowUpCreated`, plus `payload` jsonb. `AdminEvent` is the equivalent audit log for product, key, agent and tag changes and for erase-requester, delete-ticket and dead-letter retry/discard actions (D-006); its payload never holds erased values or secrets.
- Optimistic concurrency on `Ticket` (and `KbArticle`) through the Npgsql `xmin` concurrency token; conflicts surface as `Result` conflict errors via an `IUnitOfWork` exception translation, never a raw EF exception to handlers.
- Full-text search: generated `tsvector` columns with GIN indexes. The ticket vector covers the subject (weight A) and the message vector the body (weight B); the search query joins them and matches an exact ticket number by equality (D-027: stored generated vectors, no trigger and no maintained column). KB vector covers title (weight A), summary (B), body (C). Language config is `english` (**Assumption**, i18n seam only).
- Timestamps are UTC (`timestamptz`) and set through `TimeProvider`, never `DateTime.UtcNow`. Message bodies are stored sanitized (sanitizer is PHASE-05); `Message` carries reserved nullable `message_id` and `in_reply_to` columns for later inbound email.
- Tokens and keys: `TicketAccessToken` stores only a hash and expiry (90 days sliding, **Assumption** from spec §10); `ProductApiKey` stores prefix plus hash. Hashing abstractions (`IAccessTokenService`, `IApiKeyHasher`) are declared here and implemented in PHASE-04/05.
- Outbox: `EmailOutboxItem` columns for status (`Pending`, `Sending`, `Sent`, `DeadLettered`, `Discarded`), attempts, `next_attempt_at`, last error, claimed-by and claim expiry. Claiming (`FOR UPDATE SKIP LOCKED`) is in `IEmailOutboxStore` (worker claim/ack), enqueueing in `IEmailOutbox` (same transaction as the triggering change).
- Persistence uses separate `*Record` classes (`TicketRecord`, `MessageRecord`, internal to `TechStrap.Infrastructure`) mapped by fluent configuration with snake_case names and an `xmin` token; repositories map between records and Domain types and never expose records, `IQueryable` or `DbSet` (D-026). Domain keeps no EF attributes or EF-driven shapes.
- Handlers will depend only on the abstractions listed below. Constants (page sizes, token lifetime, max lengths) are named constants in `TechStrap.Domain` or `TechStrap.Application`, shared with Contracts only when they cross a client boundary.
- Migrations are generated with `dotnet ef migrations add` only. Because the schema is large, split into several named migrations by area (core, tickets and events, auth and keys, KB, outbox, search) for reviewability. **Assumption.**

## Application Boundaries

Follow _template `APPLICATION_ARCHITECTURE.md` (not copied into this repo). This phase adds no HTTP, worker or scheduled entry points, and so no handlers. It defines and implements the abstractions handlers will use from PHASE-04 on.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| None: no entry points in this phase | n/a | Defines `ITicketRepository`, `IRequesterRepository`, `IProductRepository`, `IAgentRepository`, `ITagRepository`, `IKbRepository`, `IEmailOutbox`, `IEmailOutboxStore`, `ITicketNumberAllocator`, `IUnitOfWork`; consumes `TimeProvider` | EF Core repositories, `TicketNumberAllocator`, `EmailOutboxStore`, `UnitOfWork` in `TechStrap.Infrastructure` | Domain returns `DomainResult` errors (not found, conflict, invalid transition) and Application converts them to `Result` (D-028); no transport mapping yet | Verified by integration tests only |

Handlers must not depend on HTTP objects, EF types, concrete infrastructure, or transport response types. Link an approved decision for every exception.

## Razor Component Boundaries

Follow _template `RAZOR_COMPONENT_ARCHITECTURE.md`. ViewModels are Razor-only and feature-local; API contracts use DTO names.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| N/A — no Razor in this phase | n/a | n/a | n/a | n/a |

## Syntax Circus Packages

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.Common` | `Result`/`Result<T>`, error kinds | Domain methods and repositories return transport-neutral outcomes | Domain tests assert error kinds for invalid transitions |
| `SyntaxCircus.EntityFrameworkCore.Postgres` | snake_case naming, migrate-on-startup | All new tables and columns must follow the convention; migration runs through the existing startup path | `SchemaConventionTests` checks every table and column is snake_case; `MigrationStartupTests` still pass |

Npgsql and EF Core packages come from the locked central versions (`03-PACKAGE-MAP.md`). Hashing, email and storage packages are not touched here.

Record the exact package version in the linked package map. In the foundation phase, lock every selected version in `Directory.Packages.props`.

## Deliverables

- [ ] Domain entities, value types, enums and invariants in `TechStrap.Domain`
- [ ] Repository, unit-of-work, numbering and outbox abstractions in `TechStrap.Application`
- [ ] EF configurations, repositories and services in `TechStrap.Infrastructure`
- [ ] Tool-generated migrations covering the full schema, FTS columns and indexes
- [ ] Integration tests on Testcontainers Postgres 17 and Domain unit tests
- [ ] Dev seed data (products, agents, requesters, tickets in every status, KB articles) behind `IDevelopmentDataSeeder`
- [ ] Short `docs/architecture` schema note (ER diagram) linked from `02-ARCHITECTURE.md`

## Actionable Tasks

- [ ] **P03-T01** Write `TicketStatusTransitionTests` and implement the `TicketStatus` enum and transition rules on `Ticket` (including Closed read-only and spam flag)
  - **Depends on:** none (inside this phase)
  - **Validation:** `TechStrap.Domain.Tests` `TicketStatusTransitionTests` covers every allowed and forbidden transition pair and passes
- [ ] **P03-T02** Write `ProductTests`, `ProductApiKeyTests`, `TagTests`, `AgentTests` and implement `Product` (key slug rules, branding fields, active flag), `ProductApiKey` (kind `Trusted`/`Public`, prefix, hash, revoked), `Tag` (unique slug, colour), `Agent` (subject, role `Agent`/`Admin`, active)
  - **Depends on:** none
  - **Validation:** domain tests pass for key slug validation, accent colour format, revoked key behaviour and role changes
- [ ] **P03-T03** Write `RequesterTests`, `MessageTests`, `TicketAccessTokenTests` and implement `Requester` (case-insensitive email, optional external user ref), `Message` (author type, visibility, reserved email columns), `Attachment`, `TicketAccessToken` (hash, revoke, sliding expiry)
  - **Depends on:** P03-T01
  - **Validation:** tests cover email normalisation, internal-vs-public visibility, token expiry sliding with a fake `TimeProvider`, and revoked tokens
- [ ] **P03-T04** Write `TicketCreationTests` and implement ticket aggregate creation and mutations (reply, internal note, assign, priority, product move, tag add/remove, follow-up creation with `parent_ticket_id`) raising `TicketEvent`s and keeping the immutable number
  - **Depends on:** P03-T01, P03-T03
  - **Validation:** tests assert one event per mutation, number unchanged after `MoveToProduct`, follow-up links to the closed parent, and mutations on Closed return a conflict `Result`
- [ ] **P03-T05** Implement `KbCategory`, `KbArticle` (Draft/Published/Archived, slug unique within product) and `TicketArticle` with `KbArticleTests`
  - **Depends on:** none
  - **Validation:** tests cover publish/archive transitions, slug rules and shared (null product) articles
- [ ] **P03-T06** Implement `AdminEvent` and `EmailOutboxItem` domain types with `EmailOutboxItemTests` (retry/backoff schedule, dead-letter after N attempts as a named constant)
  - **Depends on:** none
  - **Validation:** tests assert backoff progression with a fake `TimeProvider` and the dead-letter threshold
- [ ] **P03-T07** Declare the Application abstractions listed in the boundary table (`IAttachmentStore` and the hasher/sanitizer/renderer interfaces are declared in PHASE-04/05); add an `AbstractionShapeTests` architecture test: none of them reference EF, `HttpContext` or Infrastructure types, and all async methods take `CancellationToken`
  - **Depends on:** P03-T04, P03-T05, P03-T06
  - **Validation:** `dotnet test tests/TechStrap.Architecture.Tests --filter AbstractionShapeTests` passes; a bad fixture is flagged
- [ ] **P03-T08** Write `SchemaConventionTests` and EF `IEntityTypeConfiguration` classes for all entities (keys, FKs, unique indexes on ticket number, requester email lower-cased, product key, tag slug, API key prefix; enum conversions to strings; jsonb for `metadata`, `custom_fields`, event payloads)
  - **Depends on:** P03-T07
  - **Validation:** tests introspect the EF model and assert snake_case names, required indexes, and string-backed enums
- [ ] **P03-T09** Generate migrations with `dotnet ef migrations add` (core, tickets and events, auth and keys, KB, outbox) and apply them in `MigrationStartupTests`
  - **Depends on:** P03-T08
  - **Validation:** `dotnet ef migrations has-pending-model-changes` exits 0; migrations apply on a fresh Postgres 17 container; diff review shows tool-generated files only
- [ ] **P03-T10** Write `TicketNumberAllocatorTests` (integration) then implement `TicketNumberAllocator` with a per-product counter
  - **Depends on:** P03-T09
  - **Validation:** 50 concurrent allocations for one product produce 1..50 with no gaps or duplicates; two products have independent sequences; a rolled-back transaction does not leave a duplicate
- [ ] **P03-T11** Add FTS: generated `tsvector` columns and GIN indexes for tickets and KB articles (migration via the EF tool using the supported Npgsql API; raw SQL only inside a tool-generated migration `Sql()` call where the API has no equivalent), with `TicketSearchTests` and `KbSearchTests`
  - **Depends on:** P03-T09
  - **Validation:** a seeded ticket is found by a subject word, by a message-body word and by number; KB title matches rank above body matches; `EXPLAIN` in a test shows the GIN index is usable (index scan on a large-enough seeded set)
- [ ] **P03-T12** Implement `UnitOfWork` with `UnitOfWorkTests` (atomic multi-write, event written in the same transaction, rollback on failure, concurrency conflict translated to a conflict `Result`)
  - **Depends on:** P03-T09
  - **Validation:** forcing a failure after the ticket write leaves no ticket, message, event or outbox row; two stale updates yield one success and one conflict
- [ ] **P03-T13** Implement `TicketRepository`, `RequesterRepository`, `ProductRepository`, `AgentRepository`, `TagRepository`, `KbRepository` with `*RepositoryTests` integration tests (queries by number, requester email, product key, tag slug; queue-view queries Unassigned, Mine, Open, Pending, All; paging; no persistence types leak)
  - **Depends on:** P03-T10, P03-T11, P03-T12
  - **Validation:** each repository test class passes against Testcontainers; queue-view queries return expected sets for a seeded fixture; no method signature exposes `DbContext`, `IQueryable` or EF types (`AbstractionShapeTests`)
- [ ] **P03-T14** Implement `IEmailOutbox` and `EmailOutboxStore` (`FOR UPDATE SKIP LOCKED` claim, ack, fail with backoff, dead-letter, retry, discard) with `EmailOutboxStoreTests`
  - **Depends on:** P03-T06, P03-T12
  - **Validation:** two concurrent claimers never receive the same row; expired claims are reclaimable; dead-lettered rows are listed and retryable; enqueue participates in the caller's transaction (rollback removes the row)
- [ ] **P03-T15** Extend the dev seeder with realistic demo data and `DevSeederTests`
  - **Depends on:** P03-T13
  - **Validation:** running the seeder twice is idempotent; seeded data includes tickets in all five statuses, a spam ticket, a follow-up pair, both key kinds and a published KB article; `docker compose up` with the seed flag shows data in Postgres
- [ ] **P03-T16** Add an ER diagram and schema notes to `docs/architecture` and link them from `02-ARCHITECTURE.md`
  - **Depends on:** P03-T09
  - **Validation:** every table in the migration appears in the diagram (checked by a short script listing tables against diagram entities)
- [ ] **P03-T17** (D-024) Write `AgentPublicIdentityTests` first, then add the nullable `PublicDisplayName` to `Agent` (trimmed, max 60, blank becomes null, rejects `@` and control characters), the pure Domain resolver `AgentPublicIdentity.Resolve(agent, productDisplayName)` (override, else the first word of the agent's name; formatted `{name} from {Product} Support`, or `{Product} Support` when the agent has no name; **Assumption:** the suffix is kept with an override), the EF mapping `public_display_name` (nullable, max 60) and a tool-generated migration (a follow-up migration if P03-T09 has already landed)
  - **Depends on:** P03-T02, P03-T08, P03-T09
  - **Validation:** tests: "Sam W." with no override gives "Sam from Orbitly Support", override "Samantha" gives "Samantha from Orbitly Support", whitespace override falls back to the default, an email-like or over-long override is rejected, an agent with no name gives "Orbitly Support"; `SchemaConventionTests` sees the nullable snake_case column; `dotnet ef migrations has-pending-model-changes` exits 0 and the migration applies on a fresh Postgres 17 container


## Success Criteria

- [ ] `AgentPublicIdentityTests` pass (default, override, edge cases) and the nullable `agents.public_display_name` column is in the migrations (D-024).
- [ ] `dotnet test tests/TechStrap.Domain.Tests tests/TechStrap.Application.Tests tests/TechStrap.Infrastructure.IntegrationTests tests/TechStrap.Architecture.Tests` passes.
- [ ] A fresh Postgres 17 database migrates to the full schema; `dotnet ef migrations has-pending-model-changes` exits 0.
- [ ] Ticket numbers are gap-free and unique under 50-way concurrency per product and keep their prefix after a product move.
- [ ] Every ticket mutation writes its `TicketEvent` in the same transaction (rollback test passes).
- [ ] Stale concurrent ticket updates produce a conflict `Result`, not a lost update.
- [ ] FTS returns tickets and KB articles by the expected fields and ranks KB titles first.
- [ ] Outbox claiming is exclusive under concurrency.
- [ ] No Domain type references EF, and no Application abstraction exposes EF types (architecture tests green).

## Boundary Validation

- [ ] Application use-case entry points delegate to the named handlers listed above. (None added.)
- [ ] Framework-owned operational or static exemptions execute no application workflow.
- [ ] Handler constructor dependencies contain only approved abstractions (abstractions here are the approved set; `AbstractionShapeTests`).
- [ ] Persistence and integration entities do not cross infrastructure boundaries.
- [ ] Cancellation reaches asynchronous handler dependencies (every async abstraction method takes a `CancellationToken`).
- [ ] Expected outcomes and transport mapping have focused tests (domain `Result` error kinds; mapping begins in PHASE-04).
- [ ] Infrastructure implementations have integration coverage where applicable (all repositories, allocator, unit of work, outbox store, FTS).
- [ ] Inline Razor components contain only simple parameters and, at most, one
      trivial synchronous `EventCallback`-forwarding callback. (N/A.)
- [ ] Every component beyond the inline ceiling uses paired `.razor` and
      `.razor.cs` files, with all C# in code-behind. (N/A.)
- [ ] Each Razor ViewModel is feature-local and presentation-only; the recorded
      direct-model decision does not expose an API ViewModel. (N/A.)
- [ ] A factory or presentation service is used only for non-trivial mapping,
      asynchronous assembly, or multiple dependencies. (N/A.)
- [ ] API request and response contracts use DTO names and contracts, never
      Razor ViewModels. (N/A.)
- [ ] Repeated or business-meaningful literals are named constants at the
      right scope, not bare magic values (status names, token lifetime, outbox retry limit, field lengths).
- [ ] Duplicated-looking logic across flows was evaluated for genuine
      divergence before extracting (or intentionally not extracting) a shared
      abstraction (`TicketEvent` and `AdminEvent` stay separate types; their audiences and retention differ).

## Risks and Open Questions

- [ ] Schema size makes one PR large; split by area (task order above) and merge incrementally.
- [x] Ticket FTS over message bodies: resolved by D-027 (stored generated tsvector columns, no trigger, no application-maintained column).
- [x] Whether EF maps Domain types directly or through separate persistence entities: resolved by D-026 (separate `*Record` persistence entities).
- [ ] Erase-requester (PHASE-06) must anonymise messages and cascade attachments; confirm the schema supports it without breaking `TicketEvent` immutability (event payloads must not hold PII beyond ids).
- [ ] Postgres `xmin` concurrency token behaviour with Npgsql 10; verify in `UnitOfWorkTests`.
- [ ] Hash algorithm choices for tokens and API keys are settled in PHASE-04/05 (schema stores opaque hash plus prefix only).

## Handoff

Before PHASE-04 starts: all migrations are merged and applied in CI, repositories and `IUnitOfWork` pass integration tests, the seeder produces demo data, and the abstractions are stable (changes after this need a decision-log note). Next: [PHASE-04-agent-auth-and-admin-config.md](PHASE-04-agent-auth-and-admin-config.md).
