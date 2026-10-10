# Security review

Release 0.3.0 (PHASE-12a). Evidence-based review of the security-relevant paths of TechStrap. Every checklist row cites existing tests by name (grep-able under `tests/` and `scripts/tests/`), says `Task N adds <TestName>` for a gap a later task of PHASE-12a closes, or points at a finding `SR-NN` in the Findings section.

## Purpose and method

The review walks six path groups taken from the PHASE-12 spec (Review scope, with the D-051 corrections): customer access tokens, API keys, uploads, sanitiser and rendering, authorization and headers, privacy and operations.

For each group the method is:

1. List the checks the spec asks for.
2. Find the test that already proves each check, and cite it by name. A check without a test gets a test (a `Task N adds` note) or a finding.
3. Record anything that is a judgement rather than a provable behaviour as a finding with a severity and a status.
4. Keep the evidence honest: a row that cites a test means that test fails if the control is removed (the mutation check done in the task that adds or edits the test).

Scans (dependency and image) run in CI (P12-T09); no SBOM is published (owner decision 2026-10-09) and are recorded here (SR-13); the architecture conformance gate (P12-T19) fills in the last section.

## Severity scale

- **Critical** - exploitable now, data or account takeover; blocks the release.
- **High** - blocks the release.
- **Medium** - may ship with documented acceptance.
- **Low** - tracked, fix when convenient or accept with reasoning.
- **Info** - observation or design note, no action.

Statuses are `Open` (work remains, named in the finding), `Fixed` (closed by a change, test named) and `Accepted` (reasoned, no change).

## Finding template

```
### SR-NN: short title
- Path group: <1-6 and name>
- Severity: Critical | High | Medium | Low | Info
- Status: Open | Fixed | Accepted
- Evidence: <what was observed, the test or file, and the reasoning or the fix>
```

## Release 0.3.0

### 1. Customer access tokens

| Check | Evidence or finding |
| --- | --- |
| CSPRNG, 256 bits, only the hash at rest | `AccessTokenServiceTests.A_token_is_256_bits_of_base64url_and_only_its_hash_is_on_the_entity`, `AccessTokenServiceTests.Ten_thousand_tokens_never_collide`, `AccessTokenServiceTests.The_hash_is_the_documented_sha256` |
| The plaintext link is not stored beyond what sending needs | Api `SensitiveDataLeakTests.A_key_submission_leaves_the_plaintext_key_and_token_out_of_logs_and_every_column_but_the_outbox_payload`; the plaintext link in `email_outbox.payload` is SR-01 |
| Constant-time comparison | Hashed database lookup (`TicketRepository.GetAccessTokenByHashAsync`, `CustomerAccess`), `CustomerAccessTests.An_overlong_token_is_not_found_without_a_lookup`; SR-02 |
| Sliding expiry 90 days, cap 365 days, revocation | Domain `TicketAccessTokenTests`; Infrastructure `CustomerReplyIntegrationTests.A_successful_reply_slides_the_token_expiry`; `EraseRequesterIntegrationTests.Tokens_are_revoked` (erase) |
| Uniform 404 for every failure | `CustomerAccessTests.Every_failure_is_the_same_error`; Api `CustomerUniformNotFoundTests.Every_failure_mode_returns_the_same_404_bytes`; Portal `TicketUniformNotFoundHostTests`; attachments: `GetCustomerAttachmentRequestHandlerTests` and `CustomerAttachmentEndpointTests.Other_ticket_and_internal_note_attachments_look_like_missing_ones` |
| Timing of unknown versus expired token | Same single database lookup path (reasoning in SR-02); no timing test |
| Lost-link flow does not reveal which addresses exist | `LostLinkUniformityTests.Known_and_unknown_addresses_are_indistinguishable`, `LostLinkUniformityTests.The_email_goes_only_to_the_requesters_own_address`, `RequestNewAccessLinkRequestHandlerTests`, Portal `LostLinkHostTests`, `CustomerRateLimitTests.Lost_link_requests_are_limited_per_ip` |
| No token in logs, Referer, caches or search indexes | Portal `TicketTokenLeakTests`, `TicketReplyHostTests.The_token_and_the_reply_text_never_reach_a_log_event_at_any_level`, `TicketHeaderHostTests`; Api `SensitiveQuerySentryProcessorTests`, `LogRedactionTests`, `CustomerErrorPathCacheTests` |
| Per-IP rate limit on token access | `CustomerRateLimitTests.Token_access_is_limited_per_ip` |
| A follow-up ticket gets its own token | `CustomerReplyIntegrationTests.A_follow_up_ticket_token_differs_from_the_parents_and_opens_only_its_own_ticket` |

