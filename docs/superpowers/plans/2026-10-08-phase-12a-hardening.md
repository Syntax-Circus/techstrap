# PHASE-12a Release hardening (part 1): Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Execute the PHASE-12 security review (T01-T10) and the architecture conformance gate (T19) as an evidence-based, test-backed pass over the six path groups, fold in the carried-forward PHASE-03/04 items (last-admin guard, double revoke, N+1 lookups), and make dependency and image scans fail CI. One pull request (PHASE-12a), recorded under D-051. No Contracts change, no new entry point, no new handler.

**Architecture:** The review is a living document, `docs/security/SECURITY-REVIEW.md` (a "Release 0.3.0" section), whose every checklist row cites either existing test names (proven), a new test added by a task of this plan (GAP), or a finding `SR-NN` (written reasoning, no test). Shared attack corpora live under `tests/Shared/Fixtures/` and are copied to each consuming test project's output folder; their helpers (`HostileUploadCorpus`, `XssCorpus`, `XssAssertions`) live in `tests/Shared/*.cs` and are linked into the projects that need them. Code changes are small and inside existing handlers/repositories (reordering the last-admin lock, batching two N+1 lookups, a cap). The conformance gate is an Architecture test whose report is its own test output. CI gains a vulnerable-package check and Trivy image scans (High/Critical fail the build), and `release.yml` attaches a CycloneDX SBOM per image to the GitHub Release.

**Tech Stack:** .NET 10, xUnit v3 + Shouldly + NSubstitute + bUnit, Testcontainers (Postgres), Pester, GitHub Actions, Trivy, jq.

**Spec:** `docs/architecture/PHASE-12-release-hardening.md` (Review scope, P12-T01..T10, P12-T19 map to the tasks below; its "Corrections (D-051)" section wins where it differs); `docs/architecture/04-DECISION-LOG.md` D-051 (being recorded by a parallel change), D-022, D-029, D-039, D-043.

