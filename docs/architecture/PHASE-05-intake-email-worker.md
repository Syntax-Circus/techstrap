# PHASE-05: Intake, Email and Worker

## Objective

A customer can submit a ticket through the portal web form, a trusted product-app key or a public embedded key; the system creates the requester, ticket, first message, access token and a queued confirmation email in one transaction; and the Worker drains the outbox reliably (claim, send, retry with backoff, dead-letter). Portal branding for a product is readable through a public endpoint.

## Dependencies

- **Depends on:** [PHASE-04](PHASE-04-agent-auth-and-admin-config.md) (products, keys, hasher, auth conventions, Result mapping).
- **Unblocks:** [PHASE-06](PHASE-06-ticket-operations.md); PHASE-11 (client SDK builds on the intake endpoint).
- **External prerequisites:** an SMTP server or a capture container (Mailpit in compose for local development, **Assumption**); writable storage volume for attachments.

## Architecture Decisions

- One use case, two entry points: `POST /api/public/products/{key}/tickets` (portal web form, multipart, anonymous, per-IP limited) and `POST /api/intake/tickets` (API key) (D-034: API-key intake is JSON-only in v1; the web form is multipart). Both call `SubmitTicketRequestHandler`. The handler accepts the Contracts `SubmitTicketRequest` directly (D-016); the controller supplies an Application-owned submit context beside it with `Channel` (`Web` or `Api`), `TrustLevel` (`Untrusted` for web form and public keys, `Trusted` for trusted keys), the honeypot flag, the uploaded files as streams and the optional `Idempotency-Key`; the handler applies the rules. Header names are constants in Contracts: `X-Api-Key` and `Idempotency-Key`. The key kind comes from the authenticated key principal, not the request body.
- Trust rules (in the handler, unit tested): a `Trusted` key may supply external user reference and trusted metadata; for `Public` keys and the web form the external user ref is rejected or dropped (D-032: dropped, warning `external-user-ref-ignored`) and metadata is stored with `MetadataTrusted = false`. Public keys can only create tickets, never read them. Metadata size and key count are capped by named constants.
- API-key authentication uses `SyntaxCircus.AspNetCore.Authentication` API-key scheme; the key lookup verifies against hashes through `IApiKeyHasher`; revoked or wrong-product keys get a uniform 401. The key principal carries product id and key kind claims; the web-form route resolves the product from the path key and requires the product to be active.
- Abuse controls (host-level, per `CLIENT_IP_RATE_LIMITING.md` from PHASE-01): web form per-IP fixed window; public key per key-plus-IP window (a partitioned fixed window keyed `{keyPrefix}:{ip}` (public) or `{keyPrefix}` (trusted)); trusted key a higher separate limit. Limits are validated options bound at startup, partitioned by the resolved client IP: the Portal forwards the original IP in `X-Forwarded-For` and the API trusts only the pinned compose subnet (D-019), so web-form limits apply to the visitor, not to the Portal container. Honeypot field: the controller maps it into the submit context; the handler discards the submission and returns a normal-looking success result (no ticket created, a named outcome the controller maps to the same 201 shape). Body and attachment size limits are enforced twice: Kestrel/form limits at the host edge and named constants in the handler (D-032: 10 MiB per file, 25 MiB per message, 5 files, PNG/JPEG/GIF/WebP/PDF/text/log/CSV/ZIP checked by declared type and leading bytes). Allowlist checks use declared type plus content sniffing in `IAttachmentStore`.
- Requester upsert is by case-insensitive email, concurrency-safe (unique index plus retry on conflict). Name updates only fill blanks; a public or web submitter never overwrites a known name.
- Message bodies are stored sanitized through `IHtmlSanitizer` (HtmlSanitizer library in Infrastructure). Plain text input is the norm for the web form; Markdown is not accepted from customers. **Assumption.**
- Access token: `IAccessTokenService` generates a 256-bit token, stores only the hash and an initial 90-day sliding expiry. Absolute cap 365 days from issue (D-032, `TicketAccessToken.MaxLifetime`); the portal link is `{TECHSTRAP_PORTAL_PUBLIC_URL}/t/{token}` built from validated `PortalLinkOptions`. The response carries ticket number and view URL (trusted and public API callers) while the web form redirects the user to a confirmation page that does not reveal the token (link comes by email only). **Assumption**: web form response returns only the ticket number.
- Attachments stream into `IAttachmentStore` (implemented over `SyntaxCircus.Storage` on a local volume); storage keys are random, never user filenames; files are saved before the DB commit and cleaned up on rollback (compensating delete), with an orphan-sweep note for PHASE-12. Downloads come in PHASE-06.
- Outbox: the handler enqueues the confirmation email through `IEmailOutbox` inside the same `IUnitOfWork` transaction. Email failure never fails the request. Templates: text and HTML, per-product branding (name, logo, accent, from and reply-to), English-only with an i18n seam; the outbox row holds template data and the Worker renders at send time with the product's current branding (D-033).
- `DrainEmailOutboxHandler` is the Worker entry point: a `BackgroundService` loop resolves `IDrainEmailOutboxHandler` from a new DI scope each iteration (the handler is scoped), calls it per tick with the stoppingToken, and maps the result to sleep behaviour (more work: continue; idle: wait the poll interval). The handler claims a batch with `IEmailOutboxStore` (`FOR UPDATE SKIP LOCKED`), renders with `IEmailTemplateRenderer` and sends through the Application-owned `IOutboundEmailSender` (D-033), acks or fails with exponential backoff, and dead-letters after N attempts (named constant). Delivery is at-least-once; the outbox id goes into the `Message-ID` header. Worker never migrates and never serves business HTTP (health only).
- `GetPublicProductRequestHandler` backs `GET /api/public/products/{key}`: returns branding only (name, logo, accent, contact-form flags), `Cache-Control: public, max-age=<n>` on a hit and `no-store` on 404, inactive products return 404. The portal consumes it in PHASE-09.
- Abstractions added in this phase beyond the original catalog (listed in `02-ARCHITECTURE.md` section 3.1): `IOutboundEmailSender` (Application; adapted to `SyntaxCircus.Email.IEmailSender` in Infrastructure, D-033), `IAccessTokenService`, `IAttachmentStore`, `IHtmlSanitizer`, `IEmailTemplateRenderer`, `IIntakeIdempotencyStore` (D-020), and the options types `PortalLinkOptions`, `EmailBrandingOptions` and `EmailOutboxWorkerOptions`. No other additions.
- **Idempotency (D-020):** `POST /api/intake/tickets` accepts an optional `Idempotency-Key` header scoped per API key with 24 h retention; a repeat returns the original ticket number with a fresh access link and creates no ticket, message or outbox row (P05-T17; the stored response never holds the link, D-033). The SDK relies on it to retry submits (PHASE-11).
- New-ticket alerts to agents are not sent in this phase; PHASE-06 introduces `ITicketNotificationPlanner` and wires it into `SubmitTicketRequestHandler`. The requester confirmation email is the only email here.