### 2. API keys

| Check | Evidence or finding |
| --- | --- |
| Hasher: hash at rest, constant-time verify | `ApiKeyHasherTests` including `Verify_compares_in_constant_time` |
| Lookup by full hash; the prefix is not a lookup key | SR-03 (display, audit and rate-limit partition); SR-10 for invented prefixes |
| Shown once, never retrievable again | `ApiKeyEndpointTests.Only_the_prefix_and_hash_are_stored_and_the_plaintext_is_shown_once`, `ApiKeyEndpointTests.The_create_response_is_not_cacheable`, `CreateProductApiKeyRequestHandlerTests.The_audit_event_holds_the_prefix_but_no_secret_or_hash`, Admin `NewApiKeyDialogTests` |
| Revocation takes effect | `ApiKeyAuthTests.A_revoked_key_is_401`, `ApiKeyAuthTests.All_401_responses_are_identical`, `RevokeProductApiKeyRequestHandlerTests`, `ApiKeyEndpointTests.A_key_revoked_through_the_api_is_401_on_the_next_intake_call` and `ApiKeyEndpointTests.Revoking_twice_is_204_both_times_and_audits_once` (SR-16) |
| Trusted versus Public keys | `SubmitTicketRequestHandlerTests.An_untrusted_submission_drops_the_external_ref_with_a_warning_and_marks_metadata_untrusted`, `SubmitTicketRequestHandlerTests.An_untrusted_submitter_never_overwrites_a_known_external_ref`; Api `IntakeEndpointTests.A_public_key_submission_drops_the_external_ref_with_a_warning`; Admin `TicketDetailPageTests` |
| Product isolation and credential separation | `ApiKeyAuthTests.A_key_creates_tickets_only_for_its_own_product`, `ApiKeyAuthTests.An_agent_bearer_token_is_not_accepted_on_intake`, `ApiKeyAuthTests.An_api_key_is_not_accepted_on_agent_routes`, `ApiKeyEndpointTests.Revoking_a_key_of_another_product_is_404` |
| Rate limit per key and IP, correct behind the proxy | `IntakeRateLimitTests`, `PublicApiHardeningTests.Behind_a_trusted_proxy_each_forwarded_visitor_has_their_own_limit`, `PublicApiHardeningTests.A_spoofed_forwarded_for_from_an_untrusted_peer_is_ignored`, `TrustedProxyStartupTests` |
| Never logged or echoed | `SensitiveDataLeakTests.Error_responses_never_echo_the_api_key`, `AdminLeakTests`, `LogRedactionTests.Api_keys_encoded_tokens_and_uppercase_hashes_are_redacted` |
| SDK and MAUI public key can be extracted from a shipped app | SR-12 |

### 3. Uploads