### Owner decisions (2026-10-08), recorded as D-051
1. PHASE-12 ends at `v0.3.0`; `v1.0.0` is a later API-lock decision. Three pull requests: 12a hardening (this plan), 12b docs and scripts, 12c UAT and release.
2. The load and recovery budgets are accepted as stated. The UAT box and its Postgres exist; the Authentik clients and groups (owner action #7) must exist before 12c's T14.
3. OpenAPI stays served anonymously in Production; 12a asserts that the document and `/health/ready` reveal no secrets.
4. The carried-forward PHASE-03/04 items fold into 12a (the GIN plan on load-sized data goes to 12c with the load run).
5. Scans fail CI on High/Critical from day one; `.trivyignore` holds documented waivers.

### Decisions made while drafting (D-051 records the user-visible ones)
- **Review document shape** (`docs/security/SECURITY-REVIEW.md`): `# Security review`, `## Purpose and method`, `## Severity scale` (Critical/High/Medium/Low/Info), `## Finding template`, `## Release 0.3.0` with six `### N. <group>` checklists (tables `| Check | Evidence or finding |`), `## Findings` (`### SR-NN: title` followed by `- Path group:`, `- Severity:`, `- Status: Open|Fixed|Accepted`, `- Evidence:` lines), `## Architecture conformance` (filled in Task 8). The six groups are: 1 Customer access tokens, 2 API keys, 3 Uploads, 4 Sanitiser and rendering, 5 Authorization and headers (T06 and T08), 6 Privacy and operations (T07 and T09). A row whose test a later task adds says `Task N adds <TestName>` until that task completes it.
- **Findings beyond SR-01..SR-13** (found while drafting): SR-14 last-admin actor read before the lock (Low, fixed in Task 2); SR-15 N+1 lookups in forced tag delete and notification preferences, no cap (Low, fixed in Task 6); SR-16 revoke is idempotent but not concurrency-safe (two simultaneous revokes may both write and audit; the key ends revoked either way) (Low, Accepted, Task 2).
- **Double revoke (Task 2 ruling):** `RevokeProductApiKeyRequestHandler` returns `Success` when `key.IsRevoked`, so a sequential double revoke is 204 both times with one `ApiKeyRevoked` audit row. A concurrent double revoke can write twice, but the end state is identical and nothing security-relevant differs. So: test-only route, no xmin concurrency token, no migration (SR-16 records the residual).
- **Last-admin ruling (Task 2):** `UpdateAgentRequestHandler` resolves the actor (`CurrentAgent.RequireActiveAsync`) before `CountActiveAdminsLockedAsync`. The lock query is a tracked `FromSqlRaw`, so reading the actor after it returns the fresh row (an actor deactivated by the transaction we waited for is refused as `agent-inactive`). The handler is reordered; no new repository method.
- **Notification preference cap:** `DomainLimits.NotificationPreferencesMaxCount = 2000` (as built; was 200 in the first draft, raised because the Admin page saves the full set), error `notification-preferences-too-many` (Validation, target `preferences`); batch lookup `IProductRepository.GetExistingIdsAsync(IReadOnlyCollection<Guid>, ct) -> IReadOnlySet<Guid>`. Forced tag delete batches through `ITicketRepository.GetByIdsAsync(IReadOnlyCollection<Guid>, ct)` in chunks of 200 (a private constant `TicketBatchSize` of `DeleteTagRequestHandler`).
- **Fixture layout:** `tests/Shared/Fixtures/hostile-uploads/manifest.json` (+ `content/*.bin|*.txt` for small payloads; oversize payloads are generated from a `generate` entry), `tests/Shared/Fixtures/xss-corpus.txt` (one vector per line, `#` comment lines). Test projects copy them with `<None Include="../Shared/Fixtures/**" LinkBase="Fixtures" CopyToOutputDirectory="PreserveNewest" />` and read `Path.Combine(AppContext.BaseDirectory, "Fixtures", ...)`. The existing links (`<Compile Include="../Shared/*.cs" />`) are non-recursive, so fixture folders never become compile items. Projects that link only named files (Api.Tests, Infrastructure.IntegrationTests) add the new helper files by name.
- **`XssAssertions.ContainsNoActiveContent(string html)`** is scoped so that inert text does not false-positive: tag names `<script|iframe|object|embed|base|meta|form|math|template` anywhere; `on[a-z]+\s*=` only inside a tag (`<[^>]*\son[a-z]+\s*=`); `javascript:`, `vbscript:` and `data:text/html` only as the start of an attribute value (`=\s*["']?\s*(javascript|vbscript|data:text/html)`); `srcdoc` and `expression(` only inside a tag. A unit test (`XssAssertionsTests`) pins positives and negatives (`&lt;script&gt;` and `<a href="https://x" rel="noopener">` pass).
- **Preview and message-body tests live where the real sanitiser lives:** `Application.Tests` can only substitute the renderer, so the "corpus through `RenderKbPreviewRequestHandler`" check is an Api test over `POST /api/kb/preview`, and the message-body path is an Api test (hostile body submitted, read back as agent and as customer). The bUnit tests for `MessageBubble` and `CustomerMessageBody` pin that the components add no markup of their own around an already sanitised body (they cannot sanitise; D-021/D-045).
- **Disk full is injected at `IStorageProvider`** (SyntaxCircus.Storage), below `AttachmentStore` and `KbImageStore`, by replacing the registration with a `FailingStorageProvider` that delegates to the real provider and throws `IOException("No space left on device")` on the Nth `StoreAsync`. That exercises the stores' partial-object cleanup and the handlers' compensating deletes together.
- **SBOM and Release timing:** the GitHub Release is created by `publish-nuget.yml` (after NuGet publish, `gh release create`), while `release.yml` builds the images. `release.yml` therefore gets a third job `sbom` (needs `images`) that generates one CycloneDX file per pushed image with Trivy, then waits (bounded retry on `gh release view`) for the Release and runs `gh release upload --clobber`.
- **Conformance gate sources:** doc side = the section 7 tables of `02-ARCHITECTURE.md` (7.1 to 7.5; 7.6 is exempt); code side = controller actions (`ControllerBase` subclasses in `TechStrap.Api` with an `Http*Attribute`), `TicketHub` public methods, Api hosted services (constructor `I*Handler`) and Worker hosted loops (handler found by source scan, because they resolve scoped handlers from a scope factory).

## Global Constraints
- Build: SDK 10.0.401, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`; `dotnet build TechStrap.slnx -c Release` ends with 0 warnings. `_camelCase` private fields, PascalCase constants, file-scoped namespaces. Nothing in 12a touches `TechStrap.Contracts`.
- Architecture rules (tests/TechStrap.Architecture.Tests): Domain and Contracts stay dependency-free; the Admin references Contracts + Hosting only; the Portal references Contracts + Hosting only; no `HttpClient` in `.razor`/`.razor.cs`; no inline `<script>`/`<style>`; route literals only in `PortalRoutes` (`RouteLiteralTests`); public classes for Razor components; copy in `*Copy` classes; repeated numbers are named constants (the cap and batch size are named constants). Handler constructor dependencies stay approved abstractions (`HandlerConstructorDependencyTests`).
- Security: no secrets in the repo or in logs; the only secrets in tests are the existing fixtures (`TestJwt` signing key, `DevelopmentApiKeys`, Testcontainers credentials); corpora contain attack strings only, never live credentials.
- Migrations: none. No task adds one (Task 2 rules out the xmin token); `dotnet ef migrations has-pending-model-changes` must report "No changes". If a task is ever changed to add one, it needs `dotnet ef migrations add AddProductApiKeyVersion --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api` and an update of `tests/TechStrap.Api.Tests/ExpectedMigrations.cs`-style expectations.
- Encoding: non-ASCII in C# only as `\u` escapes (the RTL override is U+202E written as a backslash-u escape, NUL is `\u0000`, in code and in `manifest.json`); new files LF; existing files via the Edit tool (CRLF preserved); docs and fixtures ASCII (the corpus encodes non-ASCII vectors as `\u` escapes in the JSON manifest, never raw).
- Tests: xUnit v3 + Shouldly + NSubstitute (+ bUnit in Admin.Tests, Testcontainers in Infrastructure.IntegrationTests/Api.Tests); every awaiting test carries `Timeout` and `Xunit.TestContext.Current.CancellationToken`; no sleeps (use gates and bounded query polling). TDD with RED recorded; mutations run against committed code and restored with `git checkout -- <file>`.
- Commits: Conventional Commits, staged by explicit path (never `git add -A`/`-f`, never `.superpowers/`), `git diff --cached --stat` first, each ending with exactly:
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- Verification (whole PR): see the section below.

## Review Focus
1. A full disk on the second file of a submission must not leave a partial ticket, a partial file or a stack trace in the response - Task 3 `A_full_disk_on_the_second_file_is_a_clean_problem_response_with_no_ticket_and_no_file`.
2. An Admin route added without the Admin policy (or an existing one demoted) must fail a test - Task 5 `The_admin_only_route_set_matches_the_pinned_list`.
3. A vulnerable transitive package or a High/Critical image finding must fail CI - Task 7 (`ci.yml` steps pinned by `PublishWorkflow.Tests.ps1` `CI scans and SBOM (PHASE-12a)`).
4. A sanitiser or renderer regression on any one corpus vector must fail a named test per surface - Task 4 `Every_corpus_vector_sanitises_to_no_active_content` (and the KB, preview and email counterparts).
5. Erasing a requester must leave no email, name or token in any log event at any level - Task 6 `Erasing_a_requester_writes_no_email_name_or_token_to_any_log_event`.

---

### Task 1: Review document skeleton, existing evidence and findings (P12-T01)

**Files:**
- Create: `docs/security/SECURITY-REVIEW.md`
- Modify: `scripts/tests/RepositoryDocs.Tests.ps1` (new `Describe 'Security review (PHASE-12a)'`)

**Interfaces:**
- Produces: the document skeleton and the finding ids SR-01..SR-16 used by every later task (Tasks 2-7 update the rows and statuses named below; Task 8 finalises).

- [x] **Step 1: Pins first (RED).** Append to `RepositoryDocs.Tests.ps1`:
  ```powershell
  Describe 'Security review (PHASE-12a)' {
      BeforeAll { $script:Review = Get-RepoText 'docs/security/SECURITY-REVIEW.md' }

      It 'has the method, the severity scale, the finding template and a Release 0.3.0 section' {
          foreach ($heading in 'Purpose and method', 'Severity scale', 'Finding template', 'Release 0.3.0', 'Findings') {
              $script:Review | Should -Match ('(?m)^## ' + [regex]::Escape($heading) + '\s*$') -Because "the review needs a $heading section"
          }
          foreach ($level in 'Critical', 'High', 'Medium', 'Low', 'Info') { $script:Review | Should -Match ('(?m)^- \*\*' + $level + '\*\*') }
      }

      It 'has the six path-group checklists' {
          foreach ($group in '1\. Customer access tokens', '2\. API keys', '3\. Uploads', '4\. Sanitiser and rendering', '5\. Authorization and headers', '6\. Privacy and operations') {
              $script:Review | Should -Match ('(?m)^### ' + $group + '\s*$')
          }
      }

      It 'gives every SR finding a path group, a severity, a status and evidence' {
          $findings = [regex]::Matches($script:Review, '(?ms)^### (SR-\d\d): .*?(?=^### |^## |\z)')
          $findings.Count | Should -BeGreaterOrEqual 16
          foreach ($finding in $findings) {
              $finding.Value | Should -Match '(?m)^- Path group: .+$' -Because $finding.Groups[1].Value
              $finding.Value | Should -Match '(?m)^- Severity: (Critical|High|Medium|Low|Info)\s*$' -Because $finding.Groups[1].Value
              $finding.Value | Should -Match '(?m)^- Status: (Open|Fixed|Accepted)\s*$' -Because $finding.Groups[1].Value
              $finding.Value | Should -Match '(?m)^- Evidence: .+$' -Because $finding.Groups[1].Value
          }
      }

      It 'has no TODO anywhere' { $script:Review | Should -Not -Match 'TODO' }

      It 'is ASCII only' { ([regex]::IsMatch($script:Review, '[^\x00-\x7F]')) | Should -BeFalse }
  }
  ```
  Run `pwsh -File scripts/Invoke-ScriptTests.ps1`: RED (the file does not exist; `Get-RepoText` throws in `BeforeAll`).
- [x] **Step 2: Write the document.** Sections as in "Decisions made while drafting". Severity scale bullets in the form `- **Critical** - ...` (exploitable now, data or account takeover; blocks the release), `- **High** - blocks the release`, `- **Medium** - may ship with documented acceptance`, `- **Low** - tracked, fix when convenient or accept with reasoning`, `- **Info** - observation or design note, no action`. The finding template is a fenced block with the four bullet lines. Checklist rows (each `| Check | Evidence or finding |`; the names are existing tests unless the cell starts with "Task N adds"):
  1. **Customer access tokens.** CSPRNG 256-bit, hash at rest: `AccessTokenServiceTests.A_token_is_256_bits_of_base64url_and_only_its_hash_is_on_the_entity`, `Ten_thousand_tokens_never_collide`, `The_hash_is_the_documented_sha256`; Api `SensitiveDataLeakTests.A_key_submission_leaves_the_plaintext_key_and_token_out_of_logs_and_every_column_but_the_outbox_payload` (plaintext link in `email_outbox.payload`: SR-01). Compare: hashed DB lookup (`TicketRepository.GetAccessTokenByHashAsync`, `CustomerAccess`), `CustomerAccessTests.An_overlong_token_is_not_found_without_a_lookup` (SR-02). Sliding expiry 90 d / cap 365 d / revocation: Domain `TicketAccessTokenTests`, Infra `CustomerReplyIntegrationTests.A_successful_reply_slides_the_token_expiry`, `Tokens_are_revoked` (erase). Uniform 404: `CustomerAccessTests.Every_failure_is_the_same_error`, Api `CustomerUniformNotFoundTests.Every_failure_mode_returns_the_same_404_bytes`, Portal `TicketUniformNotFoundHostTests`, attachments `GetCustomerAttachmentRequestHandlerTests` and `CustomerAttachmentEndpointTests.Other_ticket_and_internal_note_attachments_look_like_missing_ones`; timing of unknown vs expired: same single DB lookup path (SR-02 reasoning, no timing test). Lost link: `LostLinkUniformityTests.Known_and_unknown_addresses_are_indistinguishable`, `The_email_goes_only_to_the_requesters_own_address`, `RequestNewAccessLinkRequestHandlerTests`, Portal `LostLinkHostTests`, `CustomerRateLimitTests.Lost_link_requests_are_limited_per_ip`. No token in logs/Referer/caches/indexes: Portal `TicketTokenLeakTests`, `TicketReplyHostTests.The_token_and_the_reply_text_never_reach_a_log_event_at_any_level`, `TicketHeaderHostTests`, Api `SensitiveQuerySentryProcessorTests`, `LogRedactionTests`, `CustomerErrorPathCacheTests`. Per-IP limit: `CustomerRateLimitTests.Token_access_is_limited_per_ip`. Follow-up token gets its own token: Task 2 adds `CustomerReplyIntegrationTests.A_follow_up_ticket_token_differs_from_the_parents_and_opens_only_its_own_ticket`.
  2. **API keys.** Hasher: `ApiKeyHasherTests` (incl. `Verify_compares_in_constant_time`); lookup by full hash, prefix is display/audit/rate-limit partition (SR-03, SR-10). Show once: `ApiKeyEndpointTests.Only_the_prefix_and_hash_are_stored_and_the_plaintext_is_shown_once`, `The_create_response_is_not_cacheable`, `CreateProductApiKeyRequestHandlerTests.The_audit_event_holds_the_prefix_but_no_secret_or_hash`, Admin `NewApiKeyDialogTests`. Revoke: `ApiKeyAuthTests.A_revoked_key_is_401`, `All_401_responses_are_identical`, `RevokeProductApiKeyRequestHandlerTests`; Task 2 adds `ApiKeyEndpointTests.A_key_revoked_through_the_api_is_401_on_the_next_intake_call` and `Revoking_twice_is_204_both_times_and_audits_once` (SR-16). Trusted vs Public: `SubmitTicketRequestHandlerTests.An_untrusted_submission_drops_the_external_ref_with_a_warning_and_marks_metadata_untrusted`, `An_untrusted_submitter_never_overwrites_a_known_external_ref`, Api `IntakeEndpointTests.A_public_key_submission_drops_the_external_ref_with_a_warning`, Admin `TicketDetailPageTests`. Isolation: `ApiKeyAuthTests.A_key_creates_tickets_only_for_its_own_product`, `An_agent_bearer_token_is_not_accepted_on_intake`, `An_api_key_is_not_accepted_on_agent_routes`, `ApiKeyEndpointTests.Revoking_a_key_of_another_product_is_404`. Rate limit per key+IP behind the proxy: `IntakeRateLimitTests`, `PublicApiHardeningTests.Behind_a_trusted_proxy_each_forwarded_visitor_has_their_own_limit`, `A_spoofed_forwarded_for_from_an_untrusted_peer_is_ignored`, `TrustedProxyStartupTests`. Never logged: `SensitiveDataLeakTests.Error_responses_never_echo_the_api_key`, `AdminLeakTests`, `LogRedactionTests.Api_keys_encoded_tokens_and_uppercase_hashes_are_redacted`. SDK/MAUI public-key extraction: SR-12.
  3. **Uploads.** Limits: `AttachmentStoreTests.An_oversize_file_is_rejected_even_if_its_declared_length_lies`, `SubmitTicketRequestHandlerTests` (six files / 25 MiB), `PublicIntakeEndpointTests.Six_files_is_400...`, `A_form_body_over_the_limit_is_413`, `RequestTooLargeMiddlewareTests`, Portal `AttachmentRulesTests`. Extension + magic bytes: `AttachmentStoreTests.A_renamed_executable_is_rejected_by_its_leading_bytes`, `A_declared_type_that_does_not_match_the_content_is_rejected`, `Text_with_binary_content_is_rejected`, `PublicIntakeEndpointTests.An_executable_renamed_to_pdf_is_400...` (text containing HTML: SR-04). Zero-byte: Portal `AttachmentRulesTests.An_empty_file_is_named`; Task 3 adds the server-side `AttachmentStoreTests.A_zero_byte_file_is_refused_as_attachment_empty`. File names: Domain `AttachmentFileNameTests`, `AttachmentStoreTests.A_path_traversal_file_name_never_escapes_the_root`, Portal `TicketAttachmentHostTests.A_right_to_left_override_and_quotes_are_removed_from_the_name`, `A_name_that_could_split_a_header_cannot`; Task 3 adds the corpus theories. Random keys: `AttachmentStoreTests.A_png_is_stored_under_a_random_key...`, `KbImageStoreTests.Two_uploads_of_the_same_file_get_different_keys` (v7 ids: SR-05). attachment + nosniff + sandbox: Api `AttachmentDownloadEndpointTests`, `CustomerAttachmentEndpointTests`, Portal `TicketAttachmentHostTests`, Admin `AttachmentPassThroughTests`. SVG/HTML images refused: `KbImageStoreTests.Anything_that_is_not_a_plain_png_jpeg_gif_or_webp_is_refused...`. KB prefix isolation: `KbImageStoreTests.An_attachment_cannot_be_reached_through_the_image_reader`, `A_name_the_store_could_not_have_written_is_never_opened`, Api `KbImageServingTests`. Download authorization: `GetAttachmentRequestHandlerTests.An_anonymous_caller_is_refused_without_a_lookup`, `AttachmentDownloadEndpointTests.An_anonymous_caller_is_401`. Disk full: `SubmitTicketRequestHandlerTests.An_exception_removes_the_files_the_attempt_stored_and_propagates`, `SubmitTicketIntegrationTests.A_failure_after_creation_leaves_no_rows_and_no_orphan_file`; Task 3 adds the HTTP-level tests; quota: SR-11; antivirus: SR-08.
  4. **Sanitiser and rendering.** Message sanitiser: `HtmlSanitizerTests` (inline vectors); Task 4 adds `HtmlSanitizerTests.Every_corpus_vector_sanitises_to_no_active_content`. KB: `KbHtmlSanitizerTests`, `KbContentRendererTests`, `MarkdownRendererTests`, `MessagePipelineUnchangedTests`; Task 4 adds `KbHtmlSanitizerTests.Every_corpus_vector_sanitises_to_no_active_content` and `KbContentRendererTests.Every_corpus_vector_renders_inert_as_markdown_and_as_html`. Admin preview: `RenderKbPreviewRequestHandlerTests`; Task 4 adds Api `KbPreviewXssTests.Every_corpus_vector_renders_to_no_active_content_in_the_preview`. Email: `EmailTemplateRendererTests.Customer_content_is_escaped_in_html_and_plain_in_text`, `The_reply_body_is_inserted_as_given_but_model_fields_are_encoded` (reply body raw because already sanitised); Task 4 adds `EmailTemplateRendererTests.Every_corpus_vector_is_encoded_in_the_subject_name_and_article_title_fields` and `The_sanitised_reply_body_stays_inert_in_the_email`. MarkupString sites (Admin `Features/Tickets/MessageBubble.razor` and `Features/Kb/KbPreviewPane.razor`; Portal `Components/Kb/KbArticleBody.razor` and `Components/Tickets/CustomerMessageBody.razor`): Admin.Tests `MarkupStringSiteTests.MarkupString_is_used_in_exactly_two_files_the_message_bubble_and_the_kb_preview_pane`, Arch `PortalRuleTests.In_09c_exactly_KbArticleBody_and_CustomerMessageBody_turn_text_into_markup`; Task 4 adds the bUnit pins `MessageBubbleBodyTests` and `CustomerMessageBodyTests` and the end-to-end Api `MessageBodyXssEndpointTests`. No inline script/handlers: `AdminRuleTests`, `PortalRuleTests`, Api `ContentSecurityPolicyHostTests.No_Admin_page_renders_an_inline_script...`.
  5. **Authorization and headers.** Every route names a policy: `RoutePolicyCoverageTests.Every_api_route_declares_exactly_one_known_policy`, `Every_public_and_api_key_route_is_rate_limited`, `PublicApiHardeningTests.The_fallback_authorization_policy_denies_anonymous_callers`. 401/403 matrix: `AgentAccessCoverageTests.Anonymous_callers_get_401_and_callers_outside_the_groups_get_403_everywhere`, `An_agent_group_member_is_refused_on_every_admin_only_route`, `A_deactivated_agent_is_refused_on_every_agent_endpoint`; Task 5 adds `The_admin_only_route_set_matches_the_pinned_list`. Group roles (D-029): `ClaimsCurrentAgentClaimsTests`, `AgentAuthTests`, `AgentAccessOptionsTests`, `AgentProvisioningTests`; deactivation: `AgentManagementEndpointTests`, Admin `AgentAccessHostTests`; last-admin guard: SR-14, Task 2 adds the lock-path tests. Hub: `HubPolicyCoverageTests`, `TicketHubTests`, `HubWebSocketTests`. IDOR: customer side rows 1 and 3, keys `ApiKeyEndpointTests.Revoking_a_key_of_another_product_is_404`, agents single-tenant (SR-06). OIDC/PKCE/forwarding: Admin `AdminSignInTests`. Headers/CSP: `SecurityHeadersHostTests`, `PathHeaderRuleHostTests`, `HealthEndpointTests.Responses_carry_security_headers`, `ContentSecurityPolicyHostTests`, `CspBuilderTests`, `CspStyleTests`, `KbImageCspTests`. HSTS: `StrictTransportSecurityHostTests` (Admin, Portal); Task 5 adds the API case. CORS: SR-07; Task 5 adds `CorsAbsenceHostTests.No_response_carries_an_Access_Control_Allow_Origin_header`. Errors: `ResultMappingTests`, Admin/Portal `UnhandledErrorHostTests`; Task 5 adds `ApiUnhandledErrorHostTests.An_unhandled_exception_in_Production_is_a_problem_response_without_detail_or_stack`. OpenAPI/health: `OpenApiSecurityTests`, `OpenApiSurfaceTests`, `HealthEndpointTests.The_OpenAPI_document_is_served_anonymously`; Task 5 adds `PublicDocumentsSecretsTests`.
  6. **Privacy and operations.** Redaction: `LogRedactionTests`, `PiiRedactionQueryValueTests`, `AdminHostRedactionTests`, Arch `LoggingSafetyTests`, Portal `RequestLogRedactionHostTests` (residuals: SR-09). Erase: `EraseRequesterIntegrationTests.Nothing_personal_remains_after_erase`, `Tokens_are_revoked`, `Outbox_rows_by_address_and_by_ticket_are_gone...`, `RequesterErasureTests`, Api `EraseRequesterEndpointTests`; Task 6 adds `EraseRequesterEndpointTests.Erasing_a_requester_writes_no_email_name_or_token_to_any_log_event`. Hard delete: `DeleteTicketIntegrationTests`, `DeleteTicketEndpointTests`. Spam: `MarkTicketSpamRequestHandlerTests`, `TicketTagAndSpamEndpointTests`. Non-root containers: Pester `Dockerfiles.Tests.ps1` (`USER 10001:10001`). Secrets only via env: `EnvExampleCompletenessTests`, `ProductionBlankTemplateTests`, Pester `ConfigContract`, `TrackedFiles`; image secret scan: Task 7 (Trivy secret scanner is on by default for `trivy image`; no separate history scan: SR-13 note). Forced tag delete / notification preferences N+1: SR-15, Task 6. Dependency scan, image scan, SBOM: Task 7, SR-13.
  Findings (use the finding template; severity / status at Task 1):
  - SR-01 group 1, Low, Accepted: the plaintext access link sits in `email_outbox.payload` until the 90-day purge (D-039); the payload is needed to send the mail; erase removes the rows; backups inherit the retention.
  - SR-02 group 1, Info, Accepted: the token is compared by a hashed database lookup (SHA-256 of a 256-bit random value, indexed), so there is no secret-dependent comparison to time; a `FixedTimeEquals` would add nothing. Unknown and expired tokens take the same path.
  - SR-03 group 2, Info, Accepted: API keys are looked up by the full SHA-256 hash (`ProductApiKeyValidator`); the stored prefix is for display, audit and the rate-limit partition (the spec's "prefix lookup" wording is corrected by D-051).
  - SR-04 group 3, Low, Accepted: a `.txt` or `.log` file whose text is HTML passes the signature check as `text/plain`; neutralised by `Content-Disposition: attachment`, `nosniff` and the download sandbox header; never rendered inline.
  - SR-05 group 3, Info, Accepted: storage keys use time-ordered v7 GUIDs; guessing one grants nothing because every read is authorized by ticket and role (agent) or token (customer).
  - SR-06 group 5, Info, Accepted: agents are single-tenant by design (D-004); any active agent can read any ticket, so there is no agent-side IDOR boundary to test.
  - SR-07 group 5, Info, Accepted: no CORS is configured anywhere, so browsers refuse cross-origin reads by default; Task 5 pins it.
  - SR-08 group 3, Low, Accepted: antivirus scanning is out of scope for 0.x; the controls are the allow-list, signature check, attachment disposition and the sandbox header.
  - SR-09 group 6, Low, Accepted: the redaction enricher matches emails, tokens, keys and sensitive query values; display names are not pattern-redacted and `Exception` objects are not rewritten (documented in the enricher remarks).
  - SR-10 group 2, Low, Accepted: an invented API-key prefix gets its own rate-limit partition per IP; the per-IP floor still applies, and a flood of invented prefixes cannot lock out a real key.
  - SR-11 group 3, Low, Open (closed by Task 3): no storage quota; a full disk is handled by cleanup and a clean error.
  - SR-12 group 2, Info, Accepted: the SDK and MAUI public key can be extracted from a shipped app; a Public key can only create tickets for its own product, cannot set an external user ref or trusted metadata, and is rate limited per key and IP; revoke and rotate if abused.
  - SR-13 group 6, Info, Open (closed by Task 7): the local `dotnet list package --vulnerable --include-transitive` result; the image scan runs in CI.
  - SR-14 group 5, Low, Open (closed by Task 2): the last-admin guard read the actor before taking the lock.
  - SR-15 group 6, Low, Open (closed by Task 6): forced tag delete and notification-preference validation looked rows up one at a time; preferences had no cap.
  - SR-16 group 2, Low, Open (closed by Task 2): revoke is idempotent but a concurrent double revoke can write and audit twice.
- [x] **Step 3: GREEN** `pwsh -File scripts/Invoke-ScriptTests.ps1`; `git diff --check`; confirm the file is LF and ASCII (`Select-String -Pattern '[^\x00-\x7F]'` finds nothing).
- [x] **Step 4: Mutation.** Delete the `- Severity:` line of SR-05 (the finding pin dies); add the text `TODO` to a row (the TODO pin dies); restore.
- [x] **Step 5: Commit** `docs(security): evidence-based security review document for release 0.3.0 (P12-T01)`.

### Task 2: Tokens, keys and the carried-forward PHASE-04 items (P12-T02, P12-T03 gaps)

**Files:**
- Modify: `src/TechStrap.Application/Agents/UpdateAgentRequestHandler.cs` (lock before the actor read), `docs/security/SECURITY-REVIEW.md`
- Test: `tests/TechStrap.Infrastructure.IntegrationTests/CustomerReplyIntegrationTests.cs` (extend), `tests/TechStrap.Api.Tests/Products/ApiKeyEndpointTests.cs` (extend), `tests/TechStrap.Application.Tests/Agents/UpdateAgentRequestHandlerTests.cs` (extend), `tests/TechStrap.Infrastructure.IntegrationTests/AgentAdminLockTests.cs` (extend)

**Interfaces:**
- Consumes: Task 1 (rows and SR-14, SR-16). Produces: the reordered handler (no signature change); SR-14 Fixed, SR-16 Accepted.

- [x] **Step 1: Failing tests.**
  - `CustomerReplyIntegrationTests.A_follow_up_ticket_token_differs_from_the_parents_and_opens_only_its_own_ticket`: seed a Closed ticket with a token (as `A_reply_on_a_closed_ticket_commits_the_follow_up_its_message_tokens_and_emails` does), reply on it, then read both plaintext links from the outbox payloads the way that test does; assert the two plaintext tokens differ, their stored hashes differ, `GetAccessTokenByHashAsync(hash(followUpToken)).TicketId` is the follow-up's id and the parent's token still resolves to the parent's id (and not the follow-up's).
  - `ApiKeyEndpointTests.A_key_revoked_through_the_api_is_401_on_the_next_intake_call`: as an Admin create a product and a key through the API (same helpers the class already uses), submit once with `HeaderNames.ApiKey` (201), `DELETE /api/products/{id}/api-keys/{keyId}` (204), submit again: 401, and the response bytes and headers equal those of a request with an unknown key (the `All_401_responses_are_identical` comparison).
  - `ApiKeyEndpointTests.Revoking_twice_is_204_both_times_and_audits_once`: two `DELETE` calls both 204; `SELECT count(*) FROM admin_events WHERE type = 'ApiKeyRevoked'` (use the column names `ApiTestDatabase.ScalarAsync` callers in this class use) is 1.
  - `UpdateAgentRequestHandlerTests.The_admin_lock_is_taken_before_the_actor_is_read_untracked`: `Received.InOrder(() => { _agents.CountActiveAdminsLockedAsync(...); _agents.GetBySubjectAsync("actor", ...); })` on a deactivation.
  - `UpdateAgentRequestHandlerTests.An_actor_deactivated_while_waiting_for_the_lock_is_refused`: `CountActiveAdminsLockedAsync` is stubbed with `.Returns(call => { _actor.SetActive(false); return 2; })` (the other transaction committing while we waited); the result is the failure `agent-inactive` and `_agents.DidNotReceive().Update(Arg.Any<Agent>())`.
  - `AgentAdminLockTests.A_second_counter_waits_for_the_first_unit_of_work_and_sees_its_commit`: seed admins A and B; scope 1 starts a unit of work and calls `CountActiveAdminsLockedAsync` (count 2); scope 2 starts its own unit of work and starts the same call as an un-awaited task; poll (loop over `pg_stat_activity` with `wait_event_type = 'Lock'` and `query LIKE '%FOR UPDATE%'`, each iteration an awaited query under the test timeout, no sleeps) until the waiting backend appears and assert scope 2's task is not completed; in scope 1 deactivate B (`SetActive(false)`, update) and commit; scope 2's task now completes with count 1.
- [x] **Step 2: RED.** `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter-class "*UpdateAgentRequestHandler*"` (the two ordering tests fail); the Infra and Api additions are new behaviour proofs of existing code and should pass: record that (they are GAP tests, not regressions).
- [x] **Step 3: Implement.** In `UpdateAgentRequestHandler.HandleAsync` validate `request.IsActive` first (no database), then take the lock, then resolve the actor:
  ```csharp
  if (request.IsActive is not { } isActive) { /* unchanged validation failure */ }

  // Lock first: the lock query tracks the admin rows, so the actor read after it is the committed state of the transaction we queued behind.
  var activeAdmins = isActive ? 0 : await agents.CountActiveAdminsLockedAsync(cancellationToken);
  var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
  if (actor.IsFailure) { return Result<AgentDto>.Failure(actor.Errors[0]); }
  ```
  Update the class remarks (one sentence) and the existing handler tests that assume the old order (none should, they stub both). No other file changes: `RevokeProductApiKeyRequestHandler` is already idempotent and gets no xmin token (Decisions made while drafting).
- [x] **Step 4: GREEN.** `dotnet test --project tests/TechStrap.Application.Tests -c Release`, `--project tests/TechStrap.Infrastructure.IntegrationTests --filter-class "*CustomerReply*" --filter-class "*AgentAdminLock*"`, `--project tests/TechStrap.Api.Tests --filter-class "*ApiKeyEndpoint*"` (Docker); build 0 warnings; Architecture tests.
- [x] **Step 5: Mutations.** Move the actor read back before the lock (both ordering tests die); make the follow-up reuse the parent's token (the follow-up token test dies); drop the `IsRevoked` early return so a second revoke audits again (`Revoking_twice...` dies); remove `FOR UPDATE` from `CountActiveAdminsLockedAsync` (the lock-path test dies); restore each.
- [x] **Step 6: Review document.** Group 1 follow-up row and group 2 revoke rows cite the new tests; SR-14 Status Fixed (evidence: the two ordering tests and the lock-path test); SR-16 Status Accepted (evidence: `Revoking_twice_is_204_both_times_and_audits_once`; concurrent writes converge on the same end state, no migration).
- [x] **Step 7: Commit** `test(api,infra): follow-up token isolation, revoke-then-use, double revoke; last-admin actor read after the lock (P12-T02, P12-T03)`.

### Task 3: Uploads (P12-T04)

**Files:**
- Create: `tests/Shared/Fixtures/hostile-uploads/manifest.json`, `tests/Shared/Fixtures/hostile-uploads/content/*` (small payloads), `tests/Shared/HostileUploadCorpus.cs`, `tests/TechStrap.Api.Tests/Support/FailingStorageProvider.cs`, `tests/TechStrap.Api.Tests/Intake/DiskFullIntakeTests.cs`, `tests/TechStrap.Api.Tests/Kb/KbImageDiskFullTests.cs`
- Modify: `tests/TechStrap.Infrastructure.IntegrationTests/TechStrap.Infrastructure.IntegrationTests.csproj` and `tests/TechStrap.Api.Tests/TechStrap.Api.Tests.csproj` (fixture copy item + `Compile Include="../Shared/HostileUploadCorpus.cs" LinkBase="Shared"`), `tests/TechStrap.Infrastructure.IntegrationTests/AttachmentStoreTests.cs`, `KbImageStoreTests.cs`, `tests/TechStrap.Api.Tests/Intake/PublicIntakeEndpointTests.cs`, `docs/security/SECURITY-REVIEW.md`

**Interfaces:**
- Produces: `HostileUploadCorpus.All` (`IReadOnlyList<HostileUpload>`; `HostileUpload { string Id; string FileName; string? DeclaredContentType; byte[] Content; Expectation Attachment; Expectation KbImage }`, `Expectation { bool Stored; string? ErrorCode; string? StoredName }`), `HostileUploadCorpus.Rows()` as `IEnumerable<TheoryDataRow<string>>` of ids and `HostileUploadCorpus.Get(id)`. `FailingStorageProvider(IStorageProvider inner, int failOnStoreCall)` plus `FailingStorageProvider.Register(IServiceCollection, int failOnStoreCall)` which replaces the `IStorageProvider` registration.

- [x] **Step 1: Corpus.** `manifest.json` is an array of entries `{ "id", "fileName", "declaredContentType", "content": "content/<file>" | { "generate": { "prefixHex", "totalBytes" } }, "attachment": { "stored": bool, "errorCode", "storedName" }, "kbImage": { ... } }`. File names are JSON strings using `\u` escapes. Entries (id: file name; content; attachment outcome; KB outcome):
  - `oversize`: `big.png`; generated, prefix `89504e470d0a1a0a`, 10 MiB + 1 bytes; attachment rejected `attachment-too-large`; KB rejected `kb-image-too-large`.
  - `double-extension`: `report.pdf.exe`; content `content/mz.bin` (bytes `MZ` + padding); attachment rejected `attachment-type-not-allowed`; KB rejected `kb-image-type-not-allowed`.
  - `png-named-pdf`: `scan.pdf`; a 1x1 PNG (`content/pixel.png`, declared `application/pdf`); attachment rejected `attachment-type-not-allowed`; KB stored (the name is ignored; type decided by the bytes).
  - `traversal-passwd`: `../../etc/passwd`; text; attachment rejected `attachment-type-not-allowed` (no allowed extension after sanitising to `passwd`); KB rejected `kb-image-type-not-allowed`.
  - `traversal-text`: `../../notes.txt`; text `hello`; attachment stored as `notes.txt`; KB rejected.
  - `backslash-traversal`: `..\\..\\x.txt`; text; attachment stored as `x.txt`; KB rejected.
  - `rtl-override`: `invoice` + U+202E (as a backslash-u escape in the JSON) + `txt.png`; the 1x1 PNG; attachment stored as `invoicetxt.png`; KB stored.
  - `zero-byte`: `empty.txt`; empty file; attachment rejected `attachment-empty`; KB rejected `kb-image-type-not-allowed` (read `KbImageStore` first; if it answers a different code for empty input, pin that code - the store is unchanged by this task).
  - `svg-as-png`: `logo.png`; `<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)"/>`; attachment rejected `attachment-type-not-allowed`; KB rejected `kb-image-type-not-allowed`.
  - `html-as-txt`: `page.txt`; `<html><script>alert(1)</script></html>`; attachment stored as `page.txt` with content type `text/plain` (SR-04); KB rejected.
  - `long-name`: 300 characters (`a` repeated 296 + `.txt`; write the string out in the manifest); text; attachment stored with a name of at most 255 characters ending `.txt`; KB rejected. (Express `storedName` as `"maxLength:255,suffix:.txt"` in a second field `storedNameRule` and test accordingly.)
  - `nul-in-name`: `a\u0000b.txt`; text; attachment stored as `ab.txt`; KB rejected.
  `HostileUploadCorpus` loads the manifest with `System.Text.Json` from `Path.Combine(AppContext.BaseDirectory, "Fixtures", "hostile-uploads")` and materialises generated payloads. A test `HostileUploadCorpusTests.The_manifest_names_every_hostile_class_in_the_spec` (Infra) asserts the ids above are all present and every `content` file exists.
- [x] **Step 2: Failing tests (RED).**
  - Infra `AttachmentStoreTests.Every_hostile_upload_gets_its_recorded_outcome` (theory over `HostileUploadCorpus.Rows()`): save through the real store (the class's existing construction over a temp-root local storage provider); `Stored` -> success with `FileName` equal to `storedName`/matching the rule, key matches `attachments/{ticket:N}/{32 hex}`, nothing written outside the temp root; rejected -> failure with the recorded error code and zero files on disk.
  - Infra `AttachmentStoreTests.A_zero_byte_file_is_refused_as_attachment_empty` (a named fact for the gap even though the theory covers it).
  - Infra `KbImageStoreTests.Every_hostile_upload_gets_its_recorded_kb_outcome` (theory): stored -> key `kb-images/{32 hex}.png`, none of the file name survives; rejected -> the recorded code, no file left.
  - Api `PublicIntakeEndpointTests.Every_hostile_upload_through_the_multipart_form_gets_its_recorded_outcome` (one fact looping the corpus, one request per entry, aggregating failures into one message; use a fresh `X-Forwarded-For`-free client per entry only if the public-submit limit would be hit - otherwise raise the limit through the settings the class already uses): rejected -> 400 with the error code in `errorCodes`, `SELECT count(*) FROM tickets` unchanged and no file under the storage root; stored -> 201/200 and exactly one file under `attachments/`.
  - Api `DiskFullIntakeTests.A_full_disk_on_the_second_file_is_a_clean_problem_response_with_no_ticket_and_no_file` (`[Collection(ProcessEnvironmentCollection.Name)]`, Production environment with the `TrustedProxy__TrustedNetworks__0` environment variable exactly as `StrictTransportSecurityHostTests` does; if Production start needs more settings, copy what `ProductionBlankTemplateTests` supplies): `FailingStorageProvider.Register(services, failOnStoreCall: 2)`; submit two valid PNGs through `POST /api/public/products/{key}/tickets`; expect `500`, `application/problem+json`, JSON has `status` 500 and `traceId` and no `detail`; the body contains neither `No space left` nor `IOException` nor a stack frame (`" at "`); `tickets`, `messages` and `attachments` tables are empty; the storage root has no files (the first, already stored, file was cleaned up).
  - Api `KbImageDiskFullTests.A_full_disk_on_a_kb_image_upload_is_a_clean_problem_response_and_leaves_no_file`: agent bearer (`TestJwt`), `FailingStorageProvider.Register(services, 1)`, `POST /api/kb/images` with a valid PNG: same ProblemDetails assertions, no file under `kb-images/`.
  RED: the project build fails first (missing helper); after the helper compiles, any theory row whose recorded outcome differs from the real store is a real finding - fix the manifest if the manifest was wrong, or record a finding (Low) and fix the store in this task if the store is wrong. Disk-full and zero-byte tests are expected to pass against existing behaviour (GAP proofs); record that.
- [x] **Step 3: Implement.** Write `FailingStorageProvider`:
  ```csharp
  public sealed class FailingStorageProvider(IStorageProvider inner, int failOnStoreCall) : IStorageProvider
  {
      private int _stores;

      public async Task StoreAsync(StoreObjectRequest request, CancellationToken ct)
      {
          if (Interlocked.Increment(ref _stores) == failOnStoreCall)
          {
              throw new IOException("No space left on device");
          }
          await inner.StoreAsync(request, ct);
      }
      // ReadAsync, DeleteAsync and any other member: delegate to inner unchanged (match the interface's actual members).

      public static void Register(IServiceCollection services, int failOnStoreCall)
      {
          var real = services.Last(d => d.ServiceType == typeof(IStorageProvider));
          services.Remove(real);
          services.AddSingleton<IStorageProvider>(sp => new FailingStorageProvider(
              (IStorageProvider)(real.ImplementationInstance ?? real.ImplementationFactory?.Invoke(sp) ?? ActivatorUtilities.CreateInstance(sp, real.ImplementationType!)),
              failOnStoreCall));
      }
  }
  ```
  Pass it to the factory via `configureServices`. Add the csproj items (`<None Include="../Shared/Fixtures/**" LinkBase="Fixtures" CopyToOutputDirectory="PreserveNewest" />`) to Infra.IntegrationTests and Api.Tests.
- [x] **Step 4: GREEN.** `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter-class "*AttachmentStore*" --filter-class "*KbImageStore*" --filter-class "*HostileUploadCorpus*"`; `--project tests/TechStrap.Api.Tests --filter-class "*PublicIntakeEndpoint*" --filter-class "*DiskFull*"` (Docker); build 0 warnings.
- [x] **Step 5: Mutations.** Stop sanitising names in `AttachmentFileName.Sanitize` (traversal, RTL and NUL rows die); allow `.exe` in `IntakeLimits.AllowedExtensions` (double-extension row dies); skip the signature match in `AttachmentStore` (png-named-pdf and svg-as-png rows die); remove the compensating delete in `AttachmentStore` catch (the disk-full "no file" assertion dies); delete the `content.Length == 0` check (zero-byte test dies); restore each.
- [x] **Step 6: Review document.** Group 3 rows cite the corpus theories and the disk-full tests; SR-11 Status Accepted (evidence: the disk-full tests prove a clean error, no partial ticket and no orphan file; no quota is implemented and none is planned for 0.x; operators monitor the volume - 12b documents it); SR-04 evidence gains `html-as-txt`.
- [x] **Step 7: Commit** `test(uploads): hostile upload corpus fixture across stores and intake; zero-byte and disk-full outcomes (P12-T04)`.

### Task 4: Sanitiser and rendering (P12-T05)

**Files:**
- Create: `tests/Shared/Fixtures/xss-corpus.txt`, `tests/Shared/XssAssertions.cs`, `tests/Shared/XssCorpus.cs`, `tests/TechStrap.Infrastructure.IntegrationTests/XssAssertionsTests.cs`, `tests/TechStrap.Api.Tests/Kb/KbPreviewXssTests.cs`, `tests/TechStrap.Api.Tests/Intake/MessageBodyXssEndpointTests.cs`, `tests/TechStrap.Admin.Tests/Components/MessageBubbleBodyTests.cs`, `tests/TechStrap.Portal.Tests/Components/CustomerMessageBodyTests.cs`
- Modify: csproj files of Infrastructure.IntegrationTests and Api.Tests (link `XssAssertions.cs`, `XssCorpus.cs`, fixture copy; Admin.Tests and Portal.Tests already link `../Shared/*.cs`, and need only the fixture item if they read the corpus), `HtmlSanitizerTests.cs`, `KbHtmlSanitizerTests.cs`, `KbContentRendererTests.cs`, `EmailTemplateRendererTests.cs`, `docs/security/SECURITY-REVIEW.md`

**Interfaces:**
- Produces: `XssCorpus.Vectors` (`IReadOnlyList<string>`, comment and blank lines skipped) and `XssCorpus.Rows()` (`IEnumerable<TheoryDataRow<string>>` with display name `vector-NN`); `XssAssertions.ContainsNoActiveContent(string html) -> bool` and `XssAssertions.ShouldHaveNoActiveContent(string html, string because)` (throws a Shouldly failure listing the matching pattern and the offending fragment).

- [x] **Step 1: Corpus** `xss-corpus.txt`, one vector per line (ASCII; `#` starts a comment line). At least these 50:
  ```
  # script
  <script>alert(1)</script>
  <SCRIPT SRC=//evil.example/x.js></SCRIPT>
  <scr<script>ipt>alert(1)</scr</script>ipt>
  <script>alert&#40;1&#41;</script>
  # event handlers
  <img src=x onerror=alert(1)>
  <img src="x" onerror="alert(1)">
  <IMG SRC=x ONERROR=alert(1)>
  <img/src=x/onerror=alert(1)>
  <body onload=alert(1)>
  <div onmouseover="alert(1)">hover</div>
  <input autofocus onfocus=alert(1)>
  <details open ontoggle=alert(1)>
  <video><source onerror=alert(1)></video>
  <marquee onstart=alert(1)>x</marquee>
  # svg and math
  <svg onload=alert(1)>
  <svg><script>alert(1)</script></svg>
  <svg><animate onbegin=alert(1) attributeName=x dur=1s>
  <math><mi xlink:href="javascript:alert(1)">x</mi></math>
  <math><mglyph><style><img src=x onerror=alert(1)>
  <svg></p><style><a id="</style><img src=1 onerror=alert(1)>">
  # urls
  <a href="javascript:alert(1)">x</a>
  <a href="JaVaScRiPt:alert(1)">x</a>
  <a href="&#106;avascript:alert(1)">x</a>
  <a href="java&#x09;script:alert(1)">x</a>
  <a href="  javascript:alert(1)">x</a>
  <a href="vbscript:msgbox(1)">x</a>
  <a href="data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==">x</a>
  <a href="data:text/html,<script>alert(1)</script>">x</a>
  <img src="javascript:alert(1)">
  # frames and embeds
  <iframe src="javascript:alert(1)"></iframe>
  <iframe srcdoc="<script>alert(1)</script>"></iframe>
  <object data="javascript:alert(1)"></object>
  <embed src="data:text/html,<script>alert(1)</script>">
  <frameset onload=alert(1)>
  # document-level
  <base href="https://evil.example/">
  <meta http-equiv="refresh" content="0;url=javascript:alert(1)">
  <link rel="stylesheet" href="https://evil.example/x.css">
  <template><script>alert(1)</script></template>
  # css and forms
  <div style="width:expression(alert(1))">x</div>
  <div style="background:url(javascript:alert(1))">x</div>
  <style>@import 'https://evil.example/x.css';</style>
  <form action="https://evil.example/steal"><input type=submit></form>
  <button formaction="javascript:alert(1)">x</button>
  <form><button formaction=javascript:alert(1)>x</button></form>
  # markdown flavoured
  [x](javascript:alert(1))
  ![x](javascript:alert(1))
  [x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)
  <https://x.example/"onmouseover="alert(1)>
  # mutation and encoding
  <noscript><p title="</noscript><img src=x onerror=alert(1)>">
  <textarea></textarea><img src=x onerror=alert(1)>
  "><script>alert(1)</script>
  %3Cscript%3Ealert(1)%3C/script%3E
  &lt;script&gt;alert(1)&lt;/script&gt;
  <a href="x" onclick="alert(1)" onclick="alert(2)">x</a>
  ```
  (The two encoded lines are inert-by-construction controls: they must stay inert, and a renderer that decodes them twice would fail.)
- [x] **Step 2: Helper and its tests.** `XssAssertions` holds `[GeneratedRegex]` members (case-insensitive, culture-invariant): `<(script|iframe|object|embed|base|meta|form|math|template)\b`; `<[^>]*[\s/"']on[a-z]+\s*=`; `=\s*["']?\s*(javascript|vbscript|data:text/html)`; `<[^>]*\bsrcdoc\b`; `<[^>]*expression\s*\(`. `XssAssertionsTests` (Infra project; xUnit theory): flags each of `<script>x</script>`, `<img src=x onerror=alert(1)>`, `<a href="javascript:x">`, `<iframe srcdoc="x">`, `<div style="width:expression(x)">`, `<form action=y>`, `<math>`, `<template>`; passes `&lt;script&gt;alert(1)&lt;/script&gt;`, `<p>onerror=alert(1) is text</p>`, `<a href="https://x.example" rel="noopener noreferrer nofollow">x</a>`, empty string. Plus `The_corpus_has_at_least_fifty_vectors_and_no_non_ascii` (`XssCorpus.Vectors.Count >= 50`, all ASCII).
- [x] **Step 3: Failing corpus tests (RED).** Each theory takes `XssCorpus.Rows()`:
  - Infra `HtmlSanitizerTests.Every_corpus_vector_sanitises_to_no_active_content`: `new HtmlSanitizerAdapter().Sanitize(vector)` -> `XssAssertions.ShouldHaveNoActiveContent`.
  - Infra `KbHtmlSanitizerTests.Every_corpus_vector_sanitises_to_no_active_content`: same with `KbHtmlSanitizer`.
  - Infra `KbContentRendererTests.Every_corpus_vector_renders_inert_as_markdown_and_as_html`: the renderer's Markdown entry with the vector as the source and with `"# Title\n\n" + vector`, and its raw-HTML entry (use the members the existing tests of this class use); output has no active content.
  - Infra `EmailTemplateRendererTests.Every_corpus_vector_is_encoded_in_the_subject_name_and_article_title_fields`: render each email kind whose model carries customer text (confirmation: subject and name; agent reply: subject, name, linked article titles; solved notice) with the vector in each field; the HTML part has no active content, and the text part still contains the vector verbatim (plain text is not HTML). `The_sanitised_reply_body_stays_inert_in_the_email`: `HtmlSanitizerAdapter.Sanitize(vector)` passed as `messageHtml` to `RenderAgentReply` -> no active content (this pins that "inserted as given" is safe because of the sanitiser, not the template).
  - Api `KbPreviewXssTests.Every_corpus_vector_renders_to_no_active_content_in_the_preview`: one fact, one `ApiFactory`, agent bearer, `POST /api/kb/preview` for each vector (Markdown body), collects failures with the vector id into one message.
  - Api `MessageBodyXssEndpointTests.A_hostile_body_is_inert_in_the_agent_and_the_customer_views`: for five vectors (`<script>alert(1)</script>`, the `onerror` image, the `svg onload`, the `javascript:` link, the `iframe srcdoc`) submit a ticket with that body through `POST /api/intake/tickets` (Trusted key), read `GET /api/tickets/{id}` as an agent and `GET /api/customer/ticket` with the token; every message `BodyHtml` has no active content.
  - Admin.Tests `MessageBubbleBodyTests.A_sanitised_body_is_rendered_inside_the_message_body_div_with_nothing_added` (bUnit: three sanitiser outputs inline, e.g. `<p>Hello <a href="https://x.example" rel="noopener noreferrer nofollow">link</a></p>`; the rendered `.ts-message-body` inner HTML equals the body and `XssAssertions.ContainsNoActiveContent(markup)` is true for the whole component, including attachment and article labels containing `<script>` text, which Razor encodes).
  - Portal.Tests `CustomerMessageBodyTests.A_sanitised_body_is_rendered_with_headings_demoted_and_nothing_added`: same shape; an `<h1>` becomes `<h2>` (existing `BodyHeadings` behaviour) and no active content appears.
  RED: the compile fails until the helpers exist; after that each vector the sanitisers pass is green at once (the corpus is a regression net); a vector that fails is a finding to fix in `HtmlSanitizerAdapter` / `KbHtmlSanitizer` configuration in this task (record it as SR-17 with the vector, Fixed) - if none fails, record "no vector failed" in the review.
- [x] **Step 4: GREEN.** `dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --filter-class "*Sanitizer*" --filter-class "*KbContentRenderer*" --filter-class "*EmailTemplateRenderer*" --filter-class "*XssAssertions*"`; `--project tests/TechStrap.Api.Tests --filter-class "*Xss*"` (Docker); `--project tests/TechStrap.Admin.Tests --filter-class "*MessageBubbleBody*"`; `--project tests/TechStrap.Portal.Tests --filter-class "*CustomerMessageBody*"`; Architecture tests; build 0 warnings.
- [x] **Step 5: Mutations.** Add `script` and `onerror` to the sanitiser allow-lists (corpus theories die, one row per vector); allow `javascript` in `AllowedSchemes` (the URL vectors die); make `KbContentRenderer` pass Markdown raw HTML through unsanitised (renderer and preview tests die); render `{messageHtml}` unsanitised path with the raw vector in `EmailTemplateRenderer` (the subject/name encode test dies); make the helper's handler regex ignore `onerror` (`XssAssertionsTests` dies); restore each.
- [x] **Step 6: Review document.** Group 4 rows cite the new tests; the MarkupString row records the four sites and the pins; the note that the Admin rule lives in `MarkupStringSiteTests` and the Portal rule in `PortalRules` stays.
- [x] **Step 7: Commit** `test(content): shared XSS corpus run through every sanitiser and renderer (P12-T05)`.

### Task 5: Authorization and headers (P12-T06, P12-T08)

**Files:**
- Create: `tests/TechStrap.Api.Tests/Hosting/CorsAbsenceHostTests.cs`, `tests/TechStrap.Api.Tests/Hosting/ApiUnhandledErrorHostTests.cs`, `tests/TechStrap.Api.Tests/PublicDocumentsSecretsTests.cs`
- Modify: `tests/TechStrap.Api.Tests/Auth/AgentAccessCoverageTests.cs`, `tests/TechStrap.Api.Tests/Hosting/StrictTransportSecurityHostTests.cs`, `docs/security/SECURITY-REVIEW.md`

**Interfaces:**
- Produces: `AgentAccessCoverageTests.AdminOnlyRoutes` (private static `IReadOnlySet<string>` of `"METHOD api/template"` strings built from the live `RoutePattern.RawText`).

- [x] **Step 1: Capture the live list.** Temporarily print (or debug-assert) the live set of `RouteEndpoint`s whose `IAuthorizeData.Policy == AuthorizationPolicies.Admin` as `"METHOD " + RawText` and compare it with the Admin-marked rows of `02-ARCHITECTURE.md` section 7 (17 routes: `POST api/products`, `PUT api/products/{id}`, `GET|POST api/products/{id}/api-keys`, `DELETE api/products/{id}/api-keys/{keyId}`, `GET api/tags/summary`, `POST api/tags`, `PUT|DELETE api/tags/{id}`, `GET api/admin-events`, `PUT api/agents/{id}`, `DELETE api/tickets/{id}`, `POST api/requesters/{id}/erase`, `GET api/dead-letters`, `POST api/dead-letters/{id}/retry`, `DELETE api/dead-letters/{id}`, `DELETE api/kb/categories/{id}`). Route-constraint spelling comes from the code (e.g. `{id:guid}`): copy the live spelling into the constant. Any route that differs from the document (extra Admin route in code, or a documented one missing) is a finding: decide fix-the-code or fix-the-doc and note it in the review (Task 8 corrects the doc).
- [x] **Step 2: Failing tests (RED).**
  - `AgentAccessCoverageTests.The_admin_only_route_set_matches_the_pinned_list` (new `AdminOnlyRoutes` constant): the live Admin-policy set equals `AdminOnlyRoutes`; the failure message lists "unpinned" and "no longer admin" separately. Write it first with an empty constant (RED shows the live set), then fill the constant.
  - `CorsAbsenceHostTests.No_response_carries_an_Access_Control_Allow_Origin_header` (theory over `/health/live`, `/openapi/v1.json`, `/api/public/products`, `/api/agents/me`): send `GET` and a preflight `OPTIONS` (`Origin: https://evil.example`, `Access-Control-Request-Method: GET`); no response has `Access-Control-Allow-Origin`, `Access-Control-Allow-Credentials` or `Vary: Origin`.
  - `ApiUnhandledErrorHostTests.An_unhandled_exception_in_Production_is_a_problem_response_without_detail_or_stack` (`[Collection(ProcessEnvironmentCollection.Name)]`, Production with the `TrustedProxy__TrustedNetworks__0` variable, pattern of `StrictTransportSecurityHostTests`): a `ThrowingStartupFilter` (pattern of the Portal `UnhandledErrorHostTests`: `next(app); app.Map("/__test/throw", b => b.Run(_ => throw new InvalidOperationException("password=hunter2 at SecretClass.Method")))`) registered through `ApiFactory(configureServices: ...)`; assert 500, `application/problem+json`, JSON has `status` 500, a `title` and a `traceId`, no `detail`, no `exception`/`stackTrace` property, and the body contains neither `hunter2` nor `SecretClass` nor `InvalidOperationException`. If the Api's `UseProblemDetailsExceptionHandling` sits before the filter's mapped branch and the branch is never reached, register the filter so its `Configure` calls `next(app)` first (as above) and verify the branch is hit by a Development-environment control run in the same class (`The_throwing_endpoint_is_reached`).
  - `StrictTransportSecurityHostTests.The_Api_sends_Strict_Transport_Security_only_outside_Development` (theory Development false / Production true, `/health/live`, same env-variable pattern).
  - `PublicDocumentsSecretsTests.The_openapi_document_and_readiness_reveal_no_secrets`: Production environment as above, with a database from `ApiTestDatabase`; fetch `/openapi/v1.json` and `/health/ready` anonymously; neither body contains the database connection string, its `Password=` value, the literal `Password=`, `tsk_`, `tsp_` (the OpenAPI text may describe the key format - if `ApiKeyFormat` prefixes appear in an operation description, change the assertion to the key regex `ts[kp]_[A-Za-z0-9_-]{43}` and say so in the review), the `TestJwt` signing key string, the configured `Authority` secret-free values only (issuer URL is public and is not asserted absent), the SMTP password and Sentry DSN setting values if the test sets them (set known canary values `canary-smtp-secret` and `https://canary@sentry.invalid/1` in the factory settings and assert their absence).
- [x] **Step 3: Implement/GREEN.** Fill `AdminOnlyRoutes` from Step 1. Any production fix the tests force (for example an OpenAPI description that leaks a canary) is made in the smallest owning file; record it in the review as Fixed. `dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-class "*AgentAccessCoverage*" --filter-class "*CorsAbsence*" --filter-class "*ApiUnhandledError*" --filter-class "*StrictTransportSecurity*" --filter-class "*PublicDocumentsSecrets*"` (Docker); build 0 warnings.
- [x] **Step 4: Mutations.** Change one Admin controller action to `AuthorizationPolicies.Agent` (the pinned-list test dies); add a throwaway Admin-policy route (the pinned-list test dies); call `services.AddCors()` + `app.UseCors(p => p.AllowAnyOrigin())` (CORS test dies); expose `detail` in the exception handler options (error test dies); remove HSTS for the Api host (HSTS test dies); put the connection string in the readiness response (secrets test dies); restore each.
- [x] **Step 5: Review document.** Group 5 rows cite the new tests; SR-07 evidence names `CorsAbsenceHostTests`; the T08 table (CSP, nosniff, Referrer-Policy, HSTS, no CORS, ProblemDetails) is a row per host with its test names (API, Admin, Portal) so the "results recorded" criterion holds.
- [x] **Step 6: Commit** `test(api): pinned Admin-only route list, no CORS, Production ProblemDetails, HSTS and no-secrets checks (P12-T06, P12-T08)`.

### Task 6: Privacy and the N+1 carry-forwards (P12-T07)

**Files:**
- Modify: `src/TechStrap.Domain/Rules/DomainLimits.cs` (`NotificationPreferencesMaxCount = 2000` ), `src/TechStrap.Application/Persistence/IProductRepository.cs` (`GetExistingIdsAsync`), `src/TechStrap.Application/Persistence/ITicketRepository.cs` (`GetByIdsAsync`), `src/TechStrap.Application/Tags/DeleteTagRequestHandler.cs`, `src/TechStrap.Application/Agents/UpdateNotificationPreferencesRequestHandler.cs` (+ its error in `AgentErrors.cs`), the Infrastructure implementations (`ProductRepository`, `TicketRepository`; `GetByIdsAsync` loads the same graph as `GetByIdAsync`, tracked), `docs/security/SECURITY-REVIEW.md`
- Test: `tests/TechStrap.Api.Tests/Requesters/EraseRequesterEndpointTests.cs` (extend), `tests/TechStrap.Application.Tests/Tags/DeleteTagRequestHandlerTests.cs`, `tests/TechStrap.Application.Tests/Agents/UpdateNotificationPreferencesRequestHandlerTests.cs`, `tests/TechStrap.Infrastructure.IntegrationTests/ProductRepositoryTests.cs`, `DeleteTagIntegrationTests.cs`, `TicketRepository`-level test in `RequesterAndTagRepositoryTests.cs` or a new `TicketBatchLoadTests.cs`

**Interfaces:**
- Produces: `Task<IReadOnlySet<Guid>> IProductRepository.GetExistingIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)`; `Task<IReadOnlyList<Ticket>> ITicketRepository.GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)` (tracked, same includes as `GetByIdAsync`; missing ids are simply absent); `DomainLimits.NotificationPreferencesMaxCount`; error `notification-preferences-too-many`.

- [x] **Step 1: Failing tests.**
  - Api `EraseRequesterEndpointTests.Erasing_a_requester_writes_no_email_name_or_token_to_any_log_event`: submit a ticket through the intake with email `erase.me.7f3a@example.com` and name `Zelda Quillfeather`, keep the plaintext token from the response/link; as Admin erase the requester; then over every event in `factory.LogSink.Events` (all levels; rendered message, every property value rendered, and `Exception?.ToString()`) assert none contains the email, its local part, the name, or the token; also assert the sink saw events at all (`Events.Count > 0` and at least one event from the erase request) so a silent sink cannot pass.
  - App `DeleteTagRequestHandlerTests.A_forced_delete_loads_the_carrying_tickets_in_batches_not_one_by_one`: 450 carriers -> `_tickets.Received(3).GetByIdsAsync(...)` (chunks of 200, 200, 50) and `DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), ...)`; every ticket is updated once; `A_missing_carrying_ticket_fails_loudly` keeps the existing `InvalidOperationException` behaviour when a batch returns fewer tickets than asked.
  - App `UpdateNotificationPreferencesRequestHandlerTests.Products_are_checked_with_one_repository_call`: 30 preferences -> `_products.Received(1).GetExistingIdsAsync(...)` with the 30 ids and `DidNotReceive().GetByIdAsync(...)`; `More_than_the_cap_is_refused_before_any_lookup`: `NotificationPreferencesMaxCount + 1` entries -> failure `notification-preferences-too-many` (400), no repository call; the existing unknown/repeated/null-entry tests keep their codes and `preferences[i]` targets (unknown product: the first index whose id is not in the returned set).
  - Infra `ProductRepositoryTests.GetExistingIdsAsync_returns_only_the_ids_that_exist_and_nothing_for_an_empty_list`; Infra `DeleteTagIntegrationTests.A_forced_delete_across_two_batches_detaches_every_ticket` (205 tickets carrying the tag, forced delete, every ticket has a `TagRemoved` event with reason `tag-deleted`, tag gone; this proves `GetByIdsAsync` returns tracked entities that `Update` can persist); a repository test that `GetByIdsAsync` returns the same messages/tags graph as `GetByIdAsync`.
- [x] **Step 2: RED.** `dotnet test --project tests/TechStrap.Application.Tests -c Release --filter-class "*DeleteTag*" --filter-class "*UpdateNotificationPreferences*"` (compile failure for the new members). The erase-log test is a GAP proof: if it fails, the leak is a real finding - fix the logging call that carried the value (the redaction enricher is fail-closed; check what the erase handler and request log write), record it as Fixed in the review.
- [x] **Step 3: Implement.** `DeleteTagRequestHandler`:
  ```csharp
  private const int TicketBatchSize = 200;

  foreach (var batch in carriers.Chunk(TicketBatchSize))
  {
      var loaded = await tickets.GetByIdsAsync(batch, cancellationToken);
      if (loaded.Count != batch.Length)
      {
          throw new InvalidOperationException($"Tickets carrying tag {tagId} could not be loaded ({loaded.Count} of {batch.Length}).");
      }

      foreach (var ticket in loaded) { /* DetachDeletedTag, Update: unchanged body */ }
  }
  ```
  `UpdateNotificationPreferencesRequestHandler`: after the null-list check, `if (request.Preferences.Count > DomainLimits.NotificationPreferencesMaxCount) return the too-many failure`; validate null entries and repeats in the existing loop without any lookup; then one `var existing = await products.GetExistingIdsAsync(ids, ct)` and a second pass reporting `notification-product-unknown` at the first missing index. Repositories: `GetExistingIdsAsync` is `Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync` into a `HashSet` (no query for an empty list); `GetByIdsAsync` mirrors `GetByIdAsync`'s query with `ids.Contains`.
- [x] **Step 4: GREEN.** `dotnet test --project tests/TechStrap.Application.Tests -c Release`; `--project tests/TechStrap.Infrastructure.IntegrationTests --filter-class "*DeleteTag*" --filter-class "*ProductRepository*" --filter-class "*RequesterAndTagRepository*"`; `--project tests/TechStrap.Api.Tests --filter-class "*EraseRequester*" --filter-class "*Notification*" --filter-class "*Tag*"` (Docker); Architecture tests (`HandlerConstructorDependencyTests` stays green: no new dependency); build 0 warnings.
- [x] **Step 5: Mutations.** Replace the batch with the per-id `GetByIdAsync` loop (batch test dies); set the chunk size to 1 (the `Received(3)` test dies); raise the cap check to `>` the max plus one (cap test dies); look products up per preference again (single-call test dies); log the requester email in the erase handler (`Erasing_a_requester_writes_no_email...` dies); restore each.
- [x] **Step 6: Review document.** SR-15 Status Fixed (evidence: the batch, cap and integration tests); group 6 erase row cites the log test; SR-09 evidence mentions that the erase test covers email, name and token and that names appear nowhere in logs because no handler logs them.
- [x] **Step 7: Commit** `fix(app): batched tag-delete and preference lookups with a cap; erase leaves no PII in logs (P12-T07)`.

### Task 7: CI scans and SBOM (P12-T09)

**Files:**
- Create: `.trivyignore`
- Modify: `.github/workflows/ci.yml`, `.github/workflows/release.yml`, `Directory.Build.props` (only if the local scan is clean), `scripts/tests/PublishWorkflow.Tests.ps1`, `docs/security/SECURITY-REVIEW.md`, `docs/development/RELEASING.md` (one paragraph: the SBOM attachment and the waiver file)

**Interfaces:**
- Produces: CI steps `Vulnerable packages (direct and transitive)` in `build-test` and `Scan the four images (Trivy)` in `docker-build`; the `.trivyignore` waiver format; release job `sbom`.

- [x] **Step 1: Local scan and JSON shape.** Run `dotnet restore TechStrap.slnx`, then `dotnet list TechStrap.slnx package --vulnerable --include-transitive --format json > <scratch>/vulnerable.json`. Confirm the shape (`projects[].frameworks[].topLevelPackages[]` and `transitivePackages[]`, each with `vulnerabilities`; `problems[]` at the top when restore assets are missing) and test the jq filter against a clean result and against a hand-edited copy that adds one package. Record the result as SR-13 (`Info`; Status Accepted if clean, Open if any package is flagged, in which case upgrade it in this task through `Directory.Packages.props`, `Check-PackageVersions.ps1` permitting, or add a documented waiver). If the fallback is needed (the JSON format is unavailable), grep the plain output for `has the following vulnerable packages`.
- [x] **Step 2: Pins first (RED).** In `scripts/tests/PublishWorkflow.Tests.ps1` add:
  ```powershell
  Describe 'CI scans and SBOM (PHASE-12a)' {
      BeforeAll {
          $script:Ci = Get-RepoText '.github/workflows/ci.yml'
          $script:Rel = Get-RepoText '.github/workflows/release.yml'
      }

      It 'ci.yml fails the build on a vulnerable direct or transitive package' {
          $script:Ci | Should -Match ([regex]::Escape('dotnet list TechStrap.slnx package --vulnerable --include-transitive --format json'))
          $script:Ci | Should -Match 'jq -e'
      }

      It 'ci.yml scans the four images with Trivy at High and Critical and fails the job' {
          $script:Ci | Should -Match 'aquasecurity/trivy-action@'
          $script:Ci | Should -Match 'severity: HIGH,CRITICAL'
          $script:Ci | Should -Match "exit-code: '1'"
          $script:Ci | Should -Match 'ignore-unfixed: true'
          $script:Ci | Should -Match 'trivyignores: \.trivyignore'
          foreach ($app in 'api', 'admin', 'portal', 'worker') { $script:Ci | Should -Match ("techstrap-$app") }
      }

      It '.trivyignore exists and documents the waiver format' {
          $text = Get-RepoText '.trivyignore'
          $text | Should -Match 'CVE'
          $text | Should -Match 'reason'
          $text | Should -Match 'review'
      }

      It 'release.yml attaches a CycloneDX SBOM per image to the GitHub Release' {
          $script:Rel | Should -Match 'trivy image --format cyclonedx'
          $script:Rel | Should -Match 'gh release upload'
          $script:Rel | Should -Match 'sbom-'
          $script:Rel | Should -Match 'NuGet packages and the GitHub Release come from publish-nuget\.yml on the same tag'
      }
  }
  ```
  `pwsh -File scripts/Invoke-ScriptTests.ps1`: RED.
- [x] **Step 3: `ci.yml`.** In `build-test`, directly after `Restore tools and packages`:
  ```yaml
      - name: Vulnerable packages (direct and transitive)
        run: |
          dotnet list TechStrap.slnx package --vulnerable --include-transitive --format json > vulnerable.json
          if ! jq -e '(.problems // []) == [] and ([.projects[].frameworks[]? | (.topLevelPackages[]?, .transitivePackages[]?)] | length == 0)' vulnerable.json; then
            jq '[.projects[] | {path, frameworks: [.frameworks[]? | {framework, top: [.topLevelPackages[]?.id], transitive: [.transitivePackages[]?.id]}]}]' vulnerable.json
            exit 1
          fi
  ```
  In `docker-build`, after the build step, one scan step per image (the local image names are what `Build-TechStrapDocker.ps1` tags without a registry; read its `Get-ImageTags` to confirm `techstrap-<app>:0.0.0-ci.<run>`):
  ```yaml
      - name: Scan techstrap-api (Trivy)
        uses: aquasecurity/trivy-action@0.33.1
        with:
          image-ref: techstrap-api:0.0.0-ci.${{ github.run_number }}
          severity: HIGH,CRITICAL
          exit-code: '1'
          ignore-unfixed: true
          trivyignores: .trivyignore
  ```
  repeated for `admin`, `portal` and `worker`. Confirm the action tag resolves when the PR runs; if the single-platform build does not `--load` the image into the runner's daemon, add `--load` through the script's existing parameter (read lines ~101-135) rather than pushing.
- [x] **Step 4: `.trivyignore`.** Header comments only: the waiver format `# CVE-YYYY-NNNN  reason: <why not exploitable here>  review: <YYYY-MM-DD, at most 90 days out>`, one waiver per line, no waivers at the start; add that a waiver must also be recorded as an accepted finding in `docs/security/SECURITY-REVIEW.md`.
- [x] **Step 5: `release.yml`.** Keep the header sentence about `publish-nuget.yml`. Add a job:
  ```yaml
    sbom:
      name: CycloneDX SBOM per image, attached to the GitHub Release
      needs: images
      runs-on: ubuntu-latest
      permissions:
        contents: write
        packages: read
      env:
        GH_TOKEN: ${{ github.token }}
        TAG_NAME: ${{ github.ref_name }}
      steps:
        - name: Log in to GHCR
          uses: docker/login-action@v4
          with: { registry: ghcr.io, username: "${{ github.actor }}", password: "${{ secrets.GITHUB_TOKEN }}" }
        - name: Generate one SBOM per image
          run: |
            version="${TAG_NAME#v}"
            for app in api admin portal worker; do
              docker run --rm -v "$PWD:/out" -v /var/run/docker.sock:/var/run/docker.sock -e TRIVY_USERNAME="${{ github.actor }}" -e TRIVY_PASSWORD="${{ secrets.GITHUB_TOKEN }}" \
                aquasec/trivy:0.65.0 image --format cyclonedx --output "/out/sbom-$app.cdx.json" "ghcr.io/syntax-circus/techstrap-$app:$version"
            done
        - name: Attach the SBOMs to the Release (the Release is created by publish-nuget.yml)
          run: |
            for attempt in $(seq 1 40); do gh release view "$TAG_NAME" >/dev/null 2>&1 && break; sleep 30; done
            gh release view "$TAG_NAME" >/dev/null
            gh release upload "$TAG_NAME" sbom-api.cdx.json sbom-admin.cdx.json sbom-portal.cdx.json sbom-worker.cdx.json --clobber
  ```
  (adjust the Trivy image tag to a released one that resolves; the Pester pin only needs `trivy image --format cyclonedx`, `sbom-` and `gh release upload`). The workflow also uploads the files as a workflow artifact so a failed upload does not lose them.
