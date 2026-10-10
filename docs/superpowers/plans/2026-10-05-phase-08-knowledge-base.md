# PHASE-08 Knowledge Base Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give TechStrap a knowledge base.
- **Agents** write Markdown articles in the Admin, with a live sanitized preview, images and categories. They publish and archive articles and link them from replies.
- **The API** serves published articles, search, categories and a sitemap anonymously to the PHASE-09 portal. It serves uploaded images from a public URL.

**Architecture:**
- **Already in place (PHASE-03):** the KB schema, unique `(product_id, slug)` indexes and a weighted full-text search vector.
- **Domain:** lifecycle fixes and publish validation.
- **Markdown:** `IMarkdownRenderer` and `IHtmlSanitizer` from PHASE-06a are reused. They gain pipe tables, plus `img` and table tags limited to http(s).
- **Images:** stored through `SyntaxCircus.Storage` under `kb-images/`, and served anonymously by the API with `nosniff` and a CSP sandbox. Their URLs come from `TECHSTRAP_API_PUBLIC_URL`.
- **Public endpoints:** anonymous, under the existing Public rate limiter, with short cache headers.
- **Admin:** the KB list, editor and categories pages are built from the 07a-07c patterns: typed clients, thin pages, stale guards, uncertain writes, `ConfirmDialog`, the palette and `RailLink`.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core + Npgsql (Postgres full-text search), Markdig, the HTML sanitizer, `SyntaxCircus.Storage`, Blazor Server, xUnit v3, Shouldly, NSubstitute, bUnit and Pester.

**Spec:** `docs/architecture/PHASE-08-knowledge-base.md`, the KB sections of `UX-BRIEF-admin.md`, `PHASE-09-public-portal.md` (consumers), D-021, D-043, and the owner decisions of 2026-10-05, recorded as D-044 in Task 1.

### Owner decisions (2026-10-05), recorded as D-044
- **Slug uniqueness:** blocked across scopes. A product article cannot reuse a shared article's slug, and a shared article cannot reuse any product's slug.
- **Image URLs:** `TECHSTRAP_API_PUBLIC_URL`, with images at `https://<api public host>/kb-images/<key>`.
  - Blank in Development: the request's own origin is used.
  - Required in Production.
  - The reverse proxy exposes `/kb-images/`.
- **Audit:** KB changes stay out of the audit log in this phase.
- **Delivery:** one PR.

### Technical decisions this plan makes (D-044; the owner confirms at plan review)
- **Scope.**
  - An article with a null product is shared.
  - Product and slug are immutable after create.
  - Categories have a single level and are product or shared.
  - A shared article may use only a shared category.
  - The category slug `search` is reserved.
- **Lifecycle.**
  - Draft, then Published, then Archived.
  - Updating an Archived article returns it to Draft.
  - Edits to a Published article are live immediately.
  - No revision history and no hard delete.
  - Publishing requires title, slug, body and category. If any is missing, the API returns 400 `kb-publish-incomplete`.
- **Roles.** Agents manage articles, preview, images, and category create and update. Category delete is Admin-only.
- **Images.**
  - 5 MB limit.
  - png, jpeg, gif or webp only, checked by magic bytes. SVG is refused.
  - No database row and no orphan cleanup.
  - Served with an immutable long cache.
- **Public API.**
  - `max-age=60` on hits, `no-store` on 404, `max-age=300` on the sitemap.
  - Search page size 10 (max 25), with a plain-text `ts_headline` snippet (the consumer encodes it).
  - An unknown or inactive product returns an empty result.
  - Article lookup tries the product first, then shared.
- **Reply links.**
  - At most 10 per reply.
  - Each article must be Published and either shared or in the ticket's product.
  - The customer email lists the portal links.
- **Admin.**
  - The "Knowledge base" rail link shows for every agent, plus the `go-kb` command.
  - One new `MarkupString` site: the preview pane.
  - Image upload appends a snippet. There is no caret insert, paste or drag-drop yet.
  - "View on portal" uses an optional Admin `TECHSTRAP_PORTAL_PUBLIC_URL`.
- **CSP.** No change is needed. In Development, the Admin's `img-src` already allows `http://localhost:*` and `http://127.0.0.1:*`, and Production allows `https:`. `KbImageCspTests` pins this.

### Decisions made while drafting (D-044 records them)
- **A KB-only content profile.** `IKbContentRenderer` (the KB Markdig profile with pipe tables, plus `KbHtmlSanitizer` allowing http(s) `img` and table tags) is added alongside the shared sanitizer, not by widening it.
  - Agent replies, which customers read, keep the old pipeline. This stops tracking pixels and tables appearing in replies.
  - `MessagePipelineUnchangedTests` pins the old output exactly.
- **Category migration.** Categories gain a nullable `description` (300 chars) and an `xmin` concurrency version. This is one migration, `AddKbCategoryVersionAndDescription`, generated with `dotnet ef`.
- **Slug rule.** The cross-scope rule also covers category slugs (`kb-category-slug-taken`), so portal category addresses stay unambiguous.
- **Publish and archive** return 200 with the article and take an optional `?version=`. A stale version gets 409.
- **Errors without a `Result` kind.** An incomplete publish, a wrong image type or an oversize image returns 400 with a field code. A body larger than the 6 MB request cap gets Kestrel's 413.
- **Article list filters.** `ListKbArticlesRequest` adds `sharedOnly`, `includeShared` (default true) and `status`, plus `Text` search ordered by best match.
- **Public categories.** Only categories holding at least one published article are listed. An unknown or inactive product returns an empty list; for the article page it returns a uniform 404.
- **Images route.** `GET /kb-images/{guid}.{ext}` is a minimal-API route and is excluded from OpenAPI.
- **Reply links.** An article that has no category cannot be linked. A second `PlanAgentReplyAsync` overload carries the links into `AgentReplyEmail.Articles`.
- **Admin editor.**
  - **Unknown upload outcome.** An image upload whose outcome is unknown is not held until a reload: nothing changes until the snippet is inserted.
  - **Article picker.** It sits behind a "Link a knowledge base article" button, in public replies only.
  - **Ticket timeline.** It shows linked article titles as plain text, because `LinkedArticleDto` has no portal address. This is recorded as a known gap.
  - **"View on portal"** for a shared article uses the first product by name.
  - **Preview pane.** `KbPreviewPane` is the second allowed `MarkupString` site.
- **Seed data.** It gains the Paperplane category `guides` and the published article `using-dark-mode`, so `DevSeederTests` now pins 5 articles. `HostFactory` defaults `TECHSTRAP_API_PUBLIC_URL=https://api.test`.
- **Known mutation survivors.** These are honest and documented:
  - Removing the archived filter from the category in-use check survives, because the foreign key still refuses the delete.
  - Removing the image endpoint's own `nosniff` survives, because the shared security headers set it.
- **Shared files between Tasks 1-8 and 9-11:**
  - `ConfigContract.Tests.ps1`, on different rows;
  - `PHASE-08-knowledge-base.md`: Tasks 7 and 8 tick T01-T12, Task 11 ticks T13-T20;
  - the roadmap row: Task 8 sets "In progress", Task 11 sets "complete";
  - D-044: Task 11 appends one consequence;
  - `RepositoryDocs.Tests.ps1`: Task 11 rewrites Task 8's ticks test.

  The tasks run in order, so these overlaps are safe.

## Global Constraints

- **Build.** .NET SDK 10.0.401 targeting `net10.0`, with `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild`. Private fields are `_camelCase`, constants are PascalCase, and namespaces are file-scoped.
- **Packages.** No new package unless a task names one. Versions are managed centrally.
- **References.** Admin and Portal reference only Contracts and Hosting. Domain and Contracts reference nothing.
- **API.**
  - One `[FromServices]` handler per action.
  - Every action has a `ControllerActions` row and an explicit policy.
  - `RoutePolicyCoverage` and the OpenAPI security tests stay green.
- **Persistence.** Generate any migration with `dotnet ef migrations add` only. Never hand-edit a migration. The EF pending-model check must be clean.
- **Config (D-043).** Every new key is added to the host's `appsettings.json`, its `.env.example` and its `deploy/.env.<app>.example`. `scripts/tests/ConfigContract.Tests.ps1` must pass.
- **Admin.**
  - Components never inject `HttpClient`.
  - Copy lives in `*Copy` constants.
  - No inline script or style. JS goes in `wwwroot/js` modules.
  - Writes use `CancellationToken.None`, `_disposed` guards and the `WriteOutcomes` classifier. An uncertain write is held until a reload.
  - List loads use `_loadId` stale guards.
- **Encoding.** Write non-ASCII as `\u` escapes, using Python or .NET, never GNU sed. The Write tool decodes `\uXXXX`, so grep afterwards. `SourceEncodingTests` and `SourceEscapeTests` must pass.
- **Secrets and PII.** Never log tokens, cookies, emails, names or search text.
- **Tests.**
  - Failing test first, with RED and GREEN recorded.
  - Prove each pin with a recorded mutation.
  - Signed-in host tests call `AssertEveryCallBore`.
  - Leak tests run at Verbose.
  - Tests that set environment variables go in `ProcessEnvironmentCollection`.
  - Run a `--no-incremental` rebuild before the final runs, because mutation runs can leave stale DLLs.
- **Commits.** Use Conventional Commits. End each one with exactly these two lines, on separate lines:
  ```
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
  ```
- **Forbidden.** `git add -f`, committing `.superpowers/`, `docker compose down -v`, killing processes you did not start, and committing without first running `git diff --cached --stat`.
- **Verification.**
  - `dotnet build TechStrap.slnx -c Release`: 0 warnings.
  - `dotnet test --solution TechStrap.CI.slnf -c Release`: passes.
  - `pwsh -File scripts/Invoke-ScriptTests.ps1`: passes.
  - The EF pending-model check is clean.

## Review Focus

1. **Stored XSS through KB Markdown or the preview.**
   - Raw HTML, `javascript:` or `data:` links and images, and `on*` attributes are stripped.
   - `img` is limited to http(s).
   - Message-body rendering is unchanged.
   - Pinned in Task 2 (sanitizer corpus) and Task 10 (preview pane).
2. **Image upload abuse.** Polyglot or SVG files, a spoofed content type, oversize files and key traversal are refused. Images are served with `nosniff` and a CSP sandbox. Pinned in Task 5.
3. **Unpublished or wrong-product content leaking publicly.** This covers search, article, category counts and the sitemap. Snippets are HTML-encoded. Pinned in Task 6.
4. **Slug collisions across scopes, lost edits on a version conflict, and Archived-to-Draft edits.** Pinned in Tasks 1, 3, 4 and 10.
5. **A reply linking an unpublished or other-product article, or a broken link in the customer email.** Pinned in Task 8.

---

### Task 1: Record D-044; the Domain lifecycle, publish validation and the category version with its generated migration; the Contracts DTOs; the cross-scope slug rule

**Review Focus pin:** Review Focus 4 (slug collisions across scopes, lost edits on conflict, and Archived-to-Draft edits). Pinned here by `KbArticleTests` (an edit returns an Archived article to Draft and keeps its first publish time; a publish without a category is a field error), `KbScopeRuleTests` (`ArticleSlugTakenAsync` and `CategorySlugTakenAsync` refuse a slug used in another scope, an archived article still holds its slug, and two stale category edits give one conflict) and `RepositoryDocs.Tests.ps1` (D-044 exists). Tasks 3, 4 and 10 pin the same rule in the handlers and the editor.

**Files:**
- Create: `docs/superpowers/plans/2026-10-05-phase-08-knowledge-base.md` (this plan, saved as the plan of record that D-044 links to)
- Modify: `docs/architecture/04-DECISION-LOG.md`
- Modify: `docs/architecture/05-SCHEMA.md`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`
- Modify: `src/TechStrap.Application/Knowledge/KbArticleQuery.cs`
- Modify: `src/TechStrap.Application/Knowledge/KbSearchQuery.cs`
- Modify: `src/TechStrap.Application/Persistence/IKbRepository.cs`
- Create: `src/TechStrap.Contracts/Kb/KbArticleDtos.cs`
- Create: `src/TechStrap.Contracts/Kb/KbCategoryDtos.cs`
- Create: `src/TechStrap.Contracts/Kb/KbNames.cs`
- Create: `src/TechStrap.Contracts/Kb/PublicKbDtos.cs`
- Modify: `src/TechStrap.Domain/Knowledge/KbArticle.cs`
- Modify: `src/TechStrap.Domain/Knowledge/KbCategory.cs`
- Modify: `src/TechStrap.Domain/Rules/DomainLimits.cs`
- Create: `src/TechStrap.Infrastructure/Migrations/20261005144542_AddKbCategoryVersionAndDescription.cs`
- Modify: `src/TechStrap.Infrastructure/Migrations/TechStrapDbContextModelSnapshot.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Configurations/KnowledgeRecordConfigurations.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Mapping/KnowledgeMappings.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Records/KnowledgeRecords.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.cs`
- Test (modify): `tests/TechStrap.Api.Tests/Tickets/TicketLifecycleEndToEndTests.cs`
- Test (create): `tests/TechStrap.Application.Tests/Knowledge/KbContractParityTests.cs`
- Test (modify): `tests/TechStrap.Domain.Tests/Knowledge/KbArticleTests.cs`
- Test (modify): `tests/TechStrap.Infrastructure.IntegrationTests/KbRepositoryGuardTests.cs`
- Test (modify): `tests/TechStrap.Infrastructure.IntegrationTests/KbRepositoryTests.cs`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/KbScopeRuleTests.cs`
- Test (modify): `tests/TechStrap.Infrastructure.IntegrationTests/KbSearchTests.cs`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/KbTestData.cs`
- Generated, never edited: `src/TechStrap.Infrastructure/Migrations/<timestamp>_AddKbCategoryVersionAndDescription.Designer.cs` (about 1,280 lines, written by `dotnet ef`; it is the whole model, so it is not pasted here)

**Interfaces:**
- Consumes: `KbArticle`, `KbCategory`, `KbRepository` and `IKbRepository` as they are on `main`; `HasXminConcurrencyToken` and `ApplyOriginalVersion` (D-026); `DomainLimits`, `Guard` and `DomainErrors`; `PostgresIntegrationTestBase`, `PersistenceTestHost` and `TicketScenario` (test hosts over a real Postgres); `Get-RepoText` in `RepositoryDocs.Tests.ps1`.
- Produces:
  - Domain: `KbArticle.Update` returns an Archived article to Draft and keeps `PublishedAt`; `KbArticle.Publish` returns a Validation error `KbArticle.PublishIncompleteCode` (`kb-publish-incomplete`) whose target is `title`, `slug`, `body` or `category`; `KbCategory.Create(Guid? productId, string? slug, string? name, int sortOrder, TimeProvider clock, string? description = null)`, `KbCategory.Update(string? name, string? description, int sortOrder)`, `KbCategory.Restore(Guid id, Guid? productId, string name, string slug, string? description, int sortOrder, uint version)`, `KbCategory.Description` and `KbCategory.Version`; the Validation error `kb-category-reserved-slug` (target `slug`) for the slug `search`; `DomainLimits.KbCategoryDescriptionMaxLength` (300) and `DomainLimits.KbReservedCategorySlug`.
  - Persistence: `kb_categories.description` (nullable, 300) and the `xmin` concurrency token, in the generated migration `AddKbCategoryVersionAndDescription`; `KbRepository.UpdateCategory` applies the caller's version.
  - Repository: `Task<bool> IKbRepository.ArticleSlugTakenAsync(Guid? productId, string slug, CancellationToken)` and `CategorySlugTakenAsync(...)` (the cross-scope rule: a product slug is taken when that product or the shared space has it; a shared slug is taken when any scope has it; any status counts); `KbArticleQuery.SharedOnly` and `KbSearchQuery.SharedOnly` (last, defaulted).
  - Contracts (`TechStrap.Contracts.Kb`): `KbArticleStatuses`, `KbLimits`, `KbArticleDto`, `KbArticleListItemDto`, `CreateKbArticleRequest`, `UpdateKbArticleRequest`, `ListKbArticlesRequest`, `KbPreviewRequest`, `KbPreviewResponse`, `KbImageUploadResponse`, `KbCategoryDto`, `CreateKbCategoryRequest`, `UpdateKbCategoryRequest`, `PublicKbSearchResultDto`, `PublishedKbArticleDto`, `PublicKbCategoryDto`, `KbSitemapEntryDto` (exact shapes are in the code below).
  - Tests: `KbTestData.SharedCategoryId` and `KbTestData.EnsureSharedCategoryAsync(host)` for any test that publishes an article (publishing needs a category).
  - Docs: decision `D-044` (date 2026-10-05) with the owner decisions, the defaults and the technical decisions of this phase. Tasks 2 to 11 cite it.

- [ ] **Step 1: Write the failing tests**

Nine test files change or arrive. They cover the lifecycle (`KbArticleTests`), the category description, the reserved slug and the version (`KbArticleTests`, `KbScopeRuleTests`), the cross-scope slug rule (`KbScopeRuleTests`), the Contracts constants staying equal to the Domain facts (`KbContractParityTests`) and D-044 and the schema doc (`RepositoryDocs.Tests.ps1`). Publishing now needs a category, so every existing test that publishes a category-less article gets a category: the new `KbTestData` helper adds one shared category per database, and the Integration helpers (`Article(...)`, `AddAsync(...)`, `SaveAsync(...)`) use it. `TicketLifecycleEndToEndTests` creates its own category. A few existing tests that assumed the old `KbCategory.Update(name, sortOrder)` change with the new signature.

Modify `scripts/tests/RepositoryDocs.Tests.ps1` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -78,6 +78,29 @@ Describe 'D-043 (scoped configuration and one image-only deploy compose)' {
     }
 }
 
+Describe 'D-044 (the knowledge base)' {
+    BeforeAll { $script:Log = Get-RepoText 'docs/architecture/04-DECISION-LOG.md' }
+
+    It 'is in the decision log with its date, its status, a header bullet and an index row' {
+        $script:Log | Should -Match '(?m)^## D-044: PHASE-08: the knowledge base'
+        $script:Log | Should -Match '(?s)## D-044:.*?- \*\*Status:\*\* Approved \(owner 2026-10-05.*?- \*\*Date:\*\* 2026-10-05'
+        $script:Log | Should -Match '(?m)^\| D-044 \|.*\| 2026-10-05 \|'
+        $script:Log | Should -Match '(?m)^- \*\*Owner decision \(2026-10-05, PHASE-08 planning\):\*\* D-044'
+    }
+
+    It 'records the owner decisions and the technical decisions the API tasks rely on' {
+        foreach ($phrase in 'Slug uniqueness is blocked across scopes', 'TECHSTRAP_API_PUBLIC_URL', 'No audit', 'kb-publish-incomplete', 'IKbContentRenderer', 'api/public/kb/{productKey}/search', 'No render cache') {
+            $script:Log | Should -Match ([regex]::Escape($phrase))
+        }
+    }
+
+    It 'documents the category description and version in the schema doc' {
+        $schema = Get-RepoText 'docs/architecture/05-SCHEMA.md'
+        $schema | Should -Match '(?s)kb_categories \{.*?text description "nullable".*?xid xmin "concurrency token".*?\}'
+        $schema | Should -Match '\| `AddKbCategoryVersionAndDescription` \|'
+    }
+}
+
 Describe 'the deployment runbook' {
     BeforeAll { $script:Runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md' }
 
```

Modify `tests/TechStrap.Api.Tests/Tickets/TicketLifecycleEndToEndTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -81,12 +81,14 @@ public sealed class TicketLifecycleEndToEndTests(TestPostgres postgres) : IDispo
             var provider = scope.ServiceProvider;
             var clock = provider.GetRequiredService<TimeProvider>();
             var tag = Tag.Create("billing", "Billing", "#DC2626", clock).Value;
-            var article = KbArticle.Create(null, null, "reset-password", "Reset your password", null, "Steps.", me.Id, clock).Value;
+            var category = KbCategory.Create(null, "general", "General", 1, clock).Value;
+            var article = KbArticle.Create(null, category.Id, "reset-password", "Reset your password", null, "Steps.", me.Id, clock).Value;
             article.Publish(clock).IsSuccess.ShouldBeTrue();
             tagId = tag.Id;
             articleId = article.Id;
             await using var work = await provider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
             provider.GetRequiredService<ITagRepository>().Add(tag);
+            provider.GetRequiredService<IKbRepository>().AddCategory(category);
             provider.GetRequiredService<IKbRepository>().AddArticle(article);
             (await work.CommitAsync(Ct)).IsSuccess.ShouldBeTrue();
         }
```

Create `tests/TechStrap.Application.Tests/Knowledge/KbContractParityTests.cs`:

```csharp
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Rules;

namespace TechStrap.Application.Tests.Knowledge;

/// <summary>Contracts is dependency-free, so it repeats a few Domain facts as constants. These tests keep the copies equal.</summary>
public sealed class KbContractParityTests
{
    [Fact]
    public void Status_names_match_the_domain_enum()
    {
        var names = typeof(KbArticleStatuses).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToArray();

        names.ShouldBe(Enum.GetNames<KbArticleStatus>(), ignoreOrder: true);
    }

    [Fact]
    public void The_reserved_category_slug_and_the_preview_limit_match_the_domain()
    {
        KbLimits.ReservedCategorySlug.ShouldBe(DomainLimits.KbReservedCategorySlug);
        KbLimits.MaxPreviewChars.ShouldBe(DomainLimits.KbBodyMaxLength);
        KbLimits.MaxSearchTextChars.ShouldBe(DomainLimits.SearchTextMaxLength);
    }
}
```

Modify `tests/TechStrap.Domain.Tests/Knowledge/KbArticleTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -8,8 +8,10 @@ public sealed class KbArticleTests
     private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
     private readonly Guid _author = Guid.NewGuid();
 
-    private KbArticle Draft(Guid? productId = null) =>
-        KbArticle.Create(productId, null, "reset-password", "Reset your password", "Short summary", "# Steps\n1. Click reset", _author, _clock).Value;
+    private readonly Guid _category = Guid.NewGuid();
+
+    private KbArticle Draft(Guid? productId = null, bool withCategory = true) =>
+        KbArticle.Create(productId, withCategory ? _category : null, "reset-password", "Reset your password", "Short summary", "# Steps\n1. Click reset", _author, _clock).Value;
 
     [Fact]
     public void A_new_article_is_a_draft_with_no_publish_time()
@@ -86,16 +88,85 @@ public sealed class KbArticleTests
     }
 
     [Fact]
-    public void An_archived_article_can_be_published_again_but_not_edited()
+    public void An_archived_article_can_be_published_again()
     {
         var article = Draft();
         article.Archive(_clock);
 
-        article.Update(null, "New", null, "body", _clock).Error!.Code.ShouldBe("article-archived");
         article.Publish(_clock).IsSuccess.ShouldBeTrue();
         article.Status.ShouldBe(KbArticleStatus.Published);
     }
 
+    [Fact]
+    public void Editing_an_archived_article_returns_it_to_draft_and_keeps_the_first_publish_time()
+    {
+        var article = Draft();
+        article.Publish(_clock);
+        var firstPublished = article.PublishedAt;
+        article.Archive(_clock);
+        _clock.Advance(TimeSpan.FromHours(2));
+
+        article.Update(_category, "New title", null, "new body", _clock).IsSuccess.ShouldBeTrue();
+
+        article.Status.ShouldBe(KbArticleStatus.Draft);
+        article.PublishedAt.ShouldBe(firstPublished);
+        article.Title.ShouldBe("New title");
+        article.UpdatedAt.ShouldBe(_clock.GetUtcNow());
+    }
+
+    [Fact]
+    public void A_failed_edit_leaves_an_archived_article_archived()
+    {
+        var article = Draft();
+        article.Archive(_clock);
+
+        article.Update(_category, " ", null, "body", _clock).Error!.Code.ShouldBe("title-required");
+
+        article.Status.ShouldBe(KbArticleStatus.Archived);
+    }
+
+    [Fact]
+    public void Editing_a_published_article_keeps_it_published()
+    {
+        var article = Draft();
+        article.Publish(_clock);
+
+        article.Update(_category, "Live edit", null, "body", _clock).IsSuccess.ShouldBeTrue();
+
+        article.Status.ShouldBe(KbArticleStatus.Published);
+    }
+
+    [Fact]
+    public void Publishing_without_a_category_is_a_validation_error_on_category_and_changes_nothing()
+    {
+        var article = Draft(withCategory: false);
+
+        var result = article.Publish(_clock);
+
+        result.Error!.ShouldSatisfyAllConditions(
+            error => error.Kind.ShouldBe(DomainErrorKind.Validation),
+            error => error.Code.ShouldBe("kb-publish-incomplete"),
+            error => error.Target.ShouldBe("category"));
+        article.Status.ShouldBe(KbArticleStatus.Draft);
+        article.PublishedAt.ShouldBeNull();
+    }
+
+    [Theory]
+    [InlineData("title")]
+    [InlineData("slug")]
+    [InlineData("body")]
+    public void Publishing_a_restored_article_that_lacks_a_required_field_names_that_field(string missing)
+    {
+        var article = KbArticle.Restore(
+            Guid.NewGuid(), null, _category, missing == "slug" ? "" : "a-slug", missing == "title" ? " " : "T", null, missing == "body" ? "" : "b",
+            KbArticleStatus.Draft, _author, _clock.GetUtcNow(), _clock.GetUtcNow(), null, 1);
+
+        var result = article.Publish(_clock);
+
+        result.Error!.Code.ShouldBe("kb-publish-incomplete");
+        result.Error.Target.ShouldBe(missing);
+    }
+
     [Fact]
     public void Editing_changes_content_and_stamps_updated_at_but_never_the_slug()
     {
@@ -119,11 +190,34 @@ public sealed class KbArticleTests
         category.IsShared.ShouldBeTrue();
         category.SortOrder.ShouldBe(10);
         KbCategory.Create(null, "Getting Started", "x", 0, _clock).Error!.Code.ShouldBe("slug-invalid");
-        category.Update("Start here", 5).IsSuccess.ShouldBeTrue();
+        category.Update("Start here", "  Where to begin  ", 5).IsSuccess.ShouldBeTrue();
         category.Name.ShouldBe("Start here");
+        category.Description.ShouldBe("Where to begin");
         category.Slug.ShouldBe("getting-started");
     }
 
+    [Fact]
+    public void The_category_slug_search_is_reserved_in_any_scope()
+    {
+        KbCategory.Create(null, "search", "Search", 0, _clock).Error!.ShouldSatisfyAllConditions(
+            error => error.Code.ShouldBe("kb-category-reserved-slug"),
+            error => error.Kind.ShouldBe(DomainErrorKind.Validation),
+            error => error.Target.ShouldBe("slug"));
+        KbCategory.Create(Guid.NewGuid(), "search", "Search", 0, _clock).Error!.Code.ShouldBe("kb-category-reserved-slug");
+        KbCategory.Create(null, "searching", "Searching", 0, _clock).IsSuccess.ShouldBeTrue();
+    }
+
+    [Fact]
+    public void A_category_description_is_optional_and_limited()
+    {
+        KbCategory.Create(null, "faq", "FAQ", 0, _clock).Value.Description.ShouldBeNull();
+        KbCategory.Create(null, "faq", "FAQ", 0, _clock, "Common questions").Value.Description.ShouldBe("Common questions");
+        KbCategory.Create(null, "faq", "FAQ", 0, _clock, new string('x', 301)).Error!.Code.ShouldBe("description-too-long");
+        var category = KbCategory.Create(null, "faq", "FAQ", 0, _clock).Value;
+        category.Update("FAQ", new string('x', 301), 0).Error!.Code.ShouldBe("description-too-long");
+        category.Description.ShouldBeNull();
+    }
+
     [Fact]
     public void A_ticket_article_link_is_a_value_of_ticket_message_and_article()
     {
@@ -152,7 +246,7 @@ public sealed class KbArticleTests
     public void Stored_times_are_whole_microseconds()
     {
         var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero).AddTicks(3));
-        var article = KbArticle.Create(null, null, "a-slug", "T", null, "b", _author, clock).Value;
+        var article = KbArticle.Create(null, _category, "a-slug", "T", null, "b", _author, clock).Value;
         (article.CreatedAt.Ticks % 10).ShouldBe(0);
         (article.UpdatedAt.Ticks % 10).ShouldBe(0);
 
```

Modify `tests/TechStrap.Infrastructure.IntegrationTests/KbRepositoryGuardTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -13,10 +13,12 @@ public sealed class KbRepositoryGuardTests(PostgresFixture postgres) : PostgresI
     private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
 
     private static KbArticle Article(TicketScenario scenario, string slug, string title, Guid? productId, string body = "body") =>
-        KbArticle.Create(productId, null, slug, title, "summary", body, scenario.Agent.Id, scenario.Host.Clock).Value;
+        KbArticle.Create(productId, KbTestData.SharedCategoryId, slug, title, "summary", body, scenario.Agent.Id, scenario.Host.Clock).Value;
 
-    private static Task<Result> AddAsync(TicketScenario scenario, params KbArticle[] articles) =>
-        scenario.Host.CommitAsync(sp =>
+    private static async Task<Result> AddAsync(TicketScenario scenario, params KbArticle[] articles)
+    {
+        await KbTestData.EnsureSharedCategoryAsync(scenario.Host);
+        return await scenario.Host.CommitAsync(sp =>
         {
             foreach (var article in articles)
             {
@@ -25,6 +27,7 @@ public sealed class KbRepositoryGuardTests(PostgresFixture postgres) : PostgresI
 
             return Task.CompletedTask;
         });
+    }
 
     private static Task<Result> UpdateAsync(PersistenceTestHost host, Guid id, Action<KbArticle> change) =>
         host.CommitAsync(async sp =>
```

Modify `tests/TechStrap.Infrastructure.IntegrationTests/KbRepositoryTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -12,10 +12,12 @@ public sealed class KbRepositoryTests(PostgresFixture postgres) : PostgresIntegr
     private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
 
     private static KbArticle Article(TicketScenario scenario, string slug, string title, Guid? productId, Guid? categoryId = null) =>
-        KbArticle.Create(productId, categoryId, slug, title, "summary", "# body", scenario.Agent.Id, scenario.Host.Clock).Value;
+        KbArticle.Create(productId, categoryId ?? KbTestData.SharedCategoryId, slug, title, "summary", "# body", scenario.Agent.Id, scenario.Host.Clock).Value;
 
-    private static Task<Result> AddAsync(TicketScenario scenario, params KbArticle[] articles) =>
-        scenario.Host.CommitAsync(sp =>
+    private static async Task<Result> AddAsync(TicketScenario scenario, params KbArticle[] articles)
+    {
+        await KbTestData.EnsureSharedCategoryAsync(scenario.Host);
+        return await scenario.Host.CommitAsync(sp =>
         {
             var kb = sp.GetRequiredService<IKbRepository>();
             foreach (var article in articles)
@@ -25,6 +27,7 @@ public sealed class KbRepositoryTests(PostgresFixture postgres) : PostgresIntegr
 
             return Task.CompletedTask;
         });
+    }
 
     [Fact]
     public async Task An_article_round_trips_and_is_found_by_id_and_by_slug_in_its_own_product_or_the_shared_space()
@@ -61,7 +64,7 @@ public sealed class KbRepositoryTests(PostgresFixture postgres) : PostgresIntegr
         {
             var kb = sp.GetRequiredService<IKbRepository>();
             var loaded = (await kb.GetArticleAsync(article.Id, Ct))!;
-            loaded.Update(null, "FAQ v2", null, "new body", host.Clock);
+            loaded.Update(KbTestData.SharedCategoryId, "FAQ v2", null, "new body", host.Clock);
             loaded.Publish(host.Clock);
             kb.UpdateArticle(loaded);
         });
@@ -159,7 +162,7 @@ public sealed class KbRepositoryTests(PostgresFixture postgres) : PostgresIntegr
         {
             var kb = sp.GetRequiredService<IKbRepository>();
             var loaded = (await kb.GetCategoryAsync(late.Id, Ct))!;
-            loaded.Update("Later", 1);
+            loaded.Update("Later", null, 1);
             kb.UpdateCategory(loaded);
         });
 
@@ -202,7 +205,7 @@ public sealed class KbRepositoryTests(PostgresFixture postgres) : PostgresIntegr
 
         blocked.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ReferenceViolation);
         removed.IsSuccess.ShouldBeTrue();
-        (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListCategoriesAsync(null, true, Ct))).ShouldHaveSingleItem().Slug.ShouldBe("used");
+        (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ListCategoriesAsync(null, true, Ct))).Select(c => c.Slug).ShouldBe([KbTestData.SharedCategorySlug, "used"]);
     }
 
     [Fact]
```

Create `tests/TechStrap.Infrastructure.IntegrationTests/KbScopeRuleTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>D-044: slugs are unique across scopes, and a category carries a description and a concurrency version.</summary>
public sealed class KbScopeRuleTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static KbArticle Article(TicketScenario scenario, string slug, Guid? productId, KbArticleStatus status = KbArticleStatus.Draft)
    {
        var article = KbArticle.Create(productId, KbTestData.SharedCategoryId, slug, "Title", null, "body", scenario.Agent.Id, scenario.Host.Clock).Value;
        if (status == KbArticleStatus.Archived)
        {
            article.Archive(scenario.Host.Clock);
        }

        return article;
    }

    private static async Task AddAsync(TicketScenario scenario, params KbArticle[] articles)
    {
        await KbTestData.EnsureSharedCategoryAsync(scenario.Host);
        (await scenario.Host.CommitAsync(sp =>
        {
            foreach (var article in articles)
            {
                sp.GetRequiredService<IKbRepository>().AddArticle(article);
            }

            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
    }

    private static Task<bool> TakenAsync(TicketScenario scenario, Guid? productId, string slug) =>
        scenario.Host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().ArticleSlugTakenAsync(productId, slug, Ct));

    [Fact]
    public async Task A_product_article_cannot_reuse_a_shared_slug_but_another_products_slug_is_free()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await AddAsync(scenario, Article(scenario, "welcome", null), Article(scenario, "acme-only", scenario.Acme.Id));

        (await TakenAsync(scenario, scenario.Orbitly.Id, "welcome")).ShouldBeTrue();
        (await TakenAsync(scenario, scenario.Acme.Id, "welcome")).ShouldBeTrue();
        (await TakenAsync(scenario, scenario.Acme.Id, "acme-only")).ShouldBeTrue();
        (await TakenAsync(scenario, scenario.Orbitly.Id, "acme-only")).ShouldBeFalse();
        (await TakenAsync(scenario, scenario.Orbitly.Id, "unused")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_shared_article_cannot_reuse_any_products_slug()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await AddAsync(scenario, Article(scenario, "orbitly-guide", scenario.Orbitly.Id));

        (await TakenAsync(scenario, null, "orbitly-guide")).ShouldBeTrue();
        (await TakenAsync(scenario, null, "unused")).ShouldBeFalse();
    }

    [Fact]
    public async Task An_archived_article_still_holds_its_slug()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        await AddAsync(scenario, Article(scenario, "old", scenario.Acme.Id, KbArticleStatus.Archived));

        (await TakenAsync(scenario, scenario.Acme.Id, "old")).ShouldBeTrue();
        (await TakenAsync(scenario, null, "old")).ShouldBeTrue();
    }

    [Fact]
    public async Task Category_slugs_follow_the_same_cross_scope_rule()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        (await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddCategory(KbCategory.Create(null, "shared-cat", "Shared", 1, host.Clock).Value);
            kb.AddCategory(KbCategory.Create(scenario.Acme.Id, "acme-cat", "Acme", 2, host.Clock).Value);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        Task<bool> Taken(Guid? productId, string slug) =>
            host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().CategorySlugTakenAsync(productId, slug, Ct));

        (await Taken(scenario.Orbitly.Id, "shared-cat")).ShouldBeTrue();
        (await Taken(null, "acme-cat")).ShouldBeTrue();
        (await Taken(scenario.Acme.Id, "acme-cat")).ShouldBeTrue();
        (await Taken(scenario.Orbitly.Id, "acme-cat")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_category_description_round_trips_and_two_stale_category_edits_give_one_conflict()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(scenario.Acme.Id, "faq", "FAQ", 1, host.Clock, "Common questions").Value;
        (await host.CommitAsync(sp => { sp.GetRequiredService<IKbRepository>().AddCategory(category); return Task.CompletedTask; })).IsSuccess.ShouldBeTrue();
        await using var first = host.CreateScope();
        await using var second = host.CreateScope();
        var firstKb = first.ServiceProvider.GetRequiredService<IKbRepository>();
        var secondKb = second.ServiceProvider.GetRequiredService<IKbRepository>();
        var byFirst = (await firstKb.GetCategoryAsync(category.Id, Ct))!;
        var bySecond = (await secondKb.GetCategoryAsync(category.Id, Ct))!;
        byFirst.Description.ShouldBe("Common questions");
        byFirst.Version.ShouldBeGreaterThan(0u);
        byFirst.Update("First", null, 1);
        bySecond.Update("Second", null, 1);

        await using var firstWork = await first.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        firstKb.UpdateCategory(byFirst);
        var firstResult = await firstWork.CommitAsync(Ct);
        await using var secondWork = await second.ServiceProvider.GetRequiredService<IUnitOfWork>().BeginAsync(Ct);
        secondKb.UpdateCategory(bySecond);
        var secondResult = await secondWork.CommitAsync(Ct);

        firstResult.IsSuccess.ShouldBeTrue();
        secondResult.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        var stored = (await host.ReadAsync(sp => sp.GetRequiredService<IKbRepository>().GetCategoryAsync(category.Id, Ct)))!;
        stored.Name.ShouldBe("First");
        stored.Description.ShouldBeNull();
    }
}
```

Modify `tests/TechStrap.Infrastructure.IntegrationTests/KbSearchTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -12,7 +12,7 @@ public sealed class KbSearchTests(PostgresFixture postgres) : PostgresIntegratio
 
     private static KbArticle Article(TicketScenario scenario, string slug, string title, string summary, string body, Guid? productId, bool publish = true)
     {
-        var article = KbArticle.Create(productId, null, slug, title, summary, body, scenario.Agent.Id, scenario.Host.Clock).Value;
+        var article = KbArticle.Create(productId, KbTestData.SharedCategoryId, slug, title, summary, body, scenario.Agent.Id, scenario.Host.Clock).Value;
         if (publish)
         {
             article.Publish(scenario.Host.Clock);
@@ -23,6 +23,7 @@ public sealed class KbSearchTests(PostgresFixture postgres) : PostgresIntegratio
 
     private static async Task SaveAsync(TicketScenario scenario, params KbArticle[] articles)
     {
+        await KbTestData.EnsureSharedCategoryAsync(scenario.Host);
         var result = await scenario.Host.CommitAsync(sp =>
         {
             foreach (var article in articles)
```

Create `tests/TechStrap.Infrastructure.IntegrationTests/KbTestData.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// A shared category for KB tests that publish articles: publishing needs a category (D-044), and the foreign key needs the row to exist.
/// </summary>
internal static class KbTestData
{
    public static readonly Guid SharedCategoryId = Guid.Parse("0199a000-0000-7000-8000-00000000c001");

    public const string SharedCategorySlug = "kb-test-general";

    /// <summary>Stages the shared category once per database; calling it again is harmless.</summary>
    public static async Task EnsureSharedCategoryAsync(PersistenceTestHost host)
    {
        await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            if (await kb.GetCategoryAsync(SharedCategoryId, TestContext.Current.CancellationToken) is null)
            {
                kb.AddCategory(KbCategory.Restore(SharedCategoryId, null, "General", SharedCategorySlug, null, 0, 0));
            }
        });
    }
}
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: the build FAILS with errors such as `CS0234` (`The type or namespace name 'Kb' does not exist in the namespace 'TechStrap.Contracts'`), `CS1061` (`'KbCategory' does not contain a definition for 'Description'` and for `'Version'`; `'IKbRepository' does not contain a definition for 'ArticleSlugTakenAsync'` and for `CategorySlugTakenAsync`) and `CS1501` (`No overload for method 'Update' takes 3 arguments`, `'Create' takes 6 arguments`, `'Restore' takes 7 arguments`).

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1`
Expected: FAIL, 3 failed (`is in the decision log with its date, its status, a header bullet and an index row`, `records the owner decisions and the technical decisions the API tasks rely on`, `documents the category description and version in the schema doc`) and 10 passed.

- [ ] **Step 3: Save the plan, then write D-044 and the schema documentation**

Save this plan as `docs/superpowers/plans/2026-10-05-phase-08-knowledge-base.md` (the decision links to it). Then append the D-044 section to the decision log (after the D-043 section, separated by a `---` line), add the header bullet and the index row, and update the schema document: the `kb_categories` entity gains `description` and `xmin`, and the migration table gains a row. Apply the hunks below. The D-044 text is the decision of record: later tasks and the Admin plan cite it by name.

Modify `docs/architecture/04-DECISION-LOG.md` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -17,6 +17,7 @@ Approval basis:
 - **Owner decision (2026-10-04, PHASE-07b planning):** D-041, the owner decisions on read-only roles in the Admin, the logo URL field, the tag ticket count and the single PR. Its technical decisions were proposed in the PHASE-07b plan and approved when the owner approved the plan.
 - **Owner decision (2026-10-04, PHASE-07c planning):** D-042, the owner decisions on the CSP, the theme flash, the time zone source, the shared host wiring and the single PR. Its defaults were proposed in the PHASE-07c plan and approved when the owner approved the plan.
 - **Owner decision (2026-10-05, scoped configuration and deployment, before PHASE-08):** D-043, the owner decisions on one image-only deploy compose, scoped env files on the host, a separate Postgres and explicit image tags. Its technical decisions were proposed in the plan and approved when the owner approved the plan.
+- **Owner decision (2026-10-05, PHASE-08 planning):** D-044, the owner decisions on slugs across scopes, the image URL setting, no KB audit and the single PR. Its defaults and technical decisions were proposed in the PHASE-08 plan and approved when the owner approved the plan.
 - **Owner decision (2026-10-02, PHASE-02):** D-023 (visual direction) was chosen by the owner after reviewing the mockups.
 - **Owner decision (2026-10-02, after UX briefs):** D-024 (customer-facing identity, spam recovery, portal prefill) settled the open owner questions in UX-BRIEF-admin (Q11) and UX-BRIEF-portal.
 
@@ -69,6 +70,7 @@ Approval basis:
 | D-041 | PHASE-07b: roles are read-only in the Admin; the product logo is a validated URL; the tag list shows ticket counts; Admin guard, browser preferences and client decisions | Approved (owner 2026-10-04; technical decisions at PHASE-07b plan review) | 2026-10-04 | PHASE-07, PHASE-08, PHASE-12 |
 | D-042 | PHASE-07c: CSP with `style-src-attr`, theme init script, browser time zone, shared host wiring, command palette, responsive rail, session-expired banner, Sentry search scrub, OpenAPI security schemes | Approved (owner 2026-10-04; defaults at PHASE-07c plan review) | 2026-10-04 | PHASE-07, PHASE-09, PHASE-11, PHASE-12 |
 | D-043 | Scoped per-project configuration (every key in `appsettings.json`, a `.env.example` and a deploy env template per host) and one image-only deploy compose for UAT and production, with a separate Postgres | Approved (owner 2026-10-05; technical decisions at plan review) | 2026-10-05 | PHASE-12, PHASE-08, PHASE-09 |
+| D-044 | PHASE-08: slugs unique across scopes, a separate KB Markdown profile, image URLs from `TECHSTRAP_API_PUBLIC_URL`, 400 outcomes for publish and image errors, public routes with the product key, reply-link validation and email links | Approved (owner 2026-10-05; defaults at PHASE-08 plan review) | 2026-10-05 | PHASE-08, PHASE-09, PHASE-12 |
 
 ---
 
@@ -1555,3 +1557,66 @@ PHASE-07 is merged. Before PHASE-08 (the knowledge base) the owner wants TechStr
 ### Approval
 - **Approved by:** Jon Seeley (owner, scoped configuration and deploy compose planning)
 - **Approved on:** 2026-10-05
+
+---
+
+## D-044: PHASE-08: the knowledge base (scope, slugs, Markdown profile, images, public API, reply links)
+
+- **Status:** Approved (owner 2026-10-05; defaults at PHASE-08 plan review)
+- **Date:** 2026-10-05
+- **Owner:** Jon Seeley
+- **Related artifacts:** D-011, D-014, D-017, D-021, D-026, D-027, D-035, D-043, `docs/architecture/PHASE-08-knowledge-base.md`, `docs/architecture/UX-BRIEF-admin.md`, `docs/architecture/PHASE-09-public-portal.md`, `docs/superpowers/plans/2026-10-05-phase-08-knowledge-base.md`
+
+### Context
+PHASE-03 already delivered the KB tables, the unique `(product_id, slug)` indexes, the weighted `search_vector` and the dev seed, and PHASE-06a delivered `IMarkdownRenderer` and `IHtmlSanitizer`. Reading the code before PHASE-08 found these gaps:
+- **Domain.** An Archived article could not be edited, `Publish` validated nothing, and `KbCategory` had no concurrency version.
+- **Markdown.** The sanitizer allow-list had no `img` or table tags, and Markdig had no pipe tables. The same sanitizer renders agent replies that customers read in email and in the portal.
+- **API.** There were no KB handlers or controllers, no image store, no public image route, no setting for the API's own public address, no `ts_headline` snippet and no cache headers.
+- **Reply links.** The reply handler only checked that a linked article exists, and the customer email carried no article links.
+- **Slugs.** The database lets a product article and a shared article use the same slug, so `/p/{key}/kb/{category}/{slug}` could be ambiguous.
+
+### Decision
+**Owner decisions (2026-10-05)**
+- **Slug uniqueness is blocked across scopes.** A product article cannot reuse a shared article's slug, and a shared article cannot reuse any product's slug. It is one extra repository check on create; there is no index change.
+- **Image URLs.** A new setting `TECHSTRAP_API_PUBLIC_URL`; images live at `https://<api public host>/kb-images/<key>`. It is blank in Development (the request's own origin is used) and required in Production. The owner's reverse proxy exposes `/kb-images/` publicly.
+- **No audit.** KB changes stay out of the audit log in this phase; articles carry an author and timestamps.
+- **Delivery.** One PR.
+
+**Defaults (proposed in the plan; approved when the owner approves it)**
+- **Scope model.** An article with a null `ProductId` is shared. `ProductId` and `Slug` are immutable after create. Categories are single-level and either product-scoped or shared; a shared article may use only a shared category. The category slug `search` is reserved (the portal's `/p/{key}/kb/search` page).
+- **Lifecycle.** Draft, then Published, then Archived. Updating an Archived article returns it to Draft. Edits to a Published article are live immediately. There is no revision history and no hard delete.
+- **Publish validation.** Title, slug, body and category must all be set. A failure is a 400 with the code `kb-publish-incomplete` and the missing field as the target.
+- **Roles.** Agents manage articles, previews, images, and category create and update. Category delete is Admin only and is refused with a 409 while the category holds articles.
+- **Markdown.** Raw HTML stays off. The knowledge base gets its own content profile: pipe tables, `img` (http and https only, with `alt`) and the table tags. Agent replies keep the existing pipeline unchanged.
+- **Images.** 5 MB limit; png, jpeg, gif and webp only, recognised by their first bytes; SVG is rejected. The key is `kb-images/{guid}.{ext}`. There is no database row and no orphan cleanup. They are served anonymously with `nosniff`, `Content-Security-Policy: default-src 'none'; sandbox` and `Cache-Control: public, max-age=31536000, immutable`.
+- **Public API.** Anonymous, with the existing `Public` rate limiter. `Cache-Control: public, max-age=60` on a hit and `no-store` on a 404; the sitemap uses `max-age=300`. Search uses `websearch_to_tsquery` and `ts_rank`, page size 10, at most 25. The snippet comes from `ts_headline` over the summary and is plain text (the consumer encodes it). An unknown or inactive product returns an empty result. An article lookup checks the product's own scope first, then the shared one. The DTOs carry no author and no ids of unpublished items.
+- **Reply linking.** At most 10 links. Each article must be Published and either shared or in the ticket's product. The customer email lists links of the form `{PortalPublicUrl}/p/{key}/kb/{category}/{slug}`. The Admin picker offers Published articles only.
+- **Admin.** A rail link "Knowledge base" for every agent and the palette command `go-kb`. Routes `/kb`, `/kb/new`, `/kb/{id}` and `/kb/categories`. A toolbar with bold, italic, link, list, code and image. A live preview with a 300 ms debounce through one sanitised preview pane, which is the one new `MarkupString` site. Image upload uses the file picker and appends the snippet (no caret insertion, paste or drag-and-drop yet). `NavigationLock` guards unsaved changes, a 409 keeps the draft and shows a banner, and `ConfirmDialog` guards archive and category delete.
+- **Config (D-043).** `TECHSTRAP_API_PUBLIC_URL` goes through the four D-043 edits for the Api. An optional Admin setting `TECHSTRAP_PORTAL_PUBLIC_URL`, blank by default, powers a "View on portal" link that hides when it is unset.
+- **Seed.** One Paperplane category and one published Paperplane article.
+
+**Technical decisions (proposed in the plan; approved when the owner approves it)**
+- **A separate KB content profile, not a wider shared sanitizer.** `IKbContentRenderer` (Markdig with pipe tables, then a KB sanitizer) serves the preview and the public article. `IMarkdownRenderer` and `IHtmlSanitizer` are untouched, so a tracking image or a table in an agent reply is still removed. A corpus test pins that the message pipeline output is unchanged. Both profiles keep raw HTML off.
+- **No `422`, `415` or `413` from a handler.** `Result` has no such kinds, so an incomplete publish, a wrong image type and an oversize image inside the request limit are 400 with their own codes (`kb-publish-incomplete`, `kb-image-type-not-allowed`, `kb-image-too-large`). A body beyond the request limit is still stopped by Kestrel as 413.
+- **Cross-scope slugs for categories too.** The same rule applies to category slugs (`kb-category-slug-taken`), so a category address is never ambiguous. The `search` slug is `kb-category-reserved-slug`.
+- **A category gets a description and a version.** `kb_categories` gains a nullable `description` (300 characters) and the `xmin` concurrency token, in one generated migration. An update with a stale version is a 409 `concurrency-conflict`.
+- **Public routes carry the product key.** `GET api/public/kb/{productKey}/search`, `/categories`, `/articles/{categorySlug}/{slug}` and `/sitemap`, plus `GET kb-images/{key}` at the root of the Api. The article route carries the category slug because the portal URL does.
+- **Archiving hides, editing reopens.** Archive keeps the row and its first publication date. Publishing an Archived article goes straight to Published, subject to the publish validation.
+- **No render cache.** The article is rendered on each read; a cache is added only if a measurement asks for one.
+
+### Alternatives Considered
+- **Widen the shared sanitizer.** Rejected: agent replies reach customers by email and in the portal, so a widened list would let a reply carry a tracking image or a table.
+- **Per-scope slugs with the product winning at read time.** Rejected by the owner: a shared article could be hidden silently.
+- **Relative `/kb-images/<key>` served on the portal origin.** Rejected by the owner: it needs a proxy route or a new Portal adapter that PHASE-09 does not plan.
+- **Audit publish, archive and category changes.** Rejected for this phase (owner).
+- **Return `422` for an incomplete publish.** Rejected: `Result` cannot produce it, and the Admin already maps 400 to field errors.
+
+### Consequences
+- **The API needs one new setting in Production.** `TECHSTRAP_API_PUBLIC_URL` is required; the Api refuses to start without it. The owner must route `/kb-images/` through the reverse proxy.
+- **Existing tests that published a category-less article now need a category.** Publishing validates it.
+- **A search snippet is encoded text.** The portal must show it as markup that is already safe, not encode it again.
+- **Still open (owner):** the reverse proxy route for `/kb-images/`, and checking an uploaded image loads from the API public URL.
+
+### Approval
+- **Approved by:** Jon Seeley (owner, PHASE-08 planning)
+- **Approved on:** 2026-10-05
```

Modify `docs/architecture/05-SCHEMA.md` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -111,6 +111,8 @@ erDiagram
         uuid id PK
         uuid product_id FK "nullable"
         text slug "unique with product, nulls equal"
+        text description "nullable"
+        xid xmin "concurrency token"
     }
     kb_articles {
         uuid id PK
@@ -163,5 +165,6 @@ erDiagram
 | `AddSearchVectors` | the three generated `search_vector` columns and their GIN indexes |
 | `AddRequesterConcurrencyToken` | no DDL (Npgsql treats `xmin` as a system column); records the `requesters` concurrency token in the model |
 | `AddOutboxClaimIndexes` | two partial indexes on `email_outbox` (Sending by `locked_until`, DeadLettered by `created_at`); drops `ix_email_outbox_status` |
+| `AddKbCategoryVersionAndDescription` | `kb_categories.description` (nullable, 300 characters) and the `kb_categories` `xmin` concurrency token (the token is model-only in a fresh database, as for `requesters`) |
 
 All migrations are generated by `dotnet ef migrations add`; none is edited by hand.
```

- [ ] **Step 4: Change the Domain**

The article lifecycle changes in `KbArticle`: an edit reopens an Archived article as a Draft and `Publish` validates. `KbCategory` gains a description, a version and the reserved slug. `DomainLimits` holds the two new constants. `Create` keeps its five positional arguments and takes the description last, so existing callers still compile.

Modify `src/TechStrap.Domain/Knowledge/KbArticle.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -11,11 +11,15 @@ public enum KbArticleStatus
 
 /// <summary>
 /// A knowledge-base article written in Markdown. Draft goes to Published (or Archived); Published goes to Archived; Archived
-/// can be published again. An Archived article cannot be edited. The slug is unique within its product (shared articles have a
-/// null product and their own slug space).
+/// can be published again, and editing an Archived article returns it to Draft (D-044). Publishing needs a title, a slug, a body and a
+/// category, because the portal URL carries the category slug. The slug is unique within its product (shared articles have a
+/// null product and their own slug space); the handlers also block a slug that is taken in the other scope (D-044).
 /// </summary>
 public sealed class KbArticle
 {
+    /// <summary>The code of the Validation error <see cref="Publish"/> returns when a required field is missing; its target names the field (title, slug, body or category).</summary>
+    public const string PublishIncompleteCode = "kb-publish-incomplete";
+
     private KbArticle(
         Guid id,
         Guid? productId,
@@ -119,11 +123,6 @@ public sealed class KbArticle
 
     public DomainResult Update(Guid? categoryId, string? title, string? summary, string? bodyMarkdown, TimeProvider clock)
     {
-        if (Status == KbArticleStatus.Archived)
-        {
-            return DomainErrors.Conflict("article-archived", "An archived article cannot be edited.");
-        }
-
         var articleTitle = Guard.RequiredText(title, DomainLimits.KbTitleMaxLength, "title");
         var articleSummary = Guard.OptionalText(summary, DomainLimits.KbSummaryMaxLength, "summary");
         var body = Guard.RequiredText(bodyMarkdown, DomainLimits.KbBodyMaxLength, "body");
@@ -136,6 +135,12 @@ public sealed class KbArticle
         Title = articleTitle.Value;
         Summary = articleSummary.Value;
         BodyMarkdown = body.Value;
+        if (Status == KbArticleStatus.Archived)
+        {
+            // An edit reopens an archived article as a draft (D-044); the first publication date is kept.
+            Status = KbArticleStatus.Draft;
+        }
+
         UpdatedAt = DomainTime.Now(clock);
         return DomainResult.Ok();
     }
@@ -147,6 +152,11 @@ public sealed class KbArticle
             return DomainErrors.Conflict("article-already-published", "The article is already published.");
         }
 
+        if (FirstMissingForPublish() is { } missing)
+        {
+            return DomainErrors.Validation(PublishIncompleteCode, $"The article cannot be published without a {missing}.", missing);
+        }
+
         var now = DomainTime.Now(clock);
         Status = KbArticleStatus.Published;
         PublishedAt ??= now;
@@ -154,6 +164,26 @@ public sealed class KbArticle
         return DomainResult.Ok();
     }
 
+    private string? FirstMissingForPublish()
+    {
+        if (string.IsNullOrWhiteSpace(Title))
+        {
+            return "title";
+        }
+
+        if (string.IsNullOrWhiteSpace(Slug))
+        {
+            return "slug";
+        }
+
+        if (string.IsNullOrWhiteSpace(BodyMarkdown))
+        {
+            return "body";
+        }
+
+        return CategoryId is null ? "category" : null;
+    }
+
     public DomainResult Archive(TimeProvider clock)
     {
         if (Status == KbArticleStatus.Archived)
```

Modify `src/TechStrap.Domain/Knowledge/KbCategory.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -2,16 +2,21 @@ using TechStrap.Domain.Rules;
 
 namespace TechStrap.Domain.Knowledge;
 
-/// <summary>A single-level knowledge-base category, product-scoped or shared (null product).</summary>
+/// <summary>
+/// A single-level knowledge-base category, product-scoped or shared (null product). The slug and the product never change. The slug
+/// <c>search</c> is reserved for the portal's KB search page (D-044).
+/// </summary>
 public sealed class KbCategory
 {
-    private KbCategory(Guid id, Guid? productId, string name, string slug, int sortOrder)
+    private KbCategory(Guid id, Guid? productId, string name, string slug, string? description, int sortOrder, uint version)
     {
         Id = id;
         ProductId = productId;
         Name = name;
         Slug = slug;
+        Description = description;
         SortOrder = sortOrder;
+        Version = version;
     }
 
     public Guid Id { get; }
@@ -23,32 +28,48 @@ public sealed class KbCategory
 
     public string Slug { get; }
 
+    public string? Description { get; private set; }
+
     public int SortOrder { get; private set; }
 
+    /// <summary>Opaque optimistic-concurrency token as loaded (Postgres <c>xmin</c>).</summary>
+    public uint Version { get; }
+
     public bool IsShared => ProductId is null;
 
-    public static DomainResult<KbCategory> Create(Guid? productId, string? slug, string? name, int sortOrder, TimeProvider clock)
+    public static DomainResult<KbCategory> Create(Guid? productId, string? slug, string? name, int sortOrder, TimeProvider clock, string? description = null)
     {
         var categorySlug = Guard.Slug(slug, DomainLimits.KbSlugMaxLength, "slug");
         var categoryName = Guard.RequiredText(name, DomainLimits.NameMaxLength, "name");
+        var categoryDescription = Guard.OptionalText(description, DomainLimits.KbCategoryDescriptionMaxLength, "description");
+        if (Guard.FirstError(categorySlug, categoryName, categoryDescription) is { } error)
+        {
+            return error;
+        }
+
+        if (categorySlug.Value == DomainLimits.KbReservedCategorySlug)
+        {
+            return DomainErrors.Validation("kb-category-reserved-slug", "The slug \"search\" is reserved for the knowledge-base search page.", "slug");
+        }
 
-        return Guard.FirstError(categorySlug, categoryName) is { } error
-            ? error
-            : DomainResult<KbCategory>.Ok(new KbCategory(EntityId.New(clock), productId, categoryName.Value, categorySlug.Value, sortOrder));
+        return DomainResult<KbCategory>.Ok(
+            new KbCategory(EntityId.New(clock), productId, categoryName.Value, categorySlug.Value, categoryDescription.Value, sortOrder, 0));
     }
 
-    public static KbCategory Restore(Guid id, Guid? productId, string name, string slug, int sortOrder) =>
-        new(id, productId, name, slug, sortOrder);
+    public static KbCategory Restore(Guid id, Guid? productId, string name, string slug, string? description, int sortOrder, uint version) =>
+        new(id, productId, name, slug, description, sortOrder, version);
 
-    public DomainResult Update(string? name, int sortOrder)
+    public DomainResult Update(string? name, string? description, int sortOrder)
     {
         var categoryName = Guard.RequiredText(name, DomainLimits.NameMaxLength, "name");
-        if (categoryName.IsFailure)
+        var categoryDescription = Guard.OptionalText(description, DomainLimits.KbCategoryDescriptionMaxLength, "description");
+        if (Guard.FirstError(categoryName, categoryDescription) is { } error)
         {
-            return categoryName.Error!;
+            return error;
         }
 
         Name = categoryName.Value;
+        Description = categoryDescription.Value;
         SortOrder = sortOrder;
         return DomainResult.Ok();
     }
```

Modify `src/TechStrap.Domain/Rules/DomainLimits.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -25,6 +25,10 @@ public static class DomainLimits
     public const int KbTitleMaxLength = 200;
     public const int KbSummaryMaxLength = 500;
     public const int KbBodyMaxLength = 200_000;
+    public const int KbCategoryDescriptionMaxLength = 300;
+
+    /// <summary>The category slug the portal uses for KB search (<c>/p/{key}/kb/search</c>), so no category may take it (D-044).</summary>
+    public const string KbReservedCategorySlug = "search";
     public const int KeyPrefixMinLength = 4;
     public const int KeyPrefixMaxLength = 16;
     public const int HashMaxLength = 200;
```

- [ ] **Step 5: Change the persistence model, the mapping and the repository**

The record gets `Description` and `Version`; the configuration maps the description length and the `xmin` token (as for products and articles); the mapping copies both. `UpdateCategory` applies the caller's version as the original token, the same way `UpdateArticle` does, so a stale category edit conflicts at commit. The two slug methods are the repository half of the cross-scope rule: the handlers (Tasks 3 and 4) call them before staging, and the unique index still decides a race. `SharedOnly` lets the agent list ask for shared articles only.

Modify `src/TechStrap.Infrastructure/Persistence/Records/KnowledgeRecords.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -14,7 +14,12 @@ internal sealed class KbCategoryRecord
 
     public string Slug { get; set; } = string.Empty;
 
+    public string? Description { get; set; }
+
     public int SortOrder { get; set; }
+
+    /// <summary>Postgres <c>xmin</c>, the optimistic concurrency token.</summary>
+    public uint Version { get; set; }
 }
 
 internal sealed class KbArticleRecord
```

Modify `src/TechStrap.Infrastructure/Persistence/Configurations/KnowledgeRecordConfigurations.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -14,6 +14,8 @@ internal sealed class KbCategoryRecordConfiguration : IEntityTypeConfiguration<K
         builder.Property(c => c.Id).ValueGeneratedNever();
         builder.Property(c => c.Name).HasMaxLength(DomainLimits.NameMaxLength).IsRequired();
         builder.Property(c => c.Slug).HasMaxLength(DomainLimits.KbSlugMaxLength).IsRequired();
+        builder.Property(c => c.Description).HasMaxLength(DomainLimits.KbCategoryDescriptionMaxLength);
+        builder.HasXminConcurrencyToken(c => c.Version);
         builder.HasOne<ProductRecord>().WithMany().HasForeignKey(c => c.ProductId).OnDelete(DeleteBehavior.Restrict);
 
         // Shared categories (null product) share one slug space: nulls count as equal.
```

Modify `src/TechStrap.Infrastructure/Persistence/Mapping/KnowledgeMappings.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -37,7 +37,7 @@ internal static class KnowledgeMappings
     }
 
     public static KbCategory ToDomain(this KbCategoryRecord record) =>
-        KbCategory.Restore(record.Id, record.ProductId, record.Name, record.Slug, record.SortOrder);
+        KbCategory.Restore(record.Id, record.ProductId, record.Name, record.Slug, record.Description, record.SortOrder, record.Version);
 
     public static KbCategoryRecord ToRecord(this KbCategory category)
     {
@@ -49,6 +49,7 @@ internal static class KnowledgeMappings
     public static void CopyTo(this KbCategory category, KbCategoryRecord record)
     {
         record.Name = category.Name;
+        record.Description = category.Description;
         record.SortOrder = category.SortOrder;
     }
 
```

Modify `src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -24,21 +24,26 @@ internal sealed class KbRepository(TechStrapDbContext context) : IKbRepository
         (await context.Set<KbArticleRecord>().FirstOrDefaultAsync(a => a.Id == id && a.Status == KbArticleStatus.Published, cancellationToken))?.ToDomain();
 
     public Task<PagedResult<KbArticle>> ListArticlesAsync(KbArticleQuery query, CancellationToken cancellationToken) =>
-        ListAsync(query.ProductId, query.IncludeShared, query.Status, query.CategoryId, query.Page, query.PageSize, cancellationToken);
+        ListAsync(query.ProductId, query.IncludeShared, query.SharedOnly, query.Status, query.CategoryId, query.Page, query.PageSize, cancellationToken);
 
     public Task<PagedResult<KbArticle>> ListPublishedArticlesAsync(PublishedKbArticleQuery query, CancellationToken cancellationToken) =>
-        ListAsync(query.ProductId, query.IncludeShared, KbArticleStatus.Published, query.CategoryId, query.Page, query.PageSize, cancellationToken);
+        ListAsync(query.ProductId, query.IncludeShared, false, KbArticleStatus.Published, query.CategoryId, query.Page, query.PageSize, cancellationToken);
 
     public Task<PagedResult<KbArticle>> SearchAsync(KbSearchQuery query, CancellationToken cancellationToken) =>
-        SearchAsync(query.Text, query.ProductId, query.IncludeShared, query.Status, query.CategoryId, query.Page, query.PageSize, cancellationToken);
+        SearchAsync(query.Text, query.ProductId, query.IncludeShared, query.SharedOnly, query.Status, query.CategoryId, query.Page, query.PageSize, cancellationToken);
 
     public Task<PagedResult<KbArticle>> SearchPublishedAsync(PublishedKbSearchQuery query, CancellationToken cancellationToken) =>
-        SearchAsync(query.Text, query.ProductId, query.IncludeShared, KbArticleStatus.Published, query.CategoryId, query.Page, query.PageSize, cancellationToken);
+        SearchAsync(query.Text, query.ProductId, query.IncludeShared, false, KbArticleStatus.Published, query.CategoryId, query.Page, query.PageSize, cancellationToken);
 
     // One filter for the agent and customer entry points, so the two cannot drift apart. Only the public methods above choose the status.
-    private IQueryable<KbArticleRecord> Filter(IQueryable<KbArticleRecord> articles, Guid? productId, bool includeShared, KbArticleStatus? status, Guid? categoryId)
+    private IQueryable<KbArticleRecord> Filter(
+        IQueryable<KbArticleRecord> articles, Guid? productId, bool includeShared, bool sharedOnly, KbArticleStatus? status, Guid? categoryId)
     {
-        if (productId is { } product)
+        if (sharedOnly)
+        {
+            articles = articles.Where(a => a.ProductId == null);
+        }
+        else if (productId is { } product)
         {
             articles = includeShared
                 ? articles.Where(a => a.ProductId == product || a.ProductId == null)
@@ -59,12 +64,12 @@ internal sealed class KbRepository(TechStrapDbContext context) : IKbRepository
     }
 
     private async Task<PagedResult<KbArticle>> ListAsync(
-        Guid? productId, bool includeShared, KbArticleStatus? status, Guid? categoryId, int requestedPage, int requestedPageSize, CancellationToken cancellationToken)
+        Guid? productId, bool includeShared, bool sharedOnly, KbArticleStatus? status, Guid? categoryId, int requestedPage, int requestedPageSize, CancellationToken cancellationToken)
     {
         var page = Paging.NormalizePage(requestedPage);
         var pageSize = Paging.NormalizePageSize(requestedPageSize);
 
-        var articles = Filter(context.Set<KbArticleRecord>().AsNoTracking(), productId, includeShared, status, categoryId);
+        var articles = Filter(context.Set<KbArticleRecord>().AsNoTracking(), productId, includeShared, sharedOnly, status, categoryId);
         var total = await articles.CountAsync(cancellationToken);
         var records = await articles.OrderByDescending(a => a.UpdatedAt).ThenByDescending(a => a.Id)
             .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
@@ -73,7 +78,7 @@ internal sealed class KbRepository(TechStrapDbContext context) : IKbRepository
     }
 
     private async Task<PagedResult<KbArticle>> SearchAsync(
-        string? rawText, Guid? productId, bool includeShared, KbArticleStatus? status, Guid? categoryId, int requestedPage, int requestedPageSize, CancellationToken cancellationToken)
+        string? rawText, Guid? productId, bool includeShared, bool sharedOnly, KbArticleStatus? status, Guid? categoryId, int requestedPage, int requestedPageSize, CancellationToken cancellationToken)
     {
         var page = Paging.NormalizePage(requestedPage);
         var pageSize = Paging.NormalizePageSize(requestedPageSize);
@@ -86,7 +91,7 @@ internal sealed class KbRepository(TechStrapDbContext context) : IKbRepository
         var articles = Filter(
             context.Set<KbArticleRecord>().AsNoTracking()
                 .Where(a => a.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text))),
-            productId, includeShared, status, categoryId);
+            productId, includeShared, sharedOnly, status, categoryId);
         var total = await articles.CountAsync(cancellationToken);
         var records = await articles
             .OrderByDescending(a => a.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text)))
@@ -108,6 +113,16 @@ internal sealed class KbRepository(TechStrapDbContext context) : IKbRepository
         }
     }
 
+    public Task<bool> ArticleSlugTakenAsync(Guid? productId, string slug, CancellationToken cancellationToken) =>
+        productId is { } product
+            ? context.Set<KbArticleRecord>().AnyAsync(a => a.Slug == slug && (a.ProductId == product || a.ProductId == null), cancellationToken)
+            : context.Set<KbArticleRecord>().AnyAsync(a => a.Slug == slug, cancellationToken);
+
+    public Task<bool> CategorySlugTakenAsync(Guid? productId, string slug, CancellationToken cancellationToken) =>
+        productId is { } product
+            ? context.Set<KbCategoryRecord>().AnyAsync(c => c.Slug == slug && (c.ProductId == product || c.ProductId == null), cancellationToken)
+            : context.Set<KbCategoryRecord>().AnyAsync(c => c.Slug == slug, cancellationToken);
+
     public async Task<KbCategory?> GetCategoryAsync(Guid id, CancellationToken cancellationToken) =>
         (await context.Set<KbCategoryRecord>().FirstOrDefaultAsync(c => c.Id == id, cancellationToken))?.ToDomain();
 
@@ -127,7 +142,16 @@ internal sealed class KbRepository(TechStrapDbContext context) : IKbRepository
 
     public void AddCategory(KbCategory category) => context.Set<KbCategoryRecord>().Add(category.ToRecord());
 
-    public void UpdateCategory(KbCategory category) => category.CopyTo(context.FindLoaded<KbCategoryRecord>(category.Id));
+    public void UpdateCategory(KbCategory category)
+    {
+        var record = context.FindLoaded<KbCategoryRecord>(category.Id);
+        category.CopyTo(record);
+        if (context.Entry(record).State != EntityState.Added)
+        {
+            // The check is against the version the caller's Domain object carries, not the one this scope loaded (D-026).
+            context.ApplyOriginalVersion(record, category.Version);
+        }
+    }
 
     public void RemoveCategory(KbCategory category) => context.Set<KbCategoryRecord>().Remove(context.FindLoaded<KbCategoryRecord>(category.Id));
 
```

Modify `src/TechStrap.Application/Persistence/IKbRepository.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -46,6 +46,16 @@ public interface IKbRepository
     /// <summary>See the category and product TODO on <see cref="AddArticle"/>. Throws when the article was not loaded in this unit of work. After a failed commit reload the article rather than retrying the same object; after a successful commit its <c>Version</c> is stale, so reload before updating again.</summary>
     void UpdateArticle(KbArticle article);
 
+    /// <summary>
+    /// The cross-scope slug rule (D-044). With a product: true when that product or the shared space already has an article with the slug.
+    /// With no product (a shared article): true when any article of any scope has it. Any status counts. The unique index alone only
+    /// separates the scopes, so a create handler asks this first; a race still ends in a <c>duplicate</c> commit conflict.
+    /// </summary>
+    Task<bool> ArticleSlugTakenAsync(Guid? productId, string slug, CancellationToken cancellationToken);
+
+    /// <summary>The same cross-scope rule for category slugs (D-044), so a portal category address is never ambiguous.</summary>
+    Task<bool> CategorySlugTakenAsync(Guid? productId, string slug, CancellationToken cancellationToken);
+
     Task<KbCategory?> GetCategoryAsync(Guid id, CancellationToken cancellationToken);
 
     /// <summary>Ordered by sort order then name. With a product, <paramref name="includeShared"/> adds the shared categories; without one, every category is returned.</summary>
```

Modify `src/TechStrap.Application/Knowledge/KbArticleQuery.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -5,6 +5,7 @@ namespace TechStrap.Application.Knowledge;
 
 /// <summary>
 /// Filters for a KB article list, newest update first. With a product, <see cref="IncludeShared"/> also returns shared articles.
+/// <see cref="SharedOnly"/> returns only the shared articles and wins over <see cref="ProductId"/>.
 /// Implementations normalize <see cref="Page"/> and <see cref="PageSize"/> through <see cref="Paging"/> before querying.
 /// </summary>
 public sealed record KbArticleQuery(
@@ -13,4 +14,5 @@ public sealed record KbArticleQuery(
     KbArticleStatus? Status = null,
     Guid? CategoryId = null,
     int Page = 1,
-    int PageSize = Paging.DefaultPageSize);
+    int PageSize = Paging.DefaultPageSize,
+    bool SharedOnly = false);
```

Modify `src/TechStrap.Application/Knowledge/KbSearchQuery.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -6,6 +6,7 @@ namespace TechStrap.Application.Knowledge;
 /// <summary>
 /// A knowledge-base full-text search (D-011, D-027): web-search syntax over title (weight A), summary (B) and body (C), best
 /// match first. Public search passes <c>Status = Published</c>. With a product, <see cref="IncludeShared"/> also searches shared articles.
+/// <see cref="SharedOnly"/> searches only the shared articles and wins over <see cref="ProductId"/>.
 /// Implementations normalize <see cref="Page"/> and <see cref="PageSize"/> through <see cref="Paging"/> before querying.
 /// </summary>
 public sealed record KbSearchQuery(
@@ -15,4 +16,5 @@ public sealed record KbSearchQuery(
     KbArticleStatus? Status = null,
     Guid? CategoryId = null,
     int Page = 1,
-    int PageSize = Paging.DefaultPageSize);
+    int PageSize = Paging.DefaultPageSize,
+    bool SharedOnly = false);
```

- [ ] **Step 6: Generate the migration with the tool**

Never write a migration by hand and never edit the snapshot (`PendingModelChangesWarning` crashes the host at start). Generate it:

```bash
dotnet tool restore
dotnet ef migrations add AddKbCategoryVersionAndDescription --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api -c TechStrapDbContext
```

Expected: `Build succeeded.` then `Done. To undo this action, use 'ef migrations remove'`. Three files change in `src/TechStrap.Infrastructure/Migrations/`: `<timestamp>_AddKbCategoryVersionAndDescription.cs` and `.Designer.cs` are new, and `TechStrapDbContextModelSnapshot.cs` gains the two properties. The timestamp is the moment you run the command, so it differs from the one below; the content must not. Review gate: `Up` adds `description` (`character varying(300)`, nullable) and `xmin` (`xid`, `rowVersion: true`, default `0u`) to `kb_categories`, and `Down` drops both. The generated migration file begins with a byte order mark, which this plan omits; keep what the tool wrote.

Create `src/TechStrap.Infrastructure/Migrations/20261005144542_AddKbCategoryVersionAndDescription.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechStrap.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKbCategoryVersionAndDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "kb_categories",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "kb_categories",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "description",
                table: "kb_categories");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "kb_categories");
        }
    }
}
```

Modify `src/TechStrap.Infrastructure/Migrations/TechStrapDbContextModelSnapshot.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -444,6 +444,11 @@ namespace TechStrap.Infrastructure.Migrations
                         .HasColumnType("uuid")
                         .HasColumnName("id");
 
+                    b.Property<string>("Description")
+                        .HasMaxLength(300)
+                        .HasColumnType("character varying(300)")
+                        .HasColumnName("description");
+
                     b.Property<string>("Name")
                         .IsRequired()
                         .HasMaxLength(100)
@@ -464,6 +469,12 @@ namespace TechStrap.Infrastructure.Migrations
                         .HasColumnType("integer")
                         .HasColumnName("sort_order");
 
+                    b.Property<uint>("Version")
+                        .IsConcurrencyToken()
+                        .ValueGeneratedOnAddOrUpdate()
+                        .HasColumnType("xid")
+                        .HasColumnName("xmin");
+
                     b.HasKey("Id")
                         .HasName("pk_kb_categories");
 
```

- [ ] **Step 7: Add the Contracts DTOs and constants**

These are the names and shapes every later task and the Admin plan use. A null `ProductId`, `ProductKey` or `CategoryId` means shared or none. `Version` is the `xmin` token: it is sent back unchanged on an update. Contracts has no enums and no attributes, so the article status is a string from `KbArticleStatuses`.

Create `src/TechStrap.Contracts/Kb/KbNames.cs`:

```csharp
namespace TechStrap.Contracts.Kb;

/// <summary>Wire names for the KB article status. Contracts carries no enums (naming rule); handlers parse these, case-insensitive.</summary>
public static class KbArticleStatuses
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Archived = "Archived";
}

/// <summary>Knowledge-base limits and fixed names that more than one project reads (D-044).</summary>
public static class KbLimits
{
    /// <summary>The largest image an agent may upload: 5 MB.</summary>
    public const long MaxImageBytes = 5L * 1024 * 1024;

    /// <summary>The multipart field that carries the image on <c>POST api/kb/images</c>.</summary>
    public const string ImageFieldName = "file";

    /// <summary>The public path prefix of an uploaded image: <c>kb-images/{guid}.{ext}</c>.</summary>
    public const string ImagePathPrefix = "kb-images/";

    /// <summary>The longest Markdown source the preview endpoint renders. It equals the article body limit, so a body that can be saved can be previewed.</summary>
    public const int MaxPreviewChars = 200_000;

    public const int DefaultPublicSearchPageSize = 10;

    public const int MaxPublicSearchPageSize = 25;

    /// <summary>The category slug the portal reserves for its KB search page; no category may use it.</summary>
    public const string ReservedCategorySlug = "search";

    /// <summary>The most characters of a search query the public search reads; longer text is cut.</summary>
    public const int MaxSearchTextChars = 200;
}
```

Create `src/TechStrap.Contracts/Kb/KbArticleDtos.cs`:

```csharp
namespace TechStrap.Contracts.Kb;

/// <summary>
/// The agent view of one article, with the Markdown source. <paramref name="Version"/> is the concurrency token: send it back
/// unchanged in <see cref="UpdateKbArticleRequest"/>. <paramref name="Status"/> is one of <see cref="KbArticleStatuses"/>.
/// </summary>
public sealed record KbArticleDto(
    Guid Id,
    Guid? ProductId,
    Guid? CategoryId,
    string Slug,
    string Title,
    string? Summary,
    string BodyMarkdown,
    string Status,
    Guid AuthorAgentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    uint Version);

/// <summary>One row of the agent article list. A null product means the article is shared by every product.</summary>
public sealed record KbArticleListItemDto(
    Guid Id,
    Guid? ProductId,
    Guid? CategoryId,
    string Slug,
    string Title,
    string Status,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A new article, always a Draft. A null <paramref name="ProductId"/> makes it shared. The product and the slug are permanent. A shared
/// article may only use a shared category; a product article may use a shared category or one of its own product.
/// </summary>
public sealed record CreateKbArticleRequest(Guid? ProductId, Guid? CategoryId, string? Slug, string? Title, string? Summary, string? BodyMarkdown);

/// <summary>
/// Replaces the editable fields. <paramref name="Version"/> must equal the version last read, otherwise the update is a 409. Updating an
/// Archived article returns it to Draft. The product and the slug cannot change.
/// </summary>
public sealed record UpdateKbArticleRequest(Guid? CategoryId, string? Title, string? Summary, string? BodyMarkdown, uint Version);

/// <summary>The filters of <c>GET api/kb/articles</c>. <paramref name="Text"/> is a full-text search; blank lists by newest update.</summary>
/// <param name="ProductId">Only this product's articles (plus the shared ones unless <paramref name="IncludeShared"/> is false).</param>
/// <param name="SharedOnly">Only shared articles. It wins over <paramref name="ProductId"/>.</param>
/// <param name="Status">One of <see cref="KbArticleStatuses"/>; null for every status.</param>
public sealed record ListKbArticlesRequest(
    Guid? ProductId,
    bool SharedOnly,
    bool IncludeShared,
    string? Status,
    Guid? CategoryId,
    string? Text,
    int Page,
    int PageSize);

public sealed record KbPreviewRequest(string? BodyMarkdown);

/// <summary>Sanitized HTML, produced by the same pipeline the public article page uses.</summary>
public sealed record KbPreviewResponse(string Html);

/// <summary>The stored key (<c>kb-images/{guid}.{ext}</c>) and the absolute public URL to put in the Markdown.</summary>
public sealed record KbImageUploadResponse(string Key, string Url);
```

Create `src/TechStrap.Contracts/Kb/KbCategoryDtos.cs`:

```csharp
namespace TechStrap.Contracts.Kb;

/// <summary><paramref name="Version"/> is the concurrency token; send it back unchanged in <see cref="UpdateKbCategoryRequest"/>. A null product means shared.</summary>
public sealed record KbCategoryDto(Guid Id, Guid? ProductId, string Slug, string Name, string? Description, int SortOrder, uint Version);

/// <summary>The product and the slug are permanent. The slug <c>search</c> is reserved, and a slug already used in another scope is refused.</summary>
public sealed record CreateKbCategoryRequest(Guid? ProductId, string? Slug, string? Name, string? Description, int SortOrder);

/// <summary><paramref name="Version"/> must equal the version last read, otherwise the update is a 409.</summary>
public sealed record UpdateKbCategoryRequest(string? Name, string? Description, int SortOrder, uint Version);
```

Create `src/TechStrap.Contracts/Kb/PublicKbDtos.cs`:

```csharp
namespace TechStrap.Contracts.Kb;

/// <summary>
/// One public search hit. <paramref name="Snippet"/> is plain text from the article summary with every HTML character encoded, so it is
/// safe to place in markup as it is. <paramref name="ProductKey"/> is null for a shared article.
/// </summary>
public sealed record PublicKbSearchResultDto(string Slug, string Title, string Snippet, string CategorySlug, string CategoryName, string? ProductKey);

/// <summary>A published article for the portal. <paramref name="Html"/> is sanitized; there is no author and no id.</summary>
public sealed record PublishedKbArticleDto(
    string? ProductKey,
    string CategorySlug,
    string CategoryName,
    string Slug,
    string Title,
    string? Summary,
    string Html,
    DateTimeOffset PublishedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A category with the number of published articles the product can see in it (its own plus the shared ones).</summary>
public sealed record PublicKbCategoryDto(string Slug, string Name, string? Description, int ArticleCount);

public sealed record KbSitemapEntryDto(string? ProductKey, string CategorySlug, string Slug, DateTimeOffset UpdatedAt);
```

- [ ] **Step 8: Run the tests and confirm they pass**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then:

```bash
dotnet test --project tests/TechStrap.Domain.Tests -c Release --no-build
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build
dotnet test --project tests/TechStrap.Application.Tests -c Release --no-build
dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter-class "*TicketLifecycleEndToEndTests"
pwsh -File scripts/Invoke-ScriptTests.ps1
```

Expected: PASS. Domain 398 tests (`KbArticleTests` 25), Integration 521 (`KbScopeRuleTests` 5, `KbRepositoryTests` 7, `KbRepositoryGuardTests` 11, `KbSearchTests` 5), Application 567 (`KbContractParityTests` 2), the lifecycle test 5, Pester 279. The migration is checked by the existing `MigrationStartupTests` (the model matches the last migration). Also run `dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api -c TechStrapDbContext`; expected: `No changes have been made to the model since the last migration.`

- [ ] **Step 9: Prove each pin with a mutation**

Each change below was applied to the finished code in the scratch copy, the named tests were run, and the change was reverted (`mut.sh` copies the file, edits it, rebuilds with `--no-incremental`, runs the filter and restores the file with a fresh timestamp, so no stale DLL survives). A pin counts only if a named test fails. Repeat any of them with `git stash` as your undo; do not commit a mutation.

| Mutation | Failing tests |
| --- | --- |
| `KbArticle.Update`: delete the `if (Status == KbArticleStatus.Archived) { Status = KbArticleStatus.Draft; }` block | `KbArticleTests.Editing_an_archived_article_returns_it_to_draft_and_keeps_the_first_publish_time` |
| `KbArticle.FirstMissingForPublish`: `return CategoryId is null ? "category" : null;` becomes `return null;` | `KbArticleTests.Publishing_without_a_category_is_a_validation_error_on_category_and_changes_nothing` |
| `KbRepository.ArticleSlugTakenAsync`, product branch: `(a.ProductId == product || a.ProductId == null)` becomes `a.ProductId == product` | `KbScopeRuleTests.A_product_article_cannot_reuse_a_shared_slug_but_another_products_slug_is_free` |
| `KbRepository.ArticleSlugTakenAsync`, shared branch: `a.Slug == slug` becomes `a.Slug == slug && a.ProductId == null` | `KbScopeRuleTests.A_shared_article_cannot_reuse_any_products_slug`, `KbScopeRuleTests.An_archived_article_still_holds_its_slug` |

The helper behind every mutation table in this plan (save it outside the repository as `mut.sh`). It writes nothing to the repository except the mutated file, and it restores that file and gives it a fresh timestamp, because a restored file with an old timestamp lets an incremental build keep the mutated DLL (the stale-DLL trap). Usage: `mut.sh <file> <file holding the old text> <file holding the new text> <test project> <test filter>`, for example `mut.sh src/TechStrap.Domain/Knowledge/KbArticle.cs old.txt new.txt tests/TechStrap.Domain.Tests "*Archived*"`.

```bash
#!/bin/bash
# usage: mut.sh <file> <old-literal-file> <new-literal-file> <test-project> <filter-method>
f="$1"; old="$2"; new="$3"; proj="$4"; filt="$5"
cp "$f" "$f.orig"
python - "$f" "$old" "$new" <<'PY'
import sys
f,o,n=sys.argv[1:4]
s=open(f,encoding='utf-8',newline='').read()
o=open(o,encoding='utf-8').read().rstrip('\n'); n=open(n,encoding='utf-8').read().rstrip('\n')
crlf='\r\n' in s
t=s.replace('\r\n','\n')
assert t.count(o)==1,('not unique',t.count(o))
t=t.replace(o,n)
open(f,'w',encoding='utf-8',newline='').write(t.replace('\n','\r\n') if crlf else t)
PY
if [ $? -eq 0 ]; then
dotnet build "$proj" -c Release --no-incremental 2>&1 | grep -E " error |rror\(s\)" | sort -u | head -3
dotnet test --project "$proj" -c Release --no-build --filter-method "$filt" 2>&1 | grep -E "^failed|Test run summ|total:|failed:|succeeded:"
fi
mv -f "$f.orig" "$f"; touch "$f"
```

- [ ] **Step 10: Whole-project check**

Run: `dotnet test --solution TechStrap.CI.slnf -c Release` and `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS. (The old tests that published a category-less article are the ones changed in Step 1; if another one fails with `kb-publish-incomplete`, give it a category the same way.)

- [ ] **Step 11: Commit**

```bash
git add docs/architecture \
  docs/superpowers/plans/2026-10-05-phase-08-knowledge-base.md \
  scripts/tests/RepositoryDocs.Tests.ps1 \
  src/TechStrap.Domain \
  src/TechStrap.Contracts \
  src/TechStrap.Application \
  src/TechStrap.Infrastructure \
  tests
git diff --cached --stat
git commit -m "feat(kb): D-044, the article lifecycle, publish validation, the category version and the cross-scope slug rule" -m "D-044 records the owner decisions of 2026-10-05 for PHASE-08 (slugs unique across scopes, the image URL setting, no audit, one PR) and the defaults. An edit now returns an Archived article to Draft, and Publish needs a title, a slug, a body and a category (a field error otherwise). KbCategory gains a description, the xmin concurrency token (one generated migration) and the reserved slug search. The repository can say whether a slug is taken in any scope, for articles and categories. The KB DTOs and constants arrive in Contracts. Existing tests that published a category-less article get a category." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 2: The KB content profile: pipe tables, `img` and table tags, `IKbContentRenderer` and the preview handler

**Review Focus pin:** Review Focus 1 (stored XSS through KB Markdown or the preview). Pinned here by `KbContentRendererTests` and `KbHtmlSanitizerTests` (a 33-input and a 26-input attack corpus: every output holds only allow-listed tags and attributes, no script link, and an image keeps only an absolute http or https source), `MessagePipelineUnchangedTests` (agent replies keep their old output, byte for byte) and `RenderKbPreviewRequestHandlerTests`. Task 7 repeats the corpus through `POST /api/kb/preview`; Task 10 through the Admin preview pane.

**Files:**
- Create: `src/TechStrap.Application/Content/IKbContentRenderer.cs`
- Create: `src/TechStrap.Application/Knowledge/KbErrors.cs`
- Create: `src/TechStrap.Application/Knowledge/RenderKbPreviewRequestHandler.cs`
- Modify: `src/TechStrap.Infrastructure/Content/ContentServiceCollectionExtensions.cs`
- Create: `src/TechStrap.Infrastructure/Content/KbContentRenderer.cs`
- Create: `src/TechStrap.Infrastructure/Content/KbHtmlSanitizer.cs`
- Test (create): `tests/TechStrap.Application.Tests/Knowledge/RenderKbPreviewRequestHandlerTests.cs`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/KbContentRendererTests.cs`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/KbHtmlSanitizerTests.cs`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/MessagePipelineUnchangedTests.cs`

**Interfaces:**
- Consumes: `MarkdigMarkdownRenderer` and `HtmlSanitizerAdapter` (the message pipeline, which stays exactly as it is), `IMarkdownRenderer`, `IHtmlSanitizer`, `AddTechStrapContent`, `KbLimits.MaxPreviewChars` (Task 1), `Result<T>` and `ResultError`.
- Produces:
  - `TechStrap.Application.Content.IKbContentRenderer { string Render(string markdown); }`: Markdown to safe HTML for the knowledge base. Registered by `AddTechStrapContent` (singleton).
  - `KbContentRenderer` (internal): Markdig with raw HTML off, pipe tables, autolinks and strikethrough; soft line breaks stay spaces (long-form text). It catches Markdig's nesting `ArgumentException` and shows the text encoded.
  - `KbHtmlSanitizer` (internal): the message allow-list plus `img`, `table`, `thead`, `tbody`, `tr`, `th`, `td`; attributes `href`, `src`, `alt`; schemes http, https, mailto. An `img` keeps only an absolute http or https `src` (else the element is removed) and gains `loading="lazy"` and `referrerpolicy="no-referrer"`; every `a` gains `rel="noopener noreferrer nofollow"`.
  - `IRenderKbPreviewRequestHandler.HandleAsync(KbPreviewRequest request, CancellationToken) : Task<Result<KbPreviewResponse>>`; a source over `KbLimits.MaxPreviewChars` is a Validation error `body-too-long` (target `body`); a null or empty source gives `Html = ""` without rendering.
  - `KbErrors` (internal, `TechStrap.Application.Knowledge`): the error factory the later KB handlers extend.

- [ ] **Step 1: Write the failing tests**

Four new test files. `KbHtmlSanitizerTests` feeds raw HTML straight into the sanitizer, the second line of defense behind Markdig's disabled raw HTML; `KbContentRendererTests` feeds Markdown through the whole profile. Both share one checker (`KbSafeHtml.ShouldBeSafe`, which parses the output with AngleSharp and allows only the listed tags and attributes, no script link and an absolute http or https image source). `MessagePipelineUnchangedTests` holds exact outputs captured from the message pipeline before this task, so any widening of the shared pipeline fails there.

Create `tests/TechStrap.Application.Tests/Knowledge/RenderKbPreviewRequestHandlerTests.cs`:

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Content;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class RenderKbPreviewRequestHandlerTests
{
    private readonly IKbContentRenderer _renderer = Substitute.For<IKbContentRenderer>();

    private RenderKbPreviewRequestHandler Handler() => new(_renderer);

    [Fact]
    public async Task The_preview_is_the_output_of_the_shared_kb_renderer()
    {
        _renderer.Render("# Hi").Returns("<h1>Hi</h1>\n");

        var result = await Handler().HandleAsync(new KbPreviewRequest("# Hi"), TestContext.Current.CancellationToken);

        result.Value.Html.ShouldBe("<h1>Hi</h1>\n");
        _renderer.Received(1).Render("# Hi");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task An_empty_source_previews_as_empty_html_without_rendering(string? body)
    {
        var result = await Handler().HandleAsync(new KbPreviewRequest(body), TestContext.Current.CancellationToken);

        result.Value.Html.ShouldBeEmpty();
        _renderer.DidNotReceiveWithAnyArgs().Render(default!);
    }

    [Fact]
    public async Task A_source_at_the_limit_is_rendered()
    {
        var body = new string('a', KbLimits.MaxPreviewChars);
        _renderer.Render(body).Returns("<p>ok</p>");

        var result = await Handler().HandleAsync(new KbPreviewRequest(body), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_source_over_the_limit_is_a_validation_error_on_body_and_is_never_rendered()
    {
        var result = await Handler().HandleAsync(new KbPreviewRequest(new string('a', KbLimits.MaxPreviewChars + 1)), TestContext.Current.CancellationToken);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("body-too-long"),
            error => error.Target.ShouldBe("body"));
        _renderer.DidNotReceiveWithAnyArgs().Render(default!);
    }
}
```

Create `tests/TechStrap.Infrastructure.IntegrationTests/KbContentRendererTests.cs`:

```csharp
using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// D-021, D-044: the KB content profile. Review Focus 1 (stored XSS through KB Markdown or the preview): whatever an agent types, the
/// output holds only allow-listed tags and attributes, and an image keeps only an absolute http or https source.
/// </summary>
public sealed class KbContentRendererTests
{
    private readonly KbContentRenderer _renderer = new();

    public static TheoryData<string> Attacks() =>
    [
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<svg onload=alert(1)>",
        "<iframe src=\"https://evil.example\"></iframe>",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<details open ontoggle=alert(1)>x</details>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
        "[x](javascript:alert(1))",
        "[x](JaVaScRiPt:alert(1))",
        "[x](&#106;avascript:alert(1))",
        "[x](jav&#x09;ascript:alert(1))",
        "[x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)",
        "[x](vbscript:msgbox(1))",
        "![x](javascript:alert(1))",
        "![x](data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+)",
        "![x](data:image/png;base64,AAAA)",
        "![x](//evil.example/a.png)",
        "![x](/kb-images/a.png)",
        "![x](kb-images/a.png)",
        "![x](mailto:a@example.com)",
        "![x](ftp://evil.example/a.png)",
        "![x](file:///etc/passwd)",
        "![x](https://ok.example/a.png \"t\" onerror=\"alert(1)\")",
        "![x\" onerror=\"alert(1)](https://ok.example/a.png)",
        "![](https://ok.example/a.png)<img src=x onerror=alert(1)>",
        "| a | b |\n|---|---|\n| <script>alert(1)</script> | <img src=x onerror=alert(1)> |",
        "| a |\n|---|\n| [x](javascript:alert(1)) |",
        "<table><tr><td onclick=alert(1)>x</td></tr></table>",
        "<style>*{background:url(javascript:alert(1))}</style>",
        "<form action=\"javascript:alert(1)\"><button>x</button></form>",
        "<base href=\"javascript:alert(1)//\">",
        "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
    ];

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Attack_vectors_leave_only_allow_listed_tags_attributes_and_web_image_sources(string markdown)
    {
        KbSafeHtml.ShouldBeSafe(_renderer.Render(markdown));
    }

    [Fact]
    public void An_https_image_keeps_its_source_and_alt_and_gets_lazy_loading_and_no_referrer()
    {
        var html = _renderer.Render("![Router front](https://api.example.com/kb-images/0199.png)");

        html.ShouldBe("<p><img src=\"https://api.example.com/kb-images/0199.png\" alt=\"Router front\" loading=\"lazy\" referrerpolicy=\"no-referrer\"></p>\n");
    }

    [Theory]
    [InlineData("![x](/kb-images/a.png)")]
    [InlineData("![x](//evil.example/a.png)")]
    [InlineData("![x](javascript:alert(1))")]
    [InlineData("![x](data:image/png;base64,AAAA)")]
    [InlineData("![x](mailto:a@example.com)")]
    public void An_image_with_any_other_source_is_removed_entirely(string markdown) =>
        _renderer.Render(markdown).ShouldNotContain("<img");

    [Fact]
    public void A_pipe_table_renders_as_a_table()
    {
        var html = _renderer.Render("| Name | Port |\n|------|------|\n| smtp | 587 |");

        html.ShouldContain("<table>");
        html.ShouldContain("<th>Name</th>");
        html.ShouldContain("<td>587</td>");
    }

    [Fact]
    public void Raw_html_is_shown_as_text_never_as_markup()
    {
        var html = _renderer.Render("Use <b onclick=x>bold</b> here");

        html.ShouldBe("<p>Use &lt;b onclick=x&gt;bold&lt;/b&gt; here</p>\n");
    }

    [Fact]
    public void Links_get_the_safe_rel_and_bare_urls_are_linked()
    {
        var html = _renderer.Render("See [docs](https://example.com/docs) or https://example.com/faq");

        html.ShouldContain("href=\"https://example.com/docs\" rel=\"noopener noreferrer nofollow\"");
        html.ShouldContain("href=\"https://example.com/faq\" rel=\"noopener noreferrer nofollow\"");
    }

    [Fact]
    public void A_soft_line_break_stays_a_space_in_long_form_text()
    {
        _renderer.Render("one\ntwo").ShouldBe("<p>one\ntwo</p>\n");
    }

    [Fact]
    public void Pathological_nesting_falls_back_to_encoded_text()
    {
        var html = _renderer.Render(new string('[', 200) + "<script>alert(1)</script>");

        html.ShouldNotContain("<script");
        html.ShouldStartWith("<p>");
    }

    [Fact]
    public void An_empty_source_renders_nothing()
    {
        _renderer.Render(string.Empty).ShouldBeEmpty();
    }
}
```

Create `tests/TechStrap.Infrastructure.IntegrationTests/KbHtmlSanitizerTests.cs`:

```csharp
using AngleSharp.Html.Parser;
using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The checks both KB profiles share: only allow-listed tags and attributes, no script link, and an image source that is absolute http or https.</summary>
internal static class KbSafeHtml
{
    private static readonly string[] _allowedTags =
    [
        "html", "head", "body", "p", "br", "strong", "b", "em", "i", "u", "a", "ul", "ol", "li", "blockquote", "code", "pre", "h1", "h2", "h3", "h4", "h5", "h6",
        "hr", "del", "s", "img", "table", "thead", "tbody", "tr", "th", "td",
    ];

    private static readonly string[] _allowedAttributes = ["href", "src", "alt", "rel", "loading", "referrerpolicy"];

    public static void ShouldBeSafe(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var element in document.All)
        {
            _allowedTags.ShouldContain(element.LocalName, $"tag <{element.LocalName}> survived in: {html}");
            foreach (var attribute in element.Attributes)
            {
                _allowedAttributes.ShouldContain(attribute.Name, $"attribute {attribute.Name} survived in: {html}");
            }

            if (element.LocalName == "a" && element.GetAttribute("href") is { } href)
            {
                href.ShouldNotStartWith("javascript:", Case.Insensitive);
                href.ShouldNotStartWith("data:", Case.Insensitive);
                href.ShouldNotStartWith("vbscript:", Case.Insensitive);
            }

            if (element.LocalName == "img")
            {
                var src = element.GetAttribute("src");
                (src is not null && (src.StartsWith("http://", StringComparison.Ordinal) || src.StartsWith("https://", StringComparison.Ordinal)))
                    .ShouldBeTrue($"img src {src} is not http or https in: {html}");
            }
        }
    }
}

/// <summary>
/// The KB sanitizer on its own, fed raw HTML (D-044). Markdig has raw HTML off, so this is the second line of defense: if the Markdown
/// step ever let markup through, these inputs must still come out safe.
/// </summary>
public sealed class KbHtmlSanitizerTests
{
    private readonly KbHtmlSanitizer _sanitizer = new();

    public static TheoryData<string> Attacks() =>
    [
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<img src=\"https://ok.example/a.png\" onerror=\"alert(1)\" onload=\"alert(2)\" style=\"x\">",
        "<img src=\"javascript:alert(1)\" alt=\"x\">",
        "<img src=\"data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+\">",
        "<img src=\"//evil.example/a.png\">",
        "<img src=\"/kb-images/a.png\">",
        "<img src=\"mailto:a@example.com\">",
        "<img srcset=\"https://ok.example/a.png 1x\" src=\"https://ok.example/a.png\">",
        "<svg onload=alert(1)><circle/></svg>",
        "<iframe src=\"https://evil.example\"></iframe>",
        "<object data=\"x\"></object>",
        "<embed src=\"x\">",
        "<a href=\"javascript:alert(1)\">x</a>",
        "<a href=\"JaVaScRiPt:alert(1)\">x</a>",
        "<a href=\"&#106;avascript:alert(1)\" onclick=\"alert(1)\">x</a>",
        "<a href=\"data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==\">x</a>",
        "<table onclick=\"alert(1)\"><tr><td style=\"background:url(javascript:alert(1))\">x</td></tr></table>",
        "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>",
        "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">",
        "<details open ontoggle=alert(1)>x</details>",
        "<style>*{background:url(javascript:alert(1))}</style>",
        "<form action=\"javascript:alert(1)\"><button>x</button></form>",
        "<base href=\"javascript:alert(1)//\">",
        "<meta http-equiv=\"refresh\" content=\"0;url=javascript:alert(1)\">",
        "<link rel=\"stylesheet\" href=\"javascript:alert(1)\">",
    ];

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Attack_vectors_leave_only_allow_listed_tags_attributes_and_web_image_sources(string html) =>
        KbSafeHtml.ShouldBeSafe(_sanitizer.Sanitize(html));

    [Fact]
    public void A_web_image_and_a_table_survive_with_their_allowed_attributes()
    {
        var clean = _sanitizer.Sanitize("<table><thead><tr><th>A</th></tr></thead><tbody><tr><td><img src=\"https://ok.example/a.png\" alt=\"pic\" width=\"9\"></td></tr></tbody></table>");

        clean.ShouldBe("<table><thead><tr><th>A</th></tr></thead><tbody><tr><td><img src=\"https://ok.example/a.png\" alt=\"pic\" loading=\"lazy\" referrerpolicy=\"no-referrer\"></td></tr></tbody></table>");
    }

    [Fact]
    public void Every_link_gets_the_safe_rel() =>
        _sanitizer.Sanitize("<a href=\"https://example.com\" rel=\"opener\">x</a>").ShouldBe("<a href=\"https://example.com\" rel=\"noopener noreferrer nofollow\">x</a>");
}
```

Create `tests/TechStrap.Infrastructure.IntegrationTests/MessagePipelineUnchangedTests.cs`:

```csharp
using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// D-044: the knowledge base got its own content profile, so agent replies (which customers read in email and in the portal) keep
/// the pipeline they had before PHASE-08. These exact outputs were captured from <c>MarkdigMarkdownRenderer</c> and
/// <c>HtmlSanitizerAdapter</c> before the KB profile existed; a change to either shows up here.
/// </summary>
public sealed class MessagePipelineUnchangedTests
{
    private readonly MarkdigMarkdownRenderer _markdown = new();
    private readonly HtmlSanitizerAdapter _sanitizer = new();

    private string Render(string markdown) => _sanitizer.Sanitize(_markdown.ToHtml(markdown));

    [Fact]
    public void An_image_in_a_reply_is_still_removed_so_a_reply_cannot_carry_a_tracking_pixel() =>
        Render("![tracker](https://t.example/p.png)").ShouldBe("<p></p>\n");

    [Fact]
    public void A_pipe_table_in_a_reply_is_still_plain_text_with_line_breaks() =>
        Render("| a | b |\n|---|---|\n| 1 | 2 |").ShouldBe("<p>| a | b |<br>\n|---|---|<br>\n| 1 | 2 |</p>\n");

    [Fact]
    public void A_line_break_in_a_reply_is_still_a_hard_break() =>
        Render("Line one\nLine two").ShouldBe("<p>Line one<br>\nLine two</p>\n");

    [Fact]
    public void Links_in_a_reply_keep_their_rel_and_bare_urls_are_linked() =>
        Render("[docs](https://example.com/docs) and https://example.com/faq").ShouldBe(
            "<p><a href=\"https://example.com/docs\" rel=\"noopener noreferrer nofollow\">docs</a> and "
            + "<a href=\"https://example.com/faq\" rel=\"noopener noreferrer nofollow\">https://example.com/faq</a></p>\n");

    [Fact]
    public void Raw_html_in_a_reply_is_still_escaped() =>
        Render("**bold** and <img src=x onerror=alert(1)>").ShouldBe("<p><strong>bold</strong> and &lt;img src=x onerror=alert(1)&gt;</p>\n");

    [Fact]
    public void Headings_lists_and_code_in_a_reply_are_unchanged() =>
        Render("# Title\n\n- one\n- two\n\n`code`").ShouldBe("<h1>Title</h1>\n<ul>\n<li>one</li>\n<li>two</li>\n</ul>\n<p><code>code</code></p>\n");
}
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: the build FAILS with `CS0246` for `IKbContentRenderer`, `RenderKbPreviewRequestHandler`, `KbContentRenderer` and `KbHtmlSanitizer` ("could not be found"). `MessagePipelineUnchangedTests` compiles against code that exists today, and it passes before and after this task: it is a characterization test, not a red one.

- [ ] **Step 3: Add the renderer, the sanitizer, the registration and the preview handler**

The KB profile is separate from the message profile on purpose. Agent replies reach customers by email and in the portal, so widening the shared sanitizer would let a reply carry a tracking image or a table (decision D-044). `KbErrors` is created here with the preview error and grows in later tasks. The handler is registered by the existing name-convention scan (`AddApplicationHandlers`); nothing else needs wiring.

Create `src/TechStrap.Application/Content/IKbContentRenderer.cs`:

```csharp
namespace TechStrap.Application.Content;

/// <summary>
/// Knowledge-base Markdown to safe HTML (D-021, D-044). One call runs the KB Markdown pipeline (raw HTML off, pipe tables) and the
/// KB sanitizer, so the editor preview and the public article page cannot drift apart. The result is safe to place in a page as it is.
/// Agent replies and customer messages do not use it: they keep <see cref="IMarkdownRenderer"/> and <see cref="IHtmlSanitizer"/>.
/// </summary>
public interface IKbContentRenderer
{
    string Render(string markdown);
}
```

Create `src/TechStrap.Application/Knowledge/KbErrors.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

/// <summary>The errors the KB handlers return. The codes are part of the API contract (D-044) and the Admin matches on them.</summary>
internal static class KbErrors
{
    public static ResultError PreviewTooLong() =>
        new("body-too-long", $"The text is longer than {KbLimits.MaxPreviewChars:N0} characters and cannot be previewed.", ResultErrorKind.Validation, "body");
}
```

Create `src/TechStrap.Application/Knowledge/RenderKbPreviewRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Content;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IRenderKbPreviewRequestHandler
{
    Task<Result<KbPreviewResponse>> HandleAsync(KbPreviewRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/preview (Agent, D-021). Renders Markdown with the same <see cref="IKbContentRenderer"/> the public article page uses, so
/// the preview is exactly what readers will see. A source longer than <see cref="KbLimits.MaxPreviewChars"/> is a 400, so the endpoint
/// cannot be used as a free renderer. Nothing is stored.
/// </summary>
public sealed class RenderKbPreviewRequestHandler(IKbContentRenderer renderer) : IRenderKbPreviewRequestHandler
{
    public Task<Result<KbPreviewResponse>> HandleAsync(KbPreviewRequest request, CancellationToken cancellationToken)
    {
        var markdown = request.BodyMarkdown ?? string.Empty;
        if (markdown.Length > KbLimits.MaxPreviewChars)
        {
            return Task.FromResult(Result<KbPreviewResponse>.Failure(KbErrors.PreviewTooLong()));
        }

        return Task.FromResult(Result<KbPreviewResponse>.Success(new KbPreviewResponse(markdown.Length == 0 ? string.Empty : renderer.Render(markdown))));
    }
}
```

Create `src/TechStrap.Infrastructure/Content/KbHtmlSanitizer.cs`:

```csharp
using AngleSharp.Dom;
using GanssHtmlSanitizer = Ganss.Xss.HtmlSanitizer;

namespace TechStrap.Infrastructure.Content;

/// <summary>
/// The KB allow-list sanitizer (D-044): the message profile plus tables and <c>img</c>. It is the second line of defense behind Markdig's
/// disabled raw HTML, and it is tested on its own. An image keeps only an absolute http or https <c>src</c> and its <c>alt</c>; an image
/// with any other source (relative, protocol-relative, <c>data:</c>, <c>javascript:</c>, <c>mailto:</c>) is removed.
/// </summary>
internal sealed class KbHtmlSanitizer
{
    private static readonly string[] _tags =
    [
        "p", "br", "strong", "b", "em", "i", "u", "a", "ul", "ol", "li", "blockquote", "code", "pre", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "del", "s",
        "img", "table", "thead", "tbody", "tr", "th", "td",
    ];

    private readonly GanssHtmlSanitizer _sanitizer;

    public KbHtmlSanitizer()
    {
        _sanitizer = new GanssHtmlSanitizer();
        _sanitizer.AllowedTags.Clear();
        _sanitizer.AllowedAttributes.Clear();
        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedClasses.Clear();
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.UriAttributes.Clear();
        _sanitizer.UriAttributes.Add("href");
        _sanitizer.UriAttributes.Add("src");
        foreach (var tag in _tags)
        {
            _sanitizer.AllowedTags.Add(tag);
        }

        _sanitizer.AllowedAttributes.Add("href");
        _sanitizer.AllowedAttributes.Add("src");
        _sanitizer.AllowedAttributes.Add("alt");
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not IElement element)
            {
                return;
            }

            if (string.Equals(element.LocalName, "a", StringComparison.OrdinalIgnoreCase))
            {
                element.SetAttribute("rel", "noopener noreferrer nofollow");
            }
            else if (string.Equals(element.LocalName, "img", StringComparison.OrdinalIgnoreCase))
            {
                if (IsAbsoluteWebUrl(element.GetAttribute("src")))
                {
                    element.SetAttribute("loading", "lazy");
                    element.SetAttribute("referrerpolicy", "no-referrer");
                }
                else
                {
                    element.Remove();
                }
            }
        };
    }

    public string Sanitize(string html) => _sanitizer.Sanitize(html);

    // The sanitizer has already dropped any scheme that is not allowed; a mailto: or a relative path is still a valid link target, but never an image source.
    private static bool IsAbsoluteWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
```

Create `src/TechStrap.Infrastructure/Content/KbContentRenderer.cs`:

```csharp
using System.Net;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using TechStrap.Application.Content;

namespace TechStrap.Infrastructure.Content;

/// <summary>
/// The knowledge-base content profile (D-021, D-044): Markdig with raw HTML off and pipe tables, then <see cref="KbHtmlSanitizer"/>.
/// The message profile (<see cref="MarkdigMarkdownRenderer"/> and <see cref="HtmlSanitizerAdapter"/>) is deliberately not widened.
/// </summary>
internal sealed class KbContentRenderer : IKbContentRenderer
{
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();

    private readonly KbHtmlSanitizer _sanitizer = new();

    public string Render(string markdown)
    {
        var text = markdown ?? string.Empty;
        string html;
        try
        {
            html = Markdown.ToHtml(text, _pipeline);
        }
        catch (ArgumentException)
        {
            // Markdig refuses very deep nesting (for example 128 unclosed "[" from pasted terminal output). The text is shown as
            // encoded plain text; nothing from the body is logged.
            html = "<p>" + WebUtility.HtmlEncode(text) + "</p>";
        }

        return _sanitizer.Sanitize(html);
    }
}
```

Modify `src/TechStrap.Infrastructure/Content/ContentServiceCollectionExtensions.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -10,6 +10,7 @@ public static class ContentServiceCollectionExtensions
     {
         services.TryAddSingleton<IHtmlSanitizer, HtmlSanitizerAdapter>();
         services.TryAddSingleton<IMarkdownRenderer, MarkdigMarkdownRenderer>();
+        services.TryAddSingleton<IKbContentRenderer, KbContentRenderer>();
         return services;
     }
 }
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then:

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter-class "*KbContentRendererTests"
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter-class "*KbHtmlSanitizerTests"
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter-class "*MessagePipelineUnchangedTests"
dotnet test --project tests/TechStrap.Application.Tests -c Release --no-build --filter-class "*RenderKbPreviewRequestHandlerTests"
```

Expected: PASS, 45, 28, 6 and 5 tests.

- [ ] **Step 5: Prove each pin with a mutation**

Applied to the finished code, the named tests run, the change reverted (same method as Task 1). The two sanitizer mutations show why the corpus is run twice: the Markdown step already blocks raw HTML, so only the raw-HTML corpus proves the allow-list.

| Mutation | Failing tests |
| --- | --- |
| `KbHtmlSanitizer`: add `_sanitizer.AllowedSchemes.Add("javascript")` | `KbHtmlSanitizerTests.Attack_vectors_leave_only_allow_listed_tags_attributes_and_web_image_sources` (the `<a href="javascript:...">` cases) and `KbContentRendererTests.Attack_vectors_...` (the table cell with a `javascript:` link) |
| `KbHtmlSanitizer`: replace `IsAbsoluteWebUrl(element.GetAttribute("src"))` with `element.GetAttribute("src") is not null` | `KbContentRendererTests.An_image_with_any_other_source_is_removed_entirely` (`/kb-images/a.png`, `//evil.example/a.png`, `mailto:`), `KbHtmlSanitizerTests.Attack_vectors_...` (`<img src="//evil.example/a.png">`) |
| `KbHtmlSanitizer`: add `AllowedAttributes.Add("onerror")` and `Add("onclick")` | `KbHtmlSanitizerTests.Attack_vectors_...` (three inputs: the `onerror`/`onload` image, the `onclick` table) |
| `KbHtmlSanitizer`: add `AllowedTags.Add("iframe")` | `KbHtmlSanitizerTests.Attack_vectors_...` (the `<iframe>` input) |
| `KbContentRenderer`: remove `.DisableHtml()` from the Markdig pipeline | `KbContentRendererTests.Raw_html_is_shown_as_text_never_as_markup` (the corpus still passes: the sanitizer is the second line) |
| `HtmlSanitizerAdapter` (messages): add `"img"` to the tag list | `MessagePipelineUnchangedTests.An_image_in_a_reply_is_still_removed_so_a_reply_cannot_carry_a_tracking_pixel` |
| `MarkdigMarkdownRenderer` (messages): add `.UsePipeTables()` | `MessagePipelineUnchangedTests.A_pipe_table_in_a_reply_is_still_plain_text_with_line_breaks` |
| `RenderKbPreviewRequestHandler`: `markdown.Length > KbLimits.MaxPreviewChars` becomes `... * 2` | `RenderKbPreviewRequestHandlerTests.A_source_over_the_limit_is_a_validation_error_on_body_and_is_never_rendered` |

- [ ] **Step 6: Whole-project check**

Run: `dotnet test --solution TechStrap.CI.slnf -c Release` (the architecture tests check the handler shape: sealed, public interface, `CancellationToken` last, no forbidden constructor dependency).
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Application \
  src/TechStrap.Infrastructure \
  tests
git diff --cached --stat
git commit -m "feat(kb): the KB content profile (pipe tables, img and table tags), IKbContentRenderer and the preview handler" -m "The knowledge base gets its own Markdown profile: Markdig with raw HTML off and pipe tables, then a sanitizer that adds table tags and img (an absolute http or https source only, with alt). The message pipeline is untouched, so agent replies keep their output; exact outputs captured before the change pin that. RenderKbPreviewRequestHandler renders with the same renderer the public article page will use (D-021, D-044) and refuses a source over 200,000 characters." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 3: Article handlers: list, get, create and update

**Review Focus pin:** Review Focus 4 (slug collisions across scopes, lost edits on conflict, Archived-to-Draft edits). Pinned here by `CreateKbArticleRequestHandlerTests` and `KbArticleHandlerIntegrationTests` (a product slug equal to a shared slug is refused, and the other way round), `UpdateKbArticleRequestHandlerTests` and `KbArticleHandlerIntegrationTests` (a stale version is a 409; an Archived article is stored as Draft). Task 10 pins the editor half (a 409 keeps the draft).

**Files:**
- Create: `src/TechStrap.Application/Knowledge/CreateKbArticleRequestHandler.cs`
- Create: `src/TechStrap.Application/Knowledge/GetKbArticleRequestHandler.cs`
- Create: `src/TechStrap.Application/Knowledge/KbCategoryRules.cs`
- Modify: `src/TechStrap.Application/Knowledge/KbErrors.cs`
- Create: `src/TechStrap.Application/Knowledge/KbMapping.cs`
- Create: `src/TechStrap.Application/Knowledge/ListKbArticlesRequestHandler.cs`
- Create: `src/TechStrap.Application/Knowledge/UpdateKbArticleRequestHandler.cs`
- Modify: `src/TechStrap.Application/Persistence/IKbRepository.cs`
- Test (create): `tests/TechStrap.Application.Tests/Knowledge/CreateKbArticleRequestHandlerTests.cs`
- Test (create): `tests/TechStrap.Application.Tests/Knowledge/KbFixture.cs`
- Test (create): `tests/TechStrap.Application.Tests/Knowledge/ListAndGetKbArticleHandlerTests.cs`
- Test (create): `tests/TechStrap.Application.Tests/Knowledge/UpdateKbArticleRequestHandlerTests.cs`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/KbArticleHandlerIntegrationTests.cs`

**Interfaces:**
- Consumes: `ICurrentAgentClaims` and `CurrentAgent.RequireActiveAsync`, `IAgentRepository`, `IProductRepository.GetByIdAsync`, `IKbRepository` (`GetArticleAsync`, `GetCategoryAsync`, `ListArticlesAsync`, `SearchAsync`, `AddArticle`, `UpdateArticle`, `ArticleSlugTakenAsync` from Task 1), `IUnitOfWork` and `PersistenceErrorCodes`, `KbArticle`, the Contracts types from Task 1, `UnitOfWorkSubstitute` (tests).
- Produces:
  - `IListKbArticlesRequestHandler.HandleAsync(ListKbArticlesRequest, CancellationToken) : Task<Result<PagedResponse<KbArticleListItemDto>>>`: newest update first, or best match first when `Text` is not blank; a status that is not Draft, Published or Archived is a Validation error `status-invalid` (target `status`).
  - `IGetKbArticleRequestHandler.HandleAsync(Guid id, CancellationToken) : Task<Result<KbArticleDto>>`; 404 `kb-article-not-found`.
  - `ICreateKbArticleRequestHandler.HandleAsync(CreateKbArticleRequest, CancellationToken) : Task<Result<KbArticleDto>>`: a Draft authored by the signed-in agent. Errors: Domain validation (target = the field), `product-not-found` and `kb-category-not-found` (Validation, targets `productId`, `categoryId`), `kb-category-scope-mismatch` (Validation, target `categoryId`), `kb-slug-taken` (409, pre-check and `duplicate` at commit).
  - `IUpdateKbArticleRequestHandler.HandleAsync(Guid id, UpdateKbArticleRequest, CancellationToken) : Task<Result<KbArticleDto>>`: `concurrency-conflict` (409) when `request.Version` differs from the stored version; the same category rule; an Archived article comes back as Draft.
  - Internal helpers: `KbMapping` (`ToDto`, `ToListItem`, `StatusName`, `TryParseStatus`), `KbCategoryRules.CheckAsync` (the category and article product match, a handler duty because the schema cannot express it), and more `KbErrors`.
  - Test helper `KbFixture` (Application.Tests): a signed-in active agent, two products and builders for stored articles (version 5) and categories (version 3).

- [ ] **Step 1: Write the failing tests**

The Application tests use NSubstitute for the repositories and the unit of work. The Integration test runs the real handlers over a real Postgres through `PersistenceTestHost`, so the cross-scope rule and the version check are proven end to end, not only against substitutes. The version check lives in the handler: `KbRepository.UpdateArticle` applies the version of the object it is given, which a handler loads a moment earlier, so without the handler check two edits from the same stale version would both succeed.

Create `tests/TechStrap.Application.Tests/Knowledge/CreateKbArticleRequestHandlerTests.cs`:

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class CreateKbArticleRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();

    private CreateKbArticleRequestHandler Handler(params Result[] commits) =>
        new(_kb.Claims, _kb.Agents, _kb.Products, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create(commits), _kb.Clock);

    private static CreateKbArticleRequest Request(Guid? productId = null, Guid? categoryId = null, string? slug = "reset-password", string? title = "Reset your password") =>
        new(productId, categoryId, slug, title, "Short", "Steps.");

    [Fact]
    public async Task A_new_article_is_a_draft_by_the_signed_in_agent_and_the_dto_carries_the_stored_version()
    {
        var category = _kb.StoredCategory(null);
        _kb.KnowledgeBase.GetArticleAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var created = (KbArticle)_kb.KnowledgeBase.ReceivedCalls().First(c => c.GetMethodInfo().Name == nameof(IKbRepository.AddArticle)).GetArguments()[0]!;
            return KbArticle.Restore(created.Id, created.ProductId, created.CategoryId, created.Slug, created.Title, created.Summary, created.BodyMarkdown, created.Status, created.AuthorId, created.CreatedAt, created.UpdatedAt, null, 9);
        });

        var result = await Handler().HandleAsync(Request(_kb.Orbitly.Id, category.Id), Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe(KbArticleStatuses.Draft),
            dto => dto.ProductId.ShouldBe(_kb.Orbitly.Id),
            dto => dto.CategoryId.ShouldBe(category.Id),
            dto => dto.AuthorAgentId.ShouldBe(_kb.Agent.Id),
            dto => dto.Slug.ShouldBe("reset-password"),
            dto => dto.PublishedAt.ShouldBeNull(),
            dto => dto.Version.ShouldBe(9u));
        _kb.KnowledgeBase.Received(1).AddArticle(Arg.Is<KbArticle>(a => a.Status == KbArticleStatus.Draft && a.ProductId == _kb.Orbitly.Id));
    }

    [Fact]
    public async Task A_null_product_makes_a_shared_article()
    {
        var result = await Handler().HandleAsync(Request(), Ct);

        result.Value.ProductId.ShouldBeNull();
        _kb.KnowledgeBase.Received(1).AddArticle(Arg.Is<KbArticle>(a => a.IsShared));
    }

    [Fact]
    public async Task A_bad_slug_is_a_validation_error_on_slug_and_nothing_is_staged()
    {
        var result = await Handler().HandleAsync(Request(slug: "Not A Slug"), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Target.ShouldBe("slug"));
        _kb.KnowledgeBase.DidNotReceive().AddArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task A_missing_title_is_a_validation_error_on_title()
    {
        var result = await Handler().HandleAsync(Request(title: " "), Ct);

        result.Errors.ShouldHaveSingleItem().Target.ShouldBe("title");
    }

    [Fact]
    public async Task An_unknown_product_is_a_validation_error_on_product_id()
    {
        var result = await Handler().HandleAsync(Request(Guid.NewGuid()), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("product-not-found"),
            error => error.Target.ShouldBe("productId"));
        _kb.KnowledgeBase.DidNotReceive().AddArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task An_unknown_category_is_a_validation_error_on_category_id()
    {
        var result = await Handler().HandleAsync(Request(categoryId: Guid.NewGuid()), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("kb-category-not-found"),
            error => error.Target.ShouldBe("categoryId"));
    }

    [Fact]
    public async Task A_shared_article_cannot_use_a_product_category()
    {
        var category = _kb.StoredCategory(_kb.Orbitly.Id);

        var result = await Handler().HandleAsync(Request(null, category.Id), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-scope-mismatch");
        _kb.KnowledgeBase.DidNotReceive().AddArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task A_product_article_cannot_use_another_products_category()
    {
        var category = _kb.StoredCategory(_kb.Paperplane.Id);

        var result = await Handler().HandleAsync(Request(_kb.Orbitly.Id, category.Id), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-scope-mismatch");
    }

    [Fact]
    public async Task A_product_article_may_use_a_shared_category_or_one_of_its_own()
    {
        var shared = _kb.StoredCategory(null, "general");
        var own = _kb.StoredCategory(_kb.Orbitly.Id, "own");

        (await Handler().HandleAsync(Request(_kb.Orbitly.Id, shared.Id, "one"), Ct)).IsSuccess.ShouldBeTrue();
        (await Handler().HandleAsync(Request(_kb.Orbitly.Id, own.Id, "two"), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_slug_already_used_in_either_scope_is_a_conflict_and_nothing_is_staged()
    {
        _kb.KnowledgeBase.ArticleSlugTakenAsync(_kb.Orbitly.Id, "reset-password", Arg.Any<CancellationToken>()).Returns(true);

        var result = await Handler().HandleAsync(Request(_kb.Orbitly.Id), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("kb-slug-taken"));
        _kb.KnowledgeBase.DidNotReceive().AddArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task A_duplicate_found_at_commit_is_the_same_conflict()
    {
        var result = await Handler(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.Duplicate)).HandleAsync(Request(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-slug-taken");
    }

    [Fact]
    public async Task A_deactivated_agent_is_refused_and_nothing_is_staged()
    {
        _kb.Agent.SetActive(false);

        var result = await Handler().HandleAsync(Request(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("agent-inactive");
        _kb.KnowledgeBase.DidNotReceive().AddArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_repository_calls()
    {
        using var source = new CancellationTokenSource();
        var category = _kb.StoredCategory(null);

        await Handler().HandleAsync(Request(_kb.Orbitly.Id, category.Id), source.Token);

        await _kb.KnowledgeBase.Received().GetCategoryAsync(category.Id, source.Token);
        await _kb.KnowledgeBase.Received().ArticleSlugTakenAsync(_kb.Orbitly.Id, "reset-password", source.Token);
    }
}
```

Create `tests/TechStrap.Application.Tests/Knowledge/KbFixture.cs`:

```csharp
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Knowledge;

/// <summary>Substitutes and builders the KB handler tests share: a signed-in active agent, two products, and articles and categories with known versions.</summary>
internal sealed class KbFixture
{
    public KbFixture()
    {
        Agent = Agent.Create("sam", "Sam", "sam@example.com", AgentRole.Agent, Clock).Value;
        Claims.Current.Returns(new AgentClaims("sam", "Sam", "sam@example.com", AgentRole.Agent));
        Agents.GetBySubjectAsync("sam", Arg.Any<CancellationToken>()).Returns(Agent);
        Products.GetByIdAsync(Orbitly.Id, Arg.Any<CancellationToken>()).Returns(Orbitly);
        Products.GetByIdAsync(Paperplane.Id, Arg.Any<CancellationToken>()).Returns(Paperplane);
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));

    public ICurrentAgentClaims Claims { get; } = Substitute.For<ICurrentAgentClaims>();

    public IAgentRepository Agents { get; } = Substitute.For<IAgentRepository>();

    public IProductRepository Products { get; } = Substitute.For<IProductRepository>();

    public IKbRepository KnowledgeBase { get; } = Substitute.For<IKbRepository>();

    public Agent Agent { get; }

    public Product Orbitly { get; } = Product.Create("orbitly", "Orbitly", "ORB", null, new FakeTimeProvider()).Value;

    public Product Paperplane { get; } = Product.Create("paperplane", "Paperplane", "PPL", null, new FakeTimeProvider()).Value;

    /// <summary>A stored category (version 3) that the repository substitute returns by id.</summary>
    public KbCategory StoredCategory(Guid? productId, string slug = "account")
    {
        var category = KbCategory.Restore(Guid.CreateVersion7(), productId, "Account", slug, null, 1, 3);
        KnowledgeBase.GetCategoryAsync(category.Id, Arg.Any<CancellationToken>()).Returns(category);
        return category;
    }

    /// <summary>A stored article (version 5) that the repository substitute returns by id.</summary>
    public KbArticle StoredArticle(
        KbArticleStatus status = KbArticleStatus.Draft, Guid? productId = null, Guid? categoryId = null, string slug = "reset-password", DateTimeOffset? publishedAt = null)
    {
        var article = KbArticle.Restore(
            Guid.CreateVersion7(), productId, categoryId, slug, "Reset your password", "Short", "Steps.", status, Agent.Id, Clock.GetUtcNow(), Clock.GetUtcNow(), publishedAt, 5);
        KnowledgeBase.GetArticleAsync(article.Id, Arg.Any<CancellationToken>()).Returns(article);
        return article;
    }
}
```

Create `tests/TechStrap.Application.Tests/Knowledge/ListAndGetKbArticleHandlerTests.cs`:

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class ListAndGetKbArticleHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();

    private static ListKbArticlesRequest Request(
        Guid? productId = null, bool sharedOnly = false, bool includeShared = true, string? status = null, Guid? categoryId = null, string? text = null, int page = 1, int pageSize = 25) =>
        new(productId, sharedOnly, includeShared, status, categoryId, text, page, pageSize);

    [Fact]
    public async Task A_blank_text_lists_with_every_filter_forwarded()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Published, _kb.Orbitly.Id);
        var categoryId = Guid.NewGuid();
        _kb.KnowledgeBase.ListArticlesAsync(Arg.Any<KbArticleQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<KbArticle>([article], 2, 10, 11));

        var result = await new ListKbArticlesRequestHandler(_kb.KnowledgeBase).HandleAsync(
            Request(_kb.Orbitly.Id, includeShared: false, status: "published", categoryId: categoryId, text: "  ", page: 2, pageSize: 10), Ct);

        await _kb.KnowledgeBase.Received(1).ListArticlesAsync(
            new KbArticleQuery(_kb.Orbitly.Id, false, KbArticleStatus.Published, categoryId, 2, 10, false), Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().SearchAsync(default!, Ct);
        result.Value.ShouldSatisfyAllConditions(
            page => page.Page.ShouldBe(2),
            page => page.PageSize.ShouldBe(10),
            page => page.TotalCount.ShouldBe(11),
            page => page.Items.ShouldHaveSingleItem().ShouldBe(new KbArticleListItemDto(article.Id, article.ProductId, null, "reset-password", "Reset your password", "Published", article.UpdatedAt)));
    }

    [Fact]
    public async Task A_text_searches_best_match_first_with_the_same_filters()
    {
        _kb.KnowledgeBase.SearchAsync(Arg.Any<KbSearchQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<KbArticle>([], 1, 25, 0));

        await new ListKbArticlesRequestHandler(_kb.KnowledgeBase).HandleAsync(Request(sharedOnly: true, text: "router reset"), Ct);

        await _kb.KnowledgeBase.Received(1).SearchAsync(new KbSearchQuery("router reset", null, true, null, null, 1, 25, true), Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListArticlesAsync(default!, Ct);
    }

    [Fact]
    public async Task A_status_that_is_not_one_of_the_three_is_a_validation_error_on_status()
    {
        var result = await new ListKbArticlesRequestHandler(_kb.KnowledgeBase).HandleAsync(Request(status: "Live"), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("status-invalid"),
            error => error.Target.ShouldBe("status"));
    }

    [Fact]
    public async Task Get_returns_the_article_with_its_markdown_and_version_in_any_status()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Archived);

        var result = await new GetKbArticleRequestHandler(_kb.KnowledgeBase).HandleAsync(article.Id, Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe("Archived"),
            dto => dto.BodyMarkdown.ShouldBe("Steps."),
            dto => dto.Version.ShouldBe(5u));
    }

    [Fact]
    public async Task Get_of_an_unknown_article_is_not_found()
    {
        var result = await new GetKbArticleRequestHandler(_kb.KnowledgeBase).HandleAsync(Guid.NewGuid(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
    }
}
```

Create `tests/TechStrap.Application.Tests/Knowledge/UpdateKbArticleRequestHandlerTests.cs`:

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

/// <summary>Review Focus 4: an edit never overwrites a newer version, and an Archived article is edited back to Draft.</summary>
public sealed class UpdateKbArticleRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();

    private UpdateKbArticleRequestHandler Handler(params Result[] commits) => new(_kb.KnowledgeBase, UnitOfWorkSubstitute.Create(commits), _kb.Clock);

    [Fact]
    public async Task An_edit_changes_the_content_and_stages_the_article()
    {
        var category = _kb.StoredCategory(null);
        var article = _kb.StoredArticle(KbArticleStatus.Draft, productId: _kb.Orbitly.Id);
        _kb.Clock.Advance(TimeSpan.FromHours(1));

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(category.Id, "New title", "New summary", "New body", 5), Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Title.ShouldBe("New title"),
            dto => dto.Summary.ShouldBe("New summary"),
            dto => dto.BodyMarkdown.ShouldBe("New body"),
            dto => dto.CategoryId.ShouldBe(category.Id),
            dto => dto.Slug.ShouldBe("reset-password"),
            dto => dto.UpdatedAt.ShouldBe(_kb.Clock.GetUtcNow()));
        _kb.KnowledgeBase.Received(1).UpdateArticle(article);
    }

    [Fact]
    public async Task A_stale_version_is_a_conflict_and_nothing_is_saved()
    {
        var article = _kb.StoredArticle();

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "New title", null, "New body", 4), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict));
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        article.Title.ShouldBe("Reset your password");
    }

    [Fact]
    public async Task A_conflict_found_at_commit_is_passed_through()
    {
        var article = _kb.StoredArticle();

        var result = await Handler(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ConcurrencyConflict))
            .HandleAsync(article.Id, new UpdateKbArticleRequest(null, "New title", null, "New body", 5), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
    }

    [Fact]
    public async Task An_archived_article_comes_back_as_a_draft_with_its_first_publish_time()
    {
        var published = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var article = _kb.StoredArticle(KbArticleStatus.Archived, categoryId: Guid.NewGuid(), publishedAt: published);

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "Revised", null, "Revised body", 5), Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe(KbArticleStatuses.Draft),
            dto => dto.PublishedAt.ShouldBe(published));
        _kb.KnowledgeBase.Received(1).UpdateArticle(article);
    }

    [Fact]
    public async Task Editing_a_published_article_keeps_it_published()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Published, publishedAt: _kb.Clock.GetUtcNow());

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "Live edit", null, "Live body", 5), Ct);

        result.Value.Status.ShouldBe(KbArticleStatuses.Published);
    }

    [Fact]
    public async Task An_unknown_article_is_not_found()
    {
        var result = await Handler().HandleAsync(Guid.NewGuid(), new UpdateKbArticleRequest(null, "T", null, "B", 1), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.NotFound),
            error => error.Code.ShouldBe("kb-article-not-found"));
    }

    [Fact]
    public async Task A_category_of_another_scope_is_refused_and_the_article_is_not_changed()
    {
        var paperplaneCategory = _kb.StoredCategory(_kb.Paperplane.Id);
        var article = _kb.StoredArticle(productId: _kb.Orbitly.Id);

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(paperplaneCategory.Id, "New title", null, "New body", 5), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-scope-mismatch");
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        article.Title.ShouldBe("Reset your password");
    }

    [Fact]
    public async Task A_blank_title_is_a_validation_error_and_an_archived_article_stays_archived()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Archived);

        var result = await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, " ", null, "Body", 5), Ct);

        result.Errors.ShouldHaveSingleItem().Target.ShouldBe("title");
        article.Status.ShouldBe(KbArticleStatus.Archived);
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_repository()
    {
        using var source = new CancellationTokenSource();
        var article = _kb.StoredArticle();
        _kb.KnowledgeBase.GetArticleAsync(article.Id, source.Token).Returns(article);

        await Handler().HandleAsync(article.Id, new UpdateKbArticleRequest(null, "T", null, "B", 5), source.Token);

        await _kb.KnowledgeBase.Received().GetArticleAsync(article.Id, source.Token);
    }
}
```

Create `tests/TechStrap.Infrastructure.IntegrationTests/KbArticleHandlerIntegrationTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Application.Agents;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Agents;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>The real create and update handlers against real Postgres: the cross-scope slug rule, version conflicts and Archived to Draft (Review Focus 4).</summary>
public sealed class KbArticleHandlerIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class StubAgentClaims : ICurrentAgentClaims
    {
        public AgentClaims? Current { get; } = new("oidc|sam", "Sam W.", "sam@example.com", AgentRole.Agent);
    }

    private PersistenceTestHost NewHost() =>
        new(Database, configure: services =>
        {
            services.AddSingleton<ICurrentAgentClaims, StubAgentClaims>();
            services.AddScoped<ICreateKbArticleRequestHandler, CreateKbArticleRequestHandler>();
            services.AddScoped<IUpdateKbArticleRequestHandler, UpdateKbArticleRequestHandler>();
            services.AddScoped<IGetKbArticleRequestHandler, GetKbArticleRequestHandler>();
        });

    private static async Task<T> InScopeAsync<T>(PersistenceTestHost host, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = host.CreateScope();
        return await work(scope.ServiceProvider);
    }

    private static Task<SyntaxCircus.Common.Result<KbArticleDto>> CreateAsync(PersistenceTestHost host, CreateKbArticleRequest request) =>
        InScopeAsync(host, sp => sp.GetRequiredService<ICreateKbArticleRequestHandler>().HandleAsync(request, Ct));

    private static Task<SyntaxCircus.Common.Result<KbArticleDto>> UpdateAsync(PersistenceTestHost host, Guid id, UpdateKbArticleRequest request) =>
        InScopeAsync(host, sp => sp.GetRequiredService<IUpdateKbArticleRequestHandler>().HandleAsync(id, request, Ct));

    [Fact]
    public async Task A_slug_is_refused_across_scopes_in_both_directions_but_free_for_another_product()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        await KbTestData.EnsureSharedCategoryAsync(host);
        var category = KbTestData.SharedCategoryId;

        (await CreateAsync(host, new CreateKbArticleRequest(null, category, "welcome", "Welcome", null, "Hello"))).IsSuccess.ShouldBeTrue();
        (await CreateAsync(host, new CreateKbArticleRequest(scenario.Acme.Id, category, "acme-guide", "Acme guide", null, "Hello"))).IsSuccess.ShouldBeTrue();

        var productReusesShared = await CreateAsync(host, new CreateKbArticleRequest(scenario.Orbitly.Id, category, "welcome", "Welcome again", null, "Hello"));
        var sharedReusesProduct = await CreateAsync(host, new CreateKbArticleRequest(null, category, "acme-guide", "Acme guide shared", null, "Hello"));
        var otherProductFree = await CreateAsync(host, new CreateKbArticleRequest(scenario.Orbitly.Id, category, "acme-guide", "Orbitly guide", null, "Hello"));

        productReusesShared.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-slug-taken");
        sharedReusesProduct.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-slug-taken");
        otherProductFree.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Two_edits_from_the_same_version_give_one_success_and_one_conflict_and_the_second_text_is_not_lost_silently()
    {
        await using var host = NewHost();
        await TicketScenario.CreateAsync(host);
        var created = (await CreateAsync(host, new CreateKbArticleRequest(null, null, "faq", "FAQ", null, "Body"))).Value;

        var first = await UpdateAsync(host, created.Id, new UpdateKbArticleRequest(null, "First edit", null, "Body 1", created.Version));
        var second = await UpdateAsync(host, created.Id, new UpdateKbArticleRequest(null, "Second edit", null, "Body 2", created.Version));

        first.IsSuccess.ShouldBeTrue();
        first.Value.Version.ShouldNotBe(created.Version);
        second.Errors.ShouldHaveSingleItem().Code.ShouldBe("concurrency-conflict");
        var stored = (await InScopeAsync(host, sp => sp.GetRequiredService<IGetKbArticleRequestHandler>().HandleAsync(created.Id, Ct))).Value;
        stored.Title.ShouldBe("First edit");
        stored.Version.ShouldBe(first.Value.Version);
    }

    [Fact]
    public async Task Editing_an_archived_article_stores_it_as_a_draft_and_keeps_the_first_publish_time()
    {
        await using var host = NewHost();
        await TicketScenario.CreateAsync(host);
        await KbTestData.EnsureSharedCategoryAsync(host);
        var created = (await CreateAsync(host, new CreateKbArticleRequest(null, KbTestData.SharedCategoryId, "old", "Old", null, "Body"))).Value;
        host.Clock.Advance(TimeSpan.FromHours(1));
        (await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<Application.Persistence.IKbRepository>();
            var article = (await kb.GetArticleAsync(created.Id, Ct))!;
            article.Publish(host.Clock).IsSuccess.ShouldBeTrue();
            article.Archive(host.Clock).IsSuccess.ShouldBeTrue();
            kb.UpdateArticle(article);
        })).IsSuccess.ShouldBeTrue();
        var archived = (await InScopeAsync(host, sp => sp.GetRequiredService<IGetKbArticleRequestHandler>().HandleAsync(created.Id, Ct))).Value;
        archived.Status.ShouldBe(KbArticleStatuses.Archived);
        host.Clock.Advance(TimeSpan.FromHours(1));

        var updated = await UpdateAsync(host, created.Id, new UpdateKbArticleRequest(KbTestData.SharedCategoryId, "Old, revised", null, "New body", archived.Version));

        updated.Value.Status.ShouldBe(KbArticleStatuses.Draft);
        updated.Value.PublishedAt.ShouldBe(archived.PublishedAt);
        var stored = (await InScopeAsync(host, sp => sp.GetRequiredService<IGetKbArticleRequestHandler>().HandleAsync(created.Id, Ct))).Value;
        stored.Status.ShouldBe(KbArticleStatuses.Draft);
        stored.Title.ShouldBe("Old, revised");
    }
}
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: the build FAILS with `CS0246` ("could not be found") for `CreateKbArticleRequestHandler`, `UpdateKbArticleRequestHandler`, `ICreateKbArticleRequestHandler`, `IUpdateKbArticleRequestHandler`, `IGetKbArticleRequestHandler` and `GetKbArticleRequestHandler`.

- [ ] **Step 3: Add the handlers and their helpers**

Create validates in this order: the signed-in agent, the Domain fields (a bad slug is a 400 before anything is looked up), the product, the category, the cross-scope slug, then stage and commit. After a successful commit the article is read again so the DTO carries the version the database assigned. Update checks the version before touching the category or the content. KB changes are not audited (D-044), so no audit event is staged. The old `TODO(Application handler)` comment on `IKbRepository.AddArticle` becomes a statement of where the category rule is enforced.

Modify `src/TechStrap.Application/Persistence/IKbRepository.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -32,10 +32,9 @@ public interface IKbRepository
     Task<PagedResult<KbArticle>> SearchPublishedAsync(PublishedKbSearchQuery query, CancellationToken cancellationToken);
 
     /// <summary>
-    /// TODO(Application handler): the repository cannot check that the article's category belongs to the article's product (the method returns
-    /// nothing and the schema has no composite key for it). The create and edit handlers must load the category and require its ProductId to
-    /// equal the article's ProductId, or be null (a shared category) when the article belongs to a product. A shared article may only use a
-    /// shared category. Return a Validation error from the handler otherwise, before calling this.
+    /// The repository cannot check that the article's category belongs to the article's product (the method returns nothing and the schema has no
+    /// composite key for it). The create and edit handlers enforce it with <c>KbCategoryRules.CheckAsync</c> (D-044): a shared article may only use a
+    /// shared category, and a product article a shared category or one of its own product.
     /// <para>
     /// Staging only. After a failed commit, reload the article and redo the change; do not retry the same object. After a successful
     /// commit the Domain <c>Version</c> is stale: reload before updating again.
@@ -43,7 +42,7 @@ public interface IKbRepository
     /// </summary>
     void AddArticle(KbArticle article);
 
-    /// <summary>See the category and product TODO on <see cref="AddArticle"/>. Throws when the article was not loaded in this unit of work. After a failed commit reload the article rather than retrying the same object; after a successful commit its <c>Version</c> is stale, so reload before updating again.</summary>
+    /// <summary>See the category and product rule on <see cref="AddArticle"/>. Throws when the article was not loaded in this unit of work. After a failed commit reload the article rather than retrying the same object; after a successful commit its <c>Version</c> is stale, so reload before updating again.</summary>
     void UpdateArticle(KbArticle article);
 
     /// <summary>
```

Modify `src/TechStrap.Application/Knowledge/KbErrors.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -1,11 +1,44 @@
 using SyntaxCircus.Common;
+using TechStrap.Application.Persistence;
 using TechStrap.Contracts.Kb;
 
 namespace TechStrap.Application.Knowledge;
 
-/// <summary>The errors the KB handlers return. The codes are part of the API contract (D-044) and the Admin matches on them.</summary>
+/// <summary>
+/// The errors the KB handlers return. The codes are part of the API contract (D-044) and the Admin matches on them. A thing named in the
+/// route that does not exist is a NotFound; a thing named in the body that does not exist is a Validation error on that field.
+/// </summary>
 internal static class KbErrors
 {
     public static ResultError PreviewTooLong() =>
         new("body-too-long", $"The text is longer than {KbLimits.MaxPreviewChars:N0} characters and cannot be previewed.", ResultErrorKind.Validation, "body");
+
+    public static ResultError ArticleNotFound() => new("kb-article-not-found", "That article does not exist.", ResultErrorKind.NotFound);
+
+    public static ResultError CategoryNotFound() => new("kb-category-not-found", "That category does not exist.", ResultErrorKind.NotFound);
+
+    public static ResultError CategoryNotFoundInBody() =>
+        new("kb-category-not-found", "That category does not exist.", ResultErrorKind.Validation, "categoryId");
+
+    public static ResultError ProductNotFoundInBody() =>
+        new("product-not-found", "That product does not exist.", ResultErrorKind.Validation, "productId");
+
+    public static ResultError StatusInvalid() =>
+        new("status-invalid", "The status must be Draft, Published or Archived.", ResultErrorKind.Validation, "status");
+
+    public static ResultError SlugTaken() =>
+        new("kb-slug-taken", "Another article already uses this slug, here or in the shared space. Choose a different one.", ResultErrorKind.Conflict);
+
+    public static ResultError CategorySlugTaken() =>
+        new("kb-category-slug-taken", "Another category already uses this slug, here or in the shared space. Choose a different one.", ResultErrorKind.Conflict);
+
+    public static ResultError CategoryScopeMismatch() =>
+        new(
+            "kb-category-scope-mismatch",
+            "A shared article can only use a shared category, and a product article only a shared category or one of its own product.",
+            ResultErrorKind.Validation,
+            "categoryId");
+
+    public static ResultError Stale(string what) =>
+        new(PersistenceErrorCodes.ConcurrencyConflict, $"This {what} changed since you opened it. Reload it and apply your change again.", ResultErrorKind.Conflict);
 }
```

Create `src/TechStrap.Application/Knowledge/KbMapping.cs`:

```csharp
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Knowledge;

internal static class KbMapping
{
    public static KbArticleDto ToDto(KbArticle article) =>
        new(
            article.Id, article.ProductId, article.CategoryId, article.Slug, article.Title, article.Summary, article.BodyMarkdown, StatusName(article.Status),
            article.AuthorId, article.CreatedAt, article.UpdatedAt, article.PublishedAt, article.Version);

    public static KbArticleListItemDto ToListItem(KbArticle article) =>
        new(article.Id, article.ProductId, article.CategoryId, article.Slug, article.Title, StatusName(article.Status), article.UpdatedAt);

    public static KbCategoryDto ToDto(KbCategory category) =>
        new(category.Id, category.ProductId, category.Slug, category.Name, category.Description, category.SortOrder, category.Version);

    public static string StatusName(KbArticleStatus status) => status switch
    {
        KbArticleStatus.Draft => KbArticleStatuses.Draft,
        KbArticleStatus.Published => KbArticleStatuses.Published,
        KbArticleStatus.Archived => KbArticleStatuses.Archived,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped article status."),
    };

    /// <summary>Case-insensitive; blank means no filter. Returns false for a name that is not a status.</summary>
    public static bool TryParseStatus(string? text, out KbArticleStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (Enum.TryParse<KbArticleStatus>(text.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            status = parsed;
            return true;
        }

        return false;
    }
}
```

Create `src/TechStrap.Application/Knowledge/KbCategoryRules.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Knowledge;

internal static class KbCategoryRules
{
    /// <summary>
    /// The category and article product match (D-044), a handler duty because the schema has no composite key for it. A shared article
    /// (no product) may only use a shared category; a product article may use a shared category or one of its own product. A missing
    /// category id passes (the article simply has none yet). Returns the error to give back, or null when the category is acceptable.
    /// </summary>
    public static async Task<ResultError?> CheckAsync(IKbRepository kb, Guid? categoryId, Guid? articleProductId, CancellationToken cancellationToken)
    {
        if (categoryId is not { } id)
        {
            return null;
        }

        var category = await kb.GetCategoryAsync(id, cancellationToken);
        if (category is null)
        {
            return KbErrors.CategoryNotFoundInBody();
        }

        return category.ProductId is null || category.ProductId == articleProductId ? null : KbErrors.CategoryScopeMismatch();
    }
}
```

Create `src/TechStrap.Application/Knowledge/GetKbArticleRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IGetKbArticleRequestHandler
{
    Task<Result<KbArticleDto>> HandleAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>GET /api/kb/articles/{id} (Agent). Any status, with the Markdown source and the concurrency version.</summary>
public sealed class GetKbArticleRequestHandler(IKbRepository knowledgeBase) : IGetKbArticleRequestHandler
{
    public async Task<Result<KbArticleDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var article = await knowledgeBase.GetArticleAsync(id, cancellationToken);
        return article is null ? Result<KbArticleDto>.Failure(KbErrors.ArticleNotFound()) : Result<KbArticleDto>.Success(KbMapping.ToDto(article));
    }
}
```

Create `src/TechStrap.Application/Knowledge/ListKbArticlesRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Knowledge;

public interface IListKbArticlesRequestHandler
{
    Task<Result<PagedResponse<KbArticleListItemDto>>> HandleAsync(ListKbArticlesRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/kb/articles (Agent). Newest update first, or best match first when <c>text</c> is set (full-text search over title, summary and
/// body). Every status is listed unless the request names one. The product filter includes shared articles unless the request turns that off.
/// </summary>
public sealed class ListKbArticlesRequestHandler(IKbRepository knowledgeBase) : IListKbArticlesRequestHandler
{
    public async Task<Result<PagedResponse<KbArticleListItemDto>>> HandleAsync(ListKbArticlesRequest request, CancellationToken cancellationToken)
    {
        if (!KbMapping.TryParseStatus(request.Status, out var status))
        {
            return Result<PagedResponse<KbArticleListItemDto>>.Failure(KbErrors.StatusInvalid());
        }

        var page = string.IsNullOrWhiteSpace(request.Text)
            ? await knowledgeBase.ListArticlesAsync(
                new KbArticleQuery(request.ProductId, request.IncludeShared, status, request.CategoryId, request.Page, request.PageSize, request.SharedOnly),
                cancellationToken)
            : await knowledgeBase.SearchAsync(
                new KbSearchQuery(request.Text, request.ProductId, request.IncludeShared, status, request.CategoryId, request.Page, request.PageSize, request.SharedOnly),
                cancellationToken);

        return Result<PagedResponse<KbArticleListItemDto>>.Success(
            new PagedResponse<KbArticleListItemDto>([.. page.Items.Select(KbMapping.ToListItem)], page.Page, page.PageSize, page.TotalCount));
    }
}
```

Create `src/TechStrap.Application/Knowledge/CreateKbArticleRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Knowledge;

public interface ICreateKbArticleRequestHandler
{
    Task<Result<KbArticleDto>> HandleAsync(CreateKbArticleRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/articles (Agent). Creates a Draft authored by the signed-in agent. A null product makes the article shared. The slug must
/// be free in the article's own scope and in the other one (D-044); the category must fit the article's scope. KB changes are not audited.
/// </summary>
public sealed class CreateKbArticleRequestHandler(
    ICurrentAgentClaims currentAgent,
    IAgentRepository agents,
    IProductRepository products,
    IKbRepository knowledgeBase,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICreateKbArticleRequestHandler
{
    public async Task<Result<KbArticleDto>> HandleAsync(CreateKbArticleRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var actor = await CurrentAgent.RequireActiveAsync(currentAgent, agents, cancellationToken);
        if (actor.IsFailure)
        {
            return Result<KbArticleDto>.Failure(actor.Errors[0]);
        }

        var created = KbArticle.Create(request.ProductId, request.CategoryId, request.Slug, request.Title, request.Summary, request.BodyMarkdown, actor.Value.Id, clock);
        if (created.IsFailure)
        {
            return Result<KbArticleDto>.Failure(created.Error!.ToError());
        }

        var article = created.Value;
        if (article.ProductId is { } productId && await products.GetByIdAsync(productId, cancellationToken) is null)
        {
            return Result<KbArticleDto>.Failure(KbErrors.ProductNotFoundInBody());
        }

        if (await KbCategoryRules.CheckAsync(knowledgeBase, article.CategoryId, article.ProductId, cancellationToken) is { } categoryError)
        {
            return Result<KbArticleDto>.Failure(categoryError);
        }

        if (await knowledgeBase.ArticleSlugTakenAsync(article.ProductId, article.Slug, cancellationToken))
        {
            return Result<KbArticleDto>.Failure(KbErrors.SlugTaken());
        }

        knowledgeBase.AddArticle(article);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            // Two agents creating the same slug at once: the unique index decides, and the loser sees the same answer as the pre-check.
            return Result<KbArticleDto>.Failure(committed.Errors[0].Code == PersistenceErrorCodes.Duplicate ? KbErrors.SlugTaken() : committed.Errors[0]);
        }

        // Reload so the DTO carries the version the database assigned.
        var saved = await knowledgeBase.GetArticleAsync(article.Id, cancellationToken) ?? article;
        return Result<KbArticleDto>.Success(KbMapping.ToDto(saved));
    }
}
```

Create `src/TechStrap.Application/Knowledge/UpdateKbArticleRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IUpdateKbArticleRequestHandler
{
    Task<Result<KbArticleDto>> HandleAsync(Guid id, UpdateKbArticleRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// PUT /api/kb/articles/{id} (Agent). Replaces the category, title, summary and body. The caller sends the version they read: a different
/// stored version is a 409, so a second agent's edit is never silently overwritten (D-026). The repository checks it again at commit.
/// Editing an Archived article returns it to Draft; editing a Published article keeps it live. The product and the slug never change.
/// </summary>
public sealed class UpdateKbArticleRequestHandler(
    IKbRepository knowledgeBase,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IUpdateKbArticleRequestHandler
{
    public async Task<Result<KbArticleDto>> HandleAsync(Guid id, UpdateKbArticleRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var article = await knowledgeBase.GetArticleAsync(id, cancellationToken);
        if (article is null)
        {
            return Result<KbArticleDto>.Failure(KbErrors.ArticleNotFound());
        }

        if (article.Version != request.Version)
        {
            return Result<KbArticleDto>.Failure(KbErrors.Stale("article"));
        }

        if (await KbCategoryRules.CheckAsync(knowledgeBase, request.CategoryId, article.ProductId, cancellationToken) is { } categoryError)
        {
            return Result<KbArticleDto>.Failure(categoryError);
        }

        var updated = article.Update(request.CategoryId, request.Title, request.Summary, request.BodyMarkdown, clock);
        if (updated.IsFailure)
        {
            return Result<KbArticleDto>.Failure(updated.Error!.ToError());
        }

        knowledgeBase.UpdateArticle(article);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<KbArticleDto>.Failure(committed.Errors[0]);
        }

        var saved = await knowledgeBase.GetArticleAsync(article.Id, cancellationToken) ?? article;
        return Result<KbArticleDto>.Success(KbMapping.ToDto(saved));
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then:

```bash
dotnet test --project tests/TechStrap.Application.Tests -c Release --no-build --filter-class "*CreateKbArticleRequestHandlerTests"
dotnet test --project tests/TechStrap.Application.Tests -c Release --no-build --filter-class "*UpdateKbArticleRequestHandlerTests"
dotnet test --project tests/TechStrap.Application.Tests -c Release --no-build --filter-class "*ListAndGetKbArticleHandlerTests"
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter-class "*KbArticleHandlerIntegrationTests"
```

Expected: PASS, 13, 9, 5 and 3 tests.

- [ ] **Step 5: Prove each pin with a mutation**

Applied to the finished code, the named tests run, the change reverted (same method as Task 1). The pairs show a pin held at two levels: the substitute-based unit test and the real-database test.

| Mutation | Failing tests |
| --- | --- |
| `UpdateKbArticleRequestHandler`: `if (article.Version != request.Version)` becomes `if (article.Version != request.Version && article.Id == Guid.Empty)` (the check never fires) | `UpdateKbArticleRequestHandlerTests.A_stale_version_is_a_conflict_and_nothing_is_saved`; `KbArticleHandlerIntegrationTests.Two_edits_from_the_same_version_give_one_success_and_one_conflict_and_the_second_text_is_not_lost_silently` |
| `CreateKbArticleRequestHandler`: the `ArticleSlugTakenAsync` check never fires (same `&& article.Id == Guid.Empty` trick) | `CreateKbArticleRequestHandlerTests.A_slug_already_used_in_either_scope_is_a_conflict_and_nothing_is_staged`; `KbArticleHandlerIntegrationTests.A_slug_is_refused_across_scopes_in_both_directions_but_free_for_another_product` (the unique index alone lets a product article and a shared article share a slug) |
| `KbCategoryRules.CheckAsync`: the last line returns `null` | `CreateKbArticleRequestHandlerTests.A_shared_article_cannot_use_a_product_category`, `CreateKbArticleRequestHandlerTests.A_product_article_cannot_use_another_products_category` |
| `KbArticle.Update` (Domain): the reopened status is `Archived` instead of `Draft` | `UpdateKbArticleRequestHandlerTests.An_archived_article_comes_back_as_a_draft_with_its_first_publish_time`; `KbArticleHandlerIntegrationTests.Editing_an_archived_article_stores_it_as_a_draft_and_keeps_the_first_publish_time` |

- [ ] **Step 6: Whole-project check**

Run: `dotnet test --solution TechStrap.CI.slnf -c Release`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Application \
  tests
git diff --cached --stat
git commit -m "feat(kb): article handlers (list, get, create, update) with the cross-scope slug rule and the version check" -m "Create makes a Draft authored by the signed-in agent: a bad slug or title is a field error, an unknown product or category is a field error, a shared article may only use a shared category and a product article a shared category or its own, and a slug used in the same scope or the other one is a 409. Update needs the version the caller read (a stale one is a 409, so no edit is silently overwritten) and returns an Archived article to Draft. List searches best match first when text is given. Proven with substitutes and against a real Postgres." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 4: Lifecycle and categories: publish, archive and the category handlers

**Review Focus pin:** Review Focus 4 (lost edits on conflict). Pinned here by `PublishAndArchiveKbArticleHandlerTests` and `KbLifecycleIntegrationTests` (a stale version stops a publish or an archive), `KbCategoryHandlerTests` and `KbLifecycleIntegrationTests` (a stale category edit is a 409; a category that holds an article is not deleted; the slug `search` is reserved; a category slug is refused across scopes).

**Files:**
- Create: `src/TechStrap.Application/Knowledge/ArchiveKbArticleRequestHandler.cs`
- Create: `src/TechStrap.Application/Knowledge/CreateKbCategoryRequestHandler.cs`
- Create: `src/TechStrap.Application/Knowledge/DeleteKbCategoryRequestHandler.cs`
- Modify: `src/TechStrap.Application/Knowledge/KbErrors.cs`
- Create: `src/TechStrap.Application/Knowledge/ListKbCategoriesRequestHandler.cs`
- Create: `src/TechStrap.Application/Knowledge/PublishKbArticleRequestHandler.cs`
- Create: `src/TechStrap.Application/Knowledge/UpdateKbCategoryRequestHandler.cs`
- Modify: `src/TechStrap.Application/Persistence/IKbRepository.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.cs`
- Test (create): `tests/TechStrap.Application.Tests/Knowledge/KbCategoryHandlerTests.cs`
- Test (create): `tests/TechStrap.Application.Tests/Knowledge/PublishAndArchiveKbArticleHandlerTests.cs`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/KbLifecycleIntegrationTests.cs`

**Interfaces:**
- Consumes: `KbArticle.Publish` and `Archive` (Task 1), `KbCategory.Create` and `Update` (Task 1), `IKbRepository` (`GetCategoryAsync`, `ListCategoriesAsync`, `AddCategory`, `UpdateCategory`, `RemoveCategory`, `CategorySlugTakenAsync`), `IProductRepository.GetByIdAsync`, `KbErrors`, `KbMapping` and `KbFixture` (Task 3), `PersistenceErrorCodes.Duplicate` and `ReferenceViolation`.
- Produces:
  - `IPublishKbArticleRequestHandler.HandleAsync(Guid id, uint? version, CancellationToken) : Task<Result<KbArticleDto>>`: 400 `kb-publish-incomplete` (target `title`, `slug`, `body` or `category`), 409 `article-already-published`, 404, and 409 `concurrency-conflict` when `version` is given and stale. The first publication date survives a re-publish.
  - `IArchiveKbArticleRequestHandler.HandleAsync(Guid id, uint? version, CancellationToken) : Task<Result<KbArticleDto>>`: 409 `article-already-archived`, 404, 409 when the version is stale.
  - `IListKbCategoriesRequestHandler.HandleAsync(Guid? productId, bool includeShared, CancellationToken) : Task<Result<IReadOnlyList<KbCategoryDto>>>`.
  - `ICreateKbCategoryRequestHandler.HandleAsync(CreateKbCategoryRequest, CancellationToken) : Task<Result<KbCategoryDto>>`: `kb-category-reserved-slug` (Validation, target `slug`), `product-not-found`, `kb-category-slug-taken` (409, any scope).
  - `IUpdateKbCategoryRequestHandler.HandleAsync(Guid id, UpdateKbCategoryRequest, CancellationToken) : Task<Result<KbCategoryDto>>`: 404 `kb-category-not-found`, 409 `concurrency-conflict`.
  - `IDeleteKbCategoryRequestHandler.HandleAsync(Guid id, CancellationToken) : Task<Result>`: 409 `kb-category-in-use` while any article of any status uses it (also when the foreign key refuses it at commit).
  - `Task<bool> IKbRepository.CategoryHasArticlesAsync(Guid categoryId, CancellationToken)`.

- [ ] **Step 1: Write the failing tests**

Same split as Task 3: substitutes for the outcomes, a real Postgres for the flow (draft without a category cannot be published, publish then archive then publish again keeps the first date, a category with an archived article still blocks its delete until the article moves).

Create `tests/TechStrap.Application.Tests/Knowledge/KbCategoryHandlerTests.cs`:

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class KbCategoryHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();

    private CreateKbCategoryRequestHandler Creator(params Result[] commits) => new(_kb.Products, _kb.KnowledgeBase, UnitOfWorkSubstitute.Create(commits), _kb.Clock);

    [Fact]
    public async Task A_category_is_created_with_its_description_and_the_stored_version()
    {
        _kb.KnowledgeBase.GetCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
            KbCategory.Restore(call.Arg<Guid>(), _kb.Orbitly.Id, "Account", "account", "Sign-in help", 2, 11));

        var result = await Creator().HandleAsync(new CreateKbCategoryRequest(_kb.Orbitly.Id, "account", "Account", "Sign-in help", 2), Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Slug.ShouldBe("account"),
            dto => dto.Description.ShouldBe("Sign-in help"),
            dto => dto.SortOrder.ShouldBe(2),
            dto => dto.Version.ShouldBe(11u));
        _kb.KnowledgeBase.Received(1).AddCategory(Arg.Is<KbCategory>(c => c.Slug == "account" && c.ProductId == _kb.Orbitly.Id));
    }

    [Fact]
    public async Task The_search_slug_is_reserved_in_any_scope()
    {
        var result = await Creator().HandleAsync(new CreateKbCategoryRequest(null, "search", "Search", null, 0), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("kb-category-reserved-slug"),
            error => error.Target.ShouldBe("slug"));
        _kb.KnowledgeBase.DidNotReceive().AddCategory(Arg.Any<KbCategory>());
    }

    [Fact]
    public async Task A_slug_used_in_either_scope_is_a_conflict_and_a_duplicate_at_commit_is_the_same_conflict()
    {
        _kb.KnowledgeBase.CategorySlugTakenAsync(null, "faq", Arg.Any<CancellationToken>()).Returns(true);

        var pre = await Creator().HandleAsync(new CreateKbCategoryRequest(null, "faq", "FAQ", null, 0), Ct);
        var race = await Creator(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.Duplicate)).HandleAsync(new CreateKbCategoryRequest(null, "other", "Other", null, 0), Ct);

        pre.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(error => error.Kind.ShouldBe(ResultErrorKind.Conflict), error => error.Code.ShouldBe("kb-category-slug-taken"));
        race.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-slug-taken");
        _kb.KnowledgeBase.DidNotReceive().AddCategory(Arg.Is<KbCategory>(c => c.Slug == "faq"));
    }

    [Fact]
    public async Task An_unknown_product_is_a_validation_error_on_product_id()
    {
        var result = await Creator().HandleAsync(new CreateKbCategoryRequest(Guid.NewGuid(), "faq", "FAQ", null, 0), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(error => error.Code.ShouldBe("product-not-found"), error => error.Target.ShouldBe("productId"));
    }

    [Fact]
    public async Task An_update_changes_name_description_and_sort_order()
    {
        var category = _kb.StoredCategory(null);
        var handler = new UpdateKbCategoryRequestHandler(_kb.KnowledgeBase, UnitOfWorkSubstitute.Create());

        var result = await handler.HandleAsync(category.Id, new UpdateKbCategoryRequest("Sign in", "Help", 4, 3), Ct);

        result.Value.ShouldSatisfyAllConditions(dto => dto.Name.ShouldBe("Sign in"), dto => dto.Description.ShouldBe("Help"), dto => dto.SortOrder.ShouldBe(4), dto => dto.Slug.ShouldBe("account"));
        _kb.KnowledgeBase.Received(1).UpdateCategory(category);
    }

    [Fact]
    public async Task A_stale_category_version_is_a_conflict_and_an_unknown_category_is_not_found()
    {
        var category = _kb.StoredCategory(null);
        var handler = new UpdateKbCategoryRequestHandler(_kb.KnowledgeBase, UnitOfWorkSubstitute.Create());

        var stale = await handler.HandleAsync(category.Id, new UpdateKbCategoryRequest("Sign in", null, 1, 2), Ct);
        var missing = await handler.HandleAsync(Guid.NewGuid(), new UpdateKbCategoryRequest("Sign in", null, 1, 1), Ct);

        stale.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        missing.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
        _kb.KnowledgeBase.DidNotReceive().UpdateCategory(Arg.Any<KbCategory>());
    }

    [Fact]
    public async Task A_blank_name_is_a_validation_error_on_name()
    {
        var category = _kb.StoredCategory(null);
        var handler = new UpdateKbCategoryRequestHandler(_kb.KnowledgeBase, UnitOfWorkSubstitute.Create());

        var result = await handler.HandleAsync(category.Id, new UpdateKbCategoryRequest(" ", null, 1, 3), Ct);

        result.Errors.ShouldHaveSingleItem().Target.ShouldBe("name");
    }

    [Fact]
    public async Task A_category_holding_articles_is_not_deleted()
    {
        var category = _kb.StoredCategory(null);
        _kb.KnowledgeBase.CategoryHasArticlesAsync(category.Id, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new DeleteKbCategoryRequestHandler(_kb.KnowledgeBase, UnitOfWorkSubstitute.Create()).HandleAsync(category.Id, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(error => error.Kind.ShouldBe(ResultErrorKind.Conflict), error => error.Code.ShouldBe("kb-category-in-use"));
        _kb.KnowledgeBase.DidNotReceive().RemoveCategory(Arg.Any<KbCategory>());
    }

    [Fact]
    public async Task An_empty_category_is_deleted_and_a_reference_violation_at_commit_is_the_same_conflict()
    {
        var category = _kb.StoredCategory(null);

        var deleted = await new DeleteKbCategoryRequestHandler(_kb.KnowledgeBase, UnitOfWorkSubstitute.Create()).HandleAsync(category.Id, Ct);
        var raced = await new DeleteKbCategoryRequestHandler(_kb.KnowledgeBase, UnitOfWorkSubstitute.Create(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ReferenceViolation)))
            .HandleAsync(category.Id, Ct);

        deleted.IsSuccess.ShouldBeTrue();
        _kb.KnowledgeBase.Received(2).RemoveCategory(category);
        raced.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-in-use");
    }

    [Fact]
    public async Task Deleting_an_unknown_category_is_not_found()
    {
        var result = await new DeleteKbCategoryRequestHandler(_kb.KnowledgeBase, UnitOfWorkSubstitute.Create()).HandleAsync(Guid.NewGuid(), Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
    }

    [Fact]
    public async Task Listing_forwards_the_product_and_shared_choice_and_maps_each_category()
    {
        var category = KbCategory.Restore(Guid.NewGuid(), null, "Account", "account", null, 1, 3);
        _kb.KnowledgeBase.ListCategoriesAsync(_kb.Orbitly.Id, false, Arg.Any<CancellationToken>()).Returns([category]);

        var result = await new ListKbCategoriesRequestHandler(_kb.KnowledgeBase).HandleAsync(_kb.Orbitly.Id, false, Ct);

        result.Value.ShouldHaveSingleItem().ShouldBe(new KbCategoryDto(category.Id, null, "account", "Account", null, 1, 3));
    }
}
```

Create `tests/TechStrap.Application.Tests/Knowledge/PublishAndArchiveKbArticleHandlerTests.cs`:

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Application.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class PublishAndArchiveKbArticleHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();

    private PublishKbArticleRequestHandler Publisher(params Result[] commits) => new(_kb.KnowledgeBase, UnitOfWorkSubstitute.Create(commits), _kb.Clock);

    private ArchiveKbArticleRequestHandler Archiver(params Result[] commits) => new(_kb.KnowledgeBase, UnitOfWorkSubstitute.Create(commits), _kb.Clock);

    [Fact]
    public async Task Publishing_a_complete_draft_sets_the_publish_time_and_stages_it()
    {
        var article = _kb.StoredArticle(categoryId: Guid.NewGuid());
        _kb.Clock.Advance(TimeSpan.FromHours(1));

        var result = await Publisher().HandleAsync(article.Id, null, Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe(KbArticleStatuses.Published),
            dto => dto.PublishedAt.ShouldBe(_kb.Clock.GetUtcNow()));
        _kb.KnowledgeBase.Received(1).UpdateArticle(article);
    }

    [Fact]
    public async Task Publishing_without_a_category_is_a_validation_error_on_category_and_nothing_is_staged()
    {
        var article = _kb.StoredArticle(categoryId: null);

        var result = await Publisher().HandleAsync(article.Id, null, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Validation),
            error => error.Code.ShouldBe("kb-publish-incomplete"),
            error => error.Target.ShouldBe("category"));
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(Arg.Any<KbArticle>());
        article.Status.ShouldBe(KbArticleStatus.Draft);
    }

    [Fact]
    public async Task Publishing_a_published_article_is_a_conflict()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Published, categoryId: Guid.NewGuid(), publishedAt: _kb.Clock.GetUtcNow());

        var result = await Publisher().HandleAsync(article.Id, null, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.Conflict),
            error => error.Code.ShouldBe("article-already-published"));
    }

    [Fact]
    public async Task Republishing_an_archived_article_keeps_the_first_publish_time()
    {
        var first = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var article = _kb.StoredArticle(KbArticleStatus.Archived, categoryId: Guid.NewGuid(), publishedAt: first);

        var result = await Publisher().HandleAsync(article.Id, null, Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe(KbArticleStatuses.Published),
            dto => dto.PublishedAt.ShouldBe(first));
    }

    [Fact]
    public async Task A_stale_version_stops_a_publish_and_a_matching_one_goes_through()
    {
        var article = _kb.StoredArticle(categoryId: Guid.NewGuid());

        var stale = await Publisher().HandleAsync(article.Id, 4, Ct);
        var current = await Publisher().HandleAsync(article.Id, 5, Ct);

        stale.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        current.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Publish_and_archive_of_an_unknown_article_are_not_found()
    {
        (await Publisher().HandleAsync(Guid.NewGuid(), null, Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
        (await Archiver().HandleAsync(Guid.NewGuid(), null, Ct)).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
    }

    [Fact]
    public async Task Archiving_a_published_article_archives_it_and_keeps_the_publish_time()
    {
        var published = _kb.Clock.GetUtcNow();
        var article = _kb.StoredArticle(KbArticleStatus.Published, categoryId: Guid.NewGuid(), publishedAt: published);
        _kb.Clock.Advance(TimeSpan.FromHours(2));

        var result = await Archiver().HandleAsync(article.Id, null, Ct);

        result.Value.ShouldSatisfyAllConditions(
            dto => dto.Status.ShouldBe(KbArticleStatuses.Archived),
            dto => dto.PublishedAt.ShouldBe(published),
            dto => dto.UpdatedAt.ShouldBe(_kb.Clock.GetUtcNow()));
        _kb.KnowledgeBase.Received(1).UpdateArticle(article);
    }

    [Fact]
    public async Task Archiving_an_archived_article_is_a_conflict_and_a_stale_version_stops_an_archive()
    {
        var archived = _kb.StoredArticle(KbArticleStatus.Archived);
        var draft = _kb.StoredArticle();

        var twice = await Archiver().HandleAsync(archived.Id, null, Ct);
        var stale = await Archiver().HandleAsync(draft.Id, 1, Ct);

        twice.Errors.ShouldHaveSingleItem().Code.ShouldBe("article-already-archived");
        stale.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
        _kb.KnowledgeBase.DidNotReceive().UpdateArticle(draft);
    }

    [Fact]
    public async Task A_conflict_found_at_commit_is_passed_through()
    {
        var article = _kb.StoredArticle(categoryId: Guid.NewGuid());

        var result = await Publisher(UnitOfWorkSubstitute.Conflict(PersistenceErrorCodes.ConcurrencyConflict)).HandleAsync(article.Id, null, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe(PersistenceErrorCodes.ConcurrencyConflict);
    }
}
```

Create `tests/TechStrap.Infrastructure.IntegrationTests/KbLifecycleIntegrationTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Agents;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Agents;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>Publish, archive and the category handlers against real Postgres.</summary>
public sealed class KbLifecycleIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed class StubAgentClaims : ICurrentAgentClaims
    {
        public AgentClaims? Current { get; } = new("oidc|sam", "Sam W.", "sam@example.com", AgentRole.Agent);
    }

    private PersistenceTestHost NewHost() =>
        new(Database, configure: services =>
        {
            services.AddSingleton<ICurrentAgentClaims, StubAgentClaims>();
            services.AddScoped<ICreateKbArticleRequestHandler, CreateKbArticleRequestHandler>();
            services.AddScoped<IUpdateKbArticleRequestHandler, UpdateKbArticleRequestHandler>();
            services.AddScoped<IPublishKbArticleRequestHandler, PublishKbArticleRequestHandler>();
            services.AddScoped<IArchiveKbArticleRequestHandler, ArchiveKbArticleRequestHandler>();
            services.AddScoped<ICreateKbCategoryRequestHandler, CreateKbCategoryRequestHandler>();
            services.AddScoped<IUpdateKbCategoryRequestHandler, UpdateKbCategoryRequestHandler>();
            services.AddScoped<IDeleteKbCategoryRequestHandler, DeleteKbCategoryRequestHandler>();
        });

    private static async Task<T> RunAsync<THandler, T>(PersistenceTestHost host, Func<THandler, Task<T>> call)
        where THandler : notnull
    {
        await using var scope = host.CreateScope();
        return await call(scope.ServiceProvider.GetRequiredService<THandler>());
    }

    private static Task<Result<KbCategoryDto>> CreateCategoryAsync(PersistenceTestHost host, Guid? productId, string slug) =>
        RunAsync<ICreateKbCategoryRequestHandler, Result<KbCategoryDto>>(host, h => h.HandleAsync(new CreateKbCategoryRequest(productId, slug, slug, "About " + slug, 1), Ct));

    private static Task<Result<KbArticleDto>> CreateArticleAsync(PersistenceTestHost host, Guid? productId, Guid? categoryId, string slug) =>
        RunAsync<ICreateKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(new CreateKbArticleRequest(productId, categoryId, slug, "Title " + slug, null, "Body"), Ct));

    [Fact]
    public async Task A_draft_without_a_category_cannot_be_published_and_one_with_a_category_can_be_published_archived_and_published_again()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var category = (await CreateCategoryAsync(host, scenario.Acme.Id, "getting-started")).Value;
        var bare = (await CreateArticleAsync(host, scenario.Acme.Id, null, "bare")).Value;
        var complete = (await CreateArticleAsync(host, scenario.Acme.Id, category.Id, "complete")).Value;

        var incomplete = await RunAsync<IPublishKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(bare.Id, bare.Version, Ct));
        host.Clock.Advance(TimeSpan.FromHours(1));
        var published = await RunAsync<IPublishKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(complete.Id, complete.Version, Ct));
        host.Clock.Advance(TimeSpan.FromHours(1));
        var archived = await RunAsync<IArchiveKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(complete.Id, published.Value.Version, Ct));
        host.Clock.Advance(TimeSpan.FromHours(1));
        var again = await RunAsync<IPublishKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(complete.Id, archived.Value.Version, Ct));

        incomplete.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(error => error.Code.ShouldBe("kb-publish-incomplete"), error => error.Target.ShouldBe("category"));
        published.Value.Status.ShouldBe(KbArticleStatuses.Published);
        published.Value.Version.ShouldNotBe(complete.Version);
        archived.Value.Status.ShouldBe(KbArticleStatuses.Archived);
        again.Value.Status.ShouldBe(KbArticleStatuses.Published);
        again.Value.PublishedAt.ShouldBe(published.Value.PublishedAt);
    }

    [Fact]
    public async Task A_stale_version_on_publish_is_a_conflict_and_changes_nothing()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var category = (await CreateCategoryAsync(host, scenario.Acme.Id, "faq")).Value;
        var article = (await CreateArticleAsync(host, scenario.Acme.Id, category.Id, "faq-one")).Value;
        var edited = (await RunAsync<IUpdateKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(article.Id, new UpdateKbArticleRequest(category.Id, "Edited", null, "Body", article.Version), Ct))).Value;

        var stale = await RunAsync<IPublishKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(article.Id, article.Version, Ct));

        stale.Errors.ShouldHaveSingleItem().Code.ShouldBe("concurrency-conflict");
        (await RunAsync<IPublishKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(article.Id, edited.Version, Ct))).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_category_that_holds_an_article_of_any_status_cannot_be_deleted_until_the_article_moves()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        var used = (await CreateCategoryAsync(host, scenario.Acme.Id, "used")).Value;
        var other = (await CreateCategoryAsync(host, scenario.Acme.Id, "other")).Value;
        var article = (await CreateArticleAsync(host, scenario.Acme.Id, used.Id, "holder")).Value;
        var archived = (await RunAsync<IArchiveKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(article.Id, article.Version, Ct))).Value;

        var blocked = await RunAsync<IDeleteKbCategoryRequestHandler, Result>(host, h => h.HandleAsync(used.Id, Ct));
        await RunAsync<IUpdateKbArticleRequestHandler, Result<KbArticleDto>>(host, h => h.HandleAsync(article.Id, new UpdateKbArticleRequest(other.Id, "Moved", null, "Body", archived.Version), Ct));
        var deleted = await RunAsync<IDeleteKbCategoryRequestHandler, Result>(host, h => h.HandleAsync(used.Id, Ct));

        blocked.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-in-use");
        deleted.IsSuccess.ShouldBeTrue();
        (await RunAsync<IDeleteKbCategoryRequestHandler, Result>(host, h => h.HandleAsync(used.Id, Ct))).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-not-found");
    }

    [Fact]
    public async Task Category_slugs_are_refused_across_scopes_and_the_search_slug_is_reserved()
    {
        await using var host = NewHost();
        var scenario = await TicketScenario.CreateAsync(host);
        (await CreateCategoryAsync(host, null, "shared-cat")).IsSuccess.ShouldBeTrue();
        (await CreateCategoryAsync(host, scenario.Acme.Id, "acme-cat")).IsSuccess.ShouldBeTrue();

        (await CreateCategoryAsync(host, scenario.Orbitly.Id, "shared-cat")).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-slug-taken");
        (await CreateCategoryAsync(host, null, "acme-cat")).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-slug-taken");
        (await CreateCategoryAsync(host, scenario.Orbitly.Id, "acme-cat")).IsSuccess.ShouldBeTrue();
        (await CreateCategoryAsync(host, scenario.Orbitly.Id, "search")).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-category-reserved-slug");
    }

    [Fact]
    public async Task Two_category_edits_from_the_same_version_give_one_conflict_and_the_description_is_stored()
    {
        await using var host = NewHost();
        await TicketScenario.CreateAsync(host);
        var category = (await CreateCategoryAsync(host, null, "faq")).Value;

        var first = await RunAsync<IUpdateKbCategoryRequestHandler, Result<KbCategoryDto>>(host, h => h.HandleAsync(category.Id, new UpdateKbCategoryRequest("First", "Described", 2, category.Version), Ct));
        var second = await RunAsync<IUpdateKbCategoryRequestHandler, Result<KbCategoryDto>>(host, h => h.HandleAsync(category.Id, new UpdateKbCategoryRequest("Second", null, 3, category.Version), Ct));

        first.Value.ShouldSatisfyAllConditions(dto => dto.Name.ShouldBe("First"), dto => dto.Description.ShouldBe("Described"), dto => dto.Version.ShouldNotBe(category.Version));
        second.Errors.ShouldHaveSingleItem().Code.ShouldBe("concurrency-conflict");
    }
}
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: the build FAILS with `CS0246` ("could not be found") for `PublishKbArticleRequestHandler`, `ArchiveKbArticleRequestHandler`, `CreateKbCategoryRequestHandler`, `UpdateKbCategoryRequestHandler`, `IDeleteKbCategoryRequestHandler` and the other new handler interfaces.

- [ ] **Step 3: Add the repository check, the errors and the handlers**

Publish and archive load the article, compare the optional version, call the Domain method (which owns the lifecycle rules) and commit; the DTO is read again after the commit. Category delete asks `CategoryHasArticlesAsync` first and also maps a `reference-violation` at commit to the same 409, so a race gives the same answer. The Admin-only rule for the delete is the controller's policy (Task 7); the handler stays role-free like the tag handlers.

Modify `src/TechStrap.Application/Persistence/IKbRepository.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -55,6 +55,9 @@ public interface IKbRepository
     /// <summary>The same cross-scope rule for category slugs (D-044), so a portal category address is never ambiguous.</summary>
     Task<bool> CategorySlugTakenAsync(Guid? productId, string slug, CancellationToken cancellationToken);
 
+    /// <summary>True when any article, in any status, uses the category. The category delete handler asks this before removing it (the foreign key is the backstop).</summary>
+    Task<bool> CategoryHasArticlesAsync(Guid categoryId, CancellationToken cancellationToken);
+
     Task<KbCategory?> GetCategoryAsync(Guid id, CancellationToken cancellationToken);
 
     /// <summary>Ordered by sort order then name. With a product, <paramref name="includeShared"/> adds the shared categories; without one, every category is returned.</summary>
```

Modify `src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -123,6 +123,9 @@ internal sealed class KbRepository(TechStrapDbContext context) : IKbRepository
             ? context.Set<KbCategoryRecord>().AnyAsync(c => c.Slug == slug && (c.ProductId == product || c.ProductId == null), cancellationToken)
             : context.Set<KbCategoryRecord>().AnyAsync(c => c.Slug == slug, cancellationToken);
 
+    public Task<bool> CategoryHasArticlesAsync(Guid categoryId, CancellationToken cancellationToken) =>
+        context.Set<KbArticleRecord>().AnyAsync(a => a.CategoryId == categoryId, cancellationToken);
+
     public async Task<KbCategory?> GetCategoryAsync(Guid id, CancellationToken cancellationToken) =>
         (await context.Set<KbCategoryRecord>().FirstOrDefaultAsync(c => c.Id == id, cancellationToken))?.ToDomain();
 
```

Modify `src/TechStrap.Application/Knowledge/KbErrors.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -39,6 +39,9 @@ internal static class KbErrors
             ResultErrorKind.Validation,
             "categoryId");
 
+    public static ResultError CategoryInUse() =>
+        new("kb-category-in-use", "This category still holds articles. Move its articles to another category first.", ResultErrorKind.Conflict);
+
     public static ResultError Stale(string what) =>
         new(PersistenceErrorCodes.ConcurrencyConflict, $"This {what} changed since you opened it. Reload it and apply your change again.", ResultErrorKind.Conflict);
 }
```

Create `src/TechStrap.Application/Knowledge/PublishKbArticleRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IPublishKbArticleRequestHandler
{
    Task<Result<KbArticleDto>> HandleAsync(Guid id, uint? version, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/articles/{id}/publish (Agent). Needs a title, a slug, a body and a category (the portal address carries the category slug); a
/// missing one is a 400 <c>kb-publish-incomplete</c> whose target names the field. The first publication date is kept when an Archived article is
/// published again. The caller may send the version they last read; a different stored version is a 409, so they never publish text they have not seen.
/// </summary>
public sealed class PublishKbArticleRequestHandler(IKbRepository knowledgeBase, IUnitOfWork unitOfWork, TimeProvider clock) : IPublishKbArticleRequestHandler
{
    public async Task<Result<KbArticleDto>> HandleAsync(Guid id, uint? version, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var article = await knowledgeBase.GetArticleAsync(id, cancellationToken);
        if (article is null)
        {
            return Result<KbArticleDto>.Failure(KbErrors.ArticleNotFound());
        }

        if (version is { } expected && expected != article.Version)
        {
            return Result<KbArticleDto>.Failure(KbErrors.Stale("article"));
        }

        var published = article.Publish(clock);
        if (published.IsFailure)
        {
            return Result<KbArticleDto>.Failure(published.Error!.ToError());
        }

        knowledgeBase.UpdateArticle(article);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<KbArticleDto>.Failure(committed.Errors[0]);
        }

        var saved = await knowledgeBase.GetArticleAsync(article.Id, cancellationToken) ?? article;
        return Result<KbArticleDto>.Success(KbMapping.ToDto(saved));
    }
}
```

Create `src/TechStrap.Application/Knowledge/ArchiveKbArticleRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IArchiveKbArticleRequestHandler
{
    Task<Result<KbArticleDto>> HandleAsync(Guid id, uint? version, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/articles/{id}/archive (Agent). Takes the article out of public search, the article page, the sitemap and the category counts. The row
/// and its first publication date stay. Archiving an Archived article is a 409. The version is optional, as for publish.
/// </summary>
public sealed class ArchiveKbArticleRequestHandler(IKbRepository knowledgeBase, IUnitOfWork unitOfWork, TimeProvider clock) : IArchiveKbArticleRequestHandler
{
    public async Task<Result<KbArticleDto>> HandleAsync(Guid id, uint? version, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var article = await knowledgeBase.GetArticleAsync(id, cancellationToken);
        if (article is null)
        {
            return Result<KbArticleDto>.Failure(KbErrors.ArticleNotFound());
        }

        if (version is { } expected && expected != article.Version)
        {
            return Result<KbArticleDto>.Failure(KbErrors.Stale("article"));
        }

        var archived = article.Archive(clock);
        if (archived.IsFailure)
        {
            return Result<KbArticleDto>.Failure(archived.Error!.ToError());
        }

        knowledgeBase.UpdateArticle(article);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<KbArticleDto>.Failure(committed.Errors[0]);
        }

        var saved = await knowledgeBase.GetArticleAsync(article.Id, cancellationToken) ?? article;
        return Result<KbArticleDto>.Success(KbMapping.ToDto(saved));
    }
}
```

Create `src/TechStrap.Application/Knowledge/ListKbCategoriesRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IListKbCategoriesRequestHandler
{
    Task<Result<IReadOnlyList<KbCategoryDto>>> HandleAsync(Guid? productId, bool includeShared, CancellationToken cancellationToken);
}

/// <summary>GET /api/kb/categories (Agent). Sort order, then name. Without a product every category is listed; with one, its own plus the shared ones unless that is turned off.</summary>
public sealed class ListKbCategoriesRequestHandler(IKbRepository knowledgeBase) : IListKbCategoriesRequestHandler
{
    public async Task<Result<IReadOnlyList<KbCategoryDto>>> HandleAsync(Guid? productId, bool includeShared, CancellationToken cancellationToken)
    {
        var categories = await knowledgeBase.ListCategoriesAsync(productId, includeShared, cancellationToken);
        return Result<IReadOnlyList<KbCategoryDto>>.Success([.. categories.Select(KbMapping.ToDto)]);
    }
}
```

Create `src/TechStrap.Application/Knowledge/CreateKbCategoryRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Knowledge;

public interface ICreateKbCategoryRequestHandler
{
    Task<Result<KbCategoryDto>> HandleAsync(CreateKbCategoryRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/categories (Agent). The slug and the product are permanent. The slug must be free here and in the other scope (D-044), and
/// <c>search</c> is reserved (a Domain rule). A null product makes the category shared.
/// </summary>
public sealed class CreateKbCategoryRequestHandler(
    IProductRepository products,
    IKbRepository knowledgeBase,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICreateKbCategoryRequestHandler
{
    public async Task<Result<KbCategoryDto>> HandleAsync(CreateKbCategoryRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var created = KbCategory.Create(request.ProductId, request.Slug, request.Name, request.SortOrder, clock, request.Description);
        if (created.IsFailure)
        {
            return Result<KbCategoryDto>.Failure(created.Error!.ToError());
        }

        var category = created.Value;
        if (category.ProductId is { } productId && await products.GetByIdAsync(productId, cancellationToken) is null)
        {
            return Result<KbCategoryDto>.Failure(KbErrors.ProductNotFoundInBody());
        }

        if (await knowledgeBase.CategorySlugTakenAsync(category.ProductId, category.Slug, cancellationToken))
        {
            return Result<KbCategoryDto>.Failure(KbErrors.CategorySlugTaken());
        }

        knowledgeBase.AddCategory(category);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<KbCategoryDto>.Failure(committed.Errors[0].Code == PersistenceErrorCodes.Duplicate ? KbErrors.CategorySlugTaken() : committed.Errors[0]);
        }

        var saved = await knowledgeBase.GetCategoryAsync(category.Id, cancellationToken) ?? category;
        return Result<KbCategoryDto>.Success(KbMapping.ToDto(saved));
    }
}
```

Create `src/TechStrap.Application/Knowledge/UpdateKbCategoryRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Application.Results;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IUpdateKbCategoryRequestHandler
{
    Task<Result<KbCategoryDto>> HandleAsync(Guid id, UpdateKbCategoryRequest request, CancellationToken cancellationToken);
}

/// <summary>PUT /api/kb/categories/{id} (Agent). Changes the name, description and sort order. The caller sends the version they read; a stale one is a 409 (D-026).</summary>
public sealed class UpdateKbCategoryRequestHandler(IKbRepository knowledgeBase, IUnitOfWork unitOfWork) : IUpdateKbCategoryRequestHandler
{
    public async Task<Result<KbCategoryDto>> HandleAsync(Guid id, UpdateKbCategoryRequest request, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var category = await knowledgeBase.GetCategoryAsync(id, cancellationToken);
        if (category is null)
        {
            return Result<KbCategoryDto>.Failure(KbErrors.CategoryNotFound());
        }

        if (category.Version != request.Version)
        {
            return Result<KbCategoryDto>.Failure(KbErrors.Stale("category"));
        }

        var updated = category.Update(request.Name, request.Description, request.SortOrder);
        if (updated.IsFailure)
        {
            return Result<KbCategoryDto>.Failure(updated.Error!.ToError());
        }

        knowledgeBase.UpdateCategory(category);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result<KbCategoryDto>.Failure(committed.Errors[0]);
        }

        var saved = await knowledgeBase.GetCategoryAsync(category.Id, cancellationToken) ?? category;
        return Result<KbCategoryDto>.Success(KbMapping.ToDto(saved));
    }
}
```

Create `src/TechStrap.Application/Knowledge/DeleteKbCategoryRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;

namespace TechStrap.Application.Knowledge;

public interface IDeleteKbCategoryRequestHandler
{
    Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// DELETE /api/kb/categories/{id} (Admin, D-022). A category that still holds articles, in any status, is a 409 <c>kb-category-in-use</c>; there is no
/// reassign flow. The database foreign key backs the check up, so a race ends in the same answer.
/// </summary>
public sealed class DeleteKbCategoryRequestHandler(IKbRepository knowledgeBase, IUnitOfWork unitOfWork) : IDeleteKbCategoryRequestHandler
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var scope = await unitOfWork.BeginAsync(cancellationToken);
        var category = await knowledgeBase.GetCategoryAsync(id, cancellationToken);
        if (category is null)
        {
            return Result.Failure(KbErrors.CategoryNotFound());
        }

        if (await knowledgeBase.CategoryHasArticlesAsync(category.Id, cancellationToken))
        {
            return Result.Failure(KbErrors.CategoryInUse());
        }

        knowledgeBase.RemoveCategory(category);
        var committed = await scope.CommitAsync(cancellationToken);
        if (committed.IsFailure)
        {
            return Result.Failure(committed.Errors[0].Code == PersistenceErrorCodes.ReferenceViolation ? KbErrors.CategoryInUse() : committed.Errors[0]);
        }

        return Result.Success();
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then:

```bash
dotnet test --project tests/TechStrap.Application.Tests -c Release --no-build --filter-class "*PublishAndArchiveKbArticleHandlerTests"
dotnet test --project tests/TechStrap.Application.Tests -c Release --no-build --filter-class "*KbCategoryHandlerTests"
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter-class "*KbLifecycleIntegrationTests"
```

Expected: PASS, 9, 11 and 5 tests.

- [ ] **Step 5: Prove each pin with a mutation**

Applied to the finished code, the named tests run, the change reverted (same method as Task 1).

| Mutation | Failing tests |
| --- | --- |
| `PublishKbArticleRequestHandler`: the optional-version check never fires | `PublishAndArchiveKbArticleHandlerTests.A_stale_version_stops_a_publish_and_a_matching_one_goes_through`; `KbLifecycleIntegrationTests.A_stale_version_on_publish_is_a_conflict_and_changes_nothing` |
| `ArchiveKbArticleRequestHandler`: the optional-version check never fires | `PublishAndArchiveKbArticleHandlerTests.Archiving_an_archived_article_is_a_conflict_and_a_stale_version_stops_an_archive` |
| `UpdateKbCategoryRequestHandler`: `category.Version != request.Version` never fires | `KbCategoryHandlerTests.A_stale_category_version_is_a_conflict_and_an_unknown_category_is_not_found`; `KbLifecycleIntegrationTests.Two_category_edits_from_the_same_version_give_one_conflict_and_the_description_is_stored` |
| `DeleteKbCategoryRequestHandler`: the `CategoryHasArticlesAsync` check never fires | `KbCategoryHandlerTests.A_category_holding_articles_is_not_deleted` |

One mutation does not fail a test, on purpose: making `CategoryHasArticlesAsync` ignore Archived articles still leaves `A_category_that_holds_an_article_of_any_status_cannot_be_deleted_until_the_article_moves` green, because the foreign key from articles to categories refuses the delete anyway and the handler maps it to the same `kb-category-in-use`. The handler check gives the early, cheap answer; the foreign key is the backstop.

- [ ] **Step 6: Whole-project check**

Run: `dotnet test --solution TechStrap.CI.slnf -c Release`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/TechStrap.Application \
  src/TechStrap.Infrastructure \
  tests
git diff --cached --stat
git commit -m "feat(kb): publish, archive and category handlers" -m "Publish needs a title, a slug, a body and a category (a field error otherwise) and keeps the first publication date; archive takes an article out of public view. Both accept an optional version and answer 409 when it is stale. Categories can be listed, created (the slug search is reserved, a slug used in any scope is a 409), updated with their version, and deleted only while no article of any status uses them (the foreign key is the backstop)." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 5: Images: `IKbImageStore`, the upload handler, public image serving and `TECHSTRAP_API_PUBLIC_URL`

**Review Focus pin:** Review Focus 2 (image upload abuse). Pinned here by `KbImageStoreTests` (a real Local storage provider: SVG, html, a gif that is really a page, a png or jpeg with a script inside, a file with only a png signature, an oversize file with a lying length, a spoofed type, and thirteen path-traversal names are all refused, and a stored key is random and the store's own), `KbImageServingTests` (anonymous GET, `nosniff`, the CSP sandbox, a one-year immutable cache, no cookie or challenge, a bearer token changes nothing, no name outside the store's shape is served) and `ApiPublicUrlTests`. Task 7 adds the upload endpoint and its 413, 415 and wrong-field answers.

**Files:**
- Modify: `deploy/.env.api.example`
- Modify: `docs/self-hosting/DEPLOYMENT.md`
- Test (modify): `scripts/tests/ConfigContract.Tests.ps1`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`
- Modify: `src/TechStrap.Api/.env.example`
- Create: `src/TechStrap.Api/Options/ApiPublicUrlOptions.cs`
- Modify: `src/TechStrap.Api/Program.cs`
- Modify: `src/TechStrap.Api/Startup/AttachmentSandbox.cs`
- Create: `src/TechStrap.Api/Startup/KbImageEndpoints.cs`
- Create: `src/TechStrap.Api/Startup/KbImageUrls.cs`
- Modify: `src/TechStrap.Api/appsettings.json`
- Create: `src/TechStrap.Application/Knowledge/IKbImageStore.cs`
- Create: `src/TechStrap.Application/Knowledge/UploadKbImageRequestHandler.cs`
- Modify: `src/TechStrap.Infrastructure/Attachments/AttachmentServiceCollectionExtensions.cs`
- Create: `src/TechStrap.Infrastructure/Attachments/KbImageSignatures.cs`
- Create: `src/TechStrap.Infrastructure/Attachments/KbImageStore.cs`
- Test (modify): `tests/TechStrap.Api.Tests/Config/ProductionBlankTemplateTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/HostFactory.cs`
- Test (create): `tests/TechStrap.Api.Tests/Kb/ApiPublicUrlTests.cs`
- Test (create): `tests/TechStrap.Api.Tests/Kb/KbImageServingTests.cs`
- Test (create): `tests/TechStrap.Application.Tests/Knowledge/UploadKbImageRequestHandlerTests.cs`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/KbImageStoreTests.cs`

**Interfaces:**
- Consumes: `SyntaxCircus.Storage` (`IStorageProvider`, `LocalFileStorageProvider`, `StoreObjectRequest`, `AddStorageProvider` through `AddTechStrapAttachments`), `OwnedReadStream`, `KbLimits.MaxImageBytes` and `KbLimits.ImagePathPrefix` (Task 1), `KbErrors` (Task 3), `UseSecurityHeaders` and `UseAttachmentSandbox` in `Program.cs` (the sandbox middleware must run before the security headers, which overwrite the CSP at response start), `ApiFactory` and `TestJwt` (tests), `ProcessEnvironmentCollection`.
- Produces:
  - `IncomingKbImage(long Length, Stream Content)`, `StoredKbImage(string Key, string FileName, string ContentType, long Size)`, `KbImageContent(Stream Content, string ContentType, long Size)`.
  - `IKbImageStore`: `Task<Result<StoredKbImage>> SaveAsync(IncomingKbImage, CancellationToken)` (Validation errors, target `file`: `kb-image-too-large` over 5 MB, `kb-image-type-not-allowed` for anything that is not png, jpeg, gif or webp by its leading bytes and first structure, or that carries markup) and `Task<KbImageContent?> OpenReadAsync(string fileName, CancellationToken)` (null unless the name is exactly `{32 hex}.{png|jpg|gif|webp}` and the object exists). Registered scoped by `AddTechStrapAttachments`.
  - `KbImageName.IsValid(string?)`, `StorageKey(fileName)` (`kb-images/{fileName}`) and `ContentTypeOf(fileName)`; the key is `kb-images/{Guid.CreateVersion7():N}.{ext}` with the extension from the sniffed type.
  - `IKbImageUrls.UrlFor(string fileName) : string` (Application) and its Api implementation: `{TECHSTRAP_API_PUBLIC_URL}/kb-images/{fileName}`, or in Development with the setting blank the request origin.
  - `IUploadKbImageRequestHandler.HandleAsync(IncomingKbImage image, CancellationToken) : Task<Result<KbImageUploadResponse>>` (Key and Url). Task 7 makes the parameter nullable (no file is a 400).
  - `ApiPublicUrlOptions` (`Key = "TECHSTRAP_API_PUBLIC_URL"`, `IsAcceptable(string? value, bool isDevelopment)`), validated on start: blank only in Development, otherwise an absolute http or https URL with a host and no user info, query or fragment.
  - `GET /kb-images/{name}` (`KbImageEndpoints.MapKbImages`): anonymous, outside `api/`, not in the OpenAPI document; 200 with `X-Content-Type-Options: nosniff`, `Content-Security-Policy` containing `default-src 'none'` and `sandbox`, `Cache-Control: public, max-age=31536000, immutable` and `Cross-Origin-Resource-Policy: cross-origin`; 404 with `Cache-Control: no-store`.
  - The setting `TECHSTRAP_API_PUBLIC_URL` in `src/TechStrap.Api/appsettings.json`, `src/TechStrap.Api/.env.example` and `deploy/.env.api.example` (the three D-043 edits that apply: compose owns neither the local nor the deploy value). Host tests default it to `https://api.test` in `HostFactory`.

- [ ] **Step 1: Write the failing tests**

The Integration tests use the real `LocalFileStorageProvider` over a temp directory. The Api tests start the real host with the temp directory as its storage root. `ProductionBlankTemplateTests`, `EnvExampleCompletenessTests` and `ConfigContract.Tests.ps1` learn the new key (it is required in Production, so the blank deploy template must name it); `HostFactory` gives every host test a default `TECHSTRAP_API_PUBLIC_URL`, as it already does for the portal URL, so existing Production-environment host tests keep starting. The RepositoryDocs test pins that the runbook mentions the setting and the `/kb-images/` proxy route.

Modify `scripts/tests/ConfigContract.Tests.ps1` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -40,7 +40,7 @@ BeforeAll {
     # value fails binding ("Failed to convert configuration value '' to type Int32"); the nullable settings TlsMode and TotalSendTimeout bind a blank as null.
     $script:CommonBlank = @('SENTRY__DSN', 'SENTRY__ENVIRONMENT', 'OPENTELEMETRY__OTLPENDPOINT', 'OPENTELEMETRY__HEADERS', 'OPENTELEMETRY__SERVICENAME', 'OPENTELEMETRY__SERVICEVERSION', 'OPENTELEMETRY__ENVIRONMENT')
     $script:BlankKeys = @{
-        Api    = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'CONNECTIONSTRINGS__TECHSTRAP', 'AUTHENTICATION__JWTBEARER__AUTHORITY', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_ADMIN_PUBLIC_URL', 'STORAGE__LOCAL__ROOTPATH')
+        Api    = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'CONNECTIONSTRINGS__TECHSTRAP', 'AUTHENTICATION__JWTBEARER__AUTHORITY', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_API_PUBLIC_URL', 'TECHSTRAP_ADMIN_PUBLIC_URL', 'STORAGE__LOCAL__ROOTPATH')
         Worker = $script:CommonBlank + @('CONNECTIONSTRINGS__TECHSTRAP', 'EMAIL__SMTP__HOST', 'EMAIL__SMTP__USERNAME', 'EMAIL__SMTP__PASSWORD', 'EMAIL__SMTP__DEFAULTFROM', 'EMAIL__SMTP__TLSMODE', 'EMAIL__SMTP__TOTALSENDTIMEOUT', 'EMAILOUTBOX__WORKERID')
         Admin  = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'API__BASEURL', 'AUTH__AUTHORITY', 'AUTH__CLIENTID', 'AUTH__CLIENTSECRET', 'DATAPROTECTION__KEYRINGPATH')
         Portal = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'DATAPROTECTION__KEYRINGPATH')
```

Modify `scripts/tests/RepositoryDocs.Tests.ps1` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -94,6 +94,12 @@ Describe 'D-044 (the knowledge base)' {
         }
     }
 
+    It 'tells the operator about the Api public URL and the /kb-images/ proxy route' {
+        $runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md'
+        $runbook | Should -Match 'TECHSTRAP_API_PUBLIC_URL'
+        $runbook | Should -Match ([regex]::Escape('/kb-images/'))
+    }
+
     It 'documents the category description and version in the schema doc' {
         $schema = Get-RepoText 'docs/architecture/05-SCHEMA.md'
         $schema | Should -Match '(?s)kb_categories \{.*?text description "nullable".*?xid xmin "concurrency token".*?\}'
```

Modify `tests/TechStrap.Api.Tests/Config/ProductionBlankTemplateTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -26,7 +26,7 @@ public sealed class ProductionBlankTemplateTests
     // What each host reports when the template is the only configuration. The Portal reads nothing required yet, so only the trusted proxies (which compose supplies) stop it.
     public static TheoryData<HostKind, string[]> HostsAndTheKeysTheyReportAlone() => new()
     {
-        { HostKind.Api, ["ConnectionStrings:TechStrap", "Authentication:JwtBearer:Authority", "TECHSTRAP_PORTAL_PUBLIC_URL", "Storage:Local:RootPath"] },
+        { HostKind.Api, ["ConnectionStrings:TechStrap", "Authentication:JwtBearer:Authority", "TECHSTRAP_PORTAL_PUBLIC_URL", "TECHSTRAP_API_PUBLIC_URL", "Storage:Local:RootPath"] },
         { HostKind.Worker, ["ConnectionStrings:TechStrap", "Email:Smtp:Host", "Email:Smtp:DefaultFrom"] },
         { HostKind.Admin, ["Auth:Authority", "Auth:ClientId", "Auth:ClientSecret", "Api:BaseUrl"] },
         { HostKind.Portal, ["TrustedProxy"] },
@@ -35,7 +35,7 @@ public sealed class ProductionBlankTemplateTests
     // What remains once compose has set its own values: exactly what the operator must fill in.
     public static TheoryData<HostKind, string[]> HostsAndTheKeysTheOperatorMustFill() => new()
     {
-        { HostKind.Api, ["ConnectionStrings:TechStrap", "Authentication:JwtBearer:Authority", "TECHSTRAP_PORTAL_PUBLIC_URL"] },
+        { HostKind.Api, ["ConnectionStrings:TechStrap", "Authentication:JwtBearer:Authority", "TECHSTRAP_PORTAL_PUBLIC_URL", "TECHSTRAP_API_PUBLIC_URL"] },
         { HostKind.Worker, ["ConnectionStrings:TechStrap", "Email:Smtp:Host", "Email:Smtp:DefaultFrom"] },
         { HostKind.Admin, ["Auth:Authority", "Auth:ClientId", "Auth:ClientSecret"] },
     };
@@ -113,6 +113,7 @@ public sealed class ProductionBlankTemplateTests
             "Authentication:JwtBearer:Authority=https://idp.example.com/application/o/techstrap/",
             "Authentication:JwtBearer:Audiences:0=techstrap-api",
             "TECHSTRAP_PORTAL_PUBLIC_URL=https://support.example.com",
+            "TECHSTRAP_API_PUBLIC_URL=https://api.example.com",
         ],
         [HostKind.Worker] =
         [
```

Modify `tests/TechStrap.Api.Tests/EnvExampleCompletenessTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -78,6 +78,7 @@ public sealed partial class EnvExampleCompletenessTests
                 "AUTHENTICATION__JWTBEARER__AUTHORITY",
                 "AUTHENTICATION__JWTBEARER__AUDIENCES__0",
                 "TECHSTRAP_PORTAL_PUBLIC_URL",
+                "TECHSTRAP_API_PUBLIC_URL",
                 "TECHSTRAP_ADMIN_PUBLIC_URL",
                 "STORAGE__LOCAL__ROOTPATH",
                 AutoCloseOptions.DaysKey,
```

Modify `tests/TechStrap.Api.Tests/HostFactory.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -58,6 +58,7 @@ public class HostFactory<TProgram>(
             {
                 [ApiStartupTasks.MigrateOnStartupKey] = "false",
                 ["TECHSTRAP_PORTAL_PUBLIC_URL"] = "https://portal.test",
+                ["TECHSTRAP_API_PUBLIC_URL"] = "https://api.test",
                 ["Storage:Local:RootPath"] = Path.Combine(Path.GetTempPath(), "techstrap-tests-default-storage"),
             });
             configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?>());
```

Create `tests/TechStrap.Api.Tests/Kb/ApiPublicUrlTests.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using NSubstitute;
using TechStrap.Api.Options;
using TechStrap.Api.Startup;

namespace TechStrap.Api.Tests.Kb;

/// <summary>D-044: <c>TECHSTRAP_API_PUBLIC_URL</c> is required outside Development, validated on start, and the only source of an image URL when it is set.</summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ApiPublicUrlTests
{
    [Theory]
    [InlineData("https://api.example.com", false, true)]
    [InlineData("https://api.example.com/", false, true)]
    [InlineData("http://localhost:8080", false, true)]
    [InlineData("https://example.com/techstrap-api", false, true)]
    [InlineData("", false, false)]
    [InlineData("   ", false, false)]
    [InlineData(null, false, false)]
    [InlineData("", true, true)]
    [InlineData(null, true, true)]
    [InlineData("api.example.com", true, false)]
    [InlineData("/kb", true, false)]
    [InlineData("ftp://api.example.com", true, false)]
    [InlineData("javascript:alert(1)", true, false)]
    [InlineData("https://user:pw@api.example.com", true, false)]
    [InlineData("https://api.example.com?x=1", true, false)]
    [InlineData("https://api.example.com#frag", true, false)]
    public void The_value_is_checked_the_way_the_start_up_check_does(string? value, bool isDevelopment, bool acceptable) =>
        ApiPublicUrlOptions.IsAcceptable(value, isDevelopment).ShouldBe(acceptable);

    [Fact]
    public async Task Production_refuses_to_start_without_it_and_names_the_key()
    {
        await using var factory = new ApiFactory(
            environment: "Production",
            settings: new Dictionary<string, string?> { ["TrustedProxy:RequireTrustedProxiesInProduction"] = "false", ["TECHSTRAP_API_PUBLIC_URL"] = "" });

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        string.Join(" ", Messages(failure)).ShouldContain("TECHSTRAP_API_PUBLIC_URL");
    }

    [Fact]
    public async Task Development_starts_with_it_blank()
    {
        await using var development = new ApiFactory();
        using var client = development.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_malformed_address_stops_start_even_in_Development()
    {
        await using var factory = new ApiFactory(settings: new Dictionary<string, string?> { ["TECHSTRAP_API_PUBLIC_URL"] = "api.example.com" });

        var failure = Should.Throw<Exception>(() => factory.CreateClient());

        string.Join(" ", Messages(failure)).ShouldContain("TECHSTRAP_API_PUBLIC_URL");
    }

    [Fact]
    public void A_configured_address_builds_the_url_and_ignores_the_request_host()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("evil.example");
        accessor.HttpContext.Returns(context);
        var urls = new KbImageUrls(Microsoft.Extensions.Options.Options.Create(new ApiPublicUrlOptions { PublicUrl = "https://api.example.com/" }), accessor);

        urls.UrlFor("abc.png").ShouldBe("https://api.example.com/kb-images/abc.png");
    }

    [Fact]
    public void A_blank_address_falls_back_to_the_origin_of_the_request()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 8080);
        context.Request.PathBase = "/base";
        accessor.HttpContext.Returns(context);
        var urls = new KbImageUrls(Microsoft.Extensions.Options.Options.Create(new ApiPublicUrlOptions()), accessor);

        urls.UrlFor("abc.png").ShouldBe("http://localhost:8080/base/kb-images/abc.png");
    }

    [Fact]
    public void A_blank_address_with_no_request_is_an_error_not_a_relative_url()
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns((HttpContext?)null);
        var urls = new KbImageUrls(Microsoft.Extensions.Options.Options.Create(new ApiPublicUrlOptions()), accessor);

        Should.Throw<InvalidOperationException>(() => urls.UrlFor("abc.png"));
    }

    private static IEnumerable<string> Messages(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }
}
```

Create `tests/TechStrap.Api.Tests/Kb/KbImageServingTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using TechStrap.Api.Tests.Auth;
using TechStrap.Application.Knowledge;

namespace TechStrap.Api.Tests.Kb;

/// <summary>
/// Review Focus 2 on the wire: <c>GET /kb-images/{name}</c> is anonymous and serves an image with headers that stop a browser treating it as
/// anything else, never serves a name the store could not have written, and leaks nothing about the caller.
/// </summary>
public sealed class KbImageServingTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }, .. new byte[40]];

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-kbserving-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, recursive: true);
        }
    }

    private ApiFactory Factory() => new(settings: new Dictionary<string, string?> { ["Storage:Local:RootPath"] = _storage });

    private static async Task<StoredKbImage> StoreAsync(ApiFactory factory, byte[] bytes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IKbImageStore>().SaveAsync(new IncomingKbImage(bytes.Length, new MemoryStream(bytes)), Ct);
        return result.Value;
    }

    [Fact]
    public async Task An_anonymous_caller_gets_the_image_with_safe_headers_and_a_long_immutable_cache()
    {
        await using var factory = Factory();
        var stored = await StoreAsync(factory, Png);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/kb-images/{stored.FileName}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Png);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        response.Content.Headers.ContentLength.ShouldBe(Png.Length);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("default-src 'none'");
        csp.ShouldContain("sandbox");
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=31536000, immutable");
        response.Headers.GetValues("Cross-Origin-Resource-Policy").ShouldBe(["cross-origin"]);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
        response.Headers.Contains("WWW-Authenticate").ShouldBeFalse();
    }

    [Fact]
    public async Task A_bearer_token_makes_no_difference_and_nothing_about_the_caller_is_echoed()
    {
        await using var factory = Factory();
        var stored = await StoreAsync(factory, Png);
        using var anonymous = factory.CreateClient();
        using var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        using var garbage = factory.CreateClient();
        garbage.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        foreach (var client in new[] { anonymous, agent, garbage })
        {
            using var response = await client.GetAsync($"/kb-images/{stored.FileName}", Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.Select(header => header.Key).ShouldNotContain("Vary");
            response.Headers.Contains("WWW-Authenticate").ShouldBeFalse();
        }
    }

    [Fact]
    public async Task An_unknown_well_formed_name_is_a_404_that_is_not_cached()
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/kb-images/0123456789abcdef0123456789abcdef.png", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
    }

    [Theory]
    [InlineData("/kb-images/..%2F..%2Fappsettings.json")]
    [InlineData("/kb-images/%2e%2e%2fsecret.png")]
    [InlineData("/kb-images/..%5C..%5Csecret.png")]
    [InlineData("/kb-images/%252e%252e%252fsecret.png")]
    [InlineData("/kb-images/0123456789abcdef0123456789abcdef")]
    [InlineData("/kb-images/0123456789abcdef0123456789abcdef.svg")]
    [InlineData("/kb-images/0123456789ABCDEF0123456789ABCDEF.png")]
    [InlineData("/kb-images/kb-images%2F0123456789abcdef0123456789abcdef.png")]
    public async Task A_name_the_store_could_not_have_written_is_a_404_and_reads_nothing_outside_the_prefix(string path)
    {
        await using var factory = Factory();
        var stored = await StoreAsync(factory, Png);
        File.WriteAllBytes(Path.Combine(_storage, "secret.png"), Png);
        File.WriteAllBytes(Path.Combine(_storage, "appsettings.json"), "{}"u8.ToArray());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("{}");
        stored.FileName.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("/kb-images/")]
    [InlineData("/kb-images")]
    [InlineData("/kb-images/a/b.png")]
    [InlineData("/kb-images/..;/secret.png")]
    [InlineData("/kb-images/../secret.png")]
    [InlineData("/kb-images/attachments/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef")]
    public async Task A_path_that_matches_no_image_route_never_returns_a_file(string path)
    {
        await using var factory = Factory();
        await StoreAsync(factory, Png);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, Ct);

        // The Api answers an unmatched anonymous path with its fallback (401) or a 404; either way no file.
        new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized }.ShouldContain(response.StatusCode);
        response.Content.Headers.ContentType?.MediaType.ShouldNotStartWith("image/");
    }

    [Fact]
    public async Task Only_get_is_served()
    {
        await using var factory = Factory();
        var stored = await StoreAsync(factory, Png);
        using var client = factory.CreateClient();

        using var post = await client.PostAsync($"/kb-images/{stored.FileName}", new ByteArrayContent(Png), Ct);
        using var delete = await client.DeleteAsync($"/kb-images/{stored.FileName}", Ct);

        new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.Unauthorized }.ShouldContain(post.StatusCode);
        new[] { HttpStatusCode.MethodNotAllowed, HttpStatusCode.Unauthorized }.ShouldContain(delete.StatusCode);
        File.Exists(Path.Combine(_storage, stored.Key)).ShouldBeTrue();
    }
}
```

Create `tests/TechStrap.Application.Tests/Knowledge/UploadKbImageRequestHandlerTests.cs`:

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class UploadKbImageRequestHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly IKbImageStore _store = Substitute.For<IKbImageStore>();
    private readonly IKbImageUrls _urls = Substitute.For<IKbImageUrls>();

    [Fact]
    public async Task The_response_carries_the_stored_key_and_the_absolute_url_for_its_file_name()
    {
        var image = new IncomingKbImage(3, new MemoryStream([1, 2, 3]));
        _store.SaveAsync(image, Ct).Returns(Result<StoredKbImage>.Success(new StoredKbImage("kb-images/abc.png", "abc.png", "image/png", 3)));
        _urls.UrlFor("abc.png").Returns("https://api.example.com/kb-images/abc.png");

        var result = await new UploadKbImageRequestHandler(_store, _urls).HandleAsync(image, Ct);

        result.Value.Key.ShouldBe("kb-images/abc.png");
        result.Value.Url.ShouldBe("https://api.example.com/kb-images/abc.png");
    }

    [Fact]
    public async Task A_refused_image_returns_the_stores_error_and_builds_no_url()
    {
        var image = new IncomingKbImage(3, new MemoryStream([1, 2, 3]));
        _store.SaveAsync(image, Ct).Returns(Result<StoredKbImage>.Failure(new ResultError("kb-image-type-not-allowed", "No.", ResultErrorKind.Validation, "file")));

        var result = await new UploadKbImageRequestHandler(_store, _urls).HandleAsync(image, Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-image-type-not-allowed");
        _urls.DidNotReceiveWithAnyArgs().UrlFor(default!);
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef.png", true)]
    [InlineData("0123456789abcdef0123456789abcdef.jpg", true)]
    [InlineData("0123456789abcdef0123456789abcdef.gif", true)]
    [InlineData("0123456789abcdef0123456789abcdef.webp", true)]
    [InlineData("0123456789abcdef0123456789abcdef.jpeg", false)]
    [InlineData("0123456789abcdef0123456789abcdef.svg", false)]
    [InlineData("../0123456789abcdef0123456789abcdef.png", false)]
    [InlineData("0123456789abcdef0123456789abcdef.png\n", false)]
    [InlineData(null, false)]
    public void Only_the_exact_name_shape_the_store_writes_is_valid(string? name, bool valid) =>
        KbImageName.IsValid(name).ShouldBe(valid);
}
```

Create `tests/TechStrap.Infrastructure.IntegrationTests/KbImageStoreTests.cs`:

```csharp
using System.Text;
using Microsoft.Extensions.Options;
using SyntaxCircus.Storage;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;
using TechStrap.Infrastructure.Attachments;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Review Focus 2 (image upload abuse), against the real Local storage provider: polyglot and SVG uploads, a spoofed content type, an oversize file and a
/// path-traversal name are all refused, and the stored key is the store's own.
/// </summary>
public sealed class KbImageStoreTests : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static readonly byte[] Png = [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }, .. new byte[40]];
    private static readonly byte[] Jpeg = [.. new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46, 0x49, 0x46, 0 }, .. new byte[40]];
    private static readonly byte[] Gif = [.. "GIF89a"u8.ToArray(), .. new byte[20]];
    private static readonly byte[] Webp = [.. "RIFF"u8.ToArray(), 0x24, 0, 0, 0, .. "WEBPVP8 "u8.ToArray(), .. new byte[20]];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "techstrap-kbimages-" + Guid.NewGuid().ToString("N"));
    private readonly KbImageStore _store;

    public KbImageStoreTests() =>
        _store = new KbImageStore(new LocalFileStorageProvider(Options.Create(new LocalStorageOptions { RootPath = _root })));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static IncomingKbImage Upload(byte[] bytes, long? declared = null) => new(declared ?? bytes.Length, new MemoryStream(bytes));

    public static TheoryData<string, byte[], string, string> Accepted() => new()
    {
        { "png", Png, "image/png", "png" },
        { "jpeg", Jpeg, "image/jpeg", "jpg" },
        { "gif", Gif, "image/gif", "gif" },
        { "webp", Webp, "image/webp", "webp" },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public async Task A_png_jpeg_gif_or_webp_is_stored_under_a_random_kb_images_key_with_its_own_extension_and_reads_back(string label, byte[] bytes, string contentType, string extension)
    {
        var stored = (await _store.SaveAsync(Upload(bytes), Ct)).Value;

        stored.ContentType.ShouldBe(contentType, label);
        stored.Size.ShouldBe(bytes.Length);
        stored.FileName.ShouldMatch("^[0-9a-f]{32}\\." + extension + "$");
        stored.Key.ShouldBe("kb-images/" + stored.FileName);
        File.ReadAllBytes(Path.Combine(_root, stored.Key)).ShouldBe(bytes);
        await using var read = (await _store.OpenReadAsync(stored.FileName, Ct))!.Content;
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy, Ct);
        copy.ToArray().ShouldBe(bytes);
    }

    [Fact]
    public async Task Two_uploads_of_the_same_file_get_different_keys()
    {
        var first = (await _store.SaveAsync(Upload(Png), Ct)).Value;
        var second = (await _store.SaveAsync(Upload(Png), Ct)).Value;

        first.Key.ShouldNotBe(second.Key);
    }

    public static TheoryData<string, byte[]> Refused() => new()
    {
        { "svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"></svg>") },
        { "svg with an xml prolog", Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>") },
        { "html", Encoding.UTF8.GetBytes("<html><body><script>alert(1)</script></body></html>") },
        { "a script", Encoding.UTF8.GetBytes("alert(1)") },
        { "an executable", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0, 0, 0] },
        { "a pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n") },
        { "a zip", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0] },
        { "an empty file", [] },
        { "a png signature with no header chunk", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13] },
        { "a riff file that is not webp", [.. "RIFF"u8.ToArray(), 0x24, 0, 0, 0, .. "WAVEfmt "u8.ToArray(), .. new byte[20]] },
        { "a gif that is really a page", [.. "GIF89a"u8.ToArray(), .. new byte[8], .. "<script>alert(document.domain)</script>"u8.ToArray()] },
        { "a png with a script in a text chunk", [.. Png, .. "<ScRiPt>alert(1)</ScRiPt>"u8.ToArray()] },
        { "a jpeg with an svg inside", [.. Jpeg, .. "<svg onload=alert(1)>"u8.ToArray()] },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Anything_that_is_not_a_plain_png_jpeg_gif_or_webp_is_refused_and_nothing_is_stored(string label, byte[] bytes)
    {
        var result = await _store.SaveAsync(Upload(bytes), Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Code.ShouldBe("kb-image-type-not-allowed", label),
            error => error.Target.ShouldBe("file"));
        Directory.Exists(Path.Combine(_root, "kb-images")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_by_its_declared_length_and_by_its_real_length()
    {
        var declaredTooLarge = await _store.SaveAsync(new IncomingKbImage(KbLimits.MaxImageBytes + 1, new MemoryStream(Png)), Ct);
        var liesAboutLength = await _store.SaveAsync(new IncomingKbImage(10, new MemoryStream([.. Png, .. new byte[KbLimits.MaxImageBytes]])), Ct);

        declaredTooLarge.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-image-too-large");
        liesAboutLength.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-image-too-large");
        Directory.Exists(Path.Combine(_root, "kb-images")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_of_exactly_the_limit_is_accepted()
    {
        var bytes = new byte[KbLimits.MaxImageBytes];
        Png.CopyTo(bytes, 0);

        (await _store.SaveAsync(Upload(bytes), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("../appsettings.json")]
    [InlineData("..%2F..%2Fsecret.png")]
    [InlineData("kb-images/0123456789abcdef0123456789abcdef.png")]
    [InlineData("/etc/passwd")]
    [InlineData("a/../0123456789abcdef0123456789abcdef.png")]
    [InlineData("0123456789abcdef0123456789abcdef.png/..")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.png")]
    [InlineData("0123456789abcdef0123456789abcdef.svg")]
    [InlineData("0123456789abcdef0123456789abcdef.png ")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcde.png")]
    [InlineData("0123456789abcdef0123456789abcdef.png\0.svg")]
    [InlineData("")]
    public async Task A_name_the_store_could_not_have_written_is_never_opened(string name)
    {
        Directory.CreateDirectory(Path.Combine(_root, "attachments"));
        File.WriteAllBytes(Path.Combine(_root, "secret.png"), Png);
        File.WriteAllBytes(Path.Combine(_root, "appsettings.json"), "{}"u8.ToArray());

        (await _store.OpenReadAsync(name, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task A_well_formed_name_that_is_not_stored_is_null()
    {
        (await _store.OpenReadAsync("0123456789abcdef0123456789abcdef.png", Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task An_attachment_cannot_be_reached_through_the_image_reader()
    {
        var storage = new LocalFileStorageProvider(Options.Create(new LocalStorageOptions { RootPath = _root }));
        await storage.StoreAsync(new StoreObjectRequest("attachments/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.png", new MemoryStream(Png), "image/png"), Ct);

        (await _store.OpenReadAsync("attachments/0123456789abcdef0123456789abcdef/0123456789abcdef0123456789abcdef.png", Ct)).ShouldBeNull();
    }
}
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: the build FAILS with `CS0246` ("could not be found") for `IKbImageStore`, `IKbImageUrls`, `KbImageStore`, `IncomingKbImage` and `StoredKbImage`.

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1` and the same for `RepositoryDocs.Tests.ps1`
Expected: FAIL, 1 failed in each (`appsettings.json is blank only where blank is valid, and nowhere else` and `tells the operator about the Api public URL and the /kb-images/ proxy route`).

- [ ] **Step 3: Add the store, the sniffing and the upload handler**

What counts as an image is decided by the first bytes and the first structure (a PNG needs its `IHDR` chunk, a WebP its `VP8 `, `VP8L` or `VP8X` chunk), never by the file name or the declared content type, and a file that also carries markup a browser could run (`<script`, `<svg`, `<html`, `<iframe`, `<body`, `<!doctype`, `<?php`, matched without case anywhere in the file) is refused. SVG has no entry, so it is refused. Reading stops at 5 MB + 1 byte, so a declared length that lies cannot exhaust memory. Only a name the store itself could have written reaches storage on a read, so no path segment or prefix change can. Note: `Ascii.ToLower` stops at the first byte above 0x7F and every PNG starts with one, so the lowering is a plain loop.

Create `src/TechStrap.Application/Knowledge/IKbImageStore.cs`:

```csharp
using System.Text.RegularExpressions;
using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

/// <summary>An uploaded image as a readable stream. <see cref="Length"/> is the declared length. The file name and the declared content type are deliberately absent: the leading bytes decide the type and the store picks the name.</summary>
public sealed record IncomingKbImage(long Length, Stream Content);

/// <summary>A stored image. <see cref="Key"/> is <c>kb-images/{name}</c>; <see cref="FileName"/> is the <c>{guid}.{ext}</c> part, which is also its public address.</summary>
public sealed record StoredKbImage(string Key, string FileName, string ContentType, long Size);

/// <summary>A stored image opened for reading. The caller disposes <see cref="Content"/>.</summary>
public sealed record KbImageContent(Stream Content, string ContentType, long Size);

/// <summary>
/// KB images over SyntaxCircus.Storage (D-021, D-044). Separate from <c>IAttachmentStore</c> because the <c>kb-images/</c> prefix is publicly readable and
/// ticket attachments never live under it. An image is stored under a random key and has no database row.
/// </summary>
public interface IKbImageStore
{
    /// <summary>Validation errors (target "file"): <c>kb-image-too-large</c> over 5 MB, <c>kb-image-type-not-allowed</c> for anything that is not a png, jpeg, gif or webp by its leading bytes (SVG included), or that carries markup.</summary>
    Task<Result<StoredKbImage>> SaveAsync(IncomingKbImage image, CancellationToken cancellationToken);

    /// <summary>Opens <c>kb-images/{fileName}</c>, or returns null when the name is not a well-formed image name or the object is missing. Nothing outside the prefix can be reached.</summary>
    Task<KbImageContent?> OpenReadAsync(string fileName, CancellationToken cancellationToken);
}

/// <summary>The public address of a stored image: <c>{TECHSTRAP_API_PUBLIC_URL or the request origin}/kb-images/{fileName}</c>.</summary>
public interface IKbImageUrls
{
    string UrlFor(string fileName);
}

/// <summary>The only image names the store writes and serves: 32 lower-case hex digits, a dot and one of four extensions. Anything else, including any path segment, is refused before storage is touched.</summary>
public static partial class KbImageName
{
    public const string Png = "png";
    public const string Jpeg = "jpg";
    public const string Gif = "gif";
    public const string Webp = "webp";

    // \z, not $: $ would also accept a trailing newline.
    [GeneratedRegex(@"^[0-9a-f]{32}\.(png|jpg|gif|webp)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool IsValid(string? name) => name is not null && Pattern().IsMatch(name);

    public static string StorageKey(string fileName) => KbLimits.ImagePathPrefix + fileName;

    /// <summary>The content type for a valid name's extension. Only call it for names <see cref="IsValid"/> accepts.</summary>
    public static string ContentTypeOf(string fileName) => fileName[(fileName.LastIndexOf('.') + 1)..] switch
    {
        Png => "image/png",
        Jpeg => "image/jpeg",
        Gif => "image/gif",
        Webp => "image/webp",
        _ => throw new ArgumentException("Not a KB image name.", nameof(fileName)),
    };
}
```

Create `src/TechStrap.Application/Knowledge/UploadKbImageRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IUploadKbImageRequestHandler
{
    Task<Result<KbImageUploadResponse>> HandleAsync(IncomingKbImage image, CancellationToken cancellationToken);
}

/// <summary>
/// POST /api/kb/images (Agent). Hands the bytes to <see cref="IKbImageStore"/>, which checks the size and the type and stores them under a random key,
/// then returns that key and the absolute public URL to put in the Markdown. No database row is written and unused images are not cleaned up (D-044).
/// </summary>
public sealed class UploadKbImageRequestHandler(IKbImageStore store, IKbImageUrls urls) : IUploadKbImageRequestHandler
{
    public async Task<Result<KbImageUploadResponse>> HandleAsync(IncomingKbImage image, CancellationToken cancellationToken)
    {
        var stored = await store.SaveAsync(image, cancellationToken);
        return stored.IsFailure
            ? Result<KbImageUploadResponse>.Failure(stored.Errors[0])
            : Result<KbImageUploadResponse>.Success(new KbImageUploadResponse(stored.Value.Key, urls.UrlFor(stored.Value.FileName)));
    }
}
```

Create `src/TechStrap.Infrastructure/Attachments/KbImageSignatures.cs`:

```csharp
using System.Text;
using TechStrap.Application.Knowledge;

namespace TechStrap.Infrastructure.Attachments;

/// <summary>
/// What counts as a KB image (D-044): png, jpeg, gif or webp, recognized by the leading bytes and a plausible first structure, never by the file name or the
/// declared type. SVG has no entry, so it is refused. A file that also carries markup a browser could run (a gif that is really a script) is refused too.
/// </summary>
internal static class KbImageSignatures
{
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] _gif87 = "GIF87a"u8.ToArray();
    private static readonly byte[] _gif89 = "GIF89a"u8.ToArray();

    // Lower-case, because the scan lowers the content first. A real image has none of these byte runs; a polyglot that wants to be a page does.
    private static readonly byte[][] _markup =
    [
        "<script"u8.ToArray(), "<svg"u8.ToArray(), "<html"u8.ToArray(), "<iframe"u8.ToArray(), "<body"u8.ToArray(), "<!doctype"u8.ToArray(), "<?php"u8.ToArray(),
    ];

    /// <summary>The file name extension (png, jpg, gif or webp) for an accepted image, or null.</summary>
    public static string? Identify(ReadOnlySpan<byte> content)
    {
        var extension = Match(content);
        return extension is not null && !CarriesMarkup(content) ? extension : null;
    }

    private static string? Match(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(_png) && content.Length >= 33 && content.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return KbImageName.Png;
        }

        // FF D8 FF, then a marker byte (APPn, DQT, SOFn, COM...).
        if (content.Length >= 4 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF && content[3] >= 0xC0 && content[3] != 0xFF)
        {
            return KbImageName.Jpeg;
        }

        if (content.Length >= 13 && (content.StartsWith(_gif87) || content.StartsWith(_gif89)))
        {
            return KbImageName.Gif;
        }

        if (content.Length >= 20 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8)
            && (content.Slice(12, 4).SequenceEqual("VP8 "u8) || content.Slice(12, 4).SequenceEqual("VP8L"u8) || content.Slice(12, 4).SequenceEqual("VP8X"u8)))
        {
            return KbImageName.Webp;
        }

        return null;
    }

    private static bool CarriesMarkup(ReadOnlySpan<byte> content)
    {
        // Lowered byte by byte: Ascii.ToLower stops at the first byte above 0x7F, and every png starts with one.
        var lower = new byte[content.Length];
        for (var i = 0; i < content.Length; i++)
        {
            lower[i] = content[i] is >= (byte)'A' and <= (byte)'Z' ? (byte)(content[i] | 0x20) : content[i];
        }

        foreach (var marker in _markup)
        {
            if (lower.AsSpan().IndexOf(marker) >= 0)
            {
                return true;
            }
        }

        return false;
    }
}
```

Create `src/TechStrap.Infrastructure/Attachments/KbImageStore.cs`:

```csharp
using System.Buffers;
using SyntaxCircus.Common;
using SyntaxCircus.Storage;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Infrastructure.Attachments;

internal sealed class KbImageStore(IStorageProvider storage) : IKbImageStore
{
    private const string Target = "file";

    public async Task<Result<StoredKbImage>> SaveAsync(IncomingKbImage image, CancellationToken cancellationToken)
    {
        if (image.Length > KbLimits.MaxImageBytes)
        {
            return TooLarge();
        }

        // Read at most MaxImageBytes + 1 bytes so a declared length that lies cannot exhaust memory.
        await using var content = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        try
        {
            long total = 0;
            while (total <= KbLimits.MaxImageBytes)
            {
                var want = (int)Math.Min(buffer.Length, KbLimits.MaxImageBytes + 1 - total);
                var read = await image.Content.ReadAsync(buffer.AsMemory(0, want), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                content.Write(buffer, 0, read);
                total += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (content.Length > KbLimits.MaxImageBytes)
        {
            return TooLarge();
        }

        var extension = KbImageSignatures.Identify(content.GetBuffer().AsSpan(0, (int)content.Length));
        if (extension is null)
        {
            return Failure("kb-image-type-not-allowed", "Only PNG, JPEG, GIF and WebP images can be uploaded.");
        }

        var fileName = $"{Guid.CreateVersion7():N}.{extension}";
        var key = KbImageName.StorageKey(fileName);
        var contentType = KbImageName.ContentTypeOf(fileName);
        content.Position = 0;
        try
        {
            await storage.StoreAsync(new StoreObjectRequest(key, content, contentType), cancellationToken);
        }
        catch
        {
            // A failed copy can leave a partial object behind.
            await DeleteQuietlyAsync(key);
            throw;
        }

        return Result<StoredKbImage>.Success(new StoredKbImage(key, fileName, contentType, content.Length));
    }

    public async Task<KbImageContent?> OpenReadAsync(string fileName, CancellationToken cancellationToken)
    {
        // Only a name the store itself could have written reaches storage, so no path segment, dot-dot or prefix change can.
        if (!KbImageName.IsValid(fileName))
        {
            return null;
        }

        var result = await storage.ReadAsync(KbImageName.StorageKey(fileName), cancellationToken);
        return result is null ? null : new KbImageContent(new OwnedReadStream(result.Content, result), KbImageName.ContentTypeOf(fileName), result.Content.CanSeek ? result.Content.Length : -1);
    }

    private async Task DeleteQuietlyAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception)
        {
            // Best effort only; the original failure is the one to surface.
        }
    }

    private static Result<StoredKbImage> TooLarge() =>
        Failure("kb-image-too-large", $"This image is too large. Images can be up to {KbLimits.MaxImageBytes / (1024 * 1024)} MB.");

    private static Result<StoredKbImage> Failure(string code, string message) =>
        Result<StoredKbImage>.Failure(new ResultError(code, message, ResultErrorKind.Validation, Target));
}
```

Modify `src/TechStrap.Infrastructure/Attachments/AttachmentServiceCollectionExtensions.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -3,6 +3,7 @@ using Microsoft.Extensions.DependencyInjection;
 using Microsoft.Extensions.DependencyInjection.Extensions;
 using SyntaxCircus.Storage;
 using TechStrap.Application.Attachments;
+using TechStrap.Application.Knowledge;
 
 namespace TechStrap.Infrastructure.Attachments;
 
@@ -18,6 +19,7 @@ public static class AttachmentServiceCollectionExtensions
                 "Storage:Local:RootPath is required for the Local storage provider.")
             .ValidateOnStart();
         services.TryAddScoped<IAttachmentStore, AttachmentStore>();
+        services.TryAddScoped<IKbImageStore, KbImageStore>();
         return services;
     }
 }
```

- [ ] **Step 4: Add the setting, the URL builder and the public image endpoint**

With `TECHSTRAP_API_PUBLIC_URL` set the URL starts with it and the request is never consulted, so a forged Host header cannot change a stored address; only Development with the setting blank falls back to the request origin. The endpoint is a minimal-API route, not a controller: it is a static-file route that runs no application workflow (D-021). The CSP is `default-src 'none'` from the Api policy plus `sandbox`, which the existing attachment sandbox middleware appends for the `/kb-images` prefix. The endpoint also sets `nosniff` itself; the shared security headers set it too, so removing the endpoint's own line changes nothing observable (see the mutations).

Create `src/TechStrap.Api/Options/ApiPublicUrlOptions.cs`:

```csharp
namespace TechStrap.Api.Options;

/// <summary>
/// The Api's own public address (<c>TECHSTRAP_API_PUBLIC_URL</c>, D-044): the origin readers of the portal and the Admin use to load KB images from
/// <c>{url}/kb-images/{name}</c>. Required outside Development, where blank falls back to the origin of the request that uploads the image.
/// </summary>
public sealed class ApiPublicUrlOptions
{
    public const string Key = "TECHSTRAP_API_PUBLIC_URL";

    public string PublicUrl { get; set; } = string.Empty;

    /// <summary>
    /// True when the value is acceptable. Blank is acceptable only in Development. A value must be an absolute http or https URL with a host
    /// and no user info, query or fragment (a path prefix is allowed, for an Api published under one).
    /// </summary>
    public static bool IsAcceptable(string? value, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return isDevelopment;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && !string.IsNullOrEmpty(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
```

Create `src/TechStrap.Api/Startup/KbImageUrls.cs`:

```csharp
using Microsoft.Extensions.Options;
using TechStrap.Api.Options;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Startup;

/// <summary>
/// The absolute public URL of a stored image (D-044). With <c>TECHSTRAP_API_PUBLIC_URL</c> set, the URL starts with it and the request is never
/// consulted, so a forged Host header cannot change a stored address. Only in Development, where the setting may be blank, does it fall back to the
/// origin of the current request.
/// </summary>
internal sealed class KbImageUrls(IOptions<ApiPublicUrlOptions> options, IHttpContextAccessor accessor) : IKbImageUrls
{
    public string UrlFor(string fileName)
    {
        var baseUrl = options.Value.PublicUrl.Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
        {
            var request = accessor.HttpContext?.Request
                ?? throw new InvalidOperationException("The image URL needs TECHSTRAP_API_PUBLIC_URL or a current request.");
            baseUrl = $"{request.Scheme}://{request.Host}{request.PathBase}";
        }

        return $"{baseUrl}/{KbLimits.ImagePathPrefix}{fileName}";
    }
}
```

Create `src/TechStrap.Api/Startup/KbImageEndpoints.cs`:

```csharp
using TechStrap.Application.Knowledge;

namespace TechStrap.Api.Startup;

/// <summary>
/// <c>GET /kb-images/{name}</c>, anonymous (D-021, D-044). It is the one public file route: it runs no application workflow, reads only an object
/// whose name is exactly the shape the store writes, and serves it with headers that stop a browser treating it as anything but an image.
/// The route is outside <c>api/</c> and is not part of the API contract, so the route-policy coverage test and the OpenAPI document leave it out; <c>KbImageServingTests</c> pins its behavior.
/// </summary>
public static class KbImageEndpoints
{
    public const string Route = "/kb-images/{name}";

    public static IEndpointRouteBuilder MapKbImages(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, async (string name, IKbImageStore store, HttpContext context, CancellationToken cancellationToken) =>
        {
            var image = await store.OpenReadAsync(name, cancellationToken);
            if (image is null)
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.NotFound();
            }

            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.CacheControl = "public, max-age=31536000, immutable";
            headers["Cross-Origin-Resource-Policy"] = "cross-origin";
            if (image.Size >= 0)
            {
                context.Response.ContentLength = image.Size;
            }

            // Results.Stream disposes the stream when the response is done.
            return Results.Stream(image.Content, image.ContentType);
        }).AllowAnonymous().ExcludeFromDescription();
        return app;
    }
}
```

Modify `src/TechStrap.Api/Startup/AttachmentSandbox.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -2,10 +2,10 @@ namespace TechStrap.Api.Startup;
 
 public static class AttachmentSandbox
 {
-    private static readonly string[] _prefixes = ["/api/attachments", "/api/customer/attachments"];
+    private static readonly string[] _prefixes = ["/api/attachments", "/api/customer/attachments", "/kb-images"];
 
     /// <summary>
-    /// Downloads (agent and customer) get <c>Content-Security-Policy: sandbox</c>. The shared security-headers middleware overwrites the CSP
+    /// Downloads (agent and customer) and the public KB images (<c>/kb-images</c>) get <c>Content-Security-Policy: sandbox</c> appended to the API policy (<c>default-src 'none'</c>). The shared security-headers middleware overwrites the CSP
     /// when the response starts, and start callbacks run last-registered-first, so this must be registered before it to have the final say.
     /// </summary>
     public static IApplicationBuilder UseAttachmentSandbox(this IApplicationBuilder app) =>
```

Modify `src/TechStrap.Api/Program.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -104,6 +104,16 @@ builder.Services.AddTechStrapTicketOperations(builder.Configuration);
 builder.Services.AddResultProblemDetails();
 builder.Services.AddApplicationHandlers();
 
+// KB images (D-044): the Api's own public address builds each image URL. Required outside Development; blank there means the request origin.
+builder.Services.AddOptions<ApiPublicUrlOptions>()
+    .Configure<IConfiguration>((options, configuration) => options.PublicUrl = configuration[ApiPublicUrlOptions.Key]?.Trim() ?? string.Empty)
+    .Validate<IHostEnvironment>(
+        (options, environment) => ApiPublicUrlOptions.IsAcceptable(options.PublicUrl, environment.IsDevelopment()),
+        $"{ApiPublicUrlOptions.Key} must be an absolute http or https URL without user info, query or fragment (it is required outside Development).")
+    .ValidateOnStart();
+builder.Services.AddHttpContextAccessor();
+builder.Services.AddScoped<TechStrap.Application.Knowledge.IKbImageUrls, KbImageUrls>();
+
 var app = builder.Build();
 telemetry.LogStartupWarning(app.Logger);
 
@@ -152,6 +162,7 @@ app.MapGroup(string.Empty).AllowAnonymous().MapStandardHealthChecks();
 app.MapOpenApi().AllowAnonymous().RequireRateLimiting(PublicRateLimitOptions.PolicyName);
 
 app.MapControllers();
+app.MapKbImages();
 
 app.Run();
 
```

- [ ] **Step 5: Add the setting to its files and the runbook (D-043)**

A new setting is the D-043 edits: the host's `appsettings.json` (blank: the code default is "none"), its `.env.example` (blank, with a comment) and the matching `deploy/.env.api.example` (blank, required). Compose owns neither the local nor the deploy value, so neither compose file changes. The runbook tells the operator the Api needs the setting and that the reverse proxy must route `/kb-images/` to the Api.

Modify `src/TechStrap.Api/appsettings.json` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -42,6 +42,7 @@
     "PerAddressWindowMinutes": 60
   },
   "TECHSTRAP_PORTAL_PUBLIC_URL": "",
+  "TECHSTRAP_API_PUBLIC_URL": "",
   "TECHSTRAP_ADMIN_PUBLIC_URL": "",
   "TECHSTRAP_AUTOCLOSE_DAYS": "7",
   "Storage": {
```

Modify `src/TechStrap.Api/.env.example` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -44,6 +44,10 @@ LOSTLINK__PERADDRESSWINDOWMINUTES=60
 # --- Public portal base URL used in customer links (required, absolute http or https) ---
 TECHSTRAP_PORTAL_PUBLIC_URL=http://localhost:8082
 
+# --- The Api's own public base URL, used to build knowledge-base image URLs ({url}/kb-images/{name}) ---
+# Required in Production (absolute http or https; the reverse proxy must route /kb-images/ to the Api). Blank here: the origin of the uploading request is used.
+TECHSTRAP_API_PUBLIC_URL=
+
 # Optional: Admin app base URL; when set, assignment emails link to {url}/tickets/{number}.
 # Example: http://localhost:8081
 TECHSTRAP_ADMIN_PUBLIC_URL=
```

Modify `deploy/.env.api.example` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -53,6 +53,8 @@ LOSTLINK__PERADDRESSWINDOWMINUTES=60
 # -- Public URLs [Api] --
 # Required: the customer portal base URL as customers see it, absolute http or https (emailed links and canonical URLs).
 TECHSTRAP_PORTAL_PUBLIC_URL=
+# Required: the Api base URL as readers of the portal and the Admin reach it, absolute http or https. Knowledge-base images load from {url}/kb-images/{name}, so the reverse proxy must route /kb-images/ to the Api.
+TECHSTRAP_API_PUBLIC_URL=
 # Optional: the Admin base URL; when set, assignment emails link to {url}/tickets/{number}.
 TECHSTRAP_ADMIN_PUBLIC_URL=
 
```

Modify `docs/self-hosting/DEPLOYMENT.md` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -80,7 +80,7 @@ Keep a filled env file out of the repository. A new setting is added to `appsett
 
    | File | Required | Notes |
    | --- | --- | --- |
-   | `.env.api` | `ConnectionStrings__TechStrap`, `AUTHENTICATION__JWTBEARER__AUTHORITY`, `AUTHENTICATION__JWTBEARER__AUDIENCES__0` (present and blank in the template: fill it), `TECHSTRAP_PORTAL_PUBLIC_URL` | The Api trusts the compose subnet and `REVERSE_PROXY_CIDR`. `TECHSTRAP_ADMIN_PUBLIC_URL` is optional. |
+   | `.env.api` | `ConnectionStrings__TechStrap`, `AUTHENTICATION__JWTBEARER__AUTHORITY`, `AUTHENTICATION__JWTBEARER__AUDIENCES__0` (present and blank in the template: fill it), `TECHSTRAP_PORTAL_PUBLIC_URL`, `TECHSTRAP_API_PUBLIC_URL` | The Api trusts the compose subnet and `REVERSE_PROXY_CIDR`. `TECHSTRAP_ADMIN_PUBLIC_URL` is optional. `TECHSTRAP_API_PUBLIC_URL` is the Api address as portal readers reach it (D-044): knowledge-base images load from `{url}/kb-images/{name}`, so the reverse proxy must route `/kb-images/` to the Api and the Api refuses to start without the setting. |
    | `.env.worker` | `ConnectionStrings__TechStrap`, `EMAIL__SMTP__HOST`, `EMAIL__SMTP__DEFAULTFROM` | To run without email set `EMAILOUTBOX__ENABLED=false`. Keep `TECHSTRAP_AUTOCLOSE_DAYS` equal to the Api value. |
    | `.env.admin` | `AUTH__AUTHORITY` (https), `AUTH__CLIENTID`, `AUTH__CLIENTSECRET` | See [ADMIN-APP.md](../development/ADMIN-APP.md) and [AGENT-AUTHENTICATION.md](AGENT-AUTHENTICATION.md). The group keys must match the Api. |
    | `.env.portal` | none yet | PHASE-09 adds the Api address and the public URL. |
```

- [ ] **Step 6: Run the tests and confirm they pass**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then:

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter-class "*KbImageStoreTests"
dotnet test --project tests/TechStrap.Application.Tests -c Release --no-build --filter-class "*UploadKbImageRequestHandlerTests"
dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter-class "*KbImageServingTests"
dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter-class "*ApiPublicUrlTests"
dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter-class "*ProductionBlankTemplateTests"
dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter-class "*EnvExampleCompletenessTests"
pwsh -File scripts/Invoke-ScriptTests.ps1
```

Expected: PASS, 35, 11, 18, 22, 16 and 13 tests, and Pester 280 (`ConfigContract.Tests.ps1` stays green with the new key in its blank list). `ApiPublicUrlTests` runs in `ProcessEnvironmentCollection` (it starts hosts in Production).

- [ ] **Step 7: Prove each pin with a mutation**

Applied to the finished code, the named tests run, the change reverted (same method as Task 1).

| Mutation | Failing tests |
| --- | --- |
| `KbImageName` pattern: `\z` becomes `$` (accepts a trailing newline) | `UploadKbImageRequestHandlerTests.Only_the_exact_name_shape_the_store_writes_is_valid` (`...png` followed by a newline) |
| `KbImageName` pattern: drop the leading `^` | `KbImageStoreTests.A_name_the_store_could_not_have_written_is_never_opened` (`a/../0123...png`) |
| `KbImageStore`: the read-back size check `content.Length > MaxImageBytes` is loosened to `* 4` | `KbImageStoreTests.A_file_over_the_limit_is_refused_by_its_declared_length_and_by_its_real_length` |
| `KbImageSignatures.Identify`: skip the markup scan (`return extension;`) | `KbImageStoreTests.Anything_that_is_not_a_plain_png_jpeg_gif_or_webp_is_refused_and_nothing_is_stored` for "a jpeg with an svg inside", "a png with a script in a text chunk" and "a gif that is really a page" |
| `KbImageSignatures.Match`: accept any file that starts with the PNG signature (drop the `IHDR` and length check) | `KbImageStoreTests.Anything_that_is_not_...` for "a png signature with no header chunk" |
| `KbImageEndpoints`: `Cache-Control` becomes `no-cache` | `KbImageServingTests.An_anonymous_caller_gets_the_image_with_safe_headers_and_a_long_immutable_cache` |
| `AttachmentSandbox`: remove `"/kb-images"` from the prefixes | `KbImageServingTests.An_anonymous_caller_gets_the_image_with_safe_headers_and_a_long_immutable_cache` (no `sandbox` in the CSP) |
| `KbImageUrls`: ignore `PublicUrl` (always use the request) | `ApiPublicUrlTests.A_configured_address_builds_the_url_and_ignores_the_request_host` |
| `KbImageUrls`: always `http://localhost` instead of the request origin | `ApiPublicUrlTests.A_blank_address_falls_back_to_the_origin_of_the_request` |
| `ApiPublicUrlOptions.IsAcceptable`: a blank value is acceptable everywhere (`return true;`) | `ApiPublicUrlTests.Production_refuses_to_start_without_it_and_names_the_key` and three cases of `The_value_is_checked_the_way_the_start_up_check_does` |
| `ApiPublicUrlOptions.IsAcceptable`: drop the user-info check | `ApiPublicUrlTests.The_value_is_checked_the_way_the_start_up_check_does` (`https://user:pw@api.example.com`) |

One mutation is a non-pin, recorded honestly: setting the endpoint's own `X-Content-Type-Options` to an empty value leaves `KbImageServingTests` green, because the shared security-headers middleware sets `nosniff` on every response. The endpoint keeps its own line so the route stays safe if that configuration is ever changed; the test pins the header the browser sees.

- [ ] **Step 8: Whole-project check**

Run: `dotnet test --solution TechStrap.CI.slnf -c Release` and `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS. (`OpenApiSurfaceTests` stays green because the image route is excluded from the OpenAPI description.)

- [ ] **Step 9: Commit**

```bash
git add src \
  deploy/.env.api.example \
  docs/self-hosting/DEPLOYMENT.md \
  scripts/tests \
  tests
git diff --cached --stat
git commit -m "feat(kb): image upload store, public image serving and TECHSTRAP_API_PUBLIC_URL" -m "IKbImageStore stores png, jpeg, gif and webp (recognized by their first bytes and structure, never by name or declared type; SVG and anything carrying markup refused, 5 MB limit) under a random kb-images key. GET /kb-images/{name} serves only names the store could have written, anonymously, with nosniff, a sandbox CSP and a one-year immutable cache. The new setting TECHSTRAP_API_PUBLIC_URL builds the absolute image URL: required outside Development, validated on start, and never taken from the request when it is set. It follows the four D-043 edits and the runbook names the proxy route." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 6: Public handlers: search with `ts_headline`, the article page, the categories and the sitemap

**Review Focus pin:** Review Focus 3 (unpublished or wrong-product content leaking publicly). Pinned here by `PublicKbIntegrationTests` over a real Postgres (drafts and archived articles never reach search, the article page, the categories or the sitemap; another product's articles never do; shared ones do; an inactive product looks like an unknown one; the snippet is plain text, cut from the summary only, and the consumer encodes it) and `PublicKbHandlerTests`. Task 7 repeats the flow over HTTP with the cache headers.

**Files:**
- Create: `src/TechStrap.Application/Knowledge/GetKbSitemapRequestHandler.cs`
- Create: `src/TechStrap.Application/Knowledge/GetPublishedKbArticleRequestHandler.cs`
- Create: `src/TechStrap.Application/Knowledge/ListPublicKbCategoriesRequestHandler.cs`
- Create: `src/TechStrap.Application/Knowledge/PublicKbModels.cs`
- Create: `src/TechStrap.Application/Knowledge/PublicProductScope.cs`
- Create: `src/TechStrap.Application/Knowledge/SearchPublicKbArticlesRequestHandler.cs`
- Modify: `src/TechStrap.Application/Persistence/IKbRepository.cs`
- Modify: `src/TechStrap.Contracts/Kb/KbNames.cs`
- Create: `src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.PublicReads.cs`
- Modify: `src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.cs`
- Test (create): `tests/TechStrap.Application.Tests/Knowledge/PublicKbHandlerTests.cs`
- Test (create): `tests/TechStrap.Infrastructure.IntegrationTests/PublicKbIntegrationTests.cs`

**Interfaces:**
- Consumes: `IProductRepository.GetByKeyAsync`, `IKbRepository` and `KbRepository` (Task 1), `IKbContentRenderer` (Task 2), `KbErrors.ArticleNotFound` (Task 3), `KbLimits` (Task 1), `FullTextSearch.Config`, `SearchText.Normalize`, `Paging`, `PagedResult<T>`, `PagedResponse<T>`, `KbFixture` and `PublicKbIntegrationTests` seed helpers (tests).
- Produces:
  - Application models: `PublicKbSearchQuery(string Text, Guid ProductId, string? CategorySlug = null, int Page = 1, int PageSize = 10)`, `PublicKbSearchHit(Slug, Title, Snippet, CategorySlug, CategoryName, ProductKey)` (the snippet is plain text, not yet encoded), `PublicKbArticleView(KbArticle Article, string CategorySlug, string CategoryName, string? ProductKey)`, `PublicKbCategoryCount(KbCategory Category, int ArticleCount)`, `PublicKbSitemapRow(ProductKey, CategorySlug, Slug, UpdatedAt)`.
  - `IKbRepository`: `SearchPublicAsync(PublicKbSearchQuery, ct) : Task<PagedResult<PublicKbSearchHit>>`, `GetPublicArticleAsync(Guid productId, string categorySlug, string slug, ct) : Task<PublicKbArticleView?>`, `ListPublicCategoriesAsync(Guid productId, ct)`, `ListPublicSitemapAsync(Guid productId, ct)`. Every one forces `Status = Published`, requires the article and its category to be the product's or shared, and takes no status from the caller.
  - `ISearchPublicKbArticlesRequestHandler.HandleAsync(string? productKey, string? text, string? categorySlug, int page, int pageSize, CancellationToken) : Task<Result<PagedResponse<PublicKbSearchResultDto>>>`: 10 a page by default, at most 25; blank text or an unknown, blank or inactive product gives an empty page; the snippet is `WebUtility.HtmlEncode`d.
  - `IGetPublishedKbArticleRequestHandler.HandleAsync(string? productKey, string? categorySlug, string? slug, CancellationToken) : Task<Result<PublishedKbArticleDto>>`: `Html` from `IKbContentRenderer`; everything unavailable is the same 404 `kb-article-not-found`.
  - `IListPublicKbCategoriesRequestHandler.HandleAsync(string? productKey, CancellationToken) : Task<Result<IReadOnlyList<PublicKbCategoryDto>>>` (empty categories left out) and `IGetKbSitemapRequestHandler.HandleAsync(string? productKey, CancellationToken) : Task<Result<IReadOnlyList<KbSitemapEntryDto>>>` (newest update first, at most `KbLimits.MaxSitemapEntries` = 10,000). Both give `[]` for an unknown or inactive product.
  - `PublicProductScope.ResolveAsync(IProductRepository, string? productKey, ct)` (internal): the product, or null for a blank, unknown or inactive key.

- [ ] **Step 1: Write the failing tests**

`PublicKbIntegrationTests` seeds two products, a shared category, a category per product and published, draft and archived articles, plus two deliberately misfiled published articles (an Orbitly article in an Acme category and an Acme article in an Orbitly category: data the handlers refuse to create but a hand edit could leave), so each of the two scope conditions in the query is pinned on its own. The handlers run over the real repository and the real renderer.

Create `tests/TechStrap.Application.Tests/Knowledge/PublicKbHandlerTests.cs`:

```csharp
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Application.Content;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Tests.Knowledge;

public sealed class PublicKbHandlerTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private readonly KbFixture _kb = new();
    private readonly IKbContentRenderer _renderer = Substitute.For<IKbContentRenderer>();

    public PublicKbHandlerTests()
    {
        _kb.Products.GetByKeyAsync("orbitly", Arg.Any<CancellationToken>()).Returns(_kb.Orbitly);
        _kb.Products.GetByKeyAsync("paperplane", Arg.Any<CancellationToken>()).Returns(_kb.Paperplane);
        var dormant = Product.Create("dormant", "Dormant", "DOR", null, _kb.Clock).Value;
        dormant.SetActive(false);
        _kb.Products.GetByKeyAsync("dormant", Arg.Any<CancellationToken>()).Returns(dormant);
    }

    private SearchPublicKbArticlesRequestHandler Searcher() => new(_kb.Products, _kb.KnowledgeBase);

    private GetPublishedKbArticleRequestHandler Reader() => new(_kb.Products, _kb.KnowledgeBase, _renderer);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("nobody")]
    [InlineData("dormant")]
    public async Task An_unknown_inactive_or_blank_product_gives_an_empty_page_on_every_public_list_and_never_reaches_the_repository(string? key)
    {
        var search = await Searcher().HandleAsync(key, "router", null, 1, 10, Ct);
        var categories = await new ListPublicKbCategoriesRequestHandler(_kb.Products, _kb.KnowledgeBase).HandleAsync(key, Ct);
        var sitemap = await new GetKbSitemapRequestHandler(_kb.Products, _kb.KnowledgeBase).HandleAsync(key, Ct);

        search.Value.ShouldSatisfyAllConditions(page => page.Items.ShouldBeEmpty(), page => page.TotalCount.ShouldBe(0));
        categories.Value.ShouldBeEmpty();
        sitemap.Value.ShouldBeEmpty();
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().SearchPublicAsync(default!, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListPublicCategoriesAsync(default, Ct);
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().ListPublicSitemapAsync(default, Ct);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_search_text_gives_an_empty_page_without_a_query(string? text)
    {
        var result = await Searcher().HandleAsync("orbitly", text, null, 1, 10, Ct);

        result.Value.Items.ShouldBeEmpty();
        await _kb.KnowledgeBase.DidNotReceiveWithAnyArgs().SearchPublicAsync(default!, Ct);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(10, 10)]
    [InlineData(25, 25)]
    [InlineData(26, 25)]
    [InlineData(1000, 25)]
    public async Task The_page_size_defaults_to_ten_and_is_capped_at_twenty_five(int requested, int used)
    {
        _kb.KnowledgeBase.SearchPublicAsync(Arg.Any<PublicKbSearchQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<PublicKbSearchHit>([], 1, used, 0));

        await Searcher().HandleAsync("orbitly", "router", "faq", 1, requested, Ct);

        await _kb.KnowledgeBase.Received(1).SearchPublicAsync(new PublicKbSearchQuery("router", _kb.Orbitly.Id, "faq", 1, used), Ct);
    }

    [Fact]
    public async Task The_snippet_is_html_encoded_before_it_leaves_the_handler()
    {
        _kb.KnowledgeBase.SearchPublicAsync(Arg.Any<PublicKbSearchQuery>(), Arg.Any<CancellationToken>()).Returns(new PagedResult<PublicKbSearchHit>(
            [new PublicKbSearchHit("reset", "Reset", "Use <script>alert(1)</script> & \"quotes\" 'here'", "general", "General", null)], 1, 10, 1));

        var result = await Searcher().HandleAsync("orbitly", "reset", null, 1, 10, Ct);

        var hit = result.Value.Items.ShouldHaveSingleItem();
        hit.Snippet.ShouldBe("Use &lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;quotes&quot; &#39;here&#39;");
        hit.ShouldSatisfyAllConditions(item => item.Slug.ShouldBe("reset"), item => item.CategorySlug.ShouldBe("general"), item => item.ProductKey.ShouldBeNull());
    }

    [Fact]
    public async Task The_article_is_rendered_by_the_shared_renderer_and_carries_no_author_or_id()
    {
        var published = new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        var article = _kb.StoredArticle(KbArticleStatus.Published, _kb.Orbitly.Id, Guid.NewGuid(), "reset-password", published);
        _kb.KnowledgeBase.GetPublicArticleAsync(_kb.Orbitly.Id, "account", "reset-password", Ct).Returns(new PublicKbArticleView(article, "account", "Account", "orbitly"));
        _renderer.Render("Steps.").Returns("<p>Steps.</p>\n");

        var result = await Reader().HandleAsync(" orbitly ", " account ", " reset-password ", Ct);

        result.Value.ShouldBe(new PublishedKbArticleDto("orbitly", "account", "Account", "reset-password", "Reset your password", "Short", "<p>Steps.</p>\n", published, article.UpdatedAt));
    }

    [Theory]
    [InlineData("nobody", "account", "reset-password")]
    [InlineData("dormant", "account", "reset-password")]
    [InlineData("orbitly", "account", "missing")]
    [InlineData("orbitly", "", "reset-password")]
    [InlineData("orbitly", "account", " ")]
    [InlineData(null, "account", "reset-password")]
    public async Task Everything_that_is_not_a_visible_published_article_is_the_same_not_found(string? product, string category, string slug)
    {
        var result = await Reader().HandleAsync(product, category, slug, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            error => error.Kind.ShouldBe(ResultErrorKind.NotFound),
            error => error.Code.ShouldBe("kb-article-not-found"),
            error => error.Message.ShouldBe("That article does not exist."));
        _renderer.DidNotReceiveWithAnyArgs().Render(default!);
    }

    [Fact]
    public async Task An_article_without_a_publish_time_is_not_served()
    {
        var article = _kb.StoredArticle(KbArticleStatus.Published, _kb.Orbitly.Id, Guid.NewGuid(), "reset-password", null);
        _kb.KnowledgeBase.GetPublicArticleAsync(_kb.Orbitly.Id, "account", "reset-password", Ct).Returns(new PublicKbArticleView(article, "account", "Account", "orbitly"));

        var result = await Reader().HandleAsync("orbitly", "account", "reset-password", Ct);

        result.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
    }

    [Fact]
    public async Task Categories_and_sitemap_entries_are_mapped_from_the_repository_rows()
    {
        var category = KbCategory.Restore(Guid.NewGuid(), null, "Account", "account", "Sign-in help", 1, 3);
        _kb.KnowledgeBase.ListPublicCategoriesAsync(_kb.Orbitly.Id, Ct).Returns([new PublicKbCategoryCount(category, 4)]);
        var updated = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
        _kb.KnowledgeBase.ListPublicSitemapAsync(_kb.Orbitly.Id, Ct).Returns([new PublicKbSitemapRow(null, "account", "reset-password", updated)]);

        var categories = await new ListPublicKbCategoriesRequestHandler(_kb.Products, _kb.KnowledgeBase).HandleAsync("orbitly", Ct);
        var sitemap = await new GetKbSitemapRequestHandler(_kb.Products, _kb.KnowledgeBase).HandleAsync("orbitly", Ct);

        categories.Value.ShouldBe([new PublicKbCategoryDto("account", "Account", "Sign-in help", 4)]);
        sitemap.Value.ShouldBe([new KbSitemapEntryDto(null, "account", "reset-password", updated)]);
    }
}
```

Create `tests/TechStrap.Infrastructure.IntegrationTests/PublicKbIntegrationTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;
using TechStrap.Domain.Products;
using TechStrap.Infrastructure.Content;

namespace TechStrap.Infrastructure.IntegrationTests;

/// <summary>
/// Review Focus 3 (unpublished or wrong-product content leaking publicly), against real Postgres: the four public handlers over the real repository and renderer.
/// Drafts and archived articles never appear in search, the article page, the categories or the sitemap; another product's articles never appear; shared ones do.
/// </summary>
public sealed class PublicKbIntegrationTests(PostgresFixture postgres) : PostgresIntegrationTestBase(postgres)
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private sealed record World(
        PersistenceTestHost Host,
        TicketScenario Scenario,
        KbCategory Shared,
        KbCategory AcmeCategory,
        KbCategory OrbitlyCategory,
        Dictionary<string, KbArticle> Articles);

    private static async Task<World> SeedAsync(PersistenceTestHost host)
    {
        var scenario = await TicketScenario.CreateAsync(host);
        var shared = KbCategory.Create(null, "general", "General", 5, host.Clock, "For everyone").Value;
        var acmeCategory = KbCategory.Create(scenario.Acme.Id, "acme-cat", "Acme things", 1, host.Clock).Value;
        var orbitlyCategory = KbCategory.Create(scenario.Orbitly.Id, "orb-cat", "Orbitly things", 1, host.Clock).Value;
        var empty = KbCategory.Create(scenario.Acme.Id, "empty-cat", "Empty", 9, host.Clock).Value;
        var articles = new Dictionary<string, KbArticle>
        {
            ["acme-published"] = Article(scenario, host, scenario.Acme.Id, acmeCategory, "acme-published", "Router reset guide", "How to reset the router", "# Steps\n\n| a | b |\n|---|---|\n| 1 | 2 |"),
            ["acme-draft"] = Article(scenario, host, scenario.Acme.Id, acmeCategory, "acme-draft", "Router draft", "A draft about the router", "body"),
            ["acme-archived"] = Article(scenario, host, scenario.Acme.Id, acmeCategory, "acme-archived", "Router archived", "An archived router note", "body"),
            ["orbitly-published"] = Article(scenario, host, scenario.Orbitly.Id, orbitlyCategory, "orbitly-published", "Router for Orbitly", "Orbitly router help", "body"),
            ["shared-published"] = Article(scenario, host, null, shared, "shared-published", "Shared router tips", "Tips for any router", "body"),
            ["shared-draft"] = Article(scenario, host, null, shared, "shared-draft", "Shared router draft", "A shared router draft", "body"),

            // Data the handlers would refuse to create but a hand edit or an older version could have left: each pairs an article with a category of the other scope.
            ["orbitly-in-acme-category"] = Article(scenario, host, scenario.Orbitly.Id, acmeCategory, "orbitly-in-acme-category", "Router misfiled by Orbitly", "A misfiled router note", "body"),
            ["acme-in-orbitly-category"] = Article(scenario, host, scenario.Acme.Id, orbitlyCategory, "acme-in-orbitly-category", "Router misfiled by Acme", "A misfiled router note", "body"),
        };
        Publish(host, articles["acme-published"], articles["orbitly-published"], articles["shared-published"], articles["orbitly-in-acme-category"], articles["acme-in-orbitly-category"]);
        Publish(host, articles["acme-archived"]);
        articles["acme-archived"].Archive(host.Clock).IsSuccess.ShouldBeTrue();
        (await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            foreach (var category in new[] { shared, acmeCategory, orbitlyCategory, empty })
            {
                kb.AddCategory(category);
            }

            foreach (var article in articles.Values)
            {
                kb.AddArticle(article);
            }

            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();
        return new World(host, scenario, shared, acmeCategory, orbitlyCategory, articles);
    }

    private static KbArticle Article(TicketScenario scenario, PersistenceTestHost host, Guid? productId, KbCategory category, string slug, string title, string summary, string body) =>
        KbArticle.Create(productId, category.Id, slug, title, summary, body, scenario.Agent.Id, host.Clock).Value;

    private static void Publish(PersistenceTestHost host, params KbArticle[] articles)
    {
        foreach (var article in articles)
        {
            article.Publish(host.Clock).IsSuccess.ShouldBeTrue();
        }
    }

    private static Task<T> WithHandlersAsync<T>(World world, Func<IServiceProvider, Task<T>> work) => world.Host.ReadAsync(work);

    private static ISearchPublicKbArticlesRequestHandler Search(IServiceProvider sp) =>
        new SearchPublicKbArticlesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>());

    private static Task<List<string>> SearchSlugsAsync(World world, string productKey, string text, string? category = null) =>
        WithHandlersAsync(world, async sp =>
        {
            var result = await Search(sp).HandleAsync(productKey, text, category, 1, 25, Ct);
            return result.Value.Items.Select(item => item.Slug).ToList();
        });

    [Fact]
    public async Task Search_returns_only_published_articles_of_the_product_and_the_shared_space()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        (await SearchSlugsAsync(world, "acme", "router")).Order().ShouldBe(["acme-published", "shared-published"]);
        (await SearchSlugsAsync(world, "orbitly", "router")).Order().ShouldBe(["orbitly-published", "shared-published"]);
    }

    [Fact]
    public async Task Archiving_a_published_article_takes_it_out_of_search_the_article_page_the_categories_and_the_sitemap()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);
        var published = world.Articles["acme-published"];
        (await SearchSlugsAsync(world, "acme", "router")).ShouldContain("acme-published");
        (await host.CommitAsync(async sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            var loaded = (await kb.GetArticleAsync(published.Id, Ct))!;
            loaded.Archive(host.Clock).IsSuccess.ShouldBeTrue();
            kb.UpdateArticle(loaded);
        })).IsSuccess.ShouldBeTrue();

        (await SearchSlugsAsync(world, "acme", "router")).ShouldBe(["shared-published"]);
        var page = await WithHandlersAsync(world, sp => new GetPublishedKbArticleRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>(), new KbContentRenderer())
            .HandleAsync("acme", "acme-cat", "acme-published", Ct));
        page.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
        var sitemap = await WithHandlersAsync(world, sp => new GetKbSitemapRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", Ct));
        sitemap.Value.Select(entry => entry.Slug).ShouldBe(["shared-published"]);
        var categories = await WithHandlersAsync(world, sp => new ListPublicKbCategoriesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", Ct));
        categories.Value.Select(category => category.Slug).ShouldBe(["general"]);
    }

    [Fact]
    public async Task A_category_filter_narrows_the_search_to_that_category()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        (await SearchSlugsAsync(world, "acme", "router", "general")).ShouldBe(["shared-published"]);
        (await SearchSlugsAsync(world, "acme", "router", "acme-cat")).ShouldBe(["acme-published"]);
        (await SearchSlugsAsync(world, "acme", "router", "orb-cat")).ShouldBeEmpty();
        (await SearchSlugsAsync(world, "acme", "router", "no-such-category")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_summary_snippet_is_plain_text_and_every_html_character_in_it_is_encoded()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
        var hostile = Article(scenario, host, null, category, "hostile", "Printer help", "Reset the printer <script>alert(1)</script> & <img src=x onerror=alert(2)> now", "body");
        Publish(host, hostile);
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IKbRepository>().AddCategory(category);
            sp.GetRequiredService<IKbRepository>().AddArticle(hostile);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var result = await host.ReadAsync(sp => Search(sp).HandleAsync("acme", "printer", null, 1, 10, Ct));

        var snippet = result.Value.Items.ShouldHaveSingleItem().Snippet;
        snippet.ShouldNotContain("<");
        snippet.ShouldNotContain(">");
        snippet.ShouldContain("&amp;");
        snippet.ShouldContain("&lt;img");
    }

    [Fact]
    public async Task The_snippet_comes_from_the_summary_never_the_body()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
        var article = Article(scenario, host, null, category, "body-match", "Unrelated title", "A short summary about nothing", "The secret-body-phrase quokka lives here");
        Publish(host, article);
        (await host.CommitAsync(sp =>
        {
            sp.GetRequiredService<IKbRepository>().AddCategory(category);
            sp.GetRequiredService<IKbRepository>().AddArticle(article);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var result = await host.ReadAsync(sp => Search(sp).HandleAsync("acme", "quokka", null, 1, 10, Ct));

        var hit = result.Value.Items.ShouldHaveSingleItem();
        hit.Snippet.ShouldBe("A short summary about nothing");
        hit.Snippet.ShouldNotContain("quokka");
    }

    [Fact]
    public async Task The_article_page_serves_a_published_article_in_scope_and_nothing_else()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);
        Task<Result<PublishedKbArticleDto>> Get(string product, string category, string slug) =>
            WithHandlersAsync(world, sp => new GetPublishedKbArticleRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>(), new KbContentRenderer())
                .HandleAsync(product, category, slug, Ct));

        var own = await Get("acme", "acme-cat", "acme-published");
        var shared = await Get("orbitly", "general", "shared-published");

        own.Value.ShouldSatisfyAllConditions(
            dto => dto.ProductKey.ShouldBe("acme"),
            dto => dto.CategorySlug.ShouldBe("acme-cat"),
            dto => dto.CategoryName.ShouldBe("Acme things"),
            dto => dto.Title.ShouldBe("Router reset guide"),
            dto => dto.Html.ShouldContain("<table>"),
            dto => dto.PublishedAt.ShouldBe(world.Articles["acme-published"].PublishedAt!.Value));
        shared.Value.ProductKey.ShouldBeNull();
        foreach (var (product, category, slug) in new[]
        {
            ("acme", "acme-cat", "acme-draft"),
            ("acme", "acme-cat", "acme-archived"),
            ("acme", "orb-cat", "orbitly-published"),
            ("acme", "orb-cat", "acme-published"),
            ("orbitly", "acme-cat", "acme-published"),
            ("acme", "general", "acme-published"),
            ("acme", "general", "shared-draft"),
            ("acme", "acme-cat", "no-such-article"),
            ("nope", "acme-cat", "acme-published"),
        })
        {
            (await Get(product, category, slug)).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found", $"{product}/{category}/{slug}");
        }
    }

    [Fact]
    public async Task An_inactive_product_is_indistinguishable_from_an_unknown_one()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);
        var dormant = Product.Create("dormant", "Dormant", "DOR", null, host.Clock).Value;
        dormant.SetActive(false);
        (await host.CommitAsync(sp => { sp.GetRequiredService<IProductRepository>().Add(dormant); return Task.CompletedTask; })).IsSuccess.ShouldBeTrue();

        var inactive = await SearchSlugsAsync(world, "dormant", "router");
        var unknown = await SearchSlugsAsync(world, "never-heard-of-it", "router");
        var categories = await WithHandlersAsync(world, sp => new ListPublicKbCategoriesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("dormant", Ct));
        var sitemap = await WithHandlersAsync(world, sp => new GetKbSitemapRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("dormant", Ct));
        var article = await WithHandlersAsync(world, sp => new GetPublishedKbArticleRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>(), new KbContentRenderer())
            .HandleAsync("dormant", "general", "shared-published", Ct));

        inactive.ShouldBeEmpty();
        unknown.ShouldBeEmpty();
        categories.Value.ShouldBeEmpty();
        sitemap.Value.ShouldBeEmpty();
        article.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-found");
    }

    [Fact]
    public async Task Categories_count_only_published_articles_the_product_can_see_and_leave_empty_ones_out()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        var acme = await WithHandlersAsync(world, sp => new ListPublicKbCategoriesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", Ct));
        var orbitly = await WithHandlersAsync(world, sp => new ListPublicKbCategoriesRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("orbitly", Ct));

        acme.Value.ShouldBe([new PublicKbCategoryDto("acme-cat", "Acme things", null, 1), new PublicKbCategoryDto("general", "General", "For everyone", 1)]);
        orbitly.Value.ShouldBe([new PublicKbCategoryDto("orb-cat", "Orbitly things", null, 1), new PublicKbCategoryDto("general", "General", "For everyone", 1)]);
    }

    [Fact]
    public async Task The_sitemap_lists_published_articles_in_scope_newest_first_with_their_product_key_and_category()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);

        var sitemap = await WithHandlersAsync(world, sp => new GetKbSitemapRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>()).HandleAsync("acme", Ct));

        sitemap.Value.Select(entry => (entry.ProductKey, entry.CategorySlug, entry.Slug)).Order().ShouldBe(
            [((string?)null, "general", "shared-published"), ("acme", "acme-cat", "acme-published")]);
    }

    [Fact]
    public async Task Search_results_are_ranked_title_above_summary_above_body_and_paged()
    {
        await using var host = new PersistenceTestHost(Database);
        var scenario = await TicketScenario.CreateAsync(host);
        var category = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
        var inBody = Article(scenario, host, null, category, "in-body", "Unrelated guide", "A summary", "Steps to reset your password are below");
        var inSummary = Article(scenario, host, null, category, "in-summary", "Another guide", "How to reset things", "Body text");
        var inTitle = Article(scenario, host, null, category, "in-title", "Reset your password", "A summary", "Body text");
        Publish(host, inBody, inSummary, inTitle);
        (await host.CommitAsync(sp =>
        {
            var kb = sp.GetRequiredService<IKbRepository>();
            kb.AddCategory(category);
            kb.AddArticle(inBody);
            kb.AddArticle(inSummary);
            kb.AddArticle(inTitle);
            return Task.CompletedTask;
        })).IsSuccess.ShouldBeTrue();

        var all = await host.ReadAsync(sp => Search(sp).HandleAsync("acme", "reset", null, 1, 25, Ct));
        var second = await host.ReadAsync(sp => Search(sp).HandleAsync("acme", "reset", null, 2, 2, Ct));

        all.Value.Items.Select(item => item.Slug).ShouldBe(["in-title", "in-summary", "in-body"]);
        second.Value.ShouldSatisfyAllConditions(page => page.TotalCount.ShouldBe(3), page => page.PageSize.ShouldBe(2), page => page.Items.ShouldHaveSingleItem().Slug.ShouldBe("in-body"));
    }

    [Fact]
    public async Task The_preview_and_the_published_page_render_the_same_markdown_to_the_same_html()
    {
        await using var host = new PersistenceTestHost(Database);
        var world = await SeedAsync(host);
        var renderer = new KbContentRenderer();
        const string Markdown = "# Steps\n\n| a | b |\n|---|---|\n| 1 | 2 |";

        var preview = await new RenderKbPreviewRequestHandler(renderer).HandleAsync(new KbPreviewRequest(Markdown), Ct);
        var page = await WithHandlersAsync(world, sp => new GetPublishedKbArticleRequestHandler(sp.GetRequiredService<IProductRepository>(), sp.GetRequiredService<IKbRepository>(), renderer)
            .HandleAsync("acme", "acme-cat", "acme-published", Ct));

        page.Value.Html.ShouldBe(preview.Value.Html);
    }

    [Fact]
    public void The_public_article_dto_has_no_author_and_no_id()
    {
        var names = typeof(PublishedKbArticleDto).GetProperties().Select(property => property.Name).ToArray();

        names.ShouldNotContain("AuthorAgentId");
        names.ShouldNotContain("Id");
        names.ShouldNotContain("Version");
        names.ShouldNotContain("BodyMarkdown");
        typeof(PublicKbSearchResultDto).GetProperties().Select(property => property.Name).ShouldNotContain("Id");
    }
}
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: the build FAILS with `CS0246` ("could not be found") for `SearchPublicKbArticlesRequestHandler`, `GetPublishedKbArticleRequestHandler`, `ISearchPublicKbArticlesRequestHandler` and the other new public handlers and models.

- [ ] **Step 3: Add the public read models and the repository queries**

The article query is `PublishedIn(productId)`: Published, the article is the product's or shared, and its category exists and is the product's or shared. The search then ranks with `ts_rank` over the weighted vector (title A, summary B, body C), pages, and fetches the names for the page of ids. `ts_headline` runs afterwards, only for that page, over the summary only (cheap, and a body match with no summary match shows the start of the summary). The option string uses quoted empty markers (`StartSel="", StopSel=""`): unquoted empty values are read as a comma, and the highlight tags would otherwise be encoded into the text. The ranked order is kept because a join does not keep it. `KbRepository` becomes `partial` so the public reads live in their own file.

Create `src/TechStrap.Application/Knowledge/PublicKbModels.cs`:

```csharp
using TechStrap.Domain.Knowledge;

namespace TechStrap.Application.Knowledge;

/// <summary>
/// A public (portal) full-text search over Published articles (D-044). With <see cref="ProductId"/> it searches that product's articles and the shared
/// ones; <see cref="CategorySlug"/> narrows to one category. Implementations normalize <see cref="Page"/> and <see cref="PageSize"/>
/// through <c>Paging</c> and truncate <see cref="Text"/> to <c>DomainLimits.SearchTextMaxLength</c>.
/// </summary>
public sealed record PublicKbSearchQuery(string Text, Guid ProductId, string? CategorySlug = null, int Page = 1, int PageSize = 10);

/// <summary>One search hit. <see cref="Snippet"/> is plain text cut from the article summary by <c>ts_headline</c>; it is NOT encoded yet, so a handler must encode it before it leaves the API.</summary>
public sealed record PublicKbSearchHit(string Slug, string Title, string Snippet, string CategorySlug, string CategoryName, string? ProductKey);

/// <summary>A Published article with the category and product names the portal address needs. <see cref="ProductKey"/> is null for a shared article.</summary>
public sealed record PublicKbArticleView(KbArticle Article, string CategorySlug, string CategoryName, string? ProductKey);

/// <summary>A category with the number of Published articles a product can see in it.</summary>
public sealed record PublicKbCategoryCount(KbCategory Category, int ArticleCount);

public sealed record PublicKbSitemapRow(string? ProductKey, string CategorySlug, string Slug, DateTimeOffset UpdatedAt);
```

Modify `src/TechStrap.Application/Persistence/IKbRepository.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -45,6 +45,24 @@ public interface IKbRepository
     /// <summary>See the category and product rule on <see cref="AddArticle"/>. Throws when the article was not loaded in this unit of work. After a failed commit reload the article rather than retrying the same object; after a successful commit its <c>Version</c> is stale, so reload before updating again.</summary>
     void UpdateArticle(KbArticle article);
 
+    /// <summary>
+    /// Portal search (D-044): only Published articles of the product and the shared space, best match first, with a <c>ts_headline</c> snippet cut from the
+    /// summary (never the body, which is costly). Blank text returns an empty page. A category slug narrows to that category.
+    /// </summary>
+    Task<PagedResult<PublicKbSearchHit>> SearchPublicAsync(PublicKbSearchQuery query, CancellationToken cancellationToken);
+
+    /// <summary>
+    /// Portal article lookup: a Published article visible to the product (its own, else a shared one with the slug) whose category has the given slug
+    /// and is itself visible to the product. Null for anything else, so a draft, an archived article, another product's article and a wrong category look alike.
+    /// </summary>
+    Task<PublicKbArticleView?> GetPublicArticleAsync(Guid productId, string categorySlug, string slug, CancellationToken cancellationToken);
+
+    /// <summary>Categories visible to the product (its own and the shared ones) with their Published article counts, empty ones left out, sort order then name.</summary>
+    Task<IReadOnlyList<PublicKbCategoryCount>> ListPublicCategoriesAsync(Guid productId, CancellationToken cancellationToken);
+
+    /// <summary>Published articles visible to the product, newest update first, at most <c>KbLimits.MaxSitemapEntries</c>.</summary>
+    Task<IReadOnlyList<PublicKbSitemapRow>> ListPublicSitemapAsync(Guid productId, CancellationToken cancellationToken);
+
     /// <summary>
     /// The cross-scope slug rule (D-044). With a product: true when that product or the shared space already has an article with the slug.
     /// With no product (a shared article): true when any article of any scope has it. Any status counts. The unique index alone only
```

Modify `src/TechStrap.Contracts/Kb/KbNames.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -32,4 +32,7 @@ public static class KbLimits
 
     /// <summary>The most characters of a search query the public search reads; longer text is cut.</summary>
     public const int MaxSearchTextChars = 200;
+
+    /// <summary>The most entries one sitemap lists (the newest updates first); the sitemap protocol allows 50,000.</summary>
+    public const int MaxSitemapEntries = 10_000;
 }
```

Modify `src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -8,7 +8,7 @@ using TechStrap.Infrastructure.Persistence.Records;
 
 namespace TechStrap.Infrastructure.Persistence.Repositories;
 
-internal sealed class KbRepository(TechStrapDbContext context) : IKbRepository
+internal sealed partial class KbRepository(TechStrapDbContext context) : IKbRepository
 {
     public async Task<KbArticle?> GetArticleAsync(Guid id, CancellationToken cancellationToken) =>
         (await context.Set<KbArticleRecord>().FirstOrDefaultAsync(a => a.Id == id, cancellationToken))?.ToDomain();
```

Create `src/TechStrap.Infrastructure/Persistence/Repositories/KbRepository.PublicReads.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using SyntaxCircus.Common;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Domain.Knowledge;
using TechStrap.Infrastructure.Persistence.Mapping;
using TechStrap.Infrastructure.Persistence.Records;

namespace TechStrap.Infrastructure.Persistence.Repositories;

// The portal reads (D-044). Every method here forces Status = Published and a product scope; none takes a status from the caller.
internal sealed partial class KbRepository
{
    private sealed class SnippetRow
    {
        public Guid Id { get; set; }

        public string Snippet { get; set; } = string.Empty;
    }

    // Published articles of the product and the shared space whose category is also the product's or shared. An article with no category never qualifies.
    private IQueryable<KbArticleRecord> PublishedIn(Guid productId) =>
        context.Set<KbArticleRecord>().AsNoTracking().Where(article =>
            article.Status == KbArticleStatus.Published
            && (article.ProductId == productId || article.ProductId == null)
            && context.Set<KbCategoryRecord>().Any(category => category.Id == article.CategoryId && (category.ProductId == productId || category.ProductId == null)));

    public async Task<PagedResult<PublicKbSearchHit>> SearchPublicAsync(PublicKbSearchQuery query, CancellationToken cancellationToken)
    {
        var page = Paging.NormalizePage(query.Page);
        var pageSize = Math.Clamp(Paging.NormalizePageSize(query.PageSize), 1, KbLimits.MaxPublicSearchPageSize);
        var text = SearchText.Normalize(query.Text);
        if (string.IsNullOrEmpty(text))
        {
            return new PagedResult<PublicKbSearchHit>([], page, pageSize, 0);
        }

        var matches = PublishedIn(query.ProductId)
            .Where(article => article.SearchVector.Matches(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text)));
        if (!string.IsNullOrWhiteSpace(query.CategorySlug))
        {
            var categorySlug = query.CategorySlug.Trim();
            matches = matches.Where(article => context.Set<KbCategoryRecord>().Any(category => category.Id == article.CategoryId && category.Slug == categorySlug));
        }

        var total = await matches.CountAsync(cancellationToken);
        var ids = await matches
            .OrderByDescending(article => article.SearchVector.Rank(EF.Functions.WebSearchToTsQuery(FullTextSearch.Config, text)))
            .ThenByDescending(article => article.UpdatedAt).ThenByDescending(article => article.Id)
            .Skip(Paging.Offset(page, pageSize)).Take(pageSize)
            .Select(article => article.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return new PagedResult<PublicKbSearchHit>([], page, pageSize, total);
        }

        var rows = await (
            from article in context.Set<KbArticleRecord>().AsNoTracking()
            where ids.Contains(article.Id)
            join category in context.Set<KbCategoryRecord>().AsNoTracking() on article.CategoryId equals category.Id
            join owner in context.Set<ProductRecord>().AsNoTracking() on article.ProductId equals owner.Id into owners
            from owner in owners.DefaultIfEmpty()
            select new { article.Id, article.Slug, article.Title, CategorySlug = category.Slug, CategoryName = category.Name, ProductKey = owner == null ? null : owner.Key })
            .ToListAsync(cancellationToken);

        // ts_headline runs only for the page of ids, over the summary only; empty (quoted) markers keep the snippet plain text.
        var idArray = ids.ToArray();
        var snippets = await context.Database.SqlQuery<SnippetRow>($"""
            SELECT a.id AS "Id",
                   ts_headline('english', coalesce(a.summary, ''), websearch_to_tsquery('english', {text}),
                               'StartSel="", StopSel="", MaxWords=35, MinWords=15, MaxFragments=1, ShortWord=2') AS "Snippet"
            FROM kb_articles a
            WHERE a.id = ANY({idArray})
            """).ToListAsync(cancellationToken);
        var snippetById = snippets.ToDictionary(row => row.Id, row => row.Snippet);
        var rowById = rows.ToDictionary(row => row.Id);

        // The page order is the rank order, which the join does not keep.
        return new PagedResult<PublicKbSearchHit>(
            [.. ids.Where(rowById.ContainsKey).Select(id => rowById[id]).Select(row =>
                new PublicKbSearchHit(row.Slug, row.Title, snippetById.GetValueOrDefault(row.Id, string.Empty), row.CategorySlug, row.CategoryName, row.ProductKey))],
            page, pageSize, total);
    }

    public async Task<PublicKbArticleView?> GetPublicArticleAsync(Guid productId, string categorySlug, string slug, CancellationToken cancellationToken)
    {
        // The product's own article wins over a shared one with the same slug (the cross-scope rule makes that a legacy case only).
        var article = await PublishedIn(productId)
            .Where(a => a.Slug == slug && context.Set<KbCategoryRecord>().Any(category => category.Id == a.CategoryId && category.Slug == categorySlug))
            .OrderBy(a => a.ProductId == null ? 1 : 0)
            .FirstOrDefaultAsync(cancellationToken);
        if (article is null)
        {
            return null;
        }

        var category = await context.Set<KbCategoryRecord>().AsNoTracking().FirstAsync(c => c.Id == article.CategoryId, cancellationToken);
        var productKey = article.ProductId is { } ownerId
            ? await context.Set<ProductRecord>().AsNoTracking().Where(p => p.Id == ownerId).Select(p => p.Key).FirstOrDefaultAsync(cancellationToken)
            : null;
        return new PublicKbArticleView(article.ToDomain(), category.Slug, category.Name, productKey);
    }

    public async Task<IReadOnlyList<PublicKbCategoryCount>> ListPublicCategoriesAsync(Guid productId, CancellationToken cancellationToken)
    {
        var counts = await PublishedIn(productId)
            .GroupBy(article => article.CategoryId)
            .Select(group => new { CategoryId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        if (counts.Count == 0)
        {
            return [];
        }

        var ids = counts.Select(count => count.CategoryId).ToList();
        var categories = await context.Set<KbCategoryRecord>().AsNoTracking()
            .Where(category => ids.Contains(category.Id))
            .OrderBy(category => category.SortOrder).ThenBy(category => category.Name).ThenBy(category => category.Id)
            .ToListAsync(cancellationToken);
        var countById = counts.ToDictionary(count => count.CategoryId!.Value, count => count.Count);
        return [.. categories.Select(category => new PublicKbCategoryCount(category.ToDomain(), countById[category.Id]))];
    }

    public async Task<IReadOnlyList<PublicKbSitemapRow>> ListPublicSitemapAsync(Guid productId, CancellationToken cancellationToken) =>
        await (
            from article in PublishedIn(productId)
            join category in context.Set<KbCategoryRecord>().AsNoTracking() on article.CategoryId equals category.Id
            join owner in context.Set<ProductRecord>().AsNoTracking() on article.ProductId equals owner.Id into owners
            from owner in owners.DefaultIfEmpty()
            orderby article.UpdatedAt descending, article.Id descending
            select new PublicKbSitemapRow(owner == null ? null : owner.Key, category.Slug, article.Slug, article.UpdatedAt))
            .Take(KbLimits.MaxSitemapEntries)
            .ToListAsync(cancellationToken);
}
```

- [ ] **Step 4: Add the four public handlers**

An unknown, blank or inactive product key behaves the same everywhere: an empty list for search, categories and sitemap (so a caller cannot tell which keys exist), and the uniform 404 for the article. The article lookup trims the three route values and requires a publish time, so a row that is Published without one is not served. The DTOs carry no author, no id and no unpublished item.

Create `src/TechStrap.Application/Knowledge/PublicProductScope.cs`:

```csharp
using TechStrap.Application.Persistence;
using TechStrap.Domain.Products;

namespace TechStrap.Application.Knowledge;

/// <summary>Resolves a portal product key. Blank, unknown and inactive keys all give null, so the public KB never says which keys exist.</summary>
internal static class PublicProductScope
{
    public static async Task<Product?> ResolveAsync(IProductRepository products, string? productKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productKey))
        {
            return null;
        }

        var product = await products.GetByKeyAsync(productKey.Trim(), cancellationToken);
        return product is { IsActive: true } ? product : null;
    }
}
```

Create `src/TechStrap.Application/Knowledge/SearchPublicKbArticlesRequestHandler.cs`:

```csharp
using System.Net;
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;

namespace TechStrap.Application.Knowledge;

public interface ISearchPublicKbArticlesRequestHandler
{
    Task<Result<PagedResponse<PublicKbSearchResultDto>>> HandleAsync(string? productKey, string? text, string? categorySlug, int page, int pageSize, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/kb/{productKey}/search (anonymous, D-044). Published articles of the product and the shared space, best match first, 10 a page and
/// at most 25. An unknown or inactive product, and blank text, give an empty page, never a 404, so a caller cannot tell which keys exist. The snippet
/// is cut from the summary by the database as plain text; the consumer encodes it. Also used by contact-form deflection.
/// </summary>
public sealed class SearchPublicKbArticlesRequestHandler(IProductRepository products, IKbRepository knowledgeBase) : ISearchPublicKbArticlesRequestHandler
{
    public async Task<Result<PagedResponse<PublicKbSearchResultDto>>> HandleAsync(
        string? productKey, string? text, string? categorySlug, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = pageSize < 1 ? KbLimits.DefaultPublicSearchPageSize : Math.Min(pageSize, KbLimits.MaxPublicSearchPageSize);
        var normalisedPage = Paging.NormalizePage(page);
        var product = await PublicProductScope.ResolveAsync(products, productKey, cancellationToken);
        if (product is null || string.IsNullOrWhiteSpace(text))
        {
            return Result<PagedResponse<PublicKbSearchResultDto>>.Success(new PagedResponse<PublicKbSearchResultDto>([], normalisedPage, size, 0));
        }

        var found = await knowledgeBase.SearchPublicAsync(new PublicKbSearchQuery(text, product.Id, categorySlug, normalisedPage, size), cancellationToken);
        return Result<PagedResponse<PublicKbSearchResultDto>>.Success(new PagedResponse<PublicKbSearchResultDto>(
            [.. found.Items.Select(hit => new PublicKbSearchResultDto(hit.Slug, hit.Title, WebUtility.HtmlEncode(hit.Snippet), hit.CategorySlug, hit.CategoryName, hit.ProductKey))],
            found.Page, found.PageSize, found.TotalCount));
    }
}
```

Create `src/TechStrap.Application/Knowledge/GetPublishedKbArticleRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Content;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IGetPublishedKbArticleRequestHandler
{
    Task<Result<PublishedKbArticleDto>> HandleAsync(string? productKey, string? categorySlug, string? slug, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/kb/{productKey}/articles/{categorySlug}/{slug} (anonymous, D-044). Renders with the same <see cref="IKbContentRenderer"/> as the Admin preview.
/// Everything that is not a Published article of this product's scope under that category (unknown or inactive product, draft, archived, another
/// product's article, wrong category) is the same 404. The DTO has no author and no ids.
/// </summary>
public sealed class GetPublishedKbArticleRequestHandler(IProductRepository products, IKbRepository knowledgeBase, IKbContentRenderer renderer) : IGetPublishedKbArticleRequestHandler
{
    public async Task<Result<PublishedKbArticleDto>> HandleAsync(string? productKey, string? categorySlug, string? slug, CancellationToken cancellationToken)
    {
        var product = await PublicProductScope.ResolveAsync(products, productKey, cancellationToken);
        if (product is null || string.IsNullOrWhiteSpace(categorySlug) || string.IsNullOrWhiteSpace(slug))
        {
            return Result<PublishedKbArticleDto>.Failure(KbErrors.ArticleNotFound());
        }

        var view = await knowledgeBase.GetPublicArticleAsync(product.Id, categorySlug.Trim(), slug.Trim(), cancellationToken);
        if (view is null || view.Article.PublishedAt is not { } publishedAt)
        {
            return Result<PublishedKbArticleDto>.Failure(KbErrors.ArticleNotFound());
        }

        var article = view.Article;
        return Result<PublishedKbArticleDto>.Success(new PublishedKbArticleDto(
            view.ProductKey, view.CategorySlug, view.CategoryName, article.Slug, article.Title, article.Summary, renderer.Render(article.BodyMarkdown), publishedAt, article.UpdatedAt));
    }
}
```

Create `src/TechStrap.Application/Knowledge/ListPublicKbCategoriesRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IListPublicKbCategoriesRequestHandler
{
    Task<Result<IReadOnlyList<PublicKbCategoryDto>>> HandleAsync(string? productKey, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/kb/{productKey}/categories (anonymous, D-044). The product's categories and the shared ones, each with the number of Published
/// articles the product can see in it; a category with none is left out. Drafts and archived articles are never counted. An unknown or inactive product gives an empty list.
/// </summary>
public sealed class ListPublicKbCategoriesRequestHandler(IProductRepository products, IKbRepository knowledgeBase) : IListPublicKbCategoriesRequestHandler
{
    public async Task<Result<IReadOnlyList<PublicKbCategoryDto>>> HandleAsync(string? productKey, CancellationToken cancellationToken)
    {
        var product = await PublicProductScope.ResolveAsync(products, productKey, cancellationToken);
        if (product is null)
        {
            return Result<IReadOnlyList<PublicKbCategoryDto>>.Success([]);
        }

        var categories = await knowledgeBase.ListPublicCategoriesAsync(product.Id, cancellationToken);
        return Result<IReadOnlyList<PublicKbCategoryDto>>.Success(
            [.. categories.Select(item => new PublicKbCategoryDto(item.Category.Slug, item.Category.Name, item.Category.Description, item.ArticleCount))]);
    }
}
```

Create `src/TechStrap.Application/Knowledge/GetKbSitemapRequestHandler.cs`:

```csharp
using SyntaxCircus.Common;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Application.Knowledge;

public interface IGetKbSitemapRequestHandler
{
    Task<Result<IReadOnlyList<KbSitemapEntryDto>>> HandleAsync(string? productKey, CancellationToken cancellationToken);
}

/// <summary>
/// GET /api/public/kb/{productKey}/sitemap (anonymous, D-044). One entry per Published article the product can see, newest update first, for the portal
/// sitemap. A shared article has a null product key; the portal builds its address from the key it is serving. An unknown or inactive product gives an empty list.
/// </summary>
public sealed class GetKbSitemapRequestHandler(IProductRepository products, IKbRepository knowledgeBase) : IGetKbSitemapRequestHandler
{
    public async Task<Result<IReadOnlyList<KbSitemapEntryDto>>> HandleAsync(string? productKey, CancellationToken cancellationToken)
    {
        var product = await PublicProductScope.ResolveAsync(products, productKey, cancellationToken);
        if (product is null)
        {
            return Result<IReadOnlyList<KbSitemapEntryDto>>.Success([]);
        }

        var rows = await knowledgeBase.ListPublicSitemapAsync(product.Id, cancellationToken);
        return Result<IReadOnlyList<KbSitemapEntryDto>>.Success([.. rows.Select(row => new KbSitemapEntryDto(row.ProductKey, row.CategorySlug, row.Slug, row.UpdatedAt))]);
    }
}
```

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then:

```bash
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter-class "*PublicKbIntegrationTests"
dotnet test --project tests/TechStrap.Application.Tests -c Release --no-build --filter-class "*PublicKbHandlerTests"
```

Expected: PASS, 12 and 24 tests.

- [ ] **Step 6: Prove each pin with a mutation**

Applied to the finished code, the named tests run, the change reverted (same method as Task 1). Each scope condition has a mutation of its own, which is why the misfiled articles are in the data: with only well-formed data, dropping either one condition would not fail anything.

| Mutation | Failing tests |
| --- | --- |
| `PublishedIn`: `article.Status == Published` becomes `Published || Draft` | `PublicKbIntegrationTests`: `Search_returns_only_published_articles_of_the_product_and_the_shared_space`, `A_category_filter_narrows_the_search_to_that_category`, `Archiving_a_published_article_takes_it_out_of_...`, `Categories_count_only_published_articles_...`, `The_sitemap_lists_published_articles_in_scope_...` (5 fail) |
| `PublishedIn`: `article.Status == Published` becomes `!= Draft` (archived leaks) | `PublicKbIntegrationTests` (6 fail, including `The_article_page_serves_a_published_article_in_scope_and_nothing_else`) |
| `PublishedIn`: drop `(article.ProductId == productId || article.ProductId == null)` | `PublicKbIntegrationTests` (5 fail: the Orbitly article in an Acme category becomes visible to Acme) |
| `PublishedIn`: shared articles excluded (`article.ProductId == productId` only) | `PublicKbIntegrationTests` (7 fail) |
| `PublishedIn`: the category condition becomes `true` | `PublicKbIntegrationTests` (5 fail: the Acme article in an Orbitly category becomes visible) |
| `GetPublicArticleAsync`: the category slug condition removed | `PublicKbIntegrationTests.The_article_page_serves_a_published_article_in_scope_and_nothing_else` |
| `SearchPublicKbArticlesRequestHandler`: the snippet is no longer encoded | `PublicKbHandlerTests.The_snippet_is_html_encoded_before_it_leaves_the_handler`; `PublicKbIntegrationTests.A_summary_snippet_is_plain_text_and_every_html_character_in_it_is_encoded` |
| `PublicProductScope`: `product is { IsActive: true }` becomes `product` | `PublicKbHandlerTests.An_unknown_inactive_or_blank_product_gives_an_empty_page_...(key: "dormant")`; `PublicKbIntegrationTests.An_inactive_product_is_indistinguishable_from_an_unknown_one` |
| The headline is cut from `summary || body_markdown` | `PublicKbIntegrationTests.The_snippet_comes_from_the_summary_never_the_body` |
| `SearchPublicKbArticlesRequestHandler`: the page size cap `Math.Min(pageSize, 25)` becomes `pageSize` | `PublicKbHandlerTests.The_page_size_defaults_to_ten_and_is_capped_at_twenty_five` (26 and 1000) |

- [ ] **Step 7: Whole-project check**

Run: `dotnet test --solution TechStrap.CI.slnf -c Release`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/TechStrap.Application \
  src/TechStrap.Contracts \
  src/TechStrap.Infrastructure \
  tests
git diff --cached --stat
git commit -m "feat(kb): public search, article, categories and sitemap handlers" -m "Four anonymous reads for the portal: search (ts_rank over the weighted vector, a ts_headline snippet from the summary only, plain text (the consumer encodes it), 10 a page and at most 25), the article page (the product's own article, else a shared one, under its category, rendered with the same renderer as the preview), the categories with published-article counts and the sitemap. Only Published articles whose article and category are the product's or shared are ever read; an unknown or inactive product gives an empty list or the one uniform 404. Proven against a real Postgres, including misfiled rows that isolate each scope condition." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 7: Controllers, route policy, cache headers and the OpenAPI document

**Review Focus pin:** Review Focus 1, 2 and 3 over HTTP, and the role rules. Pinned here by `KbEndpointTests` (the preview returns sanitized html for a hostile source, refuses an oversize source and non-agents; the upload refuses a polyglot, an SVG and a mislabeled file and answers 413, 415 and 400 for the oversize, wrong-type and missing-file cases; an article is public only while it is Published and its cache headers are exact; a category delete is Admin only; the public routes are rate limited), `RoutePolicyCoverageTests` (every public route carries the limiter), `ResultMappingTests` (every action has a success row) and the OpenAPI tests.

**Files:**
- Modify: `docs/architecture/02-ARCHITECTURE.md`
- Modify: `docs/architecture/PHASE-08-knowledge-base.md`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`
- Create: `src/TechStrap.Api/Controllers/KbArticlesController.cs`
- Create: `src/TechStrap.Api/Controllers/KbCategoriesController.cs`
- Create: `src/TechStrap.Api/Controllers/KbImagesController.cs`
- Create: `src/TechStrap.Api/Controllers/KbPreviewController.cs`
- Create: `src/TechStrap.Api/Controllers/PublicKbController.cs`
- Create: `src/TechStrap.Api/Startup/KbRequestLimits.cs`
- Modify: `src/TechStrap.Application/Knowledge/KbErrors.cs`
- Modify: `src/TechStrap.Application/Knowledge/UploadKbImageRequestHandler.cs`
- Test (modify): `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs`
- Test (create): `tests/TechStrap.Api.Tests/Kb/KbEndpointTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/OpenApiSecurityTests.cs`
- Test (modify): `tests/TechStrap.Api.Tests/OpenApiSurfaceTests.cs`
- Test (modify): `tests/TechStrap.Application.Tests/Knowledge/UploadKbImageRequestHandlerTests.cs`

**Interfaces:**
- Consumes: Every handler from Tasks 2 to 6; `AuthorizationPolicies.Agent`, `Admin` and `Public`; `PublicRateLimitOptions.PolicyName`; `ToActionResult` (`SyntaxCircus.AspNetCore.Common`); `ReadFormBeforeBindingAttribute` and `RequestTooLargeMiddleware` (the intake multipart pattern); `ControllerActions.ExpectedSuccess`, `RoutePolicyCoverageTests`, `OpenApiSurfaceTests`, `OpenApiSecurityTests` (the tests that gate every new route); `ApiFactory`, `TestPostgres`, `TestJwt`.
- Produces:
  Agent routes (policy `Agent`; the category delete adds `Admin`):
  - `GET api/kb/articles?productId=&sharedOnly=&includeShared=&status=&categoryId=&text=&page=&pageSize=` -> 200 `PagedResponse<KbArticleListItemDto>`; `GET api/kb/articles/{id:guid}` -> 200 `KbArticleDto`; `POST api/kb/articles` -> 201 `KbArticleDto`; `PUT api/kb/articles/{id:guid}` -> 200; `POST api/kb/articles/{id:guid}/publish?version=` and `.../archive?version=` -> 200 `KbArticleDto`.
  - `POST api/kb/preview` -> 200 `KbPreviewResponse`; `POST api/kb/images` (multipart, field `file`) -> 201 `KbImageUploadResponse`, 400 `kb-image-type-not-allowed`, `kb-image-too-large` or `file-required`, 413 `request-too-large` over 6 MB, 415 `unsupported-media-type`.
  - `GET api/kb/categories?productId=&includeShared=` -> 200 `KbCategoryDto[]`; `POST` -> 201; `PUT api/kb/categories/{id:guid}` -> 200; `DELETE api/kb/categories/{id:guid}` (Admin) -> 204.
  Public routes (anonymous, `public` rate limit, `Cache-Control: no-store` first, then `public, max-age=60` on a success and 300 for the sitemap):
  - `GET api/public/kb/{productKey}/search?q=&category=&page=&pageSize=`, `.../categories`, `.../articles/{categorySlug}/{slug}`, `.../sitemap`.
  - `KbRequestLimits.JsonBodyBytes` (1 MiB) and `ImageFormBytes` (5 MB plus 1 MiB of framing); `IUploadKbImageRequestHandler.HandleAsync(IncomingKbImage? image, ...)` (null is 400 `file-required`); `KbErrors.FileRequired()`.
  - The route tables of `PHASE-08-knowledge-base.md` and `02-ARCHITECTURE.md` match the as-built routes.

- [ ] **Step 1: Write the failing tests**

`KbEndpointTests` starts the real host over a real Postgres and signs in an admin and an agent. The 413 test starts Kestrel (`factory.UseKestrel(0)`: `TestServer` has no request-size feature) and sends `Expect: 100-continue` so the server can refuse before the client streams the body. `ControllerActions.ExpectedSuccess` gets one row per new action (a missing row fails `ResultMappingTests`). The upload handler now accepts a null image, so the controller can pass a missing field through the handler's own validation and the controller test harness (which builds the form with no file) still reaches the handler.

Modify `scripts/tests/RepositoryDocs.Tests.ps1` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -94,6 +94,17 @@ Describe 'D-044 (the knowledge base)' {
         }
     }
 
+    It 'lists the as-built KB routes in PHASE-08 and the architecture, and not the old public routes' {
+        foreach ($name in 'docs/architecture/PHASE-08-knowledge-base.md', 'docs/architecture/02-ARCHITECTURE.md') {
+            $text = Get-RepoText $name
+            foreach ($route in '/api/public/kb/{productKey}/search', '/api/public/kb/{productKey}/categories', '/api/public/kb/{productKey}/articles/{categorySlug}/{slug}', '/api/public/kb/{productKey}/sitemap', '/api/kb/articles/{id}/publish') {
+                $text | Should -Match ([regex]::Escape($route)) -Because "$name lists $route"
+            }
+            $text | Should -Not -Match ([regex]::Escape('/api/public/sitemap')) -Because "$name must not list the old sitemap route"
+            $text | Should -Not -Match ([regex]::Escape('/api/public/kb/search?product=')) -Because "$name must not list the old search route"
+        }
+    }
+
     It 'tells the operator about the Api public URL and the /kb-images/ proxy route' {
         $runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md'
         $runbook | Should -Match 'TECHSTRAP_API_PUBLIC_URL'
```

Modify `tests/TechStrap.Api.Tests/Controllers/ControllerActions.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -52,6 +52,22 @@ public static class ControllerActions
         ["IntakeController.Submit"] = 201,
         ["PublicIntakeController.Submit"] = 201,
         ["PublicProductsController.Get"] = 200,
+        ["KbArticlesController.List"] = 200,
+        ["KbArticlesController.Get"] = 200,
+        ["KbArticlesController.Create"] = 201,
+        ["KbArticlesController.Update"] = 200,
+        ["KbArticlesController.Publish"] = 200,
+        ["KbArticlesController.Archive"] = 200,
+        ["KbCategoriesController.List"] = 200,
+        ["KbCategoriesController.Create"] = 201,
+        ["KbCategoriesController.Update"] = 200,
+        ["KbCategoriesController.Delete"] = 204,
+        ["KbPreviewController.Render"] = 200,
+        ["KbImagesController.Upload"] = 201,
+        ["PublicKbController.Search"] = 200,
+        ["PublicKbController.Categories"] = 200,
+        ["PublicKbController.Article"] = 200,
+        ["PublicKbController.Sitemap"] = 200,
         ["CustomerTicketsController.Get"] = 200,
         ["CustomerTicketsController.Reply"] = 201,
         ["CustomerTicketsController.GetAttachment"] = 200,
```

Create `tests/TechStrap.Api.Tests/Kb/KbEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TechStrap.Api.Startup;
using TechStrap.Api.Tests.Auth;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;

namespace TechStrap.Api.Tests.Kb;

/// <summary>The knowledge-base API over the real host and a real database: policies, the write and read flow, the preview, image upload and the public cache headers.</summary>
public sealed class KbEndpointTests(TestPostgres postgres) : IDisposable
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly byte[] Png = [.. new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }, .. new byte[40]];

    private readonly string _storage = Path.Combine(Path.GetTempPath(), "techstrap-kbapi-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, recursive: true);
        }
    }

    private sealed record Started(ApiFactory Factory, HttpClient Admin, HttpClient Agent, HttpClient Anonymous) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            Agent.Dispose();
            Anonymous.Dispose();
            await Factory.DisposeAsync();
        }
    }

    private async Task<Started> StartAsync(IReadOnlyDictionary<string, string?>? extra = null, bool kestrel = false)
    {
        var database = await ApiTestDatabase.CreateAsync(postgres);
        var settings = new Dictionary<string, string?>(database.Settings) { ["Storage:Local:RootPath"] = _storage };
        foreach (var pair in extra ?? new Dictionary<string, string?>())
        {
            settings[pair.Key] = pair.Value;
        }

        var factory = new ApiFactory(settings: settings);
        if (kestrel)
        {
            factory.UseKestrel(0); // TestServer has no IHttpMaxRequestBodySizeFeature, so RequestSizeLimit needs the real server.
        }

        var admin = factory.CreateClient().Bearer(TestJwt.Token("admin", [TestJwt.AdminGroup], email: "admin@example.com"));
        var agent = factory.CreateClient().Bearer(TestJwt.Token("agent", [TestJwt.AgentGroup], email: "agent@example.com"));
        (await admin.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        (await agent.GetAsync("/api/agents/me", Ct)).EnsureSuccessStatusCode();
        return new Started(factory, admin, agent, factory.CreateClient());
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private static async Task<ProductDto> CreateProductAsync(HttpClient admin, string key, string prefix)
    {
        using var response = await admin.PostAsJsonAsync("/api/products", new CreateProductRequest(key, key, prefix, null), Ct);
        return await ReadAsync<ProductDto>(response);
    }

    private static async Task<KbCategoryDto> CreateCategoryAsync(HttpClient agent, Guid? productId, string slug)
    {
        using var response = await agent.PostAsJsonAsync("/api/kb/categories", new CreateKbCategoryRequest(productId, slug, slug, null, 1), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ReadAsync<KbCategoryDto>(response);
    }

    private static async Task<KbArticleDto> CreateArticleAsync(HttpClient agent, Guid? productId, Guid? categoryId, string slug, string body = "Body")
    {
        using var response = await agent.PostAsJsonAsync("/api/kb/articles", new CreateKbArticleRequest(productId, categoryId, slug, "Title " + slug, "Summary of " + slug, body), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await ReadAsync<KbArticleDto>(response);
    }

    [Theory]
    [InlineData("GET", "/api/kb/articles")]
    [InlineData("GET", "/api/kb/articles/0199a000-0000-7000-8000-000000000001")]
    [InlineData("POST", "/api/kb/articles")]
    [InlineData("PUT", "/api/kb/articles/0199a000-0000-7000-8000-000000000001")]
    [InlineData("POST", "/api/kb/articles/0199a000-0000-7000-8000-000000000001/publish")]
    [InlineData("POST", "/api/kb/articles/0199a000-0000-7000-8000-000000000001/archive")]
    [InlineData("POST", "/api/kb/preview")]
    [InlineData("POST", "/api/kb/images")]
    [InlineData("GET", "/api/kb/categories")]
    [InlineData("POST", "/api/kb/categories")]
    [InlineData("PUT", "/api/kb/categories/0199a000-0000-7000-8000-000000000001")]
    [InlineData("DELETE", "/api/kb/categories/0199a000-0000-7000-8000-000000000001")]
    public async Task Every_agent_route_refuses_an_anonymous_caller_and_a_signed_in_user_who_is_not_an_agent(string method, string path)
    {
        await using var factory = new ApiFactory();
        using var anonymous = factory.CreateClient();
        using var outsider = factory.CreateClient().Bearer(TestJwt.Token("nobody", ["some-other-group"], email: "nobody@example.com"));

        using var withoutToken = await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);
        using var withoutGroup = await outsider.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Ct);

        withoutToken.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        withoutGroup.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_cannot_delete_a_category_but_an_admin_can_once_it_is_empty()
    {
        await using var started = await StartAsync();
        var category = await CreateCategoryAsync(started.Agent, null, "general");
        var article = await CreateArticleAsync(started.Agent, null, category.Id, "welcome");

        using var byAgent = await started.Agent.DeleteAsync($"/api/kb/categories/{category.Id}", Ct);
        using var blocked = await started.Admin.DeleteAsync($"/api/kb/categories/{category.Id}", Ct);
        using var moved = await started.Agent.PutAsJsonAsync($"/api/kb/articles/{article.Id}", new UpdateKbArticleRequest(null, article.Title, null, "Body", article.Version), Ct);
        using var deleted = await started.Admin.DeleteAsync($"/api/kb/categories/{category.Id}", Ct);
        using var gone = await started.Admin.DeleteAsync($"/api/kb/categories/{category.Id}", Ct);

        byAgent.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-category-in-use");
        moved.StatusCode.ShouldBe(HttpStatusCode.OK);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await gone.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-category-not-found");
    }

    [Fact]
    public async Task An_article_written_by_an_agent_reaches_the_public_api_only_while_it_is_published()
    {
        await using var started = await StartAsync();
        var product = await CreateProductAsync(started.Admin, "orbitly", "ORB");
        var category = await CreateCategoryAsync(started.Agent, product.Id, "account");
        var draft = await CreateArticleAsync(started.Agent, product.Id, category.Id, "reset-password", "# Reset\n\nOpen the app.");
        draft.Status.ShouldBe("Draft");
        var search = $"/api/public/kb/orbitly/search?q=reset";
        var article = "/api/public/kb/orbitly/articles/account/reset-password";

        (await ReadAsync<PagedResponse<PublicKbSearchResultDto>>(await started.Anonymous.GetAsync(search, Ct))).Items.ShouldBeEmpty();
        using var hiddenDraft = await started.Anonymous.GetAsync(article, Ct);
        hiddenDraft.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        hiddenDraft.Headers.GetValues("Cache-Control").Single().ShouldBe("no-store");
        (await hiddenDraft.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-article-not-found");

        using var publishResponse = await started.Agent.PostAsync($"/api/kb/articles/{draft.Id}/publish?version={draft.Version}", null, Ct);
        var published = await ReadAsync<KbArticleDto>(publishResponse);
        published.ShouldSatisfyAllConditions(dto => dto.Status.ShouldBe("Published"), dto => dto.PublishedAt.ShouldNotBeNull(), dto => dto.Version.ShouldNotBe(draft.Version));

        using var searchResponse = await started.Anonymous.GetAsync(search, Ct);
        var hits = await ReadAsync<PagedResponse<PublicKbSearchResultDto>>(searchResponse);
        hits.Items.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            hit => hit.Slug.ShouldBe("reset-password"),
            hit => hit.CategorySlug.ShouldBe("account"),
            hit => hit.ProductKey.ShouldBe("orbitly"),
            hit => hit.Snippet.ShouldBe("Summary of reset-password"));
        searchResponse.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=60");
        using var articleResponse = await started.Anonymous.GetAsync(article, Ct);
        var page = await ReadAsync<PublishedKbArticleDto>(articleResponse);
        page.Html.ShouldBe("<h1>Reset</h1>\n<p>Open the app.</p>\n");
        page.Title.ShouldBe("Title reset-password");
        articleResponse.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=60");
        (await articleResponse.Content.ReadAsStringAsync(Ct)).ShouldNotContain("authorAgentId");
        using var categories = await started.Anonymous.GetAsync("/api/public/kb/orbitly/categories", Ct);
        (await ReadAsync<List<PublicKbCategoryDto>>(categories)).ShouldBe([new PublicKbCategoryDto("account", "account", null, 1)]);
        using var sitemap = await started.Anonymous.GetAsync("/api/public/kb/orbitly/sitemap", Ct);
        (await ReadAsync<List<KbSitemapEntryDto>>(sitemap)).ShouldHaveSingleItem().Slug.ShouldBe("reset-password");
        sitemap.Headers.GetValues("Cache-Control").Single().ShouldBe("public, max-age=300");

        using var archive = await started.Agent.PostAsync($"/api/kb/articles/{published.Id}/archive", null, Ct);
        (await ReadAsync<KbArticleDto>(archive)).Status.ShouldBe("Archived");
        (await ReadAsync<PagedResponse<PublicKbSearchResultDto>>(await started.Anonymous.GetAsync(search, Ct))).Items.ShouldBeEmpty();
        (await started.Anonymous.GetAsync(article, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync<List<KbSitemapEntryDto>>(await started.Anonymous.GetAsync("/api/public/kb/orbitly/sitemap", Ct))).ShouldBeEmpty();
        (await ReadAsync<List<PublicKbCategoryDto>>(await started.Anonymous.GetAsync("/api/public/kb/orbitly/categories", Ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_product_gets_empty_lists_and_one_uniform_not_found_for_the_article()
    {
        await using var started = await StartAsync();

        (await ReadAsync<PagedResponse<PublicKbSearchResultDto>>(await started.Anonymous.GetAsync("/api/public/kb/nobody/search?q=anything", Ct))).Items.ShouldBeEmpty();
        (await ReadAsync<List<PublicKbCategoryDto>>(await started.Anonymous.GetAsync("/api/public/kb/nobody/categories", Ct))).ShouldBeEmpty();
        (await ReadAsync<List<KbSitemapEntryDto>>(await started.Anonymous.GetAsync("/api/public/kb/nobody/sitemap", Ct))).ShouldBeEmpty();
        using var article = await started.Anonymous.GetAsync("/api/public/kb/nobody/articles/a/b", Ct);
        article.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_slug_is_refused_across_scopes_and_a_stale_version_is_a_conflict()
    {
        await using var started = await StartAsync();
        var product = await CreateProductAsync(started.Admin, "orbitly", "ORB");
        var shared = await CreateCategoryAsync(started.Agent, null, "general");
        var welcome = await CreateArticleAsync(started.Agent, null, shared.Id, "welcome");

        using var reuse = await started.Agent.PostAsJsonAsync("/api/kb/articles", new CreateKbArticleRequest(product.Id, shared.Id, "welcome", "Welcome again", null, "Body"), Ct);
        using var firstEdit = await started.Agent.PutAsJsonAsync($"/api/kb/articles/{welcome.Id}", new UpdateKbArticleRequest(shared.Id, "First", null, "Body", welcome.Version), Ct);
        using var secondEdit = await started.Agent.PutAsJsonAsync($"/api/kb/articles/{welcome.Id}", new UpdateKbArticleRequest(shared.Id, "Second", null, "Body", welcome.Version), Ct);
        using var staleCategory = await started.Agent.PutAsJsonAsync($"/api/kb/categories/{shared.Id}", new UpdateKbCategoryRequest("Renamed", null, 1, shared.Version + 100), Ct);

        reuse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await reuse.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-slug-taken");
        firstEdit.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondEdit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await secondEdit.Content.ReadAsStringAsync(Ct)).ShouldContain("concurrency-conflict");
        staleCategory.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await started.Agent.GetFromJsonAsync<KbArticleDto>($"/api/kb/articles/{welcome.Id}", Ct))!.Title.ShouldBe("First");
    }

    [Fact]
    public async Task The_list_filters_by_status_scope_and_text_and_publish_without_a_category_is_a_field_error()
    {
        await using var started = await StartAsync();
        var product = await CreateProductAsync(started.Admin, "orbitly", "ORB");
        var category = await CreateCategoryAsync(started.Agent, null, "general");
        var shared = await CreateArticleAsync(started.Agent, null, category.Id, "shared-one", "printer text");
        var own = await CreateArticleAsync(started.Agent, product.Id, null, "own-one", "scanner text");

        var all = await started.Agent.GetFromJsonAsync<PagedResponse<KbArticleListItemDto>>("/api/kb/articles", Ct);
        var sharedOnly = await started.Agent.GetFromJsonAsync<PagedResponse<KbArticleListItemDto>>("/api/kb/articles?sharedOnly=true", Ct);
        var ownOnly = await started.Agent.GetFromJsonAsync<PagedResponse<KbArticleListItemDto>>($"/api/kb/articles?productId={product.Id}&includeShared=false", Ct);
        var searched = await started.Agent.GetFromJsonAsync<PagedResponse<KbArticleListItemDto>>("/api/kb/articles?text=scanner", Ct);
        using var badStatus = await started.Agent.GetAsync("/api/kb/articles?status=Live", Ct);
        using var incomplete = await started.Agent.PostAsync($"/api/kb/articles/{own.Id}/publish", null, Ct);

        all!.Items.Select(item => item.Slug).Order().ShouldBe(["own-one", "shared-one"]);
        sharedOnly!.Items.ShouldHaveSingleItem().Id.ShouldBe(shared.Id);
        ownOnly!.Items.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
        searched!.Items.ShouldHaveSingleItem().Id.ShouldBe(own.Id);
        badStatus.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        incomplete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await incomplete.Content.ReadAsStringAsync(Ct);
        body.ShouldContain("kb-publish-incomplete");
        body.ShouldContain("category");
    }

    [Fact]
    public async Task The_preview_returns_sanitised_html_and_refuses_hostile_markup_an_oversize_source_and_non_agents()
    {
        await using var started = await StartAsync();
        const string Hostile = "<script>alert(1)</script> [x](javascript:alert(1)) ![p](javascript:alert(2)) ![q](//evil.example/a.png) <img src=x onerror=alert(3)>\n\n| a |\n|---|\n| <b onclick=x>1</b> |\n\n![ok](https://cdn.example.com/a.png)";

        using var response = await started.Agent.PostAsJsonAsync("/api/kb/preview", new KbPreviewRequest(Hostile), Ct);
        var html = (await ReadAsync<KbPreviewResponse>(response)).Html;
        using var tooLong = await started.Agent.PostAsJsonAsync("/api/kb/preview", new KbPreviewRequest(new string('a', KbLimits.MaxPreviewChars + 1)), Ct);
        using var anonymous = await started.Anonymous.PostAsJsonAsync("/api/kb/preview", new KbPreviewRequest("# hi"), Ct);

        html.ShouldNotContain("<script");
        html.ShouldNotContain("href=\"javascript");
        html.ShouldNotContain("src=\"javascript");
        html.ShouldNotContain("//evil.example");
        html.ShouldNotContain("onerror=\"");
        html.ShouldNotContain("onclick=\"");
        html.ShouldNotContain("<img src=x");
        html.ShouldContain("<table>");
        html.ShouldContain("<img src=\"https://cdn.example.com/a.png\" alt=\"ok\"");
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLong.Content.ReadAsStringAsync(Ct)).ShouldContain("body-too-long");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_preview_of_an_article_equals_its_public_page()
    {
        await using var started = await StartAsync();
        var product = await CreateProductAsync(started.Admin, "orbitly", "ORB");
        var category = await CreateCategoryAsync(started.Agent, product.Id, "account");
        const string Markdown = "# Title\n\n- one\n- two\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n![pic](https://cdn.example.com/a.png)";
        var article = await CreateArticleAsync(started.Agent, product.Id, category.Id, "same", Markdown);
        (await started.Agent.PostAsync($"/api/kb/articles/{article.Id}/publish", null, Ct)).EnsureSuccessStatusCode();

        var preview = await ReadAsync<KbPreviewResponse>(await started.Agent.PostAsJsonAsync("/api/kb/preview", new KbPreviewRequest(Markdown), Ct));
        var page = await ReadAsync<PublishedKbArticleDto>(await started.Anonymous.GetAsync("/api/public/kb/orbitly/articles/account/same", Ct));

        page.Html.ShouldBe(preview.Html);
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName = "photo.png", string contentType = "image/png", string field = "file")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, field, fileName } };
    }

    [Fact]
    public async Task An_uploaded_image_returns_its_key_and_public_url_and_is_then_served_anonymously()
    {
        await using var started = await StartAsync();

        using var response = await started.Agent.PostAsync("/api/kb/images", Form(Png), Ct);
        var uploaded = await ReadAsync<KbImageUploadResponse>(response);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        uploaded.Key.ShouldStartWith("kb-images/");
        uploaded.Key.ShouldEndWith(".png");
        uploaded.Key.ShouldNotContain("photo");
        uploaded.Url.ShouldBe("https://api.test/" + uploaded.Key);
        using var served = await started.Anonymous.GetAsync("/" + uploaded.Key, Ct);
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await served.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Png);
        served.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        served.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("sandbox");
    }

    public static TheoryData<string, byte[], string, string> RefusedUploads() => new()
    {
        { "svg named png", "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>"u8.ToArray(), "image/png", "photo.png" },
        { "html named png", "<html><script>alert(1)</script></html>"u8.ToArray(), "image/png", "photo.png" },
        { "png named svg with an svg type", Png, "image/svg+xml", "photo.svg" },
        { "gif that is a page", [.. "GIF89a"u8.ToArray(), .. new byte[8], .. "<script>alert(1)</script>"u8.ToArray()], "image/gif", "photo.gif" },
        { "text", "hello"u8.ToArray(), "text/plain", "notes.txt" },
    };

    [Theory]
    [MemberData(nameof(RefusedUploads))]
    public async Task A_polyglot_svg_or_mislabelled_upload_is_refused_and_nothing_is_stored(string label, byte[] bytes, string contentType, string fileName)
    {
        await using var started = await StartAsync();

        using var response = await started.Agent.PostAsync("/api/kb/images", Form(bytes, fileName, contentType), Ct);

        // The png named .svg with an svg content type is a real png: the bytes decide, so it is stored under a random png name (the label says so).
        if (label.StartsWith("png named svg", StringComparison.Ordinal))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Created, label);
            (await ReadAsync<KbImageUploadResponse>(response)).Key.ShouldEndWith(".png");
            return;
        }

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, label);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-image-type-not-allowed");
        Directory.Exists(Path.Combine(_storage, "kb-images")).ShouldBeFalse(label);
    }

    [Fact]
    public async Task A_missing_file_a_wrong_field_a_wrong_content_type_and_oversize_bodies_are_refused_with_their_own_statuses()
    {
        await using var started = await StartAsync();
        var overLimit = new byte[KbLimits.MaxImageBytes + 1024];
        Png.CopyTo(overLimit, 0);

        using var noFile = await started.Agent.PostAsync("/api/kb/images", new MultipartFormDataContent { { new StringContent("x"), "other" } }, Ct);
        using var wrongField = await started.Agent.PostAsync("/api/kb/images", Form(Png, field: "image"), Ct);
        using var json = await started.Agent.PostAsJsonAsync("/api/kb/images", new { file = "x" }, Ct);
        using var tooLarge = await started.Agent.PostAsync("/api/kb/images", Form(overLimit), Ct);
        using var anonymous = await started.Anonymous.PostAsync("/api/kb/images", Form(Png), Ct);

        noFile.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noFile.Content.ReadAsStringAsync(Ct)).ShouldContain("file-required");
        wrongField.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        json.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        tooLarge.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooLarge.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-image-too-large");
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        Directory.Exists(Path.Combine(_storage, "kb-images")).ShouldBeFalse();
    }

    [Fact]
    public async Task An_image_body_far_over_the_limit_is_stopped_by_the_server_as_413_problem_details_and_nothing_is_stored()
    {
        await using var started = await StartAsync(kestrel: true);
        var farOver = new byte[KbRequestLimits.ImageFormBytes + 1];
        Png.CopyTo(farOver, 0);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/kb/images") { Content = Form(farOver) };

        // Expect: 100-continue lets Kestrel reject on Content-Length before the client streams the whole body into a closing socket.
        request.Headers.ExpectContinue = true;
        using var response = await started.Agent.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("request-too-large");
        Directory.Exists(Path.Combine(_storage, "kb-images")).ShouldBeFalse();
    }

    [Fact]
    public async Task The_public_kb_routes_are_rate_limited_per_ip()
    {
        await using var started = await StartAsync(new Dictionary<string, string?> { ["RateLimiting:Public:PermitLimit"] = "3" });

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
        {
            using var response = await started.Anonymous.GetAsync("/api/public/kb/nobody/sitemap", Ct);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(3).ShouldAllBe(status => status == HttpStatusCode.OK);
        statuses.Skip(3).ShouldAllBe(status => status == HttpStatusCode.TooManyRequests);
    }
}
```

Modify `tests/TechStrap.Api.Tests/OpenApiSecurityTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -62,6 +62,11 @@ public sealed partial class OpenApiSecurityTests
     [InlineData("/api/products", "post", "Bearer")]
     [InlineData("/api/products/{id}/api-keys", "post", "Bearer")]
     [InlineData("/api/attachments/{id}", "get", "Bearer")]
+    [InlineData("/api/kb/articles", "get", "Bearer")]
+    [InlineData("/api/kb/articles/{id}/publish", "post", "Bearer")]
+    [InlineData("/api/kb/preview", "post", "Bearer")]
+    [InlineData("/api/kb/images", "post", "Bearer")]
+    [InlineData("/api/kb/categories/{id}", "delete", "Bearer")]
     [InlineData("/api/intake/tickets", "post", "ApiKey")]
     [InlineData("/api/customer/ticket", "get", "TicketToken")]
     [InlineData("/api/customer/ticket/replies", "post", "TicketToken")]
@@ -78,6 +83,10 @@ public sealed partial class OpenApiSecurityTests
     [InlineData("/api/customer/access-link", "post")]
     [InlineData("/api/public/products/{productKey}", "get")]
     [InlineData("/api/public/products/{productKey}/tickets", "post")]
+    [InlineData("/api/public/kb/{productKey}/search", "get")]
+    [InlineData("/api/public/kb/{productKey}/categories", "get")]
+    [InlineData("/api/public/kb/{productKey}/articles/{categorySlug}/{slug}", "get")]
+    [InlineData("/api/public/kb/{productKey}/sitemap", "get")]
     public async Task A_public_operation_names_no_scheme(string path, string method)
     {
         await using var factory = new ApiFactory();
```

Modify `tests/TechStrap.Api.Tests/OpenApiSurfaceTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -36,6 +36,7 @@ public sealed partial class OpenApiSurfaceTests
     [InlineData("/api/customer/ticket/replies")]
     [InlineData("/api/public/products/{productKey}/tickets")]
     [InlineData("/api/tickets/{id}/replies")]
+    [InlineData("/api/kb/images")]
     public async Task The_multipart_routes_document_a_multipart_request_body(string path)
     {
         await using var factory = new ApiFactory();
```

Modify `tests/TechStrap.Application.Tests/Knowledge/UploadKbImageRequestHandlerTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -36,6 +36,15 @@ public sealed class UploadKbImageRequestHandlerTests
         _urls.DidNotReceiveWithAnyArgs().UrlFor(default!);
     }
 
+    [Fact]
+    public async Task A_request_with_no_file_is_a_validation_error_on_file_and_reaches_nothing()
+    {
+        var result = await new UploadKbImageRequestHandler(_store, _urls).HandleAsync(null, Ct);
+
+        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(error => error.Code.ShouldBe("file-required"), error => error.Target.ShouldBe("file"));
+        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default!, Ct);
+    }
+
     [Theory]
     [InlineData("0123456789abcdef0123456789abcdef.png", true)]
     [InlineData("0123456789abcdef0123456789abcdef.jpg", true)]
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: the build FAILS: `CS0103` (`The name 'KbRequestLimits' does not exist in the current context`) and `CS8625` (`Cannot convert null literal to non-nullable reference type`, the null-image test).

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1`
Expected: FAIL, 1 failed (`lists the as-built KB routes in PHASE-08 and the architecture, and not the old public routes`).

- [ ] **Step 3: Add the limits, the null-file rule and the five controllers**

Every action takes its handler through one `[FromServices]` parameter, passes the `CancellationToken` and maps the `Result` with `ToActionResult`, like the existing controllers; every route carries an explicit policy. The public controller sets `no-store` first and replaces it with the short public cache only on a success, so a 404 or an error stays uncacheable. The image controller follows the intake multipart pattern (`ReadFormBeforeBinding`, a size limit and a form limit above it so Kestrel trips first as a 413 problem+json) and gives the handler a null image when the `file` field is missing. An unmatched path on the Api answers 401 or 404 through the fallback authorization; that is existing behavior.

Create `src/TechStrap.Api/Startup/KbRequestLimits.cs`:

```csharp
using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Startup;

/// <summary>Request size limits for the knowledge-base endpoints (D-044). The handlers enforce the exact limits; these stop an oversize body early, as a 413.</summary>
public static class KbRequestLimits
{
    /// <summary>1 MiB: a 200,000-character article is at most 800 KB as UTF-8, plus the JSON around it.</summary>
    public const int JsonBodyBytes = 1024 * 1024;

    /// <summary>The image limit plus 1 MiB for the multipart framing, so an image over 5 MB (but not absurdly over) still reaches the handler's <c>kb-image-too-large</c> answer.</summary>
    public const long ImageFormBytes = KbLimits.MaxImageBytes + (1024 * 1024);
}
```

Modify `src/TechStrap.Application/Knowledge/KbErrors.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -13,6 +13,9 @@ internal static class KbErrors
     public static ResultError PreviewTooLong() =>
         new("body-too-long", $"The text is longer than {KbLimits.MaxPreviewChars:N0} characters and cannot be previewed.", ResultErrorKind.Validation, "body");
 
+    public static ResultError FileRequired() =>
+        new("file-required", "Choose an image to upload.", ResultErrorKind.Validation, "file");
+
     public static ResultError ArticleNotFound() => new("kb-article-not-found", "That article does not exist.", ResultErrorKind.NotFound);
 
     public static ResultError CategoryNotFound() => new("kb-category-not-found", "That category does not exist.", ResultErrorKind.NotFound);
```

Modify `src/TechStrap.Application/Knowledge/UploadKbImageRequestHandler.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -5,17 +5,22 @@ namespace TechStrap.Application.Knowledge;
 
 public interface IUploadKbImageRequestHandler
 {
-    Task<Result<KbImageUploadResponse>> HandleAsync(IncomingKbImage image, CancellationToken cancellationToken);
+    Task<Result<KbImageUploadResponse>> HandleAsync(IncomingKbImage? image, CancellationToken cancellationToken);
 }
 
 /// <summary>
-/// POST /api/kb/images (Agent). Hands the bytes to <see cref="IKbImageStore"/>, which checks the size and the type and stores them under a random key,
+/// POST /api/kb/images (Agent). A request with no file is a 400 <c>file-required</c>. Otherwise hands the bytes to <see cref="IKbImageStore"/>, which checks the size and the type and stores them under a random key,
 /// then returns that key and the absolute public URL to put in the Markdown. No database row is written and unused images are not cleaned up (D-044).
 /// </summary>
 public sealed class UploadKbImageRequestHandler(IKbImageStore store, IKbImageUrls urls) : IUploadKbImageRequestHandler
 {
-    public async Task<Result<KbImageUploadResponse>> HandleAsync(IncomingKbImage image, CancellationToken cancellationToken)
+    public async Task<Result<KbImageUploadResponse>> HandleAsync(IncomingKbImage? image, CancellationToken cancellationToken)
     {
+        if (image is null)
+        {
+            return Result<KbImageUploadResponse>.Failure(KbErrors.FileRequired());
+        }
+
         var stored = await store.SaveAsync(image, cancellationToken);
         return stored.IsFailure
             ? Result<KbImageUploadResponse>.Failure(stored.Errors[0])
```

Create `src/TechStrap.Api/Controllers/KbArticlesController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.Knowledge;
using TechStrap.Application.Persistence;
using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Controllers;

/// <summary>The knowledge-base articles (PHASE-08, D-044). Every agent may list, read, write, publish and archive; deleting is not offered.</summary>
[ApiController]
[Route("api/kb/articles")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class KbArticlesController : ControllerBase
{
    /// <summary>Articles of any status, newest update first; <c>text</c> searches title, summary and body, best match first. Shared articles are included with a product unless <c>includeShared</c> is false.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? productId,
        [FromQuery] string? status,
        [FromQuery] Guid? categoryId,
        [FromQuery] string? text,
        [FromServices] IListKbArticlesRequestHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] bool sharedOnly = false,
        [FromQuery] bool includeShared = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize) =>
        (await handler.HandleAsync(new ListKbArticlesRequest(productId, sharedOnly, includeShared, status, categoryId, text, page, pageSize), cancellationToken))
            .ToActionResult(this, Ok);

    /// <summary>One article with its Markdown source and the version to send back on an update.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, [FromServices] IGetKbArticleRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>A new Draft. The product (null for shared) and the slug are permanent; the slug must be free in every scope.</summary>
    [HttpPost]
    [RequestSizeLimit(KbRequestLimits.JsonBodyBytes)]
    public async Task<IActionResult> Create([FromBody] CreateKbArticleRequest request, [FromServices] ICreateKbArticleRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToActionResult(this, article => StatusCode(StatusCodes.Status201Created, article));

    /// <summary>Replaces the category, title, summary and body. The version is required; a stale one is 409. An Archived article returns to Draft.</summary>
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(KbRequestLimits.JsonBodyBytes)]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateKbArticleRequest request, [FromServices] IUpdateKbArticleRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Publishes. Needs a title, a slug, a body and a category (400 <c>kb-publish-incomplete</c>). The version is optional; a stale one is 409.</summary>
    [HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(
        Guid id, [FromServices] IPublishKbArticleRequestHandler handler, CancellationToken cancellationToken, [FromQuery] uint? version = null) =>
        (await handler.HandleAsync(id, version, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Archives: out of public search, the article page, the sitemap and the category counts. The version is optional; a stale one is 409.</summary>
    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(
        Guid id, [FromServices] IArchiveKbArticleRequestHandler handler, CancellationToken cancellationToken, [FromQuery] uint? version = null) =>
        (await handler.HandleAsync(id, version, cancellationToken)).ToActionResult(this, Ok);
}
```

Create `src/TechStrap.Api/Controllers/KbCategoriesController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Controllers;

/// <summary>The knowledge-base categories (PHASE-08, D-044). Agents list, create and update; only an Admin deletes (D-022).</summary>
[ApiController]
[Route("api/kb/categories")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class KbCategoriesController : ControllerBase
{
    /// <summary>Sort order, then name. Without <c>productId</c> every category is listed; with one, its own plus the shared ones unless <c>includeShared</c> is false.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? productId, [FromServices] IListKbCategoriesRequestHandler handler, CancellationToken cancellationToken, [FromQuery] bool includeShared = true) =>
        (await handler.HandleAsync(productId, includeShared, cancellationToken)).ToActionResult(this, Ok);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateKbCategoryRequest request, [FromServices] ICreateKbCategoryRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToActionResult(this, category => StatusCode(StatusCodes.Status201Created, category));

    /// <summary>Changes the name, description and sort order. The version is required; a stale one is 409.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateKbCategoryRequest request, [FromServices] IUpdateKbCategoryRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToActionResult(this, Ok);

    /// <summary>Deletes a category that holds no article of any status (409 <c>kb-category-in-use</c> otherwise). Admin only.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.Admin)]
    public async Task<IActionResult> Delete(Guid id, [FromServices] IDeleteKbCategoryRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToActionResult(this, NoContent);
}
```

Create `src/TechStrap.Api/Controllers/KbPreviewController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Controllers;

/// <summary>The Admin editor preview (D-021, D-044): Markdown in, sanitized HTML out, nothing stored. Agents only, and the source is limited so it cannot serve as a free renderer.</summary>
[ApiController]
[Route("api/kb/preview")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class KbPreviewController : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(KbRequestLimits.JsonBodyBytes)]
    public async Task<IActionResult> Render([FromBody] KbPreviewRequest request, [FromServices] IRenderKbPreviewRequestHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToActionResult(this, Ok);
}
```

Create `src/TechStrap.Api/Controllers/KbImagesController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Security;
using TechStrap.Api.Startup;
using TechStrap.Application.Knowledge;

namespace TechStrap.Api.Controllers;

/// <summary>Multipart form posted to upload one KB image: the file in the field named <c>file</c>.</summary>
public sealed class KbImageForm
{
    public IFormFile? File { get; set; }
}

/// <summary>Uploads of KB images (PHASE-08, D-044). The images are read back, publicly, from <c>GET /kb-images/{name}</c>.</summary>
[ApiController]
[Route("api/kb/images")]
[Authorize(Policy = AuthorizationPolicies.Agent)]
public sealed class KbImagesController : ControllerBase
{
    /// <summary>One png, jpeg, gif or webp up to 5 MB, by its leading bytes. A larger body than the request limit is a 413 before the handler runs.</summary>
    [HttpPost]
    [ReadFormBeforeBinding]
    [RequestSizeLimit(KbRequestLimits.ImageFormBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = KbRequestLimits.ImageFormBytes * 2)] // Kestrel's limit must trip first, as a 413
    public async Task<IActionResult> Upload([FromForm] KbImageForm form, [FromServices] IUploadKbImageRequestHandler handler, CancellationToken cancellationToken)
    {
        if (form.File is not { } file)
        {
            return (await handler.HandleAsync(null, cancellationToken)).ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));
        }

        await using var stream = file.OpenReadStream();
        return (await handler.HandleAsync(new IncomingKbImage(file.Length, stream), cancellationToken))
            .ToActionResult(this, response => StatusCode(StatusCodes.Status201Created, response));
    }
}
```

Create `src/TechStrap.Api/Controllers/PublicKbController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyntaxCircus.AspNetCore.Common;
using TechStrap.Api.Options;
using TechStrap.Api.Security;
using TechStrap.Application.Knowledge;
using TechStrap.Contracts.Kb;

namespace TechStrap.Api.Controllers;

/// <summary>
/// The knowledge base for the portal (PHASE-08, PHASE-09, D-044): anonymous, rate limited per IP. A hit is cacheable for 60 seconds (the sitemap for 300);
/// a 404 is never cached. An unknown or inactive product key gives an empty list, never a 404, except on the article page, where everything unavailable is one 404.
/// </summary>
[ApiController]
[Route("api/public/kb/{productKey}")]
[Authorize(Policy = AuthorizationPolicies.Public)]
[EnableRateLimiting(PublicRateLimitOptions.PolicyName)]
public sealed class PublicKbController : ControllerBase
{
    public const int HitMaxAgeSeconds = 60;
    public const int SitemapMaxAgeSeconds = 300;

    /// <summary>Published articles of the product and the shared space, best match first, 10 a page (at most 25). <c>category</c> is an optional category slug.</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        string productKey,
        [FromQuery] string? q,
        [FromQuery] string? category,
        [FromServices] ISearchPublicKbArticlesRequestHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = KbLimits.DefaultPublicSearchPageSize) =>
        CachedFor(HitMaxAgeSeconds, (await handler.HandleAsync(productKey, q, category, page, pageSize, cancellationToken)));

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(string productKey, [FromServices] IListPublicKbCategoriesRequestHandler handler, CancellationToken cancellationToken) =>
        CachedFor(HitMaxAgeSeconds, await handler.HandleAsync(productKey, cancellationToken));

    /// <summary>One Published article as sanitized HTML. A draft, an archived article, another product's article, a wrong category and an unknown key are the same 404.</summary>
    [HttpGet("articles/{categorySlug}/{slug}")]
    public async Task<IActionResult> Article(
        string productKey, string categorySlug, string slug, [FromServices] IGetPublishedKbArticleRequestHandler handler, CancellationToken cancellationToken) =>
        CachedFor(HitMaxAgeSeconds, await handler.HandleAsync(productKey, categorySlug, slug, cancellationToken));

    [HttpGet("sitemap")]
    public async Task<IActionResult> Sitemap(string productKey, [FromServices] IGetKbSitemapRequestHandler handler, CancellationToken cancellationToken) =>
        CachedFor(SitemapMaxAgeSeconds, await handler.HandleAsync(productKey, cancellationToken));

    // no-store first, so an error stays uncacheable; a success then asks for the short public cache.
    private IActionResult CachedFor<T>(int seconds, SyntaxCircus.Common.Result<T> result)
    {
        Response.Headers.CacheControl = "no-store";
        return result.ToActionResult(this, value =>
        {
            Response.Headers.CacheControl = $"public, max-age={seconds}";
            return Ok(value);
        });
    }
}
```

- [ ] **Step 4: Update the route tables in the phase spec and the architecture document**

The two documents still list the pre-implementation routes (`GET /api/public/kb/search?product=` and `GET /api/public/sitemap`, a 204 for publish, `KbImageDto`). The script below rewrites exactly those table rows to the as-built routes and adds the `IKbContentRenderer` component row. Save it outside the repository (for example `docs_task7.py` in your scratch folder) and run it from the repository root: `python docs_task7.py`. It asserts each row exists once, so it fails loudly if the documents changed. Expected output: `ok`.

```python
"""Rewrites the KB route rows of PHASE-08-knowledge-base.md and 02-ARCHITECTURE.md to the as-built routes (D-044). Run from the repo root."""
import re
import sys

sys.path.insert(0, 'C:/tmp/claude/D--dev-SyntaxCircus-techstrap/42551360-63f8-4931-99a2-3f692676cb7a/scratchpad')


def load(path):
    text = open(path, encoding='utf-8', newline='').read()
    crlf = '\r\n' in text
    return text.replace('\r\n', '\n'), crlf


def save(path, text, crlf):
    open(path, 'w', encoding='utf-8', newline='').write(text.replace('\n', '\r\n') if crlf else text)


def replace_row(text, prefix, row):
    pattern = re.compile(r'^' + re.escape(prefix) + r'.*$', re.MULTILINE)
    assert len(pattern.findall(text)) == 1, prefix
    return pattern.sub(lambda _: row, text)


# ---------------- PHASE-08 ----------------
path = 'docs/architecture/PHASE-08-knowledge-base.md'
text, crlf = load(path)
rows = {
    '| `GET /api/kb/articles` ': '| `GET /api/kb/articles` (filters: `productId`, `sharedOnly`, `includeShared`, `status`, `categoryId`, `text`, paging) | `ListKbArticlesRequestHandler` | `IKbRepository` | EF `KbRepository` (FTS for text) | 200 `PagedResponse<KbArticleListItemDto>`; 400 `status-invalid` | Named handler |',
    '| `GET /api/kb/articles/{id}` ': '| `GET /api/kb/articles/{id}` | `GetKbArticleRequestHandler` | `IKbRepository` | `KbRepository` | 200 `KbArticleDto` incl. Markdown and `Version`; 404 `kb-article-not-found` | Named handler |',
    '| `POST /api/kb/articles` ': '| `POST /api/kb/articles` | `CreateKbArticleRequestHandler` | `IKbRepository`, `IProductRepository`, `IUnitOfWork`, `ICurrentAgentClaims`, `TimeProvider` | `KbRepository`, EF unit of work | 201 `KbArticleDto` (a Draft); 400 validation, `kb-category-not-found`, `kb-category-scope-mismatch`, `product-not-found`; 409 `kb-slug-taken` (any scope) | Named handler |',
    '| `PUT /api/kb/articles/{id}` ': '| `PUT /api/kb/articles/{id}` | `UpdateKbArticleRequestHandler` | `IKbRepository`, `IUnitOfWork`, `TimeProvider` | `KbRepository` | 200 `KbArticleDto` (an Archived article returns to Draft); 404; 400; 409 `concurrency-conflict` | Named handler |',
    '| `POST /api/kb/articles/{id}/publish` ': '| `POST /api/kb/articles/{id}/publish?version=` | `PublishKbArticleRequestHandler` | `IKbRepository`, `IUnitOfWork`, `TimeProvider` | `KbRepository` | 200 `KbArticleDto`; 400 `kb-publish-incomplete` (target title, slug, body or category); 404; 409 | Named handler |',
    '| `POST /api/kb/articles/{id}/archive` ': '| `POST /api/kb/articles/{id}/archive?version=` | `ArchiveKbArticleRequestHandler` | `IKbRepository`, `IUnitOfWork`, `TimeProvider` | `KbRepository` | 200 `KbArticleDto`; 404; 409 | Named handler |',
    '| `POST /api/kb/preview` ': '| `POST /api/kb/preview` | `RenderKbPreviewRequestHandler` | `IKbContentRenderer` | KB Markdig profile and KB sanitiser | 200 `KbPreviewResponse` (sanitized HTML); 400 `body-too-long` | Named handler (D-021, D-044); Agent policy only |',
    '| `POST /api/kb/images` ': '| `POST /api/kb/images` (multipart, field `file`) | `UploadKbImageRequestHandler` | `IKbImageStore` (over `SyntaxCircus.Storage`), `IKbImageUrls` | `KbImageStore : IKbImageStore` using `IStorageProvider` | 201 `KbImageUploadResponse`; 400 `kb-image-type-not-allowed`, `kb-image-too-large`, `file-required`; 413 from Kestrel far over the limit | Named handler. `IKbImageStore` is a KB-specific abstraction distinct from `IAttachmentStore` because of the public-read prefix |',
    '| `GET /api/kb/categories` ': '| `GET /api/kb/categories` | `ListKbCategoriesRequestHandler` | `IKbRepository` | `KbRepository` | 200 `KbCategoryDto[]` | Named handler |',
    '| `POST /api/kb/categories` ': '| `POST /api/kb/categories` | `CreateKbCategoryRequestHandler` | `IKbRepository`, `IProductRepository`, `IUnitOfWork` | `KbRepository` | 201 `KbCategoryDto`; 400 `kb-category-reserved-slug`; 409 `kb-category-slug-taken` | Named handler |',
    '| `PUT /api/kb/categories/{id}` ': '| `PUT /api/kb/categories/{id}` | `UpdateKbCategoryRequestHandler` | `IKbRepository`, `IUnitOfWork` | `KbRepository` | 200 `KbCategoryDto`; 404 `kb-category-not-found`; 409 `concurrency-conflict` | Named handler |',
    '| `DELETE /api/kb/categories/{id}` (Admin) ': '| `DELETE /api/kb/categories/{id}` (Admin) | `DeleteKbCategoryRequestHandler` | `IKbRepository`, `IUnitOfWork` | `KbRepository` | 204; 404; 409 `kb-category-in-use` while it holds articles | Named handler |',
    '| `GET /api/public/kb/search?product={key}': '| `GET /api/public/kb/{productKey}/search?q=&category=&page=&pageSize=` (anonymous) | `SearchPublicKbArticlesRequestHandler` | `IKbRepository`, `IProductRepository` | `KbRepository` (FTS, `ts_headline` over the summary) | 200 `PagedResponse<PublicKbSearchResultDto>` (10 a page, at most 25; HTML-encoded snippet); an empty page for an unknown or inactive key (no enumeration); `Cache-Control: public, max-age=60` | Named handler; also used by deflection (same use case) |',
    '| `GET /api/public/kb/articles/{product}/{slug}`': '| `GET /api/public/kb/{productKey}/articles/{categorySlug}/{slug}` (anonymous) | `GetPublishedKbArticleRequestHandler` | `IKbRepository`, `IProductRepository`, `IKbContentRenderer` | `KbRepository`, KB Markdig profile and sanitiser | 200 `PublishedKbArticleDto` (sanitized HTML); uniform 404 `kb-article-not-found` with `no-store` | Named handler |',
    '| `GET /api/public/kb/categories?product={key}`': '| `GET /api/public/kb/{productKey}/categories` (anonymous) | `ListPublicKbCategoriesRequestHandler` | `IKbRepository`, `IProductRepository` | `KbRepository` | 200 `PublicKbCategoryDto[]` with published-article counts (empty categories left out) | Named handler |',
    '| `GET /api/public/sitemap` ': '| `GET /api/public/kb/{productKey}/sitemap` (anonymous) | `GetKbSitemapRequestHandler` | `IKbRepository`, `IProductRepository` | `KbRepository` | 200 `KbSitemapEntryDto[]` (product key, category slug, slug, `updated_at`; `max-age=300`) | Named handler; consumed by portal `MapSitemap` in P09 |',
    '| KB image files under `kb-images/` ': '| `GET /kb-images/{name}` (anonymous, root of the Api) | Exempt | None | `KbImageEndpoints` over `IKbImageStore` | The image bytes with `nosniff`, `CSP: default-src \'none\'; sandbox` and a one-year immutable cache; 404 otherwise | Exempt: static assets execute no workflow (D-021) |',
}
for prefix, row in rows.items():
    text = replace_row(text, prefix, row)
text = text.replace('Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |\n| :----', 'Allowed abstractions | Infrastructure implementation | Outcome/transport mapping | Decision |\n| :----')
save(path, text, crlf)

# ---------------- 02-ARCHITECTURE ----------------
path = 'docs/architecture/02-ARCHITECTURE.md'
text, crlf = load(path)
arch = {
    '| `GET /api/kb/articles` (Agent)': '| `GET /api/kb/articles` (Agent) | `ListKbArticlesRequestHandler` | `IKbRepository` | EF repos (FTS) | 200 paged `KbArticleListItemDto`; 400 `status-invalid` | H, C, I | D-011 |',
    '| `GET /api/kb/articles/{id}` (Agent)': '| `GET /api/kb/articles/{id}` (Agent) | `GetKbArticleRequestHandler` | `IKbRepository` | EF repos | 200 `KbArticleDto` (Markdown source); 404 | H, C | none |',
    '| `POST /api/kb/articles` (Agent)': '| `POST /api/kb/articles` (Agent) | `CreateKbArticleRequestHandler` | `IKbRepository`, `IProductRepository`, `ICurrentAgentClaims` | EF repos | 201 `KbArticleDto`; 400; 409 `kb-slug-taken` (slugs are unique across scopes, D-044) | H, C, I | D-044 |',
    '| `PUT /api/kb/articles/{id}` (Agent)': '| `PUT /api/kb/articles/{id}` (Agent) | `UpdateKbArticleRequestHandler` | `IKbRepository` | EF repos | 200 `KbArticleDto` (an Archived article returns to Draft); 404; 409 `concurrency-conflict` | H, C, I | D-044 |',
    '| `POST /api/kb/articles/{id}/publish` (Agent)': '| `POST /api/kb/articles/{id}/publish` (Agent) | `PublishKbArticleRequestHandler` | `IKbRepository` | EF repos | 200 `KbArticleDto`; 400 `kb-publish-incomplete`; 404; 409 | H, C | D-044 |',
    '| `POST /api/kb/articles/{id}/archive` (Agent)': '| `POST /api/kb/articles/{id}/archive` (Agent) | `ArchiveKbArticleRequestHandler` | `IKbRepository` | EF repos | 200 `KbArticleDto`; 404; 409 | H, C | none |',
    '| `POST /api/kb/preview` (Agent)': '| `POST /api/kb/preview` (Agent) | `RenderKbPreviewRequestHandler` | `IKbContentRenderer` | KB Markdig profile, KB sanitiser | 200 `KbPreviewResponse` (sanitized HTML); 400 oversize | H, C | D-021, D-044 |',
    '| `POST /api/kb/images` (Agent; multipart)': '| `POST /api/kb/images` (Agent; multipart) | `UploadKbImageRequestHandler` | `IKbImageStore`, `IKbImageUrls` | KB image store (public-read `kb-images/` prefix) | 201 `KbImageUploadResponse` (key and URL); 400 type or size (SVG rejected) | H, C, I | D-044 |',
    '| `GET /api/public/kb/search?product={key}': '| `GET /api/public/kb/{productKey}/search?q=&category=` (anonymous, `public` limit; also deflection) | `SearchPublicKbArticlesRequestHandler` | `IKbRepository`, `IProductRepository` | EF repos (FTS) | 200 `PagedResponse<PublicKbSearchResultDto>` with `Cache-Control: public, max-age=60` | H, C, I | D-011, D-044 |',
    '| `GET /api/public/kb/articles/{product}/{slug}`': '| `GET /api/public/kb/{productKey}/articles/{categorySlug}/{slug}` (anonymous, `public` limit) | `GetPublishedKbArticleRequestHandler` | `IKbRepository`, `IProductRepository`, `IKbContentRenderer` | EF repos, KB Markdig profile and sanitiser | 200 `PublishedKbArticleDto` (sanitized HTML) with `Cache-Control: public`; 404 `no-store` | H, C, I | D-014, D-044 |',
    '| `GET /api/public/kb/categories?product={key}`': '| `GET /api/public/kb/{productKey}/categories` (anonymous, `public` limit) | `ListPublicKbCategoriesRequestHandler` | `IKbRepository`, `IProductRepository` | EF repos | 200 `PublicKbCategoryDto[]` | H, C | none |',
    '| `GET /api/public/sitemap` ': '| `GET /api/public/kb/{productKey}/sitemap` (anonymous, `public` limit) | `GetKbSitemapRequestHandler` | `IKbRepository`, `IProductRepository` | EF repos | 200 `KbSitemapEntryDto[]` | H, C | none |',
}
for prefix, row in arch.items():
    text = replace_row(text, prefix, row)
text = text.replace('| `IKbImageStore` | Over `SyntaxCircus.Storage` | KB images under the public-read `kb-images/` prefix (PHASE-08) |', '| `IKbImageStore` | Over `SyntaxCircus.Storage` | KB images under the public-read `kb-images/` prefix, served by `GET /kb-images/{name}` (PHASE-08, D-044) |\n| `IKbContentRenderer` | KB Markdig profile and sanitiser | The KB preview and the public article page share it; agent replies keep `IMarkdownRenderer` and `IHtmlSanitizer` (D-044) |')
save(path, text, crlf)
print('ok')
```

`docs/architecture/02-ARCHITECTURE.md` is rewritten by the same script (its twelve route rows and the `IKbImageStore` component row, plus a new `IKbContentRenderer` row).

- [ ] **Step 5: Run the tests and confirm they pass**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then:

```bash
dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter-class "*KbEndpointTests"
dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter-class "*ResultMappingTests"
dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter-class "*RoutePolicyCoverageTests"
dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter-class "*OpenApiSurfaceTests"
dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter-class "*OpenApiSecurityTests"
dotnet test --project tests/TechStrap.Application.Tests -c Release --no-build --filter-class "*UploadKbImageRequestHandlerTests"
pwsh -File scripts/Invoke-ScriptTests.ps1
```

Expected: PASS, 28, 121, 2, 5, 25 and 12 tests, and Pester 281.

- [ ] **Step 6: Prove each pin with a mutation**

Applied to the finished code, the named tests run, the change reverted (same method as Task 1).

| Mutation | Failing tests |
| --- | --- |
| `KbCategoriesController.Delete`: remove `[Authorize(Policy = AuthorizationPolicies.Admin)]` | `KbEndpointTests.An_agent_cannot_delete_a_category_but_an_admin_can_once_it_is_empty` |
| `PublicKbController`: remove `[EnableRateLimiting(...)]` | `KbEndpointTests.The_public_kb_routes_are_rate_limited_per_ip`; `RoutePolicyCoverageTests.Every_public_and_api_key_route_is_rate_limited` |
| `PublicKbController.CachedFor`: the success header becomes `no-store` | `KbEndpointTests.An_article_written_by_an_agent_reaches_the_public_api_only_while_it_is_published` |
| `PublicKbController.CachedFor`: delete the `no-store` line that runs first (a 404 becomes cacheable) | `KbEndpointTests.An_article_written_by_an_agent_reaches_the_public_api_only_while_it_is_published` |
| `KbPreviewController`: `[Authorize(Policy = Agent)]` becomes `[AllowAnonymous]` | `KbEndpointTests.The_preview_returns_sanitised_html_and_refuses_hostile_markup_an_oversize_source_and_non_agents`; `KbEndpointTests.Every_agent_route_refuses_an_anonymous_caller_...(POST /api/kb/preview)` |

- [ ] **Step 7: Whole-project check**

Run: `dotnet test --solution TechStrap.CI.slnf -c Release` and `pwsh -File scripts/Invoke-ScriptTests.ps1`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src \
  tests \
  scripts/tests \
  docs/architecture
git diff --cached --stat
git commit -m "feat(kb): KB controllers, public routes with cache headers, and the OpenAPI surface" -m "Five controllers: the agent articles, categories (delete is Admin only), preview and image upload, and the anonymous public KB (search, article, categories, sitemap) behind the public rate limit with no-store on errors and a short public cache on hits. Every action has an explicit policy and a ControllerActions row, so the route-policy and OpenAPI tests cover it. The upload handler takes a missing file as a 400 file-required. The phase spec and the architecture document list the as-built routes. Host tests cover the write and read flow, the preview corpus, the upload refusals and 413, the cache headers and the rate limit." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```


### Task 8: Reply linking, the customer email links and the seed

**Review Focus pin:** Review Focus 5 (a reply links an unpublished or other-product article, or the email carries a broken link). Pinned here by `AddAgentReplyRequestHandlerTests`, `AgentReplyIntegrationTests` (a draft, an archived, another product's, a misfiled and a category-less article are refused with nothing stored or emailed; shared and own-product articles link and the payload holds one portal link each with the ticket's own product key), `TicketNotificationPlannerTests`, `EmailTemplateRendererTests` (a title is escaped, a relative or script link is dropped, at most ten are listed) and `AgentReplyEndpointTests` over HTTP.

**Files:**
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`
- Modify: `docs/architecture/PHASE-08-knowledge-base.md`
- Modify: `docs/development/DEV-DATA.md`
- Modify: `docs/development/TICKET-OPERATIONS.md`
- Test (modify): `scripts/tests/RepositoryDocs.Tests.ps1`
- Modify: `src/TechStrap.Application/Email/EmailTemplates.cs`
- Modify: `src/TechStrap.Application/Intake/PortalLinkOptions.cs`
- Modify: `src/TechStrap.Application/Tickets/AddAgentReplyRequestHandler.cs`
- Modify: `src/TechStrap.Application/Tickets/Notifications/ITicketNotificationPlanner.cs`
- Modify: `src/TechStrap.Infrastructure/Email/EmailTemplateRenderer.cs`
- Modify: `src/TechStrap.Infrastructure/Seeding/DevelopmentDataSeeder.cs`
- Modify: `src/TechStrap.Infrastructure/Tickets/TicketNotificationPlanner.cs`
- Test (modify): `tests/TechStrap.Api.Tests/Tickets/AgentReplyEndpointTests.cs`
- Test (modify): `tests/TechStrap.Application.Tests/Tickets/AddAgentReplyRequestHandlerTests.cs`
- Test (modify): `tests/TechStrap.Infrastructure.IntegrationTests/AgentReplyIntegrationTests.cs`
- Test (modify): `tests/TechStrap.Infrastructure.IntegrationTests/DevSeederTests.cs`
- Test (modify): `tests/TechStrap.Infrastructure.IntegrationTests/EmailTemplateRendererTests.cs`
- Test (modify): `tests/TechStrap.Infrastructure.IntegrationTests/TicketNotificationPlannerTests.cs`

**Interfaces:**
- Consumes: `AddAgentReplyRequestHandler` and `TicketErrors.Invalid`, `ITicketNotificationPlanner` and `TicketNotificationPlanner` (D-033: payloads are template data and the Worker renders at send time), `AgentReplyEmail`, `EmailTemplateRenderer.RenderAgentReply`, `PortalLinkOptions`, `IKbRepository.GetArticleAsync` and `GetCategoryAsync`, `DevelopmentDataSeeder`, `TicketOperationLimits.MaxLinkedArticles` (10).
- Produces:
  - Validation in `AddAgentReplyRequestHandler`: an unknown id is still 400 `article-not-found`; an article that is not Published, or belongs to another product (not shared, not the ticket's), or has no category, or whose category belongs to another product, is 400 `kb-article-not-linkable` (target `linkedArticleIds`). Nothing is stored, sent or planned. The existing limit of 10 and the de-duplication stay.
  - `ReplyArticleLink(string Title, string CategorySlug, string Slug)` (`TechStrap.Application.Tickets.Notifications`) and a second `ITicketNotificationPlanner.PlanAgentReplyAsync(Ticket, Message, Agent author, bool solved, IReadOnlyList<ReplyArticleLink> articles, CancellationToken)`; the existing five-argument method delegates with an empty list.
  - `ArticleLinkEntry(string Title, string Url)` and `AgentReplyEmail.Articles` (`IReadOnlyList<ArticleLinkEntry>?`, last parameter, default null: a row queued before this task has none). The planner builds each URL as `PortalLinkOptions.ArticleLink(productKey, categorySlug, articleSlug)` = `{TECHSTRAP_PORTAL_PUBLIC_URL}/p/{key}/kb/{category}/{slug}` with the TICKET's product key (a shared article is served under the visited product).
  - The customer email lists "Related articles" (HTML links and plain-text lines) after the message and before the solved line and the "View your request" button. A link is dropped unless its URL is an absolute http or https address; at most ten; titles are encoded.
  - Seed: a Paperplane category `guides` and a published Paperplane article `using-dark-mode`, so two products plus the shared space each have a published article; `DevSeederTests` pins five articles.
  - Docs: `DEV-DATA.md`, `TICKET-OPERATIONS.md`, the phase spec (P08-T01 to T12 and four deliverables ticked, the slug risk resolved) and the roadmap row (In progress).

- [ ] **Step 1: Write the failing tests**

The handler unit tests change in two ways: every `PlanAgentReplyAsync` expectation moves to the six-argument overload (a leftover five-argument `DidNotReceive...` or `ThrowsAsync...` would pass vacuously because the handler no longer calls it, so each is changed), and `GivenArticle` now builds a Published article with a category. `AgentReplyIntegrationTests` seeds a richer world (a second product, shared, own and foreign categories, a draft, an archived, an other-product article in a shared category, a misfiled one and a category-less one) so each rule is isolated. The other-product article uses a shared category on purpose: with the foreign category the category rule alone would refuse it and the product rule would be unpinned.

Modify `scripts/tests/RepositoryDocs.Tests.ps1` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -105,6 +105,22 @@ Describe 'D-044 (the knowledge base)' {
         }
     }
 
+    It 'ticks the API tasks P08-T01 to P08-T12 and the roadmap row says the phase is in progress' {
+        $phase = Get-RepoText 'docs/architecture/PHASE-08-knowledge-base.md'
+        foreach ($number in 1..12) {
+            $id = 'P08-T{0:00}' -f $number
+            $phase | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is done"
+        }
+        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 08 \|.*D-044.*\| In progress'
+    }
+
+    It 'describes the dev knowledge base seed and the reply link rule' {
+        (Get-RepoText 'docs/development/DEV-DATA.md') | Should -Match 'using-dark-mode'
+        $operations = Get-RepoText 'docs/development/TICKET-OPERATIONS.md'
+        $operations | Should -Match 'kb-article-not-linkable'
+        $operations | Should -Match ([regex]::Escape('/p/{product key}/kb/{category slug}/{article slug}'))
+    }
+
     It 'tells the operator about the Api public URL and the /kb-images/ proxy route' {
         $runbook = Get-RepoText 'docs/self-hosting/DEPLOYMENT.md'
         $runbook | Should -Match 'TECHSTRAP_API_PUBLIC_URL'
```

Modify `tests/TechStrap.Api.Tests/Tickets/AgentReplyEndpointTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -2,6 +2,7 @@ using System.Net;
 using System.Net.Http.Headers;
 using System.Net.Http.Json;
 using TechStrap.Api.Tests.Auth;
+using TechStrap.Contracts.Kb;
 using TechStrap.Contracts.Tickets;
 
 namespace TechStrap.Api.Tests.Tickets;
@@ -68,6 +69,71 @@ public sealed class AgentReplyEndpointTests(TestPostgres postgres) : IDisposable
         (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'agent-reply'")).ShouldBe(1);
     }
 
+    [Fact]
+    public async Task Review_Focus_5_a_reply_links_only_published_articles_of_the_tickets_product_and_the_email_carries_their_portal_links()
+    {
+        var (factory, database, seed) = await StartAsync();
+        await using var _f = factory;
+        using var sam = TicketTestData.AgentClient(factory, "sam");
+        var ticket = seed.Tickets[0];
+        var own = ticket.ProductId == seed.Orbitly.Id ? seed.Orbitly : seed.Paperplane;
+        var other = ticket.ProductId == seed.Orbitly.Id ? seed.Paperplane : seed.Orbitly;
+        async Task<Guid> ArticleAsync(Guid? productId, string categorySlug, string slug, bool publish)
+        {
+            using var category = await sam.PostAsJsonAsync("/api/kb/categories", new CreateKbCategoryRequest(productId, categorySlug, categorySlug, null, 1), Ct);
+            Guid categoryId;
+            if (category.StatusCode == HttpStatusCode.Created)
+            {
+                categoryId = (await category.Content.ReadFromJsonAsync<KbCategoryDto>(Ct))!.Id;
+            }
+            else
+            {
+                categoryId = (await sam.GetFromJsonAsync<List<KbCategoryDto>>("/api/kb/categories", Ct))!.Single(c => c.Slug == categorySlug).Id;
+            }
+
+            using var created = await sam.PostAsJsonAsync("/api/kb/articles", new CreateKbArticleRequest(productId, categoryId, slug, "Title " + slug, null, "Steps."), Ct);
+            var article = (await created.Content.ReadFromJsonAsync<KbArticleDto>(Ct))!;
+            if (publish)
+            {
+                (await sam.PostAsync($"/api/kb/articles/{article.Id}/publish", null, Ct)).EnsureSuccessStatusCode();
+            }
+
+            return article.Id;
+        }
+
+        var ownPublished = await ArticleAsync(own.Id, "own-cat", "own-published", publish: true);
+        var ownDraft = await ArticleAsync(own.Id, "own-cat", "own-draft", publish: false);
+        var otherPublished = await ArticleAsync(other.Id, "other-cat", "other-published", publish: true);
+        var sharedPublished = await ArticleAsync(null, "general", "shared-published", publish: true);
+
+        async Task<HttpResponseMessage> ReplyAsync(params Guid[] ids)
+        {
+            using var form = new MultipartFormDataContent { { new StringContent("See these"), "body" } };
+            foreach (var id in ids)
+            {
+                form.Add(new StringContent(id.ToString()), "linkedArticleIds");
+            }
+
+            return await sam.PostAsync($"/api/tickets/{ticket.Id}/replies", form, Ct);
+        }
+
+        using var draft = await ReplyAsync(ownPublished, ownDraft);
+        using var foreign = await ReplyAsync(sharedPublished, otherPublished);
+        (await database.ScalarAsync<long>("SELECT count(*) FROM email_outbox WHERE kind = 'agent-reply'")).ShouldBe(0);
+        using var accepted = await ReplyAsync(ownPublished, sharedPublished);
+
+        draft.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
+        (await draft.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-article-not-linkable");
+        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
+        (await foreign.Content.ReadAsStringAsync(Ct)).ShouldContain("kb-article-not-linkable");
+        accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
+        var payload = await database.ScalarAsync<string>("SELECT payload::text FROM email_outbox WHERE kind = 'agent-reply'");
+        payload.ShouldContain($"https://help.test/p/{own.Key}/kb/own-cat/own-published");
+        payload.ShouldContain($"https://help.test/p/{own.Key}/kb/general/shared-published");
+        payload.ShouldNotContain(other.Key);
+        payload.ShouldNotContain("own-draft");
+    }
+
     [Fact]
     public async Task A_reply_with_an_executable_attachment_is_400_attachment_type_not_allowed()
     {
```

Modify `tests/TechStrap.Application.Tests/Tickets/AddAgentReplyRequestHandlerTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -71,9 +71,14 @@ public sealed class AddAgentReplyRequestHandlerTests
 
     private static IncomingAttachment Png(string name = "shot.png", long length = 12) => new(name, "image/png", length, new MemoryStream([1, 2, 3]));
 
-    private KbArticle GivenArticle(string slug)
+    // A Published article with a category: the only kind a reply may link (D-044). The product and the category's product default to shared.
+    private KbArticle GivenArticle(string slug, Guid? productId = null, Guid? categoryProductId = null, KbArticleStatus status = KbArticleStatus.Published, bool withCategory = true)
     {
-        var article = KbArticle.Create(null, null, slug, "Reset your password", null, "Steps.", _sam.Id, _clock).Value;
+        var category = KbCategory.Restore(Guid.CreateVersion7(), categoryProductId, "Account", "account", null, 1, 1);
+        _kb.GetCategoryAsync(category.Id, Arg.Any<CancellationToken>()).Returns(category);
+        var article = KbArticle.Restore(
+            Guid.CreateVersion7(), productId, withCategory ? category.Id : null, slug, "Reset your password", null, "Steps.", status, _sam.Id, _clock.GetUtcNow(), _clock.GetUtcNow(),
+            status == KbArticleStatus.Draft ? null : _clock.GetUtcNow(), 1);
         _kb.GetArticleAsync(article.Id, Arg.Any<CancellationToken>()).Returns(article);
         return article;
     }
@@ -88,7 +93,7 @@ public sealed class AddAgentReplyRequestHandlerTests
         _sanitizer.Received(1).Sanitize("<p>Hi **Ann**</p>");
         _tickets.Received(1).Update(_ticket);
         await _planner.Received(1).PlanAgentReplyAsync(
-            _ticket, Arg.Is<Message>(m => m.Visibility == MessageVisibility.Public && m.Body == "<p>Hi **Ann**</p>"), _sam, false, Ct);
+            _ticket, Arg.Is<Message>(m => m.Visibility == MessageVisibility.Public && m.Body == "<p>Hi **Ann**</p>"), _sam, false, Arg.Any<IReadOnlyList<ReplyArticleLink>>(), Ct);
         result.Value.Message.ShouldSatisfyAllConditions(
             m => m.BodyHtml.ShouldBe("<p>Hi **Ann**</p>"),
             m => m.AuthorName.ShouldBe("Sam"),
@@ -130,7 +135,7 @@ public sealed class AddAgentReplyRequestHandlerTests
         _stagedEvents.Select(e => e.Type).ShouldBe([TicketEventType.MessageAdded, TicketEventType.StatusChanged, TicketEventType.StatusChanged]);
         _stagedEvents[1].Payload.ShouldContain("Pending");
         _stagedEvents[2].Payload.ShouldContain("Solved");
-        await _planner.Received(1).PlanAgentReplyAsync(ticket, Arg.Any<Message>(), _sam, true, Ct);
+        await _planner.Received(1).PlanAgentReplyAsync(ticket, Arg.Any<Message>(), _sam, true, Arg.Any<IReadOnlyList<ReplyArticleLink>>(), Ct);
         await _planner.DidNotReceive().PlanSolvedAsync(Arg.Any<Ticket>(), Arg.Any<CancellationToken>());
     }
 
@@ -148,7 +153,7 @@ public sealed class AddAgentReplyRequestHandlerTests
             e => e.Code.ShouldBe("ticket-closed"));
         await _store.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, Ct);
         _tickets.DidNotReceiveWithAnyArgs().Update(default!);
-        await _planner.DidNotReceiveWithAnyArgs().PlanAgentReplyAsync(default!, default!, default!, default, Ct);
+        await _planner.DidNotReceiveWithAnyArgs().PlanAgentReplyAsync(default!, default!, default!, default, default!, Ct);
     }
 
     [Theory]
@@ -202,6 +207,69 @@ public sealed class AddAgentReplyRequestHandlerTests
         _tickets.DidNotReceiveWithAnyArgs().Update(default!);
     }
 
+    [Theory]
+    [InlineData(KbArticleStatus.Draft)]
+    [InlineData(KbArticleStatus.Archived)]
+    public async Task A_draft_or_archived_article_is_not_linkable_and_nothing_is_stored_or_planned(KbArticleStatus status)
+    {
+        var article = GivenArticle("reset", status: status);
+
+        var result = await Reply(Request(articles: [article.Id]), [Png()]);
+
+        result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
+            e => e.Kind.ShouldBe(ResultErrorKind.Validation),
+            e => e.Code.ShouldBe("kb-article-not-linkable"),
+            e => e.Target.ShouldBe("linkedArticleIds"));
+        await _store.DidNotReceiveWithAnyArgs().SaveAsync(default, default!, Ct);
+        _tickets.DidNotReceiveWithAnyArgs().Update(default!);
+        _kb.DidNotReceiveWithAnyArgs().AddTicketArticle(default!);
+        await _planner.DidNotReceiveWithAnyArgs().PlanAgentReplyAsync(default!, default!, default!, default, default!, Ct);
+    }
+
+    [Fact]
+    public async Task An_article_of_another_product_is_not_linkable_but_the_tickets_own_product_and_shared_articles_are()
+    {
+        var other = GivenArticle("other", productId: Guid.NewGuid());
+        var own = GivenArticle("own", productId: _ticket.ProductId, categoryProductId: _ticket.ProductId);
+        var shared = GivenArticle("shared");
+
+        var refused = await Reply(Request(articles: [shared.Id, other.Id]));
+        var accepted = await Reply(Request(articles: [own.Id, shared.Id]));
+
+        refused.Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-linkable");
+        accepted.IsSuccess.ShouldBeTrue();
+        accepted.Value.Message.LinkedArticles.Select(a => a.Slug).ShouldBe(["own", "shared"]);
+    }
+
+    [Fact]
+    public async Task An_article_with_no_category_or_a_category_of_another_product_cannot_get_a_portal_link_so_it_is_not_linkable()
+    {
+        var uncategorised = GivenArticle("bare", withCategory: false);
+        var misfiled = GivenArticle("misfiled", categoryProductId: Guid.NewGuid());
+
+        (await Reply(Request(articles: [uncategorised.Id]))).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-linkable");
+        (await Reply(Request(articles: [misfiled.Id]))).Errors.ShouldHaveSingleItem().Code.ShouldBe("kb-article-not-linkable");
+    }
+
+    [Fact]
+    public async Task The_planner_gets_each_linked_articles_title_category_and_slug_in_the_order_given()
+    {
+        var first = GivenArticle("first");
+        var second = GivenArticle("second");
+
+        var result = await Reply(Request(articles: [second.Id, first.Id]));
+
+        result.IsSuccess.ShouldBeTrue();
+        await _planner.Received(1).PlanAgentReplyAsync(
+            _ticket, Arg.Any<Message>(), _sam, false,
+            Arg.Is<IReadOnlyList<ReplyArticleLink>>(links => links.SequenceEqual(new[]
+            {
+                new ReplyArticleLink("Reset your password", "account", "second"),
+                new ReplyArticleLink("Reset your password", "account", "first"),
+            })),
+            Ct);
+    }
+
     [Fact]
     public async Task Too_many_linked_articles_is_a_field_error()
     {
@@ -371,7 +439,7 @@ public sealed class AddAgentReplyRequestHandlerTests
     [Fact]
     public async Task An_exception_after_saving_deletes_the_stored_attachments_and_rethrows()
     {
-        _planner.PlanAgentReplyAsync(default!, default!, default!, default, Ct).ThrowsAsyncForAnyArgs(new InvalidOperationException("boom"));
+        _planner.PlanAgentReplyAsync(default!, default!, default!, default, default!, Ct).ThrowsAsyncForAnyArgs(new InvalidOperationException("boom"));
 
         await Should.ThrowAsync<InvalidOperationException>(() => Reply(Request(), [Png()]));
 
@@ -386,7 +454,7 @@ public sealed class AddAgentReplyRequestHandlerTests
         var result = await Reply(Request());
 
         result.IsSuccess.ShouldBeTrue();
-        await _planner.Received(1).PlanAgentReplyAsync(_ticket, Arg.Any<Message>(), _sam, false, Ct);
+        await _planner.Received(1).PlanAgentReplyAsync(_ticket, Arg.Any<Message>(), _sam, false, Arg.Any<IReadOnlyList<ReplyArticleLink>>(), Ct);
         _tickets.Received(1).Update(_ticket);
     }
 
@@ -405,6 +473,6 @@ public sealed class AddAgentReplyRequestHandlerTests
         await _tickets.Received().GetStateAsync(_ticket.Id, CancellationToken.None);
         await _kb.Received().GetArticleAsync(article.Id, token);
         await _store.Received().SaveAsync(_ticket.Id, Arg.Any<IncomingAttachment>(), token);
-        await _planner.Received().PlanAgentReplyAsync(_ticket, Arg.Any<Message>(), _sam, false, token);
+        await _planner.Received().PlanAgentReplyAsync(_ticket, Arg.Any<Message>(), _sam, false, Arg.Any<IReadOnlyList<ReplyArticleLink>>(), token);
     }
 }
```

Modify `tests/TechStrap.Infrastructure.IntegrationTests/AgentReplyIntegrationTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -27,7 +27,7 @@ public sealed class AgentReplyIntegrationTests(PostgresFixture postgres) : Postg
 
     private readonly string _root = Path.Combine(Path.GetTempPath(), "techstrap-reply-" + Guid.NewGuid().ToString("N"));
 
-    private sealed record Seed(Guid TicketId, Guid ArticleId);
+    private sealed record Seed(Guid TicketId, Guid ArticleId, Guid DraftId, Guid ArchivedId, Guid OwnProductId, Guid OtherProductId, Guid NoCategoryId, Guid ForeignCategoryId);
 
     private sealed class StubAgentClaims : ICurrentAgentClaims
     {
@@ -73,7 +73,7 @@ public sealed class AgentReplyIntegrationTests(PostgresFixture postgres) : Postg
 
     private static async Task<Seed> SeedAsync(PersistenceTestHost host, bool eraseRequester = false)
     {
-        Guid ticketId = default, articleId = default;
+        Guid ticketId = default, articleId = default, draftId = default, archivedId = default, ownId = default, otherId = default, noCategoryId = default, foreignCategoryId = default;
         (await host.CommitAsync(sp =>
         {
             var product = Product.Create("orbitly", "Orbitly", "ORB", null, host.Clock).Value;
@@ -87,8 +87,42 @@ public sealed class AgentReplyIntegrationTests(PostgresFixture postgres) : Postg
             sp.GetRequiredService<IRequesterRepository>().Add(requester);
             var sam = Agent.Create("sub-sam", "Sam Taylor", "sam.taylor@techstrap.test", AgentRole.Agent, host.Clock).Value;
             sp.GetRequiredService<IAgentRepository>().Add(sam);
-            var article = KbArticle.Create(null, null, "reset-password", "Reset your password", null, "Steps.", sam.Id, host.Clock).Value;
-            sp.GetRequiredService<IKbRepository>().AddArticle(article);
+            var paperplane = Product.Create("paperplane", "Paperplane", "PPL", null, host.Clock).Value;
+            sp.GetRequiredService<IProductRepository>().Add(paperplane);
+            var kb = sp.GetRequiredService<IKbRepository>();
+            var shared = KbCategory.Create(null, "general", "General", 1, host.Clock).Value;
+            var own = KbCategory.Create(product.Id, "account", "Account", 2, host.Clock).Value;
+            var foreign = KbCategory.Create(paperplane.Id, "guides", "Guides", 3, host.Clock).Value;
+            kb.AddCategory(shared);
+            kb.AddCategory(own);
+            kb.AddCategory(foreign);
+            KbArticle Make(Guid? productId, Guid? categoryId, string slug, bool publish, bool archive = false)
+            {
+                var made = KbArticle.Create(productId, categoryId, slug, "Title " + slug, null, "Steps.", sam.Id, host.Clock).Value;
+                if (publish)
+                {
+                    made.Publish(host.Clock).IsSuccess.ShouldBeTrue();
+                }
+
+                if (archive)
+                {
+                    made.Archive(host.Clock).IsSuccess.ShouldBeTrue();
+                }
+
+                kb.AddArticle(made);
+                return made;
+            }
+
+            var article = Make(null, shared.Id, "reset-password", publish: true);
+            (draftId, archivedId) = (Make(null, shared.Id, "shared-draft", publish: false).Id, Make(null, shared.Id, "shared-archived", publish: true, archive: true).Id);
+            (ownId, otherId) = (Make(product.Id, own.Id, "orbitly-only", publish: true).Id, Make(paperplane.Id, shared.Id, "paperplane-only", publish: true).Id);
+
+            // Published, but filed in another product's category, or in none: the portal address cannot be built for this product, so neither is linkable.
+            foreignCategoryId = Make(null, foreign.Id, "misfiled", publish: true).Id;
+            var uncategorised = KbArticle.Restore(
+                Guid.CreateVersion7(), null, null, "no-category", "Title no-category", null, "Steps.", KbArticleStatus.Published, sam.Id, host.Clock.GetUtcNow(), host.Clock.GetUtcNow(), host.Clock.GetUtcNow(), 0);
+            kb.AddArticle(uncategorised);
+            noCategoryId = uncategorised.Id;
             var ticket = Ticket.Create(TicketNumber.Create("ORB", 1).Value, product.Id, requester.Id, "Cannot log in", TicketChannel.Web, null, false, host.Clock).Value;
             ticket.AddCustomerReply(requester.Id, "<p>Help</p>", host.Clock).IsSuccess.ShouldBeTrue();
             ticket.ChangeStatus(TicketStatus.Open, Actor.ForAgent(sam.Id), host.Clock).IsSuccess.ShouldBeTrue();
@@ -96,7 +130,7 @@ public sealed class AgentReplyIntegrationTests(PostgresFixture postgres) : Postg
             (ticketId, articleId) = (ticket.Id, article.Id);
             return Task.CompletedTask;
         })).IsSuccess.ShouldBeTrue();
-        return new Seed(ticketId, articleId);
+        return new Seed(ticketId, articleId, draftId, archivedId, ownId, otherId, noCategoryId, foreignCategoryId);
     }
 
     private static IncomingAttachment PngFile() => new("shot.png", "image/png", Png.Length, new MemoryStream(Png));
@@ -142,6 +176,58 @@ public sealed class AgentReplyIntegrationTests(PostgresFixture postgres) : Postg
         result.Value.Ticket.Status.ShouldBe("Pending");
     }
 
+    [Fact]
+    public async Task Review_Focus_5_an_unpublished_other_product_or_unfiled_article_is_refused_and_nothing_is_stored_or_emailed()
+    {
+        await using var host = NewHost();
+        var seed = await SeedAsync(host);
+        var messagesBefore = await ScalarAsync("SELECT count(*) FROM messages");
+
+        foreach (var (label, id) in new[]
+        {
+            ("draft", seed.DraftId),
+            ("archived", seed.ArchivedId),
+            ("another product", seed.OtherProductId),
+            ("filed in another product's category", seed.ForeignCategoryId),
+            ("no category", seed.NoCategoryId),
+        })
+        {
+            var result = await ReplyAsync(host, seed.TicketId, new AddAgentReplyRequest("See this", [seed.ArticleId, id], null, null), PngFile());
+
+            result.Errors.ShouldHaveSingleItem().ShouldSatisfyAllConditions(
+                error => error.Code.ShouldBe("kb-article-not-linkable", label),
+                error => error.Kind.ShouldBe(ResultErrorKind.Validation),
+                error => error.Target.ShouldBe("linkedArticleIds"));
+        }
+
+        (await ScalarAsync("SELECT count(*) FROM messages")).ShouldBe(messagesBefore);
+        (await ScalarAsync("SELECT count(*) FROM ticket_articles")).ShouldBe(0);
+        (await ScalarAsync("SELECT count(*) FROM email_outbox")).ShouldBe(0);
+        FilesUnderRoot().ShouldBeEmpty();
+    }
+
+    [Fact]
+    public async Task Review_Focus_5_shared_and_own_product_articles_link_and_the_customer_email_carries_one_portal_link_for_each_with_the_tickets_product_key()
+    {
+        await using var host = NewHost();
+        var seed = await SeedAsync(host);
+
+        var result = await ReplyAsync(host, seed.TicketId, new AddAgentReplyRequest("Two guides", [seed.ArticleId, seed.OwnProductId], null, null));
+
+        result.IsSuccess.ShouldBeTrue();
+        result.Value.Message.LinkedArticles.Select(article => article.Slug).Order().ShouldBe(["orbitly-only", "reset-password"]);
+        (await ScalarAsync($"SELECT count(*) FROM ticket_articles WHERE message_id = '{result.Value.Message.Id}'")).ShouldBe(2);
+        await using var connection = new NpgsqlConnection(Database.ConnectionString);
+        await connection.OpenAsync(Ct);
+        await using var command = connection.CreateCommand();
+        command.CommandText = "SELECT payload::text FROM email_outbox WHERE kind = 'agent-reply'";
+        var payload = (string)(await command.ExecuteScalarAsync(Ct))!;
+        payload.ShouldContain("https://help.test/p/orbitly/kb/general/reset-password");
+        payload.ShouldContain("https://help.test/p/orbitly/kb/account/orbitly-only");
+        payload.ShouldNotContain("paperplane");
+        payload.ShouldNotContain("shared-draft");
+    }
+
     [Fact]
     public async Task A_failed_commit_leaves_no_message_rows_and_no_files()
     {
```

Modify `tests/TechStrap.Infrastructure.IntegrationTests/DevSeederTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -113,6 +113,22 @@ public sealed class DevSeederTests(PostgresFixture postgres) : PostgresIntegrati
         found.Items.ShouldHaveSingleItem().Slug.ShouldBe("reset-password");
     }
 
+    [Fact]
+    public async Task Seeding_publishes_knowledge_base_articles_in_two_products_and_the_shared_space_each_under_a_category()
+    {
+        await using var provider = BuildProvider();
+
+        await SeedAsync(provider);
+
+        var published = await ReadAsync(provider, sp => sp.GetRequiredService<IKbRepository>().ListArticlesAsync(new KbArticleQuery(Status: KbArticleStatus.Published), Ct));
+        var products = (await ReadAsync(provider, sp => sp.GetRequiredService<IProductRepository>().ListAsync(false, Ct))).ToDictionary(p => p.Id, p => p.Key);
+        published.Items.Select(a => a.ProductId is { } id ? products[id] : "shared").Order().ShouldBe(["orbitly", "paperplane", "shared"]);
+        published.Items.ShouldAllBe(article => article.CategoryId != null);
+        var paperplaneId = products.Single(p => p.Value == "paperplane").Key;
+        var categories = await ReadAsync(provider, sp => sp.GetRequiredService<IKbRepository>().ListPublicCategoriesAsync(paperplaneId, Ct));
+        categories.Select(c => (c.Category.Slug, c.ArticleCount)).ShouldBe([("getting-started", 1), ("guides", 1)]);
+    }
+
     [Theory]
     [InlineData(DevelopmentApiKeys.OrbitlyTrusted)]
     [InlineData(DevelopmentApiKeys.OrbitlyPublic)]
@@ -174,7 +190,7 @@ public sealed class DevSeederTests(PostgresFixture postgres) : PostgresIntegrati
         var all = await ReadAsync(first, sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.All, PageSize: 100), Ct));
         var spam = await ReadAsync(first, sp => sp.GetRequiredService<ITicketRepository>().ListAsync(new TicketQuery(TicketView.Spam), Ct));
         (all.TotalCount + spam.TotalCount).ShouldBe(7);
-        (await ReadAsync(first, sp => sp.GetRequiredService<IKbRepository>().ListArticlesAsync(new KbArticleQuery(), Ct))).TotalCount.ShouldBe(4);
+        (await ReadAsync(first, sp => sp.GetRequiredService<IKbRepository>().ListArticlesAsync(new KbArticleQuery(), Ct))).TotalCount.ShouldBe(5);
     }
 
     [Fact]
```

Modify `tests/TechStrap.Infrastructure.IntegrationTests/EmailTemplateRendererTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -101,6 +101,68 @@ public sealed class EmailTemplateRendererTests
     private static AgentReplyEmail Reply(bool solved = false, string? requester = "Ann") =>
         new("ORB-42", "Printer jam", requester, "https://help.test/t/abc", "Sam from Orbitly Support", Guid.NewGuid(), solved);
 
+    private static AgentReplyEmail ReplyWith(params ArticleLinkEntry[] articles) => Reply() with { Articles = articles };
+
+    [Fact]
+    public void An_agent_reply_lists_each_linked_article_as_a_link_in_html_and_a_line_in_text()
+    {
+        var email = Renderer().RenderAgentReply(
+            ReplyWith(new ArticleLinkEntry("Reset your password", "https://help.test/p/orbitly/kb/account/reset-password"), new ArticleLinkEntry("Export to CSV", "https://help.test/p/orbitly/kb/general/export-csv")),
+            "<p>See these</p>", Orbitly);
+
+        email.Html.ShouldContain("Related articles:");
+        email.Html.ShouldContain("<a href=\"https://help.test/p/orbitly/kb/account/reset-password\"");
+        email.Html.ShouldContain(">Reset your password</a>");
+        email.Html.ShouldContain("<a href=\"https://help.test/p/orbitly/kb/general/export-csv\"");
+        email.Text.ShouldContain("Related articles:\n- Reset your password: https://help.test/p/orbitly/kb/account/reset-password\n- Export to CSV: https://help.test/p/orbitly/kb/general/export-csv\n");
+        email.Html.IndexOf("Related articles:", StringComparison.Ordinal).ShouldBeGreaterThan(email.Html.IndexOf("See these", StringComparison.Ordinal));
+        email.Html.IndexOf("Related articles:", StringComparison.Ordinal).ShouldBeLessThan(email.Html.IndexOf("View your request", StringComparison.Ordinal));
+    }
+
+    [Fact]
+    public void A_reply_with_no_articles_is_unchanged_and_a_row_queued_before_phase_08_still_renders()
+    {
+        var plain = Renderer().RenderAgentReply(Reply(), "<p>Hi</p>", Orbitly);
+        var empty = Renderer().RenderAgentReply(Reply() with { Articles = [] }, "<p>Hi</p>", Orbitly);
+
+        plain.Html.ShouldNotContain("Related articles");
+        plain.Text.ShouldNotContain("Related articles");
+        empty.Html.ShouldBe(plain.Html);
+        empty.Text.ShouldBe(plain.Text);
+    }
+
+    [Fact]
+    public void An_article_title_is_escaped_and_a_link_that_is_not_an_absolute_web_address_is_dropped_so_no_broken_link_is_emailed()
+    {
+        var email = Renderer().RenderAgentReply(
+            ReplyWith(
+                new ArticleLinkEntry("<script>alert(1)</script>", "https://help.test/p/orbitly/kb/a/b"),
+                new ArticleLinkEntry("Relative", "/p/orbitly/kb/a/c"),
+                new ArticleLinkEntry("Script", "javascript:alert(1)"),
+                new ArticleLinkEntry("Blank url", ""),
+                new ArticleLinkEntry(" ", "https://help.test/p/orbitly/kb/a/d")),
+            "<p>See</p>", Orbitly);
+
+        email.Html.ShouldNotContain("<script>");
+        email.Html.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
+        email.Html.ShouldNotContain("javascript:");
+        email.Html.ShouldNotContain("href=\"/p/");
+        email.Html.ShouldNotContain("kb/a/d");
+        email.Text.ShouldNotContain("javascript:");
+        email.Text.ShouldNotContain("Relative");
+    }
+
+    [Fact]
+    public void At_most_ten_articles_are_listed()
+    {
+        var articles = Enumerable.Range(1, 14).Select(i => new ArticleLinkEntry("Article " + i, $"https://help.test/p/orbitly/kb/a/slug-{i}")).ToArray();
+
+        var email = Renderer().RenderAgentReply(ReplyWith(articles), "<p>See</p>", Orbitly);
+
+        email.Text.ShouldContain("slug-10");
+        email.Text.ShouldNotContain("slug-11");
+    }
+
     [Fact]
     public void An_agent_reply_shows_the_public_name_the_body_and_the_link()
     {
```

Modify `tests/TechStrap.Infrastructure.IntegrationTests/TicketNotificationPlannerTests.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -106,6 +106,37 @@ public sealed class TicketNotificationPlannerTests(PostgresFixture postgres) : P
     private static Message AgentMessage(PersistenceTestHost host, Seed seed, Agent author) =>
         Message.Create(seed.TicketId, AuthorType.Agent, author.Id, MessageVisibility.Public, "<p>Try again.</p>", host.Clock).Value;
 
+    [Fact]
+    public async Task A_reply_with_linked_articles_puts_each_portal_link_in_the_payload_with_the_tickets_product_key_and_a_trailing_slash_does_not_double()
+    {
+        await using var host = NewHost(extra: new Dictionary<string, string?> { [PortalLinkOptions.PublicUrlKey] = "https://help.test/" });
+        var seed = await SeedAsync(host);
+
+        (await host.CommitAsync(sp => PlanAsync(sp, seed, (planner, ticket, sam, _) =>
+            planner.PlanAgentReplyAsync(
+                ticket, AgentMessage(host, seed, sam), sam, false,
+                [new ReplyArticleLink("Reset your password", "account", "reset-password"), new ReplyArticleLink("Shared tips", "general", "shared-tips")],
+                Ct)))).IsSuccess.ShouldBeTrue();
+
+        var payload = await TextAsync("SELECT payload::text FROM email_outbox");
+        payload.ShouldContain("https://help.test/p/orbitly/kb/account/reset-password");
+        payload.ShouldContain("https://help.test/p/orbitly/kb/general/shared-tips");
+        payload.ShouldNotContain("//p/");
+        payload.ShouldNotContain("nimbus");
+    }
+
+    [Fact]
+    public async Task A_reply_without_articles_queues_a_payload_with_no_article_list()
+    {
+        await using var host = NewHost();
+        var seed = await SeedAsync(host);
+
+        (await host.CommitAsync(sp => PlanAsync(sp, seed, (planner, ticket, sam, _) => planner.PlanAgentReplyAsync(ticket, AgentMessage(host, seed, sam), sam, false, [], Ct))))
+            .IsSuccess.ShouldBeTrue();
+
+        (await TextAsync("SELECT payload::text FROM email_outbox")).ShouldContain("\"articles\": null");
+    }
+
     [Fact]
     public async Task A_reply_queues_one_branded_email_to_the_requester_with_a_fresh_link_and_the_public_name()
     {
```


- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet build TechStrap.slnx -c Release`
Expected: the build FAILS with `CS0246` (`The type or namespace name 'ReplyArticleLink' could not be found`) and `CS1501` (`No overload for method 'PlanAgentReplyAsync' takes 6 arguments`).

Run: `pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1`
Expected: FAIL, 2 failed (`ticks the API tasks P08-T01 to P08-T12 and the roadmap row says the phase is in progress`, `describes the dev knowledge base seed and the reply link rule`).

- [ ] **Step 3: Validate the linked articles in the reply handler**

The check runs before any attachment is saved or any row staged. `IsLinkable` is one expression so each condition has its own test: Published, shared or the ticket's own product, and filed in a category the same product can see (the portal address carries the category slug, so an article with no usable category cannot get a link and is not linkable). The handler also collects each article's title, category slug and slug for the planner, in the order the agent gave.

Modify `src/TechStrap.Application/Tickets/AddAgentReplyRequestHandler.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -23,7 +23,9 @@ public interface IAddAgentReplyRequestHandler
 
 /// <summary>
 /// A public agent reply: Markdown rendered then sanitized, attachments saved (with compensation if anything later fails), linked KB
-/// articles, an optional "send and solve", and the customer email planned in the same unit of work.
+/// articles, an optional "send and solve", and the customer email planned in the same unit of work. A linked article must be Published and
+/// visible to the ticket (shared, or in the ticket's own product) and have a category, because the customer email links to its portal
+/// page; anything else is a 400 <c>kb-article-not-linkable</c> and nothing is stored or sent (D-044).
 /// </summary>
 public sealed class AddAgentReplyRequestHandler(
     ICurrentAgentClaims currentAgent,
@@ -84,6 +86,7 @@ public sealed class AddAgentReplyRequestHandler(
         var (agent, ticket) = loaded.Value;
 
         var linked = new List<LinkedArticleDto>();
+        var emailLinks = new List<ReplyArticleLink>();
         foreach (var articleId in articleIds)
         {
             var article = await kb.GetArticleAsync(articleId, cancellationToken);
@@ -92,7 +95,15 @@ public sealed class AddAgentReplyRequestHandler(
                 return Fail(TicketErrors.Invalid("linkedArticleIds", "article-not-found", "One of the linked articles does not exist. Remove it and try again."));
             }
 
+            var category = article.CategoryId is { } categoryId ? await kb.GetCategoryAsync(categoryId, cancellationToken) : null;
+            if (!IsLinkable(article, category, ticket.ProductId))
+            {
+                return Fail(TicketErrors.Invalid(
+                    "linkedArticleIds", "kb-article-not-linkable", "One of the linked articles is not published for this ticket's product. Remove it and try again."));
+            }
+
             linked.Add(new LinkedArticleDto(article.Id, article.Title, article.Slug));
+            emailLinks.Add(new ReplyArticleLink(article.Title, category!.Slug, article.Slug));
         }
 
         var html = sanitizer.Sanitize(markdown.ToHtml(request.Body ?? string.Empty));
@@ -130,7 +141,7 @@ public sealed class AddAgentReplyRequestHandler(
                 kb.AddTicketArticle(new TicketArticle(ticket.Id, message.Value.Id, articleId));
             }
 
-            await planner.PlanAgentReplyAsync(ticket, message.Value, agent, solve, cancellationToken);
+            await planner.PlanAgentReplyAsync(ticket, message.Value, agent, solve, emailLinks, cancellationToken);
 
             var committed = await TicketMutation.CommitAsync(scope, cancellationToken);
             if (committed.IsFailure)
@@ -159,5 +170,12 @@ public sealed class AddAgentReplyRequestHandler(
         }
     }
 
+    // Published, in the ticket's product or shared, and filed in a category the same product can see (the portal address carries the category slug).
+    private static bool IsLinkable(KbArticle article, KbCategory? category, Guid ticketProductId) =>
+        article.Status == KbArticleStatus.Published
+        && (article.ProductId is null || article.ProductId == ticketProductId)
+        && category is not null
+        && (category.ProductId is null || category.ProductId == ticketProductId);
+
     private static Result<AgentMessageResponse> Fail(ResultError error) => Result<AgentMessageResponse>.Failure(error);
 }
```

- [ ] **Step 4: Carry the links to the customer email**

The payload stays template data (D-033): titles and URLs, never a rendered body, about 4 KB for ten links, well under the 16,000-byte outbox cap. The URL is built when the reply is planned, with the ticket's product key, so a later archive does not change what was promised in an email already queued. The renderer drops anything that is not an absolute web address instead of emailing a broken link.

Modify `src/TechStrap.Application/Email/EmailTemplates.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -17,7 +17,10 @@ public sealed record TicketConfirmationEmail(string TicketNumber, string Subject
 
 /// <summary>Payload of an agent-reply row: no body (D-033, 16 000 cap); the Worker loads the message by id at send time. ReopenDays 0 means a row queued before 06b: the renderer uses TicketNotices.DefaultReopenDays.</summary>
 public sealed record AgentReplyEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink,
-    string AgentPublicName, Guid MessageId, bool Solved, int ReopenDays = 0);
+    string AgentPublicName, Guid MessageId, bool Solved, int ReopenDays = 0, IReadOnlyList<ArticleLinkEntry>? Articles = null);
+
+/// <summary>A KB article the reply links, as it appears in the customer email: its title and the absolute portal URL (D-044). A row queued before PHASE-08 has none.</summary>
+public sealed record ArticleLinkEntry(string Title, string Url);
 
 public sealed record TicketSolvedEmail(string TicketNumber, string Subject, string? RequesterName, string PortalLink, int ReopenDays);
 
```

Modify `src/TechStrap.Application/Intake/PortalLinkOptions.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -8,4 +8,8 @@ public sealed class PortalLinkOptions
     public string PublicUrl { get; set; } = string.Empty;
 
     public string TicketLink(string token) => $"{PublicUrl.TrimEnd('/')}/t/{token}";
+
+    /// <summary>The portal page of a published KB article: <c>{url}/p/{product key}/kb/{category slug}/{article slug}</c> (PHASE-09 route, D-044).</summary>
+    public string ArticleLink(string productKey, string categorySlug, string articleSlug) =>
+        $"{PublicUrl.TrimEnd('/')}/p/{Uri.EscapeDataString(productKey)}/kb/{Uri.EscapeDataString(categorySlug)}/{Uri.EscapeDataString(articleSlug)}";
 }
```

Modify `src/TechStrap.Application/Tickets/Notifications/ITicketNotificationPlanner.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -4,6 +4,9 @@ using TechStrap.Domain.Tickets;
 
 namespace TechStrap.Application.Tickets.Notifications;
 
+/// <summary>A KB article a reply links: the planner turns it into <c>{portal}/p/{product key}/kb/{category}/{slug}</c> with the ticket's own product key.</summary>
+public sealed record ReplyArticleLink(string Title, string CategorySlug, string Slug);
+
 /// <summary>
 /// Decides who is emailed about an agent action and stages outbox rows (template data only, D-033) in the caller's
 /// unit of work. Never throws for an email problem: a skipped or failed notice is logged by code and the action stands.
@@ -12,6 +15,9 @@ public interface ITicketNotificationPlanner
 {
     Task PlanAgentReplyAsync(Ticket ticket, Message message, Agent author, bool solved, CancellationToken cancellationToken);
 
+    /// <summary>The same notice with the linked KB articles the reply carries; each becomes a portal link in the email (D-044). At most <c>TicketOperationLimits.MaxLinkedArticles</c>.</summary>
+    Task PlanAgentReplyAsync(Ticket ticket, Message message, Agent author, bool solved, IReadOnlyList<ReplyArticleLink> articles, CancellationToken cancellationToken);
+
     Task PlanSolvedAsync(Ticket ticket, CancellationToken cancellationToken);
 
     Task PlanAssignedAsync(Ticket ticket, Agent assignee, Agent actor, CancellationToken cancellationToken);
```

Modify `src/TechStrap.Infrastructure/Tickets/TicketNotificationPlanner.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -39,6 +39,10 @@ internal sealed class TicketNotificationPlanner(
     private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);
 
     public Task PlanAgentReplyAsync(Ticket ticket, Message message, Agent author, bool solved, CancellationToken cancellationToken) =>
+        PlanAgentReplyAsync(ticket, message, author, solved, [], cancellationToken);
+
+    public Task PlanAgentReplyAsync(
+        Ticket ticket, Message message, Agent author, bool solved, IReadOnlyList<ReplyArticleLink> articles, CancellationToken cancellationToken) =>
         PlanCustomerAsync(
             ticket,
             EmailTemplates.AgentReply,
@@ -50,7 +54,10 @@ internal sealed class TicketNotificationPlanner(
                 AgentPublicIdentity.Resolve(author, product.Branding.DisplayName),
                 message.Id,
                 solved,
-                autoClose.Value.Days),
+                autoClose.Value.Days,
+                articles.Count == 0
+                    ? null
+                    : [.. articles.Select(article => new ArticleLinkEntry(article.Title, portalOptions.Value.ArticleLink(product.Key, article.CategorySlug, article.Slug)))]),
             cancellationToken);
 
     public Task PlanSolvedAsync(Ticket ticket, CancellationToken cancellationToken) =>
```

Modify `src/TechStrap.Infrastructure/Email/EmailTemplateRenderer.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -4,6 +4,7 @@ using Microsoft.Extensions.Options;
 using TechStrap.Application.Email;
 using TechStrap.Application.Tickets.Notifications;
 using TechStrap.Contracts.Branding;
+using TechStrap.Contracts.Tickets;
 using TechStrap.Domain.Products;
 
 namespace TechStrap.Infrastructure.Email;
@@ -75,6 +76,18 @@ internal sealed class EmailTemplateRenderer(IOptions<EmailBrandingOptions> optio
         text.Append(Greeting(model.RequesterName)).Append("\n\n");
         text.Append($"{model.AgentPublicName} replied:").Append("\n\n");
         text.Append(HtmlText.ToPlainText(messageHtml)).Append("\n\n");
+        var articles = WebArticleLinks(model.Articles);
+        if (articles.Count > 0)
+        {
+            text.Append("Related articles:").Append('\n');
+            foreach (var article in articles)
+            {
+                text.Append($"- {article.Title}: {article.Url}").Append('\n');
+            }
+
+            text.Append('\n');
+        }
+
         if (model.Solved)
         {
             text.Append(solvedLine).Append("\n\n");
@@ -89,6 +102,17 @@ internal sealed class EmailTemplateRenderer(IOptions<EmailBrandingOptions> optio
         body.Append($"<p style=\"{ParagraphStyle}\">{HtmlGreeting(model.RequesterName)}</p>");
         body.Append($"<p style=\"{ParagraphStyle}\">{Encode(model.AgentPublicName)} replied:</p>");
         body.Append($"<div style=\"margin:0 0 16px 0;\">{messageHtml}</div>");
+        if (articles.Count > 0)
+        {
+            body.Append("<p style=\"margin:0 0 8px 0;\">Related articles:</p><ul style=\"margin:0 0 16px 0;padding-left:20px;\">");
+            foreach (var article in articles)
+            {
+                body.Append($"<li><a href=\"{Encode(article.Url)}\" style=\"color:{colors.AccentInk};\">{Encode(article.Title)}</a></li>");
+            }
+
+            body.Append("</ul>");
+        }
+
         if (model.Solved)
         {
             body.Append($"<p style=\"{ParagraphStyle}\">{Encode(solvedLine)}</p>");
@@ -100,6 +124,10 @@ internal sealed class EmailTemplateRenderer(IOptions<EmailBrandingOptions> optio
         return Build(subject, text.ToString(), Layout(branding, colors, body.ToString(), showPoweredBy), branding);
     }
 
+    // At most the reply limit, and only absolute http or https addresses: a link that is not one is dropped rather than emailed broken (D-044).
+    private static List<ArticleLinkEntry> WebArticleLinks(IReadOnlyList<ArticleLinkEntry>? articles) =>
+        [.. (articles ?? []).Where(article => IsWebUrl(article.Url) && !string.IsNullOrWhiteSpace(article.Title)).Take(TicketOperationLimits.MaxLinkedArticles)];
+
     public RenderedEmail RenderTicketSolved(TicketSolvedEmail model, EmailBranding branding)
     {
         var showPoweredBy = options.Value.ShowPoweredBy;
```

- [ ] **Step 5: Seed a published Paperplane article**

The seed marker stays `welcome`; a database seeded before this task keeps its four articles (the seed runs once), which `DEV-DATA.md` says.

Modify `src/TechStrap.Infrastructure/Seeding/DevelopmentDataSeeder.cs` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -179,8 +179,10 @@ public sealed class DevelopmentDataSeeder(
 
         var general = Must(KbCategory.Create(null, "getting-started", "Getting started", 1, clock));
         var account = Must(KbCategory.Create(orbitly.Id, "account", "Account", 2, clock));
+        var guides = Must(KbCategory.Create(paperplane.Id, "guides", "Guides", 3, clock));
         knowledgeBase.AddCategory(general);
         knowledgeBase.AddCategory(account);
+        knowledgeBase.AddCategory(guides);
 
         var welcome = Must(KbArticle.Create(null, general.Id, "welcome", "Welcome to support", "How to reach us.", "# Welcome\n\nUse the contact form and we will reply by email.", sam.Id, clock));
         Must(welcome.Publish(clock));
@@ -189,10 +191,13 @@ public sealed class DevelopmentDataSeeder(
         var export = Must(KbArticle.Create(orbitly.Id, null, "export-csv", "Exporting to CSV", null, "# Exporting\n\nDraft in progress.", riley.Id, clock));
         var pricing = Must(KbArticle.Create(orbitly.Id, null, "old-pricing", "Old pricing", null, "# Pricing\n\nReplaced.", sam.Id, clock));
         Must(pricing.Archive(clock));
+        var darkMode = Must(KbArticle.Create(paperplane.Id, guides.Id, "using-dark-mode", "Using dark mode", "Switch Paperplane to the dark theme.", "# Using dark mode\n\nOpen **Settings**, choose **Appearance**, then **Dark**.", riley.Id, clock));
+        Must(darkMode.Publish(clock));
         knowledgeBase.AddArticle(welcome);
         knowledgeBase.AddArticle(reset);
         knowledgeBase.AddArticle(export);
         knowledgeBase.AddArticle(pricing);
+        knowledgeBase.AddArticle(darkMode);
     }
 
     private async Task<Ticket> NewTicketAsync(Product product, Requester requester, string subject, string firstMessage, CancellationToken cancellationToken)
```

- [ ] **Step 6: Update the developer docs, the phase spec and the roadmap**

`DEV-DATA.md` and `TICKET-OPERATIONS.md` are plain additions (shown below). The phase spec and the roadmap contain non-ASCII characters, so they are changed by a script, as in Task 7: save it outside the repository, run `python docs_task8.py` from the repository root and expect `ok`.

Modify `docs/development/DEV-DATA.md` (each hunk shows its context; apply the `+` and `-` lines):

```diff
@@ -4,6 +4,23 @@ With `ASPNETCORE_ENVIRONMENT=Development` and `TECHSTRAP_SEED_DEV_DATA=true`, th
 requesters, tags, tickets in every status and knowledge base articles. Seeding runs once per database (markers: product
 `orbitly`, shared article `welcome`).
 
+## Dev knowledge base
+
+Three categories (shared `getting-started`, Orbitly `account`, Paperplane `guides`) and five articles. Three are published, so the
+public KB endpoints return something for both products (D-044):
+
+| Article | Product | Category | Status |
+| --- | --- | --- | --- |
+| `welcome` | shared | `getting-started` | Published |
+| `reset-password` | Orbitly | `account` | Published |
+| `using-dark-mode` | Paperplane | `guides` | Published |
+| `export-csv` | Orbitly | none | Draft |
+| `old-pricing` | Orbitly | none | Archived |
+
+For example `GET /api/public/kb/paperplane/categories` lists `getting-started` and `guides` (each with one article), and a reply may link
+any Published article that is shared or belongs to the ticket's product. A database seeded before PHASE-08 keeps its four articles: the seed
+runs once, so start from a fresh development database to get the Paperplane article.
+
 ## Dev API keys
 
 These keys are fake and work only against a database the development seeder filled. Never use them anywhere else.
```

Modify `docs/development/TICKET-OPERATIONS.md` (each hunk shows its context; apply the `+` and `-` lines):

````diff
@@ -48,7 +48,10 @@ Spam tickets appear only in the `spam` view.
 ## Reply and note
 
 A reply is multipart so files can travel with it. The body is Markdown, rendered and sanitized on the server.
-`statusAfter` is empty or `Pending` (the default) or `Solved` ("send and solve"). `linkedArticleIds` may repeat.
+`statusAfter` is empty or `Pending` (the default) or `Solved` ("send and solve"). `linkedArticleIds` may repeat (at most 10).
+Each linked article must be Published and either shared or in the ticket's product, and it must have a category; anything else is a 400
+`kb-article-not-linkable` and nothing is stored or emailed (D-044). The customer email lists each one as
+`{TECHSTRAP_PORTAL_PUBLIC_URL}/p/{product key}/kb/{category slug}/{article slug}`.
 
 ```bash
 curl -s -H "$AUTH" -X POST "$API/api/tickets/$ID/replies" \
````

```python
"""Ticks the API tasks in PHASE-08-knowledge-base.md and moves the roadmap row to In progress (D-044). Run from the repo root."""
import re


def load(path):
    text = open(path, encoding='utf-8', newline='').read()
    return text.replace('\r\n', '\n'), '\r\n' in text


def save(path, text, crlf):
    open(path, 'w', encoding='utf-8', newline='').write(text.replace('\n', '\r\n') if crlf else text)


path = 'docs/architecture/PHASE-08-knowledge-base.md'
text, crlf = load(path)

count = 0


def tick(match):
    global count
    count += 1
    return '- [x] **P08-T' + match.group(1) + '**'


text = re.sub(r'^- \[ \] \*\*P08-T(0[1-9]|1[0-2])\*\*', tick, text, flags=re.MULTILINE)
assert count == 12, count

for old in [
    '- [ ] KB persistence verified',
    '- [ ] `IMarkdownRenderer`, `IHtmlSanitizer`, `IKbImageStore`',
    '- [ ] 12 agent handlers',
    '- [ ] Seed/demo KB data',
    '- [ ] **KB image serving**',
    '- [ ] `ts_headline` on large bodies',
    '- [ ] Per-product vs shared slug collisions',
    '- [ ] Carried forward from the PHASE-03 final review: The category and article product match',
    '- [ ] Carried forward from the PHASE-03 final review: `KbCategory` has no concurrency version',
]:
    assert old in text, old
    text = text.replace(old, old.replace('- [ ]', '- [x]', 1), 1)

slug_old = ('- [x] Per-product vs shared slug collisions need a clear rule (shared wins on conflict at read time; creation blocks duplicates '
            'across both scopes) ' + chr(0x2014) + ' confirm.')
assert slug_old in text
text = text.replace(slug_old, slug_old + ' **Resolved (D-044):** creation blocks a slug used in any scope, for articles and categories.')

pipeline_old = 'Raw HTML in Markdown is disabled in the pipeline (**Assumption**) and the sanitizer is the second line of defense.'
assert pipeline_old in text
text = text.replace(
    pipeline_old,
    pipeline_old + ' The knowledge base has its own profile, `IKbContentRenderer` (pipe tables, `img` and table tags), '
    'so agent replies keep the narrower message pipeline (D-044).')
save(path, text, crlf)

path = 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md'
text, crlf = load(path)
old = '| D-011, D-014, D-021 | Not started |'
assert text.count(old) == 1
text = text.replace(old, '| D-011, D-014, D-021, D-044 | In progress: API tasks T01 to T12 implemented (D-044); editor tasks T13 to T20 pending |')
save(path, text, crlf)
print('ok')
```

`docs/architecture/99-IMPLEMENTATION-ROADMAP.md` is changed by the same script (the phase 08 row: `D-044` added to the decisions and the status `In progress: API tasks T01 to T12 implemented (D-044); editor tasks T13 to T20 pending`).

- [ ] **Step 7: Run the tests and confirm they pass**

Run: `dotnet build TechStrap.slnx -c Release` (0 warnings), then:

```bash
dotnet test --project tests/TechStrap.Application.Tests -c Release --no-build --filter-class "*AddAgentReplyRequestHandlerTests"
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter-class "*AgentReplyIntegrationTests"
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter-class "*TicketNotificationPlannerTests"
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter-class "*EmailTemplateRendererTests"
dotnet test --project tests/TechStrap.Infrastructure.IntegrationTests -c Release --no-build --filter-class "*DevSeederTests"
dotnet test --project tests/TechStrap.Api.Tests -c Release --no-build --filter-class "*AgentReplyEndpointTests"
pwsh -File scripts/Invoke-ScriptTests.ps1
```

Expected: PASS, 36, 5, 32, 26, 13 and 4 tests, and Pester 283.

- [ ] **Step 8: Prove each pin with a mutation**

Applied to the finished code, the named tests run, the change reverted (same method as Task 1).

| Mutation | Failing tests |
| --- | --- |
| `AddAgentReplyRequestHandler.IsLinkable`: `Status == Published` becomes `Published || Draft` | `AddAgentReplyRequestHandlerTests.A_draft_or_archived_article_is_not_linkable_and_nothing_is_stored_or_planned(status: Draft)`; `AgentReplyIntegrationTests.Review_Focus_5_an_unpublished_other_product_or_unfiled_article_is_refused_and_nothing_is_stored_or_emailed` |
| `IsLinkable`: `Status == Published` becomes `!= Draft` (archived leaks) | `AddAgentReplyRequestHandlerTests.A_draft_or_archived_article_is_not_linkable_and_nothing_is_stored_or_planned(status: Archived)` |
| `IsLinkable`: delete the `article.ProductId is null || article.ProductId == ticketProductId` condition | `AddAgentReplyRequestHandlerTests.An_article_of_another_product_is_not_linkable_but_the_tickets_own_product_and_shared_articles_are`; `AgentReplyIntegrationTests.Review_Focus_5_an_unpublished_other_product_or_unfiled_article_is_refused_and_nothing_is_stored_or_emailed` |
| `IsLinkable`: delete the two category conditions | `AddAgentReplyRequestHandlerTests.An_article_with_no_category_or_a_category_of_another_product_cannot_get_a_portal_link_so_it_is_not_linkable`; `AgentReplyIntegrationTests.Review_Focus_5_an_unpublished_...` |
| `TicketNotificationPlanner`: `ArticleLink(product.Key, ...)` becomes `ArticleLink("wrong", ...)` | `TicketNotificationPlannerTests.A_reply_with_linked_articles_puts_each_portal_link_in_the_payload_with_the_tickets_product_key_and_a_trailing_slash_does_not_double`; `AgentReplyIntegrationTests.Review_Focus_5_shared_and_own_product_articles_link_and_the_customer_email_carries_one_portal_link_for_each_with_the_tickets_product_key` |
| `PortalLinkOptions.ArticleLink`: `PublicUrl.TrimEnd('/')` becomes `PublicUrl` (a trailing slash doubles) | `TicketNotificationPlannerTests.A_reply_with_linked_articles_puts_each_portal_link_...` |
| `EmailTemplateRenderer.WebArticleLinks`: drop the `IsWebUrl` condition | `EmailTemplateRendererTests.An_article_title_is_escaped_and_a_link_that_is_not_an_absolute_web_address_is_dropped_so_no_broken_link_is_emailed` |
| `EmailTemplateRenderer`: the list item title is not encoded | `EmailTemplateRendererTests.An_article_title_is_escaped_and_a_link_that_is_not_an_absolute_web_address_is_dropped_so_no_broken_link_is_emailed` |

- [ ] **Step 9: Whole-project check**

Run, from a clean build, the whole phase check:

```bash
dotnet build TechStrap.slnx -c Release --no-incremental
dotnet test --solution TechStrap.CI.slnf -c Release
pwsh -File scripts/Invoke-ScriptTests.ps1
dotnet ef migrations has-pending-model-changes --project src/TechStrap.Infrastructure --startup-project src/TechStrap.Api -c TechStrapDbContext
```

Expected: 0 warnings and 0 errors; 4429 tests passed (the scratch copy of Tasks 1 to 8 gave this; an Admin task adds its own); Pester 283 passed; `No changes have been made to the model since the last migration.` Rebuild with `--no-incremental` before this final run: a mutation leaves a stale DLL that a normal build can miss.

- [ ] **Step 10: Commit**

```bash
git add src \
  tests \
  docs/development \
  docs/architecture \
  scripts/tests
git diff --cached --stat
git commit -m "feat(kb): validate and email linked articles, and seed a published Paperplane article" -m "A reply may link only Published articles that are shared or in the ticket's product and filed in a category that product can see; anything else is a 400 kb-article-not-linkable and nothing is stored or emailed. The customer email lists each linked article as {portal}/p/{product key}/kb/{category}/{slug} (the ticket's product key, titles encoded, links that are not absolute web addresses dropped, at most ten). The dev seed gets a Paperplane category and a published Paperplane article, so two products and the shared space each have published content. The API tasks P08-T01 to P08-T12 are ticked." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

---

### Task 9: The knowledge base client, the article list, the rail link and the palette command (P08-T13, P08-T14 client half, P08-T15)

**Review Focus pins:**
- **(4)** The version travels with every write, so publishing or archiving an article another agent has changed is a 409 and never a silent overwrite: `KbClientTests.Publish_and_archive_post_to_their_routes_with_no_body_and_an_incomplete_publish_names_its_field` (the `?version=` query), `Update_sends_the_version_and_a_stale_version_is_a_conflict_that_is_not_retried`. A list answer that was overtaken by a newer filter never replaces the newer list: `KbArticleListPageTests.A_load_that_was_overtaken_by_a_newer_filter_never_replaces_the_newer_list`.
- **The gate and the token.** The list is for every agent, and every call it makes carries the agent's token: `KbListHostTests` (each test that reaches the API ends in `AssertEveryCallBore`), and a user the API refuses reaches nothing but `/api/agents/me`.

**Files:**
- Modify: `src/TechStrap.Admin/Clients/ApiClientRegistration.cs`
- Modify: `src/TechStrap.Admin/Clients/ApiErrorCodes.cs`
- Modify: `src/TechStrap.Admin/Clients/ApiFields.cs`
- Create: `src/TechStrap.Admin/Clients/KbClient.cs`
- Modify: `src/TechStrap.Admin/Components/Layout/NavMenu.razor`
- Modify: `src/TechStrap.Admin/Components/Ui/ShellCopy.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbArticleListPage.razor`
- Create: `src/TechStrap.Admin/Features/Kb/KbArticleListPage.razor.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbArticleRowViewModel.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbCopy.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbDefaults.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbStatusBadge.razor`
- Create: `src/TechStrap.Admin/Features/Kb/_Imports.razor`
- Modify: `src/TechStrap.Admin/Features/Shell/CommandRegistry.cs`
- Create: `src/TechStrap.Admin/Styles/_kb.scss`
- Modify: `src/TechStrap.Admin/Styles/app.scss`
- Create: `tests/TechStrap.Admin.Tests/Clients/KbClientTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Clients/KbErrorCodesTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/CommandPaletteTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/CommandRegistryTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/KbArticleListPageTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/KbListFilterTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/KbStatusBadgeTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/LayoutResilienceTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/Components/NavMenuTests.cs`
- Create: `tests/TechStrap.Admin.Tests/KbListHostTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/ScrollRegionSiteTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Support/CountingTimeProvider.cs`
- Create: `tests/TechStrap.Admin.Tests/Support/TestData.Kb.cs`

**Interfaces:**
- Consumes:
  - **Contracts from Task 1** (`src/TechStrap.Contracts/Kb/`): `KbArticleDto`, `KbArticleListItemDto`, `CreateKbArticleRequest`, `UpdateKbArticleRequest`, `ListKbArticlesRequest(ProductId, SharedOnly, IncludeShared, Status, CategoryId, Text, Page, PageSize)`, `KbPreviewRequest`, `KbPreviewResponse`, `KbImageUploadResponse`, `KbCategoryDto`, `CreateKbCategoryRequest`, `UpdateKbCategoryRequest`, `KbArticleStatuses`, `KbLimits.ImageFieldName`. The existing `PagedResponse<T>`.
  - **Routes from Tasks 3 to 7** (Agent policy): `GET/POST api/kb/articles`, `GET/PUT api/kb/articles/{id}`, `POST api/kb/articles/{id}/publish?version=` and `/archive?version=` (both answer the article, 200), `POST api/kb/preview`, `POST api/kb/images` (multipart, field `file`), `GET/POST api/kb/categories`, `PUT/DELETE api/kb/categories/{id}`. Error codes and field targets as in Task 1: `kb-slug-taken`, `kb-category-slug-taken`, `kb-category-reserved-slug`, `kb-category-in-use`, `kb-category-scope-mismatch`, `kb-article-not-found`, `kb-category-not-found`, `kb-publish-incomplete`, `kb-image-type-not-allowed`, `kb-image-too-large`, `kb-article-not-linkable`; the list takes `includeShared` (default true) and `sharedOnly`.
  - **The Admin as it is after 07c:** `ApiConnection` (`GetAsync`, `SendAsync`, `SendContentAsync`), `ApiUri.Build`, `ProductsClient.Narrow`, `AttachmentFileName.Clean`, `ApiClientRegistration`, `ApiErrorCodes`, `ApiFields`, `ShellCopy`, `NavMenu`, `RailLink`, `CommandRegistry`, `ScrollRegion`, `PagerControl`, `LoadingState`, `ErrorState`, `EmptyState`, `RelativeTime`, and the test bases `AdminComponentTest` (the fake clock `Time`, strict JS modules), `ApiHarness`, `AdminFactory`, `StubApiHandler`, `AssertEveryCallBore`.
  - **The 07c lessons** (Global Constraints): `_loadId` stale guards, the `UncertainMarks` and `WriteOutcomes.Classify` family for writes (Tasks 10 and 11 use them), `CancellationToken.None` for writes.
- Produces:
  - `IKbClient` and `KbClient` with `ListAsync`, `GetAsync`, `CreateAsync`, `UpdateAsync`, `PublishAsync(id, version)`, `ArchiveAsync(id, version)`, `PreviewAsync`, `UploadImageAsync(KbImageFile)`, `ListCategoriesAsync`, `CreateCategoryAsync`, `UpdateCategoryAsync`, `DeleteCategoryAsync`; `KbImageFile(FileName, ContentType, OpenRead)`; the registration in `AddTechStrapApiClients`.
  - `ApiErrorCodes.Kb*` (the eleven codes of Task 1), `ApiErrorCodes.ArticleNotFound` (a reply that links an unknown article id) and `ApiErrorCodes.RequestTooLarge` (Kestrel's 413 problem type); `ApiFields.Title`, `Summary`, `Body`, `Category`, `CategoryId`, `Description`, `SortOrder`, `File` (the field targets the API sends: a publish check says `category`, a create or update says the request property `categoryId`).
  - `KbDefaults` (page sizes, the three 300 ms debounces, `Statuses`, `CanonicalStatus`, `ImageExtensions`), `KbQueryKeys`, `KbListFilter` (the one value the address, the selects and the API request share), `KbCopy`, `KbArticleRowViewModel` (with `ProductNameOf`), `KbStatusBadge`, `KbArticleListPage` at `/kb`.
  - `ShellCopy.KbLink`, the rail link `/kb` for every agent, the palette command `go-kb`.
  - Test support: `TestData.Kb*` builders and `CountingTimeProvider` (counts live timers, so "released with the component" is a number and not a guess).

**Rules:**
1. **One client.** `KbClient` is the only place the Admin talks to the KB API. Reads go through the retrying read client; every write (including `PreviewAsync`, which is a POST) goes through the write client and is never retried. A caller that cancels (a superseded preview) gets `OperationCanceledException`, never a `Result`.
2. **Query string.** `ListAsync` always sends `page` and `pageSize`; `sharedOnly` only when true; `includeShared` is always sent with a product (the API defaults it to true, so a product filter says `false` out loud when it means that product's own articles); every other filter only when set; the search text is trimmed and a blank one is left out.
3. **The version travels.** Update, publish and archive send the version the article was loaded with (publish and archive as `?version=`), so a stale write is `concurrency-conflict` (`WriteOutcome.Conflict`).
4. **Upload.** `UploadImageAsync` sends multipart with the file in the part named `KbLimits.ImageFieldName` (`file`), the content type the browser gave and the file name cleaned by `AttachmentFileName.Clean`. The stream is opened when the request is built and closed when it has been sent, so the same `KbImageFile` can be sent again.
5. **The list.** Filters (product including "Shared", category, status, search text) and the page are the address, parsed defensively (an unknown status, a bad id and a bad page are dropped, never sent). A select commits at once and replaces the history entry; the search box waits 300 ms (`TimeProvider` timer; Enter commits at once); a page change adds a history entry. Product filter means that product's own articles; "Shared" is its own choice (`product=shared`).
6. **Loads.** Every load takes the next `_loadId`; only the latest may change the screen. A failed refresh keeps the list on screen under the error; a page past the end goes to the last real page (replacing the history entry); a lookup (products, categories) that fails only empties its filter and a row shows a fixed phrase ("Another product") for a name it cannot resolve, never the id. The timer is released when the page goes.
7. **Words, not colour.** The status is the word itself (`KbStatusBadge`); a status the Admin does not know is shown as the API sent it, in the plain style.
8. **The rail and the palette.** "Knowledge base" is in the rail for every agent (between Queue and the admin group) and in the palette as `go-kb`, group "Go to", before "My settings". Every rail and palette test that counts links or commands is updated in the same commit.
9. **Tables.** The list table sits directly inside a `ScrollRegion` and carries `role="table"` (`ScrollRegionSiteTests` now expects 8 ledger tables).

- [ ] **Step 1: Write the failing tests**

The client tests first (they read like a spec of the wire), then the pure tests, the page tests and the host tests. `TestData.Kb.cs` and `CountingTimeProvider` are shared by Tasks 10 and 11.


`tests/TechStrap.Admin.Tests/Clients/KbClientTests.cs`

```csharp
using System.Net;
using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>The knowledge base client over the real handler pipeline and a stub API: the routes, the bodies, the multipart upload and the rule that a write is never retried.</summary>
public sealed class KbClientTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid ArticleId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private static KbArticleDto Article(uint version = 3, string status = KbArticleStatuses.Draft) =>
        new(ArticleId, ProductId, CategoryId, "reset-password", "Reset your password", "How to reset it.", "# Steps", status, Guid.NewGuid(), Now, Now, null, version);

    private static KbCategoryDto Category(uint version = 1) => new(CategoryId, null, "getting-started", "Getting started", null, 10, version);

    private static async Task<(ApiHarness Api, IKbClient Client)> StartAsync()
    {
        var api = await ApiHarness.CreateAsync();
        return (api, api.Get<IKbClient>());
    }

    private static JsonElement Body(ApiHarness api) => JsonDocument.Parse(api.Stub.Requests.Last().Body!).RootElement;

    [Fact]
    public async Task List_sends_the_filters_and_the_paging_and_leaves_blank_filters_out()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, "/api/kb/articles", new PagedResponse<KbArticleListItemDto>([], 2, 25, 0));

        var result = await client.ListAsync(new ListKbArticlesRequest(ProductId, SharedOnly: false, IncludeShared: true, KbArticleStatuses.Published, CategoryId, "reset password", 2, 25), Ct);

        result.IsSuccess.ShouldBeTrue();
        api.Stub.Requests.ShouldHaveSingleItem().Query.ShouldBe($"?productId={ProductId}&includeShared=true&status=Published&categoryId={CategoryId}&text=reset%20password&page=2&pageSize=25");

        await client.ListAsync(new ListKbArticlesRequest(null, SharedOnly: true, IncludeShared: false, null, null, " ", 1, 10), Ct);
        api.Stub.Requests.Last().Query.ShouldBe("?sharedOnly=true&page=1&pageSize=10");

        // The API defaults includeShared to true, so a product filter says false out loud: it means that product's own articles.
        await client.ListAsync(new ListKbArticlesRequest(ProductId, SharedOnly: false, IncludeShared: false, null, null, null, 1, 25), Ct);
        api.Stub.Requests.Last().Query.ShouldBe($"?productId={ProductId}&includeShared=false&page=1&pageSize=25");
    }

    [Fact]
    public async Task Get_returns_the_article_with_its_version_and_a_404_keeps_the_article_not_found_code()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, $"/api/kb/articles/{ArticleId}", Article(version: 7));

        (await client.GetAsync(ArticleId, Ct)).Value.Version.ShouldBe(7u);

        api.Stub.OnProblem(HttpMethod.Get, $"/api/kb/articles/{ArticleId}", HttpStatusCode.NotFound, ApiErrorCodes.KbArticleNotFound, "No such article.");
        (await client.GetAsync(ArticleId, Ct)).Errors.ShouldHaveSingleItem()
            .ShouldBe(new ResultError(ApiErrorCodes.KbArticleNotFound, "No such article.", ResultErrorKind.NotFound));
    }

    [Fact]
    public async Task Create_posts_the_request_and_a_taken_slug_is_a_conflict_with_its_code()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/articles", Article(), HttpStatusCode.Created);

        var created = await client.CreateAsync(new CreateKbArticleRequest(ProductId, CategoryId, "reset-password", "Reset your password", "How to reset it.", "# Steps"), Ct);

        created.Value.Slug.ShouldBe("reset-password");
        var body = Body(api);
        body.GetProperty("productId").GetGuid().ShouldBe(ProductId);
        body.GetProperty("slug").GetString().ShouldBe("reset-password");
        body.GetProperty("bodyMarkdown").GetString().ShouldBe("# Steps");

        api.Stub.OnProblem(HttpMethod.Post, "/api/kb/articles", HttpStatusCode.Conflict, ApiErrorCodes.KbSlugTaken, "That slug is taken.");
        (await client.CreateAsync(new CreateKbArticleRequest(null, null, "reset-password", "T", "S", "B"), Ct)).Errors[0].Code.ShouldBe(ApiErrorCodes.KbSlugTaken);
    }

    // Review Focus 4: the version the article was loaded with always travels, so a stale save is a 409 and never an overwrite.
    [Fact]
    public async Task Update_sends_the_version_and_a_stale_version_is_a_conflict_that_is_not_retried()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Put, $"/api/kb/articles/{ArticleId}", Article(version: 4));

        var saved = await client.UpdateAsync(ArticleId, new UpdateKbArticleRequest(CategoryId, "Reset your password", "How to reset it.", "# Steps", Version: 3), Ct);

        saved.Value.Version.ShouldBe(4u);
        Body(api).GetProperty("version").GetUInt32().ShouldBe(3u);
        Body(api).GetProperty("title").GetString().ShouldBe("Reset your password");

        api.Stub.OnProblem(HttpMethod.Put, $"/api/kb/articles/{ArticleId}", HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict, "This article changed since you opened it.");
        var stale = await client.UpdateAsync(ArticleId, new UpdateKbArticleRequest(CategoryId, "T", "S", "B", Version: 3), Ct);

        WriteOutcomes.Classify(stale.Errors[0]).ShouldBe(WriteOutcome.Conflict);
        api.Stub.Count(HttpMethod.Put, $"/api/kb/articles/{ArticleId}").ShouldBe(2);
    }

    [Fact]
    public async Task Publish_and_archive_post_to_their_routes_with_no_body_and_an_incomplete_publish_names_its_field()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish", Article(version: 4, status: KbArticleStatuses.Published));
        api.Stub.OnJson(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/archive", Article(version: 5, status: KbArticleStatuses.Archived));

        (await client.PublishAsync(ArticleId, 3, Ct)).Value.Status.ShouldBe(KbArticleStatuses.Published);
        (await client.ArchiveAsync(ArticleId, 4, Ct)).Value.Version.ShouldBe(5u);

        // Review Focus 4: the version the article was loaded with travels in the query string, so a stale publish is a 409.
        api.Stub.Requests.Select(r => (r.Method, r.Path, r.Query, r.Body)).ShouldBe(
        [
            (HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish", "?version=3", (string?)null),
            (HttpMethod.Post, $"/api/kb/articles/{ArticleId}/archive", "?version=4", null),
        ]);

        api.Stub.OnProblem(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish", HttpStatusCode.Conflict, ApiErrorCodes.ConcurrencyConflict, "This article changed.");
        WriteOutcomes.Classify((await client.PublishAsync(ArticleId, 2, Ct)).Errors[0]).ShouldBe(WriteOutcome.Conflict);

        api.Stub.On(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish", _ => StubApiHandler.ValidationProblem(ApiFields.Category, ApiErrorCodes.KbPublishIncomplete, "Choose a category before publishing."));
        var incomplete = await client.PublishAsync(ArticleId, 3, Ct);

        incomplete.Errors[0].Code.ShouldBe(ApiErrorCodes.KbPublishIncomplete);
        incomplete.Errors[0].Target.ShouldBe(ApiFields.Category);
    }

    [Fact]
    public async Task Preview_posts_the_markdown_to_the_preview_route_and_returns_the_html_unchanged()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/preview", new KbPreviewResponse("<p>Hi</p>"));

        var result = await client.PreviewAsync(new KbPreviewRequest("Hi"), Ct);

        result.Value.Html.ShouldBe("<p>Hi</p>");
        Body(api).GetProperty("bodyMarkdown").GetString().ShouldBe("Hi");
    }

    [Fact]
    public async Task A_superseded_preview_is_cancelled_by_its_caller_and_never_turned_into_a_result()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/preview", new KbPreviewResponse("<p>x</p>"));
        using var canceled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => client.PreviewAsync(new KbPreviewRequest("Hi"), cancelled.Token));
    }

    [Fact]
    public async Task Upload_sends_multipart_with_the_file_in_the_part_named_file_and_a_cleaned_file_name()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/images", new KbImageUploadResponse("kb-images/abc.png", "https://api.example/kb-images/abc.png"), HttpStatusCode.Created);

        var result = await client.UploadImageAsync(new KbImageFile("dir/\"shot\".png", "image/png", () => new MemoryStream([0x89, 0x50, 0x4E, 0x47])), Ct);

        result.Value.Url.ShouldBe("https://api.example/kb-images/abc.png");
        var request = api.Stub.Requests.ShouldHaveSingleItem();
        request.ContentType!.ShouldStartWith("multipart/form-data");
        var body = request.Body!;
        body.ShouldContain("name=file");
        body.ShouldContain("filename=dirshot.png");
        body.ShouldContain("Content-Type: image/png");
    }

    [Theory]
    [InlineData(ApiErrorCodes.KbImageTypeNotAllowed)]
    [InlineData(ApiErrorCodes.KbImageTooLarge)]
    public async Task An_image_the_api_refuses_keeps_its_code(string code)
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.On(HttpMethod.Post, "/api/kb/images", _ => StubApiHandler.ValidationProblem(ApiFields.File, code, "Not that image."));

        var result = await client.UploadImageAsync(new KbImageFile("a.png", "image/png", () => new MemoryStream([1])), Ct);

        result.Errors[0].Code.ShouldBe(code);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task A_write_that_gets_an_unknown_answer_is_one_call_and_an_uncertain_write(HttpStatusCode status)
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnStatus(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish", status);

        var result = await client.PublishAsync(ArticleId, 3, Ct);

        api.Stub.Count(HttpMethod.Post, $"/api/kb/articles/{ArticleId}/publish").ShouldBe(1);
        ApiErrorCodes.IsUncertainWrite(result.Errors[0].Code).ShouldBeTrue();
    }

    [Fact]
    public async Task Categories_are_listed_created_updated_and_deleted_on_their_routes()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)[Category()]);
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/categories", Category(), HttpStatusCode.Created);
        api.Stub.OnJson(HttpMethod.Put, $"/api/kb/categories/{CategoryId}", Category(version: 2));
        api.Stub.OnStatus(HttpMethod.Delete, $"/api/kb/categories/{CategoryId}", HttpStatusCode.NoContent);

        (await client.ListCategoriesAsync(Ct)).Value.ShouldHaveSingleItem().Slug.ShouldBe("getting-started");
        (await client.CreateCategoryAsync(new CreateKbCategoryRequest(null, "getting-started", "Getting started", null, 10), Ct)).IsSuccess.ShouldBeTrue();
        Body(api).GetProperty("slug").GetString().ShouldBe("getting-started");

        (await client.UpdateCategoryAsync(CategoryId, new UpdateKbCategoryRequest("Getting started", null, 20, Version: 1), Ct)).Value.Version.ShouldBe(2u);
        Body(api).GetProperty("version").GetUInt32().ShouldBe(1u);
        Body(api).GetProperty("sortOrder").GetInt32().ShouldBe(20);

        (await client.DeleteCategoryAsync(CategoryId, Ct)).IsSuccess.ShouldBeTrue();
        api.Stub.Requests.Last().Method.ShouldBe(HttpMethod.Delete);
    }

    [Fact]
    public async Task A_category_that_still_holds_articles_is_a_conflict_with_the_in_use_code()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnProblem(HttpMethod.Delete, $"/api/kb/categories/{CategoryId}", HttpStatusCode.Conflict, ApiErrorCodes.KbCategoryInUse, "Move the 3 articles first.");

        var result = await client.DeleteCategoryAsync(CategoryId, Ct);

        result.Errors.ShouldHaveSingleItem().ShouldBe(new ResultError(ApiErrorCodes.KbCategoryInUse, "Move the 3 articles first.", ResultErrorKind.Conflict));
    }

    [Fact]
    public async Task Every_call_carries_the_agents_bearer_token()
    {
        var (api, client) = await StartAsync();
        await using var _ = api;
        api.Stub.OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)[]);
        api.Stub.OnJson(HttpMethod.Post, "/api/kb/preview", new KbPreviewResponse(string.Empty));

        await client.ListCategoriesAsync(Ct);
        await client.PreviewAsync(new KbPreviewRequest("x"), Ct);

        api.Stub.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }
}
```

`tests/TechStrap.Admin.Tests/Clients/KbErrorCodesTests.cs`

```csharp
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;

namespace TechStrap.Admin.Tests.Clients;

/// <summary>The codes and field names the knowledge base screens branch on are the API's wire strings. A typo would make a screen treat a known refusal as a generic failure.</summary>
public sealed class KbErrorCodesTests
{
    [Theory]
    [InlineData(ApiErrorCodes.KbSlugTaken, "kb-slug-taken")]
    [InlineData(ApiErrorCodes.KbCategorySlugTaken, "kb-category-slug-taken")]
    [InlineData(ApiErrorCodes.KbCategoryReservedSlug, "kb-category-reserved-slug")]
    [InlineData(ApiErrorCodes.KbCategoryInUse, "kb-category-in-use")]
    [InlineData(ApiErrorCodes.KbCategoryScopeMismatch, "kb-category-scope-mismatch")]
    [InlineData(ApiErrorCodes.KbArticleNotFound, "kb-article-not-found")]
    [InlineData(ApiErrorCodes.KbCategoryNotFound, "kb-category-not-found")]
    [InlineData(ApiErrorCodes.KbPublishIncomplete, "kb-publish-incomplete")]
    [InlineData(ApiErrorCodes.KbImageTypeNotAllowed, "kb-image-type-not-allowed")]
    [InlineData(ApiErrorCodes.KbImageTooLarge, "kb-image-too-large")]
    [InlineData(ApiErrorCodes.KbArticleNotLinkable, "kb-article-not-linkable")]
    [InlineData(ApiErrorCodes.ArticleNotFound, "article-not-found")]
    [InlineData(ApiErrorCodes.RequestTooLarge, "request-too-large")]
    public void The_knowledge_base_codes_are_the_api_wire_codes(string constant, string wire) => constant.ShouldBe(wire);

    [Theory]
    [InlineData(ApiErrorCodes.KbSlugTaken)]
    [InlineData(ApiErrorCodes.KbCategorySlugTaken)]
    [InlineData(ApiErrorCodes.KbCategoryInUse)]
    [InlineData(ApiErrorCodes.KbPublishIncomplete)]
    [InlineData(ApiErrorCodes.KbArticleNotLinkable)]
    public void A_refusal_is_never_an_uncertain_write_and_is_classified_as_other(string code)
    {
        ApiErrorCodes.IsUncertainWrite(code).ShouldBeFalse();
        WriteOutcomes.Classify(new ResultError(code, "m", ResultErrorKind.Conflict)).ShouldBe(WriteOutcome.Other);
    }

    [Fact]
    public void The_knowledge_base_field_targets_are_the_kebab_case_names_the_api_sends()
    {
        string[] targets = [ApiFields.Title, ApiFields.Summary, ApiFields.Body, ApiFields.Category, ApiFields.Description, ApiFields.SortOrder, ApiFields.File];

        targets.ShouldBe(["title", "summary", "body", "category", "description", "sort-order", "file"]);
    }

    [Fact]
    public void A_category_error_on_a_request_is_named_by_the_request_property_and_a_publish_error_by_the_field()
    {
        ApiFields.CategoryId.ShouldBe("categoryId");
        ApiFields.Category.ShouldBe("category");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/CommandPaletteTests.cs`

Replace (edit 1 of 3)

```csharp
        [
            "Queue: Unassigned", "Queue: Mine", "Queue: Open", "Queue: Pending", "Queue: All", "Queue: Spam",
            "My settings",
        ]);
        var palette = cut.Find("dialog[data-palette]").InnerHtml;
```

with

```csharp
        [
            "Queue: Unassigned", "Queue: Mine", "Queue: Open", "Queue: Pending", "Queue: All", "Queue: Spam",
            "Knowledge base", "My settings",
        ]);
        var palette = cut.Find("dialog[data-palette]").InnerHtml;
```

Replace (edit 2 of 3)

```csharp
        Options(cut).ShouldContain("Products");
        Options(cut).ShouldContain("Failed emails");
        Options(cut).Count.ShouldBe(12);
        cut.FindAll(".ts-palette-group").Select(g => g.TextContent).Distinct().ShouldBe(["Go to", "Admin"]);
    }
```

with

```csharp
        Options(cut).ShouldContain("Products");
        Options(cut).ShouldContain("Failed emails");
        Options(cut).Count.ShouldBe(13);
        cut.FindAll(".ts-palette-group").Select(g => g.TextContent).Distinct().ShouldBe(["Go to", "Admin"]);
    }
```

Replace (edit 3 of 3)

```csharp
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 2));
        cut.Find("dialog[data-palette] input").GetAttribute("value").ShouldBeNullOrEmpty();
        Options(cut).Count.ShouldBe(7);
    }
```

with

```csharp
        cut.WaitForAssertion(() => Dialogs.VerifyInvoke("open", 2));
        cut.Find("dialog[data-palette] input").GetAttribute("value").ShouldBeNullOrEmpty();
        Options(cut).Count.ShouldBe(8);
    }
```

`tests/TechStrap.Admin.Tests/Components/CommandRegistryTests.cs`

Replace

```csharp
        [
            "Queue: Unassigned", "Queue: Mine", "Queue: Open", "Queue: Pending", "Queue: All", "Queue: Spam",
            "My settings",
        ]);
        Registry.Available(isAdmin: false).ShouldAllBe(c => !c.AdminOnly);
```

with

```csharp
        [
            "Queue: Unassigned", "Queue: Mine", "Queue: Open", "Queue: Pending", "Queue: All", "Queue: Spam",
            "Knowledge base", "My settings",
        ]);
        Registry.Available(isAdmin: false).ShouldAllBe(c => !c.AdminOnly);
```

`tests/TechStrap.Admin.Tests/Components/KbArticleListPageTests.cs`

```csharp
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The article list: filters in the URL, one load per filter, the newest answer wins, and every empty and failed state (PHASE-08 T15).</summary>
public sealed class KbArticleListPageTests : AdminComponentTest
{
    private static readonly Guid PaperplaneId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly NavigationManager _navigation;
    private readonly CountingTimeProvider _timers;

    public KbArticleListPageTests()
    {
        _timers = new CountingTimeProvider(Time);
        Services.AddSingleton<TimeProvider>(_timers);
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.KbPage(
        [
            TestData.KbItem("Reset your password", "reset-password", KbArticleStatuses.Published, TestData.OrbitlyId, TestData.AccountCategoryId, id: TestData.ArticleId),
            TestData.KbItem("Welcome", "welcome", KbArticleStatuses.Draft, productId: null, categoryId: null),
        ])));
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<KbCategoryDto>>(
            [TestData.KbCategory("Account", "account", TestData.OrbitlyId, TestData.AccountCategoryId)]));
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product("Orbitly"), TestData.Product("Paperplane", PaperplaneId)]));
        Services.AddSingleton(_kb);
        Services.AddSingleton(_products);
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private IRenderedComponent<KbArticleListPage> RenderList(string query = "")
    {
        _navigation.NavigateTo($"/kb{query}");
        return Render<KbArticleListPage>();
    }

    private IEnumerable<ListKbArticlesRequest> Requests() =>
        _kb.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IKbClient.ListAsync)).Select(call => (ListKbArticlesRequest)call.GetArguments()[0]!);

    // ---- the list ------------------------------------------------------------------------------------------------

    [Fact]
    public void The_first_load_asks_for_page_one_of_25_with_no_filter_and_reads_the_lookups_once()
    {
        RenderList();

        var request = Requests().Single();
        request.Page.ShouldBe(1);
        request.PageSize.ShouldBe(25);
        request.ProductId.ShouldBeNull();
        request.SharedOnly.ShouldBeFalse();
        request.Status.ShouldBeNull();
        request.Text.ShouldBeNull();
        _products.Received(1).ListAsync(Arg.Any<CancellationToken>());
        _kb.Received(1).ListCategoriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Each_row_links_to_its_editor_and_says_its_product_its_category_and_its_status_in_words()
    {
        var cut = RenderList();

        var rows = cut.FindAll("tbody tr");
        rows.Count.ShouldBe(2);
        var first = rows.Single(r => r.GetAttribute("data-article") == "reset-password");
        first.QuerySelector("a")!.GetAttribute("href").ShouldBe($"/kb/{TestData.ArticleId}");
        first.QuerySelector("a")!.TextContent.ShouldBe("Reset your password");
        first.Children[1].TextContent.ShouldBe("Orbitly");
        first.Children[2].TextContent.ShouldBe("Account");
        first.QuerySelector(".ts-pill")!.TextContent.ShouldBe("Published");
        var second = rows.Single(r => r.GetAttribute("data-article") == "welcome");
        second.Children[1].TextContent.ShouldBe("Shared");
        second.Children[2].TextContent.ShouldBe("No category");
        second.QuerySelector(".ts-pill")!.TextContent.ShouldBe("Draft");
    }

    [Fact]
    public void A_product_the_lookups_did_not_return_shows_a_fixed_phrase_and_never_its_id()
    {
        var unknown = Guid.NewGuid();
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([TestData.KbItem(productId: unknown)])));

        var cut = RenderList();

        cut.Find("tbody tr").Children[1].TextContent.ShouldBe("Another product");
        cut.Markup.ShouldNotContain(unknown.ToString());
    }

    [Fact]
    public void The_table_sits_in_a_named_scroll_region_and_the_pager_shows_the_total()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([TestData.KbItem()], page: 2, total: 60)));

        var cut = RenderList("?page=2");

        cut.Find("div.ts-scroll").GetAttribute("aria-label").ShouldBe("Articles");
        cut.Find(".ts-pager-summary").TextContent.ShouldContain("of 60");
    }

    // ---- filters in the URL --------------------------------------------------------------------------------------

    [Fact]
    public void The_filters_in_the_query_string_become_the_request_and_an_unknown_status_is_dropped()
    {
        var categoryId = Guid.NewGuid();

        RenderList($"?product={PaperplaneId}&category={categoryId}&status=published&search=refund%20policy&page=3");

        var request = Requests().Single();
        request.ProductId.ShouldBe(PaperplaneId);
        request.CategoryId.ShouldBe(categoryId);
        request.Status.ShouldBe("Published");
        request.Text.ShouldBe("refund policy");
        request.Page.ShouldBe(3);

        _kb.ClearReceivedCalls();
        _navigation.NavigateTo("/kb?status=Banana&product=not-a-guid&page=zero");
        Requests().Single().ShouldBe(new ListKbArticlesRequest(null, false, false, null, null, null, 1, 25));
    }

    [Fact]
    public void The_shared_choice_asks_for_the_shared_articles_only()
    {
        RenderList("?product=shared");

        var request = Requests().Single();
        request.SharedOnly.ShouldBeTrue();
        request.ProductId.ShouldBeNull();
    }

    [Fact]
    public void Choosing_a_product_a_status_or_shared_goes_to_page_one_with_that_filter_in_the_address()
    {
        var cut = RenderList("?page=2");
        _kb.ClearReceivedCalls();

        cut.Find("#kb-product").Change(PaperplaneId.ToString());
        cut.WaitForAssertion(() => Requests().Select(r => r.ProductId).ShouldBe([PaperplaneId]));
        _navigation.Uri.ShouldEndWith($"/kb?product={PaperplaneId}");

        cut.Find("#kb-product").Change("shared");
        cut.WaitForAssertion(() => Requests().Last().SharedOnly.ShouldBeTrue());
        _navigation.Uri.ShouldEndWith("/kb?product=shared");

        cut.Find("#kb-status").Change("Archived");
        cut.WaitForAssertion(() => Requests().Last().Status.ShouldBe("Archived"));
        _navigation.Uri.ShouldContain("status=Archived");
    }

    [Fact]
    public void Typing_in_search_issues_one_call_after_the_debounce_and_replaces_the_history_entry()
    {
        var cut = RenderList();
        _kb.ClearReceivedCalls();

        cut.Find("input[type=search]").Input("r");
        cut.Find("input[type=search]").Input("re");
        cut.Find("input[type=search]").Input("refund");
        Time.Advance(KbDefaults.SearchDebounce - TimeSpan.FromMilliseconds(1));
        Requests().ShouldBeEmpty();

        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => Requests().Select(r => r.Text).ShouldBe(["refund"]));
        Time.Advance(KbDefaults.SearchDebounce * 3);
        Requests().Select(r => r.Text).ShouldBe(["refund"]);
        _navigation.Uri.ShouldEndWith("/kb?search=refund");
        ((BunitNavigationManager)_navigation).History.Last().Options.ReplaceHistoryEntry.ShouldBeTrue();
    }

    [Fact]
    public void A_search_that_is_still_waiting_is_released_with_the_page_so_nothing_fires_after_it_is_gone()
    {
        var cut = RenderList();
        _kb.ClearReceivedCalls();
        cut.Find("input[type=search]").Input("refund");
        _timers.LiveTimers.ShouldBe(1);

        cut.Instance.Dispose();
        Time.Advance(KbDefaults.SearchDebounce * 3);

        _timers.LiveTimers.ShouldBe(0);
        Requests().ShouldBeEmpty();
        _navigation.Uri.ShouldNotContain("search=refund");
    }

    [Fact]
    public void Paging_goes_to_the_next_page_in_the_address()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            TestData.Ok(TestData.KbPage([TestData.KbItem()], call.Arg<ListKbArticlesRequest>().Page, 60)));
        var cut = RenderList();

        cut.FindAll(".ts-pager button").Single(b => b.TextContent == "Next").Click();

        cut.WaitForAssertion(() => Requests().Last().Page.ShouldBe(2));
        _navigation.Uri.ShouldEndWith("/kb?page=2");
    }

    [Fact]
    public void A_page_past_the_end_goes_to_the_last_real_page_instead_of_showing_an_empty_list()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.Arg<ListKbArticlesRequest>().Page == 9
                ? TestData.Ok(TestData.KbPage([], page: 9, total: 30))
                : TestData.Ok(TestData.KbPage([TestData.KbItem()], call.Arg<ListKbArticlesRequest>().Page, 30)));

        var cut = RenderList("?page=9");

        cut.WaitForAssertion(() => Requests().Last().Page.ShouldBe(2));
        _navigation.Uri.ShouldEndWith("/kb?page=2");
        ((BunitNavigationManager)_navigation).History.Last().Options.ReplaceHistoryEntry.ShouldBeTrue();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    // ---- empty and failed states ---------------------------------------------------------------------------------

    [Fact]
    public void An_empty_knowledge_base_invites_the_first_article()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([])));

        var cut = RenderList();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No articles yet");
        cut.Find(".ts-state a.btn").GetAttribute("href").ShouldBe("/kb/new");
    }

    [Fact]
    public void A_filtered_empty_result_says_no_articles_match_and_clearing_goes_back_to_the_bare_list()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([])));
        var cut = RenderList("?status=Draft&search=zzz");

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No articles match");

        cut.FindAll(".ts-state button").Single().Click();

        _navigation.Uri.ShouldEndWith("/kb");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<PagedResponse<KbArticleListItemDto>>("api-error", "The API is unavailable."),
            TestData.Ok(TestData.KbPage([TestData.KbItem()])));

        var cut = RenderList();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the articles. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void A_failed_refresh_keeps_the_list_that_is_on_screen()
    {
        var cut = RenderList();
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<PagedResponse<KbArticleListItemDto>>("api-error", "The API is unavailable."));

        cut.FindAll("button").Single(b => b.TextContent == "Refresh").Click();

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("The API is unavailable."));
        cut.FindAll("tbody tr").Count.ShouldBe(2);
    }

    [Fact]
    public void A_failed_lookup_only_empties_that_filter_and_the_list_still_loads()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<ProductDto>>("api-error"));
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<KbCategoryDto>>("api-error"));

        var cut = RenderList();

        cut.FindAll("tbody tr").Count.ShouldBe(2);
        cut.FindAll("#kb-product option").Count.ShouldBe(2);
        cut.FindAll("#kb-category option").Count.ShouldBe(1);
    }

    // Review Focus 4: a slow answer that was overtaken by a newer filter must not replace the newer list.
    [Fact]
    public void A_load_that_was_overtaken_by_a_newer_filter_never_replaces_the_newer_list()
    {
        var slow = new TaskCompletionSource<Result<PagedResponse<KbArticleListItemDto>>>();
        _kb.ListAsync(Arg.Is<ListKbArticlesRequest>(r => r.Status == null), Arg.Any<CancellationToken>()).Returns(_ => slow.Task);
        _kb.ListAsync(Arg.Is<ListKbArticlesRequest>(r => r.Status == "Draft"), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.KbPage([TestData.KbItem("Newer", "newer", KbArticleStatuses.Draft)])));
        var cut = RenderList();
        cut.FindAll("tbody tr").ShouldBeEmpty();

        _navigation.NavigateTo("/kb?status=Draft");
        cut.WaitForAssertion(() => cut.Find("tbody tr a").TextContent.ShouldBe("Newer"));
        slow.SetResult(TestData.Ok(TestData.KbPage([TestData.KbItem("Older", "older")])));

        cut.WaitForAssertion(() => cut.Find("tbody tr a").TextContent.ShouldBe("Newer"));
        cut.Markup.ShouldNotContain("Older");
    }

    [Fact]
    public void The_page_links_to_the_new_article_form_and_the_categories_page()
    {
        var cut = RenderList();

        cut.Find("a[href='/kb/new']").TextContent.ShouldBe("New article");
        cut.Find("a[href='/kb/categories']").TextContent.ShouldBe("Categories");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/KbListFilterTests.cs`

```csharp
using TechStrap.Admin.Features.Kb;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The list filter is the one thing the URL, the select boxes and the API request share.</summary>
public sealed class KbListFilterTests
{
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    [Fact]
    public void The_address_leaves_out_page_one_and_empty_values_and_escapes_the_search()
    {
        KbListFilter.Empty.Uri().ShouldBe("/kb");
        new KbListFilter(ProductId, false, CategoryId, "Draft", "reset & more", 2).Uri()
            .ShouldBe($"/kb?product={ProductId}&category={CategoryId}&status=Draft&search=reset%20%26%20more&page=2");
        KbListFilter.Empty.WithProduct("shared").Uri().ShouldBe("/kb?product=shared");
    }

    [Theory]
    [InlineData("shared", true, false)]
    [InlineData("SHARED", true, false)]
    [InlineData("aaaaaaaa-0000-0000-0000-000000000001", false, true)]
    [InlineData("not-a-guid", false, false)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    public void The_product_choice_is_shared_a_product_or_every_product_and_nothing_else(string? value, bool shared, bool product)
    {
        var filter = new KbListFilter(Guid.NewGuid(), false, null, null, null, 4).WithProduct(value);

        filter.SharedOnly.ShouldBe(shared);
        (filter.ProductId is not null).ShouldBe(product);
        filter.Page.ShouldBe(1);
    }

    [Fact]
    public void The_request_carries_the_filter_a_product_means_its_own_articles_and_the_text_is_trimmed()
    {
        new KbListFilter(ProductId, false, CategoryId, KbArticleStatuses.Published, "  reset  ", 3).ToRequest()
            .ShouldBe(new ListKbArticlesRequest(ProductId, false, false, KbArticleStatuses.Published, CategoryId, "reset", 3, 25));
        KbListFilter.Empty.WithProduct("shared").ToRequest().SharedOnly.ShouldBeTrue();
    }

    [Theory]
    [InlineData("draft", "Draft")]
    [InlineData(" PUBLISHED ", "Published")]
    [InlineData("archived", "Archived")]
    [InlineData("deleted", null)]
    [InlineData("", null)]
    public void A_status_is_one_of_the_three_in_its_canonical_spelling_or_dropped(string value, string? expected) =>
        KbDefaults.CanonicalStatus(value).ShouldBe(expected);

    [Fact]
    public void Only_a_set_filter_counts_as_a_filter()
    {
        KbListFilter.Empty.HasFilters.ShouldBeFalse();
        (KbListFilter.Empty with { Search = "  " }).HasFilters.ShouldBeFalse();
        (KbListFilter.Empty with { Page = 4 }).HasFilters.ShouldBeFalse();
        KbListFilter.Empty.WithProduct("shared").HasFilters.ShouldBeTrue();
        (KbListFilter.Empty with { Status = "Draft" }).HasFilters.ShouldBeTrue();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/KbStatusBadgeTests.cs`

```csharp
using Bunit;
using TechStrap.Admin.Features.Kb;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The status is always the word itself; the border style only repeats it.</summary>
public sealed class KbStatusBadgeTests : BunitContext
{
    [Theory]
    [InlineData(KbArticleStatuses.Draft, "ts-pill")]
    [InlineData(KbArticleStatuses.Published, "ts-pill ts-pill--on")]
    [InlineData(KbArticleStatuses.Archived, "ts-pill ts-pill--off")]
    public void Each_status_shows_its_word_and_its_own_style(string status, string css)
    {
        var cut = Render<KbStatusBadge>(p => p.Add(b => b.Status, status));

        cut.Find("span").TextContent.ShouldBe(status);
        cut.Find("span").GetAttribute("class").ShouldBe(css);
        cut.Find("span").GetAttribute("data-status").ShouldBe(status);
    }

    [Fact]
    public void A_status_the_admin_does_not_know_is_shown_as_the_api_sent_it_in_the_plain_style()
    {
        var cut = Render<KbStatusBadge>(p => p.Add(b => b.Status, "Review"));

        cut.Find("span").TextContent.ShouldBe("Review");
        cut.Find("span").GetAttribute("class").ShouldBe("ts-pill");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/LayoutResilienceTests.cs`

Replace (edit 1 of 2)

```csharp

        cut.WaitForAssertion(() => _logs.Lines.ShouldContain(line => line.Contains("InvalidOperationException", StringComparison.Ordinal)));
        cut.FindAll("a.ts-rail-link").Count.ShouldBe(7);
        cut.FindAll(".ts-rail-badge").ShouldBeEmpty();
        NoLeak();
```

with

```csharp

        cut.WaitForAssertion(() => _logs.Lines.ShouldContain(line => line.Contains("InvalidOperationException", StringComparison.Ordinal)));
        cut.FindAll("a.ts-rail-link").Count.ShouldBe(8);
        cut.FindAll(".ts-rail-badge").ShouldBeEmpty();
        NoLeak();
```

Replace (edit 2 of 2)

```csharp
        var rail = Render<NavMenu>(p => p.SignedIn());
        var guarded = Render<AdminOnly>(p => p.AddChildContent("<p id='admin'>admin content</p>"));
        rail.FindAll("a.ts-rail-link").Count.ShouldBe(7);

        Services.GetRequiredService<SessionExpiry>().Report();

        session.ExpiredWhileWorking.ShouldBeTrue();
        rail.WaitForAssertion(() => rail.FindAll("a.ts-rail-link").Count.ShouldBe(7));
        guarded.WaitForAssertion(() => guarded.Find("#admin").TextContent.ShouldBe("admin content"));
    }
```

with

```csharp
        var rail = Render<NavMenu>(p => p.SignedIn());
        var guarded = Render<AdminOnly>(p => p.AddChildContent("<p id='admin'>admin content</p>"));
        rail.FindAll("a.ts-rail-link").Count.ShouldBe(8);

        Services.GetRequiredService<SessionExpiry>().Report();

        session.ExpiredWhileWorking.ShouldBeTrue();
        rail.WaitForAssertion(() => rail.FindAll("a.ts-rail-link").Count.ShouldBe(8));
        guarded.WaitForAssertion(() => guarded.Find("#admin").TextContent.ShouldBe("admin content"));
    }
```

`tests/TechStrap.Admin.Tests/Components/NavMenuTests.cs`

Replace (edit 1 of 3)

```csharp

        cut.FindAll("a.ts-rail-link").Select(a => (a.GetAttribute("href"), a.TextContent.Trim())).ShouldBe(
            [("/queue", "Queue"), ("/account/notifications", "My settings")]);
        cut.FindAll("a[href^='/settings']").ShouldBeEmpty();
        cut.FindAll("a[href^='/ops']").ShouldBeEmpty();
```

with

```csharp

        cut.FindAll("a.ts-rail-link").Select(a => (a.GetAttribute("href"), a.TextContent.Trim())).ShouldBe(
            [("/queue", "Queue"), ("/kb", "Knowledge base"), ("/account/notifications", "My settings")]);
        cut.FindAll("a[href^='/settings']").ShouldBeEmpty();
        cut.FindAll("a[href^='/ops']").ShouldBeEmpty();
```

Replace (edit 2 of 3)

```csharp
        [
            ("/queue", "Queue"),
            ("/settings/products", "Products"),
            ("/settings/agents", "Agents"),
```

with

```csharp
        [
            ("/queue", "Queue"),
            ("/kb", "Knowledge base"),
            ("/settings/products", "Products"),
            ("/settings/agents", "Agents"),
```

Replace (edit 3 of 3)

```csharp
        cut.Render(); // force a re-render while the answer is pending

        cut.FindAll("a.ts-rail-link").Count.ShouldBe(7);
        gate.SetResult(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Admin, true, "Samantha", null)));
        await reload;
        cut.FindAll("a.ts-rail-link").Count.ShouldBe(7);
    }
```

with

```csharp
        cut.Render(); // force a re-render while the answer is pending

        cut.FindAll("a.ts-rail-link").Count.ShouldBe(8);
        gate.SetResult(Result<AgentDto>.Success(new AgentDto(Guid.NewGuid(), "Sam", "sam@orbitly.test", AgentRoles.Admin, true, "Samantha", null)));
        await reload;
        cut.FindAll("a.ts-rail-link").Count.ShouldBe(8);
    }
```

`tests/TechStrap.Admin.Tests/KbListHostTests.cs`

```csharp
using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The whole Admin host with the fake sign-in and the stub API, for the knowledge base list (PHASE-08). Every agent may open it, and every call it makes carries the agent's token.
/// Pages prerender, so the HTML already holds the data the stub served.
/// </summary>
public sealed class KbListHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static AdminFactory FactoryWithKbData()
    {
        var factory = new AdminFactory();
        var product = new ProductDto(OrbitlyId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 1);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)[new KbCategoryDto(CategoryId, OrbitlyId, "account", "Account", null, 10, 1)])
            .OnJson(HttpMethod.Get, "/api/kb/articles", new PagedResponse<KbArticleListItemDto>(
                [new KbArticleListItemDto(Guid.NewGuid(), OrbitlyId, CategoryId, "reset-password", "Reset your password", KbArticleStatuses.Published, Now.AddHours(-3))], 1, 25, 1));
        return factory;
    }

    private static HttpClient NoRedirectClient(AdminFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task An_anonymous_visitor_is_sent_to_the_sign_in_landing_and_the_api_is_never_asked()
    {
        await using var factory = FactoryWithKbData();
        using var client = NoRedirectClient(factory);

        using var response = await client.GetAsync("/kb", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/signin");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_user_the_api_refuses_gets_the_no_access_page_and_only_the_me_call_is_made()
    {
        await using var factory = FactoryWithKbData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        var html = await client.GetStringAsync("/kb", Ct);

        html.ShouldNotContain("Reset your password");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Outsider);
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("Admin")]
    public async Task Every_agent_sees_the_article_list_with_its_data_and_the_rail_link(string who)
    {
        await using var factory = FactoryWithKbData();
        var principal = who == "Admin" ? AdminTestPrincipal.Admin : AdminTestPrincipal.Agent;
        using var client = factory.CreateClient().SignedInAs(principal);

        var html = await client.GetStringAsync("/kb", Ct);
        var page = await new HtmlParser().ParseDocumentAsync(html, Ct);

        page.QuerySelector("h1")!.TextContent.ShouldBe("Knowledge base");
        page.QuerySelector("tbody tr a")!.TextContent.ShouldBe("Reset your password");
        page.QuerySelector("tbody tr")!.Children[1].TextContent.ShouldBe("Orbitly");
        page.QuerySelector("tbody tr")!.Children[2].TextContent.ShouldBe("Account");
        page.QuerySelector("a.ts-rail-link[href='/kb']")!.TextContent.Trim().ShouldBe("Knowledge base");
        page.QuerySelector("a.ts-rail-link[href='/kb']")!.GetAttribute("aria-current").ShouldBe("page");
        html.ShouldNotContain("You don't have access");
        factory.Api.Requests[0].Path.ShouldBe("/api/agents/me");
        factory.Api.Requests.ShouldContain(r => r.Path == "/api/kb/articles" && r.Query == "?page=1&pageSize=25");
        factory.Api.AssertEveryCallBore(principal);
    }

    [Fact]
    public async Task The_filters_in_the_address_reach_the_api_as_the_query_string()
    {
        await using var factory = FactoryWithKbData();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        await client.GetStringAsync($"/kb?product={OrbitlyId}&status=Draft&search=reset", Ct);

        factory.Api.Requests.ShouldContain(r => r.Path == "/api/kb/articles" && r.Query == $"?productId={OrbitlyId}&includeShared=false&status=Draft&text=reset&page=1&pageSize=25");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }
}
```

`tests/TechStrap.Admin.Tests/ScrollRegionSiteTests.cs`

Replace

```csharp
        }

        tables.ShouldBe(7);
        unwrapped.ShouldBeEmpty();
        withoutRole.ShouldBeEmpty();
```

with

```csharp
        }

        tables.ShouldBe(8);
        unwrapped.ShouldBeEmpty();
        withoutRole.ShouldBeEmpty();
```

`tests/TechStrap.Admin.Tests/Support/CountingTimeProvider.cs`

```csharp
namespace TechStrap.Admin.Tests.Support;

/// <summary>
/// A <see cref="TimeProvider"/> that lends its clock and timers to another one (the fake clock of <see cref="AdminComponentTest"/>) and counts the timers that are alive. A component that starts a timer and does not release it
/// when it goes leaves <see cref="LiveTimers"/> above zero, which "no call is made after it is gone" cannot see when the component also checks its own disposed flag.
/// </summary>
public sealed class CountingTimeProvider(TimeProvider inner) : TimeProvider
{
    private int _live;

    /// <summary>Timers created through this provider that have not been disposed.</summary>
    public int LiveTimers => Volatile.Read(ref _live);

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override long GetTimestamp() => inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref _live);
        return new Counted(inner.CreateTimer(callback, state, dueTime, period), this);
    }

    private sealed class Counted(ITimer timer, CountingTimeProvider owner) : ITimer
    {
        private int _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period) => timer.Change(dueTime, period);

        public void Dispose()
        {
            timer.Dispose();
            Release();
        }

        public ValueTask DisposeAsync()
        {
            Release();
            return timer.DisposeAsync();
        }

        private void Release()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref owner._live);
            }
        }
    }
}
```

`tests/TechStrap.Admin.Tests/Support/TestData.Kb.cs`

```csharp
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Tests.Support;

internal static partial class TestData
{
    public static readonly Guid GettingStartedId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    public static readonly Guid AccountCategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    public static readonly Guid ArticleId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    public static KbArticleListItemDto KbItem(
        string title = "Reset your password",
        string slug = "reset-password",
        string status = KbArticleStatuses.Published,
        Guid? productId = null,
        Guid? categoryId = null,
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), productId, categoryId, slug, title, status, Now.AddHours(-2));

    public static PagedResponse<KbArticleListItemDto> KbPage(IReadOnlyList<KbArticleListItemDto> items, int page = 1, int total = -1) =>
        new(items, page, 25, total < 0 ? items.Count : total);

    public static KbCategoryDto KbCategory(string name = "Getting started", string slug = "getting-started", Guid? productId = null, Guid? id = null, int sortOrder = 10, uint version = 1, string? description = null) =>
        new(id ?? GettingStartedId, productId, slug, name, description, sortOrder, version);

    public static KbArticleDto KbArticle(
        string title = "Reset your password",
        string slug = "reset-password",
        string status = KbArticleStatuses.Draft,
        Guid? productId = null,
        Guid? categoryId = null,
        string summary = "How to reset it.",
        string body = "# Steps",
        uint version = 3,
        Guid? id = null,
        DateTimeOffset? publishedAt = null) =>
        new(id ?? ArticleId, productId, categoryId, slug, title, summary, body, status, Guid.NewGuid(), Now.AddDays(-2), Now.AddHours(-1), publishedAt, version);
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
```

Expected: the test project does not build (26 errors), because the client, the page, the filter, the badge, the error codes and the test builders do not exist yet. The first errors:
- `CS0234`: The type or namespace name 'Kb' does not exist in the namespace 'TechStrap.Admin.Features' (are you missing an assembly reference?) (6 times)
- `CS0246`: The type or namespace name 'IKbClient' could not be found (are you missing a using directive or an assembly reference?) (4 times)
- `CS0117`: 'ApiErrorCodes' does not contain a definition for 'KbImageTypeNotAllowed' (4 times)

- [ ] **Step 3: Implement**

The client first (it is the wire contract the page reads), then the error codes and field names, the constants and the one filter value, the view model and the badge, the page, and last the rail link, the palette command and the styles. New files are given whole; the others as edits.

`src/TechStrap.Admin/Clients/ApiClientRegistration.cs`

Replace

```csharp
        services.AddScoped<IAdminEventsClient, AdminEventsClient>();
        services.AddScoped<IDeadLettersClient, DeadLettersClient>();
        services.AddScoped<AgentSession>();
        return services;
```

with

```csharp
        services.AddScoped<IAdminEventsClient, AdminEventsClient>();
        services.AddScoped<IDeadLettersClient, DeadLettersClient>();
        services.AddScoped<IKbClient, KbClient>();
        services.AddScoped<AgentSession>();
        return services;
```

`src/TechStrap.Admin/Clients/ApiErrorCodes.cs`

Replace

```csharp
    public const string PublicDisplayNameInvalid = "public-display-name-invalid";
    public const string LogoPathInvalid = "logo-path-invalid";

    /// <summary>A write that failed (any 5xx, transport error, timeout or unreadable answer) this way may still have been applied (the answer was lost, late or unreadable): say so and offer a reload, never a bare retry.</summary>
```

with

```csharp
    public const string PublicDisplayNameInvalid = "public-display-name-invalid";
    public const string LogoPathInvalid = "logo-path-invalid";
    public const string KbSlugTaken = "kb-slug-taken";
    public const string KbCategorySlugTaken = "kb-category-slug-taken";
    public const string KbCategoryReservedSlug = "kb-category-reserved-slug";
    public const string KbCategoryInUse = "kb-category-in-use";
    public const string KbCategoryScopeMismatch = "kb-category-scope-mismatch";
    public const string KbArticleNotFound = "kb-article-not-found";
    public const string KbCategoryNotFound = "kb-category-not-found";
    public const string KbPublishIncomplete = "kb-publish-incomplete";
    public const string KbImageTypeNotAllowed = "kb-image-type-not-allowed";
    public const string KbImageTooLarge = "kb-image-too-large";
    public const string KbArticleNotLinkable = "kb-article-not-linkable";

    /// <summary>Kestrel answers a request body far over the limit (413) with this problem type before any handler runs; a picture over 5 MB but under the request cap is <see cref="KbImageTooLarge"/>.</summary>
    public const string RequestTooLarge = "request-too-large";

    /// <summary>A reply that links an article id the API does not know (404). Linking an unpublished or other-product article is <see cref="KbArticleNotLinkable"/>.</summary>
    public const string ArticleNotFound = "article-not-found";

    /// <summary>A write that failed (any 5xx, transport error, timeout or unreadable answer) this way may still have been applied (the answer was lost, late or unreadable): say so and offer a reload, never a bare retry.</summary>
```

`src/TechStrap.Admin/Clients/ApiFields.cs`

Replace

```csharp
    // Agents.
    public const string PublicDisplayName = "public-display-name";
}
```

with

```csharp
    // Agents.
    public const string PublicDisplayName = "public-display-name";

    // Knowledge base. The publish check names title, slug, body or category; a category has a name, a description and a sort order; an upload has its file.
    public const string Title = "title";
    public const string Summary = "summary";
    public const string Body = "body";
    public const string Category = "category";

    /// <summary>The name the API gives a category error on a request (kb-category-not-found, kb-category-scope-mismatch): the request property, not the publish check's <c>category</c>.</summary>
    public const string CategoryId = "categoryId";
    public const string Description = "description";
    public const string SortOrder = "sort-order";
    public const string File = "file";
}
```

`src/TechStrap.Admin/Clients/KbClient.cs`

```csharp
using System.Net.Http.Headers;
using SyntaxCircus.Common;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;

namespace TechStrap.Admin.Clients;

/// <summary>
/// A picture to upload. The stream is opened when the request is built and closed when it has been sent, so the same <see cref="KbImageFile"/> can be sent again after a failure
/// (the browser file stays selected until an upload succeeds).
/// </summary>
public sealed record KbImageFile(string FileName, string ContentType, Func<Stream> OpenRead);

/// <summary>
/// The knowledge base of the API: articles, the live preview, image upload and categories (PHASE-08). Reads work for every agent. A write is never retried: a stale
/// <c>Version</c> is a Conflict result with code <see cref="ApiErrorCodes.ConcurrencyConflict"/>, and a write whose answer was lost is one the caller treats as uncertain.
/// </summary>
public interface IKbClient
{
    /// <summary>
    /// <c>GET /api/kb/articles</c>: newest change first, or best match first for <see cref="ListKbArticlesRequest.Text"/>. Page and PageSize are always sent; <c>sharedOnly</c> only when true; <c>includeShared</c>
    /// (which the API defaults to true) is always sent with a product, so a product filter means exactly what the caller asked for; every other filter only when it is set. 400 status-invalid.
    /// </summary>
    Task<Result<PagedResponse<KbArticleListItemDto>>> ListAsync(ListKbArticlesRequest request, CancellationToken cancellationToken);

    /// <summary><c>GET /api/kb/articles/{id}</c>: the whole article with its Markdown and the <c>Version</c> the next update must send. 404 is kb-article-not-found.</summary>
    Task<Result<KbArticleDto>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/kb/articles</c> (201). 409 kb-slug-taken (the slug is used in this product or, across scopes, by a shared article or another product);
    /// 409 kb-category-scope-mismatch; 400 fields: title, slug, summary, body, category.
    /// </summary>
    Task<Result<KbArticleDto>> CreateAsync(CreateKbArticleRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// <c>PUT /api/kb/articles/{id}</c>. The product and the slug are permanent. 409 concurrency-conflict when the version is stale. Updating an archived article moves it back to Draft;
    /// the answer carries the status the article has now.
    /// </summary>
    Task<Result<KbArticleDto>> UpdateAsync(Guid id, UpdateKbArticleRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/kb/articles/{id}/publish?version=N</c>: the version the article was loaded with travels, so publishing something another agent has changed is a 409 concurrency-conflict and not a
    /// publish of text nobody here has seen. Answers the article as it is now. 400 kb-publish-incomplete names the missing field (title, slug, body or category); 409 article-already-published.
    /// </summary>
    Task<Result<KbArticleDto>> PublishAsync(Guid id, uint version, CancellationToken cancellationToken);

    /// <summary><c>POST /api/kb/articles/{id}/archive?version=N</c>: the article leaves the portal. Version and answer as for publish. 409 article-already-archived.</summary>
    Task<Result<KbArticleDto>> ArchiveAsync(Guid id, uint version, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST /api/kb/preview</c>: the Markdown rendered and sanitized by the same pipeline as the portal. A write call, so it is never retried; the editor cancels a call that a newer
    /// keystroke has replaced.
    /// </summary>
    Task<Result<KbPreviewResponse>> PreviewAsync(KbPreviewRequest request, CancellationToken cancellationToken);

    /// <summary><c>POST /api/kb/images</c> as multipart/form-data with the file in the part named <see cref="KbLimits.ImageFieldName"/> (<c>file</c>). 400 kb-image-type-not-allowed or kb-image-too-large.</summary>
    Task<Result<KbImageUploadResponse>> UploadImageAsync(KbImageFile file, CancellationToken cancellationToken);

    /// <summary><c>GET /api/kb/categories</c>: every category, shared ones included, by sort order.</summary>
    Task<Result<IReadOnlyList<KbCategoryDto>>> ListCategoriesAsync(CancellationToken cancellationToken);

    /// <summary><c>POST /api/kb/categories</c> (201). 409 kb-category-slug-taken; 400 kb-category-reserved-slug (the slug "search"); 400 fields: slug, name, description.</summary>
    Task<Result<KbCategoryDto>> CreateCategoryAsync(CreateKbCategoryRequest request, CancellationToken cancellationToken);

    /// <summary><c>PUT /api/kb/categories/{id}</c>. The product and the slug are permanent. 409 concurrency-conflict when the version is stale; 404 kb-category-not-found.</summary>
    Task<Result<KbCategoryDto>> UpdateCategoryAsync(Guid id, UpdateKbCategoryRequest request, CancellationToken cancellationToken);

    /// <summary><c>DELETE /api/kb/categories/{id}</c> (Admin, 204). 409 kb-category-in-use while articles are in it.</summary>
    Task<Result> DeleteCategoryAsync(Guid id, CancellationToken cancellationToken);
}

internal sealed class KbClient(ApiConnection connection) : IKbClient
{
    public Task<Result<PagedResponse<KbArticleListItemDto>>> ListAsync(ListKbArticlesRequest request, CancellationToken cancellationToken) =>
        connection.GetAsync<PagedResponse<KbArticleListItemDto>>(
            ApiUri.Build(
                "api/kb/articles",
                ("productId", request.ProductId),
                ("sharedOnly", request.SharedOnly ? "true" : null),
                ("includeShared", request.ProductId is null ? null : request.IncludeShared ? "true" : "false"),
                ("status", request.Status),
                ("categoryId", request.CategoryId),
                ("text", string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim()),
                ("page", request.Page),
                ("pageSize", request.PageSize)),
            cancellationToken);

    public Task<Result<KbArticleDto>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        connection.GetAsync<KbArticleDto>($"api/kb/articles/{id}", cancellationToken);

    public Task<Result<KbArticleDto>> CreateAsync(CreateKbArticleRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<KbArticleDto>(HttpMethod.Post, "api/kb/articles", request, cancellationToken);

    public Task<Result<KbArticleDto>> UpdateAsync(Guid id, UpdateKbArticleRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<KbArticleDto>(HttpMethod.Put, $"api/kb/articles/{id}", request, cancellationToken);

    public Task<Result<KbArticleDto>> PublishAsync(Guid id, uint version, CancellationToken cancellationToken) =>
        connection.SendAsync<KbArticleDto>(HttpMethod.Post, ApiUri.Build($"api/kb/articles/{id}/publish", ("version", version)), null, cancellationToken);

    public Task<Result<KbArticleDto>> ArchiveAsync(Guid id, uint version, CancellationToken cancellationToken) =>
        connection.SendAsync<KbArticleDto>(HttpMethod.Post, ApiUri.Build($"api/kb/articles/{id}/archive", ("version", version)), null, cancellationToken);

    public Task<Result<KbPreviewResponse>> PreviewAsync(KbPreviewRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<KbPreviewResponse>(HttpMethod.Post, "api/kb/preview", request, cancellationToken);

    public async Task<Result<KbImageUploadResponse>> UploadImageAsync(KbImageFile file, CancellationToken cancellationToken)
    {
        Stream? stream = null;
        try
        {
            stream = file.OpenRead();
            using var form = new MultipartFormDataContent();
            var part = new StreamContent(stream);
            part.Headers.ContentType = MediaTypeHeaderValue.TryParse(file.ContentType, out var type) ? type : new MediaTypeHeaderValue("application/octet-stream");

            // The browser's file name is cleaned first: an empty or odd name would make the multipart body throw (the reply composer does the same).
            form.Add(part, KbLimits.ImageFieldName, AttachmentFileName.Clean(file.FileName));
            return await connection.SendContentAsync<KbImageUploadResponse>(HttpMethod.Post, "api/kb/images", form, cancellationToken);
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync();
            }
        }
    }

    public async Task<Result<IReadOnlyList<KbCategoryDto>>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        ProductsClient.Narrow(await connection.GetAsync<List<KbCategoryDto>>("api/kb/categories", cancellationToken));

    public Task<Result<KbCategoryDto>> CreateCategoryAsync(CreateKbCategoryRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<KbCategoryDto>(HttpMethod.Post, "api/kb/categories", request, cancellationToken);

    public Task<Result<KbCategoryDto>> UpdateCategoryAsync(Guid id, UpdateKbCategoryRequest request, CancellationToken cancellationToken) =>
        connection.SendAsync<KbCategoryDto>(HttpMethod.Put, $"api/kb/categories/{id}", request, cancellationToken);

    public Task<Result> DeleteCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        connection.SendAsync(HttpMethod.Delete, $"api/kb/categories/{id}", null, cancellationToken);
}
```

`src/TechStrap.Admin/Components/Layout/NavMenu.razor`

Replace

```razor
        <div @ref="_panel" id="@PanelId" class="ts-rail-panel" data-open="@(_railOpen ? "true" : "false")">
            <RailLink Href="/queue">@ShellCopy.QueueLink</RailLink>
            @if (Session.IsAdmin)
            {
```

with

```razor
        <div @ref="_panel" id="@PanelId" class="ts-rail-panel" data-open="@(_railOpen ? "true" : "false")">
            <RailLink Href="/queue">@ShellCopy.QueueLink</RailLink>
            <RailLink Href="/kb">@ShellCopy.KbLink</RailLink>
            @if (Session.IsAdmin)
            {
```

`src/TechStrap.Admin/Components/Ui/ShellCopy.cs`

Replace

```csharp
    public const string PaginationLabel = "Pagination";
    public const string QueueLink = "Queue";
    public const string MySettingsLink = "My settings";
    public const string AdminLinksLabel = "Admin";
```

with

```csharp
    public const string PaginationLabel = "Pagination";
    public const string QueueLink = "Queue";
    public const string KbLink = "Knowledge base";
    public const string MySettingsLink = "My settings";
    public const string AdminLinksLabel = "Admin";
```

`src/TechStrap.Admin/Features/Kb/KbArticleListPage.razor`

```razor
@page "/kb"
@attribute [Authorize]

<PageTitle>@KbCopy.Heading</PageTitle>
<div class="ts-settings ts-kb-list">
    <div class="ts-settings-head">
        <h1>@KbCopy.Heading</h1>
        <div class="ts-kb-head-actions">
            <a class="btn btn-outline-secondary" href="/kb/categories">@KbCopy.Categories</a>
            <a class="btn btn-primary" href="/kb/new">@KbCopy.NewArticle</a>
        </div>
    </div>

    <form class="ts-filterbar" role="search" @onsubmit="SubmitSearchAsync" @onsubmit:preventDefault>
        <button type="submit" hidden>Search</button>
        <div class="ts-filter ts-filter--search">
            <label for="kb-search">@KbCopy.SearchLabel</label>
            <input id="kb-search" type="search" class="form-control" autocomplete="off" spellcheck="false" value="@_text" @oninput="OnSearchInput" />
        </div>
        <div class="ts-filter">
            <label for="kb-product">@KbCopy.ColumnProduct</label>
            <select id="kb-product" class="form-select" @onchange="OnProductChangedAsync">
                <option value="" selected="@(_filter.ProductValue.Length == 0)">@KbCopy.AllProducts</option>
                <option value="@KbListFilter.SharedValue" selected="@_filter.SharedOnly">@KbCopy.Shared</option>
                @foreach (var product in _products)
                {
                    <option value="@product.Id" selected="@(_filter.ProductId == product.Id)">@product.Name</option>
                }
            </select>
        </div>
        <div class="ts-filter">
            <label for="kb-category">@KbCopy.ColumnCategory</label>
            <select id="kb-category" class="form-select" @onchange="OnCategoryChangedAsync">
                <option value="" selected="@(_filter.CategoryId is null)">@KbCopy.AllCategories</option>
                @foreach (var category in _categories)
                {
                    <option value="@category.Id" selected="@(_filter.CategoryId == category.Id)">@category.Name</option>
                }
            </select>
        </div>
        <div class="ts-filter">
            <label for="kb-status">@KbCopy.ColumnStatus</label>
            <select id="kb-status" class="form-select" @onchange="OnStatusChangedAsync">
                <option value="" selected="@(_filter.Status is null)">@KbCopy.AllStatuses</option>
                @foreach (var status in KbDefaults.Statuses)
                {
                    <option value="@status" selected="@(_filter.Status == status)">@status</option>
                }
            </select>
        </div>
        <div class="ts-filter ts-filter--actions">
            @if (_filter.HasFilters)
            {
                <button type="button" class="btn btn-outline-secondary" @onclick="ClearAsync">@KbCopy.ClearFilters</button>
            }
            <button type="button" class="btn btn-outline-secondary" @onclick="ReloadAsync">@KbCopy.Refresh</button>
        </div>
    </form>
    <p class="visually-hidden" role="status">@_announcement</p>

    @if (_loading && _page is null)
    {
        <LoadingState Rows="6" Label="@KbCopy.Loading" />
    }
    else
    {
        @if (_error is not null)
        {
            <ErrorState Message="@_error" OnRetry="ReloadAsync" />
        }

        @if (_rows.Count > 0)
        {
            <ScrollRegion Label="@KbCopy.TableLabel">
                <table role="table" class="table ts-ledger ts-settings-table" aria-busy="@(_loading ? "true" : null)">
                    <thead role="rowgroup">
                        <tr role="row">
                            <th role="columnheader" scope="col">@KbCopy.ColumnTitle</th>
                            <th role="columnheader" scope="col">@KbCopy.ColumnProduct</th>
                            <th role="columnheader" scope="col">@KbCopy.ColumnCategory</th>
                            <th role="columnheader" scope="col">@KbCopy.ColumnStatus</th>
                            <th role="columnheader" scope="col">@KbCopy.ColumnUpdated</th>
                        </tr>
                    </thead>
                    <tbody role="rowgroup">
                        @foreach (var row in _rows)
                        {
                            <tr role="row" @key="row.Id" data-article="@row.Slug">
                                <td role="cell"><a href="@row.Href">@row.Title</a></td>
                                <td role="cell">@row.ProductName</td>
                                <td role="cell">@row.CategoryName</td>
                                <td role="cell"><KbStatusBadge Status="@row.Status" /></td>
                                <td role="cell"><RelativeTime When="row.UpdatedAt" /></td>
                            </tr>
                        }
                    </tbody>
                </table>
            </ScrollRegion>
            <PagerControl Page="_filter.Page" PageSize="KbDefaults.PageSize" TotalCount="_page!.TotalCount" OnPageChanged="OnPageChangedAsync" />
        }
        else if (_page is not null && _error is null)
        {
            @if (_filter.HasFilters)
            {
                <EmptyState Heading="@KbCopy.NoMatchHeading">
                    <button type="button" class="btn btn-outline-secondary" @onclick="ClearAsync">@KbCopy.ClearFilters</button>
                </EmptyState>
            }
            else
            {
                <EmptyState Heading="@KbCopy.NoArticlesHeading">
                    <p>@KbCopy.NoArticlesHint</p>
                    <a class="btn btn-primary" href="/kb/new">@KbCopy.NewArticle</a>
                </EmptyState>
            }
        }
    }
</div>
```

`src/TechStrap.Admin/Features/Kb/KbArticleListPage.razor.cs`

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// The article list: filters (product, category, status) and search, all in the URL. Every load takes the next <c>_loadId</c> and only the latest load may change the screen, so a slow answer
/// that was overtaken (another filter, a page change) is ignored. A failed refresh keeps the list that is on screen. The page asks the API only for what any agent may read.
/// </summary>
public sealed partial class KbArticleListPage : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private KbListFilter _filter = KbListFilter.Empty;
    private KbListFilter? _loadedFor;
    private IReadOnlyList<ProductDto> _products = [];
    private IReadOnlyList<KbCategoryDto> _categories = [];
    private IReadOnlyList<KbArticleRowViewModel> _rows = [];
    private PagedResponse<KbArticleListItemDto>? _page;
    private ITimer? _timer;
    private string _text = string.Empty;
    private string? _appliedSearch;
    private string? _error;
    private string _announcement = string.Empty;
    private bool _loading;
    private bool _lookupsLoaded;
    private bool _disposed;
    private int _loadId;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private IProductsClient ProductsClient { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [SupplyParameterFromQuery(Name = KbQueryKeys.Product)]
    public string? Product { get; set; }

    [SupplyParameterFromQuery(Name = KbQueryKeys.Category)]
    public string? Category { get; set; }

    [SupplyParameterFromQuery(Name = KbQueryKeys.Status)]
    public string? Status { get; set; }

    [SupplyParameterFromQuery(Name = KbQueryKeys.Search)]
    public string? Search { get; set; }

    [SupplyParameterFromQuery(Name = KbQueryKeys.Page)]
    public string? PageNumber { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        // Everything in the query string is untrusted: unparseable or unknown values are dropped rather than sent to the API.
        var filter = KbListFilter.Empty.WithProduct(Product) with
        {
            CategoryId = Guid.TryParse(Category, out var categoryId) ? categoryId : null,
            Status = KbDefaults.CanonicalStatus(Status),
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            Page = int.TryParse(PageNumber, NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 1,
        };
        _filter = filter;

        // Follow the URL (clear filters, the back button) unless the agent is mid-word: a pending timer means the text is newer than the filter.
        if (_timer is null && filter.Search != _appliedSearch)
        {
            _text = filter.Search ?? string.Empty;
        }

        _appliedSearch = filter.Search;

        if (!_lookupsLoaded)
        {
            _lookupsLoaded = true;
            await LoadLookupsAsync();
        }

        if (filter != _loadedFor)
        {
            _loadedFor = filter;
            await LoadAsync();
        }
    }

    private async Task LoadLookupsAsync()
    {
        try
        {
            var products = ProductsClient.ListAsync(_lifetime.Token);
            var categories = Kb.ListCategoriesAsync(_lifetime.Token);
            await Task.WhenAll(products, categories);

            // A failed lookup only means that filter has no options, and a row shows a fixed phrase for a name it cannot resolve; the list itself still works.
            _products = products.Result.IsSuccess ? products.Result.Value : [];
            _categories = categories.Result.IsSuccess ? categories.Result.Value : [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _products = [];
            _categories = [];
        }
    }

    private async Task LoadAsync()
    {
        var loadId = ++_loadId;
        var filter = _filter;
        _loading = true;
        _error = null;
        try
        {
            var result = await Kb.ListAsync(filter.ToRequest(), _lifetime.Token);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return;
            }

            if (result.IsSuccess)
            {
                ShowPage(result.Value, filter);
            }
            else
            {
                // Keep whatever list is already on screen; the alert explains and offers a retry.
                _error = $"{KbCopy.LoadFailed} {result.Errors[0].Message}";
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception) when (loadId == _loadId)
        {
            // Fixed copy only: an exception message can carry a host or a port.
            _error = $"{KbCopy.LoadFailed} {KbCopy.TryAgain}";
        }
        finally
        {
            if (loadId == _loadId)
            {
                _loading = false;
            }
        }
    }

    private void ShowPage(PagedResponse<KbArticleListItemDto> page, KbListFilter filter)
    {
        // A page past the end (a pasted link, or the last articles on it were archived meanwhile) is not an empty list: go to the last real page.
        if (page.Items.Count == 0 && (page.TotalCount > 0 || filter.Page > 1))
        {
            var lastPage = page.TotalCount > 0 ? (int)Math.Ceiling(page.TotalCount / (double)KbDefaults.PageSize) : 1;
            if (lastPage != filter.Page)
            {
                Navigation.NavigateTo((filter with { Page = lastPage }).Uri(), new NavigationOptions { ReplaceHistoryEntry = true });
                return;
            }
        }

        _page = page;
        _rows = [.. page.Items.Select(article => KbArticleRowViewModel.From(article, _products, _categories))];
        _announcement = KbCopy.Count(_rows.Count, page.TotalCount);
    }

    private Task ReloadAsync() => LoadAsync();

    private void OnSearchInput(ChangeEventArgs e)
    {
        _text = e.Value as string ?? string.Empty;
        _timer?.Dispose();
        _timer = Time.CreateTimer(_ => _ = InvokeAsync(CommitSearchAsync), null, KbDefaults.SearchDebounce, Timeout.InfiniteTimeSpan);
    }

    private Task SubmitSearchAsync() => CommitSearchAsync();

    private Task CommitSearchAsync()
    {
        _timer?.Dispose();
        _timer = null;
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        var search = string.IsNullOrWhiteSpace(_text) ? null : _text.Trim();
        return search == _filter.Search ? Task.CompletedTask : CommitAsync(_filter with { Search = search, Page = 1 });
    }

    private Task OnProductChangedAsync(ChangeEventArgs e) => CommitAsync(_filter.WithProduct(e.Value as string));

    private Task OnCategoryChangedAsync(ChangeEventArgs e) => CommitAsync(_filter with { CategoryId = ParseGuid(e), Page = 1 });

    private Task OnStatusChangedAsync(ChangeEventArgs e) => CommitAsync(_filter with { Status = KbDefaults.CanonicalStatus(e.Value as string), Page = 1 });

    private Task OnPageChangedAsync(int page) => CommitAsync(_filter with { Page = page }, replace: false);

    private Task ClearAsync() => CommitAsync(_filter.Cleared());

    private Task CommitAsync(KbListFilter filter, bool replace = true)
    {
        Navigation.NavigateTo(filter.Uri(), new NavigationOptions { ReplaceHistoryEntry = replace });
        return Task.CompletedTask;
    }

    private static Guid? ParseGuid(ChangeEventArgs e) => Guid.TryParse(e.Value as string, out var id) ? id : null;

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

`src/TechStrap.Admin/Features/Kb/KbArticleRowViewModel.cs`

```csharp
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Kb;

/// <summary>One row of the article list, ready to draw. Mapping is simple, so there is no factory (PHASE-08 component boundaries).</summary>
internal sealed record KbArticleRowViewModel(Guid Id, string Href, string Title, string Slug, string ProductName, string CategoryName, string Status, DateTimeOffset UpdatedAt)
{
    public static KbArticleRowViewModel From(KbArticleListItemDto article, IReadOnlyList<ProductDto> products, IReadOnlyList<KbCategoryDto> categories) => new(
        article.Id,
        $"/kb/{article.Id}",
        article.Title,
        article.Slug,
        ProductNameOf(article.ProductId, products),
        article.CategoryId is { } categoryId ? categories.FirstOrDefault(c => c.Id == categoryId)?.Name ?? KbCopy.NoCategory : KbCopy.NoCategory,
        article.Status,
        article.UpdatedAt);

    /// <summary>"Shared" for an article of no product; the product's name; and a fixed phrase for a product the lookups did not return.</summary>
    internal static string ProductNameOf(Guid? productId, IReadOnlyList<ProductDto> products) =>
        productId is not { } id ? KbCopy.Shared : products.FirstOrDefault(p => p.Id == id)?.Name ?? KbCopy.UnknownProduct;
}
```

`src/TechStrap.Admin/Features/Kb/KbCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Kb;

/// <summary>The words of the article list and the parts the knowledge base screens share. Plain, sentence case (docs/BRAND.md section 3).</summary>
public static class KbCopy
{
    public const string Heading = "Knowledge base";
    public const string TableLabel = "Articles";
    public const string Loading = "Loading articles";
    public const string LoadFailed = "Couldn't load the articles.";
    public const string TryAgain = "Try again in a moment.";
    public const string NewArticle = "New article";
    public const string Categories = "Categories";

    public const string SearchLabel = "Search articles";
    public const string AllProducts = "All products";
    public const string AllCategories = "All categories";
    public const string AllStatuses = "Any status";
    public const string ClearFilters = "Clear filters";
    public const string Refresh = "Refresh";

    public const string ColumnTitle = "Title";
    public const string ColumnProduct = "Product";
    public const string ColumnCategory = "Category";
    public const string ColumnStatus = "Status";
    public const string ColumnUpdated = "Updated";

    public const string Shared = "Shared";
    public const string NoCategory = "No category";
    public const string UnknownProduct = "Another product";

    public const string NoMatchHeading = "No articles match";
    public const string NoArticlesHeading = "No articles yet";
    public const string NoArticlesHint = "Write the first article, then publish it for customers to find.";

    public static string Count(int shown, int total) => $"{shown} of {total} articles";
}
```

`src/TechStrap.Admin/Features/Kb/KbDefaults.cs`

```csharp
using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Features.Kb;

/// <summary>The knowledge base screens' named constants: page sizes and debounce times are never literals at a call site (PHASE-08 boundary validation).</summary>
public static class KbDefaults
{
    /// <summary>Articles per page in the list.</summary>
    public const int PageSize = 25;

    /// <summary>Results the reply composer's article picker shows.</summary>
    public const int PickerPageSize = 10;

    /// <summary>How long the list's search box waits after the last keystroke before it asks the API.</summary>
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>How long the editor waits after the last keystroke before it asks the API for a new preview (PHASE-08).</summary>
    public static readonly TimeSpan PreviewDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>How long the article picker waits after the last keystroke before it searches.</summary>
    public static readonly TimeSpan PickerDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>The picture types the editor offers, by extension. The API decides by the file's own bytes; this only keeps an obviously wrong pick from being sent.</summary>
    public static IReadOnlyList<string> ImageExtensions { get; } = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    /// <summary>The statuses the list filter offers; a status in the URL that is not one of these is dropped.</summary>
    public static IReadOnlyList<string> Statuses { get; } = [KbArticleStatuses.Draft, KbArticleStatuses.Published, KbArticleStatuses.Archived];

    /// <summary>The canonical spelling of <paramref name="value"/> from <see cref="Statuses"/> (case-insensitive), or null when it is blank or unknown.</summary>
    public static string? CanonicalStatus(string? value) => Statuses.FirstOrDefault(status => status.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>The query-string keys of the article list. Every filter lives in the URL so a view is linkable and survives a refresh.</summary>
public static class KbQueryKeys
{
    public const string Product = "product";
    public const string Category = "category";
    public const string Status = "status";
    public const string Search = "search";
    public const string Page = "page";
}

/// <summary>
/// Everything that decides which articles the list shows. Record equality is how the page knows a parameter change needs a reload. A product filter shows that product's own articles; the
/// shared ones have their own choice (<see cref="SharedOnly"/>, the word "shared" in the URL), so the two never mix on one screen.
/// </summary>
public sealed record KbListFilter(Guid? ProductId, bool SharedOnly, Guid? CategoryId, string? Status, string? Search, int Page)
{
    /// <summary>The value of the product query key (and of the select) that means the shared articles.</summary>
    public const string SharedValue = "shared";

    public static KbListFilter Empty { get; } = new(null, false, null, null, null, 1);

    public bool HasFilters => ProductId is not null || SharedOnly || CategoryId is not null || Status is not null || !string.IsNullOrWhiteSpace(Search);

    /// <summary>The same list with no filters, no search and the first page.</summary>
    public KbListFilter Cleared() => Empty;

    /// <summary>What the product select shows: the product's id, "shared", or nothing for every product.</summary>
    public string ProductValue => SharedOnly ? SharedValue : ProductId?.ToString() ?? string.Empty;

    /// <summary>The same filter with the product choice replaced by what a query string or a select said. Anything that is neither "shared" nor an id means every product.</summary>
    public KbListFilter WithProduct(string? value) => value switch
    {
        _ when SharedValue.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase) => this with { ProductId = null, SharedOnly = true, Page = 1 },
        _ when Guid.TryParse(value, out var id) => this with { ProductId = id, SharedOnly = false, Page = 1 },
        _ => this with { ProductId = null, SharedOnly = false, Page = 1 },
    };

    /// <summary>The API request for this filter.</summary>
    public ListKbArticlesRequest ToRequest() =>
        new(ProductId, SharedOnly, IncludeShared: false, Status, CategoryId, string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(), Page, KbDefaults.PageSize);

    /// <summary>The URL of this filter: <c>/kb?product=...&amp;status=Draft&amp;page=2</c>. Page 1 and empty values are left out.</summary>
    public string Uri()
    {
        var query = new Dictionary<string, string?>
        {
            [KbQueryKeys.Product] = SharedOnly ? SharedValue : ProductId?.ToString(),
            [KbQueryKeys.Category] = CategoryId?.ToString(),
            [KbQueryKeys.Status] = Status,
            [KbQueryKeys.Search] = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            [KbQueryKeys.Page] = Page > 1 ? Page.ToString(CultureInfo.InvariantCulture) : null,
        };
        return QueryHelpers.AddQueryString("/kb", query.Where(pair => !string.IsNullOrEmpty(pair.Value)).ToDictionary());
    }
}
```

`src/TechStrap.Admin/Features/Kb/KbStatusBadge.razor`

```razor
@* The status in words first (never color alone); the border style only repeats it. A status the Admin does not know is shown as the API sent it, as a plain pill. *@
<span class="@CssClass" data-status="@Status">@Status</span>

@code {
    /// <summary>One of the article statuses of the API: Draft, Published or Archived.</summary>
    [Parameter, EditorRequired]
    public string Status { get; set; } = string.Empty;

    private string CssClass => Status switch
    {
        KbArticleStatuses.Published => "ts-pill ts-pill--on",
        KbArticleStatuses.Archived => "ts-pill ts-pill--off",
        _ => "ts-pill",
    };
}
```

`src/TechStrap.Admin/Features/Kb/_Imports.razor`

```razor
@using TechStrap.Contracts.Kb
```

`src/TechStrap.Admin/Features/Shell/CommandRegistry.cs`

Replace

```csharp
        }

        yield return new PaletteCommand("go-my-settings", ShellCopy.MySettingsLink, PaletteCopy.GoToGroup, () => Go("/account/notifications"));
        yield return new PaletteCommand("go-products", ShellCopy.ProductsLink, PaletteCopy.AdminGroup, () => Go("/settings/products"), AdminOnly: true);
```

with

```csharp
        }

        yield return new PaletteCommand("go-kb", ShellCopy.KbLink, PaletteCopy.GoToGroup, () => Go("/kb"));
        yield return new PaletteCommand("go-my-settings", ShellCopy.MySettingsLink, PaletteCopy.GoToGroup, () => Go("/account/notifications"));
        yield return new PaletteCommand("go-products", ShellCopy.ProductsLink, PaletteCopy.AdminGroup, () => Go("/settings/products"), AdminOnly: true);
```

`src/TechStrap.Admin/Styles/_kb.scss`

```scss
// Knowledge base (PHASE-08): the article list. Working UI like the queue: compact, no mascot. Titles and category names come from agents and wrap instead of stretching the table.

.ts-kb-head-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.ts-kb-list {
  max-width: 1100px;
}
```

`src/TechStrap.Admin/Styles/app.scss`

Replace

```scss
@import "account";
@import "tags";
@import "audit";
@import "ops";
```

with

```scss
@import "account";
@import "tags";
@import "kb";
@import "audit";
@import "ops";
```

- [ ] **Step 4: Run the tests, then prove each pin bites**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: all PASS (`TechStrap.Admin.Tests` 1660 tests, `TechStrap.Architecture.Tests` 253). The architecture tests include `AdminRules` (no `HttpClient` in a component, no inline script or style, the Admin references Contracts and Hosting only) and `ContractNamingRules`.

Mutations, each applied to the finished code, the tests run, and the change reverted:

| Mutation | Failing tests |
| :-- | :-- |
| `NavMenu.razor`: no Knowledge base link | `NavMenuTests.A_plain_agent_sees_the_queue_and_my_settings_and_no_admin_link`, `NavMenuTests.An_admin_sees_the_five_admin_links_in_order_and_my_settings`, `NavMenuTests.The_rail_stays_complete_while_the_session_reloads` (and 1 more; 4 in all) |
| `CommandRegistry.cs`: no go-kb command | `CommandPaletteTests.A_plain_agent_is_offered_the_queue_views_and_my_settings_and_never_an_admin_page`, `CommandPaletteTests.An_admin_is_offered_the_admin_pages_too_and_each_shows_its_group`, `CommandPaletteTests.Esc_closes_the_palette_and_a_new_opening_starts_empty` (and 1 more; 4 in all) |
| `KbArticleListPage.razor.cs`: no _loadId stale guard | `KbArticleListPageTests.A_load_that_was_overtaken_by_a_newer_filter_never_replaces_the_newer_list` |
| `KbClient.cs`: KbClient.ListAsync: pageSize not sent | `KbClientTests.List_sends_the_filters_and_the_paging_and_leaves_blank_filters_out`, `KbListHostTests.Every_agent_sees_the_article_list_with_its_data_and_the_rail_link`, `KbListHostTests.The_filters_in_the_address_reach_the_api_as_the_query_string` |
| `KbDefaults.cs`: KbListFilter.WithProduct: "shared" means every product | `KbArticleListPageTests.Choosing_a_product_a_status_or_shared_goes_to_page_one_with_that_filter_in_the_address`, `KbArticleListPageTests.The_shared_choice_asks_for_the_shared_articles_only`, `KbListFilterTests.Only_a_set_filter_counts_as_a_filter` (and 3 more; 6 in all) |
| `KbArticleListPage.razor.cs`: search with no debounce | `KbArticleListPageTests.A_search_that_is_still_waiting_is_released_with_the_page_so_nothing_fires_after_it_is_gone`, `KbArticleListPageTests.Typing_in_search_issues_one_call_after_the_debounce_and_replaces_the_history_entry` |
| `KbDefaults.cs`: KbDefaults.CanonicalStatus: an unknown status goes through | `KbArticleListPageTests.The_filters_in_the_query_string_become_the_request_and_an_unknown_status_is_dropped`, `KbListFilterTests.A_status_is_one_of_the_three_in_its_canonical_spelling_or_dropped` |
| `KbArticleListPage.razor.cs`: a page past the end is not redirected | `KbArticleListPageTests.A_page_past_the_end_goes_to_the_last_real_page_instead_of_showing_an_empty_list` |
| `KbClient.cs`: KbClient.UploadImageAsync: wrong multipart field name | `KbClientTests.Upload_sends_multipart_with_the_file_in_the_part_named_file_and_a_cleaned_file_name` |
| `KbStatusBadge.razor`: Published has no distinct style | `KbStatusBadgeTests.Each_status_shows_its_word_and_its_own_style` |
| `KbClient.cs`: KbClient.ListAsync: includeShared left to the API default for a product | `KbClientTests.List_sends_the_filters_and_the_paging_and_leaves_blank_filters_out`, `KbListHostTests.The_filters_in_the_address_reach_the_api_as_the_query_string` |
| `KbClient.cs`: KbClient.PublishAsync: version not sent | `KbClientTests.Publish_and_archive_post_to_their_routes_with_no_body_and_an_incomplete_publish_names_its_field` |

- [ ] **Step 5: Build**

```bash
dotnet build TechStrap.slnx -c Release
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/TechStrap.Admin/Clients/ApiClientRegistration.cs \
  src/TechStrap.Admin/Clients/ApiErrorCodes.cs \
  src/TechStrap.Admin/Clients/ApiFields.cs \
  src/TechStrap.Admin/Clients/KbClient.cs \
  src/TechStrap.Admin/Components/Layout/NavMenu.razor \
  src/TechStrap.Admin/Components/Ui/ShellCopy.cs \
  src/TechStrap.Admin/Features/Kb/KbArticleListPage.razor \
  src/TechStrap.Admin/Features/Kb/KbArticleListPage.razor.cs \
  src/TechStrap.Admin/Features/Kb/KbArticleRowViewModel.cs \
  src/TechStrap.Admin/Features/Kb/KbCopy.cs \
  src/TechStrap.Admin/Features/Kb/KbDefaults.cs \
  src/TechStrap.Admin/Features/Kb/KbStatusBadge.razor \
  src/TechStrap.Admin/Features/Kb/_Imports.razor \
  src/TechStrap.Admin/Features/Shell/CommandRegistry.cs \
  src/TechStrap.Admin/Styles/_kb.scss \
  src/TechStrap.Admin/Styles/app.scss \
  tests/TechStrap.Admin.Tests/Clients/KbClientTests.cs \
  tests/TechStrap.Admin.Tests/Clients/KbErrorCodesTests.cs \
  tests/TechStrap.Admin.Tests/Components/CommandPaletteTests.cs \
  tests/TechStrap.Admin.Tests/Components/CommandRegistryTests.cs \
  tests/TechStrap.Admin.Tests/Components/KbArticleListPageTests.cs \
  tests/TechStrap.Admin.Tests/Components/KbListFilterTests.cs \
  tests/TechStrap.Admin.Tests/Components/KbStatusBadgeTests.cs \
  tests/TechStrap.Admin.Tests/Components/LayoutResilienceTests.cs \
  tests/TechStrap.Admin.Tests/Components/NavMenuTests.cs \
  tests/TechStrap.Admin.Tests/KbListHostTests.cs \
  tests/TechStrap.Admin.Tests/ScrollRegionSiteTests.cs \
  tests/TechStrap.Admin.Tests/Support/CountingTimeProvider.cs \
  tests/TechStrap.Admin.Tests/Support/TestData.Kb.cs
git diff --cached --stat
git commit -m "feat(admin): knowledge base client and article list" -m "IKbClient over the typed API connection, the article list with its filters in the address, the Knowledge base rail link and the go-kb palette command. A write is never retried; publish and archive send the loaded version." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

### Task 10: The article editor: toolbar, live preview, picture upload, publish and archive, the leave guard, the conflict banner and "View on portal" (P08-T14, P08-T16, P08-T17, P08-T18; Opus reviews this task)

**Review Focus pins:**
- **(1) Stored XSS through the preview.** The preview pane draws the sanitised HTML the API returned and nothing else: not the text the agent typed, not an error message from the API, nothing of its own besides fixed copy. `KbPreviewPaneTests` (the HTML is drawn exactly as given; a failure keeps the last good preview under fixed words), `MarkdownEditorTests.A_failed_preview_keeps_the_text_and_the_last_good_preview_and_says_so_in_fixed_words` (an API message full of markup never reaches the page), `MarkupStringSiteTests.MarkupString_is_used_in_exactly_two_files_the_message_bubble_and_the_kb_preview_pane` (this is the **one new `MarkupString` site**, and the reason is the preview: the API runs the same renderer and sanitiser as the portal, so the Admin shows exactly what readers will see, and it cannot render Markdown itself because it has no renderer and may not reference Infrastructure).
- **(2) Image upload abuse, the Admin half.** A file that is not a PNG, JPEG, GIF or WebP, or is over 5 MB, is refused before anything is sent, and a picture the API refuses or may not have stored adds nothing to the article: `KbImageUploadButtonTests`.
- **Secrets and PII.** Nothing an agent typed or chose reaches a log: `KbAdminLeakTests` (in `TechStrap.Api.Tests`, next to `AdminLeakTests`, at Verbose, with a positive control that proves the scan can see a term): a search term, an article's text, a preview's text and a picture's file name appear in no log event, even when the preview and the upload fail with a 500; and a session that lapsed on the first data call sends no list request, because that request carries the search text.
- **(4) Lost edits on a version conflict, and Archived to Draft.** A 409 keeps the draft and turns saving off until a reload; the update, publish and archive carry the loaded version; saving an archived article says it is a draft again: `KbArticleEditorPageTests.A_409_keeps_the_draft_shows_the_banner_and_turns_saving_off_until_the_agent_reloads`, `Reloading_after_a_409_brings_in_the_latest_version_and_the_next_save_uses_its_version`, `A_publish_on_a_stale_version_is_a_409_with_the_banner_and_nothing_is_published`, `Saving_an_archived_article_returns_it_to_draft_and_says_so`, `KbArticleEditorViewModelTests.Update_carries_the_loaded_version_and_no_product_or_slug`.

**Files:**
- Modify: `deploy/.env.admin.example`
- Modify: `scripts/tests/ConfigContract.Tests.ps1`
- Modify: `src/TechStrap.Admin/.env.example`
- Create: `src/TechStrap.Admin/Features/Kb/KbArticleEditorPage.razor`
- Create: `src/TechStrap.Admin/Features/Kb/KbArticleEditorPage.razor.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbArticleEditorPresenter.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbArticleEditorViewModel.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbEditorCopy.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbImageUploadButton.razor`
- Create: `src/TechStrap.Admin/Features/Kb/KbImageUploadButton.razor.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbPreviewPane.razor`
- Create: `src/TechStrap.Admin/Features/Kb/KbServiceCollectionExtensions.cs`
- Create: `src/TechStrap.Admin/Features/Kb/MarkdownEditor.razor`
- Create: `src/TechStrap.Admin/Features/Kb/MarkdownEditor.razor.cs`
- Create: `src/TechStrap.Admin/Features/Kb/MarkdownSnippets.cs`
- Modify: `src/TechStrap.Admin/Options/AdminOptionsRegistration.cs`
- Create: `src/TechStrap.Admin/Options/PortalUrlOptions.cs`
- Modify: `src/TechStrap.Admin/Program.cs`
- Modify: `src/TechStrap.Admin/Styles/_kb.scss`
- Modify: `src/TechStrap.Admin/appsettings.json`
- Create: `tests/TechStrap.Admin.Tests/Components/KbArticleEditorPageTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/KbArticleEditorViewModelTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/KbImageUploadButtonTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/KbPreviewPaneTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/MarkdownEditorTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/MarkdownSnippetsTests.cs`
- Create: `tests/TechStrap.Admin.Tests/KbEditorHostTests.cs`
- Create: `tests/TechStrap.Admin.Tests/KbStyleTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/MarkupStringSiteTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Options/PortalUrlOptionsTests.cs`
- Create: `tests/TechStrap.Api.Tests/KbAdminLeakTests.cs`

**Interfaces:**
- Consumes:
  - **Task 9:** `IKbClient`, `KbImageFile`, `ApiErrorCodes.Kb*`, `ApiFields`, `KbDefaults` (`PreviewDebounce`, `ImageExtensions`), `KbCopy`, `KbArticleRowViewModel.ProductNameOf`, `KbStatusBadge`, `TestData.Kb*`, `CountingTimeProvider`, `KbListHostTests` as the host-test pattern.
  - **Task 1 Contracts:** `KbArticleDto`, `CreateKbArticleRequest`, `UpdateKbArticleRequest`, `KbPreviewRequest`, `KbPreviewResponse`, `KbImageUploadResponse`, `KbCategoryDto`, `KbArticleStatuses`, `KbLimits.MaxPreviewChars` (the body limit) and `KbLimits.MaxImageBytes`.
  - **Tasks 2 and 5 behaviour:** the preview route answers `KbPreviewResponse` with the KB-profile HTML (tables, `img` with an absolute http(s) address) and 400 `body-too-long` over `MaxPreviewChars`; the image route answers the absolute public `Url` to put in the Markdown, 400 `kb-image-type-not-allowed` and `kb-image-too-large`, 413 `request-too-large`.
  - **The Admin as it is:** `ConfirmDialog`, `StatusMessageService`, `WriteOutcomes.Classify` and `ApiErrorCodes.IsUncertainWrite`, `AdminOptionsRegistration`, `ResultConversions.ToResult`, `TicketDisplay.FileSize`, `AttachmentFileName`, `LoadingState`, `ErrorState`, the `.ts-conflict`, `.ts-form`, `.ts-field` styles, `AdminFactory` with `settings:`.
- Produces:
  - `KbArticleEditorPage` (`/kb/new`, `/kb/{Id:guid}`), `KbArticleEditorPresenter`, `KbEditorLookups`, `KbEditorData`, `KbArticleEditorViewModel` (with `Take()` snapshots), `KbEditorLimits`, `KbEditorCopy`.
  - `MarkdownEditor` (`Value`, `ValueChanged`, `Id`, `Disabled`, `ErrorMessage`, `OnBlur`), `KbPreviewPane` (`Html`, `Loading`, `ErrorText`), `KbImageUploadButton` and `KbUploadedImage(AltText, Url)`, `MarkdownSnippets`.
  - `PortalUrlOptions` (`TECHSTRAP_PORTAL_PUBLIC_URL`, `IsValidBase`, `ArticleUrl`), `AddKbFeatures`, `ApiFields.CategoryId`.
  - The Admin's own `TECHSTRAP_PORTAL_PUBLIC_URL` setting with the D-043 four edits (see below).

**Rules:**
1. **Routes and who.** `/kb/new` and `/kb/{Id:guid}` are for every agent. A segment that is not an id (`/kb/not-an-id`) is not an article; `/kb/categories` (Task 11) is a separate page.
2. **Create.** The product (or "Shared by every product") and the slug are chosen once and are read-only afterwards (the update request has neither). The slug follows the title until the agent edits it. The category list is `KbEditorLookups.CategoriesFor(product)`: the shared categories and, for a product article, that product's own; a shared article may use only a shared category (the API's `kb-category-scope-mismatch`), and changing the product drops a category the new choice may not use. A successful create goes to `/kb/{id}` replacing the history entry, without reading the article again; the leave guard stays quiet because the form is saved.
3. **Checks.** Title 200, slug 80 (`^[a-z0-9]+(-[a-z0-9]+)*$`), summary 500 and body 200 000 characters (`KbEditorLimits`, the Domain's own numbers; the body limit is `KbLimits.MaxPreviewChars`); title, slug (create only) and body are required. They run on blur and on save, and a failing form sends nothing. The API's answers are mapped by field: `title`, `slug` (create), `summary`, `body`, and `category` or `categoryId` (the publish check names the field `category`, a create or update names the request property `categoryId`) land on the field; `kb-slug-taken` is the slug's message; `kb-category-scope-mismatch` and `kb-category-not-found` are the category's; anything else is said once above the form.
4. **Dirty.** The form is dirty when its fields differ from the last loaded or saved snapshot (`Take()`), so typing the text back clears it. The "Unsaved changes" mark, the Save button, Publish and Archive, `NavigationLock.ConfirmExternalNavigation` and the leave dialog all read it.
5. **Writes** (save, publish, archive). `CancellationToken.None`, a `_disposed` guard, and an `_epoch` guard: a write that finishes after the agent moved to another article says so once in the status bar and changes nothing else. `WriteOutcomes.Classify` decides: `Conflict` shows the banner "This article changed since you opened it" with the form kept and Save, Publish and Archive off until Reload; an uncertain outcome (timeout, unreachable, 5xx) is held, so nothing more is sent until a reload (for a create the page offers the list instead, because the article may exist); `kb-article-not-found` is the gone message; anything else is the field or form message.
6. **Publish and archive.** Publish works on what is stored: the form must be saved and complete, and the hint under the buttons says what is missing ("Save your changes before you publish.", "Choose a category before you publish."). It sends the loaded version and takes the article the API answers (so the version, status and `PublishedAt` are the server's), no second read. `kb-publish-incomplete` (another agent emptied a field) lands on the named field; `article-already-published` and `article-already-archived` reload the article with a message; Archive asks first in a `ConfirmDialog` and an archived article offers Publish and no Archive. Saving an **archived** article returns it to Draft (the answer says so, and so does the status bar).
7. **Leaving.** `NavigationLock.OnBeforeInternalNavigation` prevents an in-app move while the form is dirty and opens "Leave without saving?" (Stay closes it, Leave goes where the agent was going, with the guard off); `ConfirmExternalNavigation` is true exactly while the form is dirty.
8. **Preview.** `MarkdownEditor` owns no text. Whenever its `Value` changes (typing, a toolbar button, an image, a reload) a 300 ms timer is restarted and the call that a newer change replaced is cancelled; when the timer fires the API renders the text (`PreviewAsync`, never retried). An answer that arrives after a newer call started is ignored (`_previewId`). A failed preview keeps the text and the last good preview and shows fixed words; a text over the body limit is not sent (the API would refuse it) and says so; an empty text is not previewed; the timer and the call are released when the component goes (`CountingTimeProvider.LiveTimers` is 0). Nothing the agent typed is rendered as markup anywhere: `KbPreviewPane` is the only place.
9. **Toolbar and pictures.** Bold, italic, link, list and code add their Markdown at the **end** of the text (`MarkdownSnippets.Append`: inline snippets after a space, blocks after a blank line); there is no script and no caret (D-044). "Add image" takes one PNG, JPEG, GIF or WebP file of up to `KbLimits.MaxImageBytes`, checks type and size before anything is sent, uploads with `CancellationToken.None`, reports the answer once (`KbUploadedImage`: the alt text from the file name with the Markdown characters taken out, and the address) and the editor adds `![alt](address)`. A refused, over-limit or maybe-lost upload adds nothing and says so (`kb-image-type-not-allowed`, `kb-image-too-large` and Kestrel's `request-too-large` each have their own words; a timeout, an unreachable API or a 5xx is "may not have finished"); the input is replaced after every pick so the same file can be picked again; a result that arrives after the screen is gone is dropped. An upload changes no article until its address is added, so a lost answer needs no hold: the agent picks the picture again (an unreferenced file is harmless, D-044).
10. **View on portal.** Shown only for a published article, as `{portal}/p/{product key}/kb/{category slug}/{slug}` built by `PortalUrlOptions.ArticleUrl` from the **saved** category (each part escaped), `target="_blank" rel="noopener noreferrer"`. A shared article uses the first product by name (it is reachable under every product). No portal address, a draft, an archived article or a part that cannot be resolved: no link.
11. **The setting.** `TECHSTRAP_PORTAL_PUBLIC_URL` is optional in the Admin and blank by default. It is the same flat key the Api reads, read in `AddAdminOptions` and validated on start (`PortalUrlOptions.IsValidBase`: blank, or an absolute http or https address with no query, fragment or user info; otherwise the Admin refuses to start and the message names the variable). D-043's four edits: `src/TechStrap.Admin/appsettings.json` (blank), `src/TechStrap.Admin/.env.example`, `deploy/.env.admin.example`, and the contract's blank list for the Admin in `scripts/tests/ConfigContract.Tests.ps1`. Compose does not own it (the local compose contract pins the Admin's compose keys), so no compose edit. `scripts/tests/ConfigContract.Tests.ps1` is shared with Task 5, which adds `TECHSTRAP_API_PUBLIC_URL` to the Api's row of the same blank-key table: the two edits are different lines of different rows.
12. **Layout.** Write and Preview are side by side from 992 px; below it two buttons choose one pane (`data-pane`). Pictures and wide tables in the preview never stretch the pane; the article text and the preview wrap outside text.

- [ ] **Step 1: Write the failing tests**

The pure tests first (snippets, the form model, the lookups, the portal address), then the component tests (the pane, the editor, the picture button, the page), then the host tests, the site test and the style test.


`scripts/tests/ConfigContract.Tests.ps1`

Replace

```powershell
        Api    = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'CONNECTIONSTRINGS__TECHSTRAP', 'AUTHENTICATION__JWTBEARER__AUTHORITY', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_API_PUBLIC_URL', 'TECHSTRAP_ADMIN_PUBLIC_URL', 'STORAGE__LOCAL__ROOTPATH')
        Worker = $script:CommonBlank + @('CONNECTIONSTRINGS__TECHSTRAP', 'EMAIL__SMTP__HOST', 'EMAIL__SMTP__USERNAME', 'EMAIL__SMTP__PASSWORD', 'EMAIL__SMTP__DEFAULTFROM', 'EMAIL__SMTP__TLSMODE', 'EMAIL__SMTP__TOTALSENDTIMEOUT', 'EMAILOUTBOX__WORKERID')
        Admin  = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'API__BASEURL', 'AUTH__AUTHORITY', 'AUTH__CLIENTID', 'AUTH__CLIENTSECRET', 'DATAPROTECTION__KEYRINGPATH')
        Portal = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'DATAPROTECTION__KEYRINGPATH')
    }
```

with

```powershell
        Api    = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'CONNECTIONSTRINGS__TECHSTRAP', 'AUTHENTICATION__JWTBEARER__AUTHORITY', 'TECHSTRAP_PORTAL_PUBLIC_URL', 'TECHSTRAP_API_PUBLIC_URL', 'TECHSTRAP_ADMIN_PUBLIC_URL', 'STORAGE__LOCAL__ROOTPATH')
        Worker = $script:CommonBlank + @('CONNECTIONSTRINGS__TECHSTRAP', 'EMAIL__SMTP__HOST', 'EMAIL__SMTP__USERNAME', 'EMAIL__SMTP__PASSWORD', 'EMAIL__SMTP__DEFAULTFROM', 'EMAIL__SMTP__TLSMODE', 'EMAIL__SMTP__TOTALSENDTIMEOUT', 'EMAILOUTBOX__WORKERID')
        Admin  = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'API__BASEURL', 'AUTH__AUTHORITY', 'AUTH__CLIENTID', 'AUTH__CLIENTSECRET', 'DATAPROTECTION__KEYRINGPATH', 'TECHSTRAP_PORTAL_PUBLIC_URL')
        Portal = $script:CommonBlank + @('SECURITYHEADERS__ROBOTSTAG', 'DATAPROTECTION__KEYRINGPATH')
    }
```

`tests/TechStrap.Admin.Tests/Components/KbArticleEditorPageTests.cs`

```csharp
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Options;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The article editor (PHASE-08 T17): create and edit, save with the loaded version, a 409 that keeps the draft, publish and archive, the leave guard and "View on portal". Review Focus 4: a lost edit on a
/// version conflict, and an archived article that becomes a draft again when it is edited.
/// </summary>
public sealed class KbArticleEditorPageTests : AdminComponentTest
{
    private const string LeaveTitle = "Leave without saving?";
    private const string ArchiveTitle = "Archive this article?";
    private static readonly Guid PaperplaneId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid OtherArticleId = Guid.Parse("dddddddd-0000-0000-0000-000000000009");

    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();
    private readonly NavigationManager _navigation;
    private readonly PortalUrlOptions _portal = new() { PublicUrl = "https://help.example.com" };

    public KbArticleEditorPageTests()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>(
            [TestData.Product("Paperplane", PaperplaneId), TestData.Product("Orbitly")]));
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<KbCategoryDto>>(
        [
            TestData.KbCategory("Getting started", "getting-started", null, TestData.GettingStartedId, sortOrder: 10),
            TestData.KbCategory("Account", "account", TestData.OrbitlyId, TestData.AccountCategoryId, sortOrder: 20),
        ]));
        _kb.GetAsync(TestData.ArticleId, Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(Stored()));
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(call => TestData.Ok(new KbPreviewResponse($"<p>{call.Arg<KbPreviewRequest>().BodyMarkdown}</p>")));
        Services.AddSingleton(_kb);
        Services.AddSingleton(_products);
        Services.AddKbFeatures();
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(_portal));
        _navigation = Services.GetRequiredService<NavigationManager>();
    }

    private KbArticleDto _stored = TestData.KbArticle(productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId);

    private KbArticleDto Stored() => _stored;

    private IRenderedComponent<KbArticleEditorPage> RenderEditor(Guid? id = null)
    {
        _navigation.NavigateTo(id is null ? "/kb/new" : $"/kb/{id}");
        return Render<KbArticleEditorPage>(p => p.Add(c => c.Id, id));
    }

    private IRenderedComponent<KbArticleEditorPage> RenderStored(KbArticleDto? article = null)
    {
        _stored = article ?? _stored;
        return RenderEditor(_stored.Id);
    }

    private IEnumerable<object?[]> Calls(string method) => _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == method).Select(c => c.GetArguments());

    private IEnumerable<UpdateKbArticleRequest> Updates() => Calls(nameof(IKbClient.UpdateAsync)).Select(a => (UpdateKbArticleRequest)a[1]!);

    private IEnumerable<CreateKbArticleRequest> Creates() => Calls(nameof(IKbClient.CreateAsync)).Select(a => (CreateKbArticleRequest)a[0]!);

    private static string Value(IRenderedComponent<KbArticleEditorPage> cut, string id) => cut.Find($"#{id}").GetAttribute("value")!;

    private static void Save(IRenderedComponent<KbArticleEditorPage> cut) => cut.Find("form.ts-kb-form").Submit();

    private static IReadOnlyList<string> Alerts(IRenderedComponent<KbArticleEditorPage> cut) => [.. cut.FindAll("[role=alert]").Select(a => a.TextContent.Trim())];

    private static IRenderedComponent<ConfirmDialog> Dialog(IRenderedComponent<KbArticleEditorPage> cut, string title) =>
        cut.FindComponents<ConfirmDialog>().Single(d => d.Instance.Title == title);

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<KbArticleEditorPage> cut, string label) =>
        cut.FindAll(".ts-kb-actions button").Single(b => b.TextContent.Trim() == label);

    private void FillValidNewArticle(IRenderedComponent<KbArticleEditorPage> cut)
    {
        cut.Find("#ts-kb-product").Change(TestData.OrbitlyId.ToString());
        cut.Find("#ts-kb-category").Change(TestData.AccountCategoryId.ToString());
        cut.Find("#ts-kb-title").Input("  Reset your password  ");
        cut.Find("#ts-kb-summary").Input("How to reset it.");
        cut.Find("#ts-kb-body").Input("# Steps");
    }

    // ---- creating ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_new_article_asks_for_a_product_a_category_a_title_a_slug_a_summary_and_the_text_and_reads_no_article()
    {
        var cut = RenderEditor();

        cut.Find("h1").TextContent.ShouldBe("New article");
        cut.FindAll("#ts-kb-product option").Select(o => o.TextContent).ShouldBe(["Shared by every product", "Paperplane", "Orbitly"]);
        cut.FindAll("#ts-kb-category option").Select(o => o.TextContent).ShouldBe(["No category", "Getting started"]);
        cut.FindAll(".ts-kb-status .ts-pill").ShouldBeEmpty();
        cut.Find("#ts-kb-slug").ShouldNotBeNull();
        cut.Find("#ts-kb-body").ShouldNotBeNull();
        Button(cut, "Create draft").HasAttribute("disabled").ShouldBeFalse();
        cut.FindAll(".ts-kb-actions button").Select(b => b.TextContent.Trim()).ShouldBe(["Create draft"]);
        _kb.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_slug_follows_the_title_until_the_agent_edits_it()
    {
        var cut = RenderEditor();

        cut.Find("#ts-kb-title").Input("Reset your password");
        Value(cut, "ts-kb-slug").ShouldBe("reset-your-password");

        cut.Find("#ts-kb-slug").Input("reset");
        cut.Find("#ts-kb-title").Input("Reset your account password");
        Value(cut, "ts-kb-slug").ShouldBe("reset");
    }

    [Fact]
    public void Choosing_a_product_offers_its_categories_and_the_shared_ones_and_changing_it_drops_a_category_it_may_not_use()
    {
        var cut = RenderEditor();

        cut.Find("#ts-kb-product").Change(TestData.OrbitlyId.ToString());
        cut.FindAll("#ts-kb-category option").Select(o => o.TextContent).ShouldBe(["No category", "Getting started", "Account"]);

        cut.Find("#ts-kb-category").Change(TestData.AccountCategoryId.ToString());
        cut.Find("#ts-kb-product").Change(string.Empty);

        cut.FindAll("#ts-kb-category option").Select(o => o.TextContent).ShouldBe(["No category", "Getting started"]);
        cut.Find("#ts-kb-category option[selected]").TextContent.ShouldBe("No category");
    }

    [Fact]
    public void Creating_sends_the_trimmed_text_never_cancels_and_goes_to_the_new_article_without_reading_it_again()
    {
        var created = TestData.KbArticle("Reset your password", "reset-your-password", productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, id: OtherArticleId);
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(created));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => Creates().Count().ShouldBe(1));
        Creates().Single().ShouldBe(new CreateKbArticleRequest(TestData.OrbitlyId, TestData.AccountCategoryId, "reset-your-password", "Reset your password", "How to reset it.", "# Steps"));
        ((CancellationToken)Calls(nameof(IKbClient.CreateAsync)).Single()[1]!).CanBeCanceled.ShouldBeFalse();
        _navigation.Uri.ShouldEndWith($"/kb/{OtherArticleId}");
        ((BunitNavigationManager)_navigation).History.Last().Options.ReplaceHistoryEntry.ShouldBeTrue();
        StatusMessages.Current.ShouldBe("Created the draft Reset your password");
        _kb.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        cut.Find("h1").TextContent.ShouldBe("Reset your password");
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Draft");
    }

    [Fact]
    public void Saving_an_empty_form_sends_nothing_and_says_what_is_missing()
    {
        var cut = RenderEditor();

        Save(cut);

        Creates().ShouldBeEmpty();
        Alerts(cut).ShouldBe(["Enter a title.", "Enter a slug.", "Write the article before you save it."]);
        cut.Find("#ts-kb-title").GetAttribute("aria-invalid").ShouldBe("true");
        cut.Find("#ts-kb-title").GetAttribute("aria-describedby").ShouldBe("ts-kb-title-error");
    }

    [Fact]
    public void A_slug_that_is_not_lower_case_words_is_refused_before_it_is_sent()
    {
        var cut = RenderEditor();
        FillValidNewArticle(cut);
        cut.Find("#ts-kb-slug").Input("Reset Password");

        Save(cut);

        Creates().ShouldBeEmpty();
        Alerts(cut).ShouldBe(["Use lower-case letters, numbers and single hyphens, up to 80 characters."]);
    }

    [Fact]
    public void A_slug_that_is_taken_is_a_field_error_and_the_form_is_kept_for_another_try()
    {
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.KbSlugTaken, "taken", ResultErrorKind.Conflict));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => Alerts(cut).ShouldBe(["Another article already uses this slug. Slugs are shared across products, so pick another."]));
        Value(cut, "ts-kb-title").ShouldBe("  Reset your password  ");
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# Steps");
        cut.Find("#ts-kb-slug").GetAttribute("aria-invalid").ShouldBe("true");

        cut.Find("#ts-kb-slug").Input("reset-your-password-2");
        Save(cut);
        cut.WaitForAssertion(() => Creates().Count().ShouldBe(2));
    }

    [Fact]
    public void A_category_the_api_refuses_for_the_product_is_a_field_error_on_the_category()
    {
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.KbCategoryScopeMismatch, "no", ResultErrorKind.Conflict));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => Alerts(cut).ShouldBe(["A shared article can only use a shared category."]));
    }

    [Theory]
    [InlineData(ApiErrorCodes.KbCategoryNotFound, "categoryId", "That category no longer exists. Choose another.")]
    [InlineData("kb-category-scope-mismatch", "categoryId", "A shared article can only use a shared category.")]
    public void A_category_error_the_api_names_by_the_request_property_lands_on_the_category_select(string code, string target, string message)
    {
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<KbArticleDto>.Failure(new ResultError(code, "from the API", ResultErrorKind.Validation, target)));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => Alerts(cut).ShouldBe([message]));
    }

    [Fact]
    public void A_400_names_its_field_and_a_message_for_no_field_is_shown_above_the_form()
    {
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(Result<KbArticleDto>.Failure(
            new ResultError("validation-failed", "Too long.", ResultErrorKind.Validation, ApiFields.Title),
            [new ResultError("validation-failed", "Something else is wrong.", ResultErrorKind.Validation, "unknown-field")]));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => cut.Find("#ts-kb-title-error").TextContent.ShouldBe("Too long."));
        cut.Find("p.ts-form-error").TextContent.ShouldBe("Something else is wrong.");
    }

    [Fact]
    public void A_create_with_an_unknown_outcome_is_held_nothing_is_sent_twice_and_the_list_is_offered()
    {
        _kb.CreateAsync(Arg.Any<CreateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ApiTimeout, "slow"));
        var cut = RenderEditor();
        FillValidNewArticle(cut);

        Save(cut);

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The article may have been created."));
        cut.Find(".ts-conflict a").GetAttribute("href").ShouldBe("/kb");
        Button(cut, "Create draft").HasAttribute("disabled").ShouldBeTrue();
        Save(cut);
        Creates().Count().ShouldBe(1);
        Value(cut, "ts-kb-title").ShouldBe("  Reset your password  ");
    }

    // ---- editing -------------------------------------------------------------------------------------------------

    [Fact]
    public void An_article_is_loaded_with_its_fields_its_status_and_a_read_only_product_and_slug()
    {
        var cut = RenderStored();

        cut.Find("h1").TextContent.ShouldBe("Reset your password");
        Value(cut, "ts-kb-title").ShouldBe("Reset your password");
        cut.Find("#ts-kb-summary").GetAttribute("value").ShouldBe("How to reset it.");
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# Steps");
        cut.Find("#ts-kb-category option[selected]").TextContent.ShouldBe("Account");
        cut.Find("dl.ts-readonly").TextContent.ShouldContain("Orbitly");
        cut.Find("dl.ts-readonly code").TextContent.ShouldBe("reset-password");
        cut.FindAll("#ts-kb-product").ShouldBeEmpty();
        cut.FindAll("#ts-kb-slug").ShouldBeEmpty();
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Draft");
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void A_shared_article_says_shared_and_an_article_that_is_missing_says_so_and_asks_for_no_form()
    {
        var shared = RenderStored(TestData.KbArticle(productId: null, categoryId: TestData.GettingStartedId));
        shared.Find("dl.ts-readonly").TextContent.ShouldContain("Shared");

        _kb.GetAsync(OtherArticleId, Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.KbArticleNotFound, "gone", ResultErrorKind.NotFound));
        var gone = RenderEditor(OtherArticleId);

        gone.Find(".ts-state--error").TextContent.ShouldContain("This article no longer exists.");
        gone.FindAll("form").ShouldBeEmpty();
        gone.FindAll(".ts-state--error button").ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_load_says_so_with_the_api_message_and_retry_loads_again()
    {
        _kb.GetAsync(TestData.ArticleId, Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>("api-error", "The API is unavailable."), TestData.Ok(Stored()));

        var cut = RenderEditor(TestData.ArticleId);

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load this article. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();
        cut.WaitForAssertion(() => Value(cut, "ts-kb-title").ShouldBe("Reset your password"));
    }

    [Fact]
    public void A_lookup_that_fails_is_a_failed_load_and_never_reads_as_article_not_found()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Fail<IReadOnlyList<ProductDto>>("product-not-found", "No such product.", ResultErrorKind.NotFound));

        var cut = RenderEditor(TestData.ArticleId);

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load this article.");
        cut.Markup.ShouldNotContain("no longer exists");
    }

    // Review Focus 4: the loaded version always travels.
    [Fact]
    public void Saving_sends_the_loaded_version_and_the_edited_fields_and_the_answer_replaces_the_form()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => TestData.Ok(TestData.KbArticle(call.Arg<UpdateKbArticleRequest>().Title!, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4)));
        var cut = RenderStored();

        cut.Find("#ts-kb-title").Input("  A better title ");
        cut.Find("#ts-kb-body").Input("# New steps");
        Button(cut, "Save").HasAttribute("disabled").ShouldBeFalse();
        Save(cut);

        cut.WaitForAssertion(() => Updates().Count().ShouldBe(1));
        Updates().Single().ShouldBe(new UpdateKbArticleRequest(TestData.AccountCategoryId, "A better title", "How to reset it.", "# New steps", 3u));
        ((CancellationToken)Calls(nameof(IKbClient.UpdateAsync)).Single()[2]!).CanBeCanceled.ShouldBeFalse();
        StatusMessages.Current.ShouldBe("Saved A better title");
        cut.WaitForAssertion(() => Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue());
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
        cut.Find("h1").TextContent.ShouldBe("A better title");

        // The next save carries the new version.
        cut.Find("#ts-kb-title").Input("Another");
        Save(cut);
        cut.WaitForAssertion(() => Updates().Count().ShouldBe(2));
        Updates().Last().Version.ShouldBe(4u);
    }

    [Fact]
    public void The_unsaved_mark_shows_while_the_form_differs_and_goes_when_the_text_is_typed_back()
    {
        var cut = RenderStored();
        cut.FindAll(".ts-dirty").ShouldBeEmpty();

        cut.Find("#ts-kb-title").Input("Reset your password!");
        cut.Find(".ts-dirty").TextContent.ShouldBe("Unsaved changes");

        cut.Find("#ts-kb-title").Input("Reset your password");
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
    }

    // Review Focus 4: an edit on a stale version is a 409, and the agent's text is never lost.
    [Fact]
    public void A_409_keeps_the_draft_shows_the_banner_and_turns_saving_off_until_the_agent_reloads()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ConcurrencyConflict, "This article changed.", ResultErrorKind.Conflict));
        var cut = RenderStored();
        cut.Find("#ts-kb-body").Input("# My careful edit");

        Save(cut);

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("This article changed since you opened it."));
        cut.Find(".ts-conflict").TextContent.ShouldContain("Your edits are still in the form.");
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# My careful edit");
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
        cut.Find(".ts-dirty").ShouldNotBeNull();
        Save(cut);
        Updates().Count().ShouldBe(1);
    }

    [Fact]
    public void Reloading_after_a_409_brings_in_the_latest_version_and_the_next_save_uses_its_version()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ConcurrencyConflict, "This article changed.", ResultErrorKind.Conflict), TestData.Ok(TestData.KbArticle(version: 9)));
        var cut = RenderStored();
        cut.Find("#ts-kb-body").Input("# My careful edit");
        Save(cut);
        cut.WaitForAssertion(() => cut.Find(".ts-conflict").ShouldNotBeNull());
        _stored = TestData.KbArticle(title: "Their title", productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, body: "# Their text", version: 8);

        cut.Find(".ts-conflict button").Click();

        cut.WaitForAssertion(() => Value(cut, "ts-kb-title").ShouldBe("Their title"));
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# Their text");
        cut.FindAll(".ts-conflict").ShouldBeEmpty();
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
        cut.Find("#ts-kb-title").Input("Mine again");
        Save(cut);
        cut.WaitForAssertion(() => Updates().Count().ShouldBe(2));
        Updates().Last().Version.ShouldBe(8u);
    }

    // Review Focus 4: an archived article that is edited becomes a draft again, and the editor says so.
    [Fact]
    public void Saving_an_archived_article_returns_it_to_draft_and_says_so()
    {
        var archived = TestData.KbArticle(status: KbArticleStatuses.Archived, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, publishedAt: TestData.Now.AddDays(-30));
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.KbArticle(status: KbArticleStatuses.Draft, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4, publishedAt: TestData.Now.AddDays(-30))));
        var cut = RenderStored(archived);
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Archived");

        cut.Find("#ts-kb-title").Input("Reopened");
        Save(cut);

        cut.WaitForAssertion(() => cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Draft"));
        StatusMessages.Current.ShouldBe("Saved. This archived article is a draft again: publish it to put it back on the portal.");
        cut.Find(".ts-kb-status .ts-pill").GetAttribute("data-status").ShouldBe("Draft");
    }

    [Fact]
    public void A_save_with_an_unknown_outcome_is_held_until_a_reload_and_nothing_is_sent_twice()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ApiUnavailable, "down"));
        var cut = RenderStored();
        cut.Find("#ts-kb-title").Input("Edited");
        Save(cut);

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The change may have gone through."));
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
        Save(cut);
        Updates().Count().ShouldBe(1);
        Value(cut, "ts-kb-title").ShouldBe("Edited");

        _stored = TestData.KbArticle(title: "Edited", productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4);
        cut.Find(".ts-conflict button").Click();

        cut.WaitForAssertion(() => cut.FindAll(".ts-conflict").ShouldBeEmpty());
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
    }

    [Fact]
    public void An_article_deleted_meanwhile_turns_a_save_into_the_gone_message()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.KbArticleNotFound, "gone", ResultErrorKind.NotFound));
        var cut = RenderStored();
        cut.Find("#ts-kb-title").Input("Edited");

        Save(cut);

        cut.WaitForAssertion(() => cut.Find(".ts-state--error").TextContent.ShouldContain("This article no longer exists."));
        cut.FindAll("form").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_save_that_finishes_after_the_page_is_gone_changes_nothing_and_says_nothing()
    {
        var pending = new TaskCompletionSource<Result<KbArticleDto>>();
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderStored();
        cut.Find("#ts-kb-title").Input("Edited");
        var saving = Task.Run(() => Save(cut), Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => Updates().Count().ShouldBe(1));

        cut.Instance.Dispose();
        pending.SetResult(TestData.Ok(TestData.KbArticle("Edited", version: 4)));
        await saving;
        await cut.InvokeAsync(() => { });

        StatusMessages.Current.ShouldBeNull();
    }

    [Fact]
    public async Task A_save_that_finishes_after_the_agent_moved_to_another_article_says_so_once_and_leaves_the_new_screen_alone()
    {
        var pending = new TaskCompletionSource<Result<KbArticleDto>>();
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        _kb.GetAsync(OtherArticleId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbArticle("The other one", "other", id: OtherArticleId, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId)));
        var cut = RenderStored();
        cut.Find("#ts-kb-title").Input("Edited");
        var saving = Task.Run(() => Save(cut), Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => Updates().Count().ShouldBe(1));

        // The leave guard would ask: the agent chose to leave, which this test does by changing the parameter directly.
        cut.Render(p => p.Add(c => c.Id, OtherArticleId));
        cut.WaitForAssertion(() => Value(cut, "ts-kb-title").ShouldBe("The other one"));
        pending.SetResult(TestData.Ok(TestData.KbArticle("Edited", version: 4)));
        await saving;

        cut.WaitForAssertion(() => StatusMessages.Current.ShouldBe("Saved Edited"));
        Value(cut, "ts-kb-title").ShouldBe("The other one");
        cut.FindAll(".ts-dirty").ShouldBeEmpty();
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void A_load_that_was_overtaken_by_another_article_never_replaces_the_newer_screen()
    {
        var slow = new TaskCompletionSource<Result<KbArticleDto>>();
        _kb.GetAsync(TestData.ArticleId, Arg.Any<CancellationToken>()).Returns(_ => slow.Task);
        _kb.GetAsync(OtherArticleId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbArticle("Newer", "newer", id: OtherArticleId, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId)));
        var cut = RenderEditor(TestData.ArticleId);

        cut.Render(p => p.Add(c => c.Id, OtherArticleId));
        cut.WaitForAssertion(() => Value(cut, "ts-kb-title").ShouldBe("Newer"));
        slow.SetResult(TestData.Ok(TestData.KbArticle("Older", "older", productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId)));

        cut.WaitForAssertion(() => Value(cut, "ts-kb-title").ShouldBe("Newer"));
        cut.Markup.ShouldNotContain("Older");
    }

    // ---- the preview ---------------------------------------------------------------------------------------------

    [Fact]
    public void Typing_in_the_article_text_previews_it_through_the_api_after_the_debounce()
    {
        var cut = RenderStored();
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p># Steps</p>"));

        cut.Find("#ts-kb-body").Input("# Steps\nMore");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p># Steps\nMore</p>"));
    }

    // ---- publish -------------------------------------------------------------------------------------------------

    [Fact]
    public void Publish_is_offered_only_once_the_article_is_saved_and_complete_and_the_hint_says_what_is_missing()
    {
        var cut = RenderStored();
        Button(cut, "Publish").HasAttribute("disabled").ShouldBeFalse();
        cut.FindAll("#ts-kb-publish-hint").ShouldBeEmpty();

        cut.Find("#ts-kb-title").Input("Edited");
        Button(cut, "Publish").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("#ts-kb-publish-hint").TextContent.ShouldBe("Save your changes before you publish.");

        var noCategory = RenderStored(TestData.KbArticle(productId: TestData.OrbitlyId, categoryId: null));
        Button(noCategory, "Publish").HasAttribute("disabled").ShouldBeTrue();
        noCategory.Find("#ts-kb-publish-hint").TextContent.ShouldBe("Choose a category before you publish.");
    }

    [Fact]
    public void Publishing_sends_the_loaded_version_shows_the_answer_and_a_portal_link_and_reads_nothing_again()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4, publishedAt: TestData.Now)));
        var cut = RenderStored();
        cut.FindAll(".ts-kb-portal-link").ShouldBeEmpty();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Published"));
        var call = Calls(nameof(IKbClient.PublishAsync)).Single();
        call[1].ShouldBe(3u);
        ((CancellationToken)call[2]!).CanBeCanceled.ShouldBeFalse();
        Calls(nameof(IKbClient.GetAsync)).Count().ShouldBe(1);
        StatusMessages.Current.ShouldBe("Published Reset your password");
        cut.FindAll(".ts-kb-actions button").Select(b => b.TextContent.Trim()).ShouldBe(["Save", "Archive"]);
        var link = cut.Find("a.ts-kb-portal-link");
        link.GetAttribute("href").ShouldBe("https://help.example.com/p/orbitly/kb/account/reset-password");
        link.GetAttribute("target").ShouldBe("_blank");
        link.GetAttribute("rel").ShouldBe("noopener noreferrer");
        link.TextContent.ShouldBe("View on portal");
    }

    [Fact]
    public void Publish_that_the_api_says_is_incomplete_names_the_field_and_changes_nothing()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>())
            .Returns(Result<KbArticleDto>.Failure(new ResultError(ApiErrorCodes.KbPublishIncomplete, "no category", ResultErrorKind.Validation, ApiFields.Category)));
        var cut = RenderStored();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find("p.ts-form-error").TextContent.ShouldBe("Choose a category before you publish."));
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Draft");
        Calls(nameof(IKbClient.GetAsync)).Count().ShouldBe(1);
    }

    [Fact]
    public void A_publish_with_an_unknown_outcome_is_held_until_a_reload()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ApiTimeout, "slow"));
        var cut = RenderStored();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The publish may have gone through."));
        Button(cut, "Publish").HasAttribute("disabled").ShouldBeTrue();
        Button(cut, "Archive").HasAttribute("disabled").ShouldBeTrue();
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
        Button(cut, "Publish").Click();
        Calls(nameof(IKbClient.PublishAsync)).Count().ShouldBe(1);
    }

    // Review Focus 4: publishing an article another agent has changed meanwhile is a 409, never a publish of text nobody here has seen.
    [Fact]
    public void A_publish_on_a_stale_version_is_a_409_with_the_banner_and_nothing_is_published()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ConcurrencyConflict, "changed", ResultErrorKind.Conflict));
        var cut = RenderStored();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("This article changed since you opened it."));
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Draft");
        Button(cut, "Publish").HasAttribute("disabled").ShouldBeTrue();
        Button(cut, "Archive").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void An_article_someone_else_already_published_is_reloaded_with_a_message()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _stored = TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4);
            return TestData.Fail<KbArticleDto>("article-already-published", "already", ResultErrorKind.Conflict);
        });
        var cut = RenderStored();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Published"));
        StatusMessages.Current.ShouldBe("Someone published this article already. It has been reloaded.");
    }

    [Fact]
    public void If_the_read_after_an_already_published_answer_fails_the_form_is_held_until_a_reload()
    {
        _kb.PublishAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>("article-already-published", "already", ResultErrorKind.Conflict));
        _kb.GetAsync(TestData.ArticleId, Arg.Any<CancellationToken>()).Returns(TestData.Ok(Stored()), TestData.Fail<KbArticleDto>("api-error", "down"));
        var cut = RenderStored();

        Button(cut, "Publish").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The article could not be reloaded."));
        Button(cut, "Save").HasAttribute("disabled").ShouldBeTrue();
    }

    // ---- archive -------------------------------------------------------------------------------------------------

    [Fact]
    public void Archive_asks_first_and_sends_nothing_until_the_dialog_is_confirmed()
    {
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));
        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeFalse();

        Button(cut, "Archive").Click();

        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeTrue();
        Dialog(cut, ArchiveTitle).Markup.ShouldContain("removed from the portal, from search and from the sitemap");
        _kb.DidNotReceive().ArchiveAsync(Arg.Any<Guid>(), Arg.Any<uint>(), Arg.Any<CancellationToken>());

        Dialog(cut, ArchiveTitle).FindAll(".ts-dialog-actions button")[0].Click();

        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeFalse();
        _kb.DidNotReceive().ArchiveAsync(Arg.Any<Guid>(), Arg.Any<uint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Confirming_archives_with_the_loaded_version_and_shows_the_answer_archived_without_a_portal_link()
    {
        _kb.ArchiveAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(
            TestData.Ok(TestData.KbArticle(status: KbArticleStatuses.Archived, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 5, publishedAt: TestData.Now)));
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, publishedAt: TestData.Now));
        cut.Find("a.ts-kb-portal-link").ShouldNotBeNull();
        Button(cut, "Archive").Click();

        Dialog(cut, ArchiveTitle).FindAll(".ts-dialog-actions button")[1].Click();

        cut.WaitForAssertion(() => cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Archived"));
        var call = Calls(nameof(IKbClient.ArchiveAsync)).Single();
        call[1].ShouldBe(3u);
        ((CancellationToken)call[2]!).CanBeCanceled.ShouldBeFalse();
        Calls(nameof(IKbClient.GetAsync)).Count().ShouldBe(1);
        StatusMessages.Current.ShouldBe("Archived Reset your password");
        cut.FindAll("a.ts-kb-portal-link").ShouldBeEmpty();
        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeFalse();
        cut.FindAll(".ts-kb-actions button").Select(b => b.TextContent.Trim()).ShouldBe(["Save", "Publish"]);
    }

    [Fact]
    public void An_archive_with_an_unknown_outcome_closes_the_dialog_and_holds_the_form_until_a_reload()
    {
        _kb.ArchiveAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ApiUnavailable, "down"));
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));
        Button(cut, "Archive").Click();

        Dialog(cut, ArchiveTitle).FindAll(".ts-dialog-actions button")[1].Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("The archive may have gone through."));
        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeFalse();
        Button(cut, "Archive").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void An_archive_on_a_stale_version_is_a_409_with_the_banner_and_the_dialog_closes()
    {
        _kb.ArchiveAsync(TestData.ArticleId, Arg.Any<uint>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbArticleDto>(ApiErrorCodes.ConcurrencyConflict, "changed", ResultErrorKind.Conflict));
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));
        Button(cut, "Archive").Click();

        Dialog(cut, ArchiveTitle).FindAll(".ts-dialog-actions button")[1].Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict").TextContent.ShouldContain("This article changed since you opened it."));
        Dialog(cut, ArchiveTitle).Instance.Open.ShouldBeFalse();
        cut.Find(".ts-kb-status .ts-pill").TextContent.ShouldBe("Published");
    }

    [Fact]
    public void An_archived_article_offers_publish_again_and_no_archive()
    {
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Archived, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));

        cut.FindAll(".ts-kb-actions button").Select(b => b.TextContent.Trim()).ShouldBe(["Save", "Publish"]);
    }

    // ---- leaving -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_form_that_has_not_changed_leaves_without_asking()
    {
        var cut = RenderStored();
        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeFalse();

        _navigation.NavigateTo("/queue");

        _navigation.Uri.ShouldEndWith("/queue");
        Dialog(cut, LeaveTitle).Instance.Open.ShouldBeFalse();
    }

    [Fact]
    public void A_changed_form_asks_before_an_in_app_move_and_before_the_browser_leaves()
    {
        var cut = RenderStored();
        cut.Find("#ts-kb-body").Input("# Unsaved");

        cut.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation.ShouldBeTrue();
        _navigation.NavigateTo("/queue");

        _navigation.Uri.ShouldEndWith($"/kb/{TestData.ArticleId}");
        Dialog(cut, LeaveTitle).Instance.Open.ShouldBeTrue();
        Dialog(cut, LeaveTitle).Markup.ShouldContain("If you leave now they are lost.");
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# Unsaved");
    }

    [Fact]
    public void Staying_keeps_the_page_and_the_text_and_leaving_goes_where_the_agent_was_going()
    {
        var cut = RenderStored();
        cut.Find("#ts-kb-body").Input("# Unsaved");
        _navigation.NavigateTo("/queue");

        Dialog(cut, LeaveTitle).FindAll(".ts-dialog-actions button")[0].Click();

        Dialog(cut, LeaveTitle).Instance.Open.ShouldBeFalse();
        _navigation.Uri.ShouldEndWith($"/kb/{TestData.ArticleId}");
        cut.Find("#ts-kb-body").GetAttribute("value").ShouldBe("# Unsaved");

        _navigation.NavigateTo("/queue");
        Dialog(cut, LeaveTitle).FindAll(".ts-dialog-actions button")[1].Click();

        _navigation.Uri.ShouldEndWith("/queue");
    }

    [Fact]
    public void After_a_save_the_form_is_clean_and_leaving_does_not_ask()
    {
        _kb.UpdateAsync(TestData.ArticleId, Arg.Any<UpdateKbArticleRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.KbArticle("Edited", productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId, version: 4)));
        var cut = RenderStored();
        cut.Find("#ts-kb-title").Input("Edited");
        Save(cut);
        cut.WaitForAssertion(() => cut.FindAll(".ts-dirty").ShouldBeEmpty());

        _navigation.NavigateTo("/kb");

        _navigation.Uri.ShouldEndWith("/kb");
        Dialog(cut, LeaveTitle).Instance.Open.ShouldBeFalse();
    }

    [Fact]
    public void A_new_article_with_text_asks_before_it_is_left_and_an_empty_one_does_not()
    {
        var empty = RenderEditor();
        _navigation.NavigateTo("/kb");
        _navigation.Uri.ShouldEndWith("/kb");
        Dialog(empty, LeaveTitle).Instance.Open.ShouldBeFalse();

        var typed = RenderEditor();
        typed.Find("#ts-kb-title").Input("Half written");
        _navigation.NavigateTo("/queue");

        Dialog(typed, LeaveTitle).Instance.Open.ShouldBeTrue();
        _navigation.Uri.ShouldEndWith("/kb/new");
    }

    // ---- View on portal ------------------------------------------------------------------------------------------

    [Fact]
    public void A_draft_has_no_portal_link_and_neither_does_anything_when_no_portal_address_is_set()
    {
        RenderStored().FindAll("a.ts-kb-portal-link").ShouldBeEmpty();

        _portal.PublicUrl = null;
        var published = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));

        published.FindAll("a.ts-kb-portal-link").ShouldBeEmpty();
        _portal.PublicUrl = string.Empty;
        RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId)).FindAll("a.ts-kb-portal-link").ShouldBeEmpty();
    }

    [Fact]
    public void A_shared_article_is_linked_under_the_first_product_by_name()
    {
        var cut = RenderStored(TestData.KbArticle("Welcome", "welcome", KbArticleStatuses.Published, productId: null, categoryId: TestData.GettingStartedId));

        cut.Find("a.ts-kb-portal-link").GetAttribute("href").ShouldBe("https://help.example.com/p/orbitly/kb/getting-started/welcome");
    }

    [Fact]
    public void The_portal_link_uses_the_saved_category_and_not_one_picked_but_not_saved()
    {
        var cut = RenderStored(TestData.KbArticle(status: KbArticleStatuses.Published, productId: TestData.OrbitlyId, categoryId: TestData.AccountCategoryId));

        cut.Find("#ts-kb-category").Change(TestData.GettingStartedId.ToString());

        cut.Find("a.ts-kb-portal-link").GetAttribute("href").ShouldBe("https://help.example.com/p/orbitly/kb/account/reset-password");
    }
}
```

`tests/TechStrap.Admin.Tests/Components/KbArticleEditorViewModelTests.cs`

```csharp
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The editor's form model checks with the server's limits, builds the two requests and knows what publishing still needs.</summary>
public sealed class KbArticleEditorViewModelTests
{
    private static KbArticleEditorViewModel Complete() => new()
    {
        Id = TestData.ArticleId, ProductId = TestData.OrbitlyId, CategoryId = TestData.AccountCategoryId, Slug = "reset-password", Title = "Reset your password", Summary = "How.", Body = "# Steps", Version = 4,
    };

    [Fact]
    public void A_loaded_article_fills_the_form_and_a_missing_summary_is_an_empty_string()
    {
        var model = KbArticleEditorViewModel.From(TestData.KbArticle(summary: "x") with { Summary = null, Status = KbArticleStatuses.Archived, Version = 9 });

        model.Summary.ShouldBe(string.Empty);
        model.Status.ShouldBe("Archived");
        model.Version.ShouldBe(9u);
        model.Id.ShouldBe(TestData.ArticleId);
    }

    [Fact]
    public void Create_sends_the_trimmed_text_a_blank_summary_as_null_and_the_body_untouched()
    {
        var model = Complete();
        model.Title = "  Reset  ";
        model.Slug = " reset ";
        model.Summary = "   ";
        model.Body = "  # Steps\n";

        var request = model.ToCreateRequest();

        request.ShouldBe(new CreateKbArticleRequest(TestData.OrbitlyId, TestData.AccountCategoryId, "reset", "Reset", null, "  # Steps\n"));
    }

    // Review Focus 4: the version the article was loaded with always travels, and the product and the slug never do.
    [Fact]
    public void Update_carries_the_loaded_version_and_no_product_or_slug()
    {
        var request = Complete().ToUpdateRequest();

        request.ShouldBe(new UpdateKbArticleRequest(TestData.AccountCategoryId, "Reset your password", "How.", "# Steps", 4u));
        typeof(UpdateKbArticleRequest).GetProperties().Select(p => p.Name).ShouldBe(["CategoryId", "Title", "Summary", "BodyMarkdown", "Version"]);
    }

    [Fact]
    public void The_snapshot_is_equal_until_a_field_changes_and_equal_again_when_the_text_is_typed_back()
    {
        var model = Complete();
        var saved = model.Take();

        model.Take().ShouldBe(saved);
        model.Body = "# Steps!";
        model.Take().ShouldNotBe(saved);
        model.Body = "# Steps";
        model.Take().ShouldBe(saved);
        model.CategoryId = null;
        model.Take().ShouldNotBe(saved);
    }

    [Theory]
    [InlineData(ApiFields.Title, "", "Enter a title.")]
    [InlineData(ApiFields.Title, "  ", "Enter a title.")]
    [InlineData(ApiFields.Slug, "", "Enter a slug.")]
    [InlineData(ApiFields.Slug, "Reset Password", "Use lower-case letters, numbers and single hyphens, up to 80 characters.")]
    [InlineData(ApiFields.Slug, "reset--password", "Use lower-case letters, numbers and single hyphens, up to 80 characters.")]
    [InlineData(ApiFields.Body, "", "Write the article before you save it.")]
    public void A_field_that_fails_says_why_in_the_servers_terms(string field, string value, string message)
    {
        var model = Complete();
        switch (field)
        {
            case ApiFields.Title:
                model.Title = value;
                break;
            case ApiFields.Slug:
                model.Slug = value;
                break;
            default:
                model.Body = value;
                break;
        }

        model.Check(field, creating: true).ShouldBe(message);
    }

    [Fact]
    public void The_limits_are_the_servers_and_a_slug_is_checked_only_when_the_article_is_created()
    {
        var model = Complete();

        model.Title = new string('a', KbEditorLimits.TitleMaxLength);
        model.Check(ApiFields.Title, true).ShouldBeNull();
        model.Title = new string('a', KbEditorLimits.TitleMaxLength + 1);
        model.Check(ApiFields.Title, true).ShouldBe("Use 200 characters or fewer.");
        model.Summary = new string('a', KbEditorLimits.SummaryMaxLength + 1);
        model.Check(ApiFields.Summary, true).ShouldBe("Use 500 characters or fewer.");
        model.Body = new string('a', KbEditorLimits.BodyMaxLength + 1);
        model.Check(ApiFields.Body, true).ShouldBe("The article is too long to save.");
        model.Slug = "Not A Slug";
        model.Check(ApiFields.Slug, creating: false).ShouldBeNull();
        KbEditorLimits.SlugMaxLength.ShouldBe(80);
        KbEditorLimits.BodyMaxLength.ShouldBe(200_000);
    }

    [Fact]
    public void Publishing_needs_a_title_a_slug_a_body_and_a_category_and_names_the_first_that_is_missing()
    {
        Complete().FirstMissingForPublish().ShouldBeNull();
        var noTitle = Complete();
        noTitle.Title = " ";
        noTitle.FirstMissingForPublish().ShouldBe("title");
        var noBody = Complete();
        noBody.Body = "";
        noBody.FirstMissingForPublish().ShouldBe("body");
        var noCategory = Complete();
        noCategory.CategoryId = null;
        noCategory.FirstMissingForPublish().ShouldBe("category");
    }

    [Theory]
    [InlineData("Reset your password", "reset-your-password")]
    [InlineData("  Hello, World!  ", "hello-world")]
    [InlineData("100% sure -- really", "100-sure-really")]
    [InlineData("!!!", "")]
    public void A_slug_is_suggested_from_the_title(string title, string expected) => KbArticleEditorViewModel.SlugFrom(title).ShouldBe(expected);

    [Fact]
    public void A_suggested_slug_is_cut_to_the_limit_without_a_dangling_hyphen()
    {
        var slug = KbArticleEditorViewModel.SlugFrom(string.Join(' ', Enumerable.Repeat("word", 40)));

        slug.Length.ShouldBeLessThanOrEqualTo(KbEditorLimits.SlugMaxLength);
        slug.ShouldNotEndWith("-");
    }
}

/// <summary>What the editor's choices resolve against.</summary>
public sealed class KbEditorLookupsTests
{
    private static readonly Guid Orbitly = TestData.OrbitlyId;
    private static readonly Guid Paperplane = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static KbEditorLookups Lookups() => new(
        [TestData.Product("Paperplane", Paperplane), TestData.Product("Orbitly", Orbitly)],
        [
            TestData.KbCategory("Billing", "billing", Orbitly, Guid.NewGuid(), sortOrder: 20),
            TestData.KbCategory("Getting started", "getting-started", null, Guid.NewGuid(), sortOrder: 10),
            TestData.KbCategory("Shipping", "shipping", Paperplane, Guid.NewGuid(), sortOrder: 5),
        ]);

    [Fact]
    public void A_product_article_may_use_a_shared_category_or_its_own_and_a_shared_article_only_a_shared_one()
    {
        Lookups().CategoriesFor(Orbitly).Select(c => c.Name).ShouldBe(["Getting started", "Billing"]);
        Lookups().CategoriesFor(Paperplane).Select(c => c.Name).ShouldBe(["Shipping", "Getting started"]);
        Lookups().CategoriesFor(null).Select(c => c.Name).ShouldBe(["Getting started"]);
    }

    [Fact]
    public void A_shared_article_is_linked_under_the_first_product_by_name_and_a_product_article_under_its_own()
    {
        Lookups().PortalProductKey(Paperplane).ShouldBe("paperplane");
        Lookups().PortalProductKey(null).ShouldBe("orbitly");
        Lookups().PortalProductKey(Guid.NewGuid()).ShouldBeNull();
        KbEditorLookups.Empty.PortalProductKey(null).ShouldBeNull();
    }

    [Fact]
    public void Names_and_slugs_resolve_and_an_unknown_id_is_a_fixed_phrase_or_nothing()
    {
        var lookups = Lookups();

        lookups.ProductName(null).ShouldBe("Shared");
        lookups.ProductName(Orbitly).ShouldBe("Orbitly");
        lookups.ProductName(Guid.NewGuid()).ShouldBe("Another product");
        lookups.CategorySlug(lookups.Categories[0].Id).ShouldBe("billing");
        lookups.CategorySlug(Guid.NewGuid()).ShouldBeNull();
        lookups.CategorySlug(null).ShouldBeNull();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/KbImageUploadButtonTests.cs`

```csharp
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The picture button: a wrong type or an oversize picture is refused before anything is sent, and a picture the API refuses never changes the article (Review Focus 2, the Admin half; PHASE-08 T18).</summary>
public sealed class KbImageUploadButtonTests : AdminComponentTest
{
    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly List<KbUploadedImage> _uploaded = [];

    public KbImageUploadButtonTests()
    {
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new KbImageUploadResponse("kb-images/a.png", "https://api.example/kb-images/a.png")));
        Services.AddSingleton(_kb);
    }

    private IRenderedComponent<KbImageUploadButton> RenderButton(bool disabled = false) =>
        Render<KbImageUploadButton>(p => p.Add(b => b.Disabled, disabled).Add(b => b.OnUploaded, image => _uploaded.Add(image)));

    private static void Pick(IRenderedComponent<KbImageUploadButton> cut, string name, int bytes = 4, string type = "image/png") =>
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[bytes], name, null, type));

    private int Uploads() => _kb.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IKbClient.UploadImageAsync));

    [Fact]
    public void A_picture_is_uploaded_once_and_reported_with_its_address_and_an_alt_text_from_its_file_name()
    {
        var cut = RenderButton();

        Pick(cut, "login-screen.PNG", bytes: 8);

        cut.WaitForAssertion(() => _uploaded.ShouldBe([new KbUploadedImage("login screen", "https://api.example/kb-images/a.png")]));
        Uploads().ShouldBe(1);
        var file = (KbImageFile)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.UploadImageAsync)).GetArguments()[0]!;
        file.FileName.ShouldBe("login-screen.PNG");
        file.ContentType.ShouldBe("image/png");
        using var stream = file.OpenRead();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Length.ShouldBe(8);
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void The_upload_is_a_write_that_is_never_cancelled_by_the_screen()
    {
        var cut = RenderButton();

        Pick(cut, "a.png");

        cut.WaitForAssertion(() => Uploads().ShouldBe(1));
        var token = (CancellationToken)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.UploadImageAsync)).GetArguments()[1]!;
        token.CanBeCanceled.ShouldBeFalse();
    }

    [Theory]
    [InlineData("drawing.svg", "image/svg+xml")]
    [InlineData("notes.pdf", "application/pdf")]
    [InlineData("noextension", "image/png")]
    [InlineData("shot.png.exe", "image/png")]
    public void A_file_that_is_not_a_png_jpeg_gif_or_webp_is_refused_before_anything_is_sent(string name, string type)
    {
        var cut = RenderButton();

        Pick(cut, name, type: type);

        cut.Find("[role=alert]").TextContent.ShouldBe("Use a PNG, JPEG, GIF or WebP picture.");
        Uploads().ShouldBe(0);
        _uploaded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("a.png")]
    [InlineData("a.JPG")]
    [InlineData("a.jpeg")]
    [InlineData("a.gif")]
    [InlineData("a.webp")]
    public void The_four_picture_types_are_accepted_by_extension(string name)
    {
        var cut = RenderButton();

        Pick(cut, name);

        cut.WaitForAssertion(() => _uploaded.Count.ShouldBe(1));
    }

    [Fact]
    public void A_picture_over_the_limit_is_refused_before_anything_is_sent_and_the_message_names_the_limit()
    {
        var cut = RenderButton();

        Pick(cut, "huge.png", bytes: (int)KbLimits.MaxImageBytes + 1);

        cut.Find("[role=alert]").TextContent.ShouldBe("This picture is larger than 5 MB.");
        Uploads().ShouldBe(0);
        _uploaded.ShouldBeEmpty();
    }

    [Fact]
    public void A_picture_exactly_at_the_limit_is_sent()
    {
        var cut = RenderButton();

        Pick(cut, "edge.png", bytes: (int)KbLimits.MaxImageBytes);

        cut.WaitForAssertion(() => _uploaded.Count.ShouldBe(1));
    }

    [Fact]
    public void The_input_only_offers_the_four_types()
    {
        var cut = RenderButton();

        cut.Find("input[type=file]").GetAttribute("accept").ShouldBe(".png,.jpg,.jpeg,.gif,.webp");
        cut.Find("label.ts-kb-image-label").TextContent.ShouldBe("Add image");
    }

    [Theory]
    [InlineData(ApiErrorCodes.KbImageTypeNotAllowed, "Use a PNG, JPEG, GIF or WebP picture.")]
    [InlineData(ApiErrorCodes.KbImageTooLarge, "This picture is larger than 5 MB.")]
    [InlineData(ApiErrorCodes.RequestTooLarge, "This picture is larger than 5 MB.")]
    [InlineData(ApiErrorCodes.ApiTimeout, "The upload may not have finished. Nothing was added to the article; pick the picture again.")]
    [InlineData(ApiErrorCodes.ApiUnavailable, "The upload may not have finished. Nothing was added to the article; pick the picture again.")]
    public void A_picture_the_api_refuses_or_may_not_have_stored_says_so_and_adds_nothing(string code, string message)
    {
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbImageUploadResponse>(code, "from the API"));
        var cut = RenderButton();

        Pick(cut, "a.png");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldBe(message));
        _uploaded.ShouldBeEmpty();
    }

    [Fact]
    public void Any_other_failure_shows_the_api_message_after_a_fixed_sentence()
    {
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbImageUploadResponse>("validation-failed", "The file is missing."));
        var cut = RenderButton();

        Pick(cut, "a.png");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldBe("Couldn't upload the picture. Nothing was added to the article. The file is missing."));
    }

    [Fact]
    public void An_upload_that_throws_says_it_may_not_have_finished_and_never_shows_the_exception_text()
    {
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns<Task<Result<KbImageUploadResponse>>>(_ => throw new IOException("C:\\Users\\agent\\secret.png"));
        var cut = RenderButton();

        Pick(cut, "a.png");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("may not have finished"));
        cut.Markup.ShouldNotContain("secret.png");
        _uploaded.ShouldBeEmpty();
    }

    [Fact]
    public async Task While_it_uploads_the_agent_is_told_and_the_input_is_off_and_afterwards_the_same_picture_can_be_picked_again()
    {
        var pending = new TaskCompletionSource<Result<KbImageUploadResponse>>();
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderButton();

        // The file input waits for the handler to finish, and the handler waits for the answer: the pick runs on its own thread.
        var picking = Task.Run(() => Pick(cut, "a.png"), Xunit.TestContext.Current.CancellationToken);

        cut.WaitForAssertion(() => cut.Find("[role=status]").TextContent.ShouldBe("Uploading the image\u2026"));
        cut.Find("input[type=file]").HasAttribute("disabled").ShouldBeTrue();
        var firstInput = cut.FindComponent<InputFile>().Instance;

        pending.SetResult(TestData.Ok(new KbImageUploadResponse("kb-images/a.png", "https://api.example/kb-images/a.png")));
        await picking;

        cut.WaitForAssertion(() => cut.FindAll("[role=status]").ShouldBeEmpty());
        cut.Find("input[type=file]").HasAttribute("disabled").ShouldBeFalse();
        cut.FindComponent<InputFile>().Instance.ShouldNotBeSameAs(firstInput);
    }

    [Fact]
    public async Task A_picture_that_finishes_after_the_screen_is_gone_changes_nothing()
    {
        var pending = new TaskCompletionSource<Result<KbImageUploadResponse>>();
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderButton();
        var picking = Task.Run(() => Pick(cut, "a.png"), Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => Uploads().ShouldBe(1));

        cut.Instance.Dispose();
        pending.SetResult(TestData.Ok(new KbImageUploadResponse("kb-images/a.png", "https://api.example/kb-images/a.png")));
        await picking;
        await cut.InvokeAsync(() => { });

        _uploaded.ShouldBeEmpty();
    }

    [Fact]
    public void A_disabled_button_cannot_be_used()
    {
        var cut = RenderButton(disabled: true);

        cut.Find("input[type=file]").HasAttribute("disabled").ShouldBeTrue();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/KbPreviewPaneTests.cs`

```csharp
using Bunit;
using TechStrap.Admin.Features.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The preview pane is the one place the Admin draws HTML from an agent's text (Review Focus 1). It draws the sanitized HTML the API returned and nothing else: not the text the agent typed, not an error
/// message from the API, and nothing of its own besides fixed copy.
/// </summary>
public sealed class KbPreviewPaneTests : BunitContext
{
    [Fact]
    public void It_draws_the_html_it_was_given_exactly_as_the_api_returned_it()
    {
        var cut = Render<KbPreviewPane>(p => p.Add(c => c.Html, "<h2>Steps</h2><p>Open <strong>Settings</strong>.</p><img src=\"https://api.example/kb-images/a.png\" alt=\"Shot\">"));

        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<h2>Steps</h2><p>Open <strong>Settings</strong>.</p><img src=\"https://api.example/kb-images/a.png\" alt=\"Shot\">");
        cut.FindAll(".ts-kb-preview-empty").ShouldBeEmpty();
        cut.Find("section").GetAttribute("aria-label").ShouldBe("Preview of the article");
    }

    [Fact]
    public void Before_the_first_answer_it_says_there_is_nothing_to_preview_and_while_a_call_runs_it_says_it_is_updating()
    {
        var empty = Render<KbPreviewPane>();
        empty.Find(".ts-kb-preview-empty").TextContent.ShouldBe("Nothing to preview yet.");
        empty.Find("section").HasAttribute("aria-busy").ShouldBeFalse();

        var busy = Render<KbPreviewPane>(p => p.Add(c => c.Loading, true));
        busy.Find(".ts-kb-preview-empty").TextContent.ShouldBe("Updating the preview");
        busy.Find("section").GetAttribute("aria-busy").ShouldBe("true");
    }

    [Fact]
    public void A_failure_keeps_the_last_good_preview_under_a_fixed_message()
    {
        var cut = Render<KbPreviewPane>(p => p.Add(c => c.Html, "<p>Earlier</p>").Add(c => c.ErrorText, KbEditorCopy.PreviewFailed));

        cut.Find(".ts-kb-preview-error").TextContent.ShouldContain("The preview is not available right now.");
        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>Earlier</p>");
    }

    [Fact]
    public void A_failure_with_nothing_to_show_has_the_message_and_no_empty_line()
    {
        var cut = Render<KbPreviewPane>(p => p.Add(c => c.ErrorText, KbEditorCopy.PreviewFailed));

        cut.Find(".ts-kb-preview-error").ShouldNotBeNull();
        cut.FindAll(".ts-kb-preview-empty").ShouldBeEmpty();
        cut.FindAll(".ts-kb-preview-body").ShouldBeEmpty();
    }
}
```

`tests/TechStrap.Admin.Tests/Components/MarkdownEditorTests.cs`

````csharp
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The Markdown editor: a textarea, a toolbar and a live preview that asks the API. A burst of typing is one call 300 ms after the last keystroke; a call that a newer change replaced is canceled and its answer
/// is never drawn; a failed preview keeps the text; nothing is previewed for an empty text; and the timer and the call go when the component does (PHASE-08 T16).
/// </summary>
public sealed class MarkdownEditorTests : AdminComponentTest
{
    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly CountingTimeProvider _timers;

    public MarkdownEditorTests()
    {
        _timers = new CountingTimeProvider(Time);
        Services.AddSingleton<TimeProvider>(_timers);
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(call => TestData.Ok(new KbPreviewResponse($"<p>{call.Arg<KbPreviewRequest>().BodyMarkdown}</p>")));
        Services.AddSingleton(_kb);
    }

    // Stands in for the page: it owns the text and passes it down, as the editor page does.
    private sealed class Host : ComponentBase
    {
        [Parameter]
        public string Text { get; set; } = string.Empty;

        [Parameter]
        public bool Disabled { get; set; }

        [Parameter]
        public string? Error { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MarkdownEditor>(0);
            builder.AddAttribute(1, nameof(MarkdownEditor.Id), "ts-kb-body");
            builder.AddAttribute(2, nameof(MarkdownEditor.Value), Text);
            builder.AddAttribute(3, nameof(MarkdownEditor.ValueChanged), EventCallback.Factory.Create<string>(this, value => Text = value));
            builder.AddAttribute(4, nameof(MarkdownEditor.Disabled), Disabled);
            builder.AddAttribute(5, nameof(MarkdownEditor.ErrorMessage), Error);
            builder.CloseComponent();
        }
    }

    private IRenderedComponent<Host> RenderEditor(string text = "") => Render<Host>(p => p.Add(h => h.Text, text));

    private IEnumerable<string?> PreviewedTexts() =>
        _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.PreviewAsync)).Select(c => ((KbPreviewRequest)c.GetArguments()[0]!).BodyMarkdown);

    private static string TextArea(IRenderedComponent<Host> cut) => cut.Find("textarea").GetAttribute("value")!;

    // ---- the preview call ----------------------------------------------------------------------------------------

    [Fact]
    public void A_burst_of_typing_is_one_preview_call_300_ms_after_the_last_keystroke()
    {
        var cut = RenderEditor();

        cut.Find("textarea").Input("a");
        cut.Find("textarea").Input("ab");
        cut.Find("textarea").Input("abc");
        Time.Advance(KbDefaults.PreviewDebounce - TimeSpan.FromMilliseconds(1));
        PreviewedTexts().ShouldBeEmpty();

        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["abc"]));
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>abc</p>"));
        Time.Advance(KbDefaults.PreviewDebounce * 5);
        PreviewedTexts().ShouldBe(["abc"]);
        KbDefaults.PreviewDebounce.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void An_article_that_is_already_written_is_previewed_once_when_the_editor_opens()
    {
        var cut = RenderEditor("# Steps");

        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["# Steps"]));
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p># Steps</p>"));
    }

    [Fact]
    public void A_change_from_outside_such_as_a_reload_is_previewed_too()
    {
        var cut = RenderEditor("one");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["one"]));

        cut.Render(p => p.Add(h => h.Text, "two"));
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["one", "two"]));
    }

    [Fact]
    public void Nothing_is_previewed_for_an_empty_text_and_clearing_the_text_clears_the_preview_without_a_call()
    {
        var cut = RenderEditor();
        Time.Advance(KbDefaults.PreviewDebounce * 2);
        PreviewedTexts().ShouldBeEmpty();
        cut.Find(".ts-kb-preview-empty").TextContent.ShouldBe("Nothing to preview yet.");

        cut.Find("textarea").Input("hello");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>hello</p>"));

        cut.Find("textarea").Input("   ");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.FindAll(".ts-kb-preview-body").ShouldBeEmpty());
        PreviewedTexts().ShouldBe(["hello"]);
    }

    [Fact]
    public async Task A_call_that_a_newer_change_replaced_is_cancelled_and_its_late_answer_is_never_drawn()
    {
        var first = new TaskCompletionSource<Result<KbPreviewResponse>>();
        CancellationToken firstToken = default;
        _kb.PreviewAsync(Arg.Is<KbPreviewRequest>(r => r.BodyMarkdown == "old"), Arg.Any<CancellationToken>()).Returns(call =>
        {
            firstToken = call.Arg<CancellationToken>();
            return first.Task;
        });
        _kb.PreviewAsync(Arg.Is<KbPreviewRequest>(r => r.BodyMarkdown == "new"), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new KbPreviewResponse("<p>NEW</p>")));
        var cut = RenderEditor();
        cut.Find("textarea").Input("old");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["old"]));
        firstToken.IsCancellationRequested.ShouldBeFalse();

        cut.Find("textarea").Input("new");

        firstToken.IsCancellationRequested.ShouldBeTrue();
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>NEW</p>"));
        first.SetResult(TestData.Ok(new KbPreviewResponse("<p>OLD</p>")));

        // The late answer is handled on the renderer's thread: let it run, then draw again, so a guard that is missing would show.
        await cut.InvokeAsync(() => { });
        cut.Render(p => p.Add(h => h.Error, "refresh"));
        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>NEW</p>");
        cut.Markup.ShouldNotContain("<p>OLD</p>");
    }

    [Fact]
    public async Task An_answer_that_arrives_after_a_newer_call_started_is_ignored_even_when_the_call_was_not_cancelled()
    {
        var slow = new TaskCompletionSource<Result<KbPreviewResponse>>();
        _kb.PreviewAsync(Arg.Is<KbPreviewRequest>(r => r.BodyMarkdown == "slow"), Arg.Any<CancellationToken>()).Returns(_ => slow.Task);
        _kb.PreviewAsync(Arg.Is<KbPreviewRequest>(r => r.BodyMarkdown == "fast"), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new KbPreviewResponse("<p>fast</p>")));
        var cut = RenderEditor();
        cut.Find("textarea").Input("slow");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["slow"]));
        cut.Find("textarea").Input("fast");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>fast</p>"));

        slow.SetResult(TestData.Ok(new KbPreviewResponse("<p>slow</p>")));
        await cut.InvokeAsync(() => { });
        cut.Render(p => p.Add(h => h.Error, "refresh"));

        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>fast</p>");
        cut.Markup.ShouldNotContain("<p>slow</p>");
    }

    [Fact]
    public void A_failed_preview_keeps_the_text_and_the_last_good_preview_and_says_so_in_fixed_words()
    {
        var cut = RenderEditor();
        cut.Find("textarea").Input("good");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>good</p>"));
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbPreviewResponse>("api-error", "<img src=x onerror=alert(1)>"));

        cut.Find("textarea").Input("good and more");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-error").TextContent.ShouldContain("The preview is not available right now."));
        TextArea(cut).ShouldBe("good and more");
        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>good</p>");
        cut.Markup.ShouldNotContain("onerror");

        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new KbPreviewResponse("<p>fine</p>")));
        cut.Find("textarea").Input("good and more!");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.FindAll(".ts-kb-preview-error").ShouldBeEmpty());
        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>fine</p>");
    }

    [Fact]
    public void A_preview_that_throws_is_a_failed_preview_and_never_reaches_the_renderer()
    {
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns<Task<Result<KbPreviewResponse>>>(_ => throw new InvalidOperationException("secret text from the article"));
        var cut = RenderEditor();

        cut.Find("textarea").Input("boom");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-error").ShouldNotBeNull());
        cut.Markup.ShouldNotContain("secret text");
        TextArea(cut).ShouldBe("boom");
    }

    [Fact]
    public void A_text_the_api_could_not_save_either_is_not_sent_for_preview_and_keeps_the_last_good_preview()
    {
        var cut = RenderEditor();
        cut.Find("textarea").Input("short");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>short</p>"));

        cut.Find("textarea").Input(new string('a', KbEditorLimits.BodyMaxLength + 1));
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find(".ts-kb-preview-error").TextContent.ShouldContain("too long to preview or save"));
        PreviewedTexts().ShouldBe(["short"]);
        cut.Find(".ts-kb-preview-body").InnerHtml.ShouldBe("<p>short</p>");
    }

    [Fact]
    public void While_a_call_runs_the_pane_is_marked_busy()
    {
        var pending = new TaskCompletionSource<Result<KbPreviewResponse>>();
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderEditor();

        cut.Find("textarea").Input("x");
        Time.Advance(KbDefaults.PreviewDebounce);

        cut.WaitForAssertion(() => cut.Find("section.ts-kb-preview").GetAttribute("aria-busy").ShouldBe("true"));
        pending.SetResult(TestData.Ok(new KbPreviewResponse("<p>x</p>")));
        cut.WaitForAssertion(() => cut.Find("section.ts-kb-preview").HasAttribute("aria-busy").ShouldBeFalse());
    }

    // ---- teardown ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_pending_timer_is_released_with_the_component_so_no_call_is_made_after_it_is_gone()
    {
        var cut = RenderEditor();
        cut.Find("textarea").Input("typed");

        _timers.LiveTimers.ShouldBe(1);

        cut.FindComponent<MarkdownEditor>().Instance.Dispose();

        // Released at once, not when it would have fired.
        _timers.LiveTimers.ShouldBe(0);
        Time.Advance(KbDefaults.PreviewDebounce * 3);
        PreviewedTexts().ShouldBeEmpty();
    }

    [Fact]
    public void A_call_in_flight_is_cancelled_with_the_component()
    {
        var pending = new TaskCompletionSource<Result<KbPreviewResponse>>();
        CancellationToken token = default;
        _kb.PreviewAsync(Arg.Any<KbPreviewRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            token = call.Arg<CancellationToken>();
            return pending.Task;
        });
        var cut = RenderEditor();
        cut.Find("textarea").Input("typed");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => PreviewedTexts().ShouldBe(["typed"]));

        cut.FindComponent<MarkdownEditor>().Instance.Dispose();

        token.IsCancellationRequested.ShouldBeTrue();
    }

    // ---- the toolbar ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Bold", "**bold text**")]
    [InlineData("Italic", "*italic text*")]
    [InlineData("Link", "[link text](https://)")]
    [InlineData("List", "- first item\n- second item")]
    [InlineData("Code", "```\ncode\n```")]
    public void Each_toolbar_button_adds_its_markdown_at_the_end_of_the_text_and_the_preview_follows(string button, string snippet)
    {
        var cut = RenderEditor("Intro");

        cut.FindAll(".ts-kb-toolbar button").Single(b => b.TextContent.Trim().EndsWith(button, StringComparison.Ordinal)).Click();

        var inline = button is "Bold" or "Italic" or "Link";
        TextArea(cut).ShouldBe(inline ? $"Intro {snippet}" : $"Intro\n\n{snippet}");
        Time.Advance(KbDefaults.PreviewDebounce);
        cut.WaitForAssertion(() => PreviewedTexts().Last().ShouldBe(inline ? $"Intro {snippet}" : $"Intro\n\n{snippet}"));
    }

    [Fact]
    public void The_toolbar_is_a_named_toolbar_that_controls_the_text_area()
    {
        var cut = RenderEditor();

        var toolbar = cut.Find("[role=toolbar]");
        toolbar.GetAttribute("aria-label").ShouldBe("Formatting");
        toolbar.GetAttribute("aria-controls").ShouldBe("ts-kb-body");
        cut.Find("textarea").Id.ShouldBe("ts-kb-body");
        cut.Find("label[for=ts-kb-body]").TextContent.ShouldBe("Article");
    }

    [Fact]
    public void A_picture_that_was_uploaded_is_added_as_markdown_with_its_alt_text_from_the_file_name()
    {
        _kb.UploadImageAsync(Arg.Any<KbImageFile>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(new KbImageUploadResponse("kb-images/a.png", "https://api.example/kb-images/a.png")));
        var cut = RenderEditor("Intro");

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromBinary(new byte[4], "login-screen.png", null, "image/png"));

        cut.WaitForAssertion(() => TextArea(cut).ShouldBe("Intro\n\n![login screen](https://api.example/kb-images/a.png)"));
    }

    [Fact]
    public void A_busy_form_disables_the_text_and_every_toolbar_button()
    {
        var cut = Render<Host>(p => p.Add(h => h.Text, "x").Add(h => h.Disabled, true));

        cut.Find("textarea").HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll(".ts-kb-toolbar button").ShouldAllBe(b => b.HasAttribute("disabled"));
        cut.Find("input[type=file]").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void A_field_error_is_tied_to_the_text_area()
    {
        var cut = Render<Host>(p => p.Add(h => h.Text, "x").Add(h => h.Error, "Write the article before you save it."));

        cut.Find("textarea").GetAttribute("aria-invalid").ShouldBe("true");
        cut.Find("textarea").GetAttribute("aria-describedby").ShouldBe("ts-kb-body-error");
        cut.Find("#ts-kb-body-error").TextContent.ShouldBe("Write the article before you save it.");
        cut.Find("textarea").ClassList.ShouldContain("is-invalid");
    }

    [Fact]
    public void Below_992px_the_write_and_preview_buttons_choose_which_pane_shows()
    {
        var cut = RenderEditor("x");

        cut.Find(".ts-kb-md").GetAttribute("data-pane").ShouldBe("write");
        var tabs = cut.FindAll(".ts-kb-tabs button");
        tabs[0].GetAttribute("aria-pressed").ShouldBe("true");
        tabs[1].GetAttribute("aria-pressed").ShouldBe("false");

        tabs[1].Click();

        cut.Find(".ts-kb-md").GetAttribute("data-pane").ShouldBe("preview");
        cut.FindAll(".ts-kb-tabs button")[1].GetAttribute("aria-pressed").ShouldBe("true");
        cut.Find("textarea").GetAttribute("value").ShouldBe("x");
    }
}
````

`tests/TechStrap.Admin.Tests/Components/MarkdownSnippetsTests.cs`

````csharp
using TechStrap.Admin.Features.Kb;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The toolbar adds Markdown at the end of the text, because Blazor cannot read the caret. These are the rules of where and how.</summary>
public sealed class MarkdownSnippetsTests
{
    [Fact]
    public void A_snippet_in_an_empty_article_is_the_whole_text()
    {
        MarkdownSnippets.Append(null, MarkdownSnippets.Bold, inline: true).ShouldBe("**bold text**");
        MarkdownSnippets.Append("", MarkdownSnippets.List, inline: false).ShouldBe("- first item\n- second item");
    }

    [Theory]
    [InlineData("Hello", "Hello **bold text**")]
    [InlineData("Hello ", "Hello **bold text**")]
    [InlineData("Hello\n", "Hello\n**bold text**")]
    public void An_inline_snippet_follows_a_space_and_never_glues_itself_to_the_last_word(string text, string expected) =>
        MarkdownSnippets.Append(text, MarkdownSnippets.Bold, inline: true).ShouldBe(expected);

    [Theory]
    [InlineData("Hello", "Hello\n\n```\ncode\n```")]
    [InlineData("Hello\n", "Hello\n\n```\ncode\n```")]
    [InlineData("Hello\n\n", "Hello\n\n```\ncode\n```")]
    public void A_block_snippet_starts_after_a_blank_line(string text, string expected) =>
        MarkdownSnippets.Append(text, MarkdownSnippets.Code, inline: false).ShouldBe(expected);

    [Fact]
    public void The_toolbar_snippets_are_the_markdown_the_preview_renders()
    {
        MarkdownSnippets.Italic.ShouldBe("*italic text*");
        MarkdownSnippets.Link.ShouldBe("[link text](https://)");
    }

    [Fact]
    public void An_image_is_markdown_with_its_alt_text_and_an_address_that_cannot_end_the_link_early()
    {
        MarkdownSnippets.Image("Login screen", "https://api.example/kb-images/abc.png").ShouldBe("![Login screen](https://api.example/kb-images/abc.png)");
        MarkdownSnippets.Image("x", " https://api.example/a b(1).png ").ShouldBe("![x](https://api.example/a%20b%281%29.png)");
    }

    [Theory]
    [InlineData("login-screen.png", "login screen")]
    [InlineData("My_Shot.JPEG", "My Shot")]
    [InlineData("a[b](c)`d`*e*.png", "a b c d e")]
    [InlineData(".png", "image")]
    [InlineData("", "image")]
    [InlineData(null, "image")]
    public void The_alt_text_comes_from_the_file_name_without_markdown_characters(string? fileName, string expected) =>
        MarkdownSnippets.AltFromFileName(fileName).ShouldBe(expected);

    [Fact]
    public void A_very_long_file_name_is_cut_for_the_alt_text()
    {
        var alt = MarkdownSnippets.AltFromFileName(new string('a', 300) + ".png");

        alt.Length.ShouldBe(MarkdownSnippets.MaxAltLength);
    }

    [Fact]
    public void Alt_text_can_never_close_the_bracket_or_start_html()
    {
        var image = MarkdownSnippets.Image("a](https://evil.example) <img src=x onerror=alert(1)>", "https://api.example/x.png");

        image.ShouldBe("![a https://evil.example img src=x onerror=alert 1](https://api.example/x.png)");
        image.Count(c => c == ']').ShouldBe(1);
        image.ShouldNotContain("<");
    }
}
````

`tests/TechStrap.Admin.Tests/KbEditorHostTests.cs`

```csharp
using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Admin.Options;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The whole Admin host with the fake sign-in and the stub API, for the article editor (PHASE-08 T17): every agent may open it, every call carries the agent's token, and the "View on portal" link is built from the
/// portal address in the Admin's own configuration. Pages prerender, so the HTML already holds the data the stub served.
/// </summary>
public sealed class KbEditorHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid ArticleId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static AdminFactory FactoryWithArticle(string status, string? portalUrl = "https://help.example.com")
    {
        var settings = new Dictionary<string, string?> { [PortalUrlOptions.PublicUrlKey] = portalUrl };
        var factory = new AdminFactory(settings: settings);
        var product = new ProductDto(OrbitlyId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 1);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)[new KbCategoryDto(CategoryId, OrbitlyId, "account", "Account", null, 10, 1)])
            .OnJson(HttpMethod.Get, $"/api/kb/articles/{ArticleId}", new KbArticleDto(
                ArticleId, OrbitlyId, CategoryId, "reset-password", "Reset your password", "How to reset it.", "# Steps", status, Guid.NewGuid(), Now.AddDays(-2), Now.AddHours(-1),
                status == KbArticleStatuses.Published ? Now.AddDays(-1) : null, 3));
        return factory;
    }

    [Theory]
    [InlineData("/kb/new")]
    [InlineData("/kb/dddddddd-0000-0000-0000-000000000001")]
    public async Task An_anonymous_visitor_is_sent_to_the_sign_in_landing_and_the_api_is_never_asked(string path)
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Draft);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(path, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/signin");
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_user_the_api_refuses_gets_the_no_access_page_and_the_article_is_never_read()
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Draft);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        var html = await client.GetStringAsync($"/kb/{ArticleId}", Ct);

        html.ShouldNotContain("Reset your password");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Outsider);
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("Admin")]
    public async Task Every_agent_opens_a_new_article_and_the_page_reads_only_the_lookups(string who)
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Draft);
        var principal = who == "Admin" ? AdminTestPrincipal.Admin : AdminTestPrincipal.Agent;
        using var client = factory.CreateClient().SignedInAs(principal);

        var html = await client.GetStringAsync("/kb/new", Ct);
        var page = await new HtmlParser().ParseDocumentAsync(html, Ct);

        page.QuerySelector("h1")!.TextContent.ShouldBe("New article");
        page.QuerySelector("#ts-kb-body")!.TagName.ShouldBe("TEXTAREA");
        page.QuerySelectorAll("#ts-kb-product option").Select(o => o.TextContent).ShouldBe(["Shared by every product", "Orbitly"]);
        factory.Api.Requests.Select(r => r.Path).Distinct().Order().ShouldBe(["/api/agents/me", "/api/kb/categories", "/api/products"]);
        factory.Api.AssertEveryCallBore(principal);
    }

    [Fact]
    public async Task A_published_article_shows_its_status_and_a_portal_link_built_from_the_admins_own_setting()
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Published);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var page = await new HtmlParser().ParseDocumentAsync(await client.GetStringAsync($"/kb/{ArticleId}", Ct), Ct);

        page.QuerySelector(".ts-kb-status .ts-pill")!.TextContent.ShouldBe("Published");
        var link = page.QuerySelector("a.ts-kb-portal-link")!;
        link.GetAttribute("href").ShouldBe("https://help.example.com/p/orbitly/kb/account/reset-password");
        link.GetAttribute("rel").ShouldBe("noopener noreferrer");
        page.QuerySelector("#ts-kb-title")!.GetAttribute("value").ShouldBe("Reset your password");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Theory]
    [InlineData(KbArticleStatuses.Draft, "https://help.example.com")]
    [InlineData(KbArticleStatuses.Published, "")]
    public async Task A_draft_and_a_missing_portal_setting_both_leave_the_link_out(string status, string portalUrl)
    {
        await using var factory = FactoryWithArticle(status, portalUrl);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var page = await new HtmlParser().ParseDocumentAsync(await client.GetStringAsync($"/kb/{ArticleId}", Ct), Ct);

        page.QuerySelectorAll("a.ts-kb-portal-link").ShouldBeEmpty();
        page.QuerySelector("#ts-kb-title")!.GetAttribute("value").ShouldBe("Reset your password");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task An_article_that_is_missing_says_so_and_draws_no_form()
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Draft);
        factory.Api.OnProblem(HttpMethod.Get, $"/api/kb/articles/{ArticleId}", HttpStatusCode.NotFound, "kb-article-not-found", "No such article.");
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync($"/kb/{ArticleId}", Ct);

        html.ShouldContain("This article no longer exists.");
        html.ShouldNotContain("ts-kb-form");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task A_segment_that_is_not_an_article_id_is_not_an_article()
    {
        await using var factory = FactoryWithArticle(KbArticleStatuses.Draft);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        using var response = await client.GetAsync("/kb/not-an-id", Ct);

        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("ts-kb-form");
    }
}
```

`tests/TechStrap.Admin.Tests/KbStyleTests.cs`

```csharp
using TechStrap.Tests.Shared;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The knowledge base screens (PHASE-08): the editor shows Write and Preview side by side from 992 px and one at a time below it, a picture can never be wider than its pane, and text that comes from agents wraps
/// instead of stretching the page. Reads the compiled CSS, so a rule that is renamed or moved to the wrong breakpoint fails here. How it looks is the owner's checklist.
/// </summary>
public sealed class KbStyleTests
{
    private const string Wide = "(min-width: 992px)";

    private static readonly CompiledCss Css = CompiledCss.Load("TechStrap.Admin");

    [Fact]
    public void Below_992px_only_the_chosen_pane_is_shown()
    {
        var outside = Css.OutsideMedia();

        outside.Declarations(".ts-kb-md[data-pane=write] .ts-kb-pane--preview,.ts-kb-md[data-pane=preview] .ts-kb-pane--write")["display"].ShouldBe("none");
        outside.Declarations(".ts-kb-tabs")["display"].ShouldBe("flex");
        outside.Declarations(".ts-kb-panes")["grid-template-columns"].ShouldBe("minmax(0, 1fr)");
    }

    [Fact]
    public void From_992px_both_panes_are_shown_side_by_side_and_the_buttons_that_choose_between_them_go()
    {
        var wide = Css.InMedia(Wide);

        wide.Declarations(".ts-kb-tabs")["display"].ShouldBe("none");
        wide.Declarations(".ts-kb-panes")["grid-template-columns"].ShouldBe("minmax(0, 1fr) minmax(0, 1fr)");
        wide.Declarations(".ts-kb-md[data-pane] .ts-kb-pane")["display"].ShouldBe("block");
    }

    [Fact]
    public void A_picture_or_a_wide_table_in_the_preview_can_never_stretch_its_pane()
    {
        Css.Declarations(".ts-kb-preview-body img")["max-width"].ShouldBe("100%");
        Css.Declarations(".ts-kb-preview-body img")["height"].ShouldBe("auto");
        Css.Declarations(".ts-kb-preview-body table")["overflow-x"].ShouldBe("auto");
        Css.Declarations(".ts-kb-preview-body pre")["overflow-x"].ShouldBe("auto");
        Css.Declarations(".ts-kb-preview")["overflow-wrap"].ShouldBe("anywhere");
    }

    [Fact]
    public void The_article_text_is_monospace_and_the_two_panes_can_shrink_below_their_content()
    {
        Css.Declarations(".ts-kb-textarea")["font-family"].ShouldBe("var(--ts-font-mono)");
        Css.Declarations(".ts-kb-pane")["min-width"].ShouldBe("0");
    }
}
```

`tests/TechStrap.Admin.Tests/MarkupStringSiteTests.cs`

Replace (edit 1 of 2)

```csharp
namespace TechStrap.Admin.Tests;

/// <summary>The one place the Admin turns API HTML into markup is the message bubble; the server sanitizes that body (PHASE-07 T10).</summary>
public sealed class MarkupStringSiteTests
{
    [Fact]
    public void MarkupString_is_used_in_exactly_one_file_the_message_bubble()
    {
        var admin = RepositoryRoot.Combine("src", "TechStrap.Admin");
```

with

```csharp
namespace TechStrap.Admin.Tests;

/// <summary>
/// The Admin turns API HTML into markup in exactly two places, and each shows HTML the server sanitized: the message bubble (a ticket message body, PHASE-07 T10) and the knowledge base preview pane
/// (the answer of <c>POST /api/kb/preview</c>, which runs the same renderer and sanitizer as the portal, PHASE-08 T16). A third place must be argued for in the same commit that adds it.
/// </summary>
public sealed class MarkupStringSiteTests
{
    [Fact]
    public void MarkupString_is_used_in_exactly_two_files_the_message_bubble_and_the_kb_preview_pane()
    {
        var admin = RepositoryRoot.Combine("src", "TechStrap.Admin");
```

Replace (edit 2 of 2)

```csharp
            .ToList();

        users.ShouldBe(["Features/Tickets/MessageBubble.razor"]);
    }
}
```

with

```csharp
            .ToList();

        users.Order(StringComparer.Ordinal).ShouldBe(["Features/Kb/KbPreviewPane.razor", "Features/Tickets/MessageBubble.razor"]);
    }
}
```

`tests/TechStrap.Admin.Tests/Options/PortalUrlOptionsTests.cs`

```csharp
using Microsoft.Extensions.Options;
using TechStrap.Admin.Options;

namespace TechStrap.Admin.Tests.Options;

/// <summary>The optional portal address behind the "View on portal" link: blank is fine, anything else must be a plain http or https address, and the article address is built from escaped parts.</summary>
public sealed class PortalUrlOptionsTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("https://help.example.com", true)]
    [InlineData("https://help.example.com/", true)]
    [InlineData("http://localhost:8082", true)]
    [InlineData("https://example.com/portal", true)]
    [InlineData("help.example.com", false)]
    [InlineData("/p/orbitly", false)]
    [InlineData("ftp://help.example.com", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://help.example.com?x=1", false)]
    [InlineData("https://help.example.com?", false)]
    [InlineData("https://help.example.com#top", false)]
    [InlineData("https://help.example.com#", false)]
    [InlineData("https://user:pass@help.example.com", false)]
    public void The_portal_address_is_blank_or_a_plain_absolute_http_or_https_address(string? value, bool valid) =>
        PortalUrlOptions.IsValidBase(value).ShouldBe(valid);

    [Fact]
    public void An_article_address_follows_the_portal_route_and_a_trailing_slash_makes_no_difference()
    {
        new PortalUrlOptions { PublicUrl = "https://help.example.com/" }.ArticleUrl("orbitly", "account", "reset-password")
            .ShouldBe("https://help.example.com/p/orbitly/kb/account/reset-password");
        new PortalUrlOptions { PublicUrl = " https://help.example.com/portal " }.ArticleUrl("orbitly", "account", "reset-password")
            .ShouldBe("https://help.example.com/portal/p/orbitly/kb/account/reset-password");
    }

    [Fact]
    public void Each_part_is_escaped_so_a_slug_can_never_add_a_segment_or_a_query()
    {
        new PortalUrlOptions { PublicUrl = "https://help.example.com" }.ArticleUrl("or bit", "a/b", "x?y=1#z")
            .ShouldBe("https://help.example.com/p/or%20bit/kb/a%2Fb/x%3Fy%3D1%23z");
    }

    [Theory]
    [InlineData(null, "orbitly", "account", "slug")]
    [InlineData("", "orbitly", "account", "slug")]
    [InlineData("https://help.example.com", null, "account", "slug")]
    [InlineData("https://help.example.com", "orbitly", "", "slug")]
    [InlineData("https://help.example.com", "orbitly", "account", " ")]
    public void With_no_portal_address_or_a_missing_part_there_is_no_link(string? portal, string? product, string? category, string? slug) =>
        new PortalUrlOptions { PublicUrl = portal }.ArticleUrl(product, category, slug).ShouldBeNull();

    [Fact]
    public async Task A_blank_portal_address_lets_the_host_start()
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { [PortalUrlOptions.PublicUrlKey] = "" });

        using var client = factory.CreateClient();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("help.example.com")]
    [InlineData("https://help.example.com?x=1")]
    public async Task A_portal_address_that_is_not_a_plain_absolute_address_stops_the_start_and_names_the_variable(string value)
    {
        await using var factory = new AdminFactory(settings: new Dictionary<string, string?> { [PortalUrlOptions.PublicUrlKey] = value });

        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("TECHSTRAP_PORTAL_PUBLIC_URL");
    }
}
```

`tests/TechStrap.Api.Tests/KbAdminLeakTests.cs`

```csharp
using System.Net;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Api.Tests;

/// <summary>
/// Never log search text, an article's text or a picture's file name (PHASE-08, the 07c lesson): whatever the Admin does for a signed-in agent in the knowledge base, the words an agent typed or chose never reach a log line, even at
/// Verbose, and a lapsed session sends nothing more (the auth package logs the path and query of an unauthenticated call, which would carry the search text). Each test proves the text really traveled, or it proves nothing.
/// </summary>
/// <remarks>Runs in the non-parallel <see cref="ProcessEnvironmentCollection"/> like <see cref="AdminLeakTests"/>: the Admin host reads process environment variables while it starts.</remarks>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class KbAdminLeakTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid ProductId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid ArticleId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");
    private static readonly Guid CategoryId = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    // Words shaped so the PII log redactor would not necessarily mask them: the test proves the Admin never logs them, not that a redactor hid them.
    private const string SearchTerm = "leakprobe.kb.search@example.com";
    private const string ArticleText = "leakprobe-confidential-draft-text-0123456789";
    private const string PictureName = "leakprobe-board-minutes.png";

    private static string Everything(LogEvent e) => string.Join('\n', [e.RenderMessage(), e.Exception?.ToString() ?? string.Empty, .. e.Properties.Values.Select(v => v.ToString())]);

    private static AdminFactory VerboseFactory() => new(settings: new Dictionary<string, string?>
    {
        ["Serilog:MinimumLevel:Default"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
        ["Serilog:MinimumLevel:Override:Microsoft.AspNetCore"] = "Verbose",
        ["Serilog:MinimumLevel:Override:System"] = "Verbose",
    });

    private static void AssertVerboseWasCaptured(AdminFactory factory) =>
        factory.LogSink.Events.ShouldContain(e => e.Level <= LogEventLevel.Debug, "the Verbose setting must have taken effect, or this test only scanned Information and above");

    private static void AssertNothingLeaked(AdminFactory factory) =>
        factory.LogSink.Events.Select(Everything).ShouldAllBe(text =>
            !text.Contains("leakprobe", StringComparison.OrdinalIgnoreCase) && !text.Contains(SearchTerm, StringComparison.OrdinalIgnoreCase)
            && !text.Contains(ArticleText, StringComparison.OrdinalIgnoreCase) && !text.Contains(PictureName, StringComparison.OrdinalIgnoreCase));

    private static void StubKb(AdminFactory factory)
    {
        var product = new ProductDto(ProductId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 1);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)[new KbCategoryDto(CategoryId, ProductId, "account", "Account", null, 10, 1)])
            .OnJson(HttpMethod.Get, $"/api/kb/articles/{ArticleId}", new KbArticleDto(
                ArticleId, ProductId, CategoryId, "reset-password", "Reset your password", "How.", ArticleText, KbArticleStatuses.Draft, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 3));
    }

    // The positive control: a scan that cannot see a term proves nothing, so the scan is shown to fail when a term is logged.
    [Fact]
    public async Task The_scan_sees_a_term_when_one_is_logged_at_Verbose()
    {
        await using var factory = VerboseFactory();
        StubKb(factory);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);
        (await client.GetStringAsync("/kb", Ct)).ShouldNotBeNull();

        factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("canary").LogTrace("typed {Text}", ArticleText);

        AssertVerboseWasCaptured(factory);
        Should.Throw<Shouldly.ShouldAssertException>(() => AssertNothingLeaked(factory));
    }

    [Fact]
    public async Task A_search_term_appears_in_no_log_event_after_a_mid_session_401_and_the_list_is_never_asked_after_it()
    {
        await using var factory = VerboseFactory();
        StubKb(factory);

        // The agent is let in by /me, then the first data call answers 401. After that the token is evicted, so every later call of the page must stay local, and the search is on the list call.
        factory.Api.OnStatus(HttpMethod.Get, "/api/products", HttpStatusCode.Unauthorized);
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var html = await client.GetStringAsync($"/kb?search={Uri.EscapeDataString(SearchTerm)}", Ct);

        factory.Api.Requests.ShouldContain(r => r.Path == "/api/products", "the 401 must actually have been answered, or this test proves nothing");
        html.ShouldNotBeNull();
        AssertVerboseWasCaptured(factory);
        AssertNothingLeaked(factory);
        factory.Api.Requests.Where(r => r.Path == "/api/kb/articles").ShouldBeEmpty("once the session lapsed no request may be sent, and the list call carries the search text");
    }

    [Fact]
    public async Task An_articles_text_a_previews_text_and_a_pictures_name_appear_in_no_log_event_even_when_the_calls_fail()
    {
        await using var factory = VerboseFactory();
        StubKb(factory);
        factory.Api
            .OnJson(HttpMethod.Post, "/api/kb/preview", new KbPreviewResponse("<p>fine</p>"))
            .OnJson(HttpMethod.Post, "/api/kb/images", new KbImageUploadResponse("kb-images/a.png", "https://api.test/kb-images/a.png"), HttpStatusCode.Created);

        using (var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent))
        {
            (await client.GetStringAsync($"/kb/{ArticleId}", Ct)).ShouldContain("Reset your password");
        }

        // The same calls the editor makes, through the host's own client pipeline (named clients, auth handler, token cache): a circuit has no HTTP request, so the scope gets the sign-in the circuit would have.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var authentication = (IHostEnvironmentAuthenticationStateProvider)scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
            authentication.SetAuthenticationState(Task.FromResult(new AuthenticationState(AdminTestPrincipal.Agent.ToClaimsPrincipal(AdminTestAuth.Scheme))));
            var kb = scope.ServiceProvider.GetRequiredService<IKbClient>();
            var picture = new KbImageFile(PictureName, "image/png", () => new MemoryStream([0x89, 0x50, 0x4E, 0x47]));

            (await kb.PreviewAsync(new KbPreviewRequest(ArticleText), CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await kb.UploadImageAsync(picture, CancellationToken.None)).IsSuccess.ShouldBeTrue();

            factory.Api.OnStatus(HttpMethod.Post, "/api/kb/preview", HttpStatusCode.InternalServerError);
            factory.Api.OnStatus(HttpMethod.Post, "/api/kb/images", HttpStatusCode.InternalServerError);
            (await kb.PreviewAsync(new KbPreviewRequest(ArticleText), CancellationToken.None)).IsFailure.ShouldBeTrue();
            (await kb.UploadImageAsync(picture, CancellationToken.None)).IsFailure.ShouldBeTrue();
        }

        factory.Api.Requests.ShouldContain(r => r.Path == "/api/kb/preview" && r.Body!.Contains(ArticleText, StringComparison.Ordinal), "the article text really traveled, or this test proves nothing");
        factory.Api.Requests.ShouldContain(r => r.Path == "/api/kb/images" && r.Body!.Contains(PictureName, StringComparison.Ordinal), "the picture's name really traveled, or this test proves nothing");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
        AssertVerboseWasCaptured(factory);
        AssertNothingLeaked(factory);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-class "*KbAdminLeakTests"
pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1
```

Expected: the Admin test project does not build (14 errors), because the editor, the preview pane, the picture button, the snippets, the portal option and the page do not exist. The first errors:
- `CS0246`: The type or namespace name 'KbArticleEditorPage' could not be found (are you missing a using directive or an assembly reference?) (16 times)
- `CS0246`: The type or namespace name 'KbImageUploadButton' could not be found (are you missing a using directive or an assembly reference?) (4 times)
- `CS0246`: The type or namespace name 'KbEditorLookups' could not be found (are you missing a using directive or an assembly reference?) (2 times)

The Api leak test builds and fails (`/kb/{id}` is not a page yet), and the config contract fails for the Admin: `appsettings.json is blank only where blank is valid, and nowhere else` (its blank list names `TECHSTRAP_PORTAL_PUBLIC_URL`, which is not in `appsettings.json` yet).

- [ ] **Step 3: Implement**

The pure parts first (snippets, the form model and the lookups, the portal option), then the pane and the picture button, the editor, the page, the registration and the setting (the D-043 four edits), and last the styles. New files are given whole; the others as edits.

`deploy/.env.admin.example`

Replace

```bash
# Claim that carries IdP group names (D-029). Authentik and most IdPs use "groups".
TECHSTRAP_GROUP_CLAIM_TYPE=groups

# -- Trusted reverse proxy [Api, Admin, Portal] --
```

with

```bash
# Claim that carries IdP group names (D-029). Authentik and most IdPs use "groups".
TECHSTRAP_GROUP_CLAIM_TYPE=groups

# -- Customer portal address [Api, Admin] --
# Optional in the Admin: the customer portal base URL as customers see it (the same value as in the Api's .env.api). It powers the "View on portal" link of a published knowledge base
# article; blank hides the link. Absolute http or https, no query or fragment.
TECHSTRAP_PORTAL_PUBLIC_URL=

# -- Trusted reverse proxy [Api, Admin, Portal] --
```

`src/TechStrap.Admin/.env.example`

Replace

```bash
TECHSTRAP_ADMIN_GROUP=techstrap-admins
TECHSTRAP_GROUP_CLAIM_TYPE=groups

# --- ASP.NET Core data protection key ring (persisted volume in containers; blank keeps keys in memory) ---
```

with

```bash
TECHSTRAP_ADMIN_GROUP=techstrap-admins
TECHSTRAP_GROUP_CLAIM_TYPE=groups

# --- Customer portal address (optional) ---
# The customer portal's public base URL, the same key the Api reads. The Admin uses it only for the "View on portal" link of a published knowledge base article; blank hides the link.
# Absolute http or https, no query or fragment. Example: http://localhost:8082
TECHSTRAP_PORTAL_PUBLIC_URL=

# --- ASP.NET Core data protection key ring (persisted volume in containers; blank keeps keys in memory) ---
```

`src/TechStrap.Admin/Features/Kb/KbArticleEditorPage.razor`

```razor
@page "/kb/new"
@page "/kb/{Id:guid}"
@attribute [Authorize]

<PageTitle>@Heading &middot; @KbCopy.Heading</PageTitle>
<NavigationLock OnBeforeInternalNavigation="OnBeforeNavigationAsync" ConfirmExternalNavigation="@IsDirty" />
<div class="ts-settings ts-kb-editor">
    <p class="ts-settings-back"><a href="/kb">&larr; @KbEditorCopy.BackToList</a></p>
    <h1>@Heading</h1>

    @if (_loading)
    {
        <LoadingState Rows="8" Label="@KbEditorCopy.Loading" />
    }
    else if (_gone)
    {
        <ErrorState Message="@KbEditorCopy.Gone" />
    }
    else if (_loadError is not null)
    {
        <ErrorState Message="@_loadError" OnRetry="LoadAsync" />
    }
    else
    {
        <div class="ts-kb-status">
            @if (!_creating)
            {
                <KbStatusBadge Status="@_model.Status" />
            }
            @if (PortalUrl is { } portalUrl)
            {
                <a class="ts-kb-portal-link" href="@portalUrl" target="_blank" rel="noopener noreferrer">@KbEditorCopy.ViewOnPortal</a>
            }
            @if (IsDirty && !_creating)
            {
                <span class="ts-dirty" role="status">@KbEditorCopy.Unsaved</span>
            }
        </div>

        @if (_conflict)
        {
            <div class="ts-conflict" role="alert">
                <p><strong>@KbEditorCopy.ConflictTitle</strong> @KbEditorCopy.ConflictKept</p>
                <button type="button" class="btn btn-outline-secondary" disabled="@_busy" @onclick="ReloadAsync">@KbEditorCopy.Reload</button>
            </div>
        }
        @if (_uncertainNote is not null)
        {
            <div class="ts-conflict" role="alert">
                <p>@_uncertainNote</p>
                @if (_creating)
                {
                    <a href="/kb">@KbEditorCopy.OpenList</a>
                }
                else
                {
                    <button type="button" class="btn btn-outline-secondary" disabled="@_busy" @onclick="ReloadAsync">@KbEditorCopy.Reload</button>
                }
            </div>
        }
        @if (_formError is not null)
        {
            <p class="ts-form-error" role="alert">@_formError</p>
        }

        <form class="ts-form ts-kb-form" novalidate @onsubmit="SaveAsync">
            <fieldset disabled="@_busy">
                @if (_creating)
                {
                    <div class="ts-field">
                        <label for="ts-kb-product" class="form-label">@KbEditorCopy.ProductLabel</label>
                        <select id="ts-kb-product" class="form-select" aria-describedby="ts-kb-product-help" @onchange="OnProductChanged">
                            <option value="" selected="@(_model.ProductId is null)">@KbEditorCopy.SharedOption</option>
                            @foreach (var product in _lookups.Products)
                            {
                                <option value="@product.Id" selected="@(_model.ProductId == product.Id)">@product.Name</option>
                            }
                        </select>
                        <p id="ts-kb-product-help" class="ts-field-help">@KbEditorCopy.ProductHelp</p>
                    </div>
                }
                else
                {
                    <dl class="ts-readonly">
                        <dt>@KbEditorCopy.ProductLabel</dt>
                        <dd>@_lookups.ProductName(_model.ProductId)</dd>
                        <dt>@KbEditorCopy.SlugLabel</dt>
                        <dd><code>@_model.Slug</code></dd>
                    </dl>
                }

                <div class="ts-field">
                    <label for="ts-kb-category" class="form-label">@KbEditorCopy.CategoryLabel</label>
                    <select id="ts-kb-category" class="form-select @(_errors.ContainsKey(ApiFields.Category) ? "is-invalid" : null)" aria-describedby="ts-kb-category-help" @onchange="OnCategoryChanged">
                        <option value="" selected="@(_model.CategoryId is null)">@KbEditorCopy.NoCategoryOption</option>
                        @foreach (var category in _lookups.CategoriesFor(_model.ProductId))
                        {
                            <option value="@category.Id" selected="@(_model.CategoryId == category.Id)">@category.Name</option>
                        }
                    </select>
                    <p id="ts-kb-category-help" class="ts-field-help">@KbEditorCopy.CategoryHelp</p>
                    @if (_errors.TryGetValue(ApiFields.Category, out var categoryError))
                    {
                        <p class="ts-field-error" role="alert">@categoryError</p>
                    }
                </div>

                @Field("ts-kb-title", KbEditorCopy.TitleLabel, ApiFields.Title, _model.Title, OnTitleInput)
                @if (_creating)
                {
                    @Field("ts-kb-slug", KbEditorCopy.SlugLabel, ApiFields.Slug, _model.Slug, OnSlugInput, KbEditorCopy.SlugHelp)
                }

                <div class="ts-field">
                    <label for="ts-kb-summary" class="form-label">@KbEditorCopy.SummaryLabel</label>
                    <textarea id="ts-kb-summary" class="form-control @(_errors.ContainsKey(ApiFields.Summary) ? "is-invalid" : null)" rows="3" aria-describedby="ts-kb-summary-help"
                              value="@_model.Summary" @oninput="OnSummaryInput" @onblur="() => CheckField(ApiFields.Summary)"></textarea>
                    <p id="ts-kb-summary-help" class="ts-field-help">@KbEditorCopy.SummaryHelp</p>
                    @if (_errors.TryGetValue(ApiFields.Summary, out var summaryError))
                    {
                        <p class="ts-field-error" role="alert">@summaryError</p>
                    }
                </div>

                <MarkdownEditor Id="ts-kb-body" Value="@_model.Body" ValueChanged="OnBodyChanged" Disabled="_busy" ErrorMessage="@(_errors.GetValueOrDefault(ApiFields.Body))"
                                OnBlur="() => CheckField(ApiFields.Body)" />
            </fieldset>

            <div class="ts-form-actions ts-kb-actions">
                <button type="submit" class="btn btn-primary" disabled="@(!CanSave)">@(_saving ? KbEditorCopy.Saving : _creating ? KbEditorCopy.SaveNew : KbEditorCopy.Save)</button>
                @if (!_creating && !_model.IsPublished)
                {
                    <button type="button" class="btn btn-outline-secondary" disabled="@(!CanPublish)" aria-describedby="ts-kb-publish-hint" @onclick="PublishAsync">@(_publishing ? KbEditorCopy.Publishing : KbEditorCopy.Publish)</button>
                }
                @if (!_creating && _model.Status != KbArticleStatuses.Archived)
                {
                    <button type="button" class="btn btn-outline-secondary" disabled="@(!CanArchive)" @onclick="AskArchive">@KbEditorCopy.Archive</button>
                }
            </div>
            @if (PublishHint is { } hint)
            {
                <p id="ts-kb-publish-hint" class="ts-field-help">@hint</p>
            }
        </form>
    }

    <ConfirmDialog Open="_archiveOpen" Title="@KbEditorCopy.ArchiveTitle" ConfirmLabel="@KbEditorCopy.ArchiveConfirm" Busy="_archiving"
                   OnConfirm="ConfirmArchiveAsync" OnCancel="CancelArchive">
        <p>@KbEditorCopy.ArchiveBody</p>
    </ConfirmDialog>

    <ConfirmDialog Open="@(_leaveTarget is not null)" Title="@KbEditorCopy.LeaveTitle" ConfirmLabel="@KbEditorCopy.LeaveConfirm" CancelLabel="@KbEditorCopy.LeaveStay"
                   Danger="true" OnConfirm="LeaveAsync" OnCancel="StayAsync">
        <p>@KbEditorCopy.LeaveBody</p>
    </ConfirmDialog>
</div>

@code {
    /// <summary>One labeled text input with its help text and its error; the value is bound on input and checked on blur.</summary>
    private RenderFragment Field(string id, string label, string field, string value, Action<string> set, string? help = null) => @<div class="ts-field">
        <label for="@id" class="form-label">@label</label>
        <input id="@id" type="text" class="form-control @(_errors.ContainsKey(field) ? "is-invalid" : null)" value="@value" autocomplete="off" spellcheck="@(field == ApiFields.Slug ? "false" : "true")"
               aria-invalid="@(_errors.ContainsKey(field) ? "true" : null)" aria-describedby="@(_errors.ContainsKey(field) ? $"{id}-error" : help is not null ? $"{id}-help" : null)"
               @oninput="e => set(e.Value?.ToString() ?? string.Empty)" @onblur="() => CheckField(field)" />
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

`src/TechStrap.Admin/Features/Kb/KbArticleEditorPage.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Options;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Options;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// Writes an article or edits one (any agent). Nothing on this page loses what the agent typed: a failed, refused or uncertain write leaves the form exactly as it was, and only a successful save
/// or an explicit Reload replaces the model. The update carries the <c>Version</c> the article was loaded with, so a stale save is a 409 and never an overwrite: the form is kept, a banner says so, and
/// saving stays off until the agent reloads. A write whose outcome is unknown is held (nothing more is sent) until a reload. Writes use <see cref="CancellationToken.None"/>: a save that is on its
/// way is never abandoned because the agent left the page, and what finishes after the page is gone changes nothing on it. Every load takes the next <c>_loadId</c> and only the latest may change
/// the screen. Unsaved changes are guarded twice: <c>NavigationLock</c> asks before an in-app move and the browser asks before the tab closes. Publishing needs the article saved first (it publishes what is
/// stored, not what is on screen), and an edit to an archived article makes it a draft again (the answer says so).
/// </summary>
public sealed partial class KbArticleEditorPage : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, string> _errors = [];
    private KbArticleEditorViewModel _model = new();
    private KbArticleEditorViewModel.Snapshot _saved = new KbArticleEditorViewModel().Take();
    private KbEditorLookups _lookups = KbEditorLookups.Empty;
    private string? _loadedFor;
    private string? _loadError;
    private string? _formError;
    private string? _uncertainNote;
    private string? _leaveTarget;
    private bool _creating;
    private bool _loading;
    private bool _busy;
    private bool _saving;
    private bool _publishing;
    private bool _archiving;
    private bool _archiveOpen;
    private bool _conflict;
    private bool _gone;
    private bool _slugEdited;
    private bool _allowLeave;
    private bool _disposed;
    private int _loadId;
    private int _epoch;

    [Inject]
    private KbArticleEditorPresenter Presenter { get; set; } = default!;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    [Inject]
    private IOptions<PortalUrlOptions> Portal { get; set; } = default!;

    /// <summary>The route segment: an article id, or absent on <c>/kb/new</c>.</summary>
    [Parameter]
    public Guid? Id { get; set; }

    private string Heading => _creating ? KbEditorCopy.NewTitle : string.IsNullOrWhiteSpace(_saved.Title) ? KbCopy.Heading : _saved.Title;

    private bool IsDirty => !_model.Take().Equals(_saved);

    private bool Held => _busy || _conflict || _uncertainNote is not null || _gone;

    // A new article can always be created; an existing one is saved once something changed. A create whose outcome is unknown may have made the article: no second create until the agent has looked at the list.
    private bool CanSave => !Held && (_creating || IsDirty);

    // Publish works on what is stored: the form must be saved, and complete.
    private bool CanPublish => !Held && !_creating && !IsDirty && _model.FirstMissingForPublish() is null;

    private bool CanArchive => !Held && !_creating && !IsDirty;

    private string? PublishHint =>
        _creating || _model.IsPublished ? null
        : IsDirty ? KbEditorCopy.SaveFirst
        : _model.FirstMissingForPublish() is { } missing ? KbEditorCopy.PublishNeeds(missing) : null;

    // Only a published article has an address on the portal, and it is the stored one: the category on screen may not be saved yet.
    private string? PortalUrl => _model.IsPublished && !_creating
        ? Portal.Value.ArticleUrl(_lookups.PortalProductKey(_model.ProductId), _lookups.CategorySlug(_saved.CategoryId), _model.Slug)
        : null;

    protected override async Task OnParametersSetAsync()
    {
        var key = Id?.ToString() ?? string.Empty;
        if (_loadedFor == key)
        {
            return;
        }

        _loadedFor = key;

        // A load or a write that is still running belongs to the previous article (or to the new-article form): it must change nothing here.
        _epoch++;
        _busy = false;
        _saving = false;
        _publishing = false;
        _archiving = false;
        _archiveOpen = false;
        ResetMessages();
        _creating = Id is null;
        _gone = false;
        _slugEdited = false;
        _model = new KbArticleEditorViewModel();
        _saved = _model.Take();
        await LoadAsync();
    }

    private void ResetMessages()
    {
        _conflict = false;
        _uncertainNote = null;
        _formError = null;
        _loadError = null;
        _errors.Clear();
    }

    private async Task LoadAsync()
    {
        // Only the latest load may change the screen: a slow answer that was overtaken (a reload, another article) is ignored.
        var loadId = ++_loadId;
        _loading = true;
        _loadError = null;
        try
        {
            var result = await Presenter.LoadAsync(Id, _lifetime.Token);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return;
            }

            if (result.IsFailure)
            {
                _gone = result.Errors[0].Code == ApiErrorCodes.KbArticleNotFound;
                _loadError = _gone ? null : $"{KbEditorCopy.LoadFailed} {result.Errors[0].Message}";
                return;
            }

            _lookups = result.Value.Lookups;
            _model = result.Value.Article is { } article ? KbArticleEditorViewModel.From(article) : new KbArticleEditorViewModel();
            _saved = _model.Take();
            _errors.Clear();
        }
        finally
        {
            if (loadId == _loadId)
            {
                _loading = false;
            }
        }
    }

    private async Task ReloadAsync()
    {
        ResetMessages();
        await LoadAsync();
    }

    // ---- editing -------------------------------------------------------------------------------------------------

    private void OnTitleInput(string value)
    {
        _model.Title = value;
        if (_creating && !_slugEdited)
        {
            _model.Slug = KbArticleEditorViewModel.SlugFrom(value);
        }
    }

    private void OnSlugInput(string value)
    {
        _model.Slug = value;
        _slugEdited = true;
    }

    private void OnSummaryInput(ChangeEventArgs e) => _model.Summary = e.Value?.ToString() ?? string.Empty;

    private void OnBodyChanged(string value) => _model.Body = value;

    private void OnCategoryChanged(ChangeEventArgs e)
    {
        _model.CategoryId = Guid.TryParse(e.Value as string, out var id) ? id : null;
        _errors.Remove(ApiFields.Category);
    }

    private void OnProductChanged(ChangeEventArgs e)
    {
        _model.ProductId = Guid.TryParse(e.Value as string, out var id) ? id : null;

        // A category that the new choice may not use (a shared article may use only a shared category) is dropped, so the form never offers a combination the API refuses.
        if (_model.CategoryId is { } category && _lookups.CategoriesFor(_model.ProductId).All(c => c.Id != category))
        {
            _model.CategoryId = null;
        }

        _errors.Remove(ApiFields.Category);
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
        foreach (var field in new[] { ApiFields.Title, ApiFields.Slug, ApiFields.Summary, ApiFields.Body })
        {
            CheckField(field);
        }

        return _errors.Count == 0;
    }

    // ---- save ----------------------------------------------------------------------------------------------------

    private async Task SaveAsync()
    {
        if (!CanSave)
        {
            return;
        }

        _formError = null;
        if (!CheckAll())
        {
            return;
        }

        var epoch = _epoch;
        var creating = _creating;
        var wasArchived = _model.Status == KbArticleStatuses.Archived;
        _busy = true;
        _saving = true;
        try
        {
            var result = creating
                ? await Kb.CreateAsync(_model.ToCreateRequest(), CancellationToken.None)
                : await Kb.UpdateAsync(_model.Id, _model.ToUpdateRequest(), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (epoch != _epoch)
            {
                // The agent moved to another article while this was on its way. The write is done and is never abandoned, but the screen is not its any more: say so once, change nothing else.
                if (result.IsSuccess)
                {
                    StatusMessages.Show(creating ? KbEditorCopy.Created(result.Value.Title) : KbEditorCopy.Saved(result.Value.Title));
                }

                return;
            }

            if (result.IsFailure)
            {
                ShowSaveFailure(result.Errors, creating);
                return;
            }

            ApplySaved(result.Value, creating, wasArchived);
        }
        finally
        {
            if (epoch == _epoch)
            {
                _busy = false;
                _saving = false;
            }
        }
    }

    private void ApplySaved(KbArticleDto article, bool created, bool wasArchived)
    {
        _model = KbArticleEditorViewModel.From(article);
        _saved = _model.Take();
        _errors.Clear();
        if (created)
        {
            StatusMessages.Show(KbEditorCopy.Created(article.Title));
            _creating = false;

            // The new address is this article: the page already holds it, so the parameter change that follows must not load it again. The form is saved, so the leave guard stays quiet.
            _loadedFor = article.Id.ToString();
            Navigation.NavigateTo($"/kb/{article.Id}", new NavigationOptions { ReplaceHistoryEntry = true });
            return;
        }

        StatusMessages.Show(wasArchived && article.Status == KbArticleStatuses.Draft ? KbEditorCopy.ArchivedReopened : KbEditorCopy.Saved(article.Title));
    }

    private void ShowSaveFailure(IReadOnlyList<ResultError> errors, bool creating)
    {
        var first = errors[0];
        if (WriteOutcomes.Classify(first) == WriteOutcome.Conflict)
        {
            _conflict = true;
        }
        else if (first.Code == ApiErrorCodes.KbArticleNotFound)
        {
            _gone = true;
        }
        else if (first.Code == ApiErrorCodes.KbSlugTaken)
        {
            _errors[ApiFields.Slug] = KbEditorCopy.SlugTaken;
        }
        else if (first.Code == ApiErrorCodes.KbCategoryScopeMismatch)
        {
            _errors[ApiFields.Category] = KbEditorCopy.CategoryScopeMismatch;
        }
        else if (first.Code == ApiErrorCodes.KbCategoryNotFound)
        {
            // The category was deleted meanwhile: the field says why the save was refused, and the agent picks another.
            _errors[ApiFields.Category] = KbEditorCopy.CategoryGone;
        }
        else if (ApiErrorCodes.IsUncertainWrite(first.Code))
        {
            _uncertainNote = creating ? KbEditorCopy.CreateUncertain : KbEditorCopy.SaveUncertain;
        }
        else
        {
            MapFieldErrors(errors, creating);
        }
    }

    // A 400 names the field in kebab-case. Anything the form has no field for is shown once, above the form, in the API's words.
    private void MapFieldErrors(IReadOnlyList<ResultError> errors, bool creating)
    {
        foreach (var error in errors)
        {
            var target = FieldOf(error.Target);
            if (target is ApiFields.Title or ApiFields.Summary or ApiFields.Body or ApiFields.Category || (creating && target is ApiFields.Slug))
            {
                _errors.TryAdd(target, error.Message);
            }
            else
            {
                _formError ??= error.Message;
            }
        }
    }

    // The API names a category error by the request property (categoryId) and a publish error by the field (category): both are the category select.
    private static string? FieldOf(string? target) => target is ApiFields.CategoryId or "category-id" ? ApiFields.Category : target;

    // ---- publish and archive -------------------------------------------------------------------------------------

    private async Task PublishAsync()
    {
        if (!CanPublish)
        {
            return;
        }

        _formError = null;
        var epoch = _epoch;
        var id = _model.Id;
        var version = _model.Version;
        var title = _model.Title;
        _busy = true;
        _publishing = true;
        try
        {
            var result = await Kb.PublishAsync(id, version, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (epoch != _epoch)
            {
                if (result.IsSuccess)
                {
                    StatusMessages.Show(KbEditorCopy.Published(title));
                }

                return;
            }

            if (result.IsFailure)
            {
                await ShowPublishFailureAsync(result.Errors[0]);
                return;
            }

            ApplyServer(result.Value);
            StatusMessages.Show(KbEditorCopy.Published(title));
        }
        finally
        {
            if (epoch == _epoch)
            {
                _busy = false;
                _publishing = false;
            }
        }
    }

    private async Task ShowPublishFailureAsync(ResultError error)
    {
        if (WriteOutcomes.Classify(error) == WriteOutcome.Conflict)
        {
            _conflict = true;
        }
        else if (error.Code == ApiErrorCodes.KbPublishIncomplete)
        {
            // The API names the field it missed (title, slug, body or category); the form said so already when it could, this is the case where another agent emptied it meanwhile.
            _formError = KbEditorCopy.PublishNeeds(error.Target ?? string.Empty);
            if (FieldOf(error.Target) is { Length: > 0 } target)
            {
                _errors[target] = _formError;
            }
        }
        else if (error.Code == ApiErrorCodes.KbArticleNotFound)
        {
            _gone = true;
        }
        else if (error.Code == "article-already-published")
        {
            StatusMessages.Show(KbEditorCopy.AlreadyPublished);
            await RefreshAfterWriteAsync(_epoch);
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _uncertainNote = KbEditorCopy.PublishUncertain;
        }
        else
        {
            _formError = error.Message;
        }
    }

    private void AskArchive()
    {
        if (CanArchive)
        {
            _archiveOpen = true;
        }
    }

    private void CancelArchive()
    {
        if (!_archiving)
        {
            _archiveOpen = false;
        }
    }

    private async Task ConfirmArchiveAsync()
    {
        if (_archiving || _creating || _conflict || _uncertainNote is not null)
        {
            return;
        }

        var epoch = _epoch;
        var id = _model.Id;
        var version = _model.Version;
        var title = _model.Title;
        _archiving = true;
        try
        {
            var result = await Kb.ArchiveAsync(id, version, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (epoch != _epoch)
            {
                if (result.IsSuccess)
                {
                    StatusMessages.Show(KbEditorCopy.Archived(title));
                }

                return;
            }

            _archiveOpen = false;
            if (result.IsFailure)
            {
                await ShowArchiveFailureAsync(result.Errors[0]);
                return;
            }

            ApplyServer(result.Value);
            StatusMessages.Show(KbEditorCopy.Archived(title));
        }
        finally
        {
            if (epoch == _epoch)
            {
                _archiving = false;
            }
        }
    }

    private async Task ShowArchiveFailureAsync(ResultError error)
    {
        if (WriteOutcomes.Classify(error) == WriteOutcome.Conflict)
        {
            _conflict = true;
        }
        else if (error.Code == ApiErrorCodes.KbArticleNotFound)
        {
            _gone = true;
        }
        else if (error.Code == "article-already-archived")
        {
            StatusMessages.Show(KbEditorCopy.AlreadyArchived);
            await RefreshAfterWriteAsync(_epoch);
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _uncertainNote = KbEditorCopy.ArchiveUncertain;
        }
        else
        {
            _formError = error.Message;
        }
    }

    // The article as the API says it is now (what a publish or an archive answered): the form and the saved snapshot both take it, so the version the next write sends is the new one.
    private void ApplyServer(KbArticleDto article)
    {
        _model = KbArticleEditorViewModel.From(article);
        _saved = _model.Take();
        _errors.Clear();
    }

    // Someone else already did what the agent asked for (published, archived): the screen is read again. If that read fails the form is held until a reload.
    private async Task RefreshAfterWriteAsync(int epoch)
    {
        var loadId = ++_loadId;
        var result = await Presenter.ReloadAsync(_model.Id, _lifetime.Token);
        if (_lifetime.IsCancellationRequested || epoch != _epoch || loadId != _loadId)
        {
            return;
        }

        if (result.IsSuccess)
        {
            _model = KbArticleEditorViewModel.From(result.Value);
            _saved = _model.Take();
            _errors.Clear();
        }
        else if (result.Errors[0].Code == ApiErrorCodes.KbArticleNotFound)
        {
            _gone = true;
        }
        else
        {
            _uncertainNote = KbEditorCopy.ReloadFailed;
        }
    }

    // ---- leaving -------------------------------------------------------------------------------------------------

    private Task OnBeforeNavigationAsync(LocationChangingContext context)
    {
        if (_allowLeave || !IsDirty)
        {
            return Task.CompletedTask;
        }

        context.PreventNavigation();
        _leaveTarget = context.TargetLocation;
        return Task.CompletedTask;
    }

    private Task LeaveAsync()
    {
        var target = _leaveTarget;
        _leaveTarget = null;
        _allowLeave = true;
        if (target is not null)
        {
            Navigation.NavigateTo(target);
        }

        return Task.CompletedTask;
    }

    private Task StayAsync()
    {
        _leaveTarget = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

`src/TechStrap.Admin/Features/Kb/KbArticleEditorPresenter.cs`

```csharp
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Kb;

/// <summary>What the editor's choices and the portal link resolve against: the products an agent can see and every category.</summary>
internal sealed record KbEditorLookups(IReadOnlyList<ProductDto> Products, IReadOnlyList<KbCategoryDto> Categories)
{
    public static KbEditorLookups Empty { get; } = new([], []);

    /// <summary>
    /// The categories an article of <paramref name="productId"/> may use, in sort order: the shared ones, and (for a product article) the product's own. A shared article may use only a shared
    /// category, which is the API's rule (kb-category-scope-mismatch).
    /// </summary>
    public IReadOnlyList<KbCategoryDto> CategoriesFor(Guid? productId) =>
        [.. Categories.Where(c => c.ProductId is null || c.ProductId == productId).OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)];

    public string ProductName(Guid? productId) => KbArticleRowViewModel.ProductNameOf(productId, Products);

    /// <summary>
    /// The product key an article's portal address is built with. A shared article is reachable under every product, so the first product by name stands in; null when there is no product to
    /// stand in (no link is shown then).
    /// </summary>
    public string? PortalProductKey(Guid? productId) => productId is { } id
        ? Products.FirstOrDefault(p => p.Id == id)?.Key
        : Products.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault()?.Key;

    public string? CategorySlug(Guid? categoryId) => categoryId is { } id ? Categories.FirstOrDefault(c => c.Id == id)?.Slug : null;
}

/// <summary>An article together with what the editor needs around it. The article is null when a new one is being written.</summary>
internal sealed record KbEditorData(KbArticleDto? Article, KbEditorLookups Lookups);

/// <summary>
/// Assembles the editor's data: the article, the products and the categories. It exists because the assembly is asynchronous and uses two clients (PHASE-08 allows exactly this presenter). The
/// article's own failure wins over a lookup failure, so a missing article is reported as missing; a lookup that 404s is turned into a plain failure so it can never read as "article not found".
/// </summary>
internal sealed class KbArticleEditorPresenter(IKbClient kb, IProductsClient products)
{
    /// <param name="articleId">The article to edit, or null for a new one.</param>
    public async Task<Result<KbEditorData>> LoadAsync(Guid? articleId, CancellationToken cancellationToken)
    {
        var articleTask = articleId is { } id ? kb.GetAsync(id, cancellationToken) : null;
        var productsTask = products.ListAsync(cancellationToken);
        var categoriesTask = kb.ListCategoriesAsync(cancellationToken);
        await Task.WhenAll(new Task?[] { articleTask, productsTask, categoriesTask }.OfType<Task>());

        if (articleTask is not null && articleTask.Result.IsFailure)
        {
            return Result<KbEditorData>.Failure(articleTask.Result.Errors[0]);
        }

        foreach (var lookup in new Result[] { productsTask.Result.ToResult(), categoriesTask.Result.ToResult() })
        {
            if (lookup.IsFailure)
            {
                var error = lookup.Errors[0];
                return Result<KbEditorData>.Failure(error.Kind == ResultErrorKind.NotFound ? new ResultError(error.Code, error.Message, ResultErrorKind.Failure) : error);
            }
        }

        return Result<KbEditorData>.Success(new KbEditorData(articleTask?.Result.Value, new KbEditorLookups(productsTask.Result.Value, categoriesTask.Result.Value)));
    }

    /// <summary>Reads the article again, for the screen's own refresh after a write; the lookups on screen are reused.</summary>
    public Task<Result<KbArticleDto>> ReloadAsync(Guid articleId, CancellationToken cancellationToken) => kb.GetAsync(articleId, cancellationToken);
}
```

`src/TechStrap.Admin/Features/Kb/KbArticleEditorViewModel.cs`

```csharp
using System.Text.RegularExpressions;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Features.Kb;

/// <summary>The limits the editor checks before it sends. They are the server's own (the Domain's), so a message here is never stricter than the API.</summary>
public static class KbEditorLimits
{
    public const int TitleMaxLength = 200;
    public const int SummaryMaxLength = 500;
    public const int SlugMaxLength = 80;

    /// <summary>The longest article body. The API says the preview limit equals it, so a body that can be saved can be previewed.</summary>
    public const int BodyMaxLength = KbLimits.MaxPreviewChars;
}

/// <summary>
/// The form model of the article editor. It holds exactly what is on screen, checks it with the server's rules and builds the requests. <see cref="Version"/> is the one the article was loaded with:
/// it travels with every update, so a stale save is a 409 and never an overwrite. The product and the slug are chosen once, when the article is created; after that they are only shown.
/// </summary>
internal sealed partial class KbArticleEditorViewModel
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlugCharacters();

    /// <summary>The article's id once it exists; empty while it is being created.</summary>
    public Guid Id { get; set; }

    public Guid? ProductId { get; set; }

    public Guid? CategoryId { get; set; }

    public string Slug { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>One of <see cref="KbArticleStatuses"/>; Draft for an article that has not been created yet.</summary>
    public string Status { get; set; } = KbArticleStatuses.Draft;

    public uint Version { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public bool IsPublished => Status == KbArticleStatuses.Published;

    public static KbArticleEditorViewModel From(KbArticleDto article) => new()
    {
        Id = article.Id,
        ProductId = article.ProductId,
        CategoryId = article.CategoryId,
        Slug = article.Slug,
        Title = article.Title,
        Summary = article.Summary ?? string.Empty,
        Body = article.BodyMarkdown,
        Status = article.Status,
        Version = article.Version,
        PublishedAt = article.PublishedAt,
    };

    public CreateKbArticleRequest ToCreateRequest() => new(ProductId, CategoryId, Slug.Trim(), Title.Trim(), Blank(Summary), Body);

    public UpdateKbArticleRequest ToUpdateRequest() => new(CategoryId, Title.Trim(), Blank(Summary), Body, Version);

    /// <summary>The fields an agent can change, as one value, so "has anything changed since it was loaded or saved" is a comparison and not a flag that stays set when the text is typed back.</summary>
    public Snapshot Take() => new(ProductId, CategoryId, Slug, Title, Summary, Body);

    public sealed record Snapshot(Guid? ProductId, Guid? CategoryId, string Slug, string Title, string Summary, string Body);

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The message for one field, or null when it is fine. The slug is checked only when the article is being created: afterwards it cannot change.</summary>
    public string? Check(string field, bool creating) => field switch
    {
        ApiFields.Title => string.IsNullOrWhiteSpace(Title) ? KbEditorCopy.TitleRequired
            : Title.Trim().Length > KbEditorLimits.TitleMaxLength ? KbEditorCopy.TitleTooLong : null,
        ApiFields.Slug when creating => string.IsNullOrWhiteSpace(Slug) ? KbEditorCopy.SlugRequired
            : Slug.Trim().Length > KbEditorLimits.SlugMaxLength || !SlugPattern().IsMatch(Slug.Trim()) ? KbEditorCopy.SlugInvalid : null,
        ApiFields.Summary => Summary.Trim().Length > KbEditorLimits.SummaryMaxLength ? KbEditorCopy.SummaryTooLong : null,
        ApiFields.Body => string.IsNullOrWhiteSpace(Body) ? KbEditorCopy.BodyRequired
            : Body.Length > KbEditorLimits.BodyMaxLength ? KbEditorCopy.BodyTooLong : null,
        _ => null,
    };

    /// <summary>The first thing publishing needs that is missing, as the API's own field name (title, slug, body or category), or null when the article can be published.</summary>
    public string? FirstMissingForPublish() =>
        string.IsNullOrWhiteSpace(Title) ? ApiFields.Title
        : string.IsNullOrWhiteSpace(Slug) ? ApiFields.Slug
        : string.IsNullOrWhiteSpace(Body) ? ApiFields.Body
        : CategoryId is null ? ApiFields.Category
        : null;

    /// <summary>A slug suggested from a title ("Reset your password" gives "reset-your-password"). The agent can change it until the article is created.</summary>
    public static string SlugFrom(string title)
    {
        var slug = NotSlugCharacters().Replace(title.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug.Length > KbEditorLimits.SlugMaxLength ? slug[..KbEditorLimits.SlugMaxLength].TrimEnd('-') : slug;
    }
}
```

`src/TechStrap.Admin/Features/Kb/KbEditorCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Kb;

/// <summary>The words of the article editor, the preview and the image button. Plain, sentence case (docs/BRAND.md section 3).</summary>
public static class KbEditorCopy
{
    public const string NewTitle = "New article";
    public const string BackToList = "Knowledge base";
    public const string Loading = "Loading article";
    public const string LoadFailed = "Couldn't load this article.";
    public const string Gone = "This article no longer exists.";

    public const string ProductLabel = "Product";
    public const string SharedOption = "Shared by every product";
    public const string ProductHelp = "Chosen once. It can't be changed after the article is created.";
    public const string CategoryLabel = "Category";
    public const string NoCategoryOption = "No category";
    public const string CategoryHelp = "Needed to publish: the portal address of an article includes its category.";
    public const string TitleLabel = "Title";
    public const string SlugLabel = "Slug";
    public const string SlugHelp = "Letters, numbers and hyphens. It is part of the article's address and can't be changed once the article exists.";
    public const string SummaryLabel = "Summary";
    public const string SummaryHelp = "One or two sentences. It is shown in search results.";
    public const string BodyLabel = "Article";

    public const string SaveNew = "Create draft";
    public const string Save = "Save";
    public const string Saving = "Saving\u2026";
    public const string Publish = "Publish";
    public const string Publishing = "Publishing\u2026";
    public const string Archive = "Archive";
    public const string Unsaved = "Unsaved changes";
    public const string SaveFirst = "Save your changes before you publish.";
    public const string ViewOnPortal = "View on portal";

    public const string TitleRequired = "Enter a title.";
    public const string TitleTooLong = "Use 200 characters or fewer.";
    public const string SlugRequired = "Enter a slug.";
    public const string SlugInvalid = "Use lower-case letters, numbers and single hyphens, up to 80 characters.";
    public const string SlugTaken = "Another article already uses this slug. Slugs are shared across products, so pick another.";
    public const string SummaryTooLong = "Use 500 characters or fewer.";
    public const string BodyRequired = "Write the article before you save it.";
    public const string BodyTooLong = "The article is too long to save.";
    public const string CategoryScopeMismatch = "A shared article can only use a shared category.";

    public const string ConflictTitle = "This article changed since you opened it.";
    public const string ConflictKept = "Your edits are still in the form. Reloading brings in the latest version and replaces them, so copy anything you need first.";
    public const string Reload = "Reload article";
    public const string SaveUncertain = "The change may have gone through. Reload the article to check before you try again.";
    public const string CreateUncertain = "The article may have been created. Open the list to check before you try again.";
    public const string OpenList = "Open the list";
    public const string PublishUncertain = "The publish may have gone through. Reload the article to check before you try again.";
    public const string ArchiveUncertain = "The archive may have gone through. Reload the article to check before you try again.";

    public const string ArchivedReopened = "Saved. This archived article is a draft again: publish it to put it back on the portal.";
    public const string ReloadFailed = "The article could not be reloaded. Reload it before you edit again.";
    public const string CategoryGone = "That category no longer exists. Choose another.";
    public const string AlreadyPublished = "Someone published this article already. It has been reloaded.";
    public const string AlreadyArchived = "Someone archived this article already. It has been reloaded.";

    public const string ArchiveTitle = "Archive this article?";
    public const string ArchiveBody = "It is removed from the portal, from search and from the sitemap. You can edit it later, which makes it a draft again, and publish it again.";
    public const string ArchiveConfirm = "Archive article";
    public const string LeaveTitle = "Leave without saving?";
    public const string LeaveBody = "You have unsaved changes. If you leave now they are lost.";
    public const string LeaveConfirm = "Leave";
    public const string LeaveStay = "Stay";

    public const string ToolbarLabel = "Formatting";
    public const string BoldButton = "Bold";
    public const string ItalicButton = "Italic";
    public const string LinkButton = "Link";
    public const string ListButton = "List";
    public const string CodeButton = "Code";
    public const string ToolbarHint = "Each button adds its Markdown at the end of the article.";

    public const string WriteTab = "Write";
    public const string PreviewTab = "Preview";
    public const string PreviewLabel = "Preview of the article";
    public const string PreviewEmpty = "Nothing to preview yet.";
    public const string PreviewWorking = "Updating the preview";
    public const string PreviewTooLong = "The article is too long to preview or save. Shorten it; the preview shows the last version that fitted.";
    public const string PreviewFailed = "The preview is not available right now. Your text is kept; the preview updates when you type again.";

    public const string ImageButton = "Add image";
    public const string ImageUploading = "Uploading the image\u2026";
    public const string ImageTypeNotAllowed = "Use a PNG, JPEG, GIF or WebP picture.";
    public const string ImageRefused = "The API did not accept this picture.";
    public const string ImageUncertain = "The upload may not have finished. Nothing was added to the article; pick the picture again.";
    public const string ImageFailed = "Couldn't upload the picture. Nothing was added to the article.";

    public static string ImageTooLarge(string limit) => $"This picture is larger than {limit}.";

    public static string Created(string title) => $"Created the draft {title}";

    public static string Saved(string title) => $"Saved {title}";

    public static string Published(string title) => $"Published {title}";

    public static string Archived(string title) => $"Archived {title}";

    public static string PublishNeeds(string field) => field switch
    {
        "title" => "Add a title before you publish.",
        "slug" => "Add a slug before you publish.",
        "body" => "Write the article before you publish.",
        _ => "Choose a category before you publish.",
    };
}
```

`src/TechStrap.Admin/Features/Kb/KbImageUploadButton.razor`

```razor
<div class="ts-kb-image">
    <label for="@_inputId" class="ts-kb-image-label">@KbEditorCopy.ImageButton</label>
    @* A new input after every pick, so the same picture can be chosen again after a failure. *@
    <InputFile @key="_inputKey" id="@_inputId" OnChange="OnPickedAsync" accept="@Accept" disabled="@(Disabled || _uploading)" />
    @if (_uploading)
    {
        <span class="ts-kb-image-status" role="status">@KbEditorCopy.ImageUploading</span>
    }
    @if (_error is not null)
    {
        <p class="ts-field-error" role="alert">@_error</p>
    }
</div>
```

`src/TechStrap.Admin/Features/Kb/KbImageUploadButton.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Features.Kb;

/// <summary>An uploaded picture: the address the API serves it from, and the alt text the editor puts in its Markdown.</summary>
public sealed record KbUploadedImage(string AltText, string Url);

/// <summary>
/// Picks one picture, checks its type and size before anything is sent, uploads it and raises <see cref="OnUploaded"/> once. A picture the browser or the API refuses never changes the article.
/// An upload has no effect on any article until the editor adds the returned address, so an answer that was lost needs no hold: the agent picks the picture again, and the earlier copy, if the API kept
/// one, is an unreferenced file (D-044: no orphan cleanup). The write is never canceled by the screen closing (<see cref="CancellationToken.None"/>); a result that arrives after that is dropped.
/// </summary>
public sealed partial class KbImageUploadButton : IDisposable
{
    private readonly string _inputId = $"ts-kb-image-{Guid.NewGuid():N}"[..20];
    private string? _error;
    private bool _uploading;
    private bool _disposed;
    private int _inputKey;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private ILogger<KbImageUploadButton> Logger { get; set; } = default!;

    /// <summary>Disables the picker while the form it belongs to is busy.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>Raised once per accepted picture, after the API stored it.</summary>
    [Parameter]
    public EventCallback<KbUploadedImage> OnUploaded { get; set; }

    private static string Accept => string.Join(',', KbDefaults.ImageExtensions);

    private async Task OnPickedAsync(InputFileChangeEventArgs e)
    {
        if (_uploading)
        {
            return;
        }

        _error = null;
        var file = e.File;
        if (!KbDefaults.ImageExtensions.Contains(Path.GetExtension(file.Name), StringComparer.OrdinalIgnoreCase))
        {
            _error = KbEditorCopy.ImageTypeNotAllowed;
            _inputKey++;
            return;
        }

        if (file.Size > KbLimits.MaxImageBytes)
        {
            _error = KbEditorCopy.ImageTooLarge(TicketDisplay.FileSize(KbLimits.MaxImageBytes));
            _inputKey++;
            return;
        }

        _uploading = true;
        StateHasChanged();
        try
        {
            var result = await Kb.UploadImageAsync(new KbImageFile(file.Name, file.ContentType, () => file.OpenReadStream(KbLimits.MaxImageBytes)), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                await OnUploaded.InvokeAsync(new KbUploadedImage(MarkdownSnippets.AltFromFileName(file.Name), result.Value.Url));
            }
            else
            {
                _error = ErrorFor(result.Errors[0]);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A file that cannot be read (the browser dropped the handle) or any other fault: only the type is logged, never the file name.
            Logger.LogWarning("An image upload ended with an unmapped {ExceptionType}.", ex.GetType().Name);
            if (!_disposed)
            {
                _error = KbEditorCopy.ImageUncertain;
            }
        }
        finally
        {
            if (!_disposed)
            {
                _uploading = false;
                _inputKey++;
            }
        }
    }

    private static string ErrorFor(SyntaxCircus.Common.ResultError error) => error.Code switch
    {
        ApiErrorCodes.KbImageTypeNotAllowed => KbEditorCopy.ImageTypeNotAllowed,
        ApiErrorCodes.KbImageTooLarge or ApiErrorCodes.RequestTooLarge => KbEditorCopy.ImageTooLarge(TicketDisplay.FileSize(KbLimits.MaxImageBytes)),
        _ when ApiErrorCodes.IsUncertainWrite(error.Code) => KbEditorCopy.ImageUncertain,
        _ => $"{KbEditorCopy.ImageFailed} {error.Message}",
    };

    public void Dispose() => _disposed = true;
}
```

`src/TechStrap.Admin/Features/Kb/KbPreviewPane.razor`

```razor
@* The ONE place the Admin renders HTML an agent's text produced. Html is what POST /api/kb/preview answered: the API ran the Markdown through the same renderer and sanitizer as the portal, so nothing
   the agent typed reaches this markup except through that answer. Nothing else in the Admin may use MarkupString (MarkupStringSiteTests). When the last call failed the previous good preview stays on
   screen under a fixed message (never the API's text), so a failed refresh never blanks what the agent was reading. *@
<section class="ts-kb-preview" aria-label="@KbEditorCopy.PreviewLabel" aria-busy="@(Loading ? "true" : null)">
    @if (ErrorText is not null)
    {
        <p class="ts-kb-preview-error" role="status">@ErrorText</p>
    }
    @if (!string.IsNullOrEmpty(Html))
    {
        <div class="ts-kb-preview-body">@((MarkupString)Html)</div>
    }
    else if (ErrorText is null)
    {
        <p class="ts-kb-preview-empty">@(Loading ? KbEditorCopy.PreviewWorking : KbEditorCopy.PreviewEmpty)</p>
    }
</section>

@code {
    /// <summary>The sanitized HTML the preview call returned; empty before the first answer and for an empty article.</summary>
    [Parameter]
    public string? Html { get; set; }

    /// <summary>A call is in flight.</summary>
    [Parameter]
    public bool Loading { get; set; }

    /// <summary>Fixed copy that says why the preview could not be updated (never text from the API); null when it could.</summary>
    [Parameter]
    public string? ErrorText { get; set; }
}
```

`src/TechStrap.Admin/Features/Kb/KbServiceCollectionExtensions.cs`

```csharp
namespace TechStrap.Admin.Features.Kb;

public static class KbServiceCollectionExtensions
{
    /// <summary>Registers the knowledge base screens' services. Scoped: one presenter per circuit, built over that circuit's clients.</summary>
    public static IServiceCollection AddKbFeatures(this IServiceCollection services)
    {
        services.AddScoped<KbArticleEditorPresenter>();
        return services;
    }
}
```

`src/TechStrap.Admin/Features/Kb/MarkdownEditor.razor`

```razor
<div class="ts-kb-md" data-pane="@(_showPreview ? "preview" : "write")">
    <div class="ts-kb-toolbar" role="toolbar" aria-label="@KbEditorCopy.ToolbarLabel" aria-controls="@Id">
        <button type="button" class="btn btn-outline-secondary" disabled="@Disabled" @onclick="() => Add(MarkdownSnippets.Bold, inline: true)"><strong>B</strong><span class="visually-hidden">@KbEditorCopy.BoldButton</span></button>
        <button type="button" class="btn btn-outline-secondary" disabled="@Disabled" @onclick="() => Add(MarkdownSnippets.Italic, inline: true)"><em>I</em><span class="visually-hidden">@KbEditorCopy.ItalicButton</span></button>
        <button type="button" class="btn btn-outline-secondary" disabled="@Disabled" @onclick="() => Add(MarkdownSnippets.Link, inline: true)">@KbEditorCopy.LinkButton</button>
        <button type="button" class="btn btn-outline-secondary" disabled="@Disabled" @onclick="() => Add(MarkdownSnippets.List, inline: false)">@KbEditorCopy.ListButton</button>
        <button type="button" class="btn btn-outline-secondary" disabled="@Disabled" @onclick="() => Add(MarkdownSnippets.Code, inline: false)">@KbEditorCopy.CodeButton</button>
        <KbImageUploadButton Disabled="Disabled" OnUploaded="OnImageUploadedAsync" />
    </div>
    <p class="ts-field-help">@KbEditorCopy.ToolbarHint</p>

    @* Below 992 px the two panes share the width, so one is shown at a time (_kb.scss); from 992 px up both are always shown. *@
    <div class="ts-kb-tabs" role="group" aria-label="@KbEditorCopy.BodyLabel">
        <button type="button" class="btn btn-outline-secondary" aria-pressed="@(_showPreview ? "false" : "true")" @onclick="() => ShowPreview(false)">@KbEditorCopy.WriteTab</button>
        <button type="button" class="btn btn-outline-secondary" aria-pressed="@(_showPreview ? "true" : "false")" @onclick="() => ShowPreview(true)">@KbEditorCopy.PreviewTab</button>
    </div>

    <div class="ts-kb-panes">
        <div class="ts-kb-pane ts-kb-pane--write">
            <label for="@Id" class="form-label">@KbEditorCopy.BodyLabel</label>
            <textarea id="@Id" class="form-control ts-kb-textarea @(ErrorMessage is null ? null : "is-invalid")" rows="20" spellcheck="true" disabled="@Disabled"
                      aria-invalid="@(ErrorMessage is null ? null : "true")" aria-describedby="@(ErrorMessage is null ? null : $"{Id}-error")"
                      value="@Value" @oninput="OnInputAsync" @onblur="OnBlur"></textarea>
            @if (ErrorMessage is not null)
            {
                <p id="@($"{Id}-error")" class="ts-field-error" role="alert">@ErrorMessage</p>
            }
        </div>
        <div class="ts-kb-pane ts-kb-pane--preview">
            <KbPreviewPane Html="@_html" Loading="_previewing" ErrorText="@_previewError" />
        </div>
    </div>
</div>
```

`src/TechStrap.Admin/Features/Kb/MarkdownEditor.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// A plain textarea with a toolbar and a live preview beside it (PHASE-08: no editor library and no JavaScript). The text lives in the owner: this component reports every change through
/// <see cref="ValueChanged"/> and never keeps text of its own. Whenever <see cref="Value"/> changes, the preview waits <see cref="KbDefaults.PreviewDebounce"/> after the last change and then asks the API
/// to render it (<see cref="IKbClient.PreviewAsync"/>), so a burst of typing is one call. A call that a newer change has replaced is canceled, and an answer that arrives after a newer call started
/// is ignored (<c>_previewId</c>). A failed preview leaves the text and the last good preview alone and says so beside it. Nothing is previewed for an empty text. The timer and the call are
/// released when the component goes.
/// </summary>
public sealed partial class MarkdownEditor : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _call;
    private ITimer? _timer;
    private string? _scheduled;
    private string _html = string.Empty;
    private bool _previewing;
    private string? _previewError;
    private bool _showPreview;
    private bool _disposed;
    private int _previewId;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Inject]
    private ILogger<MarkdownEditor> Logger { get; set; } = default!;

    [Parameter, EditorRequired]
    public string Value { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    /// <summary>The id of the textarea, so the page's label, error and toolbar controls name it.</summary>
    [Parameter, EditorRequired]
    public string Id { get; set; } = string.Empty;

    /// <summary>Disables the text and the toolbar while the form around it is busy.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>The field's error, shown under the text.</summary>
    [Parameter]
    public string? ErrorMessage { get; set; }

    /// <summary>Raised when the text area loses focus, so the owner can check the field.</summary>
    [Parameter]
    public EventCallback<FocusEventArgs> OnBlur { get; set; }

    protected override void OnParametersSet()
    {
        // The text can change from outside (a reload, an image added) as well as by typing; both arrive here, so this is the one place the preview is scheduled.
        if (Value == _scheduled)
        {
            return;
        }

        _scheduled = Value;
        _call?.Cancel();
        _timer?.Dispose();
        _timer = Time.CreateTimer(_ => _ = InvokeAsync(RefreshPreviewAsync), null, KbDefaults.PreviewDebounce, Timeout.InfiniteTimeSpan);
    }

    private async Task RefreshPreviewAsync()
    {
        _timer?.Dispose();
        _timer = null;
        if (_disposed)
        {
            return;
        }

        var id = ++_previewId;
        var text = Value;
        if (string.IsNullOrWhiteSpace(text))
        {
            _call?.Cancel();
            _html = string.Empty;
            _previewing = false;
            _previewError = null;
            StateHasChanged();
            return;
        }

        if (text.Length > KbEditorLimits.BodyMaxLength)
        {
            // The API refuses a text it could not save either (400 body-too-long), so it is not asked on every keystroke; the last good preview stays.
            _call?.Cancel();
            _previewing = false;
            _previewError = KbEditorCopy.PreviewTooLong;
            StateHasChanged();
            return;
        }

        _call?.Cancel();
        _call?.Dispose();
        var call = _call = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _previewing = true;
        StateHasChanged();
        try
        {
            var result = await Kb.PreviewAsync(new KbPreviewRequest(text), call.Token);
            if (_disposed || id != _previewId)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _html = result.Value.Html;
                _previewError = null;
            }
            else
            {
                _previewError = KbEditorCopy.PreviewFailed;
            }
        }
        catch (OperationCanceledException) when (call.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (id == _previewId && !_disposed)
        {
            // Only the type is logged: an exception message can carry text from the article.
            Logger.LogWarning("A preview ended with an unmapped {ExceptionType}.", ex.GetType().Name);
            _previewError = KbEditorCopy.PreviewFailed;
        }
        finally
        {
            if (!_disposed && id == _previewId)
            {
                _previewing = false;
                StateHasChanged();
            }
        }
    }

    private Task OnInputAsync(ChangeEventArgs e) => ValueChanged.InvokeAsync(e.Value as string ?? string.Empty);

    private Task Add(string snippet, bool inline) => ValueChanged.InvokeAsync(MarkdownSnippets.Append(Value, snippet, inline));

    private Task OnImageUploadedAsync(KbUploadedImage image) => ValueChanged.InvokeAsync(MarkdownSnippets.Append(Value, MarkdownSnippets.Image(image.AltText, image.Url), inline: false));

    private void ShowPreview(bool show) => _showPreview = show;

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
        _call?.Dispose();
    }
}
```

`src/TechStrap.Admin/Features/Kb/MarkdownSnippets.cs`

````csharp
using System.Text;
using System.Text.RegularExpressions;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// The Markdown the editor's toolbar adds. Blazor cannot read where the caret is in a textarea, so a snippet is always added at the end of the text (D-044: no caret insertion, paste or
/// drag-and-drop yet). Each one starts on its own line after a blank line, except an inline one, which follows a space, so it never glues itself to the last word.
/// </summary>
public static partial class MarkdownSnippets
{
    public const string Bold = "**bold text**";
    public const string Italic = "*italic text*";
    public const string Link = "[link text](https://)";
    public const string List = "- first item\n- second item";
    public const string Code = "```\ncode\n```";

    /// <summary>The longest alt text kept for an image whose file name is the only description the editor has.</summary>
    public const int MaxAltLength = 100;

    [GeneratedRegex(@"[\[\]()<>\\`*_\r\n]+")]
    private static partial Regex AltUnsafe();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary><paramref name="text"/> with <paramref name="snippet"/> after it: a block starts after a blank line, an inline one after a space.</summary>
    public static string Append(string? text, string snippet, bool inline)
    {
        if (string.IsNullOrEmpty(text))
        {
            return snippet;
        }

        var separator = inline
            ? text.EndsWith(' ') || text.EndsWith('\n') ? string.Empty : " "
            : text.EndsWith("\n\n", StringComparison.Ordinal) ? string.Empty : text.EndsWith('\n') ? "\n" : "\n\n";
        return text + separator + snippet;
    }

    /// <summary>
    /// <c>![alt](url)</c>. The alt text is cleaned of the characters that would end or change the Markdown. In the address a space and the two round brackets are percent-encoded: they are the
    /// characters that would end the link early.
    /// </summary>
    public static string Image(string altText, string url)
    {
        var encoded = new StringBuilder(url.Length);
        foreach (var c in url.Trim())
        {
            encoded.Append(c switch
            {
                ' ' => "%20",
                '(' => "%28",
                ')' => "%29",
                _ => c.ToString(),
            });
        }

        return $"![{CleanAlt(altText)}]({encoded})";
    }

    /// <summary>The alt text an image gets from its file name: the name without its extension, with the Markdown characters taken out, and cut to <see cref="MaxAltLength"/>.</summary>
    public static string AltFromFileName(string? fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName?.Trim() ?? string.Empty).Replace('-', ' ').Replace('_', ' ');
        var alt = CleanAlt(name);
        return alt.Length == 0 ? "image" : alt;
    }

    private static string CleanAlt(string text)
    {
        var cleaned = Whitespace().Replace(AltUnsafe().Replace(text, " "), " ").Trim();
        return cleaned.Length > MaxAltLength ? cleaned[..MaxAltLength].TrimEnd() : cleaned;
    }
}
````

`src/TechStrap.Admin/Options/AdminOptionsRegistration.cs`

Replace

```csharp
        services.AddOptions<AuthOptions>().ValidateOnStart();
        services.AddOptions<ApiOptions>().ValidateOnStart();
        services.AddOptions<AgentGroupOptions>()
            .Configure(options =>
```

with

```csharp
        services.AddOptions<AuthOptions>().ValidateOnStart();
        services.AddOptions<ApiOptions>().ValidateOnStart();
        services.AddOptions<PortalUrlOptions>()
            .Configure(options => options.PublicUrl = configuration[PortalUrlOptions.PublicUrlKey]?.Trim())
            .Validate(
                options => PortalUrlOptions.IsValidBase(options.PublicUrl),
                $"{PortalUrlOptions.PublicUrlKey} must be an absolute http or https URL without a query or fragment when set.")
            .ValidateOnStart();
        services.AddOptions<AgentGroupOptions>()
            .Configure(options =>
```

`src/TechStrap.Admin/Options/PortalUrlOptions.cs`

```csharp
namespace TechStrap.Admin.Options;

/// <summary>
/// The customer portal's public address, under the same flat key the API reads (<c>TECHSTRAP_PORTAL_PUBLIC_URL</c>), so one env file can configure both hosts. It is optional here: it only powers the
/// "View on portal" link of a published article, and the link is left out while it is blank. The Admin never sends it anywhere and never calls it.
/// </summary>
public sealed class PortalUrlOptions
{
    public const string PublicUrlKey = "TECHSTRAP_PORTAL_PUBLIC_URL";

    public string? PublicUrl { get; set; }

    /// <summary>
    /// True when the value is absent or an absolute http(s) URL without a query, a fragment or user info (the rule the API applies to its own optional Admin address). A lone "?" or "#" counts as a query or a
    /// fragment: <see cref="Uri.Query"/> and <see cref="Uri.Fragment"/> keep it, and the tests pin that.
    /// </summary>
    public static bool IsValidBase(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0);

    /// <summary>
    /// The public address of a published article, <c>{portal}/p/{product key}/kb/{category slug}/{slug}</c> (the route PHASE-09 serves), or null when the portal address is blank or one of the three
    /// parts is. Each part is escaped, so a slug can never add a path segment or a query.
    /// </summary>
    public string? ArticleUrl(string? productKey, string? categorySlug, string? slug)
    {
        if (string.IsNullOrWhiteSpace(PublicUrl) || string.IsNullOrWhiteSpace(productKey) || string.IsNullOrWhiteSpace(categorySlug) || string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return $"{PublicUrl.Trim().TrimEnd('/')}/p/{Uri.EscapeDataString(productKey)}/kb/{Uri.EscapeDataString(categorySlug)}/{Uri.EscapeDataString(slug)}";
    }
}
```

`src/TechStrap.Admin/Program.cs`

Replace (edit 1 of 2)

```csharp
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Features.Tickets;
```

with

```csharp
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Features.Shell;
using TechStrap.Admin.Features.Tickets;
```

Replace (edit 2 of 2)

```csharp
builder.Services.AddShell();
builder.Services.AddTicketFeatures();

builder.Services.AddRazorComponents()
```

with

```csharp
builder.Services.AddShell();
builder.Services.AddTicketFeatures();
builder.Services.AddKbFeatures();

builder.Services.AddRazorComponents()
```

`src/TechStrap.Admin/Styles/_kb.scss`

Replace

```scss
  max-width: 1100px;
}
```

with

```scss
  max-width: 1100px;
}

// The article editor. Write and Preview sit side by side from 992px; below that one is shown at a time and the Write / Preview buttons choose. Text that comes from outside (a slug, a title, the
// article itself) wraps instead of stretching the page, and a picture can never be wider than its pane.
.ts-kb-editor {
  max-width: 1280px;
}

.ts-kb-form {
  max-width: none;

  .ts-field {
    max-width: 560px;
  }
}

.ts-kb-status {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 16px;
  align-items: center;
  margin: 0 0 12px;
}

.ts-kb-portal-link {
  font: 500 .75rem var(--ts-font-mono);
}

.ts-kb-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 8px;
  align-items: center;
}

.ts-kb-image {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 4px 8px;
  align-items: center;
}

.ts-kb-image-label {
  margin: 0;
  cursor: pointer;
  font: 600 .875rem var(--ts-font-mono);
  text-decoration: underline;
}

.ts-kb-tabs {
  display: flex;
  gap: 4px;
  margin: 8px 0;
}

.ts-kb-panes {
  display: grid;
  grid-template-columns: minmax(0, 1fr);
  gap: 16px;
}

.ts-kb-pane {
  min-width: 0;
}

.ts-kb-md[data-pane="write"] .ts-kb-pane--preview,
.ts-kb-md[data-pane="preview"] .ts-kb-pane--write {
  display: none;
}

@media (min-width: 992px) {
  .ts-kb-tabs {
    display: none;
  }

  .ts-kb-panes {
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
  }

  .ts-kb-md[data-pane] .ts-kb-pane {
    display: block;
  }
}

.ts-kb-textarea {
  min-height: 24rem;
  font-family: var(--ts-font-mono);
}

.ts-kb-preview {
  min-height: 24rem;
  padding: 8px 12px;
  overflow-wrap: anywhere;
  background: var(--sheet);
  border: 2px solid var(--rule-strong);
}

.ts-kb-preview-body {
  img {
    max-width: 100%;
    height: auto;
  }

  pre {
    overflow-x: auto;
  }

  table {
    display: block;
    max-width: 100%;
    overflow-x: auto;
  }
}

.ts-kb-preview-error {
  padding-left: 8px;
  font-size: .8125rem;
  border-left: 3px solid var(--st-spam);
}

.ts-kb-preview-empty {
  color: var(--ink-2);
}
```

`src/TechStrap.Admin/appsettings.json`

Replace

```json
  "TECHSTRAP_ADMIN_GROUP": "techstrap-admins",
  "TECHSTRAP_GROUP_CLAIM_TYPE": "groups",
  "DataProtection": {
    "KeyRingPath": ""
```

with

```json
  "TECHSTRAP_ADMIN_GROUP": "techstrap-admins",
  "TECHSTRAP_GROUP_CLAIM_TYPE": "groups",
  "TECHSTRAP_PORTAL_PUBLIC_URL": "",
  "DataProtection": {
    "KeyRingPath": ""
```

- [ ] **Step 4: Run the tests, then prove each pin bites**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
dotnet test --project tests/TechStrap.Api.Tests -c Release --filter-class "*KbAdminLeakTests" --filter-class "*AdminLeakTests"
```

Expected: all PASS (`TechStrap.Admin.Tests` 1834 tests, `TechStrap.Architecture.Tests` 253, the leak tests 8).

Mutations, each applied to the finished code, the tests run, and the change reverted:

| Mutation | Failing tests |
| :-- | :-- |
| `MarkdownEditor.razor.cs`: a failed preview shows the API message | `MarkdownEditorTests.A_failed_preview_keeps_the_text_and_the_last_good_preview_and_says_so_in_fixed_words` |
| `MarkdownEditor.razor.cs`: preview with no debounce | `MarkdownEditorTests.A_burst_of_typing_is_one_preview_call_300_ms_after_the_last_keystroke`, `MarkdownEditorTests.A_pending_timer_is_released_with_the_component_so_no_call_is_made_after_it_is_gone` |
| `MarkdownEditor.razor.cs`: no _previewId stale guard | `MarkdownEditorTests.A_call_that_a_newer_change_replaced_is_cancelled_and_its_late_answer_is_never_drawn`, `MarkdownEditorTests.An_answer_that_arrives_after_a_newer_call_started_is_ignored_even_when_the_call_was_not_cancelled` |
| `MarkdownEditor.razor.cs`: a replaced call is not canceled on the next change | `MarkdownEditorTests.A_call_that_a_newer_change_replaced_is_cancelled_and_its_late_answer_is_never_drawn` |
| `MarkdownEditor.razor.cs`: Dispose leaves the timer running | `MarkdownEditorTests.A_pending_timer_is_released_with_the_component_so_no_call_is_made_after_it_is_gone` |
| `MarkdownEditor.razor.cs`: Dispose does not cancel the call in flight | `MarkdownEditorTests.A_call_in_flight_is_cancelled_with_the_component` |
| `MarkdownEditor.razor.cs`: no limit guard before the preview call | `MarkdownEditorTests.A_text_the_api_could_not_save_either_is_not_sent_for_preview_and_keeps_the_last_good_preview` |
| `KbArticleEditorViewModel.cs`: ViewModel: the update does not send the loaded version | `KbArticleEditorPageTests.Reloading_after_a_409_brings_in_the_latest_version_and_the_next_save_uses_its_version`, `KbArticleEditorPageTests.Saving_sends_the_loaded_version_and_the_edited_fields_and_the_answer_replaces_the_form`, `KbArticleEditorViewModelTests.Update_carries_the_loaded_version_and_no_product_or_slug` |
| `KbArticleEditorPage.razor.cs`: Editor: a 409 on save empties the form | `KbArticleEditorPageTests.A_409_keeps_the_draft_shows_the_banner_and_turns_saving_off_until_the_agent_reloads` |
| `KbArticleEditorPage.razor.cs`: Editor: saving does not stay off after a 409 | `KbArticleEditorPageTests.A_409_keeps_the_draft_shows_the_banner_and_turns_saving_off_until_the_agent_reloads`, `KbArticleEditorPageTests.A_publish_on_a_stale_version_is_a_409_with_the_banner_and_nothing_is_published` |
| `KbArticleEditorPage.razor.cs`: Editor: an archived article saved says plain "Saved" | `KbArticleEditorPageTests.Saving_an_archived_article_returns_it_to_draft_and_says_so` |
| `KbArticleEditorPage.razor.cs`: Editor: an uncertain write is not held | `KbArticleEditorPageTests.A_create_with_an_unknown_outcome_is_held_nothing_is_sent_twice_and_the_list_is_offered`, `KbArticleEditorPageTests.A_publish_with_an_unknown_outcome_is_held_until_a_reload`, `KbArticleEditorPageTests.A_save_with_an_unknown_outcome_is_held_until_a_reload_and_nothing_is_sent_twice` (and 1 more; 4 in all) |
| `KbArticleEditorPage.razor.cs`: Editor: an in-app move is not prevented | `KbArticleEditorPageTests.A_changed_form_asks_before_an_in_app_move_and_before_the_browser_leaves`, `KbArticleEditorPageTests.A_new_article_with_text_asks_before_it_is_left_and_an_empty_one_does_not`, `KbArticleEditorPageTests.Staying_keeps_the_page_and_the_text_and_leaving_goes_where_the_agent_was_going` |
| `KbArticleEditorPage.razor`: Editor: the browser is not asked before the tab closes | `KbArticleEditorPageTests.A_changed_form_asks_before_an_in_app_move_and_before_the_browser_leaves` |
| `KbArticleEditorPage.razor.cs`: Editor: publish sends no version | `KbArticleEditorPageTests.Publishing_sends_the_loaded_version_shows_the_answer_and_a_portal_link_and_reads_nothing_again` |
| `KbArticleEditorPage.razor.cs`: Editor: archive sends no version | `KbArticleEditorPageTests.Confirming_archives_with_the_loaded_version_and_shows_the_answer_archived_without_a_portal_link` |
| `KbArticleEditorPage.razor.cs`: Editor: publish allowed while the form has unsaved changes | `KbArticleEditorPageTests.Publish_is_offered_only_once_the_article_is_saved_and_complete_and_the_hint_says_what_is_missing` |
| `KbArticleEditorPage.razor.cs`: Editor: the portal link uses the unsaved category | `KbArticleEditorPageTests.The_portal_link_uses_the_saved_category_and_not_one_picked_but_not_saved` |
| `KbArticleEditorPage.razor.cs`: Editor: a draft gets a portal link | `KbArticleEditorPageTests.A_draft_has_no_portal_link_and_neither_does_anything_when_no_portal_address_is_set`, `KbArticleEditorPageTests.Confirming_archives_with_the_loaded_version_and_shows_the_answer_archived_without_a_portal_link`, `KbArticleEditorPageTests.Publishing_sends_the_loaded_version_shows_the_answer_and_a_portal_link_and_reads_nothing_again` (and 1 more; 4 in all) |
| `KbArticleEditorPage.razor.cs`: Editor: moving to another article leaves the Saving state on | `KbArticleEditorPageTests.A_save_that_finishes_after_the_agent_moved_to_another_article_says_so_once_and_leaves_the_new_screen_alone` |
| `KbArticleEditorPage.razor.cs`: Editor: a save that finishes after the page is gone still speaks | `KbArticleEditorPageTests.A_save_that_finishes_after_the_page_is_gone_changes_nothing_and_says_nothing` |
| `KbArticleEditorPage.razor.cs`: Editor: no _loadId stale guard | `KbArticleEditorPageTests.A_load_that_was_overtaken_by_another_article_never_replaces_the_newer_screen` |
| `MarkdownEditor.razor.cs`: MarkupString used in a third file | `MarkupStringSiteTests.MarkupString_is_used_in_exactly_two_files_the_message_bubble_and_the_kb_preview_pane` |
| `KbImageUploadButton.razor.cs`: no size check | `KbImageUploadButtonTests.A_picture_over_the_limit_is_refused_before_anything_is_sent_and_the_message_names_the_limit` |
| `KbImageUploadButton.razor.cs`: no type check | `KbImageUploadButtonTests.A_file_that_is_not_a_png_jpeg_gif_or_webp_is_refused_before_anything_is_sent` |
| `KbImageUploadButton.razor.cs`: reports a picture after the screen is gone | `KbImageUploadButtonTests.A_picture_that_finishes_after_the_screen_is_gone_changes_nothing` |
| `PortalUrlOptions.cs`: parts of the address are not escaped | `PortalUrlOptionsTests.Each_part_is_escaped_so_a_slug_can_never_add_a_segment_or_a_query` |
| `PortalUrlOptions.cs`: an address with a query is accepted | `PortalUrlOptionsTests.A_portal_address_that_is_not_a_plain_absolute_address_stops_the_start_and_names_the_variable`, `PortalUrlOptionsTests.The_portal_address_is_blank_or_a_plain_absolute_http_or_https_address` |
| `AdminOptionsRegistration.cs`: Admin options: the portal address is not read from configuration | `KbEditorHostTests.A_published_article_shows_its_status_and_a_portal_link_built_from_the_admins_own_setting` |
| `KbArticleEditorPresenter.cs`: KbEditorLookups: every category for every product | `KbArticleEditorPageTests.A_new_article_asks_for_a_product_a_category_a_title_a_slug_a_summary_and_the_text_and_reads_no_article`, `KbArticleEditorPageTests.Choosing_a_product_offers_its_categories_and_the_shared_ones_and_changing_it_drops_a_category_it_may_not_use`, `KbEditorLookupsTests.A_product_article_may_use_a_shared_category_or_its_own_and_a_shared_article_only_a_shared_one` |
| `MarkdownSnippets.cs`: the alt text is not cleaned | `MarkdownSnippetsTests.A_very_long_file_name_is_cut_for_the_alt_text`, `MarkdownSnippetsTests.The_alt_text_comes_from_the_file_name_without_markdown_characters` |
| `MarkdownSnippets.cs`: a block is glued to the text | `MarkdownEditorTests.A_picture_that_was_uploaded_is_added_as_markdown_with_its_alt_text_from_the_file_name`, `MarkdownEditorTests.Each_toolbar_button_adds_its_markdown_at_the_end_of_the_text_and_the_preview_follows`, `MarkdownSnippetsTests.A_block_snippet_starts_after_a_blank_line` |
| `KbPreviewPane.razor`: no ErrorText message shown | `KbPreviewPaneTests.A_failure_keeps_the_last_good_preview_under_a_fixed_message`, `KbPreviewPaneTests.A_failure_with_nothing_to_show_has_the_message_and_no_empty_line` |
| `KbPreviewPane.razor`: draws nothing of the HTML it was given | `KbPreviewPaneTests.A_failure_keeps_the_last_good_preview_under_a_fixed_message`, `KbPreviewPaneTests.It_draws_the_html_it_was_given_exactly_as_the_api_returned_it` |
| `KbImageUploadButton.razor.cs`: the 413 of Kestrel is not told from a lost upload | `KbImageUploadButtonTests.A_picture_the_api_refuses_or_may_not_have_stored_says_so_and_adds_nothing` |
| `PortalUrlOptions.cs`: an address with a fragment is accepted | `PortalUrlOptionsTests.The_portal_address_is_blank_or_a_plain_absolute_http_or_https_address` |
| `KbArticleListPage.razor.cs`: the list is asked before the lookups, so the search text goes out before a 401 is known | `TechStrap.Api.Tests.KbAdminLeakTests.A_search_term_appears_in_no_log_event_after_a_mid_session_401_and_the_list_is_never_asked_after_it` |

- [ ] **Step 5: Build**

```bash
dotnet build TechStrap.slnx -c Release
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 6: The config contract**

```bash
pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/ConfigContract.Tests.ps1
```

Expected: PASS (Tests Passed: 84, Failed: 0, Skipped: 0). The setting is in `appsettings.json` (blank), `src/TechStrap.Admin/.env.example` and `deploy/.env.admin.example`, and on the Admin's blank list. Mutations, each applied and reverted:

| Mutation | Failing checks |
| :-- | :-- |
| delete the `TECHSTRAP_PORTAL_PUBLIC_URL=` line from `deploy/.env.admin.example` | `the deploy env template lists the same keys minus the ones compose owns and the Development-only ones, both ways` |
| delete the `TECHSTRAP_PORTAL_PUBLIC_URL` key from `src/TechStrap.Admin/appsettings.json` | `appsettings.json plus appsettings.Development.json and .env.example list the same keys, both ways`, `the deploy env template lists the same keys minus the ones compose owns and the Development-only ones, both ways`, `appsettings.json is blank only where blank is valid, and nowhere else` |
| take the key off the Admin's blank list in `ConfigContract.Tests.ps1` | `appsettings.json is blank only where blank is valid, and nowhere else`, `a blank value in .env.example or the deploy template is on the blank list, and an array element is never left blank` |

- [ ] **Step 7: Commit**

```bash
git add deploy/.env.admin.example \
  scripts/tests/ConfigContract.Tests.ps1 \
  src/TechStrap.Admin/.env.example \
  src/TechStrap.Admin/Features/Kb/KbArticleEditorPage.razor \
  src/TechStrap.Admin/Features/Kb/KbArticleEditorPage.razor.cs \
  src/TechStrap.Admin/Features/Kb/KbArticleEditorPresenter.cs \
  src/TechStrap.Admin/Features/Kb/KbArticleEditorViewModel.cs \
  src/TechStrap.Admin/Features/Kb/KbEditorCopy.cs \
  src/TechStrap.Admin/Features/Kb/KbImageUploadButton.razor \
  src/TechStrap.Admin/Features/Kb/KbImageUploadButton.razor.cs \
  src/TechStrap.Admin/Features/Kb/KbPreviewPane.razor \
  src/TechStrap.Admin/Features/Kb/KbServiceCollectionExtensions.cs \
  src/TechStrap.Admin/Features/Kb/MarkdownEditor.razor \
  src/TechStrap.Admin/Features/Kb/MarkdownEditor.razor.cs \
  src/TechStrap.Admin/Features/Kb/MarkdownSnippets.cs \
  src/TechStrap.Admin/Options/AdminOptionsRegistration.cs \
  src/TechStrap.Admin/Options/PortalUrlOptions.cs \
  src/TechStrap.Admin/Program.cs \
  src/TechStrap.Admin/Styles/_kb.scss \
  src/TechStrap.Admin/appsettings.json \
  tests/TechStrap.Admin.Tests/Components/KbArticleEditorPageTests.cs \
  tests/TechStrap.Admin.Tests/Components/KbArticleEditorViewModelTests.cs \
  tests/TechStrap.Admin.Tests/Components/KbImageUploadButtonTests.cs \
  tests/TechStrap.Admin.Tests/Components/KbPreviewPaneTests.cs \
  tests/TechStrap.Admin.Tests/Components/MarkdownEditorTests.cs \
  tests/TechStrap.Admin.Tests/Components/MarkdownSnippetsTests.cs \
  tests/TechStrap.Admin.Tests/KbEditorHostTests.cs \
  tests/TechStrap.Admin.Tests/KbStyleTests.cs \
  tests/TechStrap.Admin.Tests/MarkupStringSiteTests.cs \
  tests/TechStrap.Admin.Tests/Options/PortalUrlOptionsTests.cs \
  tests/TechStrap.Api.Tests/KbAdminLeakTests.cs
git diff --cached --stat
git commit -m "feat(admin): knowledge base editor with live preview and picture upload" -m "Markdown editor with a debounced server preview through one sanitized pane (the second MarkupString site), picture upload, publish and archive with the loaded version, a leave guard, the conflict banner and View on portal from the new optional TECHSTRAP_PORTAL_PUBLIC_URL." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```

### Task 11: The categories page, the reply composer's article picker, the CSP check for pictures and the closing docs (P08-T19, P08-T20)

**Review Focus pins:**
- **(5) A reply that links an unpublished or other-product article, the Admin half.** The picker asks for `Published` articles of the ticket's product plus the shared ones and drops any row that is not `Published`, whatever the answer holds; a refusal by the API (`kb-article-not-linkable`, or `article-not-found`) keeps the text and the chips and says what to do: `ArticlePickerTests.A_row_that_is_not_published_is_never_offered_even_if_the_answer_holds_one`, `Typing_searches_once_after_the_debounce_for_published_articles_of_the_ticket_product_and_the_shared_ones`, `ReplyComposerArticleTests.An_article_the_api_will_not_link_keeps_the_text_and_the_choice_and_says_what_to_do`, `TicketPageArticleLinkingTests`.
- **(4) Lost edits on a category conflict, and the roles.** A category save carries the version the list was read with and a 409 keeps the edit open: `KbCategoriesPageTests.A_409_says_the_category_changed_keeps_the_edit_open_and_offers_a_reload`. Category delete is Admin only: the button is drawn only for an Admin (`A_plain_agent_opens_the_page_lists_creates_and_edits_but_is_never_offered_delete`, `KbCategoriesHostTests.A_plain_agent_sees_the_categories_and_the_create_form_but_no_delete_button`), and a category with articles cannot be deleted (`A_category_with_articles_is_blocked_with_a_message_and_the_dialog_cannot_be_confirmed_again`).
- **(2) Pictures in the browser.** The Admin's CSP already lets an API picture load in Development (loopback on any port) and in Production (https): `KbImageCspTests`. This task adds no CSP code, and the test says why: the loopback entries are `http://localhost:*` and `http://127.0.0.1:*`, and the API's own address is on one of them.

**Files:**
- Modify: `docs/architecture/04-DECISION-LOG.md`
- Modify: `docs/architecture/99-IMPLEMENTATION-ROADMAP.md`
- Modify: `docs/architecture/PHASE-08-knowledge-base.md`
- Modify: `docs/development/ADMIN-APP.md`
- Modify: `scripts/tests/RepositoryDocs.Tests.ps1`
- Create: `src/TechStrap.Admin/Features/Kb/ArticleChoice.cs`
- Create: `src/TechStrap.Admin/Features/Kb/ArticlePicker.razor`
- Create: `src/TechStrap.Admin/Features/Kb/ArticlePicker.razor.cs`
- Create: `src/TechStrap.Admin/Features/Kb/ArticlePickerCopy.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbCategoriesCopy.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbCategoriesPage.razor`
- Create: `src/TechStrap.Admin/Features/Kb/KbCategoriesPage.razor.cs`
- Create: `src/TechStrap.Admin/Features/Kb/KbCategoryRowViewModel.cs`
- Modify: `src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor`
- Modify: `src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor.cs`
- Modify: `src/TechStrap.Admin/Features/Tickets/ReplyComposerModel.cs`
- Modify: `src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor`
- Modify: `src/TechStrap.Admin/Styles/_kb.scss`
- Create: `tests/TechStrap.Admin.Tests/Components/ArticlePickerTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/KbCategoriesPageTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/ReplyComposerArticleTests.cs`
- Create: `tests/TechStrap.Admin.Tests/Components/TicketPageArticleLinkingTests.cs`
- Create: `tests/TechStrap.Admin.Tests/KbCategoriesHostTests.cs`
- Create: `tests/TechStrap.Admin.Tests/KbImageCspTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/KbStyleTests.cs`
- Modify: `tests/TechStrap.Admin.Tests/ScrollRegionSiteTests.cs`

**Interfaces:**
- Consumes:
  - **Task 9:** `IKbClient`, `KbListFilter`'s siblings (`KbDefaults`, `KbCopy`), `TestData.Kb*`, `CountingTimeProvider`, `KbListHostTests` and `KbEditorHostTests` as the host-test pattern.
  - **Task 10:** `KbEditorLimits.SlugMaxLength`, `ApiErrorCodes`/`ApiFields`.
  - **Contracts from Task 1:** `KbCategoryDto`, `CreateKbCategoryRequest`, `UpdateKbCategoryRequest`, `ListKbArticlesRequest`, `KbLimits.ReservedCategorySlug`; the existing `TicketOperationLimits.MaxLinkedArticles` (10) and `AddAgentReplyRequest.LinkedArticleIds`.
  - **Tasks 3, 4 and 8 behaviour:** `DELETE api/kb/categories/{id}` is Admin only and answers 409 `kb-category-in-use` while articles are in it; `PUT api/kb/categories/{id}` answers 409 `concurrency-conflict` for a stale version; a reply that links a draft, an archived or an other-product article is 400 `kb-article-not-linkable` (unknown id 404 `article-not-found`), at most 10 links; the customer email carries a link to each.
  - **The Admin as it is:** `AgentSession.IsAdmin`, `UncertainMarks`, `ConfirmDialog`, `AdminPageTest` (a session, an Admin unless `AsAgent()`), `ReplyComposer`, `ComposerDraft`, `DraftStore`, `TicketDetailPage`, `TechStrapCsp.ForBlazorApp`.
- Produces:
  - `KbCategoriesPage` (`/kb/categories`), `KbCategoryRowViewModel`, `KbCategoryForm`, `KbCategoriesCopy`.
  - `ArticlePicker`, `ArticleChoice(Id, Title, IsShared)`, `ArticlePickerCopy`; `ComposerDraft.LinkedArticles`; `ReplyComposer.ProductId`; the chips and the "Link a knowledge base article" button in the public reply; `ReplyComposerCopy.ArticleNotLinkable`.
  - The closing docs: `ADMIN-APP.md` ("Knowledge base (08)", "Known gaps in 08", the configuration and roles tables), the PHASE-08 ticks T13 to T20 and the Admin deliverable, the roadmap row, and a line in D-044.

**Rules:**
1. **Categories page.** `/kb/categories` is for every agent. It lists every category in sort order (then name) with its product or "Shared", slug, order and description. Create takes a product (or shared), a name, a slug that follows the name until edited, an optional description and a sort order that starts at the next free step (highest + 10) while the form is untouched. The slug `search` is refused before sending (the portal keeps it); `kb-category-slug-taken` and `kb-category-reserved-slug` are the slug's field error; a 400 names its field (`name`, `description`, `slug` on create); anything else is said above the form. A category's product and slug are permanent: the row edit has name, description and sort order only, and the update carries the version the list was read with (so re-ordering is saved through the update, and the answer's version is the next save's).
2. **Writes and loads.** `CancellationToken.None`, `_disposed` guards, `_loadId` stale guard on the list read (products and categories together). A 409 (`WriteOutcome.Conflict`) or an unknown outcome says so above the list and offers "Reload list"; a category deleted meanwhile says so and reads the list again.
3. **Delete.** The button is drawn only when `AgentSession.IsAdmin`. The `ConfirmDialog` names the category; confirming sends one `DELETE` with `CancellationToken.None`. 409 `kb-category-in-use` keeps the dialog open with the API's message and disables Confirm (the API would answer the same again); a 403 says only an admin can; `kb-category-not-found` closes and reads the list again; an unknown outcome is held in `UncertainMarks` (asking again shows the held state and sends nothing) until a later read of the list.
4. **The picker** is behind "Link a knowledge base article" in a **public** reply only (a note shows none of it), so a composer that never links an article never mounts it and never needs the KB client. It searches nothing until the agent types, waits 300 ms, and asks `ListAsync(productId = the ticket's product, includeShared = true, status = Published, text, page 1, 10)`; a row that is not `Published` is dropped; a shared result carries a "Shared" mark; Add reports the choice (`OnAdd`), the selection itself is the draft's. An answer that was overtaken is ignored (`_loadId`); a failed search says so in fixed words; a ticket that moves to another product stops offering the old product's results; the timer is released with the component.
5. **The chips and the send.** `ComposerDraft.LinkedArticles` (at most 10, never the same article twice, in the order chosen) survives a mode switch, a failed send, a conflict and a closed screen, because the draft does. A public reply sends exactly those ids (`AddAgentReplyRequest.LinkedArticleIds`, taken when the send starts), a note sends none, and an accepted send removes only the ones it sent (an article chosen while it was on its way stays). While a send is in flight the chips' Remove buttons and the toggle are off. `kb-article-not-linkable` and `article-not-found` keep the text and the chips and say: "One of the linked articles can't be linked: it may have been unpublished, moved to another product or removed. Remove the articles you no longer want, then send again. Your text is kept."
6. **The ticket page** passes `_model.ProductId` to the composer (`ReplyComposer.ProductId`). The timeline is unchanged: it already lists the titles of the linked articles as plain text (the linked-article DTO has no portal address); links in the timeline are a known gap.
7. **CSP.** No code change: `TechStrapCsp.ForBlazorApp(allowLoopbackImages: true)` allows `http://localhost:*` and `http://127.0.0.1:*` for images (Development only), and `https:` always. `KbImageCspTests` asks the policy the question a browser asks for each shape of the API's public address, and fails if the wildcard port goes.
8. **Tables.** The categories table is the ninth ledger table (`ScrollRegionSiteTests` expects 9).
9. **Closing docs.** The docs test comes first (it is in Step 1 and fails until the script has run); the script below edits `ADMIN-APP.md`, the PHASE-08 document (T13 to T20 and the Admin deliverable ticked, an "As built (Admin)" note), the roadmap row and D-044 (one bullet under Consequences), and stops with an `AssertionError` naming the file if any text it replaces is not there exactly once. The success criteria stay unticked: they need the owner's manual checks.

**Files shared with Tasks 1 to 8** (this plan edits them in place; apply the intent to the file as the earlier tasks left it): `scripts/tests/ConfigContract.Tests.ps1` (Task 10 adds `TECHSTRAP_PORTAL_PUBLIC_URL` to the Admin's blank list; Task 5 adds `TECHSTRAP_API_PUBLIC_URL` to the Api's row), `docs/architecture/PHASE-08-knowledge-base.md` (Task 7 edits the route tables, Task 8 ticks T01 to T12), `docs/architecture/99-IMPLEMENTATION-ROADMAP.md` (Task 8 sets the row to "In progress", this task completes it), `docs/architecture/04-DECISION-LOG.md` (Task 1 writes D-044, this task appends one bullet), `scripts/tests/RepositoryDocs.Tests.ps1` (Task 8's `ticks the API tasks P08-T01 to P08-T12 and the roadmap row says the phase is in progress` is rewritten here to cover T01 to T20, the Admin deliverable, the "complete, pending merge" row and the new ADMIN-APP section: the old test would fail the moment the row is completed). Nothing else is shared: the Admin and its tests are this plan's alone.

- [ ] **Step 1: Write the failing tests**

The category tests (the page, the form, the host), the picker tests, the composer tests, the ticket-page test and the CSP test.


`scripts/tests/RepositoryDocs.Tests.ps1`

Replace

```powershell
    }

    It 'ticks the API tasks P08-T01 to P08-T12 and the roadmap row says the phase is in progress' {
        $phase = Get-RepoText 'docs/architecture/PHASE-08-knowledge-base.md'
        foreach ($number in 1..12) {
            $id = 'P08-T{0:00}' -f $number
            $phase | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is done"
        }
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 08 \|.*D-044.*\| In progress'
    }
```

with

```powershell
    }

    It 'ticks every PHASE-08 task P08-T01 to P08-T20 and the Admin deliverable, and the roadmap row says the phase is complete, pending merge' {
        $phase = Get-RepoText 'docs/architecture/PHASE-08-knowledge-base.md'
        foreach ($number in 1..20) {
            $id = 'P08-T{0:00}' -f $number
            $phase | Should -Match ('(?m)^- \[x\] \*\*' + $id + '\*\*') -Because "$id is done"
        }
        $phase | Should -Match '(?m)^- \[x\] Admin KB list, editor with live preview and image upload, categories page, article picker in the reply composer\.'
        (Get-RepoText 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md') | Should -Match '(?m)^\| 08 \|.*D-044.*\| PHASE-08 complete \(pending merge\)'
        (Get-RepoText 'docs/development/ADMIN-APP.md') | Should -Match '(?m)^## Knowledge base \(08\)'
    }
```

`tests/TechStrap.Admin.Tests/Components/ArticlePickerTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The article picker of the reply composer (PHASE-08 T20): it searches published articles of the ticket's product and the shared ones, only after the agent types and after the debounce, never offers a draft or an
/// archived article, and reports a choice without keeping the selection itself. Review Focus 5 (the Admin half): a reply never offers what the API would refuse to link.
/// </summary>
public sealed class ArticlePickerTests : AdminComponentTest
{
    private static readonly Guid Other = Guid.Parse("dddddddd-0000-0000-0000-0000000000a2");

    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly List<ArticleChoice> _added = [];
    private readonly CountingTimeProvider _timers;

    public ArticlePickerTests()
    {
        _timers = new CountingTimeProvider(Time);
        Services.AddSingleton<TimeProvider>(_timers);
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.KbPage(
        [
            TestData.KbItem("Reset your password", "reset-password", KbArticleStatuses.Published, TestData.OrbitlyId, id: TestData.ArticleId),
            TestData.KbItem("Welcome", "welcome", KbArticleStatuses.Published, productId: null, id: Other),
        ])));
        Services.AddSingleton(_kb);
    }

    private IRenderedComponent<ArticlePicker> RenderPicker(IReadOnlyList<ArticleChoice>? selected = null, bool disabled = false, Guid? productId = null) =>
        Render<ArticlePicker>(p => p
            .Add(c => c.ProductId, productId ?? TestData.OrbitlyId)
            .Add(c => c.Selected, selected ?? [])
            .Add(c => c.Disabled, disabled)
            .Add(c => c.OnAdd, choice => _added.Add(choice)));

    private IEnumerable<ListKbArticlesRequest> Requests() =>
        _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.ListAsync)).Select(c => (ListKbArticlesRequest)c.GetArguments()[0]!);

    private void Search(IRenderedComponent<ArticlePicker> cut, string text)
    {
        cut.Find("input[type=search]").Input(text);
        Time.Advance(KbDefaults.PickerDebounce);
    }

    [Fact]
    public void Nothing_is_called_until_the_agent_types()
    {
        var cut = RenderPicker();
        Time.Advance(KbDefaults.PickerDebounce * 3);

        Requests().ShouldBeEmpty();
        cut.FindAll("ul").ShouldBeEmpty();
        cut.Find("label").TextContent.ShouldBe("Search published articles");
    }

    [Fact]
    public void Typing_searches_once_after_the_debounce_for_published_articles_of_the_ticket_product_and_the_shared_ones()
    {
        var cut = RenderPicker();

        cut.Find("input[type=search]").Input("r");
        cut.Find("input[type=search]").Input("re");
        cut.Find("input[type=search]").Input("reset");
        Time.Advance(KbDefaults.PickerDebounce - TimeSpan.FromMilliseconds(1));
        Requests().ShouldBeEmpty();
        Time.Advance(TimeSpan.FromMilliseconds(1));

        cut.WaitForAssertion(() => Requests().ShouldBe([new ListKbArticlesRequest(TestData.OrbitlyId, SharedOnly: false, IncludeShared: true, KbArticleStatuses.Published, null, "reset", 1, 10)]));
        KbDefaults.PickerDebounce.ShouldBe(TimeSpan.FromMilliseconds(300));
        KbDefaults.PickerPageSize.ShouldBe(10);
    }

    [Fact]
    public void Results_show_their_title_and_a_shared_mark_and_an_add_button_that_names_the_article()
    {
        var cut = RenderPicker();

        Search(cut, "reset");

        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));
        var items = cut.FindAll("ul.ts-kb-picker-results li");
        items[0].QuerySelector(".ts-kb-picker-title")!.TextContent.ShouldBe("Reset your password");
        items[0].QuerySelectorAll(".ts-pill").ShouldBeEmpty();
        items[1].QuerySelector(".ts-pill")!.TextContent.ShouldBe("Shared");
        items[0].QuerySelector("button")!.GetAttribute("aria-label").ShouldBe("Add Reset your password");
        cut.Find("[role=status].visually-hidden").TextContent.ShouldBe("2 articles");
    }

    // Review Focus 5: a draft or an archived article is never offered, whatever the answer holds.
    [Fact]
    public void A_row_that_is_not_published_is_never_offered_even_if_the_answer_holds_one()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage(
        [
            TestData.KbItem("Live", "live", KbArticleStatuses.Published, id: Guid.NewGuid()),
            TestData.KbItem("A draft", "draft", KbArticleStatuses.Draft, id: Guid.NewGuid()),
            TestData.KbItem("Old", "old", KbArticleStatuses.Archived, id: Guid.NewGuid()),
        ])));
        var cut = RenderPicker();

        Search(cut, "x");

        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Select(l => l.QuerySelector(".ts-kb-picker-title")!.TextContent).ShouldBe(["Live"]));
        cut.Markup.ShouldNotContain("A draft");
        Requests().Single().Status.ShouldBe("Published");
    }

    [Fact]
    public void Adding_reports_the_choice_and_keeps_no_selection_of_its_own()
    {
        var cut = RenderPicker();
        Search(cut, "reset");
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));

        cut.FindAll("ul.ts-kb-picker-results li")[1].QuerySelector("button")!.Click();

        _added.ShouldBe([new ArticleChoice(Other, "Welcome", IsShared: true)]);
        cut.FindAll("ul.ts-kb-picker-results li")[1].QuerySelector("button")!.TextContent.ShouldBe("Add");
    }

    [Fact]
    public void An_article_that_is_already_selected_says_added_and_cannot_be_added_twice()
    {
        var cut = RenderPicker(selected: [new ArticleChoice(TestData.ArticleId, "Reset your password", false)]);
        Search(cut, "reset");
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));

        var first = cut.FindAll("ul.ts-kb-picker-results li")[0].QuerySelector("button")!;

        first.TextContent.ShouldBe("Added");
        first.HasAttribute("disabled").ShouldBeTrue();
        cut.FindAll("ul.ts-kb-picker-results li")[1].QuerySelector("button")!.HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void At_the_limit_every_add_is_off_and_the_picker_says_so()
    {
        var full = Enumerable.Range(0, TicketOperationLimits.MaxLinkedArticles).Select(i => new ArticleChoice(Guid.NewGuid(), $"Article {i}", false)).ToList();
        var cut = RenderPicker(selected: full);
        Search(cut, "reset");
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));

        cut.FindAll("ul.ts-kb-picker-results button").ShouldAllBe(b => b.HasAttribute("disabled"));
        cut.Find(".ts-kb-picker-state[role=status]").TextContent.ShouldBe("A reply can link up to 10 articles.");
        cut.FindAll("ul.ts-kb-picker-results li")[0].QuerySelector("button")!.Click();
        _added.ShouldBeEmpty();
    }

    [Fact]
    public void A_disabled_picker_cannot_search_or_add()
    {
        var cut = RenderPicker(disabled: true);

        cut.Find("input[type=search]").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void Blank_text_clears_the_results_and_calls_nothing()
    {
        var cut = RenderPicker();
        Search(cut, "reset");
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));

        Search(cut, "   ");

        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results").ShouldBeEmpty());
        Requests().Count().ShouldBe(1);
        cut.FindAll(".ts-kb-picker-state").ShouldBeEmpty();
    }

    [Fact]
    public void An_answer_with_no_published_match_says_so()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([])));
        var cut = RenderPicker();

        Search(cut, "nothing");

        cut.WaitForAssertion(() => cut.Find(".ts-kb-picker-state").TextContent.ShouldBe("No published article matches."));
    }

    [Fact]
    public void A_failed_search_says_so_in_fixed_words_and_never_shows_the_api_message()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<PagedResponse<KbArticleListItemDto>>("api-error", "<img src=x onerror=alert(1)>"));
        var cut = RenderPicker();

        Search(cut, "reset");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldBe("Couldn't search the articles. Try again in a moment."));
        cut.Markup.ShouldNotContain("onerror");
    }

    [Fact]
    public void A_search_that_throws_is_a_failed_search_and_never_reaches_the_renderer()
    {
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns<Task<Result<PagedResponse<KbArticleListItemDto>>>>(_ => throw new InvalidOperationException("secret host:8080"));
        var cut = RenderPicker();

        Search(cut, "reset");

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("Couldn't search"));
        cut.Markup.ShouldNotContain("secret");
    }

    [Fact]
    public async Task An_answer_that_was_overtaken_by_a_newer_search_never_replaces_the_newer_list()
    {
        var slow = new TaskCompletionSource<Result<PagedResponse<KbArticleListItemDto>>>();
        _kb.ListAsync(Arg.Is<ListKbArticlesRequest>(r => r.Text == "old"), Arg.Any<CancellationToken>()).Returns(_ => slow.Task);
        _kb.ListAsync(Arg.Is<ListKbArticlesRequest>(r => r.Text == "new"), Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.KbPage([TestData.KbItem("Newer", "newer", KbArticleStatuses.Published, id: Guid.NewGuid())])));
        var cut = RenderPicker();
        Search(cut, "old");
        cut.WaitForAssertion(() => Requests().Count().ShouldBe(1));

        Search(cut, "new");
        cut.WaitForAssertion(() => cut.Find(".ts-kb-picker-title").TextContent.ShouldBe("Newer"));
        slow.SetResult(TestData.Ok(TestData.KbPage([TestData.KbItem("Older", "older", KbArticleStatuses.Published, id: Guid.NewGuid())])));

        // The late answer is handled on the renderer's thread: let it run, then draw again, so a guard that is missing would show.
        await cut.InvokeAsync(() => { });
        cut.Render(p => p.Add(c => c.Disabled, false));
        cut.Find(".ts-kb-picker-title").TextContent.ShouldBe("Newer");
        cut.Markup.ShouldNotContain("Older");
    }

    [Fact]
    public void A_pending_timer_is_released_with_the_component_so_no_call_is_made_after_it_is_gone()
    {
        var cut = RenderPicker();
        cut.Find("input[type=search]").Input("reset");
        _timers.LiveTimers.ShouldBe(1);

        cut.Instance.Dispose();

        // Released at once, not when it would have fired.
        _timers.LiveTimers.ShouldBe(0);
        Time.Advance(KbDefaults.PickerDebounce * 3);
        Requests().ShouldBeEmpty();
    }

    [Fact]
    public void A_ticket_that_moves_to_another_product_stops_offering_the_old_products_articles()
    {
        var cut = RenderPicker();
        Search(cut, "reset");
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-picker-results li").Count.ShouldBe(2));

        cut.Render(p => p.Add(c => c.ProductId, Guid.NewGuid()).Add(c => c.Selected, []).Add(c => c.OnAdd, choice => _added.Add(choice)));

        cut.FindAll("ul.ts-kb-picker-results").ShouldBeEmpty();
        Search(cut, "reset");
        cut.WaitForAssertion(() => Requests().Last().ProductId.ShouldNotBe(TestData.OrbitlyId));
    }
}
```

`tests/TechStrap.Admin.Tests/Components/KbCategoriesPageTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// The categories page (PHASE-08 T19): any agent lists, creates and edits (the sort order is saved through the update, with the version the list was read with); only an admin sees Delete, and a delete is blocked
/// while articles are in the category. A write whose outcome is unknown is held until the list is read again.
/// </summary>
public sealed class KbCategoriesPageTests : AdminPageTest
{
    private static readonly Guid PaperplaneId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid ShippingId = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private readonly IKbClient _kb = Substitute.For<IKbClient>();
    private readonly IProductsClient _products = Substitute.For<IProductsClient>();

    public KbCategoriesPageTests()
    {
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok<IReadOnlyList<KbCategoryDto>>(
        [
            TestData.KbCategory("Account", "account", TestData.OrbitlyId, TestData.AccountCategoryId, sortOrder: 20, version: 4, description: "Sign-in and passwords"),
            TestData.KbCategory("Getting started", "getting-started", null, TestData.GettingStartedId, sortOrder: 10, version: 2),
            TestData.KbCategory("Shipping", "shipping", PaperplaneId, ShippingId, sortOrder: 30, version: 1),
        ]));
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product("Orbitly"), TestData.Product("Paperplane", PaperplaneId)]));
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<CreateKbCategoryRequest>();
            return TestData.Ok(TestData.KbCategory(request.Name!, request.Slug!, request.ProductId, Guid.NewGuid(), request.SortOrder, 1, request.Description));
        });
        _kb.UpdateCategoryAsync(Arg.Any<Guid>(), Arg.Any<UpdateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<UpdateKbCategoryRequest>();
            return TestData.Ok(TestData.KbCategory(request.Name!, "account", TestData.OrbitlyId, call.Arg<Guid>(), request.SortOrder, request.Version + 1, request.Description));
        });
        _kb.DeleteCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok());
        Services.AddSingleton(_kb);
        Services.AddSingleton(_products);
    }

    private IRenderedComponent<KbCategoriesPage> RenderPage() => Render<KbCategoriesPage>();

    private IEnumerable<CreateKbCategoryRequest> Creates() =>
        _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.CreateCategoryAsync)).Select(c => (CreateKbCategoryRequest)c.GetArguments()[0]!);

    private IEnumerable<(Guid Id, UpdateKbCategoryRequest Request)> Updates() =>
        _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.UpdateCategoryAsync)).Select(c => ((Guid)c.GetArguments()[0]!, (UpdateKbCategoryRequest)c.GetArguments()[1]!));

    private IReadOnlyList<Guid> Deletes() =>
        [.. _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.DeleteCategoryAsync)).Select(c => (Guid)c.GetArguments()[0]!)];

    private static string Value(IRenderedComponent<KbCategoriesPage> cut, string id) => cut.Find($"#{id}").GetAttribute("value")!;

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<KbCategoriesPage> cut, string slug) => cut.Find($"tr[data-category='{slug}']");

    private static AngleSharp.Dom.IElement Dialog(IRenderedComponent<KbCategoriesPage> cut) =>
        cut.FindAll("dialog").Single(d => d.QuerySelector("h2")!.TextContent.StartsWith("Delete the category ", StringComparison.Ordinal));

    private static AngleSharp.Dom.IElement Confirm(IRenderedComponent<KbCategoriesPage> cut) => Dialog(cut).QuerySelector(".ts-dialog-actions button:not(.btn-outline-secondary)")!;

    // The first text of the name cell (the description, when there is one, sits in its own element after it).
    private static IReadOnlyList<string> RowNames(IRenderedComponent<KbCategoriesPage> cut) =>
        [.. cut.FindAll("tbody tr").Select(r => r.Children[0].ChildNodes.First(n => n.NodeType == AngleSharp.Dom.NodeType.Text && !string.IsNullOrWhiteSpace(n.TextContent)).TextContent.Trim())];

    // ---- the list ------------------------------------------------------------------------------------------------

    [Fact]
    public void Categories_are_listed_in_sort_order_with_their_product_slug_order_and_description()
    {
        var cut = RenderPage();

        cut.FindAll("tbody tr").Select(r => r.GetAttribute("data-category")).ShouldBe(["getting-started", "account", "shipping"]);
        Row(cut, "getting-started").Children[1].TextContent.ShouldBe("Shared");
        Row(cut, "account").Children[1].TextContent.ShouldBe("Orbitly");
        Row(cut, "shipping").Children[1].TextContent.ShouldBe("Paperplane");
        Row(cut, "account").QuerySelector("code")!.TextContent.ShouldBe("account");
        Row(cut, "account").QuerySelector(".ts-kb-category-order")!.TextContent.ShouldBe("20");
        Row(cut, "account").QuerySelector(".ts-kb-category-description")!.TextContent.ShouldBe("Sign-in and passwords");
        Row(cut, "shipping").QuerySelectorAll(".ts-kb-category-description").ShouldBeEmpty();
        cut.Find("div.ts-scroll").GetAttribute("aria-label").ShouldBe("Categories");
        _kb.Received(1).ListCategoriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_category_of_a_product_the_lookup_did_not_return_shows_a_fixed_phrase_and_never_its_id()
    {
        _products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product("Orbitly")]));

        var cut = RenderPage();

        Row(cut, "shipping").Children[1].TextContent.ShouldBe("Another product");
        cut.Markup.ShouldNotContain(PaperplaneId.ToString());
    }

    [Fact]
    public void No_categories_is_a_plain_empty_state_and_the_create_form_is_still_there()
    {
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<KbCategoryDto>>([]));

        var cut = RenderPage();

        cut.Find(".ts-state-heading").TextContent.ShouldBe("No categories yet");
        cut.Find("form.ts-kb-category-create").ShouldNotBeNull();
        Value(cut, "ts-cat-order").ShouldBe("10");
    }

    [Fact]
    public void A_failed_load_shows_the_api_message_and_retry_loads_again()
    {
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(
            TestData.Fail<IReadOnlyList<KbCategoryDto>>("api-error", "The API is unavailable."),
            TestData.Ok<IReadOnlyList<KbCategoryDto>>([TestData.KbCategory()]));

        var cut = RenderPage();

        cut.Find(".ts-state--error").TextContent.ShouldContain("Couldn't load the categories. The API is unavailable.");
        cut.Find(".ts-state--error button").Click();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(1));
    }

    [Fact]
    public void A_load_that_was_overtaken_by_a_reload_never_replaces_the_newer_list()
    {
        var slow = new TaskCompletionSource<Result<IReadOnlyList<KbCategoryDto>>>();
        _kb.ListCategoriesAsync(Arg.Any<CancellationToken>()).Returns(
            _ => slow.Task,
            _ => Task.FromResult(TestData.Ok<IReadOnlyList<KbCategoryDto>>([TestData.KbCategory("Newer", "newer")])));
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbCategoryDto>(ApiErrorCodes.ApiTimeout, "slow"));
        var cut = RenderPage();
        cut.FindAll("tbody tr").ShouldBeEmpty();

        // A second read (the reload after an unknown outcome) starts while the first is still waiting.
        cut.Find("#ts-cat-name").Input("Billing");
        cut.Find("form.ts-kb-category-create").Submit();
        cut.WaitForAssertion(() => cut.Find(".ts-conflict button").ShouldNotBeNull());
        cut.Find(".ts-conflict button").Click();
        cut.WaitForAssertion(() => RowNames(cut).ShouldBe(["Newer"]));
        slow.SetResult(TestData.Ok<IReadOnlyList<KbCategoryDto>>([TestData.KbCategory("Older", "older")]));

        cut.WaitForAssertion(() => RowNames(cut).ShouldBe(["Newer"]));
    }

    // ---- who may do what -----------------------------------------------------------------------------------------

    [Fact]
    public void A_plain_agent_opens_the_page_lists_creates_and_edits_but_is_never_offered_delete()
    {
        AsAgent();

        var cut = RenderPage();

        cut.FindAll("tbody tr").Count.ShouldBe(3);
        cut.Find("form.ts-kb-category-create").ShouldNotBeNull();
        cut.FindAll("button.ts-edit").Count.ShouldBe(3);
        cut.FindAll("button.ts-delete").ShouldBeEmpty();
        cut.FindAll("section.ts-no-access").ShouldBeEmpty();
    }

    [Fact]
    public void An_admin_is_offered_delete_on_every_row()
    {
        var cut = RenderPage();

        cut.FindAll("button.ts-delete").Count.ShouldBe(3);
    }

    // ---- create --------------------------------------------------------------------------------------------------

    [Fact]
    public void A_new_category_starts_with_the_next_sort_order_after_the_highest_and_a_slug_that_follows_the_name()
    {
        var cut = RenderPage();

        Value(cut, "ts-cat-order").ShouldBe("40");
        cut.Find("#ts-cat-name").Input("Billing issues");
        Value(cut, "ts-cat-slug").ShouldBe("billing-issues");

        cut.Find("#ts-cat-slug").Input("billing");
        cut.Find("#ts-cat-name").Input("Billing and invoices");
        Value(cut, "ts-cat-slug").ShouldBe("billing");
        cut.FindAll("#ts-cat-product option").Select(o => o.TextContent).ShouldBe(["Shared by every product", "Orbitly", "Paperplane"]);
    }

    [Fact]
    public void Creating_sends_the_trimmed_values_and_never_cancels_adds_the_category_in_order_and_says_so()
    {
        var cut = RenderPage();
        cut.Find("#ts-cat-product").Change(PaperplaneId.ToString());
        cut.Find("#ts-cat-name").Input("  Billing  ");
        cut.Find("#ts-cat-description").Input("  Invoices and refunds ");
        cut.Find("#ts-cat-order").Input("15");

        cut.Find("form.ts-kb-category-create").Submit();

        cut.WaitForAssertion(() => Creates().Count().ShouldBe(1));
        Creates().Single().ShouldBe(new CreateKbCategoryRequest(PaperplaneId, "billing", "Billing", "Invoices and refunds", 15));
        ((CancellationToken)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.CreateCategoryAsync)).GetArguments()[1]!).CanBeCanceled.ShouldBeFalse();
        RowNames(cut).ShouldBe(["Getting started", "Billing", "Account", "Shipping"]);
        StatusMessages.Current.ShouldBe("Created the category Billing");
        Value(cut, "ts-cat-name").ShouldBe(string.Empty);
        Value(cut, "ts-cat-order").ShouldBe("40");
    }

    [Fact]
    public void A_blank_description_is_sent_as_nothing()
    {
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Billing");

        cut.Find("form.ts-kb-category-create").Submit();

        cut.WaitForAssertion(() => Creates().Single().Description.ShouldBeNull());
    }

    [Fact]
    public void An_empty_form_sends_nothing_and_says_what_is_wrong()
    {
        var cut = RenderPage();
        cut.Find("#ts-cat-order").Input("many");

        cut.Find("form.ts-kb-category-create").Submit();

        Creates().ShouldBeEmpty();
        cut.FindAll("[role=alert]").Select(a => a.TextContent.Trim()).ShouldBe(["Enter a name.", "Enter a slug.", "Enter a whole number."]);
    }

    [Fact]
    public void The_slug_search_is_refused_before_it_is_sent_because_the_portal_keeps_it_for_its_search_page()
    {
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Search");

        cut.Find("form.ts-kb-category-create").Submit();

        Creates().ShouldBeEmpty();
        cut.Find("#ts-cat-slug-error").TextContent.ShouldBe("The slug \"search\" is kept for the portal's search page. Pick another.");
    }

    [Theory]
    [InlineData(ApiErrorCodes.KbCategorySlugTaken, "Another category already uses this slug. Slugs are shared across products, so pick another.")]
    [InlineData(ApiErrorCodes.KbCategoryReservedSlug, "The slug \"search\" is kept for the portal's search page. Pick another.")]
    public void A_slug_the_api_refuses_is_a_field_error_and_the_form_is_kept_for_another_try(string code, string message)
    {
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbCategoryDto>(code, "no", ResultErrorKind.Conflict));
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Billing");

        cut.Find("form.ts-kb-category-create").Submit();

        cut.WaitForAssertion(() => cut.Find("#ts-cat-slug-error").TextContent.ShouldBe(message));
        Value(cut, "ts-cat-name").ShouldBe("Billing");
        RowNames(cut).Count.ShouldBe(3);
    }

    [Fact]
    public void A_400_names_its_field_and_a_message_for_no_field_is_shown_above_the_form()
    {
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(Result<KbCategoryDto>.Failure(
            new ResultError("description-too-long", "Too long.", ResultErrorKind.Validation, ApiFields.Description),
            [new ResultError("product-not-found", "No such product.", ResultErrorKind.Validation, "productId")]));
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Billing");

        cut.Find("form.ts-kb-category-create").Submit();

        cut.WaitForAssertion(() => cut.Find("#ts-cat-description-error").TextContent.ShouldBe("Too long."));
        cut.Find("p.ts-form-error").TextContent.ShouldBe("No such product.");
    }

    [Fact]
    public void A_create_with_an_unknown_outcome_says_so_and_offers_a_reload()
    {
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbCategoryDto>(ApiErrorCodes.ApiUnavailable, "down"));
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Billing");

        cut.Find("form.ts-kb-category-create").Submit();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict p").TextContent.ShouldBe("The change may have gone through. Reload the list to check before you try again."));
        cut.Find(".ts-conflict button").TextContent.ShouldBe("Reload list");
        Value(cut, "ts-cat-name").ShouldBe("Billing");
    }

    // ---- edit and re-order ---------------------------------------------------------------------------------------

    [Fact]
    public void Editing_opens_the_name_description_and_order_with_the_slug_and_product_fixed()
    {
        var cut = RenderPage();

        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();

        var row = Row(cut, "account");
        row.QuerySelector("#ts-cat-edit-name")!.GetAttribute("value").ShouldBe("Account");
        row.QuerySelector("#ts-cat-edit-description")!.GetAttribute("value").ShouldBe("Sign-in and passwords");
        row.QuerySelector("#ts-cat-edit-order")!.GetAttribute("value").ShouldBe("20");
        row.QuerySelector("code")!.TextContent.ShouldBe("account");
        row.Children[1].TextContent.ShouldBe("Orbitly");
        row.QuerySelectorAll("input").Count.ShouldBe(3);
    }

    // PHASE-08 T19: the sort order is persisted through the update call, with the version the list was read with.
    [Fact]
    public void Saving_a_new_sort_order_sends_the_update_with_the_loaded_version_and_re_sorts_the_list()
    {
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-cat-edit-order").Input("5");

        cut.Find("button.ts-save").Click();

        cut.WaitForAssertion(() => Updates().Count().ShouldBe(1));
        var (id, request) = Updates().Single();
        id.ShouldBe(TestData.AccountCategoryId);
        request.ShouldBe(new UpdateKbCategoryRequest("Account", "Sign-in and passwords", 5, Version: 4));
        ((CancellationToken)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.UpdateCategoryAsync)).GetArguments()[2]!).CanBeCanceled.ShouldBeFalse();
        cut.WaitForAssertion(() => RowNames(cut).ShouldBe(["Account", "Getting started", "Shipping"]));
        cut.FindAll("tr.ts-kb-category-editing").ShouldBeEmpty();
        StatusMessages.Current.ShouldBe("Saved the category Account");

        // The version in the answer is the one the next save sends.
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-cat-edit-order").Input("6");
        cut.Find("button.ts-save").Click();
        cut.WaitForAssertion(() => Updates().Count().ShouldBe(2));
        Updates().Last().Request.Version.ShouldBe(5u);
    }

    [Fact]
    public void An_edit_that_fails_the_form_checks_sends_nothing()
    {
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-cat-edit-name").Input("  ");
        cut.Find("#ts-cat-edit-order").Input("x");

        cut.Find("button.ts-save").Click();

        Updates().ShouldBeEmpty();
        cut.Find("#ts-cat-edit-name-error").TextContent.ShouldBe("Enter a name.");
        cut.Find("#ts-cat-edit-order-error").TextContent.ShouldBe("Enter a whole number.");
    }

    [Fact]
    public void Cancelling_an_edit_changes_nothing()
    {
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-cat-edit-name").Input("Changed");

        cut.Find("button.ts-cancel").Click();

        Updates().ShouldBeEmpty();
        RowNames(cut)[1].ShouldStartWith("Account");
    }

    // Review Focus 4 (categories): a stale version is a 409 and never an overwrite.
    [Fact]
    public void A_409_says_the_category_changed_keeps_the_edit_open_and_offers_a_reload()
    {
        _kb.UpdateCategoryAsync(Arg.Any<Guid>(), Arg.Any<UpdateKbCategoryRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<KbCategoryDto>(ApiErrorCodes.ConcurrencyConflict, "changed", ResultErrorKind.Conflict));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();
        cut.Find("#ts-cat-edit-name").Input("My rename");

        cut.Find("button.ts-save").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict p").TextContent.ShouldBe("This category changed since you opened the list. Reload the list, then make your change again."));
        cut.Find("#ts-cat-edit-name").GetAttribute("value").ShouldBe("My rename");
        cut.Find(".ts-conflict button").Click();
        cut.WaitForAssertion(() => cut.FindAll(".ts-conflict").ShouldBeEmpty());
        _kb.Received(2).ListCategoriesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void An_edit_of_a_category_deleted_meanwhile_says_so_and_reads_the_list_again()
    {
        _kb.UpdateCategoryAsync(Arg.Any<Guid>(), Arg.Any<UpdateKbCategoryRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<KbCategoryDto>(ApiErrorCodes.KbCategoryNotFound, "gone", ResultErrorKind.NotFound));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();

        cut.Find("button.ts-save").Click();

        cut.WaitForAssertion(() => StatusMessages.Current.ShouldBe("That category no longer exists."));
        cut.WaitForAssertion(() => _kb.Received(2).ListCategoriesAsync(Arg.Any<CancellationToken>()));
        cut.FindAll("tr.ts-kb-category-editing").ShouldBeEmpty();
    }

    [Fact]
    public void An_edit_with_an_unknown_outcome_says_so_and_offers_a_reload()
    {
        _kb.UpdateCategoryAsync(Arg.Any<Guid>(), Arg.Any<UpdateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail<KbCategoryDto>(ApiErrorCodes.ApiTimeout, "slow"));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-edit")!.Click();

        cut.Find("button.ts-save").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-conflict p").TextContent.ShouldContain("may have gone through"));
        cut.Find(".ts-conflict button").TextContent.ShouldBe("Reload list");
    }

    // ---- delete --------------------------------------------------------------------------------------------------

    [Fact]
    public void Delete_asks_first_naming_the_category_and_sends_nothing_until_it_is_confirmed()
    {
        var cut = RenderPage();

        Row(cut, "shipping").QuerySelector("button.ts-delete")!.Click();

        Dialog(cut).QuerySelector("h2")!.TextContent.ShouldBe("Delete the category Shipping?");
        Dialog(cut).TextContent.ShouldContain("This can't be undone.");
        Dialog(cut).ClassName!.ShouldContain("ts-dialog--danger");
        Dialogs.VerifyInvoke("open", 1);
        Deletes().ShouldBeEmpty();
    }

    [Fact]
    public void Confirming_deletes_once_and_never_cancels_and_removes_the_row()
    {
        var cut = RenderPage();
        Row(cut, "shipping").QuerySelector("button.ts-delete")!.Click();

        Confirm(cut).Click();

        cut.WaitForAssertion(() => Deletes().ShouldBe([ShippingId]));
        ((CancellationToken)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.DeleteCategoryAsync)).GetArguments()[1]!).CanBeCanceled.ShouldBeFalse();
        cut.WaitForAssertion(() => RowNames(cut).ShouldBe(["Getting started", "Account"]));
        StatusMessages.Current.ShouldBe("Deleted the category Shipping");
    }

    [Fact]
    public void Cancelling_the_dialog_sends_nothing()
    {
        var cut = RenderPage();
        Row(cut, "shipping").QuerySelector("button.ts-delete")!.Click();

        Dialog(cut).QuerySelector(".ts-dialog-actions button.btn-outline-secondary")!.Click();

        Deletes().ShouldBeEmpty();
        RowNames(cut).Count.ShouldBe(3);
    }

    [Fact]
    public void A_category_with_articles_is_blocked_with_a_message_and_the_dialog_cannot_be_confirmed_again()
    {
        _kb.DeleteCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail(ApiErrorCodes.KbCategoryInUse, "Move the 3 articles first.", ResultErrorKind.Conflict));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();

        Confirm(cut).Click();

        cut.WaitForAssertion(() => Dialog(cut).QuerySelector(".ts-dialog-error")!.TextContent
            .ShouldBe("This category still has articles, so it can't be deleted. Move them to another category first. Move the 3 articles first."));
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
        RowNames(cut).Count.ShouldBe(3);
        Confirm(cut).Click();
        Deletes().Count.ShouldBe(1);
    }

    [Fact]
    public void A_category_deleted_meanwhile_closes_the_dialog_and_reads_the_list_again()
    {
        _kb.DeleteCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.KbCategoryNotFound, "gone", ResultErrorKind.NotFound));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();

        Confirm(cut).Click();

        cut.WaitForAssertion(() => StatusMessages.Current.ShouldBe("That category no longer exists."));
        cut.WaitForAssertion(() => _kb.Received(2).ListCategoriesAsync(Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void A_delete_with_an_unknown_outcome_is_held_until_the_list_is_read_again()
    {
        _kb.DeleteCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.ApiTimeout, "slow"));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();
        Confirm(cut).Click();

        cut.WaitForAssertion(() => Dialog(cut).QuerySelector(".ts-dialog-error")!.TextContent.ShouldBe("The delete may have gone through. Reload the list to check before you try again."));
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();

        // Asking again shows the same held state and sends nothing.
        Dialog(cut).QuerySelector(".ts-dialog-actions button.btn-outline-secondary")!.Click();
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();
        Dialog(cut).QuerySelector(".ts-dialog-error")!.TextContent.ShouldContain("may have gone through");
        Deletes().Count.ShouldBe(1);

        // A later read releases it.
        Dialog(cut).QuerySelector("button.btn-link")!.Click();
        cut.WaitForAssertion(() => _kb.Received(2).ListCategoriesAsync(Arg.Any<CancellationToken>()));
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();
        Dialog(cut).QuerySelectorAll(".ts-dialog-error").ShouldBeEmpty();
        Confirm(cut).HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void A_403_says_only_an_admin_can_delete_and_cannot_be_confirmed_again()
    {
        _kb.DeleteCategoryAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(TestData.Fail(ApiErrorCodes.AdminAccessRequired, "no", ResultErrorKind.Forbidden));
        var cut = RenderPage();
        Row(cut, "account").QuerySelector("button.ts-delete")!.Click();

        Confirm(cut).Click();

        cut.WaitForAssertion(() => Dialog(cut).QuerySelector(".ts-dialog-error")!.TextContent.ShouldBe("Only an admin can delete a category."));
        Confirm(cut).HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task A_save_that_finishes_after_the_page_is_gone_changes_nothing_and_says_nothing()
    {
        var pending = new TaskCompletionSource<Result<KbCategoryDto>>();
        _kb.CreateCategoryAsync(Arg.Any<CreateKbCategoryRequest>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderPage();
        cut.Find("#ts-cat-name").Input("Billing");
        var saving = Task.Run(() => cut.Find("form.ts-kb-category-create").Submit(), Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => Creates().Count().ShouldBe(1));

        cut.Instance.Dispose();
        pending.SetResult(TestData.Ok(TestData.KbCategory("Billing", "billing")));
        await saving;
        await cut.InvokeAsync(() => { });

        StatusMessages.Current.ShouldBeNull();
    }
}

/// <summary>The checks the category form makes with the server's rules.</summary>
public sealed class KbCategoryFormTests
{
    [Theory]
    [InlineData("", "Enter a name.")]
    [InlineData("   ", "Enter a name.")]
    public void A_blank_name_is_refused(string value, string message) => KbCategoryForm.CheckName(value).ShouldBe(message);

    [Fact]
    public void The_name_limit_is_100_and_the_description_limit_300()
    {
        KbCategoryForm.CheckName(new string('a', 100)).ShouldBeNull();
        KbCategoryForm.CheckName(new string('a', 101)).ShouldBe("Use 100 characters or fewer.");
        KbCategoryForm.CheckDescription(new string('a', 300)).ShouldBeNull();
        KbCategoryForm.CheckDescription(new string('a', 301)).ShouldBe("Use 300 characters or fewer.");
        KbCategoryForm.CheckDescription(string.Empty).ShouldBeNull();
    }

    [Theory]
    [InlineData("getting-started", null)]
    [InlineData("a", null)]
    [InlineData("", "Enter a slug.")]
    [InlineData("Getting-started", "Use lower-case letters, numbers and single hyphens, up to 80 characters.")]
    [InlineData("getting--started", "Use lower-case letters, numbers and single hyphens, up to 80 characters.")]
    [InlineData("-getting", "Use lower-case letters, numbers and single hyphens, up to 80 characters.")]
    [InlineData("search", "The slug \"search\" is kept for the portal's search page. Pick another.")]
    public void A_slug_is_lower_case_words_and_never_the_reserved_search(string value, string? message) => KbCategoryForm.CheckSlug(value).ShouldBe(message);

    [Theory]
    [InlineData("10", true, 10)]
    [InlineData(" -5 ", true, -5)]
    [InlineData("0", true, 0)]
    [InlineData("1.5", false, 0)]
    [InlineData("many", false, 0)]
    [InlineData("", false, 0)]
    public void The_sort_order_is_a_whole_number(string value, bool valid, int expected)
    {
        KbCategoryForm.TryParseSortOrder(value, out var parsed).ShouldBe(valid);
        parsed.ShouldBe(expected);
        (KbCategoryForm.CheckSortOrder(value) is null).ShouldBe(valid);
    }

    [Fact]
    public void The_next_sort_order_is_one_step_after_the_highest_and_an_empty_list_starts_at_the_step()
    {
        KbCategoryForm.NextSortOrder([]).ShouldBe(10);
        KbCategoryForm.NextSortOrder(
        [
            KbCategoryRowViewModel.From(TestData.KbCategory(sortOrder: 20), []),
            KbCategoryRowViewModel.From(TestData.KbCategory(sortOrder: 35), []),
        ]).ShouldBe(45);
    }

    [Theory]
    [InlineData("Getting started", "getting-started")]
    [InlineData("  Billing & invoices!  ", "billing-invoices")]
    public void A_slug_is_suggested_from_the_name(string name, string expected) => KbCategoryForm.SlugFrom(name).ShouldBe(expected);

    [Fact]
    public void Rows_are_ordered_by_sort_order_then_name_then_slug()
    {
        var rows = KbCategoryRowViewModel.Sorted(
        [
            KbCategoryRowViewModel.From(TestData.KbCategory("b", "b", sortOrder: 5), []),
            KbCategoryRowViewModel.From(TestData.KbCategory("A", "a2", sortOrder: 5), []),
            KbCategoryRowViewModel.From(TestData.KbCategory("a", "a1", sortOrder: 5), []),
            KbCategoryRowViewModel.From(TestData.KbCategory("z", "z", sortOrder: 1), []),
        ]);

        rows.Select(r => r.Slug).ShouldBe(["z", "a1", "a2", "b"]);
    }
}
```

`tests/TechStrap.Admin.Tests/Components/ReplyComposerArticleTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyntaxCircus.Common;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>
/// Linking knowledge base articles from a reply (PHASE-08 T20). The picker is behind a button, so a composer that never links an article never needs the knowledge base client. A choice lives in the ticket's
/// draft: it survives a mode switch, a failed send and a closed screen, a public reply sends exactly those ids in the order they were chosen, a note sends none, and an accepted send takes out the ones it sent.
/// Review Focus 5 (the Admin half): the API's refusal of a link keeps the text and the choice and says what to do.
/// </summary>
public sealed class ReplyComposerArticleTests : AdminComponentTest
{
    private static readonly Guid FirstArticle = Guid.Parse("dddddddd-0000-0000-0000-0000000000b1");
    private static readonly Guid SecondArticle = Guid.Parse("dddddddd-0000-0000-0000-0000000000b2");

    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly IKbClient _kb = Substitute.For<IKbClient>();

    public ReplyComposerArticleTests()
    {
        Services.AddSingleton(_tickets);
        Services.AddSingleton(_kb);
        Services.AddScoped<DraftStore>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Accepted(MessageVisibilities.Public));
        _tickets.AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Accepted(MessageVisibilities.Internal));
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>()).Returns(_ => TestData.Ok(TestData.KbPage(
        [
            TestData.KbItem("Reset your password", "reset-password", KbArticleStatuses.Published, TestData.OrbitlyId, id: FirstArticle),
            TestData.KbItem("Welcome", "welcome", KbArticleStatuses.Published, productId: null, id: SecondArticle),
        ])));
    }

    private static Result<AgentMessageResponse> Accepted(string visibility) =>
        TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent, visibility), TestData.State(rowVersion: 8)));

    private DraftStore Drafts => Services.GetRequiredService<DraftStore>();

    private IRenderedComponent<ReplyComposer> RenderComposer(Guid? productId = null) =>
        Render<ReplyComposer>(p => p
            .Add(c => c.TicketId, TestData.TicketId)
            .Add(c => c.TicketNumber, "ORB-42")
            .Add(c => c.RequesterEmail, "ada@example.com")
            .Add(c => c.ProductId, productId ?? TestData.OrbitlyId)
            .Add(c => c.RowVersion, 7u));

    private IEnumerable<AddAgentReplyRequest> ReplyRequests() =>
        _tickets.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync)).Select(c => (AddAgentReplyRequest)c.GetArguments()[1]!);

    private IEnumerable<ListKbArticlesRequest> Searches() =>
        _kb.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(IKbClient.ListAsync)).Select(c => (ListKbArticlesRequest)c.GetArguments()[0]!);

    private static void OpenPicker(IRenderedComponent<ReplyComposer> cut) => cut.Find("button.ts-composer-link-article").Click();

    private void SearchAndAdd(IRenderedComponent<ReplyComposer> cut, params string[] titles)
    {
        cut.Find(".ts-kb-picker input[type=search]").Input("a");
        Time.Advance(KbDefaults.PickerDebounce);
        cut.WaitForAssertion(() => cut.FindAll(".ts-kb-picker-results li").Count.ShouldBeGreaterThan(0));
        foreach (var title in titles)
        {
            cut.FindAll(".ts-kb-picker-results li").Single(l => l.QuerySelector(".ts-kb-picker-title")!.TextContent == title).QuerySelector("button")!.Click();
        }
    }

    private static IReadOnlyList<string> Chips(IRenderedComponent<ReplyComposer> cut) => [.. cut.FindAll("ul.ts-kb-chips li .ts-kb-chip-title").Select(c => c.TextContent)];

    private static void Type(IRenderedComponent<ReplyComposer> cut, string text) => cut.Find("textarea").Input(text);

    [Fact]
    public void A_public_reply_offers_a_button_to_link_an_article_and_mounts_no_picker_and_makes_no_knowledge_base_call()
    {
        var cut = RenderComposer();

        var button = cut.Find("button.ts-composer-link-article");

        button.TextContent.Trim().ShouldBe("Link a knowledge base article");
        button.GetAttribute("aria-expanded").ShouldBe("false");
        cut.FindAll(".ts-kb-picker").ShouldBeEmpty();
        cut.FindAll("ul.ts-kb-chips").ShouldBeEmpty();
        _kb.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void The_button_opens_and_closes_the_picker_and_says_which_it_will_do()
    {
        var cut = RenderComposer();

        OpenPicker(cut);

        cut.Find("button.ts-composer-link-article").GetAttribute("aria-expanded").ShouldBe("true");
        cut.Find("button.ts-composer-link-article").TextContent.Trim().ShouldBe("Hide the article search");
        cut.Find("button.ts-composer-link-article").GetAttribute("aria-controls").ShouldBe(cut.Find(".ts-kb-picker").ParentElement!.Id);

        OpenPicker(cut);

        cut.FindAll(".ts-kb-picker").ShouldBeEmpty();
    }

    [Fact]
    public void The_picker_searches_the_tickets_product_and_the_shared_articles()
    {
        var productId = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c3");
        var cut = RenderComposer(productId);
        OpenPicker(cut);

        cut.Find(".ts-kb-picker input[type=search]").Input("password");
        Time.Advance(KbDefaults.PickerDebounce);

        cut.WaitForAssertion(() => Searches().ShouldBe([new ListKbArticlesRequest(productId, false, true, KbArticleStatuses.Published, null, "password", 1, 10)]));
    }

    [Fact]
    public void A_chosen_article_becomes_a_chip_in_the_draft_and_remove_takes_it_out()
    {
        var cut = RenderComposer();
        OpenPicker(cut);

        SearchAndAdd(cut, "Reset your password", "Welcome");

        Chips(cut).ShouldBe(["Reset your password", "Welcome"]);
        Drafts.Get(TestData.TicketId).LinkedArticles.Select(a => a.Id).ShouldBe([FirstArticle, SecondArticle]);
        cut.Find("ul.ts-kb-chips").GetAttribute("aria-labelledby").ShouldBe(cut.Find("p.ts-composer-articles-label").Id);
        cut.Find("p.ts-composer-articles-label").TextContent.ShouldBe("Linked articles");

        cut.Find("ul.ts-kb-chips li button").Click();

        Chips(cut).ShouldBe(["Welcome"]);
        Drafts.Get(TestData.TicketId).LinkedArticles.Select(a => a.Id).ShouldBe([SecondArticle]);
    }

    [Fact]
    public void A_remove_button_names_the_article_it_removes()
    {
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome");

        cut.Find("ul.ts-kb-chips li button").GetAttribute("aria-label").ShouldBe("Remove Welcome");
    }

    [Fact]
    public void Sending_a_public_reply_sends_the_chosen_ids_in_the_order_they_were_chosen_and_takes_them_out_of_the_draft()
    {
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome", "Reset your password");
        Type(cut, "Try this article");

        cut.Find(".ts-composer-actions button.btn-primary").Click();

        cut.WaitForAssertion(() => ReplyRequests().Count().ShouldBe(1));
        ReplyRequests().Single().LinkedArticleIds.ShouldBe([SecondArticle, FirstArticle]);
        cut.WaitForAssertion(() => cut.FindAll("ul.ts-kb-chips").ShouldBeEmpty());
        Drafts.Get(TestData.TicketId).LinkedArticles.ShouldBeEmpty();
    }

    [Fact]
    public void A_reply_with_no_article_chosen_sends_an_empty_list_as_before()
    {
        var cut = RenderComposer();
        Type(cut, "Plain reply");

        cut.Find(".ts-composer-actions button.btn-primary").Click();

        cut.WaitForAssertion(() => ReplyRequests().Single().LinkedArticleIds.ShouldBe([]));
    }

    [Fact]
    public void A_failed_send_keeps_the_text_and_the_chosen_articles()
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>("api-error", "down"));
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome");
        Type(cut, "Try this article");

        cut.Find(".ts-composer-actions button.btn-primary").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-composer-error").ShouldNotBeNull());
        Chips(cut).ShouldBe(["Welcome"]);
        Drafts.Get(TestData.TicketId).PublicText.ShouldBe("Try this article");
    }

    // Review Focus 5: the API refuses a link to an unpublished or other-product article; the text and the choice stay.
    [Theory]
    [InlineData(ApiErrorCodes.KbArticleNotLinkable, ResultErrorKind.Validation)]
    [InlineData(ApiErrorCodes.ArticleNotFound, ResultErrorKind.NotFound)]
    public void An_article_the_api_will_not_link_keeps_the_text_and_the_choice_and_says_what_to_do(string code, ResultErrorKind kind)
    {
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Fail<AgentMessageResponse>(code, "from the API", kind));
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome");
        Type(cut, "Try this article");

        cut.Find(".ts-composer-actions button.btn-primary").Click();

        cut.WaitForAssertion(() => cut.Find(".ts-composer-error").TextContent.ShouldBe(ReplyComposerCopy.ArticleNotLinkable));
        cut.Find(".ts-composer-error").TextContent.ShouldContain("Remove the articles you no longer want, then send again. Your text is kept.");
        Chips(cut).ShouldBe(["Welcome"]);
        Drafts.Get(TestData.TicketId).PublicText.ShouldBe("Try this article");
    }

    [Fact]
    public void A_note_hides_the_article_controls_sends_no_articles_and_the_choice_is_still_there_in_a_public_reply()
    {
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome");

        cut.FindAll(".ts-composer-modes button")[1].Click();

        cut.FindAll(".ts-composer-articles").ShouldBeEmpty();
        Type(cut, "Internal only");
        cut.Find(".ts-composer-actions button.btn-primary").Click();
        cut.WaitForAssertion(() => _tickets.Received(1).AddNoteAsync(Arg.Any<Guid>(), Arg.Any<AddInternalNoteRequest>(), Arg.Any<CancellationToken>()));
        ReplyRequests().ShouldBeEmpty();

        cut.FindAll(".ts-composer-modes button")[0].Click();

        Chips(cut).ShouldBe(["Welcome"]);
    }

    [Fact]
    public void The_chosen_articles_survive_the_screen_being_closed_and_opened_again()
    {
        var first = RenderComposer();
        OpenPicker(first);
        SearchAndAdd(first, "Welcome");
        first.Dispose();

        var second = RenderComposer();

        Chips(second).ShouldBe(["Welcome"]);
    }

    [Fact]
    public async Task An_article_chosen_while_a_send_is_on_its_way_stays_for_the_next_reply()
    {
        var pending = new TaskCompletionSource<Result<AgentMessageResponse>>();
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>()).Returns(_ => pending.Task);
        var cut = RenderComposer();
        OpenPicker(cut);
        SearchAndAdd(cut, "Welcome");
        Type(cut, "Try this article");
        var sending = Task.Run(() => cut.Find(".ts-composer-actions button.btn-primary").Click(), Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => ReplyRequests().Count().ShouldBe(1));

        // The draft is shared, so another composer (or this one, through the store) can add while the first send is on its way.
        Drafts.Get(TestData.TicketId).LinkedArticles.Add(new ArticleChoice(FirstArticle, "Reset your password", false));
        pending.SetResult(Accepted(MessageVisibilities.Public));
        await sending;

        ReplyRequests().Single().LinkedArticleIds.ShouldBe([SecondArticle]);
        cut.WaitForAssertion(() => Drafts.Get(TestData.TicketId).LinkedArticles.Select(a => a.Id).ShouldBe([FirstArticle]));
    }

    [Fact]
    public void While_a_send_is_on_its_way_the_chips_and_the_button_cannot_be_used()
    {
        var draft = Drafts.Get(TestData.TicketId);
        draft.LinkedArticles.Add(new ArticleChoice(SecondArticle, "Welcome", true));
        draft.InFlight = true;
        draft.InFlightMode = ComposerMode.PublicReply;

        var cut = RenderComposer();

        cut.Find("ul.ts-kb-chips li button").HasAttribute("disabled").ShouldBeTrue();
        cut.Find("button.ts-composer-link-article").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public async Task The_draft_never_holds_more_than_ten_articles_or_the_same_article_twice_whatever_the_picker_reports()
    {
        var cut = RenderComposer();
        OpenPicker(cut);
        var picker = cut.FindComponent<ArticlePicker>().Instance;

        await cut.InvokeAsync(() => picker.OnAdd.InvokeAsync(new ArticleChoice(SecondArticle, "Welcome", true)));
        await cut.InvokeAsync(() => picker.OnAdd.InvokeAsync(new ArticleChoice(SecondArticle, "Welcome", true)));
        for (var i = 0; i < 12; i++)
        {
            await cut.InvokeAsync(() => picker.OnAdd.InvokeAsync(new ArticleChoice(Guid.NewGuid(), $"Article {i}", false)));
        }

        Drafts.Get(TestData.TicketId).LinkedArticles.Count.ShouldBe(TicketOperationLimits.MaxLinkedArticles);
        Drafts.Get(TestData.TicketId).LinkedArticles.Count(a => a.Id == SecondArticle).ShouldBe(1);
        Chips(cut).Count.ShouldBe(TicketOperationLimits.MaxLinkedArticles);
    }
}
```

`tests/TechStrap.Admin.Tests/Components/TicketPageArticleLinkingTests.cs`

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Features.Tickets;
using TechStrap.Admin.Tests.Support;
using TechStrap.Contracts.Agents;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Contracts.Tags;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Tests.Components;

/// <summary>The ticket page hands its product to the reply composer, so the picker offers that product's published articles and the shared ones, and the reply carries the chosen ids (PHASE-08 T20).</summary>
public sealed class TicketPageArticleLinkingTests : AdminComponentTest
{
    private static readonly Guid ArticleId = Guid.Parse("dddddddd-0000-0000-0000-0000000000d1");

    private readonly ITicketsClient _tickets = Substitute.For<ITicketsClient>();
    private readonly IKbClient _kb = Substitute.For<IKbClient>();

    public TicketPageArticleLinkingTests()
    {
        var products = Substitute.For<IProductsClient>();
        var agents = Substitute.For<IAgentsClient>();
        var tags = Substitute.For<ITagsClient>();
        products.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<ProductDto>>([TestData.Product()]));
        agents.ListAllAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<AgentListItemDto>>([TestData.Agent("Sam Ortiz", TestData.SamAgentId)]));
        tags.ListAsync(Arg.Any<CancellationToken>()).Returns(TestData.Ok<IReadOnlyList<TagDto>>([TestData.Tag()]));
        _tickets.GetAsync("ORB-42", Arg.Any<CancellationToken>()).Returns(TestData.Ok(TestData.Detail()));
        _tickets.ReplyAsync(Arg.Any<Guid>(), Arg.Any<AddAgentReplyRequest>(), Arg.Any<IReadOnlyList<ReplyAttachment>>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(new AgentMessageResponse(TestData.Message(MessageAuthorTypes.Agent, MessageVisibilities.Public), TestData.State(rowVersion: 8))));
        _kb.ListAsync(Arg.Any<ListKbArticlesRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Ok(TestData.KbPage([TestData.KbItem("Reset your password", "reset-password", KbArticleStatuses.Published, TestData.OrbitlyId, id: ArticleId)])));
        Services.AddSingleton(_tickets);
        Services.AddSingleton(_kb);
        Services.AddSingleton(products);
        Services.AddSingleton(agents);
        Services.AddSingleton(tags);
        Services.AddTicketFeatures();
        Services.AddSingleton(AgentSessions.SignedIn());
        Services.AddSingleton(Substitute.For<IRequestersClient>());
    }

    [Fact]
    public void The_picker_on_a_ticket_searches_that_tickets_product_and_the_reply_carries_the_chosen_article()
    {
        var cut = Render<TicketDetailPage>(p => p.Add(c => c.Number, "ORB-42"));

        cut.Find("button.ts-composer-link-article").Click();
        cut.Find(".ts-kb-picker input[type=search]").Input("password");
        Time.Advance(KbDefaults.PickerDebounce);
        cut.WaitForAssertion(() => cut.FindAll(".ts-kb-picker-results li").Count.ShouldBe(1));
        var search = (ListKbArticlesRequest)_kb.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IKbClient.ListAsync)).GetArguments()[0]!;
        search.ProductId.ShouldBe(TestData.OrbitlyId);
        search.IncludeShared.ShouldBeTrue();
        search.Status.ShouldBe(KbArticleStatuses.Published);

        cut.Find(".ts-kb-picker-results li button").Click();
        cut.Find("textarea").Input("Here is the guide");
        cut.Find(".ts-composer-actions button.btn-primary").Click();

        cut.WaitForAssertion(() => _tickets.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync)).ShouldBe(1));
        var request = (AddAgentReplyRequest)_tickets.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ITicketsClient.ReplyAsync)).GetArguments()[1]!;
        request.LinkedArticleIds.ShouldBe([ArticleId]);
    }
}
```

`tests/TechStrap.Admin.Tests/KbCategoriesHostTests.cs`

```csharp
using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Mvc.Testing;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;
using TechStrap.Tests.Shared.AdminHost;

namespace TechStrap.Admin.Tests;

/// <summary>
/// The whole Admin host with the fake sign-in and the stub API, for the categories page (PHASE-08 T19). Every agent may open it and edit; only an admin is offered Delete (the page is not behind <c>AdminOnly</c>, so the
/// button's absence for an agent is the thing to pin). Every call carries the agent's token.
/// </summary>
public sealed class KbCategoriesHostTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;
    private static readonly Guid OrbitlyId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static AdminFactory FactoryWithCategories()
    {
        var factory = new AdminFactory();
        var product = new ProductDto(OrbitlyId, "orbitly", "Orbitly", "ORB", true, new ProductBrandingDto("Orbitly", null, "#1D4ED8", "#FFFFFF", "#1D4ED8", null, null), 1);
        factory.Api
            .OnJson(HttpMethod.Get, "/api/products", (IReadOnlyList<ProductDto>)[product])
            .OnJson(HttpMethod.Get, "/api/kb/categories", (IReadOnlyList<KbCategoryDto>)
            [
                new KbCategoryDto(Guid.NewGuid(), null, "getting-started", "Getting started", null, 10, 1),
                new KbCategoryDto(Guid.NewGuid(), OrbitlyId, "account", "Account", "Sign-in", 20, 3),
            ]);
        return factory;
    }

    [Fact]
    public async Task An_anonymous_visitor_is_sent_to_the_sign_in_landing_and_the_api_is_never_asked()
    {
        await using var factory = FactoryWithCategories();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/kb/categories", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        factory.Api.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_user_the_api_refuses_gets_the_no_access_page_and_the_categories_are_never_read()
    {
        await using var factory = FactoryWithCategories();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Outsider);

        var html = await client.GetStringAsync("/kb/categories", Ct);

        html.ShouldNotContain("Getting started");
        factory.Api.Requests.ShouldAllBe(r => r.Path == "/api/agents/me");
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Outsider);
    }

    // Review Focus 1 of the roles: category delete is Admin only, so a plain agent is never offered the button.
    [Fact]
    public async Task A_plain_agent_sees_the_categories_and_the_create_form_but_no_delete_button()
    {
        await using var factory = FactoryWithCategories();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Agent);

        var page = await new HtmlParser().ParseDocumentAsync(await client.GetStringAsync("/kb/categories", Ct), Ct);

        page.QuerySelectorAll("tr[data-category]").Length.ShouldBe(2);
        page.QuerySelector("tr[data-category='account']")!.Children[1].TextContent.ShouldBe("Orbitly");
        page.QuerySelector("form.ts-kb-category-create").ShouldNotBeNull();
        page.QuerySelectorAll("button.ts-edit").Length.ShouldBe(2);
        page.QuerySelectorAll("button.ts-delete").ShouldBeEmpty();
        page.QuerySelector("a.ts-rail-link[href='/kb']")!.GetAttribute("aria-current").ShouldBe("page");
        factory.Api.Requests.Select(r => r.Path).Distinct().Order().ShouldBe(["/api/agents/me", "/api/kb/categories", "/api/products"]);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Agent);
    }

    [Fact]
    public async Task An_admin_sees_a_delete_button_on_every_category()
    {
        await using var factory = FactoryWithCategories();
        using var client = factory.CreateClient().SignedInAs(AdminTestPrincipal.Admin);

        var page = await new HtmlParser().ParseDocumentAsync(await client.GetStringAsync("/kb/categories", Ct), Ct);

        page.QuerySelectorAll("button.ts-delete").Length.ShouldBe(2);
        factory.Api.AssertEveryCallBore(AdminTestPrincipal.Admin);
    }
}
```

`tests/TechStrap.Admin.Tests/KbImageCspTests.cs`

```csharp
using TechStrap.Hosting.Security;

namespace TechStrap.Admin.Tests;

/// <summary>
/// Review Focus 2, the browser half (PHASE-08): an uploaded picture is served by the API at <c>{TECHSTRAP_API_PUBLIC_URL}/kb-images/{key}</c>, and the Admin's preview (and, later, the Portal) draws it through <c>img-src</c>. In
/// Production the address is an https one, which <c>https:</c> already covers. In Development it is the API on a loopback port (compose publishes it on 127.0.0.1:8080, or the request's own origin is used), which the
/// loopback entries cover on any port. Without them a picture would upload and then never show in the preview. This asks the policy the question a browser asks, for each shape of address.
/// </summary>
public sealed class KbImageCspTests
{
    private static string[] ImageSources(bool development) =>
        TechStrapCsp.ForBlazorApp(allowLoopbackImages: development).Split(';', StringSplitOptions.TrimEntries)
            .Select(directive => directive.Split(' '))
            .Single(parts => parts[0] == "img-src")[1..];

    /// <summary>The part of CSP source matching these tests need: a scheme-only source (<c>https:</c>) and a host source with an optional wildcard port (<c>http://localhost:*</c>).</summary>
    private static bool Allows(string[] sources, string address)
    {
        var url = new Uri(address);
        return sources.Any(source =>
        {
            if (source.EndsWith(':'))
            {
                return string.Equals(source[..^1], url.Scheme, StringComparison.Ordinal);
            }

            if (!Uri.TryCreate(source.Replace(":*", ":0", StringComparison.Ordinal), UriKind.Absolute, out var allowed))
            {
                return false;
            }

            var anyPort = source.EndsWith(":*", StringComparison.Ordinal);
            return string.Equals(allowed.Scheme, url.Scheme, StringComparison.Ordinal)
                && string.Equals(allowed.Host, url.Host, StringComparison.OrdinalIgnoreCase)
                && (anyPort || allowed.Port == url.Port);
        });
    }

    [Theory]
    [InlineData("http://localhost:8080/kb-images/0198c7e2-1111-7000-8000-000000000001.png")]
    [InlineData("http://localhost:5280/kb-images/0198c7e2-1111-7000-8000-000000000001.webp")]
    [InlineData("http://127.0.0.1:8080/kb-images/0198c7e2-1111-7000-8000-000000000001.png")]
    [InlineData("https://api.example.com/kb-images/0198c7e2-1111-7000-8000-000000000001.png")]
    public void In_development_a_picture_from_the_api_on_loopback_or_over_https_is_allowed(string address) =>
        Allows(ImageSources(development: true), address).ShouldBeTrue(address);

    [Theory]
    [InlineData("https://api.example.com/kb-images/0198c7e2-1111-7000-8000-000000000001.png", true)]
    [InlineData("http://localhost:8080/kb-images/0198c7e2-1111-7000-8000-000000000001.png", false)]
    [InlineData("http://127.0.0.1:8080/kb-images/0198c7e2-1111-7000-8000-000000000001.png", false)]
    [InlineData("http://api.example.com/kb-images/0198c7e2-1111-7000-8000-000000000001.png", false)]
    public void In_production_only_an_https_address_is_allowed_so_the_api_public_url_must_be_https(string address, bool allowed) =>
        Allows(ImageSources(development: false), address).ShouldBe(allowed, address);

    [Theory]
    [InlineData("http://api/kb-images/0198c7e2-1111-7000-8000-000000000001.png")]
    [InlineData("http://api.example.com/kb-images/0198c7e2-1111-7000-8000-000000000001.png")]
    public void In_development_a_plain_http_address_that_is_not_loopback_is_still_refused(string address) =>
        Allows(ImageSources(development: true), address).ShouldBeFalse(address);

    [Fact]
    public void The_matcher_itself_tells_a_wildcard_port_from_a_fixed_one()
    {
        Allows(["http://localhost:*"], "http://localhost:1").ShouldBeTrue();
        Allows(["http://localhost:8080"], "http://localhost:8080/x.png").ShouldBeTrue();
        Allows(["http://localhost:8080"], "http://localhost:9090/x.png").ShouldBeFalse();
        Allows(["https:"], "http://a.example/x.png").ShouldBeFalse();
    }
}
```

`tests/TechStrap.Admin.Tests/KbStyleTests.cs`

Replace

```csharp

    [Fact]
    public void The_article_text_is_monospace_and_the_two_panes_can_shrink_below_their_content()
    {
```

with

```csharp

    [Fact]
    public void A_chip_and_a_picker_result_wrap_a_long_title_instead_of_stretching_the_composer()
    {
        Css.Declarations(".ts-kb-chip")["max-width"].ShouldBe("100%");
        Css.Declarations(".ts-kb-chip-title")["overflow-wrap"].ShouldBe("anywhere");
        Css.Declarations(".ts-kb-picker-title")["overflow-wrap"].ShouldBe("anywhere");
        Css.Declarations(".ts-kb-picker-title")["min-width"].ShouldBe("0");
        Css.Declarations(".ts-kb-chips")["flex-wrap"].ShouldBe("wrap");
    }

    [Fact]
    public void The_sort_order_input_of_a_category_row_stays_narrow_and_the_numbers_line_up()
    {
        Css.Declarations(".ts-kb-category-editing input[type=number]")["max-width"].ShouldBe("8rem");
        Css.Declarations(".ts-kb-category-order")["font-variant-numeric"].ShouldBe("tabular-nums");
    }

    [Fact]
    public void The_article_text_is_monospace_and_the_two_panes_can_shrink_below_their_content()
    {
```

`tests/TechStrap.Admin.Tests/ScrollRegionSiteTests.cs`

Replace

```csharp
        }

        tables.ShouldBe(8);
        unwrapped.ShouldBeEmpty();
        withoutRole.ShouldBeEmpty();
```

with

```csharp
        }

        tables.ShouldBe(9);
        unwrapped.ShouldBeEmpty();
        withoutRole.ShouldBeEmpty();
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: the test project does not build (10 errors), because the categories page, the picker, the chips, `ReplyComposer.ProductId` and the category builders do not exist. The first errors:
- `CS0246`: The type or namespace name 'KbCategoriesPage' could not be found (are you missing a using directive or an assembly reference?) (12 times)
- `CS0246`: The type or namespace name 'ArticleChoice' could not be found (are you missing a using directive or an assembly reference?) (4 times)
- `CS0246`: The type or namespace name 'ArticlePicker' could not be found (are you missing a using directive or an assembly reference?) (4 times)

The docs test fails too, until Step 6 has run the script: `ticks every PHASE-08 task P08-T01 to P08-T20 and the Admin deliverable, and the roadmap row says the phase is complete, pending merge`.

- [ ] **Step 3: Implement**

The categories page and its form model first, then the picker, then the composer and the ticket page, and last the styles. New files are given whole; the others as edits. No CSP code changes (see rule 7).

`src/TechStrap.Admin/Features/Kb/ArticleChoice.cs`

```csharp
namespace TechStrap.Admin.Features.Kb;

/// <summary>An article an agent may link from a reply: the id the reply sends, the title the chip shows, and whether it is shared by every product.</summary>
public sealed record ArticleChoice(Guid Id, string Title, bool IsShared);
```

`src/TechStrap.Admin/Features/Kb/ArticlePicker.razor`

```razor
<div class="ts-kb-picker">
    <label for="@InputId" class="form-label">@ArticlePickerCopy.SearchLabel</label>
    <input id="@InputId" type="search" class="form-control" autocomplete="off" spellcheck="false" disabled="@Disabled" value="@_text" aria-describedby="@($"{InputId}-help")" @oninput="OnInput" />
    <p id="@($"{InputId}-help")" class="ts-field-help">@ArticlePickerCopy.SearchHelp</p>
    <p class="visually-hidden" role="status">@_announcement</p>

    @if (_loading)
    {
        <p class="ts-kb-picker-state">@ArticlePickerCopy.Searching</p>
    }
    @if (_error is not null)
    {
        <p class="ts-field-error" role="alert">@_error</p>
    }
    else if (_searched && !_loading && _results.Count == 0)
    {
        <p class="ts-kb-picker-state">@ArticlePickerCopy.NoResults</p>
    }

    @if (_results.Count > 0)
    {
        <ul class="ts-kb-picker-results" aria-label="@ArticlePickerCopy.ResultsLabel">
            @foreach (var result in _results)
            {
                var added = IsSelected(result);
                <li @key="result.Id" data-article="@result.Id">
                    <span class="ts-kb-picker-title">@result.Title</span>
                    @if (result.IsShared)
                    {
                        <span class="ts-pill">@KbCopy.Shared</span>
                    }
                    <button type="button" class="btn btn-outline-secondary" disabled="@(Disabled || added || AtLimit)" aria-label="@ArticlePickerCopy.AddLabel(result.Title)" @onclick="() => AddAsync(result)">@(added ? ArticlePickerCopy.Added : ArticlePickerCopy.Add)</button>
                </li>
            }
        </ul>
    }
    @if (AtLimit)
    {
        <p class="ts-kb-picker-state" role="status">@ArticlePickerCopy.LimitReached(TicketOperationLimits.MaxLinkedArticles)</p>
    }
</div>
```

`src/TechStrap.Admin/Features/Kb/ArticlePicker.razor.cs`

```csharp
using Microsoft.AspNetCore.Components;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Tickets;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// Searches the published articles an agent may link from a reply (PHASE-08 T20): the ticket's product plus the shared ones, never a draft or an archived article. The search waits
/// <see cref="KbDefaults.PickerDebounce"/> after the last keystroke, asks for <see cref="KbArticleStatuses.Published"/> only and drops anything else the answer might hold, and reports a choice through
/// <see cref="OnAdd"/>; the selection itself belongs to the composer's draft. Every search takes the next <c>_loadId</c> and only the latest may change the list. Nothing is searched for blank text, and nothing is called until the agent types.
/// </summary>
public sealed partial class ArticlePicker : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _id = Guid.NewGuid().ToString("N")[..8];
    private IReadOnlyList<ArticleChoice> _results = [];
    private ITimer? _timer;
    private string _text = string.Empty;
    private string? _error;
    private string _announcement = string.Empty;
    private bool _loading;
    private bool _searched;
    private bool _disposed;
    private Guid _lastProduct;
    private int _loadId;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    /// <summary>The ticket's product: its own articles are offered, and the shared ones.</summary>
    [Parameter, EditorRequired]
    public Guid ProductId { get; set; }

    /// <summary>What the draft already links, so an article cannot be added twice and the limit is known.</summary>
    [Parameter]
    public IReadOnlyList<ArticleChoice> Selected { get; set; } = [];

    [Parameter]
    public bool Disabled { get; set; }

    [Parameter]
    public EventCallback<ArticleChoice> OnAdd { get; set; }

    private string InputId => $"ts-kb-picker-{_id}";

    private bool AtLimit => Selected.Count >= TicketOperationLimits.MaxLinkedArticles;

    private bool IsSelected(ArticleChoice choice) => Selected.Any(s => s.Id == choice.Id);

    protected override void OnParametersSet()
    {
        // A ticket moved to another product must not keep offering the old product's articles.
        if (_searched && _results.Count > 0 && _lastProduct != ProductId)
        {
            _results = [];
            _searched = false;
        }

        _lastProduct = ProductId;
    }

    private void OnInput(ChangeEventArgs e)
    {
        _text = e.Value as string ?? string.Empty;
        _timer?.Dispose();
        _timer = Time.CreateTimer(_ => _ = InvokeAsync(SearchAsync), null, KbDefaults.PickerDebounce, Timeout.InfiniteTimeSpan);
    }

    private async Task SearchAsync()
    {
        _timer?.Dispose();
        _timer = null;
        if (_disposed)
        {
            return;
        }

        var loadId = ++_loadId;
        var text = _text.Trim();
        if (text.Length == 0)
        {
            _results = [];
            _error = null;
            _loading = false;
            _searched = false;
            _announcement = string.Empty;
            StateHasChanged();
            return;
        }

        _loading = true;
        _error = null;
        StateHasChanged();
        try
        {
            var result = await Kb.ListAsync(
                new ListKbArticlesRequest(ProductId, SharedOnly: false, IncludeShared: true, KbArticleStatuses.Published, CategoryId: null, text, Page: 1, KbDefaults.PickerPageSize),
                _lifetime.Token);
            if (_disposed || loadId != _loadId)
            {
                return;
            }

            if (result.IsFailure)
            {
                _error = ArticlePickerCopy.SearchFailed;
                return;
            }

            // Defense in depth: the request asks for published articles, and a row that is not one is never offered, whatever the answer holds.
            _results = [.. result.Value.Items.Where(a => a.Status == KbArticleStatuses.Published).Select(a => new ArticleChoice(a.Id, a.Title, a.ProductId is null))];
            _searched = true;
            _announcement = ArticlePickerCopy.Count(_results.Count);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception) when (loadId == _loadId && !_disposed)
        {
            // Fixed copy only: an exception message can carry a host or a port.
            _error = ArticlePickerCopy.SearchFailed;
        }
        finally
        {
            if (!_disposed && loadId == _loadId)
            {
                _loading = false;
                StateHasChanged();
            }
        }
    }

    private async Task AddAsync(ArticleChoice choice)
    {
        if (Disabled || AtLimit || IsSelected(choice))
        {
            return;
        }

        await OnAdd.InvokeAsync(choice);
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Dispose();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

`src/TechStrap.Admin/Features/Kb/ArticlePickerCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Kb;

/// <summary>The words of the reply composer's article picker and its chips. Plain, sentence case (docs/BRAND.md section 3).</summary>
public static class ArticlePickerCopy
{
    public const string SearchLabel = "Search published articles";
    public const string SearchHelp = "Only published articles of this ticket's product, and shared ones, can be linked. The customer's email gets a link to each.";
    public const string Searching = "Searching";
    public const string NoResults = "No published article matches.";
    public const string ResultsLabel = "Published articles";
    public const string SearchFailed = "Couldn't search the articles. Try again in a moment.";
    public const string Add = "Add";
    public const string Added = "Added";
    public const string Toggle = "Link a knowledge base article";
    public const string ToggleClose = "Hide the article search";
    public const string LinkedLabel = "Linked articles";
    public const string Remove = "Remove";

    public static string AddLabel(string title) => $"Add {title}";

    public static string RemoveLabel(string title) => $"Remove {title}";

    public static string LimitReached(int limit) => $"A reply can link up to {limit} articles.";

    public static string Count(int count) => count == 1 ? "1 article" : $"{count} articles";
}
```

`src/TechStrap.Admin/Features/Kb/KbCategoriesCopy.cs`

```csharp
namespace TechStrap.Admin.Features.Kb;

/// <summary>The words of the categories page: create, rename and re-order, and the delete confirmation that is blocked while articles are in a category. Plain, sentence case (docs/BRAND.md section 3).</summary>
public static class KbCategoriesCopy
{
    public const string Heading = "Categories";
    public const string BackToList = "Knowledge base";
    public const string Loading = "Loading categories";
    public const string LoadFailed = "Couldn't load the categories.";
    public const string NoCategories = "No categories yet";
    public const string NoCategoriesHint = "Create a category, then articles can be published in it.";
    public const string TableLabel = "Categories";

    public const string NewHeading = "New category";
    public const string ProductLabel = "Product";
    public const string SharedOption = "Shared by every product";
    public const string ProductHelp = "Chosen once. A shared category can be used by every product's articles; a product's own category only by that product's.";
    public const string NameLabel = "Name";
    public const string SlugLabel = "Slug";
    public const string SlugHelp = "Letters, numbers and hyphens. It is part of the address of every article in the category and can't be changed once the category exists.";
    public const string DescriptionLabel = "Description";
    public const string DescriptionHelp = "Optional. Shown on the portal.";
    public const string SortOrderLabel = "Sort order";
    public const string SortOrderHelp = "Lower numbers come first.";
    public const string Create = "Create category";
    public const string Creating = "Creating\u2026";

    public const string ColumnName = "Category";
    public const string ColumnScope = "Product";
    public const string ColumnSlug = "Slug";
    public const string ColumnOrder = "Order";
    public const string ColumnActions = "Actions";
    public const string Shared = "Shared";
    public const string UnknownProduct = "Another product";
    public const string Edit = "Edit";
    public const string Save = "Save";
    public const string Cancel = "Cancel";
    public const string Delete = "Delete";
    public const string Reload = "Reload list";

    public const string NameRequired = "Enter a name.";
    public const string NameTooLong = "Use 100 characters or fewer.";
    public const string SlugRequired = "Enter a slug.";
    public const string SlugInvalid = "Use lower-case letters, numbers and single hyphens, up to 80 characters.";
    public const string SlugReserved = "The slug \"search\" is kept for the portal's search page. Pick another.";
    public const string SlugTaken = "Another category already uses this slug. Slugs are shared across products, so pick another.";
    public const string DescriptionTooLong = "Use 300 characters or fewer.";
    public const string SortOrderInvalid = "Enter a whole number.";

    public const string DeleteConfirm = "Delete category";
    public const string DeleteBody = "No articles are in this category as far as the list shows. This can't be undone.";
    public const string DeleteUncertain = "The delete may have gone through. Reload the list to check before you try again.";
    public const string CategoryGone = "That category no longer exists.";
    public const string SaveUncertain = "The change may have gone through. Reload the list to check before you try again.";
    public const string SaveConflict = "This category changed since you opened the list. Reload the list, then make your change again.";
    public const string DeleteForbidden = "Only an admin can delete a category.";

    public static string DeleteTitle(string name) => $"Delete the category {name}?";

    public static string DeleteInUse(string reason) => $"This category still has articles, so it can't be deleted. Move them to another category first. {reason}";

    public static string DeleteFailed(string reason) => $"Couldn't delete the category. Nothing was changed. {reason}";

    public static string Created(string name) => $"Created the category {name}";

    public static string Saved(string name) => $"Saved the category {name}";

    public static string Deleted(string name) => $"Deleted the category {name}";
}
```

`src/TechStrap.Admin/Features/Kb/KbCategoriesPage.razor`

```razor
@page "/kb/categories"
@attribute [Authorize]

<PageTitle>@KbCategoriesCopy.Heading &middot; @KbCopy.Heading</PageTitle>
<div class="ts-settings ts-kb-categories">
    <p class="ts-settings-back"><a href="/kb">&larr; @KbCategoriesCopy.BackToList</a></p>
    <h1>@KbCategoriesCopy.Heading</h1>

    <form class="ts-form ts-kb-category-create" novalidate @onsubmit="CreateAsync">
        <h2>@KbCategoriesCopy.NewHeading</h2>
        <fieldset disabled="@_creating">
            <div class="ts-field">
                <label for="ts-cat-product" class="form-label">@KbCategoriesCopy.ProductLabel</label>
                <select id="ts-cat-product" class="form-select" aria-describedby="ts-cat-product-help" @onchange="OnNewProductChanged">
                    <option value="" selected="@(_newProductId is null)">@KbCategoriesCopy.SharedOption</option>
                    @foreach (var product in _products)
                    {
                        <option value="@product.Id" selected="@(_newProductId == product.Id)">@product.Name</option>
                    }
                </select>
                <p id="ts-cat-product-help" class="ts-field-help">@KbCategoriesCopy.ProductHelp</p>
            </div>
            <div class="ts-field">
                <label for="ts-cat-name" class="form-label">@KbCategoriesCopy.NameLabel</label>
                <input id="ts-cat-name" type="text" class="form-control @(Err(_createErrors, ApiFields.Name) is null ? null : "is-invalid")" value="@_newName" autocomplete="off" @oninput="OnNewNameInput" @onblur="() => CheckCreate(ApiFields.Name)" />
                @ErrorLine("ts-cat-name-error", Err(_createErrors, ApiFields.Name))
            </div>
            <div class="ts-field">
                <label for="ts-cat-slug" class="form-label">@KbCategoriesCopy.SlugLabel</label>
                <input id="ts-cat-slug" type="text" class="form-control @(Err(_createErrors, ApiFields.Slug) is null ? null : "is-invalid")" value="@_newSlug" autocomplete="off" spellcheck="false" @oninput="OnNewSlugInput" @onblur="() => CheckCreate(ApiFields.Slug)" />
                <p class="ts-field-help">@KbCategoriesCopy.SlugHelp</p>
                @ErrorLine("ts-cat-slug-error", Err(_createErrors, ApiFields.Slug))
            </div>
            <div class="ts-field">
                <label for="ts-cat-description" class="form-label">@KbCategoriesCopy.DescriptionLabel</label>
                <input id="ts-cat-description" type="text" class="form-control @(Err(_createErrors, ApiFields.Description) is null ? null : "is-invalid")" value="@_newDescription" autocomplete="off" @oninput="e => _newDescription = e.Value?.ToString() ?? string.Empty" @onblur="() => CheckCreate(ApiFields.Description)" />
                <p class="ts-field-help">@KbCategoriesCopy.DescriptionHelp</p>
                @ErrorLine("ts-cat-description-error", Err(_createErrors, ApiFields.Description))
            </div>
            <div class="ts-field">
                <label for="ts-cat-order" class="form-label">@KbCategoriesCopy.SortOrderLabel</label>
                <input id="ts-cat-order" type="number" class="form-control @(Err(_createErrors, ApiFields.SortOrder) is null ? null : "is-invalid")" value="@_newSortOrder" autocomplete="off" @oninput="OnNewSortOrderInput" @onblur="() => CheckCreate(ApiFields.SortOrder)" />
                <p class="ts-field-help">@KbCategoriesCopy.SortOrderHelp</p>
                @ErrorLine("ts-cat-order-error", Err(_createErrors, ApiFields.SortOrder))
            </div>
        </fieldset>
        @if (_createFormError is not null)
        {
            <p class="ts-form-error" role="alert">@_createFormError</p>
        }
        <div class="ts-form-actions">
            <button type="submit" class="btn btn-primary" disabled="@_creating">@(_creating ? KbCategoriesCopy.Creating : KbCategoriesCopy.Create)</button>
        </div>
    </form>

    @if (_rowError is not null)
    {
        <div class="ts-conflict" role="alert">
            <p>@_rowError</p>
            @if (_rowReload)
            {
                <button type="button" class="btn btn-outline-secondary" @onclick="ReloadAsync">@KbCategoriesCopy.Reload</button>
            }
        </div>
    }

    @if (_loading && _rows.Count == 0)
    {
        <LoadingState Rows="4" Label="@KbCategoriesCopy.Loading" />
    }
    else if (_error is not null)
    {
        <ErrorState Message="@_error" OnRetry="ReloadAsync" />
    }
    else if (_rows.Count == 0)
    {
        <EmptyState Heading="@KbCategoriesCopy.NoCategories"><p>@KbCategoriesCopy.NoCategoriesHint</p></EmptyState>
    }
    else
    {
        <ScrollRegion Label="@KbCategoriesCopy.TableLabel">
            <table role="table" class="table ts-ledger ts-settings-table">
                <thead role="rowgroup">
                    <tr role="row">
                        <th role="columnheader" scope="col">@KbCategoriesCopy.ColumnName</th>
                        <th role="columnheader" scope="col">@KbCategoriesCopy.ColumnScope</th>
                        <th role="columnheader" scope="col">@KbCategoriesCopy.ColumnSlug</th>
                        <th role="columnheader" scope="col">@KbCategoriesCopy.ColumnOrder</th>
                        <th role="columnheader" scope="col"><span class="visually-hidden">@KbCategoriesCopy.ColumnActions</span></th>
                    </tr>
                </thead>
                <tbody role="rowgroup">
                    @foreach (var row in _rows)
                    {
                        @if (_editing == row.Id)
                        {
                            <tr role="row" @key="row.Id" class="ts-kb-category-editing" data-category="@row.Slug">
                                <td role="cell">
                                    <label for="ts-cat-edit-name" class="visually-hidden">@KbCategoriesCopy.NameLabel</label>
                                    <input id="ts-cat-edit-name" type="text" class="form-control @(Err(_editErrors, ApiFields.Name) is null ? null : "is-invalid")" value="@_editName" autocomplete="off" @oninput="e => _editName = e.Value?.ToString() ?? string.Empty" />
                                    @ErrorLine("ts-cat-edit-name-error", Err(_editErrors, ApiFields.Name))
                                    <label for="ts-cat-edit-description" class="visually-hidden">@KbCategoriesCopy.DescriptionLabel</label>
                                    <input id="ts-cat-edit-description" type="text" class="form-control @(Err(_editErrors, ApiFields.Description) is null ? null : "is-invalid")" value="@_editDescription" autocomplete="off" placeholder="@KbCategoriesCopy.DescriptionLabel" @oninput="e => _editDescription = e.Value?.ToString() ?? string.Empty" />
                                    @ErrorLine("ts-cat-edit-description-error", Err(_editErrors, ApiFields.Description))
                                </td>
                                <td role="cell">@row.ProductName</td>
                                <td role="cell"><code>@row.Slug</code></td>
                                <td role="cell">
                                    <label for="ts-cat-edit-order" class="visually-hidden">@KbCategoriesCopy.SortOrderLabel</label>
                                    <input id="ts-cat-edit-order" type="number" class="form-control @(Err(_editErrors, ApiFields.SortOrder) is null ? null : "is-invalid")" value="@_editSortOrder" autocomplete="off" @oninput="e => _editSortOrder = e.Value?.ToString() ?? string.Empty" />
                                    @ErrorLine("ts-cat-edit-order-error", Err(_editErrors, ApiFields.SortOrder))
                                </td>
                                <td role="cell" class="ts-settings-actions">
                                    <button type="button" class="btn btn-primary ts-save" disabled="@_busy" @onclick="SaveEditAsync">@KbCategoriesCopy.Save</button>
                                    <button type="button" class="btn btn-outline-secondary ts-cancel" disabled="@_busy" @onclick="CancelEdit">@KbCategoriesCopy.Cancel</button>
                                </td>
                            </tr>
                        }
                        else
                        {
                            <tr role="row" @key="row.Id" data-category="@row.Slug">
                                <td role="cell">
                                    @row.Name
                                    @if (!string.IsNullOrWhiteSpace(row.Description))
                                    {
                                        <span class="ts-field-help ts-kb-category-description">@row.Description</span>
                                    }
                                </td>
                                <td role="cell">@row.ProductName</td>
                                <td role="cell"><code>@row.Slug</code></td>
                                <td role="cell" class="ts-kb-category-order">@row.SortOrder</td>
                                <td role="cell" class="ts-settings-actions">
                                    <button type="button" class="btn btn-outline-secondary ts-edit" disabled="@_busy" @onclick="() => BeginEdit(row)">@KbCategoriesCopy.Edit</button>
                                    @if (Session.IsAdmin)
                                    {
                                        <button type="button" class="btn btn-outline-secondary ts-delete" disabled="@_busy" @onclick="() => AskDelete(row)">@KbCategoriesCopy.Delete</button>
                                    }
                                </td>
                            </tr>
                        }
                    }
                </tbody>
            </table>
        </ScrollRegion>
    }

    <ConfirmDialog Open="@(Deleting is not null)" Title="@(Deleting is null ? string.Empty : KbCategoriesCopy.DeleteTitle(Deleting.Name))" ConfirmLabel="@KbCategoriesCopy.DeleteConfirm"
                   Danger="true" Busy="_deleteBusy" ConfirmDisabled="_deleteBlocked" Error="@_deleteError" OnConfirm="ConfirmDeleteAsync" OnCancel="CancelDelete">
        <p>@KbCategoriesCopy.DeleteBody</p>
        @if (_deleteUncertain)
        {
            <div class="ts-dialog-recovery">
                <button type="button" class="btn btn-link" @onclick="ReloadAfterUncertainAsync">@KbCategoriesCopy.Reload</button>
            </div>
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

`src/TechStrap.Admin/Features/Kb/KbCategoriesPage.razor.cs`

```csharp
using System.Globalization;
using Microsoft.AspNetCore.Components;
using SyntaxCircus.Common;
using TechStrap.Admin.Auth;
using TechStrap.Admin.Clients;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Kb;

/// <summary>
/// The categories (any agent): the list in sort order with the product each belongs to, create, rename, re-describe and re-order (the sort order is saved through the update, with the version the list was read
/// with), and delete (Admin only: the button is drawn only for an admin, and the API answers 403 to anyone else, which the dialog reports). A delete is blocked by the API while articles are in the category (409
/// <c>kb-category-in-use</c>): the dialog says so and cannot be confirmed. Writes use <see cref="CancellationToken.None"/> and never retry; an unknown outcome says so and offers a reload, and a delete whose outcome is
/// unknown is held (<see cref="UncertainMarks"/>) until a later read of the list. Every load takes the next <c>_loadId</c> and only the latest may change the screen.
/// </summary>
public sealed partial class KbCategoriesPage : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, string> _createErrors = [];
    private readonly Dictionary<string, string> _editErrors = [];
    private readonly UncertainMarks _uncertainDeletes = new();
    private IReadOnlyList<KbCategoryRowViewModel> _rows = [];
    private IReadOnlyList<ProductDto> _products = [];
    private Guid? _newProductId;
    private Guid _deletingId;
    private Guid _editing;
    private string _newName = string.Empty;
    private string _newSlug = string.Empty;
    private string _newDescription = string.Empty;
    private string _newSortOrder = KbCategoryForm.SortOrderStep.ToString(CultureInfo.InvariantCulture);
    private string _editName = string.Empty;
    private string _editDescription = string.Empty;
    private string _editSortOrder = string.Empty;
    private string? _error;
    private string? _rowError;
    private string? _createFormError;
    private string? _deleteError;
    private bool _slugEdited;
    private bool _sortOrderTouched;
    private bool _loading = true;
    private bool _creating;
    private bool _busy;
    private bool _rowReload;
    private bool _deleteBusy;
    private bool _deleteBlocked;
    private bool _deleteUncertain;
    private bool _disposed;
    private int _loadId;

    [Inject]
    private IKbClient Kb { get; set; } = default!;

    [Inject]
    private IProductsClient ProductsClient { get; set; } = default!;

    [Inject]
    private AgentSession Session { get; set; } = default!;

    [Inject]
    private StatusMessageService StatusMessages { get; set; } = default!;

    // The row being deleted, looked up fresh each time so a refreshed list is always what is confirmed.
    private KbCategoryRowViewModel? Deleting => _deletingId == Guid.Empty ? null : _rows.FirstOrDefault(r => r.Id == _deletingId);

    protected override async Task OnInitializedAsync()
    {
        Session.Changed += OnSessionChanged;
        await LoadAsync();
    }

    private void OnSessionChanged()
    {
        if (!_disposed)
        {
            _ = InvokeAsync(StateHasChanged);
        }
    }

    // True when this call read the list and it is now on screen. Only the latest load may change the screen: a slow answer that was overtaken is ignored.
    private async Task<bool> LoadAsync()
    {
        var loadId = ++_loadId;
        _loading = true;
        _error = null;
        try
        {
            var categories = Kb.ListCategoriesAsync(_lifetime.Token);
            var products = ProductsClient.ListAsync(_lifetime.Token);
            await Task.WhenAll(categories, products);
            if (_lifetime.IsCancellationRequested || loadId != _loadId)
            {
                return false;
            }

            if (categories.Result.IsFailure || products.Result.IsFailure)
            {
                var failure = categories.Result.IsFailure ? categories.Result.Errors[0] : products.Result.Errors[0];
                _error = $"{KbCategoriesCopy.LoadFailed} {failure.Message}";
                return false;
            }

            _products = products.Result.Value;
            _rows = KbCategoryRowViewModel.Sorted(categories.Result.Value.Select(c => KbCategoryRowViewModel.From(c, _products)));
            _uncertainDeletes.ReleaseForLoad(loadId);

            // A form the agent has not started keeps offering the next free place in the order.
            if (!_sortOrderTouched && _newName.Length == 0)
            {
                _newSortOrder = KbCategoryForm.NextSortOrder(_rows).ToString(CultureInfo.InvariantCulture);
            }

            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (loadId == _loadId)
            {
                _loading = false;
            }
        }
    }

    private async Task ReloadAsync()
    {
        _rowError = null;
        _rowReload = false;
        await LoadAsync();
    }

    // ---- create --------------------------------------------------------------------------------------------------

    private void OnNewSortOrderInput(ChangeEventArgs e)
    {
        _newSortOrder = e.Value?.ToString() ?? string.Empty;
        _sortOrderTouched = true;
    }

    private void OnNewProductChanged(ChangeEventArgs e) => _newProductId = Guid.TryParse(e.Value as string, out var id) ? id : null;

    private void OnNewNameInput(ChangeEventArgs e)
    {
        _newName = e.Value?.ToString() ?? string.Empty;
        if (!_slugEdited)
        {
            _newSlug = KbCategoryForm.SlugFrom(_newName);
        }
    }

    private void OnNewSlugInput(ChangeEventArgs e)
    {
        _newSlug = e.Value?.ToString() ?? string.Empty;
        _slugEdited = true;
    }

    private void CheckCreate(string field)
    {
        var message = field switch
        {
            ApiFields.Name => KbCategoryForm.CheckName(_newName),
            ApiFields.Slug => KbCategoryForm.CheckSlug(_newSlug),
            ApiFields.Description => KbCategoryForm.CheckDescription(_newDescription),
            _ => KbCategoryForm.CheckSortOrder(_newSortOrder),
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
        foreach (var field in new[] { ApiFields.Name, ApiFields.Slug, ApiFields.Description, ApiFields.SortOrder })
        {
            CheckCreate(field);
        }

        if (_createErrors.Count > 0 || !KbCategoryForm.TryParseSortOrder(_newSortOrder, out var sortOrder))
        {
            return;
        }

        _creating = true;
        try
        {
            var request = new CreateKbCategoryRequest(_newProductId, _newSlug.Trim(), _newName.Trim(), Blank(_newDescription), sortOrder);
            var result = await Kb.CreateCategoryAsync(request, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsFailure)
            {
                ShowFailure(result.Errors, _createErrors, create: true);
                return;
            }

            var category = result.Value;
            _rows = KbCategoryRowViewModel.Sorted([.. _rows, KbCategoryRowViewModel.From(category, _products)]);
            _newName = string.Empty;
            _newSlug = string.Empty;
            _newDescription = string.Empty;
            _newSortOrder = KbCategoryForm.NextSortOrder(_rows).ToString(CultureInfo.InvariantCulture);
            _slugEdited = false;
            _sortOrderTouched = false;
            _createErrors.Clear();
            StatusMessages.Show(KbCategoriesCopy.Created(category.Name));
        }
        finally
        {
            _creating = false;
        }
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // A slug taken or reserved is a field error on the slug; a 400 names its field; an unknown outcome or a stale version is said once, above the list.
    private void ShowFailure(IReadOnlyList<ResultError> errors, Dictionary<string, string> fieldErrors, bool create)
    {
        var first = errors[0];
        if (create && first.Code == ApiErrorCodes.KbCategorySlugTaken)
        {
            fieldErrors[ApiFields.Slug] = KbCategoriesCopy.SlugTaken;
            return;
        }

        if (create && first.Code == ApiErrorCodes.KbCategoryReservedSlug)
        {
            fieldErrors[ApiFields.Slug] = KbCategoriesCopy.SlugReserved;
            return;
        }

        if (WriteOutcomes.Classify(first) == WriteOutcome.Conflict)
        {
            _rowError = KbCategoriesCopy.SaveConflict;
            _rowReload = true;
            return;
        }

        if (ApiErrorCodes.IsUncertainWrite(first.Code))
        {
            _rowError = KbCategoriesCopy.SaveUncertain;
            _rowReload = true;
            return;
        }

        foreach (var error in errors)
        {
            // The edit form shows only name, description and sort order: an error for any other field goes above the list, never into a field error that would block Save.
            if (error.Target is ApiFields.Name or ApiFields.Description || (create && error.Target is ApiFields.Slug))
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

    private void BeginEdit(KbCategoryRowViewModel row)
    {
        _rowError = null;
        _rowReload = false;
        _editErrors.Clear();
        _editing = row.Id;
        _editName = row.Name;
        _editDescription = row.Description ?? string.Empty;
        _editSortOrder = row.SortOrder.ToString(CultureInfo.InvariantCulture);
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

        Set(_editErrors, ApiFields.Name, KbCategoryForm.CheckName(_editName));
        Set(_editErrors, ApiFields.Description, KbCategoryForm.CheckDescription(_editDescription));
        Set(_editErrors, ApiFields.SortOrder, KbCategoryForm.CheckSortOrder(_editSortOrder));
        if (_editErrors.Count > 0 || !KbCategoryForm.TryParseSortOrder(_editSortOrder, out var sortOrder))
        {
            return;
        }

        _busy = true;
        _rowError = null;
        _rowReload = false;
        try
        {
            var result = await Kb.UpdateCategoryAsync(row.Id, new UpdateKbCategoryRequest(_editName.Trim(), Blank(_editDescription), sortOrder, row.Version), CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = KbCategoryRowViewModel.Sorted(_rows.Select(r => r.Id == row.Id ? KbCategoryRowViewModel.From(result.Value, _products) : r));
                _editing = Guid.Empty;
                StatusMessages.Show(KbCategoriesCopy.Saved(result.Value.Name));
            }
            else if (result.Errors[0].Code == ApiErrorCodes.KbCategoryNotFound)
            {
                _editing = Guid.Empty;
                StatusMessages.Show(KbCategoriesCopy.CategoryGone);
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

    private void AskDelete(KbCategoryRowViewModel row)
    {
        _rowError = null;
        _deletingId = row.Id;
        _deleteUncertain = _uncertainDeletes.Contains(row.Id);
        _deleteBlocked = _deleteUncertain;
        _deleteError = _deleteUncertain ? KbCategoriesCopy.DeleteUncertain : null;
    }

    private void CancelDelete()
    {
        if (!_deleteBusy)
        {
            _deletingId = Guid.Empty;
            _deleteError = null;
            _deleteBlocked = false;
            _deleteUncertain = false;
        }
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_deleteBusy || Deleting is not { } category || _uncertainDeletes.Contains(category.Id))
        {
            return;
        }

        _deleteBusy = true;
        _deleteError = null;
        _deleteUncertain = false;
        StateHasChanged();
        try
        {
            var result = await Kb.DeleteCategoryAsync(category.Id, CancellationToken.None);
            if (_disposed)
            {
                return;
            }

            if (result.IsSuccess)
            {
                _rows = [.. _rows.Where(r => r.Id != category.Id)];
                _deletingId = Guid.Empty;
                StatusMessages.Show(KbCategoriesCopy.Deleted(category.Name));
                return;
            }

            await ShowDeleteFailureAsync(result.Errors[0], category.Id);
        }
        finally
        {
            _deleteBusy = false;
        }
    }

    private async Task ShowDeleteFailureAsync(ResultError error, Guid categoryId)
    {
        if (error.Code == ApiErrorCodes.KbCategoryInUse)
        {
            // Articles are in the category: the dialog stays open to say so and cannot be confirmed (the API would answer the same again).
            _deleteError = KbCategoriesCopy.DeleteInUse(error.Message);
            _deleteBlocked = true;
        }
        else if (error.Code == ApiErrorCodes.KbCategoryNotFound)
        {
            _deletingId = Guid.Empty;
            StatusMessages.Show(KbCategoriesCopy.CategoryGone);
            await LoadAsync();
        }
        else if (error.Kind == ResultErrorKind.Forbidden)
        {
            _deleteError = KbCategoriesCopy.DeleteForbidden;
            _deleteBlocked = true;
        }
        else if (ApiErrorCodes.IsUncertainWrite(error.Code))
        {
            _deleteError = KbCategoriesCopy.DeleteUncertain;
            _deleteUncertain = true;
            _deleteBlocked = true;
            _uncertainDeletes.Add(categoryId, _loadId);
        }
        else
        {
            _deleteError = KbCategoriesCopy.DeleteFailed(error.Message);
        }
    }

    private async Task ReloadAfterUncertainAsync()
    {
        _deletingId = Guid.Empty;
        _deleteError = null;
        _deleteBlocked = false;
        _deleteUncertain = false;
        await LoadAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        Session.Changed -= OnSessionChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

`src/TechStrap.Admin/Features/Kb/KbCategoryRowViewModel.cs`

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using TechStrap.Admin.Clients;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Products;

namespace TechStrap.Admin.Features.Kb;

/// <summary>One category row, ready to draw. <see cref="Version"/> is the one the list was read with: the update sends it, so a stale save is a 409 and never an overwrite.</summary>
internal sealed record KbCategoryRowViewModel(Guid Id, Guid? ProductId, string ProductName, string Slug, string Name, string? Description, int SortOrder, uint Version)
{
    public static KbCategoryRowViewModel From(KbCategoryDto category, IReadOnlyList<ProductDto> products) => new(
        category.Id,
        category.ProductId,
        category.ProductId is { } id ? products.FirstOrDefault(p => p.Id == id)?.Name ?? KbCategoriesCopy.UnknownProduct : KbCategoriesCopy.Shared,
        category.Slug,
        category.Name,
        category.Description,
        category.SortOrder,
        category.Version);

    /// <summary>Rows in sort order, then by name, as the API lists them (a created or renamed row is placed the same way before the next read).</summary>
    public static IReadOnlyList<KbCategoryRowViewModel> Sorted(IEnumerable<KbCategoryRowViewModel> rows) =>
        [.. rows.OrderBy(r => r.SortOrder).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Slug, StringComparer.Ordinal)];
}

/// <summary>The checks the category form makes before it sends, with the server's own rules. The field names of a 400 are <see cref="ApiFields"/>.</summary>
internal static partial class KbCategoryForm
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 300;
    public const int SortOrderStep = 10;

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotSlugCharacters();

    public static string? CheckName(string value) =>
        string.IsNullOrWhiteSpace(value) ? KbCategoriesCopy.NameRequired : value.Trim().Length > NameMaxLength ? KbCategoriesCopy.NameTooLong : null;

    public static string? CheckSlug(string value)
    {
        var slug = value.Trim();
        return string.IsNullOrEmpty(slug) ? KbCategoriesCopy.SlugRequired
            : slug.Length > KbEditorLimits.SlugMaxLength || !SlugPattern().IsMatch(slug) ? KbCategoriesCopy.SlugInvalid
            : slug == KbLimits.ReservedCategorySlug ? KbCategoriesCopy.SlugReserved : null;
    }

    public static string? CheckDescription(string value) => value.Trim().Length > DescriptionMaxLength ? KbCategoriesCopy.DescriptionTooLong : null;

    public static string? CheckSortOrder(string value) => TryParseSortOrder(value, out _) ? null : KbCategoriesCopy.SortOrderInvalid;

    public static bool TryParseSortOrder(string value, out int sortOrder) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sortOrder);

    /// <summary>A slug suggested from a name ("Getting started" gives "getting-started"). The agent can change it until the category is created.</summary>
    public static string SlugFrom(string name)
    {
        var slug = NotSlugCharacters().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        return slug.Length > KbEditorLimits.SlugMaxLength ? slug[..KbEditorLimits.SlugMaxLength].TrimEnd('-') : slug;
    }

    /// <summary>The sort order a new category starts with: the step after the highest in the list, so it comes last until the agent says otherwise.</summary>
    public static int NextSortOrder(IEnumerable<KbCategoryRowViewModel> rows) => rows.Select(r => r.SortOrder).DefaultIfEmpty(0).Max() + SortOrderStep;
}
```

`src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor`

Replace (edit 1 of 2)

```razor
<NavigationLock ConfirmExternalNavigation="@HasUnsentText" />
<section class="@SectionCss" aria-label="Reply composer" data-shortcut-scope="composer">
```

with

```razor
@using TechStrap.Admin.Features.Kb
<NavigationLock ConfirmExternalNavigation="@HasUnsentText" />
<section class="@SectionCss" aria-label="Reply composer" data-shortcut-scope="composer">
```

Replace (edit 2 of 2)

```razor
              placeholder="@(IsPublic ? ReplyComposerCopy.ReplyPlaceholder : ReplyComposerCopy.NotePlaceholder)"
              @bind="Text" @bind:event="oninput"></textarea>

    @if (_filesDropped)
```

with

```razor
              placeholder="@(IsPublic ? ReplyComposerCopy.ReplyPlaceholder : ReplyComposerCopy.NotePlaceholder)"
              @bind="Text" @bind:event="oninput"></textarea>

    @if (IsPublic)
    {
        <div class="ts-composer-articles">
            @if (_draft.LinkedArticles.Count > 0)
            {
                <p class="ts-composer-articles-label" id="@LinkedLabelId">@ArticlePickerCopy.LinkedLabel</p>
                <ul class="ts-kb-chips" aria-labelledby="@LinkedLabelId">
                    @foreach (var article in _draft.LinkedArticles)
                    {
                        <li @key="article.Id" class="ts-kb-chip" data-article="@article.Id">
                            <span class="ts-kb-chip-title">@article.Title</span>
                            <button type="button" class="btn btn-link" aria-label="@ArticlePickerCopy.RemoveLabel(article.Title)" disabled="@Sending" @onclick="() => RemoveArticle(article)">@ArticlePickerCopy.Remove</button>
                        </li>
                    }
                </ul>
            }
            <button type="button" class="btn btn-outline-secondary ts-composer-link-article" aria-expanded="@(_pickerOpen ? "true" : "false")" aria-controls="@PickerId" disabled="@Sending" @onclick="TogglePicker">
                @(_pickerOpen ? ArticlePickerCopy.ToggleClose : ArticlePickerCopy.Toggle)
            </button>
            @if (_pickerOpen)
            {
                <div id="@PickerId">
                    <ArticlePicker ProductId="ProductId" Selected="_draft.LinkedArticles" Disabled="Sending" OnAdd="AddArticle" />
                </div>
            }
        </div>
    }

    @if (_filesDropped)
```

`src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor.cs`

Replace (edit 1 of 11)

```csharp
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Intake;
```

with

```csharp
using TechStrap.Admin.Clients;
using TechStrap.Admin.Components.Ui;
using TechStrap.Admin.Features.Kb;
using TechStrap.Admin.Features.Shell;
using TechStrap.Contracts.Intake;
```

Replace (edit 2 of 11)

```csharp
    private bool _disposed;
    private bool _filesDropped;

    [Inject]
```

with

```csharp
    private bool _disposed;
    private bool _filesDropped;
    private bool _pickerOpen;

    [Inject]
```

Replace (edit 3 of 11)

```csharp
    [Parameter, EditorRequired]
    public string RequesterEmail { get; set; } = string.Empty;

    /// <summary>The ticket's current RowVersion, replaced by the page after every write and every reload.</summary>
```

with

```csharp
    [Parameter, EditorRequired]
    public string RequesterEmail { get; set; } = string.Empty;

    /// <summary>The ticket's product: the article picker offers its published articles and the shared ones. A ticket that moves to another product changes it.</summary>
    [Parameter, EditorRequired]
    public Guid ProductId { get; set; }

    /// <summary>The ticket's current RowVersion, replaced by the page after every write and every reload.</summary>
```

Replace (edit 4 of 11)

```csharp

    private string StatusId => $"ts-composer-status-{_id}";

    private string SectionCss => IsPublic ? "ts-composer ts-composer--public" : "ts-composer ts-composer--note";
```

with

```csharp

    private string StatusId => $"ts-composer-status-{_id}";

    private string PickerId => $"ts-composer-picker-{_id}";

    private string LinkedLabelId => $"ts-composer-linked-{_id}";

    private string SectionCss => IsPublic ? "ts-composer ts-composer--public" : "ts-composer ts-composer--note";
```

Replace (edit 5 of 11)

```csharp
    private void RemoveFile(IBrowserFile file) => _files.Remove(file);

    /// <param name="statusAfterOverride">"Send and solve" passes Solved; otherwise the status choice in the draft applies.</param>
    private async Task SubmitAsync(string? statusAfterOverride = null)
```

with

```csharp
    private void RemoveFile(IBrowserFile file) => _files.Remove(file);

    private void TogglePicker() => _pickerOpen = !_pickerOpen;

    // The picker offers published articles only; the draft keeps the choice, in the order it was made, never twice and never beyond the limit the API enforces.
    private void AddArticle(ArticleChoice article)
    {
        if (_draft.LinkedArticles.Count < TicketOperationLimits.MaxLinkedArticles && _draft.LinkedArticles.All(a => a.Id != article.Id))
        {
            _draft.LinkedArticles.Add(article);
        }
    }

    private void RemoveArticle(ArticleChoice article) => _draft.LinkedArticles.Remove(article);

    /// <param name="statusAfterOverride">"Send and solve" passes Solved; otherwise the status choice in the draft applies.</param>
    private async Task SubmitAsync(string? statusAfterOverride = null)
```

Replace (edit 6 of 11)

```csharp
        var statusAfter = StatusAfterFor(statusAfterOverride);
        var files = Attachments();
        _error = null;
        draft.UncertainSend = null;
```

with

```csharp
        var statusAfter = StatusAfterFor(statusAfterOverride);
        var files = Attachments();

        // Only a public reply links articles; a note never does, whatever the draft holds. The ids are taken now, so a change made while the send is on its way is not part of it.
        IReadOnlyList<Guid> articleIds = mode == ComposerMode.PublicReply ? [.. draft.LinkedArticles.Select(a => a.Id)] : [];
        _error = null;
        draft.UncertainSend = null;
```

Replace (edit 7 of 11)

```csharp
        {
            result = mode == ComposerMode.PublicReply
                ? await Tickets.ReplyAsync(ticketId, new AddAgentReplyRequest(text, [], statusAfter, rowVersion), files, CancellationToken.None)
                : await Tickets.AddNoteAsync(ticketId, new AddInternalNoteRequest(text, rowVersion), CancellationToken.None);
        }
```

with

```csharp
        {
            result = mode == ComposerMode.PublicReply
                ? await Tickets.ReplyAsync(ticketId, new AddAgentReplyRequest(text, articleIds, statusAfter, rowVersion), files, CancellationToken.None)
                : await Tickets.AddNoteAsync(ticketId, new AddInternalNoteRequest(text, rowVersion), CancellationToken.None);
        }
```

Replace (edit 8 of 11)

```csharp
        try
        {
            await HandleResultAsync(mode, draft, number, text, result);
        }
        finally
```

with

```csharp
        try
        {
            await HandleResultAsync(mode, draft, number, text, articleIds, result);
        }
        finally
```

Replace (edit 9 of 11)

```csharp

    // Runs after the write finished, possibly after this component was disposed: the shared draft is settled first, and the UI callbacks only run while alive.
    private async Task HandleResultAsync(ComposerMode mode, ComposerDraft draft, string number, string sentText, Result<AgentMessageResponse> result)
    {
        if (result.IsSuccess)
```

with

```csharp

    // Runs after the write finished, possibly after this component was disposed: the shared draft is settled first, and the UI callbacks only run while alive.
    private async Task HandleResultAsync(ComposerMode mode, ComposerDraft draft, string number, string sentText, IReadOnlyList<Guid> sentArticleIds, Result<AgentMessageResponse> result)
    {
        if (result.IsSuccess)
```

Replace (edit 10 of 11)

```csharp
                    draft.PublicText = string.Empty;
                }

                draft.FilesDropped = false;
```

with

```csharp
                    draft.PublicText = string.Empty;
                }

                // The articles that were sent are linked now; one the agent added while the send was on its way stays for the next reply.
                draft.LinkedArticles.RemoveAll(a => sentArticleIds.Contains(a.Id));

                draft.FilesDropped = false;
```

Replace (edit 11 of 11)

```csharp
            _error = error.Message;
        }
        else
        {
```

with

```csharp
            _error = error.Message;
        }
        else if (error.Code is ApiErrorCodes.KbArticleNotLinkable or ApiErrorCodes.ArticleNotFound)
        {
            _error = ReplyComposerCopy.ArticleNotLinkable;
        }
        else
        {
```

`src/TechStrap.Admin/Features/Tickets/ReplyComposerModel.cs`

Replace (edit 1 of 3)

```csharp
using System.Globalization;
using TechStrap.Contracts.Tickets;
```

with

```csharp
using System.Globalization;
using TechStrap.Admin.Features.Kb;
using TechStrap.Contracts.Tickets;
```

Replace (edit 2 of 3)

```csharp

    public string NoteText { get; set; } = string.Empty;

    /// <summary>The status to apply on send: Pending by default, or empty for "leave unchanged". "Send and solve" does not use this.</summary>
```

with

```csharp

    public string NoteText { get; set; } = string.Empty;

    /// <summary>
    /// The knowledge base articles the public reply will link (at most <see cref="TicketOperationLimits.MaxLinkedArticles"/>). They live in the draft, so switching to a note and back, a conflict reload and a
    /// failed send all keep them; an accepted send takes out exactly the ones it sent. A note never sends them.
    /// </summary>
    public List<ArticleChoice> LinkedArticles { get; } = [];

    /// <summary>The status to apply on send: Pending by default, or empty for "leave unchanged". "Send and solve" does not use this.</summary>
```

Replace (edit 3 of 3)

```csharp
    public const string NoteFailed = "Couldn't add the note. Your text is kept.";

    /// <summary>A write that timed out, could not reach the API or got an unreadable answer may still have been saved: never offer a bare "Try again".</summary>
    public const string ReplyUncertain = $"The reply may already have been sent. {Kept} Check the timeline before sending again.";
```

with

```csharp
    public const string NoteFailed = "Couldn't add the note. Your text is kept.";

    /// <summary>The API refused a linked article: it is not published, or it belongs to another product, or it is gone. It does not say which one.</summary>
    public const string ArticleNotLinkable = "One of the linked articles can't be linked: it may have been unpublished, moved to another product or removed. Remove the articles you no longer want, then send again. Your text is kept.";

    /// <summary>A write that timed out, could not reach the API or got an unreadable answer may still have been saved: never offer a bare "Try again".</summary>
    public const string ReplyUncertain = $"The reply may already have been sent. {Kept} Check the timeline before sending again.";
```

`src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor`

Replace

```razor
                else
                {
                    <ReplyComposer TicketId="_model.Id" TicketNumber="@_model.Number" RequesterEmail="@_model.Requester.Email"
                                   RowVersion="_model.RowVersion" OnSent="OnSentAsync" OnConflict="OnConflictAsync" OnGone="RefreshAsync" />
                }
```

with

```razor
                else
                {
                    <ReplyComposer TicketId="_model.Id" TicketNumber="@_model.Number" RequesterEmail="@_model.Requester.Email" ProductId="_model.ProductId"
                                   RowVersion="_model.RowVersion" OnSent="OnSentAsync" OnConflict="OnConflictAsync" OnGone="RefreshAsync" />
                }
```

`src/TechStrap.Admin/Styles/_kb.scss`

Replace

```scss
  color: var(--ink-2);
}
```

with

```scss
  color: var(--ink-2);
}

// The categories page.
.ts-kb-categories {
  max-width: 1100px;
}

.ts-kb-category-description {
  display: block;
}

.ts-kb-category-order {
  font-variant-numeric: tabular-nums;
}

.ts-kb-category-editing input[type="number"] {
  max-width: 8rem;
}

// The article picker and the chips of the reply composer. A chip is a title and a Remove button, never color alone; long titles wrap.
.ts-composer-articles {
  margin: 8px 0;
}

.ts-kb-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 8px;
  padding: 0;
  margin: 0 0 8px;
  list-style: none;
}

.ts-kb-chip {
  display: inline-flex;
  gap: 4px;
  align-items: center;
  max-width: 100%;
  padding: 1px 4px 1px 8px;
  background: var(--sheet);
  border: 2px solid var(--rule-strong);
}

.ts-kb-chip-title {
  font: 500 .8125rem var(--ts-font-mono);
  overflow-wrap: anywhere;
}

.ts-kb-picker {
  margin-top: 8px;
}

.ts-kb-picker-results {
  padding: 0;
  margin: 8px 0 0;
  list-style: none;

  li {
    display: flex;
    flex-wrap: wrap;
    gap: 4px 8px;
    align-items: center;
    padding: 4px 0;
    border-top: 1px solid var(--rule-strong);
  }
}

.ts-kb-picker-title {
  flex: 1 1 12rem;
  min-width: 0;
  overflow-wrap: anywhere;
}

.ts-kb-picker-state {
  font-size: .8125rem;
  color: var(--ink-2);
}
```

- [ ] **Step 4: Run the tests, then prove each pin bites**

```bash
dotnet test --project tests/TechStrap.Admin.Tests -c Release
dotnet test --project tests/TechStrap.Architecture.Tests -c Release
```

Expected: all PASS (`TechStrap.Admin.Tests` 1933 tests, `TechStrap.Architecture.Tests` 253).

Mutations, each applied to the finished code, the tests run, and the change reverted:

| Mutation | Failing tests |
| :-- | :-- |
| `KbCategoriesPage.razor`: Categories: Delete is drawn for every agent | `KbCategoriesPageTests.A_plain_agent_opens_the_page_lists_creates_and_edits_but_is_never_offered_delete`, `KbCategoriesHostTests.A_plain_agent_sees_the_categories_and_the_create_form_but_no_delete_button` |
| `KbCategoriesPage.razor.cs`: Categories: a category with articles can be confirmed again | `KbCategoriesPageTests.A_category_with_articles_is_blocked_with_a_message_and_the_dialog_cannot_be_confirmed_again` |
| `KbCategoriesPage.razor.cs`: Categories: the update does not send the loaded version | `KbCategoriesPageTests.Saving_a_new_sort_order_sends_the_update_with_the_loaded_version_and_re_sorts_the_list` |
| `KbCategoriesPage.razor.cs`: Categories: an uncertain delete is not held | `KbCategoriesPageTests.A_delete_with_an_unknown_outcome_is_held_until_the_list_is_read_again` |
| `KbCategoryRowViewModel.cs`: Categories: the reserved slug search is not refused before sending | `KbCategoriesPageTests.The_slug_search_is_refused_before_it_is_sent_because_the_portal_keeps_it_for_its_search_page`, `KbCategoryFormTests.A_slug_is_lower_case_words_and_never_the_reserved_search` |
| `KbCategoriesPage.razor.cs`: Categories: no _loadId stale guard | `KbCategoriesPageTests.A_load_that_was_overtaken_by_a_reload_never_replaces_the_newer_list` |
| `KbCategoriesPage.razor.cs`: Categories: the sort order is not re-sorted after an update | `KbCategoriesPageTests.Saving_a_new_sort_order_sends_the_update_with_the_loaded_version_and_re_sorts_the_list` |
| `ArticlePicker.razor.cs`: Picker: a row that is not published is offered | `ArticlePickerTests.A_row_that_is_not_published_is_never_offered_even_if_the_answer_holds_one` |
| `ArticlePicker.razor.cs`: Picker: asks for every status | `ArticlePickerTests.A_row_that_is_not_published_is_never_offered_even_if_the_answer_holds_one`, `ArticlePickerTests.Typing_searches_once_after_the_debounce_for_published_articles_of_the_ticket_product_and_the_shared_ones`, `ReplyComposerArticleTests.The_picker_searches_the_tickets_product_and_the_shared_articles` (and 1 more; 4 in all) |
| `ArticlePicker.razor.cs`: Picker: leaves out the shared articles | `ArticlePickerTests.Typing_searches_once_after_the_debounce_for_published_articles_of_the_ticket_product_and_the_shared_ones`, `ReplyComposerArticleTests.The_picker_searches_the_tickets_product_and_the_shared_articles` |
| `ArticlePicker.razor.cs`: Picker: searches with no debounce | `ArticlePickerTests.A_pending_timer_is_released_with_the_component_so_no_call_is_made_after_it_is_gone`, `ArticlePickerTests.Typing_searches_once_after_the_debounce_for_published_articles_of_the_ticket_product_and_the_shared_ones` |
| `ArticlePicker.razor.cs`: Picker: no _loadId stale guard | `ArticlePickerTests.An_answer_that_was_overtaken_by_a_newer_search_never_replaces_the_newer_list` |
| `ArticlePicker.razor.cs`: Picker: the timer is not released with the component | `ArticlePickerTests.A_pending_timer_is_released_with_the_component_so_no_call_is_made_after_it_is_gone` |
| `ReplyComposer.razor.cs`: Composer: the reply sends no article ids | `ReplyComposerArticleTests.An_article_chosen_while_a_send_is_on_its_way_stays_for_the_next_reply`, `ReplyComposerArticleTests.Sending_a_public_reply_sends_the_chosen_ids_in_the_order_they_were_chosen_and_takes_them_out_of_the_draft`, `TicketPageArticleLinkingTests.The_picker_on_a_ticket_searches_that_tickets_product_and_the_reply_carries_the_chosen_article` |
| `ReplyComposer.razor.cs`: Composer: an accepted send clears every chosen article | `ReplyComposerArticleTests.An_article_chosen_while_a_send_is_on_its_way_stays_for_the_next_reply` |
| `ReplyComposer.razor.cs`: Composer: the API refusing a link is a generic failure | `ReplyComposerArticleTests.An_article_the_api_will_not_link_keeps_the_text_and_the_choice_and_says_what_to_do` |
| `ReplyComposer.razor.cs`: Composer: no limit of ten articles in the draft | `ReplyComposerArticleTests.The_draft_never_holds_more_than_ten_articles_or_the_same_article_twice_whatever_the_picker_reports` |
| `ReplyComposer.razor.cs`: Composer: the same article can be added twice | `ReplyComposerArticleTests.The_draft_never_holds_more_than_ten_articles_or_the_same_article_twice_whatever_the_picker_reports` |
| `ReplyComposer.razor`: Composer: the article controls are shown in note mode | `ReplyComposerArticleTests.A_note_hides_the_article_controls_sends_no_articles_and_the_choice_is_still_there_in_a_public_reply` |
| `TicketDetailPage.razor`: Ticket page: the composer is told no product | `TicketPageArticleLinkingTests.The_picker_on_a_ticket_searches_that_tickets_product_and_the_reply_carries_the_chosen_article` |
| `TechStrapCsp.cs`: CSP: the loopback image entries lose their wildcard port | `KbImageCspTests.In_development_a_picture_from_the_api_on_loopback_or_over_https_is_allowed` |
| `ApiErrorCodes.cs`: a typo in the in-use code | `KbErrorCodesTests.The_knowledge_base_codes_are_the_api_wire_codes` |
| `KbCategoriesPage.razor.cs`: Categories: a save that finishes after the page is gone still speaks | `KbCategoriesPageTests.A_save_that_finishes_after_the_page_is_gone_changes_nothing_and_says_nothing` |

- [ ] **Step 5: Build**

```bash
dotnet build TechStrap.slnx -c Release
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 6: The documents**

Save the script below outside the repository (for example `$TEMP/p08_admin_docs.py`) and run it from the repository root with `python $TEMP/p08_admin_docs.py`. It edits `ADMIN-APP.md`, the PHASE-08 document, the roadmap and the decision log, and stops with an `AssertionError` that names the file and the start of the text if any replaced text is not there exactly once.

```python
import os
import re

ROOT = os.environ.get('EXP', os.getcwd())


def read(path):
    return open(os.path.join(ROOT, path), encoding='utf-8', newline='').read()


def write(path, text):
    open(os.path.join(ROOT, path), 'w', encoding='utf-8', newline='').write(text)


def edit(path, old, new, count=1):
    """Exact replace in a repo-relative file; works with LF or CRLF working trees; stops if the text is not there exactly once."""
    s = read(path)
    if '\r\n' in s:
        old = old.replace('\r\n', '\n').replace('\n', '\r\n')
        new = new.replace('\r\n', '\n').replace('\n', '\r\n')
    assert s.count(old) == count, (path, old[:70], s.count(old))
    write(path, s.replace(old, new))


ADMIN_DOC = 'docs/development/ADMIN-APP.md'
PHASE = 'docs/architecture/PHASE-08-knowledge-base.md'
ROADMAP = 'docs/architecture/99-IMPLEMENTATION-ROADMAP.md'
LOG = 'docs/architecture/04-DECISION-LOG.md'

# ------------------------------------------------------------------------------------------------------------------------- ADMIN-APP.md
edit(ADMIN_DOC, '''the app as it is once 07c is merged; the 07b and 07c parts say so.
''', '''the app as it is once 07c is merged; the 07b and 07c parts say so. **PHASE-08** adds the knowledge base screens (the article list, the editor with a live preview and picture upload, the categories
page) and an article picker in the reply composer; the "Knowledge base (08)" section below describes them.
''')

edit(ADMIN_DOC, '''| `TECHSTRAP_GROUP_CLAIM_TYPE` | no | `groups` | Same. |
''', '''| `TECHSTRAP_GROUP_CLAIM_TYPE` | no | `groups` | Same. |
| `TECHSTRAP_PORTAL_PUBLIC_URL` | no | | The customer portal's public base URL, the same key the API reads. The Admin uses it only for the "View on portal" link of a published article; blank hides the link. Absolute http or https, no query or fragment (the Admin refuses to start otherwise). |
''')

edit(ADMIN_DOC, '''| Audit log, failed emails (retry, discard) | no | yes |
''', '''| Audit log, failed emails (retry, discard) | no | yes |
| Knowledge base: list, write, save, publish and archive articles; preview; upload pictures; create and edit categories; link articles from a reply | yes | yes |
| Delete a category | no (the button is not drawn) | yes |
''')

edit(ADMIN_DOC, '''  Clients/        ApiConnection and the typed clients (IAgentsClient, IProductsClient, ITagsClient, ITicketsClient, IRequestersClient, IAdminEventsClient, IDeadLettersClient), the /attachments/{id} pass-through
''', '''  Clients/        ApiConnection and the typed clients (IAgentsClient, IProductsClient, ITagsClient, ITicketsClient, IRequestersClient, IAdminEventsClient, IDeadLettersClient, IKbClient), the /attachments/{id} pass-through
''')

edit(ADMIN_DOC, '''    Ops/          Admin only. DeadLetters/ (DeadLettersPage)
''', '''    Ops/          Admin only. DeadLetters/ (DeadLettersPage)
    Kb/           Every agent. KbArticleListPage, KbArticleEditorPage (with KbArticleEditorPresenter and the form model), MarkdownEditor, KbPreviewPane, KbImageUploadButton, MarkdownSnippets, KbCategoriesPage, ArticlePicker (mounted by ReplyComposer)
''')

edit(ADMIN_DOC, '''- The single `MarkupString` is the message body in `MessageBubble` (the API sanitizes it). Everything else is encoded.
''', '''- `MarkupString` is used in exactly two files, and both draw HTML the API sanitized: the message body in `MessageBubble`, and the knowledge base preview in `KbPreviewPane` (the answer of `POST /api/kb/preview`, which runs the same renderer and
  sanitizer as the portal). `MarkupStringSiteTests` pins the two. Everything else is encoded.
''')

edit(ADMIN_DOC, '''| Go to | Queue: Unassigned, Mine, Open, Pending, All, Spam; My settings | Every agent |
''', '''| Go to | Queue: Unassigned, Mine, Open, Pending, All, Spam; Knowledge base; My settings | Every agent |
''')

edit(ADMIN_DOC, '''no knowledge-base article picker (PHASE-08); no presence''', '''no knowledge-base article picker (added in 08); no presence''')

edit(ADMIN_DOC, '''`InputFile` has no `MaxAllowedSize` and bUnit does not enforce stream limits, so the composer checks files itself.''', '''`InputFile` has no `MaxAllowedSize` and bUnit does not enforce stream limits, so the composer and the picture button check files themselves; `UploadFiles` returns only when the change handler has finished, so a test that holds an upload open
(a pending task) starts the pick on its own thread (`Task.Run`) and awaits it after the answer is released.''')

SECTION = '''## Knowledge base (08)

Every agent can write and publish articles; deleting a category is the one thing that needs the Admin role. The rail has a "Knowledge base" link for everyone, and the palette has "Knowledge base" (`go-kb`). The API behind all of it is in
[PHASE-08-knowledge-base.md](../architecture/PHASE-08-knowledge-base.md); the decisions are D-044.

- **Articles** (`/kb`): the list, 25 to a page, newest change first. Search the title, summary and text, and filter by product (including "Shared", the articles every product shows), category and status (Draft, Published or Archived). Every filter is in the address, so a
  view can be linked. Each row shows the title, the product or "Shared", the category, the status as a word, and when it changed. An empty knowledge base invites the first article.
- **Editor** (`/kb/new`, `/kb/{id}`): product (chosen once, or "Shared by every product"), category, title, slug, summary and the article text. The slug follows the title until you edit it, and like the product it is permanent once the article exists. The category list offers the
  shared categories and, for a product article, that product's own; a shared article can only use a shared category. The text is Markdown in a plain text box with a toolbar (bold, italic, link, list, code) beside a live preview.
- **Preview**: 300 ms after you stop typing, the Admin asks the API to render the text (`POST /api/kb/preview`) and draws the answer, which is the same HTML the portal will show (tables and pictures included, anything unsafe removed). A call that a newer keystroke replaced is
  canceled and its answer is never drawn; a failed preview keeps your text and the last good preview and says so; an empty text is not previewed. Below 992 px Write and Preview are two buttons and one pane at a time.
- **Pictures**: "Add image" takes a PNG, JPEG, GIF or WebP file of up to 5 MB (anything else is refused before it is sent), uploads it and adds `![file name](address)` at the **end** of the text. The toolbar buttons also add their Markdown at the end. Blazor cannot read the
  caret, and paste and drag-and-drop are not built (D-044). If the answer to an upload is lost nothing is added to the text and you pick the picture again; the earlier copy, if the API kept one, is an unreferenced file.
- **Save, publish, archive**: Save sends the version the article was loaded with, so a stale save is a 409: you see "This article changed since you opened it", your edits stay in the form, saving stays off, and Reload brings in the latest version (and replaces your edits, so copy what you need
  first). Editing an **archived** article makes it a draft again, and the status line says so. Publish needs the article saved first and a title, slug, text and category (a line under the buttons says which is missing); it sends the loaded version too. Archive asks first ("It is removed from the portal,
  from search and from the sitemap"). A write whose answer is lost (a timeout, an unreachable API, a 5xx) is held: the page says it may have gone through and nothing more is sent until you reload.
- **Leaving with unsaved changes**: moving to another page in the Admin asks "Leave without saving?" (Stay or Leave), and closing the tab or following an outside link gets the browser's own prompt. A form that is back to what was saved does not ask.
- **View on portal**: a published article shows this link, built from `TECHSTRAP_PORTAL_PUBLIC_URL` as `{portal}/p/{product key}/kb/{category slug}/{slug}` (from the saved category, not one you have picked but not saved). A shared article is reachable under every product, so the link uses the
  first product by name. With no portal address configured, or for a draft or an archived article, there is no link.
- **Categories** (`/kb/categories`): every category with its product (or "Shared"), slug, sort order and description; create one (the slug follows the name until you edit it; the slug `search` is kept for the portal's search page; a slug already used anywhere is refused), rename it,
  describe it and change its sort order in the row (saved through the update, with the version the list was read with). **Delete** is drawn only for an Admin. A category that still has articles cannot be deleted: the dialog says so and cannot be confirmed; move the articles first.
- **Linking an article from a reply**: a public reply has "Link a knowledge base article". It opens a search of the **published** articles of the ticket's product and the shared ones (nothing is searched until you type, and a draft or an archived article is never offered); each result has an
  Add button, and the choices show as chips with Remove. At most 10 articles. The chips belong to the ticket's draft: they survive switching to a note and back, a failed send and leaving the ticket; a note never sends them; an accepted send takes out exactly the ones it sent. If the API
  refuses a link (an article unpublished or moved to another product meanwhile) the text and the chips stay and you are told to remove the ones you no longer want. The customer's email gets a link to each article.

## Local time, theme, security headers and layout (07c)
'''
edit(ADMIN_DOC, '''## Local time, theme, security headers and layout (07c)
''', SECTION)

GAPS = '''
## Known gaps in 08

Recorded in D-044 and tracked for later phases:

- Pictures and toolbar snippets are added at the end of the text, not at the caret, and there is no paste or drag-and-drop of a picture (Blazor cannot read the caret without script). The alt text is the file name; the Markdown can be edited after.
- There is no revision history, no hard delete of an article (archiving is the removal), no "create an article from this ticket" and no clean-up of pictures that no article uses.
- The ticket timeline lists the titles of the linked articles as plain text, not as links: the linked-article DTO has no portal address.
- "View on portal" for a shared article uses the first product by name. The canonical address of a shared article under several products is the portal's decision (PHASE-09).
- The CSP already lets the Admin draw an API picture in Development (`img-src` has `http://localhost:*` and `http://127.0.0.1:*` on any port) and in Production over https; a plain-http `TECHSTRAP_API_PUBLIC_URL` in Production would show no pictures in the preview. `KbImageCspTests` pins both.
- The manual checks (write and publish an article with a picture; the picture loads through the API's public address behind the proxy; a linked article appears in a reply email) are the owner's.
'''
s = read(ADMIN_DOC)
assert s.rstrip().endswith('- The Compose smoke is by hand (or the manual workflow), not on every pull request, because it builds four images.')
write(ADMIN_DOC, s.rstrip('\r\n') + ('\r\n' if '\r\n' in s else '\n') + (GAPS.replace('\n', '\r\n') if '\r\n' in s else GAPS))

# ------------------------------------------------------------------------------------------------------------------------- PHASE-08 ticks
s = read(PHASE)
for number in range(13, 21):
    old = '- [ ] **P08-T%02d**' % number
    assert s.count(old) == 1, old
    s = s.replace(old, '- [x] **P08-T%02d**' % number)
old = '- [ ] Admin KB list, editor with live preview and image upload, categories page, article picker in the reply composer.'
assert s.count(old) == 1
s = s.replace(old, '- [x] Admin KB list, editor with live preview and image upload, categories page, article picker in the reply composer.')
note = '''## As built (Admin)

The Admin names follow the Contracts as built: the list row is `KbArticleListItemDto`, the picture answer is `KbImageUploadResponse`, the list query is `ListKbArticlesRequest`, and the reply carries `AddAgentReplyRequest.LinkedArticleIds`. Publish and archive send
the loaded version (`?version=`) and answer the article. The preview pane (`KbPreviewPane`) and the message body (`MessageBubble`) are the only two `MarkupString` sites. The picker is behind a button, so a reply that links nothing never calls the knowledge base. Details and the gaps are in
`docs/development/ADMIN-APP.md`, "Knowledge base (08)".

## Syntax Circus Packages
'''
assert s.count('## Syntax Circus Packages\n') == 1 or s.count('## Syntax Circus Packages\r\n') == 1
nl = '\r\n' if '\r\n' in s else '\n'
s = s.replace('## Syntax Circus Packages' + nl, note.replace('\n', nl))
write(PHASE, s)

# ------------------------------------------------------------------------------------------------------------------------- roadmap row (Task 8 set it to In progress)
s = read(ROADMAP)
lines = s.split('\r\n') if '\r\n' in s else s.split('\n')
joiner = '\r\n' if '\r\n' in s else '\n'
hits = [i for i, line in enumerate(lines) if line.startswith('| 08 | [Knowledge base](PHASE-08-knowledge-base.md)')]
assert len(hits) == 1, hits
cells = lines[hits[0]].split(' | ')
cells[-1] = 'PHASE-08 complete (pending merge): the API (Tasks 1-8) and the Admin (editor, categories, article picker) are implemented; the owner\'s manual checks are open |'
lines[hits[0]] = ' | '.join(cells)
write(ROADMAP, joiner.join(lines))

# ------------------------------------------------------------------------------------------------------------------------- D-044: what the Admin tasks add
s = read(LOG)
nl = '\r\n' if '\r\n' in s else '\n'
start = s.index('## D-044')
following = re.search(r'\n## D-\d+', s[start + 5:])
end = start + 5 + following.start() if following else len(s)
section = s[start:end]
marker = nl + '### Approval'
assert section.count(marker) == 1, 'D-044 has no single Approval heading'
addition = '''- **Admin as built.** Publish and archive send the loaded version, so publishing text another agent has changed is a 409. A write whose answer is lost is held until a reload, except a picture upload, which changes no article until its address is added, so the agent just picks it again. The picker is behind a button and searches only Published articles of the ticket's product and the shared ones. "View on portal" is built from the Admin's own optional `TECHSTRAP_PORTAL_PUBLIC_URL` (the same key and four edits as the Api's; blank hides the link) and uses the first product by name for a shared article. The Admin CSP needed no change: its Development `img-src` already allows loopback on any port and Production allows https.
'''.replace('\n', nl)
assert section.count(nl + '### Alternatives Considered') == 1
# Append to the end of the Consequences list, before the next heading (Approval), keeping a blank line.
consequences = section.index(nl + '### Consequences')
approval = section.index(marker)
assert consequences < approval
block = section[consequences:approval].rstrip() + nl + addition.rstrip() + nl
section = section[:consequences] + block + section[approval:]
write(LOG, s[:start] + section + s[end:])
```

Then run the documents' own checks:

```bash
pwsh -File scripts/Invoke-ScriptTests.ps1 -Path scripts/tests/RepositoryDocs.Tests.ps1
```

Expected: PASS. Mutations, each applied to the finished docs and reverted:

| Mutation | Failing check |
| :-- | :-- |
| untick `P08-T13` in `PHASE-08-knowledge-base.md` | `ticks every PHASE-08 task P08-T01 to P08-T20 and the Admin deliverable, and the roadmap row says the phase is complete, pending merge` |
| put the roadmap row back to `In progress` | `ticks every PHASE-08 task P08-T01 to P08-T20 and the Admin deliverable, and the roadmap row says the phase is complete, pending merge` |

- [ ] **Step 7: Final verification**

```bash
dotnet build TechStrap.slnx -c Release --no-incremental
dotnet test --solution TechStrap.CI.slnf -c Release
pwsh -File scripts/Invoke-ScriptTests.ps1
```

Expected: the build has 0 warnings and 0 errors; the CI solution passes (4781 tests); the script tests pass (Tests Passed: 283, Failed: 0, Skipped: 0). The `--no-incremental` rebuild matters: a mutation run can leave a stale DLL behind.

- [ ] **Step 8: Commit**

```bash
git add docs/architecture/04-DECISION-LOG.md \
  docs/architecture/99-IMPLEMENTATION-ROADMAP.md \
  docs/architecture/PHASE-08-knowledge-base.md \
  docs/development/ADMIN-APP.md \
  scripts/tests/RepositoryDocs.Tests.ps1 \
  src/TechStrap.Admin/Features/Kb/ArticleChoice.cs \
  src/TechStrap.Admin/Features/Kb/ArticlePicker.razor \
  src/TechStrap.Admin/Features/Kb/ArticlePicker.razor.cs \
  src/TechStrap.Admin/Features/Kb/ArticlePickerCopy.cs \
  src/TechStrap.Admin/Features/Kb/KbCategoriesCopy.cs \
  src/TechStrap.Admin/Features/Kb/KbCategoriesPage.razor \
  src/TechStrap.Admin/Features/Kb/KbCategoriesPage.razor.cs \
  src/TechStrap.Admin/Features/Kb/KbCategoryRowViewModel.cs \
  src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor \
  src/TechStrap.Admin/Features/Tickets/ReplyComposer.razor.cs \
  src/TechStrap.Admin/Features/Tickets/ReplyComposerModel.cs \
  src/TechStrap.Admin/Features/Tickets/TicketDetailPage.razor \
  src/TechStrap.Admin/Styles/_kb.scss \
  tests/TechStrap.Admin.Tests/Components/ArticlePickerTests.cs \
  tests/TechStrap.Admin.Tests/Components/KbCategoriesPageTests.cs \
  tests/TechStrap.Admin.Tests/Components/ReplyComposerArticleTests.cs \
  tests/TechStrap.Admin.Tests/Components/TicketPageArticleLinkingTests.cs \
  tests/TechStrap.Admin.Tests/KbCategoriesHostTests.cs \
  tests/TechStrap.Admin.Tests/KbImageCspTests.cs \
  tests/TechStrap.Admin.Tests/KbStyleTests.cs \
  tests/TechStrap.Admin.Tests/ScrollRegionSiteTests.cs
git diff --cached --stat
git commit -m "feat(admin): knowledge base categories, the reply article picker and the closing docs" -m "The categories page (create, edit with the loaded version, delete for an admin only and blocked while articles are in a category), the published-article picker with chips in the public reply, the CSP picture check, and the PHASE-08 Admin docs and ticks." -m "$(printf '%s\n%s' 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>' 'Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi')"
```

The commit message ends with exactly these two lines, and nothing else is staged:

```text
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01ReiWu2p7mSuArnHAMeBiMi
```