| Check | Evidence or finding |
| --- | --- |
| Size and count limits (per file, six files, 25 MiB per submission) | `AttachmentStoreTests.An_oversize_file_is_rejected_even_if_its_declared_length_lies`; `SubmitTicketRequestHandlerTests` (six files, 25 MiB); `PublicIntakeEndpointTests.Six_files_is_400_attachments_too_many`, `PublicIntakeEndpointTests.A_form_body_over_the_limit_is_413`; `RequestTooLargeMiddlewareTests`; Portal `AttachmentRulesTests` |
| Extension allow-list and magic-byte check | `AttachmentStoreTests.A_renamed_executable_is_rejected_by_its_leading_bytes`, `AttachmentStoreTests.A_declared_type_that_does_not_match_the_content_is_rejected`, `AttachmentStoreTests.Text_with_binary_content_is_rejected`, `PublicIntakeEndpointTests.An_executable_renamed_to_pdf_is_400_attachment_type_not_allowed_and_nothing_is_stored`; the corpus rows `double-extension`, `png-named-pdf`, `svg-as-png`, `oversize` and the extension-layer rows `exe-with-png-bytes`, `bat-with-text`, `pdf-bytes-named-exe` in the three theories above; text that contains HTML is SR-04 (corpus row `html-as-txt`) |
| Zero-byte files | Portal `AttachmentRulesTests.An_empty_file_is_named`; server-side `AttachmentStoreTests.A_zero_byte_file_is_refused_as_attachment_empty` and the corpus `zero-byte` row (`attachment-empty`; the KB image store answers `kb-image-type-not-allowed`, it has no empty code) |
| Hostile file names | Domain `AttachmentFileNameTests`, `AttachmentStoreTests.A_path_traversal_file_name_never_escapes_the_root`, Portal `TicketAttachmentHostTests.A_right_to_left_override_and_quotes_are_removed_from_the_name`, `TicketAttachmentHostTests.A_name_that_could_split_a_header_cannot`; the hostile-upload corpus (`tests/Shared/Fixtures/hostile-uploads`: traversal, backslash, RTL override, NUL, 300-character and double-extension names) (stored names and content types are also read back from the attachments table through the endpoint) through `AttachmentStoreTests.Every_hostile_upload_gets_its_recorded_outcome`, `KbImageStoreTests.Every_hostile_upload_gets_its_recorded_kb_outcome` and `PublicIntakeEndpointTests.Every_hostile_upload_through_the_multipart_form_gets_its_recorded_outcome` |
| Random storage keys | `AttachmentStoreTests.A_png_is_stored_under_a_random_key_with_its_canonical_type_and_reads_back`, `KbImageStoreTests.Two_uploads_of_the_same_file_get_different_keys`; time-ordered v7 ids are SR-05 |
| Served as attachment with nosniff and a sandbox header | Api `AttachmentDownloadEndpointTests`, `CustomerAttachmentEndpointTests`; Portal `TicketAttachmentHostTests`; Admin `AttachmentPassThroughTests` |
| SVG and HTML images refused | `KbImageStoreTests.Anything_that_is_not_a_plain_png_jpeg_gif_or_webp_is_refused_and_nothing_is_stored` |
| Knowledge-base prefix isolation from attachments | `KbImageStoreTests.An_attachment_cannot_be_reached_through_the_image_reader`, `KbImageStoreTests.A_name_the_store_could_not_have_written_is_never_opened`; Api `KbImageServingTests` |
| Uploaded product logos (D-052): type, size and name rules | `ProductLogoStoreTests` (a GIF, an SVG and a zero-byte file are refused as `product-logo-type-not-allowed`, an oversize file as `product-logo-too-large`, nothing is stored), `ProductLogoEndpointTests` (admin-only routes, 400, 413) and the `productLogo` column of the hostile-upload corpus |
| Product logo serving and prefix isolation | `ProductLogoServingTests` (nosniff, sandbox CSP, immutable cache, 404 with `no-store` for any name the store could not have written) |
| Product logo disk full | `ProductLogoDiskFullTests` (a full disk leaves no partial file and answers the generic 500) |
| Download authorization | `GetAttachmentRequestHandlerTests.An_anonymous_caller_is_refused_without_a_lookup`, `AttachmentDownloadEndpointTests.An_anonymous_caller_is_401` |
| Disk full or storage failure | `SubmitTicketRequestHandlerTests.An_exception_removes_the_files_the_attempt_stored_and_propagates`, `SubmitTicketIntegrationTests.A_failure_after_creation_leaves_no_rows_and_no_orphan_file`; HTTP level: `DiskFullIntakeTests.A_full_disk_on_the_second_file_is_a_clean_problem_response_with_no_ticket_and_no_file` and `KbImageDiskFullTests.A_full_disk_on_a_kb_image_upload_is_a_clean_problem_response_and_leaves_no_file`; no quota is SR-11 |
| Antivirus | SR-08 |

### 4. Sanitiser and rendering

