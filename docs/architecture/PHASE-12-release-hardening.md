# PHASE-12: Release Hardening

## Objective

Make TechStrap safe and repeatable to run for others and tag `v1.0.0`:
a documented security review of the token, API-key, upload and sanitizer paths
(with fixes), an intake load test with agreed budgets, a tested backup and
restore runbook, self-host guides (generic OIDC and an Authentik worked
example), a UAT deployment of the release candidate, and the `v1.0.0` tag with
images and packages published.

## Dependencies

- **Depends on:** all earlier phases: [PHASE-01](PHASE-01-foundation.md) through [PHASE-11](PHASE-11-client-sdk.md) (Phase 02 and 11 deliverables are inputs to the review and release).
- **Unblocks:** Post-1.0 sub-projects (inbound email, extras, workflows, passkeys; see `99-IMPLEMENTATION-ROADMAP.md`).
- **External prerequisites:** UAT host with the existing observability stack and CI/CD; the owner's Authentik instance and the `syntax-circus-authentik` repo (source of the Authentik provisioning/blueprint steps); GHCR and nuget.org publishing configured (P01, P11); SMTP relay for UAT; a scratch environment to rehearse restore.

## Architecture Decisions

- **No new server entry points or handlers.** Hardening changes modify existing handlers/infrastructure through normal PRs under their owning phase's boundary rules; each fix is a small PR with a regression test.
- **Security review is evidence-based:** a checklist per path (below) is executed, findings are recorded in `docs/security/SECURITY-REVIEW-1.0.md` with severity, status and the test that proves the fix. Release is blocked by any open High/Critical finding (**Assumption**: Medium may ship with documented acceptance).
- **Review scope (paths):**
  - *Access tokens (customer):* 256-bit CSPRNG, hashed at rest, constant-time compare, sliding expiry, revocation, uniform 404, no token in logs/Referer/caches/indexes, lost-link uniform response and timing, token scoped to one ticket + requester, follow-up ticket gets its own token.
  - *API keys:* hashed with `IApiKeyHasher`, shown once, prefix/identifier for lookup, revoke immediacy, Trusted vs Public privileges enforced server-side (Public cannot set external user ref or trusted metadata; metadata flagged untrusted and never rendered as trusted), per-key+IP rate limit works behind the proxy, SDK/MAUI public-key extraction threat model.
  - *Uploads (ticket attachments and KB images):* size limits (per file and per message), extension and magic-byte allowlist agreement, filename sanitization, random storage keys, no path traversal (`SyntaxCircus.Storage` Local), served as `attachment` with `nosniff`, no inline SVG/HTML, antivirus out of scope (documented), authorization on every download (agent or customer-token), KB image prefix isolation, storage quota/disk-full behavior.
  - *Sanitizer / rendering:* `IHtmlSanitizer` allow-list config reviewed; XSS corpus (OWASP cheat sheet + mutation payloads) run against message bodies, KB Markdown (API render and admin preview), email HTML templates (encoded user content), and every `MarkupString` site (admin: `MessageThread`, `KbPreviewPane`; portal: `KbArticleBody`, `CustomerMessageBody`); CSP and security headers verified on all hosts.
  - *Auth/authorization:* claim-gated policy on every agent endpoint, role checks (Admin-only actions per D-022), IDOR on ticket/attachment/KB ids, hub authentication and `access_token` query handling (P10), group-derived roles (D-029) and agent deactivation effect, token forwarding in admin, OIDC redirect URI/PKCE settings.
  - *Privacy/ops:* PII redaction in Serilog (emails, tokens, keys), erase-requester completeness (messages, attachments, search vectors, events payloads), hard delete, spam handling, secrets only via env (no secrets in images/compose/git), non-root containers, dependency vulnerability scan, SBOM.