- [x] **Step 6: NuGetAudit (conditional).** Only if Step 1 was clean: add `<NuGetAudit>true</NuGetAudit><NuGetAuditMode>all</NuGetAuditMode><NuGetAuditLevel>low</NuGetAuditLevel>` to `Directory.Build.props` so the build itself warns (an error, `TreatWarningsAsErrors`) on a vulnerable transitive package; rebuild `dotnet build TechStrap.slnx -c Release` to prove 0 warnings. If any warning appears, do not add the setting; leave the CI step as the gate and say so in SR-13.
- [x] **Step 7: GREEN + mutations.** `pwsh -File scripts/Invoke-ScriptTests.ps1`; `actionlint` if available (otherwise rely on the Pester structure pins and the PR run); `dotnet build TechStrap.slnx -c Release`. Mutations: drop `--include-transitive` (pin dies); set `severity: CRITICAL` only (pin dies); remove `exit-code` (pin dies); remove the `gh release upload` line (pin dies); empty `.trivyignore` of the word `review` (pin dies); restore each.
- [x] **Step 8: Review document and RELEASING.md.** SR-13 as decided in Step 1; group 6 scan/SBOM rows cite the CI steps and the Pester pins; `RELEASING.md` gains a short paragraph "Scans and SBOMs" (what fails the build, where waivers live, where the SBOMs appear) without removing the sections the existing pins require.
- [x] **Step 9: Commit** `ci: fail on vulnerable packages and High/Critical image findings; CycloneDX SBOMs on the release (P12-T09)`.