| Check | Evidence or finding |
| --- | --- |
| Shared XSS corpus (P12-T05) | `tests/Shared/Fixtures/xss-corpus.txt` (54 ASCII vectors incl. two inert-by-construction encoded controls) read by `XssCorpus`; `XssAssertions` detects active tags, in-tag event handlers, script schemes in attribute values, `srcdoc` and CSS `expression(` (its own `XssAssertionsTests`: eight positives, five negatives, corpus size and ASCII check). Every vector was run through every sanitiser and renderer below: no vector got through, no production change (no SR entry needed) |
| Message sanitiser | `HtmlSanitizerTests` (inline vectors); `HtmlSanitizerTests.Every_corpus_vector_sanitises_to_no_active_content` |
| Knowledge-base content (Markdown and HTML) | `KbHtmlSanitizerTests`, `KbContentRendererTests`, `MarkdownRendererTests`, `MessagePipelineUnchangedTests`; `KbHtmlSanitizerTests.Every_corpus_vector_sanitises_to_no_active_content` and `KbContentRendererTests.Every_corpus_vector_renders_inert_from_markdown_source_alone_and_under_a_heading` |
| Admin knowledge-base preview | `RenderKbPreviewRequestHandlerTests`; Api `KbPreviewXssTests.Every_corpus_vector_renders_to_no_active_content_in_the_preview` |
| Email templates | `EmailTemplateRendererTests.Customer_content_is_escaped_in_html_and_plain_in_text`, `EmailTemplateRendererTests.The_reply_body_is_inserted_as_given_but_model_fields_are_encoded` (the reply body is raw because it is already sanitised); `EmailTemplateRendererTests.Every_corpus_vector_is_encoded_in_the_subject_name_and_article_title_fields` and `EmailTemplateRendererTests.The_sanitised_reply_body_stays_inert_in_the_email` |
| MarkupString sites (Admin `Features/Tickets/MessageBubble.razor` and `Features/Kb/KbPreviewPane.razor`; Portal `Components/Kb/KbArticleBody.razor` and `Components/Tickets/CustomerMessageBody.razor`) | Admin.Tests `MarkupStringSiteTests.MarkupString_is_used_in_exactly_two_files_the_message_bubble_and_the_kb_preview_pane`; Architecture `PortalRuleTests.In_09c_exactly_KbArticleBody_and_CustomerMessageBody_turn_text_into_markup`; the bUnit pins `MessageBubbleBodyTests.A_sanitised_body_is_rendered_inside_the_message_body_div_with_nothing_added` (Admin) and `CustomerMessageBodyTests.A_sanitised_body_is_rendered_with_headings_demoted_and_nothing_added` (Portal), and the end-to-end Api `MessageBodyXssEndpointTests.A_hostile_body_is_inert_in_the_agent_and_the_customer_views`. The rule that only two Admin files and two Portal files use `MarkupString` stays in `MarkupStringSiteTests` (Admin) and `PortalRules`/`PortalRuleTests` (Portal) |
| No inline script or event-handler attributes | `AdminRuleTests`, `PortalRuleTests`; Api `ContentSecurityPolicyHostTests.No_Admin_page_renders_an_inline_script_a_style_element_or_an_event_handler_attribute` |

### 5. Authorization and headers

| Check | Evidence or finding |
| --- | --- |
| Every route names a policy | `RoutePolicyCoverageTests.Every_api_route_declares_exactly_one_known_policy`, `RoutePolicyCoverageTests.Every_public_and_api_key_route_is_rate_limited`, `PublicApiHardeningTests.The_fallback_authorization_policy_denies_anonymous_callers` |
| 401 and 403 matrix | `AgentAccessCoverageTests.Anonymous_callers_get_401_and_callers_outside_the_groups_get_403_everywhere`, `AgentAccessCoverageTests.An_agent_group_member_is_refused_on_every_admin_only_route`, `AgentAccessCoverageTests.A_deactivated_agent_is_refused_on_every_agent_endpoint`; `AgentAccessCoverageTests.The_admin_only_route_set_matches_the_pinned_list` pins the 17 D-022 Admin-only routes (the live Admin-policy set equals a constant, so a demoted or newly added Admin route fails the build; it matches the 17 `(Admin)` rows of `02-ARCHITECTURE.md` section 7, no discrepancy) |
| Group roles (D-029) | `ClaimsCurrentAgentClaimsTests`, `AgentAuthTests`, `AgentAccessOptionsTests`, `AgentProvisioningTests` |
| Deactivation | `AgentManagementEndpointTests`; Admin `AgentAccessHostTests` |
| Last-admin guard | SR-14 (fixed); `UpdateAgentRequestHandlerTests.The_admin_lock_is_taken_before_the_actor_is_read_untracked`, `UpdateAgentRequestHandlerTests.An_actor_deactivated_while_waiting_for_the_lock_is_refused`, `AgentAdminLockTests.A_second_counter_waits_for_the_first_unit_of_work_and_sees_its_commit` |
| Hub authorization | `HubPolicyCoverageTests`, `TicketHubTests`, `HubWebSocketTests` |
| IDOR | Customer side: rows 1 and 3; keys: `ApiKeyEndpointTests.Revoking_a_key_of_another_product_is_404`; agents are single-tenant (SR-06) |
| OIDC, PKCE and forwarding | Admin `AdminSignInTests` |
| Security headers and CSP | `SecurityHeadersHostTests`, `PathHeaderRuleHostTests`, `HealthEndpointTests.Responses_carry_security_headers`, `ContentSecurityPolicyHostTests`, `CspBuilderTests`, `CspStyleTests`, `KbImageCspTests` |
| HSTS | `StrictTransportSecurityHostTests` (`The_Api_sends_Strict_Transport_Security_only_outside_Development`, Admin, Portal); the API case found HSTS sent in Development, SR-17 (fixed) |
| CORS | SR-07; `CorsAbsenceHostTests.No_response_carries_an_Access_Control_Allow_Origin_header` (GET and a preflight OPTIONS with a foreign Origin on `/health/live`, `/openapi/v1.json`, `/api/public/products`, `/api/agents/me`: no Allow-Origin, Allow-Credentials, Allow-Methods, Allow-Headers or `Vary: Origin`) |
| Error responses leak nothing | `ResultMappingTests`; Admin and Portal `UnhandledErrorHostTests`; `ApiUnhandledErrorHostTests.An_unhandled_exception_in_Production_is_a_problem_response_without_detail_or_stack` (Production: 500 `application/problem+json`, type `internal-error`, the fixed text "An unexpected error occurred.", no exception message, no stack frame, and the thrown exception type name `SecretFailureException` does not appear), with `The_throwing_endpoint_is_reached` proving the throwing branch runs |
| OpenAPI and health documents | `OpenApiSecurityTests`, `OpenApiSurfaceTests`, `HealthEndpointTests.The_OpenAPI_document_is_served_anonymously`; `PublicDocumentsSecretsTests.The_openapi_document_and_readiness_reveal_no_secrets` (Production, owner decision D-051: OpenAPI stays served anonymously; neither it nor `/health/ready` contains the connection string, its password, `Password=`, the JWT signing key, an SMTP password canary and a Sentry DSN canary (the Api binds no SMTP options, so the SMTP value is a placeholder that is never bound there; it guards against a future binding), or anything shaped like an API key `ts[kp]_[A-Za-z0-9_-]{43}`; the literal prefixes `tsk_` and `tsp_` were not asserted absent because the API-key regex is the precise check) |