## Application Boundaries

Follow _template `APPLICATION_ARCHITECTURE.md` (not copied into this repo). Controllers inject handlers with `[FromServices]`; the Worker's hosted service resolves the handler from a fresh DI scope per iteration via `IServiceScopeFactory`.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| `POST /api/public/products/{key}/tickets` (portal web form, multipart) | `SubmitTicketRequestHandler` | `IProductRepository`, `IRequesterRepository`, `ITicketRepository`, `ITicketNumberAllocator`, `IAccessTokenService`, `IAttachmentStore`, `IHtmlSanitizer`, `IEmailOutbox`, `IIntakeIdempotencyStore`, `IUnitOfWork`, `TimeProvider`, `IOptions<PortalLinkOptions>`, `ILogger` | EF repositories, `TicketNumberAllocator`, `AccessTokenService`, `SyntaxCircus.Storage`-backed `AttachmentStore`, HtmlSanitizer adapter, `EmailOutbox`, `UnitOfWork` | 201 `SubmitTicketResponse` (ticket number only; the view link arrives by email); 400 validation incl. disallowed file; 404 unknown or inactive product; 413 oversize; 429 from rate limiter at host. Honeypot yields the same 201 (D-032) | Same use case as the intake route; channel and trust differ via the request model |
| `POST /api/intake/tickets` (API key) | `SubmitTicketRequestHandler` | as above | as above | 201 `SubmitTicketResponse` (number, view URL); 401 uniform for bad key; 400; 429 | Mandatory flow; shared handler is a single use case, not accidental duplication |
| Worker loop: drain email outbox | `DrainEmailOutboxHandler` | `IEmailOutboxStore`, `IProductRepository`, `IEmailTemplateRenderer`, `IOutboundEmailSender`, `IOptions<EmailOutboxWorkerOptions>`, `ILogger` | EF `EmailOutboxStore`, `EmailTemplateRenderer`, `SmtpOutboundEmailSender` over `SyntaxCircus.Email` | Host maps `Result` to continue/idle/backoff; failures logged; handler never throws for expected send failures | Mandatory flow; Hosted service resolves the scoped handler from a fresh DI scope per iteration (`IServiceScopeFactory`) |
| `GET /api/public/products/{key}` | `GetPublicProductRequestHandler` | `IProductRepository` | EF `ProductRepository` | 200 `PublicProductDto` with `Cache-Control`; 404 `no-store` | Mandatory flow |
| `/health/live`, `/health/ready` (Worker and Api) | Exempt: framework operational endpoints (PHASE-01) | n/a | n/a | n/a | Exempt |