### Task 8: Conformance gate and close-out (P12-T19, P12-T10)

**Files:**
- Create: `tests/TechStrap.Architecture.Tests/EntryPointCatalogTests.cs`, `tests/TechStrap.Architecture.Tests/EntryPointCatalog.cs` (the parser and the reflection collector), `tests/TechStrap.Architecture.Tests/EntryPointCatalogParserTests.cs`
- Modify: `tests/TechStrap.Architecture.Tests/TechStrap.Architecture.Tests.csproj` (add `ProjectReference` to `src/TechStrap.Worker/TechStrap.Worker.csproj`), `docs/architecture/02-ARCHITECTURE.md` (discrepancy fixes only), `docs/security/SECURITY-REVIEW.md`, `docs/architecture/PHASE-12-release-hardening.md` (ticks and notes), `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`, `docs/architecture/00-DISCOVERY-INDEX.md` (if it carries a phase-12 status cell), `scripts/tests/RepositoryDocs.Tests.ps1`, this plan (ticks and `## As built`)

**Interfaces:**
- Produces: `EntryPointCatalog.FromDocument(string markdown) -> IReadOnlyList<EntryPoint>` and `EntryPointCatalog.FromCode() -> IReadOnlyList<EntryPoint>` where `EntryPoint { string Kind /* http|hub|loop */; string Key; string Handler }` (`http`: `"GET api/tags/{id}"`; `hub`: `"TicketHub.JoinTicket"`; `loop`: `"EmailOutboxWorker"`), `EntryPointCatalog.Diff(doc, code) -> (InDocNotInCode, InCodeNotInDoc, HandlerMismatches)`.