#### Browser-facing headers per host (P12-T08)

| Header or behaviour | API | Admin | Portal |
| --- | --- | --- | --- |
| Content-Security-Policy | `ContentSecurityPolicyHostTests`, `CspBuilderTests`, `HealthEndpointTests.Responses_carry_security_headers` (the API policy allows nothing) | `ContentSecurityPolicyHostTests`, `CspStyleTests`, `AdminRuleTests` | `ContentSecurityPolicyHostTests`, `KbImageCspTests`, `PortalRuleTests` |
| X-Content-Type-Options nosniff | `SecurityHeadersHostTests`, `HealthEndpointTests.Responses_carry_security_headers` | `SecurityHeadersHostTests`, `PathHeaderRuleHostTests` | `SecurityHeadersHostTests`, `PathHeaderRuleHostTests` |
| Referrer-Policy | `SecurityHeadersHostTests`, `HealthEndpointTests.Responses_carry_security_headers` | `SecurityHeadersHostTests` | `SecurityHeadersHostTests` |
| Strict-Transport-Security (not in Development) | `StrictTransportSecurityHostTests.The_Api_sends_Strict_Transport_Security_only_outside_Development` (SR-17, fixed) | `StrictTransportSecurityHostTests.The_Admin_sends_Strict_Transport_Security_only_outside_Development` | `StrictTransportSecurityHostTests.The_Portal_sends_Strict_Transport_Security_only_outside_Development` |
| No CORS | `CorsAbsenceHostTests.No_response_carries_an_Access_Control_Allow_Origin_header` (SR-07) | none configured (SR-07); not exercised separately, the Admin calls the API server side | none configured (SR-07); not exercised separately, the Portal calls the API server side |
| Unhandled error is a plain ProblemDetails or error page | `ApiUnhandledErrorHostTests.An_unhandled_exception_in_Production_is_a_problem_response_without_detail_or_stack` | Admin `UnhandledErrorHostTests` | Portal `UnhandledErrorHostTests.An_unhandled_exception_returns_500_with_the_plain_error_page` |
| No secrets in anonymous documents | `PublicDocumentsSecretsTests.The_openapi_document_and_readiness_reveal_no_secrets` (D-051) | n/a (no OpenAPI) | n/a (no OpenAPI) |

### 6. Privacy and operations

