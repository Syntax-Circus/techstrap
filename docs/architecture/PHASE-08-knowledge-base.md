# PHASE-08: Knowledge Base

## Objective

Deliver the knowledge base end to end except the public pages: the KB API
(categories, articles, publish/archive, image upload, public search and
sitemap data), Markdown rendering with sanitization, and the admin Markdown
editor with live preview plus the "link articles from a reply" picker.
Portal KB pages, the contact-form deflection UI and SEO tags are
[Phase 09](PHASE-09-public-portal.md).

## Dependencies

- **Depends on:** [PHASE-06](PHASE-06-ticket-operations.md) for the API part (agent policy, `AddAgentReplyRequestHandler` accepting linked article ids, `TicketArticle`). The admin-editor part also needs [PHASE-07](PHASE-07-admin-app.md) (shell, typed-client pattern, `ReplyComposer`).
- **Unblocks:** [PHASE-09](PHASE-09-public-portal.md), [PHASE-12](PHASE-12-release-hardening.md).
- **External prerequisites:** KB tables and FTS vectors/indexes from [PHASE-03](PHASE-03-domain-and-persistence.md) (**Assumption**: P03 delivered `KbCategory`, `KbArticle`, `TicketArticle` and the weighted `search_vector`; P08-T01 verifies and adds a migration only if something is missing). `Storage` provider configured for the API.

## Architecture Decisions

- **Split of work:** API tasks (T01–T12) need only P06 and may run in parallel with P07; admin-editor tasks (T13–T20) need P07.
- **Scope model:** `KbArticle.product_id` null = shared across all products; public queries for product `{key}` return that product's published articles plus shared ones. Slug unique within product (and among shared). Categories likewise product-or-shared. Single-level categories ([01-REQUIREMENTS.md](01-REQUIREMENTS.md)).
- **Lifecycle:** `Draft` -> `Published` -> `Archived` (Archived -> Draft by update). `PublishKbArticleRequestHandler` sets `published_at` on first publish and validates title, slug, category and non-empty body (the portal article URL includes the category slug); `ArchiveKbArticleRequestHandler` removes it from public search/sitemap. Edits to a Published article stay live (no revision history; cut for core).
- **Markdown pipeline:** Markdig converts stored Markdown to HTML on read, then `IHtmlSanitizer` (HtmlSanitizer library) sanitizes; both behind application abstractions `IMarkdownRenderer` and `IHtmlSanitizer`, implemented in Infrastructure. Raw HTML in Markdown is disabled in the pipeline (**Assumption**) and the sanitizer is the second line of defense. Rendered HTML is cached in-memory keyed by article id + `updated_at` (**Assumption**, bounded size).
- **Search:** Postgres FTS (`websearch_to_tsquery`, `ts_rank_cd`) over title (weight A), summary (B), body (C); published only; capped result count and snippet via `ts_headline` on the summary. One handler (`SearchPublicKbArticlesRequestHandler`) serves portal search and contact-form deflection ("same use case" per [02-ARCHITECTURE.md](02-ARCHITECTURE.md)).
- **Images:** `UploadKbImageRequestHandler` validates size (constant, default 5 MB, **Assumption**), content sniffs magic bytes (png/jpeg/gif/webp; **SVG rejected**), generates a random storage key under the `kb-images/` prefix via `IKbImageStore` (over `SyntaxCircus.Storage`), and returns the public URL. The prefix is publicly readable: the API serves it as static assets from storage, which are exempt endpoints that execute no application workflow (D-021); ticket attachments never live under this prefix. Orphan cleanup is out of scope (Risks).
- **Public endpoints** are anonymous, rate-limited per IP (pattern `CLIENT_IP_RATE_LIMITING.md`, same pinned-subnet/forwarded-headers config as P05) and return `Cache-Control: public, max-age` short TTL (**Assumption**: 60 s).
- **Public data minimization:** public DTOs omit author identity, ids of unpublished items, and internal timestamps other than `published_at`/`updated_at`.
- **Reply linking:** `AddAgentReplyRequestHandler` (P06) already accepts article ids and records `TicketArticle`; P08 adds validation that ids refer to **Published** articles visible to the ticket's product (shared or same product) — implemented in P06's handler via `IKbRepository`, tested here (**Assumption**: if P06 shipped the field without validation, P08-T10 adds it). Outbound emails render linked articles as `/p/{key}/kb/{category}/{slug}` links (portal route from P09, per `02-ARCHITECTURE.md` section 8.2).
- **Editor preview (D-021):** `POST /api/kb/preview` is handled by `RenderKbPreviewRequestHandler` (Agent policy only; `IMarkdownRenderer` then `IHtmlSanitizer`), so one Markdig plus HtmlSanitizer pipeline serves previews and published pages. Admin has no local renderer; the preview pane calls the API through `IKbClient` and renders the returned sanitized HTML.
- **Admin editor** is a plain textarea with debounced live preview (no JS editor library; no JS interop) and toolbar buttons that insert Markdown snippets; image upload through `InputFile`.