Handlers must not depend on HTTP objects, EF types, concrete infrastructure, or transport response types. Link an approved decision for every exception.

## Razor Component Boundaries

Follow _template `RAZOR_COMPONENT_ARCHITECTURE.md`. ViewModels are Razor-only and feature-local; API contracts use DTO names.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| N/A — no Razor in this phase | n/a | n/a | n/a | `SubmitTicketRequest` (multipart fields), `SubmitTicketResponse` and `PublicProductDto` are defined in `TechStrap.Contracts` for Portal (PHASE-09) and the SDK (PHASE-11); the Portal form will own its separate ViewModel |

## Syntax Circus Packages

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.Email` | Outbound SMTP | Worker sends queued emails | `SmtpDrainIntegrationTests` against a Mailpit/Testcontainers SMTP capture: message arrives with `Message-ID` equal to the outbox id |
| `SyntaxCircus.Storage` | Attachment storage | Intake attachments over a local volume | `AttachmentStoreTests`: save, read back, delete, random key, path traversal rejected |
| `SyntaxCircus.AspNetCore.Authentication` | API-key authentication scheme | Product API keys for `/api/intake/tickets` | `ApiKeyAuthTests`: valid, revoked, wrong secret, uniform 401 |
| `SyntaxCircus.AspNetCore.Common` | Rate-limiter helpers, forwarded client IP, ProblemDetails, Result mapping | Public endpoint hardening per the client-IP pattern | `IntakeRateLimitTests`: 429 past the limit per IP and per key+IP; other IPs unaffected |
| `SyntaxCircus.Common` | `Result` | Handler outcomes | Handler unit tests |

Third-party: the HtmlSanitizer library (version in `03-PACKAGE-MAP.md`), verified by `HtmlSanitizerTests` for script, event-handler and `javascript:` URL removal. Versions are all in `03-PACKAGE-MAP.md`.

Record the exact package version in the linked package map. In the foundation phase, lock every selected version in `Directory.Packages.props`.

## Deliverables

- [ ] Contracts: `SubmitTicketRequest`, `SubmitTicketResponse`, `PublicProductDto`
- [ ] Handlers: `SubmitTicketRequestHandler`, `DrainEmailOutboxHandler`, `GetPublicProductRequestHandler` with interfaces
- [ ] `PublicIntakeController`, `IntakeController`, `PublicProductsController`
- [ ] Infrastructure: `AttachmentStore`, `AccessTokenService`, HtmlSanitizer adapter, `EmailOutbox` and email sender wiring
- [ ] Worker `EmailOutboxWorker` (BackgroundService), health endpoint, `.env.example`
- [ ] Branded text and HTML email templates (confirmation)
- [ ] API-key auth scheme, honeypot, rate limits and body limits configured
- [ ] Compose update: Worker service plus a local SMTP capture container (Mailpit) and the shared storage volume
- [ ] Integration tests for the intake-to-email cycle
- [ ] `Idempotency-Key` support on API-key intake with its table and migration (D-020)

## Actionable Tasks

- [x] **P05-T01** Add Contracts DTOs (`SubmitTicketRequest`, `SubmitTicketResponse`, `PublicProductDto`), attachment/metadata limit constants (`IntakeLimits`) and the header-name constants (`X-Api-Key`, `X-Ticket-Token`, `Idempotency-Key`) with `ContractNamingTests` coverage (Contracts holds no attributes or validation framework, D-016)
  - **Depends on:** none (inside this phase)
  - **Validation:** `ContractNamingTests` pass; limits constants referenced by both Contracts and the handler (no duplicated literals, checked by a parity test (`IntakeLimits.MaxFileBytes == DomainLimits.AttachmentMaxBytes`))
- [x] **P05-T02** Implement `IAccessTokenService` and `AccessTokenServiceTests` (256-bit token, hash-only storage, constant-time verify, sliding expiry via `TimeProvider`)
  - **Depends on:** none
  - **Validation:** tests assert two tokens never collide in 10,000 draws, plaintext never persisted, verify accepts only the right token, expiry slides by the configured days
- [x] **P05-T03** Implement `IHtmlSanitizer` adapter with `HtmlSanitizerTests` (scripts, event handlers, `javascript:` and `data:` URLs removed; safe formatting and links kept with `rel`)
  - **Depends on:** none
  - **Validation:** `HtmlSanitizerTests` pass for an XSS payload corpus (at least 20 vectors)
- [x] **P05-T04** Implement `IAttachmentStore` over `SyntaxCircus.Storage` with `AttachmentStoreTests` (random keys, size cap, allowlist by type and content sniffing, traversal-safe, delete)
  - **Depends on:** P05-T01
  - **Validation:** tests reject `.exe`, mismatched content type and oversize files; a path-traversal filename never escapes the root
- [x] **P05-T05** Implement `IEmailTemplateRenderer` with `EmailTemplateRendererTests` (confirmation template, text and HTML, product name/logo/accent, i18n seam, HTML-escaped user content, accent contrast from PHASE-02 applied)
  - **Depends on:** none
  - **Validation:** snapshot tests for two products with different branding; injected `<script>` in the subject is escaped in HTML and absent as markup in text
- [x] **P05-T06** Write `SubmitTicketRequestHandlerTests` first (happy path per channel and trust level; trusted key keeps external ref and trusted metadata; public key and web drop external ref and mark metadata untrusted; inactive product; honeypot discard; oversize or disallowed attachment; sanitised body; requester reused on same email in different case; confirmation email enqueued; no email enqueue failure leaks to caller) and implement the handler
  - **Depends on:** P05-T01, P05-T02, P05-T03, P05-T04, P05-T05
  - **Validation:** all handler tests pass with NSubstitute substitutes and fake `TimeProvider`; a cancellation test shows the token reaches repositories, store and outbox
- [x] **P05-T07** Write `SubmitTicketIntegrationTests` (Testcontainers) covering intake-to-outbox in one transaction: ticket, message, `Created` event, token hash and outbox row exist together; forced failure after ticket creation leaves nothing and deletes the stored attachment; 20 concurrent submissions get distinct sequential numbers
  - **Depends on:** P05-T06
  - **Validation:** `SubmitTicketIntegrationTests` pass; rollback test leaves zero rows and no orphan file
- [x] **P05-T08** Add API-key authentication scheme and `IntakeController` (`POST /api/intake/tickets`) plus `PublicIntakeController` (multipart web form route) delegating via `[FromServices]`, with host-level honeypot mapping, body size limits and options validation
  - **Depends on:** P05-T06
  - **Validation:** `IntakeControllerTests` and `ApiKeyAuthTests`: 201 shapes, uniform 401 for revoked or unknown keys, key for product A cannot post to product B, oversize body 413, controllers delegate and pass `RequestAborted`
- [x] **P05-T09** Configure rate limiting per the client-IP pattern: per-IP window for the web form, per key+IP window for public keys, a separate higher window for trusted keys, validated at startup
  - **Depends on:** P05-T08
  - **Validation:** `IntakeRateLimitTests` (via the default trusted network and a startup filter faking `RemoteIpAddress`) return 429 past the limit for one IP and 201 for another (partitioning uses the forwarded client IP, D-019); bad option values fail boot
- [x] **P05-T10** Write `GetPublicProductRequestHandlerTests` and implement the handler and `PublicProductsController` with cache headers
  - **Depends on:** P05-T01
  - **Validation:** tests cover active (200 with `max-age`), inactive and unknown (404 `no-store`), and that no key or email data appears in `PublicProductDto`
- [x] **P05-T11** Write `DrainEmailOutboxHandlerTests` first (claims a batch, acks on success, fails with exponential backoff, dead-letters at the threshold, sets `Message-ID` to the outbox id, honours cancellation mid-batch, empty queue returns idle) and implement the handler
  - **Depends on:** none (outbox store from PHASE-03)
  - **Validation:** all handler tests pass with a fake sender and fake `TimeProvider`; backoff schedule asserted exactly
- [x] **P05-T12** Implement the Worker `EmailOutboxWorker` (BackgroundService, resolving `IDrainEmailOutboxHandler` from a fresh DI scope per iteration via `IServiceScopeFactory`, poll interval option, graceful shutdown) and wire `SyntaxCircus.Email`
  - **Depends on:** P05-T11
  - **Validation:** `EmailOutboxWorkerTests` show the loop calls the handler with the stopping token and sleeps only when idle; the architecture test confirms Worker has no direct repository or `DbContext` use in the loop class
- [x] **P05-T13** Write `EmailDrainIntegrationTests` against an SMTP capture container: two Worker instances drain 100 queued rows with no duplicates, a failing SMTP target causes retry then dead letter, the crashed-claim row becomes claimable again
  - **Depends on:** P05-T12, P05-T07
  - **Validation:** tests pass in CI with Testcontainers; the captured mailbox has exactly 100 distinct `Message-ID`s in the no-failure case
- [x] **P05-T14** Extend compose files (local, UAT, production): Worker service, Mailpit for local, the `techstrap-storage` volume mounted at `/app/storage` on both api and worker, `.env.example` entries (SMTP, `TECHSTRAP_PORTAL_PUBLIC_URL`, intake limits, rate limits, outbox poll and retry), Worker health check
  - **Depends on:** P05-T12
  - **Validation:** `docker compose up -d` brings Worker healthy; submitting through `POST /api/intake/tickets` with a seeded key produces a message in Mailpit within the poll interval
- [x] **P05-T15** Extend architecture tests: `SubmitTicketRequestHandler` and `DrainEmailOutboxHandler` constructor rules, `ControllerBoundaryTests` for the new controllers, and a check that the Worker project does not reference `Api`
  - **Depends on:** P05-T08, P05-T12
  - **Validation:** `dotnet test tests/TechStrap.Architecture.Tests` passes and fails on a bad fixture
- [x] **P05-T16** Update dev seeder with a trusted and a public key for the demo product; add an intake smoke script (`scripts/Send-TestTicket.ps1`) and README usage
  - **Depends on:** P05-T14
  - **Validation:** running the script against local compose creates a ticket, prints the number, and Mailpit shows a branded confirmation containing a `/t/{token}` link
- [x] **P05-T17** (D-020) Write `IntakeIdempotencyTests` and `IntakeIdempotencyIntegrationTests` first, then add `Idempotency-Key` support to `POST /api/intake/tickets`: `IIntakeIdempotencyStore` over the existing `intake_idempotency_keys` table (created in PHASE-03; no new migration; unique per API key and key hash, 24 h retention constant, expired rows pruned opportunistically), checked inside the creating transaction so a repeat returns the stored `SubmitTicketResponse` creating no ticket, message or outbox row, and issuing a fresh access token and link; an over-long key is a 400
  - **Depends on:** P05-T07, P05-T08
  - **Validation:** tests prove the same key twice returns the same ticket number with a fresh link, and creates one ticket; the same key on a different API key creates a second ticket; an expired key (fake `TimeProvider`) creates a new ticket; two concurrent requests with one key create exactly one ticket; no header means no store access; the header name comes from the Contracts constant
- [x] **P05-T18** (D-024) Extend `IEmailTemplateRenderer`'s model with `AgentPublicName` (an already-resolved string; the renderer never receives agent email, id or full name) and bind an `EmailBrandingOptions.ShowPoweredBy` from `TECHSTRAP_PORTAL_SHOW_POWERED_BY` (default `true`). The "Powered by TechStrap" line links to https://github.com/Syntax-Circus/techstrap (one constant) in HTML, appears as the bare URL in text, and is omitted entirely when false. The key is already in the Worker `.env.example`; Rendering happens in the Worker (D-033), so only the Worker reads the setting. Outbox rows store the resolved name captured at enqueue
  - **Depends on:** P05-T05, P03-T17
  - **Validation:** snapshot tests for HTML and text with the flag true and false; the option defaults to true when unset; the link target is defined once; a model carrying only the resolved name cannot leak an agent email (shape test); `EnvExampleCompletenessTests` pass


## Success Criteria

- [x] A `Trusted` key submission stores the external user ref and trusted metadata; a `Public` key or web submission does not and its metadata is flagged untrusted (tests).
- [x] One transaction creates requester, ticket, first message, `Created` event, access token hash and outbox row; failure leaves no partial state (`SubmitTicketIntegrationTests`).
- [x] Concurrent submissions never produce duplicate ticket numbers.
- [x] The Worker delivers queued emails, retries with backoff and dead-letters after the configured attempts; two Workers never double-claim a row (`EmailDrainIntegrationTests`).
- [x] Disallowed or oversize attachments, honeypot hits and rate-limited callers are handled exactly as specified in tests (400, silent 201, 429).
- [x] No response, log line or `PublicProductDto` exposes a token, key hash or other internal data (`SensitiveDataLeakTests` and `PublicProductEndpointTests`). Deliberate carve-outs: the API-key submission response carries `viewUrl`, and `email_outbox.payload` holds the first link (D-033).
- [x] `GET /api/public/products/{key}` returns branding with correct `Cache-Control`.
- [x] Email templates render the resolved agent name and honour `TECHSTRAP_PORTAL_SHOW_POWERED_BY`: the GitHub-linked line when true, none when false (D-024).
- [x] A repeated `Idempotency-Key` on API-key intake returns the original ticket number with a fresh link and creates no second ticket (D-020).
- [x] `docker compose up` produces a confirmation email in Mailpit from a real intake call (verified locally with `scripts/Send-TestTicket.ps1`; CI runs on the pull request).

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
      right scope, not bare magic values (size limits, retry count, backoff base, poll interval, token lifetime).
- [x] Duplicated-looking logic across flows was evaluated for genuine
      divergence before extracting (or intentionally not extracting) a shared
      abstraction (web-form and API-key intake share one handler; the two controllers stay separate because transport, auth and limits differ).

## Risks and Open Questions

- [x] Honeypot returns a fake success; confirm that is acceptable versus a visible error (**Assumption**: fake success). Resolved: D-032.
- [x] Dropping a public-key external user ref silently versus rejecting; confirm preference (**Assumption**: drop with a warning). Resolved: D-032.
- [ ] File storage and DB can diverge on a crash; compensating delete handles the common case, orphan sweep deferred to PHASE-12. Carried forward to PHASE-12.
- [x] Rendering emails at enqueue time freezes branding at that moment; acceptable for transactional mail. Resolved: D-033, render at send.
- [x] Content sniffing quality for the attachment allowlist; choose the mechanism during P05-T04 and note it in the decision log. Resolved: D-032 (declared type plus leading bytes).
- [x] `IEmailSender` shape in the pinned `SyntaxCircus.Email` version must be verified against the package source before implementation. Resolved: `EmailMessage` has `MessageId` and no custom headers; D-033 adapter.
- [ ] New-ticket agent alerts depend on `ITicketNotificationPlanner` (PHASE-06); the planner call is added to `SubmitTicketRequestHandler` there. Carried forward to PHASE-06.
- [ ] Unsubscribe and bounce handling are out of scope until inbound email (sub-project 2). Carried forward to that sub-project.
- [x] Carried forward from the PHASE-03 final review: `IAccessTokenService` is declared and implemented here (not in PHASE-03), together with the dev-key re-seed through the real hasher shared with PHASE-04. Delivered in this phase.
- [x] Carried forward from the PHASE-03 final review: Ticket access tokens slide 90 days with no absolute cap in the Domain: enforce an absolute expiry cap when tokens are issued and refreshed. Delivered in this phase (D-032).
- [x] Carried forward from the PHASE-03 final review: Intake must check that a supplied ticket number belongs to the product of the API key before attaching a follow-up to it. Moved to PHASE-09 (D-034).
- [x] Carried forward from the PHASE-03 final review: D-024 at email render: customer emails use `AgentPublicIdentity` and never the agent's name or address; test the rendered output. Delivered in this phase (P05-T18).
- [x] Carried forward from the PHASE-04 final review: Callers of `IApiKeyHasher.Verify` must guard against a null or missing `X-Api-Key`, because `Verify` throws on null. Delivered in this phase.
- [x] Carried forward from the PHASE-04 final review: Intake endpoints need an explicit API-key policy. Decide whether the fallback policy becomes the Agent policy. Resolved: D-034 (explicit Public and ApiKey policies).
- [ ] Carried forward (D-033): the plaintext customer link token in `email_outbox.payload` needs a retention or redaction job. Target: PHASE-12 (retention).
- [ ] Carried forward (D-034): attachments on the API-key route (JSON-only in v1) and the follow-up ticket-number ownership check. Target: PHASE-11 (API attachments) and PHASE-09 (follow-ups).
- [ ] Known limit (Task 13): unauthenticated callers can create rate-limit partitions by inventing key prefixes. Each such request costs one indexed hash lookup and ends in a 401, and idle fixed-window partitions are evicted. Recorded in `docs/development/INTAKE.md`.

## Handoff

Implementation plan: [2026-10-03-phase-05-intake-email-worker.md](../superpowers/plans/2026-10-03-phase-05-intake-email-worker.md). Operator guide: [INTAKE.md](../development/INTAKE.md).

Before PHASE-06 starts: an external caller can submit a ticket by all three routes and receive a branded confirmation email via the Worker; the outbox, token and attachment services are covered by integration tests; compose runs Worker and Mailpit. Next: [PHASE-06-ticket-operations.md](PHASE-06-ticket-operations.md).