| Check | Evidence or finding |
| --- | --- |
| Log redaction | `LogRedactionTests`, `PiiRedactionQueryValueTests`, `AdminHostRedactionTests`; Architecture `LoggingSafetyTests`; Portal `RequestLogRedactionHostTests`; residual gaps are SR-09 |
| Erase a requester | `EraseRequesterIntegrationTests.Nothing_personal_remains_after_erase`, `EraseRequesterIntegrationTests.Tokens_are_revoked`, `EraseRequesterIntegrationTests.Outbox_rows_by_address_and_by_ticket_are_gone_and_others_stay`, `RequesterErasureTests`; Api `EraseRequesterEndpointTests`; `EraseRequesterEndpointTests.Erasing_a_requester_writes_no_email_name_or_token_to_any_log_event` (submits through the intake, erases as Admin, scans every captured event's message, properties and exception for the email, its local part, the name and the token; asserts the sink saw events including the erase request) |
| Hard delete of a ticket | `DeleteTicketIntegrationTests`, `DeleteTicketEndpointTests` |
| Spam handling | `MarkTicketSpamRequestHandlerTests`, `TicketTagAndSpamEndpointTests` |
| Containers run as non-root | Pester `Dockerfiles.Tests.ps1` (`USER 10001:10001`) |
| Secrets only through the environment | `EnvExampleCompletenessTests`, `ProductionBlankTemplateTests`; Pester `ConfigContract`, `TrackedFiles`; image secret scan: the CI step `Scan techstrap-<app> image (Trivy)` (the Trivy secret scanner is on by default for `trivy image`; there is no separate history scan, see SR-13) |
| Forced tag delete and notification preferences | SR-15; `DeleteTagRequestHandlerTests.A_forced_delete_loads_the_carrying_tickets_in_batches_not_one_by_one`, `DeleteTagIntegrationTests.A_forced_delete_across_two_batches_detaches_every_ticket`, `UpdateNotificationPreferencesRequestHandlerTests.Products_are_checked_with_one_repository_call`, `UpdateNotificationPreferencesRequestHandlerTests.More_than_the_cap_is_refused_before_any_lookup` |
| Dependency scan, image scan | CI step `Vulnerable packages (direct and transitive)` in `build-test` (`dotnet list TechStrap.slnx package --vulnerable --include-transitive --format json`, `jq -e` fails on any flagged package or restore problem); four CI steps `Scan techstrap-<app> image (Trivy)` in `docker-build` (High and Critical, `ignore-unfixed`, waivers only in `.trivyignore`); `NuGetAudit` (mode all, level low) in `Directory.Build.props` makes the build itself fail on a vulnerable transitive package; Pester `PublishWorkflow.Tests.ps1` `CI scans (PHASE-12a)`; SR-13 |

## Findings

### SR-01: Plaintext access link in the email outbox
- Path group: 1. Customer access tokens
- Severity: Low
- Status: Accepted
- Evidence: the plaintext access link sits in `email_outbox.payload` until the 90-day purge (D-039). The payload is needed to send the mail, erase removes the rows, and backups inherit the retention.

### SR-02: Token comparison is a hashed lookup
- Path group: 1. Customer access tokens
- Severity: Info
- Status: Accepted
- Evidence: the token is compared by a hashed database lookup (SHA-256 of a 256-bit random value, indexed), so there is no secret-dependent comparison to time; a `FixedTimeEquals` would add nothing. Unknown and expired tokens take the same path.

### SR-03: API keys are looked up by full hash
- Path group: 2. API keys
- Severity: Info
- Status: Accepted
- Evidence: keys are looked up by the full SHA-256 hash (`ProductApiKeyValidator`). The stored prefix is for display, audit and the rate-limit partition; the "prefix lookup" wording in the spec is corrected by D-051.

### SR-04: Text attachments that contain HTML
- Path group: 3. Uploads
- Severity: Low
- Status: Accepted
- Evidence: a `.txt` or `.log` file whose text is HTML passes the signature check as `text/plain`. It is neutralised by `Content-Disposition: attachment`, `nosniff` and the download sandbox header, and is never rendered inline. The corpus row `html-as-txt` pins it: `page.txt` holding `<html><script>` is stored as `text/plain` by the attachment store and refused by the KB image store.

### SR-05: Storage keys are time-ordered GUIDs
- Path group: 3. Uploads
- Severity: Info
- Status: Accepted
- Evidence: storage keys use time-ordered v7 GUIDs. Guessing one grants nothing because every read is authorized by ticket and role (agent) or by token (customer). Uploaded product logos (D-052) are public by design, like KB images; their names are version 7 GUIDs and nothing else is in the path.

### SR-06: Agents are single-tenant by design
- Path group: 5. Authorization and headers
- Severity: Info
- Status: Accepted
- Evidence: agents are single-tenant by design (D-004); any active agent can read any ticket, so there is no agent-side IDOR boundary to test.

### SR-07: No CORS configured
- Path group: 5. Authorization and headers
- Severity: Info
- Status: Accepted
- Evidence: no CORS is configured anywhere, so browsers refuse cross-origin reads by default. `CorsAbsenceHostTests.No_response_carries_an_Access_Control_Allow_Origin_header` pins it: GET and preflight OPTIONS with a foreign Origin get no CORS header on four representative paths (health, OpenAPI, a public route, an agent route). Mutation check: adding `AddCors` and `UseCors(AllowAnyOrigin)` fails the test on all four paths.

### SR-08: No antivirus scanning
- Path group: 3. Uploads
- Severity: Low
- Status: Accepted
- Evidence: antivirus scanning is out of scope for 0.x. The controls are the allow-list, the signature check, the attachment disposition and the sandbox header.

### SR-09: Redaction residuals
- Path group: 6. Privacy and operations
- Severity: Low
- Status: Accepted
- Evidence: the redaction enricher matches emails, tokens, keys and sensitive query values. Display names are not pattern-redacted and `Exception` objects are not rewritten (documented in the enricher remarks).

### SR-10: Invented API-key prefixes get their own rate-limit partition
- Path group: 2. API keys
- Severity: Low
- Status: Accepted
- Evidence: an invented API-key prefix gets its own rate-limit partition per IP. The per-IP floor still applies, and a flood of invented prefixes cannot lock out a real key.

### SR-11: No attachment storage quota
- Path group: 3. Uploads
- Severity: Low
- Status: Accepted
- Evidence: there is no storage quota and none is planned for 0.x; operators monitor the volume (release 12b documents it). A full disk is handled by cleanup and a clean error: `DiskFullIntakeTests` (second file of a submission fails after the first was stored and the failed copy left a partial object) and `KbImageDiskFullTests` prove a 500 ProblemDetails with only the generic detail (no exception text, no stack frame), no ticket, message or attachment row, and no file left under the storage root.

### SR-12: Public key extractable from a shipped app
- Path group: 2. API keys
- Severity: Info
- Status: Accepted
- Evidence: the SDK and MAUI public key can be extracted from a shipped app. A Public key can only create tickets for its own product, cannot set an external user ref or trusted metadata, and is rate limited per key and IP; revoke and rotate if abused.

### SR-13: Dependency and image scan results
- Path group: 6. Privacy and operations
- Severity: Info
- Status: Accepted
- Evidence: the local `dotnet list TechStrap.slnx package --vulnerable --include-transitive` (P12-T09, 2026-10-08) reported no vulnerable package, direct or transitive, and no restore problems. CI now fails on any finding (step `Vulnerable packages (direct and transitive)`), `NuGetAudit` (all, low) is on in `Directory.Build.props` and the Release build stayed at 0 warnings, and the four images are scanned by Trivy at High and Critical (fix available) with waivers only in `.trivyignore`. The image scan result itself is first produced by the CI run of this branch; it has not been run locally. No SBOM is published (owner decision 2026-10-09, D-051 amendment); an on-demand command is in `docs/development/RELEASING.md`. Pins: Pester `CI scans (PHASE-12a)`.

### SR-14: Last-admin guard read the actor before taking the lock
- Path group: 5. Authorization and headers
- Severity: Low
- Status: Fixed
- Evidence: `UpdateAgentRequestHandler` now validates `isActive`, takes the admin row lock (`CountActiveAdminsLockedAsync`), and only then reads the actor with `IAgentRepository.GetBySubjectFreshAsync` (an untracked query), so an actor deactivated by the transaction it queued behind is refused with `agent-inactive`. The fresh read matters over HTTP: the authorization handler has already tracked the actor row in the request scope, and a tracked read would return that stale record by identity resolution. Proved by `AgentAdminLockTests.An_actor_deactivated_by_the_transaction_they_queued_behind_is_refused_even_when_the_row_is_already_tracked` (real Postgres, actor tracked first, second scope deactivates the actor and commits while the handler waits), `UpdateAgentRequestHandlerTests.The_admin_lock_is_taken_before_the_actor_is_read_untracked`, `UpdateAgentRequestHandlerTests.An_actor_deactivated_while_waiting_for_the_lock_is_refused` and the real-Postgres `AgentAdminLockTests.A_second_counter_waits_for_the_first_unit_of_work_and_sees_its_commit` (a second counter blocks on the row lock and sees the first unit of work's commit). Fixed in P12-T03.

### SR-15: Per-row lookups and uncapped notification preferences
- Path group: 6. Privacy and operations
- Severity: Low
- Status: Fixed
- Evidence: forced tag delete now loads the carrying tickets with `ITicketRepository.GetByIdsAsync` in chunks of 200 (`DeleteTagRequestHandlerTests.A_forced_delete_loads_the_carrying_tickets_in_batches_not_one_by_one`, `A_missing_carrying_ticket_fails_loudly`; real Postgres `DeleteTagIntegrationTests.A_forced_delete_across_two_batches_detaches_every_ticket` with 205 tickets; `TicketBatchLoadTests` for graph parity). Notification preferences are capped at `DomainLimits.NotificationPreferencesMaxCount` (2000) with `notification-preferences-too-many`, refused before any lookup (the cap is only a pre-lookup bound: duplicates and unknown ids are already refused, so a valid list cannot exceed the catalogue; the Admin page saves the full set, one entry per active product, on every toggle, which is why the bound is generous), and products are checked with one `IProductRepository.GetExistingIdsAsync` call (`UpdateNotificationPreferencesRequestHandlerTests.More_than_the_cap_is_refused_before_any_lookup`, `Products_are_checked_with_one_repository_call`, `ProductRepositoryTests.GetExistingIdsAsync_returns_only_the_ids_that_exist_and_nothing_for_an_empty_list`). Fixed in P12-T07.

### SR-16: Concurrent double revoke can write and audit twice
- Path group: 2. API keys
- Severity: Low
- Status: Accepted
- Evidence: revoke is idempotent for sequential calls (`ApiKeyEndpointTests.Revoking_twice_is_204_both_times_and_audits_once`: both 204, one `ApiKeyRevoked` audit row). Two truly concurrent revokes can both pass the `IsRevoked` check and each write and audit; both converge on the same end state (revoked, `revoked_at` set), so there is no security impact, only a possible duplicate audit row. Accepted: no concurrency token and no migration for an admin-only, idempotent, audit-only duplicate.

### SR-17: The API sent Strict-Transport-Security in Development
- Path group: 5. Authorization and headers
- Severity: Low
- Status: Fixed
- Evidence: `StrictTransportSecurityHostTests.The_Api_sends_Strict_Transport_Security_only_outside_Development` failed for Development: the API host sent the header on every response while the Admin and Portal hosts remove it in Development (a browser given HSTS for localhost refuses plain http on that host, whatever the port, for the length of the policy). `src/TechStrap.Api/Program.cs` now registers the same start callback before `UseSecurityHeaders` in Development. Production still sends `max-age=31536000; includeSubDomains`. Fixed in P12-T08.

### SR-18: MVC rendered bound request records in Trace logs
- Path group: 6. Privacy and operations
- Severity: Low
- Status: Fixed
- Evidence: at Trace (Serilog Verbose), ASP.NET Core MVC rendered the bound request record (name, subject, body) in `Executing action method ... with arguments`. `EraseRequesterEndpointTests.Erasing_a_requester_writes_no_email_name_or_token_to_any_log_event` (Verbose, intake then erase, scanning message, properties and exception for the email, name, subject, body and token) failed on that event. `MvcArgumentsRedactionEnricher` (Hosting; Api, Admin and Portal) now replaces the `Arguments` property of MVC events with `[arguments]` at any level; proved by `MvcArgumentsRedactionEnricherTests` and the Verbose erase test. Fixed in P12-T07.

## Architecture conformance

The conformance gate (P12-T19) is the Architecture test class `EntryPointCatalogTests` (parser and collector: `EntryPointCatalog`; parser fixtures: `EntryPointCatalogParserTests`). It reads the entry-point tables of `docs/architecture/02-ARCHITECTURE.md` sections 7.1 to 7.5 (7.6 is the exempt list and is not read) and compares them, in both directions and by handler name, with reflection over the Api controllers (route templates with constraints stripped), the `TicketHub` methods and the hosted loops of the Api, Infrastructure and Worker assemblies (a loop's handler is found by reading its source for the one `I...Handler` it resolves).

Result: 69 entry points (62 HTTP routes, 3 hub methods, 4 loops: `EmailOutboxWorker`, `OutboxRetentionWorker`, `AutoCloseWorker`, `TicketChangeListener`); each maps to exactly one handler; **0 discrepancies** after the fixes below, and the test prints the full `KIND | KEY | HANDLER | IN DOC | IN CODE` table when it runs.

The first run reported three differences, all fixed in the document (the code was right):

- `POST /api/public/products/{key}/tickets` and `GET /api/public/products/{key}`: the code names the route parameter `productKey`; section 7 (and the rate-limit table and the intake flow text that quote the first route) now say `{productKey}`.
- Section 7.5 named the listener row only as "API hosted listener"; it now names the hosted class `TicketChangeListener` (Infrastructure, registered by the Api), so the loop exists on both sides.

No production code changed for the gate. The live Admin route set (17 routes) was already equal to the documented set (Task 5); the gate now keeps the whole table honest.

## Release 0.3.0 sign-off

PHASE-12a closes with 18 findings (SR-01 to SR-18): 4 Fixed (SR-14, SR-15, SR-17, SR-18) and 14 Accepted, none Open. 0 open High or Critical findings (the highest severity recorded is Low). Reviewed 2026-10-08. CI fails on vulnerable packages and on High or Critical image findings, and no SBOM is published, by owner decision 2026-10-09 (SR-13). Not covered here, by design: antivirus (SR-08), storage quota (SR-11), and the load, backup and UAT work of 12b and 12c. No tag is cut by this phase; `v0.3.0` is tagged after the soak (D-051).