- **Tooling (Assumption):** `dotnet list package --vulnerable --include-transitive`, `dotnet-outdated`-style review, container image scan (Trivy), optional Nistify scan/SBOM from the `nistify` project; findings triaged, not auto-fixed.
- **Load test:** k6 scripts in `tests/load/` (Assumption), exercising `POST /api/intake/tickets` (Trusted and Public keys), the portal form post, customer ticket reads and KB search against a compose/UAT stack with realistic payloads and attachments. **Initial budgets (Assumption; owner to confirm):** sustain 20 req/s intake for 10 min on the UAT box with p95 < 500 ms and 0 server errors; spike to 100 req/s for 60 s with rate limiting returning 429 (not 5xx) and recovery within 30 s; the email outbox drains within 2 min of the spike's end; Postgres connections stay below the pool maximum; no dead letters caused by load. Results and the box specs are stored in `docs/load-test-results.md`.
- **Backup/restore runbook (`docs/runbooks/backup-restore.md`):** `pg_dump -Fc` of the TechStrap database + archive of the attachments volume (and the `kb-images/` prefix, and the data-protection keys volume for admin/portal) taken consistently (dump first, then volume; attachments are append-only so ordering is safe); encrypted at rest/off-box; retention schedule; scheduled script; **restore drill** into a scratch compose stack, then verification (row counts, a ticket with attachment opens, migration state, admin login). Documents RPO/RTO targets (**Assumption**: RPO 24 h, RTO 4 h), required secrets/env, disaster scenarios (DB corruption, volume loss, host loss) and what is *not* backed up (observability data). Follows the `docs/runbooks/` convention used in sibling repos.
- **Self-host docs:** `docs/self-hosting.md` (generic OIDC: authority, audience, required claims, group claim name and values, access-token must contain the group claim, `offline_access`, redirect/post-logout URIs, PKCE; compose layout, env reference table for all hosts, reverse proxy and forwarded-headers/pinned subnet, TLS outside compose, SMTP, storage volumes, first admin via the IdP admin group (D-029), upgrade and rollback procedure, health checks, PgBouncer caveat for `LISTEN`) and `docs/self-hosting-authentik.md` (worked example referencing the `syntax-circus-authentik` repo for provider/blueprint setup rather than duplicating it; shows the group mapping to `TECHSTRAP_AGENT_GROUP`/`TECHSTRAP_ADMIN_GROUP` and claim scope mapping). A non-Authentik provider (e.g. Keycloak) is mentioned as untested (**Assumption**).
- **UAT deployment:** release candidate tag `v1.0.0-rc.N` builds images and packages; `deploy/docker-compose.yml` with `deploy/.env.uat.example` deploys on the UAT box (D-043: image-only, scoped env files under `/etc/techstrap/uat/`, a separate Postgres; runbook `docs/self-hosting/DEPLOYMENT.md`); soak for at least 48 hours with seed-free real usage by the owner (**Assumption**); dashboards/alerts in the existing observability stack cover health, error rate, outbox lag, dead letters and listener reconnects.
- **Release:** `v1.0.0` annotated tag on `main` after docs merge; the tag workflow publishes four GHCR images (`ghcr.io/syntax-circus/techstrap-{api,admin,portal,worker}`), the three NuGet packages, and a GitHub Release with notes (Conventional Commits changelog). Version comes from GitVersion; images also tagged `latest`.
- **Architecture conformance gate:** every API/worker/hub entry point named in [02-ARCHITECTURE.md](02-ARCHITECTURE.md) appears in exactly one phase table (P04–P06, P08, P10) and every controller action/hub method/hosted loop calls exactly one named handler; verified by Architecture.Tests plus a doc-vs-code reflection report.

## Application Boundaries