## Application Boundaries

Follow _template APPLICATION_ARCHITECTURE.md. Routes are fixed in
`02-ARCHITECTURE.md` section 7.4 (the source of truth). All controller actions take their
handler via `[FromServices]`, pass `CancellationToken`, and map `Result` through
`SyntaxCircus.AspNetCore.Common` ProblemDetails mapping. Agent endpoints require
the agent policy; category writes and article publish/archive/delete follow
the roles recorded in P04 and D-022 (any Agent may edit and publish articles and create or
update categories; only Admin may delete categories).

| Entry point/use case | Named handler | Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |
| :------------------- | :------------ | :------------------- | :---------------------------- | :------------------------ | :------- |
| `GET /api/kb/articles` (filters: product, category, status, text) | `ListKbArticlesRequestHandler` | `IKbRepository`, `ICurrentUserService` | EF `KbRepository` (FTS for text) | 200 `PagedResponse<KbArticleSummaryDto>` | Named handler |
| `GET /api/kb/articles/{id}` | `GetKbArticleRequestHandler` | `IKbRepository` | `KbRepository` | 200 `KbArticleDto` incl. Markdown; 404 | Named handler |
| `POST /api/kb/articles` | `CreateKbArticleRequestHandler` | `IKbRepository`, `IUnitOfWork`, `ICurrentUserService`, `TimeProvider` | `KbRepository`, EF unit of work | 201 + location; 400 validation; 409 duplicate slug | Named handler |
| `PUT /api/kb/articles/{id}` | `UpdateKbArticleRequestHandler` | `IKbRepository`, `IUnitOfWork`, `TimeProvider` | `KbRepository` | 200; 404; 409 concurrency/slug | Named handler |
| `POST /api/kb/articles/{id}/publish` | `PublishKbArticleRequestHandler` | `IKbRepository`, `IUnitOfWork`, `TimeProvider` | `KbRepository` | 204; 422 when incomplete | Named handler |
| `POST /api/kb/articles/{id}/archive` | `ArchiveKbArticleRequestHandler` | `IKbRepository`, `IUnitOfWork`, `TimeProvider` | `KbRepository` | 204; 404 | Named handler |
| `POST /api/kb/preview` | `RenderKbPreviewRequestHandler` | `IMarkdownRenderer`, `IHtmlSanitizer` | Markdig renderer, HtmlSanitizer adapter | 200 `KbPreviewResponse` (sanitized HTML); 400 when the source exceeds the size limit | Named handler (D-021); Agent policy only |
| `POST /api/kb/images` (multipart) | `UploadKbImageRequestHandler` | `IKbImageStore` (over `SyntaxCircus.Storage`), `ICurrentUserService`, `TimeProvider` | `KbImageStore : IKbImageStore` using `IStorageProvider` | 201 `KbImageDto{url}`; 413/415/400 | Named handler. `IKbImageStore` is a KB-specific abstraction distinct from `IAttachmentStore` because of the public-read prefix |
| `GET /api/kb/categories` | `ListKbCategoriesRequestHandler` | `IKbRepository` | `KbRepository` | 200 | Named handler |
| `POST /api/kb/categories` | `CreateKbCategoryRequestHandler` | `IKbRepository`, `IUnitOfWork` | `KbRepository` | 201; 409 slug | Named handler |
| `PUT /api/kb/categories/{id}` | `UpdateKbCategoryRequestHandler` | `IKbRepository`, `IUnitOfWork` | `KbRepository` | 200; 404; 409 | Named handler |
| `DELETE /api/kb/categories/{id}` (Admin) | `DeleteKbCategoryRequestHandler` | `IKbRepository`, `IUnitOfWork` | `KbRepository` | 204; 409 when it still holds articles | Named handler |
| `GET /api/public/kb/search?product={key}&q=&category=` (anonymous) | `SearchPublicKbArticlesRequestHandler` | `IKbRepository`, `IProductRepository` | `KbRepository` (FTS) | 200 `KbSearchResponse`; empty list for unknown/inactive key (no enumeration) | Named handler; also used by deflection (same use case) |
| `GET /api/public/kb/articles/{product}/{slug}` (anonymous) | `GetPublishedKbArticleRequestHandler` | `IKbRepository`, `IProductRepository`, `IMarkdownRenderer`, `IHtmlSanitizer` | `KbRepository`, `MarkdigMarkdownRenderer`, `HtmlSanitizerAdapter` | 200 `PublishedKbArticleDto` (sanitized HTML); uniform 404 | Named handler |
| `GET /api/public/kb/categories?product={key}` (anonymous) | `ListPublicKbCategoriesRequestHandler` | `IKbRepository`, `IProductRepository` | `KbRepository` | 200 with published-article counts | Named handler |
| `GET /api/public/sitemap` (anonymous) | `GetSitemapEntriesRequestHandler` | `IKbRepository`, `IProductRepository` | `KbRepository` | 200 `SitemapEntryDto[]` (relative path + `updated_at`) | Named handler; consumed by portal `MapSitemap` in P09 |
| KB image files under `kb-images/` | Exempt | None | API static-file serving from storage | Static bytes | Exempt: static assets execute no workflow (D-021) |
| Admin KB editor (UI) | Consumes the agent handlers above | Admin `IKbClient` | `KbClient : ApiClientBase` | `Result` | UI only; no new server entry point |