- [x] **Step 1: Read first.** Open `docs/architecture/02-ARCHITECTURE.md` sections 7.1-7.5 (the tables used here; 7.6 is the exemption list and is skipped). The tables have two leading columns: entry point cell (HTTP rows start with a backticked `METHOD /path` that may be followed by a parenthesised policy/limit note; loop rows say "hosted service" and name a backticked `...Worker` class; hub rows start with "SignalR `TicketHub.A` / `B` / `C`") and the handler cell (first backticked token ending in `Handler`). Non-entry rows ("Alerts ... no entry point") are skipped. Controllers: find them with `typeof(TechStrap.Api.Program).Assembly` (`ControllerBase` subclasses; actions have an `HttpGet|HttpPost|HttpPut|HttpDelete|HttpPatch` attribute; template = class `[Route]` with `[controller]` replaced + action template; constraints (`:guid`) stripped; leading `/` stripped; query strings in the doc stripped; the handler = the single `[FromServices]` parameter's type name with the leading `I` removed - `ControllerBoundaryRules` already guarantees exactly one).
- [x] **Step 2: Failing tests (RED).**
  - `EntryPointCatalogParserTests` (fixture markdown snippets inline): an HTTP row with a policy note parses to `GET api/products/{id}` + handler; a row with `?q=&category=` drops the query; the multi-method hub row yields three hub entries with the same handler; a loop row yields `loop` with the worker class; a "no entry point" row yields nothing; a table outside 7.1-7.5 is ignored; a route in the document whose segment names differ from the code is reported as a diff (parity test of `Diff`).
  - `EntryPointCatalogTests.Every_documented_entry_point_exists_in_code_with_the_same_handler`, `Every_coded_entry_point_is_in_the_catalog` and `The_conformance_report_lists_every_entry_point_once` (writes the full table `KIND | KEY | HANDLER | IN DOC | IN CODE` and both diff lists to `ITestOutputHelper`, and fails when either list or the handler-mismatch list is non-empty). Code side details: hub methods = public methods declared on `TicketHub` (handler = its constructor's single `I*Handler`); Api loops = `IHostedService` implementations declared in `TechStrap.Api` (handler = their constructor's single `I*Handler`, the listener row); Worker loops = `BackgroundService` subclasses in the Worker assembly, handler found by reading `src/TechStrap.Worker/**/<Class>.cs` (via `RepositoryRoot` pattern used by the existing source-scanning Architecture tests) for the one `I<Name>Handler` it resolves.
  Run `dotnet test --project tests/TechStrap.Architecture.Tests -c Release --filter-class "*EntryPointCatalog*"`: RED on the first real diff.
- [x] **Step 3: Fix discrepancies.** For each reported difference decide doc or code (the default is the document, because section 7 is declared the source of truth for routes and names only after it is true): rename a doc route parameter to the code's, add a missing doc row, or (rarely) correct a code route. The listener row in 7.5 gains the hosted listener's class name in backticks so the loop key exists on both sides. Record each fix in the review's `## Architecture conformance` section and keep the final report text there (entry-point count, loops, hub methods, "0 discrepancies").
- [x] **Step 4: GREEN.** Architecture tests all pass (`dotnet test --project tests/TechStrap.Architecture.Tests -c Release`), including `ProjectReferenceDirectionTests` (the new test-project reference is not a src reference); build 0 warnings.
- [x] **Step 5: Close-out pins first (RED).** Append to `RepositoryDocs.Tests.ps1`:
  - the review: no finding has `Status: Open`; no row contains `Task \d adds`; every group heading table has no empty evidence cell; `## Architecture conformance` exists and states `0 discrepancies`; SR-01..SR-16 all present;
  - the spec: `P12-T01` .. `P12-T10` and `P12-T19` are `- [x]` and each has an `**As built (12a):**` note; T11-T18, T20, T21 stay unticked;
  - the roadmap row for phase 12 says `12a complete (pending merge)`;
  - the plan `2026-10-08-phase-12a-hardening.md` has no unticked `- [ ]` steps and has `## As built`.
- [x] **Step 6: Docs.** `docs/security/SECURITY-REVIEW.md`: every "Task N adds ..." cell becomes the test name (verify each name exists with `Select-String` across `tests/`), every finding status is Fixed or Accepted with its evidence, a short "Release 0.3.0 sign-off" paragraph lists the open High/Critical count (0) and the date. Spec `PHASE-12-release-hardening.md`: tick T01-T10 and T19 with one-line `**As built (12a):**` notes naming the test classes and the review ids, tick the review deliverable (the filename per D-051), do not touch T11-T18, T20, T21. Roadmap: phase 12 row -> `12a complete (pending merge)`. Plan: tick all steps and write `## As built`.
- [x] **Step 7: GREEN + mutation.** `pwsh -File scripts/Invoke-ScriptTests.ps1`, `Check-PackageVersions.ps1`. Mutations: change a route parameter name in a controller (`Every_coded_entry_point_is_in_the_catalog` dies); delete one doc row (`Every_coded_entry_point_is_in_the_catalog` dies); rename a handler in one doc row (the handler-mismatch assertion dies); set one finding to `Status: Open` (the close-out pin dies); restore each.
- [x] **Step 8: Commit** `docs: PHASE-12a close-out (review statuses, conformance report, spec ticks, roadmap)`.

## Verification (whole PR)
```
dotnet build TechStrap.slnx -c Release                            # 0 warnings
dotnet test --solution TechStrap.CI.slnf -c Release --no-build    # Docker running
pwsh -File scripts/Invoke-ScriptTests.ps1
pwsh -File scripts/Check-PackageVersions.ps1
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api --configuration Release --no-build   # "No changes": 12a adds no migration
dotnet list TechStrap.slnx package --vulnerable --include-transitive   # clean, or every hit waived and recorded (SR-13)
```
Then `superpowers:finishing-a-development-branch`: PR "PHASE-12a: security review and hardening (D-051)" against `main` from `feat/phase-12a-hardening`. First observe the new CI steps pass on the PR (the Trivy scans run only there); a High/Critical finding on a base image is fixed by a base-image bump or documented in `.trivyignore` plus a review finding, never by lowering the severity. No tag is cut in this PR (`v0.3.0` is 12c).

## Risks / open items
- **Corpus vs sanitiser:** a vector the sanitiser lets through is a real finding; the fix may be a sanitiser configuration change that alters rendered output. `MessagePipelineUnchangedTests` and the KB renderer tests are the guard; keep any change to one allow-list entry per commit if it happens.
- **Production-mode API tests** (`DiskFullIntakeTests`, `ApiUnhandledErrorHostTests`, `PublicDocumentsSecretsTests`, HSTS) need the process-environment trusted-proxy variable and may need further Production settings; they run in the non-parallel `ProcessEnvironmentCollection`. If Production startup demands a setting the factory does not supply, copy the exact set from `ProductionBlankTemplateTests`.
- **Concurrent revoke** is accepted (SR-16): a duplicate audit row is possible under a simultaneous double revoke. If the owner prefers strictness, the xmin token is a small follow-up (`ProductApiKey` row version, migration `AddProductApiKeyVersion`, conflict mapped to 409) that this plan deliberately does not take.
- **`GetByIdsAsync` graph parity:** it must load exactly what `GetByIdAsync` loads (messages, tags, events as the aggregate needs for `DetachDeletedTag`); the 205-ticket integration test is the guard.
- **Release SBOM timing:** `release.yml` and `publish-nuget.yml` run concurrently on a tag; the `sbom` job waits up to 20 minutes for the Release. If `publish-nuget.yml` fails before creating the Release, the job fails and the SBOMs remain as workflow artifacts.
- **Trivy availability** (action tag and database download) is an external dependency of CI; a transient database-download failure fails the job. Re-run the job; do not add `skip-db-update`.
- **Image scans are single-platform** (amd64) in CI; the arm64 images share the same packages and base tags, so the amd64 result stands in.
- **Antivirus, storage quota, load/GIN plan** are explicitly not here: SR-08, SR-11, and the load run in 12c.
- **Conformance gate scope:** hub and loop handlers are verified by constructor or source scan, not by executing the loops; a loop that resolves two handlers would be reported once per handler found.

## As built

Deviations from the plan, per task (all verified in the task reports):

- Task 1 (review skeleton, Pester pins): none.
- Task 2 (access tokens): the follow-up ticket token is read from `FollowUpViewUrl` in the reply response, not from a separate field.
- Task 3 (API keys and uploads): the shared corpus has 15 rows, three added at review to isolate the allow-list ("allow .exe" is an equivalent mutant); the 500 body from the disk-full test carries a fixed generic detail and no trace id; `FailingStorageProvider` writes a one-byte partial object before failing, so the cleanup is exercised.
- Task 4 (sanitiser corpus): `ScriptScheme` detection is scoped to attribute values and the in-tag prefix check is quote-aware; the email tests strip the static head of the template; the renderer theory was renamed to say what it covers (Markdig `DisableHtml`); `style|link|frameset` are flagged as active tags.
- Task 5 (authorization and headers): the Api sent HSTS in Development, fixed and recorded as SR-17; the throw test uses an agent bearer token and a private exception type; the 500 body shape is pinned.
- Task 6 (privacy): `MvcArgumentsRedactionEnricher` fixes MVC rendering bound request records at Trace (SR-18); the erase test runs at Verbose; the validation order (list-shape errors before lookups) is pinned.
- Task 7 (scans and SBOM): `aquasecurity/trivy-action@v0.36.0` with `aquasec/trivy:0.75.0`; image references carry the `-amd64` suffix; `NuGetAuditMode=all` and `NuGetAuditLevel=low` were added; the `jq` filter was validated locally with an equivalent Python check because `jq` is not installed on the author machine.
- Task 8 (conformance gate and close-out): the Api-side hosted loop (`TicketChangeListener`) lives in Infrastructure (internal), so the collector reads `IHostedService` implementations from the Api, Infrastructure and Worker assemblies instead of the Api alone; the hub collector skips the `OnConnectedAsync`/`OnDisconnectedAsync` overrides (handshake, exempt in 7.6). The gate's first run found three document differences (two route parameters named `{key}` where the code says `{productKey}`, and the unnamed listener class); all fixed in the document, no production code changed.