Follow _template APPLICATION_ARCHITECTURE.md. Phase 12 adds **no new server
entry points and no new handlers**. Fixes found during review are made inside
the existing handlers/infrastructure of the owning phase. The table records
what the phase audits and the exempt operational endpoints it verifies.

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| All existing API controller actions, hub methods, listener, worker loops | Existing handlers per P04, P05, P06, P08, P10 (full catalog in [02-ARCHITECTURE.md](02-ARCHITECTURE.md)) | Unchanged | Unchanged except for security fixes | ProblemDetails mapping unchanged except for any hardened error behavior | Audited; **no new server entry point**; reflection test enumerates entry points and fails if any lacks exactly one handler |
| `POST /api/intake/tickets`, `POST /api/public/products/{key}/tickets` | `SubmitTicketRequestHandler` (P05) | Unchanged | Unchanged | Load-test target; abuse-path review | Audited and load-tested; fixes (limits, rate-limit tuning) land in P05's boundaries |
| Customer-token routes (view/reply/lost-link/attachment) | `GetCustomerTicketRequestHandler`, `AddCustomerReplyRequestHandler`, `RequestNewAccessLinkRequestHandler`, `GetAttachmentRequestHandler` (P06) | Unchanged | Unchanged | Uniform 404/identical-response behavior reviewed | Audited |
| Product API key and agent management routes | `CreateProductApiKeyRequestHandler`, `RevokeProductApiKeyRequestHandler`, `GetCurrentAgentRequestHandler`, `UpdateAgentRequestHandler` (P04) | Unchanged | Unchanged | Key lifecycle and group-derived roles reviewed | Audited |
| KB image upload and public KB routes | `UploadKbImageRequestHandler`, `RenderKbPreviewRequestHandler`, `GetPublishedKbArticleRequestHandler` (P08) | Unchanged | Unchanged | Upload/sanitizer reviewed | Audited |
| `TicketHub` and Postgres listener | `UpdateTicketPresenceHandler`, `RelayTicketChangeHandler` (P10) | Unchanged | Unchanged | Hub auth reviewed | Audited |
| `/health/live`, `/health/ready`, `/openapi/v1.json`, static assets | Exempt | `SyntaxCircus.AspNetCore.Common` / ASP.NET Core | Framework endpoints | 200/503, JSON, files | Exempt: confirmed they execute no application workflow; confirm OpenAPI is not exposing admin-only details in production and `/health/ready` reveals no secrets |
| Release workflows (images, NuGet, release notes) | Exempt | GitHub Actions | `.github/workflows/*` | Pipeline | Not a server application entry point |

## Razor Component Boundaries