## Razor Component Boundaries

Follow _template RAZOR_COMPONENT_ARCHITECTURE.md. All admin-side (portal KB
pages are in [Phase 09](PHASE-09-public-portal.md)). Feature folder
`TechStrap.Admin/Features/Kb`.

| Component/feature | `.razor.cs` decision | ViewModel/factory decision | State behavior | API DTO boundary |
| :---------------- | :------------------- | :------------------------- | :------------- | :--------------- |
| `KbArticleListPage` (`/kb`) | Paired (query params, async load, paging, injection) | `KbArticleRowViewModel` (simple, built in code-behind) | Loading/Error/Empty/Content; filters product/category/status/text in query string | `KbArticleSummaryDto`, `PagedResponse<>` |
| `KbStatusBadge` | Inline (parameter only) | None | Stateless | Status constants from Contracts |
| `KbArticleEditorPage` (`/kb/new`, `/kb/{Id:guid}`) | Paired (route, load, save/publish/archive orchestration, dirty tracking, navigation guard) | `KbArticleEditorViewModel` (form model); lookups assembled by feature-local `KbArticleEditorPresenter` (article + categories + products, multiple dependencies) | Dirty tracking; unsaved-changes prompt; 409 -> reload banner keeping the draft text | `KbArticleDto`, `CreateKbArticleRequest`, `UpdateKbArticleRequest` |
| `MarkdownEditor` (textarea + toolbar + split preview) | Paired (debounce timer, snippet insertion, disposal) | None (binds a `string`) | Local text; debounced (constant 300 ms) preview refresh; disposes timer | None |
| `KbPreviewPane` | Paired (receives the sanitized HTML returned by `IKbClient.PreviewAsync` from the editor's debounced call; renders `MarkupString` of it, the second and last `MarkupString` site in admin) | None | Re-renders on parameter change; loading and error states for the preview call | `KbPreviewResponse` |
| `KbImageUploadButton` | Paired (`InputFile`, size/type pre-checks, async upload, error state) | None | Uploading/Error; raises one callback with the returned URL | `KbImageDto` |
| `KbCategoriesPage` | Paired (CRUD inline, confirm delete) | `KbCategoryRowViewModel` | Loading/Error/Empty/Content; delete blocked message on 409 | `KbCategoryDto`, `Create/UpdateKbCategoryRequest` |
| `ArticlePicker` (in `ReplyComposer`) | Paired (debounced search, selection state, injection) | `ArticleChoiceViewModel` (title + status + link) | Selected ids list owned by `ReplyComposer` (parameter + callback) | `KbArticleSummaryDto`; sends article ids in `AddAgentReplyRequest` |
| `ReplyComposer` (changed from P07) | Already paired; adds `ArticlePicker` and selected-article chips | `ReplyComposerViewModel` gains `LinkedArticleIds` | Draft preserves selected articles | `AddAgentReplyRequest.LinkedKbArticleIds` |
| `LinkedArticlesList` (ticket timeline entry) | Inline | `LinkedArticleViewModel` built by timeline factory | Stateless | `MessageDto.LinkedArticles` |

## Syntax Circus Packages

Versions in [03-PACKAGE-MAP.md](03-PACKAGE-MAP.md). Markdig and HtmlSanitizer
are third-party NuGet packages (not Syntax Circus) already listed there.

| Package | Concern | Why it belongs in this phase | Verification |
| :------ | :------ | :--------------------------- | :----------- |
| `SyntaxCircus.Storage` | Image bytes | `UploadKbImageRequestHandler` stores images via `IStorageProvider` | Integration test with Local provider: store, read-back, path traversal key rejected |
| `SyntaxCircus.Common` | `Result` | All handlers return `Result`/`Result<T>` | Unit tests |
| `SyntaxCircus.AspNetCore.Common` | ProblemDetails mapping, security headers | Controller outcome mapping; image response headers | Api.Tests outcome tests |
| `SyntaxCircus.EntityFrameworkCore.Postgres` | Persistence | FTS queries, migrations if needed | Integration tests (Testcontainers) |
| `SyntaxCircus.Blazor.Auth` / `Http.Resilience` / `Blazor.Components` | Admin typed client, error boundaries | Admin editor follows the P07 pattern | bUnit + stub-handler tests |

## Deliverables

- [ ] KB persistence verified (migration only if P03 gap); FTS ranking verified.
- [ ] `IMarkdownRenderer`, `IHtmlSanitizer`, `IKbImageStore`, `IKbRepository` KB members and Infrastructure implementations.
- [ ] 12 agent handlers (including `RenderKbPreviewRequestHandler`) and 4 public handlers with controllers and DTOs in Contracts.
- [ ] Reply-article linking validated and surfaced in the timeline.
- [ ] Admin KB list, editor with live preview and image upload, categories page, article picker in the reply composer.
- [ ] Seed/demo KB data for local development.

## Actionable Tasks

- [ ] **P08-T01** Verify P03's KB entities, unique constraints (product+slug, shared slug), FTS vector/GIN index; add an EF migration via `dotnet ef` only for gaps
  - **Depends on:** P06
  - **Validation:** Integration test (Testcontainers) inserts articles and asserts title matches outrank body matches; `dotnet ef migrations has-pending-model-changes` clean.
- [ ] **P08-T02** Define KB DTOs/requests/responses (including `KbPreviewRequest`/`KbPreviewResponse`) and constants (`KbArticleStatuses`, `KbLimits`) in `TechStrap.Contracts`
  - **Depends on:** P08-T01
  - **Validation:** Build; Architecture.Tests confirm Contracts has no inward references; no `*ViewModel` types in Contracts.
- [ ] **P08-T03** Implement `IMarkdownRenderer` (Markdig, raw HTML off), the `IHtmlSanitizer` implementation with an explicit allow-list (headings, lists, code, tables, links with `rel=noopener nofollow`, images from `kb-images/` or https) and `RenderKbPreviewRequestHandler` (D-021)
  - **Depends on:** P08-T02
  - **Validation:** Unit tests over an XSS corpus (`<script>`, `javascript:` links, `onerror`, SVG/`iframe`, data URIs, nested/mutated markup) producing safe output; a fixed Markdown fixture renders to expected HTML; `RenderKbPreviewRequestHandlerTests` assert the preview output equals the published-page pipeline output for the same Markdown and that oversize input is rejected.
- [ ] **P08-T04** Implement agent article handlers: `ListKbArticlesRequestHandler`, `GetKbArticleRequestHandler`, `CreateKbArticleRequestHandler`, `UpdateKbArticleRequestHandler` (slug uniqueness, concurrency token)
  - **Depends on:** P08-T02
  - **Validation:** Application.Tests with NSubstitute for each success/validation/not-found/conflict outcome, cancellation passed to repository calls.
- [ ] **P08-T05** Implement `PublishKbArticleRequestHandler` and `ArchiveKbArticleRequestHandler` with lifecycle rules and `TimeProvider`
  - **Depends on:** P08-T04
  - **Validation:** Unit tests: publish incomplete -> failure; first publish sets `published_at`, re-publish does not change it; archive hides from public search (integration test).
- [ ] **P08-T06** Implement category handlers: list/create/update/delete (delete blocked while articles exist)
  - **Depends on:** P08-T02
  - **Validation:** Unit tests for each outcome; integration test for the blocked delete.
- [ ] **P08-T07** Implement `UploadKbImageRequestHandler` + `IKbImageStore` over `SyntaxCircus.Storage` (size limit, magic-byte sniff, random key, `kb-images/` prefix, SVG rejected)
  - **Depends on:** P08-T02
  - **Validation:** Unit tests (oversize -> 413 outcome, wrong magic bytes -> 415, SVG -> 415); integration test with Local provider verifies the stored key is random and not user-controlled.
- [ ] **P08-T08** Implement public handlers: `SearchPublicKbArticlesRequestHandler`, `GetPublishedKbArticleRequestHandler`, `ListPublicKbCategoriesRequestHandler`, `GetSitemapEntriesRequestHandler`
  - **Depends on:** P08-T03, P08-T05
  - **Validation:** Integration tests: draft/archived never returned; shared + product articles returned; unknown and inactive keys give the same empty/404 result; `ts_headline` snippets contain no unsanitized markup.
- [ ] **P08-T09** Add controllers (`KbArticlesController`, `KbCategoriesController`, `KbImagesController`, `KbPreviewController`, `PublicKbController`) with `[FromServices]` handlers, agent policy, role rules, per-IP rate limit and short cache headers on public routes
  - **Depends on:** P08-T04 … P08-T08
  - **Validation:** Api.Tests: 401/403 for non-agents on agent routes; public routes anonymous; 429 after limit; ProblemDetails shape; Architecture.Tests: every action has exactly one handler parameter.
- [ ] **P08-T10** Validate linked article ids in `AddAgentReplyRequestHandler` (published, product-visible) and include linked articles in the timeline/email template data
  - **Depends on:** P08-T05
  - **Validation:** Unit tests: draft/foreign-product id rejected; integration test writes `TicketArticle` rows in the same transaction as the message; email template test includes the portal link.
- [ ] **P08-T11** Add KB seed/demo data to the local seeder
  - **Depends on:** P08-T05
  - **Validation:** Fresh local run shows published and draft articles in two products plus one shared article.
- [ ] **P08-T12** Regenerate/check the OpenAPI document includes all KB operations with security schemes
  - **Depends on:** P08-T09
  - **Validation:** Api.Tests snapshot of `/openapi/v1.json` operation ids; reviewed diff.
- [ ] **P08-T13** Add `IKbClient` (admin) with ProblemDetails -> `Result`, no retry on mutating calls, multipart image upload
  - **Depends on:** P08-T09, P07 typed-client pattern
  - **Validation:** Stub-handler unit tests per method; multipart content-type and filename asserted.
- [ ] **P08-T14** Add `IKbClient.PreviewAsync` (`POST /api/kb/preview`, no retry, cancellation on superseded requests) for the editor preview
  - **Depends on:** P08-T03, P08-T13
  - **Validation:** Stub-handler tests: success returns the sanitized HTML, 400 maps to a `Result` failure, a superseded call is cancelled; Api.Tests assert the XSS corpus yields no executable output through `POST /api/kb/preview` and that non-agents get 401/403.
- [ ] **P08-T15** Build `KbArticleListPage` and `KbStatusBadge`
  - **Depends on:** P08-T13
  - **Validation:** bUnit: filters call the client with expected parameters; empty/error states; status badges for each status constant.
- [ ] **P08-T16** Build `MarkdownEditor` (debounced preview, snippet toolbar) and `KbPreviewPane`
  - **Depends on:** P08-T14
  - **Validation:** bUnit with `FakeTimeProvider` and a fake `IKbClient`: rapid input yields one preview call; the pane renders the API's sanitized HTML as returned; a failed preview shows an inline error without losing the text; timer disposed on teardown.
- [ ] **P08-T17** Build `KbArticleEditorPage` + `KbArticleEditorPresenter` (save, publish, archive, unsaved-changes guard, 409 handling)
  - **Depends on:** P08-T15, P08-T16
  - **Validation:** bUnit: save sends concurrency token; publish disabled until required fields valid; 409 keeps the text and shows a banner; presenter unit-tested.
- [ ] **P08-T18** Build `KbImageUploadButton` and insert returned image Markdown at the caret/end
  - **Depends on:** P08-T16
  - **Validation:** bUnit with `InputFile` test helper: oversize/wrong-type shows error without calling the API; success inserts `![alt](url)`.
- [ ] **P08-T19** Build `KbCategoriesPage`
  - **Depends on:** P08-T13
  - **Validation:** bUnit: delete-blocked message on 409; reorder/sort order persisted via update call.
- [ ] **P08-T20** Add `ArticlePicker` and linked-article chips to `ReplyComposer`; render linked articles in the timeline
  - **Depends on:** P08-T10, P08-T13, P07-T11
  - **Validation:** bUnit: selecting an article adds its id to the submit request; only published results offered; draft preserved on failure.

## Success Criteria

- [ ] An agent can create a category, write an article in Markdown with live preview and an uploaded image, publish it, and archive it.
- [ ] Anonymous callers can search published articles (ranked, title-weighted), fetch an article (sanitized HTML), list categories and get sitemap entries; drafts/archived never leak.
- [ ] An agent can attach published articles to a public reply; the customer email and the timeline show them.
- [ ] XSS corpus passes through the render/sanitize pipeline with no executable output in published pages and in `POST /api/kb/preview`.
- [ ] Image uploads reject SVG, oversize and mismatched types; stored keys are server-generated.
- [ ] Every KB entry point maps to exactly one named handler; `dotnet build`/`dotnet test` green incl. integration tests.

## Boundary Validation

- [ ] Application use-case entry points delegate to the named handlers listed above.
- [ ] Framework-owned operational or static exemptions execute no application workflow (`kb-images/` static serving only).
- [ ] Handler constructor dependencies contain only approved abstractions (no `IStorageProvider` or Markdig types directly in handlers).
- [ ] Persistence and integration entities do not cross infrastructure boundaries (handlers return DTOs/domain results, never EF entities).
- [ ] Cancellation reaches asynchronous handler dependencies.
- [ ] Expected outcomes and transport mapping have focused tests.
- [ ] Infrastructure implementations have integration coverage (`KbRepository` FTS, `KbImageStore`, renderer/sanitizer).
- [ ] Inline Razor components contain only simple parameters and, at most, one trivial synchronous `EventCallback`-forwarding callback.
- [ ] Every component beyond the inline ceiling uses paired `.razor` and `.razor.cs` files, with all C# in code-behind.
- [ ] Each Razor ViewModel is feature-local and presentation-only; no direct-model exception exposes an API ViewModel.
- [ ] A factory or presentation service is used only for non-trivial mapping, asynchronous assembly, or multiple dependencies (`KbArticleEditorPresenter` only).
- [ ] API request and response contracts use DTO names and contracts, never Razor ViewModels.
- [ ] Repeated or business-meaningful literals are named constants (status strings, image limits, debounce interval, cache TTL, `kb-images/` prefix).
- [ ] Duplicated-looking logic across flows was evaluated for genuine divergence (public search vs. agent list share a repository query helper only where ranking logic truly matches; preview and published rendering share one pipeline through `IMarkdownRenderer`, D-021).

## Risks and Open Questions

- [ ] **Preview round trip (D-021, resolved):** each debounced preview calls `POST /api/kb/preview`; keep the debounce constant and the size limit so the endpoint cannot be used as a free renderer (Agent policy only).
- [ ] **KB image serving** is not a catalog handler; it is exempt static assets under a public-read prefix (D-021). Add an image-serving handler only if access rules are ever needed.
- [ ] Orphaned images (uploaded but never referenced) accumulate; a cleanup job is deferred.
- [ ] `ts_headline` on large bodies is costly; limit to summary field and cap result count (default 10 per page; **Assumption**).
- [ ] Per-product vs shared slug collisions need a clear rule (shared wins on conflict at read time; creation blocks duplicates across both scopes) — confirm.
- [ ] bUnit (in the package map) comes with `tests/TechStrap.Admin.Tests` from [PHASE-07](PHASE-07-admin-app.md); Markdig and HtmlSanitizer versions are pinned in the package map.
- [ ] Carried forward from the PHASE-03 final review: The category and article product match (a product article may only use a shared category or one of its own product) is enforced in the create and edit handlers; the repository cannot check it.
- [ ] Carried forward from the PHASE-03 final review: `KbCategory` has no concurrency version yet; add one with the category edit handlers (the model change needs a tool-generated migration).

## Handoff

Before [PHASE-09](PHASE-09-public-portal.md) starts: the four public KB
endpoints are deployed in the API with DTOs in `TechStrap.Contracts`, the
OpenAPI document lists them, seed data includes published articles in at least
two products plus a shared article, and the portal route shape for articles
(`/p/{key}/kb/{category}/{slug}`) used in reply-email links is agreed. Admin KB editing
need not be finished for Phase 09 to begin, but published seed data must exist.