Follow _template RAZOR_COMPONENT_ARCHITECTURE.md. **No new components** are
introduced. The table lists the existing components reviewed or touched by
hardening (all remain governed by their phases' decisions). A hardening change
that adds a component must first add its row to this table.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| Admin `MessageThread` (P07), `KbPreviewPane` (P08) — `MarkupString` sites | Unchanged (paired); review that `MarkupString` is only fed sanitized strings | Unchanged | Unchanged | Sanitized `MessageDto` body / sanitized `POST /api/kb/preview` output (D-021) |
| Portal `KbArticleBody`, `CustomerMessageBody` (P09) — `MarkupString` sites | Unchanged; test asserts these are the only `MarkupString` uses | Unchanged | Unchanged | `PublishedKbArticleDto.Html`, `CustomerMessageDto` body |
| Portal `ContactPage`, `CustomerReplyForm`, `AttachmentInput`, `LostLinkPage` (upload and token surfaces) | Unchanged; verify antiforgery, honeypot, client hints vs server limits | Unchanged | Unchanged | `SubmitTicketRequest`, `AddCustomerReplyRequest`, `RequestNewAccessLinkRequest` |
| Admin `NewApiKeyDialog`, `ApiKeysPanel` (P07) — secret handling | Unchanged; verify show-once and no persistence/logging of secrets | Unchanged | Secret cleared on close | `CreateProductApiKeyResponse` |
| Admin `KbImageUploadButton` (P08) | Unchanged; verify client pre-checks match server limits and server remains authoritative | Unchanged | Unchanged | `KbImageDto` |
| Error pages/boundaries (`GlobalErrorBoundary`, `NotFoundPage`) in both apps | Unchanged; verify no stack traces or ids leak in production | Unchanged | Unchanged | None |

## Syntax Circus Packages

Versions are already locked in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md); this
phase verifies configuration and upgrades only for security fixes.

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.AspNetCore.Common` | Security headers, ProblemDetails, health, correlation id | Verify CSP/headers on every host; no detail leakage in errors | Header test matrix per host; production error responses contain no exception detail |
| `SyntaxCircus.AspNetCore.Authentication` | JWT bearer, API keys | Policy/claim gating and API key handling are review targets | Negative authorization tests per endpoint group |
| `SyntaxCircus.AspNetCore.Serilog`, `SyntaxCircus.Observability` | Logging/telemetry | PII/token redaction; alerts and dashboards for UAT | Log redaction tests; dashboard/alert checklist |
| `SyntaxCircus.Storage` | Attachments/KB images | Upload path review, backup scope | Traversal/key tests; restore drill includes attachments |
| `SyntaxCircus.Email` | SMTP outbound | Template encoding, outbox/dead-letter behavior under load | Email template XSS/encoding test; load-test outbox drain |
| `SyntaxCircus.Blazor.Auth` | Admin token forwarding | Session expiry, token handling in circuits | Expiry/forwarding review; no tokens in logs |
| `SyntaxCircus.Blazor.Seo` | Portal SEO | Verify noindex/robots/sitemap exclusions for token pages | Host tests/prod smoke |
| `SyntaxCircus.DotEnv`, `.EntityFrameworkCore.Postgres`, `.Http.Resilience`, `.Common` | Config, migrations, clients | Self-host env reference and migration/advisory-lock behavior on restore/upgrade | Upgrade/restore drill, env table matches `.env.example` files |

## Deliverables

- [ ] `docs/security/SECURITY-REVIEW-1.0.md` with findings, fixes and test references; no open High/Critical items.
- [ ] Regression tests for every fixed finding; XSS and upload corpora committed as test fixtures.
- [ ] `tests/load/` scripts and `docs/load-test-results.md` meeting the agreed budgets.
- [ ] `docs/runbooks/backup-restore.md`, backup script(s), and a recorded restore drill.
- [ ] `docs/self-hosting.md` and `docs/self-hosting-authentik.md` (linking the `syntax-circus-authentik` repo).
- [ ] Dependency vulnerability report/SBOM attached to the release.
- [ ] UAT deployment of the release candidate with soak results and dashboards/alerts.
- [ ] Final documentation pass (README, CONTRIBUTING, SECURITY.md, architecture docs status updated).
- [ ] `v1.0.0` tag, GHCR images, NuGet packages and GitHub Release published.

## Actionable Tasks

- [ ] **P12-T01** Create the security-review checklist document with the six path groups above, severity scale and finding template
  - **Depends on:** P01–P11 complete
  - **Validation:** Checklist reviewed by the owner; every item has an owner and a verification method (test, manual step, or tool).
- [ ] **P12-T02** Review and test the customer access-token path (generation, hashing, comparison, expiry/revocation, uniform 404, lost-link equality, logging/Referer/caching)
  - **Depends on:** P12-T01
  - **Validation:** Tests: token length/entropy source asserted; DB contains only hashes; expired/revoked/unknown responses byte-identical; lost-link responses identical for known/unknown email; log capture and response headers contain no token leakage; findings logged.
- [ ] **P12-T03** Review and test the API-key path (hash storage, show-once, revoke, Trusted vs Public privileges, per-key+IP limits behind the proxy, untrusted metadata handling)
  - **Depends on:** P12-T01
  - **Validation:** Api.Tests: Public key cannot set external user ref/trusted metadata; revoked key rejected immediately; rate limit keyed on real client IP via forwarded headers and per key; metadata from Public keys is flagged `untrusted` in storage and UI; key never in logs.
- [ ] **P12-T04** Review and test upload paths (ticket attachments and KB images) against hostile files
  - **Depends on:** P12-T01
  - **Validation:** Fixture corpus: oversize, double extensions, mismatched magic bytes, path-traversal names, SVG/HTML disguised as images, zero-byte, Unicode/RTL-override names; all rejected or neutralized; downloads send `Content-Disposition: attachment` and `nosniff`; storage keys are server-generated; disk-full produces a clean error (not a partial ticket).
- [ ] **P12-T05** Review and test the sanitizer/rendering paths (message bodies, KB render, admin preview, email HTML) with an XSS corpus and enumerate all `MarkupString` sites
  - **Depends on:** P12-T01
  - **Validation:** Corpus run yields no executable output in any path; architecture test fails if `MarkupString` appears outside the four allowed components; email templates HTML-encode all user content (test with `<script>` subject).
- [ ] **P12-T06** Audit authorization: every agent endpoint group requires the policy; Admin-only operations (D-022) enforced; IDOR checks on tickets, attachments, KB, products; hub auth; group-derived role behavior (D-029)
  - **Depends on:** P12-T01
  - **Validation:** Parameterized Api.Tests enumerate all controller routes via endpoint metadata and assert 401 anonymous / 403 non-agent / 403 Agent-on-Admin-route; the test fails for a newly added route lacking a policy.
- [ ] **P12-T07** Verify privacy handling: Serilog redaction (emails, tokens, keys), erase-requester completeness (messages, attachments, search vectors, event payloads), hard delete, spam
  - **Depends on:** P12-T01
  - **Validation:** Integration test erases a requester and asserts no PII remains in tables, files or logs; log capture shows redacted placeholders.
- [ ] **P12-T08** Verify headers/CSP/CORS/forwarded-headers and error-detail behavior on API, admin and portal in Production mode
  - **Depends on:** P12-T01
  - **Validation:** Test matrix per host: CSP, `X-Content-Type-Options`, `Referrer-Policy`, HSTS note (TLS terminated outside), no CORS wildcard for credentialed endpoints, ProblemDetails without stack traces; results recorded.
- [ ] **P12-T09** Run dependency vulnerability scan, container image scan and generate an SBOM; triage findings
  - **Depends on:** P11 (final dependency set)
  - **Validation:** `dotnet list package --vulnerable --include-transitive` clean or accepted; image scan has no High/Critical without a documented waiver; SBOM stored with the release artifacts.
- [ ] **P12-T10** Fix findings from T02–T09, each as a small PR with a regression test; update the review document statuses
  - **Depends on:** P12-T02 … P12-T09
  - **Validation:** All High/Critical closed with test references; Medium accepted or fixed with rationale; `dotnet test` green.
- [ ] **P12-T11** Write k6 load scripts (intake Trusted + Public key, portal form post, customer view, KB search) with realistic payloads and a scenario runner
  - **Depends on:** P05, P09
  - **Validation:** Scripts run against local compose; thresholds encoded in the scripts (p95, error rate); README explains parameters.
- [ ] **P12-T12** Execute load tests on the UAT box, tune (rate limits, pool sizes, indexes) and record results
  - **Depends on:** P12-T11, P12-T14 (UAT up)
  - **Validation:** Budgets met (sustained 20 req/s, p95 < 500 ms, 0 5xx; spike yields 429s then recovery; outbox drains <= 2 min; no dead letters); results and box specs in `docs/load-test-results.md`; tuning changes landed with tests.
- [ ] **P12-T13** Write `docs/runbooks/backup-restore.md` and the backup script(s); schedule on UAT
  - **Depends on:** P01 compose, P05 storage layout, P08 image prefix
  - **Validation:** Reviewed against the actual volume/DB names in compose; script produces encrypted dump + volume archive; retention pruning verified on a test directory.
- [ ] **P12-T14** Deploy the first published version (`v0.1.0`) to UAT with `deploy/docker-compose.yml`, the env files made from the `deploy/.env.<app>.example` templates and `deploy/.env.uat.example` (D-043); wire dashboards and alerts. The first published version is `v0.1.0` (D-049 addendum); `v1.0.0` is cut when the SDK-facing Contracts surface is locked.
  - **Depends on:** P12-T10 (or accepted open items), P11-T16
  - **Validation:** All four containers healthy; migrations applied once by the API; agent signs in via the owner's Authentik; a ticket submitted through the portal, SDK sample and email link works end to end; alerts fire in a forced-failure test (stop worker -> outbox lag alert).
- [ ] **P12-T15** Perform the restore drill from a UAT backup into a scratch stack and record timings and verification results in the runbook
  - **Depends on:** P12-T13, P12-T14
  - **Validation:** Restored stack passes: migration state matches, ticket counts match, an old attachment downloads, admin login works; measured RTO within target (or target adjusted and noted).
- [ ] **P12-T16** Extend `docs/self-hosting/DEPLOYMENT.md` (D-043) and write `docs/self-hosting.md` (generic OIDC, an env reference built from the `deploy/.env.<app>.example` templates and the contract test, proxy/subnet/forwarded headers, SMTP, volumes, admin group, upgrade/rollback, PgBouncer note)
  - **Depends on:** P12-T14
  - **Validation:** A clean-room run-through by following only the doc (fresh VM or fresh compose project with a non-Authentik test IdP, e.g. a local OIDC test server) reaches a working agent sign-in and ticket flow; env table matches every `.env.example` (script check).
- [ ] **P12-T17** Write `docs/self-hosting-authentik.md` referencing `syntax-circus-authentik` for provider/application setup and showing the group-to-claim mapping used by `TECHSTRAP_AGENT_GROUP`/`TECHSTRAP_ADMIN_GROUP`
  - **Depends on:** P12-T16
  - **Validation:** Steps reproduced against a fresh Authentik application/provider in UAT; agent in group signs in, non-member rejected; links to the authentik repo resolve; no secrets or copied provisioning code in this repo.
- [ ] **P12-T18** Soak the RC on UAT for at least 48 hours with real use; triage and fix regressions; collect SDK feedback
  - **Depends on:** P12-T14, P12-T15
  - **Validation:** Soak log shows no unexplained 5xx, no dead letters, stable memory/connections, listener reconnect count explained; issues filed and fixed or deferred with reasons.
- [ ] **P12-T19** Run the architecture conformance gate: reflection report of entry points vs. handler catalog vs. phase tables; confirm Architecture.Tests pass (project-reference direction, one-handler-per-action, `MarkupString` rule)
  - **Depends on:** P12-T10
  - **Validation:** Report lists every entry point once with its handler; discrepancies fixed in docs or code; CI green.
- [ ] **P12-T20** Final documentation pass: README (quickstart, screenshots), CONTRIBUTING, SECURITY.md contact/process, update statuses in `00-DISCOVERY-INDEX.md`/`99-IMPLEMENTATION-ROADMAP.md`, mark the original spec superseded, add CHANGELOG/release notes draft
  - **Depends on:** P12-T16, P12-T17, P12-T19
  - **Validation:** Link check passes; docs reference the actual shipped env keys and image names; owner approves the release notes.
- [ ] **P12-T21** Tag and publish `v1.0.0` from `main`; verify images, packages, release and the upgrade path from `rc`
  - **Depends on:** P12-T18, P12-T20
  - **Validation:** CI publishes `ghcr.io/syntax-circus/techstrap-{api,admin,portal,worker}:1.0.0` and `latest`, the three NuGet packages at `1.0.0`, and a GitHub Release with SBOM/notes; `docker compose pull && up` on UAT upgrades from rc without data loss; fresh install from the self-host guide works against the released images.

## Success Criteria

- [ ] Security review complete with no open High/Critical findings; each fix has a regression test; accepted Mediums are documented.
- [ ] Load budgets are met on the UAT box and results recorded; abuse load yields 429s, not failures.
- [ ] A backup is restored into a scratch environment and verified; runbook timings recorded.
- [ ] Someone following only `docs/self-hosting.md` (and the Authentik guide for the example) can deploy a working instance with their own OIDC provider.
- [ ] UAT ran the RC for at least 48 hours without unresolved defects.
- [ ] Every entry point maps to exactly one named handler and appears in exactly one phase table (conformance report).
- [ ] `v1.0.0` images and NuGet packages are published and installable.
- [ ] `dotnet build`, `dotnet test`, load thresholds and CI are green on the tag.

## Boundary Validation

- [ ] Application use-case entry points delegate to the named handlers listed above (audited across all phases; no new entry points).
- [ ] Framework-owned operational or static exemptions execute no application workflow (health, OpenAPI, static assets verified).
- [ ] Handler constructor dependencies contain only approved abstractions (architecture test re-run; security fixes introduced no concrete infrastructure dependency).
- [ ] Persistence and integration entities do not cross infrastructure boundaries.
- [ ] Cancellation reaches asynchronous handler dependencies (spot-checked on upload and intake paths during review).
- [ ] Expected outcomes and transport mapping have focused tests (hardened error responses are covered).
- [ ] Infrastructure implementations have integration coverage where applicable (backup/restore drill and storage/upload paths).
- [ ] Inline Razor components contain only simple parameters and, at most, one trivial synchronous `EventCallback`-forwarding callback (no changes; re-checked by architecture tests).
- [ ] Every component beyond the inline ceiling uses paired `.razor` and `.razor.cs` files, with all C# in code-behind.
- [ ] Each Razor ViewModel is feature-local and presentation-only; no direct-model decision exposes an API ViewModel.
- [ ] A factory or presentation service is used only for non-trivial mapping, asynchronous assembly, or multiple dependencies (no additions in this phase).
- [ ] API request and response contracts use DTO names and contracts, never Razor ViewModels.
- [ ] Repeated or business-meaningful literals are named constants at the right scope (limits, TTLs and budgets introduced or tuned here live in constants/options, not inline).
- [ ] Duplicated-looking logic across flows was evaluated for genuine divergence before extracting (the two attachment pass-throughs and the two sanitized-render paths are reviewed together; consolidate only if the review shows a shared defect pattern).

## Risks and Open Questions

- [ ] Load budgets are **Assumptions**; confirm against the UAT box capacity and expected real traffic.
- [ ] UAT shares a host with observability and CI/CD; load tests may disturb them — schedule off-hours and cap resources.
- [ ] Restore drill requires a spare environment; if none, use a temporary compose project on the UAT box.
- [ ] Antivirus scanning of uploads is out of scope for 1.0 (documented in the review and self-host guide); revisit if public uploads are abused.
- [ ] Single-instance API (presence store, in-process publish; see [PHASE-10](PHASE-10-live-updates.md)) is a documented scaling limit.
- [ ] Non-Authentik OIDC providers are untested beyond a generic test IdP.
- [ ] `v1.0.0` locks the Contracts public API (see [PHASE-11](PHASE-11-client-sdk.md)); confirm nothing needs to change before tagging.
- [ ] Trivy/Nistify tool availability and licensing on the owner's CI are not confirmed (**Assumption**: free/OSS usage).
- [ ] Carried forward from the PHASE-03 final review: Ticket search GIN plan and shape on load-sized data (shared with PHASE-06).
- [ ] Carried forward from the PHASE-03 final review: The search vectors are generated columns, so a write does not read the new vector back; revisit the tsvector read-back on writes if a handler needs it.
- [ ] Carried forward from the PHASE-04 final review: Add a deterministic lock-path test for the last-admin guard.
- [ ] Carried forward from the PHASE-04 final review: The actor is read before the last-admin lock is taken; move the read after the lock or re-check.
- [ ] Carried forward from the PHASE-04 final review: Add a double-revoke end-to-end test, or an xmin concurrency token on `ProductApiKey`.
- [ ] Carried forward from the PHASE-04 final review: Forced tag delete loads each ticket one at a time (N+1); batch the loads.
- [ ] Carried forward from the PHASE-04 final review: Notification-preference validation looks up each product one at a time (N+1) and has no cap on the list size; batch the lookup and cap the list.
- [ ] Carried forward from PHASE-05 (D-033): done in PHASE-06c (D-039): the Worker deletes sent outbox rows after 90 days. Only the backup-retention statement remains here.

## Handoff

`v1.0.0` is tagged, published and running on UAT; the release notes list known
limitations (single API instance, no inbound email, no retention automation beyond the email outbox,
no passkeys). The follow-on sub-projects (inbound email, extras, workflows,
passkeys) each start with their own spec cycle using
`99-IMPLEMENTATION-ROADMAP.md` as the entry point; this phase is the last in
the v1 sequence.
